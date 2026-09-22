#!/bin/sh
# ============================================================
#  AI2P - install_required.sh  (Linux: apt/dnf, macOS: brew)
#
#  Checks and installs the build environment (TZ v1.8, ch. 13.1):
#    1. .NET SDK 8+  AND  ASP.NET Core runtime 8.x
#       (the app targets net8.0; Blazor interactivity silently breaks
#        when the app is rolled forward to runtime 9/10, so the 8.x
#        runtime must be present even if a newer SDK is installed)
#    2. CMake
#    3. C/C++ compiler (gcc/clang; macOS: Xcode Command Line Tools)
#    4. git submodules (git submodule update --init)
#    5. makeself      (needed by MakePackage.sh, T-285, to turn a release
#                      folder into a single self-extracting .run installer;
#                      the build itself does not need it)
#
#  Usage:  ./install_required.sh
#          ./install_required.sh --help   (out of i18n/help/install_required.*.txt)
#  Note :  package installation may ask for your sudo password.
# ============================================================
set -u
cd "$(dirname "$0")"

# --help is printed from a separate file (T-65-S0) so that it can be translated
# without touching the code. The language comes from the same command line
# ("--help --lang ru"): before the T-65-S0 rework the tail was dropped and the
# help was always English. Empty - the loc.sh ladder works: AI2P_LANG, then en.
help_lang() {
    _want=0
    for _a in "$@"; do
        if [ "$_want" -eq 1 ]; then printf '%s' "$_a"; return 0; fi
        case "$_a" in
            --lang|-lang|-Lang) _want=1 ;;
            --lang=*)           printf '%s' "${_a#--lang=}"; return 0 ;;
        esac
    done
    return 0
}
case "${1:-}" in
    -h|--help)
        if [ -f "./i18n/loc.sh" ]; then
            AI2P_LOC_DIR="./i18n"; . "./i18n/loc.sh"
        fi
        # T-319: годность загрузчика — по функциям, а не по наличию файла
        if command -v ai2p_help >/dev/null 2>&1; then
            ai2p_set_lang "$(help_lang "$@")"; ai2p_help install_required
        else
            echo "Usage: ./install_required.sh   - checks and installs the build environment."
        fi
        exit 0 ;;
esac

ERRORS=0
OS="$(uname -s)"

# sudo only when not root (and not needed for brew)
SUDO=""
if [ "$(id -u)" -ne 0 ]; then
    SUDO="sudo"
fi

# ---------- package manager detection ----------
PM=""
case "$OS" in
    Linux)
        if command -v apt-get >/dev/null 2>&1; then PM="apt"
        elif command -v dnf  >/dev/null 2>&1; then PM="dnf"
        else
            echo "[ERROR] Neither apt-get nor dnf found. Install packages manually:"
            echo "        .NET SDK 8+ : https://dotnet.microsoft.com/download"
            echo "        CMake       : https://cmake.org/download/"
            exit 1
        fi
        ;;
    Darwin)
        if command -v brew >/dev/null 2>&1; then PM="brew"
        else
            echo "[ERROR] Homebrew not found. Install it first:"
            echo "        https://brew.sh/"
            exit 1
        fi
        ;;
    *)
        echo "[ERROR] Unsupported OS: $OS"
        exit 1
        ;;
esac

echo "=== AI2P install_required ($OS, package manager: $PM) ==="
echo ""

pm_install() {
    case "$PM" in
        apt)  $SUDO apt-get install -y "$@" ;;
        dnf)  $SUDO dnf install -y "$@" ;;
        brew) brew install "$@" ;;
    esac
}

APT_UPDATED=0
apt_update_once() {
    if [ "$PM" = "apt" ] && [ "$APT_UPDATED" -eq 0 ]; then
        $SUDO apt-get update
        APT_UPDATED=1
    fi
}

# ---------- 1. .NET SDK 8+ ----------
have_dotnet8() {
    command -v dotnet >/dev/null 2>&1 || return 1
    dotnet --list-sdks 2>/dev/null | awk -F. '{ if ($1+0 >= 8) found=1 } END { exit (found ? 0 : 1) }'
}

if have_dotnet8; then
    echo "[OK]   .NET SDK 8+ is installed:"
    dotnet --list-sdks
else
    echo "[....] .NET SDK 8+ not found - installing..."
    case "$PM" in
        apt)
            apt_update_once
            pm_install dotnet-sdk-8.0 || ERRORS=1
            ;;
        dnf)
            pm_install dotnet-sdk-8.0 || ERRORS=1
            ;;
        brew)
            brew install --cask dotnet-sdk || ERRORS=1
            ;;
    esac
    if have_dotnet8; then
        echo "[OK]   .NET SDK 8 installed."
    else
        echo "[ERROR] .NET SDK 8 installation failed."
        echo "        Manual install: https://dotnet.microsoft.com/download"
        ERRORS=1
    fi
fi
echo ""

# ---------- 1b. ASP.NET Core runtime 8.x ----------
# A newer SDK (9/10) builds net8.0 fine, but the app must RUN on the 8.x
# runtime: Blazor's client script (blazor.web.js) comes from the net8.0
# build, and mixing it with a 9/10 server breaks interactivity silently.
have_aspnet8_runtime() {
    command -v dotnet >/dev/null 2>&1 || return 1
    dotnet --list-runtimes 2>/dev/null | grep -q '^Microsoft\.AspNetCore\.App 8\.'
}

if have_aspnet8_runtime; then
    echo "[OK]   ASP.NET Core runtime 8.x is installed:"
    dotnet --list-runtimes | grep '^Microsoft\.AspNetCore\.App 8\.'
else
    echo "[....] ASP.NET Core runtime 8.x not found - installing..."
    case "$PM" in
        apt)
            apt_update_once
            pm_install aspnetcore-runtime-8.0 || ERRORS=1
            ;;
        dnf)
            pm_install aspnetcore-runtime-8.0 || ERRORS=1
            ;;
        brew)
            # No versioned runtime cask in Homebrew; install the official way,
            # side by side with the existing SDK, into the same dotnet root.
            DOTNET_HOME="/usr/local/share/dotnet"
            if command -v dotnet >/dev/null 2>&1; then
                RESOLVED="$(dirname "$(readlink -f "$(command -v dotnet)")")"
                [ -n "$RESOLVED" ] && DOTNET_HOME="$RESOLVED"
            fi
            echo "       Installing ASP.NET Core runtime 8.0 into $DOTNET_HOME ..."
            if curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh; then
                $SUDO bash /tmp/dotnet-install.sh --channel 8.0 --runtime aspnetcore \
                      --install-dir "$DOTNET_HOME" || ERRORS=1
            else
                echo "[ERROR] Could not download dotnet-install.sh."
                ERRORS=1
            fi
            ;;
    esac
    if have_aspnet8_runtime; then
        echo "[OK]   ASP.NET Core runtime 8.x installed."
    else
        echo "[ERROR] ASP.NET Core runtime 8.x installation failed."
        echo "        Manual install: https://dotnet.microsoft.com/download/dotnet/8.0"
        ERRORS=1
    fi
fi
echo ""

# ---------- 2. CMake ----------
if command -v cmake >/dev/null 2>&1; then
    echo "[OK]   CMake is installed:"
    cmake --version | head -n 1
else
    echo "[....] CMake not found - installing..."
    apt_update_once
    pm_install cmake || ERRORS=1
    if command -v cmake >/dev/null 2>&1; then
        echo "[OK]   CMake installed."
    else
        echo "[ERROR] CMake installation failed."
        ERRORS=1
    fi
fi
echo ""

# ---------- 3. C/C++ compiler ----------
case "$OS" in
    Linux)
        if command -v cc >/dev/null 2>&1 || command -v gcc >/dev/null 2>&1; then
            echo "[OK]   C/C++ compiler is installed:"
            (cc --version 2>/dev/null || gcc --version) | head -n 1
        else
            echo "[....] C/C++ compiler not found - installing..."
            case "$PM" in
                apt)
                    apt_update_once
                    pm_install build-essential || ERRORS=1
                    ;;
                dnf)
                    pm_install gcc gcc-c++ make || ERRORS=1
                    ;;
            esac
            if command -v cc >/dev/null 2>&1 || command -v gcc >/dev/null 2>&1; then
                echo "[OK]   C/C++ compiler installed."
            else
                echo "[ERROR] C/C++ compiler installation failed."
                ERRORS=1
            fi
        fi
        ;;
    Darwin)
        if xcode-select -p >/dev/null 2>&1; then
            echo "[OK]   Xcode Command Line Tools (clang) are installed:"
            clang --version | head -n 1
        else
            echo "[....] Xcode Command Line Tools not found - requesting install..."
            echo "       A dialog will appear; confirm the installation, then"
            echo "       re-run this script."
            xcode-select --install || true
            ERRORS=1
        fi
        ;;
esac
echo ""

# ---------- 4. git + submodules ----------
if ! command -v git >/dev/null 2>&1; then
    echo "[....] git not found - installing..."
    apt_update_once
    pm_install git || ERRORS=1
fi
if [ -f ".gitmodules" ]; then
    echo "[....] Initializing git submodules..."
    if git submodule update --init --recursive; then
        echo "[OK]   git submodules are up to date."
    else
        echo "[ERROR] git submodule init failed."
        ERRORS=1
    fi
else
    echo "[OK]   No .gitmodules yet - nothing to initialize."
fi
echo ""

# ---------- 5. makeself (installer packages, T-285) ----------
# MakePackage.sh turns a release folder into a single self-extracting
# installer (AI2P_v_1_NN_linux_x64.run / AI2P_v_1_NN_full_macos_arm64.run).
# Not needed to build or run the app - only to hand it to someone else.
have_makeself() {
    command -v makeself >/dev/null 2>&1 || command -v makeself.sh >/dev/null 2>&1
}

if have_makeself; then
    echo "[OK]   makeself is installed:"
    (makeself --version 2>/dev/null || makeself.sh --version 2>/dev/null || echo "       makeself") | head -n 1
else
    echo "[....] makeself not found - installing..."
    apt_update_once
    pm_install makeself || ERRORS=1
    if have_makeself; then
        echo "[OK]   makeself installed - MakePackage.sh can build packages now."
    else
        echo "[ERROR] makeself installation failed."
        echo "        Manual install: https://makeself.io/ (or your distro package 'makeself')"
        ERRORS=1
    fi
fi
echo ""

# ---------- 6. openssl (HTTPS certificates, T-206) ----------
# Needed only to MAKE a certificate for the https protocol: a self-signed one,
# your own certificate authority, or a .pfx out of a PEM pair. Not needed to
# build or run the app - but on Linux/macOS there is no built-in replacement,
# so it is installed when missing.
if command -v openssl >/dev/null 2>&1; then
    echo "[OK]   openssl is installed:"
    openssl version
else
    echo "[....] openssl not found - installing..."
    apt_update_once
    pm_install openssl || ERRORS=1
    if command -v openssl >/dev/null 2>&1; then
        echo "[OK]   openssl installed - a certificate for https can be made now."
    else
        echo "[ERROR] openssl installation failed."
        echo "        Manual install: your distro package 'openssl' (macOS: brew install openssl)"
        ERRORS=1
    fi
fi
echo ""

# ---------- Summary ----------
echo "=== Summary ==="
if [ "$ERRORS" -ne 0 ]; then
    echo "[ERROR] Some steps failed - see messages above."
    exit 1
fi
echo "All required tools are installed."
exit 0

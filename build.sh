#!/bin/sh
# ============================================================
#  AI2P - build.sh  (Linux / macOS)
#  Builds the whole solution: C# projects + (later) native C/C++
#  CMake subprojects (TZ v1.8, ch. 4.2).
#  Usage:  ./build.sh [Debug|Release] [--lang xx]
#          ./build.sh --help [--lang xx]
# ============================================================
set -u
cd "$(dirname "$0")"

# --help печатается из отдельного файла i18n/help/build.<язык>.txt (T-65-S0),
# язык берётся из той же командной строки: --lang xx / --lang=xx, иначе лесенка
# loc.sh — переменная окружения AI2P_LANG, затем en
CONFIGURATION="Debug"
LANG_OPT=""
HELP=0
while [ "$#" -gt 0 ]; do
    case "$1" in
        -h|--help)          HELP=1 ;;
        --lang|-lang|-Lang) if [ "$#" -ge 2 ]; then shift; LANG_OPT="$1"; fi ;;
        --lang=*)           LANG_OPT="${1#--lang=}" ;;
        Debug|Release)      CONFIGURATION="$1" ;;
    esac
    shift
done

if [ "$HELP" -eq 1 ]; then
    if [ -f "./i18n/loc.sh" ]; then
        AI2P_LOC_DIR="./i18n"; . "./i18n/loc.sh"
    fi
    # T-319: годность загрузчика — по функциям, а не по наличию файла
    if command -v ai2p_help >/dev/null 2>&1; then
        ai2p_set_lang "$LANG_OPT"; ai2p_help build
    else
        echo "Usage: ./build.sh [Debug|Release]"
    fi
    exit 0
fi

echo "=== AI2P build ($CONFIGURATION) ==="

# 1. C# solution (dotnet restore is part of build)
if ! dotnet build AI2P.sln -c "$CONFIGURATION"; then
    echo "[ERROR] dotnet build failed."
    exit 1
fi

# 2. native C/C++ plugins (CMake) - nothing yet, placeholder for native/

echo "=== Build OK ==="
echo "Run:  dotnet run --project src/AI2P.Server"
exit 0

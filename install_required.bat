@echo off
rem ============================================================
rem  AI2P - install_required.bat  (Windows 10/11)
rem
rem  Checks and installs the build environment (TZ v1.8, ch. 13.1):
rem    1. .NET SDK 8+          (winget: Microsoft.DotNet.SDK.8)
rem       + ASP.NET Core runtime 8.x (winget: Microsoft.DotNet.AspNetCore.8)
rem         - the app targets net8.0 and must run on the 8.x runtime:
rem           Blazor interactivity breaks silently on runtime 9/10
rem    2. CMake                (winget: Kitware.CMake)
rem    3. C/C++ compiler MSVC  (winget: VS 2022 Build Tools + VC workload)
rem    4. git submodules       (git submodule update --init)
rem    5. Inno Setup 6         (winget: JRSoftware.InnoSetup)
rem       - needed by MakePackage.ps1 (T-285) to build the .exe installer
rem         out of a release folder; the build itself does not need it
rem
rem  Usage:  install_required.bat
rem          install_required.bat --help   (out of i18n\help\install_required.*.txt)
rem  Note :  installers may require admin confirmation (UAC).
rem          After installation open a NEW console so that PATH
rem          changes take effect, then re-run this script to verify.
rem ============================================================
setlocal EnableExtensions
cd /d "%~dp0"

rem --help prints i18n\help\install_required.<lang>.txt (T-65-S0). It goes through
rem PowerShell on purpose: "type" would decode a UTF-8 file in the console codepage
rem and turn a translated help into garbage, while this .bat stays pure ASCII.
rem The language of the help is taken from "-Lang xx" / "--lang xx" of the same
rem command line (T-65-S0 rework: it used to be dropped, so --help -Lang ru
rem printed English). Without it the ladder of loc.ps1 works: AI2P_LANG, then en.
rem %~dp0 is saved into %SELF% before the first shift - shift moves %0 too.
set "SELF=%~dp0"
if /i "%~1"=="--help" goto help
if /i "%~1"=="-h" goto help
if "%~1"=="/?" goto help
goto start

:help
set "HLANG="
:helpparse
shift
if "%~1"=="" goto helprun
if /i "%~1"=="-Lang" goto helplang
if /i "%~1"=="--lang" goto helplang
goto helpparse

:helplang
shift
set "HLANG=%~1"
goto helpparse

:helprun
if exist "%SELF%i18n\loc.ps1" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command ". '%SELF%i18n\loc.ps1'; Set-Ai2pLang '%HLANG%'; Show-Ai2pHelp 'install_required'"
) else (
    echo Usage: install_required.bat   - checks and installs the build environment.
)
exit /b 0

:start
set ERRORS=0

echo === AI2P install_required (Windows) ===
echo.

rem ---------- 0. winget ----------
where winget >nul 2>nul
if errorlevel 1 (
    echo [ERROR] winget not found. Install "App Installer" from Microsoft Store:
    echo         https://aka.ms/getwinget
    echo         Then re-run this script.
    exit /b 1
)

rem ---------- 1. .NET SDK 8+ ----------
set DOTNET_OK=
for /f "tokens=1 delims=." %%v in ('dotnet --list-sdks 2^>nul') do (
    if %%v GEQ 8 set DOTNET_OK=1
)
if defined DOTNET_OK (
    echo [OK]   .NET SDK 8+ is installed:
    dotnet --list-sdks
) else (
    echo [....] .NET SDK 8+ not found - installing via winget...
    winget install --id Microsoft.DotNet.SDK.8 -e --silent --accept-package-agreements --accept-source-agreements
    if errorlevel 1 (
        echo [ERROR] .NET SDK 8 installation failed.
        set ERRORS=1
    ) else (
        echo [OK]   .NET SDK 8 installed.
    )
)
echo.

rem ---------- 1b. ASP.NET Core runtime 8.x ----------
rem A newer SDK (9/10) builds net8.0 fine, but the app must RUN on the
rem 8.x runtime - Blazor interactivity breaks silently on runtime 9/10.
dotnet --list-runtimes 2>nul | findstr /b /c:"Microsoft.AspNetCore.App 8." >nul
if not errorlevel 1 (
    echo [OK]   ASP.NET Core runtime 8.x is installed:
    dotnet --list-runtimes | findstr /b /c:"Microsoft.AspNetCore.App 8."
) else (
    echo [....] ASP.NET Core runtime 8.x not found - installing via winget...
    winget install --id Microsoft.DotNet.AspNetCore.8 -e --silent --accept-package-agreements --accept-source-agreements
    if errorlevel 1 (
        echo [ERROR] ASP.NET Core runtime 8 installation failed.
        set ERRORS=1
    ) else (
        echo [OK]   ASP.NET Core runtime 8 installed.
    )
)
echo.

rem ---------- 2. CMake ----------
where cmake >nul 2>nul
if not errorlevel 1 (
    echo [OK]   CMake is installed:
    cmake --version | findstr /i "version"
) else (
    echo [....] CMake not found - installing via winget...
    winget install --id Kitware.CMake -e --silent --accept-package-agreements --accept-source-agreements
    if errorlevel 1 (
        echo [ERROR] CMake installation failed.
        set ERRORS=1
    ) else (
        echo [OK]   CMake installed.
    )
)
echo.

rem ---------- 3. C/C++ compiler (MSVC Build Tools) ----------
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
set "VCDIR="
if exist "%VSWHERE%" (
    for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2^>nul`) do set "VCDIR=%%i"
)
if defined VCDIR (
    echo [OK]   MSVC C/C++ build tools found:
    echo        %VCDIR%
) else (
    echo [....] MSVC C/C++ build tools not found - installing VS 2022 Build Tools...
    echo        This is a large download and may take a while.
    winget install --id Microsoft.VisualStudio.2022.BuildTools -e --silent --accept-package-agreements --accept-source-agreements --override "--quiet --wait --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
    if errorlevel 1 (
        echo [ERROR] VS Build Tools installation failed.
        set ERRORS=1
    ) else (
        echo [OK]   VS 2022 Build Tools with C/C++ workload installed.
    )
)
echo.

rem ---------- 4. git + submodules ----------
where git >nul 2>nul
if errorlevel 1 (
    echo [....] git not found - installing via winget...
    winget install --id Git.Git -e --silent --accept-package-agreements --accept-source-agreements
    if errorlevel 1 (
        echo [ERROR] git installation failed.
        set ERRORS=1
    )
)
if exist ".gitmodules" (
    echo [....] Initializing git submodules...
    git submodule update --init --recursive
    if errorlevel 1 (
        echo [ERROR] git submodule init failed.
        set ERRORS=1
    ) else (
        echo [OK]   git submodules are up to date.
    )
) else (
    echo [OK]   No .gitmodules yet - nothing to initialize.
)
echo.

rem ---------- 5. Inno Setup 6 (installer packages, T-285) ----------
rem MakePackage.ps1 turns a release folder into a single .exe installer
rem (AI2P_v_1_NN_win64.exe / AI2P_full_v_1_NN_win64.exe) with ISCC.exe.
rem Note: no parentheses around the echo of the path - the default install
rem path contains "(x86)" and would close a cmd block early.
set "ISCC="
where iscc >nul 2>nul
if not errorlevel 1 set "ISCC=iscc (in PATH)"
if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if defined ISCC echo [OK]   Inno Setup 6 compiler is installed:
if defined ISCC echo        %ISCC%
if not defined ISCC call :install_iscc
echo.

rem ---------- 6. openssl (HTTPS certificates, T-206) ----------
rem Needed only to MAKE a certificate for the https protocol: convert a PEM
rem pair into .pfx, look inside a certificate, build your own CA. Windows can
rem do the same with built-in PowerShell (New-SelfSignedCertificate +
rem Export-PfxCertificate), so a missing openssl is NOT an error here.
where openssl >nul 2>nul
if not errorlevel 1 (
    echo [OK]   openssl is installed:
    openssl version
) else (
    echo [....] openssl not found - installing via winget...
    winget install --id ShiningLight.OpenSSL.Light -e --silent --accept-package-agreements --accept-source-agreements
    where openssl >nul 2>nul
    if not errorlevel 1 (
        echo [OK]   openssl installed.
    ) else (
        echo [NOTE] openssl is not available - this is not fatal.
        echo        A certificate for https can be made by built-in PowerShell:
        echo        New-SelfSignedCertificate + Export-PfxCertificate
        echo        See the documentation, chapter "HTTPS".
    )
)
echo.

rem ---------- Summary ----------
echo === Summary ===
if %ERRORS% NEQ 0 (
    echo [ERROR] Some steps failed - see messages above.
    exit /b 1
)
echo All required tools are installed.
echo If anything was installed just now, open a NEW console
echo and re-run this script to verify PATH is updated.
exit /b 0

:install_iscc
echo [....] Inno Setup 6 not found - installing via winget...
winget install --id JRSoftware.InnoSetup -e --silent --accept-package-agreements --accept-source-agreements
if errorlevel 1 (
    echo [ERROR] Inno Setup installation failed.
    echo         Manual install: https://jrsoftware.org/isdl.php
    set ERRORS=1
) else (
    echo [OK]   Inno Setup 6 installed - MakePackage.cmd can build installers now.
)
goto :eof

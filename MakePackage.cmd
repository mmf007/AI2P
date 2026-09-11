@echo off
rem ============================================================
rem  AI2P - MakePackage.cmd  (Windows, T-285)
rem  A wrapper around MakePackage.ps1: a .ps1 in Windows is associated with
rem  "edit", so from Explorer or FAR it opens in Notepad instead of running.
rem
rem  RUN IT FROM THE RELEASE FOLDER (builds\<os>\release or releasefull).
rem  The result is one installer file in ..\..\packages:
rem      releasefull -> AI2P_full_v_1_NN_win64.exe
rem      release     -> AI2P_v_1_NN_win64.exe
rem  The build number NN is taken from version.json of the release by itself.
rem
rem  Usage:
rem    MakePackage.cmd                 build the installer
rem    MakePackage.cmd D:\out          put the package into another folder
rem    MakePackage.cmd -Lang ru        the language of the messages
rem    MakePackage.cmd --help          the full help
rem    MakePackage.cmd --help -Lang ru the full help in Russian
rem  Inno Setup 6 (ISCC.exe) is needed - install_required.bat installs it.
rem
rem  THIS FILE IS PURE ASCII AND IN ENGLISH ON PURPOSE (T-65-S0): cmd.exe decodes
rem  a .cmd in the console codepage and keeps its read position in BYTES, so a
rem  multi-byte character shifts the parsing. All the localized text belongs to
rem  MakePackage.ps1 - --help is simply forwarded there.
rem
rem  -Lang IS PARSED HERE AND FORWARDED, INCLUDING TO --help (T-65-S0 rework):
rem  the wrapper used to call the .ps1 with -Help alone and dropped the rest of
rem  the command line, so "MakePackage.cmd --help -Lang ru" printed English.
rem  %~dp0 IS SAVED INTO %SELF% BEFORE THE FIRST SHIFT: shift moves %0 too.
rem ============================================================
setlocal
set "SELF=%~dp0"
set "MPOUT="
set "MPLANG="
set "MPHELP="

:parse
if "%~1"=="" goto run
if /i "%~1"=="--help" goto arghelp
if /i "%~1"=="-h" goto arghelp
if "%~1"=="/?" goto arghelp
if /i "%~1"=="-Lang" goto arglang
if /i "%~1"=="--lang" goto arglang
if not defined MPOUT set "MPOUT=%~1"
shift
goto parse

:arghelp
set "MPHELP=1"
shift
goto parse

:arglang
shift
set "MPLANG=%~1"
shift
goto parse

:run
set "MPLANGOPT="
if defined MPLANG set "MPLANGOPT=-Lang %MPLANG%"
if defined MPHELP goto help
if defined MPOUT (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SELF%MakePackage.ps1" -Output "%MPOUT%" %MPLANGOPT%
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SELF%MakePackage.ps1" %MPLANGOPT%
)
exit /b %ERRORLEVEL%

:help
powershell -NoProfile -ExecutionPolicy Bypass -File "%SELF%MakePackage.ps1" -Help %MPLANGOPT%
exit /b %ERRORLEVEL%

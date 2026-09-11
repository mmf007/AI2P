@echo off
rem ============================================================
rem  AI2P - build.cmd  (Windows)
rem  A wrapper around build.ps1: a .ps1 in Windows is associated with "edit",
rem  so from Explorer or FAR it opens in Notepad instead of running.
rem  Usage:  build.cmd [Debug|Release] [-Lang xx]   (Debug by default)
rem          build.cmd --help [-Lang xx]
rem
rem  THIS FILE IS PURE ASCII AND IN ENGLISH ON PURPOSE (T-65-S0): cmd.exe decodes
rem  a .cmd in the console codepage and keeps its read position in BYTES, so a
rem  multi-byte character shifts the parsing. The help text belongs to build.ps1.
rem
rem  -Lang IS PARSED HERE AND FORWARDED, INCLUDING TO --help (T-65-S0 rework):
rem  the wrapper used to call the .ps1 with -Help alone and dropped the rest of
rem  the command line, so "build.cmd --help -Lang ru" printed English.
rem  %~dp0 IS SAVED INTO %SELF% BEFORE THE FIRST SHIFT: shift moves %0 too.
rem ============================================================
setlocal
set "SELF=%~dp0"
set "CONFIG="
set "BLANG="
set "BHELP="

:parse
if "%~1"=="" goto run
if /i "%~1"=="--help" goto arghelp
if /i "%~1"=="-h" goto arghelp
if "%~1"=="/?" goto arghelp
if /i "%~1"=="-Lang" goto arglang
if /i "%~1"=="--lang" goto arglang
if not defined CONFIG set "CONFIG=%~1"
shift
goto parse

:arghelp
set "BHELP=1"
shift
goto parse

:arglang
shift
set "BLANG=%~1"
shift
goto parse

:run
set "BLANGOPT="
if defined BLANG set "BLANGOPT=-Lang %BLANG%"
if defined BHELP goto help
if not defined CONFIG set "CONFIG=Debug"
powershell -NoProfile -ExecutionPolicy Bypass -File "%SELF%build.ps1" -Configuration %CONFIG% %BLANGOPT%
exit /b %ERRORLEVEL%

:help
powershell -NoProfile -ExecutionPolicy Bypass -File "%SELF%build.ps1" -Help %BLANGOPT%
exit /b %ERRORLEVEL%

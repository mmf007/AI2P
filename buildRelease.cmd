@echo off
rem ============================================================
rem  AI2P - buildRelease.cmd  (Windows)   [called publish.cmd before T-285]
rem  A wrapper around buildRelease.ps1: a .ps1 in Windows is associated with
rem  "edit", so from Explorer or FAR it opens in Notepad instead of running.
rem  Usage:  buildRelease.cmd [folder] [Release|Debug] [full] [-Lang xx]
rem          buildRelease.cmd --help [-Lang xx]
rem  Defaults: builds\windows\release, configuration Release.
rem
rem  The "full" switch (also -SelfContained) means A RELEASE WITH THE RUNTIME
rem  INSIDE: the default folder is builds\windows\releasefull and there is
rem  nothing to install on the machine. Without it everything is as before:
rem  builds\windows\release, the runtime is taken from the machine.
rem
rem  The OS folder (windows, linux, macos) between builds and release appeared in
rem  T-243: the releases of different systems no longer overwrite each other.
rem  The builds folder itself lives INSIDE the working folder, next to AI2P.sln
rem  (T-131-S0); before 1.114 it was one level above it.
rem
rem  THIS FILE IS PURE ASCII AND IN ENGLISH ON PURPOSE (T-65-S0): cmd.exe decodes
rem  a .cmd in the console codepage and keeps its read position in BYTES, so a
rem  multi-byte character shifts the parsing. The help text belongs to the .ps1.
rem
rem  -Lang IS PARSED HERE AND FORWARDED, INCLUDING TO --help (T-65-S0 rework):
rem  the wrapper used to call the .ps1 with -Help alone and dropped the rest of
rem  the command line, so "buildRelease.cmd --help -Lang ru" printed English.
rem  Both spellings are accepted (-Lang and --lang) and normalized to -Lang.
rem  %~dp0 IS SAVED INTO %SELF% BEFORE THE FIRST SHIFT: shift moves %0 too, and
rem  after it %~dp0 is the folder of an ARGUMENT, not of this script.
rem ============================================================
setlocal
set "SELF=%~dp0"

set "PUBOUT="
set "PUBCONFIG="
set "PUBFULL="
set "PUBLANG="
set "PUBHELP="

:parse
if "%~1"=="" goto run
if /i "%~1"=="--help" goto arghelp
if /i "%~1"=="-h" goto arghelp
if "%~1"=="/?" goto arghelp
if /i "%~1"=="-Lang" goto arglang
if /i "%~1"=="--lang" goto arglang
if /i "%~1"=="full" goto argfull
if /i "%~1"=="-SelfContained" goto argfull
if /i "%~1"=="--self-contained" goto argfull
if not defined PUBOUT goto argout
if not defined PUBCONFIG goto argcfg
shift
goto parse

:arghelp
set "PUBHELP=1"
shift
goto parse

:arglang
shift
set "PUBLANG=%~1"
shift
goto parse

:argfull
set "PUBFULL=-SelfContained"
shift
goto parse

:argout
set "PUBOUT=%~1"
shift
goto parse

:argcfg
set "PUBCONFIG=%~1"
shift
goto parse

:run
set "PUBLANGOPT="
if defined PUBLANG set "PUBLANGOPT=-Lang %PUBLANG%"
if defined PUBHELP goto help
if not defined PUBCONFIG set "PUBCONFIG=Release"
if defined PUBOUT (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SELF%buildRelease.ps1" -Output "%PUBOUT%" -Configuration %PUBCONFIG% %PUBFULL% %PUBLANGOPT%
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%SELF%buildRelease.ps1" -Configuration %PUBCONFIG% %PUBFULL% %PUBLANGOPT%
)
exit /b %ERRORLEVEL%

:help
powershell -NoProfile -ExecutionPolicy Bypass -File "%SELF%buildRelease.ps1" -Help %PUBLANGOPT%
exit /b %ERRORLEVEL%

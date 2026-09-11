@echo off
rem ============================================================
rem  AI2P - install.cmd  (Windows)
rem  A wrapper around install.ps1: a .ps1 in Windows is associated with "edit",
rem  so from Explorer or FAR it opens in Notepad instead of running. A .cmd runs.
rem  It lives IN THE RELEASE FOLDER next to install.ps1 (buildRelease puts it
rem  there) and works the same way: the source is this folder, the target is the
rem  first parameter.
rem
rem  Usage:
rem    install.cmd D:\AI2P                  install or update
rem    install.cmd D:\AI2P -WhatIf          only show what would be done
rem    install.cmd D:\AI2P -Force           do not compare versions; install the
rem                                         runtime without asking
rem    install.cmd D:\AI2P -NoRuntimeCheck  do not check the .NET runtime (T-211)
rem    install.cmd D:\AI2P -Lang ru         the language of the messages
rem    install.cmd --help                   the full help
rem    install.cmd --help -Lang ru          the full help in Russian
rem
rem  THIS FILE IS PURE ASCII AND IN ENGLISH ON PURPOSE (T-65-S0, the mine found in
rem  T-34-S0): cmd.exe decodes a .cmd in the console codepage and keeps its read
rem  position in BYTES, so a multi-byte character shifts the parsing and the tail
rem  of the next line lands in stdout. All the localized text belongs to
rem  install.ps1 - --help is simply forwarded there.
rem
rem  THE WHOLE COMMAND LINE IS FORWARDED TO --help TOO (T-65-S0 rework): the
rem  wrapper used to call the .ps1 with -Help alone, so "install.cmd --help
rem  -Lang ru" printed English. The double-dash spelling --lang is normalized
rem  to -Lang here - PowerShell does not bind "--lang" to a parameter.
rem  %~dp0 IS SAVED INTO %SELF% BEFORE THE FIRST SHIFT: shift moves %0 too.
rem ============================================================
setlocal
set "SELF=%~dp0"
set "ARGS="
set "IHELP="
if "%~1"=="" set "IHELP=1"

:parse
if "%~1"=="" goto run
if /i "%~1"=="--help" goto arghelp
if /i "%~1"=="-h" goto arghelp
if "%~1"=="/?" goto arghelp
if /i "%~1"=="--lang" goto arglang
if /i "%~1"=="-lang" goto arglang
set "ARGS=%ARGS% %1"
shift
goto parse

:arghelp
set "IHELP=1"
shift
goto parse

:arglang
shift
set "ARGS=%ARGS% -Lang %1"
shift
goto parse

:run
if defined IHELP goto help
powershell -NoProfile -ExecutionPolicy Bypass -File "%SELF%install.ps1"%ARGS%
exit /b %ERRORLEVEL%

:help
powershell -NoProfile -ExecutionPolicy Bypass -File "%SELF%install.ps1" -Help%ARGS%
exit /b %ERRORLEVEL%

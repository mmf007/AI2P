@echo off
rem ============================================================
rem  AI2P - makeAsServise.cmd  (Windows, T-271)
rem  A wrapper around makeAsServise.ps1: a .ps1 in Windows is associated with
rem  "edit", so from Explorer or FAR it opens in Notepad instead of running.
rem
rem  It lives IN THE INSTALL FOLDER next to AI2P.Server.exe and turns that
rem  installation into an OS service named AI2P. RUN IT AS ADMINISTRATOR.
rem
rem  Usage:
rem    makeAsServise.cmd                   create the service and start it
rem    makeAsServise.cmd -NoStart          create it but do not start it
rem    makeAsServise.cmd -Manual           start the service by hand
rem    makeAsServise.cmd -Remove           remove the service (files are kept)
rem    makeAsServise.cmd -UnprotectSecrets drop DPAPI from the organization keys
rem    makeAsServise.cmd -Force            do it in spite of the warnings
rem    makeAsServise.cmd -WhatIf           only show what would be done
rem    makeAsServise.cmd -Lang ru          the language of the messages
rem    makeAsServise.cmd --help            the full help
rem    makeAsServise.cmd --help -Lang ru   the full help in Russian
rem
rem  THIS FILE IS PURE ASCII AND IN ENGLISH ON PURPOSE (T-65-S0): cmd.exe decodes
rem  a .cmd in the console codepage and keeps its read position in BYTES, so a
rem  multi-byte character shifts the parsing. All the localized text belongs to
rem  makeAsServise.ps1 - --help is simply forwarded there.
rem
rem  THE WHOLE COMMAND LINE IS FORWARDED TO --help TOO (T-65-S0 rework): the
rem  wrapper used to call the .ps1 with -Help alone, so "--help -Lang ru"
rem  printed English. The double-dash spelling --lang is normalized to -Lang.
rem  %~dp0 IS SAVED INTO %SELF% BEFORE THE FIRST SHIFT: shift moves %0 too.
rem ============================================================
setlocal
set "SELF=%~dp0"
set "ARGS="
set "MHELP="

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
set "MHELP=1"
shift
goto parse

:arglang
shift
set "ARGS=%ARGS% -Lang %1"
shift
goto parse

:run
if defined MHELP goto help
powershell -NoProfile -ExecutionPolicy Bypass -File "%SELF%makeAsServise.ps1"%ARGS%
exit /b %ERRORLEVEL%

:help
powershell -NoProfile -ExecutionPolicy Bypass -File "%SELF%makeAsServise.ps1" -Help%ARGS%
exit /b %ERRORLEVEL%

@echo off
rem ============================================================
rem  AI2P - ai2p.cmd  (Windows)
rem  Command line client for the AI agent (T-34-S0): a thin wrapper around
rem  "AI2P.Server.exe cli ...". It lives IN THE INSTALL FOLDER next to the
rem  program and starts it with the "cli" subcommand, so the release holds
rem  no second executable.
rem
rem  Credentials come from the environment variables that AI2P itself sets for
rem  the agent process: AI2P_URL (the loopback address of the server) and
rem  AI2P_JOB_TOKEN (the job token, alive only while the job runs). Outside a
rem  job the client does not work, and that is on purpose.
rem
rem  Usage:
rem    ai2p help                     the commands and the actions of this job
rem    ai2p --help                   the same help out of i18n\help\ai2p.<lang>.txt
rem    ai2p parent                   the parent task
rem    ai2p siblings                 the sibling tasks
rem    ai2p children T-22            the subtasks of a task
rem    ai2p task T-15                a task by its code
rem    ai2p status T-15 --status pending --restart
rem
rem  THIS FILE IS ASCII ONLY, AND THAT IS NOT A STYLE CHOICE (found live in
rem  T-34-S0). cmd.exe decodes a .cmd in the console codepage and keeps its
rem  read position in BYTES: a multi-byte character shifts the position, cmd
rem  resumes in the middle of the next line and prints its tail into stdout
rem  ("'...' is not recognized as an internal or external command"). The agent
rem  reads that stdout, so the answer would arrive with garbage glued in front.
rem ============================================================
setlocal
rem --help prints i18n\help\ai2p.<lang>.txt (T-65-S0). It goes through PowerShell on
rem purpose: "type" would decode a UTF-8 file in the console codepage and turn a
rem translated help into garbage, while this .cmd itself stays pure ASCII.
rem The language of the help is taken from "-Lang xx" / "--lang xx" of the same
rem command line (T-65-S0 rework: it used to be dropped, so --help -Lang ru
rem printed English). Without it the ladder of loc.ps1 works: AI2P_LANG, then en.
rem %~dp0 IS SAVED INTO %SELF% BEFORE THE FIRST SHIFT: shift moves %0 too, and
rem after it %~dp0 would be the folder of an ARGUMENT. (%* is NOT shifted.)
set "SELF=%~dp0"
if /i "%~1"=="--help" goto help
if /i "%~1"=="-h" goto help
if "%~1"=="/?" goto help
goto run

:help
set "ALANG="
:helpparse
shift
if "%~1"=="" goto helprun
if /i "%~1"=="-Lang" goto helplang
if /i "%~1"=="--lang" goto helplang
goto helpparse

:helplang
shift
set "ALANG=%~1"
goto helpparse

:helprun
if exist "%SELF%i18n\loc.ps1" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command ". '%SELF%i18n\loc.ps1'; Set-Ai2pLang '%ALANG%'; Show-Ai2pHelp 'ai2p'"
    exit /b 0
)

:run
if not exist "%SELF%AI2P.Server.exe" (
    echo AI2P.Server.exe not found next to ai2p.cmd - run it from the AI2P install folder.
    exit /b 1
)
"%SELF%AI2P.Server.exe" cli %*
exit /b %ERRORLEVEL%

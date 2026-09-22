# AI2P — building, releasing and the layout of the repository

> The main page of the repository is `Readme.md` in its root; the documentation for the user is the
> [short description](../README.md) and the [contents](../index.md). Everything here is for the one
> who builds AI2P from the sources.

A system of joint work of AI agents and people on tasks: a work planner, an orchestrator of AI
agents, an accumulator of experience. It works locally (Windows 10–11, Linux, macOS), the UI goes
through a web browser.

The documentation falls into two parts: the **project** one — in the `doc/` directory in the root of
the repository (`doc/AI2P_ТЗ_v1.NN.md` — the description of the system as it is, the previous
versions are there too; from version 1.60 the number of the specification matches the version of the
application; `doc/AI2P_release.md` — the versions, the release and the update), and the one
**shipped together with the program** — in `AI2P_app/doc/` next to the code: the documents of the
models ([`models/`](../models/README.md)) and of the import kinds
([`import/`](../import/README.md)), they are opened in the UI by the "i" button. The `AI2P_app/doc`
directory is copied into the release output as a whole.

> Links to files outside `AI2P_app/doc` are not put into a shipped document: it is drawn inside the
> application, and a relative link outwards leads nowhere there (specification, ch. 14).

**The state: stage 1 — a local task planner.** Implemented: SQLite + an event log + file storage
(specification, ch. 6), the entities (ch. 2), the HTTP API (API-first, ch. 3), the UI in the VS Code
style (ch. 11): the task board (kanban, drag-n-drop), the task card (a .md description, the history,
the chat, the artifacts, the jobs), the lists and forms of projects, teams and executors, the work
history with filters, a person's Inbox, the settings; the HumanConnector (a human executor through
the job queue); multilingualism ru/en (the JSON dictionaries in `i18n/`).

## Setting up the build environment

The `install_required` scripts check and install the build tools: **.NET SDK 8+**, the **ASP.NET Core
8.x runtime**, **CMake**, a **C/C++ compiler**, and then initialise the git submodules (if there is a
`.gitmodules`). The scripts are idempotent — what is already installed is not reinstalled; they may
be run again as a check.

> **Why exactly the 8.x runtime is needed even if SDK 9/10 is installed.** The application is built
> for net8.0; a newer SDK builds it, but it must not be run on the 9/10 runtime (roll-forward): the
> Blazor client script (`blazor.web.js`) is taken of version 8 while the server would work on 9/10 —
> the interactivity (the buttons, the events) silently stops working. An example: the `dotnet-sdk`
> brew cask on macOS currently installs .NET 10 — the script will add the 8.0 runtime next to it, and
> the application will start on that.

### Windows

Run it in an ordinary console (cmd or PowerShell); UAC confirmation requests are possible. winget is
required (the "App Installer" from the Microsoft Store, present in Windows 10/11 by default).

```bat
cd AI2P_app
install_required.bat
```

It will ask for administrator rights. But not right away. The dialog may not appear in front — you
have to look for it among the active applications.

What it does:

1. **.NET SDK 8+** — if there is no version ≥ 8, it installs `Microsoft.DotNet.SDK.8` through winget;
   then it checks the **ASP.NET Core 8.x runtime** and, if it is absent, installs
   `Microsoft.DotNet.AspNetCore.8`;
2. **CMake** — if it is absent, it installs `Kitware.CMake`;
3. **The C/C++ compiler** — it looks for MSVC through vswhere; if there is none, it installs the VS
   2022 Build Tools with the VCTools workload (a big download, several GB);
4. **git submodules** — `git submodule update --init --recursive` (skipped while there is no
   `.gitmodules`).

After the installation open a **new** console (so that the PATH is refreshed) and run the script once
more — it should show all `[OK]`.

### Linux (apt / dnf) and macOS (brew)

```sh
cd AI2P_app
chmod +x install_required.sh
./install_required.sh
```

What it does: it determines the OS and the package manager (apt-get / dnf / brew), and then the same
steps:

1. **.NET SDK 8+** — `dotnet-sdk-8.0` (macOS: `brew install --cask dotnet-sdk`); then the **ASP.NET
   Core 8.x runtime** — on Linux the `aspnetcore-runtime-8.0` package; on macOS the official
   `dotnet-install.sh` installs the 8.0 runtime next to the existing SDK into the same dotnet
   directory (it may ask for sudo);
2. **CMake**;
3. **The C/C++ compiler** — on Linux `build-essential` (apt) or `gcc gcc-c++ make` (dnf); on macOS the
   Xcode Command Line Tools (`xcode-select --install` — a dialog will appear, after the installation
   run the script again);
4. **git submodules** — as on Windows.

Installing the packages may ask for the sudo password. On macOS [Homebrew](https://brew.sh/) has to
be installed beforehand.

## Installing the packages

The dependencies fall into two kinds (specification, ch. 13.1):

1. **Binary ones (NuGet)** — MudBlazor, Microsoft.Data.Sqlite, Serilog, Microsoft.Extensions.AI, the
   OpenAI SDK, Anthropic.SDK, ModelContextProtocol and others. They **do not have to be installed
   separately**: `dotnet restore` (a part of `dotnet build`) downloads them automatically by the
   references from the `.csproj`.
2. **Subproject packages (the sources)** in `packages/` — sqlite-vec, gigachat-adapter (candidates,
   they will be needed at stages 3+). They have no separate scripts any more:
   `install_packages.bat` and `install_packages.sh` were removed in T-65-S0 as unnecessary — no list
   of packages ever appeared in them over the whole history of the project, and the work ascribed to
   them is done by others: the build tools are installed by `install_required.*`, and the packages
   for the models (ComfyUI, musubi-tuner, Python) by the program itself when a model is installed. If
   a subproject is needed, it is added by a single command:

```sh
git submodule add <address> packages/<name>
```

There is no `packages/` directory in the repository at all right now (the empty one was removed in
T-207): all the current dependencies are NuGet ones, and `git submodule add` creates the directory
itself. The empty `native/` — the groundwork for C/C++ plugins (CMake) — is removed as well.

## The language of the scripts' output

The build and installation scripts speak **English by default** (T-65-S0). A script is not duplicated
per language: the texts are moved outside, into the `i18n/` directory:

```
i18n/scripts.en.txt          the messages, the base language
i18n/scripts.ru.txt          the same set of keys in Russian
i18n/loc.ps1                 the loader for PowerShell
i18n/loc.sh                  the loader for POSIX sh
i18n/help/<script>.<language>.txt  the text printed by --help
```

The language is chosen by a ladder, the first non-empty one wins:

1. the script's switch — `-Lang ru` (PowerShell) or `--lang ru` (sh);
2. the `AI2P_LANG` environment variable;
3. `en`.

The `"language"` key from `config.json` is deliberately NOT asked about: the language of the
application interface and the language of the installation console are different things.

The help is printed by `--help` (for a `.ps1` — `-Help`), and it is taken from a separate file:

```powershell
install.cmd --help
install.cmd D:\AI2P -Lang ru
.\makeAsServise.cmd --help
.\MakePackage.cmd --help
```

```sh
./install.sh --help
./install.sh ~/ai/AI2P --lang ru
./makeAsServise.sh --help
./MakePackage.sh --help
```

The `.cmd`/`.bat` wrappers translate nothing themselves and are written in **pure ASCII, in
English**: `cmd.exe` decodes the file in the console codepage while keeping the reading position in
bytes, and one multi-byte character shifts the parsing of everything that follows (caught live in
T-34-S0). They pass all the text, including `--help`, on to their `.ps1`.

A new language is **two files and not a line of code**: a copy of `scripts.en.txt` with translated
values and copies of the needed files of `help/`. The sets of keys have to match one for one, that is
guarded by `T65S0Tests`.

The own output of `build.*`, `buildRelease.*` and `install_required.*` is deliberately not translated
(tier B of the T-64-S0 analysis): they are run on the build machine, and they are already English or
mixed. They do have a `--help`.

## Building and running

Building the whole solution (C# only for now; the C/C++ will be added later) with a single
script:

```powershell
# Windows
cd AI2P_app
.\build.ps1              # Debug; a variant: .\build.ps1 -Configuration Release
build.cmd                # the same; it starts by Enter from the explorer or FAR
                         # (a .ps1 on Windows is associated with "edit", not with "run");
                         # a variant: build.cmd Release
```

```sh
# Linux / macOS
cd AI2P_app
chmod +x build.sh
./build.sh               # Debug; a variant: ./build.sh Release
```

Running:

```sh
dotnet run --project src/AI2P.Server
```

The server raises the UI at `http://localhost:5480` (the port and the rest are in `config.json`) and
opens the browser (`openBrowserOnStart`). The path to the config may be overridden:
`dotnet run --project src/AI2P.Server -- --config <path>`.

The `config.json` with the default settings is copied into the build directory **only if it is not
there yet** — local edits are not wiped by a rebuild (specification, ch. 10).

## A release output into a separate directory

The output is a self-sufficient set of files that runs without the sources and without `dotnet run`.
It is needed so that a working old version stays next to the one being developed.

```powershell
# Windows
cd AI2P_app
buildRelease.cmd                          # Release into builds\windows\release
buildRelease.cmd D:\AI2P_v1.45            # into the given directory
.\buildRelease.ps1 -Clean                 # clear the directory (except the data) and lay it out anew
.\buildRelease.ps1 -SelfContained         # with the runtime inside: ASP.NET Core 8 is not needed on the machine
```

```sh
# Linux / macOS
cd AI2P_app
chmod +x buildRelease.sh
./buildRelease.sh                         # Release into builds/linux/release
./buildRelease.sh ~/ai/AI2P --clean
```

Before T-285 these scripts were called `publish.cmd` / `publish.ps1` / `publish.sh` — the name
changed, the behaviour stayed.

The `builds` directory itself lives **inside the working directory** `AI2P_app`, next to `AI2P.sln`
(T-131-S0); before 1.114 it was created one level above, outside the working directory.

Between `builds` and `release`/`releasefull` there stands the **directory of the operating system**
(T-243): `windows`, `linux` or `macos`. The system is set by the RID of a full output
(`-Runtime`/`--runtime`), and for an ordinary one by the system the build runs on; the same thing
decides which installation scripts are put into the output. What is built for different systems does
not overwrite each other any more.

A laid-out copy is started by `AI2P.Server.exe` (Windows) or `./AI2P.Server` from its directory; the
current directory of the process does not matter.

**The server runs as an ORDINARY user, not as root** (T-135). The installation directory on Linux and
macOS is `~/ai/AI2P`: the data (`data/`), the journals (`logs/`), the settings and the secrets lie
inside it, the server does not write outside the home directory, and the port (5480) is an
unprivileged one. Root rights are needed only by the dependency installer (`install_required.sh`) —
that is a one-off, system-level operation. The application closes the `secrets.json` file with `0600`
rights: it holds the server administrator password and the organization keys.

A path with `~` is understood both by the scripts and by the application itself: `./install.sh
~/ai/AI2P`, the directories in `config.json` and in the local server form (`~/ai`), the project
directory, the security rules.

What the output **does not copy** and **does not overwrite** on a repeated run — it is moved by hand:

* `data/` — the database and the project files;
* `logs/` — if needed;
* `secrets.json` — the API keys (without it the cloud models are inactive; a
  `secrets.example.json` lies next to it);
* `config.json` — it is created from the defaults only if it is not in the directory yet.

Two versions do not work on one port at the same time (the single-instance check, specification, ch.
3): the second one will see the port taken, open the browser on the first one and finish. To keep both
running, change `ui.port` in the `config.json` of the laid-out copy.

## An installation package as a single file (T-285)

The output is a directory; giving it to a person is inconvenient. `MakePackage` makes **a single
installer file** out of a ready output. The script is put into the output by `buildRelease` itself,
and it has to be run **from the output directory**; the result lands in `../../packages` (that is,
`builds/packages`).

```powershell
# Windows: Inno Setup 6 is needed (install_required.bat installs it)
cd builds\windows\releasefull
.\MakePackage.cmd                    # -> ..\..\packages\AI2P_v_1_99_full_windows_x64.exe
cd ..\release
.\MakePackage.cmd                    # -> ..\..\packages\AI2P_v_1_99_windows_x64.exe
```

```sh
# Linux / macOS: makeself is needed (install_required.sh installs it)
cd builds/linux/releasefull
./MakePackage.sh                     # -> ../../packages/AI2P_v_1_99_full_linux_x64.run
```

The file name is put together by itself from the output's `version.json` — the build number `NN` does
not have to be asked for:

| the output | Windows | Linux | macOS |
|---|---|---|---|
| `releasefull` | `AI2P_v_1_NN_full_windows_x64.exe` | `AI2P_v_1_NN_full_linux_x64.run` | `AI2P_v_1_NN_full_macos_arm64.run` |
| `release` | `AI2P_v_1_NN_windows_x64.exe` | `AI2P_v_1_NN_linux_x64.run` | `AI2P_v_1_NN_macos_arm64.run` |

The parts of the name come in this order: `AI2P_v_` + the version number + `_full` for the full
release + the system (`windows`, `linux`, `macos`) + the architecture (`x64`, `arm64`, `arm`,
`x86`). For a full release the system and the architecture are named by its runtime (`win-x64`,
`linux-arm64`, `osx-arm64`); for the usual one by the OS folder and by the architecture of the
current compiler (T-234-S0).

The extension sets the kind of the installer: on Windows this is Inno Setup (`.exe`), on Linux and
macOS a self-extracting makeself archive (`.run`), which runs the same `install.sh` inside. The
package is built on the system the output was built for: there is nothing to build a `.run` with from
Windows, and the other way round — the script will honestly say so.

The user's data (`data/`, `logs/`, `secrets/`, `secrets.json`, the `installed.json` inventory) does
not get into the package, and is not touched when installing over a previous one: `config.json` is put
only if it is absent, and a `config.new.json` is always put next to it — the application will merge
them at the first start (`ConfigMerge`).

**Where the working files lie (T-287).** Next to the program — but only if its directory is writable.
An "for all users" installation (`C:\Program Files\AI2P`) is closed for writing to an ordinary user,
so its `config.json`, `data/`, `logs/` and `secrets/` lie in the common data directory of the
computer: on Windows `C:\ProgramData\AI2P`, on Linux and macOS `/var/lib/ai2p` (and if that is not
allowed either — in the user's data directory). The rule lives in one place — `AppHome`
(`src/AI2P.Server/AppHome.cs`), the scripts do not repeat it but only look for an already created
`config.json` in the same places. The directory may also be set by hand: by the `AI2P_HOME` variable
or by the `--config` switch. The details are in `doc/en/man/install.md`.

## The structure of the solution

```
AI2P.sln
src/
├── AI2P.Core/        — the core: the entities (ch. 2), the events (6.4.3), the API contracts,
│                       the IAgentConnector interface (7.2)
├── AI2P.Storage/     — SQLite (the schema, 6.4.2), the event log, the file storage (6.4.4),
│                       the services: projects, teams, executors, tasks, jobs, chat
├── AI2P.Connectors/  — the HumanConnector + the task start orchestrator; the AI connectors are stage 2
├── AI2P.UI/          — the Blazor components (MudBlazor): the VS Code frame (ch. 11), the board,
│                       the task card, the Inbox, the history, the reference books; ApiClient, i18n
└── AI2P.Server/      — the ASP.NET Core host: the HTTP API (/api/...) + the Blazor Server UI,
                        config.json, Serilog (the .jsonl logs)
tests/     — AI2P.Tests: the tests of the storage, the event log, the human-executor cycle
i18n/      — the localization dictionaries (ru.json, en.json) and the texts of the build
              and installation scripts (scripts.<language>.txt, loc.ps1, loc.sh,
              help/<script>.<language>.txt, T-65-S0) — they are copied into the build directory
doc/       — the documentation next to the code
```

## The tests

```sh
cd AI2P_app
dotnet test
```

## The data storage

The `storage.dataDir` directory from `config.json` (by default `./data` next to the application):
`ai2p.db` (SQLite, WAL) + `projects/<slug>/tasks/<T-N>/description.md`, `artifacts/`, `.trash/`. The
primary source of truth about the history is the event journal (the `events` table); the technical
logs are in `logging.dir`, the files `ai2p-YYYYMMDD.jsonl`. A second instance of the application
detects the taken port, opens the browser on the running instance and finishes (specification, ch. 3,
principle 5).

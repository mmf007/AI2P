# Installing AI2P and where its data lives

This chapter answers two questions: **where the program is installed** and **where its working
files live afterwards** — the configuration, the task database, the logs and the keys. The
second question matters more than it looks: the working files survive version updates, they
are what you copy when moving to another computer, and they are the ones you must not lose.

---

## 1. Three ways to install

| Way | What it is | When it is convenient |
|---|---|---|
| **Installation package** | a single file `AI2P_v_1_NN_…` (or `AI2P_v_1_NN_full_…` — with the runtime inside), installed by a double click | the usual case on Windows |
| **Installation script** | `install.cmd` / `install.sh` from the release directory | when the release is already downloaded and the directory is chosen by hand |
| **The release itself** | an unpacked directory, started by `AI2P.Server.exe` | a trial run, a portable installation on a flash drive |

On Windows the package asks whether to install **for all users** or **for me only**. The
program directory depends on that answer:

* **for all users** — `C:\Program Files\AI2P` (administrator rights are required);
* **for me only** — `C:\Users\<you>\AppData\Local\Programs\AI2P`.

On Linux and macOS the installation goes into the home directory: `~/ai/AI2P` by default.

---

## 2. Where the working files live

There is a single rule: **the working files live next to the program if its directory is
writable.**

Next to the program means `config.json`, the data directory `data/` (the server database, the
organization databases, the project files), the logs `logs/` and the keys `secrets/`.

An ordinary user **cannot** write into the Windows programs directory (`C:\Program Files`),
and that is a rule of the system rather than a fault: otherwise any user of the computer could
replace `AI2P.Server.exe`, which an administrator then runs. So an all-users installation keeps
its working files elsewhere:

| Installation | Program | Working files |
|---|---|---|
| Windows, all users | `C:\Program Files\AI2P` | **`C:\ProgramData\AI2P`** |
| Windows, me only | `…\AppData\Local\Programs\AI2P` | next to the program |
| A release in your own folder (`D:\AI2P`) | `D:\AI2P` | next to the program |
| Linux/macOS, `~/ai/AI2P` | `~/ai/AI2P` | next to the program |
| Linux/macOS, `/opt/ai2p` | `/opt/ai2p` | `/var/lib/ai2p`, or `~/.local/share/ai2p` without rights to it |

`C:\ProgramData\AI2P` is the shared directory of **this computer**: there is one installation,
the data is common, and the service (it runs as the system) sees the same data as the person
does. The installer creates that directory and opens it for writing to all users of the
computer; uninstalling the program **leaves it alone** — it is your data.

### How to know for sure

At startup the program says where its working files are:

```
AI2P: the program directory C:\Program Files\AI2P\ is not writable —
the working files (config.json, data, logs, secrets) are kept in C:\ProgramData\AI2P
```

The same line goes into the technical log (`logs/ai2p-<date>.jsonl`), and the full paths of the
configuration and of the data directory appear in the log right after the start.

### How to set the directory yourself

| Way | What it does |
|---|---|
| `AI2P.Server.exe --config D:\myAI2P\config.json` | take exactly this `config.json`; `data/`, `logs/` and `secrets/` will be next to it |
| the `AI2P_HOME=D:\myAI2P` environment variable | the same, but set once for a service, a container or a shortcut |

A directory set this way beats every rule: the program does not interfere with it.

---

## 3. Updating the version

Installing over an older version **does not touch** the working files: `config.json`, `data/`
and `secrets/` stay as they were. A `config.new.json` — the configuration of the new version —
is placed next to the program, and at the first start the program merges them: the new one is
the base (it holds every new parameter with its default), your values go on top. It reports the
merge with a console line, and a copy of the applied file (`config.new.json.applied`) is left
next to the working `config.json` — it shows what arrived and when.

The merge happens **exactly once per version**: a repeated start rewrites nothing.

### Data left next to the program

If you used to install AI2P into `C:\Program Files\AI2P` and ran it **as an administrator**, the
data may have been created right there. After the update the program will then say:

```
AI2P: the data directory C:\Program Files\AI2P\data next to the program is left over from
earlier runs — the data now lives in C:\ProgramData\AI2P; move it there by hand if you need it
```

It does **not** move that data itself: shifting a database behind the owner's back is not
something to do silently. The settings (the `config.json` of the previous installation) are
taken along, though — the port, the host name and the directories were set by you, and there is
no reason to lose them. To move the data as well: stop AI2P, copy the `data` directory (and
`secrets`, if it is there) into the new working directory and start it again.

### `AI2P_HOME: parameter not set` when updating on Linux/macOS

Versions **1.100 and 1.101** broke the update off at this line:

```
./install.sh: 147: AI2P_HOME: parameter not set
```

Nothing was copied at all, and the installed version stayed as it was. The installer itself
was to blame: it read the `AI2P_HOME` variable, which an ordinary installation does not have.
From version **1.102** on this no longer happens.

If all you have is a 1.100 or 1.101 release folder, it can still do the update — just set the
variable to an empty value:

```sh
AI2P_HOME= ./install.sh ~/ai/AI2P
```

The same cure works for `makeAsServise.sh` of those versions.

---

### Updating from the program itself

«Settings → Main», next to the version number, has a **«Check for updates»** button. It goes to
the releases repository (`https://github.com/mmf007/ai2p` by default, the address is edited right
there) and looks for the file **for this installation**: the same system, the same architecture and
the same installation kind — a full release (with the runtime inside) or an ordinary one. The
installation kind is read from the `version.json` lying next to the program.

If a newer version is out, an **«Update»** button lights up next to it. Before installing, the
program asks who is busy right now: the update **restarts the server**, and the running agents of
every open organisation will be stopped — the question shows their list. Then the package is
downloaded, the program shuts down, and a separate script finishes the job: it waits for the
process to end, installs the package silently and brings the server back (a service — with
`net start` / `systemctl`, a console run — by starting the program again). The page in the browser
will need a refresh.

Two checkboxes next to it:

* **automatic check** — only look whether a new version is out (the answer goes into the log);
* **automatic update** — check and install straight away.

When the program is run from a **console**, the automatic check happens at startup. When it works
as an **OS service**, there is no startup for weeks — then the checkbox creates an entry in the
**schedule** (once a day, 2:00 local time by default), and the settings show its code: the time is
changed in the entry itself, like in any schedule. Clearing the checkbox deletes the entry.

## 4. The operating system service

The service and the ordinary run use **the same** working directory — it is chosen by the
program directory, not by the rights of whoever started it. So making the installation a
service (`makeAsServise`, see [service](service.md)) does not give you a second database
"for the system".

---

## 5. If the program does not start

Started from a shortcut, AI2P is an ordinary console program: on a startup failure it **states
the reason in words and keeps the window** until a key is pressed (the window closes by itself
in a minute). What you may see:

| What is shown | What it means | What to do |
|---|---|---|
| `AI2P: startup failed — Access to the path … is denied` | the working files were pointed at a directory that cannot be written to | point at another one (`AI2P_HOME`, `--config`) |
| `There is nowhere to keep the AI2P working files` | the program directory and both data directories are all closed | the same: set the directory explicitly |
| `AI2P is already running (…) — opening the browser and exiting` | not an error: there is one instance per computer and it is already working | nothing |
| the window closes at once and silently | a version **older than 1.100**: a startup failure there took the window down together with the message | update, or run `AI2P.Server.exe` from a console and read the output |

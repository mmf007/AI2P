# Running AI2P as an operating system service

By default AI2P is an **ordinary console program**: you start `AI2P.Server.exe` and it runs,
you close the window and it stops. That is convenient for watching what happens, and that is
how the first start works.

A task server, however, is usually needed **all the time**: it keeps the job queue, launches
AI agents, replicates with the other servers of the cluster and watches the schedules. It has
no reason to wait until the owner of the computer logs in and starts a window. For that the
application can run as an **operating system service**.

The service name is **`AI2P`**, the same on every system.

---

## 1. How to turn an installation into a service

The service is created **by hand and once** — by the `makeAsServise` script in the
installation folder (the folder that holds `AI2P.Server.exe`; the installer puts the script
there).

### Windows

Open a console **as administrator** (only an administrator may create services) and run:

```powershell
cd D:\AI2P
.\makeAsServise.cmd
```

The script creates the `AI2P` service, sets it to start automatically with the computer,
enables restart after a failure and starts it right away. At the end it prints the address
where the interface opens.

Keys:

| Key | What it does |
|---|---|
| `-WhatIf` | only show what would be done; change nothing |
| `-NoStart` | create the service but do not start it |
| `-Manual` | start the service manually rather than at system startup |
| `-Remove` | remove the service (files and data are left alone) |
| `-Target D:\AI2P` | configure another installation, not the one the script was run from |
| `-Account .\mike -Password ***` | run the service as a user instead of LocalSystem |
| `-UnprotectSecrets` | remove the DPAPI protection from the organization keys (see section 4) |
| `-Force` | do it despite the warnings |

### Linux

```sh
cd ~/ai/AI2P
./makeAsServise.sh
```

A **user** systemd unit is created, `~/.config/systemd/user/AI2P.service`, and that is not
accidental: the AI2P server runs as an ordinary user, not as root, and keeps everything of
its own — data, logs, settings and secrets — inside its own folder. So that such a service
also comes up without anyone logging in, the script runs `loginctl enable-linger` itself.

A system unit (`/etc/systemd/system/AI2P.service`, needs `sudo`) is created with the
`--system` key. The other keys are `--no-start`, `--manual`, `--remove`, `--target <folder>`.

### macOS

```sh
cd ~/ai/AI2P
./makeAsServise.sh
```

A launchd job is created, `~/Library/LaunchAgents/AI2P.plist`. The keys are the same except
for `--system`.

---

## 2. How a service run differs from a console one

It is the same program; there is no separate «server» build. The application **finds out by
itself** how it was started: under the Windows service manager and under systemd it
recognizes itself, while launchd gives no such sign — there the mode is set by the
`--service` key right in the job.

There are only three differences:

* **the browser does not open at startup.** A service has no desktop and nowhere to open a
  window — open the address yourself;
* **a taken port is an error.** A console run on a taken port opens the browser on the
  already running instance and exits quietly; a service in that case exits **with an error**,
  otherwise the service manager would treat the quiet exit as normal work and say nothing;
* **it stops on the system's command** rather than on Ctrl+C: the application manages to
  close the database, unload the local models it started and finish the replication sessions.

Everything else — data, settings, port, interface, cluster — stays the same.

You can see how the server was started right in the interface: **Settings → General**, the
**«Run mode»** line next to the application version.

---

## 3. Managing the service

**Windows**

```powershell
Get-Service AI2P            # state
Start-Service AI2P
Stop-Service AI2P
.\makeAsServise.ps1 -Remove # remove the service
```

**Linux** (user unit)

```sh
systemctl --user status AI2P
systemctl --user start AI2P
systemctl --user stop AI2P
journalctl --user -u AI2P -f     # what the server writes
./makeAsServise.sh --remove
```

**macOS**

```sh
launchctl list | grep AI2P
launchctl unload ~/Library/LaunchAgents/AI2P.plist
launchctl load   ~/Library/LaunchAgents/AI2P.plist
./makeAsServise.sh --remove
```

In any mode the application writes its own logs into the `logs/` folder of the installation
(`ai2p-<date>.jsonl`) — the reason for a failed start is visible there as well.

---

## 4. Windows: which account the service runs under

This is the one place where the choice really matters.

By default the service runs as **LocalSystem**, the computer account. The **organization
key** (it encrypts the API keys of the models) is protected on Windows by DPAPI in the
**user scope**: only the account that wrote it can decrypt it. So a LocalSystem service will
not read the API keys, and the cloud models will stop working — from the outside it looks
like «the model is there but the job does not run».

That is why `makeAsServise` looks into `secrets.json` and, having found protected values
there, **does not create the service silently** but offers a choice:

* **`-Account <account> -Password <password>`** — the service runs as the same user and the
  keys stay available as before. The account needs the **«Log on as a service»** right
  (`secpol.msc` → Local Policies → User Rights Assignment); without it the service does not
  start and says so with error 1069;
* **`-UnprotectSecrets`** — remove DPAPI: the organization keys stay in `secrets.json` as
  ordinary base64, exactly the way they are stored on Linux and macOS, and the file
  permissions protect them. The previous file is kept next to it as `secrets.json.dpapi.bak`;
* **`-Force`** — create the service as is, knowing that the API keys are unavailable to it.

The same applies to the **Claude CLI login**: it belongs to the user profile, and a
LocalSystem service will not see it. If agents on this server work through the Claude CLI,
the service has to be created under the same user.

On Linux and macOS there is no such choice at all: the organization key is stored there as
ordinary base64, and the service already runs as the same user.

### When the working files are not next to the program

An installation **into a system program directory** (`C:\Program Files\AI2P`, `/usr`,
`/opt`, `/Applications`) keeps `config.json`, `data`, `logs` and `secrets` not next to the
program but in a data directory: on Windows that is `C:\ProgramData\AI2P`, on Linux and
macOS `/var/lib/ai2p`; if that one is not writable either, the application falls back to
the user's data directory.

`makeAsServise` handles this on its own: it finds the working `config.json`, puts the
`serviceMode` note there, looks for protected keys there — and **writes that path into the
service with the `--config` key**. The last part matters: the service runs under a
different account, and the user's data directory would be a **different** one for it, so
without an explicit path it would create an empty database instead of the working one. The
script prints such a case as a "Working files: …" line.

You can also set the directory yourself — with the `AI2P_HOME` environment variable or the
program's own `--config` key; every script then uses exactly that one.

---

## 5. Updating the version

**Nothing special is required.** The installation remembers that it is a service, and all
three ways of updating take that into account:

* `install.cmd` / `install.ps1` (Windows) and `./install.sh` (Linux, macOS) stop the service
  before copying the files and start it back afterwards;
* the **installation package** (`AI2P_v_1_NN_win64.exe`) does the same, and on uninstalling
  the program it also removes the service.

The service is looked up **in the system** — by the record in the service manager, the
systemd unit or the launchd job — and only the one that leads **exactly into this
installation**: a foreign one, leading to another folder, is never touched by any script.

The installation package on Windows needs administrator rights for that: there is no other
way to stop the service, and it says so honestly instead of failing later on locked files.

When the service is created, the mark `"serviceMode": true` is put into the `config.json` of
the installation. It is a **note**, not a switch: it survives a version update (the installer
does not touch the working `config.json`) and lets the application and the scripts say «this
installation is configured as a service, but right now there is no service in the system» —
for example when it was removed by hand or the folder was moved to another machine.

---

## 6. Frequently asked questions

**May I start the program by hand while the service is running?**
Yes, but on another port: two instances do not work on one port. A console run on a taken
port simply opens the browser on the already running server and exits.

**How do I see what the service is doing if it does not start?**
First `logs/ai2p-<date>.jsonl` in the installation folder — the application writes there in
any mode. If the log is empty, the process did not get as far as starting: on Windows look at
the event log and at the error text from `Start-Service`, on Linux at
`systemctl --user status AI2P`.

**I changed the port in `config.json` — do I need to do anything with the service?**
No. The service starts the same program from the same folder, and it reads the settings at
startup — restarting the service is enough.

**I moved the installation to another folder.**
Create the service again from the new folder: `makeAsServise` will see that the service leads
elsewhere and will ask to confirm the reassignment with the `-Force` key.

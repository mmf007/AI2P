# Plugins and MCP

The **Settings → Plugins and MCP** tab is the place where everything external is connected to
AI2P: programs installed on your computer (video editors, the ffmpeg converter) and MCP servers
with tools of their own.

The point of the section in one sentence: **the AI agent gets new actions, and rights are issued
for those actions**. A plugin is not an "extension" that may do whatever it likes: everything it
brings goes through the action catalog and through the security rules of the task.

## What a plugin is made of

A plugin record is four things at once:

| Part | Where it lives | What it gives |
|---|---|---|
| **Actions** | the action catalog, the security rules | new tools for the AI agent |
| **Experience records** | the general experience of the organisation | the agent knows how to use those tools |
| **Software** | the package catalog and the `config.json` of this server | the external program the plugin calls |
| **Document** | `doc/<language>/plugins/<code>.md` | the description, the **«i»** button in the list row |

The first two parts are a **property of the organisation**: they live in the database and are
replicated to all the servers. The third is a **property of this computer**: the located program
and its path are kept in the `config.json` of the server and are never replicated. On a neighbour
in the cluster the same program lives at a different path, and half of the servers have none at
all.

The description of the plugin itself arrives as a **manifest file**, `plugins/<code>/plugin.json`
in the data directory. The manifest lists the actions, names the program it needs and declares the
settings of the record; it is not edited by hand in the section — the actions and their parameters
change only together with the manifest.

The distribution carries six records: gateways into **Shotcut / Kdenlive**,
**DaVinci Resolve**, **Blender VSE** and **OpenShot**, the **video converter (ffmpeg)**
and the **LoRA adapter trainer (musubi-tuner)**.

## Two kinds of records: a gateway and an MCP connection

| | **Gateway** (`gateway`) | **MCP connection** (`mcp`) |
|---|---|---|
| What is out there | a program on this computer | an MCP server (its own process or a network address) |
| Where the action list comes from | from the manifest, it is fixed | **fetched from the server itself**, the list is dynamic |
| What it takes to make it work | find or install the program | connect and fetch the tool list |
| Secret | not needed | the MCP server token, if it requires one |
| Where the work is done | at our end: we write a file and call the program | on the other side |

The difference worth remembering: for a gateway the set of capabilities is known in advance and
changes only with a new version of AI2P, while **for an MCP server it can change any day**. That
is why the MCP tool list is fetched by an **explicit human action** — the "Refresh the tool list"
button — and it shows what was added and what is gone. An MCP server cannot silently grant itself
a new capability.

## States on this server

| State | What it means |
|---|---|
| **declared** | the manifest is on disk, there is no organisation record yet — the plugin has not been initialised |
| **looking for the software** | the record exists, but the program it needs was not found on this server and no path was given |
| **initialized** | everything is in place: the actions are registered, the experience is in, the program is found — the tools are published to the agent |
| **disabled** | a human has switched the plugin off for a while; the tools are not published, the records stay |
| **removed** | the actions and the experience records are deleted across the whole organisation |

The state is **each server's own**: on the computer that has Blender the plugin is initialized,
and on the next one it is "looking for the software". That is exactly the point of distributed
work: one machine shoots, another edits.

There is also a row **with no manifest**: the record arrived by replication while the file
`plugins/<code>/plugin.json` is not on this computer. Such a row is shown on purpose — otherwise
there would be no way at all to learn here about a plugin that works on a neighbour.

## How to initialise

1. Open **Settings → Plugins and MCP**. The list is built from the manifests on the disk of this
   server and from the records of the organisation.
2. Press **Initialise** in the plugin row. An organisation record is created, every action gets
   an entry in the action catalog, and the experience records of the plugin go into the general
   experience of the organisation. For a record of the MCP kind the tool list is fetched first of
   all: only something that has a catalog entry may be published to the agent.
3. Give the plugin its program — in one of three ways:
   * the plugin **found it itself** in `PATH` and checked the version — nothing to do;
   * **Install** — if the plugin has a package (this is how a portable Shotcut is installed, and
     this is how ffmpeg is installed);
   * **Point to where the program is already installed** — the path by hand; either the program
     file or the folder it was installed into will do. This is how DaVinci Resolve is connected:
     its distribution is handed out behind a registration form, and we cannot download it for you.
4. The **Check** button runs the search again and shows what was found.

Initialising is an **action of the server administrator**. A reader sees the section but has no
buttons in it: registering actions is issuing rights, not adjusting the looks.

It has to be done **on every server where the plugin is meant to work**: the organisation record
will reach the neighbour by replication on its own, the program and its path will not.

## "Software not found on this server"

That line is not an error. It means exactly what it says: the program is not on this computer and
no path to it was given. While that is so, **the actions of the plugin are not published to the
agent** — a tool that is known to answer with a refusal would waste the agent's move.

What to do:

* install the program with the **Install** button, if there is a package;
* install it yourself and show the path with **Point to where the program is already installed**;
* do nothing, if editing is not done on this computer: let the task go where the plugin is
  initialized.

The version check is part of the search: a program older than the minimum usable version counts
as not found. That is not pedantry but protection from a refusal half an hour into the run: the
set of keys of ffmpeg changed between 4.x and 7.x, and that of `melt` between versions six and
seven.

## MCP is connected only through our record

The rule is simple and hard: **an MCP server is connected by a plugin record and in no other way.**

An MCP server written straight into the configuration of the CLI agent (bypassing AI2P) passes by
all of our security: its tools are in neither the action catalog, nor the rules of the task, nor
the log. The agent uses them, and you neither know about it nor can forbid them. So such a
connection is not "one more way to configure things" — it is a hole.

How it works at our end:

* the tools of the MCP server are fetched as a list and each of them gets an **entry in the
  action catalog** — from that minute on it can be allowed or forbidden by an ordinary security
  rule of the task or the project;
* **for plugin tools the policy is the reverse**: a tool without a catalog entry is
  **forbidden**. For the ordinary tools of the system the rule is the other one (no entry means
  no prohibition), and that is exactly why it is inverted for plugins: an MCP server that added a
  tool in a new version would otherwise smuggle it past the rules silently;
* a tool that disappeared from the list has its catalog entry withdrawn — it is neither published
  nor allowed;
* the **token** of the MCP server is kept in the `secrets/` subdirectory of this server, is not
  replicated and goes neither into `config.json` nor into the log;
* the **tool list is per server** (it lives in `config.json`): an MCP server is started where its
  program is installed, and a neighbour may have a different version. What is replicated is not
  the list but its consequence — the entries in the action catalog.

## A long run: a program as the executor of a task

The actions of a plugin are tools of the AI agent: the agent calls an action and gets an answer
within seconds. Training a LoRA adapter does not work that way — it runs for hours and takes
the whole graphics card. That is why a plugin has a second kind of capability: **long-run
operations** (the `run` block of the manifest). They are called not by the agent but by a
**task** — with an executor of the third type, the **software executor** (see
[Executors](performers.md)).

What such an operation has on top of an ordinary action:

* a **working folder** — a relative one is counted from the project folder and is never let
  outside it;
* **two limits instead of one**: the **silence** timeout separately from the overall duration
  limit. While the program prints, it is alive; one that has died must become an error in half
  an hour, not in twelve hours;
* a **rule for reading the result** — what was named as the output, a file at a path from the
  manifest, or «the newest file of a folder by mask». The third one exists exactly for
  trainers: the name of an epoch file (`epoch-0007.safetensors`) cannot be named in advance;
* the **«Single instance»** flag — a column of the plugin form. While such an operation is
  busy, a second task with it **waits** in the queue. This is not «slower»: two trainings on
  one graphics card mean an out-of-memory refusal within a minute.

**What is visible while the program computes.** Its output is read continuously and goes at
once to two places: to the **console of the task card** — live, line after line — and to the
**job log** — forever. The log can be looked at without waiting for the end of the work.
Stopping is the usual task stop button, and the whole process tree is killed with it. A
non-zero exit code, silence longer than the limit and the overall timeout all become a job
error alike, and the last lines of the output lie in its text — without them «it crashed» is
unfixable.

An operation has no command line of its own, just like an action: the arguments are written in
the manifest, and the task substitutes into them only the declared settings of the record and
paths inside the project folder.

**A plugin record is created not only by hand.** Installing a model that declares LoRA training
packages creates by itself the record of the **trainer plugin** that leads to all those
packages, and writes down the found path of the program — but only **into an empty field**: a
path given by a human is never overwritten by a repeated installation. The record appears in
the **«declared»** state — the catalog actions and the experience records are still created by
the «Initialise» button: publishing tools to the agent is a decision of a human, not a
consequence of an installation.

## The «Plugins and MCP» security rule

Security rules have a separate kind for this section — **access to plugins and MCP**. An
ordinary rule answers the question «what exactly is the agent allowed to do» (the action code),
and this one answers «**which plugins may be used at all**».

* The **pattern** is a plugin code (`trainer.musubi`) or a code with an operation
  (`trainer.musubi:lora.train`). Masks and regular expressions work as in the other kinds of
  rules.
* There are **two operations, and at least one must be ticked**: «**use**» — the agent calls a
  tool of the plugin; «**run**» — a software-executor task starts the program. The second one
  is checked **before** the program starts and covers all the paths at once: the agent, the
  human, the hierarchy queue and the delayed start.
* **Everything is allowed by default.** There are no rules of this kind — so it is allowed; a
  ban is created explicitly. The rule is set on any of the three levels (organization, project,
  task), and when they overlap the stricter one wins.
* «**Ask**» in starting a program means a **refusal**: the start comes from the queue, and
  there is nobody to ask there.

This rule is created in the same forms as the rest: Settings → Security (the whole
organization), the project card → «Security», the task card → «Security».

## Disable, enable, remove

* **Disable** — temporarily: the tools stop being published to the agent, the catalog and
  experience records stay where they are. Back again — **Enable**.
* **Remove** — for good: the actions and the experience records of the plugin are deleted
  **across the whole organisation**. The record settings and the path to the program are kept, so
  that a second initialisation does not start from scratch.

The experience records the plugin has put into the general experience of the organisation are
visible and editable on the **Settings → General experience** tab — the "Open general experience"
button on the plugin form leads there as well.

## Neighbouring chapters

* [Configuration](config.md) — the settings screen as a whole and what is on its other tabs.
* [Tasks](tasks.md) — the security rules of a task, the thing that allows and forbids actions.
* [Project objects](objects.md) — the project media library: the gateways build the timeline out
  of it.
* The [Plugins and MCP](../plugins/README.md) section — a document per plugin: what it can do,
  what it needs to work and the operating system limits.

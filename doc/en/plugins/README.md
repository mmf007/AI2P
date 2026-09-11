# Plugins and MCP: the plugin documents

The directory of plugin documents: one file per **plugin code** (`tool.ffmpeg.md`). The
document is opened by the **«i»** button in the plugin row: **Settings → Plugins and MCP**.

A plugin is an organisation record plus a manifest file `plugins/<code>/plugin.json` in the
data directory. The manifest describes what the plugin can do (its actions, which are the tools
of the AI agent), where to get the external program and which settings the record has; the
program itself and the path to it belong to **this computer** and are not replicated.

## Section contents

* [editor.blender](editor.blender.md) — Blender VSE — the `editor.blender` gateway
* [editor.openshot](editor.openshot.md) — OpenShot — the `editor.openshot` gateway (backup)
* [editor.resolve](editor.resolve.md) — DaVinci Resolve (OTIO)
* [editor.shotcut](editor.shotcut.md) — Shotcut / Kdenlive (MLT XML)
* [tool.ffmpeg](tool.ffmpeg.md) — Video converter (ffmpeg) — plugin `tool.ffmpeg`
* [trainer.musubi](trainer.musubi.md) — Musubi Tuner (LoRA) — the `trainer.musubi` plugin

## What every plugin has in common

**The actions are narrow and named.** An agent tool does exactly one named piece of work, and
its parameters live in the manifest and in the record settings. Actions of the «run a command
line» kind deliberately do not exist: that is execution of arbitrary code with the right to
write files, and no security rule narrows it down.

**A plugin tool is covered by a security rule** — every action has its own action reference
record, created when the plugin is initialised. A tool without such a record is not published
at all.

**Paths are limited.** A plugin may read and write inside the project folder and inside the
external directories opened by the security rules of the task — nowhere else.

**Installation is local.** The plugin description is replicated to every server of the
organisation, while the found program and its path live in the `config.json` of the computer
where it is installed.

## How to add a document for a new plugin

Put a file `<plugin code>.md` here — with exactly the code the plugin is named by in its
manifest (`tool.ffmpeg`). Create the same file in every other language: the set of documents must
be the same in all of them. The contents above are not edited by hand — they are built from the
directory by the `test/t18s1/mktoc.py` script, which has to be run after the file is added.

Write external addresses in these documents **in full**, with the `https://` scheme — the
document is opened by the «i» button inside a page of the application, and a relative link is
dead there.

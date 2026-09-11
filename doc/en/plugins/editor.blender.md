# Blender VSE — the `editor.blender` gateway

Builds an edit in the **Blender Video Sequence Editor** and hands it to Blender itself: we
generate a Python script, Blender runs it headlessly and saves the `.blend` project on its
own — or renders the finished movie.

## Why a script and not an edited `.blend`

A `.blend` file is a **binary dump of Blender's internal structures** with a DNA block (field
types and offsets are written into the file itself and change from version to version). Third
party code does not write into it: parsing and rebuilding such a file would have to be redone
for every Blender release.

There is exactly one practical path, and it is the official one:

```
blender --background --factory-startup --python ai2p_timeline.py -- ai2p_timeline.blend
```

We write the script, Blender executes it and saves the project itself. The same run also
renders the movie — just ask for a result with a different extension.

## What has to be installed

| what | how |
|---|---|
| Blender 3.0 or newer | Settings → Plugins and MCP → the `editor.blender` plugin → **Install** (portable archive), or point to an already installed `blender` |

The plugin first **looks for `blender` in PATH** (the `system` block of the package entry,
checked with `--version`, minimum 3.0): whoever does 3D already has Blender, and downloading a
second 386 MiB copy next to it makes no sense. The path to the program is a **per-machine**
value: it lives in the server `config.json` (`plugins.editor.blender.path`), not in the
organisation database, and is not replicated across the cluster. The plugin description, on the
contrary, is replicated.

Building the script works **without** an installed Blender — the program is needed only to run it.

## Agent actions

| tool | what it does |
|---|---|
| `blender_timeline_write` | build `ai2p_timeline.py` from the project media library: one VSE video track and one audio track, ordered as in `media_list`. Filters: `scene`, `kind`, `tag`. The file name is `file` |
| `blender_render` | run the built script in headless Blender. An `out` ending in `.blend` (the default) gives a project with tracks, any other extension gives a rendered movie (mp4, H.264 + AAC) |

The order of work: put resources into the media library (`media_add`), then
`blender_timeline_write`, then `blender_render`. The human's own `.blend` is never touched: the
gateway writes only its own `ai2p_timeline.py` and `ai2p_timeline.blend`.

## Security: why it is special for this gateway

A directory restriction is **illusory** with Blender: Python inside Blender opens any file with
`open()`, and no AI2P security rule sees that. What really limits it is not the path but the
fact that **we write the text of the script**. Hence three rules, all three in force:

1. **There is no arbitrary script from the AI.** There is no "run this Python" action in the
   catalogue and there never will be. An action is our template plus substitutions, and the
   model fills in the parameters only: the clip filters and the file name.
2. **Substitutions are escaped** as a Python string literal. A file name with a quote, a
   newline or a backslash stays a value and does not become code.
3. **Only our own file is executed.** The render action has no "what to run" parameter at all
   (`ownFileOnly` in the manifest) — otherwise the agent would write its own `.py` with the
   file writing tool and have us run it.

Plus the general rule of the branch: writing is allowed into the project folder and into
directories opened by the task security rules; paths inside the script are relative only
(resolved from the folder of the script itself), and the saved `.blend` keeps them relative
(`save_as_mainfile(relative_remap=True)`).

## Operating system limits

The codec set of Blender builds **differs**, and that is the main thing to know before rendering.

* **Windows.** The portable `blender-5.2.1-windows-x64.zip` is installed by our package
  (404,851,964 bytes, taken with a HEAD request on 2026-09-03). The official blender.org build
  carries FFmpeg with H.264 and AAC — rendering to mp4 works out of the box. Verified live on
  Blender 4.0.2.
* **Linux.** We have no package: the release holds `blender-5.2.1-linux-x64.tar.xz` and
  installing it is separate work. The human installs the program and points to it by hand.
  A build **from the distribution repository** (`apt install blender`, `dnf install blender`)
  is often linked against the system FFmpeg, and its encoder set differs between
  distributions: H.264 and AAC may be missing entirely. If the render refuses because of a
  codec — take the blender.org build, or render with the Shotcut gateway or the `tool.ffmpeg`
  converter. Blender opens no window under `--background`, so variables like
  `QT_QPA_PLATFORM` are not needed. **Not verified live.**
* **macOS.** We have no package: the release holds only a `.dmg`, and for 5.2.1 it is
  **arm64 only** — Intel machines need an older version. The path is entered by hand.
  **Not verified live.**
* **Common.** Blender before 4.4 calls the timeline pieces `sequences`, from 4.4 on `strips`;
  our script understands both names, so it works on 3.x as well as on 5.x.

## What this gateway does not do

* It does not open or edit the human's `.blend` — only its own.
* It does not do transitions, titles or colour grading: the VSE is a weak editor (no nested
  timelines, few transitions, almost no audio processing). Take this gateway when the project
  **already has 3D** and you want the edit in the same file; with no 3D take Shotcut/Kdenlive
  (`editor.shotcut`) instead — richer editing and simpler rendering.
* It does not transcode footage. If the pieces have different formats, run the `tool.ffmpeg`
  converter first.

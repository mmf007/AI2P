# OpenShot — the `editor.openshot` gateway (backup)

Builds an **OpenShot project** `.osp` from the project media library. The `.osp` format is plain
**UTF-8 JSON** (since OpenShot 2.0), so the file is written directly: no code execution and no
manual import of a foreign format — a human just double-clicks the ready file.

## Why this gateway is the backup one

By **formal criteria** OpenShot is the best of the four candidates of the branch:
cross-platform, free, an open project format, machine-writable, and the file opens at once and
whole.

By **reliability** it is worse, and it is fairer to say so out loud:

* **It has no headless render at all.** `openshot-qt` renders only through its own window;
  Shotcut/Kdenlive have `melt` for that, and the movie is rendered without a human. That is why
  this plugin's manifest has no `render` block and no second action: promising a render that
  does not exist is worse than not having one.
* **OpenShot's stability reputation is weak** — crashes on long projects have been known for
  years.

**The first choice for editing is `editor.shotcut` (Shotcut / Kdenlive).** Take OpenShot when
the human works in it, or when Shotcut is not installed on that machine.

## What has to be installed

| what | how |
|---|---|
| OpenShot 2.0 or newer | install it yourself and point at it: Settings → Plugins and MCP → the `editor.openshot` plugin → the path field |

**There is deliberately no Install button here.** On Windows OpenShot ships only as the
installer `OpenShot-v4.0.0-x86_64.exe` (229,078,328 bytes, released 2026-08-30,
https://github.com/OpenShot/openshot-qt/releases), the project has no portable archive at all,
and our package installer unpacks archives rather than running third-party installers. So the
package code in the manifest is empty and the plugin shows the "point at where it already is"
field.

You may point either at the program file or at the folder you installed it into. OpenShot does
not report its version on the command line, so only the **presence of the executable** is
checked: `openshot-qt.exe` (Windows), `openshot-qt` or the AppImage (Linux), the application
from the `.dmg` (macOS).

The path to the program is a value of **this computer**: it lives in the server's `config.json`
(`plugins.editor.openshot.path`) and is not replicated across the cluster. The plugin
description, on the contrary, is replicated.

**Important and non-obvious:** until the path is set, the plugin state on this server is
"looking for the software", and the action is **not published** to the agent — even though
building the `.osp` would work without OpenShot installed (we write the file ourselves). This is
a general rule of the plugin core rather than a quirk of this gateway; `editor.resolve` behaves
the same way.

## Agent actions

| tool | what it does |
|---|---|
| `openshot_timeline_write` | build `ai2p_library.osp` from the project media library: a video track (layer `L2`, number 2000000) and an audio track (layer `L1`, number 1000000), ordered as in `media_list`. Filters: `scene`, `kind`, `tag`. The file name is `file` |

The order of work: resources go into the media library (`media_add`), then
`openshot_timeline_write`, then a **human** opens the file in OpenShot and, if a movie is
needed, starts the export themselves.

The human's own project file is never touched: only our `ai2p_library.osp` is written.

## How the built file is laid out

`.osp` differs from MLT and OTIO in that its clip list is **flat**:

* `files` — the resource list: path, media kind, reader type (`FFmpegReader` for video and
  audio, `QtImageReader` for images), duration and the `has_video` / `has_audio` /
  `has_single_image` flags;
* `clips` — **a single array for the whole file**, and each clip names its track and its place
  itself via `layer` (a number from `layers`) and `position` (seconds from the start). There is
  no track-as-sequence like in MLT, so the start of every clip is computed by us;
* `layers` — five layers, two of them labelled (`A1` and `V1`);
* `profile` — the OpenShot profile name as a string (`FHD PAL 1080p 25 fps` for 1920×1080 at 25
  fps); frame size and rate come from the plugin record settings.

The scene, the take and the caption of a resource go into the `metadata` of the file entry
(`ai2p_scene`, `ai2p_take`, `ai2p_caption`) — OpenShot does not show them, but when the file is
read by eye they tell where the clip came from.

## Paths and security

**Paths are relative only** — and this is the format's native form: OpenShot itself saves the
project with relative paths and expands them on open, from the folder of the `.osp`. An absolute
path would kill portability across the cluster: on another machine the same clip sits elsewhere
and the project would open with every clip missing. The JSON key is exactly `path`, and it
appears **twice** — in the `files` entry and in the clip's `reader`.

The clip path is computed **from the folder of the output file**, not from the project folder. A
resource from the organisation store (`store:`) lies outside the project folder, so it is copied
into the `ai2p_media` subfolder next to the `.osp`; the same is done with any resource that
would otherwise have to be addressed through `..`.

The folder restriction works **literally**: we write JSON and execute no code, so both the path
of the file itself and the paths inside it are checked. Writing is allowed into the project
folder and into the folders opened by the task security rules — nowhere else; a path outside
gives a clear refusal and no file appears.

## Operating-system limits

Building an `.osp` is writing a text file, and it is **the same on every system**. What differs
is only how OpenShot itself is obtained there.

* **Windows.** Only the installer `OpenShot-v4.0.0-x86_64.exe` (229,078,328 bytes); there is
  also a 32-bit `OpenShot-v4.0.0-x86.exe` (224,873,009 bytes). There is no portable archive and
  our package cannot install it. After installation the executable is `openshot-qt.exe`.
  **Not verified live: OpenShot is not installed on this machine.**
* **Linux.** `OpenShot-v4.0.0-x86_64.AppImage` (254,457,024 bytes) or the distribution package
  (`apt install openshot-qt`). The AppImage runs as an ordinary file — point at it. OpenShot
  always needs its window: the program has no headless mode, which is exactly why there is no
  headless render. **Not verified live.**
* **macOS.** `OpenShot-v4.0.0-x86_64.dmg` (292,188,716 bytes). The path is set by hand.
  **Not verified live.**
* **Common.** The numbers were taken by a request to the GitHub release list on 2026-09-03
  (release v4.0.0 of 2026-08-30, `libopenshot` 1.0.0). The `version` block of the built file is
  not decoration: on open OpenShot runs its upgrade steps for old projects according to it.

## What this gateway does not do

* **It does not render.** The movie is rendered from the `.osp` by a human who opens the
  project. If a movie without a human is needed, take `editor.shotcut` (it has `melt`) or the
  `tool.ffmpeg` converter.
* **It does not edit the human's project** — only its own `ai2p_library.osp` is written.
* **It carries no transitions, effects or colour correction** — only the layout of resources on
  two tracks with their durations.
* **It does not transcode footage.** If the pieces have different formats, run the `tool.ffmpeg`
  converter first.
* **It does not use `libopenshot`.** Next to the editor there is the `libopenshot` library with
  Python, C++ and Ruby bindings — through it a project could be both built and rendered without
  a window. It is out of scope here: that is a separate external dependency with its own
  installation, while the `.osp` format we write directly and without it.

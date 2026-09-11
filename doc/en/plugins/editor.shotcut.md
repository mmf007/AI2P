# Shotcut / Kdenlive (MLT XML)

**Plugin code:** `editor.shotcut`
**Kind:** gateway (`gateway`)
**Interchange format:** MLT XML — one file opens both in Shotcut (`.mlt`) and in Kdenlive
**Software:** installed by us (a portable Shotcut) or an already installed `melt` is taken as is
**What it does:** builds a timeline from the project media library and **renders a finished clip out of it with no window**

## Why this is the first choice of the four gateways

Of the four editors of this branch (Shotcut/Kdenlive, DaVinci Resolve, Blender VSE, OpenShot)
only this one has a **render with no window and no human**: the `melt` program of the MLT
distribution reads the file we built and writes a finished `mp4`. Resolve and OpenShot have no
such path at all; Blender has one, but through executing our Python script.

So the chain here is closed: the agent built the timeline → the agent rendered the clip →
the human got the result as a file. Not a single step with the mouse.

The second argument: **one file for two editors**. MLT XML is the native format of Shotcut and
it is also read by Kdenlive (`Project → Open`). A separate gateway into Kdenlive was not needed.

## Agent actions

| Tool | What it does |
|---|---|
| `shotcut_timeline_write` | build `ai2p_library.mlt` out of the project media library |
| `shotcut_render` | render a clip from the built timeline with the `melt` program |

Parameters of `shotcut_timeline_write` (all optional):

| Field | Meaning |
|---|---|
| `scene` | take only the resources of this scene |
| `kind` | take only this kind of media (`video`, `audio`, `image`, `subtitle`, `project`) |
| `tag` | take only the resources carrying this tag |
| `file` | where to put the file; `ai2p_library.mlt` in the project folder root by default |

Parameters of `shotcut_render`:

| Field | Meaning |
|---|---|
| `file` | what to render; `ai2p_library.mlt` by default |
| `out` | where to put the result; extension `.mp4`, codecs H.264 + AAC |

**The render has no command line of its own.** The arguments of `melt` are fixed by the plugin
manifest, and the agent passes only two paths. There is no "run an arbitrary command" action in
the system on purpose: that is executing someone else's code with the right to write files, and
a security rule cannot narrow it down.

The order of the clips is **the same one `media_list` prints**: scene, order (`order`), take
(`take`), object code. A human who has looked at the list must see exactly that list in the editor.

The track layout:

| Track | What goes onto it |
|---|---|
| `V1` | `video`, `image`, `project` |
| `A1` | `audio` (the track is marked `hide="video"`) |

A resource with no known duration (an image, a title card) gets **5 seconds**, not zero: a clip
of zero length is a silently lost shot.

## Software

The plugin first **looks for the program on this computer**: `melt`, `qmelt`, `melt.exe`,
`qmelt.exe` in `PATH`, checked with the `--version` key, the minimum usable version is **7.0**.
Found — taken as is; not found — the **Install** button downloads a portable Shotcut and unpacks
it itself. The third path is to give the path by hand on the plugin form
(**Settings → Plugins and MCP → Shotcut / Kdenlive (MLT XML)**); either the program file or the
folder it was installed into will do.

In the Shotcut distribution for Windows the program is called **`melt.exe`**, not `qmelt.exe` —
both names have to be looked for. In the Linux builds and in some macOS builds it is `qmelt`.

The path to the program is kept **in `config.json` of this server** and is never replicated: on a
neighbour in the cluster Shotcut lives at a different path, and half of the servers have none at
all. The plugin description, on the contrary, is replicated across the whole organisation.

While the program has not been found, the state of the plugin on this server is
**"looking for the software"**, and its actions are not published to the agent: a tool that is
known to answer with a refusal would waste the agent's move. That is exactly what the refusal
"software not found on this server" means.

## Operating system limits

| System | What matters |
|---|---|
| **Windows** | Works with no reservations. The render program is called `melt.exe` and sits next to `shotcut.exe` in the installation folder. The `QT_QPA_PLATFORM` environment variable is not needed and is not set. |
| **Linux** | On a server **with no display** `melt` does not start at all — it dies with "could not connect to display", because it is a Qt program. So the plugin starts it with `QT_QPA_PLATFORM=offscreen`; the variable is set **only on Linux** (`envOs` in the manifest). If you call `melt` by hand — set it yourself. In distribution builds the program is usually called `melt`, in the Shotcut distribution — `qmelt`. |
| **macOS** | The program lives **inside the application bundle**: `/Applications/Shotcut.app/Contents/MacOS/qmelt`. It is not in `PATH`, so the automatic search most often finds nothing — the path is given by hand. Homebrew builds (`brew install mlt`) give a `melt` in `PATH` and will do as well. |
| **All systems** | The set of encoders depends on the MLT build. Our defaults — `libx264` for video and `aac` for audio — are present in the Shotcut distribution on all three systems, but a trimmed system build of MLT may lack them; then the render refuses with the text of `melt` itself, and the material has to be prepared with the `tool.ffmpeg` plugin. |
| **Kdenlive** | The file opens, but Kdenlive **recalculates it into its own project profile** on opening. If the frame rate of our timeline did not match the Kdenlive profile, the clip durations will shift: keep the "Frames per second" setting the same on both sides. |

## Plugin record settings

**Settings → Plugins and MCP → Shotcut / Kdenlive (MLT XML) → Configure:**

| Setting | Default | Meaning |
|---|---|---|
| Frames per second | 25 | the frame rate of the timeline; the clip durations are recalculated into it as well |
| Frame width | 1920 | the size of the MLT profile |
| Frame height | 1080 | the same |

The settings are a property of the **organisation record**, they are replicated: agreed to edit
at 25 frames — agreed across the whole cluster. The path to the program, on the contrary, is
each server's own.

## Paths and security

* **All the paths inside the `.mlt` are relative only** (the `resource` property). An absolute
  path kills portability: on a neighbour in the cluster the same clip lives elsewhere, and the
  timeline opens empty.
* The path of a clip is counted **from the folder of the output file**.
* A resource from the organisation storage (`store:`) lies outside the project folder, so on
  export it is **copied** into the `ai2p_media/` subfolder next to the timeline: otherwise the
  reference to it would be either absolute or with `..`.
* The agent writes **only its own file** `ai2p_library.mlt`. The human's project is never
  touched: the human may keep it open in the editor, and saving from the editor would overwrite
  our record (or the other way round). The finished timeline is imported by the human.
* Writing is allowed **into the project folder**, and if the security rules of the task have
  opened external folders — into them as well. Both the path of the file itself and every path
  inside it are checked.

## Working with the ffmpeg converter plugin

MLT will put material of different sizes and different frame rates together, but with an
on-the-fly recalculation — that is, slowly and not always exactly. It is cheaper to bring it into
shape beforehand:

1. `ffmpeg_uniform` — a common frame size and a common frame rate;
2. `ffmpeg_extract_audio` — pull the audio out into a WAV, if it is needed as a separate track;
3. `media_add` — put the resulting files into the media library;
4. `shotcut_timeline_write` and `shotcut_render`.

Transcoding is an **explicit step**, not silent magic inside the export: the agent sees what it
is doing, and you see what came out.

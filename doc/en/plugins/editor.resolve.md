# DaVinci Resolve (OTIO)

**Plugin code:** `editor.resolve`
**Kind:** gateway (`gateway`)
**Interchange format:** OTIO — `.otio`, plain JSON, an Academy Software Foundation standard
**Software:** **not installed by us** — point at an already installed Resolve
**What it does:** builds an `.otio` timeline from the project media library; **a human creates the timeline**

## The main point: free Resolve cannot be scripted

Everything else in this document follows from this, so it comes first.

DaVinci Resolve does have a scripting API (`DaVinciResolveScript`), but **it is closed in the
free edition**, and since version **19.1 (November 2024)** it is closed for good: the
out-of-process bridge stopped accepting connections at all. In the free edition scripts run
only from Resolve's own internal console, by hand. An external program — and AI2P is exactly
an external program as far as Resolve is concerned — cannot connect on Windows, Linux or
macOS. Automation exists only in the paid **Resolve Studio**.

So the chain of work is:

1. the agent fills the project media library (`media_add`, `media_list`);
2. the agent calls `resolve_timeline_write` and **writes the file `ai2p_scenes.otio`** into the
   project folder;
3. **a human opens Resolve and imports the file by hand:** `File → Import → Timeline → OTIO`
   (in older builds `File → Import Timeline → AAF, EDL, XML…`, with `.otio` in the same list
   of formats).

The timeline **will not appear by itself**. If a task report says "the timeline was created in
Resolve", that is untrue: what was created is a file the human still has to import.

We never write into the Resolve project: a Resolve project is a closed database (PostgreSQL or
its own format), and reaching into it from the outside is not something we do.

## The agent action

| Tool | What it does |
|---|---|
| `resolve_timeline_write` | build `ai2p_scenes.otio` from the project media library |

Parameters (all optional):

| Field | Meaning |
|---|---|
| `scene` | take only the resources of this scene |
| `kind` | take only this media kind (`video`, `audio`, `image`, `subtitle`, `project`) |
| `tag` | take only the resources carrying this tag |
| `file` | where to put the file; by default `ai2p_scenes.otio` in the project folder root |

Clip order is **the same one `media_list` prints**: scene, `order`, `take`, object code. A
human who read that list must see exactly it in Resolve.

Track layout:

| OTIO track | Track kind | What goes there |
|---|---|---|
| `V1` | `Video` | `video`, `image`, `project` |
| `A1` | `Audio` | `audio` |

A resource with no known duration (a still, a title card) gets **5 seconds**, not zero: a
zero-length clip is a silently lost shot.

**Subtitles (`subtitle`) do not travel in the `.otio`:** OTIO has no track for them. The human
adds the `.srt` in Resolve separately (`File → Import → Subtitle`).

## Software: we cannot install it, you can only point at it

This plugin has no Install button **on purpose**. The DaVinci Resolve distribution weighs
3–4 GB and is served from the Blackmagic Design site **behind a registration form**; there is
no permanent direct link that could be put into the package catalogue. We cannot download it
for you and we do not try.

What to do: install Resolve yourself and give the path in the plugin form —
**Settings → Plugins and MCP → DaVinci Resolve (OTIO)**. Either the program file or the folder
you installed it into will do:

| System | What to point at |
|---|---|
| Windows | `C:\Program Files\Blackmagic Design\DaVinci Resolve\Resolve.exe`, or that folder |
| Linux | `/opt/resolve/bin/resolve`, or `/opt/resolve` |
| macOS | `/Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/MacOS/DaVinci Resolve` |

The path lives **in this server's `config.json`** and is never replicated: on the neighbouring
machine Resolve sits elsewhere, and half the servers do not have it at all.

Only **the presence of the executable** is checked: Resolve does not report its version on the
command line, so the plugin declares no acceptable version range.

Until the path is given and the program is found, the plugin state on this server is
**"looking for the software"**, and its actions are not published to the agent: a tool that is
bound to answer with a refusal would waste the agent's turn. That is the "software not found on
this server" refusal, and one line in the plugin form cures it.

## Operating system limits

| System | What matters |
|---|---|
| **Windows** | Free Resolve reads H.264/H.265 in `.mp4`/`.mov`. AAC audio is **unsupported** (see below) — in every version and on every system. The program is usually at `C:\Program Files\Blackmagic Design\DaVinci Resolve\Resolve.exe`. |
| **Linux** | The free build **does not decode H.264 or H.265 at all**: it ships without the licensed codecs. Our generations (fal.ai, ComfyUI) are exactly `mp4/H.264`, so without transcoding you get a timeline where every clip says "Media Offline". What works: DNxHR (`.mov`), ProRes (`.mov`), CinemaDNG, and uncompressed WAV for sound. The program is at `/opt/resolve/bin/resolve`. |
| **macOS** | H.264/H.265 are read (the decoder is the system one). AAC is still unsupported. |
| **Every system** | **AAC is unsupported even in the paid Resolve Studio.** Sound from our clips and generations (`aac`, `mp3`) must be converted to WAV. |

Separately: Resolve reads OTIO out of the box **starting with version 18.5**, the free edition
included. Older builds have no OTIO import item at all — there you have to upgrade.

## Working with the ffmpeg converter plugin

Transcoding is an **explicit step**, not silent magic inside the export: the agent sees what it
does and you see what came out. The order of work before building the `.otio`:

1. `ffmpeg_to_edit` — turn every clip into an editing intermediate (DNxHR HQ in a MOV container
   plus uncompressed audio by default);
2. `ffmpeg_extract_audio` — pull the sound into WAV if it is needed as a separate track;
3. `media_add` — put the resulting files into the media library (with their own scene, order
   and take);
4. `resolve_timeline_write` — build `ai2p_scenes.otio`.

If the ffmpeg converter plugin is not set up, or ffmpeg is not found on this server, steps 1–2
refuse with a clear text ("the plugin program was not found on this server"), and that has to
be fixed before building the timeline, not after: the `.otio` will be built even from unusable
footage — it merely references files — but there will be nothing to open it with.

On Windows and macOS the transcoding step is optional but useful: DNxHR scrubs frame by frame,
while long-GOP H.264 jerks.

## What OTIO does not carry

OTIO is an **interchange format for an editing decision**, not a project format. It carries:

* the set of clips and the references to the files;
* clip order and durations;
* the track layout;
* our notes in `metadata.ai2p` — scene, take, order, caption, media kind.

It does **not** carry effects, transitions, colour grading, retiming, Fusion compositing or the
Fairlight mix. The round trip is **lossy**: export a timeline from Resolve into `.otio` and
bring it back, and all the processing is gone.

The practical conclusion: our `.otio` is a **rough assembly** (order and timing). The finishing
is done by a human inside Resolve, and re-importing our file over their work is not acceptable —
import it as a new timeline instead.

## Plugin record settings

**Settings → Plugins and MCP → DaVinci Resolve (OTIO) → Configure:**

| Setting | Default | Meaning |
|---|---|---|
| Frames per second | 25 | the timeline rate; clip durations are converted into it |
| Frame width | 1920 | project size, goes into `metadata` |
| Frame height | 1080 | the same |

The settings are a property of the **organisation record** and are replicated: agreeing to edit
at 25 fps is an agreement for the whole cluster. The path to the program, on the contrary, is
each server's own.

## Paths and security

* **Every path inside the `.otio` is relative** (the `target_url` field). An absolute path kills
  portability: on the neighbouring machine the same clip sits elsewhere.
* A clip path is computed **from the folder of the output file**.
* A resource from the organisation store (`store:`) lies outside the project folder, so the
  export **copies** it into the `ai2p_media/` subfolder next to the timeline: otherwise the
  reference would be either absolute or contain `..`.
* The agent writes **only its own file** `ai2p_scenes.otio`. The Resolve project and any file of
  the human are never touched.
* Writing is allowed **into the project folder**, and into external folders if the task security
  rules opened them. The limit works literally: both the path of the file itself and every path
  written inside it are checked.

# Video converter (ffmpeg) — plugin `tool.ffmpeg`

**What it does:** prepares footage for editing — transcodes clips into an editing intermediate,
pulls the audio out, concatenates pieces by list and normalises them to a common frame and rate.

**Why it is needed.** Our generations are mp4/H.264 with aac/mp3 audio (fal.ai, ComfyUI,
`SaveAudioMP3`). Free DaVinci Resolve on Linux **does not decode H.264/H.265 at all**, AAC is
not supported even in the paid Studio, and the encoder sets of Blender builds differ. The
converter makes the «generation → editing» link independent of what a particular editor build
on a particular operating system can do: the footage goes through ffmpeg before editing.

---

## Actions

The actions are **narrow and named**. Each one is described whole in the plugin manifest: the
ffmpeg options come from the distribution file, the working parameters come from the record
settings, and the AI agent supplies **paths only** — every one of them checked.

| Agent tool | What it does | What it takes | What comes out |
|---|---|---|---|
| `ffmpeg_to_edit` | Transcode to the editing intermediate | `in` — the file, `out` — where to (optional) | `<name>_edit.mov`: DNxHR or ProRes plus uncompressed audio |
| `ffmpeg_extract_audio` | Extract the audio | `in`, `out` | `<name>_audio.wav`, uncompressed |
| `ffmpeg_concat` | Concatenate by list | `files` — a list of paths, or a media library filter (`scene`, `kind`, `tag`), `out` | `ai2p_concat.<extension of the sources>` |
| `ffmpeg_uniform` | Normalise to a common frame and rate | `in`, `out` | `<name>_uniform.mov` of the required size and rate |

**There is no «run an ffmpeg command line» action, and there never will be.** That is execution
of arbitrary code with the right to write files, and no security rule narrows it down: ffmpeg
has `-f lavfi`, the `file:`, `concat:` and `tcp:` protocols and the `-y` option over any file.
Asking the agent for such a command is pointless — it does not exist; if another working
profile is needed, change the **plugin record setting**.

### The usual order

1. `ffmpeg_uniform` on every piece — one common frame size and rate.
2. `ffmpeg_concat` — the join. It is a **stream copy**, so the pieces must share one format;
   on pieces of different formats the join does not work.
3. `ffmpeg_to_edit` — the intermediate for editing in the editor.
4. `ffmpeg_extract_audio` — if the audio is needed as a separate track.

---

## Record settings

They are set in **Settings → Plugins and MCP**, in the plugin record form; they cannot be
changed from the task text.

| Key | Default | What it means |
|---|---|---|
| `videoCodec` | `dnxhd` | intermediate codec: `dnxhd` (DNxHR) or `prores_ks` (ProRes) |
| `videoProfile` | `dnxhr_hq` | profile: `dnxhr_lb`/`sq`/`hq`/`hqx` for DNxHR, `0`–`3` for ProRes |
| `pixelFormat` | `yuv422p` | pixel format |
| `audioCodec` | `pcm_s16le` | audio codec (uncompressed PCM) |
| `audioRate` | `48000` | sample rate, Hz |
| `width`, `height` | `1920`, `1080` | the common frame size for `ffmpeg_uniform` |
| `fps` | `25` | the common frame rate |

**DNxHR is the default rather than ProRes** because the ffmpeg ProRes encoder (`prores_ks`) on
Windows and Linux produces files Resolve does not always read, while DNxHR is the native Avid
format and every editor of the branch takes it.

**The pair «profile ↔ pixel format» is linked:** `dnxhr_hq` requires `yuv422p`, and `prores_ks`
profile 3 requires `yuv422p10le`. A mismatch is rejected by ffmpeg itself, and the refusal is
visible to the agent in the tool answer.

---

## Where the files go and what is forbidden

* By default the result lands **next to the source** (footage lives in per-scene folders, and a
  file that moved to the project root has to be hunted by hand); its name gets the suffix
  `_edit`, `_audio` or `_uniform`.
* The input path and the output path are valid **only inside the project folder** or inside an
  external directory opened by the **security rules of the task**. Everything else is refused,
  before the program is started.
* The result **never lands over the source**: ffmpeg with `-y` would overwrite it with an empty
  file before even reading it, and silently lost footage is worse than a refusal.
* The security rules of the task apply here as well: a subdirectory closed for reading is
  closed for transcoding too.

## A long conversion

The converter runs as a **separate process**, so it does not hang the job. Progress is noted in
the job call log every 15 seconds («N s of footage processed»), and when the waiting time runs
out (2 hours per operation by default) the process is stopped and the agent gets a clear
refusal telling how much footage had been processed. That matters: from a bare «did not
finish» one cannot tell a stuck program from honestly long work.

---

## Operating system limits

ffmpeg builds **differ in their set of encoders**, and that is not a detail: non-free and GPL
codecs are missing from some builds entirely. Our four operations rest on encoders present in
**every** build (`dnxhd`, `prores_ks`, `pcm_s16le`/`pcm_s24le`) and on the built-in
H.264/H.265/AAC decoders — so the converter works on a stripped build too. If an export back to
H.264/H.265 is ever needed, a build with `libx264`/`libx265` is required (GPL licence): LGPL
builds have neither. The encoder set of a build is listed by `ffmpeg -encoders`; a missing
encoder is named by ffmpeg plainly («Unknown encoder») and the refusal reaches the agent.

**Windows.** The program is installed by our `ffmpeg` package with the Install button: the
build `ffmpeg-9.0.1-essentials_build.zip` from https://www.gyan.dev/ffmpeg/builds/
(111,253,802 bytes, with `ffmpeg.exe` and `ffprobe.exe` inside). Checked live: version 9.0.1,
every needed encoder in place. The link is versioned — a moving one would mean everybody has
their own version.

**Linux.** We have no package: ffmpeg is installed by the system — `apt install ffmpeg`
(Debian, Ubuntu), `dnf install ffmpeg` (Fedora, needs RPM Fusion). The trap here is the
**repository version**: Debian 12 carries 5.1 while the plugin needs **6.0 or newer**, and a
found 5.1 is rejected by the version check — then install a fresh build (a static one from
https://johnvansickle.com/ffmpeg/ for instance) and set the path by hand in the plugin form.
Not checked live on Linux.

**macOS.** We have no package: `brew install ffmpeg`, after which the path is found
automatically or set by hand. Not checked live.

**The version check is mandatory on every system.** The ffmpeg option set changed noticeably
between 4.x and 7.x, and a silently found old program would break the work during the job —
while the cause would be looked for in the plugin.

---

## How the program is found

1. **The manual path**, if it is set in the plugin form (`ffmpegPath` in the `config.json` of
   this server) — both the program file and the directory holding it will do.
2. **A search on this server**: the `ffmpeg` command in PATH, the version asked with
   `-version` and required to be 6.0 or newer.
3. **Installation by the `ffmpeg` package** — the Install button in the plugin form (Windows).

The found path **never reaches the organisation database**: it lives in the `config.json` of
this computer. The plugin description is replicated to every server of the organisation, while
the installation is always local — every server has its own answer to «is it installed here».

While the program is not found, the plugin **publishes no actions at all**: there is nothing to
transcode with, and showing the agent a tool that is bound to refuse only wastes its turn.

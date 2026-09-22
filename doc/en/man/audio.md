# Working with audio models

**What this chapter is for:** building a sound scene out of several parts — music, a song,
the speech of particular people, a noise — and understanding which models exist for it, which
audio samples they need and what they cannot do.

We walk through an example scene:

> Calm country music is playing, sung by vocalist **X**. Two people, **Y** and **Z**, talk
> about the view from a mountain onto a burning forest. A firefighting helicopter flies over
> their heads.

## 1. The main rule: one task — one sound layer

No audio model of the catalog builds such a scene in one go:

* a music model sings, but does not talk in the voices of particular people;
* a speech model talks, but does not play music or sing;
* a speech model with a voice sample takes **one** sample per job — a dialogue of two voices
  in one job will not work;
* the catalog has no sound-effects model;
* AI2P does not lay tracks over each other: there is no mixing either in the connectors or
  in the `tool.ffmpeg` plugin.

So the scene is split into **layers**, each layer is a separate task (or several), and the
finished files are mixed in an audio or video editor.

| Layer | Task skill | Catalog models | Audio sample needed |
|---|---|---|---|
| country music with vocalist X | `audio-song` | ACE-Step-1.5-XL-Turbo / -Base / -SFT (local), ElevenLabs-Music (cloud) | no — and there is nowhere to pass it (section 3) |
| lines of Y | `audio-speech` | Chatterbox-TTS, Zonos-2-TTS (by sample), ElevenLabs-TTS-v3 (stock voices) | yes, a recording of Y's voice (section 4) |
| lines of Z | `audio-speech` | the same | yes, a recording of Z's voice |
| the helicopter fly-over | — | no model | yes — a ready recording (section 5) |

## 2. Audio models of the catalog

Skills: `audio-song` — songs with vocals, `audio-music` — instrumental music, `audio-speech` —
speech. All three are "text → sound": the prompt is the **task description**, while the title,
the acceptance criteria and the project experience do not reach the model.

| Model | Where it runs | What it does | Audio sample | Price |
|---|---|---|---|---|
| [ACE-Step-1.5-XL-Turbo](../models/ACE-Step-1.5-XL-Turbo.md), [-Base](../models/ACE-Step-1.5-XL-Base.md), [-SFT](../models/ACE-Step-1.5-XL-SFT.md) | local, ComfyUI, NVIDIA 8 GB+ | songs and music; writes the lyrics itself if the description has none | not accepted | free, MIT weights |
| [ElevenLabs-Music](../models/ElevenLabs-Music.md) | cloud, fal.ai gateway | songs and music, 3 s to 10 min | not accepted | $0.6 per minute |
| [ElevenLabs-TTS-v3](../models/ElevenLabs-TTS-v3.md) | cloud, fal.ai gateway | speech in a stock voice (the `voice` field) | not accepted | see the model page |
| [Chatterbox-TTS](../models/Chatterbox-TTS.md) | cloud, fal.ai gateway | speech in a voice cloned from a sample | one recording up to 30 s | $0.025 per 1000 characters |
| [Zonos-2-TTS](../models/Zonos-2-TTS.md) | cloud, fal.ai gateway | speech in a voice cloned from a sample, WAV 44.1 kHz | one recording up to 30 s, **required** | see the model page |

Some **video models** also produce sound (Veo-3.1, Seedance-2.5, Wan-3.0-Prime, Kling-3.0,
LTX-2.5, Gemini-Omni-Flash), but only inside a clip; how to use that — section 5.

The preparation is the same for all of them: the model is active (a cloud one has the
`fal.apiKey` key set, a local one has its files installed), an AI executor is created on it,
the executor is a member of the project team, and for a local model the team's work is
started. Step by step this is shown in the [video clip example](sample_video1.md), chapters
1.3–1.4.

## 3. The music and vocalist X

**Which sample is needed for X?** None: the music models of the catalog do not accept a voice
sample. ElevenLabs-Music has no field for a recording, and the workflows shipped for ACE-Step
have no audio loading node. The models that clone a voice from a sample (Chatterbox, Zonos)
**talk, they do not sing**.

So X's voice is described **in words** — gender, timbre, manner:

```
A calm country ballad, 80 beats per minute: acoustic guitar, slide guitar,
double bass, brushes on the snare. A warm vintage sound.
Male vocals: a low baritone, soft, slightly husky, singing quietly.
The lyrics are about mountains and smoke over the forest, in English.
Save the result to file sound/music.mp3.
```

* The task skill is `audio-song`; an instrumental without a voice is `audio-music` plus the
  words "no vocals".
* Put your own lyrics straight into the description — ACE-Step picks them up; if you don't,
  it writes them itself.
* Duration: for ACE-Step it is `length` in the model profile, **in seconds**; for
  ElevenLabs-Music it is the duration field in the `request` section of the profile (30 s in
  the template).
* Write "Save the result to file …" **on a separate line**: the whole line is cut out of the
  prompt.
* The name of a real artist in the description does not replace a sample and creates a legal
  risk — describe the character of the voice.

**To keep X's voice the same from track to track:**

| Technique | What it gives |
|---|---|
| the same voice description | it is handy to keep it as the passport of a project object (kind "style" or "character") and put an `@obj:` reference |
| a fixed `seed` in the profile (ACE-Step) | a repeatable result; empty or 0 — random for every job |
| a LoRA adapter (ACE-Step) | a ready adapter can be applied, but it cannot be trained from AI2P (the model document, section on LoRA training) |

If the talk of Y and Z runs over the song, the vocals will have to be turned down in the mix.
It is simpler to order a track where the vocals do not start at once: "a 30-second intro
without vocals".

## 4. The voices of Y and Z: speech synthesis from a sample

**Yes, there are speech synthesis models** — three catalog records with the `audio-speech`
skill:

* **ElevenLabs-TTS-v3** — the best-rated one, but it speaks in one of the provider's **stock**
  voices (the `voice` field in the `request` section of the profile, `Rachel` by default) and
  accepts no sample. It fits when Y and Z are not particular people: create two executors on
  the same model with different `voice` values (each executor has its own profile).
* **Chatterbox-TTS** and **Zonos-2-TTS** — clone a voice from a recorded **sample** (zero-shot
  clone): nothing has to be trained, 10–20 seconds of recording are enough.

There is no local speech model in the catalog: all three run in the cloud and need the fal.ai
gateway key.

### 4.1. Which samples are needed for Y and Z

**One** recording for each person:

| Requirement | Why |
|---|---|
| 10–20 s long (3–30 s allowed) | shorter — the timbre is not caught; longer than 30 s — the gateway rejects it |
| one speaker, no music, noise or echo | the model copies everything it hears, the room included |
| even loudness, no clipping | distortions carry over into the synthesis |
| the same manner as needed in the scene | a calm conversation — a calm sample, not shouting |
| the same language as the lines | the pronunciation is more accurate |
| WAV or MP3 | the formats both catalog records accept |
| **consent of the voice owner** | the model license gives no rights to someone else's voice |

Put the files into the project folder, for example `refs/voice_y.wav` and `refs/voice_z.wav`.
It is handy to create them as project objects of the **"reference audio"** kind — children of
the characters Y and Z: this way the samples are visible in the object list and do not get
lost.

### 4.2. One line — one task

A job has **one** sample, so the dialogue is built from separate tasks: a line of Y, a line of
Z, Y again… The task description is **spoken aloud in full**, so it must hold only the text of
the line and the instructions to the system, each on its own line:

```
Look, over there, past the second ridge — the smoke is over the whole valley already.
Voice sample refs/voice_y.wav.
Save the result to file sound/dialog_01_y.wav.
```

* A line with an audio file name and a word such as `voice`, `sample`, `speaker`, `timbre`
  (in Russian — «образец», «голос») is an instruction: the file goes to the model and the line
  is cut out of the text. The recognition words are **Russian and English only**.
* **Better not to put an `@obj:` reference into a voice-over task.** It expands into the object
  card — name, kind, number and passport; the path is cut out of it, but the name and the
  passport stay in the text and will be **read aloud**. Name the file on a line, as in the
  example. (For the music model of section 3 a reference is fine: there the passport is the
  style description.)
* The task skill is `audio-speech`, the executor is on Chatterbox-TTS or Zonos-2-TTS. It is
  handy to keep the dialogue tasks as subtasks of one parent — "Dialogue of Y and Z".
* The intonation comes from the text of the line and from the sample. Fine-tuning fields are in
  the `request` section of the profile: Chatterbox has `exaggeration` (expressiveness), Zonos
  has `accurate_mode` (closer to the sample or more expressive) and `language` (the text
  normalization language).
* Make all lines **with the same model**: the file formats will match and joining them is
  simpler.

The finished lines are joined in order by the `ffmpeg_concat` action of the
[tool.ffmpeg](../plugins/tool.ffmpeg.md) plugin ("Join by list"). It adds no pauses between the
lines — those, like laying speech over music, are done in an editor.

## 5. The firefighting helicopter

**Is a separate helicopter sound needed?** Yes. The catalog has no sound-effects model, and the
ones it has do not fit:

* speech models (Chatterbox, Zonos) clone a **voice** from a sample, not a noise — a helicopter
  recording in the sample will not turn into a helicopter sound;
* music models (ACE-Step, ElevenLabs-Music) make **music**: the words "helicopter rumble" in the
  description give at best a musical colour, not a realistic fly-over.

Three working ways, from reliable to experimental:

1. **A ready recording** from a sound library under a license that allows your use — put it
   into the project folder (`sound/helicopter.wav`) and into the mix. The best option for a
   short recognizable effect.
2. **Sound from a video model.** "Text → video with sound" models (Veo-3.1, Seedance-2.5,
   Wan-3.0-Prime and others) voice the scene themselves. A task like "A firefighting helicopter
   flies low over a burning forest, the rotor rumble rises and fades" gives a clip, and the
   `ffmpeg_extract_audio` action of the `tool.ffmpeg` plugin takes the sound out of it. This is
   paid, and the sound quality of such models has not been checked separately.
3. **Your own catalog record.** The fal.ai gateway has dedicated sound-effects models; there are
   no shipped records for them, but a cloud model of the gateway is added as a catalog record
   without code changes. Check the id and the request fields against the
   `https://fal.ai/api/models` catalog and the endpoint schema — the gateway rejects an unknown
   field with HTTP 422 already in a paid job.

## 6. Mixing the scene

You now have files in the project folder:

```
sound/music.mp3          — country with vocalist X
sound/dialog_01_y.wav …  — lines of Y and Z
sound/helicopter.wav     — the helicopter fly-over
```

They are mixed in an audio or video editor: the music in the background and quieter than the
speech, the lines in order with pauses, the helicopter on top with a fade-in and fade-out. If
the scene goes into a clip, it is handy to mix it straight in a video editor connected by a
plugin (OpenShot, Shotcut, DaVinci Resolve, Blender — see [Plugins and MCP](../plugins/README.md)).

## 7. Frequent errors

| Message or symptom | What to do |
|---|---|
| **The model read the object name or passport aloud** | the voice-over task has an `@obj:` reference — replace it with the line "Voice sample path/to/file.wav" |
| **The model read "Save the result to file…" aloud** | the instruction was on the same line as the speech — move it to a line of its own |
| **"Reference audio was not passed"** | the description has no audio file name with a word like `voice` or `sample`; Chatterbox and Zonos require the recording |
| **The result file went to the model as a sample** | the result line or the file name contains a recognition word (`voice`, `sample`, `speaker`…) — name the result file differently, e.g. `dialog_01_y.wav` |
| **"This model does not accept a recording"** | the task went to a model without samples (ElevenLabs-TTS-v3 or a music model) — set the executor explicitly |
| **HTTP 422 from the gateway** | the sample is longer than 30 s, the format does not fit, or a request field is unknown to the endpoint |
| **The voice does not sound like the sample** | the recording has noise, music or a second voice — take a clean 10–20 s |
| **The song is in the wrong language** | state the language in the description; for ACE-Step, the language code in the `language` field of the workflow |
| **The track is shorter or longer than expected** | for ACE-Step `length` is in seconds |

## 8. Worth remembering

* A media model receives **only the task description**; for a speech model all of it becomes
  the text it will speak.
* A constant voice of a speech model is held by a **sample**, not by training: the same
  recording in all lines of a person.
* The music models of the catalog do not clone a voice from a sample — a singer's voice is set
  in words.
* The catalog makes no sound effects, and the layers are mixed not by AI2P but by an editor.
* The rights to a voice and to library sounds are your concern: the model license does not give
  them.

## See also

* [Project objects](objects.md) — the "reference audio" kind and the `@obj:` reference.
* [A video clip made from a character's reference frame](sample_video1.md) — installing a model,
  the executor and the start, step by step.
* [AI models](../models/README.md) — the document of every audio model: prices, licenses,
  request fields.

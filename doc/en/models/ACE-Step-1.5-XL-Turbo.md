# ACE-Step-1.5-XL-Turbo

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `acestep_v1_5_xl_turbo`
**API key:** not needed — the server runs locally without authorization
**What it does:** text → music and songs, skills `audio-song` 84, `audio-music` 86

A music generation model by the ACE-Step team, the **Turbo** variant: a distilled model
that renders a track in **eight steps** instead of fifty and without classifier-free
guidance. This is the fastest way to get a finished track — a sensible first choice while
you are still working out the wording.

The feature that makes ACE-Step 1.5 worth taking: a **language model planner** runs inside
it. It turns your request into a song blueprint on its own — structure, lyrics, tempo and
key. So there is no separate lyrics field to fill in: describe the task in words, and the
model will write the lyrics itself (or use yours, if you wrote them). It understands more
than fifty languages.

It runs through **ComfyUI**: AI2P starts it as a local server, sends the workflow and
picks up the finished `.mp3` into the task artifacts.

## No key, but an installation

Everything is installed with the **"Install"** button in the model form: **Settings →
Catalogs → Models → ACE-Step-1.5-XL-Turbo → "Install"**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **four weight files** into the model repository (`storage.modelsRepo`), group `ACE-Step-1.5`:

| File | Size | Where |
|---|---|---|
| `acestep_v1.5_xl_turbo_bf16.safetensors` | ~9.3 GiB | `diffusion_models` |
| `qwen_4b_ace15.safetensors` | ~7.8 GiB | `text_encoders` |
| `qwen_0.6b_ace15.safetensors` | ~1.1 GiB | `text_encoders` |
| `ace_1.5_vae.safetensors` | ~322 MiB | `vae` |

**About 19.9 GB of downloads in total.** Downloads resume: an interrupted installation
continues where it stopped, already downloaded files are not fetched again.

The three ACE-Step 1.5 XL entries in the catalog (Turbo, Base, SFT) share **one group**
and three files out of four. If you have already installed one of them, the next one only
downloads its own weights — about 9.3 GiB rather than the full 19.9 GB.

Until the files are in place the model **cannot be active** — this is also checked when
the application starts.

## Licence

| | |
|---|---|
| Model weights | **MIT** |
| Commercial use | allowed — the authors call this the model's headline feature |
| What is required | keep the license text and the copyright notice |
| License text | <https://huggingface.co/ACE-Step/Ace-Step1.5> (model card) |
| Payment per generation | none — your own graphics card does the work |

ACE-Step 1.5 states commercial use explicitly: the model was trained on licensed,
royalty-free and synthetic recordings precisely so that the output can be used
commercially. That sets it apart from models trained on data of unclear origin.

The files AI2P downloads are the Comfy-Org repack
(<https://huggingface.co/Comfy-Org/ace_step_1.5_ComfyUI_files>), published under
**Apache 2.0**. Licenses were checked against the model cards on 2026-08-27; open models
rarely change them, but check the card again before a commercial release.

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P starts it as an external
program and talks to it over HTTP, so that license does not extend to your product — but
if you hand someone a build with ComfyUI inside, GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| GPU | NVIDIA, **8 GB VRAM or more** (the authors claim it runs from 4 GB) |
| NVIDIA driver | **580 or newer** — on an older driver torch dies without a message |
| Disk space | ~22 GB (weights + the ComfyUI package) |
| RAM | 16 GB or more |
| OS | Windows (portable ComfyUI build); on Linux ComfyUI is installed by hand |

## How long it takes

Eight steps means **seconds to tens of seconds** per track: the authors claim a full song
in under 10 seconds on an RTX 3090. The job timeout in the profile is set to 60 minutes
(`params.timeoutMinutes`), which is ample even on slow hardware.

Progress is visible in the **job console** in the task card: ComfyUI's own output,
including its progress bar, is streamed there.

## Generation parameters

Edited in the model profile ("Connection profile" button):

| Parameter | Default | Meaning |
|---|---|---|
| `length` | 120 | **track duration in SECONDS** (not frames as for video) |
| `steps` | 8 | diffusion steps; more than eight makes no sense for Turbo |
| `width` / `height` | not set | audio has no frame size; the job summary shows the profile defaults 768×512, which have nothing to do with the audio |
| `negative` | empty | negative prompt — **has no effect on Turbo** (see below) |
| `timeoutMinutes` | 60 | how long to wait for the result |

**About duration.** In this entry `length` means seconds and goes to two places in the
graph at once: the size of the empty latent and the planner's `duration` field. In the job
summary it is labelled "frames" — that label is shared by all media models; read it as
"seconds". The model is designed for tracks of up to roughly ten minutes.

**About the negative prompt.** Turbo runs without classifier-free guidance (`cfg = 1`), so
the negative condition in the workflow is a `ConditioningZeroOut` node — exactly as in the
official ComfyUI template. You may fill in `negative` in the profile, but it will not
affect the audio; if you need a negative prompt, use **ACE-Step-1.5-XL-Base** or **-SFT**.

**About the vocal language.** The `language` field in the graph is set to `unknown` — the
model detects the language from your text. The official ComfyUI templates put `en` there,
which would give English pronunciation for non-English lyrics. If you always sing in one
language, put its code (`ru`, `en`, `zh`, …) directly into the model's workflow template.

The task description goes into the prompt as a whole (the model's `tags` field). An
instruction like "put the result into file X.mp3" is executed by the connector: the file
is copied into the project folder and the line itself is cut out of the prompt.

## How to write the task

The model expects a description of the music, not a command. What works:

* **style, tempo, instruments, mood** — "calm ambient, 72 BPM, warm pads, vinyl crackle";
* **vocals** — "female vocal, quiet, with a long reverb tail";
* **your own lyrics** — write them straight into the task description, the planner picks
  them up;
* **instrumental** — say so: "no vocals, instruments only".

Every run takes a **random seed**, so two jobs with the same text produce different
tracks. If you need a repeatable result, put a numeric `seed` into the model profile.

## LoRA training

**Applying — yes, training — no.**

The model accepts a ready adapter: ComfyUI knows the official ACE-Step LoRA format, and
AI2P inserts a `LoraLoaderModelOnly` node into the graph on the fly. Put the file into
`<model repository>/loras/` and name the adapter object in the task description with an
`@obj:` reference.

But **an adapter cannot be trained from AI2P**, and the profile says so honestly
(`lora.train.kind: external`). There are two reasons:

* [musubi-tuner](https://github.com/kohya-ss/musubi-tuner), which AI2P uses to train LoRA
  for the other local models, does not know ACE-Step at all — it has no acestep script
  whatsoever (checked on 2026-08-27);
* the official ACE-Step trainer (<https://github.com/ace-step/ACE-Step-1.5>, `train.py`)
  trains on **audio recordings**, whereas a LoRA dataset in AI2P is made of image frames:
  both the dataset editor and the size checks assume images.

So the adapter is trained outside AI2P with the official trainer and brought here as a
finished file.

## Common problems

* **The process disappears without a message** — almost always an old NVIDIA driver (see
  above).
* **"Model is not installed"** — the files are not fully downloaded; open "Install" and
  the window will show what is left.
* **The negative prompt has no effect** — by design for Turbo (see "Generation parameters").
* **The vocal sings in the wrong language** — put the language code into the `language`
  field of the workflow template instead of `unknown`.
* **The track is shorter or longer than expected** — that is `length` in the profile, and
  it is in seconds.
* **ComfyUI is busy with someone else's process** — AI2P only shuts down the server it
  started itself; an already running foreign ComfyUI on 8188 is left alone.

# ACE-Step-1.5-XL-Base

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `acestep_v1_5_xl_base`
**API key:** not needed — the server runs locally without authorization
**What it does:** text → music and songs, skills `audio-song` 86, `audio-music` 87

A music generation model by the ACE-Step team, the **Base** variant — the pre-trained
weights without preference tuning: 50 diffusion steps and `cfg 6`. The authors rate its
quality as medium and its **diversity as high**: it is more willing to go for unusual
solutions. Take it when you need options rather than one "correct" track.

The three ACE-Step 1.5 XL entries in the catalog differ only in this:

| Entry | Steps | `cfg` | Quality | Diversity |
|---|---|---|---|---|
| ACE-Step-1.5-XL-Turbo | 8 | 1 (no CFG) | commercial | medium |
| **ACE-Step-1.5-XL-Base** | 50 | 6 | medium | **high** |
| ACE-Step-1.5-XL-SFT | 50 | 7 | **high** | medium |

A feature shared by all three: a **language model planner** runs inside the model. It
turns your request into a song blueprint on its own — structure, lyrics, tempo and key. So
there is no separate lyrics field to fill in: describe the task in words, and the model
will write the lyrics itself (or use yours, if you wrote them). It understands more than
fifty languages.

It runs through **ComfyUI**: AI2P starts it as a local server, sends the workflow and
picks up the finished `.mp3` into the task artifacts.

## No key, but an installation

Everything is installed with the **"Install"** button in the model form: **Settings →
Catalogs → Models → ACE-Step-1.5-XL-Base → "Install"**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **four weight files** into the model repository (`storage.modelsRepo`), group `ACE-Step-1.5`:

| File | Size | Where |
|---|---|---|
| `acestep_v1.5_xl_base_bf16.safetensors` | ~9.3 GiB | `diffusion_models` |
| `qwen_4b_ace15.safetensors` | ~7.8 GiB | `text_encoders` |
| `qwen_0.6b_ace15.safetensors` | ~1.1 GiB | `text_encoders` |
| `ace_1.5_vae.safetensors` | ~322 MiB | `vae` |

**About 19.9 GB of downloads in total.** Downloads resume: an interrupted installation
continues where it stopped, already downloaded files are not fetched again.

All three ACE-Step 1.5 XL entries share **one group** and three files out of four. If you
have already installed Turbo or SFT, this entry only downloads its own weights — about
9.3 GiB.

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
commercially.

The files AI2P downloads are the Comfy-Org repack
(<https://huggingface.co/Comfy-Org/ace_step_1.5_ComfyUI_files>), published under
**Apache 2.0**. Licenses were checked against the model cards on 2026-08-27.

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

Fifty steps against Turbo's eight is roughly **six times longer** — minutes per track
rather than tens of seconds — plus a second pass for the negative condition (`cfg 6` turns
classifier-free guidance on). The job timeout in the profile is set to 60 minutes
(`params.timeoutMinutes`).

Progress is visible in the **job console** in the task card.

## Generation parameters

Edited in the model profile ("Connection profile" button):

| Parameter | Default | Meaning |
|---|---|---|
| `length` | 120 | **track duration in SECONDS** (not frames as for video) |
| `steps` | 50 | diffusion steps |
| `width` / `height` | not set | audio has no frame size; the job summary shows the profile defaults 768×512, which have nothing to do with the audio |
| `negative` | empty | negative prompt (unlike Turbo, it works here) |
| `timeoutMinutes` | 60 | how long to wait for the result |

**About duration.** The duration of each track is named by the **prompter model**
from the task description ("a minute and a half" becomes `duration: 90`). The profile's
`length` field is the fallback now: it applies when no prompter is chosen, the prompter did
not answer, or the task says nothing about duration. The value goes to two places in the
graph at once — the size of the empty latent and the planner's `duration` field — and its
bounds are hard: 1 to 1000 seconds (checked against a live ComfyUI). In the job summary
`length` is labelled "frames" — that label is shared by all media models; read it as
"seconds".

**About the vocal language.** The language is named by the prompter — a code from
the node's list (`ru`, `en`, `zh`, `ja`, … 51 values in all). If nobody names it, it stays
`unknown` and the model detects the language from the lyrics itself; the official ComfyUI
templates put `en` there, which would give English pronunciation to non-English lyrics. A
permanent language can also be fixed without a prompter — by its code right in the model's
workflow template.

Without a prompter the task description goes into the prompt as a whole (the model's `tags` field); with a prompter, `tags` gets the style tags it composed. An
instruction like "put the result into file X.mp3" is executed by the connector: the file
is copied into the project folder and the line itself is cut out of the prompt.

## The prompter

This entry is marked in its profile as **"needs a prompter"**. The prompter is ANOTHER
EXECUTOR: before the generation it reads the task description and prepares the control json
for ACE-Step as a separate job. Any AI executor will do - a CLI subscription, a local model,
a cloud API: it works through its own connector, just as on an ordinary task. You assign it
in two places: the field **"Prompter"** in the AI executor's card (the default for all its
tasks) and the field **"Prompter"** in the task form, next to the list **"Can replace the
prompter"** - for the case when the assigned one is busy with other work.

The prompter fills seven fields of the `TextEncodeAceStepAudio1.5` node:

| Field | What it is | If not named |
|---|---|---|
| `tags` | style tags: genre, tempo, instruments, mood, vocals | the whole task description |
| `lyrics` | song lyrics | empty — the model writes them itself |
| `duration` | duration in seconds (1…1000) | `length` of the profile |
| `language` | vocal language code from the node's list | `unknown` |
| `bpm` | tempo, beats per minute (10…300) | 120 |
| `keyscale` | key and mode (`C major` … `B minor`) | `C major` |
| `timesignature` | time signature: 2, 3, 4 or 6 | 4 |

**What happens if you do not set a prompter.** The task WILL NOT START: it stops with an
error saying that the executor needs a prompter and there is none, neither on the task nor
on the executor itself. The system cannot silently go "as usual": the track parameters would
then be taken out of thin air, and you would see it half an hour later, when the wrong track
is ready. An answer with no json in it and a failed prompter job end the same way. The other
two outcomes are softer: if the assigned prompter is busy, the work is taken by the first
free one from "can replace the prompter", and if all of them are busy the task waits, paused,
until someone becomes free; if the prompter asked a human a question, the task is paused too
and continues with the answer.

**Starting it again.** A task that is paused or in error and already has its json goes
straight to the generation — the prompter is not asked twice. A task in draft, pending or
needs-fix starts from the beginning: the prompter prepares a new json.

**How to steer the result from the task description.** Write what has to reach the fields:
the duration ("a minute", "90 seconds"), the vocal language, the tempo, the mode, the time
signature — and give the lyrics verbatim, the prompter carries them over as they are.
Describe the style in words: it turns them into tags. What it actually named is visible in
the job console and in the `prompter.json` file among the task files.

**The rules it follows** live next to the model profile — the file
`models/prompter_<record id>.md` in the data directory. You may edit it: the text goes into
the prompter's prompt as a whole, and an edit applies from the next job on. The file is
rewritten by the installation when its version goes up, and replication does not carry it
to other servers.

## How to write the task

The model expects a description of the music, not a command. What works:

* **style, tempo, instruments, mood** — "calm ambient, 72 BPM, warm pads, vinyl crackle";
* **vocals** — "female vocal, quiet, with a long reverb tail";
* **your own lyrics** — write them straight into the task description, the planner picks
  them up;
* **instrumental** — say so: "no vocals, instruments only".

Every run takes a **random seed**, so two jobs with the same text produce different
tracks. If you need a repeatable result, put a numeric `seed` into the model profile. The
spread between runs is noticeably wider for Base than for SFT: that is its property, not a
malfunction.

The description is read by the prompter, so write the numbers and the language
straight into it: "90 seconds", "vocals in Russian", "120 beats per minute", "in a minor
key". What it made of that is shown in the job console.

## LoRA training

**Applying — yes, training from AI2P — no.**

The model accepts a ready adapter: AI2P inserts a `LoraLoaderModelOnly` node into the
graph on the fly. Put the file into `<model repository>/loras/` and name the adapter
object in the task description with an `@obj:` reference.

An adapter cannot be trained from AI2P, and the profile says so honestly:
`lora.train.kind: external` with an empty command — the «Train» button refuses at once
instead of burning half an hour first. Checked against repository files on 2026-09-14:

* **the model does have a trainer** — the official
  [ACE-Step-1.5](https://github.com/ace-step/ACE-Step-1.5) under the MIT license, and it
  runs on Windows with a SINGLE GPU: `python -m acestep.training_v2.cli.train_fixed`, no
  `torchrun`, `--num-devices` defaults to 1, DataLoader workers deliberately 0 on Windows.
  It needs 16 GB of VRAM at least, 20 GB or more recommended;
* **but it needs different weights from the ones we install.** The trainer reads a
  checkpoint directory in HuggingFace layout (`config.json` +
  `model-0000N-of-00004.safetensors`, about 19.9 GB per variant, plus the VAE and the
  labelling language model), while AI2P installs the Comfy-Org repack: different files,
  different layout. A second copy of the weights is not downloaded by the installer;
* **and the format of the trained file is unverified**: the trainer emits a peft adapter
  over its own DiT, while the «official ACE-Step format» branch in `comfy/lora.py` sits
  under the `ACEStep` class, whereas 1.5 is a separate `ACEStep15` class;
* [musubi-tuner](https://github.com/kohya-ss/musubi-tuner), which AI2P uses to train LoRA
  for the other local models, does not know ACE-Step at all (checked on 2026-08-27).

**The dataset limits are declared nonetheless** — AI2P checks the dataset against them and
you build it for outside training. The dataset is AUDIO (`media: audio`): recordings up to
240 s, 48 000 Hz, 2 channels, WAV, MP3, FLAC, OGG, Opus, 10 recordings and up, captions in
a `.txt` file next to the recording (a transcript or tags). The dataset editor shows
exactly these fields and stores a recording as is — audio goes through no image squeezing.

The official trainer's order of work: prepare recordings with `<name>.lyrics.txt` and
captions → preprocess into tensors → start training (LoRA or LoKr, which is about ten
times faster). Details — [LoRA Training Tutorial](https://github.com/ace-step/ACE-Step-1.5/blob/main/docs/en/LoRA_Training_Tutorial.md).

## Common problems

* **The process disappears without a message** — almost always an old NVIDIA driver (see
  above).
* **"Model is not installed"** — the files are not fully downloaded; open "Install" and
  the window will show what is left.
* **Too slow** — that is 50 steps with CFG; use Turbo for drafts.
* **The vocal sings in the wrong language** — name the language in the task
  description, the prompter passes it on; without a prompter, put the language code into
  the `language` field of the workflow template instead of `unknown`.
* **The track is shorter or longer than expected** — name the duration in the task
  description (the prompter hands it over in seconds); without a prompter it is `length` in
  the profile, which is in seconds too.
* **ComfyUI is busy with someone else's process** — AI2P only shuts down the server it
  started itself; an already running foreign ComfyUI on 8188 is left alone.

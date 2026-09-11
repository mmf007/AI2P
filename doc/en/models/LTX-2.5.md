# LTX-2.5

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `ltx_2_5_distilled`
**API key:** not needed — the server runs locally without authentication
**What it does:** text → video **with sound**, skill `video-generate` 85

A video model by Lightricks, 22 billion parameters, the distilled variant. It is the
**only open model that draws video and sound in a single pass**: the soundtrack is
computed as its own latent next to the video and lands in the same `.mp4`. For every other
local model in the catalogue the sound has to be added separately.

Its picture quality is above Wan 2.2 and HunyuanVideo 1.5, but it is heavier as well: 22B
against 14B, and the weights are behind an agreement (see below — the files have to be put
in place by hand).

It runs through **ComfyUI**: AI2P starts it as a local server, sends the workflow and
picks up the finished `.mp4` into the task artifacts.

## Installation: the files have to be put in place by hand

The weight repository is **gated**: HuggingFace serves the files only to those who
accepted the licence, and only with a personal token. The AI2P downloader works without a
token and will be refused (checked with a request on 2026-08-27), so the order is:

1. open <https://huggingface.co/Lightricks/LTX-2.5>, sign in to your HuggingFace account
   and accept the licence;
2. download the four files from the table below;
3. put them into the `LTX-2.5` subdirectory of the model repository
   (`storage.modelsRepo`) — **flat, without nested folders**, keeping the file names;
4. press **“Install”** in the model form: **Settings → Catalogs → Models → LTX-2.5 →
   “Install”**. The installation will see the matching sizes, will fetch only the ComfyUI
   package, and the model becomes installed.

| File | Size | Where |
|---|---|---|
| `ltx-2.5-22b-distilled-transformer-comfy-int8-convrot.safetensors` | ~20 GiB | `diffusion_models` |
| `gemma4-12b-with-proj-ltx-2.5-comfy-int8-convrot.safetensors` | ~14.3 GiB | `text_encoders` |
| `ltx-2.5-video-vae-bf16.safetensors` | ~1.4 GiB | `vae` |
| `ltx-2.5-audio-vae-bf16.safetensors` | ~348 MiB | `vae` |

**About 38.7 GB in total.** Until the files are in place the model **cannot be active** —
this is also checked when the application starts.

## Licence

| | |
|---|---|
| Model weights | **LTX-2 Community License Agreement** (neither Apache nor MIT) |
| Commercial use | allowed under an agreement you have to accept personally |
| What is required | accept the licence on HuggingFace, keep the notice; the terms limit large deployments |
| Licence text | <https://github.com/Lightricks/LTX-2/blob/main/LICENSE.md> |
| Model card | <https://huggingface.co/Lightricks/LTX-2.5> |
| Cost per generation | none — your graphics card does the work |

This is **not** a free licence: the repository is gated, and until the agreement is
accepted the files are not available at all. Read the full text before a commercial
release. The licence was checked against the model card on 2026-08-27 (`license_name:
ltx-2-community-license-agreement`).

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P starts it as an external
program and talks to it over HTTP, so that licence does not propagate to your product —
but if you ship a build with ComfyUI inside it, the GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| Graphics card | NVIDIA, **24 GB VRAM or more** (32 GB is comfortable) |
| NVIDIA driver | **580 or newer** — on an older driver torch dies without a message |
| Disk space | ~41 GB (weights plus the ComfyUI package) |
| RAM | 32 GB or more |
| OS | Windows (the portable ComfyUI build); on Linux ComfyUI is installed by hand |

The weights are taken in the `int8-convrot` build — the same one the official ComfyUI
template uses: it is half the size of bf16, where the transformer alone takes 39 GiB.

## How long to wait

The model is distilled and takes only eight steps, so a 1280×704 clip of 121 frames
(5 seconds at 24 frames per second) computes **faster** than Wan 2.2 with its twenty
steps — single to tens of minutes. The job timeout in the profile is set to 240 minutes
(`params.timeoutMinutes`).

## Generation parameters

| Parameter | Default | Meaning |
|---|---|---|
| `width` / `height` | 1280 × 704 | frame size; both numbers are multiples of 32, as the latent node requires |
| `length` | 121 | frames in the clip; 24 frames per second, that is 5 seconds |
| `steps` | 8 | **for reference**: the step count comes from the template sigmas, see below |
| `negative` | empty | negative prompt |
| `timeoutMinutes` | 240 | how long to wait for the result |

**About the steps.** For a distilled model the noise schedule is given as a list of
numbers right in the template (the `ManualSigmas` node) rather than as a step count: nine
values means eight steps. Changing `steps` in the profile does not affect generation, the
value is there for the job summary; to change the schedule, edit `sigmas` in
`models/workflow_….json` of the organization directory.

**What our template lacks against the official one.** The official ComfyUI template
computes in two passes: the first at half size, the second raises the resolution with the
`LTXVLatentUpsampler` node. The AI2P connector does no arithmetic over `width`/`height`,
so there would be nowhere to take the “half” from, and the job summary would name the
wrong size to the person — here it is a single pass straight at the target resolution.
Prompt enhancement by a separate language model (`TextGenerateLTX2Prompt`) is disabled as
well: it pulls in another weight file and rewrites the task text silently.

## LoRA training

The adapter is **applied** as usual: the `LoraLoaderModelOnly` node is inserted into the
graph on the fly, the files are taken from `<model repository>/loras`. LTX has a rich
ready-made LoRA ecosystem — camera-motion, style and control adapters are published by
Lightricks itself.

**Training an adapter from AI2P, however, is not possible**, and the record says so
honestly: [musubi-tuner](https://github.com/kohya-ss/musubi-tuner), which AI2P uses to
train LoRA for the other local models, has no LTX trainer at all — it has Wan,
HunyuanVideo, Kandinsky, Qwen-Image, Z-Image, but not LTX. So the profile declares the
training method as “external”: the “train” button answers with a clear refusal and a link
instead of hanging for half an hour and failing.

An adapter can be trained separately with the official Lightricks trainer
(<https://github.com/Lightricks/LTX-Video-Trainer>) and the finished `.safetensors` file
put into `<model repository>/loras` — AI2P will pick it up from there.

## Common problems

* **The installation is refused during download** — the repository is gated, the files
  have to be brought by hand (see “Installation”).
* **“The model is not installed” after a manual copy** — check the file names and that
  they sit directly in the `LTX-2.5` group directory rather than in nested folders; the
  size must match the table down to the byte.
* **A clip without sound** — the sound comes from the same pass; if it is missing, check
  that the `LTXVEmptyLatentAudio` and `LTXVAudioVAEDecode` nodes are still in the template.
* **Out of VRAM** — reduce `width`/`height` or `length`.

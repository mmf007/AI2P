# FLUX.2-klein-4B-Edit

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `flux2_klein_4b_edit`
**API key:** not needed — the server runs locally without authentication
**What it does:** picture + instruction → image, skills `image-edit` 86,
`image-inpaint` 83, `image-generate` 80, `image-text` 78

The very same model as `FLUX.2-klein-4B`, but in the **image editing** mode: it takes a
source picture and an instruction in plain words (“repaint the wall blue”, “remove the
wires”, “change the background to an evening one”) and returns the corrected frame. The
weights are the same, only the generation graph differs.

The licence is **Apache 2.0**, the only free one in the FLUX.2 family.

It runs through **ComfyUI**: AI2P uploads the source picture into the engine
(`POST /upload/image`), substitutes its name into the workflow and picks up the finished
`.png` into the task artifacts.

## Where the source picture comes from

The path to the file is given in the task description relative to the project folder, or
as a reference to a project object (`@obj:OBJ-3`) — then its reference frames are
substituted. Without a source picture a job for this model does not start at all: the
picture is declared **required** (`refImage.required`).

The size of the result is taken from the picture itself (the `GetImageSize` node), so
`width` and `height` from the profile are not used in this mode. Before editing, the
picture is scaled down to one megapixel — exactly as in the official ComfyUI template.

## No key, but an installation

Everything is installed with the **“Install”** button in the model form:
**Settings → Catalogs → Models → FLUX.2-klein-4B-Edit → “Install”**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **three weight files** into the model repository (`storage.modelsRepo`), group
  `FLUX.2-klein-4B`:

| File | Size | Where |
|---|---|---|
| `flux-2-klein-base-4b.safetensors` | ~7.2 GiB | `diffusion_models` |
| `qwen_3_4b.safetensors` | ~7.5 GiB | `text_encoders` |
| `flux2-vae.safetensors` | ~320 MiB | `vae` |

**About 16 GB of download in total** — but the **group is shared** with the
`FLUX.2-klein-4B` record: if that one is already installed, nothing has to be downloaded
and both records become active at once.

Until the files are in place the model **cannot be active** — this is also checked when
the application starts.

## Licence

| | |
|---|---|
| Model weights | **Apache 2.0** |
| Commercial use | allowed |
| What is required | keep the licence text and the copyright notice |
| Licence text | <https://huggingface.co/black-forest-labs/FLUX.2-klein-4B> (model card) |
| Payment per generation | none — your own graphics card does the work |

The licence was verified against the model card on 2026-08-27
(`cardData.license: apache-2.0`). Only the 4B version is free: `FLUX.2-klein-9B` and
`FLUX.2-dev` come under the **FLUX Non-Commercial License v2.1** (no commercial use
without an agreement with Black Forest Labs).

The files are downloaded from the open Comfy-Org repack
(<https://huggingface.co/Comfy-Org/vae-text-encorder-for-flux-klein-4b>) under the same
Apache 2.0: the original Black Forest Labs repositories answer `401 Unauthorized` without
a HuggingFace token.

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P runs it as an external
program and talks to it over HTTP, so that licence does not spread to your product — but
if you hand someone a build with ComfyUI inside, the GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| Graphics card | NVIDIA, **12 GB VRAM or more** (16 GB is comfortable) |
| NVIDIA driver | **580 or newer** — on an older driver torch dies without a message |
| Disk space | ~19 GB (weights plus the ComfyUI package) |
| RAM | 16 GB or more |
| OS | Windows (portable ComfyUI build); on Linux ComfyUI is installed by hand |

## How long it takes

Twenty steps on a roughly one-megapixel frame is **tens of seconds** on a modern card.
Editing is slightly slower than drawing from scratch: the source picture is encoded into a
latent as well. The job timeout in the profile is 60 minutes (`params.timeoutMinutes`).

The progress is visible in the **job console** of the task card.

## Generation parameters

Edited in the model profile (the “Connection profile” button):

| Parameter | Default | Meaning |
|---|---|---|
| `steps` | 20 | diffusion steps |
| `negative` | empty | negative prompt — it does work (`cfg 5`) |
| `timeoutMinutes` | 60 | how long to wait for the result |
| `width` / `height` | 1024 × 1024 | **not used in this mode**: the size comes from the source picture |

The whole task description goes into the prompt. Write an instruction rather than a
description of the whole scene: the model changes what is named and tries to leave the
rest alone.

## LoRA training

Fully supported: an adapter is both **applied** (the `LoraLoaderModelOnly` node is
inserted into the graph on the fly) and **trained** from AI2P itself — with the LoRA
editor in the project object card.

Training is done by [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — the
`flux_2_cache_latents.py`, `flux_2_cache_text_encoder_outputs.py` and
`flux_2_train_network.py` scripts (`--network_module networks.lora_flux_2`,
`--model_version klein-base-4b`). Nothing extra is downloaded: the trainer works on the
very base checkpoint that is installed for generation.

The `musubi-tuner` package (sources, ~30 MB) and **Python 3.12** (~45 MB) are installed
together with the model by the “Install” button. The `torch` environment (~3 GB) is
created by the trainer itself on the first training run.

The adapter is shared by both records: the one trained here also works in
`FLUX.2-klein-4B` and the other way round — the model behind them is one.

The finished adapter is placed into
`<model repository>/loras/<object code>.safetensors`. All training keys, `dataset.toml`
and `train.cmd` live in the model profile (`lora.train`) and are rewritten before every
run — edit them in the profile, not in the files.

## Common errors

* **“The model does not take a picture”** — the job was started on a record without
  editing; for editing pick exactly `FLUX.2-klein-4B-Edit`.
* **The source file is not found** — the path is resolved **from the project folder**, not
  from the disk root.
* **The model redrew everything** — the instruction was a description of the whole scene;
  say what exactly to change and add “leave the rest unchanged”.
* **Out of VRAM** — shrink the source picture: it is scaled to a megapixel, but very large
  files are still heavier.
* **ComfyUI is busy with someone else's process** — AI2P shuts down only the server it
  started itself.

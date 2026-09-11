# FLUX.2-dev

> **Warning: the licence is not a free one.** The FLUX.2 [dev] weights are released under
> the **FLUX Non-Commercial License v2.1** — they may only be used for non-commercial and
> non-production work. Commercial use requires a separate licence from Black Forest Labs.
> See the “Licence” section below; the free alternative in the same family is
> `FLUX.2-klein-4B` (Apache 2.0).

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `flux2_dev`
**API key:** not needed — the server runs locally without authentication
**What it does:** text → image, skills `image-generate` 94, `image-photo` 93,
`image-concept` 92, `image-text` 90

The largest model of the FLUX.2 family by Black Forest Labs, 32 billion parameters — **the
best quality among open weights** and the heaviest record in the catalogue: about 54 GB of
download. It is worth keeping if you have a powerful card and the work is non-commercial;
in every other case take `FLUX.2-klein-4B` or `Qwen-Image-2512`.

It runs through **ComfyUI**: AI2P starts it as a local server, sends the workflow and
picks up the finished `.png` into the task artifacts.

## No key, but an installation

Everything is installed with the **“Install”** button in the model form:
**Settings → Catalogs → Models → FLUX.2-dev → “Install”**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **three weight files** into the model repository (`storage.modelsRepo`), group
  `FLUX.2-dev`:

| File | Size | Where |
|---|---|---|
| `flux2_dev_fp8mixed.safetensors` | ~33.0 GiB | `diffusion_models` |
| `mistral_3_small_flux2_fp8.safetensors` | ~16.8 GiB | `text_encoders` |
| `flux2-vae.safetensors` | ~320 MiB | `vae` |

**About 54 GB of download in total.** It resumes: an interrupted installation continues
from where it stopped. Both heavy files are already fp8 builds — the original bf16 ones
are twice as large and pointless on a consumer card.

The text encoder of dev is its own — **Mistral 3 Small**, not Qwen3 as in klein: files are
not shared between the records of the family.

Until the files are in place the model **cannot be active** — this is also checked when
the application starts.

## Licence

| | |
|---|---|
| Model weights | **FLUX Non-Commercial License v2.1** — NOT a free licence |
| Commercial use | **forbidden** without a separate agreement with Black Forest Labs |
| What is allowed | personal research, experimentation, study, hobby projects — anything you receive no direct or indirect payment for |
| What is required | keep the licence text and the notices, do not remove the content filters |
| The generated output | under the licence it is **not considered a derivative** of the model, which does not lift the ban on commercial use of the weights themselves |
| Licence text | <https://huggingface.co/black-forest-labs/FLUX.2-dev/blob/main/LICENSE.md> |
| Commercial licence | <https://bfl.ai/> |
| Payment per generation | none — your own graphics card does the work |

The licence was verified against the repository `LICENSE.md` on 2026-08-27. The wording of
the source: the weights, parameters and inference code are made “freely available for your
**non-commercial and non-production** use”.

**The same licence covers FLUX.2 [klein] 9B** (both the base version and the fp8 builds).
In the whole family only 4B is free: it carries Apache 2.0.

The files are downloaded from the open Comfy-Org repack
(<https://huggingface.co/Comfy-Org/flux2-dev>) — the original Black Forest Labs
repositories are gated behind licence acceptance and answer `401 Unauthorized` without a
HuggingFace token. The repack is published under **the very same** non-commercial licence:
the fact that the file downloads without a token lifts no restrictions.

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P runs it as an external
program and talks to it over HTTP, so that licence does not spread to your product — but
if you hand someone a build with ComfyUI inside, the GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| Graphics card | NVIDIA, **24 GB VRAM or more**; on 16 GB it runs with offloading into system memory and noticeably slower |
| NVIDIA driver | **580 or newer** — on an older driver torch dies without a message |
| Disk space | ~57 GB (weights plus the ComfyUI package) |
| RAM | **48 GB or more** — with offloading the whole model is held there |
| OS | Windows (portable ComfyUI build); on Linux ComfyUI is installed by hand |

This is the most demanding record in the catalogue by all three measures at once: video
memory, system memory and disk space.

## How long it takes

Twenty steps on a 1024×1024 frame is **one to two minutes** on a 24 GB card and **much
longer** if the model is being offloaded into system memory. The job timeout in the
profile is 120 minutes (`params.timeoutMinutes`).

The progress is visible in the **job console** of the task card.

## Generation parameters

Edited in the model profile (the “Connection profile” button):

| Parameter | Default | Meaning |
|---|---|---|
| `width` / `height` | 1024 × 1024 | frame size |
| `steps` | 20 | diffusion steps |
| `negative` | empty | **has no effect**: dev has no negative condition at all (see below) |
| `timeoutMinutes` | 120 | how long to wait for the result |

**About the negative prompt.** FLUX.2 [dev] is a distilled model with guidance baked in:
the graph holds `FluxGuidance` (strength 4) and `BasicGuider`, there is no negative
condition there. You may fill in `negative` in the profile, but it will not change the
picture. If you need a negative prompt, take `FLUX.2-klein-4B` — it uses base weights and
a real `cfg`.

The whole task description goes into the prompt. FLUX.2 understands long connected
descriptions well — write sentences rather than a list of keywords.

## LoRA training

**Applying — yes; training from AI2P — no.**

A ready adapter is attached as in every ComfyUI record: the `LoraLoaderModelOnly` node is
inserted into the graph on the fly, the file is taken from `<model repository>/loras`.

Training an adapter from AI2P is not possible even though a trainer exists:
[musubi-tuner](https://github.com/kohya-ss/musubi-tuner) does support FLUX.2 [dev]
(`flux_2_train_network.py --model_version dev`), but it needs the **original** weights
from the gated Black Forest Labs repository — the single `flux2-dev.safetensors` and the
split Mistral 3 — not the repack AI2P installs. They can only be fetched with a
HuggingFace token after accepting the licence, so the record carries
`lora.train.kind = external`: the training button refuses at once.

Besides, the trainer's author explicitly recommends training adapters on the klein base
weights rather than on dev — that is what they are for. An adapter trained on
`FLUX.2-klein-4B` will **not** fit dev: these are models of different size.

## Common errors

* **Out of VRAM** — reduce `width`/`height`; if that does not help, this model is not for
  your card, take `FLUX.2-klein-4B`.
* **Generation takes tens of minutes** — the model is being offloaded into system memory;
  check that there is enough of it (see “Hardware requirements”).
* **The negative prompt does nothing** — that is how dev works (see “Generation
  parameters”).
* **`401 Unauthorized` on a manual download of your own** — you are pulling from a Black
  Forest Labs repository; the AI2P manifest points at the open Comfy-Org repack.
* **The LoRA training button refuses** — that is intended (see above).

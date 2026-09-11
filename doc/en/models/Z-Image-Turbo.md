# Z-Image-Turbo

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `z_image_turbo`
**API key:** not needed — the server runs locally without authentication
**What it does:** text → image, skills `image-generate` 85, `image-photo` 84,
`image-concept` 82, `image-text` 78

An image model by Tongyi-MAI (Alibaba), the **Turbo** variant: 6 billion parameters and
**eight steps** per frame instead of the usual twenty to fifty. It is the fastest and the
least demanding of the local image models in the catalogue — a sensible first choice if
the graphics card is modest.

It runs through **ComfyUI**: AI2P starts it as a local server, sends the workflow and
picks up the finished `.png` into the task artifacts.

## No key, but an installation

Everything is installed with the **“Install”** button in the model form:
**Settings → Catalogs → Models → Z-Image-Turbo → “Install”**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **three weight files** into the model repository (`storage.modelsRepo`), group `Z-Image`:

| File | Size | Where |
|---|---|---|
| `z_image_turbo_bf16.safetensors` | ~11.5 GiB | `diffusion_models` |
| `qwen_3_4b.safetensors` | ~7.5 GiB | `text_encoders` |
| `ae.safetensors` | ~320 MiB | `vae` |

**About 21 GB of download in total.** It resumes: an interrupted installation continues
from where it stopped, files already downloaded are not fetched again.

Until the files are in place the model **cannot be active** — this is also checked when
the application starts.

## Licence

| | |
|---|---|
| Model weights | **Apache 2.0** |
| Commercial use | allowed |
| What is required | keep the licence text and the copyright notice |
| Licence text | <https://huggingface.co/Tongyi-MAI/Z-Image-Turbo> (model card) |
| Payment per generation | none — your own graphics card does the work |

The files AI2P downloads are the Comfy-Org repack
(<https://huggingface.co/Comfy-Org/z_image_turbo>), released under the same Apache 2.0.
The licence was verified against the model card on 2026-08-27; for open models it rarely
changes, but check the card again before a commercial release.

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P runs it as an external
program and talks to it over HTTP, so that licence does not spread to your product — but
if you hand someone a build with ComfyUI inside, the GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| Graphics card | NVIDIA, **8 GB VRAM or more** (16 GB is comfortable) |
| NVIDIA driver | **580 or newer** — on an older driver torch dies without a message |
| Disk space | ~23 GB (weights plus the ComfyUI package) |
| RAM | 16 GB or more |
| OS | Windows (portable ComfyUI build); on Linux ComfyUI is installed by hand |

## How long it takes

Eight steps means **seconds to tens of seconds** per 1024×1024 frame on a modern card,
not hours as with the video models. The job timeout in the profile is 60 minutes
(`params.timeoutMinutes`), which is plenty even on slow hardware.

The progress is visible in the **job console** of the task card: ComfyUI's own output,
progress bar included, is streamed there.

## Generation parameters

Edited in the model profile (the “Connection profile” button):

| Parameter | Default | Meaning |
|---|---|---|
| `width` / `height` | 1024 × 1024 | frame size |
| `steps` | 8 | diffusion steps; more than eight makes no sense for Turbo |
| `negative` | empty | negative prompt — **has no effect on Turbo** (see below) |
| `timeoutMinutes` | 60 | how long to wait for the result |

**About the negative prompt.** The Turbo mode runs without classifier-free guidance
(`cfg = 1`), so the negative condition in the workflow is produced by the
`ConditioningZeroOut` node — exactly as in the official ComfyUI template. You may fill in
`negative` in the profile, but it will not change the picture; if you need a negative
prompt, use a non-Turbo model.

The whole task description goes into the prompt. An instruction such as “put the result
into file X.png” is carried out by the connector: the file is copied into the project
folder and the line itself is cut out of the prompt.

## LoRA training

Fully supported: an adapter is both **applied** (the `LoraLoaderModelOnly` node is
inserted into the graph on the fly) and **trained** from AI2P itself — with the LoRA
editor in the project object card.

Training is done by [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — the
`zimage_cache_latents.py`, `zimage_cache_text_encoder_outputs.py` and
`zimage_train_network.py` scripts (`--network_module networks.lora_zimage`), a single
graphics card, attention through `sdpa`.

The `musubi-tuner` package (sources, ~30 MB) and **Python 3.12** (~45 MB) are installed
together with the model by the “Install” button; a Python 3.10–3.12 already present on
the computer is taken as it is. The `torch` environment (~3 GB) is created by the trainer
itself on the first training run.

**A peculiarity of Turbo.** Turbo is a distilled checkpoint, and the trainer's author
does not recommend training LoRA on it, so on the first run `train.cmd` downloads the
**base** weights `z_image_bf16.safetensors` (~11.5 GB) once into the `train` subfolder of
the `Z-Image` group and trains on them. The text encoder and the VAE are taken from the
installed ones — they are identical for the base and the turbo version. The resulting
adapter works with the turbo weights.

The finished adapter is placed into
`<model repository>/loras/<object code>.safetensors`. All training keys, `dataset.toml`
and `train.cmd` live in the model profile (`lora.train`) and are rewritten before every
run — edit them in the profile, not in the files.

## Common errors

* **The process disappears without a message** — almost always an old NVIDIA driver
  (see above).
* **“The model is not installed”** — the files are not fully downloaded; open “Install”
  and the window will show what is left.
* **The negative prompt does nothing** — that is how Turbo works (see “Generation
  parameters”).
* **Out of VRAM** — reduce `width`/`height`.
* **ComfyUI is busy with someone else's process** — AI2P shuts down only the server it
  started itself; another ComfyUI already running on 8188 is left alone.

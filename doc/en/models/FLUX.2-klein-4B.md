# FLUX.2-klein-4B

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `flux2_klein_4b`
**API key:** not needed — the server runs locally without authentication
**What it does:** text → image, skills `image-generate` 88, `image-photo` 87,
`image-concept` 86, `image-text` 80

The smallest model of the **FLUX.2** family by Black Forest Labs: 4 billion parameters and
a **free Apache 2.0 licence** — the only one in the family with it (both 9B and [dev] are
non-commercial). It draws noticeably better than SDXL and SD 3.5, and by hardware demands
it sits between Z-Image-Turbo and Qwen-Image.

The **base** checkpoint is used (`flux-2-klein-base-4b`): twenty steps, `cfg 5` and a real
negative prompt. There is also a distilled one (four steps, `cfg 1`) — that is a different
weight file and it is not in the manifest.

The same model can also **edit a picture by instruction**; for that the catalogue holds a
separate record, `FLUX.2-klein-4B-Edit`, with the very same weights.

It runs through **ComfyUI**: AI2P starts it as a local server, sends the workflow and
picks up the finished `.png` into the task artifacts.

## No key, but an installation

Everything is installed with the **“Install”** button in the model form:
**Settings → Catalogs → Models → FLUX.2-klein-4B → “Install”**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **three weight files** into the model repository (`storage.modelsRepo`), group
  `FLUX.2-klein-4B`:

| File | Size | Where |
|---|---|---|
| `flux-2-klein-base-4b.safetensors` | ~7.2 GiB | `diffusion_models` |
| `qwen_3_4b.safetensors` | ~7.5 GiB | `text_encoders` |
| `flux2-vae.safetensors` | ~320 MiB | `vae` |

**About 16 GB of download in total.** It resumes: an interrupted installation continues
from where it stopped, files already downloaded are not fetched again.

The group is shared with `FLUX.2-klein-4B-Edit` — install one and the other needs no
download. The `qwen_3_4b.safetensors` text encoder is the same file Z-Image uses, but
groups in the model repository do not overlap, so each keeps its own copy.

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
(`cardData.license: apache-2.0`).

**Do not confuse it with the rest of the family.** Apache 2.0 applies to 4B only.
`FLUX.2-klein-9B` (base and fp8 builds included) and `FLUX.2-dev` are covered by the
**FLUX Non-Commercial License v2.1**, which forbids commercial use without a separate
agreement with Black Forest Labs.

The files AI2P downloads are the Comfy-Org repack
(<https://huggingface.co/Comfy-Org/vae-text-encorder-for-flux-klein-4b>) under the same
Apache 2.0. It is also needed because the original Black Forest Labs repositories are
gated behind licence acceptance: without a HuggingFace token a download from them answers
`401 Unauthorized`.

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

It does start on an 8 GB card as well — ComfyUI offloads parts of the model into system
memory — but a frame then takes several times longer. On a weak card use `Z-Image-Turbo`
or `SDXL-1.0` instead.

## How long it takes

Twenty steps on a 1024×1024 frame is **tens of seconds** on a modern card. The job timeout
in the profile is 60 minutes (`params.timeoutMinutes`).

The progress is visible in the **job console** of the task card: ComfyUI's own output,
progress bar included, is streamed there.

## Generation parameters

Edited in the model profile (the “Connection profile” button):

| Parameter | Default | Meaning |
|---|---|---|
| `width` / `height` | 1024 × 1024 | frame size |
| `steps` | 20 | diffusion steps |
| `negative` | empty | negative prompt — **it does work** (base weights, `cfg 5`) |
| `timeoutMinutes` | 60 | how long to wait for the result |

In FLUX.2 the step schedule depends on the frame size, so the number of steps is given not
to the sampler but to the `Flux2Scheduler` node — the shipped workflow already does that.

The whole task description goes into the prompt. An instruction such as “put the result
into file X.png” is carried out by the connector: the file is copied into the project
folder and the line itself is cut out of the prompt.

## LoRA training

Fully supported: an adapter is both **applied** (the `LoraLoaderModelOnly` node is
inserted into the graph on the fly) and **trained** from AI2P itself — with the LoRA
editor in the project object card.

Training is done by [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — the
`flux_2_cache_latents.py`, `flux_2_cache_text_encoder_outputs.py` and
`flux_2_train_network.py` scripts (`--network_module networks.lora_flux_2`,
`--model_version klein-base-4b`), a single graphics card, attention through `sdpa`.

The `musubi-tuner` package (sources, ~30 MB) and **Python 3.12** (~45 MB) are installed
together with the model by the “Install” button; a Python 3.10–3.12 already present on the
computer is taken as it is. The `torch` environment (~3 GB) is created by the trainer
itself on the first training run.

**The difference from Z-Image and Qwen-Image.** There generation runs on fp8 builds the
trainer does not accept, and `train.cmd` downloads its own pair of weights. Here there is
nothing to fetch: the manifest holds the base checkpoint itself, and training uses the
very files that are already installed. The trainer's author explicitly recommends training
on the klein base weights.

An adapter trained on this record also fits `FLUX.2-klein-4B-Edit`: the model behind them
is one and the same.

The finished adapter is placed into
`<model repository>/loras/<object code>.safetensors`. All training keys, `dataset.toml`
and `train.cmd` live in the model profile (`lora.train`) and are rewritten before every
run — edit them in the profile, not in the files.

## Common errors

* **The process disappears without a message** — almost always an old NVIDIA driver
  (see above).
* **“The model is not installed”** — the files are not fully downloaded; open “Install”
  and the window will show what is left.
* **Out of VRAM** — reduce `width`/`height` or take a lighter model.
* **`401 Unauthorized` on a manual download of your own** — you are pulling from a Black
  Forest Labs repository; the AI2P manifest points at the open Comfy-Org repacks.
* **ComfyUI is busy with someone else's process** — AI2P shuts down only the server it
  started itself; another ComfyUI already running on 8188 is left alone.

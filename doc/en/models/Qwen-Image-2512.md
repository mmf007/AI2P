# Qwen-Image-2512

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `qwen_image_2512`
**API key:** not needed — the server runs locally without authentication
**What it does:** text → image, skills `image-text` 94, `image-photo` 91,
`image-generate` 90, `image-concept` 85

An image model by Alibaba, revision **2512** (20 billion parameters). Its strength is
**text inside the frame**: signs, captions, covers, posters, interfaces — in Cyrillic as
well. It is the best of the open models at lettering and layout, and that is what to pick
it for when the picture has to contain words.

It runs through **ComfyUI**: AI2P starts it as a local server, sends the workflow and
picks up the finished `.png` into the task artifacts.

## No key, but an installation

Everything is installed with the **“Install”** button in the model form:
**Settings → Catalogs → Models → Qwen-Image-2512 → “Install”**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **three weight files** into the model repository (`storage.modelsRepo`), group
  `Qwen-Image`:

| File | Size | Where |
|---|---|---|
| `qwen_image_2512_fp8_e4m3fn.safetensors` | ~19.0 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8.7 GiB | `text_encoders` |
| `qwen_image_vae.safetensors` | ~242 MiB | `vae` |

**About 30 GB of download in total.** It resumes: an interrupted installation continues
from where it stopped, files already downloaded are not fetched again.

The `Qwen-Image` group is shared with the `Qwen-Image-Edit-2511` model: the text encoder
and the VAE are the same, so the second model only downloads its own weights (~19 GiB)
instead of the whole thirty gigabytes again.

Until the files are in place the model **cannot be active** — this is also checked when
the application starts.

## Licence

| | |
|---|---|
| Model weights | **Apache 2.0** |
| Commercial use | allowed |
| What is required | keep the licence text and the copyright notice |
| Licence text | <https://huggingface.co/Qwen/Qwen-Image> (model card) |
| Payment per generation | none — your own graphics card does the work |

The files AI2P downloads are the Comfy-Org repack
(<https://huggingface.co/Comfy-Org/Qwen-Image_ComfyUI>), released under the same
Apache 2.0. The licence was verified against the model card on 2026-08-27.

Mind the neighbouring name: **Qwen-Image-3.0 (August 2026) is closed** — no weights, no
licence and no technical report at all, and it cannot be run locally. The open line ends
at revisions 2512 (generation) and 2511 (editing).

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P runs it as an external
program and talks to it over HTTP, so that licence does not spread to your product — but
if you hand someone a build with ComfyUI inside, the GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| Graphics card | NVIDIA, **16 GB VRAM or more** (24 GB is comfortable) |
| NVIDIA driver | **580 or newer** — on an older driver torch dies without a message |
| Disk space | ~32 GB (weights plus the ComfyUI package) |
| RAM | 32 GB or more |
| OS | Windows (portable ComfyUI build); on Linux ComfyUI is installed by hand |

The manifest ships the **fp8** checkpoint: it is half the size of the original bf16 one
and is meant exactly for a consumer card. For LoRA training fp8 is not suitable — see
below.

## How long it takes

A 1328×1328 frame in 20 steps takes **minutes** on a 4090-class card, and up to ten
minutes on 8–16 GB with offloading to system memory. The job timeout in the profile is
90 minutes (`params.timeoutMinutes`).

The progress is visible in the **job console** of the task card: ComfyUI's own output,
progress bar included, is streamed there.

## Generation parameters

Edited in the model profile (the “Connection profile” button):

| Parameter | Default | Meaning |
|---|---|---|
| `width` / `height` | 1328 × 1328 | frame size |
| `steps` | 20 | diffusion steps; fewer is faster and coarser |
| `negative` | empty | negative prompt (it **does** work here, `cfg = 4`) |
| `timeoutMinutes` | 90 | how long to wait for the result |

The whole task description goes into the prompt. An instruction such as “put the result
into file X.png” is carried out by the connector: the file is copied into the project
folder and the line itself is cut out of the prompt.

Write the text that has to appear in the frame **in quotes and verbatim** — the model
reproduces exactly what is quoted.

## LoRA training

Fully supported: an adapter is both **applied** (the `LoraLoaderModelOnly` node is
inserted into the graph on the fly) and **trained** from AI2P itself — with the LoRA
editor in the project object card.

Training is done by [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — the
`qwen_image_cache_latents.py`, `qwen_image_cache_text_encoder_outputs.py` and
`qwen_image_train_network.py` scripts (`--model_version original`,
`--network_module networks.lora_qwen_image`), a single graphics card, attention through
`sdpa`.

The `musubi-tuner` package (sources, ~30 MB) and **Python 3.12** (~45 MB) are installed
together with the model by the “Install” button; a Python 3.10–3.12 already present on
the computer is taken as it is. The `torch` environment (~3 GB) is created by the trainer
itself on the first training run.

**The trainer needs different weight files.** musubi-tuner states plainly that the fp8
builds — the very ones generation uses — cannot be used for training, so on the first run
`train.cmd` downloads the **bf16** pair once: `qwen_image_2512_bf16.safetensors`
(~38 GiB) and `qwen_2.5_vl_7b.safetensors` (~15.4 GiB), into the `train` subfolder of the
`Qwen-Image` group. That is about **57 GB on top of the installation**, while generation
keeps running on the light fp8 files. The VAE is taken from the installed one.

The finished adapter is placed into
`<model repository>/loras/<object code>.safetensors`. All training keys, `dataset.toml`
and `train.cmd` live in the model profile (`lora.train`) and are rewritten before every
run — edit them in the profile, not in the files.

## Common errors

* **The process disappears without a message** — almost always an old NVIDIA driver
  (see above).
* **“The model is not installed”** — the files are not fully downloaded; open “Install”
  and the window will show what is left.
* **Out of VRAM** — reduce `width`/`height`; 20B on 8 GB only runs with offloading to
  system memory and is noticeably slower.
* **Training fails while fetching weights** — check the free disk space: the trainer needs
  about 57 GB of its own (see “LoRA training”).
* **ComfyUI is busy with someone else's process** — AI2P shuts down only the server it
  started itself; another ComfyUI already running on 8188 is left alone.

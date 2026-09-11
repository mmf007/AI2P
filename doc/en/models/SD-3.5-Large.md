# SD-3.5-Large

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `sd3_5_large`
**API key:** not needed — the server runs locally without authentication
**What it does:** text → image, skills `image-generate` 80, `image-photo` 79,
`image-concept` 78, `image-text` 70

Stable Diffusion 3.5 Large by Stability AI, 8 billion parameters. In the catalogue it is
the **middle step**: noticeably better than SDXL, noticeably weaker than FLUX.2 and
Qwen-Image, but it installs as **a single file** and runs on a 12 GB card.

The Comfy-Org fp8 build is used: **the text encoders live inside the file itself**, so
`clip_g`, `clip_l` and `t5xxl` need no separate download.

It runs through **ComfyUI**: AI2P starts it as a local server, sends the workflow and
picks up the finished `.png` into the task artifacts.

## No key, but an installation

Everything is installed with the **“Install”** button in the model form:
**Settings → Catalogs → Models → SD-3.5-Large → “Install”**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **one weight file** into the model repository (`storage.modelsRepo`), group `SD-3.5`:

| File | Size | Where |
|---|---|---|
| `sd3.5_large_fp8_scaled.safetensors` | ~13.9 GiB | `checkpoints` |

**About 15 GB of download in total.** It resumes: an interrupted installation continues
from where it stopped.

The category is `checkpoints`, not `diffusion_models` — this is a full checkpoint from
which ComfyUI takes the model, the text encoders and the VAE at once.

Until the file is in place the model **cannot be active** — this is also checked when the
application starts.

## Licence

| | |
|---|---|
| Model weights | **Stability AI Community License** (not a free licence) |
| Commercial use | allowed **while annual revenue is under US $1M**; above that an Stability AI Enterprise licence is required |
| What is required | ship the licence text, keep the notice “This Stability AI Model is licensed under the Stability AI Community License, Copyright © Stability AI Ltd.” and display “Powered by Stability AI” on a website or in the product documentation |
| Licence text | <https://huggingface.co/stabilityai/stable-diffusion-3.5-large/blob/main/LICENSE.md> |
| Payment per generation | none — your own graphics card does the work |

The licence was verified against the repository `LICENSE.md` on 2026-08-27 (revision of
5 July 2024). It is **not** a free licence: research and non-commercial use is always free
of charge, commercial use only below the revenue threshold above. If that threshold is
close for you, take `FLUX.2-klein-4B` (Apache 2.0) or `Kandinsky-5.0-Image-Lite` (MIT).

The file AI2P downloads is the Comfy-Org repack
(<https://huggingface.co/Comfy-Org/stable-diffusion-3.5-fp8>) under the same licence.

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P runs it as an external
program and talks to it over HTTP, so that licence does not spread to your product — but
if you hand someone a build with ComfyUI inside, the GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| Graphics card | NVIDIA, **10 GB VRAM or more** (12 GB is comfortable) |
| NVIDIA driver | **580 or newer** — on an older driver torch dies without a message |
| Disk space | ~18 GB (weights plus the ComfyUI package) |
| RAM | 16 GB or more |
| OS | Windows (portable ComfyUI build); on Linux ComfyUI is installed by hand |

## How long it takes

Twenty steps on a 1024×1024 frame is **tens of seconds** on a modern card. The job timeout
in the profile is 60 minutes (`params.timeoutMinutes`).

The progress is visible in the **job console** of the task card.

## Generation parameters

Edited in the model profile (the “Connection profile” button):

| Parameter | Default | Meaning |
|---|---|---|
| `width` / `height` | 1024 × 1024 | frame size |
| `steps` | 20 | diffusion steps |
| `negative` | empty | negative prompt — it does work (`cfg 4.01`) |
| `timeoutMinutes` | 60 | how long to wait for the result |

The `cfg 4.01` value is taken from the official ComfyUI template as it is.

The whole task description goes into the prompt. An instruction such as “put the result
into file X.png” is carried out by the connector: the file is copied into the project
folder and the line itself is cut out of the prompt.

## LoRA training

**Applying — yes; training from AI2P — no.**

A ready adapter is attached as in every ComfyUI record: the `LoraLoaderModelOnly` node is
inserted into the graph on the fly, the file is taken from `<model repository>/loras`.
There are plenty of public SD 3.5 adapters and they all fit.

Training an adapter from AI2P is not possible: `musubi-tuner`, which trains the other
local models, does not know the Stable Diffusion family at all — that is the domain of the
same author's other tool, [sd-scripts](https://github.com/kohya-ss/sd-scripts). Hence
`lora.train.kind = external` on this record, and the training button refuses at once
instead of failing half an hour into the run. A file trained elsewhere only has to be
dropped into `loras`.

## Common errors

* **Text in the frame does not come out** — that is SD 3.5's weak spot; for captions take
  `Qwen-Image-2512`, and for Cyrillic `Kandinsky-5.0-Image-Lite`.
* **“The model is not installed”** — the file is not fully downloaded; open “Install” and
  the window will show what is left.
* **The LoRA training button refuses** — that is intended (see above).
* **Out of VRAM** — reduce `width`/`height`.
* **ComfyUI is busy with someone else's process** — AI2P shuts down only the server it
  started itself.

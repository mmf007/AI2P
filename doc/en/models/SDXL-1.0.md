# SDXL-1.0

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `sdxl_base_1_0`
**API key:** not needed — the server runs locally without authentication
**What it does:** text → image, skills `image-generate` 70, `image-concept` 70,
`image-photo` 68

Stable Diffusion XL 1.0 by Stability AI — **the least demanding record in the
catalogue**: 6.9 GB in a single file and 8 GB of video memory. It is here exactly as the
bottom step: it is weaker than every neighbour and **cannot write text inside the frame**,
but it runs where FLUX.2, Qwen-Image and Kandinsky will not start at all.

A second argument in its favour: more LoRA adapters have been written for SDXL than for
all the other models together, and they all work here unchanged.

It runs through **ComfyUI**: AI2P starts it as a local server, sends the workflow and
picks up the finished `.png` into the task artifacts.

## No key, but an installation

Everything is installed with the **“Install”** button in the model form:
**Settings → Catalogs → Models → SDXL-1.0 → “Install”**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **one weight file** into the model repository (`storage.modelsRepo`), group `SDXL-1.0`:

| File | Size | Where |
|---|---|---|
| `sd_xl_base_1.0.safetensors` | ~6.5 GiB | `checkpoints` |

**About 7 GB of download in total** — less than any other image record. It resumes.

The category is `checkpoints`: this is a full checkpoint from which ComfyUI takes the
model, the text encoders and the VAE at once. The second SDXL stage (the Refiner) is not
shipped — it is a separate file, and this record exists as the lightest one.

Until the file is in place the model **cannot be active** — this is also checked when the
application starts.

## Licence

| | |
|---|---|
| Model weights | **CreativeML Open RAIL++-M** (dated 26 July 2023) |
| Commercial use | allowed, with no royalties and no revenue threshold |
| What is required | ship the licence text, keep the copyright notice and **pass the use restrictions on** to everyone you hand the model or a derivative of it |
| Restrictions | the licence forbids a list of uses (“Attachment A. Use Restrictions”): breaking the law, harming minors, disinformation, discrimination and so on |
| Licence text | <https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0/blob/main/LICENSE.md> |
| Payment per generation | none — your own graphics card does the work |

The licence was verified against the repository `LICENSE.md` on 2026-08-27. It is an
“open but responsible” licence: the rights it grants are those of a permissive licence,
but it adds a list of forbidden uses that you must pass on together with the model.
Stability claims no rights over the generated result itself.

The file is downloaded straight from the Stability AI repository
(<https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0>), no repack is needed.

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P runs it as an external
program and talks to it over HTTP, so that licence does not spread to your product — but
if you hand someone a build with ComfyUI inside, the GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| Graphics card | NVIDIA, **6 GB VRAM or more** (8 GB is comfortable) |
| NVIDIA driver | **580 or newer** — on an older driver torch dies without a message |
| Disk space | ~10 GB (weights plus the ComfyUI package) |
| RAM | 8 GB or more |
| OS | Windows (portable ComfyUI build); on Linux ComfyUI is installed by hand |

## How long it takes

Twenty-five steps on a 1024×1024 frame is **seconds to tens of seconds** even on a modest
card. The job timeout in the profile is 60 minutes (`params.timeoutMinutes`).

The progress is visible in the **job console** of the task card.

## Generation parameters

Edited in the model profile (the “Connection profile” button):

| Parameter | Default | Meaning |
|---|---|---|
| `width` / `height` | 1024 × 1024 | frame size; SDXL was trained at 1024 |
| `steps` | 25 | diffusion steps |
| `negative` | empty | negative prompt — it does work (`cfg 7`), and it matters more here than in the newer models |
| `timeoutMinutes` | 60 | how long to wait for the result |

SDXL is poor at long descriptions: a prompt works better as an enumeration (“what, where,
in what style, what shot”) than as a flowing paragraph. Newer models (FLUX.2, Qwen-Image)
behave the other way round.

The whole task description goes into the prompt. An instruction such as “put the result
into file X.png” is carried out by the connector.

## LoRA training

**Applying — yes; training from AI2P — no.**

A ready adapter is attached as in every ComfyUI record: the `LoraLoaderModelOnly` node is
inserted into the graph on the fly, the file is taken from `<model repository>/loras`.
This is exactly the case where thousands of ready adapters are publicly available.

Training an adapter from AI2P is not possible: `musubi-tuner` does not know the Stable
Diffusion family — for SDXL there is the same author's
[sd-scripts](https://github.com/kohya-ss/sd-scripts). Hence
`lora.train.kind = external` on this record and the training button refusing at once. A
file trained elsewhere only has to be dropped into `loras`.

## Common errors

* **Text in the frame turns into mush** — SDXL cannot do it; take `Qwen-Image-2512` or
  `Kandinsky-5.0-Image-Lite`.
* **The picture is mushy at an unusual size** — SDXL was trained at 1024×1024 and copes
  badly with much smaller frames.
* **“The model is not installed”** — the file is not fully downloaded; open “Install”.
* **The LoRA training button refuses** — that is intended (see above).
* **ComfyUI is busy with someone else's process** — AI2P shuts down only the server it
  started itself.

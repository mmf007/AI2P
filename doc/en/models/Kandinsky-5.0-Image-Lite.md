# Kandinsky-5.0-Image-Lite

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `kandinsky5lite_t2i`
**API key:** not needed — the server runs locally without authentication
**What it does:** text → image, skills `image-text` 88, `image-generate` 84,
`image-concept` 83, `image-photo` 82

An image model by Kandinsky Lab (Sber), 6 billion parameters, up to 1K resolution. The
main reason to keep it: it **understands Russian prompts and Russian settings** and
**writes Cyrillic inside the frame** — signs, package labels, captions. The other open
models in the catalogue do Cyrillic worse or not at all.

The licence is **MIT**, the most permissive of all the records in the catalogue.

It is a relative of the `Kandinsky-5.0-*` video records already in the catalogue: the text
encoders are shared, but the install group is its own (the image model has its own
checkpoint and its own VAE).

It runs through **ComfyUI**: AI2P starts it as a local server, sends the workflow and
picks up the finished `.png` into the task artifacts.

## No key, but an installation

Everything is installed with the **“Install”** button in the model form:
**Settings → Catalogs → Models → Kandinsky-5.0-Image-Lite → “Install”**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **four weight files** into the model repository (`storage.modelsRepo`), group
  `Kandinsky-5-Image`:

| File | Size | Where |
|---|---|---|
| `kandinsky5lite_t2i.safetensors` | ~11.2 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8.7 GiB | `text_encoders` |
| `clip_l.safetensors` | ~235 MiB | `text_encoders` |
| `ae.safetensors` | ~320 MiB | `vae` |

**About 22 GB of download in total.** It resumes: an interrupted installation continues
from where it stopped.

About `ae.safetensors`: this is the **FLUX** VAE, not a native one. That is how the model
is built — both the official ComfyUI template and the authors' own instructions say so
(`weights/flux/vae` goes into `ComfyUI/models/vae`).

Until the files are in place the model **cannot be active** — this is also checked when
the application starts.

## Licence

| | |
|---|---|
| Model weights | **MIT** |
| Commercial use | allowed |
| What is required | keep the licence text and the copyright notice |
| Licence text | <https://huggingface.co/kandinskylab/Kandinsky-5.0-T2I-Lite> (model card) |
| Payment per generation | none — your own graphics card does the work |

The licence was verified against the model card on 2026-08-27 (`cardData.license: mit`).
MIT puts no restrictions on the field of use — unlike SDXL (which carries a list of
forbidden uses) and FLUX.2 [dev] (where commercial use is forbidden outright).

The weights are downloaded **straight from the authors**, there is no Comfy-Org repack for
this model: `kandinskylab/Kandinsky-5.0-T2I-Lite`, file
`model/kandinsky5lite_t2i.safetensors` — exactly the name the official ComfyUI template
expects.

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P runs it as an external
program and talks to it over HTTP, so that licence does not spread to your product — but
if you hand someone a build with ComfyUI inside, the GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| Graphics card | NVIDIA, **12 GB VRAM or more** (16 GB is comfortable) |
| NVIDIA driver | **580 or newer** — on an older driver torch dies without a message |
| Disk space | ~25 GB (weights plus the ComfyUI package) |
| RAM | 16 GB or more |
| OS | Windows (portable ComfyUI build); on Linux ComfyUI is installed by hand |

## How long it takes

Fifty steps is **a minute or two** per 1024×1024 frame on a modern card (the authors
measure 13 seconds on an H100). The job timeout in the profile is 90 minutes
(`params.timeoutMinutes`).

The progress is visible in the **job console** of the task card: ComfyUI's own output,
progress bar included, is streamed there.

## Generation parameters

Edited in the model profile (the “Connection profile” button):

| Parameter | Default | Meaning |
|---|---|---|
| `width` / `height` | 1024 × 1024 | frame size (the model is also trained for 1280×768) |
| `steps` | 50 | diffusion steps; below 30 the quality drops noticeably |
| `negative` | empty | negative prompt — it does work (`cfg 3.5`) |
| `timeoutMinutes` | 90 | how long to wait for the result |

The whole task description goes into the prompt. Put the text that must appear in the
frame **in quotes** — that is how the model tells a caption from a description.

## LoRA training

**Applying — yes; training from AI2P — no.**

A ready adapter is attached as in every ComfyUI record: the `LoraLoaderModelOnly` node is
inserted into the graph on the fly, the file is taken from
`<model repository>/loras`.

Training an adapter from AI2P, however, is not possible:
[musubi-tuner](https://github.com/kohya-ss/musubi-tuner) supports only the Kandinsky 5
VIDEO models and states in its own documentation (`docs/kandinsky5.md`) that the Image
Lite models are not supported. That is why the record carries
`lora.train.kind = external`: the training button refuses at once instead of failing half
an hour into the run. Should a trainer appear, filling in the `lora.train` section of the
model profile is enough — no code change needed.

Training an adapter outside AI2P (for example with the original scripts at
<https://github.com/kandinskylab/kandinsky-5>) and dropping the file into `loras` works
fine: it will be applied as usual.

## Common errors

* **Cyrillic in the frame still comes out mangled** — raise `steps` and put the caption in
  quotes; very long phrases fail on every open model.
* **“The model is not installed”** — the files are not fully downloaded; open “Install”
  and the window will show what is left.
* **The LoRA training button refuses** — that is intended, there is no trainer for Image
  Lite (see above).
* **Out of VRAM** — reduce `width`/`height`.
* **ComfyUI is busy with someone else's process** — AI2P shuts down only the server it
  started itself; another ComfyUI already running on 8188 is left alone.

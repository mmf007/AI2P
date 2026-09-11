# Qwen-Image-Edit-2511

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`,
model `qwen_image_edit_2511`
**API key:** not needed — the server runs locally without authentication
**What it does:** image + instruction → edited image, skills `image-edit` 90,
`image-text` 90, `image-inpaint` 85, `image-generate` 82

An **image editing** model by Alibaba, revision 2511. Unlike `Qwen-Image-2512`, which
draws from scratch, this one takes a **ready picture** and carries out an instruction
written in plain words: “replace the leather of the sofa with fur”, “remove the wires from
the sky”, “make the sign read in Russian”, “repaint the wall blue”. It is the only entry
in the catalogue that covers the `image-edit` and `image-inpaint` skills.

It runs through **ComfyUI**: AI2P starts it as a local server, uploads the source picture,
sends the workflow and picks up the finished `.png` into the task artifacts.

## No key, but an installation

Everything is installed with the **“Install”** button in the model form:
**Settings → Catalogs → Models → Qwen-Image-Edit-2511 → “Install”**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **three weight files** into the model repository (`storage.modelsRepo`), group
  `Qwen-Image`:

| File | Size | Where |
|---|---|---|
| `qwen_image_edit_2511_fp8mixed.safetensors` | ~19.1 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8.7 GiB | `text_encoders` |
| `qwen_image_vae.safetensors` | ~242 MiB | `vae` |

**About 30 GB of download in total.** The `Qwen-Image` group is shared with the
`Qwen-Image-2512` model: if that one is already installed, only the model's own weights
(~19 GiB) are downloaded.

Until the files are in place the model **cannot be active** — this is also checked when
the application starts.

## Licence

| | |
|---|---|
| Model weights | **Apache 2.0** |
| Commercial use | allowed |
| What is required | keep the licence text and the copyright notice |
| Licence text | <https://huggingface.co/Comfy-Org/Qwen-Image-Edit_ComfyUI> (model card) |
| Payment per generation | none — your own graphics card does the work |

The licence was verified against the model card on 2026-08-27. Note that it is the
licence of the **weights** that is free, not of the pictures you edit — the rights to the
source image remain a question of where you took it from.

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P runs it as an external
program and talks to it over HTTP, so that licence does not spread to your product — but
if you hand someone a build with ComfyUI inside, the GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| Graphics card | NVIDIA, **16 GB VRAM or more** (24 GB is comfortable) |
| NVIDIA driver | **580 or newer** — on an older driver torch dies without a message |
| Disk space | ~32 GB (weights plus ComfyUI); with `Qwen-Image-2512` installed — ~21 GB |
| RAM | 32 GB or more |
| OS | Windows (portable ComfyUI build); on Linux ComfyUI is installed by hand |

## Where the source picture comes from

The same way as for `Kandinsky-5.0-I2V-Lite-5s`: the path to the file **relative to the
project folder** is written in the task description, the connector uploads the file to
ComfyUI and substitutes its name into the workflow. The simplest way is to reference a
project object — `@obj:OBJ-3`: an object of the “reference frame” kind already names its
file, and it goes into the job on its own.

The source image is **required** here (`refImage.required`): without a picture the job
does not start at all, rather than producing an empty result.

The result size is defined by the **source picture itself** — the `FluxKontextImageScale`
node fits it to the nearest allowed size. The `width`/`height` profile fields do not
affect editing (they are kept for the job summary and for the LoRA training dataset).

## Generation parameters

Edited in the model profile (the “Connection profile” button):

| Parameter | Default | Meaning |
|---|---|---|
| `steps` | 40 | diffusion steps; fewer is faster and coarser |
| `negative` | empty | negative prompt (it does work here, `cfg = 3`) |
| `timeoutMinutes` | 90 | how long to wait for the result |
| `width` / `height` | 1328 × 1328 | do not affect editing, see above |

The whole task description goes into the prompt. Write **what to change**, not what is
depicted: “replace the background with an evening city” works better than a full
description of the scene.

## LoRA training

Fully supported: an adapter is both **applied** (the `LoraLoaderModelOnly` node is
inserted into the graph on the fly) and **trained** from AI2P itself — with the LoRA
editor in the project object card.

Training is done by [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — the same
scripts as for `Qwen-Image-2512`, but with the `--model_version edit-2511` option: it
tells the trainer that the model has a control image, and the prompts are cached together
with it.

**The trainer needs different weight files.** The fp8 builds used by generation cannot be
used for training, so on the first run `train.cmd` downloads the **bf16** pair once:
`qwen_image_edit_2511_bf16.safetensors` (~38 GiB) and `qwen_2.5_vl_7b.safetensors`
(~15.4 GiB), into the `train` subfolder of the `Qwen-Image` group. That is about **57 GB
on top of the installation**; generation keeps running on the light fp8 files.

The dataset in the LoRA editor is images with captions. For an editing model this trains
the **style of the result**, not before/after pairs: there are no control images in the
LoRA editor yet, and that is an honest limitation rather than a setting.

The finished adapter is placed into
`<model repository>/loras/<object code>.safetensors`. All training keys, `dataset.toml`
and `train.cmd` live in the model profile (`lora.train`) and are rewritten before every
run — edit them in the profile, not in the files.

## Common errors

* **“The model requires a source image”** — the task description contains neither a file
  path nor an `@obj:` reference to an object that has a file.
* **The process disappears without a message** — almost always an old NVIDIA driver
  (see above).
* **“The model is not installed”** — the files are not fully downloaded; open “Install”
  and the window will show what is left.
* **The edit “ignored” the instruction** — make the instruction smaller: one change per
  job works more reliably than a list of five.
* **ComfyUI is busy with someone else's process** — AI2P shuts down only the server it
  started itself; another ComfyUI already running on 8188 is left alone.

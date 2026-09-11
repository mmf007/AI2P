# Hunyuan3D-2.1

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `hunyuan3d_2_1`
**API key:** not needed — the server runs locally without authentication
**What it does:** image → 3D model, skill `3d-image` 86

An open model by Tencent that turns ONE picture into a three-dimensional mesh. It is the
first local catalogue entry to cover 3D: before it the `3d-*` skills belonged only to the
cloud entries Tripo and Meshy.

It runs through **ComfyUI**: AI2P starts it as a local server, sends the workflow and
picks up the finished `.glb` into the task artifacts.

**It reads the PICTURE, not the text.** There is no text encoder in the graph at all — the
conditioning comes from CLIP Vision over the starting frame. The model never sees the task
description: what is drawn in the picture is what you get. The starting frame is named in
the task description by a project object reference (`@obj:OBJ-3`) or by a file path
relative to the project folder; without it the job will not start at all.

**There will be no colour.** ComfyUI only computes the SHAPE branch of Hunyuan3D
(`VAEDecodeHunyuan3D` → `VoxelToMesh` → `SaveGLB`). The painting branch from the Tencent
repository — the one that needs Linux, CUDA 12.4 and its own CUDA rasteriser — is not
implemented in the engine and cannot be added with the stock package. If you need a
coloured mesh, take [TRELLIS-2](https://huggingface.co/microsoft/TRELLIS.2-4B), it is the
neighbouring catalogue entry.

## No key, but an installation

Everything is installed with the **“Install”** button in the model form:
**Settings → Catalogs → Models → Hunyuan3D-2.1 → “Install”**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **one weight file** into the model repository (`storage.modelsRepo`), group
  `Hunyuan3D-2.1`:

| File | Size | Where |
|---|---|---|
| `hunyuan_3d_v2.1.safetensors` | ~6.86 GiB | `checkpoints` |

**About 7.4 GB of download in total.** It resumes: an interrupted installation continues
from where it stopped, files already downloaded are not fetched again.

Until the files are in place the model **cannot be active** — this is also checked when
the application starts.

## Licence

| | |
|---|---|
| Model weights | **Tencent Hunyuan 3D 2.1 Community License** (not an open licence in the usual sense) |
| Commercial use | allowed, with reservations — see below |
| Where it does not apply | the European Union, the United Kingdom and South Korea are excluded from the licensed territory |
| Threshold | more than 1 million monthly active users — a separate licence from Tencent is required |
| What is mandatory | mark the product “Powered by Tencent Hunyuan” and ship a Notice file with any distribution to third parties |
| Licence text | <https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1/blob/main/LICENSE> |
| Payment per generation | none — your graphics card does the work |

The files AI2P downloads are the Comfy-Org repack
(<https://huggingface.co/Comfy-Org/hunyuan3D_2.1_repackaged>); it carries the same Tencent
licence, not MIT. The licence was verified against the repository text on 2026-08-27.
These terms are noticeably stricter than the Apache 2.0 of the other local models in the
catalogue: **read the whole licence before a commercial release.**

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P starts it as an external
program and talks to it over HTTP, so that licence does not spread to your product — but
if you hand someone a build with ComfyUI inside, the GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| Graphics card | NVIDIA, **from 8 GB VRAM** (12 GB is comfortable) |
| NVIDIA driver | **580 or newer** — on an older driver torch dies without a message |
| Disk space | ~10 GB (weights plus the ComfyUI package) |
| RAM | from 16 GB |
| OS | Windows (the portable ComfyUI build); on Linux ComfyUI is installed by hand |

The video memory figure is an **estimate**, not a measurement: only the shape branch is
computed, and the “10–29 GB VRAM” requirement found in reviews of Hunyuan3D 2.1 refers to
the full Tencent pipeline including texture baking. Out of memory — lower
`octree_resolution` in the workflow template.

## How long to wait

A few minutes per model on a modern card. The job timeout in the profile is set to
60 minutes (`params.timeoutMinutes`).

The progress is visible in the **job console** in the task card: ComfyUI's own output,
progress bar included, is streamed there.

## Generation parameters

Edited in the model profile (the “Connection profile” button):

| Parameter | Default | Meaning |
|---|---|---|
| `steps` | 30 | diffusion steps; below 20 the shape falls apart |
| `timeoutMinutes` | 60 | how long to wait for the result |
| `width` / `height` | 1024 | do NOT affect the result: 3D has no frame |
| `negative` | empty | has no effect — there is no text encoder in the graph |

Mesh density is set by the workflow template rather than the profile:
`octree_resolution` on `VAEDecodeHunyuan3D` (256) and `threshold` on `VoxelToMesh` (0.6).

## LoRA training

**Not supported, and that is an honest refusal rather than an omission.** There is no
public LoRA trainer for 3D architectures today:
[musubi-tuner](https://github.com/kohya-ss/musubi-tuner), on which all adapter training in
AI2P rests, only handles images and video. Hence `lora.supported: false` in the profile,
and the model form spells the reason out.

Consistency of an object across frames is achieved differently here: feed the same
reference picture of the character (a project object, the `@obj:` reference) — the same
picture and the same `seed` produce the same mesh.

## Common mistakes

* **“The model is not installed”** — the file is not fully downloaded; open “Install” and
  the window will show what is left.
* **The job refuses to start without a picture** — by design: the model only works from an
  image. Give an `@obj:` reference to a reference frame, or a file path.
* **The model came out white** — this entry never produces colour (see the top).
* **Out of VRAM** — lower `octree_resolution` in the workflow template.
* **The process disappears without a message** — almost always an old NVIDIA driver (see
  above).

# TRELLIS-2

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `trellis_2`
**API key:** not needed — the server runs locally without authentication
**What it does:** image → 3D model WITH COLOUR, skill `3d-image` 85

An open model by Microsoft Research (4 billion parameters) that turns ONE picture into a
three-dimensional mesh. It is the only one of the three local 3D catalogue entries that
produces not just shape but colour as well: a separate texture stage follows the shape one.

It runs through **ComfyUI**: AI2P starts it as a local server, sends the workflow and
picks up the finished `.glb` into the task artifacts.

**It reads the PICTURE, not the text.** There is no text encoder in the graph at all — the
conditioning comes from DINOv3 over the starting frame. The model never sees the task
description: what is drawn in the picture is what you get. The starting frame is named in
the task description by a project object reference (`@obj:OBJ-3`) or by a file path
relative to the project folder; without it the job will not start at all.

The background is removed from the starting frame automatically (BiRefNet) and the frame
is cropped to the object: 3D from a picture with a background comes out noticeably worse.

## No key, but an installation

Everything is installed with the **“Install”** button in the model form:
**Settings → Catalogs → Models → TRELLIS-2 → “Install”**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **five weight files** into the model repository (`storage.modelsRepo`), group
  `TRELLIS-2`:

| File | Size | Where |
|---|---|---|
| `trellis_2_int8_convrot.safetensors` | ~4.89 GiB | `diffusion_models` |
| `dino_v3_vit_l.safetensors` | ~1.13 GiB | `clip_vision` |
| `trellis_2_shape_vae_bf16.safetensors` | ~1.02 GiB | `vae` |
| `trellis_2_texture_vae_bf16.safetensors` | ~904 MiB | `vae` |
| `birefnet.safetensors` | ~424 MiB | `background_removal` |

**About 9.0 GB of download in total** (8.34 GiB). It resumes: an interrupted installation continues
from where it stopped, files already downloaded are not fetched again.

The weights are taken in the **int8** build — half the weight of bf16 (~9.6 GiB) and meant
for a consumer card. If you need maximum precision, change `unet_name` in the workflow
template to `trellis_2_bf16.safetensors` and add that file to the manifest yourself.

Until the files are in place the model **cannot be active** — this is also checked when
the application starts.

## Licence

| | |
|---|---|
| Model weights | **MIT** |
| Commercial use | allowed |
| What is mandatory | keep the licence text and the copyright notice |
| Licence text | <https://huggingface.co/microsoft/TRELLIS.2-4B> (the model card) |
| Payment per generation | none — your graphics card does the work |

The files AI2P downloads are the Comfy-Org repack
(<https://huggingface.co/Comfy-Org/TRELLIS.2>), released under the same MIT. Background
removal is done by BiRefNet (<https://huggingface.co/ZhengPeng7/BiRefNet>), MIT as well.
The licences were verified against the model cards on 2026-08-27; open models rarely
change them, but check the cards once more before a commercial release.

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P starts it as an external
program and talks to it over HTTP, so that licence does not spread to your product — but
if you hand someone a build with ComfyUI inside, the GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| Graphics card | NVIDIA, **from 12 GB VRAM** (16 GB is comfortable) |
| NVIDIA driver | **580 or newer** — on an older driver torch dies without a message |
| Disk space | ~13 GB (weights plus the ComfyUI package) |
| RAM | from 16 GB |
| OS | Windows (the portable ComfyUI build); on Linux ComfyUI is installed by hand |

The video memory figure is an **estimate**, not a measurement: it is derived from the size
of the int8 weights and the two VAEs. Out of memory — lower `target_resolution` on
`Trellis2UpsampleStage` in the workflow template (1536 by default).

## How long to wait

The graph has four stages — structure, shape, upsampling and texture — so a single model
takes noticeably longer than with Hunyuan3D: minutes to tens of minutes on a modern card.
The job timeout in the profile is set to 90 minutes (`params.timeoutMinutes`).

The progress is visible in the **job console** in the task card: ComfyUI's own output,
progress bar included, is streamed there.

## Generation parameters

Edited in the model profile (the “Connection profile” button):

| Parameter | Default | Meaning |
|---|---|---|
| `steps` | 20 | steps of the SHAPE stage — the main quality lever |
| `timeoutMinutes` | 90 | how long to wait for the result |
| `width` / `height` | 1024 | do NOT affect the result: 3D has no frame |
| `negative` | empty | has no effect — there is no text encoder in the graph |

The other three stages run with the step counts from the official template (12); changing
them means editing the workflow itself — they change the result little and cost just as
much time.

**What the template deliberately leaves out.** After the mesh, the official ComfyUI
template also does a UV unwrap and bakes a full set of PBR maps (`UnwrapMesh` →
`BakeTextureFromVoxel`, `BakeNormalMapFromMesh`, `BakeAmbientOcclusion` →
`ApplyTextureToMesh`). Our template has no such branch: that is another ten nodes and
baking at 2048–4096, and there is no way to verify them without a graphics card. The
colour is still there — it is carried by the mesh vertices (`PaintMesh`). If you need PBR
maps, add the branch to your own workflow: ComfyUI has all of those nodes.

## LoRA training

**Not supported, and that is an honest refusal rather than an omission.** There is no
public LoRA trainer for 3D architectures today:
[musubi-tuner](https://github.com/kohya-ss/musubi-tuner), on which all adapter training in
AI2P rests, only handles images and video. Hence `lora.supported: false` in the profile,
and the model form spells the reason out.

Consistency of an object across frames is achieved differently here: feed the same
reference picture of the character (a project object, the `@obj:` reference) — the same
picture and the same `seed` produce the same model.

## Common mistakes

* **“The model is not installed”** — the files are not fully downloaded; open “Install”
  and the window will show what is left.
* **The job refuses to start without a picture** — by design: the model only works from an
  image. Give an `@obj:` reference to a reference frame, or a file path.
* **Out of VRAM** — lower `target_resolution` on `Trellis2UpsampleStage`.
* **The object came out cropped** — background removal picked the wrong subject; give a
  frame where the object you want is alone and fully inside the frame.
* **The process disappears without a message** — almost always an old NVIDIA driver (see
  above).

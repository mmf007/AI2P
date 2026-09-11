# TripoSplat

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `triposplat`
**API key:** not needed — the server runs locally without authentication
**What it does:** image → GAUSSIAN SPLATS, skill `3d-image` 80

An open model by Tripo AI (VAST-AI) that turns ONE picture not into a mesh but into a
cloud of three-dimensional gaussians — **gaussian splats**. It is the only catalogue entry
producing that result: a `.spz` file that splat viewers, Unreal, Unity and web players
such as Babylon.js can open.

Splats are NOT a mesh: they have no polygons and no UVs, so they go into a game pipeline
as they are or are converted separately. In exchange they give a photographic picture with
soft edges and transparency where a mesh looks crude (foliage, fur, smoke, interiors).

It runs through **ComfyUI**: AI2P starts it as a local server, sends the workflow and
picks up the finished `.spz` into the task artifacts.

**It reads the PICTURE, not the text.** There is no text encoder in the graph at all — the
conditioning comes from DINOv3 over the starting frame. The model never sees the task
description: what is drawn in the picture is what you get. The starting frame is named in
the task description by a project object reference (`@obj:OBJ-3`) or by a file path
relative to the project folder; without it the job will not start at all.

The background is removed from the starting frame automatically (BiRefNet).

## No key, but an installation

Everything is installed with the **“Install”** button in the model form:
**Settings → Catalogs → Models → TripoSplat → “Install”**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **five weight files** into the model repository (`storage.modelsRepo`), group
  `TripoSplat`:

| File | Size | Where |
|---|---|---|
| `dino_v3_vit_h.safetensors` | ~1.57 GiB | `clip_vision` |
| `triposplat_fp16.safetensors` | ~707 MiB | `diffusion_models` |
| `triposplat_vae_decoder_fp16.safetensors` | ~549 MiB | `vae` |
| `flux2-vae.safetensors` | ~321 MiB | `vae` |
| `birefnet.safetensors` | ~424 MiB | `background_removal` |

**About 3.8 GB of download in total** — the lightest of the local 3D catalogue entries.
It resumes: an interrupted installation continues from where it stopped, files already
downloaded are not fetched again.

Until the files are in place the model **cannot be active** — this is also checked when
the application starts.

## Licence

| | |
|---|---|
| Model weights | **MIT** |
| Commercial use | allowed |
| What is mandatory | keep the licence text and the copyright notice |
| Licence text | <https://huggingface.co/VAST-AI/TripoSplat> (the model card) |
| Payment per generation | none — your graphics card does the work |

Background removal is done by BiRefNet (<https://huggingface.co/ZhengPeng7/BiRefNet>), MIT
as well; the frame encoder is DINOv3 (Meta) and lives in the same VAST-AI repository under
the same licence. The licences were verified against the model cards on 2026-08-27; open
models rarely change them, but check the cards once more before a commercial release.

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P starts it as an external
program and talks to it over HTTP, so that licence does not spread to your product — but
if you hand someone a build with ComfyUI inside, the GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| Graphics card | NVIDIA, **from 8 GB VRAM** (12 GB is comfortable) |
| NVIDIA driver | **580 or newer** — on an older driver torch dies without a message |
| Disk space | ~6 GB (weights plus the ComfyUI package) |
| RAM | from 16 GB |
| OS | Windows (the portable ComfyUI build); on Linux ComfyUI is installed by hand |

The video memory figure is an **estimate**, not a measurement. Out of memory — lower
`num_gaussians` on `VAEDecodeTripoSplat` in the workflow template (262144 by default).

## How long to wait

A few minutes per model on a modern card: the model itself is small (about 700 MB of
weights) and there is one stage rather than four as in TRELLIS-2. The job timeout in the
profile is set to 60 minutes (`params.timeoutMinutes`).

The progress is visible in the **job console** in the task card: ComfyUI's own output,
progress bar included, is streamed there.

## Generation parameters

Edited in the model profile (the “Connection profile” button):

| Parameter | Default | Meaning |
|---|---|---|
| `steps` | 20 | diffusion steps |
| `timeoutMinutes` | 60 | how long to wait for the result |
| `width` / `height` | 1024 | do NOT affect the result: 3D has no frame |
| `negative` | empty | has no effect — there is no text encoder in the graph |

The number of gaussians (`num_gaussians`, 262144) and the file format (`spz`) are set by
the workflow template. The formats the `SplatToFile3D` node can write are `spz`, `ply`,
`splat` and `ksplat`; `spz` was chosen as the most compact one.

**What the template deliberately leaves out.** The official ComfyUI template additionally
renders a camera orbit around the object and saves it as a video (`RenderSplat` →
`CreateVideo` → `SaveVideo`). We did not carry that branch over: it is a separate render
on every generation, and the job result is the splat file anyway. The same template also
holds `SplatToMesh`, a node that converts splats into an ordinary mesh; if you need a
mesh, add it to your own workflow or take
[TRELLIS-2](https://huggingface.co/microsoft/TRELLIS.2-4B).

## LoRA training

**Not supported, and that is an honest refusal rather than an omission.** There is no
public LoRA trainer for 3D architectures today:
[musubi-tuner](https://github.com/kohya-ss/musubi-tuner), on which all adapter training in
AI2P rests, only handles images and video. Hence `lora.supported: false` in the profile,
and the model form spells the reason out.

Consistency of an object across frames is achieved differently here: feed the same
reference picture of the character (a project object, the `@obj:` reference) — the same
picture and the same `seed` produce the same result.

## Common mistakes

* **“The model is not installed”** — the files are not fully downloaded; open “Install”
  and the window will show what is left.
* **The job refuses to start without a picture** — by design: the model only works from an
  image. Give an `@obj:` reference to a reference frame, or a file path.
* **The `.spz` file will not open in the usual program** — those are splats, not a mesh;
  you need a gaussian splat viewer or a conversion.
* **Out of VRAM** — lower `num_gaussians` in the workflow template.
* **The process disappears without a message** — almost always an old NVIDIA driver (see
  above).

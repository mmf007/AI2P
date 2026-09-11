# HunyuanVideo-1.5-720p-T2V

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `hunyuanvideo15_720p_t2v`
**API key:** not needed — the server runs locally without authentication
**What it does:** text → video, skill `video-generate` 79

A video model by Tencent, version **1.5**. Of the three large open lines in the catalogue
(Wan 2.2, HunyuanVideo 1.5, LTX-2.5) this one is the least demanding on video memory and
the only one that **computes in 720p out of the box** rather than in 480p.

The second text encoder (`byt5_small_glyphxl`) is responsible for **text inside the
frame** — if the clip needs readable lettering, that is a visible difference; hence the
`DualCLIPLoader` in the graph instead of an ordinary single-encoder loader.

It runs through **ComfyUI**: AI2P starts it as a local server, sends the workflow and
picks up the finished `.mp4` into the task artifacts.

## No key, but an installation

Everything is installed with the **“Install”** button in the model form:
**Settings → Catalogs → Models → HunyuanVideo-1.5-720p-T2V → “Install”**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **four weight files** into the model repository (`storage.modelsRepo`), group
  `HunyuanVideo-1.5`:

| File | Size | Where |
|---|---|---|
| `hunyuanvideo1.5_720p_t2v_fp16.safetensors` | ~15.5 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8.7 GiB | `text_encoders` |
| `byt5_small_glyphxl_fp16.safetensors` | ~418 MiB | `text_encoders` |
| `hunyuanvideo15_vae_fp16.safetensors` | ~2.3 GiB | `vae` |

**About 28.6 GB of download in total.** It resumes: an interrupted installation continues
from where it stopped.

Until the files are in place the model **cannot be active** — this is also checked when
the application starts.

## Licence

| | |
|---|---|
| Model weights | **Tencent Hunyuan Community License** (neither Apache nor MIT) |
| Commercial use | allowed, but with the rights holder's conditions |
| What is required | accept the licence and keep the notice; the licence limits the number of users of your product and the territories |
| Licence text | <https://github.com/Tencent-Hunyuan/HunyuanVideo-1.5/blob/master/LICENSE> |
| Model card | <https://huggingface.co/tencent/HunyuanVideo-1.5> |
| Cost per generation | none — your graphics card does the work |

This is **not** a free licence in the Apache/MIT sense: read its full text before a
commercial release, it holds conditions that Wan 2.2 and Kandinsky do not have. The
licence was checked against the model card on 2026-08-27 (`license_name:
tencent-hunyuan-community`).

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P starts it as an external
program and talks to it over HTTP, so that licence does not propagate to your product —
but if you ship a build with ComfyUI inside it, the GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| Graphics card | NVIDIA, **12 GB VRAM or more** (16 GB is comfortable) |
| NVIDIA driver | **580 or newer** — on an older driver torch dies without a message |
| Disk space | ~31 GB (weights plus the ComfyUI package), plus ~50 GB if you train LoRA |
| RAM | 32 GB or more |
| OS | Windows (the portable ComfyUI build); on Linux ComfyUI is installed by hand |

If there is not enough video memory, the `weight_dtype` of the loader node can be switched
to `fp8_e4m3fn` in the profile — the official ComfyUI template advises exactly that.

## How long to wait

A 1280×720 clip of 121 frames (5 seconds at 24 frames per second) takes **tens of
minutes** on a modern card (20 steps). The job timeout in the profile is set to 240
minutes (`params.timeoutMinutes`). The progress is visible in the **job console** of the
task card.

## Generation parameters

| Parameter | Default | Meaning |
|---|---|---|
| `width` / `height` | 1280 × 720 | frame size |
| `length` | 121 | frames in the clip; 24 frames per second, that is 5 seconds |
| `steps` | 20 | diffusion steps |
| `negative` | empty | negative prompt (the guidance in the template is 6) |
| `timeoutMinutes` | 240 | how long to wait for the result |

The official ComfyUI template also holds an upscale to 1080p and the EasyCache
accelerator next to the base generation — they are **disabled** there and are not carried
over here: those are separate weight files, which the manifest does not have.

The whole task description goes into the prompt. An instruction like “put the result into
X.mp4” is carried out by the connector: the file is copied into the project folder and the
line itself is cut out of the prompt.

## LoRA training

Fully supported: the adapter is both **applied** (the `LoraLoaderModelOnly` node is
inserted into the graph on the fly) and **trained** from AI2P itself — with the LoRA
editor in the project object card.

Training is done by [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — the scripts
`hv_1_5_cache_latents.py`, `hv_1_5_cache_text_encoder_outputs.py` and
`hv_1_5_train_network.py` (`--task t2v`, `--network_module networks.lora_hv_1_5`), a
single graphics card, attention through `sdpa`.

The `musubi-tuner` package (sources, ~30 MB) and **Python 3.12** (~45 MB) are installed
together with the model; the `torch` environment (~3 GB) the trainer creates for itself on
the first training run.

**What to know in advance.** Generation runs on the fp16 repack and on the lighter fp8
text encoder — the trainer accepts neither
([its documentation](https://github.com/kohya-ss/musubi-tuner/blob/main/docs/hunyuan_video_1_5.md)
asks directly for the original DiT and the full encoder). So on the first training run
`train.cmd` fetches the original `diffusion_pytorch_model.safetensors` (~31 GB) and the
full `qwen_2.5_vl_7b.safetensors` (~15.5 GB) into the `train` subdirectory of the
`HunyuanVideo-1.5` group, once. That is ~50 GB on top of the installation — plan the disk
space. The VAE and the lettering encoder (`byt5`) are taken from the installation as they
are.

The finished adapter is placed into
`<model repository>/loras/<object code>.safetensors`. All training switches, `dataset.toml`
and `train.cmd` live in the model profile (`lora.train`) and are rewritten before every
run — edit them in the profile, not in the files.

## Common problems

* **Out of VRAM** — switch `weight_dtype` to `fp8_e4m3fn` in the workflow template or
  reduce `width`/`height`.
* **“The model is not installed”** — the files are not fully downloaded; open “Install”,
  the window shows the remaining volume.
* **The process disappears without a message** — almost always an old NVIDIA driver.
* **ComfyUI is busy with someone else's process** — AI2P shuts down only the server it
  started itself; an already running foreign ComfyUI on 8188 is left alone.

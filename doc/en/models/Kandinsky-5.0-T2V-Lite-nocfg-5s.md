# Kandinsky-5.0-T2V-Lite-nocfg-5s

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `kandinsky5lite_t2v_nocfg_5s`
**API key:** not needed — the server runs locally without authentication
**What it does:** text → video, skill `video-generate` 74

A variant of the open Russian video model **Kandinsky 5.0 Video Lite** (2 billion
parameters) by Kandinsky Lab. It understands a Russian prompt and draws **Cyrillic inside
the frame** — no other model in the catalogue does that.

The **no-CFG** variant: the same 50 steps, but the model is trained to work without the
second pass over the negative prompt, so it computes twice as fast as the original.
The price is slightly weaker adherence to a complicated description.

The clip length is **5 seconds** (121 frames at 24 frames per second): the
length is baked into the weights, which is why the line ships separate files for 5 and for
10 seconds.

It runs through **ComfyUI**: AI2P starts it as a local server, sends the workflow and
picks up the finished `.mp4` into the task artifacts.

## No key, but an installation

Everything is installed with the **“Install”** button in the model form:
**Settings → Catalogs → Models → Kandinsky-5.0-T2V-Lite-nocfg-5s → “Install”**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **four weight files** into the model repository (`storage.modelsRepo`), group
  `Kandinsky-5`:

| File | Size | Where |
|---|---|---|
| `kandinsky5lite_t2v_nocfg_5s.safetensors` | ~4.3 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8.7 GiB | `text_encoders` |
| `clip_l.safetensors` | ~235 MiB | `text_encoders` |
| `hunyuan_video_vae_bf16.safetensors` | ~470 MiB | `vae` |

**About 14.6 GB of download in total** — the lightest of the video models in the
catalogue. The `Kandinsky-5` group is shared by the whole line: if another variant is
already installed, only its own weight file (~4.6 GB) is fetched, the encoders and the VAE
are already in place.

Until the files are in place the model **cannot be active** — this is also checked when
the application starts.

## Licence

| | |
|---|---|
| Model weights | **MIT** |
| Commercial use | allowed |
| What is required | keep the licence text and the copyright notice |
| Licence text | <https://huggingface.co/kandinskylab/Kandinsky-5.0-T2V-Lite-nocfg-5s> (model card) |
| Cost per generation | none — your graphics card does the work |

MIT is the freest licence among the video models of the catalogue: Wan 2.2 is Apache 2.0,
HunyuanVideo 1.5 and LTX-2.5 come with the rights holders' own agreements. The licence was
checked against the model card on 2026-08-27.

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P starts it as an external
program and talks to it over HTTP, so that licence does not propagate to your product —
but if you ship a build with ComfyUI inside it, the GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| Graphics card | NVIDIA, **12 GB VRAM or more** (24 GB is comfortable) |
| NVIDIA driver | **580 or newer** — on an older driver torch dies without a message |
| Disk space | ~17 GB (weights plus the ComfyUI package) |
| RAM | 16 GB or more |
| OS | Windows (the portable ComfyUI build); on Linux ComfyUI is installed by hand |

## How long to wait

Fifty steps, but without the second pass over the negative prompt — about twice as
fast as the original variant: **tens of minutes** on a modern card.

The job timeout in the profile is set to 180 minutes (`params.timeoutMinutes`). The
progress is visible in the **job console** of the task card: the native ComfyUI output
with its progress bar goes there.

## Generation parameters

| Parameter | Default | Meaning |
|---|---|---|
| `width` / `height` | 768 × 512 | frame size |
| `length` | 121 | frames in the clip; 24 frames per second, that is 5 s |
| `steps` | 50 | diffusion steps — as many as the training task of this variant uses |
| `negative` | empty | negative prompt — **does not work in this variant**: it is trained to compute without the second pass |
| `timeoutMinutes` | 180 | how long to wait for the result |

The whole task description goes into the prompt. An instruction like “put the result into
X.mp4” is carried out by the connector: the file is copied into the project folder and the
line itself is cut out of the prompt.

## LoRA training

Fully supported: the adapter is both **applied** (the `LoraLoaderModelOnly` node is
inserted into the graph on the fly) and **trained** from AI2P itself — with the LoRA
editor in the project object card.

Training is done by [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — the scripts
`kandinsky5_cache_latents.py`, `kandinsky5_cache_text_encoder_outputs.py` and
`kandinsky5_train_network.py` (`--task k5-lite-t2v-5s-nocfg-sd`,
`--network_module networks.lora_kandinsky`), a single graphics card, attention through
`sdpa`. Every variant has its own trainer task — it sets both the step count and the
schedule, so another one cannot be substituted.

The `musubi-tuner` package (sources, ~30 MB) and **Python 3.12** (~45 MB) are installed
together with the model; the `torch` environment (~3 GB) the trainer creates for itself on
the first training run. Unlike Wan 2.2 and HunyuanVideo 1.5, nothing has to be fetched
additionally: the DiT file is not fp8 to begin with, and the trainer takes its own text
encoders from HuggingFace.

The finished adapter is placed into
`<model repository>/loras/<object code>.safetensors`. All training switches, `dataset.toml`
and `train.cmd` live in the model profile (`lora.train`) and are rewritten before every
run — edit them in the profile, not in the files.

## Common problems

* **The process disappears without a message** — almost always an old NVIDIA driver
  (see above).
* **“The model is not installed”** — the files are not fully downloaded; open “Install”,
  the window shows the remaining volume.
* **Out of VRAM** — reduce `width`/`height`; a clip of 121 frames keeps the whole
  latent in memory.
* **ComfyUI is busy with someone else's process** — AI2P shuts down only the server it
  started itself; an already running foreign ComfyUI on 8188 is left alone.

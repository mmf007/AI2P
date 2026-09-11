# Wan-2.2-I2V-A14B

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `wan2.2_i2v_a14b`
**API key:** not needed — the server runs locally without authentication
**What it does:** image + text → video, skills `video-animate` 80, `video-generate` 74

The same open Alibaba line **Wan 2.2** as Wan-2.2-T2V-A14B, but the clip is drawn **from a
picture**: the start frame sets the character, the framing and the style, and the prompt
only describes the motion. This is the most predictable way to get a clip with the
character you need — the appearance comes from the frame instead of being retold in words.

The model is composite (MoE), two experts of 14 billion parameters: the “high noise” one
draws the first half of the steps, the “low noise” one finishes the second half.

It runs through **ComfyUI**: AI2P starts it as a local server, uploads the start frame,
sends the workflow and picks up the finished `.mp4` into the task artifacts.

## Where the start frame comes from

The task must name it — without a picture the model does not start at all (the refusal
comes at once, not after half an hour of computing). There are two ways:

* **a reference to a project object** — `@obj:OBJ-3` in the task description: the path of
  the reference frame is substituted automatically and the object passport goes into the
  prompt;
* **a file path** relative to the project folder, on a line of its own in the description.

`.png`, `.jpg` and `.webp` are accepted; the frame is scaled to the `width`×`height` of
the profile.

## No key, but an installation

Everything is installed with the **“Install”** button in the model form:
**Settings → Catalogs → Models → Wan-2.2-I2V-A14B → “Install”**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **four weight files** into the model repository (`storage.modelsRepo`), group
  `Wan-2.2-I2V`:

| File | Size | Where |
|---|---|---|
| `wan2.2_i2v_high_noise_14B_fp8_scaled.safetensors` | ~13.3 GiB | `diffusion_models` |
| `wan2.2_i2v_low_noise_14B_fp8_scaled.safetensors` | ~13.3 GiB | `diffusion_models` |
| `umt5_xxl_fp8_e4m3fn_scaled.safetensors` | ~6.3 GiB | `text_encoders` |
| `wan_2.1_vae.safetensors` | ~242 MiB | `vae` |

**About 35.6 GB of download in total.** The group is a separate one: the weights of
“text → video” and “image → video” differ, while the encoder and the VAE are the same, so
with both records installed those two files sit on the disk twice.

Until the files are in place the model **cannot be active** — this is also checked when
the application starts.

## Licence

| | |
|---|---|
| Model weights | **Apache 2.0** |
| Commercial use | allowed |
| What is required | keep the licence text and the copyright notice |
| Licence text | <https://huggingface.co/Wan-AI/Wan2.2-I2V-A14B> (model card) |
| Cost per generation | none — your graphics card does the work |

The files AI2P downloads are the Comfy-Org repack
(<https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged>), released under the same
Apache 2.0. The licence was checked against the model card on 2026-08-27.

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P starts it as an external
program and talks to it over HTTP, so that licence does not propagate to your product —
but if you ship a build with ComfyUI inside it, the GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| Graphics card | NVIDIA, **16 GB VRAM or more** (24 GB is comfortable) |
| NVIDIA driver | **580 or newer** — on an older driver torch dies without a message |
| Disk space | ~38 GB (weights plus the ComfyUI package), plus ~68 GB if you train LoRA |
| RAM | 32 GB or more |
| OS | Windows (the portable ComfyUI build); on Linux ComfyUI is installed by hand |

## How long to wait

A 832×480 clip of 81 frames (5 seconds at 16 frames per second) takes **tens of minutes**
on a modern card. The job timeout in the profile is set to 240 minutes
(`params.timeoutMinutes`). The progress is visible in the **job console** of the task card.

## Generation parameters

| Parameter | Default | Meaning |
|---|---|---|
| `width` / `height` | 832 × 480 | frame size; the start frame is fitted to it as well |
| `length` | 81 | frames in the clip; 16 frames per second, that is 5 seconds |
| `steps` | 20 | diffusion steps for both experts together |
| `negative` | empty | negative prompt |
| `timeoutMinutes` | 240 | how long to wait for the result |

**About the number of steps.** The expert boundary is written into the workflow template
as the number 10 — half of twenty. When you change `steps`, change `end_at_step` of the
first sampler and `start_at_step` of the second one in `models/workflow_….json` of the
organization directory: the connector does no arithmetic over parameters.

## LoRA training

Fully supported: the adapter is both **applied** (the `LoraLoaderModelOnly` node is
inserted into the graph on the fly) and **trained** from AI2P itself — with the LoRA
editor in the project object card.

Training is done by [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — the scripts
`wan_cache_latents.py` (with the `--i2v` switch), `wan_cache_text_encoder_outputs.py` and
`wan_train_network.py` (`--task i2v-A14B`, `--network_module networks.lora_wan`). The
adapter is trained on both experts at once: `--dit` and `--dit_high_noise`.

The `musubi-tuner` package and **Python 3.12** are installed together with the model; the
`torch` environment (~3 GB) the trainer creates for itself on the first training run.

**What to know in advance.** Generation runs on the `fp8_scaled` builds, and the trainer
does not accept them
([its documentation says so directly](https://github.com/kohya-ss/musubi-tuner/blob/main/docs/wan.md)),
so on the first training run `train.cmd` fetches its own **fp16** pair (~57 GB) and the
original text encoder `models_t5_umt5-xxl-enc-bf16.pth` (~11 GB) into the `train`
subdirectory of the `Wan-2.2-I2V` group, once. That is ~68 GB on top of the installation.

The finished adapter is placed into
`<model repository>/loras/<object code>.safetensors`. All training switches, `dataset.toml`
and `train.cmd` live in the model profile (`lora.train`) and are rewritten before every
run — edit them in the profile, not in the files.

## Common problems

* **“The job needs a start frame”** — the task description holds neither an `@obj:`
  reference nor a picture path; for drawing from scratch take the Wan-2.2-T2V-A14B record.
* **The first frame drifts** — the start picture differs a lot in aspect ratio from
  `width`×`height`; prepare it beforehand or change the sizes in the profile.
* **Out of VRAM** — reduce `width`/`height` or `length`.
* **The process disappears without a message** — almost always an old NVIDIA driver.

# Kandinsky-5.0-T2V-Lite-sft-5s

**Hosting:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`,
model `kandinsky5lite_t2v_sft_5s`
**API key:** not needed — the server is started locally without authorization
**What it does:** text → video (5 seconds), skills `video-generate` 76, `video-animate` 72

A video generation model of the Kandinsky 5.0 family (the Lite variant, fine-tuned, 5
seconds). It works through **ComfyUI**: AI2P starts it as a local server, sends the workflow
and picks the finished `.mp4` up into the task artifacts.

## No key is needed, an installation is

Everything is installed with the **«Install»** button on the model form: **Settings →
Catalogs → AI models → Kandinsky-5.0-T2V-Lite-sft-5s → «Install»**. What gets installed:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **four weight files** into the model repository (`storage.modelsRepo`, by default `C:\ai`
  on Windows and `~/ai` on Linux/macOS):

| File | Size | Where |
|---|---|---|
| `Kandinsky-5.0-T2V-Lite-sft-5s.safetensors` | ~4.3 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8.7 GiB | `text_encoders` |
| `clip_l.safetensors` | ~235 MiB | `text_encoders` |
| `hunyuan_video_vae_bf16.safetensors` | ~470 MiB | `vae` |

**About 16 GB of download in total.** The download resumes: an interrupted installation
continues from where it stopped, and files already downloaded are not fetched again.

Until the files are in place the model **cannot be active** — this is also checked when the
application starts.

## Licence

| | |
|---|---|
| Model weights | **MIT** |
| Commercial use | allowed |
| What is required | keep the licence text and the copyright notice |
| Licence text | <https://huggingface.co/kandinskylab/Kandinsky-5.0-T2V-Lite-sft-5s> (model card) |
| Payment per generation | none — your own computer does the work |

The licence was taken from the model card on 2026-08-27 by a request to HuggingFace (the
`cardData.license` field), not from memory. MIT is the freest licence in the catalogue:
commercial use is allowed, and the only requirement is to keep the licence text and the
copyright notice.

Separately: **ComfyUI itself is distributed under GPL-3.0**. AI2P runs it as an external
program and talks to it over HTTP, so that licence does not spread to your product — but if
you hand someone a build with ComfyUI inside, the GPL terms apply to that build.

## Hardware requirements

| | |
|---|---|
| Graphics card | NVIDIA, **6 GB of VRAM minimum** |
| NVIDIA driver | **580 or newer** — see the warning below |
| Disk space | ~18 GB (weights + the ComfyUI package) |
| RAM | 16 GB and up |
| OS | Windows (the portable ComfyUI build); on Linux ComfyUI is installed by hand |

**About the driver — important.** The ComfyUI package ships with torch built for CUDA 13.0.
On an old driver (for example 527.99 = CUDA 12.0) it fails not with a clear error but with an
**access violation** — the process simply disappears. This is not a defect of AI2P: update
the NVIDIA driver to 580+ (verified on 610.x). If the model "silently does not start", begin
with `nvidia-smi` and the driver version.

## How long to wait

On 6 GB of VRAM, generating a **768×512 clip, 121 frames, 50 steps takes about an hour**
(verified live: 58 minutes 49 seconds). That is normal — the job timeout in the profile is set
to 180 minutes (`params.timeoutMinutes`).

Since version 1.63 the same can be set **on the executor**: the "Answer timeout, min" field on
the AI executor form. If it is filled in, it wins over the `timeoutMinutes` of the profile;
**0 — wait without a limit** (the generation runs as long as it needs; it can be interrupted
with the "stop" button). Empty — as before, the value from the profile.

The progress of the generation is visible in the **job console** on the task card: the output
of ComfyUI itself, together with its progress bar, is streamed there.

## Generation parameters

They are edited in the model profile (the «Connection profile» button):

| Parameter | Default | Meaning |
|---|---|---|
| `width` / `height` | 768 × 512 | resolution; larger is noticeably slower and needs more VRAM |
| `length` | 121 | frames (≈5 seconds) |
| `steps` | 50 | diffusion steps; fewer is faster and coarser |
| `negative` | empty | the negative prompt |
| `timeoutMinutes` | 180 | how long to wait for the result |

The whole task description goes into the prompt. An instruction of the form "put the result
into the file X.mp4" is carried out by the connector: the file is copied into the project
folder, and the line itself is cut out of the prompt.

## LoRA training

Works exactly like it does for `Kandinsky-5.0-I2V-Lite-5s` and differs in two lines of the
settings: the trainer task here is `k5-lite-t2v-5s-sd` and the weights are
`Kandinsky-5.0-T2V-Lite-sft-5s.safetensors`.

The training is done by [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — a single
GPU, `sdpa` attention, 13–16 GB of VRAM. The official
[kandinsky-5-lora-train](https://github.com/kandinskylab/kandinsky-5-lora-train) needs NCCL
and several GPUs and does not start on Windows.

The `musubi-tuner` package (sources, ~30 MB) and **Python 3.12** (~45 MB) are installed
together with the model, by the «Install» button: a Python 3.10–3.12 already present on the
machine is taken as it is and not downloaded again. The environment with `torch` (~3 GB) is
created by the trainer itself on the first training run. The trainer itself fetches the original `Qwen/Qwen2.5-VL-7B-Instruct` (~16 GB)
and `openai/clip-vit-large-patch14` (~1.7 GB) — the trimmed repacks generation uses do not
fit it.

The finished adapter lands in `<models repository>/loras/<object code>.safetensors`. Every
training switch, `dataset.toml` and `train.cmd` live in the model profile (`lora.train`) and
are rewritten before every run.

An AI2P dataset is made of **images**, so what is trained this way is an appearance adapter;
a motion adapter is trained on video, and the LoRA editor has no video dataset yet.

## Common errors

* **The process disappears without a message** — almost always an old NVIDIA driver (see
  above).
* **"The model is not installed"** — the files have not been fully downloaded; open «Install»
  and the window will show the remaining size.
* **Not enough VRAM** — reduce `width`/`height` or `length`.
* **ComfyUI is busy with a foreign process** — AI2P unloads only the server it started
  itself; an already running foreign ComfyUI on 8188 is left alone.

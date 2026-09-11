# Kandinsky-5.0-I2V-Lite-5s

**Hosting:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`,
model `kandinsky5lite_i2v_5s`
**API key:** not needed — the server is started locally without authorization
**What it does:** image + text → video (5 seconds), skills `video-animate` 78,
`video-generate` 74

The same Kandinsky 5.0 Video Lite as `Kandinsky-5.0-T2V-Lite-sft-5s`, but the clip is built
**from a picture**: the start image defines what the character and the scene look like, and
the task description says what happens in the frame. That is the whole point of this entry:
**the same character across several clips** cannot be held by words, it is held by a
reference frame.

There is **no 10-second variant of the Lite i2v model**: kandinskylab publishes only
`Kandinsky-5.0-I2V-Lite-5s` (checked against the organization's repository list on
2026-08-20). Ten seconds exist for the text-to-video `T2V-Lite` and for the Pro versions —
the latter need much heavier hardware.

## How to give it a start image

The file is looked up **in the project folder**, the path is relative. Two ways:

1. **Name it in the task description** — a line with a directive word and the file name:
   "based on `refs/hero.png`", "start image `refs/hero.png`", «взять за основу
   refs/hero.png». The line is cut out of the prompt — the file name never reaches the
   generation text.
2. **Reference a project object** — `@obj:OBJ-3` in the description. On start the reference
   expands into the character passport and the paths of its reference frames; the first path
   is the start image, and the paths themselves are removed from the prompt. This is the more
   convenient way: the passport is edited in one place and applies to every following frame.

A file named in words wins over a path coming from an object. If there is no file at all, the
job fails with a clear message: this model has nothing to do without a picture.

The picture is uploaded to ComfyUI (`POST /upload/image`) into its `input` folder under the
job name and is substituted into the `LoadImage` node of the graph; then `ImageScale` brings
it to the `width`×`height` of the frame (cropping at the center), so it is better to make the
start image close to 768×512 from the beginning.

## The same character across a series of clips

| technique | what it gives |
|---|---|
| **reference frame** (this model) | the face, the clothes and the colors hold between clips |
| **fixed `seed`** (`params.seed` in the profile) | the same "temper" of generation in every frame; empty — random, as before |
| **chaining** | save the last frame of scene N as a picture and feed it into scene N+1 |
| **verbatim passport** in the description | the appearance is repeated word for word instead of being retold |

The first frame of a series is conveniently produced by an ordinary t2i model or drawn by
hand — everything else is built from it.

## No key is needed, an installation is

Everything is installed with the **«Install»** button on the model form: **Settings →
Catalogs → AI models → Kandinsky-5.0-I2V-Lite-5s → «Install»**. What gets installed:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **four weight files** into the model repository (`storage.modelsRepo`, by default `C:\ai`
  on Windows and `~/ai` on Linux/macOS):

| File | Size | Where |
|---|---|---|
| `kandinsky5lite_i2v_5s.safetensors` | ~4.3 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8.7 GiB | `text_encoders` |
| `clip_l.safetensors` | ~235 MiB | `text_encoders` |
| `hunyuan_video_vae_bf16.safetensors` | ~470 MiB | `vae` |

**About 16 GB of download in total**, but three files out of four are **exactly the same** as
in `Kandinsky-5.0-T2V-Lite-sft-5s`, and they live in the same `Kandinsky-5` group. If the
text-to-video model is already installed, only the DiT itself is downloaded — **about 4.3
GiB**. The download resumes: an interrupted installation continues from where it stopped.

Until the files are in place the model **cannot be active** — this is also checked when the
application starts.

## Licence

| | |
|---|---|
| Model weights | **MIT** |
| Commercial use | allowed |
| What is required | keep the licence text and the copyright notice |
| Licence text | <https://huggingface.co/kandinskylab/Kandinsky-5.0-I2V-Lite-5s> (model card) |
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
| NVIDIA driver | **580 or newer** |
| Disk space | ~18 GB (weights + the ComfyUI package); next to the t2v model — about 4.3 GB more |
| RAM | 16 GB and up |
| OS | Windows (the portable ComfyUI build); on Linux ComfyUI is installed by hand |

**About the driver.** The ComfyUI package ships with torch built for CUDA 13.0. On an old
driver (for example 527.99 = CUDA 12.0) it fails not with a clear error but with an **access
violation** — the process simply disappears. This is not a defect of AI2P: update the NVIDIA
driver to 580+. If the model "silently does not start", begin with `nvidia-smi`.

## How long to wait

As long as the text variant: on 6 GB of VRAM a **768×512 clip, 121 frames, 50 steps** takes
about an hour (measured on the t2v model: 58 minutes 49 seconds; the weights and the graph
here are of the same size). The job timeout is 180 minutes (`params.timeoutMinutes`); the
"Answer timeout, min" field of the executor wins over it, 0 — wait without a limit.

The progress of the generation is visible in the **job console** on the task card.

## Generation parameters

They are edited in the model profile (the «Connection profile» button):

| Parameter | Default | Meaning |
|---|---|---|
| `width` / `height` | 768 × 512 | frame resolution; the start image is brought to it as well |
| `length` | 121 | frames (≈5 seconds) |
| `steps` | 50 | diffusion steps; fewer is faster and coarser |
| `negative` | empty | the negative prompt |
| `seed` | none | a **fixed seed**: empty (or 0) — random for every job |
| `timeoutMinutes` | 180 | how long to wait for the result |

The whole task description goes into the prompt — except for the start image directive and an
instruction of the form "put the result into the file X.mp4": both are carried out by the
connector.

## LoRA training

The model works with LoRA adapters, and **an adapter can be trained right from AI2P**: the
LoRA editor (project card → "Objects" → a character object → "Dataset and training").

**What does the training.** [musubi-tuner](https://github.com/kohya-ss/musubi-tuner): it has
a ready `k5-lite-i2v-5s-sd` task for exactly these weights, it runs on a **single** GPU and
can use `sdpa` attention. The official
[kandinsky-5-lora-train](https://github.com/kandinskylab/kandinsky-5-lora-train) does not fit
here: it starts `torchrun` and `init_device_mesh("cuda")`, that is, it needs NCCL and several
GPUs — on Windows it does not start at all.

**What gets installed on top.** The `musubi-tuner` package (sources, ~30 MB) and
**Python 3.12** (~45 MB) come together with the model — by the «Install» button, the same
place where the weights are downloaded; a Python 3.10–3.12 already present on the machine is
taken as it is and not downloaded again. On the first training run the trainer creates a
`.venv` environment next to itself and installs `torch` for CUDA 12.4 into it — that is
another **~3 GB** and ten to twenty minutes the first time. Afterwards the environment is
reused.

Two more files are fetched by the trainer itself during the first text caching — **these are
not the files generation uses**: generation works with the trimmed ComfyUI repacks, while
training needs the original Hugging Face models:

| What | Size | Where from |
|---|---|---|
| `Qwen/Qwen2.5-VL-7B-Instruct` | ~16 GB | the Hugging Face cache |
| `openai/clip-vit-large-patch14` | ~1.7 GB | the Hugging Face cache |

**What the computer needs:** Python **3.10–3.12** in `PATH` (otherwise the training stops
saying so) and an NVIDIA GPU. Training an adapter for the Lite model fits into **13–16 GB of
VRAM**; latent caching needs more — it is the hungriest step. A custom Python path is set by
the `AI2P_LORA_PYTHON` environment variable, a custom torch wheel index by
`AI2P_LORA_TORCH_INDEX` (`https://download.pytorch.org/whl/cu124` by default).

**How the training goes.** AI2P collects the dataset frames into `<project folder>/lora/<object
code>/dataset` (each frame gets a `.txt` of the same name with its caption), writes
`dataset.toml` and `train.cmd` next to them and runs the latter. The script does three steps
in a row: latent cache → text encoder output cache → the training itself. The finished adapter
lands in `<models repository>/loras/<object code>.safetensors` and becomes the current adapter
of the object — that is where generation takes it from (the `LoraLoaderModelOnly` node).

`dataset.toml` and `train.cmd` are **rewritten before every run**: they belong to the model
profile (the `lora.train.files` section), so edit them there and not on disk. The number of
steps (`lora.train.start.steps`, 2000 by default), the rank and every trainer switch are
changed in the same place.

**What this path does not give.** An AI2P dataset consists of **images**, not clips, so what is
trained this way is an **appearance** adapter (a character, a style). A **motion** adapter (like
the official `Arc-right`, `Dolly-in`) is trained on video — the LoRA editor has no video dataset
yet.

## Common errors

* **"This model needs a start image"** — the description names neither an image file nor an
  object with a reference frame.
* **"Start image file not found in the project folder"** — the path is counted from the
  project folder; leaving it (`..`, `C:\…`) is forbidden on purpose.
* **"The project has no folder on this computer"** — the project folder is set on its card and
  is per-server.
* **The character still drifts** — check that the start image is the same one and that `seed`
  is fixed; clips with different `width`/`height` diverge too.
* **The process disappears without a message** — almost always an old NVIDIA driver.
* **Not enough VRAM** — reduce `width`/`height` or `length`.
* **"cannot create the virtual environment"** during LoRA training — there is no Python
  3.10–3.12 on the machine or it is not in `PATH`; install it or point `AI2P_LORA_PYTHON` at it.

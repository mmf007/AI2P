# Wan-2.2-T2V-A14B

**Placement:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `wan2.2_t2v_a14b`
**API key:** not needed — the server runs locally without authentication
**What it does:** text → video, skill `video-generate` 80

A video model by Alibaba, the open **Wan 2.2** line (2.5 and later are closed API
products, their weights are not published). In overall clip quality it is the strongest
open model in the catalogue after LTX-2.5, and clearly stronger than Kandinsky 5.0
Video Lite.

The model is **composite** (MoE): it holds two experts of 14 billion parameters each. The
“high noise” one draws the first half of the steps — the composition and the motion — and
the “low noise” one finishes the second half: details and sharpness. That is why the
install manifest lists two weight files and the ComfyUI graph holds two samplers that
hand the latent over to each other.

It runs through **ComfyUI**: AI2P starts it as a local server, sends the workflow and
picks up the finished `.mp4` into the task artifacts.

## No key, but an installation

Everything is installed with the **“Install”** button in the model form:
**Settings → Catalogs → Models → Wan-2.2-T2V-A14B → “Install”**. It installs:

* the **ComfyUI package** (~2.1 GB) — a portable build with its own Python;
* **four weight files** into the model repository (`storage.modelsRepo`), group
  `Wan-2.2-T2V`:

| File | Size | Where |
|---|---|---|
| `wan2.2_t2v_high_noise_14B_fp8_scaled.safetensors` | ~13.3 GiB | `diffusion_models` |
| `wan2.2_t2v_low_noise_14B_fp8_scaled.safetensors` | ~13.3 GiB | `diffusion_models` |
| `umt5_xxl_fp8_e4m3fn_scaled.safetensors` | ~6.3 GiB | `text_encoders` |
| `wan_2.1_vae.safetensors` | ~242 MiB | `vae` |

**About 35.6 GB of download in total.** It resumes: an interrupted installation continues
from where it stopped, files already downloaded are not fetched again.

Until the files are in place the model **cannot be active** — this is also checked when
the application starts.

## Licence

| | |
|---|---|
| Model weights | **Apache 2.0** |
| Commercial use | allowed |
| What is required | keep the licence text and the copyright notice |
| Licence text | <https://huggingface.co/Wan-AI/Wan2.2-T2V-A14B> (model card) |
| Cost per generation | none — your graphics card does the work |

The files AI2P downloads are the Comfy-Org repack
(<https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged>), released under the same
Apache 2.0. The licence was checked against the model card on 2026-08-27; for open models
it rarely changes, but check the card once more before a commercial release.

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

The experts are loaded one after another, so only one of the two files is in memory at a
time — a 832×480 clip does fit into 16 GB of VRAM, if not quickly.

## How long to wait

A 832×480 clip of 81 frames (5 seconds at 16 frames per second) takes **tens of minutes**
on a modern card: 20 steps across two experts for every frame. The job timeout in the
profile is set to 240 minutes (`params.timeoutMinutes`).

The progress is visible in the **job console** of the task card: the native ComfyUI output
with its progress bar goes there.

## Generation parameters

They are edited in the model profile (the “Connection profile” button):

| Parameter | Default | Meaning |
|---|---|---|
| `width` / `height` | 832 × 480 | frame size, the native one for the 14B model |
| `length` | 81 | frames in the clip; 16 frames per second, that is 5 seconds |
| `steps` | 20 | diffusion steps for both experts together |
| `negative` | empty | negative prompt |
| `timeoutMinutes` | 240 | how long to wait for the result |

**About the number of steps.** The expert boundary (after which step the “high noise” one
hands the work over) is written into the workflow template as the number 10 — half of
twenty. If you change `steps`, change it as well: `end_at_step` of the first sampler and
`start_at_step` of the second one in `models/workflow_….json` of the organization
directory. The connector does no arithmetic over parameters, so the boundary is not
recomputed on its own.

The turbo branch of the official ComfyUI template (4 steps instead of 20) is not carried
over here: it needs separate Lightning-LoRA files, which the install manifest does not
have.

The whole task description goes into the prompt. An instruction like “put the result into
X.mp4” is carried out by the connector: the file is copied into the project folder and the
line itself is cut out of the prompt.

## LoRA training

Fully supported: the adapter is both **applied** (the `LoraLoaderModelOnly` node is
inserted into the graph on the fly) and **trained** from AI2P itself — with the LoRA
editor in the project object card.

Training is done by [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — the scripts
`wan_cache_latents.py`, `wan_cache_text_encoder_outputs.py` and `wan_train_network.py`
(`--task t2v-A14B`, `--network_module networks.lora_wan`), a single graphics card,
attention through `sdpa`. The adapter is trained on both experts at once: the “low noise”
one is given with `--dit`, the “high noise” one with `--dit_high_noise`.

The `musubi-tuner` package (sources, ~30 MB) and **Python 3.12** (~45 MB) are installed
together with the model by the “Install” button; a Python 3.10–3.12 already present on the
computer is taken as is. The `torch` environment (~3 GB) the trainer creates for itself on
the first training run.

**What to know in advance.** Generation runs on the `fp8_scaled` builds — they are twice
as light, and it is those that the “Install” button fetches. The trainer does not accept
such builds
([its documentation says so directly](https://github.com/kohya-ss/musubi-tuner/blob/main/docs/wan.md)),
so on the first training run `train.cmd` fetches **its own fp16 pair** (~57 GB) and the
original text encoder `models_t5_umt5-xxl-enc-bf16.pth` (~11 GB) into the `train`
subdirectory of the `Wan-2.2-T2V` group, once. That is ~68 GB on top of the installation —
plan the disk space. These files cannot be put into the manifest: the “model is installed”
flag is computed from its contents, and the model would go dark for everyone who does not
train adapters.

The finished adapter is placed into
`<model repository>/loras/<object code>.safetensors`. All training switches, `dataset.toml`
and `train.cmd` live in the model profile (`lora.train`) and are rewritten before every
run — edit them in the profile, not in the files.

## Common problems

* **The process disappears without a message** — almost always an old NVIDIA driver
  (see above).
* **“The model is not installed”** — the files are not fully downloaded; open “Install”,
  the window shows the remaining volume.
* **Out of VRAM** — reduce `width`/`height` or `length`; 81 frames at 720p on a 14B model
  already need 24 GB.
* **The clip is blurry or jerky** — check that the expert boundary was not left behind
  after a change of `steps` (see “Generation parameters”).
* **ComfyUI is busy with someone else's process** — AI2P shuts down only the server it
  started itself; an already running foreign ComfyUI on 8188 is left alone.

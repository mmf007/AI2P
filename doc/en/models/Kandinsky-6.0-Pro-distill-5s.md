# Kandinsky-6.0-Pro-distill-5s

**Hosting:** LOCAL (runs on this computer)
**Connection:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, model `Kandinsky-6.0-Pro-distill-5s`
**API key:** not needed — the server starts locally without authorisation
**What it does:** text → video (5 seconds, 864×480, 24 fps), skill `video-generate` 86

Kandinsky 6.0 Video is a family of open models by Kandinsky Lab (Sber) released in September 2026: Lite (3B parameters) and Pro (29B). This record uses the **distilled** Pro version: it renders a clip in 10 steps instead of 50 with almost no loss of quality. The clip is built from the task description alone.

## No sound

The model itself renders video together with sound (44 kHz, lip-synced speech), but the sound is decoded by the MMAudio `v1-44.pth` weights, released under the non-commercial CC-BY-NC-4.0 licence. AI2P does not install such weights, so the record produces a **clip without an audio track**. The audio part of a prompt (`<AUDCAP>…`) has no effect.

## No key is needed, an installation is

Kandinsky 6 is not in the ComfyUI core: its nodes come from the extension https://github.com/kandinskylab/kandinsky-6 (the `comfyui` folder, MIT). The "Install" button in the model form does everything:

1. package `comfyui` — the portable ComfyUI build (version **0.38 or newer** is required; update an older build);
2. package `kandinsky6` — the extension: a commit archive, `pip` dependencies into ComfyUI's embedded Python and a copy of the nodes into `ComfyUI/custom_nodes/kandinsky6`;
3. weights into the `Kandinsky-6` group of the model repository:

| File | Source | Size |
|---|---|---|
| `Kandinsky-6.0-Pro-distill-5s.safetensors` | https://huggingface.co/kandinskylab/Kandinsky-6.0-Pro-distill-5s-Diffusers | 56.2 GiB |
| `qwen_2.5_vl_7b.safetensors` | https://huggingface.co/Comfy-Org/Qwen-Image_ComfyUI | 15.4 GiB |
| `clip_l.safetensors` | https://huggingface.co/comfyanonymous/flux_text_encoders | 0.2 GiB |
| `hunyuan_video_vae_bf16.safetensors` | https://huggingface.co/Kijai/HunyuanVideo_comfy | 0.5 GiB |

About **72.3 GiB** in total. All repositories are open (`gated=false`), no HuggingFace token is needed. The model can be activated only after installation.

## Licence

Kandinsky 6.0 weights are **MIT** (card https://huggingface.co/kandinskylab/Kandinsky-6.0-Pro-distill-5s-Diffusers, checked 14.10.2026), commercial use is allowed. The ComfyUI extension is MIT too. The Qwen2.5-VL encoder is Apache 2.0. The record does not use the non-commercial MMAudio sound weights (see "No sound").

## Price

Free: the model runs on your GPU, nobody has to be paid — only electricity and disk space.

## Hardware requirements

NVIDIA GPU with CUDA, a guideline of 24 GB of video memory and 96 GB of RAM or more (ComfyUI offloads part of the weights to RAM, so less memory works, only slower). One clip takes about 15–40 minutes on an RTX 4090-class card.

## Generation parameters

Profile: `width` 864, `height` 480, `length` 121 frames (5 s at 24 fps; frame count of the form 4n+1), `steps` 10, CFG 1, euler/simple sampler — as Kandinsky Lab recommends for distilled versions. `negative` is the negative prompt of the official template. There is no LoRA training for this model (`lora.supported: false`).

## Common errors

* **"Cannot execute because node Kandinsky6… does not exist"** — the extension is not loaded: reinstall the `kandinsky6` package and restart ComfyUI; check that ComfyUI is 0.38 or newer.
* **Out of video memory** — lower `width`/`height` in the profile (multiples of 16) or use Lite instead of Pro.
* **Clip without sound** — this is intended (see "No sound").

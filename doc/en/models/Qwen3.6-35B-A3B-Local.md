# Qwen3.6-35B-A3B-Local

**Hosting:** LOCAL (runs on this computer)
**Connection:** `provider: openai-compatible`, `baseUrl: http://localhost:8080/v1`
**API key:** not needed — the local server is started without authorization
**What it does:** code and texts, skill scores 72–80

The Qwen 3.6 text model (35 billion parameters, MoE with 3 billion active) in the **Q4_K_M**
quantization, running through **llama.cpp / llama-server**. The quality is lower than that of
the cloud models, but it is **free and goes nowhere**: the data never leaves the computer. A
sensible choice for tasks that must not be given to the cloud.

## No key is needed, an installation is

Everything is installed with the **«Install»** button on the model form: **Settings →
Catalogs → AI models → Qwen3.6-35B-A3B-Local → «Install»**. What gets installed:

* the **llama.cpp package** (~640 MB) — a build with `llama-server`;
* the **weights file** `Qwen3.6-35B-A3B-UD-Q4_K_M.gguf` — **~20.6 GiB** into the model
  repository (`storage.modelsRepo`, by default `C:\ai` on Windows and `~/ai` on Linux/macOS,
  group `Qwen3.6`).

The download resumes: an interrupted installation continues, and what has already been
downloaded is not fetched again. Until the files are in place the model cannot be active.

After the installation the launch command is written into the profile automatically:

```
llama-server.exe -m <path to the .gguf> --n-cpu-moe 99 -ngl 99 -c 65536 --jinja
                 --port 8080 --alias qwen3.6-35b-a3b
```

AI2P starts this process itself when the team starts working and unloads it when the team
stops — **its own process only**; an already running foreign llama-server on 8080 is left
alone.

## Licence

| | |
|---|---|
| Model weights | **Apache 2.0** |
| Commercial use | allowed |
| What is required | keep the licence text and the copyright notice |
| Licence text | <https://huggingface.co/Qwen/Qwen3.6-35B-A3B/blob/main/LICENSE> |
| Payment per generation | none — your own computer does the work |

AI2P downloads not the original weights but a GGUF quantisation
(`unsloth/Qwen3.6-35B-A3B-GGUF`) — it is published under the same licence as the original
model (`Qwen/Qwen3.6-35B-A3B`). The licence was taken from the model card on 2026-08-27 by a
request to HuggingFace (the `cardData.license` field), not from memory: for open models it
rarely changes, but check the card again before a commercial release.

The **llama.cpp engine itself is MIT** (the `LICENSE` file of the ggml-org/llama.cpp
repository, verified on 2026-08-27), so it puts no conditions on your product either.

## Hardware requirements

| | |
|---|---|
| Disk space | **~22 GB** (weights + package) |
| RAM | **24 GB minimum**, 32 GB is comfortable |
| Graphics card | not required, but speeds things up a lot |
| VRAM | 6 GB and up gives a noticeable gain; the `--n-cpu-moe 99` switch keeps the experts in RAM and moves the rest onto the GPU |
| CPU | the more cores, the faster in CPU mode |

The model is a **MoE**: out of 35 billion parameters about 3 billion are active, so it is
noticeably faster than a dense model of the same size. But it is loaded into memory whole —
20.6 GiB of weights have to fit somewhere.

**The first start is slow:** loading the weights takes minutes. AI2P waits for the API to
become ready and shows the team member in the "starting" state — this is not a hang.

## Context and maximum output

| | |
|---|---|
| Context | 65,536 tokens (`-c 65536` in the launch command) |
| Maximum output | 32,768 tokens (`params.maxTokens`) |

This pair has been verified and it matters: with a **smaller** context the answers were cut
off with the reason `length` — the model burned the limit on reasoning and returned an empty
result. If you change one of the numbers, change the other one too.

## Common errors

* **"37529 tokens exceeds context 32768"** — the launch command has a small `-c`.
  Put `-c 65536` into the "Launch command" field of the profile and restart the work of the
  team.
* **An empty result with the reason `length`** — `params.maxTokens` is too small; set it to
  32768.
* **Not enough memory / the system starts swapping** — the model needs about 24 GB of RAM;
  close what you can, or take a smaller quantization (then you will have to create your own
  reference record with your own manifest).
* **Port 8080 is busy** — change `--port` in the launch command and `baseUrl` in the profile.

## The outdated way to install

The `install_local_model.bat/.ps1/.sh` scripts in the application directory install the same
model without the UI (for offline scenarios). They create a **separate custom record** in the
reference — do not confuse it with the built-in one. Starting with version 1.42 the regular
way is the «Install» button.

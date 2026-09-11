# Qwen3.8-27B-Local

**Hosting:** LOCAL (runs on this computer)
**Connection:** `provider: openai-compatible`, `baseUrl: http://localhost:8081/v1`,
model `qwen3.8-27b`
**API key:** not needed — the local server is started without authorization
**What it does:** code and texts, skill scores 80–87

The best "one graphics card" option of the reference: 27 billion parameters, the
**UD-Q4_K_XL** quantization, running through **llama.cpp / llama-server**. On code (the score
is 87) it beats Qwen3.6-27B-Local and
Muse-Glimmer-30B-Local, and there is nothing to pay at all: **the
data never leaves the computer**. A sensible choice for tasks that must not be given to the
cloud.

## No key is needed, an installation is

Everything is installed with the **«Install»** button on the model form: **Settings →
Catalogs → AI models → Qwen3.8-27B-Local → «Install»**. What gets installed:

* the **llama.cpp package** (~0.5 GB) — a build with `llama-server`;
* the **weights file** `Qwen3.8-27B-UD-Q4_K_XL.gguf` — **16.7 GiB** (17,923,394,624 bytes)
  into the model repository (`storage.modelsRepo`, by default `C:\ai` on Windows and `~/ai` on
  Linux/macOS, group `Qwen3.8`).

The download resumes: an interrupted installation continues, what has already been downloaded
is not fetched again, and what was downloaded is checked against the exact size. **Until the
files are in place the model cannot be active** — that is an AI2P rule, not an error.

After the installation the launch command is written into the profile:

```
llama-server.exe -m <path to the .gguf> -ngl 99 -c 65536 --jinja --port 8081 --alias qwen3.8-27b
```

Port **8081** belongs to this record: the neighbouring local models use 8082 and 8083,
otherwise a second `llama-server` would not start next to the first. AI2P starts this process
itself when the team starts working and unloads it when the team stops — **its own process
only**; a foreign llama-server on the same port is left alone.

## Licence

| | |
|---|---|
| Model weights | **Apache 2.0** |
| Commercial use | allowed |
| What is required | keep the licence text and the copyright notice |
| Licence text | <https://huggingface.co/Qwen/Qwen3.8-27B> |
| Payment per generation | none — your own computer does the work |

AI2P downloads not the original weights but a GGUF quantisation (`unsloth/Qwen3.8-27B-GGUF`)
— it is published under the same licence as the original model (`Qwen/Qwen3.8-27B`). The
licence was taken from the model card on 2026-08-27 by a request to HuggingFace (the
`cardData.license` field), not from memory: for open models it rarely changes, but check the
card again before a commercial release.

The **llama.cpp engine itself is MIT** (the `LICENSE` file of the ggml-org/llama.cpp
repository, verified on 2026-08-27), so it puts no conditions on your product either.

## Hardware requirements

| | |
|---|---|
| Disk space | **20 GB and up** per model (the weights + the llama.cpp package) |
| VRAM | **24 GB** — the whole model on the graphics card (RTX 3090/4090/5090) |
| | **16 GB** — part of the layers is offloaded to RAM, noticeably slower |
| | **less than 12 GB** — take a smaller quantization (`UD-Q3_K_XL`) with your own reference record |
| RAM | **32 GB and up** |
| CPU | the more cores, the faster in CPU mode |

The numbers are **an estimate by the size of the weights, no measurements were made**: the
model has not been started on this computer. The context in the launch command is 65,536
tokens (`-c 65536`); the larger it is, the more VRAM goes to the KV cache.

**The first start is slow:** loading the weights takes minutes. AI2P waits for the API to
become ready and shows the team member in the "starting" state — this is not a hang.

## Limits and price

| | |
|---|---|
| Context | 65,536 tokens (`-c 65536` in the launch command) |
| Maximum output | 32,768 tokens (`params.maxTokens`) |
| Cost | 0 — your computer does the computing |

## Common errors

* **"N tokens exceeds context 32768"** — the launch command has a small `-c`; put `-c 65536`
  there and restart the work of the team.
* **An empty result with the reason `length`** — `params.maxTokens` is too small; set it to
  32768.
* **Not enough memory / the system starts swapping** — close what you can or take a smaller
  quantization.
* **Port 8081 is busy** — change `--port` in the launch command and `baseUrl` in the profile.

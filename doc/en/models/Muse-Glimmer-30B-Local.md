# Muse-Glimmer-30B-Local

**Hosting:** LOCAL (runs on this computer)
**Connection:** `provider: openai-compatible`, `baseUrl: http://localhost:8083/v1`,
model `muse-glimmer-30b`
**API key:** not needed — the local server is started without authorization
**What it does:** texts and code, skill scores 73–80

An open Meta model of 30 billion parameters in the **UD-Q4_K_XL** quantization, running
through **llama.cpp / llama-server**. The younger relative of the cloud
Muse-Spark-1.2. On code it is weaker than the Qwen models of the same size
(75–78 against 82–87), but it is steadier on texts — and, like all the local records, it is
**free and sends the data nowhere**.

## No key is needed, an installation is

Everything is installed with the **«Install»** button on the model form: **Settings →
Catalogs → AI models → Muse-Glimmer-30B-Local → «Install»**. What gets installed:

* the **llama.cpp package** (~0.5 GB) — a build with `llama-server`, shared with the other
  local models;
* the **weights file** `Muse-Glimmer-30B-UD-Q4_K_XL.gguf` — **14.8 GiB** (15,878,222,368
  bytes) into the model repository (`storage.modelsRepo`, by default `C:\ai` on Windows and
  `~/ai` on Linux/macOS, group `Muse-Glimmer`).

This is the smallest file among the local records of the reference, so when there is not enough
VRAM it is the easier one to start with. The download resumes and what was downloaded is
checked against the exact size. **Until the files are in place the model cannot be active** —
that is an AI2P rule, not an error.

The launch command that is written into the profile after the installation:

```
llama-server.exe -m <path to the .gguf> -ngl 99 -c 65536 --jinja --port 8083 --alias muse-glimmer-30b
```

Port **8083** belongs to this record (the Qwen models use 8081 and 8082): two `llama-server`
processes will not start on the same port next to each other.

## Licence

| | |
|---|---|
| Model weights | **Apache 2.0** |
| Commercial use | allowed |
| What is required | keep the licence text and the copyright notice |
| Licence text | <https://huggingface.co/meta-models/Muse-Glimmer-30B> |
| Payment per generation | none — your own computer does the work |

AI2P downloads not the original weights but a GGUF quantisation
(`unsloth/Muse-Glimmer-30B-GGUF`) — it is published under the same licence as the original
model (`meta-models/Muse-Glimmer-30B`). The licence was taken from the model card on
2026-08-27 by a request to HuggingFace (the `cardData.license` field), not from memory: for
open models it rarely changes, but check the card again before a commercial release. Both
cards — the quantisation and the original model — state Apache 2.0.

The **llama.cpp engine itself is MIT** (the `LICENSE` file of the ggml-org/llama.cpp
repository, verified on 2026-08-27), so it puts no conditions on your product either.

## Hardware requirements

| | |
|---|---|
| Disk space | **20 GB and up** (the weights 14.8 GiB + the llama.cpp package) |
| VRAM | **24 GB** — the whole model on the graphics card, with room for the context |
| | **16 GB** — it just about fits, or with a small offload of layers to RAM |
| | **less than 12 GB** — take a smaller quantization (`UD-Q3_K_XL`) with your own reference record |
| RAM | **32 GB and up** |
| CPU | the more cores, the faster in CPU mode |

The numbers are **an estimate by the size of the weights, no measurements were made**: the
model has not been started on this computer. The context in the launch command is 65,536
tokens (`-c 65536`); the larger it is, the more VRAM goes to the KV cache.

**The first start is slow:** loading the weights takes minutes — this is not a hang.

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
* **Port 8083 is busy** — change `--port` in the launch command and `baseUrl` in the profile.
* **Not enough memory** — take a smaller quantization or free up VRAM.

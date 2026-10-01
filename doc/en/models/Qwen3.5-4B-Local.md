# Qwen3.5-4B-Local

**Hosting:** LOCAL (runs on this computer)
**Connection:** `provider: openai-compatible`, `baseUrl: http://localhost:8084/v1`,
model `qwen3.5-4b`
**API key:** not needed — the local server starts without authorization
**What it does:** light text work and data analysis, skill scores 62-74

The least hardware-hungry record of the reference. It was added by task T-347-S0
(2026-09-23) for the PROMPTER ROLE: before a media job the prompter reads the task
description and returns a small control json following the schema in the working model's
profile. That work is always the same and always simple — raising a 27-billion model or
paying a cloud for it makes no sense.

4 billion parameters, the **Q4_K_S** quant, the **llama.cpp / llama-server** engine. The
model also suits ordinary small jobs — translation, summary, text editing — but it should
not be taken for code or complex analysis: the reference has stronger records for that.

## No key is needed, an installation is

Everything is installed by the **"Install"** button of the model form: **Settings →
Catalogs → AI models → Qwen3.5-4B-Local → "Install"**. Installed are:

* the **llama.cpp package** (~0.5 GB) — a build with `llama-server`;
* the **weights file** `Qwen3.5-4B-Q4_K_S.gguf` — **2.41 GiB** (2,590,430,368 bytes) into
  the model repository (`storage.modelsRepo`, group `Qwen3.5-4B`).

The size was taken by a HEAD request to HuggingFace on 2026-09-23: answer 200 without a
token, the `unsloth/Qwen3.5-4B-GGUF` repository is not gated. The download resumes and
what is downloaded is verified by the exact size. **While the files are not in place the
model cannot be active** — that is an AI2P rule, not an error.

After the installation the launch command is written into the profile:

```
llama-server.exe -m <path to .gguf> -ngl 99 -c 16384 --cache-type-k q8_0 --cache-type-v q8_0 --jinja --port 8084 --alias qwen3.5-4b
```

Port **8084** belongs to this record: 8080-8083 are taken by the neighbouring local
models, otherwise a second `llama-server` would not start next to the first one.

## Licence

| | |
|---|---|
| Model weights | **Apache 2.0** |
| Commercial use | allowed |
| What is required | keep the licence text and the copyright notice |
| Licence text | <https://huggingface.co/unsloth/Qwen3.5-4B-GGUF> |
| Payment per generation | none — your computer does the work |

AI2P downloads not the original weights but a GGUF quant (`unsloth/Qwen3.5-4B-GGUF`) — it
is published under the same licence as the original Qwen model. The licence was taken from
the model card on 2026-09-23 by a request to HuggingFace (the `cardData.license` field),
not from memory.

The engine itself, **llama.cpp, is under MIT**, so it puts no conditions on your product
either.

## Hardware requirements

| | |
|---|---|
| Disk space | **from 4 GB** (weights + the llama.cpp package) |
| Video memory | **3 GB** — all layers on the GPU with a context of 16,384 and a `q8_0` KV cache |
| | **2 GB** — lower `-c` to 4096 or take the `IQ4_XS` quant as your own record |
| | no GPU at all — it will run on the CPU, slower, but enough for a prompter |
| RAM | **from 10 GB** for the whole machine |
| CPU | the more cores, the faster in CPU mode |

The numbers are **an estimate from the size of the weights and of the KV cache, no
measurements were made**: the model was not started on this computer. The Q4_K_S weights
take 2.41 GiB, the key and value cache at `-c 16384` with `q8_0` takes less than 200 MiB.

## Limits and price

| | |
|---|---|
| Context | 16,384 tokens (`-c 16384` in the launch command) |
| Maximum output | 8,192 tokens (`params.maxTokens`) |
| Price | 0 — your computer does the work |

## This record is NOT retired by version seniority

The usual rule of the reference is "a model with three newer versions of the same family
is due to be switched off". It does NOT apply to this record while the weights file is
available for download from the vendor: its value is not the quality but the size, and the
reference has no replacement with such hardware requirements. There is only one reason to
switch it off — the `unsloth/Qwen3.5-4B-GGUF` repository stopped serving the file.

## Common errors

* **The media job goes into generation without a control json** — the prompter executor has
  no model chosen, or the working model's profile has no `prompter.required`.
* **Not enough video memory** — lower `-c` in the launch command: the context is the main
  consumer of memory beyond the weights themselves.
* **Port 8084 is busy** — change `--port` in the launch command and `baseUrl` in the profile.
* **The answers are worse than expected** — this is a 4-billion-parameter model; for code
  and complex analysis take stronger records.

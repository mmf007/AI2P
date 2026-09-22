# AI2P models: how the reference works

This directory holds the model documents: one file per record of the AI models reference, the
file name being the **model name** (`Claude-Sonnet-5.md`, `Qwen3.8-27B-Local.md`). It is
opened with the **«i»** button on the model form: **Settings → Catalogs → AI models**.

Here is what is common to all of them: how the three ways of connecting differ, how to set the
key, why a model may be inactive and how to create your own record.

## Section contents

**Cloud (an API key is needed)**

* [Chatterbox-TTS](Chatterbox-TTS.md) — text plus a voice sample → speech in that voice, skills `audio-speech` 88
* [Claude-Fable-5](Claude-Fable-5.md) — the API of the provider Anthropic
* [Claude-Fable-5.1](Claude-Fable-5.1.md) — the API of the provider Anthropic
* [Claude-Haiku-4.5](Claude-Haiku-4.5.md) — the Anthropic API
* [Claude-Opus-5.0](Claude-Opus-5.0.md) — the API of the provider Anthropic
* [Claude-Sonnet-5](Claude-Sonnet-5.md) — the Anthropic API
* [DeepSeek-V4-Flash](DeepSeek-V4-Flash.md) — the DeepSeek API, OpenAI-compatible
* [DeepSeek-V4-Pro](DeepSeek-V4-Pro.md) — the DeepSeek API, OpenAI-compatible
* [DeepSeek-V4.1-Flash](DeepSeek-V4.1-Flash.md) — the DeepSeek API, OpenAI-compatible
* [ElevenLabs-Music](ElevenLabs-Music.md) — text into music and songs, rights-cleared, skills `audio-song` 89, `audio-music` 89
* [ElevenLabs-TTS-v3](ElevenLabs-TTS-v3.md) — text into speech, skills `audio-speech` 96
* [Gemini-3-Ultra](Gemini-3-Ultra.md) — Google, through an OpenAI-compatible layer (**switched off on 2026-09-11**)
* [Gemini-3.1-Pro](Gemini-3.1-Pro.md) — Google, through an OpenAI-compatible layer (**switched off on 2026-09-11**)
* [Gemini-3.7-Flash](Gemini-3.7-Flash.md) — Google, through an OpenAI-compatible layer
* [Gemini-3.8-Flash](Gemini-3.8-Flash.md) — Google, through an OpenAI-compatible layer
* [Gemini-Omni-Flash](Gemini-Omni-Flash.md) — text into an 8-second video with sound, skills `video-generate` 91
* [GigaChat-3.5-Ultra](GigaChat-3.5-Ultra.md) — Sber, Russia; OpenAI-compatible
* [GLM-5.2](GLM-5.2.md) — Z.ai / Zhipu, OpenAI-compatible
* [GLM-5.3](GLM-5.3.md) — Z.ai / Zhipu, OpenAI-compatible
* [GPT-5.6-Sol](GPT-5.6-Sol.md) — the OpenAI API, OpenAI-compatible
* [GPT-5.6-Terra](GPT-5.6-Terra.md) — the OpenAI API, OpenAI-compatible
* [GPT-6-Astra](GPT-6-Astra.md) — the OpenAI API, OpenAI-compatible
* [GPT-Image-2](GPT-Image-2.md) — text into an image, billed by tokens, skills `image-generate` 96, `image-text` 95, `image-photo` 94, `image-concept` 90
* [GPT-Image-2.5](GPT-Image-2.5.md) — text into an image, billed by tokens, skills `image-generate` 97, `image-text` 96, `image-photo` 95, `image-concept` 92
* [Grok-4.6](Grok-4.6.md) — the xAI API, OpenAI-compatible
* [Inkling-975B](Inkling-975B.md) — Thinking Machines, through the OpenRouter gateway
* [Kimi-K3](Kimi-K3.md) — Moonshot AI, OpenAI-compatible
* [Kling-3.0](Kling-3.0.md) — a picture into a video with sound, up to 15 seconds, skills `video-animate` 93
* [Ling-3.0-Flash](Ling-3.0-Flash.md) — Ant Group, through the OpenRouter gateway
* [Meshy-7](Meshy-7.md) — text into a game-ready 3D model, skills `3d-generate` 87
* [MiniMax-H3-Max](MiniMax-H3-Max.md) — text into a video up to 15 seconds, skills `video-generate` 92
* [MiniMax-M3](MiniMax-M3.md) — MiniMax, OpenAI-compatible
* [Mistral-Large-3](Mistral-Large-3.md) — Mistral AI, France; OpenAI-compatible
* [Muse-Spark-1.2](Muse-Spark-1.2.md) — Meta, through the OpenRouter gateway
* [Muse-Spark-1.3](Muse-Spark-1.3.md) — Meta, through the OpenRouter gateway
* [Nano-Banana-Pro-Edit](Nano-Banana-Pro-Edit.md) — a picture plus an instruction into an edited picture, skills `image-edit` 98, `image-inpaint` 90
* [Nano-Banana-Pro](Nano-Banana-Pro.md) — text into an image up to 4K, the best text in the frame, skills `image-text` 98, `image-generate` 97, `image-photo` 96, `image-concept` 92
* [Nemotron-3-Ultra](Nemotron-3-Ultra.md) — NVIDIA, through the OpenRouter gateway
* [Qwen3.8-Max](Qwen3.8-Max.md) — Alibaba DashScope, OpenAI-compatible
* [Seedance-2.5-I2V](Seedance-2.5-I2V.md) — a picture into a video with sound, skills `video-animate` 95
* [Seedance-2.5](Seedance-2.5.md) — text into a video up to 30 seconds with sound, skills `video-generate` 97
* [Tripo-H3.1](Tripo-H3.1.md) — a picture into a 3D model with PBR textures, skills `3d-image` 92
* [Veo-3.1](Veo-3.1.md) — text into a video with sound, 4-8 seconds, up to 4K, skills `video-generate` 93
* [Wan-3.0-Prime](Wan-3.0-Prime.md) — text into a video up to 30 seconds with sound, skills `video-generate` 94
* [YandexGPT-5.1-Pro](YandexGPT-5.1-Pro.md) — Yandex Cloud, Russia; OpenAI-compatible
* [Zonos-2-TTS](Zonos-2-TTS.md) — text plus a voice sample → speech in that voice (the sample is mandatory), skills `audio-speech` 86

**Over the Claude CLI (by subscription, no key)**

* [Claude-Fable-5_cli](Claude-Fable-5_cli.md)
* [Claude-Opus-5.0_cli](Claude-Opus-5.0_cli.md)
* [Claude-Sonnet-5_cli](Claude-Sonnet-5_cli.md)

**Local (your computer does the work)**

* [Muse-Glimmer-30B-Local](Muse-Glimmer-30B-Local.md) — texts and code, skill scores 73–80
* [Qwen3.6-27B-Local](Qwen3.6-27B-Local.md) — code and texts, skill scores 75–82
* [Qwen3.6-35B-A3B-Local](Qwen3.6-35B-A3B-Local.md) — code and texts, skill scores 72–80
* [Qwen3.8-27B-Local](Qwen3.8-27B-Local.md) — code and texts, skill scores 80–87

**Local media models (video, images, audio and 3D)**

* [ACE-Step-1.5-XL-Base](ACE-Step-1.5-XL-Base.md) — text → music and songs, skills `audio-song` 86, `audio-music` 87
* [ACE-Step-1.5-XL-SFT](ACE-Step-1.5-XL-SFT.md) — text → music and songs, skills `audio-song` 90, `audio-music` 89
* [ACE-Step-1.5-XL-Turbo](ACE-Step-1.5-XL-Turbo.md) — text → music and songs, skills `audio-song` 84, `audio-music` 86
* [FLUX.2-dev](FLUX.2-dev.md) — text → image, skills `image-generate` 94, `image-photo` 93, `image-concept` 92, `image-text` 90
* [FLUX.2-klein-4B-Edit](FLUX.2-klein-4B-Edit.md) — picture + instruction → image, skills `image-edit` 86, `image-inpaint` 83, `image-generate` 80, `image-text` 78
* [FLUX.2-klein-4B](FLUX.2-klein-4B.md) — text → image, skills `image-generate` 88, `image-photo` 87, `image-concept` 86, `image-text` 80
* [Hunyuan3D-2.1](Hunyuan3D-2.1.md) — image → 3D model, skill `3d-image` 86
* [HunyuanVideo-1.5-720p-T2V](HunyuanVideo-1.5-720p-T2V.md) — text → video, skill `video-generate` 79
* [Kandinsky-5.0-I2V-Lite-5s](Kandinsky-5.0-I2V-Lite-5s.md) — image + text → video (5 seconds), skills `video-animate` 78, `video-generate` 74
* [Kandinsky-5.0-Image-Lite](Kandinsky-5.0-Image-Lite.md) — text → image, skills `image-text` 88, `image-generate` 84, `image-concept` 83, `image-photo` 82
* [Kandinsky-5.0-T2V-Lite-distil16-10s](Kandinsky-5.0-T2V-Lite-distil16-10s.md) — text → video, skill `video-generate` 70
* [Kandinsky-5.0-T2V-Lite-distil16-5s](Kandinsky-5.0-T2V-Lite-distil16-5s.md) — text → video, skill `video-generate` 70
* [Kandinsky-5.0-T2V-Lite-nocfg-10s](Kandinsky-5.0-T2V-Lite-nocfg-10s.md) — text → video, skill `video-generate` 74
* [Kandinsky-5.0-T2V-Lite-nocfg-5s](Kandinsky-5.0-T2V-Lite-nocfg-5s.md) — text → video, skill `video-generate` 74
* [Kandinsky-5.0-T2V-Lite-sft-10s](Kandinsky-5.0-T2V-Lite-sft-10s.md) — text → video, skill `video-generate` 76
* [Kandinsky-5.0-T2V-Lite-sft-5s](Kandinsky-5.0-T2V-Lite-sft-5s.md) — text → video (5 seconds), skills `video-generate` 76, `video-animate` 72
* [LTX-2.5](LTX-2.5.md) — text → video with sound, skill `video-generate` 85
* [Qwen-Image-2512](Qwen-Image-2512.md) — text → image, skills `image-text` 94, `image-photo` 91, `image-generate` 90, `image-concept` 85
* [Qwen-Image-Edit-2511](Qwen-Image-Edit-2511.md) — image + instruction → edited image, skills `image-edit` 90, `image-text` 90, `image-inpaint` 85, `image-generate` 82
* [SD-3.5-Large](SD-3.5-Large.md) — text → image, skills `image-generate` 80, `image-photo` 79, `image-concept` 78, `image-text` 70
* [SDXL-1.0](SDXL-1.0.md) — text → image, skills `image-generate` 70, `image-concept` 70, `image-photo` 68
* [TRELLIS-2](TRELLIS-2.md) — image → 3D model WITH COLOUR, skill `3d-image` 85
* [TripoSplat](TripoSplat.md) — image → GAUSSIAN SPLATS, skill `3d-image` 80
* [Wan-2.2-I2V-A14B](Wan-2.2-I2V-A14B.md) — image + text → video, skills `video-animate` 80, `video-generate` 74
* [Wan-2.2-T2V-A14B](Wan-2.2-T2V-A14B.md) — text → video, skill `video-generate` 80
* [Z-Image-Turbo](Z-Image-Turbo.md) — text → image, skills `image-generate` 85, `image-photo` 84, `image-concept` 82, `image-text` 78

## Three ways of connecting

| | cloud | local | over the CLI |
|---|---|---|---|
| Where it computes | the provider's server | **your computer** | the provider's server |
| Payment | per token | none | **by subscription**, 0 in billing |
| What is needed | **an API key** | **downloading the files** (tens of GB) | `claude login` on this computer |
| `provider` in the profile | `anthropic`, `openai-compatible` | `openai-compatible` | `anthropic` + `transport: cli` |
| `baseUrl` | the provider's address | `http://localhost:<port>/v1` | not used |
| Agent tools | AI2P tools | AI2P tools | **the CLI's own built-in tools** |
| Data leaves the computer | yes | **no** | yes |

* **Cloud** — most records of the reference. Fast, of good quality, paid; a provider key and
  internet access are required. Examples: Claude-Sonnet-5,
  GPT-5.6-Sol, DeepSeek-V4-Pro.
* **Local** (the name ends with `-Local`) — the weights lie on your disk, your graphics card
  does the computing, nothing goes outside. No key is needed, but disk space, memory and
  patience for the download are. Examples: Qwen3.8-27B-Local,
  Muse-Glimmer-30B-Local. AI2P raises the local server
  (`llama-server`) itself when the team starts working and shuts it down when the team stops —
  **its own process only**; a foreign one on the same port is left alone.
* **Over the CLI** (the name ends with `_cli`) — the same cloud model, but launched through
  Claude Code in headless mode and paid for **by subscription** rather than by tokens.
  **No API key is needed**, a completed `claude login` is. An important difference: the agent
  works with **its own** tools right in the project folder, the AI2P tools are not published to
  it. Examples: Claude-Sonnet-5_cli,
  Claude-Fable-5_cli.

## How to set the key

1. **Settings → Catalogs → AI models** — find the record in the list.
2. The **«Set API key»** button on the model form (if a key is already there the button is
   called «Update API key»; the old value is never shown).
3. Paste the value issued by the provider and save.

**The key is shared by all the models of one provider.** The profile of every record has the
"Key in secrets (reference)" field — for instance `anthropic.apiKey` or `openrouter.apiKey`.
All the records with the same reference use one key: enter it for Claude-Sonnet-5 and Opus and
Haiku start working too. Where exactly to get the key is written in the document of the
particular model, in the **"How to obtain the key"** section.

The key **belongs to the organization**: it is encrypted with the organization key and
replicated to all its servers, so there is no need to enter it on every computer. The
organization key itself lies only on its own computer (`secrets.json`) and is never replicated.

## Where the key lies on disk

A key can also be given as a **file** — the second way, meant for an installation without the
interface and for carrying old keys over. The files live in the **`secrets/` subfolder next to
`config.json`** (that is, in the folder the application is installed into), one json per
reference:

```
secrets/anthropic.apikey.json
{ "ref": "anthropic.apiKey", "value": "sk-ant-..." }
```

The full path of that file for a particular model is shown in its form as the **"Key file on
this computer"** line — right next to the key button. The subfolder is created at the first
start of the application, with a `readme.txt` explanation inside.

**A key entered in the form does not create a file here**, and that is not a fault: it goes
into the organization database encrypted so that it reaches the other servers. A file appears
for keys of the file storage — those moved from the old `secrets.json` at startup and written
by hand. If such a file exists, entering a new value in the form updates it as well.

The lookup order is: **organization database → `secrets/` → `secrets.json` → environment
variable** (`anthropic.apiKey` → `ANTHROPIC_API_KEY`). The `secrets/` subfolder never goes into
replication, into a release build or into the installation inventory — a version update leaves
it untouched.

## Why a model may be inactive

The "active" flag decides whether the model is offered when an executor is picked. It cannot
always be switched on, and that is a rule rather than an error:

* **a cloud model without an API key cannot be active** — the form says exactly that: "No API key:
  the model activates as soon as the key is set", and an attempt to switch the flag on
  through the API is rejected;
* **a local model without downloaded files cannot be active** — "Model is not installed:
  it activates automatically after installation".

The reasoning is simple: otherwise the executor would count as ready, a job would go to it and
fail at the very start — with an obscure network error instead of a clear "no key". As soon as
the key is entered or the installation is finished, the flag switches on by itself.

It follows from the same rule that **a `_cli` model is active immediately**: it needs no key
and has no files to download. There is only one check — whether Claude Code is installed for
the user AI2P runs as; AI2P does not perform it in advance.

## How to add your own record

The reference of the distribution grows with new versions, but nothing stops you from creating
a model of your own — for example the same local Qwen in a smaller quantization, or a model of
a provider that is not in the reference.

1. **Settings → Catalogs → AI models → «Add model»**.
2. **Name** — by the scheme `<common name>-<model name>`: `Claude-Opus-5.0`,
   `DeepSeek-V4-Pro`. For a local one add the `-Local` suffix. The name must be unique.
3. **Hosting** — "Cloud (provider API)" or "Local (runs on this computer)".
4. **«Profile…»** — the provider (`anthropic`, `openai-compatible`, `comfyui`), the connection
   (API or CLI agent), **Model (provider id)**, the Base URL, the key reference
   in the secrets, the launch command of the local server and the parameters (JSON, for
   instance `{"maxTokens": 32768}`).
5. **«Capabilities declaration…»** — the input and output formats, the skills with a score of
   0–100, the limits (context, maximum output) and the price per 1M tokens. Automatic executor
   selection works by this declaration: **a skill that is not in the skill reference will be
   seen by nobody** — take the codes from the list on the form instead of inventing them.
6. Set the key (for a cloud model) or press **«Install»** (for a local one) — and switch on the
   "active" flag.
7. **Put the document of your model here as well**: `doc/en/models/<model name>.md` (and
   `doc/ru/models/<model name>.md` for the Russian interface). If there is no document, the
   «i» button shows a hint with the full path where to put it.

Your own records are marked as **"Custom"** and, unlike the records of the distribution, are
deleted whole. The records of the distribution cannot be deleted — if a model is not needed,
simply switch off its "active" flag.

## What is not in the model documents

The identifiers, prices and limits of the reference have been checked against the public model
catalogue and the market review, but **not a single identifier has been verified by a live call
to the provider**: the project has no keys to all these APIs. That is why the document of every
cloud model carries one and the same line — before the first job check the id with a
`GET /models` request to the provider API. The market changes fast (about one model every two
days) and a reference record goes stale silently: a changed id gives a 404 right on the
executor, and a wrong price gives a wrong bill in billing.

The hardware requirements of the local models are **an estimate by the size of the weights**;
no measurements on real hardware were made.

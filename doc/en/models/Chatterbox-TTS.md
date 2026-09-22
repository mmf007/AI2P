# Chatterbox-TTS

**Hosting:** cloud — the [fal.ai](https://fal.ai/models) gateway
**Connection:** `provider: fal-ai`, `baseUrl: https://queue.fal.run`, model `fal-ai/chatterbox/text-to-speech`
**Key reference:** `fal.apiKey`
**What it does:** text plus a voice sample into speech in that voice, skills `audio-speech` 88

The first catalogue records that accept **reference audio** (the T-249-S0 mechanism). A
constant character voice is held here not by a LoRA adapter but by a **sample**: 3-30 seconds
of a recording go into the `audio_url` field of the request and the model copies the timbre
(zero-shot clone). The task text goes into the `text` field.

The fine-tuning fields are `exaggeration` (expressiveness, 0-1; 0.25 in the template), `temperature` (0.7) and `cfg` (0.5). The text length limit of the endpoint is 5000 characters.

## How to use it

1. Create a project object of the kind **reference recording** (the "Objects" tab of the
   project card), and put the path of a `.wav` or `.mp3` relative to the project folder into
   the file field; usually as a child of the character.
2. Refer to it from the task description with `@obj:OBJ-N` or name the file in words
   ("voice sample refs/vera.wav").
3. Point the executor at this catalogue record — the voice sample is **optional in the endpoint schema** (the field has a default voice), but the AI2P request template does contain the `{audio}` placeholder — so a job without a recording stops with a clear message instead of going to the gateway with a stranger's voice.

## How to obtain the key

1. Create an account at [fal.ai](https://fal.ai/) (sign in with GitHub or Google).
2. Top up the balance in [Billing](https://fal.ai/dashboard/billing): the gateway works
   on prepayment, with no money on the account a request is rejected.
3. Open [API Keys](https://fal.ai/dashboard/keys) and press **Add key**.
4. Copy the value — it is shown **once**.
5. In AI2P: **Settings -> Models -> Chatterbox-TTS -> "API key"**.

All fal.ai records share **one** key (the `fal.apiKey` reference): setting it once switches
on every cloud media model of the gateway. The key belongs to the organization — it is
encrypted with the organization key and replicated to all its servers (spec ch. 10).
Without a key the catalogue record cannot be active and cannot be given to an executor.

## Limits and price

| | |
|---|---|
| Text | the whole task description goes into the `text` field |
| Voice sample | the `audio_url` field, **one** recording, up to **30 seconds**, `audio/wav` or `audio/mpeg` |
| Sample duration | `maxSeconds: 30` is **stored but not verified** by AI2P — an over-long file is rejected by the gateway itself |
| Price | **$0.025 per 1000 characters** of text (gateway catalogue, 14.09.2026) |

The model identifier, the name of the sample field and the price were checked against the
provider catalogue on **14.09.2026** (a request to `https://fal.ai/api/models` and to the
queue OpenAPI schema of the endpoint). **The model was not verified by a live call** —
generation costs money. Check the id and the price yourself: `GET /models` of the gateway
catalogue (`https://fal.ai/api/models`) and the [model page](https://fal.ai/models/fal-ai/chatterbox/text-to-speech).
Gateway prices change without notice.

The billing is done by the provider. **The AI2P job cost does not count this price** and stays
zero: media models are paid per character, per second or per minute rather than per token, and
the gateway answer holds no tokens at all. The tariff is written in words into the job console
and into the result summary.

## Licence

The terms apply to the **result of the generation**, not to the weights: the provider does the
computing. The model owner is Resemble AI; the terms of the gateway itself apply alongside.

* The provider catalogue marks this record `licenseType: commercial` — **commercial use of the
  result is allowed** (checked on 14.09.2026 by a request to the catalogue).
* The weights of the model itself are open under **MIT** — the card [https://huggingface.co/ResembleAI/chatterbox](https://huggingface.co/ResembleAI/chatterbox), the
  `cardData.license` field was checked by a request to `https://huggingface.co/api/models` on
  14.09.2026. That matters for a cloud connection too: the weight licence does not forbid
  commercial use of a voice synthesized from your own sample.
* The exact terms: the [model page at the gateway](https://fal.ai/models/fal-ai/chatterbox/text-to-speech) and the
  [fal.ai terms](https://fal.ai/terms).
* The use policy of the model owner: https://www.resemble.ai/terms-of-service/
* **The rights to the voice itself are not granted by the model licence.** A sample of someone
  else's voice without their consent is a separate legal risk, and neither MIT nor Apache 2.0
  removes it.
* Payment for generation: **yes** — the provider counts it, the tariff is in the price section above.

## Hardware requirements

None: the provider does the computing. Internet access is needed to `queue.fal.run` (queueing
the job and polling its status) and to the gateway file storage (`*.fal.media`) — that is where
AI2P downloads the ready file into the task artifacts from. The voice sample goes **inside the
request body** as a `data:` URL; the gateway has no separate file upload.

## How a job is written

The prompt of a media model is the **whole task description**: neither the title, nor the
acceptance criteria, nor the project experience reach it.

* the `@obj:OBJ-3` reference in the description expands into the verbatim passport of a
  project object on **every** run — an edit of the passport takes effect on the very next job;
* a reference to an object of the kind **reference recording** MAKES passing the file mandatory;
* the line "put the result into the file `audio/line-1.wav`" also puts the ready file into the
  project folder; it always lies in the task artifacts as well;
* there is no way to pass a start image to this model (`refImage.kind: none`) — if the
  description names a picture file, AI2P says so with a line in the job console;
* the request fields are edited in the model profile, section `request`; take the values from
  the endpoint schema on the model page, an unknown value is rejected by the gateway with HTTP 422.

## LoRA

Training and attaching an adapter are **not available** here, and the catalogue says so
honestly: `"lora": { "supported": false, "reason": "provider" }`. The weights are on the
provider side and it takes no adapter file of yours. A job that names a reference to an object
with a LoRA adapter will not reach generation: AI2P refuses at once and offers an executor that
can do it in the chat (T-14-S1). A constant voice is held here by the **reference recording**,
and that is cheaper than training: a clone from ten seconds is no worse than an adapter and costs nothing.

## Common errors

* **"The reference recording was not passed"** — the task description names neither an `@obj:`
  reference to an object of the kind "reference recording" nor an audio file name; the
  `{audio}` placeholder is present in the request template and the job stops before sending.
* **"This model takes no recording"** — the executor points at another catalogue record; the
  `refAudio` section is declared only by the records named in the T-251-S0 report.
* **HTTP 422 from the gateway** — the request template has gained a field the endpoint does not
  have, or a value outside the schema enumeration.
* **"The API key was not found"** — the key is set in no fal.ai record; set it once in any of them.
* **HTTP 401 / 403 from the gateway** — the key is invalid or revoked.
* **HTTP 429** — the provider throttled the requests, run the job again later.

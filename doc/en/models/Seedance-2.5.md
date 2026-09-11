# Seedance-2.5

**Hosting:** cloud — the [fal.ai](https://fal.ai/models) gateway
**Connection:** `provider: fal-ai`, `baseUrl: https://queue.fal.run`, model `bytedance/seedance-2.5/text-to-video`
**Key reference:** `fal.apiKey`
**What it does:** text into a video up to 30 seconds with sound, skills `video-generate` 97

The top of the video market in mid-2026 (section 6.3 of the T-213 report,
`video-generate` 97): a clip **up to 30 seconds with sound in one pass** — three times
longer than Gemini Omni Flash. This record wires the "text to video" mode; animating
a picture with the same model is a separate record, **Seedance-2.5-I2V**.

## How to obtain the key

1. Create an account at [fal.ai](https://fal.ai/) (sign in with GitHub or Google).
2. Top up the balance in [Billing](https://fal.ai/dashboard/billing): the gateway works
   on prepayment, with no money on the account a request is rejected.
3. Open [API Keys](https://fal.ai/dashboard/keys) and press **Add key**.
4. Copy the value — it is shown **once**.
5. In AI2P: **Settings -> Models -> Seedance-2.5 -> "API key"**.

All fal.ai records share **one** key (the `fal.apiKey` reference): setting it once switches
on all twelve cloud media models at once. The key belongs to the organization — it is
encrypted with the organization key and replicated to all its servers (spec ch. 10).
Without a key the catalogue record cannot be active and cannot be given to an executor.

## Limits and price

| | |
|---|---|
| Duration | 4-30 seconds (5 in the request template) |
| Resolution | 480p / 720p / 1080p (720p in the template) |
| Sound | native, generated together with the picture |
| Price | about $0.473 per second of 720p (480p — about $0.2205) |
| The same in tokens | $21.4 per 1M video tokens at 480p/720p |

The model identifier and the prices were checked against the provider catalogue on
**27.08.2026** (a request to `https://fal.ai/api/models` and to the request schema of the
endpoint). **The model was not verified by a live call** — generation costs money. Check the
id and the price yourself: `GET /models` of the gateway catalogue
(`https://fal.ai/api/models`) and the [model page](https://fal.ai/models/bytedance/seedance-2.5/text-to-video). Gateway prices change without
notice.

The billing is done by the provider. **The AI2P job cost does not count this price** and stays
zero: media models are paid per picture, per second or per minute rather than per token, and
the gateway answer holds no tokens at all. The tariff is written in words into the job console
and into the result summary.

## Licence

The model weights are closed and given to nobody — there is nothing to licence here, so the
terms apply to the **result of the generation**, not to the weights. The model owner is
ByteDance; the terms of the gateway itself apply alongside.

* The provider catalogue marks this record `licenseType: commercial` — **commercial use of the
  result is allowed** (checked on 27.08.2026 by a request to the catalogue).
* The exact terms: the [model page at the gateway](https://fal.ai/models/bytedance/seedance-2.5/text-to-video) and the
  [fal.ai terms](https://fal.ai/terms).
* The owner of the model may change the terms for the result; they do not apply retroactively to
  what you have already generated, but it is worth re-reading them before publishing a series.
* Payment for generation: **yes** — the provider counts it, the tariff is in the price section above.

## Hardware requirements

None: the provider does the computing. Internet access is needed to `queue.fal.run` (queueing
the job and polling its status) and to the gateway file storage (`*.fal.media`) — that is where
AI2P downloads the ready file into the task artifacts from.

## How a job is written

The prompt of a media model is the **whole task description**: neither the title, nor the
acceptance criteria, nor the project experience reach it.

* the `@obj:OBJ-3` reference in the description expands into the verbatim passport of a
  project object (a character, a location, a style) on **every** run — an edit of the passport
  takes effect on the very next shot;
* the line "put the result into the file `video/shot-1.mp4`" also puts the ready file into the
  project folder; it always lies in the task artifacts as well;
* there is no way to pass a start image to this model — if the description names a
   picture file, AI2P says so with a line in the job console instead of keeping silent;
* the request fields (duration, resolution, voice, polygon count) are edited in the model
  profile, section `request`; take the values from the endpoint schema on the model page, an
  unknown value is rejected by the gateway with HTTP 422.

## LoRA

Training and attaching an adapter are **not available** here, and the catalogue says so
honestly: `"lora": { "supported": false, "reason": "provider" }`. The model weights are closed
and the provider takes no adapter file of yours. A job that names a reference to an object with
a LoRA adapter will not reach generation: AI2P refuses at once and offers an executor that can
do it in the chat (T-14-S1). A constant look is held here by a start image (the **Seedance-2.5-I2V** record) and a detailed object passport in the description.

## Common errors

* **HTTP 422 from the gateway** — a value in the request template is out of the enumeration: `duration` only takes `auto` and whole numbers from `4` to `30` as a string.
* **The clip came out silent** — `generate_audio` is switched off in the request template.
* **"The API key was not found"** — the key is set in no fal.ai record; set it once in any of
  them.
* **HTTP 401 / 403 from the gateway** — the key is invalid or revoked.
* **HTTP 429** — the provider throttled the requests, run the job again later.

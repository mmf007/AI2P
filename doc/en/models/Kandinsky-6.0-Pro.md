# Kandinsky-6.0-Pro

**Hosting:** cloud — the [fal.ai](https://fal.ai/models) gateway
**Connection:** `provider: fal-ai`, `baseUrl: https://queue.fal.run`, model `fal-ai/kandinsky6-pro/text-to-video`
**Key reference:** `fal.apiKey`
**What it does:** text into a 5-second video with sound, skills `video-generate` 90

Kandinsky Lab released this model on 2026-10-05; it went into the AI2P reference through task
T-466-S0 — the vendor sweep of 2026-10-13. The identifier `fal-ai/kandinsky6-pro/text-to-video`, the price and the
modalities were verified by a request to the public fal.ai catalogue.

The next generation after **Kandinsky-5.0**. The gateway price is $1.35 per 5-second 480p clip at 50 steps (≈ $0.27 per second), upscaling to 1080p $1.53, fewer steps cost less (20 steps $0.56). The Lite variant is the same version, gateway endpoint `fal-ai/kandinsky6-lite/text-to-video` ($0.16 per clip).

## How to obtain the key

1. Create an account at [fal.ai](https://fal.ai/) (sign in with GitHub or Google).
2. Top up the balance in [Billing](https://fal.ai/dashboard/billing): the gateway works
   on prepayment, with no money on the account a request is rejected.
3. Open [API Keys](https://fal.ai/dashboard/keys) and press **Add key**.
4. Copy the value — it is shown **once**.
5. In AI2P: **Settings -> Models -> Kandinsky-6.0-Pro -> "API key"**.

All fal.ai records share **one** key (the `fal.apiKey` reference): setting it once switches
on all twelve cloud media models at once. The key belongs to the organization — it is
encrypted with the organization key and replicated to all its servers (spec ch. 10).
Without a key the catalogue record cannot be active and cannot be given to an executor.

## Limits and price

| | |
|---|---|
| Duration | 5 seconds |
| Resolution | 480p, upscale to 1080p or 4x |
| Price | $1.35 per clip at 50 steps |

The model identifier and the prices were checked against the provider catalogue on
**13.10.2026** (a request to `https://fal.ai/api/models` and to the request schema of the
endpoint). **The model was not verified by a live call** — generation costs money. Check the
id and the price yourself: `GET /models` of the gateway catalogue
(`https://fal.ai/api/models`) and the [model page](https://fal.ai/models/fal-ai/kandinsky6-pro/text-to-video). Gateway prices change without
notice.

The billing is done by the provider. **The AI2P job cost does not count this price** and stays
zero: media models are paid per picture, per second or per minute rather than per token, and
the gateway answer holds no tokens at all. The tariff is written in words into the job console
and into the result summary.

## Licence

The Kandinsky 6.0 weights are **open** under the MIT licence (`kandinskylab/Kandinsky-6.0-Pro-5s-Diffusers`, verified
on 2026-10-13 by a request to the HuggingFace API), but the repository is gated by an agreement (`gated: auto`) —
without a personal token the files are not served. This record does not touch the weights: the fal.ai gateway computes.

* The gateway catalogue marks the record `licenseType: commercial` — **commercial use of the output is allowed**.
* Gateway terms: [model page](https://fal.ai/models/fal-ai/kandinsky6-pro/text-to-video) and [fal.ai terms](https://fal.ai/terms).
* Generation fee: **yes** — billed by the provider, see the price section above.

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

* **"The API key was not found"** — the key is set in no fal.ai record; set it once in any of
  them.
* **HTTP 401 / 403 from the gateway** — the key is invalid or revoked.
* **HTTP 429** — the provider throttled the requests, run the job again later.

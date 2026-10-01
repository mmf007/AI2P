# Seedream-5-Flash

**Hosting:** cloud — the [fal.ai](https://fal.ai/models) gateway
**Connection:** `provider: fal-ai`, `baseUrl: https://queue.fal.run`, model `bytedance/seedream/v5/flash/text-to-image`
**Key reference:** `fal.apiKey`
**What it does:** text into an image, skills `image-generate` 92, `image-concept` 90, `image-photo` 90

ByteDance released this model on 2026-09-23; it went into the AI2P reference through task
T-347-S0 — the vendor sweep of 2026-09-23. The identifier `bytedance/seedream/v5/flash/text-to-image`, the context length,
the price and the modalities were verified by a request to the public fal.ai catalogue.

It is connected through the fal.ai gateway, like the other media records of the
reference: text into an image, billed per image (skills `image-generate` 92,
`image-concept` 90, `image-photo` 90).

The first record of the **Seedream** family in the reference: until now ByteDance was
present only with the Seedance video line. The `flash` variant is the fast and cheap one;
the gateway also has `lite` and `pro`, and image editing is a separate `edit` endpoint.

**THE GATEWAY HAS NOT PUBLISHED A PRICE:** the tariff field of the catalogue is empty
(verified on 2026-09-23), so the declaration keeps zero instead of an invented number.
Check the rate on the model page before the first paid job.

## How to obtain the key

1. Create an account at [fal.ai](https://fal.ai/) (sign in with GitHub or Google).
2. Top up the balance in [Billing](https://fal.ai/dashboard/billing): the gateway works
   on prepayment, with no money on the account a request is rejected.
3. Open [API Keys](https://fal.ai/dashboard/keys) and press **Add key**.
4. Copy the value — it is shown **once**.
5. In AI2P: **Settings -> Models -> Seedream-5-Flash -> "API key"**.

All fal.ai records share **one** key (the `fal.apiKey` reference): setting it once switches
on all twelve cloud media models at once. The key belongs to the organization — it is
encrypted with the organization key and replicated to all its servers (spec ch. 10).
Without a key the catalogue record cannot be active and cannot be given to an executor.

## Limits and price

| | |
|---|---|
| Image size | `auto_2K` in the request template |
| Number of images | 1 in the request template |
| File format | PNG / JPEG (PNG in the template) |
| Price | NOT published by the gateway (the tariff field of the catalogue is empty as of 2026-09-23) |
| Safety checker | on (`enable_safety_checker`) |

The model identifier and the prices were checked against the provider catalogue on
**23.09.2026** (a request to `https://fal.ai/api/models` and to the request schema of the
endpoint). **The model was not verified by a live call** — generation costs money. Check the
id and the price yourself: `GET /models` of the gateway catalogue
(`https://fal.ai/api/models`) and the [model page](https://fal.ai/models/bytedance/seedream/v5/flash/text-to-image). Gateway prices change without
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
  result is allowed** (checked on 23.09.2026 by a request to the catalogue).
* The exact terms: the [model page at the gateway](https://fal.ai/models/bytedance/seedream/v5/flash/text-to-image) and the
  [fal.ai terms](https://fal.ai/terms).
* The use policy of the model owner: https://policies.google.com/terms/generative-ai/use-policy
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
do it in the chat (T-14-S1). A constant look is held here by a fixed `params.seed` plus a verbatim character passport (`@obj:`) in the description.

## Common errors

* **The bill is twice as large** — the request template has `resolution: "4K"`.
* **The text on the picture has mistakes** — ask for the text **verbatim and in quotes**; this is the strong side of the model, but the wording decides.
* **"The API key was not found"** — the key is set in no fal.ai record; set it once in any of
  them.
* **HTTP 401 / 403 from the gateway** — the key is invalid or revoked.
* **HTTP 429** — the provider throttled the requests, run the job again later.

# Meshy-7.1

**Hosting:** cloud — the [fal.ai](https://fal.ai/models) gateway
**Connection:** `provider: fal-ai`, `baseUrl: https://queue.fal.run`, model `meshy/v7.1/text-to-3d`
**Key reference:** `fal.apiKey`
**What it does:** text into a game-ready 3D model, skills `3d-generate` 89

Meshy released this model on 2026-09-19; it went into the AI2P reference through task
T-347-S0 — the vendor sweep of 2026-09-23. The identifier `meshy/v7.1/text-to-3d`, the context length,
the price and the modalities were verified by a request to the public fal.ai catalogue.

It is connected through the fal.ai gateway, like the other media records of the
reference: text into a game-ready mesh with quad topology and PBR maps
(skill `3d-generate` 89).

The next version after **Meshy-7** (that record stays active: it has only one newer
version so far, and the retirement threshold is three). Gateway catalogue price: $1.20
for a textured model, $0.80 without textures, +$0.20 for auto-rigging and +$0.12 for
animation.

## How to obtain the key

1. Create an account at [fal.ai](https://fal.ai/) (sign in with GitHub or Google).
2. Top up the balance in [Billing](https://fal.ai/dashboard/billing): the gateway works
   on prepayment, with no money on the account a request is rejected.
3. Open [API Keys](https://fal.ai/dashboard/keys) and press **Add key**.
4. Copy the value — it is shown **once**.
5. In AI2P: **Settings -> Models -> Meshy-7.1 -> "API key"**.

All fal.ai records share **one** key (the `fal.apiKey` reference): setting it once switches
on all twelve cloud media models at once. The key belongs to the organization — it is
encrypted with the organization key and replicated to all its servers (spec ch. 10).
Without a key the catalogue record cannot be active and cannot be given to an executor.

## Limits and price

| | |
|---|---|
| Mode | `preview` — geometry only, `full` — with textures (`full` in the template) |
| Topology | `quad` or `triangle` (`quad` in the template) |
| Polygons | 30,000 in the request template |
| Price | $1.20 for a textured model ($0.80 without textures) |
| Auto-rigging and animation | +$0.20 and +$0.12 per call, by flags of the request template |
| Seed | supported: `params.seed` of the profile repeats the model |

The model identifier and the prices were checked against the provider catalogue on
**23.09.2026** (a request to `https://fal.ai/api/models` and to the request schema of the
endpoint). **The model was not verified by a live call** — generation costs money. Check the
id and the price yourself: `GET /models` of the gateway catalogue
(`https://fal.ai/api/models`) and the [model page](https://fal.ai/models/meshy/v7.1/text-to-3d). Gateway prices change without
notice.

The billing is done by the provider. **The AI2P job cost does not count this price** and stays
zero: media models are paid per picture, per second or per minute rather than per token, and
the gateway answer holds no tokens at all. The tariff is written in words into the job console
and into the result summary.

## Licence

The model weights are closed and given to nobody — there is nothing to licence here, so the
terms apply to the **result of the generation**, not to the weights. The model owner is
Meshy; the terms of the gateway itself apply alongside.

* The provider catalogue marks this record `licenseType: commercial` — **commercial use of the
  result is allowed** (checked on 23.09.2026 by a request to the catalogue).
* The exact terms: the [model page at the gateway](https://fal.ai/models/meshy/v7.1/text-to-3d) and the
  [fal.ai terms](https://fal.ai/terms).
* The use policy of the model owner: https://docs.meshy.ai/
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
do it in the chat (T-14-S1). A constant look is held here by a fixed `params.seed` and one and the same text describing the object.

## Common errors

* **The mesh is too heavy for the engine** — lower `target_polycount` in the request template.
* **You need to retexture or rig a ready model** — those are separate provider endpoints, they have no catalogue records yet.
* **"The API key was not found"** — the key is set in no fal.ai record; set it once in any of
  them.
* **HTTP 401 / 403 from the gateway** — the key is invalid or revoked.
* **HTTP 429** — the provider throttled the requests, run the job again later.

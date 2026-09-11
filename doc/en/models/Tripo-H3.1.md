# Tripo-H3.1

**Hosting:** cloud — the [fal.ai](https://fal.ai/models) gateway
**Connection:** `provider: fal-ai`, `baseUrl: https://queue.fal.run`, model `tripo3d/h3.1/image-to-3d`
**Key reference:** `fal.apiKey`
**What it does:** a picture into a 3D model with PBR textures, skills `3d-image` 92

A picture into a 3D mesh with PBR textures; the best `3d-image` in section 6.5 of the T-213
report (92). **This model takes no text at all** — that is why the capability declaration
announces only a picture on the input: otherwise the executor pick (T-257) would hand it a
"make a model from this description" task it cannot read. The task description still has to
be non-empty — the path of the source picture is taken from it.

## How to obtain the key

1. Create an account at [fal.ai](https://fal.ai/) (sign in with GitHub or Google).
2. Top up the balance in [Billing](https://fal.ai/dashboard/billing): the gateway works
   on prepayment, with no money on the account a request is rejected.
3. Open [API Keys](https://fal.ai/dashboard/keys) and press **Add key**.
4. Copy the value — it is shown **once**.
5. In AI2P: **Settings -> Models -> Tripo-H3.1 -> "API key"**.

All fal.ai records share **one** key (the `fal.apiKey` reference): setting it once switches
on all twelve cloud media models at once. The key belongs to the organization — it is
encrypted with the organization key and replicated to all its servers (spec ch. 10).
Without a key the catalogue record cannot be active and cannot be given to an executor.

## Limits and price

| | |
|---|---|
| Source picture | required, one, PNG / JPEG / WebP |
| Output | GLB (the answer may also hold a preview and other mesh formats) |
| Topology | triangles; `quad` in the request template gives quads (+$0.05) |
| Price | $0.30 with standard textures (without textures — $0.20, HD — $0.40) |
| Detailed geometry | +$0.20 with `geometry_quality: "detailed"` |

The model identifier and the prices were checked against the provider catalogue on
**27.08.2026** (a request to `https://fal.ai/api/models` and to the request schema of the
endpoint). **The model was not verified by a live call** — generation costs money. Check the
id and the price yourself: `GET /models` of the gateway catalogue
(`https://fal.ai/api/models`) and the [model page](https://fal.ai/models/tripo3d/h3.1/image-to-3d). Gateway prices change without
notice.

The billing is done by the provider. **The AI2P job cost does not count this price** and stays
zero: media models are paid per picture, per second or per minute rather than per token, and
the gateway answer holds no tokens at all. The tariff is written in words into the job console
and into the result summary.

## Licence

The model weights are closed and given to nobody — there is nothing to licence here, so the
terms apply to the **result of the generation**, not to the weights. The model owner is
Tripo AI; the terms of the gateway itself apply alongside.

* The provider catalogue marks this record `licenseType: commercial` — **commercial use of the
  result is allowed** (checked on 27.08.2026 by a request to the catalogue).
* The exact terms: the [model page at the gateway](https://fal.ai/models/tripo3d/h3.1/image-to-3d) and the
  [fal.ai terms](https://fal.ai/terms).
* The use policy of the model owner: https://platform.tripo3d.ai/docs
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
* the source picture is taken IN THE PROJECT FOLDER by the relative path from the
  description ("use `refs/hero.png` as the base") or from the reference frame of the
  named object; it goes into the request whole, as a `data:` address — AI2P has no file
  storage of its own, and we have no right to publish your file outside by a link;
* the request fields (duration, resolution, voice, polygon count) are edited in the model
  profile, section `request`; take the values from the endpoint schema on the model page, an
  unknown value is rejected by the gateway with HTTP 422.

## LoRA

Training and attaching an adapter are **not available** here, and the catalogue says so
honestly: `"lora": { "supported": false, "reason": "provider" }`. The model weights are closed
and the provider takes no adapter file of yours. A job that names a reference to an object with
a LoRA adapter will not reach generation: AI2P refuses at once and offers an executor that can
do it in the chat (T-14-S1). A constant look is held here by one and the same source picture of the object: `model_seed` is supported in the request template too.

## Common errors

* **"This model needs a start image"** — the source picture file is not named.
* **Several files were downloaded** — besides the mesh the provider returns a preview render; AI2P takes everything in the answer and puts it into the task artifacts.
* **"The API key was not found"** — the key is set in no fal.ai record; set it once in any of
  them.
* **HTTP 401 / 403 from the gateway** — the key is invalid or revoked.
* **HTTP 429** — the provider throttled the requests, run the job again later.

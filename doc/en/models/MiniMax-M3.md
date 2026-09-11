# MiniMax-M3

**Hosting:** cloud (MiniMax, OpenAI-compatible)
**Connection:** `provider: openai-compatible`, `baseUrl: https://api.minimax.io/v1`,
model `minimax-m3`
**Key reference:** `minimax.apiKey`

Cheap and with a large context: $0.30 and $1.20 per million tokens with a context of
1,048,576 — four times cheaper than GLM-5.2 and ten times cheaper than
Kimi-K3. The skill scores are 80–85: the working middle of the reference.

What sets it apart is the **multimodal input**: besides text and sources the model accepts
pictures and video, while costing as much as a cheap text model. It suits jobs where the
material arrives as screenshots or a screen recording.

## How to obtain the key

1. Register at [platform.minimax.io](https://platform.minimax.io/) (the international site;
   the Chinese one has a different domain and different keys).
2. Top up the balance in the billing section.
3. Open **Account → API Keys → Create new secret key**, give it a name.
4. Copy the value — it is shown **once**.
5. In AI2P: **Settings → Catalogs → AI models → MiniMax-M3 → «Set API key»**.

The key belongs to the organization: it is encrypted with the organization key and replicated
to all servers of the organization (spec ch. 10).

## Limits and price

| | |
|---|---|
| Context | 1,048,576 tokens |
| Maximum output | 65,536 tokens |
| Input price | $0.30 per 1M tokens |
| Output price | $1.20 per 1M tokens |

**The model identifier has not been verified by a live call** — it is taken from the model
catalogue without the vendor prefix. Check it with a `GET /models` request to
`https://api.minimax.io/v1`.

## Licence

| | |
|---|---|
| Terms for the generated result | MiniMax **claims no ownership** of the generated content (“We do not claim ownership of User Contributions or User Generated Content”) |
| Commercial use | allowed |
| What is required | remember that you grant MiniMax a perpetual, irrevocable, non-exclusive licence to use what was sent and received |
| Terms text | <https://www.minimax.io/platform/protocol/terms-of-service> |
| Payment per generation | per token — the rate is in “Limits and price” |

The MiniMax-M3 weights are published under **their own community licence** (the
`MiniMaxAI/MiniMax-M3` card calls it `minimax-community`), not MIT or Apache 2.0 — if you
plan to run the model yourself, read it separately.

Verified on 2026-08-27: the weight licence against the model card, the terms for the result
against the published MiniMax terms of service
(<https://agent.minimax.io/doc/en/terms-of-service.html>).

## Hardware requirements

None: the computation happens on the provider side. Internet access to `api.minimax.io` is
required.

## Common errors

* **401 "invalid api key"** — a key from the Chinese site does not work on the international
  one (and the other way round); create the key where `baseUrl` points.
* **404 "model not found"** — the id has changed; check it against the `GET /models` list.
* **The quality on complex code is lower than expected** — the model's niche is a different
  one; for code take GLM-5.2 or Kimi-K3.

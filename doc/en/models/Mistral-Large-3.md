# Mistral-Large-3

**Hosting:** cloud (Mistral AI, France; OpenAI-compatible)
**Connection:** `provider: openai-compatible`, `baseUrl: https://api.mistral.ai/v1`,
model `mistral-large-2512`
**Key reference:** `mistral.apiKey`

The European option in the reference: the data is processed in the EU, which sometimes settles
the question of choice by itself. The strong sides are **texts and translation** (scores
81–85), the code is noticeably weaker (78–82). The price is moderate: $0.50 and $1.50 per
million tokens, the context is 262,144 tokens.

Note the **identifier**: at Mistral it is dated — `mistral-large-2512`, not
`mistral-large-3`. That is how the provider marks a checkpoint; when the next one comes out
the line in the profile will have to be changed.

## How to obtain the key

1. Register at [console.mistral.ai](https://console.mistral.ai/).
2. Enable a paid plan: **Billing → Add payment method**. On the free tier the request rate is
   heavily limited.
3. Open [console.mistral.ai/api-keys](https://console.mistral.ai/api-keys/) →
   **Create new key**, give it a name and an expiry.
4. Copy the value — it is shown **once**.
5. In AI2P: **Settings → Catalogs → AI models → Mistral-Large-3 → «Set API key»**.

The key belongs to the organization: it is encrypted with the organization key and replicated
to all servers of the organization (spec ch. 10).

## Limits and price

| | |
|---|---|
| Context | 262,144 tokens |
| Maximum output | 65,536 tokens |
| Input price | $0.50 per 1M tokens |
| Output price | $1.50 per 1M tokens |

**The model identifier has not been verified by a live call** — it is taken from the model
catalogue without the vendor prefix. Check it with a `GET /models` request to
`https://api.mistral.ai/v1`: with dated checkpoints this matters especially. Current prices —
[Mistral Pricing](https://mistral.ai/pricing).

## Licence

| | |
|---|---|
| Terms for the generated result | **the result is yours**: “Customer … owns all Output”, Mistral assigns you its rights (Commercial ToS, clause 3.1) |
| Commercial use | allowed |
| What is required | do not represent the result as human-made (clause 3.2); do not train a competing image generator on image outputs (clause 3.3); comply with the Usage Policy |
| Terms text | <https://legal.mistral.ai/terms/commercial-terms-of-service> |
| Payment per generation | per token — the rate is in “Limits and price” |

Training on your data is spelled out precisely (clause 4.2): Mistral does **not** train its
models on it — except when you opted in yourself, sent feedback, or used an experimental
model (in AI Studio those carry the `labs` prefix). This catalogue record points at a
regular model, not at a `labs` one.

The terms were verified against their text on 2026-08-27.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `api.mistral.ai` is
required.

## Common errors

* **404 "model not found"** — a new checkpoint has come out and the old date is no longer
  served; take the current id from `GET /models`.
* **429 "rate limit exceeded"** — no paid plan is enabled.
* **The key stopped working** — Mistral keys may have an expiry; check it in the console.

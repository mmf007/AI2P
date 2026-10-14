# Mistral-Large-4

**Hosting:** cloud (Mistral AI, France; OpenAI-compatible)
**Connection:** `provider: openai-compatible`, `baseUrl: https://api.mistral.ai/v1`,
**Key reference:** `mistral.apiKey`

Mistral AI released this model on 2026-10-06; it went into the AI2P reference through task
T-466-S0 — the vendor sweep of 2026-10-13. The identifier `mistralai/mistral-large-4-0`, the price and the
modalities were verified by a request to the public OpenRouter catalogue.

A context of 1,048,576 tokens, an answer of up to 262,144 tokens, $0.68 and $2.09 per million
tokens, skill scores 83-88. The closest record of the reference is **Mistral-Large-3**.

Accepted input: text and Markdown, source code, images.

## How to obtain the key

1. Register at [console.mistral.ai](https://console.mistral.ai/).
2. Enable a paid plan: **Billing → Add payment method**. On the free tier the request rate is
   heavily limited.
3. Open [console.mistral.ai/api-keys](https://console.mistral.ai/api-keys/) →
   **Create new key**, give it a name and an expiry.
4. Copy the value — it is shown **once**.
5. In AI2P: **Settings → Catalogs → AI models → Mistral-Large-4 → «Set API key»**.

The key belongs to the organization: it is encrypted with the organization key and replicated
to all servers of the organization (spec ch. 10).

## Limits and price

| | |
|---|---|
| Context | 1,048,576 tokens |
| Maximum output | 262,144 tokens |
| Input price | $0.68 per 1M tokens |
| Output price | $2.09 per 1M tokens |

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

The terms were verified against their text on 2026-10-13.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `api.mistral.ai` is
required.

## Common errors

* **404 "model not found"** — a new checkpoint has come out and the old date is no longer
  served; take the current id from `GET /models`.
* **429 "rate limit exceeded"** — no paid plan is enabled.
* **The key stopped working** — Mistral keys may have an expiry; check it in the console.

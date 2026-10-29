# Kimi-K3.1-Pro

**Hosting:** cloud (Moonshot AI, OpenAI-compatible)
**Connection:** `provider: openai-compatible`, `baseUrl: https://api.moonshot.ai/v1`,
**Key reference:** `moonshot.apiKey`

Moonshot AI released this model on 2026-10-23; it went into the AI2P reference through task
T-653-S0 — the vendor sweep of 2026-10-29. The identifier `moonshotai/kimi-k3.1-pro`, the price and the
modalities were verified by a request to the public OpenRouter catalogue.

A context of 1,048,576 tokens, an answer of up to 943,718 tokens, $2.00 and $10.00 per million
tokens, skill scores 87-92. The closest record of the reference is **Kimi-K3**.

Accepted input: text and Markdown, source code, images, video.

## How to obtain the key

1. Register at [platform.moonshot.ai](https://platform.moonshot.ai/).
2. Top up the balance in the **Billing** section. Without a balance the requests are rejected.
3. Open the [key console](https://platform.moonshot.ai/console/api-keys) →
   **Create API key**, give it a name.
4. Copy the value — it is shown **once**. The key starts with `sk-`.
5. In AI2P: **Settings → Catalogs → AI models → Kimi-K3.1-Pro → «Set API key»**.

The key belongs to the organization: it is encrypted with the organization key and replicated
to all servers of the organization (spec ch. 10).

## Limits and price

| | |
|---|---|
| Context | 1,048,576 tokens |
| Maximum output | 943,718 tokens |
| Input price | $2.00 per 1M tokens |
| Output price | $10.00 per 1M tokens |

**The model identifier has not been verified by a live call** — it is taken from the model
catalogue without the vendor prefix. Check it with a `GET /models` request to
`https://api.moonshot.ai/v1`. Current prices are in the Moonshot console.

## Licence

| | |
|---|---|
| Terms for the generated result | Moonshot **claims no ownership** of the content (“we do not claim ownership of it”) |
| Commercial use | allowed |
| What is required | remember that by default what was sent and received may be used to develop and train Moonshot models — a restriction requires a separate corporate agreement |
| Terms text | <https://platform.kimi.ai/docs/agreement/modeluse> |
| Payment per generation | per token — the rate is in “Limits and price” |

About the weights: Moonshot calls K3.1 Pro an open-weight model, but on 2026-10-29 the
`moonshotai/Kimi-K3.1-Pro` card on HuggingFace is not found by a request (answer 401), so the
weights licence is NOT verified. Its predecessor Kimi K3 has its own licence (`kimi-k3`), not MIT.
For a cloud record this does not matter — you do not receive the weights.

The terms were verified against their text and the weight licence against the model card,
both on 2026-10-29.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `api.moonshot.ai` is
required. The weights are open, but 2.8 trillion parameters cannot be raised on your own
computer — for local work the reference has models of 27–35 billion.

## Common errors

* **401 "invalid api key"** — the key was created on the Chinese site (`moonshot.cn`) while
  `baseUrl` points at the international one; they are not interchangeable.
* **404 "model not found"** — the id has changed; check it against the `GET /models` list.
* **An empty answer with the reason `length`** — raise `params.maxTokens` in the profile
  (32000 by default).

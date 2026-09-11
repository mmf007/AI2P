# Grok-4.6

**Hosting:** cloud (the xAI API, OpenAI-compatible)
**Connection:** `provider: openai-compatible`, `baseUrl: https://api.x.ai/v1`,
model `grok-4.6`
**Key reference:** `xai.apiKey`

The cheapest frontier model: skill scores 86–92 (the level of Sonnet 5) at a price of $2 and
$6 per million tokens. The context is 500,000 tokens. A good choice when you need almost the
best quality at an average price.

Two limitations worth knowing in advance:

* **A request longer than 200,000 tokens is billed entirely at the raised price** — $4 and $12
  per million, that is twice as much. This is not an error in the table below but the xAI
  tariff: **the whole** request becomes more expensive, not just the tail above the threshold.
* **The knowledge of the world goes up to 01.02.2026.** The model does not know what happened
  later; give fresh information in the job.

## How to obtain the key

1. Register in the [xAI console](https://console.x.ai/).
2. Create a team and top up the balance — without a balance the requests are rejected.
3. Open **API Keys → Create API Key**, give it a name and rights for the chat models.
4. Copy the value — it is shown **once**. The key starts with `xai-`.
5. In AI2P: **Settings → Catalogs → AI models → Grok-4.6 → «Set API key»**.

The key belongs to the organization: it is encrypted with the organization key and replicated
to all servers of the organization (spec ch. 10).

## Limits and price

| | |
|---|---|
| Context | 500,000 tokens |
| Maximum output | 64,000 tokens |
| Input price | $2.00 per 1M tokens (above 200,000 tokens per request — $4.00) |
| Output price | $6.00 per 1M tokens (above 200,000 tokens per request — $12.00) |

**The model identifier has not been verified by a live call** — it is taken from the model
catalogue without the vendor prefix. Check it with a `GET /models` request to
`https://api.x.ai/v1`. Current prices — [xAI Pricing](https://x.ai/api).

## Licence

| | |
|---|---|
| Terms for the generated result | **the result is yours**: “Customer … owns all right, title, and interest in the Output”, and xAI assigns you its rights |
| Commercial use | allowed |
| What is required | do not train other models on the result without separate permission and do not misrepresent it as human-generated |
| Terms text | <https://x.ai/legal/terms-of-service-enterprise> (the terms for API access) |
| Payment per generation | per token — the rate is in “Limits and price” |

The weights are closed — this is about the **generated result**.

Under the API terms xAI **does not use what was sent and received to train its models**.
Consumer Grok (the site and the app) is different: there the data goes into training, and
using the Output together with the xAI name and marks requires attributing the generation to
the service. AI2P works through an API key, so the terms linked above apply.

The terms were verified against their text on 2026-08-27.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `api.x.ai` is required.

## Common errors

* **The bill is twice the estimate** — the job crossed the threshold of 200,000 tokens per
  request. Keep the chat history shorter or move material into files.
* **The model "does not know" about a recent event** — the knowledge boundary is 01.02.2026.
* **403 / "no credits"** — the balance of the xAI team has not been topped up.

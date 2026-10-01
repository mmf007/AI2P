# Grok-4.7

**Hosting:** cloud (the xAI API, OpenAI-compatible)
**Connection:** `provider: openai-compatible`, `baseUrl: https://api.x.ai/v1`,
model `grok-4.7`
**Key reference:** `xai.apiKey`

xAI released this model on 2026-09-21; it went into the AI2P reference through task
T-347-S0 — the vendor sweep of 2026-09-23. The identifier `x-ai/grok-4.7`, the context length,
the price and the modalities were verified by a request to the public OpenRouter catalogue.

It continues the **Grok-4.6** line: a context of 500,000 tokens, an answer of up to 64,000
tokens, $1.60 and $4.80 per million tokens. Skill scores 89-93.

Accepted input: text and Markdown, source code, images, PDF.

## How to obtain the key

1. Register in the [xAI console](https://console.x.ai/).
2. Create a team and top up the balance — without a balance the requests are rejected.
3. Open **API Keys → Create API Key**, give it a name and rights for the chat models.
4. Copy the value — it is shown **once**. The key starts with `xai-`.
5. In AI2P: **Settings → Catalogs → AI models → Grok-4.7 → «Set API key»**.

The key belongs to the organization: it is encrypted with the organization key and replicated
to all servers of the organization (spec ch. 10).

## Limits and price

| | |
|---|---|
| Context | 500,000 tokens |
| Maximum output | 64,000 tokens |
| Input price | $1.60 per 1M tokens |
| Output price | $4.80 per 1M tokens |

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

The terms were verified against their text on 2026-09-23.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `api.x.ai` is required.

## Common errors

* **The bill is larger than the estimate** — the job probably crossed the request length
  threshold beyond which xAI charges a raised rate (not published for version 4.7).
  Keep the chat history shorter or move material into files.
* **The model "does not know" about a recent event** — xAI has not announced the knowledge
  boundary of version 4.7; give fresh facts in the job itself.
* **403 / "no credits"** — the balance of the xAI team has not been topped up.

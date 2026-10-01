# GPT-6-Luna

**Hosting:** cloud (the OpenAI API, OpenAI-compatible)
**Connection:** `provider: openai-compatible`, `baseUrl: https://api.openai.com/v1`,
model `gpt-6-luna`
**Key reference:** `openai.apiKey`

OpenAI released this model on 2026-09-22; it went into the AI2P reference through task
T-347-S0 — the vendor sweep of 2026-09-23. The identifier `openai/gpt-6-luna`, the context length,
the price and the modalities were verified by a request to the public OpenRouter catalogue.

It continues the **GPT-6-Astra** line: a context of 1,050,000 tokens, an answer of up to 128,000
tokens, $0.10 and $0.50 per million tokens. Skill scores 83-90.

Accepted input: text and Markdown, source code, images, PDF.

## How to obtain the key

1. Register at [platform.openai.com](https://platform.openai.com/).
2. Top up the balance: **Settings → Billing → Add to credit balance**. Without a balance the
   requests are rejected with a 429.
3. Open [platform.openai.com/api-keys](https://platform.openai.com/api-keys) →
   **Create new secret key**, give it a name.
4. Copy the value — it is shown **once**. The key starts with `sk-`.
5. In AI2P: **Settings → Catalogs → AI models → GPT-6-Luna → «Set API key»**.

The key is shared with GPT-5.6-Terra — both models refer to
`openai.apiKey`. The key belongs to the organization: it is encrypted with the organization
key and replicated to all servers of the organization (spec ch. 10).

## Limits and price

| | |
|---|---|
| Context | 1,00,500 tokens |
| Maximum output | 128,000 tokens |
| Input price | $0.10 per 1M tokens |
| Output price | $0.50 per 1M tokens |

**The model identifier has not been verified by a live call** — it is taken from the model
catalogue and given without the vendor prefix. Before the first job check it with a
`GET /models` request to `https://api.openai.com/v1`. Current prices —
[OpenAI Pricing](https://openai.com/api/pricing/).

## Licence

| | |
|---|---|
| Terms for the generated result | **the result is yours**: “Customer owns all Output”, and OpenAI assigns you its rights in it |
| Commercial use | allowed |
| What is required | comply with the Usage Policies; remember that uniqueness is not promised — another user may receive the same answer |
| Terms text | <https://openai.com/policies/services-agreement/> |
| Payment per generation | per token — the rate is in “Limits and price” |

The weights are closed, there is nothing to licence — the terms cover the **generated
result**.

OpenAI **does not use what goes into the API to develop its services** unless you explicitly
allow it; for consumer ChatGPT that is not the case.

Verified on 2026-09-23: the ownership wording (“you … own the Output. We hereby assign to
you all our right, title, and interest, if any, in and to Output”) was read verbatim in
OpenAI's terms; the same wording is in the API agreement linked above.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `api.openai.com` is
required.

## Common errors

* **404 "model not found"** — the id has changed; check it against the `GET /models` list.
* **429 "insufficient_quota"** — the balance has not been topped up or the organization limit
  is exhausted.
* **An empty answer with the reason `length`** — the model spent the output limit on
  reasoning; raise `params.maxTokens` in the profile (32000 by default).

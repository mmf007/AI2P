# Inkling-2-Small

**Hosting:** cloud (Thinking Machines, **through the OpenRouter gateway**)
**Connection:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
**Key reference:** `openrouter.apiKey`

Thinking Machines released this model on 2026-11-09; it went into the AI2P reference through task
T-736-S0 — the vendor sweep of 2026-11-13. The identifier `thinkingmachines/inkling-2-small`, the price and the
modalities were verified by a request to the public OpenRouter catalogue.

A context of 1,048,576 tokens, an answer of up to 943,718 tokens, $0.30 and $1.00 per million
tokens, skill scores 81-86. The closest record of the reference is **Inkling-975B**.

Accepted input: text and Markdown, source code, images.

## How to obtain the key

**One key for all the models that go through the gateway** —
Muse-Spark-1.2, Nemotron-3-Ultra,
Ling-3.0-Flash and this one refer to the same `openrouter.apiKey`.

1. Register at [openrouter.ai](https://openrouter.ai/).
2. Top up the balance: **Credits → Add credits**.
3. Open [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**.
4. Copy the value (it is shown once and starts with `sk-or-`).
5. In AI2P: **Settings → Catalogs → AI models → Inkling-2-Small → «Set API key»**.

## Limits and price

| | |
|---|---|
| Context | 1,048,576 tokens |
| Maximum output | 943,718 tokens |
| Input price | $0.30 per 1M tokens |
| Output price | $1.00 per 1M tokens |

**The model identifier has not been verified by a live call.** For the models that go through
the gateway it is given as the full slug with the vendor prefix
(`thinkingmachines/inkling-2-small`). Check it with a `GET /models` request to
`https://openrouter.ai/api/v1` — the gateway serves that list even without a key.

## Licence

| | |
|---|---|
| Terms for the generated result | set by the **model owner**, not by the gateway: “Your ownership rights in the Output are set forth in the Model Terms for each Model you use” |
| Commercial use | see the Model Terms on the model card at the gateway — the gateway is responsible for delivery, not for rights |
| What is required | comply with both the gateway terms and the model owner terms; the owner may change theirs at any time |
| Terms text | <https://openrouter.ai/terms> (29 July 2026 version) plus the Model Terms on the model card |
| Payment per generation | per token — the rate is in “Limits and price” |

The model weights are open: the `thinkingmachines/Inkling-2-Small` repository on HuggingFace,
licence **Apache 2.0** (field `cardData.license`, `gated=false`, checked 2026-11-13) —
commercial use is allowed, text: https://www.apache.org/licenses/LICENSE-2.0.
Through the gateway the terms of the model owner and of the gateway apply.

The gateway, for its part, **opts out of training on data** with the connected providers
where possible, but does not vouch for the accuracy of third-party terms and says so
explicitly.

The gateway terms were verified against their text on 2026-11-13.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `openrouter.ai` is
required. The weights are open (Apache 2.0), but 594 billion parameters cannot be raised on
your own computer — for local work the reference has smaller models.

## Common errors

* **404 "No endpoints found"** — the vendor prefix is missing or the gateway has changed the
  slug.
* **402 "Insufficient credits"** — the OpenRouter balance has not been topped up.
* **Invented facts appeared in the result** — that is the model's niche; verify the result or
  take another model for factual jobs.

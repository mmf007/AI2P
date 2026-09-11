# Inkling-975B

**Hosting:** cloud (Thinking Machines, **through the OpenRouter gateway**)
**Connection:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
model `thinkingmachines/inkling`
**Key reference:** `openrouter.apiKey`

An open model of 975 billion parameters (41 billion active) under the **Apache 2.0** licence.
Its authors meant it as **a base for fine-tuning** rather than as a record holder: skill
scores 81–87, a context of 1,048,576 tokens, a price of $0.95 and $4.05 per million. The input
accepts text, sources, pictures and audio.

**Be careful with facts.** In the world-knowledge benchmark (AA Omniscience) the model gives
about 40% accuracy with 63% hallucinations: it confidently invents what it is missing. For the
`analyze-data` and `text-docs` skills take it only where there is something to check the
result against, and where the facts come in the job rather than from the model's memory.

Thinking Machines has no public API of its own, so the record goes through the **OpenRouter**
gateway.

## How to obtain the key

**One key for all the models that go through the gateway** —
Muse-Spark-1.2, Nemotron-3-Ultra,
Ling-3.0-Flash and this one refer to the same `openrouter.apiKey`.

1. Register at [openrouter.ai](https://openrouter.ai/).
2. Top up the balance: **Credits → Add credits**.
3. Open [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**.
4. Copy the value (it is shown once and starts with `sk-or-`).
5. In AI2P: **Settings → Catalogs → AI models → Inkling-975B → «Set API key»**.

## Limits and price

| | |
|---|---|
| Context | 1,048,576 tokens |
| Maximum output | 65,536 tokens |
| Input price | $0.95 per 1M tokens |
| Output price | $4.05 per 1M tokens |

**The model identifier has not been verified by a live call.** For the models that go through
the gateway it is given as the full slug with the vendor prefix
(`thinkingmachines/inkling`). Check it with a `GET /models` request to
`https://openrouter.ai/api/v1` — the gateway serves that list even without a key.

## Licence

| | |
|---|---|
| Terms for the generated result | set by the **model owner**, not by the gateway: “Your ownership rights in the Output are set forth in the Model Terms for each Model you use” |
| Commercial use | see the Model Terms on the model card at the gateway — the gateway is responsible for delivery, not for rights |
| What is required | comply with both the gateway terms and the model owner terms; the owner may change theirs at any time |
| Terms text | <https://openrouter.ai/terms> (29 July 2026 version) plus the Model Terms on the model card |
| Payment per generation | per token — the rate is in “Limits and price” |

The weights of this model are not publicly available (no repository found on HuggingFace,
checked on 2026-08-27) — there is nothing to licence, only the model owner's and the
gateway's terms apply.

The gateway, for its part, **opts out of training on data** with the connected providers
where possible, but does not vouch for the accuracy of third-party terms and says so
explicitly.

The gateway terms were verified against their text on 2026-08-27.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `openrouter.ai` is
required. The weights are open (Apache 2.0), but 975 billion parameters cannot be raised on
your own computer — for local work the reference has smaller models.

## Common errors

* **404 "No endpoints found"** — the vendor prefix is missing or the gateway has changed the
  slug.
* **402 "Insufficient credits"** — the OpenRouter balance has not been topped up.
* **Invented facts appeared in the result** — that is the model's niche; verify the result or
  take another model for factual jobs.

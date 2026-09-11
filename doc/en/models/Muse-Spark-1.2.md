# Muse-Spark-1.2

**Hosting:** cloud (Meta, **through the OpenRouter gateway**)
**Connection:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
model `meta/muse-spark-1.2`
**Key reference:** `openrouter.apiKey`

The first Meta model of the frontier level: skill scores 85–89, a context of 1,048,576 tokens,
a price of $1.25 and $4.25 per million. It accepts almost everything as input — text, sources,
pictures, PDFs, audio and video.

**Meta has no public OpenAI-compatible endpoint of its own**, so the record is connected
through the **OpenRouter** gateway: the requests go to `openrouter.ai`, which passes them on to
the provider. The payment goes by the gateway's tariff (a commission on top), but no separate
key at Meta is needed.

## How to obtain the key

**One key for all the models that go through the gateway** —
Inkling-975B, Nemotron-3-Ultra,
Ling-3.0-Flash and this one refer to the same `openrouter.apiKey`. Enter
it once and all four work.

1. Register at [openrouter.ai](https://openrouter.ai/).
2. Top up the balance: **Credits → Add credits** (card or crypto). Without a balance only the
   free variants of the models with a queue are available.
3. Open [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**, give it a name and,
   if needed, a spending limit for the key.
4. Copy the value — it is shown **once**. The key starts with `sk-or-`.
5. In AI2P: **Settings → Catalogs → AI models → Muse-Spark-1.2 → «Set API key»**.

The key belongs to the organization: it is encrypted with the organization key and replicated
to all servers of the organization (spec ch. 10).

## Limits and price

| | |
|---|---|
| Context | 1,048,576 tokens |
| Maximum output | 65,536 tokens |
| Input price | $1.25 per 1M tokens |
| Output price | $4.25 per 1M tokens |

**The model identifier has not been verified by a live call.** For the models that go through
the gateway it is given as **the full slug with the vendor prefix**
(`meta/muse-spark-1.2`) — without the prefix the gateway answers 404. Check it with a
`GET /models` request to `https://openrouter.ai/api/v1` (the gateway serves that list even
without a key).

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
required.

## Common errors

* **404 "No endpoints found"** — the vendor prefix is missing from the "model" field, or the
  gateway has changed the slug.
* **402 "Insufficient credits"** — the OpenRouter balance has not been topped up.
* **The answer came out more expensive than estimated** — the gateway takes its own commission
  on top of the provider's price.

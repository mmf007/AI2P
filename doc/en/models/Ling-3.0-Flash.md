# Ling-3.0-Flash

**Hosting:** cloud (Ant Group, **through the OpenRouter gateway**)
**Connection:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
model `inclusionai/ling-3.0-flash`
**Key reference:** `openrouter.apiKey`

**The cheapest record of the reference: $0.021 per 1M input tokens** — roughly a hundred and
fifty times cheaper than Kimi-K3. A model of 124 billion parameters (5.1 billion
active), aimed at high-frequency agent scenarios: many short moves in a row.

Its niche in AI2P is a **cheap background** for multi-move jobs: drafts, bulk small edits,
preliminary markup of material. The skill scores are 74–82, and the context here is smaller
than that of its neighbours — 262,144 tokens.

Ant Group has no public API of its own, so the record goes through the **OpenRouter** gateway.

## How to obtain the key

**One key for all the models that go through the gateway** —
Muse-Spark-1.2, Inkling-975B,
Nemotron-3-Ultra and this one refer to the same `openrouter.apiKey`.

1. Register at [openrouter.ai](https://openrouter.ai/).
2. Top up the balance: **Credits → Add credits**. The minimum top-up lasts a long time: a
   million input tokens costs about two cents.
3. Open [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**.
4. Copy the value (it is shown once and starts with `sk-or-`).
5. In AI2P: **Settings → Catalogs → AI models → Ling-3.0-Flash → «Set API key»**.

## Limits and price

| | |
|---|---|
| Context | 262,144 tokens |
| Maximum output | 32,768 tokens |
| Input price | $0.021 per 1M tokens |
| Output price | $0.063 per 1M tokens |

**The model identifier has not been verified by a live call.** For the models that go through
the gateway it is given as the full slug with the vendor prefix (`inclusionai/ling-3.0-flash`).
Check it with a `GET /models` request to `https://openrouter.ai/api/v1`.

## Licence

| | |
|---|---|
| Terms for the generated result | set by the **model owner**, not by the gateway: “Your ownership rights in the Output are set forth in the Model Terms for each Model you use” |
| Commercial use | see the Model Terms on the model card at the gateway — the gateway is responsible for delivery, not for rights |
| What is required | comply with both the gateway terms and the model owner terms; the owner may change theirs at any time |
| Terms text | <https://openrouter.ai/terms> (29 July 2026 version) plus the Model Terms on the model card |
| Payment per generation | per token — the rate is in “Limits and price” |

The weights of this model are open too: **MIT** (the `inclusionAI/Ling-3.0-flash` card,
verified on 2026-08-27) — commercial use is allowed, the licence text and the copyright
notice must be kept. So the model can be run on your own hardware; this catalogue record
goes through the gateway, and the model owner's terms apply to the result.

The gateway, for its part, **opts out of training on data** with the connected providers
where possible, but does not vouch for the accuracy of third-party terms and says so
explicitly.

The gateway terms were verified against their text on 2026-08-27.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `openrouter.ai` is
required.

## Common errors

* **The auto-pick keeps choosing exactly this one** — the project's "price ↔ quality" slider
  is moved towards price (spec §2.7). Move it towards quality or set the executor by hand.
* **The input is too long** — the context is 262,144 tokens, four times less than that of the
  flagships.
* **404 "No endpoints found"** — the vendor prefix is missing or the gateway has changed the
  slug.

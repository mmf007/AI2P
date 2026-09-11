# Nemotron-3-Ultra

**Hosting:** cloud (NVIDIA, **through the OpenRouter gateway**)
**Connection:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
model `nvidia/nemotron-3-ultra-550b-a55b`
**Key reference:** `openrouter.apiKey`

The most open record of the reference: NVIDIA published not only the weights but also **the
training data and recipes** under the OpenMDW-1.1 licence. The architecture is a hybrid —
Mamba-2 plus Transformer, 550 billion parameters (55 billion active). Skill scores 76–82, a
context of 512,288 tokens, a price of $0.60 and $3.60 per million.

The niche is jobs where reproducibility and openness matter more than a quality record.
A **free variant** of this model is also available through the gateway (with a queue and a
rate limit): it has a different slug — create your own reference record if you want it.

NVIDIA has no public API of its own for this model, so the record goes through the
**OpenRouter** gateway.

## How to obtain the key

**One key for all the models that go through the gateway** —
Muse-Spark-1.2, Inkling-975B,
Ling-3.0-Flash and this one refer to the same `openrouter.apiKey`.

1. Register at [openrouter.ai](https://openrouter.ai/).
2. Top up the balance: **Credits → Add credits**.
3. Open [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**.
4. Copy the value (it is shown once and starts with `sk-or-`).
5. In AI2P: **Settings → Catalogs → AI models → Nemotron-3-Ultra → «Set API key»**.

## Limits and price

| | |
|---|---|
| Context | 512,288 tokens |
| Maximum output | 65,536 tokens |
| Input price | $0.60 per 1M tokens |
| Output price | $3.60 per 1M tokens |

**The model identifier has not been verified by a live call.** For the models that go through
the gateway it is given as the full slug with the vendor prefix
(`nvidia/nemotron-3-ultra-550b-a55b`). Check it with a `GET /models` request to
`https://openrouter.ai/api/v1`.

## Licence

| | |
|---|---|
| Terms for the generated result | set by the **model owner**, not by the gateway: “Your ownership rights in the Output are set forth in the Model Terms for each Model you use” |
| Commercial use | see the Model Terms on the model card at the gateway — the gateway is responsible for delivery, not for rights |
| What is required | comply with both the gateway terms and the model owner terms; the owner may change theirs at any time |
| Terms text | <https://openrouter.ai/terms> (29 July 2026 version) plus the Model Terms on the model card |
| Payment per generation | per token — the rate is in “Limits and price” |

This is a special case: the **weights are published openly, under OpenMDW-1.1** (the
`nvidia/NVIDIA-Nemotron-3-Ultra-550B-A55B-BF16` card, licence text at
<https://openmdw.ai/license/1-1/>, verified on 2026-08-27). The licence is open and allows
commercial use, and along with the weights NVIDIA published the training data and recipes.
So this model can be run on your own hardware, with no dependence on the gateway terms at
all.

The gateway, for its part, **opts out of training on data** with the connected providers
where possible, but does not vouch for the accuracy of third-party terms and says so
explicitly.

The gateway terms were verified against their text on 2026-08-27.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `openrouter.ai` is
required. The weights are open, but 550 billion parameters need a server rack — on your own
computer take the local records of the reference.

## Common errors

* **404 "No endpoints found"** — the vendor prefix is missing or the slug has changed.
* **The job hangs and ends on a timeout** — you landed on the free variant with a queue; check
  that the "model" field holds the paid slug and that there are funds on the account.
* **402 "Insufficient credits"** — the OpenRouter balance has not been topped up.

# Nemotron-3.5-Super-VL

**Hosting:** cloud (NVIDIA, **through the OpenRouter gateway**)
**Connection:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
model `nvidia/nemotron-3.5-super-vl`
**Key reference:** `openrouter.apiKey`

NVIDIA released this model on 2026-10-15; it went into the AI2P reference through task
T-558-S0 — the vendor sweep of 2026-10-20. The identifier `nvidia/nemotron-3.5-super-vl`, the price and the
modalities were verified by a request to the public OpenRouter catalogue.

A context of 262,144 tokens, an answer of up to 235,929 tokens, $0.15 and $0.50 per million
tokens, skill scores 77-83. The closest record of the reference is **Nemotron-3-Ultra**.

Accepted input: text and Markdown, source code, images, video.

## How to obtain the key

**One key for all the models that go through the gateway** —
Muse-Spark-1.2, Inkling-975B,
Ling-3.0-Flash and this one refer to the same `openrouter.apiKey`.

1. Register at [openrouter.ai](https://openrouter.ai/).
2. Top up the balance: **Credits → Add credits**.
3. Open [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**.
4. Copy the value (it is shown once and starts with `sk-or-`).
5. In AI2P: **Settings → Catalogs → AI models → Nemotron-3.5-Super-VL → «Set API key»**.

## Limits and price

| | |
|---|---|
| Context | 262,144 tokens |
| Maximum output | 235,929 tokens |
| Input price | $0.15 per 1M tokens |
| Output price | $0.50 per 1M tokens |

**The model identifier has not been verified by a live call.** For the models that go through
the gateway it is given as the full slug with the vendor prefix
(`nvidia/nemotron-3.5-super-vl`). Check it with a `GET /models` request to
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
`nvidia/NVIDIA-Nemotron-3.5-Super-VL-120B-A12B-BF16` card, licence text at
<https://openmdw.ai/license/1-1/>, verified on 2026-10-20). The licence is open and allows
commercial use.
So this model can be run on your own hardware, with no dependence on the gateway terms at
all.

The gateway, for its part, **opts out of training on data** with the connected providers
where possible, but does not vouch for the accuracy of third-party terms and says so
explicitly.

The gateway terms were verified against their text on 2026-10-20.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `openrouter.ai` is
required. The weights are open, but 120 billion parameters need a server rack — on your own
computer take the local records of the reference.

## Common errors

* **404 "No endpoints found"** — the vendor prefix is missing or the slug has changed.
* **The job hangs and ends on a timeout** — you landed on the free variant with a queue; check
  that the "model" field holds the paid slug and that there are funds on the account.
* **402 "Insufficient credits"** — the OpenRouter balance has not been topped up.

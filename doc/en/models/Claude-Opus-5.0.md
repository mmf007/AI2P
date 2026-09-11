# Claude-Opus-5.0

**Hosting:** cloud (the API of the provider Anthropic)
**Connection:** `provider: anthropic`, model `claude-opus-5`
**Key reference:** `anthropic.apiKey`

The workhorse: noticeably cheaper than Claude-Fable-5 at close quality
on most tasks. It is also the model Claude-Fable-5 falls back to when the safety classifiers
refuse.

## How to obtain the key

**The key is the same one** as for Claude-Fable-5 — both models go to the same provider and
refer to the same secret, `anthropic.apiKey`. If the key has already been entered for one of
them, the other one will work by itself.

If there is no key yet:

1. Register at the [Anthropic Console](https://console.anthropic.com/).
2. Top up the balance: **Plan & Billing → Add credits**.
3. **Settings → API keys → Create Key**, copy the value (it is shown once and starts with
   `sk-ant-`).
4. In AI2P: **Settings → Catalogs → AI models → Claude-Opus-5.0 → «Set API key»**.

The key belongs to the organization and is replicated to its servers encrypted (spec ch. 10).

## Limits and price

| | |
|---|---|
| Context | 1,000,000 tokens |
| Maximum output | 128,000 tokens |
| Input price | $5.00 per 1M tokens |
| Output price | $25.00 per 1M tokens |

Twice as cheap as Claude-Fable-5. Current prices — [Anthropic Pricing](https://www.anthropic.com/pricing).

**The model identifier has not been verified by a live call.** Before the first job check it
with a `GET /models` request to the provider API: at Anthropic the checkpoint names change,
and a wrong id gives a 404 right on the executor.

## Licence

| | |
|---|---|
| Terms for the generated result | **the result is yours**: Anthropic assigns you all its rights in Outputs (Commercial Terms, section B) |
| Commercial use | allowed — these are the terms for organizations; the consumer terms do not apply to the API |
| What is required | comply with the Usage Policy; you may not build a competing product on the service, train competing models on it, or resell access |
| Terms text | <https://www.anthropic.com/legal/commercial-terms> (17 June 2025 version) |
| Payment per generation | per token — the rate is in “Limits and price” |

The weights are closed and are not handed out to anyone — there is nothing to licence here,
so the terms cover the **generated result**, not the weights.

Under the same terms Anthropic **does not train models on what goes into the API**
(“Anthropic may not train models on Customer Content from Services”). The subscription
variant of the same model (the record with the `_cli` suffix) is governed by DIFFERENT terms
— the consumer ones, where training on materials continues until you opt out in the account
settings.

The terms were verified against their text on 2026-08-27; the provider may change them, so
open the link again before a commercial release.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `api.anthropic.com`
is required.

## Which of the two to take, and when

* **Claude-Fable-5** — the head tasks of a process, requirements analysis, planning,
  complex code.
* **Claude-Opus-5.0** — everything else: edits, documentation, translations, reviews.

You do not have to make the choice by hand: a project has a **price ↔ quality** slider, and
automatic executor selection takes into account both the skill scores of the model and its
price (spec 2.7).

## An alternative without a key

**Claude-Opus-5.0_cli** — the same model through Claude Code CLI on a subscription, without
an API key.

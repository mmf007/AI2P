# Claude-Haiku-4.5

**Hosting:** cloud (the Anthropic API)
**Connection:** `provider: anthropic`, model `claude-haiku-4-5`
**Key reference:** `anthropic.apiKey`

The cheapest Claude in the reference: $1 and $5 per million tokens against $2 and $10 for
Claude-Sonnet-5. The skill scores are 70–80 — that is **short routine**:
translations, summaries, small text edits, simple checks. It is not worth taking for complex
code or requirements analysis; Sonnet and Opus are there for that.

The second limitation is the context of **200,000 tokens** instead of a million: this model
will not cope with a task that has a very large description or a long chat history.

## How to obtain the key

1. Create an organization at [console.anthropic.com](https://console.anthropic.com/).
2. Top up the balance: **Billing → Add credits**.
3. Open **API keys → Create Key**, give it a name.
4. Copy the value — it is shown **once** (it starts with `sk-ant-`).
5. In AI2P: **Settings → Catalogs → AI models → Claude-Haiku-4.5 → «Set API key»**.

The key is shared with all Anthropic models (`anthropic.apiKey`): enter it once and Fable,
Opus, Sonnet and Haiku all work.

## Limits and price

| | |
|---|---|
| Context | 200,000 tokens |
| Maximum output | 64,000 tokens |
| Input price | $1.00 per 1M tokens |
| Output price | $5.00 per 1M tokens |

**The model identifier has not been verified by a live call.** Before the first job check it
with a `GET /models` request to the provider API. Current prices —
[Anthropic Pricing](https://www.anthropic.com/pricing).

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

None: the computation happens on the provider side. Internet access to `api.anthropic.com` is
required.

## Common errors

* **The input is too long** — 200,000 tokens run out faster than it seems: move bulky material
  into files and give links in the description.
* **The result is noticeably worse than expected** — the task is harder than the model's
  niche; set the executor by hand or move the project's "price ↔ quality" slider towards
  quality.
* **401 / "invalid x-api-key"** — the key was entered with a space or truncated.

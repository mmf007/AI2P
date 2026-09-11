# Claude-Sonnet-5

**Hosting:** cloud (the Anthropic API)
**Connection:** `provider: anthropic`, model `claude-sonnet-5`
**Key reference:** `anthropic.apiKey`

The workhorse of the Claude 5 line: the quality is close to
Claude-Opus-5.0 (skill scores 89–93 against 95–98), while the price is
twice as low and **constant** — $2 per 1M input tokens and $10 per 1M output tokens, with no
time-of-day discounts. A sensible choice when Opus is overkill and
Claude-Haiku-4.5 can no longer cope.

## How to obtain the key

1. Create an organization at [console.anthropic.com](https://console.anthropic.com/).
2. Top up the balance: **Billing → Add credits**. Without a balance the requests are rejected.
3. Open **API keys → Create Key**, give it a name.
4. Copy the value — it is shown **once**. The key starts with `sk-ant-`.
5. In AI2P: **Settings → Catalogs → AI models → Claude-Sonnet-5 → «Set API key»**.

The key is shared with all Anthropic models — Claude-Fable-5,
Claude-Opus-5.0, Claude-Haiku-4.5: all of them
refer to `anthropic.apiKey`. Enter it once and they all work.

The key belongs to the organization: it is encrypted with the organization key and replicated
to all servers of the organization (spec ch. 10).

## Limits and price

| | |
|---|---|
| Context | 1,000,000 tokens |
| Maximum output | 128,000 tokens |
| Input price | $2.00 per 1M tokens |
| Output price | $10.00 per 1M tokens |

The profile carries `params.maxTokens: 16000` and `effort: high` — that is the limit of **a
single answer**, not of the context; raise it if the result is cut off with the reason
`length`.

**The model identifier has not been verified by a live call.** Before the first job check it
with a `GET /models` request to the provider API: at Anthropic the checkpoint names change,
and a wrong id gives a 404 right on the executor. Current prices —
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

* **401 / "invalid x-api-key"** — the key was entered with a space or truncated.
* **400 "credit balance is too low"** — the balance of the Anthropic organization has not been
  topped up.
* **An empty answer with the reason `length`** — `params.maxTokens` in the model profile is
  too small.
* **A job costs more than expected** — count by both numbers: a long chat history goes into
  the input at every move of the agent.

# Claude-Fable-5

**Hosting:** cloud (the API of the provider Anthropic)
**Connection:** `provider: anthropic`, model `claude-fable-5`
**Key reference:** `anthropic.apiKey`

The strongest of the connected models: code, texts, requirements analysis and planning.
It makes sense to put the head tasks of a process and requirements analysis on it and give
the routine to cheaper models — the "price ↔ quality" ratio is configured per project.

## How to obtain the key

1. Register at the [Anthropic Console](https://console.anthropic.com/).
2. Top up the balance: **Plan & Billing → Add credits**. Without a positive balance the key
   is created, but requests are rejected with an insufficient-funds error.
3. Open **Settings → API keys → Create Key**, give the key a meaningful name
   (for example `AI2P-work`).
4. Copy the value — it is shown **once**. The key starts with `sk-ant-`.
5. In AI2P: **Settings → Catalogs → AI models → Claude-Fable-5 → «Set API key»**.
   The value is entered in a password field and is never shown anywhere again.

The key belongs to the **organization**: it is encrypted with the organization key and
replicated to all of its servers, so there is no need to enter it on every server. Details —
spec ch. 10.

As soon as the key is set, the model becomes active. Without a key a cloud model cannot be
active.

## Limits and price

| | |
|---|---|
| Context | 1,000,000 tokens |
| Maximum output | 128,000 tokens |
| Input price | $10.00 per 1M tokens |
| Output price | $50.00 per 1M tokens |

The cost of every job is computed from these numbers and goes into billing (spec 6.2-bis).
For current prices see the [Anthropic Pricing](https://www.anthropic.com/pricing) page — if
they have changed, correct `cost` in the capability declaration of the model.

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

None: the computation happens on the provider side. All that is needed is internet access to
`api.anthropic.com`.

## Common errors

* **"API key not found"** — the key has not been entered, or this server has not received
  the organization key yet (it is issued when the connection of the server to the
  organization is confirmed).
* **401 from the provider** — the key has been revoked or was copied incompletely.
* **"37529 tokens exceeds context"** — a task with a huge description; reduce the input or
  raise `params.maxTokens` in the profile.
* **A refusal from the safety classifiers** — the profile declares a fallback,
  `params.fallbacks: ["claude-opus-5"]`: the request is retried automatically with another
  model.

## An alternative without a key

There is a variant of the same family connected over the CLI — **Claude-Fable-5_cli**: it
works through a Claude Code subscription and needs no API key. See its document.

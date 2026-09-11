# DeepSeek-V4-Pro

**Hosting:** cloud (the DeepSeek API, OpenAI-compatible)
**Connection:** `provider: openai-compatible`, `baseUrl: https://api.deepseek.com`,
model `deepseek-v4-pro`
**Key reference:** `deepseek.apiKey`

A cheap model of decent level: code and texts at 84–89 out of 100 by the skill scores, at
roughly a twentieth of the price of Claude-Fable-5. A good choice for bulk routine.

## How to obtain the key

1. Register at [platform.deepseek.com](https://platform.deepseek.com/).
2. Top up the balance: **Top up** (payment by card). Without a balance the requests are
   rejected.
3. Open **API keys → Create new API key**, give it a name.
4. Copy the value — it is shown **once**. The key starts with `sk-`.
5. In AI2P: **Settings → Catalogs → AI models → DeepSeek-V4-Pro → «Set API key»**.

The key is shared with DeepSeek-V4-Flash — both models refer to
`deepseek.apiKey`. Enter it once and both work.

The key belongs to the organization: it is encrypted with the organization key and replicated
to all servers of the organization (spec ch. 10).

## Limits and price

| | |
|---|---|
| Context | 1,000,000 tokens |
| Maximum output | 65,536 tokens |
| Input price | $0.66 per 1M tokens |
| Output price | $1.98 per 1M tokens |

The numbers are updated for the **V4-Pro-0813** checkpoint: the context grew from 128,000 to a
million, the maximum output from 8,192 to 65,536 tokens, and the output even became cheaper
(it used to be $0.55 and $2.19). The former limitation "the context is an order of magnitude
smaller than that of Anthropic" is gone: it is now the same as that of
Claude-Sonnet-5.

The profile carries `params.maxTokens: 32768` — that is the limit of **a single answer**. If
the result is cut off with the reason `length`, raise it (the ceiling of the model is 65,536)
or split the task.

**The model identifier has not been verified by a live call** — check it with a `GET /models`
request to `https://api.deepseek.com`. Current prices —
[DeepSeek Pricing](https://api-docs.deepseek.com/quick_start/pricing).

## Licence

| | |
|---|---|
| Terms for the generated result | **the result is yours**: “We assign any rights, title, and interests — if any — in the Outputs … to you” (clause 4.2) |
| Commercial use | allowed explicitly and broadly: personal use, research, derivative product development and even training other models (distillation) |
| What is required | disclose to your users that the content is AI-generated (clause 8.1); do not use the DeepSeek brand without permission |
| Terms text | <https://cdn.deepseek.com/policies/en-US/deepseek-open-platform-terms-of-service.html> |
| Payment per generation | per token — the rate is in “Limits and price” |

The weights of a cloud record are not handed to you, so this is about the **generated
result**.

These are the most generous terms among the cloud records of the catalogue: permission to
train other models on the result is written into the text, while most providers forbid
exactly that. There is one duty, and it is easy to miss — mark for the end user that the
text was made by AI.

The terms were verified against their text on 2026-08-27.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `api.deepseek.com` is
required.

## Common errors

* **402 / "Insufficient Balance"** — the balance has not been topped up.
* **An empty answer with the reason `length`** — the model spent the output limit on
  reasoning; raise `params.maxTokens` in the model profile.
* **The input is too long** — a million tokens seems endless, but the chat history goes into
  the input at every move of the agent; move bulky material into files and give links in the
  description.

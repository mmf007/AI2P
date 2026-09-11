# DeepSeek-V4-Flash

**Hosting:** cloud (the DeepSeek API, OpenAI-compatible)
**Connection:** `provider: openai-compatible`, `baseUrl: https://api.deepseek.com`,
model `deepseek-v4-flash`
**Key reference:** `deepseek.apiKey`

Fast and very cheap: the quality is lower than that of
DeepSeek-V4-Pro (skill scores 76–84 against 84–89). Take it for simple
bulk work: translations, short summaries, drafts, small edits. The cheapest record of the
reference is no longer this one but Ling-3.0-Flash — $0.021 per million
input tokens.

## How to obtain the key

**The key is the same one** as for DeepSeek-V4-Pro: both models refer to `deepseek.apiKey`.
If it has already been entered, this model will work by itself.

If there is no key yet:

1. Register at [platform.deepseek.com](https://platform.deepseek.com/).
2. Top up the balance (**Top up**).
3. **API keys → Create new API key**, copy the value (it is shown once).
4. In AI2P: **Settings → Catalogs → AI models → DeepSeek-V4-Flash → «Set API key»**.

## Limits and price

| | |
|---|---|
| Context | 1,000,000 tokens |
| Maximum output | 32,768 tokens |
| Input price | $0.14 per 1M tokens |
| Output price | $0.28 per 1M tokens |

The numbers are updated: the context grew from 128,000 to a million, the maximum output from
8,192 to 32,768 tokens, and the price fell by two to four times (it used to be $0.27 and
$1.10). The output is now **seven times cheaper** than that of
DeepSeek-V4-Pro, with the same context.

The profile carries `params.maxTokens: 32768` — that is the ceiling of a single answer for
this model.

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

## When to take it

If the **price ↔ quality** slider of the project is moved towards price, automatic executor
selection will pick this model by itself (spec 2.7). If a task needs accuracy, assign the
executor by hand or move the slider towards quality.

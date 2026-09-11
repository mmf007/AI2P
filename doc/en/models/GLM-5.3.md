# GLM-5.3

**Hosting:** cloud (Z.ai / Zhipu, OpenAI-compatible)
**Connection:** `provider: openai-compatible`, `baseUrl: https://api.z.ai/api/paas/v4`,
model `glm-5.3`
**Key reference:** `zai.apiKey`

Z.ai (Zhipu) released this model on 2026-08-18; it went into the AI2P reference through task
T-216-S0 — the vendor sweep of 2026-09-11. The identifier `z-ai/glm-5.3`, the context length,
the price and the modalities were verified by a request to the public OpenRouter catalogue.

It continues the **GLM-5.2** line: a context of 1,048,576 tokens, an answer of up to 65,536
tokens, $1.40 and $4.40 per million tokens. Skill scores 84-93.

Accepted input: text and Markdown, source code.

## How to obtain the key

1. Register at [z.ai](https://z.ai/) (the international site of Zhipu).
2. Top up the balance in the billing section — without a balance the requests are rejected.
3. Open the [key list](https://z.ai/manage-apikey/apikey-list) → **Create API key**, give it a
   name.
4. Copy the value — it is shown **once**.
5. In AI2P: **Settings → Catalogs → AI models → GLM-5.3 → «Set API key»**.

The key belongs to the organization: it is encrypted with the organization key and replicated
to all servers of the organization (spec ch. 10).

## Limits and price

| | |
|---|---|
| Context | 1,048,576 tokens |
| Maximum output | 65,536 tokens |
| Input price | $1.40 per 1M tokens |
| Output price | $4.40 per 1M tokens |

**The model identifier has not been verified by a live call** — it is taken from the model
catalogue without the vendor prefix. Check it with a `GET /models` request to
`https://api.z.ai/api/paas/v4`.

Note the unusual `baseUrl`: the path is `/api/paas/v4`, not `/v1`. If you copied the address
from someone else's instructions and get a 404 on every request — that is most likely the
reason.

## Licence

| | |
|---|---|
| Terms for the generated result | **rights in prompts and outputs stay with you** (Z.AI Terms of Use, section IV) |
| Commercial use | allowed |
| What is required | do not remove the AI identifiers Z.ai adds, mark generated content as AI-made, and do not pass it off as human work |
| Terms text | <https://docs.z.ai/legal-agreement/terms-of-use> |
| Payment per generation | per token — the rate is in “Limits and price” |

For individual (non-corporate) users Z.ai may use what was sent and received to develop the
service — if that is unacceptable, a corporate contract is needed.

Separately about the weights: **GLM-5.3 is published under MIT** (the `zai-org/GLM-5.3`
model card, verified on 2026-09-11), so the model can be run on your own hardware too. This
catalogue record goes to the Z.ai cloud, where the terms above apply rather than MIT.

The terms were verified against their text on 2026-09-11.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `api.z.ai` is required.

## Common errors

* **404 on every request** — the path in `baseUrl` was truncated or extended (it must be
  `/api/paas/v4`).
* **401 "invalid api key"** — the key was created on the Chinese site (`bigmodel.cn`) while
  the address is the international one.
* **The model refuses to accept a picture** — the input is text only; for multimodal jobs take
  MiniMax-M3 or Gemini.

# GLM-5.2

**Hosting:** cloud (Z.ai / Zhipu, OpenAI-compatible)
**Connection:** `provider: openai-compatible`, `baseUrl: https://api.z.ai/api/paas/v4`,
model `glm-5.2`
**Key reference:** `zai.apiKey`

**The best code among the open models after Kimi-K3** — the `code-write` score
is 91 out of 100, and it is three times cheaper at that: $1.19 and $3.74 per million tokens.
The context is 1,048,576 tokens. The niche is obvious: bulk work with code where Opus and
GPT-5.6-Sol are too expensive.

Texts come out worse than code (scores 83–86), and the input is text and sources only — the
model does not accept pictures.

## How to obtain the key

1. Register at [z.ai](https://z.ai/) (the international site of Zhipu).
2. Top up the balance in the billing section — without a balance the requests are rejected.
3. Open the [key list](https://z.ai/manage-apikey/apikey-list) → **Create API key**, give it a
   name.
4. Copy the value — it is shown **once**.
5. In AI2P: **Settings → Catalogs → AI models → GLM-5.2 → «Set API key»**.

The key belongs to the organization: it is encrypted with the organization key and replicated
to all servers of the organization (spec ch. 10).

## Limits and price

| | |
|---|---|
| Context | 1,048,576 tokens |
| Maximum output | 65,536 tokens |
| Input price | $1.19 per 1M tokens |
| Output price | $3.74 per 1M tokens |

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

Separately about the weights: **GLM-5.2 is published under MIT** (the `zai-org/GLM-5.2`
model card, verified on 2026-08-27), so the model can be run on your own hardware too. This
catalogue record goes to the Z.ai cloud, where the terms above apply rather than MIT.

The terms were verified against their text on 2026-08-27.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `api.z.ai` is required.

## Common errors

* **404 on every request** — the path in `baseUrl` was truncated or extended (it must be
  `/api/paas/v4`).
* **401 "invalid api key"** — the key was created on the Chinese site (`bigmodel.cn`) while
  the address is the international one.
* **The model refuses to accept a picture** — the input is text only; for multimodal jobs take
  MiniMax-M3 or Gemini.

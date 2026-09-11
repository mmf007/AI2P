# Kimi-K3

**Hosting:** cloud (Moonshot AI, OpenAI-compatible)
**Connection:** `provider: openai-compatible`, `baseUrl: https://api.moonshot.ai/v1`,
model `kimi-k3`
**Key reference:** `moonshot.apiKey`

**The best open model by the Artificial Analysis composite index** at the moment the reference
was extended: 2.8 trillion parameters (104 billion active), skill scores 85–90 — right up
against the closed flagships. The context is 1,048,576 tokens, the input accepts pictures and
video. The price is $3 and $15 per million: more expensive than the Chinese neighbours, but the
quality is higher too.

**The licence is its own — the Kimi K3 License, not MIT and not Apache.** The weights are
open, but read the terms of commercial use separately; "open" here does not mean "do whatever
you like".

## How to obtain the key

1. Register at [platform.moonshot.ai](https://platform.moonshot.ai/).
2. Top up the balance in the **Billing** section. Without a balance the requests are rejected.
3. Open the [key console](https://platform.moonshot.ai/console/api-keys) →
   **Create API key**, give it a name.
4. Copy the value — it is shown **once**. The key starts with `sk-`.
5. In AI2P: **Settings → Catalogs → AI models → Kimi-K3 → «Set API key»**.

The key belongs to the organization: it is encrypted with the organization key and replicated
to all servers of the organization (spec ch. 10).

## Limits and price

| | |
|---|---|
| Context | 1,048,576 tokens |
| Maximum output | 65,536 tokens |
| Input price | $3.00 per 1M tokens |
| Output price | $15.00 per 1M tokens |

**The model identifier has not been verified by a live call** — it is taken from the model
catalogue without the vendor prefix. Check it with a `GET /models` request to
`https://api.moonshot.ai/v1`. Current prices are in the Moonshot console.

## Licence

| | |
|---|---|
| Terms for the generated result | Moonshot **claims no ownership** of the content (“we do not claim ownership of it”) |
| Commercial use | allowed |
| What is required | remember that by default what was sent and received may be used to develop and train Moonshot models — a restriction requires a separate corporate agreement |
| Terms text | <https://platform.kimi.ai/docs/agreement/modeluse> |
| Payment per generation | per token — the rate is in “Limits and price” |

Separately about the weights, because it is easy to get this wrong: Kimi K3 has **its own
licence** (the `moonshotai/Kimi-K3` card calls it `kimi-k3`), not MIT and not Apache 2.0.
The common “an open model means MIT” is false here. For a cloud record it does not matter
(you do not get the weights), but it will if you decide to run the model yourself.

The terms were verified against their text and the weight licence against the model card,
both on 2026-08-27.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `api.moonshot.ai` is
required. The weights are open, but 2.8 trillion parameters cannot be raised on your own
computer — for local work the reference has models of 27–35 billion.

## Common errors

* **401 "invalid api key"** — the key was created on the Chinese site (`moonshot.cn`) while
  `baseUrl` points at the international one; they are not interchangeable.
* **404 "model not found"** — the id has changed; check it against the `GET /models` list.
* **An empty answer with the reason `length`** — raise `params.maxTokens` in the profile
  (32000 by default).

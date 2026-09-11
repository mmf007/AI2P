# Qwen3.8-Max

**Hosting:** cloud (Alibaba DashScope, OpenAI-compatible)
**Connection:** `provider: openai-compatible`,
`baseUrl: https://dashscope-intl.aliyuncs.com/compatible-mode/v1`, model `qwen3.8-max`
**Key reference:** `qwen.apiKey`

The leader of the **OSWorld-Verified (86.1)** benchmark — that is, "working at a computer":
multi-step actions with files, tools and interfaces. This is exactly the scenario of an AI2P
agent, so the model is more useful than its general skill scores (86–92) suggest. The price is
$2 and $6 per million tokens with a context of 1,000,000; the input accepts pictures and
video.

**The address in the reference is the international one** (`dashscope-intl`). Inside China a
different host works — `dashscope.aliyuncs.com`; change `baseUrl` in the model profile if you
need it.

## How to obtain the key

1. Register in [Alibaba Cloud Model Studio](https://modelstudio.console.alibabacloud.com/)
   (the international site).
2. Activate the model service and link a payment method.
3. Open **API-KEY → Create API Key**, pick a workspace.
4. Copy the value — it is shown **once**. The key starts with `sk-`.
5. In AI2P: **Settings → Catalogs → AI models → Qwen3.8-Max → «Set API key»**.

The key belongs to the organization: it is encrypted with the organization key and replicated
to all servers of the organization (spec ch. 10). The **local** Qwen models of the reference
have no key at all — they run on your own computer.

## Limits and price

| | |
|---|---|
| Context | 1,000,000 tokens |
| Maximum output | 65,536 tokens |
| Input price | $2.00 per 1M tokens |
| Output price | $6.00 per 1M tokens |

**The model identifier has not been verified by a live call** — it is taken from the model
catalogue without the vendor prefix. Check it with a `GET /models` request to
`https://dashscope-intl.aliyuncs.com/compatible-mode/v1`. Current prices are in the Model
Studio console.

## Licence

| | |
|---|---|
| Terms for the generated result | set by the Model Studio contract: paid access by key gives you the right to use the result, and the responsibility for it is yours |
| Commercial use | allowed on paid access; content obtained in the console **trial** mode may be used only to evaluate the model |
| What is required | follow the Model Studio rules and be responsible for the legality of what is generated: the provider gives no warranties about the content |
| Terms text | <https://www.alibabacloud.com/help/en/model-studio/related-agreements> (the list of governing agreements) |
| Payment per generation | per token — the rate is in “Limits and price” |

The `qwen3.8-max` weights are closed (unlike the smaller Qwen models, which sit under Apache
2.0), so this is about the **generated result**.

Verified on 2026-08-27: the Model Studio list-of-agreements page was read (last updated
2026-06-30; the Terms of Service, the Service Level Agreement and the Open Source Model
License Terms apply); the text of the Terms of Service itself could not be read by machine —
open it via the link above and read it yourself, especially the part about trial mode.

## Hardware requirements

None: the computation happens on the provider side. Internet access to
`dashscope-intl.aliyuncs.com` is required.

## Common errors

* **404 or "model not exist"** — either the id has changed or the wrong host was taken (the
  international one against the Chinese one).
* **401 "InvalidApiKey"** — the key was created in a different workspace.
* **An empty answer with the reason `length`** — raise `params.maxTokens` in the profile
  (32000 by default).

# Qwen3.8-Omni-Flash

**Hosting:** cloud (Alibaba DashScope, OpenAI-compatible)
**Connection:** `provider: openai-compatible`,
`baseUrl: https://dashscope-intl.aliyuncs.com/compatible-mode/v1`, model `qwen3.8-omni-flash`
**Key reference:** `qwen.apiKey`

Alibaba released this model on 2026-09-21; it went into the AI2P reference through task
T-347-S0 — the vendor sweep of 2026-09-23. The identifier `qwen/qwen3.8-omni-flash`, the context length,
the price and the modalities were verified by a request to the public OpenRouter catalogue.

It continues the **Qwen3.8-Max** line: a context of 1,000,000 tokens, an answer of up to 32,768
tokens, $0.15 and $0.47 per million tokens. Skill scores 82-89.

Accepted input: text and Markdown, source code, images, audio, video.

## How to obtain the key

1. Register in [Alibaba Cloud Model Studio](https://modelstudio.console.alibabacloud.com/)
   (the international site).
2. Activate the model service and link a payment method.
3. Open **API-KEY → Create API Key**, pick a workspace.
4. Copy the value — it is shown **once**. The key starts with `sk-`.
5. In AI2P: **Settings → Catalogs → AI models → Qwen3.8-Omni-Flash → «Set API key»**.

The key belongs to the organization: it is encrypted with the organization key and replicated
to all servers of the organization (spec ch. 10). The **local** Qwen models of the reference
have no key at all — they run on your own computer.

## Limits and price

| | |
|---|---|
| Context | 1,000,000 tokens |
| Maximum output | 32,768 tokens |
| Input price | $0.15 per 1M tokens |
| Output price | $0.47 per 1M tokens |

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

The `qwen3.8-omni-flash` weights are closed (unlike the smaller Qwen models, which sit under Apache
2.0), so this is about the **generated result**.

Verified on 2026-09-23: the Model Studio list-of-agreements page was read (last updated
2026-09-23; the Terms of Service, the Service Level Agreement and the Open Source Model
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

# YandexGPT-5.1-Pro

**Hosting:** cloud (Yandex Cloud, Russia; OpenAI-compatible)
**Connection:** `provider: openai-compatible`,
`baseUrl: https://llm.api.cloud.yandex.net/v1`, model
`gpt://<folder-id>/yandexgpt/latest`
**Key reference:** `yandex.apiKey`

Strong Russian text: `text-write` 86, `text-translate` 86, `text-edit` 85. Code is the weak
spot (60–64), this model is not worth taking for programming. The context is modest by the
standards of the reference: 32,000 tokens.

## Your own address has to be put into the "model" field

Yandex Cloud expects not a short identifier but **a full resource**:

```
gpt://<folder-id>/yandexgpt/latest
```

**The folder id is different for every user**, which is why the reference leaves it as a
placeholder — the record will not work "out of the box". Open
**Settings → Catalogs → AI models → YandexGPT-5.1-Pro → «Connection profile»** and replace
the placeholder `<folder-id>` with your own folder id (it looks like `b1g...` and
is visible in the Yandex Cloud console in the folder address). Instead of `latest` you may
give a specific version of the model.

## How to obtain the key

1. Create a folder in the [Yandex Cloud console](https://console.yandex.cloud/) and attach it
   to a billing account.
2. Create a **service account** and grant it the `ai.languageModels.user` role on the folder.
3. Create an **API key** for the service account: **Service accounts → your account →
   Create new key → Create API key**.
4. Copy the secret part — it is shown **once**.
5. Copy the **folder id** and substitute it into the "model" field of the profile (see above).
6. In AI2P: **Settings → Catalogs → AI models → YandexGPT-5.1-Pro → «Set API key»**.

## Limits and price

| | |
|---|---|
| Context | 32,000 tokens |
| Maximum output | 16,000 tokens |
| Price | by the Yandex Cloud tariff (zero is set in the reference) |

The tariff is counted in roubles per thousand tokens, which is why the price is left at zero in
the declaration — **AI2P billing will not show the spending on this model**; look at it in the
Yandex Cloud console. The context of 32,000 tokens is the smallest in the reference: a long
task description or a big chat history will not fit into it.

**The model identifier has not been verified by a live call** — check the list of available
models with a `GET /models` request to `https://llm.api.cloud.yandex.net/v1` using your key.

## Licence

| | |
|---|---|
| Terms for the generated result | the client **may use the Generated Content in any way** not contrary to the terms and the law (clause 4.1); no exclusive right and no uniqueness are promised |
| Commercial use | allowed |
| What is required | do not pass the generated content off as human work (clause 3.11.4); Yandex may introduce a duty to state that the service was used (clause 3.8) |
| Terms text | <https://yandex.ru/legal/cloud_terms_yandex_foundation_models/ru/> |
| Payment per generation | per token — the rate is in “Limits and price” |

Training is stated outright (clause 5.4.1): information from requests **may be used to debug
and train models** until you set the special opt-out parameter. When working with someone
else's data it is worth doing that before the very first job.

The terms were verified against their text on 2026-08-27.

## Hardware requirements

None: the computation happens on the provider side. Internet access to
`llm.api.cloud.yandex.net` is required.

## Common errors

* **400 / "model not found"** — the placeholder `<folder-id>` is still in the
  "model" field.
* **403 "permission denied"** — the service account has not been granted the
  `ai.languageModels.user` role on the right folder.
* **The job breaks off on a long description** — 32,000 tokens of context are not enough; move
  the material into files and give links in the description.

# GPT-5.6-Terra

**Hosting:** cloud (the OpenAI API, OpenAI-compatible)
**Connection:** `provider: openai-compatible`, `baseUrl: https://api.openai.com/v1`,
model `gpt-5.6-terra`
**Key reference:** `openai.apiKey`

The middle tier of the line: skill scores 85–88 against 91–95 for
GPT-5.6-Sol, while the price is five times lower with the same context of
1,050,000 tokens. A good choice for bulk work where the flagship is overkill but the quality
must stay high.

## How to obtain the key

**The key is the very same** as for GPT-5.6-Sol: both models refer to `openai.apiKey`. If it
has already been entered, this model will work on its own.

If there is no key yet:

1. Register at [platform.openai.com](https://platform.openai.com/).
2. Top up the balance: **Settings → Billing → Add to credit balance**.
3. Open [platform.openai.com/api-keys](https://platform.openai.com/api-keys) →
   **Create new secret key**.
4. Copy the value (it is shown once and starts with `sk-`).
5. In AI2P: **Settings → Catalogs → AI models → GPT-5.6-Terra → «Set API key»**.

## Limits and price

| | |
|---|---|
| Context | 1,050,000 tokens |
| Maximum output | 128,000 tokens |
| Input price | $1.00 per 1M tokens |
| Output price | $6.00 per 1M tokens |

**Be sure to check the price before any serious spending.** The sources disagree: the market
review (report T-213) gave $2.50 and $15.00, the public model catalogue as of 17.08.2026 —
$1.00 and $6.00. The second pair of numbers went into the reference. The current price list —
[OpenAI Pricing](https://openai.com/api/pricing/).

**The model identifier has not been verified by a live call** — it is taken from the model
catalogue without the vendor prefix. Before the first job check it with a `GET /models`
request to `https://api.openai.com/v1`.

## Licence

| | |
|---|---|
| Terms for the generated result | **the result is yours**: “Customer owns all Output”, and OpenAI assigns you its rights in it |
| Commercial use | allowed |
| What is required | comply with the Usage Policies; remember that uniqueness is not promised — another user may receive the same answer |
| Terms text | <https://openai.com/policies/services-agreement/> |
| Payment per generation | per token — the rate is in “Limits and price” |

The weights are closed, there is nothing to licence — the terms cover the **generated
result**.

OpenAI **does not use what goes into the API to develop its services** unless you explicitly
allow it; for consumer ChatGPT that is not the case.

Verified on 2026-08-27: the ownership wording (“you … own the Output. We hereby assign to
you all our right, title, and interest, if any, in and to Output”) was read verbatim in
OpenAI's terms; the same wording is in the API agreement linked above.

## Hardware requirements

None: the computation happens on the provider side. Internet access to `api.openai.com` is
required.

## Common errors

* **404 "model not found"** — the id has changed; check it against the `GET /models` list.
* **429 "insufficient_quota"** — the balance of the OpenAI organization has not been topped
  up.
* **The bill came out twice the estimate** — check the price against the provider's price list
  (see above) and correct it in the model declaration.

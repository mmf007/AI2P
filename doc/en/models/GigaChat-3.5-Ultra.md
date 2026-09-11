# GigaChat-3.5-Ultra

**Hosting:** cloud (Sber, Russia; OpenAI-compatible)
**Connection:** `provider: openai-compatible`,
`baseUrl: https://gigachat.devices.sberbank.ru/api/v1`, model `GigaChat-3.5-Ultra`
**Key reference:** `gigachat.accessToken`

A Russian model of 432 billion parameters (MoE) with open weights under MIT. **The best
Russian text in the reference**: the scores are `text-write` 85, `text-edit` 84,
`text-translate` 84. The code is noticeably weaker (61–66) — take other records for
programming.

## The connection is incomplete — read this before creating a key

The record is in the reference, but **it does not work all the way out of the box**, and this
is a known limitation rather than an installation error:

* Sber issues **not a permanent key but an OAuth access token that lives for 30 minutes**.
  The AI2P connector can only handle a permanent key, so a pasted token stops working after
  half an hour and has to be entered again. That is exactly why the field in the reference is
  called `gigachat.accessToken`.
* A **Russian root certificate** (the Ministry of Digital Development one) is required in the
  computer's trusted certificate store, otherwise the connection to
  `gigachat.devices.sberbank.ru` breaks on the TLS check.

Full support (a connector of its own with token refresh, or an external proxy adapter) is a
separate task of the project. Until it exists this record is good for one-off trials rather
than for the background work of an agent.

## How to obtain the key

1. Register in [Sber Studio](https://developers.sber.ru/studio/) and create a
   **GigaChat API** project.
2. Get the **Client ID / Client Secret** pair (the "authorization key" in the interface).
3. Install the Russian root certificate on the computer AI2P runs on.
4. Exchange the authorization key for an **access token** (a `POST` request to
   `https://ngw.devices.sberbank.ru:9443/api/v2/oauth`, the `RqUID` header, the field
   `scope=GIGACHAT_API_PERS` or `GIGACHAT_API_CORP`).
5. In AI2P: **Settings → Catalogs → AI models → GigaChat-3.5-Ultra → «Set API key»** —
   paste the token you received. **Repeat steps 4–5 after 30 minutes**: the lifetime of the
   token is not extended.

## Limits and price

| | |
|---|---|
| Context | 262,144 tokens |
| Maximum output | 32,768 tokens |
| Price | by the Sber tariff (zero is set in the reference) |

The tariff is counted in Sber's own units, not in dollars per million tokens, which is why the
price is left at zero in the declaration — **AI2P billing will not show the spending on this
model**. Look at it in the Studio account.

**The model identifier has not been verified by a live call** — check it with a `GET /models`
request to `https://gigachat.devices.sberbank.ru/api/v1`.

## Licence

| | |
|---|---|
| Terms for the generated result | **the rights to the Generated Content belong to the User** (GigaChat agreement, clause 1.9) |
| Commercial use | allowed |
| What is required | under clause 5.2 you grant the Bank an irrevocable, royalty-free, non-exclusive licence to use the Generated Content — including reworking it (rights to the rework stay with the Bank) and advertising |
| Terms text | <https://developers.sber.ru/docs/ru/policies/gigachat-agreement/beta> |
| Payment per generation | per token — the rate is in “Limits and price” |

Formally the rights to the result are yours, but **the licence you grant Sber is the
broadest among the catalogue records**: reproduction, making available to the public,
reworking and use in advertising. If the result is a trade secret or must not be shown to
third parties, take another model.

The terms were verified against the text of the agreement on 2026-08-27. The agreement is
marked as beta — read it before connecting, it changes more often than the others.

## Hardware requirements

None: the computation happens on the provider side. Internet access to
`gigachat.devices.sberbank.ru` and an installed Russian root certificate are required.

## Common errors

* **401 half an hour after a successful start** — the access token has expired; get a new one.
* **A TLS error / "could not establish a trust relationship"** — the Russian root certificate
  is not installed.
* **A job with code was done badly** — the model's niche is Russian text, not programming.

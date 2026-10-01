# Claude-Haiku-4.5_cli

**Hosting:** cloud, but connected **over the CLI, not the API**
**Connection:** `provider: anthropic`, `transport: cli`, command
`claude --permission-mode acceptEdits`, model `claude-haiku-4-5`
**Key reference:** empty — **no API key is needed**

The same model as Claude-Haiku-4.5, but launched through the **Claude Code CLI**
in headless mode. You pay by **subscription**, not per token, so in AI2P billing such jobs
cost zero.

The fastest and cheapest model of the family. Its niche is simple mechanical work: translations, summaries, text edits, small code fixes. Do not give it a hard task.

## Why a separate entry per version

With the Claude Code CLI the model version is chosen by the profile's **«model»** field:
it is passed to the CLI as the `--model` flag. So every version you want to be able to pick
for an executor has **its own catalogue entry**, and the entries coexist: Claude-Sonnet-5_cli
keeps its own identifier, while this one runs on `claude-haiku-4-5`.

Two forms of the value are accepted (`claude --help`):

* the **full version name** — `claude-haiku-4-5`;
* a **latest-version alias** — `haiku`.

Distribution entries carry the full name: an alias silently moves to a newer version over
time (verified by a live call on 2026-09-24: `fable` already means `claude-fable-5-1`), and
then the entry stops meaning what its name says.

## No API key is needed

Authorisation is by the CLI session, not by a key: `secretRef` in the profile is empty and
the entry has no «Set API key» button. What you need instead:

1. **Install Claude Code** — [official guide](https://docs.claude.com/en/docs/claude-code/overview).
   Check: `claude --version` must print something.
2. **Log in with your subscription**: `claude login`.
3. Make sure `claude` is on the **PATH of the user AI2P runs as**.
4. If the executable is not on the PATH, write its full path into the `cliCommand` field.

A detailed «API vs CLI» comparison is in the Claude-Fable-5_cli document;
everything works exactly the same way here.

## The probe checks the model too

The probe button (change v1.144) does three things: runs `claude --version`, asks for the
login state and **checks the model identifier itself with a short call**. An unknown id, or
one your subscription has no access to, shows up as a probe failure at once instead of a job
crashing minutes later. If the CLI answers with a different model than requested, AI2P
reports it as a **job log event** and as a line in the result summary.

## Limits and cost

| | |
|---|---|
| Context | 200 000 tokens |
| Max output | 64 000 tokens |
| Cost | 0 (paid by subscription) |

**AI2P recognises the subscription limit and waits for the reset**: on a 429 refusal it reads
the reset time, moves the task to «waiting» and later continues the same session.

## Licence

| | |
|---|---|
| Terms for the generated output | **the output is yours**: Anthropic assigns you its rights in Outputs (Consumer Terms, §4) |
| Commercial use | allowed |
| What is mandatory | follow the Usage Policy; remember that under the consumer terms your material is used for model training until you opt out in the account settings |
| Text of the terms | <https://www.anthropic.com/legal/consumer-terms> |
| Payment for generation | by subscription, not per token — such jobs cost 0 in AI2P billing |

A Claude subscription is governed by the **consumer** terms; an API key by the **commercial**
ones (<https://www.anthropic.com/legal/commercial-terms>), which contain no training on your data.

## Hardware requirements

None in particular: the provider does the computing. You need Claude Code installed, internet
access and space in the project folder.

## Common mistakes

* **«claude not found»** — the CLI is not installed or is not on the AI2P user's PATH.
* **«Claude CLI rejected the model»** — the identifier is outdated, or the subscription has no
  access to that version; check the «model» field against `claude --help`.
* **The agent sees no task files** — the project has no folder set («Project folder»).

# Claude-Opus-5.5_cli

**Hosting:** cloud, but connected **over the CLI, not the API**
**Connection:** `provider: anthropic`, `transport: cli`, command
`claude --permission-mode acceptEdits`, model `claude-opus-5-5`
**Key reference:** empty — **no API key is needed**

The same model as Claude-Opus-5.5, but launched through the **Claude Code CLI**
in headless mode. You pay by **subscription**, not per token, so in AI2P billing such jobs
cost zero.

The senior model of the Opus line, released after Opus 5: stronger than it and cheaper on the API. Its niche is hard code, requirements analysis and long tasks over project files.

## Why a separate entry per version

With the Claude Code CLI the model version is chosen by the profile's **«model»** field:
it is passed to the CLI as the `--model` flag. So every version you want to be able to pick
for an executor has **its own catalogue entry**, and the entries coexist: Claude-Opus-5.0_cli
keeps its own identifier, while this one runs on `claude-opus-5-5`.

Two forms of the value are accepted (`claude --help`):

* the **full version name** — `claude-opus-5-5`;
* a **latest-version alias** — `opus`.

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
| Context | 1 000 000 tokens |
| Max output | 128 000 tokens |
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

## A recent Claude Code is required

This model appeared later than the CLI itself, so an **old Claude Code does not know it**.
On version 2.1.278 the call was refused:

```
[claude-code:unrecognized_model]: Claude Code 2.1.278 does not support this model;
version 2.1.280 or newer is required
```

You need **Claude Code 2.1.280 or newer** (verified by a live call on 2026-09-24 with 2.1.281:
exit code 0, answered by claude-opus-5-5). Update with `claude update`, check with
`claude --version`. If the entry's probe complains about the model, start with the CLI version.

The same run shows why distribution entries name the model by its **full name**: the `opus`
alias has already moved from `claude-opus-5` to `claude-opus-5-5`, i.e. it would mean different
things on different machines. The Claude-Opus-5.0_cli entry stays on `claude-opus-5`.

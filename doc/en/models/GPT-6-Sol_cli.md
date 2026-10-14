# GPT-6-Sol_cli

**Hosting:** cloud, connected **over the CLI, not the API**
**Connection:** `provider: openai`, `transport: cli`, command `codex`, model `gpt-6-sol`
**Key reference:** empty — **no API key is needed**, authorisation by the CLI session

The same model as GPT-6-Sol, but through the vendor's terminal agent **OpenAI Codex CLI** (https://github.com/openai/codex). Paid by the vendor subscription; such jobs cost zero in AI2P billing.

## Important: no connector yet

AI2P **cannot run jobs** through OpenAI Codex CLI yet: only Claude Code has a CLI connector. The entry is therefore created **inactive**. An executor on this entry is refused with «only anthropic is supported over CLI». The entry lets you install the CLI and sign in ahead of time.

## Install and sign in (no API key is needed)

1. Install the CLI: `npm i -g @openai/codex`.
2. Sign in: `codex login` — in a terminal on the server computer, as the user AI2P runs under.
3. Make sure `codex` is on that user's PATH; otherwise put the full path into the `cliCommand` field.

Sign-in uses a ChatGPT subscription (Plus, Pro, Team, Enterprise) or an OpenAI API key.

Non-interactive (headless) run: `codex exec "..."`.

The key button in the model catalogue row shows this CLI's sign-in command.

## Limits and cost

| | |
|---|---|
| Context | 400 000 |
| Cost | 0 (paid by subscription) |

## Licence

Output and terms of use follow the vendor subscription terms (https://github.com/openai/codex). Checked on 2026-10-06 by web search; the model id was NOT verified by a live run.

## Hardware requirements

None in particular: the vendor does the computing. You need the CLI installed, internet access and space in the project folder.

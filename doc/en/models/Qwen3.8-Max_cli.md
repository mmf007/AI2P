# Qwen3.8-Max_cli

**Hosting:** cloud, connected **over the CLI, not the API**
**Connection:** `provider: alibaba`, `transport: cli`, command `qwen`, model `qwen3.8-max`
**Key reference:** empty — **no API key is needed**, authorisation by the CLI session

The same model as Qwen3.8-Max, but through the vendor's terminal agent **Qwen Code** (https://github.com/QwenLM/qwen-code). Paid by the vendor subscription; such jobs cost zero in AI2P billing.

## Important: no connector yet

AI2P **cannot run jobs** through Qwen Code yet: only Claude Code has a CLI connector. The entry is therefore created **inactive**. An executor on this entry is refused with «only anthropic is supported over CLI». The entry lets you install the CLI and sign in ahead of time.

## Install and sign in (no API key is needed)

1. Install the CLI: `npm i -g @qwen-code/qwen-code@latest`.
2. Sign in: `qwen  →  /auth` — in a terminal on the server computer, as the user AI2P runs under.
3. Make sure `qwen` is on that user's PATH; otherwise put the full path into the `cliCommand` field.

Qwen Code is Alibaba's fork of Gemini CLI. Sign-in: run `qwen` and use `/auth` (Qwen OAuth or an API key).

Non-interactive (headless) run: `qwen -p "..."`.

The key button in the model catalogue row shows this CLI's sign-in command.

## Limits and cost

| | |
|---|---|
| Context | 262 144 |
| Cost | 0 (paid by subscription) |

## Licence

Output and terms of use follow the vendor subscription terms (https://github.com/QwenLM/qwen-code). Checked on 2026-10-06 by web search; the model id was NOT verified by a live run.

## Hardware requirements

None in particular: the vendor does the computing. You need the CLI installed, internet access and space in the project folder.

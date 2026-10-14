# Grok-4.7_cli

**Hosting:** cloud, connected **over the CLI, not the API**
**Connection:** `provider: xai`, `transport: cli`, command `grok`, model `grok-4.7`
**Key reference:** empty — **no API key is needed**, authorisation by the CLI session

The same model as Grok-4.7, but through the vendor's terminal agent **xAI Grok Build** (https://x.ai). Paid by the vendor subscription; such jobs cost zero in AI2P billing.

## Important: no connector yet

AI2P **cannot run jobs** through xAI Grok Build yet: only Claude Code has a CLI connector. The entry is therefore created **inactive**. An executor on this entry is refused with «only anthropic is supported over CLI». The entry lets you install the CLI and sign in ahead of time.

## Install and sign in (no API key is needed)

1. Install the CLI: `npm install -g @xai-official/grok`.
2. Sign in: `grok` — in a terminal on the server computer, as the user AI2P runs under.
3. Make sure `grok` is on that user's PATH; otherwise put the full path into the `cliCommand` field.

On first run `grok` opens a browser to sign in. Without a browser use the `XAI_API_KEY` environment variable.

Non-interactive (headless) run: `grok -p "..."`.

The key button in the model catalogue row shows this CLI's sign-in command.

## Limits and cost

| | |
|---|---|
| Context | 256 000 |
| Cost | 0 (paid by subscription) |

## Licence

Output and terms of use follow the vendor subscription terms (https://x.ai). Checked on 2026-10-06 by web search; the model id was NOT verified by a live run.

## Hardware requirements

None in particular: the vendor does the computing. You need the CLI installed, internet access and space in the project folder.

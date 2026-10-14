# Kimi-K3_cli

**Hosting:** cloud, connected **over the CLI, not the API**
**Connection:** `provider: moonshot`, `transport: cli`, command `kimi`, model `kimi-k3`
**Key reference:** empty — **no API key is needed**, authorisation by the CLI session

The same model as Kimi-K3, but through the vendor's terminal agent **Moonshot Kimi Code** (https://github.com/MoonshotAI/kimi-code). Paid by the vendor subscription; such jobs cost zero in AI2P billing.

## Important: no connector yet

AI2P **cannot run jobs** through Moonshot Kimi Code yet: only Claude Code has a CLI connector. The entry is therefore created **inactive**. An executor on this entry is refused with «only anthropic is supported over CLI». The entry lets you install the CLI and sign in ahead of time.

## Install and sign in (no API key is needed)

1. Install the CLI: `irm https://code.kimi.com/kimi-code/install.ps1 | iex`.
2. Sign in: `kimi  →  /login` — in a terminal on the server computer, as the user AI2P runs under.
3. Make sure `kimi` is on that user's PATH; otherwise put the full path into the `cliCommand` field.

Sign-in: run `kimi` and use `/login` — Kimi Code OAuth or a Moonshot AI Open Platform key.

A non-interactive (headless) flag is not confirmed by public sources — check the CLI documentation.

The key button in the model catalogue row shows this CLI's sign-in command.

## Limits and cost

| | |
|---|---|
| Context | 262 144 |
| Cost | 0 (paid by subscription) |

## Licence

Output and terms of use follow the vendor subscription terms (https://github.com/MoonshotAI/kimi-code). Checked on 2026-10-06 by web search; the model id was NOT verified by a live run.

## Hardware requirements

None in particular: the vendor does the computing. You need the CLI installed, internet access and space in the project folder.

# Gemini-3.1-Pro_cli

**Hosting:** cloud, connected **over the CLI, not the API**
**Connection:** `provider: google`, `transport: cli`, command `agy`, model `gemini-3.1-pro`
**Key reference:** empty — **no API key is needed**, authorisation by the CLI session

The same model as Gemini-3.1-Pro, but through the vendor's terminal agent **Google Antigravity CLI** (https://antigravity.google). Paid by the vendor subscription; such jobs cost zero in AI2P billing.

## Important: no connector yet

AI2P **cannot run jobs** through Google Antigravity CLI yet: only Claude Code has a CLI connector. The entry is therefore created **inactive**. An executor on this entry is refused with «only anthropic is supported over CLI». The entry lets you install the CLI and sign in ahead of time.

## Install and sign in (no API key is needed)

1. Install the CLI: `irm https://antigravity.google/cli/install.ps1 | iex`.
2. Sign in: `agy` — in a terminal on the server computer, as the user AI2P runs under.
3. Make sure `agy` is on that user's PATH; otherwise put the full path into the `cliCommand` field.

Antigravity CLI replaced Gemini CLI on 2026-06-18. On first run `agy` prints a link and a one-time code: open the link, sign in to Google and paste the code.

Non-interactive (headless) run: `agy -p "..."`.

The key button in the model catalogue row shows this CLI's sign-in command.

## Limits and cost

| | |
|---|---|
| Context | 1 000 000 |
| Cost | 0 (paid by subscription) |

## Licence

Output and terms of use follow the vendor subscription terms (https://antigravity.google). Checked on 2026-10-06 by web search; the model id was NOT verified by a live run.

## Hardware requirements

None in particular: the vendor does the computing. You need the CLI installed, internet access and space in the project folder.

# Mistral-Large-3_cli

**Hosting:** cloud, connected **over the CLI, not the API**
**Connection:** `provider: mistral`, `transport: cli`, command `vibe`, model `mistral-large-3`
**Key reference:** empty — **no API key is needed**, authorisation by the CLI session

The same model as Mistral-Large-3, but through the vendor's terminal agent **Mistral Vibe CLI** (https://github.com/mistralai/mistral-vibe). Paid by the vendor subscription; such jobs cost zero in AI2P billing.

## Important: no connector yet

AI2P **cannot run jobs** through Mistral Vibe CLI yet: only Claude Code has a CLI connector. The entry is therefore created **inactive**. An executor on this entry is refused with «only anthropic is supported over CLI». The entry lets you install the CLI and sign in ahead of time.

## Install and sign in (no API key is needed)

1. Install the CLI: `curl -LsSf https://mistral.ai/vibe/install.sh | bash   (pip install mistral-vibe)`.
2. Sign in: `vibe` — in a terminal on the server computer, as the user AI2P runs under.
3. Make sure `vibe` is on that user's PATH; otherwise put the full path into the `cliCommand` field.

On first run `vibe` opens a setup wizard: sign in with a Mistral account or an API key. Vibe defaults to Devstral — the entry's model is set by the «model» field.

A non-interactive (headless) flag is not confirmed by public sources — check the CLI documentation.

The key button in the model catalogue row shows this CLI's sign-in command.

## Limits and cost

| | |
|---|---|
| Context | 256 000 |
| Cost | 0 (paid by subscription) |

## Licence

Output and terms of use follow the vendor subscription terms (https://github.com/mistralai/mistral-vibe). Checked on 2026-10-06 by web search; the model id was NOT verified by a live run.

## Hardware requirements

None in particular: the vendor does the computing. You need the CLI installed, internet access and space in the project folder.

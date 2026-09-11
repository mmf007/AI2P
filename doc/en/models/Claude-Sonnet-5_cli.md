# Claude-Sonnet-5_cli

**Hosting:** cloud, but connected **over the CLI rather than the API**
**Connection:** `provider: anthropic`, `transport: cli`, command
`claude --permission-mode acceptEdits`, model `claude-sonnet-5`
**Key reference:** empty — **no API key is needed**

The same model as Claude-Sonnet-5, but launched through **Claude Code
CLI** in headless mode. You pay not per token but by **subscription**, so in AI2P billing the
cost of such jobs is zero. Its niche is bulk work with project files, when paying per token is
undesirable.

## No API key is needed

Authorization goes through the CLI session, not through a key. That is why `secretRef` is
empty in the profile and this model has no «Set API key» button.

What is needed instead of a key:

1. **Install Claude Code** — the [official guide](https://docs.claude.com/en/docs/claude-code/overview).
   To check: `claude --version` in a console must print something.
2. **Sign in with your subscription**: `claude login`. The session is stored in the OS user
   profile.
3. Make sure `claude` is reachable **from the PATH of the user AI2P runs as**. If AI2P runs as
   a service under a different account, the sign-in has to be done under that account.
4. If the executable is not on PATH — write the full path into the `cliCommand` field of the
   model profile.

## How it differs from the API variant

| | over the API | over the CLI |
|---|---|---|
| Payment | per token | by subscription |
| Cost in billing | computed | 0 |
| Tools | AI2P tools | **the CLI's own built-in tools** |
| Working directory | does not matter | **the project folder** |
| Questions to a human | built in | via the `AI2P_QUESTION` marker in the answer |
| Creating subtasks | with the `create_task` tool | via the `AI2P_SUBTASK` marker |

A detailed breakdown of the differences is in the
Claude-Fable-5_cli document; with Sonnet everything works exactly the
same way.

## Limits and price

| | |
|---|---|
| Context | 1,000,000 tokens |
| Maximum output | 128,000 tokens |
| Cost | 0 (paid by the subscription) |

**AI2P recognizes the subscription limit and waits for the reset** (change v1.63): having seen
a CLI refusal with code 429, the system takes the reset time and moves the task to "pending"
instead of "stopped with error", and on the reset it **continues the very same session**
(`--resume`) rather than starting the job over. Fill in the "token limit per window" and
"limit window, h" fields of the AI executor (for a Claude subscription the window is 5 hours)
so that the system warns you in advance.

## Licence

| | |
|---|---|
| Terms for the generated result | **the result is yours**: Anthropic assigns you its rights in Outputs (Consumer Terms, clause 4) |
| Commercial use | allowed; the “personal, non-commercial use only” clause covers evaluation access, not a paid subscription |
| What is required | comply with the Usage Policy; remember that under the consumer terms materials are used for model training until you opt out in the account settings |
| Terms text | <https://www.anthropic.com/legal/consumer-terms> (8 October 2025 version) |
| Payment per generation | by subscription, not per token — such jobs cost 0 in AI2P billing |

Do not mix up the two agreements here. A Claude subscription is the **consumer** contract;
an API key is the **commercial** one (<https://www.anthropic.com/legal/commercial-terms>),
and that one has no training on your data at all.

Practical consequence: if a job runs over someone else's code or data under a non-disclosure
agreement, the API-key variant is safer than the subscription one — or opt out of training
in the Claude account settings.

The terms were verified against their text on 2026-08-27.

## Hardware requirements

Nothing special: the provider does the computing. What is needed is an installed Claude Code,
internet access and enough space in the project directory — the agent works with the files
directly.

## Common errors

* **"claude not found"** — the CLI is not installed or is not on the PATH of the AI2P user.
* **A silent refusal or a sign-in request** — the session was created under a different OS
  account.
* **The agent does not see the task files** — the project has no directory set ("Project
  folder").

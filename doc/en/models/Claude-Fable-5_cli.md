# Claude-Fable-5_cli

**Hosting:** cloud, but connected **over the CLI rather than the API**
**Connection:** `transport: cli`, command `claude --permission-mode acceptEdits`,
model `claude-fable-5`
**Key reference:** empty — **no API key is needed**

The same model as Claude-Fable-5, but launched through **Claude Code
CLI** in headless mode. You pay not per token but by **subscription**, so in AI2P billing the
cost of such jobs is zero.

## No API key is needed

Authorization goes through the CLI session, not through a key. That is why `secretRef` is
empty in the profile and this model has no «Set API key» button.

## What is needed instead of a key

1. **Install Claude Code** — the [official guide](https://docs.claude.com/en/docs/claude-code/overview).
   To check: `claude --version` in a console must print something.
2. **Sign in with your subscription**: `claude login` (the browser opens by itself). The
   session is stored in the OS user profile.
3. Make sure `claude` is reachable **from the PATH of the user AI2P runs as**. If AI2P runs
   as a service under a different account, the sign-in has to be done under that account as
   well, otherwise the agent will run into "not authorized".
4. If the executable is not on PATH — write the full path into the `cliCommand` field of the
   model profile (the «Connection profile» button on the model form).

## How it differs from the API variant

| | over the API | over the CLI |
|---|---|---|
| Payment | per token | by subscription |
| Cost in billing | computed | 0 |
| Tools | AI2P tools (reading/writing files and so on) | **the CLI's own built-in tools** |
| Working directory | does not matter | **the project folder** — the agent works right in it |
| Questions to a human | built in | via the `AI2P_QUESTION` marker in the answer, continued with `--resume` |
| Creating subtasks | with the `create_task` tool | via the `AI2P_SUBTASK` marker in the answer |
| Reading other jobs | with the `get_task_by_code`, `get_task_by_url`, `get_task_chat` tools | via the `AI2P_GET_TASK` marker in the answer |
| External files of the job (attached images) | with the `fetch_file` tool | via the `AI2P_GET_FILE` marker in the answer |
| Moving a task along the hierarchy | with the `move_task` tool | via the `AI2P_MOVE_TASK` marker in the answer |

An important consequence: over a CLI connection AI2P tools are **not published** to the agent
— it uses its own. The AI2P security rules (spec ch. 12) do not cover its actions inside the
project directory, so choose the project directory deliberately.

### Subtasks via the `AI2P_SUBTASK` marker

A CLI agent has no `create_task` tool, and the AI2P API is closed behind a cookie sign-in —
so previously it could not split a task into subtasks at all. Now a subtask is created by a
**line in the answer**:

```
AI2P_SUBTASK: {"title": "title", "description": "description of the work", "skills": ["code-write"], "priority": 20, "acceptance": "acceptance criteria"}
```

One line — one subtask; `title` and `description` are required. As soon as the agent has
finished, AI2P executes these lines itself: removes them from the text of the answer, creates
the subtasks (the executor is picked automatically — an AI first, a human after), marks the
parent task as split and appends a "Subtasks" section listing what was created to the result.
After that the subtasks are started by the usual auto-split queue — by descending numeric
priority.

This is the very same **`AI2P.Tasks.Create`** as the tool, so the security rules for that
action apply: a ban (deny) or a confirmation requirement (confirm) — and the subtask is not
created, while the reason for the refusal goes into the job result and into the work history.
A marker counts only **from the start of a line**: a mention inside a sentence or in a report
will not become a subtask.

### Reading other jobs via the `AI2P_GET_TASK` marker

AI2P tasks live in the organization database, they are not in the project files, and `/api`
is closed behind a cookie sign-in — so a CLI agent used to see only the text of its own job
and the parent task block. If a person gave a link to another task in the wording ("take the
wording from the result of task …"), the agent could not read it. Now it requests it with a
**line in the answer**:

```
AI2P_GET_TASK: {"code": "T-15"}
```

Instead of `code` you may give `"url"` (a link of the form `…/task/<id>`) or `"title"` (a
search over part of the title — a list of the jobs found is returned, without their texts).
AI2P performs the request and sends the answer to the agent as **the next message of the same
CLI session** (`--resume`, just like when a human answers a question), after which it
continues from the same place. The answer is always complete: the job card (wording,
acceptance criteria, status, result artifacts) **and the whole chat** — there is no need to
ask for the correspondence separately. Several markers may be printed at once; in total there
are no more than 10 requests per job (a loop guard), after which the system tells the agent
the limit is exhausted.

These are the same actions — **`AI2P.Tasks.GetByCode` / `GetByUrl` / `FindByTitle` /
`GetChat`** — as the tools of the same names, so the security rules work as usual: a ban or a
confirmation requirement — the job is not read, and the refusal goes to the agent and into the
work history. Only tasks of the same project are available.

## Limits

| | |
|---|---|
| Context | 1,000,000 tokens |
| Maximum output | 128,000 tokens |
| Cost | 0 (paid by the subscription) |

**The CLI does not report how much of the subscription limit is left** (change v1.62), so
AI2P counts the spending itself — by the tokens of jobs over a sliding window. Fill in the
**"token limit per window"** and **"limit window, h"** fields of the AI executor (for a Claude
subscription the window is 5 hours): then, before starting a task, the system will warn that
there is almost no limit left and will move the start to the moment the window frees up (you
may split the task instead, or start it by force). If the fields are empty there are no
checks and everything works as before. And if the limit is reached in the middle of a job,
AI2P recognizes the CLI message (`5-hour limit reached ∙ resets 4:10pm (…)`), takes the reset
time from it and moves the task to **"pending"** until that time instead of "stopped with
error" — after which it starts by itself. For details see the
Claude-Opus-5.0_cli document.

**How long to wait for an answer** (change v1.63) — the **"Answer timeout, min"** field on the
executor form: empty — 30 minutes, a number — that many minutes, **0 — no limit**. If the
timeout runs out, the job fails with an error and a hint; the executor is not marked busy in
that case (silence from the agent does not count as a limit).

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
* **A silent refusal or a sign-in request** — the CLI session has not been created, or was
  created under a different OS account; repeat `claude login` under the right user.
* **The agent does not see the task files** — the project has no directory set (the "Project
  folder" field): that is exactly what serves as the working directory of the CLI process.

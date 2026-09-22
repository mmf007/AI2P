# Claude-Opus-5.0_cli

**Hosting:** cloud, connected **over the CLI**
**Connection:** `transport: cli`, command `claude --permission-mode acceptEdits`,
model `claude-opus-5`
**Key reference:** empty — **no API key is needed**

Claude-Opus-5.0 launched through **Claude Code CLI** in headless mode.
Payment is by subscription; in AI2P billing the cost of such jobs is zero.

## No API key is needed

Authorization goes through the CLI session. The setup is exactly the same as for
Claude-Fable-5_cli:

1. install [Claude Code](https://docs.claude.com/en/docs/claude-code/overview);
2. run `claude login` **under the OS user AI2P runs as**;
3. make sure `claude` is reachable from its PATH (otherwise write the full path into
   `cliCommand` of the model profile).

The setup is shared by both CLI models: doing it once enables both.

## Limits

| | |
|---|---|
| Context | 1,000,000 tokens |
| Maximum output | 128,000 tokens |
| Cost | 0 (paid by the subscription) |

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

Nothing special: the provider does the computing. What is needed is an installed Claude Code
and internet access.

## The subscription limit (change v1.62)

The CLI does not report how much of the subscription limit is left: it is neither in
`claude --help` nor in the JSON output of a job, and `/usage` works only in an interactive
session. So AI2P counts the spending itself — by the tokens of jobs over a sliding window.
For that to work, fill in two fields of the AI executor (Executors → the executor form):

* **token limit per window** — how many tokens you are ready to spend per window; the value
  is found empirically for your subscription;
* **limit window, h** — for a Claude subscription the window is **5 hours**.

If the fields are empty everything works as before: nothing is counted and there are no
warnings. If they are filled in, AI2P checks what is left before starting a task and, if
there is almost nothing left, moves the start to the moment the window frees up, with a
message in the task chat (instead of that you may split the task into subtasks or start it by
force). The postponed start is kept in the task: the computer may be switched off, and after
it is switched on the task will start by itself.

**If the limit is reached in the middle of the work anyway** (change v1.62), the CLI answers
with a short message of the form `5-hour limit reached ∙ resets 4:10pm (Europe/Moscow)`.
AI2P recognizes it, takes the reset time from it and **does not treat this as a task error**:
the job is closed with an explanatory artifact, the executor is marked "busy until", and the
task moves to **"pending"** — a "Pending until <time>" chip is visible in the card header and
a message appears in the chat. When the time comes the task starts by itself — **as a new
job, from the beginning** (everything the agent managed to write into the project files stays
where it is). There is no need to wait at the computer: it may be switched off. If the
message contained no reset time, we wait an hour.

## The answer timeout (change v1.63)

A CLI agent runs its own loop and **stays silent** while working — AI2P sees only the moment
the process ends. How long to wait for it is set by the **"Answer timeout, min"** field on the
executor form:

* **empty** — 30 minutes (the system default, as was hard-wired in the code before 1.63);
* **a number** — that many minutes;
* **0** — wait without a limit; only the "stop" button will interrupt the job.

If the timeout runs out, the job ends with an **error** and a hint on where to raise the
value, and the task moves to "stopped with error". The executor is **not marked busy** in that
case: before 1.63 silence from the agent was treated as an exhausted subscription limit, and
the executor dropped out of work for an hour even though the limit might have been only a
third spent. A real limit is still recognized by AI2P from the message of the CLI itself (see
the section above).

## What to remember about a CLI connection

* the agent works with the **built-in tools of the CLI**; AI2P tools are not published to it;
* the working directory of the process is the **project folder**, so it must be set;
* the AI2P security rules do not cover the actions of the agent inside that directory;
* questions to a human are passed with the `AI2P_QUESTION` marker in the answer, and the
  dialogue is continued in the same CLI session (`--resume`);
* the agent creates subtasks with the `AI2P_SUBTASK` marker in the answer — instead of the
  `create_task` tool, which it does not have (see Claude-Fable-5_cli);
* the agent moves a task along the hierarchy (another parent, or to the root) with the
  `AI2P_MOVE_TASK` marker — instead of the `move_task` tool;
* in a Condition / Loop task the agent returns its decision with the `AI2P_CONDITION`
  (`{"value": true}`) or `AI2P_LOOP` (`{"continue": false}`) marker — instead of the
  `set_condition_result` / `set_loop_result` tools; branch tasks from a template — with
  `AI2P_FROM_TEMPLATE`, stopping the hierarchy — with `AI2P_STOP_HIERARCHY` (or `ai2p` commands);
* the texts of other jobs (description, result and the whole chat) are requested by the agent
  with the `AI2P_GET_TASK` marker — instead of the `get_task_by_code` / `get_task_by_url` /
  `get_task_chat` tools, which it does not have either.

For more on the differences between the API and the CLI see the
Claude-Fable-5_cli document.

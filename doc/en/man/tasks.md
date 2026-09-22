# Tasks

**A task is the unit of work and at the same time the job for the AI.** That is the main thing
worth understanding about AI2P: the very same description a person reads with their eyes the
agent receives as a prompt. That is why a task is written the way it would be written for a live
executor — and the result depends on how it is written.

The list opens with the **"Tasks"** button on the left bar; the task list of a **project** is
also a tab in its card. A click on a row opens the **card tab** of the task.

---

## What a task keeps

Besides the obvious (the title, the description, the due date, the status, the priority) it has
what moves the work along by itself:

* **the executor** — one, from the members of the task's team; plus the list of **"can
  substitute"** for the case when the assigned one is busy (the order in the list = the order of
  preference);
* **the responsible person** — a human only; the agent's questions and confirmation requests are
  addressed to them;
* **skills** — what actually has to be done; the auto-pick works by them;
* **acceptance criteria** — how to tell that it is done; they travel to the agent together with
  the description;
* **blocking tasks** — the task is not started automatically until they are finished;
* **tags** — free words with no reference book: a tag is created by being typed and disappears
  when no task carries it any more;
* **the status on completion** — the state into which an agent that finished **normally** will
  put the task; "review" by default;
* **"time ↔ quality"** — 0.0 "be quick" … 1.0 "be thorough"; it is printed into the agent's job,
  an empty value is taken from the project settings **at the moment of the start**.

---

## How to look at the list

Four views, switched in the list toolbar; the chosen one is remembered, including between runs.

* **The board** — the columns are the states of the reference book, the header background is the
  colour of the state. A card shows a preview of the description. The columns are **rearranged by
  dragging a header**, and the order is remembered per account and project. The **cards, however,
  are not dragged around the board at all** — the status is changed on the task card, by the
  button next to the status.
* **The table** — sorting by a click on a column header (a second click changes the direction).
* **The hierarchy** — a tree by subordination. Here **dragging a row works**, with the mouse and
  with a finger (a row has a drag grip on the left), and there is "collapse/expand all". Only the
  top level is sorted: the order of children does not change.
* **Tags** — grouping by tags.

**The filter** is collapsed by default and is unfolded by the funnel button. When it is set, its
text description is shown next to it along with an "X" button — clearing everything at once.
Selection by tags adds up with **OR** ("show everything about the UI or about the build"): the
intersection is almost always empty and would look like a breakage.

The server order of the lists is **by descending numeric priority**. The queue takes tasks into
work in that same order, so the list shows what is going to happen.

A task with unanswered agent questions carries a noticeable **"?"** chip with the count in every
view.

---

## The task form

Three sections; the lower two are collapsed on opening.

**Main** — the title, the description, the responsible person, the executor and "can substitute".

**Extended** — the due date and the planned duration, the priority (the level and the number in
one row, in sync), the acceptance criteria, the blocking tasks, the import link, the "do not split
into subtasks" flag, **the status on completion** (visible only when the executor is an AI).

**Optimization** — what governs the size of the job and the selection of experience: the skills,
the tags, the "time ↔ quality" slider and the two checkboxes **"Put the parent task into the
prompt"** and **"Put the sibling tasks into the prompt"**. Both are **off** by default, and
that is not penny-pinching: the parent and neighbour blocks weigh up to 60,000 characters. The
agent is not left without context — it gets a line with the code and the title of the parent and
reads it itself when it really needs to.

Two things worth knowing about the form:

* **a click outside the window does not close it** — what has been typed is not lost; it can be
  closed without saving only by the "Cancel" button;
* **the blocking tasks** are looked up by the "Search" field next to it: the server searches both
  the title and the description at once (the description is a file, so it is the server that
  searches). The list holds no finished or cancelled tasks and not the task being edited; an
  already chosen blocking task always stays an item — otherwise there would be no way to remove
  it.

### Task type: linear, condition, loop

The **Advanced** section has a **Task type** field. Every task and every template node has one
of four types:

* **Linear** — an ordinary task, as always. This is the default, and every task and template
  created before the field appeared reads as linear.
* **Condition** — depending on the outcome of the task, one of two branches runs. For each branch
  you set the **task on “Yes”** and the **task on “No”** — only a **direct subtask** of this task
  can be chosen (so a freshly created task has an empty list: create the subtasks first). If a
  branch has no task, it has a **Create tasks** check box, and when that is cleared — a **Finish
  running the hierarchy** check box.
* **Loop (check first)** — the condition is checked before each round of subtasks.
* **Loop (check after)** — the condition is checked after each round of subtasks.

Both loops have a **loop limit** — how many rounds are allowed (a new task takes it from the
project's “Re-check rounds” setting; a template copy takes it from the template node) — and
a **Stop running the whole hierarchy when the limit is exceeded** check box.

The fields appear only for their own type: a linear task shows nothing new in the form. When a
task is created from a template, the type and all its parameters are carried over to the copy,
and the condition branch links are pointed at the tasks created from the nodes.

**What the agent does.** Conditions and loops are evaluated by the executor from the task
description and chat — AI2P does not analyse them. A Condition or Loop task gets a separate
block in its job: what to return and with which action. A condition decision is strictly
`true` ("Yes") or `false` ("No") via `set_condition_result`; a loop check result is `true`
(another pass) or `false` (exit) via `set_loop_result`. There is no third outcome: "yes", "1"
or nothing is an action error, and then the hierarchy run stops — the system never picks a
branch for the agent. The branch not taken, the passes and the loop limit are handled by the
hierarchy queue itself. If the chosen branch has no task and "Create tasks" is set, the agent
creates them before finishing — with `create_task` or `create_tasks_from_template` (from a
template node); and if it decides the work must not continue at all, it finishes the
hierarchy run with `stop_hierarchy`. A CLI agent does the same with the commands
`ai2p condition`, `ai2p loop`, `ai2p from-template`, `ai2p stop-hierarchy` or the markers
`AI2P_CONDITION`, `AI2P_LOOP`, `AI2P_FROM_TEMPLATE`, `AI2P_STOP_HIERARCHY`. How a condition and loops are passed during a hierarchy run is described in the chapter [How tasks are executed](TaskDo.md).

### The description is the prompt

The description field (and the acceptance criteria, and the chat, and the answer in the Inbox) is
a **single Markdown editor**: formatting buttons, an editable preview, inserting pictures from the
clipboard, from disk and by address, inserting video, the picture width.

Two buttons of that editor deserve a separate mention:

* **"@" — a reference to a project object.** It opens the object chooser (with previews and a
  search by name and number) and puts an `@obj:` reference at the cursor position. On **every**
  start of the job the reference expands into the object passport and the paths of its files —
  that is why the character's appearance does not have to be retold in every frame (see
  [Projects](progects.md)).
* **"All files"** (in the card header) — the list of all the files referenced in the description,
  the criteria, the chat and the results, plus the result files themselves: preview, description,
  copying the link, downloading and deleting. Only the task's own files may be deleted — that is
  the only way to remove gigabytes of video results.

---

## The task card

A two-line header: the code and the title, below it the toolbar — the chips of the state, of the
template, of the server, "who holds the job", "busy until", the question counter, then the icon
buttons: start/stop, edit, auto-split, delete, all files, a link to the task, refresh, "to the
parent task".

The tabs: **Description**, **Subtasks**, **Chat**, **Result**, **Jobs**, **History**.

While an active job is running for the task, the card **re-reads itself** every 2 seconds.

### There is a reason behind the status

The "paused" status by itself explains nothing, so the system writes the reason next to it in the
same words as in the "in the AI's hands" view:

| Mark | What happened |
|---|---|
| **"questions in the chat, N"** | the agent asked a person or the agent of a neighbouring task |
| **"Waiting until \<time\>"** | the executor ran out of limit, the start was postponed — the task will start by itself |
| **"waiting for subtasks"** | the task split into subtasks |
| **"hierarchy start"** | a hierarchical start queue is open for the task |
| **"waiting for blocking tasks"** | not all the blocking ones are in the "done" state |
| **"blocking task cancelled"** | the work the task was waiting for will not happen — it is up to a person |

### The "Jobs" tab

The list of jobs (every start of an agent is a job) and under it **the console of the selected
job**: the live line-by-line output of what the executor is doing **right now**. For media models
the ComfyUI console with the generation progress is piped there, for text ones — the start, every
tool call and the outcome, for local models — also the output of the model server itself. It is
exactly by the console that you see whether the work is going on or has stalled.

The buffer holds the last 1000 lines per job and lives in memory: after the application is
restarted the console of finished jobs is not restored. The permanent history is in the "History"
tab and in the results.

### The "Chat" tab — this is where you talk to the agent

The chat is not comments but a communication channel. Three things it exists for:

* **the agent asks.** A question is set off by a frame and a "?" icon, it may have answer-option
  buttons and a free-answer field. Until there is an answer the task stands paused, and the
  question is visible in the Inbox. Once answered, the work continues **from the same place**.
* **you interrupt the agent.** For a task in progress there is a note above the input field: the
  message will reach the executor **right in the middle of the work** — it will break off, read it
  and answer here. Nothing else needs to be pressed.
* **a question can be dismissed.** If the job that asked the question is no longer waiting for an
  answer (it failed, finished, was restarted), there is nobody to answer — then instead of the
  answer fields there is a **"Dismiss question"** button. The question leaves the counter, the
  Inbox and the pause reason at once, and stays in the correspondence with an honest note "The
  Question dismissed — there will be no answer".

---

## How tasks are started

**By hand** — the "Start" button on the card. For an AI this is the start of an agent, for a
person — a job in their Inbox. While a job is active "stop" stands in its place.

**Automatically** — when the parent finishes (a move into "review", "needs fix" or "done") its
direct children with the "automatic" start type are launched; when the last blocking task moves
into **"done"**, the tasks that were waiting for it start at once.

**On a schedule** — see [Schedule](schedule.md).

**As a whole hierarchy** — the three-arrow button on a task with subtasks. The queue goes **from
the bottom up**, from the deepest and the highest-priority ones; busy executors wait for their
turn, finished subtasks are skipped, and the task itself is started last. The confirmation has two
independent checkboxes: **"Also run tasks that stopped with an error"** and **"start the tasks
that need fixing too"** — both are off by default and are not remembered between presses, because
this is a decision here and now, not a card setting. One attempt per start: a task that fell back
into "needs fix" is not taken by the queue any more and is left to a person.

While the queue is open, a **"Stop the hierarchy run"** button appears next to it. And the "stop"
button of a task **inside** an open queue asks what exactly to stop: everything together with the
children, or only this task and the queue. Without that question a job that had been stopped would
be raised back by the queue itself on the next pass.

The step-by-step order of the pass, and conditions and loops in the queue, are described in the chapter [How tasks are executed](TaskDo.md).

**"Turn off subtask auto-start"** — the button is there too. The mark acts on the task
**and its whole subtree** and closes all three automatic starts (of the children, of those waiting
for a blocking task, of the auto-split queue). It does not restrict a manual start, and "start the
hierarchy" **removes** it — the person said "run" outright. If the automatic start is turned off
higher up the tree, the card carries an icon with a hint telling which task turned it off: keeping
silent about that is not allowed, otherwise it comes out as "I pressed it and nothing happens".

### When the executor runs out of limit

Pressing "Start" first asks about the remainder. If it is small, the **"The executor is running out of limit"** window opens — how much has been spent, when the window frees up, and four ways
out: **postpone the start** (the task will start by itself, the computer may be switched off),
**split into subtasks** (part of it will run on the remainder), **start now** by force, cancel.

If the limit cut off a job that was already running, the task does not "stop with an error" but
goes into "waiting" with a postponed start; when the time comes it is started as **a new job, from
the beginning** — what the agent did in the project files does not go anywhere.

---

## Subtasks

The "Subtasks" tab holds the children of this task: the ID, the title, the status, the numeric
priority, the executor, the due date. The status is changed right in the row. The "+" button opens
the same "blank task or template" choice as "new task": a blank one is created with the parent
already set, and the chosen template is **deployed as a subtask** and moves as a whole into the
parent's project.

**Auto-split** is a separate icon button in the header (visible while the "do not split" flag is
off and there is no active job). The agent splits the work into subtasks itself, giving each of
them skills and a numeric priority, and from there they are led by the orchestrator: the subtasks
are started in descending order of priority, every AI executor leads **one job at a time**, and the
finish of any subtask frees the executor and starts the next one.

The first call marks the task as split, so it will not be split a second time.

### Diagram: condition and loops

On the subtask diagram (the tab's third view) tasks of type "Condition" and "Loop" are recognised
by their shape, and their progress by colour. Only what the system knows for sure is drawn: the
task type, the branches set in it, the transition actually made and the pass count. Nothing is
guessed from the description text.

* **Condition** — a triangle above the task rectangle and another below it. The "Yes" branch
  arrow leaves the upper one, the "No" arrow the lower one. Until the transition is made both
  arrows are **yellow**; after it the arrow of the branch taken is **green** and the other one
  **grey**. If a branch has no task and "stop the run" is ticked, its arrow leads to a dark red
  round **STOP** sign, coloured by the same rules.
* **Loop before** — the task frame is repeated twice at the bottom and right; **loop after** —
  at the top and left. The oval at the bottom right shows passes done / pass limit (the task's
  own limit, or the project's one if the task has none). If the whole hierarchy run stopped on
  the loop, a **STOP** sign with a red arrow appears on its right.
* A **linear** task looks as before.

---

## A task on another server

In a cluster a task has an **owner server**: that is where it is edited and where its jobs are run.
On the other servers it is visible read-only, but **"start", "start the hierarchy" and the chat are
available**: the start goes to the owner as a request, the message gets there by replication — the
hints say exactly that. Editing, changing the status, deleting, splitting and dismissing a question
are for the owner only. The details are in [Several servers](servers.md).

---

## Small things that save time

* **A task that arrived from an external system** shows the import link in its header, and its
  "refresh" button asks: re-read it **from the source** (the title, the due date, the description
  and the new discussion messages) or only here.
* **"Working: \<nick\>"** appears next to the executor only when the one working is not the
  assigned one; every such substitution is also written as a message into the chat.
* **The link to the task** (the button in the header) opens a window with the local and the
  external address and copy buttons.
* **The task list re-reads itself** on any change: a task created elsewhere in the interface
  appears on the board without a "refresh".
* **A task cannot be moved between projects** — its files lie in the directory of its own project.
  The system will say "change the task's project first"; in the common "all tasks" tree the
  neighbouring rows are often from different projects, and an attempt to drag looks like a gesture
  that does not work.

## Next

* [How tasks are executed](TaskDo.md) — the order of a whole-hierarchy run.
* [Projects](progects.md) — the folder, the objects, the experience and the settings that affect
  tasks.
* [Templates](templates.md) — so as not to type the same task tree twice.
* [Executors](performers.md) — who will do the task and at what price.
* [Schedule](schedule.md) — starting by the calendar.

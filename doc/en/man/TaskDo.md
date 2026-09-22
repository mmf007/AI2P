# How tasks are executed

This chapter explains **in what order AI2P executes tasks** when you start a whole hierarchy with
the "Run hierarchy" button (the three arrows on the card of a task with subtasks). It also shows
how tasks of the types "Condition", "Loop (check first)" and "Loop (check after)" fit into that
order. How to set such tasks up and what the diagram shows is covered in the [Tasks](tasks.md)
chapter.

## The essentials in three lines

* The queue goes **from the bottom up**: first the deepest subtasks, then their parents, **the root
  last**. Among siblings the task with the higher numeric priority goes first.
* A parent is started only when **all its subtasks are finished**.
* Conditions and loops are **decided by the agent** from the task description and the chat. AI2P
  does not evaluate conditions itself: it stores the task type, carries out the agent's decision
  and counts the loop rounds.

## What "finished" means

For the queue a task is **finished** when it is in the **"review"**, **"done"** or **"cancelled"**
state. The queue skips such tasks and does not start them again.

The rule for **blocking** tasks is stricter: a blocking task counts as finished only in the
**"done"** or **"cancelled"** state. A task waiting for a blocking task that is in "review" keeps
waiting until a person accepts the result.

## How a queue pass works

Running the hierarchy marks the root as "queue open". From then on the system makes **passes**:
it walks the whole tree and starts everything that can be started right now. A pass repeats by
itself:

* when any task of this hierarchy finishes;
* when a new subtask appears in the hierarchy (for example, the agent created it);
* when a blocking task moves into "done" or "cancelled";
* once a minute by the watchdog — in case something became free without an event (for example,
  a deferred start came due).

Within one pass, starting from the root, the same thing is done for every task:

1. **Children first.** Subtasks are walked in descending priority, and on equal priority in the
   order they were created. Each subtask is walked by the same rule, so the queue goes down to
   the deepest tasks. **All** branches are walked: jobs are handed out to different executors at
   once, not one branch at a time.
2. **Then the task itself.** If it is already finished, it is skipped. If not all of its subtasks
   are finished, it waits. Otherwise the queue tries to start it.

Template tasks and deleted tasks are not seen by the queue at all.

### When a task is not started after all

Before starting a task the queue checks it. The task **waits** (the queue stays open and comes
back to it on the next pass) if:

* it is already running, waiting for an answer to a question or paused;
* it **stopped with an error** or went to **"needs fix"**, and "Also run tasks that stopped with
  an error" / "Also run tasks in the "needs fix" state" was not ticked when the run was started.
  A ticked box gives such a task **one** retry per press of the button;
* not all of its **blocking** tasks are finished;
* it has a **deferred start** (for example, its executor ran out of limit);
* its executor is **busy** with another job and there is no free substitute from the "May replace
  the executor" list. One AI executor handles one job at a time;
* the task belongs to **another server**: a start request goes there, and the queue waits for the
  result to arrive by replication.

A task is **skipped** if it does not have exactly one executor assigned, or the executor is not
found or is turned off. Such a task will never go by itself, and its parent will wait: assign an
executor, and the next pass will pick it up.

### When the queue closes

* **Nothing to start and nothing to wait for** — the whole tree is finished. This is the normal end.
* **The root has been handed in** ("review", "done", "cancelled") — the queue was leading to the
  start of the root, and it has happened. Whatever is left in the subtree, the queue closes. If
  you return the root to work, open the queue again with the button.
* **A stop** — ordered by a condition or loop task (see below), by the agent with the
  `stop_hierarchy` action, or by a person with the "stop the hierarchy run" button.

## Example: a linear tree

```
T-1 Root
├── T-2 Analysis         priority 20
│   ├── T-4 Collect data priority 10
│   └── T-5 Review       priority 15
└── T-3 Draft            priority 10
```

Order: T-5 and T-4 (T-5 has a higher priority, so it is taken first; if the executors differ,
both start at once) → T-3 (its branch is walked in the same pass, so with a free executor it
starts together with T-5) → T-2, when T-4 and T-5 are finished → T-1, when T-2 and T-3 are
finished.

## A "Condition" task

When the queue reaches a condition, it **starts the task itself at once**, without waiting for
the children, and does not go into the children yet.

1. From the description and the chat the agent decides **"Yes"** (`true`) or **"No"** (`false`)
   and reports the decision with the `set_condition_result` action (a CLI agent uses
   `ai2p condition` or the `AI2P_CONDITION` marker). There is no third answer.
2. When the condition's job has finished, the queue reads the decision:
   * the **branch not taken** — the subtask set for the other answer — is cancelled together with
     its whole subtree (tasks that are already finished are left alone);
   * the **chosen branch** has a task — it runs in the normal order;
   * the chosen branch has no task but **"Create tasks"** is set — the agent had to create the
     subtasks before handing in (`create_task` or `create_tasks_from_template`), and they run in
     the normal order;
   * the chosen branch has no task and **"Finish running the hierarchy"** is set — the queue stops.
3. The **other children** of the condition (not named in either branch) run in the normal order.
   After the condition the queue continues through the tree as usual.

If the agent **did not report a decision**, the queue **stops**: the system does not choose a
branch for the agent — a person decides. If the condition is returned to "pending" or "draft",
the previous decision is forgotten and the condition is evaluated again.

## A "Loop (check first)" task

The condition is checked **before** each round of subtasks.

1. The queue **starts the task itself at once**, like a condition — this is the analyser task.
   The agent checks the loop conditions from the description and reports the outcome with the
   `set_loop_result` action (`ai2p loop`, the `AI2P_LOOP` marker): `true` — a round is needed,
   `false` — exit.
2. **`true`** — the task is paused with the reason **"Waiting for the loop to finish"**, and its
   subtasks run in the normal order. The analyser's executor is free meanwhile and can take
   subtasks.
3. When **all** subtasks are finished, the round is counted. The analyser is **restarted** and
   checks the conditions again. On a new round the whole subtree returns to "pending" and runs
   again.
4. **`false`** — the loop is over, the analyser task becomes "done", the queue moves on. If the
   condition fails **on the very first round**, the subtasks in "pending" and "draft" (with all
   their descendants) are cancelled.

## A "Loop (check after)" task

The condition is checked **after** each round of subtasks.

1. The queue treats it like an ordinary parent: **subtasks first**, the analyser waits.
2. When all subtasks are finished, the **analyser** is started and the agent reports the outcome
   (`set_loop_result`).
3. **`true`** — the whole subtree returns to "pending", the task is paused with "Waiting for the
   loop to finish", and the subtasks go through a new round. After the round the analyser is
   restarted.
4. **`false`** — the loop is over, the task becomes "done", the queue moves on.

## The round limit

Both loops have a **loop limit** — a task field; if it is empty, the project setting "Re-check
rounds" is used. When the number of rounds reaches the limit, the loop ends as if the answer were
`false`, and the task becomes "done". If the task has **"Stop running the whole hierarchy when
the limit is exceeded"** ticked, the whole queue stops as well. A **linear** task has no loop:
even if its description contains a repeat condition, the queue runs it once.

If a loop's analyser **did not report an outcome**, the queue stops, as with a condition.

On a new round, nested conditions and loops **forget** their previous decisions: each round they
are made again.

## Stopping and resuming

* When a condition or a loop stops the queue, **no new tasks are started**, and jobs already
  running finish their work. On the diagram such a task gets a **STOP** sign.
* The **"stop the hierarchy run"** button closes the queue; **"stop"** on a task inside the queue
  asks whether to close the queue as well.
* **To continue**, press "Run hierarchy" again. Finished tasks are skipped, and the queue carries
  on from where it stopped.

## What you see on screen

* The root of an open queue shows the reason **"hierarchy run"** on its card, next to it — the
  button that stops the queue.
* A loop task during a round is paused with the reason **"Waiting for the loop to finish"**.
* On the subtask **diagram** the layout follows the queue order, and the transitions of conditions
  and the round count of loops are shown by the colour of the arrows and the "done / limit" oval —
  see the [Tasks](tasks.md) chapter for details.

## Next

* [Tasks](tasks.md) — the task form, the "Task type" field, starting and the diagram.
* [Templates](templates.md) — how to keep a tree with conditions and loops ready to use.
* [Executors](performers.md) — who executes tasks and when an executor is busy.

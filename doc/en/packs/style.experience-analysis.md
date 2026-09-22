# Experience analysis

**Pack code:** `style.experience-analysis` · **Records:** 1 · **Template nodes:** 3

## What this pack is

This is **not a working style** but a **working process**: the pack brings a ready tree of task
template nodes for the periodic review of the accumulated experience. It has a single record — the
general rule “experience is not deleted, it is switched off”; everything else lives in the jobs of
the nodes.

## Why it is needed

Experience piles up faster than it ages. After half a year of work the list holds hundreds of
records: some duplicate each other, some are stale, some ended up in the wrong scope. All of it
steals room from what is needed — the experience limit of a job is one for everybody, and a
useless record pushes out a useful one.

Nobody will sort that out by hand. So the review is filed as **a task for an AI agent**, on a
schedule — once a week or once a month.

## What is inside

| Node | Scope | What for |
|---|---|---|
| **Experience analysis** | root | the full instruction: what to read, what to do, what not to do, what to put in the report |
| **Review of the organisation's general experience** | `general` | reviewing the general working rules |
| **Review of the project experience** | `project` | reviewing one project's experience; copied once per project |

The children receive the parent's text in full: the common order of work is written in the root
once, and a child only adds its own scope to it.

**What the job tells the performer to do:** read the usage statistics (`experience_usage`), walk
the records page by page (`list_experience`), then **merge** duplicates into one summary record,
**split** records that mix two lessons, **tag** them with topics and skills, **move** records into
their proper scope (`move_experience`) and **switch off** the stale ones
(`set_experience_active`).

**What is forbidden:** deleting records, touching records of another server, switching off the
rules shipped with the distribution, and rewriting the meaning while merging.

## The switch-off rule

It is written **in words** in the root node's job, not hard-wired in code:

> A record is switched off if it reached no job for three months **while** jobs on its topic did
> occur in that time (the topic is the record's tags). “It reached nothing because there were no
> such tasks” — do not switch it off. For a record with no statistics at all, count the period
> from its creation date.

This is the most important line of the pack and the first thing worth adjusting: the period, the
definition of a topic and the condition itself are edited straight in the template, with no new
release of the program.

## How to use it

1. **Settings → Experience packs → “Experience analysis” → Install**, scope — **project** or
   **template node**. The “general rules of the organisation” scope has no project at all, and a
   task template cannot live without a project in AI2P: the nodes are then not created (the record
   installs as usual).
2. **Project templates** — the root node “Experience analysis” has appeared with two children.
   Copy the “Review of the project experience” child once per project and point each copy at its
   own project.
3. **Settings → Schedules** — create a periodic schedule (weekly or monthly) and pick the
   **Experience analysis** node as its template. Installing a pack deliberately does not create a
   schedule: running tasks spends money on the model.

Only the **root** node fits a schedule: the children come along as a copy.

## What is worth adjusting

* **Three months** is a guessed number. If you release once a quarter, three months may simply
  hold no task on a given topic; take half a year.
* **The period.** Weekly makes sense only with a large flow of tasks; in a quiet project a monthly
  review is enough, and a weekly one will burn money for nothing.
* **The report.** The acceptance criteria demand the numbers “were / left active” and the record
  ids — without them there is no way to check the work. If you have your own reporting
  requirements, put them into the node's acceptance criteria, not into the description.
* **The installation scope.** The pack's single record belongs in the general rules; the template
  nodes live in a project. So the usual arrangement is to install the pack into the “project”
  scope of the project where you keep your service tasks.
* **Cleaning up after the switch-off.** The review itself archives nothing. File an archiving rule
  “experience + inactive only + age does not matter”, otherwise the switched-off records will just
  stay in the lists.

## Where to read on

* The contents of the `packs/` section — what a pack is in general.
* The guide chapter **“Experience”** (`man/experience.md`) — the scopes, the selection into a job,
  search, moving a record, activity and archiving.

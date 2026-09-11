# Templates

**A template is a blank of a working process: a tree of tasks with descriptions, skills, an order
and accumulated experience.** One action deploys a new set of real tasks out of it.

The list opens with the **"Templates"** button on the left bar; the list of the templates of
**this project** is also a tab in the project card.

---

## What they are for

For the same reason as checklists: typical work repeats itself, and phrasing it anew every time is
both slow and worse. "Add a character to the game", "release a version", "shoot a clip" — that is
a tree of a dozen tasks with descriptions and acceptance criteria already written.

But an AI2P template has a second, less obvious role, and it matters more than the first one. A
template node has its own **experience**: the lessons gained in the previous runs of this process
are put into the job of a task created from that node. That is, a template **grows wiser from
being used** — from a "form to fill in" it becomes the place where the knowledge of how to do this
work properly lives.

---

## What a template is technically

There is no separate "template" entity in the system. A template is **the same task** with a
"template" flag; its child tasks are marked as template ones too. Everything else follows from
that:

* a template **is not executed**: it does not get onto the board, into the job queue or into the
  executor pick;
* a template may have the project, the team, the executors and the responsible person **left
  empty**;
* it has the same fields as a task — including the skills, the priority, the acceptance criteria,
  the blocking tasks, the tags, "time ↔ quality" and "the status on completion"; all of that is
  **copied** into the created task.

**A template with a project belongs to the project, a template without a project is a common
one.** The choice lists (the new task form, the schedule form) show the templates of their own
project **and** the common ones; other people's do not get into them.

---

## The template card

This is a task card with four differences:

| The difference | Why |
|---|---|
| there is no **"Chat"** tab | a template has no correspondence |
| there is no **"Result"** tab | nor any results |
| instead of the "Start" button there is **"Create a task"** | a template is not executed, it is deployed |
| the **"Experience"** and **"Statistics"** tabs are added | that is exactly what a template lives long for |

### The "Experience" tab

The experience records of the node and of **its whole subtree**: the node code, the skill, the
tags, the text, by whom and when it was created and changed. It is edited by hand (add / change /
delete), and the agent writes here too.

What is worth understanding about the selection — otherwise the experience either does not arrive
or bloats the prompt:

* the **skill** of a record means "only an executor with this skill will read this record";
  records **without a skill** are common and are shown under any filter;
* **tags** are a second, independent filter (the "or" rule, as with tasks);
* the **"always load"** checkbox drags the record into the job past the selection; in the list
  such a record is marked with an **"always"** chip, first in the tag column;
* all of it together is limited by the project setting **"experience insertion limit"**; when the
  limit is not enough, the ones marked "always" are taken first, then the most aimed ones — **the
  experience of a template node beats the experience of a project, and that beats the general
  rules of the organization**.

### The "Statistics" tab

The state changes of the tasks **created from this template**: the task, the state, the date and
time, the executor. A click on a row opens the task. This is a way to see where the process usually
stumbles.

---

## How to deploy a template

In two ways.

**From the template card** — the **"Create a task"** button. The whole template is copied together
with the hierarchy of its child tasks, and the "template" flag is removed from the copies.

**From the new task form** — the "Add" button in the task list first asks: **a blank task** or
**from a template**.

Before the copying the system asks about:

* **the base date** — if the head of the template has a due date; the due dates of all the tasks of
  the new process will be shifted from it;
* **the automatic executor pick** — the same choice as in the new task form.

What happens to the links when copying: the **blocking tasks** of a template point at template
nodes, and on deployment the references are replaced by the tasks created from those nodes (a
reference to a node that is not being copied is dropped). The **default responsible person** of the
project is set only into those nodes where no responsible person is given — the one written in the
template wins.

The third way of deploying is a **schedule**: a schedule firing creates a copy of the template
itself, by the calendar (see [Schedule](schedule.md)).

---

## The agent edits templates too

The AI agent has the actions `list_templates`, `create_template` and `update_template` — the
template nodes **of its own project**: look at the list, create a new node (title, description,
criteria, skills, parent), fix an existing one by its code (a field that is not passed is not
changed).

Two rules worth knowing:

* writing goes **on the owner server of the template node**; from another server a clear refusal
  comes back;
* the actions are closed by security rules like any others (`AI2P.Templates.List`,
  `AI2P.Templates.Create`, `AI2P.Templates.Update`).

A template node is also read by the ordinary job-reading tools — in the card it is marked
"TEMPLATE NODE".

---

## Small things that save time

* **Two list views** — a table and a hierarchy, with the same sortings and search as the task list.
* In the **explorer** templates lie along two paths: inside a project (the "Templates" branch of
  the project) and in the root "Templates" → "Projects" branch, where the common templates stand
  right in the branch.
* A template is conveniently made **out of a task that went well**: copy its description and
  criteria into a new node while you still remember what exactly worked, and write the lesson into
  the node's experience.

## Next

* [Tasks](tasks.md) — what comes out of a template.
* [Projects](progects.md) — the project experience and the limit of its insertion.
* [Schedule](schedule.md) — deploying a template by the calendar.

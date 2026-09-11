# Executors

**An executor is the one who does the task.** A person or an AI: for the system this is one and
the same notion with one and the same set of fields, and a task does not distinguish whom it was
given to. That is exactly why the work can be thrown from an AI to a person and back without
rewriting anything.

The list opens with the **"Executors"** button on the left bar (and the menu item of the same
name).

---

## What it is for

An executor answers three of the system's questions:

1. **Whom to give the work to.** A task has exactly one executor, and the choice is limited to the
   members of its team (see [Teams](teams.md)).
2. **Who can do it.** An executor has a **capability declaration**: which skills it has and how
   well. The auto-pick works by it.
3. **With what and at what price it works.** For an AI — the model, the key, the parameters, the
   price per million tokens, the limits and the timeout. For a person — the mail, the phone and
   the busyness.

An account and an executor are **different things**. An account (Settings → Users) is needed to
sign in; an executor is needed to work. A human executor has an "account" field that ties them
together; an executor without an account does not sign into the system, but tasks may still be
listed against them — that is how a contractor who does not work in AI2P is created.

---

## What is in the list

The list shows what an executor is chosen by, not everything at once:

| Column | What it means |
|---|---|
| **Nick** | the external name, unique across the whole organization; the executor is signed with it everywhere |
| **Type** | a person, an AI or «auto software» (a program) |
| **Active** | an inactive one is not filled into new teams and tasks; the old links stay |
| **Busy until** | up to which moment the executor is busy (a time in the past is not shown) |
| **% of limit** | the current spend of the provider's limit — if the provider sends it |
| **Window limit** | our own count: `spent/limit` over a sliding window and when it frees up |

Sorting is by a click on a column header (a second click changes the direction). Executors without
limits go to the end when sorting by "window limit".

---

## The executor form

The form is wider than an ordinary dialog and changes depending on the type. There is no point in
going through it field by field — what matters are the four places where it is easy to go wrong.

### The internal name

* **For an AI this is not a text but a choice from the model reference book** — and only out of
  the **active** records. Together with the model the profile (the connection parameters) and the
  capability declaration are filled in synchronously: for an executor they are "the same as the
  model's". Re-pick the model and both files change.
* **For a person** this is the full name, and its uniqueness is tracked.

Hence a time-saving rule: **if the model you need is not in the list, it is inactive.** A cloud
model without an API key and a local one without downloaded files cannot be active. You have to go
to Settings → Models, not to look for a mistake in the executor form.

### The profile and the capability declaration

Both are JSON files, but the form does not show the paths to them: they are edited by buttons.

* **The profile** is how to connect: the provider, the model, the address, the reference to the
  key, the temperature, the timeouts, the LoRA training settings. A person has a different
  profile — the mail, the phone and other human data.
* **The capability declaration** is what the executor can do: the list of skills with a mastery
  score, the input and output formats, the price (`in_per_1m` + `out_per_1m`). Its editor is **one
  and the same** for an AI, for a person and for a record of the model reference book.

The declaration is not an ornament: a task with a skill that is in no declaration will never find
an executor, and the auto-pick counts its score exactly by it.

### Busyness, limits and the timeout (for AI only)

* **"Busy until"** for an AI **is not editable**: the system sets it itself when the provider
  answers "the limit is used up". For a person these are ordinary date and time, and clearing the
  date removes the busyness.
* **"Token limit per window" and "the limit window, h"** — our own count of the spend, needed where
  the provider does not report the remainder (the Claude Code subscription: a 5-hour window). Both
  fields are set by a person, empirically. **Empty or 0 means the limits are not given**, and the
  whole mechanics is switched off entirely: the remainder is not counted, there are no warnings,
  the start goes as before.
* **"The answer timeout, min"** — how long to wait for the model. Empty means 30 minutes, **0
  means no limit**. An expired timeout is an error of the job, not a limit: the executor is not
  marked busy and the start is not postponed.

What happens when there is almost no limit left: the job **is not created**, the start of the task
is postponed to the moment the window frees up, and a warning goes into the task chat — how much
has been spent and where the start has been moved to. The postponement survives switching the
computer off, and a person may always start the task by force.

### The mail for notifications

The **"Take the e-mail from the user login"** checkbox is on by default, and then there is no
address field at all — instead it says which address will be used. If you clear the checkbox and
leave the field empty, that means **"no notifications go to this executor"**, and the form says so.

---

## The third executor type: the software executor

Besides a person and an AI there is a third type — the **software executor** («auto software»).
The work is done neither by a person nor by a language model but by **a program on this
computer**: a LoRA adapter trainer, a converter, a computing utility. The task itself stays an
ordinary task — with a deadline, acceptance, a console, a history and a result.

The internal name of such an executor is neither a model nor a full name, but a **pair of
«plugin + operation»**:

* the **plugin** is a record of the [Plugins and MCP](plugins.md) section **of this server**:
  it brings the program and the path to it;
* the **operation** is a named long run from the plugin manifest (for the musubi trainer it is
  «LoRA training»). An operation marked **«Single instance»** carries that mark in the form:
  while it is busy, the second task **waits** in the queue instead of failing.

How the software executor differs from an AI one — and why:

| The difference | Why it is so |
|---|---|
| **There is no prompt** | the program does not read text: everything it needs comes from the declared settings of the plugin record and from paths inside the project folder. An arbitrary command line exists for nobody |
| **No tokens, no price, no limits** | there is nothing to count and nobody to pay: the form has neither the limits block nor the spending line, and such an executor does not take part in «Billing» |
| **The server is mandatory** | the program sits on a particular computer. The «server» field is filled with the server where the executor was created and is never empty: an empty value would mean «at the conductor's», and on a change of conductor all such work would move to another machine. A task of another server cannot be given to it — the system refuses in words |
| **One job at a time** | while the program is computing, a second job is not given to it; the task waits exactly as it waits for a busy AI |
| **Auto-pick never takes it** | see below |

**Why auto-pick walks past the software executor.** Both pick buttons — «an AI first, then a
person» and «a person first, then an AI» — discard such an executor **by type**, before
counting skills and price at all. The third type is not expressible by the order «AI →
person», and a program's declaration can say anything: otherwise a task «draw a picture» would
silently go to the LoRA trainer. So a software executor is assigned **by hand** — or the task
is created from a template by the very button that needs this work (that is what «Train» does
in the [LoRA editor](LoRAEditor.md)).

Such an executor is created by the usual «new executor» button: the type «auto software», then
the plugin and the operation from the drop-down lists. It has neither a profile nor a
declaration button.

---

## How an executor is chosen

By hand — from the drop-down list in the task form (only the members of its team).

Automatically — by the two buttons next to the executor field: **"AI first, then human"** and
**"human first, then AI"**. The candidates are the **active** members of the task's team, and the
activity is a double one: both of the executor itself and of its membership in this particular
team. The score is counted like this:

```
score = bias × quality + (1 − bias) × cheapness
```

where *quality* is the average mastery of the required skills from the declaration, *cheapness* is
the inverse of the price from the same place, and `bias` is the project setting **"cost ↔
quality"** (see [Projects](progects.md)). A busy executor is skipped; if all the suitable ones are
busy, the system names the reason and the time when they free up. The choice is explained right in
the interface — the quality, the price, the score.

There are also **substitute executors**: the "May replace the executor" list in the task form.
The order in it is the order of preference, the first free one is taken. The assigned executor is
not changed by a substitution — the line "Working: \<nick\>" appears on the card.

---

## The AIs worth creating right away

After the first start the system creates three of them — `Jon`, `Bob` and `Stiv` — on the models
with the best skills for the chosen typical use. That is a sensible starting point, and here is
why there are three of them and not one: **every AI executor leads one job at a time**. One and the
same model can work on several tasks at once only through **different executors**. So "add
capacity" here means "add an executor", not "buy a bigger key".

---

## Special cases

* **A local model ties an executor to a server.** The weights and the start command lie on a
  particular computer, so such an executor has a "server" field; it cannot be assigned to a task of
  another server — the system refuses in words ("it works on a model of the server S1 — pass the
  task there"). If you switch a local model off at your place, your executors on that model stop
  being active too; the executors of another server are not affected.
* **The executor list is edited only on the conductor** of the organization: executors are assigned
  to the tasks of all the servers, and the list has to be a single one. The exception is creating a
  member when an account signs into the organization.
* **One account — one executor.** An account cannot be tied to two executors, saving is blocked
  with a clear error.

## Next

* [Teams](teams.md) — teams are assembled out of executors, and it is the team that sets the circle
  of a task's executors.
* [Projects](progects.md) — the "cost ↔ quality" setting the auto-pick depends on.
* [AI models](../models/README.md) — what models stand behind the AI executors.

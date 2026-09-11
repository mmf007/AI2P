# Schedule

**A schedule is the calendar by which the system creates work itself.**
For example:
* once a week deploy the "weekly report" template
* once a month — "close the month", once a quarter — "the quarterly review"
* once a night — take the old tasks into the archive.

It opens with the calendar button on the left bar (and the menu item of the same name).

---

## What it can start

Two different kinds of firing, and it is visible right in the form:

1. **A copy of a task template.** A firing deploys the template exactly the same way the "Create a
   task" button in its card does: with the whole hierarchy of subtasks, with the executor pick and
   with the base date.
2. **A system action.** No task is created and no executor is picked, so the fields of the
   template, the project, the offset and the pick rule are hidden entirely in the form. Today there
   is one action — **automatic archiving** (see [Archiving](Archives.md)).

---

## The schedule fields

| Field | What for |
|---|---|
| **Project** | the current one by default; it limits the template choice and is shown as a column. May be empty |
| **Template** | a top-level one; the form has a search by name. The templates of the chosen project **and** the templates without a project are visible |
| **The base date offset** | hours and minutes; the base date of the created tasks = the moment of the firing + the offset |
| **Executor pick rule** | "AI first, then human", "human first, then AI" or no auto-pick |
| **Once / periodically** | a one-off one has the date and time of the start |
| **The period** | weekly (the days of the week as chips + the time, like an alarm clock in a phone), monthly (the day of the month + the time), quarterly (the month of the quarter 1–3 + the day + the time, the quarters starting from January) |
| **The planned duration, min** | for the overlap check |
| **Server** | the schedule is edited on it and fires on it |
| **Active** | a switched-off schedule does not fire |

The times of the periods are **the local time of the computer**. In a short month the day is
squeezed to the last one: "the 31st" in February will fire on the 28th (or the 29th).

### The server is not a formality

A schedule is replicated between the servers of the cluster, and **without the "server" field one
schedule would fire on every server at once**. That is why it has an owner; if none is given, the
schedule is led by the conductor of the organization. The schedule list has a filter by servers,
and **by default it shows only the current one** — otherwise the picture would look twice as dense
as it is.

### The overlap check

If an executor is named in the template (in any of its nodes), on saving the system checks that
they are not busy with another active schedule at the same time: the busyness windows (the firing +
the offset, the length being the planned duration) are compared over the coming 62 days. An overlap
is **a saving error** naming the executor, the schedule and the time, not a silent superposition.

---

## Two views

They are switched in the toolbar, the choice is remembered between runs.

* **The table** — the ID, the project, the template (or the action name), "when" (a summary of the
  kind and the period), the offset, the duration, the pick rule, "active". Sorting by a click on
  any column.
* **The calendar** — a monthly grid with page turning. In a day cell there are the firing chips:
  first the time, then the template code or the start of its title (which exactly is set by the
  "Schedule calendar cell" setting in Settings → Main). The chip's tooltip is "Project —
  Template name", a click opens the form.

---

## Overdue firings

The main peculiarity of the schedule in AI2P, and it is worth understanding at once: **AI2P is a
program on your computer, not a service in the cloud.** While the computer is off, there is nothing
to fire.

That is why on the application's start the system collects everything that was missed into the list
of **overdue firings** — it is shown **at the top** of the tab: the schedule, the template, how many
were missed, the last time. A person decides on each of them: **"start now"** or **"remove from the
list"**.

Two consequences, because of which it is otherwise unclear:

* the firings that come **while the application is running** are started automatically (a check once
  a minute) — nothing has to be decided;
* **while a schedule has overdue firings hanging, its new firings wait for the person's decision**.
  If a schedule "went silent", look at the top of this tab first.

AI2P may be kept running permanently — for that it is made into an [operating system
service](service.md); then there are usually no overdue firings at all.

---

## Small things that save time

* A firing event is written into the log (`schedule.triggered`) — in the "Work history" you can see
  what was deployed and when.
* The blocking tasks of the copied nodes work as usual: the start will wait for them to finish.
* A schedule with an empty project sees all the templates without a project — that is a convenient
  way to create common regular processes not tied to one project.

## Next

* [Templates](templates.md) — what a schedule deploys.
* [Archiving](Archives.md) — the only system action on a schedule today.
* [Running AI2P as an operating system service](service.md) — so that it fires without you.

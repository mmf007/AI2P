# Projects

**A project is the container of all the work:** tasks, templates, objects, security rules,
experience and — the most important thing — the **folder on disk** in which the AI agent reads
and writes files.

The list opens with the **"Projects"** button on the left bar. It is also the first thing a
person who has not chosen a project yet sees. A click on a row opens the **"Project" tab** —
the card with its tabs; the chosen project is remembered and filled into everything new.

---

## What it is for

A project answers the questions that would otherwise have to be repeated in every task:

* **where the files are** — the project folder; without it the agent is not given the file
  tools at all;
* **who works** — the project team, and from it the circle of possible task executors;
* **how to choose an executor** — the "cost ↔ quality" slider;
* **what the agent already knows** — the project experience, which is put into every job;
* **what the agent must not do** — the project security rules.

## The project folder — why it is per-server

The project as a whole is replicated between the servers of the cluster, but **each computer has
its own directory**: `D:\work\game` on one, `~/projects/game` on another. That is why the path
is kept as **a separate row per server** and is edited only from that server. In the project
form the directories of the other servers are shown read-only — you can see where the project is
already deployed.

The same rule leads to something that sometimes surprises people: **the "active" flag is
per-server too** (in the form it is labelled exactly so — "active on this server"), and a project
whose directory is not set here cannot be made active here. Otherwise tasks would be started
"into nowhere".

The path may be typed from the home directory (`~/work/project`) — "~" is expanded both by the
folder chooser and on saving; the database keeps it already expanded, because it is used by the
agent tools, by the CLI sandbox and by file replication.

### The `Common` folder

This is a path **inside** the project directory whose contents are replicated between servers:
a quick way to pass results — media above all — to a neighbour. It is set on each server
separately; empty means "not replicated".

The path here is **relative only** (`media`, `doc/common`): a full path, `~/…` and a step up
`..` are rejected with a clear error on saving. Inside it the **`.repignore`** filter works —
the syntax is that of `.gitignore`, it is edited by the filter button next to the field.

### The name and the storage directory

The project name is unique (saving with a taken name is blocked) and is suggested from the folder
path. The project's **file storage** directory, however, is named by its external code
(`projects/PRJ-3`), and renaming the project does not touch it: the files and the relative paths
stay where they are.

---

## The card tabs

### Main

The project form: the folder, `Common`, the name, the team, "active", the setting sliders. It
opens **in read mode** — editing is switched on by the pencil in the top right corner, and
"save" appears in its place.

The settings that live here are worth understanding:

| Setting | What it does |
|---|---|
| **cost ↔ quality** | 0.0 — the auto-pick takes the cheaper ones, 1.0 — the better ones; 0.5 by default |
| **time ↔ quality** | the default for the task field of the same name: 0.0 — "be quick, quality may suffer", 1.0 — "take all the time you need"; 0.5 by default |
| **experience insertion limit** | how many characters of experience at most go into a job — in total over the general rules, the project experience and the template node experience; 100,000 by default |
| **Re-check rounds** | how many times a test-run task may return its neighbours for fixing and wait for them; 3 by default |
| **default responsible person** | a person from the team whom the form will fill into a new task; may be left empty |
| **object reference format** | what the interface buttons put into the clipboard: `@obj:OBJ-3` or `@obj:[Hero Vasya]`. **Both** forms are always read |

Two subtleties about the "default responsible person": it is the **form** that fills it in, not
the server — so a task created past the form (an agent's subtask, an import) gets no responsible
person; and changing the team clears a responsible person who is not in the new team.

### Tasks

The same task list (board / table / hierarchy, filter, search), filtered by this project. A new
task created from here gets **this** project and its team. The view, the sorting and the filter
are remembered, including between runs. About the task itself — [Tasks](tasks.md).

### Team

The members of the project team with their work statuses and the "start"/"stop" buttons — the
same ones as in the team list (see [Teams](teams.md)), only here, close at hand. Two views:
hierarchy (the default) and table.

### Objects

The list of the objects of **this** project: characters, locations, props, styles, reference
frames, LoRA adapters. The organization has no common object list on purpose — an object belongs
to a project. An object holds a **passport** — verbatim text for the prompt — and the task
description carries a **reference `@obj:OBJ-3`**, which on every start of a job expands into that
passport and the paths of the reference files.

Everything else — the kinds of objects, the four views of the list, the object tab and its
sub-objects, the two references, "what goes into the model", the owning server — has a chapter of
its own: **[Project objects](objects.md)**.

### Security

The security rules of **this project**: what the agent is allowed to do, what needs a person's
confirmation, what is forbidden. The project rules refine the organization rules (Settings →
Security), and the task rules refine the project rules.

### Templates

The same template list as the common one, but limited to this project; the "new template" button
fills the project in itself. The details are in [Templates](templates.md).

### Experience

**The project experience** is the generalisation of the work that **any** of its tasks receives.
A table: the skill, the text, by whom and when it was created and changed. The skill filter is a
multiple one, and records **with no skill are shown under any filter** — they are common ones.

A skill on a record means literally "only an executor with this skill will read this record", and
that is the main instrument against a bloated prompt: a narrow lesson must not travel to
everybody. Experience records are written by the agent itself too, when it finds out something
important for future tasks.

Three levels of experience, from the general to the particular: **the general rules of the
organization** (Settings → General experience) → **the project experience** (here) → **the
experience of a template node** ([Templates](templates.md)). When all of it together does not fit
into the limit, the selection goes by aim: a template node beats a project, a project beats the
general rules.

### History

The same work log as the common "Work history" screen, but hard-limited to this project: filters
by executor and by event type, a click on a task code opens its card, a click on the "Details"
cell shows the whole event with a "copy" button. The start of the team's work and the connection
result of every member with the error text land here too.

---

## Small things that save time

* **The current project** is switched in the top toolbar ("Project: …"), not by a "choose" button
  in the list. It decides where a new task will go.
* One person works with several projects — that is why the project list stayed just a list, and
  the work goes on in tabs.
* **An inactive project** is not filled into new tasks.
* The tab title has two lines: the kind ("Project") and either the start of the name or the short
  code (`PRJ-2`) — it is switched in Settings → Main.

## Next

* [Tasks](tasks.md) — what a project is created for in the first place.
* [Project objects](objects.md) — characters, locations, styles and LoRA adapters.
* [Teams](teams.md) — who works in the project.
* [Templates](templates.md) — how to deploy a typical process in this project.
* [The LoRA editor](LoRAEditor.md) — training an adapter from a project object.

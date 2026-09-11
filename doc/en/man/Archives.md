# Archiving

**Archiving** is moving data from the working environment into an archive one. It exists to
reduce the operational volume: closed projects, finished task hierarchies, templates and
objects that have done their job move aside, and the lists, the task tree and the search stop
showing them.

Archiving is done **per organization**. Data in an archive **cannot be changed**: it can only
be viewed or copied back into the working environment.

An archive is arranged simply. All archives of an organization live in its `arc` subdirectory:

* an **open** archive is a directory `arc/<code>/`. Inside there is its own SQLite database of
  **the very same schema** as the organization's one and a `projects` subdirectory with the
  project files. That is, an archive directory is arranged like an organization directory —
  which is exactly why it can be viewed with the ordinary screens of the program;
* a **closed** archive is a single file `arc/<code>.zip`.

---

## 1. The states of an archive

The "Active" column of the archive row holds one of four words, and they are glued together
from **two different things**:

| State | What it means | One per cluster or one per server |
|---|---|---|
| **current** | the archive the data is being moved into right now | a property of the archive itself, one per organization, **replicated** |
| **open** | lies unpacked on this computer — it can be switched to and viewed | own for every server, **not replicated** |
| **closed** | lies on this computer as a single `.zip` file — there is nothing to view | own for every server, **not replicated** |
| **deleted** | removed from this server (or never arrived here) | own for every server, **not replicated** |

Almost every rule visible on the screen follows from this:

* the current archive is **always open** on the server where the work goes on: it cannot be
  closed or deleted — data is being moved into it ("The current archive ARC-2 cannot be
  closed: data is being moved into it");
* the archive record is **common to the whole cluster**, its files are not. An archive deleted
  here calmly lies at the neighbours', and it can be **downloaded back**;
* data can be moved only **into the current archive** and only while it is **open on this
  server**.

---

## 2. How to create an archive

**Settings → Organizations**, the row of the **currently open** organization, the button
**"Add an archive"** (the box icon). The button is only there for the current organization —
archives are read in its context — and it can be pressed **only on the conductor server**: the
registry of archives is common, and if every server created archives on its own, there would
be as many "current" ones as there are machines in the cluster. On the other servers the
tooltip says exactly that: "An archive can only be created on the organisation's conductor
server".

The form asks for three things:

* **Archive name** — for a human, anything;
* **Archive code** — Latin letters, digits, hyphen and underscore, up to 64 characters. The
  code names the archive directory and its `.zip` file, which is why non-Latin letters and
  spaces are deliberately not allowed in it;
* **Archiving rules of the new archive** — where to take them from: "copy the organisation's
  common rules" (the default), "copy the rules of the previous current archive" (this option
  is there only if a previous archive existed) or "copy nothing".

While the form is open it **checks the previous current archive**: are its archiving rules
satisfied? If something the rules say it is time to move away is still left in the working
environment, the form says "Archive “…” has unsatisfied archiving rules: N record(s) left",
lists the first ten of them and offers the button **"Run archiving now"**. After that the check
runs again. This is not made a prohibition: the rules are a hint, not a barrier, and creating
a new archive is always allowed.

What happens on save:

1. the archive directory is created with an empty database of the same schema as the
   organization's one;
2. the record gets a number of the form **ARC-1**, **ARC-2**, …;
3. **the new archive becomes the current one**, and the previous current one stops being
   current — it stays an open archive that can now only be read;
4. the rules are copied into the new archive in the chosen way;
5. an order "change the current archive" spreads over the cluster. It is executed not at the
   moment the record arrives but at the very end of the replication session — first the
   **previous** current archive sends its data for the last time, and only then gives up its
   place.

---

## 3. Open, close, delete, download back

Archives are shown as **child rows** of the organization record: `└ ARC-2`, the name, the code,
the servers that have the archive, and the state. The buttons in the row appear by the state:

| Button | When it is shown | What it does |
|---|---|---|
| **Open the archive (unpack it on this server)** | the archive is not current and is closed | unfolds the `.zip` into the directory `arc/<code>/` |
| **Close the archive (pack it into a .zip)** | the archive is not current and is open | packs the directory into `arc/<code>.zip` and removes the directory |
| **Delete the archive from this server** | the archive is not current and is present here | removes the files **from this machine only**. The registry record stays |
| **Download the archive from another server** | the archive is deleted here | a window with the list of servers that have it |
| **Edit the archiving rules** | the archive is current | the rules of this very archive (see section 5) |

Deletion asks plainly: "Delete archive ARC-2 “…” from THIS server? The registry record stays:
the archive exists on other servers of the organisation and can be downloaded back".

**Downloading** goes in the background — an archive can be gigabytes big, and the form has to
answer at once. The window lists the servers that have the archive (except yourself); pressing
a server answers "Download of the archive from server … requested", and the transfer is then
carried out by replication. The archive always arrives **closed** (`.zip`), even if it is open
at the source: the state of an archive is own for every server. A broken transfer goes into
the journal and into the error line of the source server.

What travels between servers on its own:

* the **current** archive is replicated the same way as the working environment — and only it:
  it has its own database of the same schema, hence its own change journal, its own cursors
  and its own project files;
* the other archives are not replicated at all — they are carried by hand, with the "Download"
  button;
* whether an archive is open, closed or deleted here is the business of each server and is not
  replicated. Only "present / absent" travels, otherwise there would be nowhere to take the
  list of servers to download from.

---

## 4. Moving by hand: a task, a template, an object, an experience record, a project

There is no separate "to the archive" button: the move is offered **where the deletion is**.
Pressing the bin of a task, a template node, a project object, an experience record or a whole
project brings up the form **"What should be done with “…”?"** with three outcomes:

* **Move to the archive**,
* **Delete for good**,
* Cancel.

Before the button is pressed the form says three things.

**What goes along with the record.** A hierarchy is archived as a whole, hence "All its
subtasks go to the archive together with the task: …", "…all its child nodes", "…all its child
objects", and for a project — "Together with the project go its tasks and templates (N),
objects (N), experience records (N), as well as the logs and the settings of the project
itself". The composition is not invented by the form: it is counted by the transfer engine
itself, with the same checks as a real move.

**Why exactly it cannot be done**, if it cannot. The refusal comes as ready text and is shown
next to the disabled option:

* "Task T-15 is not a top-level one: a hierarchy is archived as a whole, not in parts" — only
  a root task is archived;
* "Task T-15 cannot be archived: its status is “in progress” — the work is not finished". Tasks
  go to the archive in the statuses **draft, review, done, cancelled**; any other status of the
  root or of **any** child at any depth cancels the move of the whole branch;
* "The template is used by future schedule SCH-3: switch it off or delete it first".

**That there is no archive yet**: "The organization has no current archive yet — there is
nowhere to move it. An archive is created in "Settings → Organizations → Add an archive"".
The rights are checked too: "Only an administrator of the organization may move data to the
archive".

The move always goes **into the current archive** and requires it to be **open on this
server**; otherwise — "The current archive … is not open on this server: data cannot be moved".
When it is over a summary arrives, of the form "Moved to archive ARC-2: 128 records, 40 files
moved, 3 copied".

**The links inside the moved texts** are rewritten once, at the moment of the move: the
parameter `arc=<archive code>` is added to the address of a task or a file. That is why a link
from an archived task leads into the archive and not into the working environment, where that
task no longer exists. On restoring, the parameter is removed by the same code.

---

## 5. The archiving rules

A rule answers one question: **what, and of what age, it is time to move to the archive**.
There are two groups of rules, and the set of fields is one and the same for both:

* the **common rules of the organization** — **Settings → Catalogs**, the section "Common
  archiving rules". This is the **sample**: the rules of every new archive are copied from it;
* the **rules of an archive** — own for every archive, opened by the "Edit the archiving rules"
  button in its row. These have the button **"Restore the default"**: delete all the rules of
  this archive and copy the common rules of the organization. They are set up **for the current
  archive only**: the data is moved into that one alone, so for the other archives the rules
  have already done their work and there is nothing to change — their rows have no rules button.

The fields of a rule record (the rules are numbered **ARR-1**, **ARR-2**, …):

| Field | Values |
|---|---|
| **Applies to** | Tasks, Templates, Objects, Experience, Security rules, Logs |
| **Task status** | asked **only** by rules about tasks |
| **Statuses of the children** | a list; nothing selected — any status fits |
| **Data activity** | any, active, inactive — for every kind except tasks |
| **Date** | the date of creation or the date of change |
| **Term** | by time (days) or by calendar (months and years) |
| **Active** | a switched-off rule does not take part in the selection |

About the dates it is worth remembering: a task created a year ago and edited yesterday goes
to the archive **by the date of creation**, but not **by the date of change**. In a calendar
term the months may be left unset, then only the years are taken into account.

How a rule selects tasks (the hint on the form says the same): "A task hierarchy is archived as
a whole: the root task is checked by status and age, its children only by status". That is, a
rule looks **only at root tasks**; every child at any depth is checked for its status being in
the "Statuses of the children" list, and **one unsuitable child cancels the archiving of the
whole branch**. An empty list means "any status", not "no status".

Two kinds — the **security rules** and the **logs** — can be selected by a rule, but the engine
does not move them one at a time: they have neither a hierarchy nor files. Automatic archiving
says so plainly — "Data kind «security» is not moved to the archive one record at a time" — and
counts such a record as skipped. They go to the archive as part of a **whole project**.

---

## 6. Automatic archiving on a schedule

Automatic archiving is "select by the rules of the current archive and move". It has neither a
selection nor a transfer of its own: it uses the same rules and the same engine as a manual
move.

It is set up with a **schedule**. The **"Schedule"** menu → **"Add a schedule"**, and then:

1. **"What is started"** — choose **"a system action"** instead of "a task from a template";
2. **"Action"** — **"Automatic archiving"**. The hint of the field: "The action runs on
   the conductor server of the organisation; no task is created";
3. then the usual fields of a schedule — once or periodically, the date, the time, the period.

No template is named at all, no task is created and no executor is picked: this is the work of
the system itself over its own database.

Archiving runs **only on the conductor server** of the organization: it has a single current
archive, and were it to run on every machine, the same branches would go to the archive from
several servers at once. A refusal comes not as an error but as an understandable line —
"Automatic archiving runs only on the conductor server of the organisation", "There is no
current archive: data is moved only into the current archive", "The current archive … is not
open on this server".

The result of a run is written into the organization journal as a summary: "Automatic archiving
into archive ARC-2: selected 30, moved 28, skipped 1, failed 1". Every failure has its own
reason and is named by name; the rest of the candidates go anyway.

The very same action is called by the **"Run archiving now"** button in the form of adding an
archive (section 2) — there it answers not with the journal but with the line "Moved to the
archive: N; failed: N".

---

## 7. How to view an archive: the "environment" field

In the top bar, between the **organization** and the **project**, there is the field
**"environment"**:

> Organization: **My company**  ·  environment: **working environment**  ·  Project: **…**

Next to it is the arrow **"Switch environment"**, and the menu holds "working environment" and
all the archives **open on this server** (the current one is marked with the word "(current)").
A closed archive is a `.zip`, there is nothing to read from it, and it is not in the list at
all. If not a single archive has been created, the "environment" field is absent entirely,
together with its caption.

Choosing an archive **closes all the tabs**: they show the data of the previous environment.
After that you work with the ordinary screens — the task tree, the board, the card, the
objects, the experience — only the data is taken from the archive.

An archive is open **for viewing only**. The interface does not show editing functions in an
archive at all, and if a change request still arrives (a plugin, an external client) the server
answers with a refusal: "The archive is open for viewing only: its data cannot be changed".
There are exactly two exceptions — the section that manages the archives themselves (the
registry, the rules, the move and the restore) and the screen settings of the person (the
chosen project, the layout of the tabs): both of these are written into the working
environment, not into the archive.

Going back to the working environment is the same switch, the item "working environment". If
the chosen archive is closed or deleted from this server, the program returns to the working
environment by itself.

---

## 8. Restoring from an archive

Restoring is the operation opposite to archiving: a record with all its children, files and
links returns to the working environment and leaves the archive.

It is possible **only from the current archive**: in the others the data is not changed any
more. That is why the **"Restore"** button (the opened-box icon) is on the card of a task or a
template node **only while the current archive is open**. It asks again: "Restore 'T-15 …' from
the archive back to the working environment? The whole task hierarchy moves along with its
files", — and when it is over answers "Restored from the archive". The card is closed at that:
this task is no longer in the archive.

The `arc=<code>` mark is removed from the links by the same code that put it there — the links
return to their working form.

There is **no separate "Restore" button today** for a project object, an experience record or a
whole project: the engine can do it and the call `POST /api/archives/restore` accepts them (the
kinds `object`, `experience`, `project`), but the button is not put on the screen.

---

## 9. Frequently asked questions

**Why does the "Add an archive" button not press?** You are not on the conductor server of the
organization, or you have no administrator rights in the organization.

**Why is my archive not in the "environment" menu?** The list holds only the archives **open on
this server**. A closed one has to be opened (unpacked) first in "Settings → Organizations", one
deleted here has to be downloaded from another server.

**Why does a task not go to the archive?** It is not a root one, or its status (or the status of
one of its children) means unfinished work. Only drafts and finished branches go to the
archive — "review", "done", "cancelled".

**Where did the current archive go after a new one was created?** It stayed an open archive in
its place: it stopped being the current one, it can be read, but not written into any more.

**Can the data in an archive be edited?** No. An archive is read-only; to correct something the
record has to be restored into the working environment (and that is possible only from the
current archive).

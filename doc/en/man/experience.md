# Experience

**Experience is the system's memory of how the work should be done.** A short lesson learned by
one task is pasted into the job text of other tasks — so the next performer does not step on the
same rake twice.

Both people and AI agents keep experience records. A person does it in the experience lists (the
**Experience** tab of a project card, the **Experience** tab of a template node,
**Settings → General experience**); an agent does it with the `create_experience` and
`update_experience` tools while it works.

---

## The three scopes

A record belongs to exactly one scope, and the scope decides who receives it.

| Scope | Who receives it | What it is about |
|---|---|---|
| **General rules of the organisation** | every task of the organisation, in every project | how we work: process, reporting, testing discipline |
| **Project experience** | tasks of this project | this product: how it is built, its rakes, its decisions, its names |
| **Template node experience** | tasks created from this node (and its descendants) | this step of the process |

The dividing rule in one sentence — the same text is shown in the record form and in the move
dialog:

> A general rule is about **how we work**: it is true in any project of the organisation and
> names no file, no task code and no product. **Project** experience is about this product.
> **Template node** experience is about this step of the process.

The rule is not decorative: general rules reach **every** task of the organisation, so a project
record that lands there by mistake steals room from everybody else. That is why writing into the
general experience is checked against four signs — a **task code** (`T-241`, `T-12-S0`), a **file
path**, a **source file extension**, a **project name**:

* an AI agent is **refused**, and is offered to file the record into the project experience
  instead. Silently changing the scope would mislead an agent that later looks the record up by
  its id;
* a person gets a **warning** in the form and a **“Save anyway”** button — you may know better
  than the check;
* records that arrived with a plugin skip the check entirely: a person already made that decision
  when installing the plugin.

The check runs when a general record is created, when it is edited, and when something is moved
**into** the general rules.

### Reviewing the general experience

Accumulated junk is not cleaned one record at a time. Above the list on the
**Settings → General experience** tab there is a **General experience review** block: it reports
“looks like project experience: N records”, names **which sign caught** each one, and moves the
ticked records in bulk into the project you choose. Records are moved one by one, so a refusal on
one of them (another server's record, a shipped rule) does not cancel the rest.

### Moving a record between scopes

The **Move to another scope** button in a list row opens a dialog: pick the scope, then the
project or the template node.

A move changes **the binding only**. The id, the text, the tags, the skill, the authorship, the
creation date and the “always load” / “active” marks stay as they were — the record is not
recreated, and references to its id do not break.

What cannot be moved:

* **another server's record** — junk from another server of the cluster is cleaned up there;
* **a shipped rule of the distribution** — every server seeds those itself, so a move would
  produce a second copy of the same line. An unwanted shipped rule is **switched off** by the
  activity mark, not moved and not deleted.

---

## What a record has

* **Text** — the lesson itself. One thought, imperative, no retelling of common knowledge.
* **Skill** — who the record is addressed to (`code-test`, `text-docs`, …). **A record with no
  skill and without the “always load” mark never reaches a job** — except template node
  experience, where a record without a skill is still common to every task of that node.
* **Tags** — the topic. Since 1.135 tags no longer **cut records off** from a job (see below);
  they push a record forward when the room is shared out.
* **Always load** — the record goes into the job regardless of the performer's skills. The mark
  is expensive: it taxes the prompt of **every** task, so keep it for rules that are true for any
  performer.
* **Active** — the record's on/off switch (see “Activity and archiving”).
* **Author and owning server** — only its own server may edit, move or switch a record.

---

## How a record reaches a job

When the system assembles the job text for a performer, it prints the experience blocks: the
general working rules first, then the project experience, then the template node experience. Not
everything gets in — the selection has three steps.

### Step 1. Who qualifies at all

1. the record is **active** — a switched-off record never reaches a job, not even with the
   “always load” mark;
2. the record is marked **always load** — it is taken with no further conditions;
3. otherwise the record needs a **skill**, and it must match the performer's skills.

**Tags no longer take part here.** A record with non-matching tags used to be thrown away before
any counting — and since the agent stamps new records with the task's tags, that filter would in
time start cutting off exactly what is needed, over a formal mismatch of one word. Now tags are a
**signal**: a match adds half a step of relevance, a mismatch removes nothing.

### Step 2. Experience of ancestor template nodes

A task created from a template node receives the experience **not only of its own node**, but of
every node up the chain to the root. Experience of neighbouring branches of the template still
does not concern it and is not taken.

### Step 3. The limit and the quotas

The volume of experience in one job is capped by a project setting (**100,000 characters** by
default): otherwise the experience block would push the task itself out of the model's window.
The room is shared out like this:

1. **always load** records are taken first — outside the contest and outside the quotas;
2. the remainder is split by **level quotas**: **50 %** to template node experience, **30 %** to
   project experience, **20 %** to the general rules. Inside a level records go by relevance: own
   node → parent → grandparent → project → general rules, plus half a step for matching tags;
3. **an unused quota flows over** to the neighbours: an empty level steals no room.

The quotas exist so that one wordy level cannot eat the whole budget: without them project
experience would squeeze out both the general rules and the most relevant node experience.

A record is inserted **whole**: half a lesson reads as a different lesson. The printing order is
chronological, as in the list.

**What this does not fix.** When the limit is full, an off-topic record still does not arrive —
it is no longer thrown out by a rule, it is simply overtaken by more relevant ones. Such a record
can be fetched by search (below).

If some records did not fit, the system says so in the job itself and names the search tool to
the performer — so what was dropped is not lost, it becomes available on request.

---

## The experience a task used

Which records the system **actually** pasted for the agent is visible on the task card, on the
**Used experience** tab. The tab appears only when the list is not empty (there is nothing to show
for a task that was never run).

On top there is a **total**: how many records went into the task text and how many characters that
is — as a number and as a share of the experience limit (a project setting). It shows how well the
limit is chosen.

The columns are the same as on the “Experience” tab (text, scope, skill, tags, activity, date of
last use), and on the right of every row there is an **“Edit the record”** button: what is stored
here is a link to the record, so the change goes into the record itself and there is no need to
look it up in the experience lists again. A record that was deleted or taken to an archive is shown
as “record … is not available” and has nothing to edit: the usage trail stores ids only and
outlives the record itself.

The other side of the same log is the **usage statistics** of a record: how many **tasks**
received it and when it was last used. It shows which experience works and which just lies there
taking up room in the prompt.

---

## Searching the experience

Above the filters of every experience list there is a **Search the text** box. Unlike the filters
by skill, tags and activity, search also sets the **order**: results come by rank, not by date.
The rank merges three signals — a lexical match, the freshness of the record and the number of
matching task tags; but **only a word match finds anything**, freshness and tags merely rearrange
what was found.

An AI agent uses the same search — the `search_experience` tool (scope
`project` / `template` / `general` / `all`, active records only by default) and the command

```
ai2p experience-find "query words" --scope project --limit 10
```

Search is exactly how the records that did not fit the job limit are fetched.

### What search does not find — honestly

1. **Meaning.** “how to build the package” will not find a record that says “MakePackage” and
   “Inno Setup” if the words “build” and “package” are not in it.
2. **Synonyms and abbreviations.** “spec” ≠ “specification”, “DB” ≠ “database”.
3. **Hard morphology.** Endings are stripped crudely; prefixes are not stripped at all
   (“reindex” ≠ “index”).
4. **Typos.**

### Duplicate protection

When an agent files a new record, the system compares it with the existing ones **of the same
scope**:

* on a **very strong** match the record is not created: the agent gets back the id and the text
  of the one found, with an offer to amend it;
* on a **noticeable** match the record is created but gets a `similar:<id>` tag — a marker for a
  human to review.

The same thought retold **in other words** is not caught by word comparison — that is the limit of
any lexical comparison.

---

## Activity and archiving

A record can be **switched off** without being deleted. A switched-off record stays in the lists,
you can see it and bring it back, but it does not go into jobs.

* The **Activity** filter in the experience lists: **Active / Inactive / All**, “Active” by
  default.
* The **Active** column and the toggle button in the row — the same right as edit and delete.
* The **Active** switch in the record form.

**Shipped rules of the distribution** can be switched off too — otherwise there would be no way to
remove them: seeding does not bring back what was deleted, while a switched-off rule comes back
with one click.

**An AI agent never deletes experience.** A review only **switches records off**
(`set_experience_active`), and archiving takes the switched-off ones away later — this is
deliberate: what was switched off can always be restored, what was deleted cannot. An agent may
not switch off the shipped rules of the distribution: those are the rules it works by itself.

### The “all inactive” archiving rule

In archiving rules, the **experience** data kind supports the “inactive only” selection: it covers
both switched-off and deleted records.

Together with it there is a third age kind — **age does not matter**. The old rule form demanded
an age strictly greater than zero, so the rule “archive **all** inactive records” could not be
written at all. Now it can: pick the data kind “experience”, the selection “inactive only” and the
age “age does not matter” — the form hides the number fields itself, and the rule list shows such
an age as “any”.

This rule is deliberately **not** put into the defaults of a new archive: the system must not take
somebody's experience to an archive unasked. File it yourself if you want a regular clean-up.

More about archives, rules and scheduled automatic archiving — the **[Archiving](Archives.md)**
chapter.

---

## Experience packs: working styles

**A pack is a ready bundle of experience records, installed and removed with one button.** That is
how a working discipline (“how we review”, “how we write tests”, “how we write reports”) comes out
of the box instead of being invented again in every organisation.

The **Settings → Experience packs** tab.

### Installing

1. Choose the **installation scope**: general rules of the organisation, project experience or a
   template node (for the latter two — the project or node itself as well). The scope is not
   stored in the pack file: it is your choice, and the interface does not let you skip it.
2. Press **Install**. The system files the pack's records and reports how many there are.
3. The **“i”** button in the pack row opens the **pack document** — what the style is, who it is
   for and what changes in the agents' work after installation. Documents of the shipped packs
   live in `doc/<language>/packs/`.

### Removing and reinstalling

The **Remove** button takes away exactly this pack's records — installation marks them with a
service tag `pack:<code>`. Your own records and your edits of the texts are not touched: a record
you edited stays yours, and reinstalling **will not overwrite** it.

### Exporting a pack of your own

The **Export records into a pack** block makes a pack out of **your** records: select them with
the filter and the checkboxes, give a code, a name and a description — and you get a pack file that
installs on another installation. Record ids are preserved, so installing it back gives **the same
rows**, not copies of them.

### What to know about the language

The texts of the shipped packs are written in all five interface languages, but **a record lives as
one string**: the server's language is taken at installation time. So in a job in another language
the block heading will be translated while the record texts stay in the language of installation.
That is how packs are built, not a translation bug.

### What the distribution ships

| Code | Name | About |
|---|---|---|
| `style.strict-review` | Strict code review | the job first, the diff second; every finding has a weight; a verdict is mandatory |
| `style.tdd` | Test-driven development | a red test first, then the smallest possible change |
| `style.research-report` | Research and report | discipline of sources, of numbers and of the “what was not done” section |
| `style.experience-analysis` | Experience analysis | not a style but a **task template** for reviewing the accumulated experience |

Pack files live in the `packs/` directory of the data directory, next to `plugins/` and `models/`;
the program puts the shipped packs there itself when it opens an organisation.

An overview of the packs and their documents — **[Experience packs: working
styles](../packs/README.md)**.

---

## The “Experience analysis” template

Experience piles up faster than it ages: records get duplicated, get stale, sit in the wrong scope.
Nobody will sort that out by hand — so the review is filed as **a task for an AI agent**, on a
schedule.

The `style.experience-analysis` pack brings **task template nodes** with it:

* **Experience analysis** — the root node with the full instruction: what to read (usage
  statistics → listing the records page by page → search when needed), what to do (generalise,
  split, tag, move, switch off by statistics), what **not** to do (do not delete, do not touch
  other servers' records, do not switch off shipped rules, do not rewrite the meaning while
  generalising) and what to put in the report — ids and the numbers “was / left active”;
* **Analysis of the organisation's general experience** — a child node for the general rules;
* **Analysis of the project experience** — a child node for a project; copy it once per project.

The rule for switching records off by statistics is written **in words** in the root node's job,
not hard-wired in code: a record is switched off if it reached no job for three months **while**
jobs on its topic did occur in that time. “It reached nothing because there were no such tasks” is
no reason to switch it off. Edit that wording in the template to fit your process.

### Putting the review on a schedule

1. **Settings → Experience packs → “Experience analysis” → Install**, scope — **project** or
   **template node**. The “general rules” scope has no project at all, and a task template cannot
   live without a project in AI2P — the nodes are then not created (the records install as usual).
2. **Project templates** — the root node “Experience analysis” has appeared with two children. Copy
   the “Analysis of the project experience” child once per project and point each copy at its own
   project.
3. **Settings → Schedules** — create a periodic schedule (weekly or monthly) and pick the
   **Experience analysis** node as its template.

Only the **root** node of a template fits a schedule: the children come along as a copy, they need
no schedule of their own. Installing a pack deliberately does **not** create a schedule — running
tasks spends money on the model, and a person should decide that.

More about periods, the schedule's server and overdue firings — the **[Schedule](schedule.md)**
chapter.

---

## What an AI agent can do with experience

| Tool | What it does | CLI command |
|---|---|---|
| `create_experience` | file a record | `ai2p experience` |
| `update_experience` | amend the text, skill, tags, “always load”, activity | `ai2p experience-update` |
| `search_experience` | find a record by words | `ai2p experience-find` |
| `list_experience` | list the records of a scope page by page | `ai2p experience-list` |
| `experience_usage` | usage statistics | `ai2p experience-usage` |
| `move_experience` | move a record to another scope | `ai2p experience-move` |
| `set_experience_active` | switch a record on or off | `ai2p experience-active` |

In `update_experience` **a field that was not passed is not changed**: editing the text does not
clear the marks a person has set.

**There is no deletion of experience for an agent, and there will not be.** All these actions are
listed in the action catalogue, which means the security rules of the task and the team cover them
— see the **[Configuration](config.md)** chapter, the “Actions” tab.

---

## Frequently asked

**The record exists but it is not in the job.** Check in order: is it **active**? does it have a
**skill**, and does the performer have that skill? if it has no skill — is the “always load” mark
set? if all of that holds, it probably did not fit the limit: look at the “Used experience” tab of
the last run.

**The agent refused to file a general rule.** The text contains a task code, a file path, a source
extension or a project name — that is **project** experience, not a general rule. Either file it
into the project experience or reword it without those signs.

**Project junk has piled up in the general experience.** Settings → General experience → the
“General experience review” block: tick the records and move them in bulk into the right project.

**A shipped rule is in the way.** Switch it off by the activity mark. Do not delete it — seeding
does not bring back what was deleted.

**There is far too much experience.** Put the review on a schedule (the “Experience analysis”
template) and file an archiving rule “experience + inactive only + age does not matter”.

---

## Neighbouring chapters

* **[Projects](progects.md)** — the “Experience” tab of a project card and the three levels of
  experience.
* **[Templates](templates.md)** — template node experience and the place where the knowledge about
  a process lives.
* **[Tasks](tasks.md)** — the task description as a prompt and the “Used experience” tab.
* **[Archiving](Archives.md)** — archiving rules and the automatic clean-up.
* **[Schedule](schedule.md)** — running the “Experience analysis” template periodically.
* **[Configuration](config.md)** — the “General experience”, “Experience packs” and “Actions” tabs.
* **[Experience packs: working styles](../packs/README.md)** — documents of the shipped packs.

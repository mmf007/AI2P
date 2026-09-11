# Teams

**A team is the circle of executors out of which the executor of a task is chosen.** Not a
department and not a chat: a list that answers the question "who may be given this work at all".

The list opens with the **"Teams"** button on the left bar (and the menu item of the same name).

---

## What it is for

Three things a team exists for:

1. **It narrows the choice.** A task has a team (filled in from the project), and its executor is
   chosen **only from that team's members** — both by hand and by the auto-pick. That way the work
   does not go to somebody who knows nothing about this project.
2. **It sets the agents' language.** A team has an **agent communication language**, and it may
   differ from the interface language. The field works literally: in the team's language the agent
   receives **all the text addressed to it** — the system prompt, the job, the tool descriptions,
   the answers and even the refusals of the security rules. What a person reads stays in the
   language of the installation.
3. **It holds the connections.** A team has "start" and "stop" buttons for its work: the start
   raises the AI members (checks the keys, starts the local model servers if needed), the stop
   shuts them down.

---

## The membership

Every membership row is an executor plus four things:

| Field | What for |
|---|---|
| **The professional role** | from the reference book of roles (art director, developer, tester…). This is the role **in this team**: the same person may be different in different teams |
| **The manager** | another member of the same team; empty means the top level |
| **The team lead** | who is in charge when the top level is not a single person |
| **Active in this team** | an activity of its own, separate from the activity of the executor in general |

### The hierarchy and the team lead

The hierarchy is not an ornament of the org chart. It answers the question **"who may be given
tasks without a separate permission"**: a superior gives them to their subordinates themselves.
That matters especially when one AI has other AIs under it. Cycles are forbidden — the core will
not let them through, and the form will highlight them.

There is one team lead per team, and only a top-level member may be one; if there is a single
executor at the top, they are marked as the team lead automatically. For a subordinate the flag is
disabled.

### Two activities, and that is not duplication

* **The executor's activity** (in the executor reference book) — "they work at all".
* **The membership activity** — "they work **in this** team".

A member counts as active only when both are set. A member switched off in a team is not connected
when it starts, is not raised by the per-member button and **is not chosen by the auto-pick**, but
stays active in the other teams. This is what you use to shut an executor down on one project
without touching the rest.

---

## Two list views

* **The table** — the teams as rows, the team lead is marked with an asterisk next to the nick.
* **The hierarchy** — a block per team, the members as a tree by subordination.

They are switched by the button above the list; the chosen view is remembered.

---

## Starting the team's work

The "start"/"stop" buttons exist **for the whole team** and **for every AI member separately**. A
person does not have them: they do not "connect", their state is computed by itself.

Every member shows a coloured status:

| Status | What it means |
|---|---|
| **not connected** | the team's work was not started, or the member was stopped |
| **connecting** | the check is going on; for a local model this takes **minutes** — the weights are being loaded from disk |
| **connected** | the key was accepted, the model answers |
| **connection errors** | it does not answer; the error text is in the tooltip |
| **inactive** | the member is switched off — in the reference book or in this team |

While somebody is connecting, the statuses refresh by themselves until the connection is over.

**The error text can be read in full and copied**: a status label with an error is clickable and
opens a window with the whole text and a "Copy to clipboard" button. That is not a trifle — the
error holds the exit code of the process, the last lines of the model's output and the start command
itself, that is, everything this error is taken apart with.

### When a per-member start is needed

Two cases, and both are frequent: a member was added to an **already working** team (there is no
point in restarting everybody) and a member **fell with an error** (that one alone has to be
raised). A per-member start does exactly the same as the common one, but for one executor, and does
not touch the "the team is working" flag.

### Local model servers

If a member's model has a start command set, on connecting the system **raises the model server as
an OS process itself** and waits until it answers (a probe every 5 seconds, the limit is 10
minutes). The rules worth knowing:

* **a server that already answers counts as an external one**: a second instance is not started,
  and on stopping it is not touched — you may have raised it by hand;
* **one start command means one process** for all the members and all the teams; it is shut down
  when everybody who used it has stopped their work;
* when the application is stopped, all the servers it raised are unloaded.

If the model server died at the start, the error carries the decoding of the exit code, the lifetime
of the process and the tail of its output; the typical causes (an old NVIDIA driver under a CUDA
build of torch, not enough video memory) are added in words.

---

## Small things that save time

* **A team name is unique** — saving with a taken name is blocked.
* **An inactive team** is not filled into new projects and tasks, and starting it is unavailable.
* A job started by the button **from a task card** is reflected in the team states too: there is no
  need to restart the team specially to get an up-to-date list.
* The membership is also visible and editable on the **"Team"** tab of the project card — right
  where you usually work (see [Projects](progects.md)).

## Next

* [Executors](performers.md) — who the membership is drawn from.
* [Projects](progects.md) — a team is assigned to a project, and from the project it reaches the
  tasks.
* [Tasks](tasks.md) — how a team narrows the choice of an executor.

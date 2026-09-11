# Several servers

**A server is an AI2P installation on a separate computer.** Several such installations are joined
into a **cluster**: they share the data of the organization, but each one works with its own hands.

The list opens in **Settings → Servers**. The tab is seen by the **admin** role (the owner of this
computer) and by a **server administrator** with a local sign-in.

> For a `single installation` this chapter is not needed at all: while there is one server, it is
> its own conductor, and nothing described below happens.

---

## 1. Why there are several of them

Four reasons, and all of them are about physics, not about scaling.

**There is one video card, and the work is different.** A local model is gigabytes of weights and a
process computing on a particular video card. It does not move. That is why a machine with a
powerful card becomes the server where media tasks and LoRA training are run, while light text
tasks go anywhere.

**People sit at different computers.** Everybody works on their own machine, with their own project
files and their own paths; and yet the tasks, the chat, the templates and the experience are shared.

**The work goes on while somebody else's computer is off.** The data is replicated rather than
lying on a single server: a switched-off neighbour stops nobody, it will catch up when it is
switched on.

**Files have to lie next to the one who makes them.** A job is run where the task's files lie, and
the result lands there too. There is no cross-server start in the system, on purpose.

What a cluster does **not** do: this is neither fault tolerance nor load balancing. A task is run at
its owner's, and nobody will pick it up.

---

## 2. What it consists of

### The conductor

In every organization there is **exactly one** conductor server. It hands out the codes to the
servers, keeps the organization's reference books, seeds the general rules of work and runs the
schedules with no server given. (A **task** always has a server — a task is never "nobody's".) The
topology is a **star**: all the servers replicate **only with the conductor**, they do not talk to
each other.

One and the same server may be the conductor of one organization and an ordinary member of another:
conductorship belongs to the **link** "organization ↔ server", not to the server itself.

### A server and an organization are "many to many"

One server serves several organizations, an organization lives on several servers. That is why the
records about servers lie in the **server** database, outside the organizations, and the link keeps
the **code of the server in the organization** — `S0`, `S1`, … The first one gets `S0`, the codes
for the rest are handed out by the conductor.

The code goes into the task numbers (`T-18-S1`) and therefore **is not reused** after a server is
taken out of the cluster: otherwise the numbers would become ambiguous.

### Servers recognise each other by a key, not by an address

Every server has an **internal key**, issued once on the first start and never changed. An address
is only a way to call, and it is never unique: behind NAT usually one server has a public address,
and the rest call themselves `localhost`. That is why **the system does not forbid two records with
the same address** — only the key matters.

A record may also have a **second, external address** — a public name or the address of a router
with a forwarded port. It is replicated together with the record (only the server itself knows its
own external address, while it is the neighbours who have to call it), and when calling, the
addresses are tried one after another.

---

## 3. Ownership: who edits what

The data is shared, but it is edited by **the owner of the row**.

* A **task** has an owner server: that is where it is edited, where its jobs are run, where its
  files lie. On the other servers it is visible read-only — but **"start", "start the hierarchy"
  and "write a new entry into the chat" are available**: the start goes to the owner as a request,
  the message gets there by replication.
* **The automatic starts** (of the children, of the unblocking, of the splitting) are led by the
  owner of the task — and only by it. Without that rule one firing would produce a job on every
  server. A row that arrived from a partner of an old version with no server is taken over by the
  conductor when it opens the organization.
* A **schedule** also has a server of its own, and it fires only on it.
* **The project directory and the "active" flag of a project are per-server**: the directories are
  different on different computers (see [Projects](progects.md)).
* **The executor list is edited only on the conductor** — executors are assigned to the tasks of
  all the servers, and the list has to be a single one.

The owner of a task can be changed by the **"change the server"** button on its card; the task
number **does not change** — it is the identifier and the name of the directory of its files.

---

## 4. How to connect a second computer

The connection is always **to the conductor**, and the decision is taken by a person on its side.

**On the new machine.** Install AI2P and on the first step of the wizard **clear the "Primary
cluster server" checkbox**. Then the organization is not created here (it will arrive by
replication with its own identifiers), and the last step of the wizard opens the **"Joining a
cluster"** screen: the conductor's address (`host:5480/ai2p` — the protocol and the prefix may
be omitted), the code of its organization (optional: if there is only one there, it is taken),
your signature and a note to the receiving side.

If the conductor has several organizations, it will answer with a list of them, and **several** may
be chosen at once.

**On a server that is already running** the same is done from the remote server form on the
"Servers" tab.

**On the conductor.** The request appears as a separate row at the bottom of the server list and
modally — to the owner or to the administrator working at that moment. Three decisions: **ACCEPT**,
**POSTPONE**, **DECLINE**. The form shows **the applicant's membership in the organization**: if
they are not an executor yet, the **"create an executor"** checkbox is on — it is better not to
clear it, otherwise the server will connect and the person at their end will run into "you are not
a member of any organization".

After the confirmation the connected server receives an access token and the organization key,
creates the organization at its place and fills it by replication. **The first session is started by
it** — the conductor does not call a server behind NAT.

What is worth knowing in advance:

* **an account with an empty password cannot connect over the network** — the password is set in
  advance;
* the request is **anonymous**: it asks for neither a mail nor a password on the conductor. The only
  exception is when a person with this mail **already exists** at the conductor: then the password
  **on the conductor** is asked for, and by it the same internal key is issued, so that the cluster
  does not end up with two accounts on one mail;
* **a duplicate mail or name** is named by the conductor **in the answer to the request**, not at
  the confirmation: it has to be fixed where the person stands at the screen — by the "Back" button
  to the "User" step;
* the **"show all requests"** checkbox above the list adds the history of both kinds of requests;
  without it only what still needs doing something about is visible.

### Taking a server out of the cluster

Nothing is moved automatically. The server is marked **inactive**, after which a **"Hand tasks over
to the conductor"** button appears next to it at the conductor's — otherwise the tasks of the
departed server would stay non-editable forever.

The record can be **deleted** for good only while there has not been a single successful replication
with the server (that is how an unsuccessful first attempt is removed); the issued code is freed
along with it.

### Changing the conductor

This is a **two-sided request**, not a button. It may be submitted by the server being appointed or
by today's conductor, and only under a **local sign-in of the server administrator**; a third server
does not interfere with somebody else's handover. A server with the address `localhost` cannot be a
conductor — the others would call themselves at such an address.

The request travels to the second side by ordinary replication, and the decision comes back the same
way. There is **one** simultaneous request per organization.

A **lost conductor** is provided for separately: a request submitted by the server being appointed
itself has a **12 hour** countdown, after which the "Accept unilaterally" button appears. Without it
an organization with a physically dead conductor would be left without a conductor forever. What is
accepted that way is marked separately — there was no consent from the second side.

The new conductor **announces itself to the others directly** (signed with the organization key),
because it may never have talked to them; the announcement is repeated once a minute while there are
still servers without a pair of tokens — a switched-off neighbour will find out when it is switched
on.

---

## 5. How they replicate

### The database — by a change log

Every edit of a replicated table puts a row into the change log: which table, the row key, the
operation, **the author's time** and **the author node**. After that replication comes down to "give
me the changes after my cursor".

The log is written by **SQLite triggers**, so the services know nothing about replication at all, and
a new entity is picked up by itself.

**A conflict is resolved by the "the last one wins" rule**: the incoming change is compared with our
last change of the same row by the pair (time, author); if it is not newer, it is not applied. That
also stops a change from going round in a circle. With separated ownership a real conflict is rare,
so it is shown on the diagnostics screen as a reason to look into it.

**The echo is suppressed by authorship**: its own changes are not given back to the author, while
the changes of a third server pass through the conductor.

### A session

A session goes **over one organization as a whole**: first one, then another. It is two-sided (take
what is theirs, give what is ours), and it is led by the side whose countdown expired earlier; the
second one gets "a session is already going" and skips its own start. The exchange goes in batches —
hence the progress bar and the percentage.

**The intervals are a setting of the pair, not our own.** There are two of them: the ordinary
interval and the retry interval after an error (a minimum of 30 and 15 seconds; empty means do not
replicate automatically, only by the button). They are set in the form of **the server the exchange
goes with**: there is no pair with oneself, so the form of one's own server does not have them. The
confirmation of a request sets the interval itself (300 seconds), otherwise a pair that has just been
connected would keep silent until a person opens the form.

### The files

The files travel together with the database: the organization data and the project's **`Common`**
folder (its path is set on each server separately, only relative to the project directory). Inside
`Common` the **`.repignore`** filter works — the syntax is that of `.gitignore`.

### A server behind NAT calls by itself

If the partner's record names a **loopback address** with our own port, calling there is impossible —
we would reach ourselves. The automatic pass **skips such pairs silently** (that is not an error), a
manual start answers with an explanation, and the sessions are started by that server itself. The
time of the session is then marked by the receiving side too: otherwise "The last attempt" would
stand as a dash although the exchange is going on.

---

## 6. What is visible on the screen and what to fix it with

### The server list

The local server is **always first**. The columns: the code in the organization, the name, the
address, the role (the "local" and "conductor" labels), the organizations, "active" and **the
replication status**.

In the status: **"manual"**, if no interval is set, otherwise how much is left until the next
session; during a session — the progress bar and the percentage; on an error — "error" and the time
until the retry, the details in the tooltip. The conductor itself does not have this field — it has
no pair of its own; on the conductor the statuses of all the servers are visible, on an ordinary one
only its own row.

Next to it there are the **manual start button** (a circular arrow) and the **diagnostics screen**
button.

### The replication diagnostics screen

A row per organization where this pair exists: the state of the session, the intervals, **the
cursors of both databases**, **the lag** (how many of our changes the partner has not taken yet),
received/given in the last session, the files and the bytes, the number of conflicts and the last of
them, the time of the last attempt and of the last successful session, the text of the last error and
**the contents of the retry queue** line by line (the area, the table, the key, the reason, the number
of attempts).

At the bottom there are two buttons:

* **"start the replication"** — a session right now;
* **"primary replication"** — forget the pair's cursors, the file comparison base **and the retry
  queue**: the next session will re-read and re-check everything from scratch. This is also **the
  manual lever for a stuck session** — the button releases an occupied pair.

The second button cures almost everything that looks like "the replicas have drifted apart":
re-reading the log from zero is safe, because what was postponed will arrive again, already together
with the parent rows.

### Conflicts

The conflict button appears at a server when there is something to decide. **The left version is the
conductor's, the right one is the ordinary server's** (a pair looks the same on whichever server it is
opened). There are three kinds:

* **a file** — the path, the directory, the size and the time of each version; accept the left / the
  right one or rename either of them (the decision is applied at the next replication);
* **the organization's API key** — the reference to the key, the time of the edit and the fingerprint
  (**the values are not shown**); accept the left / the right one or enter a new key;
* **a record of the AI model reference book** — the model name and the time of the edit; accept the
  left or the right one.

---

## 7. The limitations one has to know in advance

* **HTTPS is switched on, but not by itself.** While a server's protocol is `http`, the traffic
  between the servers is open and the cluster has to be deployed in a trusted network or over a VPN.
  Moving to `https` is separate work with a certificate on every machine, and a certificate from your
  own certification authority requires the "Trust the certificate of cluster peers" checkbox
  or handing the root certificate out to all the machines: see [HTTPS](https.md).
* **The manual start of a task is available only on its server** (from another one it goes as a
  request): a job is run where the files lie.
* **Model profiles are not replicated** — for a custom record of the reference book the settings
  column will be empty at the partner's.
* **Secrets are never replicated**: the organization key, the server administrator account, the
  server tokens and the `secrets/` subdirectory stay on their own computer.

## Next

* [Configuration](config.md) — the local server form, the addresses and the directories.
* [HTTPS](https.md) — the server certificate, the browser settings and a cluster over https.
* [Tasks](tasks.md) — the owner server of a task and working with somebody else's task.
* [Archiving](Archives.md) — archives are replicated too, but not all of them and not always.

# Quick start

The shortest path from the download to the first result produced by an AI agent. Nothing
extra: settings, templates, teams and the cluster are left for later — each of them has its
own chapter.

It takes 10–15 minutes, most of which is the download.

---

## Step 1. Download

[The distributions live here](https://github.com/mmf007/AI2P/releases)   

There are two files for every system, and choosing between them means answering one question,
"is the runtime needed separately?":

| File | What is inside | When to take it |
|---|---|---|
| `AI2P_v_1_NN_full_windows_x64.exe` | the program **together with the runtime** | the usual case: nothing has to be added |
| `AI2P_v_1_NN_windows_x64.exe` | the program only | if the **ASP.NET Core 8.x** runtime is already installed on the computer |

On Linux and macOS it is the same, only with the `.run` extension
(`AI2P_v_1_NN_full_linux_x64.run`). `NN` is the version number; take the largest one.

> In doubt — take **`full`**. It is bigger, but it needs nothing except itself.

## Step 2. Install

### Windows
Run the downloaded file. The installer asks whether to install **for all users** or **just for
me** — that answer decides both the program directory and where its working files end up;
the details are in the [Installation](install.md) chapter. For a trial run "just for me" is
fine: no administrator rights are needed.

### Linux

```sh
chmod +x AI2P_v_1_NN_full_linux_x64.run
./AI2P_v_1_NN_full_linux_x64.run
```

It installs into `~/ai/AI2P`. `root` rights are not needed: the server runs as an ordinary user.

### macOS

```sh
chmod +x AI2P_v_1_NN_full_macos_arm64.run
./AI2P_v_1_NN_full_macos_arm64.run
```

It installs into `~/ai/AI2P`. `root` rights are not needed: the server runs as an ordinary user.

## Step 3. Start it

The **AI2P** shortcut on the desktop (or `AI2P.Server.exe` / `./AI2P.Server` from the
installation directory). The program opens the browser at `http://localhost:5480/ai2p` itself.

It did not open — open that address by hand. The window with the error text closed too fast —
look into ["If the program does not start"](install.md#5-if-the-program-does-not-start).

## Step 4. Walk through the first-start wizard

On the very first run AI2P does not let you into the interface but leads you through steps.
There are six of them:

| Step | What it asks | What is worth knowing |
|---|---|---|
| **1. Language** | the interface language | the browser language is offered; the chosen one takes effect on all the following screens **at once** |
| **2. Server** | the server name, the protocol, the port and the **three hardware directories** | the server name is its name **on the network**; for a single installation `localhost` will do, it is enough to type it explicitly. The directories (model weights, distributions, packages) can be left as offered |
| **3. User** | name, mail, phone, password | the checkbox **"The password is the same as the server administrator password"** is ticked — leave it: then the server settings are available right away. The password may be left empty, but then signing in is possible only from this computer |
| **4. Organization** | the name and the code in the address | the code goes into the address (`/ai2p/<code>/…`). Leave the **"Primary cluster server"** checkbox on: unticked it means "connect to somebody else's server" |
| **5. Typical use** | code and analytics / video / images / no typical use | the system assembles a workplace from your answer |
| **6. The first project** | the name and the **folder** | the folder is the one in which the agent will read and write files. Without it the agent is not given the file tools |

After the last step you already have: an organization, the server `S0` (which is also the
conductor), yourself as the owner, **three AI executors `Jon`, `Bob` and `Stiv`**, the team
`<organization>_team` and a project with the folder you named.

> On a clean installation the "code and analytics" answer gives three executors on models
> **by Claude subscription**. For video and images there are no active models on a clean
> installation — the wizard says so honestly, and creates the team and the project anyway.

## Step 5. Give the system a model

The executors are there, but they can work only when their model has something to pay with.
Open **Settings** (the gear at the bottom of the left bar) → the **"Models"** tab.

Choose **one** of the paths:

**a) A Claude subscription (the fastest, no key needed).** The **"Claude CLI sign-in"** button
above the list. That is how the records with the `_cli` suffix work — and those are exactly the
ones the wizard filled in.

**b) An API key.** Find the model you need, press the key button in its row, paste the
provider's key. The record becomes active by itself: **a cloud model cannot be active without
a key**.

**c) A local model.** The "Install" button in the model's row downloads the weights and installs
the packages it needs. That takes long (gigabytes) and requires a video card — not the fastest
path for a first run.

What a particular record does, how much it costs and what it needs — the **"i"** button in its
row (and the [list of models](../models/README.md)).

## Step 6. Create a task

Open **Tasks** (the left bar) → the **"Add"** button → **"Blank task"**.

Fill in:

* **Title** — briefly, what the work is about.
* **Description** — as you would for a person: what to do, where, how to check it. This is the
  prompt; everything the agent needs to know has to be here.
* **Acceptance criteria** — how to tell that it is done.
* **Skills** — what actually has to be done (`code-write-cs`, `text-write`, for example). The
  automatic pick works by them.
* **Executor** — either choose from the list (`Jon`, `Bob`, `Stiv`), or press the auto-pick
  button next to the field: **"AI first, then human"**.

The project and the team are filled in by themselves — the ones open right now.

## Step 7. Start it and get the result

On the task card there is the **"Start"** button. After that:

* the status changes to **"in progress"**, and next to it you can see who exactly is working;
* the course of the work is visible in **"Work history"**: tool calls, reading and writing of
  files, the token spend;
* if the agent lacks something, it **asks in the task chat** — the task goes into "paused", and
  the question appears in the Inbox. Answer in the chat, and the work continues from the same
  place;
* having finished, the agent puts the result down as the **task artifact** (the report text,
  files, images), and the task moves to **"review"** — accepting the work is up to you.

Everything the agent did to the files lies in the project folder — right where you expect them.

---

## If something went wrong

| What you see | Why | What to do |
|---|---|---|
| The "Start" button does nothing | the task has no executor | assign one or press the auto-pick |
| The task went into "error" straight away | the model has no key, or the sign-in was not done | Settings → Models: the key or "Claude CLI sign-in" |
| The task is "paused" | the agent asked a question, or the executor has used up its limit | look at the task chat and the Inbox |
| The agent does not see the project files | the project has no folder set | the project card → the folder field |
| The agent refused an action | a security rule fired | Settings → Security |

## Next

* [Projects](progects.md) — the project folder, objects, experience, "cost ↔ quality".
* [Tasks](tasks.md) — the board, the hierarchy, subtasks, blocking tasks, the chat and artifacts.
* [Executors](performers.md) — AI and people, skills, limits, cost.
* [Teams](teams.md) — who works with whom and which language the agent speaks.
* [Templates](templates.md) — how not to type the same process twice.
* [Configuration](config.md) — everything else.

# Configuration

The settings in AI2P live in two places, and that is a rule, not carelessness.

* **The "Settings" screen** — what belongs to the **organization** and to **this computer**: the
  language, the reference books, the AI models, the security rules, the users, the servers, the
  notifications.
* **The `config.json` file** — what the **start** of the program itself depends on: the port, the
  address, the directories, the mail. It is edited outside the system, when there is no way into
  the system: the port is taken, for example, and the server does not come up.

Everything edited on the screen goes into that same `config.json` — but the reverse is not true:
some of the parameters are not shown in the UI on purpose (see
[§ 3](#3-what-is-edited-only-in-configjson)).

---

## 1. How to open it

The gear icon at the very bottom of the left bar (the vertical action bar), under the documentation
button. The same "Settings" is in the **AI2P → Views** menu and as the last line of the explorer.
The screen has no address of its own — it does not open by a link.

The settings open as a **working-area tab**, like a task or a project: they can be left open next to
the work and switched to and fro.

Who sees what: the organization sections (the reference books, the actions, the security, the users)
are edited by the **owner** or by an **admin**, and the form of the server itself — by a separate
**local server administrator sign-in** (the user button in the top right corner → "Local server
administrator sign-in"). It is local for real: it is accepted only from this computer.

---

## 2. The tabs

### Main

The settings of this installation and of this organization:

| What | The peculiarities |
|---|---|
| **The interface language** | changes both the interface and the **language of the system messages**; it does not affect the language in which the agent gets its job — that is set for the team (see [Teams](teams.md)) |
| **UI/API port** | **applied after restart** — the process is already listening on the previous port |
| **Open browser on start** | does not work for an OS service: a service has no desktop |
| **The second line of the tabs** | what to write under the word "Task"/"Project" — the start of the name or the short code (`T-17`, `PRJ-2`) |
| **The log output level** | Debug / Info / Warning; it takes effect **at once** on both the technical log and the work journal |
| **Schedule calendar cell** | the time + the template code or the time + the start of the title |
| **Models repository**, **Package distributives folder**, **Packages install folder** | the directories of **this computer**; edited by the server administrator |
| **The LoRA dataset frame** | to which limits a picture is squeezed and what the margins are filled with |
| **The application version**, **The start** | read-only: the build number and whether the server was raised by the console or by an OS service |

The three hardware directories are worth understanding right away, otherwise they look mysterious.
Their defaults are **relative** (`./models`, `./distribs`, `./packages`) and are counted **one step
above the program directory**: AI2P in `C:\ai\AI2P` → the model weights in `C:\ai\models`, the
packages in `C:\ai\packages`. That was done on purpose: the weights of local models are tens of
gigabytes, and they must not be put inside the program directory, which is wiped on a reinstall.
The form shows which path actually came out ("Now: …") — a relative value on its own says nothing.
An empty value for the distributions means "a temporary directory", for the packages — "`<model
repository>/packages`".

### Models

The reference book of **AI models** — what AI executors are made of in the first place. One record =
one model at one provider: the name, the provider, the API address, the reference to the key, the
price, the skills, the parameter profile and the LoRA training settings.

The peculiarities worth knowing before something fails to work:

* **A cloud model cannot be active without an API key, and a local one cannot without downloaded
  files.** That rule removes a whole class of puzzling refusals "the model is there but the job does
  not go". The key is entered by the key button in the model's row, the files by the "Install"
  button.
* The **"i"** button in the row opens the model's document: what it can do, what is needed to
  connect it, how much it costs. The full list is in [AI models](../models/README.md).
* **The Claude CLI sign-in** is a separate button above the list. The "by subscription" models
  (`*_cli`) need no key, but they do need a completed sign-in; when it expires, the jobs do not fall
  with an error but go into the "waiting for the sign-in" pause — and that is cured here.
* A local model is **switched on at a particular server**: it physically lies on one computer. The
  executor of such a model is attached to the same server.

### Catalogs

The small lists everything else is assembled from: the **skills**, the **task states**, the **roles
in teams**, the **input/output formats**, the **kinds of import sources**, the **default archiving
rules**. The records fall into **built-in** ones (they arrive with the distribution and cannot be
deleted) and **user** ones.

The skills are the most important thing here: the automatic executor pick works by them. When
creating a skill of your own, check that somebody has declared it in their capability declaration,
otherwise a task with this skill will never find an executor.

### Actions

The reference book of what the AI agent is allowed to do with the system's hands: reading and
writing project files, reading the neighbouring jobs, creating subtasks, asking a person, writing
experience, editing templates, moving a task and so on.

The main thing about this tab: **the prompts live here** — the very texts by which a tool is
described to the agent. They are not in the code. For a built-in action only the prompt is editable,
and an edit is rolled back to the factory text by a button; a user action is editable as a whole.
The texts are kept in several languages and are inserted according to the team's language.

The action codes (`AI2P.Files.Write`, `AI2P.Tasks.Create`, …) are hierarchical, separated by dots.
They are exactly what the security rules operate on, so it is worth looking in here before writing a
rule.

### Security

The rules of "what the agent may do": **allow / ask / forbid** for an action (or for a whole branch
of actions) in a scope — the whole organization, a project, a task. The "ask" rule means that before
the call the system will ask the person responsible for the task for a confirmation.

One subtlety that saves hours of investigation: a rule closes an action only if the tool **has a
record in the action reference book**. A rule does not touch a tool that is not in the reference book
at all.

Besides actions, a rule has the kind **«plugins and MCP»**: it answers not «what to do» but
«which plugins may be used» — the pattern is `trainer.musubi` or `trainer.musubi:lora.train`,
and the operations are «use» (the agent calls a tool of the plugin) and «run» (a
software-executor task starts the program). **Everything is allowed by default**, a ban is
created explicitly. The details are in [Plugins and MCP](plugins.md).

### Users

The sign-in accounts and their roles: `owner` (the owner of the whole organization), `admin` (the
master of one computer of the cluster), `project_admin`, `editor`, `reader`. An account is a
sign-in; a person **works** not as an account but as an executor that refers to this account (see
[Executors](performers.md)).

### Organizations

The list of this server's organizations and switching between them. An organization is a separate
database, a separate directory, separate reference books and its own encryption key. The **archives**
live here too: they are shown as child rows of the organization record (see
[Archiving](Archives.md)).

### Servers

Your own server, the cluster neighbours and the connection requests. **The local server form** is
exactly the settings of the server itself: the address, the port, the second (external) address, the
listening interface, the directories of this computer, the server administrator password. Only the
server administrator edits them, the rest see them read-only; **the address and the port apply after
a restart**. If the `https` protocol is chosen, the **"The HTTPS certificate"** section appears below
(see [HTTPS](https.md)). In detail — [Several servers](servers.md).

### Notifications

The rules of "what to write to a person about": a task moved into a state, a task requires a review,
the due date is approaching. Above the list there are the **mail server** settings (this is a setting
of the computer, not of the organization) and the **test letter** button: without it "I have set it
up and I am waiting" turns into "I am waiting and I do not know whether it works".

An empty list of executors or of projects in a rule means **"all"**, not "none".

### General experience

The experience records that act on **all** the projects of the organization — the general rules of
work that are put into every job of an agent. The experience of a particular project lives in the
project card, the experience of a template node lives in the template (see [Projects](progects.md),
[Templates](templates.md)).

---

## 3. What is edited only in `config.json`

The file lies in the same place as the working files of the installation (`data/`, `logs/`,
`secrets/`) — where exactly is said in [Installation](install.md). The program has to be **stopped**
before the edit: it writes into the same file.

```json
{
  "ui": {
    "protocol": "http",
    "port": 5480,
    "basePath": "/ai2p",
    "hostname": "localhost",
    "hostname2": "",
    "port2": null,
    "bindAddress": "0.0.0.0",
    "https": {
      "source": "file",
      "certFile": "",
      "keyFile": "",
      "passwordRef": "https.certPassword",
      "storeLocation": "CurrentUser",
      "storeName": "My",
      "subject": "",
      "thumbprint": "",
      "trustAnyPeer": true
    },
    "openBrowserOnStart": true
  },
  "storage": {
    "dataDir": "./data",
    "dbFile": "ai2p.db",
    "distDir": "./distribs",
    "packagesDir": "./packages",
    "docDir": "",
    "modelsRepo": "./models",
    "serverDbFile": "server.db"
  },
  "logging": { "level": "Warning", "dir": "./logs", "rotation": "day" },
  "language": "en",
  "serviceMode": false
}
```

This is the file **as it comes with the distribution**. Sections are added to it over time: the mail
(`mail`) appears when it is set up on the "Notifications" tab — it does not have to be created by
hand.

What matters here and is not on the screen:

* **`basePath`** — the prefix of the address after the port (`/ai2p`). All the links to tasks depend
  on it; it is changed when AI2P is put behind a common reverse proxy.
* **`bindAddress`** — which network interface to listen on. `0.0.0.0` means all of them (that is how
  a cluster works), `127.0.0.1` means this computer only. For a single installation the second is
  safer.
* **`hostname2` / `port2`** — the **second, external** address of the server: a public name or the
  address of a router with a forwarded port. Empty means there is no second address.
* **`https`** — the server certificate: together with `protocol: "https"` this is the move to HTTPS.
  It is edited on the screen too — in the local server form the section appears as soon as the
  `https` protocol is chosen. In detail (where to get the certificate, how to allow it in the
  browser, what a cluster is to do) — [HTTPS](https.md).
* **`dataDir`, `logging.dir`** — a relative path is counted **from `config.json`**, and `~/…` from
  the home directory. The path is written down as it was typed: the file stays portable.
* **`docDir`** — the documentation directory; empty (the usual case) means the program finds it
  itself. If a non-existent one is given, the documents are not shown, and the UI says so in plain
  words.
* **`serviceMode`** — the mark "this installation runs as an OS service"; it is set by
  `makeAsServise` and read by the installer, so as to stop the service for the duration of the
  update.

Three rules that are useful to know in advance:

1. **There are never any secrets in `config.json`.** The API keys lie encrypted in the organization
   database, the organization key and the server administrator account lie in `secrets.json`, and the
   mail password lies in `secrets/mail.password.json`. Only a **reference** stays in the
   configuration (`mail.passwordRef`). The `secrets/` subdirectory goes neither into replication nor
   into a release nor into an update; inside it there is a `readme.txt` with an explanation in the
   language of the installation.
2. **The directories are brought to the platform on start.** A configuration brought from Windows to
   Linux will not leave a `C:\ai` path in the system: an alien path is replaced by the default, and
   the correction is explained by a line in the console. A path like `~/ai` is never considered an
   alien one.
3. **Updating the version does not lose your values.** A `config.new.json` with the new defaults is
   put next to it, on the first start the files are merged (the new base + your values), and the
   applied file stays as a `config.new.json.applied` copy.

---

## Next

* [Installing AI2P and where its data lives](install.md) — where all of this is put.
* [Quick start](quickstart.md) — if the configuration is needed for the sake of the first run.
* [Several servers](servers.md) — the local server form and the cluster.
* [HTTPS](https.md) — the server certificate and the browser settings.

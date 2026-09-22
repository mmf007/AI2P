# The AI2P user guide — section contents

The `man/` section is the **user guide**: how to do in AI2P the things it is installed for.
Unlike the `models/` and `import/` sections, the documents here are not tied to reference
records — this is plain text split into chapters, and the file name can be anything.

The guide is read from the documentation window (the documentation icon on the left bar and at
the bottom of the navigator) or straight from the `doc/` directory next to the installed
application.

## Section contents

* [aboutdoc](aboutdoc.md) — How the AI2P documentation is arranged
* [Archives](Archives.md) — Archiving
* [audio](audio.md) — Working with audio models
* [build](build.md) — AI2P — building, releasing and the layout of the repository
* [config](config.md) — Configuration
* [experience](experience.md) — Experience
* [https](https.md) — HTTPS
* [install](install.md) — Installing AI2P and where its data lives
* [LoRAEditor](LoRAEditor.md) — The LoRA editor
* [objects](objects.md) — Project objects
* [performers](performers.md) — Executors
* [plugins](plugins.md) — Plugins and MCP
* [progects](progects.md) — Projects
* [quickstart](quickstart.md) — Quick start
* [sample_video1](sample_video1.md) — A video clip made from a character's reference frame. An example
* [schedule](schedule.md) — Schedule
* [servers](servers.md) — Several servers
* [service](service.md) — Running AI2P as an operating system service
* [tasks](tasks.md) — Tasks
* [TaskDo](TaskDo.md) — How tasks are executed
* [teams](teams.md) — Teams
* [templates](templates.md) — Templates

## What is inside

The section is read in two orders. **A newcomer** reads it in order: the quick start, the projects,
the tasks. **On business** — from the chapter you need; each of them is self-sufficient and names
its neighbours at the end.

### Where to start

**[Quick start](quickstart.md)**

* The minimal path from downloading the distribution to the first result of an AI agent: which of
  the two packages to take, the six steps of the first-start wizard, how to give the system a model
  (a subscription, an API key or local weights), how to create the first task and what happens after
  "start".

### The administrator's guide

**[Installing AI2P and where its data lives](install.md)**

* The three ways to install, what the "for all users" answer changes, where the working files
  (`config.json`, `data/`, `logs/`, `secrets/`) live and why an all-users installation keeps them in
  `C:\ProgramData\AI2P`, how to set that directory yourself (`AI2P_HOME`, `--config`), what an update
  does to the configuration and what to do with data left next to the program.

**[Running AI2P as an operating system service](service.md)**

* How to turn an installation into an OS service (`makeAsServise` on Windows, Linux and macOS; the
  service name is `AI2P`), how a service run differs from a console one, how to manage the service,
  which account it should run under on Windows (DPAPI and the organization keys) and what happens
  when the version is updated.

**[Configuration](config.md)**

* The two places where the settings live and why there are two of them; how to open the settings
  screen and what is on each of its tabs (main, models, catalogs, actions, security, users,
  organizations, servers, notifications, general experience); what is edited only in `config.json` —
  the address prefix, the listening interface, the second address, HTTPS — and the three rules about
  the secrets, the platform directories and the version update.

**[HTTPS](https.md)**

* How to move the server to https: where to get a certificate (a `.pfx` file, a PEM pair or the
  computer's certificate store), how to make one yourself in PowerShell and openssl, what the program
  itself checks before saving and at the start, how to **allow your certificate once** in the browser
  (Windows, macOS, Linux, Firefox, phones), how a cluster is to live with a certificate of its own
  CA, and what to do when the server did not come up.

**[Executors](performers.md)**

* A person and an AI as one notion; how an account differs from an executor; the profile and the
  capability declaration; the activity, the busyness, the window limit and the answer timeout; how
  the auto-pick and the substitute executors work; why a local model ties an executor to a server.

**[Teams](teams.md)**

* The circle of a task's executors, the agents' communication language, the hierarchy and the team
  lead, the two membership activities, starting the team's work and starting a member one by one, the
  local model servers and how to read a connection error.

**[Schedule](schedule.md)**

* A copy of a template or a system action, the schedule fields and the periods, why a schedule has a
  server of its own, the overlap check, the two views (a table and a calendar) and, most importantly,
  the list of overdue firings.

**[Several servers](servers.md)**

* What a cluster is for and what it does not do; the conductor and the server codes; the ownership of
  rows; how to connect a second computer, take it out of the cluster and change the conductor; how
  the replication of the database and of the files is arranged; the diagnostics screen, the retry
  queue and the conflicts.

**[Archiving](Archives.md)**

* Moving data from the working environment into an archive one: what it is for, the four states of an
  archive (current, open, closed, deleted), how to create an archive and how to open, close, delete
  it and download it back from another server, moving a task, a template, an object, an experience
  record and a whole project by hand, the archiving rules (the common ones and those of an archive),
  automatic archiving as a schedule action, viewing an archive through the "environment" field in the
  top bar, and restoring data from the current archive.

**[Plugins and MCP](plugins.md)**

* How external programs and MCP servers are connected: what a plugin record is made of (actions,
  experience, software, document), how a gateway differs from an MCP connection, the five plugin
  states and why each server has its own, the order of initialisation, what "software not found on
  this server" means, and why an MCP server set up outside AI2P bypasses every security rule.

### The user's guide

**[Projects](progects.md)**

* The project folder and why it is per-server, the `Common` folder and `.repignore`, the card tabs
  (main, tasks, team, objects, security, templates, experience, history), the "cost ↔ quality" and
  "time ↔ quality" settings, the project objects and the `@obj:` reference, the three levels of
  experience.

**[Project objects](objects.md)**

* What an object is for and why the `@obj:` reference beats retelling; the kinds of objects; the
  list and its four views; the object tab, its toolbar and the "Sub-objects" tab; the object form,
  the two references to it and the "what goes into the model" dialog with two fields for a LoRA
  adapter; the owning server of an object, the read-only mode on another server and how a trained
  adapter travels to the neighbour.

**[Templates](templates.md)**

* A blank of a working process and the place where the experience lives; how a template differs from
  a task; the "Experience" and "Statistics" tabs, the selection of experience by skills and tags; the
  three ways of deploying a template and what happens to the blocking tasks when it is copied.

**[Tasks](tasks.md)**

* The task description as a prompt; the four list views, the filter and the sorting; the three
  sections of the form; the card, the job console and the pause reasons; the chat, the agent's
  questions and interrupting it in the middle of the work; the manual, the automatic and the
  hierarchical start; the subtasks and the auto-split; working with a task of another server.

**[How tasks are executed](TaskDo.md)**

* The order of a whole-hierarchy run: a queue pass from the bottom up by priority, when a
  task waits, is skipped or is started, when the queue closes; how "Condition", "Loop (check
  first)" and "Loop (check after)" fit into that order, the round limit and stopping.

**[Experience](experience.md)**

* The system's memory of how the work should be done: the three scopes (general rules of the
  organisation, project experience, template node experience) and the rule that divides them; what
  a record has and how it reaches a job — activity, skill, “always load”, tags as a signal, level
  quotas and the paste limit; the “Used experience” tab and the usage statistics; searching the
  experience and what search cannot find; moving a record between scopes and reviewing the general
  experience; switching a record off and the “all inactive” archiving rule; experience packs
  (working styles) and the “Experience analysis” template on a schedule.

**[The LoRA editor](LoRAEditor.md)**

* How to use the LoRA editor: what has to exist before the training, what to write in the adapter
  description and in the frame captions, which of these texts goes into the training and which into
  the generation prompt, how to start and stop the training, whether the order of the dataset frames
  matters, frequent errors and what the editor does not do. The same page opens from the editor
  itself, by the book button.

**[Working with audio models](audio.md)**

* How to build a sound scene out of music, a song, the speech of particular people and noises:
  which audio models the catalog has and what each can do, why one task is one sound layer,
  which voice samples speech synthesis needs and how to pass them, why a singer's voice is set
  in words, where to get a sound effect, how to join and mix the layers, frequent errors.

### Examples and the service part

**[A video clip made from a character's reference frame](sample_video1.md)**

* An end-to-end example: how to turn a picture in the project folder into a video and how to hold the
  same character across a series of frames — installing a local media model, the AI executor and
  starting the team's work, the character object and its passport, the `@obj:` reference in a frame
  description, the generation parameters, the frequent errors.

### The developer's guide

**[building, releasing and the layout of the repository](build.md)**

* How to build the system from the source code.

**[How the AI2P documentation is arranged](aboutdoc.md)**

* About the documentation itself: where the `doc/` directory lives and what of it is shipped, which
  sections it consists of, how the document files are named, how to add a new document and how to
  keep the languages in step.

<img src="../images/ai2p-logo.png" alt="AI2P" width="256">

# AI2P — AI to People

### A self-hosted task planner where the executor can be an AI agent.<br>Your Jira, where some of the cards get done by themselves.

<p align="center"><img src="../images/ai2p-demo.gif" alt="AI2P demo: create a task, assign an AI agent, press Start, get the result" width="960"></p>

* **Self-hosted — your data stays with you.** The database, the project files and the keys lie next to the program. Windows, Linux, macOS.
* **Any model, including local ones.** Claude, GPT, Gemini, DeepSeek, Qwen over an API, Claude Code by subscription, local models on your own video card — and a cluster of several computers.
* **People and AI in one queue.** Not "an AI agent instead of the team": the AI takes what it can do, the rest waits for people, as in any planner.

**[Quick start](man/quickstart.md)** · **[Website](https://ai2p.org/ai2p)** · **[Download](https://github.com/mmf007/AI2P/releases)** · Without an API key AI2P works as an ordinary planner for people.

<sub>Full description below ↓</sub>

<br>
<br>
<br>
<br>
<br>
<br>
<br>
<br>
<br>
<br>
<br>
<br>
<br>
<br>

---

AI2P is what Trello or Jira usually are: projects, tasks, executors, a board, a history. There is one
difference, and it changes everything: **the executor of a task may be an AI**. Whatever the AI can
do itself it does itself, at the right moment and without a reminder; the rest is left to people and
waits for them, as in any planner.

The system works **on your computer** (Windows 10–11, Linux, macOS), the interface is in the browser.
The data goes nowhere: the database, the project files and the keys lie next to the program.

## Contents

* [What it does](#what-it-does)
* [What it looks like in work](#what-it-looks-like-in-work)
* [AI2P builds itself](#ai2p-builds-itself)
* [What is needed](#what-is-needed)
* [Quick start](#quick-start)
* [Documentation](#documentation)
* [For partners and investors](#for-partners-and-investors)
* [License](#license)

## What it does

* **It leads the work, not just a to-do list.** A task has an executor, a due date, a priority,
  acceptance criteria, blocking tasks and subtasks. Finishing a task starts the next ones by itself —
  the ones that were waiting for it.
* **It connects AI of any kind.** Text models over an API (Claude, GPT, Gemini, DeepSeek, Qwen,
  GigaChat, YandexGPT and others), Claude Code by subscription, local models on your video card, and
  also pictures, video, sound and 3D. All of them are ordinary executors: each has a nick, skills, a
  price and limits.
* **It picks an executor itself.** By the skills of the task and the "cost ↔ quality" setting of the
  project the system chooses whom to give it to — the AI first and then a person, or the other way
  round. An executor that is busy or has used up its limit is skipped, and the task waits for one to
  free up.
* **It splits big work into parts.** An agent may create subtasks and start them by priority; the
  last task of the group sums it up and, if something did not add up, returns its neighbours for
  fixing.
* **It remembers experience.** The lessons gained in the work are written down to the project, to a
  template node or to the whole organization and are put into the next job by themselves — their own
  for every skill.
* **It repeats typical processes.** A template is a tree of tasks with descriptions, skills and an
  order; a new working process is deployed out of it by a single action.
* **It works on a schedule** — from "every Monday" to the system's own actions, automatic archiving
  for example.
* **It lives on several computers.** The servers are joined into a cluster and replicate the data to
  each other; every task has an owner server on which it is carried out — that is how a local model
  computes where the video card stands.
* **It takes tasks from external systems** — Trello, GitHub, GitLab.
* **It keeps the AI within bounds.** The security rules decide what the agent is allowed to do, what
  needs a person's confirmation and what is forbidden; the agent does not go outside the project
  folder.
* **It speaks your language** — the interface, the messages and the agents' prompts are taken from
  dictionaries; the language is chosen in the `README.md` file next to the `doc/` directory

## What it looks like in work
<img src="../images/Demo_diagram_night.png" alt="An example of the screen" width="900">

1. You create a project and name its folder — that is where the agent will read and write files.
2. You write a task the same way you would write it for a person: the title, the description, the
   acceptance criteria.
3. You press "start". The task goes to an AI executor, its work is visible in the history, and it
   asks its questions in the task chat — and that is where you answer it.
4. The finished result lands as the task's artifact, and the task itself moves to review.

## AI2P builds itself

AI2P is developed in AI2P. More than 140 releases and hundreds of tasks: specifications,
code, tests, documentation and release builds are done by AI executors, while a human sets
the tasks and accepts the results. Every release is a task tree: an agent splits the work
into subtasks, hands them out to executors, and the final task sums up and sends back for
rework whatever did not add up. So this is not a demo on a shopping list, but a product that
proves itself on itself every day — honest agent mistakes and their fixes included.

## What is needed

* Windows 10/11, Linux or macOS; administrator rights only for the installation.
* Nothing else: take the **full package** — `AI2P_v_1_NN_full_windows_x64.exe` for Windows,
  `AI2P_v_1_NN_full_linux_x64.run` for Linux, `AI2P_v_1_NN_full_macos_arm64.run` for macOS.
  Everything needed to run is inside it, there is no need to install .NET separately.
  (The package without `full` in its name is smaller, but expects the ASP.NET Core 8.x runtime to be installed already.)
* An API key of the AI you are going to use — or a Claude Code subscription, or a video card for
  local models. Without a model the system works as an ordinary planner for people.

## Quick start
For the first run in a few minutes see [Quick start](man/quickstart.md)  
[The distributions live here](https://github.com/mmf007/AI2P/releases) — take the package with `full` in its name  

## Documentation
For the detailed documentation see [Documentation](index.md)

Sections: [User guide](man/README.md) · [AI models](models/README.md) ·
[Task import](import/README.md) · [Plugins and MCP](plugins/README.md) ·
[Experience packs](packs/README.md)

## For partners and investors

If you want to contribute to the development, to launch a commercial solution based on the project or
to discuss an investment, please have a look at our
[page for partners and investors](PARTNERS.md).

## License

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](../../LICENSE)

This project is distributed under the free **Apache License 2.0** — the full text of the terms is
available in the [LICENSE](../../LICENSE) file.

# Importing tasks from GitHub — connection guide

The import turns **GitHub issues** into AI2P tasks (spec § 2.10, ch. 11). To connect it you
need one string from GitHub — a **personal access token** — and one record in the import
directory. Step by step:

## 1. Get a token

GitHub issues tokens in the account settings. There are two kinds of token; both work.

**Fine-grained token (recommended — you can grant exactly as much as is needed):**

1. Sign in to GitHub and open
   <https://github.com/settings/personal-access-tokens/new>.
2. **Token name** — anything, for example `AI2P import`; **Expiration** — the lifetime.
3. **Repository access** — "All repositories", or "Only select repositories" and list the ones
   you are going to import from.
4. **Permissions → Repository permissions → Issues** — set **Read-only**.
   (GitHub turns read access to **Metadata** on by itself — it is mandatory.)
5. **Generate token** and copy the string shown. It starts with `github_pat_` and is shown
   **once**.

**Classic token:**

1. Open <https://github.com/settings/tokens> → **Generate new token (classic)**.
2. Tick the **`repo`** scope (it gives access to the issues of private repositories);
   for public repositories **`public_repo`** is enough.
3. **Generate token** and copy the string — it starts with `ghp_`.

The token gives access to your repositories — keep it like a password. **A token is needed
even for public repositories**: without one GitHub allows just 60 requests per hour per
address, and the import runs into that limit.

## 2. Create a source in the import directory

**Settings → the «Catalogs» tab → «Import sources» → «Add»:**

| Field | What to put there |
| --- | --- |
| Source name | anything, for example `GitHub work` |
| Kind | GitHub |
| Repository owner | the user **or organization** name — what stands in the address `github.com/<owner>` |
| Filter | a repository name substring, for example `planner`; empty — all repositories of the owner |
| Active | unavailable for now — it turns itself on after step 3 |

Press **"Save"**. The token button appears on a **saved** record: the value goes into the
store at once, so before saving it is not known whose value it is.

## 3. Enter the token

Open the saved source with the **"edit"** button and press **"Set token"** — paste the string
from step 1 and save.

GitHub has no API key: there is a single secret, so the form has a single button (Trello has
two). The old value is never shown — to replace the token you enter a new one. Below the form
you can see the state: "Token: set, source — organization".

The form checks the **shape** of the value and warns if it does not look like a GitHub token:
current ones start with `github_pat_` or `ghp_`, older ones are 40 characters of `0–9 a–f`.

**The "Check connection" button** asks GitHub "who am I" and answers in words right away:
whether the token was accepted, which account it was issued to, and whether that account
matches the owner named in the source. Checking the connection here is cheaper than catching
a refusal during the first import.

**Where the token is kept.** In your organization's database, **encrypted** with the
organization key (like the API keys of models): it travels to all servers of the organization
by itself, and only a server that has been given the organization key can decrypt it. There is
no need to write anything into `secrets.json`; if the value is nevertheless put there as a
file, it will be read too — the reading order is "organization → `secrets.json` →
environment variable".

**Until the token is entered the source cannot be active** — the "Active" switch is disabled.
As soon as the token is entered the source **turns itself on**; after that the activity is
yours to switch.

**Two GitHub accounts.** Every source has **its own** token reference — create a second source
and enter another value into it.

## 4. Run the import

1. Choose a **project** — the import always goes into the current project.
2. In the task list press the **cloud with an arrow** icon button ("Import tasks") — it is
   visible only when a project is selected.
3. In the dialog choose the source and the checkboxes:
   * **add new** (on by default) — issues that are not there yet become AI2P tasks;
   * **update existing** — previously imported tasks get their title, description and due date
     updated (local edits of those fields are overwritten).
4. Press **"Import"**. The outcome is shown in a message: "added X, updated Y, skipped Z,
   discussion messages N"; an `import.run` record appears in the work history.

## What exactly is imported

* **Open issues** of the owner's repositories are taken; repositories are selected by a name
  substring (an empty filter — all of them).
* **Pull requests are not imported.** In the GitHub API they look like issues, but they are not
  issues and are skipped.
* A GitHub issue → an AI2P task with the **"draft"** status: the title, **the whole issue text
  in the description** plus a source footer at the bottom ("Imported from GitHub: repository …,
  issue #N" with a link). Assignees and priority you fill in inside AI2P.
* **The issue discussion is moved into the AI2P task chat**: every comment becomes a message,
  the author is named the way GitHub names them — `github:<login>`, the message time is the
  comment time, so the chronology is preserved. A repeated import adds **only new** messages:
  the ones already moved are recognised by their external id.
* A GitHub issue has **no due date**. If the issue belongs to a **milestone** with a date, that
  date becomes the due date of the AI2P task.
* **No duplicates are created**: the internal issue id is remembered in the AI2P task
  (`github:<id>`); a repeated import skips or updates the same issue — depending on the
  checkbox.
* **Files attached to a GitHub issue are not downloaded.** GitHub has no separate list of
  attachments — images and files live as links inside the text itself, and links they stay.
  For a public repository such a link opens and the image is visible; a link to a file of a
  **private** repository will not open without signing in to GitHub.
* Labels, assignees and the state of the GitHub issue are **not carried over**: they are
  managed in AI2P.

## 5. Importing a single issue by link

Next to the bulk import button in the task list there is an **"Import a task by URL"** button:

1. Paste a link of the form `https://github.com/<owner>/<repository>/issues/<number>`
   (an address with a tail works too — `#issuecomment-…` and `?…` are dropped). The source is
   asked for only if there is more than one suitable active GitHub source: **the kind of source
   is determined by the link itself**, so a pasted GitHub address will not reach the Trello
   importer.
2. The issue contents are put **into the new task form**: title, description, due date.
3. Fill in the rest of the form (assignee, priority) and save — the discussion goes into the
   task chat right after saving. A repeated import of the same issue creates no duplicate.

AI agents have the same action as the `import_task_from_url` tool (action code
`AI2P.Tasks.ImportFromUrl` — deny/confirm security rules apply as usual). Which procedure
takes the link is decided by its shape, so one tool is enough for all sources.

## 6. Refreshing a task from GitHub

A task that arrived by import has the **"Import link"** field filled in (visible in the card
header and in the "Advanced" section of the task form). By that link the task can be
**re-read from its source**:

1. Open the task card and press **"refresh"**.
2. A confirmation with three outcomes appears:
   * **"From the import source"** — the GitHub issue is read again, and the fresh title, due
     date and **the whole description** are put into the AI2P task, while new discussion
     messages arrive in the chat;
   * **"Here only"** — the usual re-read of the task from the database, GitHub is not contacted;
   * **"Cancel"**.

What the refresh does **not** touch: acceptance criteria, assignees, status, priority, tags,
subtasks and links — they are managed in AI2P, not in GitHub. **The description is rewritten in
full**: refreshing from the source is exactly what one runs to make the task hold what is in
GitHub right now. Discussion messages that were already moved are not duplicated.

The import link can be **typed in by hand** — then a task created in AI2P without any import
can be refreshed too; and the field can be **cleared** to detach the task from its source (the
"refresh" button becomes an ordinary re-read).

## If something does not work

| Message | The cause and what to do |
| --- | --- |
| "The GitHub token was not found by the reference …" | The token has not been entered: open the source and press "Set token" (step 3). The path to the secrets file is named in the message itself. |
| "This server has not received the organization key yet…" | The server is connected to someone else's organization, but the request has not been confirmed by its conductor — without the organization key there is nothing to encrypt the value with. Confirm the connection (Settings → Servers). |
| "GitHub did not accept the TOKEN…" (answer `Bad credentials`) | The token is wrong, revoked or expired. The message shows the length and the prefix of the entered value — they show whether something else got into the field. Repeat step 1. |
| "GitHub denied access…" (403) | The token was accepted, but it has no rights to the repository: a fine-grained one needs **Issues: Read** and the repository itself in the "Repository access" list, a classic one needs the **repo** scope. |
| "GitHub is refusing temporarily: the API request rate limit is used up" | The API rate limit kicked in. Wait a few minutes; check that the import runs **with a token** — without one the limit is 80 times lower. |
| "GitHub did not find what was requested (404…)" | The owner, the repository name or the issue number is wrong. A private repository the token has no rights to looks non-existent to GitHub — that is the same 404. |
| "The link does not look like a GitHub issue…" | The address has the wrong shape: `https://github.com/<owner>/<repository>/issues/<number>` is needed. A link to a **pull request** (`/pull/<number>`) will not do — only issues are imported. |
| The import went through, but "added 0" | The filter matched no repository, the repositories have no open issues, or all the open records turned out to be pull requests. Try an empty filter. |
| Not all repositories of a private account were imported | The token was issued to **another** account: it does not see someone else's private repositories. Check with the "Check connection" button — it names the account of the token and warns about a mismatch with the owner. |

Messages arrive in full into the import window itself. The details of the requests are in the
application logs (`logs/*.jsonl`, `GitHubImporter` records): by which reference and from which
store the token was taken, what its length and prefix are (the value itself is never written to
the log), and what GitHub answered.

## Limits

* One import takes up to **10 pages of 100 records** of each kind — repositories, issues of a
  repository, comments of an issue. That is enough for 1000 repositories, 1000 open issues per
  repository and 1000 comments per issue; running into the limit is visible in the log.
* **github.com** is supported. GitHub Enterprise with its own server address is not supported
  at the moment.

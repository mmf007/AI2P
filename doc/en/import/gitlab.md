# Importing tasks from GitLab — connection guide

The import brings **GitLab issues** into AI2P tasks (spec §2.10, ch. 11). To connect you need
one string from GitLab — a **personal access token** — and one entry in the import catalog.
Both the cloud `gitlab.com` and a self-hosted GitLab server are supported.

## 1. Get an access token

GitLab issues tokens in the profile settings:

1. Sign in to GitLab in a browser and open
   <https://gitlab.com/-/user_settings/personal_access_tokens>
   (on your own server the same path from its address:
   `https://git.example.com/-/user_settings/personal_access_tokens`).
   The interface points there as well: **avatar → Edit profile → Access tokens**.
2. Press **"Add new token"** and fill in:
   * **Token name** — anything, for example `AI2P import`;
   * **Expiration date** — once it passes the token stops working and the import answers
     "GitLab did not accept the TOKEN";
   * **Select scopes** — **`read_api`** is enough. The full `api` scope also works, but it
     grants the right to change data, while the import only reads.
3. Press **"Create personal access token"** and **copy the value immediately**: GitLab will
   not show it a second time. Current tokens start with `glpat-`.

The token grants access to your projects — keep it like a password.

A **project or group token** (Settings → Access tokens inside a project) works too if it has
the `read_api` scope; it is safer in that it sees exactly one project.

## 2. Create a source in the import catalog

**Settings → the «Catalogs» tab → «Import sources» → «Add»:**

| Field | What to put in |
| --- | --- |
| Source name | anything, for example `GitLab main` |
| Kind | GitLab |
| GitLab server | empty — the cloud `https://gitlab.com`; a self-hosted server is written in full, for example `https://git.example.com` |
| GitLab project | the project path of the form `group/project` — exactly as it appears in the address bar; subgroups are separated by slashes (`group/subgroup/project`) |
| Labels | filter issues by labels, comma-separated, for example `bug,ui`; empty — all open issues of the project |
| Active | unavailable for now — turns on by itself after step 3 |

Press **"Save"**. The token button appears on a **saved** entry: the value goes into storage
at once, so before saving it is not known whom it belongs to.

**GitLab has no separate "API key"** — unlike Trello, where the key and the token are two
different values. So the GitLab source form has a single secret button, and the source
becomes active on the single token entered.

## 3. Enter the token

Open the saved source with the **"edit"** button — the form now has a **"Set token"** button.
Paste the string from step 1 and press "Save".

The old value is never shown — to replace a token you enter a new one. The state is visible
under the form: "Token: set, source — organization". If the value does not look like a GitLab
token (does not start with `glpat-`), the form warns you: that is a hint, not a ban — project
and OAuth tokens look different and work.

The **"Check connection"** button (next to it, enabled once the token is entered) asks GitLab
"who am I" and, if the source names a project, whether that project is visible to this token.
The answer comes in words: was the token accepted, which account it belongs to, is the project
visible. Checking the connection here is cheaper than catching a refusal during the first import.

**Where the value is kept.** In your organization's database, **encrypted** with the
organization key (like model API keys): it travels to all servers of the organization by
itself, and only a server that has been given the organization key can decrypt it. Nothing
needs to be written into `secrets.json`.

**Two accounts or two GitLab servers.** Every source has **its own** token reference — create
a second source (for your self-hosted GitLab, say) and enter a different value there. Importing
a single issue by link picks the source itself, by the server address in the link.

## 4. Run the import

1. Choose a **project** (in the header or on the project tab) — the import always goes into
   the current AI2P project.
2. In the task list press the **cloud with an arrow** icon button ("Import tasks") — it is
   visible only when a project is selected.
3. In the dialog choose the source and the checkboxes:
   * **add new** (on by default) — issues that are not here yet become AI2P tasks;
   * **update existing** — previously imported tasks get their title, description and due
     date refreshed from GitLab (local edits of those fields are overwritten).
4. Press **"Import"**. The outcome is shown as a message: "added X, updated Y, skipped Z";
   an `import.run` entry appears in the work history.

## What exactly is imported

* **Open issues** (`state=opened`) of the named project are taken; if labels are set, only
  the ones carrying them.
* A GitLab issue → an AI2P task with the **"draft"** status: title, due date (`due_date`),
  the **article itself** (the issue body) in the description plus a source footer
  ("Imported from GitLab: project …, issue #N" with a link). Executors and priority you fill
  in inside AI2P.
* **Attachments.** A GitLab issue has no separate attachment list: an uploaded file lives as
  a `/uploads/<hash>/<name>` link **inside the text itself**. The import finds such links,
  **downloads the files** into the AI2P project storage and rewrites the links to local ones —
  images stay images. A file that could not be downloaded (no rights, larger than 200 MB) stays
  a link to GitLab: an absolute one rather than a relative one, so it can at least be followed.
* **Discussion.** The issue comments are moved into the AI2P **task chat**: the author is shown
  as `gitlab:<login>`, the time and the order are preserved. GitLab system notes ("changed the
  label", "assigned", "closed") are skipped — they are not a conversation between people.
  A repeated import adds only the **new** messages and makes no duplicates.
* **No duplicates are created**: the task remembers an external reference of the form
  `gitlab:<server>:<project>:<number>`; a repeated import skips or updates the same issue —
  by the checkbox. The server is part of the reference on purpose: issue #42 of your GitLab
  and issue #42 of the cloud one are different issues.

## Importing one issue by link

Next to the bulk import button in the task list there is an **"Import a task by URL"** button:

1. Paste a link of the form `https://gitlab.com/<group>/<project>/-/issues/<number>`. Subgroups,
   the old address form without `/-/`, the `?…` and `#note_…` tails and a self-hosted server
   address are all understood. The source is asked about only when there is more than one
   active gitlab source and the server address does not single one out.
2. The issue content is put **into the new task form**: title, the article in the description,
   due date, downloaded files. The discussion goes into the chat as soon as the task is saved.
3. Fill in the rest of the form (executor, priority) and save. A repeated import of the same
   issue creates no duplicate.

AI agents have the same action as the `import_task_from_url` tool (action code
`AI2P.Tasks.ImportFromUrl` — the deny/confirm security rules apply as usual). Which procedure
imports it is decided by the **link form**: the agent does not need to know which sources exist.

## Refreshing a task from GitLab

An imported task has its **import link** filled in (the "Advanced" section of the task form,
a line in the card header). The **"refresh"** button on such a card **asks first**:

* **"From the import source"** — re-read the issue and put its title, due date, description
  (with freshly downloaded files) and the **new** discussion messages into the task;
* **"Here only"** — simply re-read the task without asking GitLab for anything;
* **"Cancel"**.

The description is rewritten **in full** on refresh — exactly as bulk import with the
"update existing" checkbox does. The acceptance criteria, executors, status, priority, tags
and links of the task are **not touched**: they are maintained here, not in GitLab.

Clear the "Import link" field and the task detaches from its source — there will be no more
asking.

## If something does not work

| Message | Cause and what to do |
| --- | --- |
| "The GitLab token was not found (…)" | The token has not been entered: open the source and press "Set token" (step 3). On an old installation — check `secrets.json`: the path to the file is named in the message itself. |
| "This server has not received the organization key yet…" | The server is connected to someone else's organization but the request has not been confirmed by its conductor — without the organization key there is nothing to encrypt the value with. Confirm the connection (Settings → Servers). |
| "GitLab did not accept the TOKEN (HTTP 401…)" | The token is wrong, revoked or **expired**, or it lacks the `read_api` scope. Issue a new one (step 1). A frequent cause is copying the wrong value: the message shows the length of what was entered (the value itself is never shown). |
| "GitLab denied access (HTTP 403…)" | The token was accepted, but its owner has no rights to this project, or the token lacks `read_api`. |
| "GitLab (…) did not find what was requested (HTTP 404…)" | Wrong project path or issue number. **A private project invisible to the token owner also answers 404** — GitLab deliberately does not reveal that other people's projects exist. |
| "GitLab replies 'too many requests' (HTTP 429)" | The request rate limit. Wait a minute and repeat. |
| "The import source does not name a GitLab project" | The "GitLab project" field is empty — the bulk import has nowhere to take issues from. Fill in a path of the form `group/project`. |
| "The link … does not look like a GitLab issue" | The address does not lead to an issue: the `.../-/issues/<number>` form is needed. A link to a merge request, a board or an epic will not do. |
| The import ran, but "added 0" | The labels matched no issue, or the project has no open issues; try with an empty "Labels" field. |
| A file from the description does not open | The file was not downloaded (no rights, larger than 200 MB) — the description kept a link to GitLab, and it opens only for someone signed in to GitLab. The reason is written to the log. |

Messages arrive in full inside the import window — there is no need to fish them out of a
tooltip. Request details are in the application logs (`logs/*.jsonl`, `GitLabImporter` entries):
which address was requested, where the token was taken from and how long it is (the value
itself is never written to the log), and what GitLab answered.

## How GitLab differs from Trello in this import

| | Trello | GitLab |
| --- | --- | --- |
| Secrets | two: API key + token | one: a personal token |
| How the secret travels | as query string parameters | as the `PRIVATE-TOKEN` header |
| What the source defines | a login and a board name filter | server address, project path, labels |
| Unit of import | a board card | a project issue |
| Attachments | a separate list on the card | links inside the article text |
| Task external reference | `trello:<card id>` | `gitlab:<server>:<project>:<number>` |

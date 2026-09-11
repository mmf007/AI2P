# Importing tasks from Trello — how to connect

The import moves Trello cards into AI2P tasks (spec 2.10, ch. 11). To connect you need two
strings from Trello — the **API key** and the **Token** — and one record in the import
reference. Step by step:

## 1. Obtain the API key

Trello issues keys through the integrations page (Power-Ups):

1. Sign in to Trello in a browser and open <https://trello.com/power-ups/admin>.
2. Click **«New»** (create a new integration). Fill in the required fields:
   * **Name** — anything, for example `AI2P import`;
   * **Workspace** — your workspace;
   * **Email / Author** — yours.
   The "Iframe connector URL" field may be left empty.
3. Open the integration you created → the **«API key»** tab → the
   **«Generate a new API key»** button.
4. Copy the value of the **API key** field (a string of letters and digits).

## 2. Obtain the Token

On the same page, to the right of the API key field, there is a **«Token»** link
(in the text "you can manually generate a Token"):

1. Click it — the Trello authorization page opens.
2. Click **«Allow»** at the bottom of the page.
3. Copy the **token** that is shown (a long string).

The token gives access to the boards of your account — keep it like a password.

## 3. Create a source in the import reference

**Settings → the «Catalogs» tab → «Import sources» → «Add»:**

| Field | What to enter |
| --- | --- |
| Source name | anything, for example `Trello main` |
| Kind | Trello |
| Trello login | your **username** — shown in the Trello profile as `@name` (enter it without `@`) |
| Filter | a substring of the board name, for example `AI2P`; empty — all boards |
| Active | not available yet — it turns on by itself after step 4 |

Click **«Save»**. The buttons for entering the key and the token appear on a **saved**
record: the value goes into the store right away, so before saving it is not known whose
value it is.

## 4. Enter the API key and the token (v1.65)

Open the saved source with the **«edit»** button — two buttons have appeared on the form:

1. **«Set API key»** — paste the string from step 1, «Save».
2. **«Set token»** — paste the string from step 2, «Save».

Each button asks for **exactly one field**. The old value is never shown — to replace a key
you enter a new one. Below the form you can see the state: "API key: set, source —
organization", the same for the token.

**The key and the token are easy to swap**, and Trello answers that with an unhelpful
"invalid key". That is why the form checks their shape and warns you: **the API key is
exactly 32 characters** of `0–9 a–f`, **the token is 64 of the same characters** or a string
starting with `ATTA`. Neither of them is the **"Secret"**: the integration secret from the
same page is not a token and is not needed in AI2P.

**The «Check connection» button** (next to the entry buttons; it works once both values are
filled in) asks Trello "who am I" and answers immediately in words: whether the key was
accepted, whether the token was accepted, which account the token was issued to and whether
it matches the login of the source. Checking the connection here is cheaper than catching a
rejection during the first import.

**Where these values are kept.** In the database of your organization, **encrypted** with the
organization key (like the API keys of models): they travel to all servers of the
organization by themselves, and only a server that has been issued the organization key can
decrypt them. There is no longer any need to write anything into `secrets.json`.

**Until both values are entered the source cannot be active** — the "Active" switch is
locked. As soon as the last of the two is entered, the source **turns itself on**; after that
you control activity yourself.

**Installations where the keys are already written into `secrets.json`** keep working: the
values are read in the order "organization → `secrets.json` → environment variable", and on
the first start of the new version they are moved from the file into the organization — the
form will show "set" right away. The environment variables `TRELLO_API_KEY` / `TRELLO_TOKEN`
also still work and are not moved anywhere: they are set from the outside deliberately.

**Two Trello accounts.** Every source created starting with v1.65 has **its own** key/token
pair — create a second source and enter different values there. Sources that existed before
v1.65 share one pair (the references `trello.apiKey` / `trello.token` in the parameters of
the record): to separate them, create the source anew.

## 5. Run the import

1. Choose a **project** (in the header or on the project tab) — the import always goes into
   the current project.
2. In the task list click the icon button **cloud with an arrow** ("Import tasks") — it is
   visible only when a project is selected.
3. In the dialog choose the source and the check boxes:
   * **add new** (on by default) — cards that do not exist yet become tasks;
   * **update existing** — previously imported tasks get their title, description and due
     date refreshed from Trello (local edits of these fields will be overwritten).
4. Click **«Import»**. The result is shown as a message: "added X, updated Y, skipped Z";
   an `import.run` record appears in the "Work history".

## What exactly is imported

* **Open cards of open boards** of the given login are taken; boards are filtered by a
  substring of the name (an empty filter — all of them).
* A card → a task with the status **"draft"**: title, due date, the card description plus a
  source note at the bottom ("Imported from Trello: board …, card" with a link).
  Executors and priority you fill in later, in AI2P.
* **No duplicates are created**: the external card id is remembered in the task
  (`trello:<id>`); a repeated import will skip or update the same card — depending on the
  check box.

## If something does not work

| Message | Cause and what to do |
| --- | --- |
| "Trello key not found (…)" | The API key or the token has not been entered: open the source and click "Set API key" / "Set token" (step 4). On an old installation — check `secrets.json`: the path to the file is named right in the message. |
| "This server has not received the organization key yet…" | The server is connected to somebody else's organization, but the request has not been confirmed by its conductor — without the organization key there is nothing to encrypt the values with. Confirm the connection (Settings → Servers). |
| "Trello did not accept the API KEY…" (Trello answers "invalid key") | The key is wrong: most often the **token** ends up in the key field — the message says so directly and shows the length of the value entered. Repeat step 1 and enter the key again. |
| "Trello did not accept the TOKEN…" ("invalid token") | The token is wrong, has been revoked, or the key / the "Secret" ended up in its field — repeat step 2. |
| "Trello denied access…" | The key and the token were accepted, but the account the token was issued to has no rights to this board or card. |
| "Trello did not find what was requested (HTTP 404…)" | The login (username) does not exist, or the card is not available to the account of the token — check against the Trello profile. |
| The import went through, but "added 0" | The filter matched no board, or the member has no open cards; try an empty filter. |

The messages arrive in the import window itself in full — there is no need to fish them out
of a tooltip. Details of the requests are in the application logs (`logs/*.jsonl`, records of
`TrelloImporter`): which reference and which store the key and the token were taken from,
what their length is (the values themselves are never written to the log), and what answer
Trello returned.


## Importing a single card by link (v1.50)

Next to the bulk import button in the task list there is the **«Import a task by URL»**
button:

1. Paste a link to a card of the form `https://trello.com/c/<code>` (the "Share" button on a
   Trello card). The source is asked for only if there is more than one active Trello source
   — the keys are taken from it.
2. The content of the card is put **into the new task form**: title, description, due date.
   **File attachments of the card are downloaded** into the project store and inserted into
   the description as links (images — as previews); link attachments stay links. A file
   larger than 200 MB is not downloaded — it stays a link with a note.
3. Fill in the rest of the form (executor, priority) and save. Importing the same card again
   will not create a duplicate.

AI agents have the same action available as the `import_task_from_url` tool (action code
`AI2P.Tasks.ImportFromUrl` — the deny/confirm security rules apply as usual).

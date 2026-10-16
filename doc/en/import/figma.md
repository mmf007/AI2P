# Importing a design from Figma — connection guide

The import turns a **Figma design area** into an AI2P task: screens (and, optionally, design
elements) arrive as **images**, and under every image the task description lists **all
captions** of that frame — button, heading and field texts, top to bottom. A task like
"build this screen" thus gets both the look and the exact texts. You need a Figma personal
access token and one record in the import directory.

## 1. Get a token

**The Figma API does not answer without a token at all — even for a public file** that
opens in a browser without signing in. A token is always required.

1. Sign in to Figma and open <https://www.figma.com/settings> → the **Security** tab.
2. Under **Personal access tokens** click **Generate new token**.
3. Any name, e.g. `AI2P import`; any expiration.
4. Scopes: **File content → Read only**. Nothing else is needed.
5. **Generate token** and copy the string. It starts with `figd_` and is shown **once**.

The token reads everything your account can see — keep it like a password.

## 2. Create a source in the import directory

**Settings → "Directories" tab → "Import directory" → "Add":**

| Field | Value |
| --- | --- |
| Source name | anything, e.g. `Figma design` |
| Kind | Figma |
| Active | unavailable for now — turns on by itself after step 3 |

A Figma source has no other fields: import works **only by link** to a design area.
Click **"Save"**.

## 3. Enter the token

Open the saved source with **"edit"**, click **"Set token"** and paste the string from
step 1. Figma has no API key: one secret, one button. The form warns if the value does not
start with `figd_`. Once the token is entered, the source **activates itself**.

**"Check connection"** asks Figma "who am I" and answers in words: whether the token is
accepted and which account it belongs to.

The token is stored **encrypted** in the organization database and reaches all its servers.

## 4. Import a design area

1. In Figma **select the area** — a screen, a section with screens or a page — and copy the
   link to the selection: right click → **Copy link to selection** (or `Ctrl+L`). The link
   gets a `node-id=…` parameter — that is the area.
2. In AI2P pick the project and click **"Import task by URL"** in the task list.
3. Paste the link, e.g.
   `https://www.figma.com/design/rqN1QJdU35Q18H5mpUmlFd/Home-Planner?node-id=54965-55076`.
   Two choices appear:
   * **"Design elements as separate images"** — off: main screens only; on: every screen
     element as a separate image with its own captions too;
   * **"Import the whole file"** — shown only when the link has **no** `node-id`. Without
     it such a link is refused: a whole file can be huge.
4. Click **"Import"**: images are downloaded into the project, the content fills the new
   task form. Complete the form and save.

### What is a screen and an element

* **A link to a frame** is **one screen**: its nested frames are parts of the screen.
* **A link to a page, section or group** — the top-level frames inside it become screens
  (sections and groups are passed through).
* **Elements** (if on) — components and their instances (not descended into) and nested
  frames and groups. A repeated instance of one component with the same captions is taken
  once. Very small layers (under 8 px) are skipped.
* Hidden layers are not imported.

### What the description looks like

A source header (file, area, link), then a section per screen: `## 1. Screen name`, the
image, the **"Captions:"** list. With elements — an "Elements of screen …" subsection with
the same layout per element. Images are PNG at 2× (a very large frame — 1×).

## 5. AI agents

Agents get the same action as the `import_task_from_url` tool (action code
`AI2P.Tasks.ImportFromUrl`), a CLI agent as
`ai2p import <link> --elements true|false [--wholeFile]`. **Both choices are questions for
the human**: until the agent passes them the import is not done, and the agent is asked to
check with the human. A link without `node-id` is imported only with the human's explicit
consent.

## 6. Refreshing a task from Figma

An imported task has its **"Import link"** filled in — it remembers the area and the choice
"elements" (`ai2p-elements=1`) or "whole file" (`ai2p-whole=1`). **"Refresh" → "From the
import source"** re-reads the design the same way: the title and the **whole description
are rewritten** with fresh images. Acceptance criteria, executors, status and links are not
touched. Previous images stay in the project's uploads/.

## Troubleshooting

| Message | Cause and fix |
| --- | --- |
| "The Figma token was not found by reference …" | The token is not entered: step 3. |
| "Figma rejected the TOKEN…" (`Invalid token`, 403) | The token is wrong, revoked or expired. Generate a new one (step 1). |
| "Figma denied access…" (403) | The token lacks **File content: Read**, or the file is not visible to the token's account. |
| "Figma did not find the file or node…" (404) | Wrong link: file key or `node-id`. |
| "The link has no node (node-id)…" | Copy the link to the selection or turn on "Import the whole file". |
| "No image…" under a screen | Figma did not render the node (empty or too large). Captions are still there. |
| "The per-import image limit (300)…" | Too many elements: import parts of the design by separate links. |
| "A Figma source has no bulk import" | Bulk import is not for Figma — use "Import task by URL". |

Request details are in the application logs (`logs/*.jsonl`, `FigmaImporter` records); the
token value is never logged, only its length and prefix.

## Limits

* At most **300 images** per import.
* Design comments are not moved to the task chat.
* No bulk import — by link only.

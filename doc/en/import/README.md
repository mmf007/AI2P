# Importing tasks: how the source reference works

The directory with the documents of the source kinds: one file per **kind**, the file name is
the **kind code** (`trello.md`, `github.md`, `gitlab.md`). Opened by the **«i»** button next to
the "Kind" field on the source form: **Settings → Catalogs → Imports**.

The document belongs to the **kind** and not to a reference record: how to obtain the keys of
the external system is the same for every source of that kind, and you need to know it before
the record is saved.

## Section contents

* [github](github.md) — Importing tasks from GitHub — connection guide
* [gitlab](gitlab.md) — Importing tasks from GitLab — connection guide
* [trello](trello.md) — Importing tasks from Trello — how to connect

## What every kind has in common

**There are one or two secrets.** Trello has two — an **API key** and a **Token** — so the
source form has both the "Set API key" button and the token button. GitHub and GitLab have
**one** secret, a personal access token; they have no API key button at all, and the source is
activated by that single token. Secret references of the `trello.*` form belong to Trello only.

**The secret belongs to the organization**: it is encrypted with the organization key and
replicated to its servers — there is no need to enter it on every computer.

**There are two kinds of import:** the bulk one (by a reference record — a board, a repository,
a project) and the single one by a link to a card or an issue. The single one picks the
procedure **by the link itself**, which is why an issue address of GitHub and the old form of a
GitLab address (without the `/-/` separator) are told apart by the server name: GitLab accepts
the old form only when the host name holds the word `gitlab`.

**Refreshing from the source** also goes by the link of the imported task — the button on its
card. An imported task remembers where it came from.

## How to add a document for a new kind

Put a `<kind code>.md` file here — with exactly the code the kind is named by in the reference
(`trello`, `github`, `gitlab`): the file name is worked out by the application, it cannot be
invented. Create the same file in `../../ru/import/` and add both to the section contents (with
the `test/t18s1/mktoc.py` script).

Write external addresses in these documents **in full**, with the `https://` scheme — they are
opened by the «i» button inside a page of the application, and a relative link is dead there.

## What these documents do not cover

The way to obtain the keys is described as of the day the document was written. External systems
change the interface of their settings silently: if the button names have drifted apart from the
text, look for the personal access tokens section — the sequence of actions itself is more
stable than the captions.

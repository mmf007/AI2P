# How the AI2P documentation is arranged

This page is about the documentation itself: where it lies, what it consists of, how to add a new
document to it and how to keep the languages in step. For the list of documents see the
[contents](../index.md).

## Where it lies and what is shipped

The directory lives in `AI2P_app/doc/` — next to the code, because it is **shipped together with the
program**: building the release copies the whole of `AI2P_app/doc` into the root of the release, and
the application shows these documents straight from the UI. The layout is the same for every
language (`doc/<language>/<section>/…`).

**A language has two first pages, and that is not duplication:**

* **`index.md` — the contents.** It is what the "Documentation" button leads to: reading starts from
  the list of pages. The contents are written **by hand** and grouped by meaning (the
  administrator's guide, the user's guide, the examples), so the contents-building script does not
  touch them. Every document the package holds is reachable from there by links.
* **`README.md` — a short description of the system.** What a person sees first on GitHub: what this
  is, what it is for and where to go next. It also stays the entry point for a language whose
  `index.md` has not been created yet — the documentation button falls back to it by itself.

The languages are listed in `AI2P_app/readme.md` — it is the language dispatcher, and there is
nothing in it except the logo, the name and the list of links.

The pictures lie in `doc/images/` (common to all the languages) and `doc/<language>/images/`
(language-dependent ones — screenshots with captions). A directory without a single `.md` is not
considered a language — neither contents nor a translation are required of it.

Project documents (the specification, research reports, the release procedure) are not part of
this — they live in `doc/` at the repository root and are not shipped.

## How the sections are arranged

A section is a subdirectory inside a language. Every section has **its own `README.md`** — it is the
contents of the section: it lists all of its documents and explains what they have in common.

There are three sections today:

* `man/` — the [user guide](README.md): plain text split into chapters, the file name can be
  anything;
* `models/` — [one document per record of the AI model reference](../models/README.md);
* `import/` — [one document per kind of task source](../import/README.md).

The file name of a document is worked out by the application **itself**: for models it is the model
name from the reference, for imports it is the kind code of the source. The name is taken as is,
characters not allowed in a file name are replaced with `_`. If the document exists in no language at
all, the «i» button opens a hint with the full path to put it at.

## How to add a document

1. Put the file at `<section>/<name>.md` — the name follows the rule of the section (see its
   `README.md`).
2. Write it **by hand** into the contents of the language (`index.md`) — the documents there are
   grouped by meaning, and a script cannot guess the group. It gets into the contents of the
   **section** by itself: the section contents are built by the `test/t18s1/mktoc.py` script from
   what the directories actually hold; `python test/t18s1/mktoc.py --check` shows whether they have
   drifted apart from the files.
3. Link to neighbouring documents with **relative** links (`models/GLM-5.2.md`,
   `../import/trello.md`) — the documentation window resolves those. In the documents opened by the
   **«i»** button (models, imports) write external addresses in full, with the `https://` scheme.

The contents file is called `README.md` in every directory — there is no need to change the case of
the name.

## Where to put new chapters

A new chapter is a separate `man/<name>.md` file. Write it into the [contents of the
language](../index.md) (they are written by hand and grouped by meaning), and it will get here, into
the section contents, by itself — this list is built by a script. Create a **translation under the
same name** in `../../ru/man/` at the same time: the sets of the languages have to match file for
file.

A rule worth following: the numbers, the field names and the button captions in a guide go stale
silently. Check them against the interface dictionary (`i18n/en.json`) and the code rather than from
memory — the model reference tab, for one, is called **«Models»** and not "AI models".

## Translations

Documents of other languages live in the neighbouring directories (`doc/ru/…`) and repeat the layout
one to one: as many files in `en` as there are in `ru`. This is checked by the `test/t18s1/cmp.py`
script — it prints what is missing in which language.

If a document is missing in the interface language, the application shows the Russian one, and if
that is missing too — the English one.

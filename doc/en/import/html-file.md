# Importing an HTML page ("Public HTML") — guide

The **"Public HTML"** entry is already in the import catalog — on the first line. It has no
settings and no secrets: there is nothing to create, no keys to enter, no connection to check.

## How to import

Project task list → **"Import task by URL"** → paste the page address → **"Import"**.
The address decides what happens:

| Address | What is imported |
| --- | --- |
| `file:///C:/folder/page.htm` or a full path `C:\folder\page.htm` | A page saved on disk. Pictures from the `…_files` folder next to it (the browser creates it on "Save as… → complete") are copied into the task. Frames with local files are expanded. |
| `https://…` or `http://…` | A public web page — **only that page**, the import does not follow links. Pictures are downloaded and copied into the task; all links stay clickable and lead to the source site. |

The task is created as a draft: the title comes from `<title>` (otherwise the first `<h1>`), the
description is the page text in Markdown. If the source page is deleted later, the task stays intact.

Trello, GitHub, GitLab and Figma addresses still go to their own importers — "Public HTML"
only takes what nobody else recognised.

## Limitations

* A page drawn by a script (single-page applications) arrives almost empty: the import reads
  the HTML as is and does not run scripts. Save such a page from the browser
  ("Save as… → complete") and import the file.
* A page behind a login cannot be read: the import does not know the browser cookies.
* Limits: 64 MB per page, 50 MB per picture, no more than 300 pictures.
* Styles, forms, video and audio are not transferred.
* A web page cannot take files of this computer: `file:` pictures in it are skipped.
* For an agent, a file on disk is readable only in folders opened by the task's security rules.

## Refresh

The "refresh" button in the task card re-reads the page by its import link.

# dev.to plugin (publish.devto)

Lets agents publish articles on [dev.to](https://dev.to) through the Forem API.

## Setup

1. On dev.to: **Settings → Extensions → DEV Community API Keys** — create a key.
2. In AI2P: **Settings → Plugins and MCP → dev.to → Initialize**, then paste the key into the Secret field and press Save.
   The key is stored in this server's `secrets/` (`plugin.publish.devto.token`); it never goes into a task, config.json or the organisation database.

## Agent tool `devto_publish`

| Parameter | Meaning |
|---|---|
| `file` | path to a `.md` in the project folder (required) |
| `published` | `true` publishes, `false` (default) saves a draft |

The file is Markdown with dev.to front matter:

```
---
title: Article title
description: Short description
tags: ai, devops, tools
canonical_url: https://example.com/blog/post
cover_image: https://example.com/img/cover.png
---
Article text…
```

* The first call is `POST /api/articles`; `devto_id` and `devto_url` are appended to the front matter.
* A repeat call for the same file is `PUT /api/articles/{devto_id}`: the same article is updated.
* The `published` line of the sent front matter is overwritten with the flag, so a draft cannot be published by accident.
* The tool returns the article address.

## Limits

* **Images by address only.** The dev.to API does not accept uploads: the cover and inline images must be public https links (site blog, repository).
* The action is in the action catalog (`AI2P.Plugins.DevTo.Publish`): a deny/confirm security rule on this code closes publishing.
* Medium cannot be connected this way: it no longer issues integration tokens; a human publishes there by importing the article URL.

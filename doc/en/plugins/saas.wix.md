# Wix — plugin `saas.wix`

**What it does:** lets an AI agent edit Wix sites: pages and content (blog, CMS), design,
images and files, SEO, publishing. It connects to the official Wix MCP server
(`https://mcp.wix.com/mcp`) with an **API key**. Several sites are supported, each with its own
key (or the plugin's shared key).

The agent works with a site through four **universal** AI2P actions, the same for every SaaS
site editor (Wix today, others later):

| Action | What it does |
|---|---|
| `site_list` | which sites are connected, whether a key is set, whether rules deny the site |
| `site_tools` | Wix tools for the site with argument schemas |
| `site_call` | call a Wix tool for the chosen site; `siteId` is filled in automatically |
| `site_upload` | upload a project folder (`dir`, e.g. `site/wix`) to a files-only site and publish it |

A CLI agent uses `ai2p call site_list`, `ai2p call site_tools --site ai2p`, `ai2p call site_call --stdin`,
`ai2p call site_upload --stdin`.

### Files-only sites (`site_upload`)

A site made from uploaded files (address `*.wix-site-host.com`, no Wix editor) accepts new files
only as a multipart upload, which Wix MCP tools cannot send. `site_upload` sends every file of
the folder in one request to the `drop` address of the site with the site's API key, and Wix
publishes the result at once. **The upload replaces the whole file set:** a file missing from
the folder disappears from the site. Before sending, AI2P refuses if the folder is outside the
project, has no `index.html`, or exceeds 20 MB. `wix.config.json` is never uploaded. The rule
operation is `publish` (`saas.wix:ai2p:publish`).

## 1. What to get in Wix

1. **API key.** Wix → profile icon → **Account Settings → API Keys**
   (`https://manage.wix.com/account/api-keys`) → **Generate API Key**.
   - Name it, e.g. `AI2P`.
   - **Sites:** pick only the site you need, not "All sites".
   - **Permissions:** tick what the agent may do. For full site editing: *Wix Media Manager*,
     *Wix Blog*, *Wix Data (CMS)*, *SEO / Promote*, *Wix Site Pages*, *Manage Sites*.
     What is not ticked the agent cannot do, whatever AI2P rules say.
   - Press **Generate** and copy the key: Wix shows it once.
2. **Account ID** — on the same API Keys page, on the right.
3. **Site ID** — open the site dashboard: `https://manage.wix.com/dashboard/<Site ID>/home`.
   The Site ID does not change when you connect a domain.

## 2. Connecting in AI2P

1. **Settings → Plugins and MCP → Wix** (`saas.wix`). The admin role is required.
2. **Sites → Add site**: short name (Latin, no spaces, e.g. `ai2p`; the agent and the security
   rules use it; it cannot be changed later), name, site URL, Site ID, Account ID, site API key
   (stored in `secrets/` of this server, never shown back; empty on edit means "keep").
3. Press **Initialize**. AI2P connects to Wix through the first site with a key, fetches the
   tool list and registers each tool in the action catalog. 401/403 means a wrong key or
   Account ID, or missing key permissions.
4. Jobs on this server now get `site_list`, `site_tools`, `site_call`. When Wix adds tools,
   press **Refresh tool list**: an unregistered tool is denied.

A shared key for several sites can be entered once in the **MCP server token** field.
Sites and keys are settings of **this computer** (config.json and `secrets/`).

## 3. Security

Rules of kind **"Plugins and MCP"**, operation "use". Mask: `<plugin>:<site>:<operation>`.
Operations: `read`, `pages`, `design`, `media`, `seo`, `publish`, `api` (anything unrecognised).

| Mask | Covers |
|---|---|
| `saas.wix` | all of Wix |
| `saas.wix:ai2p:*` | the whole ai2p site |
| `saas.wix:*:publish` | publishing any Wix site |
| `*:*:publish` | publishing in any SaaS |
| `*` | all plugins |

A sensible start: **confirm** `*:*:publish` and **confirm** `saas.wix:*:api`. Actions
`AI2P.Site.List / Tools / Call / Upload` and catalog entries `Plugin.saas.wix.<tool>` can be ruled too.

## 4. Writing a task

"On site `ai2p` (Wix) create an FAQ page from `site/faq.md`, upload images from `site/img`,
set the SEO title and description. Do not publish." The agent starts with `site_list`, then
`site_call tool=WixREADME`, and finds the API method via `SearchWixRESTDocumentation`.

## Limitations

- OAuth sign-in is not supported — API key only.
- Calls are mapped to operations by tool name and argument text (rules in
  `plugins/saas.wix/plugin.json`, block `saas.rules`); anything unrecognised is `api`.

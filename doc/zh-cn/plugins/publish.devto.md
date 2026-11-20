# dev.to 插件（publish.devto）

通过 Forem API 在 dev.to 发布文章。密钥：dev.to → Settings → Extensions → DEV Community API Keys；在 AI2P：设置 → 插件与 MCP → dev.to，填入「密钥」（保存在 secrets/）。

工具 `devto_publish`：`file` — 项目中带 front matter（title、description、tags、canonical_url、cover_image）的 .md；`published` — true 发布，false（默认）草稿。首次调用 POST /api/articles 并在 front matter 写入 `devto_id`/`devto_url`；再次调用为 PUT，更新同一篇文章。图片只能用公开 https 地址。返回文章地址。

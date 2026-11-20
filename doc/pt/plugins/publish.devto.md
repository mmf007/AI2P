# Plugin dev.to (publish.devto)

Publica artigos no dev.to pela API Forem. Chave: dev.to → Settings → Extensions → DEV Community API Keys; no AI2P: Configuracoes → Plugins e MCP → dev.to, campo Segredo (fica em secrets/).

Ferramenta `devto_publish`: `file` — .md do projeto com front matter (title, description, tags, canonical_url, cover_image); `published` — true publica, false (padrao) rascunho. A primeira chamada faz POST /api/articles e anota `devto_id`/`devto_url` no front matter; a seguinte faz PUT e atualiza o mesmo artigo. Imagens apenas por endereco https publico. Retorna o endereco do artigo.

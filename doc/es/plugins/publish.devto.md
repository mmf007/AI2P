# Plugin dev.to (publish.devto)

Publica articulos en dev.to mediante la API Forem. Clave: dev.to → Settings → Extensions → DEV Community API Keys; en AI2P: Ajustes → Plugins y MCP → dev.to, campo Secreto (se guarda en secrets/).

Herramienta `devto_publish`: `file` — .md del proyecto con front matter (title, description, tags, canonical_url, cover_image); `published` — true publica, false (por defecto) borrador. La primera llamada hace POST /api/articles y anota `devto_id`/`devto_url` en el front matter; la siguiente hace PUT y actualiza el mismo articulo. Imagenes solo por direccion https publica. Devuelve la direccion del articulo.

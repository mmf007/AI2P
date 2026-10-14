# GPT-6-Sol_cli

**Ubicación:** nube, conexión **por CLI, no por API**
**Conexión:** `provider: openai`, `transport: cli`, comando `codex`, modelo `gpt-6-sol`
**Referencia de clave:** vacía — **no se necesita clave API**, autorización por sesión del CLI

El mismo modelo que GPT-6-Sol, pero mediante el agente de terminal del proveedor **OpenAI Codex CLI** (https://github.com/openai/codex). Se paga con la suscripción del proveedor; en la facturación de AI2P cuesta cero.

## Importante: aún no hay conector

AI2P **aún no puede ejecutar tareas** con OpenAI Codex CLI: solo Claude Code tiene conector CLI. Por eso la entrada se crea **inactiva**.

## Instalación y acceso (no se necesita clave API)

1. Instalar el CLI: `npm i -g @openai/codex`.
2. Acceder: `codex login` — en una terminal del equipo servidor, con el usuario de AI2P.
3. Compruebe que `codex` está en el PATH de ese usuario; si no, escriba la ruta completa en `cliCommand`.

El acceso usa una suscripción de ChatGPT (Plus, Pro, Team, Enterprise) o una clave API de OpenAI.

Ejecución no interactiva (headless): `codex exec "..."`.

El botón de llave en la fila del catálogo muestra el comando de acceso de este CLI.

## Límites y coste

| | |
|---|---|
| Contexto | 400 000 |
| Coste | 0 (pagado por suscripción) |

## Licencia

Resultado y condiciones según la suscripción del proveedor (https://github.com/openai/codex). Comprobado el 06.10.2026 por búsqueda web.

## Requisitos de hardware

Ninguno especial: calcula el proveedor.

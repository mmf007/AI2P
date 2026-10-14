# Gemini-3.1-Pro_cli

**Ubicación:** nube, conexión **por CLI, no por API**
**Conexión:** `provider: google`, `transport: cli`, comando `agy`, modelo `gemini-3.1-pro`
**Referencia de clave:** vacía — **no se necesita clave API**, autorización por sesión del CLI

El mismo modelo que Gemini-3.1-Pro, pero mediante el agente de terminal del proveedor **Google Antigravity CLI** (https://antigravity.google). Se paga con la suscripción del proveedor; en la facturación de AI2P cuesta cero.

## Importante: aún no hay conector

AI2P **aún no puede ejecutar tareas** con Google Antigravity CLI: solo Claude Code tiene conector CLI. Por eso la entrada se crea **inactiva**.

## Instalación y acceso (no se necesita clave API)

1. Instalar el CLI: `irm https://antigravity.google/cli/install.ps1 | iex`.
2. Acceder: `agy` — en una terminal del equipo servidor, con el usuario de AI2P.
3. Compruebe que `agy` está en el PATH de ese usuario; si no, escriba la ruta completa en `cliCommand`.

Antigravity CLI sustituyó a Gemini CLI el 18.06.2026. En el primer arranque `agy` imprime un enlace y un código: abra el enlace, entre en Google y pegue el código.

Ejecución no interactiva (headless): `agy -p "..."`.

El botón de llave en la fila del catálogo muestra el comando de acceso de este CLI.

## Límites y coste

| | |
|---|---|
| Contexto | 1 000 000 |
| Coste | 0 (pagado por suscripción) |

## Licencia

Resultado y condiciones según la suscripción del proveedor (https://antigravity.google). Comprobado el 06.10.2026 por búsqueda web.

## Requisitos de hardware

Ninguno especial: calcula el proveedor.

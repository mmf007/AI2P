# Mistral-Large-3_cli

**Ubicación:** nube, conexión **por CLI, no por API**
**Conexión:** `provider: mistral`, `transport: cli`, comando `vibe`, modelo `mistral-large-3`
**Referencia de clave:** vacía — **no se necesita clave API**, autorización por sesión del CLI

El mismo modelo que Mistral-Large-3, pero mediante el agente de terminal del proveedor **Mistral Vibe CLI** (https://github.com/mistralai/mistral-vibe). Se paga con la suscripción del proveedor; en la facturación de AI2P cuesta cero.

## Importante: aún no hay conector

AI2P **aún no puede ejecutar tareas** con Mistral Vibe CLI: solo Claude Code tiene conector CLI. Por eso la entrada se crea **inactiva**.

## Instalación y acceso (no se necesita clave API)

1. Instalar el CLI: `curl -LsSf https://mistral.ai/vibe/install.sh | bash   (pip install mistral-vibe)`.
2. Acceder: `vibe` — en una terminal del equipo servidor, con el usuario de AI2P.
3. Compruebe que `vibe` está en el PATH de ese usuario; si no, escriba la ruta completa en `cliCommand`.

En el primer arranque `vibe` abre un asistente: acceso con cuenta Mistral o clave API. Por defecto Vibe usa Devstral; el modelo de la entrada lo fija el campo «modelo».

El modo no interactivo (headless) no está confirmado por fuentes públicas.

El botón de llave en la fila del catálogo muestra el comando de acceso de este CLI.

## Límites y coste

| | |
|---|---|
| Contexto | 256 000 |
| Coste | 0 (pagado por suscripción) |

## Licencia

Resultado y condiciones según la suscripción del proveedor (https://github.com/mistralai/mistral-vibe). Comprobado el 06.10.2026 por búsqueda web.

## Requisitos de hardware

Ninguno especial: calcula el proveedor.

# Grok-4.7_cli

**Ubicación:** nube, conexión **por CLI, no por API**
**Conexión:** `provider: xai`, `transport: cli`, comando `grok`, modelo `grok-4.7`
**Referencia de clave:** vacía — **no se necesita clave API**, autorización por sesión del CLI

El mismo modelo que Grok-4.7, pero mediante el agente de terminal del proveedor **xAI Grok Build** (https://x.ai). Se paga con la suscripción del proveedor; en la facturación de AI2P cuesta cero.

## Importante: aún no hay conector

AI2P **aún no puede ejecutar tareas** con xAI Grok Build: solo Claude Code tiene conector CLI. Por eso la entrada se crea **inactiva**.

## Instalación y acceso (no se necesita clave API)

1. Instalar el CLI: `npm install -g @xai-official/grok`.
2. Acceder: `grok` — en una terminal del equipo servidor, con el usuario de AI2P.
3. Compruebe que `grok` está en el PATH de ese usuario; si no, escriba la ruta completa en `cliCommand`.

En el primer arranque `grok` abre el navegador para el acceso. Sin navegador, use la variable `XAI_API_KEY`.

Ejecución no interactiva (headless): `grok -p "..."`.

El botón de llave en la fila del catálogo muestra el comando de acceso de este CLI.

## Límites y coste

| | |
|---|---|
| Contexto | 256 000 |
| Coste | 0 (pagado por suscripción) |

## Licencia

Resultado y condiciones según la suscripción del proveedor (https://x.ai). Comprobado el 06.10.2026 por búsqueda web.

## Requisitos de hardware

Ninguno especial: calcula el proveedor.

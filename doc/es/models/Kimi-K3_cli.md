# Kimi-K3_cli

**Ubicación:** nube, conexión **por CLI, no por API**
**Conexión:** `provider: moonshot`, `transport: cli`, comando `kimi`, modelo `kimi-k3`
**Referencia de clave:** vacía — **no se necesita clave API**, autorización por sesión del CLI

El mismo modelo que Kimi-K3, pero mediante el agente de terminal del proveedor **Moonshot Kimi Code** (https://github.com/MoonshotAI/kimi-code). Se paga con la suscripción del proveedor; en la facturación de AI2P cuesta cero.

## Importante: aún no hay conector

AI2P **aún no puede ejecutar tareas** con Moonshot Kimi Code: solo Claude Code tiene conector CLI. Por eso la entrada se crea **inactiva**.

## Instalación y acceso (no se necesita clave API)

1. Instalar el CLI: `irm https://code.kimi.com/kimi-code/install.ps1 | iex`.
2. Acceder: `kimi  →  /login` — en una terminal del equipo servidor, con el usuario de AI2P.
3. Compruebe que `kimi` está en el PATH de ese usuario; si no, escriba la ruta completa en `cliCommand`.

Acceso: ejecute `kimi` y use `/login` — Kimi Code OAuth o clave de Moonshot AI Open Platform.

El modo no interactivo (headless) no está confirmado por fuentes públicas.

El botón de llave en la fila del catálogo muestra el comando de acceso de este CLI.

## Límites y coste

| | |
|---|---|
| Contexto | 262 144 |
| Coste | 0 (pagado por suscripción) |

## Licencia

Resultado y condiciones según la suscripción del proveedor (https://github.com/MoonshotAI/kimi-code). Comprobado el 06.10.2026 por búsqueda web.

## Requisitos de hardware

Ninguno especial: calcula el proveedor.

# Qwen3.8-Max_cli

**Ubicación:** nube, conexión **por CLI, no por API**
**Conexión:** `provider: alibaba`, `transport: cli`, comando `qwen`, modelo `qwen3.8-max`
**Referencia de clave:** vacía — **no se necesita clave API**, autorización por sesión del CLI

El mismo modelo que Qwen3.8-Max, pero mediante el agente de terminal del proveedor **Qwen Code** (https://github.com/QwenLM/qwen-code). Se paga con la suscripción del proveedor; en la facturación de AI2P cuesta cero.

## Importante: aún no hay conector

AI2P **aún no puede ejecutar tareas** con Qwen Code: solo Claude Code tiene conector CLI. Por eso la entrada se crea **inactiva**.

## Instalación y acceso (no se necesita clave API)

1. Instalar el CLI: `npm i -g @qwen-code/qwen-code@latest`.
2. Acceder: `qwen  →  /auth` — en una terminal del equipo servidor, con el usuario de AI2P.
3. Compruebe que `qwen` está en el PATH de ese usuario; si no, escriba la ruta completa en `cliCommand`.

Qwen Code es la bifurcación de Gemini CLI de Alibaba. Acceso: ejecute `qwen` y use `/auth` (Qwen OAuth o clave API).

Ejecución no interactiva (headless): `qwen -p "..."`.

El botón de llave en la fila del catálogo muestra el comando de acceso de este CLI.

## Límites y coste

| | |
|---|---|
| Contexto | 262 144 |
| Coste | 0 (pagado por suscripción) |

## Licencia

Resultado y condiciones según la suscripción del proveedor (https://github.com/QwenLM/qwen-code). Comprobado el 06.10.2026 por búsqueda web.

## Requisitos de hardware

Ninguno especial: calcula el proveedor.

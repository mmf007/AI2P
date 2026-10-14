# Instalación del software de agentes CLI

## Qué es un modelo CLI

El catálogo de modelos tiene tres tipos de ubicación: **local** (icono de monitor: el modelo funciona en este ordenador), **en la nube** (icono de nube: AI2P llama a la API del proveedor con una clave) y **agente CLI** (nube dentro de un monitor: en el ordenador hay un programa del proveedor — Claude Code, Codex CLI, Qwen Code… — que trabaja con la nube por suscripción).

* El registro del catálogo y el ejecutor son **comunes** a toda la organización.
* El programa CLI se instala **en CADA servidor por separado**; lo instala **el administrador de ese servidor**.

## Qué se instala

La ventana de instalación (Ajustes → Modelos de IA → lápiz → «Instalar») arma la lista según el comando CLI del perfil: Claude Code (npm `@anthropic-ai/claude-code`), Codex CLI (npm `@openai/codex`), Qwen Code (npm `@qwen-code/qwen-code`); Antigravity (`agy`), Grok Build (`grok`), Kimi (`kimi`) y Mistral Vibe (`vibe`) se instalan a mano, AI2P solo busca el comando en el PATH.

Los programas npm necesitan **Node.js 20 o más reciente**: primero se busca en el sistema; si falta, se instala Node.js 24 LTS portátil en la carpeta de paquetes de AI2P (antes se revisa la carpeta de distribuciones). Si todo está instalado, el botón dice **«Actualizar»** (`npm install <paquete>@latest`).

## Para el usuario o instalación global

Casilla **«Solo para el usuario actual»** (activada por defecto; se guarda en config.json de ESTE servidor):

* **usuario actual**: `npm install --prefix "<paquetes>/cli/<programa>" <paquete>@latest`, sin derechos de administrador; el programa solo lo ve AI2P (añade su carpeta al PATH del proceso CLI);
* **global**: `npm install -g <paquete>@latest` (en Windows con `--prefix` de la carpeta del Node.js del sistema); requiere Node.js instalado en el sistema y casi siempre derechos de administrador.

### Si faltan derechos

1. Ejecute usted mismo el comando del mensaje en un terminal de administrador (Windows) o con `sudo` (Linux, macOS) y pulse **«Actualizar»**.
2. O reinicie AI2P con derechos de administrador (root) y repita la instalación: la ventana indica si AI2P se ejecuta con esos derechos. Es una alternativa, no una recomendación.
3. O marque «Solo para el usuario actual» e instale de nuevo.

Tras instalar, inicie sesión con el botón de llave en la fila del modelo.

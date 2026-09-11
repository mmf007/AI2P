# Modelos de AI2P: cómo está hecho el catálogo

Carpeta con los documentos de los modelos: un archivo por cada registro del catálogo de modelos
de IA, y el nombre del archivo es el **nombre del modelo** (`Claude-Sonnet-5.md`,
`Qwen3.8-27B-Local.md`). Se abre con el botón **«i»** del formulario del modelo: **Ajustes →
Catálogos → Modelos de IA**.

Aquí está lo común a todos: en qué se diferencian las tres formas de conexión, cómo poner la
clave, por qué un modelo puede estar no activo y cómo crear un registro propio.

## Índice de la sección

**En la nube (hace falta una clave de API)**

* [Claude-Fable-5](Claude-Fable-5.md) — API del proveedor Anthropic
* [Claude-Fable-5.1](Claude-Fable-5.1.md) — API del proveedor Anthropic
* [Claude-Haiku-4.5](Claude-Haiku-4.5.md) — API de Anthropic
* [Claude-Opus-5.0](Claude-Opus-5.0.md) — API del proveedor Anthropic
* [Claude-Sonnet-5](Claude-Sonnet-5.md) — API de Anthropic
* [DeepSeek-V4-Flash](DeepSeek-V4-Flash.md) — API de DeepSeek, compatible con OpenAI
* [DeepSeek-V4-Pro](DeepSeek-V4-Pro.md) — API de DeepSeek, compatible con OpenAI
* [DeepSeek-V4.1-Flash](DeepSeek-V4.1-Flash.md) — API de DeepSeek, compatible con OpenAI
* [ElevenLabs-Music](ElevenLabs-Music.md) — texto → música y canciones, libres de derechos, habilidades `audio-song` 89, `audio-music` 89
* [ElevenLabs-TTS-v3](ElevenLabs-TTS-v3.md) — texto → voz, habilidades `audio-speech` 96
* [Gemini-3-Ultra](Gemini-3-Ultra.md) — Google, a través de una capa compatible con OpenAI (**desactivada el 11.09.2026**)
* [Gemini-3.1-Pro](Gemini-3.1-Pro.md) — Google, a través de una capa compatible con OpenAI (**desactivada el 11.09.2026**)
* [Gemini-3.7-Flash](Gemini-3.7-Flash.md) — Google, a través de una capa compatible con OpenAI
* [Gemini-3.8-Flash](Gemini-3.8-Flash.md) — Google, a través de una capa compatible con OpenAI
* [Gemini-Omni-Flash](Gemini-Omni-Flash.md) — texto → vídeo con sonido, 8 segundos, habilidades `video-generate` 91
* [GigaChat-3.5-Ultra](GigaChat-3.5-Ultra.md) — Sber, Rusia; compatible con OpenAI
* [GLM-5.2](GLM-5.2.md) — Z.ai / Zhipu, compatible con OpenAI
* [GLM-5.3](GLM-5.3.md) — Z.ai / Zhipu, compatible con OpenAI
* [GPT-5.6-Sol](GPT-5.6-Sol.md) — API de OpenAI, compatible con OpenAI
* [GPT-5.6-Terra](GPT-5.6-Terra.md) — API de OpenAI, compatible con OpenAI
* [GPT-6-Astra](GPT-6-Astra.md) — API de OpenAI, compatible con OpenAI
* [GPT-Image-2](GPT-Image-2.md) — texto → imagen, cobro por tokens, habilidades `image-generate` 96, `image-text` 95, `image-photo` 94, `image-concept` 90
* [GPT-Image-2.5](GPT-Image-2.5.md) — texto → imagen, cobro por tokens, habilidades `image-generate` 97, `image-text` 96, `image-photo` 95, `image-concept` 92
* [Grok-4.6](Grok-4.6.md) — API de xAI, compatible con OpenAI
* [Inkling-975B](Inkling-975B.md) — Thinking Machines, a través de la pasarela OpenRouter
* [Kimi-K3](Kimi-K3.md) — Moonshot AI, compatible con OpenAI
* [Kling-3.0](Kling-3.0.md) — imagen → vídeo con sonido, hasta 15 segundos, habilidades `video-animate` 93
* [Ling-3.0-Flash](Ling-3.0-Flash.md) — Ant Group, a través de la pasarela OpenRouter
* [Meshy-7](Meshy-7.md) — texto → modelo 3D listo para el juego, habilidades `3d-generate` 87
* [MiniMax-H3-Max](MiniMax-H3-Max.md) — texto → vídeo de hasta 15 segundos, habilidades `video-generate` 92
* [MiniMax-M3](MiniMax-M3.md) — MiniMax, compatible con OpenAI
* [Mistral-Large-3](Mistral-Large-3.md) — Mistral AI, Francia; compatible con OpenAI
* [Muse-Spark-1.2](Muse-Spark-1.2.md) — Meta, a través de la pasarela OpenRouter
* [Muse-Spark-1.3](Muse-Spark-1.3.md) — Meta, a través de la pasarela OpenRouter
* [Nano-Banana-Pro-Edit](Nano-Banana-Pro-Edit.md) — imagen e indicación → imagen modificada, habilidades `image-edit` 98, `image-inpaint` 90
* [Nano-Banana-Pro](Nano-Banana-Pro.md) — texto → imagen de hasta 4K, el mejor texto dentro del cuadro, habilidades `image-text` 98, `image-generate` 97, `image-photo` 96, `image-concept` 92
* [Nemotron-3-Ultra](Nemotron-3-Ultra.md) — NVIDIA, a través de la pasarela OpenRouter
* [Qwen3.8-Max](Qwen3.8-Max.md) — Alibaba DashScope, compatible con OpenAI
* [Seedance-2.5-I2V](Seedance-2.5-I2V.md) — imagen → vídeo con sonido, habilidades `video-animate` 95
* [Seedance-2.5](Seedance-2.5.md) — texto → vídeo de hasta 30 segundos con sonido, habilidades `video-generate` 97
* [Tripo-H3.1](Tripo-H3.1.md) — imagen → modelo 3D con texturas PBR, habilidades `3d-image` 92
* [Veo-3.1](Veo-3.1.md) — texto → vídeo con sonido, 4–8 segundos, hasta 4K, habilidades `video-generate` 93
* [Wan-3.0-Prime](Wan-3.0-Prime.md) — texto → vídeo de hasta 30 segundos con sonido, habilidades `video-generate` 94
* [YandexGPT-5.1-Pro](YandexGPT-5.1-Pro.md) — Yandex Cloud, Rusia; compatible con OpenAI

**A través de Claude CLI (por suscripción, sin clave)**

* [Claude-Fable-5_cli](Claude-Fable-5_cli.md)
* [Claude-Opus-5.0_cli](Claude-Opus-5.0_cli.md)
* [Claude-Sonnet-5_cli](Claude-Sonnet-5_cli.md)

**Locales (calcula su ordenador)**

* [Muse-Glimmer-30B-Local](Muse-Glimmer-30B-Local.md) — textos y código, puntuaciones de habilidad 73–80
* [Qwen3.6-27B-Local](Qwen3.6-27B-Local.md) — código y textos, puntuaciones de habilidad 75–82
* [Qwen3.6-35B-A3B-Local](Qwen3.6-35B-A3B-Local.md) — código y textos, puntuaciones de habilidad 72–80
* [Qwen3.8-27B-Local](Qwen3.8-27B-Local.md) — código y textos, puntuaciones de habilidad 80–87

**Modelos de medios locales (vídeo, imágenes, sonido y 3D)**

* [ACE-Step-1.5-XL-Base](ACE-Step-1.5-XL-Base.md) — texto → música y canciones, habilidades `audio-song` 86, `audio-music` 87
* [ACE-Step-1.5-XL-SFT](ACE-Step-1.5-XL-SFT.md) — texto → música y canciones, habilidades `audio-song` 90, `audio-music` 89
* [ACE-Step-1.5-XL-Turbo](ACE-Step-1.5-XL-Turbo.md) — texto → música y canciones, habilidades `audio-song` 84, `audio-music` 86
* [FLUX.2-dev](FLUX.2-dev.md) — texto → imagen, habilidades `image-generate` 94, `image-photo` 93, `image-concept` 92, `image-text` 90
* [FLUX.2-klein-4B-Edit](FLUX.2-klein-4B-Edit.md) — imagen + indicación → imagen, habilidades `image-edit` 86, `image-inpaint` 83, `image-generate` 80, `image-text` 78
* [FLUX.2-klein-4B](FLUX.2-klein-4B.md) — texto → imagen, habilidades `image-generate` 88, `image-photo` 87, `image-concept` 86, `image-text` 80
* [Hunyuan3D-2.1](Hunyuan3D-2.1.md) — imagen → modelo 3D, habilidad `3d-image` 86
* [HunyuanVideo-1.5-720p-T2V](HunyuanVideo-1.5-720p-T2V.md) — texto → vídeo, habilidad `video-generate` 79
* [Kandinsky-5.0-I2V-Lite-5s](Kandinsky-5.0-I2V-Lite-5s.md) — imagen + texto → vídeo (5 segundos), habilidades `video-animate` 78, `video-generate` 74
* [Kandinsky-5.0-Image-Lite](Kandinsky-5.0-Image-Lite.md) — texto → imagen, habilidades `image-text` 88, `image-generate` 84, `image-concept` 83, `image-photo` 82
* [Kandinsky-5.0-T2V-Lite-distil16-10s](Kandinsky-5.0-T2V-Lite-distil16-10s.md) — texto → vídeo, habilidad `video-generate` 70
* [Kandinsky-5.0-T2V-Lite-distil16-5s](Kandinsky-5.0-T2V-Lite-distil16-5s.md) — texto → vídeo, habilidad `video-generate` 70
* [Kandinsky-5.0-T2V-Lite-nocfg-10s](Kandinsky-5.0-T2V-Lite-nocfg-10s.md) — texto → vídeo, habilidad `video-generate` 74
* [Kandinsky-5.0-T2V-Lite-nocfg-5s](Kandinsky-5.0-T2V-Lite-nocfg-5s.md) — texto → vídeo, habilidad `video-generate` 74
* [Kandinsky-5.0-T2V-Lite-sft-10s](Kandinsky-5.0-T2V-Lite-sft-10s.md) — texto → vídeo, habilidad `video-generate` 76
* [Kandinsky-5.0-T2V-Lite-sft-5s](Kandinsky-5.0-T2V-Lite-sft-5s.md) — texto → vídeo (5 segundos), habilidades `video-generate` 76, `video-animate` 72
* [LTX-2.5](LTX-2.5.md) — texto → vídeo con sonido, habilidad `video-generate` 85
* [Qwen-Image-2512](Qwen-Image-2512.md) — texto → imagen, habilidades `image-text` 94, `image-photo` 91, `image-generate` 90, `image-concept` 85
* [Qwen-Image-Edit-2511](Qwen-Image-Edit-2511.md) — imagen + indicación → imagen modificada, habilidades `image-edit` 90, `image-text` 90, `image-inpaint` 85, `image-generate` 82
* [SD-3.5-Large](SD-3.5-Large.md) — texto → imagen, habilidades `image-generate` 80, `image-photo` 79, `image-concept` 78, `image-text` 70
* [SDXL-1.0](SDXL-1.0.md) — texto → imagen, habilidades `image-generate` 70, `image-concept` 70, `image-photo` 68
* [TRELLIS-2](TRELLIS-2.md) — imagen → modelo 3D CON COLOR, habilidad `3d-image` 85
* [TripoSplat](TripoSplat.md) — imagen → SPLATS GAUSSIANOS, habilidad `3d-image` 80
* [Wan-2.2-I2V-A14B](Wan-2.2-I2V-A14B.md) — imagen + texto → vídeo, habilidades `video-animate` 80, `video-generate` 74
* [Wan-2.2-T2V-A14B](Wan-2.2-T2V-A14B.md) — texto → vídeo, habilidad `video-generate` 80
* [Z-Image-Turbo](Z-Image-Turbo.md) — texto → imagen, habilidades `image-generate` 85, `image-photo` 84, `image-concept` 82, `image-text` 78

## Tres formas de conexión

| | en la nube | local | a través del CLI |
|---|---|---|---|
| Dónde calcula | el servidor del proveedor | **su ordenador** | el servidor del proveedor |
| Pago | por tokens | no | **por suscripción**, en la facturación 0 |
| Qué hace falta | **clave de API** | **descargar los archivos** (decenas de GB) | `claude login` en este ordenador |
| `provider` en el perfil | `anthropic`, `openai-compatible` | `openai-compatible` | `anthropic` + `transport: cli` |
| `baseUrl` | la dirección del proveedor | `http://localhost:<puerto>/v1` | no se usa |
| Herramientas del agente | las herramientas de AI2P | las herramientas de AI2P | **las herramientas propias del CLI** |
| Los datos salen fuera | sí | **no** | sí |

* **En la nube**: la mayoría de los registros del catálogo. Rápido, de calidad, de pago; hacen
  falta la clave del proveedor y salida a internet. Ejemplos: Claude-Sonnet-5, GPT-5.6-Sol,
  DeepSeek-V4-Pro.
* **Local** (el nombre termina en `-Local`): los pesos están en su disco, calcula su tarjeta
  gráfica y no sale nada al exterior. No hace falta clave, pero sí espacio, memoria y paciencia
  para la descarga. Ejemplos: Qwen3.8-27B-Local, Muse-Glimmer-30B-Local. El servidor local
  (`llama-server`) lo levanta AI2P ella misma al poner en marcha el trabajo del equipo y lo apaga
  al detenerlo, y **sólo su propio proceso**: uno ajeno en ese mismo puerto no se toca.
* **A través del CLI** (el nombre termina en `_cli`): el mismo modelo en la nube, pero se lanza a
  través de Claude Code en modo headless y se paga **por suscripción**, no por tokens. **No hace
  falta clave de API**, hace falta un `claude login` hecho. Una diferencia importante: el agente
  trabaja con **sus propias** herramientas directamente en la carpeta del proyecto, y las
  herramientas de AI2P no se le publican. Ejemplos: Claude-Sonnet-5_cli, Claude-Fable-5_cli.

## Cómo poner la clave

1. **Ajustes → Catálogos → Modelos de IA**: busque el registro en la lista.
2. El botón **«Establecer la clave de API»** del formulario del modelo (si la clave ya existe, se
   llama «Actualizar la clave de API»; el valor anterior no se muestra nunca).
3. Pegue el valor entregado por el proveedor y guarde.

**La clave es común a todos los modelos de un mismo proveedor.** En el perfil de cada registro
hay un campo «Clave en los secretos (referencia)», por ejemplo `anthropic.apiKey` u
`openrouter.apiKey`. Todos los registros con esa misma referencia usan una misma clave:
introducida una vez para Claude-Sonnet-5, funcionan también Opus y Haiku. Dónde conseguir la
clave exactamente está escrito en el documento del modelo concreto, en la sección **«Cómo
conseguir la clave»**.

La clave **pertenece a la organización**: se cifra con la clave de la organización y se replica a
todos sus servidores, así que no hace falta introducirla en cada ordenador. La clave de la
organización en sí está sólo en su propio ordenador (`secrets.json`) y no se replica nunca.

## Dónde está la clave en el disco

La clave se puede indicar también como **archivo**: es la segunda forma, para instalar sin
interfaz y para trasladar claves antiguas. Los archivos están en la subcarpeta **`secrets/` junto
a `config.json`** (es decir, en la carpeta donde está instalada la aplicación), un json por cada
referencia:

```
secrets/anthropic.apikey.json
{ "ref": "anthropic.apiKey", "value": "sk-ant-..." }
```

La ruta completa de ese archivo para un modelo concreto se muestra en su formulario, en la línea
**«Archivo de clave en este ordenador»**, allí mismo donde está el botón de establecer la clave.
La subcarpeta se crea en el primer arranque de la aplicación, y dentro hay un `readme.txt` con la
explicación.

**Una clave introducida en el formulario no crea aquí ningún archivo**, y no es un error: se va
cifrada a la base de la organización para llegar a los demás servidores. El archivo aparece en
las claves del almacenamiento de archivos: las trasladadas del antiguo `secrets.json` al arrancar
y las escritas a mano. Si ese archivo existe, introducir un valor nuevo en el formulario lo
actualizará también.

El orden en el que se busca la clave: **base de la organización → `secrets/` → `secrets.json` →
variable de entorno** (`anthropic.apiKey` → `ANTHROPIC_API_KEY`). La subcarpeta `secrets/` no
entra ni en la replicación, ni en la publicación, ni en el inventario de la instalación: al
actualizar de versión se queda intacta.

## Por qué un modelo puede estar no activo

El atributo «activo» decide si el modelo se ofrece al elegir ejecutor. Activarlo no siempre se
puede, y eso es una regla, no un error:

* **un modelo en la nube sin clave de API no puede estar activo**: el formulario lo escribe tal
  cual, «No hay clave de API: el modelo se activará en cuanto se indique la clave», y el
  intento de activar el atributo por la API se rechaza;
* **un modelo local sin los archivos descargados no puede estar activo**: «El modelo no está
  instalado: se activará automáticamente tras la instalación».

El sentido es sencillo: de lo contrario el ejecutor figuraría como listo, el encargo se le iría y
se caería ya en el arranque, con un error de red poco claro en lugar de un comprensible «no hay
clave». En cuanto se introduce la clave o termina la instalación, el atributo se activa solo.

De ahí se sigue también que **un modelo `_cli` está activo enseguida**: no necesita clave y no
hay archivos que descargar. La comprobación es una sola —si Claude Code está instalado para el
usuario bajo el que funciona AI2P—, y AI2P no la hace de antemano.

## Cómo añadir un registro propio

El catálogo de la distribución se amplía con las versiones nuevas, pero nada impide crear un
modelo propio: por ejemplo, ese mismo Qwen local en una cuantización menor, o el modelo de un
proveedor que no esté en el catálogo.

1. **Ajustes → Catálogos → Modelos de IA → «Añadir modelo»**.
2. **Nombre**: según el esquema `<nombre común>-<nombre del modelo>`: `Claude-Opus-5.0`,
   `DeepSeek-V4-Pro`. En uno local añada el sufijo `-Local`. El nombre debe ser único.
3. **Ubicación**: «En la nube (API del proveedor)» o «Local (funciona en este ordenador)».
4. **«Perfil…»**: el proveedor (`anthropic`, `openai-compatible`, `comfyui`), la conexión (API o
   agente CLI), el **modelo (id del proveedor)**, la Base URL, la referencia a la clave en los
   secretos, el comando de arranque del servidor local y los parámetros (JSON, por ejemplo
   `{"maxTokens": 32768}`).
5. **«Declaración de capacidades…»**: los formatos de entrada y salida, las habilidades con
   puntuación de 0 a 100, los límites (contexto, máximo de respuesta) y el coste por 1 millón de
   tokens. Con esa declaración funciona la selección automática de ejecutor: **una habilidad que
   no esté en el catálogo de habilidades no la verá nadie**; coja los códigos de la lista del
   formulario, no se los invente.
6. Ponga la clave (si es en la nube) o pulse **«Instalar»** (si es local), y active el atributo
   «activo».
7. **El documento de su modelo** póngalo aquí mismo: `doc/es/models/<nombre del modelo>.md`. Si el
   documento no existe, el botón «i» mostrará una indicación con la ruta completa donde ponerlo.

Los registros propios se marcan como **«Personalizado»** y, a diferencia de los de la
distribución, se eliminan por completo. Los registros de la distribución no se pueden eliminar:
si un modelo no hace falta, basta con quitarle el atributo «activo».

## Qué no hay en los documentos de los modelos

Los identificadores, los precios y los límites del catálogo están cotejados con el catálogo
público de modelos y con un repaso del mercado, pero **ningún identificador se ha comprobado con
una llamada real al proveedor**: el proyecto no tiene claves de todas esas API. Por eso en cada
documento de un modelo en la nube hay una misma línea: antes del primer encargo coteje el id con
una petición `GET /models` a la API del proveedor. El mercado cambia rápido (aproximadamente un
modelo cada dos días), y un registro del catálogo queda obsoleto en silencio: un id cambiado da
un 404 ya en el ejecutor, y un precio incorrecto, una cuenta incorrecta en la facturación.

Los requisitos de equipo de los modelos locales son una **estimación por el tamaño de los
pesos**; no se han hecho mediciones en equipos reales.

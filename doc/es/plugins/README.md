# Complementos y MCP: los documentos de los complementos

El directorio con los documentos de los complementos: un archivo por **código de complemento**
(`tool.ffmpeg.md`). El documento se abre con el botón **«i»** de la fila del complemento:
**Ajustes → Complementos y MCP**.

Un complemento es un registro de la organización más un archivo de manifiesto
`plugins/<código>/plugin.json` en el directorio de datos. El manifiesto describe qué sabe hacer
el complemento (sus acciones, que son las herramientas del agente de IA), de dónde sacar el
programa externo y qué ajustes tiene el registro; el programa en sí y su ruta pertenecen a
**este ordenador** y no se replican.

## Índice de la sección

* [editor.blender](editor.blender.md) — Blender VSE — pasarela `editor.blender`
* [editor.openshot](editor.openshot.md) — OpenShot — pasarela `editor.openshot` (de reserva)
* [editor.resolve](editor.resolve.md) — DaVinci Resolve (OTIO)
* [editor.shotcut](editor.shotcut.md) — Shotcut / Kdenlive (MLT XML)
* [tool.ffmpeg](tool.ffmpeg.md) — Conversor de vídeo (ffmpeg) — complemento `tool.ffmpeg`
* [trainer.musubi](trainer.musubi.md) — Musubi Tuner (LoRA) — el complemento `trainer.musubi`

## Qué tienen en común todos los complementos

**Las acciones son limitadas y con nombre.** Una herramienta del agente hace exactamente un
trabajo con nombre, y sus parámetros viven en el manifiesto y en los ajustes del registro. Las
acciones del tipo «ejecutar una línea de comandos» no existen a propósito: eso es ejecución de
código arbitrario con derecho a escribir archivos, y ninguna regla de seguridad lo limita.

**Una herramienta de complemento la cubre una regla de seguridad**: cada acción tiene su propio
registro en el catálogo de acciones, creado al inicializar el complemento. Una herramienta sin
ese registro no se publica en absoluto.

**Las rutas están limitadas.** Un complemento puede leer y escribir en la carpeta del proyecto
y en los directorios externos abiertos por las reglas de seguridad de la tarea, en ningún sitio
más.

**La instalación es local.** La descripción del complemento se replica a todos los servidores
de la organización, pero el programa encontrado y su ruta viven en el `config.json` del
ordenador donde está instalado.

## Cómo añadir el documento de un complemento nuevo

Ponga aquí un archivo `<código del complemento>.md` — con el código exacto con el que el
complemento está nombrado en el manifiesto (`tool.ffmpeg`). Cree el mismo archivo en todos los
demás idiomas: el conjunto de documentos debe coincidir en todos. El índice de arriba no se
edita a mano: lo arma a partir del directorio el script `test/t18s1/mktoc.py`, que hay que
ejecutar después de añadir el archivo.

Escriba las direcciones externas de estos documentos **completas**, con el esquema `https://`:
el documento se abre con el botón «i» dentro de una página de la aplicación, y allí un enlace
relativo está muerto.

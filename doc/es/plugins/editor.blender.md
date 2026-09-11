# Blender VSE — pasarela `editor.blender`

Monta el vídeo en el **Blender Video Sequence Editor** y se lo entrega al propio Blender:
nosotros generamos un script de Python, Blender lo ejecuta sin ventana y guarda él mismo el
proyecto `.blend`, o renderiza la película terminada.

## Por qué un script y no editar el `.blend`

Un archivo `.blend` es un **volcado binario de las estructuras internas** de Blender con un
bloque DNA (los tipos y desplazamientos de los campos están escritos en el propio archivo y
cambian de versión en versión). El código externo no escribe en él: habría que rehacer el
análisis y el ensamblado para cada versión de Blender.

Hay exactamente un camino práctico, y además es el oficial:

```
blender --background --factory-startup --python ai2p_timeline.py -- ai2p_timeline.blend
```

Nosotros escribimos el script y Blender lo ejecuta y guarda el proyecto por su cuenta. La misma
ejecución también renderiza: basta pedir un resultado con otra extensión.

## Qué hay que instalar

| qué | cómo |
|---|---|
| Blender 3.0 o posterior | Ajustes → Plugins y MCP → plugin `editor.blender` → **Instalar** (archivo portable), o indicar la ruta de un `blender` ya instalado |

El plugin primero **busca `blender` en PATH** (bloque `system` de la entrada del paquete,
comprobado con `--version`, mínimo 3.0): quien hace 3D ya tiene Blender, y descargar al lado
una segunda copia de 386 MiB no tiene sentido. La ruta del programa es un valor **de esta
máquina**: vive en el `config.json` del servidor (`plugins.editor.blender.path`), no en la base
de la organización, y no se replica por el clúster. La descripción del plugin, en cambio, sí se
replica.

Generar el script funciona **sin** Blender instalado: el programa hace falta solo para ejecutarlo.

## Acciones del agente

| herramienta | qué hace |
|---|---|
| `blender_timeline_write` | crea `ai2p_timeline.py` desde la biblioteca multimedia del proyecto: una pista de vídeo y una de audio del VSE, en el orden de `media_list`. Filtros: `scene`, `kind`, `tag`. El nombre del archivo es `file` |
| `blender_render` | ejecuta el script generado en Blender sin ventana. Un `out` con extensión `.blend` (por defecto) da el proyecto con pistas; otra extensión da el vídeo renderizado (mp4, H.264 + AAC) |

Orden de trabajo: los recursos van a la biblioteca (`media_add`), luego
`blender_timeline_write`, luego `blender_render`. El `.blend` de la persona no se toca nunca:
la pasarela solo escribe sus propios `ai2p_timeline.py` y `ai2p_timeline.blend`.

## Seguridad: por qué aquí es especial

Con Blender la limitación por carpeta es **ilusoria**: Python dentro de Blender abre cualquier
archivo con `open()` y ninguna regla de seguridad de AI2P lo ve. Lo que realmente limita no es
la ruta, sino que **el texto del script lo escribimos nosotros**. De ahí tres reglas, y las
tres están cumplidas:

1. **No hay script libre de la IA.** La acción «ejecuta este Python» no existe en el catálogo
   ni existirá. Una acción es nuestra plantilla más sustituciones, y el modelo solo rellena
   parámetros: los filtros de clips y el nombre del archivo.
2. **Las sustituciones se escapan** como literal de cadena de Python. Un nombre de archivo con
   comilla, salto de línea o barra invertida sigue siendo un valor y no se convierte en código.
3. **Solo se ejecuta nuestro archivo.** La acción de render no tiene parámetro «qué ejecutar»
   (`ownFileOnly` en el manifiesto); si lo tuviera, el agente escribiría su propio `.py` con la
   herramienta de escritura de archivos y lo lanzaría con nuestras manos.

Además, la regla general de la rama: se puede escribir en la carpeta del proyecto y en los
directorios abiertos por las reglas de seguridad de la tarea; las rutas dentro del script son
solo relativas (se calculan desde la carpeta del propio script) y el `.blend` guardado las
mantiene relativas (`save_as_mainfile(relative_remap=True)`).

## Limitaciones por sistema operativo

El juego de códecs de las compilaciones de Blender **es distinto**, y eso es lo principal antes
de renderizar.

* **Windows.** El archivo portable `blender-5.2.1-windows-x64.zip` lo instala nuestro paquete
  (404 851 964 bytes, tomado con una petición HEAD el 03.09.2026). La compilación oficial de
  blender.org lleva FFmpeg con H.264 y AAC: el render a mp4 funciona directamente. Verificado
  en vivo con Blender 4.0.2.
* **Linux.** No tenemos paquete: en la publicación hay `blender-5.2.1-linux-x64.tar.xz` y su
  instalación es trabajo aparte. La persona instala el programa e indica la ruta a mano. Una
  compilación **del repositorio de la distribución** (`apt install blender`,
  `dnf install blender`) suele enlazarse con el FFmpeg del sistema, cuyo juego de codificadores
  varía: H.264 y AAC pueden faltar por completo. Si el render falla por códec, use la
  compilación de blender.org, o renderice con la pasarela de Shotcut o el conversor
  `tool.ffmpeg`. Con `--background` Blender no abre ventana, así que no hacen falta variables
  como `QT_QPA_PLATFORM`. **No verificado en vivo.**
* **macOS.** No tenemos paquete: en la publicación solo hay `.dmg`, y el de 5.2.1 es **solo
  arm64**; para Intel hace falta una versión anterior. La ruta se indica a mano. **No
  verificado en vivo.**
* **Común.** Blender hasta la 4.4 llama `sequences` a las piezas del montaje y desde la 4.4
  `strips`; nuestro script entiende ambos nombres, así que funciona en 3.x y en 5.x.

## Qué no hace esta pasarela

* No abre ni modifica el `.blend` de la persona, solo el suyo.
* No hace transiciones, títulos ni etalonaje: el VSE es un editor débil (sin líneas de tiempo
  anidadas, pocas transiciones, casi sin proceso de audio). Use esta pasarela si el proyecto
  **ya tiene 3D** y quiere el montaje en el mismo archivo; si no hay 3D, use Shotcut/Kdenlive
  (`editor.shotcut`): monta mejor y renderiza más fácil.
* No transcodifica el material. Si las piezas tienen formatos distintos, pase antes el
  conversor `tool.ffmpeg`.

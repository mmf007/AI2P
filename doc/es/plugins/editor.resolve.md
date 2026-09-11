# DaVinci Resolve (OTIO)

**Código del plugin:** `editor.resolve`
**Tipo:** pasarela (`gateway`)
**Formato de intercambio:** OTIO — `.otio`, JSON corriente, estándar de la Academy Software Foundation
**Software:** **no lo instalamos** — indique la ruta de un Resolve ya instalado
**Qué hace:** crea una línea de tiempo `.otio` a partir de la biblioteca multimedia del proyecto; **la línea de tiempo la crea la persona**

## Lo principal: el Resolve gratuito no se automatiza con scripts

De aquí se deriva todo lo demás en este documento, por eso va primero.

DaVinci Resolve tiene una API de scripting (`DaVinciResolveScript`), pero **está cerrada en la
versión gratuita**, y desde la versión **19.1 (noviembre de 2024)** lo está definitivamente: el
puente entre procesos dejó de aceptar conexiones. En la versión gratuita los scripts solo se
ejecutan desde la consola interna del propio Resolve, a mano. Un programa externo —y AI2P es
exactamente un programa externo para Resolve— no puede conectarse ni en Windows, ni en Linux,
ni en macOS. La automatización existe únicamente en **Resolve Studio** de pago.

Por eso la cadena de trabajo es esta:

1. el agente llena la biblioteca multimedia del proyecto (`media_add`, `media_list`);
2. el agente llama a `resolve_timeline_write` y **escribe el archivo `ai2p_scenes.otio`** en la
   carpeta del proyecto;
3. **la persona abre Resolve e importa el archivo con el ratón:**
   `File → Import → Timeline → OTIO` (en versiones antiguas
   `File → Import Timeline → AAF, EDL, XML…`, con `.otio` en la misma lista de formatos).

La línea de tiempo **no aparece sola**. Si el informe de la tarea dice «la línea de tiempo se
creó en Resolve», es falso: lo que se creó es un archivo que la persona todavía debe importar.

Nunca escribimos dentro del proyecto de Resolve: es una base de datos cerrada (PostgreSQL o su
propio formato), y no se entra en ella desde fuera.

## La acción del agente

| Herramienta | Qué hace |
|---|---|
| `resolve_timeline_write` | crear `ai2p_scenes.otio` desde la biblioteca multimedia del proyecto |

Parámetros (todos opcionales):

| Campo | Significado |
|---|---|
| `scene` | tomar solo los recursos de esta escena |
| `kind` | tomar solo este tipo de medio (`video`, `audio`, `image`, `subtitle`, `project`) |
| `tag` | tomar solo los recursos con esta etiqueta |
| `file` | dónde dejar el archivo; por defecto `ai2p_scenes.otio` en la raíz de la carpeta del proyecto |

El orden de los clips es **el mismo que imprime `media_list`**: escena, `order`, `take`, código
del objeto. Quien haya leído esa lista debe ver exactamente eso en Resolve.

Reparto por pistas:

| Pista OTIO | Tipo de pista | Qué va allí |
|---|---|---|
| `V1` | `Video` | `video`, `image`, `project` |
| `A1` | `Audio` | `audio` |

Un recurso sin duración conocida (una imagen, un rótulo) recibe **5 segundos**, no cero: un clip
de longitud cero es un plano perdido en silencio.

**Los subtítulos (`subtitle`) no viajan en el `.otio`:** OTIO no tiene una pista para ellos. El
`.srt` se añade en Resolve aparte (`File → Import → Subtitle`).

## Software: no se puede instalar, solo se puede señalar

Este plugin no tiene botón «Instalar» **a propósito**. El paquete de DaVinci Resolve pesa
3–4 GB y se descarga del sitio de Blackmagic Design **tras un formulario de registro**; no
existe un enlace directo permanente que pudiera ponerse en el catálogo de paquetes. No podemos
descargarlo por usted y no lo intentamos.

Qué hacer: instale Resolve usted mismo e indique la ruta en el formulario del plugin —
**Ajustes → Plugins y MCP → DaVinci Resolve (OTIO)**. Sirve tanto el archivo del programa como
la carpeta donde lo instaló:

| Sistema | Qué indicar |
|---|---|
| Windows | `C:\Program Files\Blackmagic Design\DaVinci Resolve\Resolve.exe`, o esa carpeta |
| Linux | `/opt/resolve/bin/resolve`, o `/opt/resolve` |
| macOS | `/Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/MacOS/DaVinci Resolve` |

La ruta vive **en el `config.json` de este servidor** y no se replica nunca: en la máquina
vecina Resolve está en otro sitio, y la mitad de los servidores no lo tienen en absoluto.

Solo se comprueba **la existencia del ejecutable**: Resolve no informa de su versión por línea
de comandos, así que el plugin no declara ningún rango de versiones válidas.

Mientras no se indique la ruta y el programa no se encuentre, el estado del plugin en este
servidor es **«buscando el software»** y sus acciones no se publican al agente: una herramienta
que con seguridad responderá con un rechazo desperdiciaría el turno del agente. Ese es el
rechazo «software no encontrado en este servidor», y se cura con una línea en el formulario.

## Limitaciones por sistema operativo

| Sistema | Qué importa |
|---|---|
| **Windows** | El Resolve gratuito lee H.264/H.265 en `.mp4`/`.mov`. El audio AAC **no está soportado** (véase abajo) — en ninguna versión ni sistema. El programa suele estar en `C:\Program Files\Blackmagic Design\DaVinci Resolve\Resolve.exe`. |
| **Linux** | La compilación gratuita **no decodifica H.264 ni H.265**: no incluye los códecs licenciados. Nuestras generaciones (fal.ai, ComfyUI) son justamente `mp4/H.264`, así que sin transcodificar obtendrá una línea de tiempo donde todos los clips dicen «Media Offline». Sí funcionan DNxHR (`.mov`), ProRes (`.mov`), CinemaDNG y WAV sin comprimir para el sonido. El programa está en `/opt/resolve/bin/resolve`. |
| **macOS** | H.264/H.265 se leen (el decodificador es del sistema). AAC sigue sin soporte. |
| **Todos los sistemas** | **AAC no está soportado ni siquiera en Resolve Studio de pago.** El sonido de nuestros clips y generaciones (`aac`, `mp3`) debe convertirse a WAV. |

Aparte: Resolve lee OTIO de serie **a partir de la versión 18.5**, también en la versión
gratuita. Las compilaciones anteriores no tienen la opción de importar OTIO — allí hay que
actualizar.

## Enlace con el plugin conversor de vídeo (ffmpeg)

La transcodificación es un **paso explícito**, no magia silenciosa dentro de la exportación: el
agente ve lo que hace y usted ve lo que salió. El orden de trabajo antes de crear el `.otio`:

1. `ffmpeg_to_edit` — pasar cada clip al formato intermedio de montaje (por defecto DNxHR HQ en
   contenedor MOV con audio sin comprimir);
2. `ffmpeg_extract_audio` — extraer el sonido a WAV si hace falta como pista aparte;
3. `media_add` — poner los archivos resultantes en la biblioteca multimedia (con su escena,
   orden y toma);
4. `resolve_timeline_write` — crear `ai2p_scenes.otio`.

Si el plugin conversor (ffmpeg) no está configurado, o ffmpeg no se encuentra en este servidor,
los pasos 1–2 rechazan con un texto claro («el programa del plugin no se encontró en este
servidor»), y eso hay que arreglarlo antes de crear la línea de tiempo, no después: el `.otio`
se creará incluso con material inservible —solo referencia archivos—, pero no habrá con qué
abrirlo.

En Windows y macOS el paso de transcodificación es opcional pero útil: DNxHR se desplaza cuadro
a cuadro, mientras que H.264 de GOP largo va a tirones.

## Qué no transporta OTIO

OTIO es un formato de **intercambio de la decisión de montaje**, no un formato de proyecto.
Transporta:

* el conjunto de clips y las referencias a los archivos;
* el orden de los clips y sus duraciones;
* el reparto por pistas;
* nuestras notas en `metadata.ai2p` — escena, toma, orden, pie y tipo de medio.

**No** transporta efectos, transiciones, etalonaje, cambios de velocidad, composición Fusion ni
la mezcla de Fairlight. El viaje de ida y vuelta es **con pérdida**: exporte una línea de tiempo
de Resolve a `.otio` y devuélvala, y todo el trabajo de acabado habrá desaparecido.

Conclusión práctica: nuestro `.otio` es un **montaje en bruto** (orden y duración). El acabado
lo hace la persona dentro de Resolve, y volver a importar nuestro archivo sobre su trabajo no es
aceptable: impórtelo como una línea de tiempo nueva.

## Ajustes del registro del plugin

**Ajustes → Plugins y MCP → DaVinci Resolve (OTIO) → «Configurar»:**

| Ajuste | Por defecto | Significado |
|---|---|---|
| Cuadros por segundo | 25 | frecuencia de la línea de tiempo; a ella se convierten las duraciones |
| Ancho del cuadro | 1920 | tamaño del proyecto, va a `metadata` |
| Alto del cuadro | 1080 | lo mismo |

Los ajustes son propiedad del **registro de la organización** y se replican: acordar montar a
25 cuadros es un acuerdo para todo el clúster. La ruta al programa, en cambio, es propia de cada
servidor.

## Rutas y seguridad

* **Todas las rutas dentro del `.otio` son relativas** (campo `target_url`). Una ruta absoluta
  rompe la portabilidad: en la máquina vecina ese mismo clip está en otro sitio.
* La ruta del clip se calcula **desde la carpeta del archivo de salida**.
* Un recurso del almacén de la organización (`store:`) está fuera de la carpeta del proyecto,
  así que la exportación lo **copia** al subdirectorio `ai2p_media/` junto a la línea de tiempo:
  de otro modo la referencia sería absoluta o contendría `..`.
* El agente escribe **solo su propio archivo** `ai2p_scenes.otio`. El proyecto de Resolve y
  cualquier archivo de la persona no se tocan nunca.
* Se puede escribir **en la carpeta del proyecto**, y en carpetas externas si las reglas de
  seguridad de la tarea las abrieron. El límite funciona literalmente: se comprueban tanto la
  ruta del archivo como cada ruta escrita dentro de él.

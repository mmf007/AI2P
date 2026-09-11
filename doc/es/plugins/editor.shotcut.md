# Shotcut / Kdenlive (MLT XML)

**Código del plugin:** `editor.shotcut`
**Tipo:** pasarela (`gateway`)
**Formato de intercambio:** MLT XML — un mismo archivo se abre en Shotcut (`.mlt`) y en Kdenlive
**Software:** lo instalamos nosotros (Shotcut portátil) o se toma un `melt` ya instalado
**Qué hace:** monta una línea de tiempo a partir de la biblioteca multimedia del proyecto y **calcula con ella un vídeo terminado sin abrir ninguna ventana**

## Por qué esta es la primera opción de las cuatro pasarelas

De los cuatro editores de esta rama (Shotcut/Kdenlive, DaVinci Resolve, Blender VSE, OpenShot)
solo este tiene **renderizado sin ventana y sin persona**: el programa `melt` que viene con MLT
lee el archivo que hemos montado y escribe un `mp4` terminado. Resolve y OpenShot no tienen ese
camino en absoluto; Blender sí lo tiene, pero ejecutando un script de Python nuestro.

Por eso aquí la cadena se cierra: el agente montó la línea de tiempo → el agente calculó el
vídeo → la persona recibió el resultado como archivo. Ni un solo paso con el ratón.

El segundo argumento: **un archivo para dos editores**. MLT XML es el formato nativo de Shotcut
y además lo lee Kdenlive (`Proyecto → Abrir`). No hizo falta una pasarela aparte para Kdenlive.

## Acciones del agente

| Herramienta | Qué hace |
|---|---|
| `shotcut_timeline_write` | montar `ai2p_library.mlt` a partir de la biblioteca multimedia del proyecto |
| `shotcut_render` | calcular el vídeo según la línea de tiempo montada con el programa `melt` |

Parámetros de `shotcut_timeline_write` (todos opcionales):

| Campo | Significado |
|---|---|
| `scene` | tomar solo los recursos de esta escena |
| `kind` | tomar solo este tipo de medio (`video`, `audio`, `image`, `subtitle`, `project`) |
| `tag` | tomar solo los recursos con esta etiqueta |
| `file` | dónde dejar el archivo; por omisión, `ai2p_library.mlt` en la raíz de la carpeta del proyecto |

Parámetros de `shotcut_render`:

| Campo | Significado |
|---|---|
| `file` | qué renderizar; por omisión, `ai2p_library.mlt` |
| `out` | dónde dejar el resultado; extensión `.mp4`, códecs H.264 + AAC |

**El renderizado no tiene línea de comandos propia.** Los argumentos de `melt` están fijados por
el manifiesto del plugin y el agente solo pasa dos rutas. En el sistema no hay ninguna acción
del tipo «ejecutar una orden cualquiera», y es a propósito: eso es ejecutar código ajeno con
derecho a escribir archivos, y una regla de seguridad no lo puede acotar.

El orden de los clips es **el mismo que imprime `media_list`**: escena, orden (`order`), toma
(`take`), código del objeto. Quien haya mirado la lista tiene que ver en el editor exactamente
esa lista.

Reparto por pistas:

| Pista | Qué va en ella |
|---|---|
| `V1` | `video`, `image`, `project` |
| `A1` | `audio` (la pista lleva la marca `hide="video"`) |

Un recurso sin duración conocida (una imagen, un rótulo) recibe **5 segundos**, no cero: un clip
de longitud cero es un plano que desaparece en silencio.

## Software

El plugin primero **busca el programa en este equipo**: `melt`, `qmelt`, `melt.exe`,
`qmelt.exe` en el `PATH`, comprobación con la clave `--version`, versión mínima válida **7.0**.
Si lo encuentra, lo toma tal cual; si no, el botón **«Instalar»** descarga un Shotcut portátil y
lo descomprime él mismo. El tercer camino es indicar la ruta a mano en el formulario del plugin
(**Ajustes → Complementos y MCP → Shotcut / Kdenlive (MLT XML)**); sirve tanto el archivo del
programa como la carpeta donde está instalado.

En la distribución de Shotcut para Windows el programa se llama **`melt.exe`**, no `qmelt.exe`:
hay que buscar los dos nombres. En las compilaciones de Linux y en algunas de macOS se llama
`qmelt`.

La ruta del programa se guarda **en el `config.json` de este servidor** y no se replica nunca:
en el equipo vecino del clúster Shotcut está en otra ruta, y la mitad de los servidores no lo
tienen en absoluto. La descripción del plugin, en cambio, se replica a toda la organización.

Mientras el programa no aparezca, el estado del plugin en este servidor es **«buscando el
software»** y sus acciones no se publican al agente: una herramienta que de antemano va a
responder con una negativa gastaría el turno del agente para nada. Eso es justamente la negativa
«programa no encontrado en este servidor».

## Limitaciones por sistema operativo

| Sistema | Qué conviene saber |
|---|---|
| **Windows** | Funciona sin reservas. El programa de renderizado se llama `melt.exe` y está junto a `shotcut.exe` en la carpeta de instalación. La variable de entorno `QT_QPA_PLATFORM` no hace falta y no se define. |
| **Linux** | En un servidor **sin pantalla** `melt` no arranca en absoluto: falla con «could not connect to display», porque es un programa Qt. Por eso el plugin lo lanza con `QT_QPA_PLATFORM=offscreen`; la variable se define **solo en Linux** (`envOs` en el manifiesto). Si llama a `melt` a mano, defínala usted. En las compilaciones de las distribuciones el programa suele llamarse `melt`; en la de Shotcut, `qmelt`. |
| **macOS** | El programa está **dentro del paquete de la aplicación**: `/Applications/Shotcut.app/Contents/MacOS/qmelt`. No está en el `PATH`, así que la búsqueda automática casi nunca encuentra nada y la ruta se indica a mano. Las compilaciones de Homebrew (`brew install mlt`) dejan un `melt` en el `PATH` y también sirven. |
| **Todos los sistemas** | El juego de codificadores depende de la compilación de MLT. Nuestros valores por omisión — `libx264` para vídeo y `aac` para sonido — están en la distribución de Shotcut en los tres sistemas, pero una compilación recortada del sistema puede no tenerlos; entonces el renderizado se niega con el texto del propio `melt` y el material hay que prepararlo con el plugin `tool.ffmpeg`. |
| **Kdenlive** | El archivo se abre, pero Kdenlive **lo recalcula a su propio perfil de proyecto** al abrirlo. Si la frecuencia de nuestra línea de tiempo no coincide con el perfil de Kdenlive, las duraciones de los clips se desplazan: mantenga el ajuste «Fotogramas por segundo» igual en ambos lados. |

## Ajustes del registro del plugin

**Ajustes → Complementos y MCP → Shotcut / Kdenlive (MLT XML) → «Configurar»:**

| Ajuste | Por omisión | Significado |
|---|---|---|
| Fotogramas por segundo | 25 | frecuencia de la línea de tiempo; a ella se recalculan también las duraciones de los clips |
| Ancho del fotograma | 1920 | tamaño del perfil MLT |
| Alto del fotograma | 1080 | lo mismo |

Los ajustes son propiedad del **registro de la organización** y se replican: si se ha acordado
montar a 25 fotogramas, se ha acordado en todo el clúster. La ruta del programa, en cambio, es
propia de cada servidor.

## Rutas y seguridad

* **Todas las rutas dentro del `.mlt` son solo relativas** (la propiedad `resource`). Una ruta
  absoluta mata la portabilidad: en el equipo vecino del clúster ese mismo vídeo está en otro
  sitio y la línea de tiempo se abre vacía.
* La ruta del clip se cuenta **desde la carpeta del archivo de salida**.
* Un recurso del almacén de la organización (`store:`) está fuera de la carpeta del proyecto,
  así que al exportar se **copia** al subdirectorio `ai2p_media/` junto a la línea de tiempo: de
  lo contrario la referencia sería absoluta o llevaría `..`.
* El agente escribe **solo su propio archivo** `ai2p_library.mlt`. El proyecto de la persona no
  se toca nunca: puede tenerlo abierto en el editor, y guardar desde el editor borraría nuestra
  escritura (o al revés). La línea de tiempo terminada la importa la persona.
* Se puede escribir **en la carpeta del proyecto** y, si las reglas de seguridad de la tarea han
  abierto carpetas externas, también en ellas. Se comprueban tanto la ruta del propio archivo
  como cada ruta que hay dentro.

## Enlace con el plugin conversor de vídeo (ffmpeg)

MLT juntará material de distinto tamaño y distinta frecuencia, pero recalculándolo sobre la
marcha, es decir, despacio y no siempre con exactitud. Sale más barato prepararlo antes:

1. `ffmpeg_uniform` — un tamaño de fotograma común y una frecuencia común;
2. `ffmpeg_extract_audio` — sacar el sonido a WAV, si hace falta como pista aparte;
3. `media_add` — poner los archivos obtenidos en la biblioteca multimedia;
4. `shotcut_timeline_write` y `shotcut_render`.

La recodificación es un **paso explícito**, no magia silenciosa dentro de la exportación: el
agente ve lo que hace y usted ve lo que ha salido.

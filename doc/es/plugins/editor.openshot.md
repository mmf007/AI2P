# OpenShot — pasarela `editor.openshot` (de reserva)

Crea un **proyecto de OpenShot** `.osp` a partir de la biblioteca multimedia del proyecto. El
formato `.osp` es JSON puro en **UTF-8** (desde OpenShot 2.0), así que el archivo se escribe
directamente: no hace falta ejecutar código ni importar a mano un formato ajeno — la persona
abre el archivo listo con un doble clic.

## Por qué esta pasarela es la de reserva

Por **criterios formales** OpenShot es el mejor de los cuatro candidatos de la rama:
multiplataforma, gratuito, formato de proyecto abierto, escritura automática, y el archivo se
abre entero y de una vez.

Por **fiabilidad** es peor, y conviene decirlo en voz alta:

* **No tiene render sin ventana en absoluto.** `openshot-qt` calcula la película solo con su
  propia ventana; Shotcut/Kdenlive tienen `melt` para eso y la película se renderiza sin
  persona. Por eso el manifiesto de este complemento no tiene bloque `render` ni una segunda
  acción: prometer un render que no existe es peor que no tenerlo.
* **La fama de estabilidad de OpenShot es floja** — los fallos en proyectos largos se conocen
  desde hace años.

**La primera opción para el montaje es `editor.shotcut` (Shotcut / Kdenlive).** Tome OpenShot
si la persona trabaja precisamente en él, o si Shotcut no está instalado en esa máquina.

## Qué hay que instalar

| qué | cómo |
|---|---|
| OpenShot 2.0 o superior | instálelo usted e indique la ruta: Ajustes → Complementos y MCP → complemento `editor.openshot` → campo de la ruta |

**Aquí no hay botón «Instalar», y es a propósito.** En Windows OpenShot se entrega solo como el
instalador `OpenShot-v4.0.0-x86_64.exe` (229 078 328 bytes, publicado el 30-08-2026,
https://github.com/OpenShot/openshot-qt/releases), el proyecto no tiene ningún archivo portable
y nuestro instalador de paquetes descomprime archivos comprimidos en lugar de ejecutar
instaladores ajenos. Por eso el código de paquete del manifiesto está vacío y el complemento
muestra el campo «indique dónde ya está instalado».

Puede indicar tanto el archivo del programa como la carpeta donde lo instaló. OpenShot no
informa de su versión por línea de comandos, así que solo se comprueba la **existencia del
ejecutable**: `openshot-qt.exe` (Windows), `openshot-qt` o el AppImage (Linux), la aplicación
del `.dmg` (macOS).

La ruta al programa es un valor de **este ordenador**: vive en el `config.json` del servidor
(`plugins.editor.openshot.path`) y no se replica por el clúster. La descripción del complemento,
al contrario, sí se replica.

**Importante y poco evidente:** mientras la ruta no esté indicada, el estado del complemento en
este servidor es «buscando el software» y la acción **no se publica** al agente, aunque crear el
`.osp` funcionaría sin OpenShot instalado (el archivo lo escribimos nosotros). Es una regla
general del núcleo de complementos, no una peculiaridad de esta pasarela; `editor.resolve` se
comporta igual.

## Acciones del agente

| herramienta | qué hace |
|---|---|
| `openshot_timeline_write` | crear `ai2p_library.osp` a partir de la biblioteca multimedia del proyecto: una pista de vídeo (capa `L2`, número 2000000) y una de audio (capa `L1`, número 1000000), en el orden de `media_list`. Filtros: `scene`, `kind`, `tag`. El nombre del archivo es `file` |

El orden de trabajo: los recursos entran en la biblioteca multimedia (`media_add`), luego
`openshot_timeline_write`, y después una **persona** abre el archivo en OpenShot y, si hace
falta la película, lanza la exportación ella misma.

El archivo de proyecto de la persona nunca se toca: solo se escribe nuestro `ai2p_library.osp`.

## Cómo está hecho el archivo generado

`.osp` se diferencia de MLT y de OTIO en que su lista de clips es **plana**:

* `files` — la lista de recursos: ruta, tipo de medio, tipo de lector (`FFmpegReader` para vídeo
  y audio, `QtImageReader` para imágenes), duración y los indicadores `has_video` / `has_audio`
  / `has_single_image`;
* `clips` — **un único array para todo el archivo**, y cada clip indica su pista y su lugar con
  `layer` (número de la capa de `layers`) y `position` (segundos desde el inicio). Aquí no hay
  pista-secuencia como en MLT, así que el inicio de cada clip lo calculamos nosotros;
* `layers` — cinco capas, dos de ellas etiquetadas (`A1` y `V1`);
* `profile` — el nombre del perfil de OpenShot como cadena (`FHD PAL 1080p 25 fps` para
  1920×1080 a 25 fps); el cuadro y la frecuencia vienen de los ajustes del registro del
  complemento.

La escena, la toma y el rótulo del recurso van a `metadata` de la entrada del archivo
(`ai2p_scene`, `ai2p_take`, `ai2p_caption`) — OpenShot no los muestra, pero al leer el archivo a
ojo indican de dónde viene el clip.

## Rutas y seguridad

**Las rutas son solo relativas** — y esa es la forma nativa del formato: el propio OpenShot
guarda el proyecto con rutas relativas y las expande al abrirlo, desde la carpeta del `.osp`.
Una ruta absoluta rompería la portabilidad en el clúster: en otra máquina el mismo clip está en
otro sitio y el proyecto se abriría con todos los clips como «archivo no encontrado». La clave
en JSON se llama exactamente `path` y aparece **dos veces**: en la entrada de `files` y en el
`reader` del clip.

La ruta del clip se calcula **desde la carpeta del archivo de salida**, no desde la carpeta del
proyecto. Un recurso del almacén de la organización (`store:`) está fuera de la carpeta del
proyecto, así que se copia a la subcarpeta `ai2p_media` junto al `.osp`; lo mismo se hace con
cualquier recurso que de otro modo habría que direccionar con `..`.

La limitación por carpetas funciona **literalmente**: escribimos JSON y no ejecutamos código,
así que se comprueban tanto la ruta del propio archivo como las rutas de su interior. Se puede
escribir en la carpeta del proyecto y en las carpetas abiertas por las reglas de seguridad de la
tarea — en ningún otro sitio; una ruta hacia fuera da un rechazo claro y no aparece archivo
alguno.

## Limitaciones por sistema operativo

Crear un `.osp` es escribir un archivo de texto, y eso es **igual en todos los sistemas**. Lo
único que cambia es cómo se consigue OpenShot en cada uno.

* **Windows.** Solo el instalador `OpenShot-v4.0.0-x86_64.exe` (229 078 328 bytes); también hay
  uno de 32 bits, `OpenShot-v4.0.0-x86.exe` (224 873 009 bytes). No hay archivo portable y
  nuestro paquete no puede instalarlo. Tras la instalación el ejecutable es `openshot-qt.exe`.
  **No comprobado en vivo: OpenShot no está instalado en esta máquina.**
* **Linux.** `OpenShot-v4.0.0-x86_64.AppImage` (254 457 024 bytes) o el paquete de la
  distribución (`apt install openshot-qt`). El AppImage se ejecuta como un archivo normal:
  apunte a él. OpenShot necesita su ventana siempre: el programa no tiene modo sin pantalla, y
  justamente por eso no hay render sin ventana. **No comprobado en vivo.**
* **macOS.** `OpenShot-v4.0.0-x86_64.dmg` (292 188 716 bytes). La ruta se indica a mano.
  **No comprobado en vivo.**
* **Común.** Las cifras se tomaron con una consulta a la lista de versiones de GitHub el
  03-09-2026 (versión v4.0.0 del 30-08-2026, `libopenshot` 1.0.0). El bloque `version` del
  archivo generado no es adorno: al abrirlo, OpenShot ejecuta según él sus pasos de
  actualización de proyectos antiguos.

## Qué no hace esta pasarela

* **No renderiza.** La película se calcula desde el `.osp` por una persona que abre el proyecto.
  Si hace falta la película sin persona, tome `editor.shotcut` (tiene `melt`) o el conversor
  `tool.ffmpeg`.
* **No edita el proyecto de la persona** — solo escribe su propio `ai2p_library.osp`.
* **No traslada transiciones, efectos ni corrección de color** — solo la distribución de los
  recursos en dos pistas con sus duraciones.
* **No transcodifica el material.** Si las piezas tienen formatos distintos, pase antes el
  conversor `tool.ffmpeg`.
* **No usa `libopenshot`.** Junto al editor existe la biblioteca `libopenshot` con enlaces para
  Python, C++ y Ruby — con ella se podría tanto armar el proyecto como renderizarlo sin ventana.
  Queda fuera del alcance de este trabajo: es una dependencia externa aparte con su propia
  instalación, mientras que el formato `.osp` lo escribimos directamente y sin ella.

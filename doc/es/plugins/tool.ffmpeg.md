# Conversor de vídeo (ffmpeg) — complemento `tool.ffmpeg`

**Qué hace:** prepara el material para el montaje — transcodifica los clips al formato
intermedio de montaje, extrae el audio, une piezas por lista y las normaliza a un cuadro y una
frecuencia comunes.

**Para qué hace falta.** Nuestras generaciones son mp4/H.264 con audio aac/mp3 (fal.ai,
ComfyUI, `SaveAudioMP3`). El DaVinci Resolve gratuito en Linux **no decodifica H.264/H.265 en
absoluto**, AAC no está soportado ni en la versión Studio de pago, y los juegos de
codificadores de las compilaciones de Blender son distintos. El conversor hace que el enlace
«generación → montaje» no dependa de lo que sepa hacer una compilación concreta del editor en
un sistema operativo concreto: antes del montaje el material pasa por ffmpeg.

---

## Acciones

Las acciones son **limitadas y con nombre**. Cada una está descrita entera en el manifiesto del
complemento: las opciones de ffmpeg llegan en el archivo de la distribución, los parámetros de
trabajo vienen de los ajustes del registro, y el agente de IA aporta **solo rutas**, y cada una
se comprueba.

| Herramienta del agente | Qué hace | Qué recibe | Qué sale |
|---|---|---|---|
| `ffmpeg_to_edit` | Transcodificar al formato intermedio de montaje | `in` — el archivo, `out` — a dónde (opcional) | `<nombre>_edit.mov`: DNxHR o ProRes con audio sin comprimir |
| `ffmpeg_extract_audio` | Extraer el audio | `in`, `out` | `<nombre>_audio.wav` sin comprimir |
| `ffmpeg_concat` | Unir por lista | `files` — lista de rutas, o un filtro de la biblioteca multimedia (`scene`, `kind`, `tag`), `out` | `ai2p_concat.<extensión de los orígenes>` |
| `ffmpeg_uniform` | Normalizar cuadro y frecuencia | `in`, `out` | `<nombre>_uniform.mov` del tamaño y la frecuencia pedidos |

**No existe la acción «ejecutar una línea de comandos de ffmpeg» y nunca existirá.** Eso es
ejecución de código arbitrario con derecho a escribir archivos, y ninguna regla de seguridad lo
limita: ffmpeg tiene `-f lavfi`, los protocolos `file:`, `concat:` y `tcp:`, y la opción `-y`
sobre cualquier archivo. Pedirle al agente esa orden no sirve de nada: no existe. Si hace falta
otro perfil de trabajo, hay que cambiar el **ajuste del registro del complemento**.

### El orden habitual

1. `ffmpeg_uniform` en cada pieza — tamaño de cuadro y frecuencia comunes.
2. `ffmpeg_concat` — la unión. Va **sin recodificar** (copia de flujos), así que las piezas
   deben compartir formato; con piezas de formatos distintos la unión no funciona.
3. `ffmpeg_to_edit` — el formato intermedio para montar en el editor.
4. `ffmpeg_extract_audio` — si el audio hace falta como pista aparte.

---

## Ajustes del registro

Se fijan en **«Ajustes → Complementos y MCP»**, en el formulario del registro; desde el texto
de la tarea no se pueden cambiar.

| Clave | Por defecto | Qué significa |
|---|---|---|
| `videoCodec` | `dnxhd` | códec intermedio: `dnxhd` (DNxHR) o `prores_ks` (ProRes) |
| `videoProfile` | `dnxhr_hq` | perfil: `dnxhr_lb`/`sq`/`hq`/`hqx` para DNxHR, `0`–`3` para ProRes |
| `pixelFormat` | `yuv422p` | formato de píxel |
| `audioCodec` | `pcm_s16le` | códec de audio (PCM sin comprimir) |
| `audioRate` | `48000` | frecuencia de muestreo, Hz |
| `width`, `height` | `1920`, `1080` | tamaño de cuadro común para `ffmpeg_uniform` |
| `fps` | `25` | frecuencia de cuadros común |

**DNxHR es el valor por defecto y no ProRes** porque el codificador ProRes de ffmpeg
(`prores_ks`) en Windows y Linux produce archivos que Resolve no siempre lee, mientras que
DNxHR es el formato nativo de Avid y lo aceptan todos los editores de esta rama.

**El par «perfil ↔ formato de píxel» está ligado:** `dnxhr_hq` exige `yuv422p`, y `prores_ks`
con perfil 3 exige `yuv422p10le`. La discrepancia la rechaza el propio ffmpeg, y el rechazo lo
ve el agente en la respuesta de la herramienta.

---

## Dónde se escriben los archivos y qué está prohibido

* Por defecto el resultado queda **junto al original** (el material vive en carpetas por
  escena, y un archivo que se va a la raíz del proyecto hay que buscarlo a mano); su nombre
  recibe el sufijo `_edit`, `_audio` o `_uniform`.
* La ruta de entrada y la de salida valen **solo dentro de la carpeta del proyecto** o dentro
  de un directorio externo abierto por las **reglas de seguridad de la tarea**. Todo lo demás
  se rechaza, antes de arrancar el programa.
* El resultado **nunca se pone encima del original**: ffmpeg con `-y` lo sobrescribiría con un
  archivo vacío antes incluso de leerlo, y perder material en silencio es peor que un rechazo.
* Las reglas de seguridad de la tarea también rigen aquí: un subdirectorio cerrado a la lectura
  está cerrado también para la transcodificación.

## Una conversión larga

El conversor se ejecuta como **proceso aparte**, así que no cuelga la tarea. El avance se anota
en el registro de llamadas cada 15 segundos («procesados N s de material»), y cuando se agota
el tiempo de espera (2 horas por operación por defecto) el proceso se detiene y el agente
recibe un rechazo claro que dice cuánto material llegó a procesarse. Eso importa: con un simple
«no terminó» no se distingue un programa colgado de un trabajo honestamente largo.

---

## Limitaciones por sistema operativo

Las compilaciones de ffmpeg **se diferencian en su juego de codificadores**, y no es un
detalle: los códecs no libres y los GPL faltan por completo en algunas. Nuestras cuatro
operaciones se apoyan en codificadores presentes en **cualquier** compilación (`dnxhd`,
`prores_ks`, `pcm_s16le`/`pcm_s24le`) y en los decodificadores internos de H.264/H.265/AAC, así
que el conversor funciona también en una compilación recortada. Si alguna vez hiciera falta
exportar de vuelta a H.264/H.265, se necesita una compilación con `libx264`/`libx265` (licencia
GPL): en las LGPL no están. El juego de codificadores se ve con `ffmpeg -encoders`; un
codificador ausente lo nombra el propio ffmpeg («Unknown encoder») y el rechazo llega al
agente.

**Windows.** El programa lo instala nuestro paquete `ffmpeg` con el botón «Instalar»: la
compilación `ffmpeg-9.0.1-essentials_build.zip` de https://www.gyan.dev/ffmpeg/builds/
(111 253 802 bytes, con `ffmpeg.exe` y `ffprobe.exe` dentro). Comprobado en vivo: versión
9.0.1, todos los codificadores necesarios en su sitio. El enlace lleva versión — uno móvil
significaría que cada cual tiene la suya.

**Linux.** No tenemos paquete: ffmpeg se instala con el sistema — `apt install ffmpeg`
(Debian, Ubuntu), `dnf install ffmpeg` (Fedora, hace falta RPM Fusion). Aquí acecha la
**versión del repositorio**: Debian 12 trae la 5.1 y el complemento necesita la **6.0 o
superior**, así que una 5.1 encontrada la rechaza la comprobación de versión — entonces
instale una compilación reciente (por ejemplo la estática de
https://johnvansickle.com/ffmpeg/) e indique la ruta a mano en el formulario del complemento.
En Linux no se comprobó en vivo.

**macOS.** No tenemos paquete: `brew install ffmpeg`, después la ruta se encuentra sola o se
indica a mano. No se comprobó en vivo.

**La comprobación de versión es obligatoria en cualquier sistema.** El juego de opciones de
ffmpeg cambió notablemente entre la 4.x y la 7.x, y un programa viejo encontrado en silencio
rompería el trabajo ya durante la tarea, mientras que la causa se buscaría en el complemento.

---

## Cómo se encuentra el programa

1. **La ruta manual**, si está puesta en el formulario del complemento (`ffmpegPath` en el
   `config.json` de este servidor) — vale tanto el archivo del programa como el directorio
   donde está.
2. **La búsqueda en este servidor**: la orden `ffmpeg` en PATH, la versión se pregunta con
   `-version` y debe ser 6.0 o superior.
3. **La instalación con el paquete** `ffmpeg` — el botón «Instalar» del formulario (Windows).

La ruta encontrada **no llega nunca a la base de la organización**: vive en el `config.json` de
este ordenador. La descripción del complemento se replica a todos los servidores de la
organización, pero la instalación siempre es local: cada servidor tiene su propia respuesta a
«¿está instalado aquí?».

Mientras el programa no se encuentre, el complemento **no publica acción alguna**: no hay con
qué transcodificar, y enseñarle al agente una herramienta que va a negarse seguro es
malgastarle el turno.

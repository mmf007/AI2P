# Trabajo con modelos de audio

**Para qué sirve este capítulo:** obtener una escena sonora de varias partes —música, una
canción, el habla de personas concretas, un ruido— y entender qué modelos hay para ello, qué
muestras de sonido necesitan y qué no saben hacer.

Lo vemos con una escena de ejemplo:

> Suena música country tranquila, canta el vocalista **X**. Dos personas, **Y** y **Z**,
> comentan la vista desde una montaña sobre un bosque en llamas. Sobre sus cabezas pasa un
> helicóptero de bomberos.

## 1. La regla principal: una tarea, una capa de sonido

Ningún modelo de audio del catálogo compone una escena así de una vez:

* un modelo de música canta, pero no habla con las voces de personas concretas;
* un modelo de voz habla, pero no toca música ni canta;
* un modelo de voz con muestra toma **una** muestra por trabajo: un diálogo de dos voces en un
  mismo trabajo no sale;
* en el catálogo no hay modelo de efectos de sonido;
* AI2P no superpone pistas: no hay mezcla ni en los conectores ni en el complemento
  `tool.ffmpeg`.

Por eso la escena se divide en **capas**; cada capa es una tarea aparte (o varias), y los
archivos terminados se mezclan en un editor de audio o de vídeo.

| Capa | Habilidad de la tarea | Modelos del catálogo | ¿Hace falta muestra de sonido? |
|---|---|---|---|
| música country con el vocalista X | `audio-song` | ACE-Step-1.5-XL-Turbo / -Base / -SFT (local), ElevenLabs-Music (nube) | no, y no hay dónde pasarla (sección 3) |
| frases de Y | `audio-speech` | Chatterbox-TTS, Zonos-2-TTS (por muestra), ElevenLabs-TTS-v3 (voces predefinidas) | sí, una grabación de la voz de Y (sección 4) |
| frases de Z | `audio-speech` | los mismos | sí, una grabación de la voz de Z |
| el paso del helicóptero | — | no hay modelo | sí, una grabación ya hecha (sección 5) |

## 2. Modelos de audio del catálogo

Habilidades: `audio-song` — canciones con voz, `audio-music` — música instrumental,
`audio-speech` — locución. Las tres son «texto → sonido»: el prompt es la **descripción de la
tarea**; el título, los criterios de aceptación y la experiencia del proyecto no llegan al
modelo.

| Modelo | Dónde calcula | Qué hace | Muestra de sonido | Precio |
|---|---|---|---|---|
| [ACE-Step-1.5-XL-Turbo](../models/ACE-Step-1.5-XL-Turbo.md), [-Base](../models/ACE-Step-1.5-XL-Base.md), [-SFT](../models/ACE-Step-1.5-XL-SFT.md) | local, ComfyUI, NVIDIA desde 8 GB | canciones y música; escribe la letra si la descripción no la trae | no la acepta | gratis, pesos MIT |
| [ElevenLabs-Music](../models/ElevenLabs-Music.md) | nube, pasarela fal.ai | canciones y música, de 3 s a 10 min | no la acepta | 0,6 $ por minuto |
| [ElevenLabs-TTS-v3](../models/ElevenLabs-TTS-v3.md) | nube, pasarela fal.ai | voz con una voz predefinida (campo `voice`) | no la acepta | ver la página del modelo |
| [Chatterbox-TTS](../models/Chatterbox-TTS.md) | nube, pasarela fal.ai | voz clonada a partir de una muestra | una grabación de hasta 30 s | 0,025 $ por 1000 caracteres |
| [Zonos-2-TTS](../models/Zonos-2-TTS.md) | nube, pasarela fal.ai | voz clonada a partir de una muestra, WAV 44,1 kHz | una grabación de hasta 30 s, **obligatoria** | ver la página del modelo |

Algunos **modelos de vídeo** también generan sonido (Veo-3.1, Seedance-2.5, Wan-3.0-Prime,
Kling-3.0, LTX-2.5, Gemini-Omni-Flash), pero solo dentro del clip; cómo aprovecharlo, en la
sección 5.

La preparación es la misma para todos: el modelo está activo (el de nube tiene puesta la clave
`fal.apiKey`, el local tiene sus archivos instalados), sobre él hay un ejecutor de IA, el
ejecutor forma parte del equipo del proyecto y, en un modelo local, el trabajo del equipo está
iniciado. Paso a paso está en el [ejemplo del vídeo](sample_video1.md), capítulos 1.3–1.4.

## 3. La música y el vocalista X

**¿Qué muestra hace falta para X?** Ninguna: los modelos de música del catálogo no aceptan
muestras de voz. ElevenLabs-Music no tiene campo para una grabación, y los workflows que se
entregan para ACE-Step no tienen nodo de carga de audio. Los modelos que clonan una voz por
muestra (Chatterbox, Zonos) **hablan, no cantan**.

Por eso la voz de X se describe **con palabras**: sexo, timbre, forma de cantar.

```
Una balada country tranquila, 80 pulsaciones por minuto: guitarra acústica, slide,
contrabajo, escobillas sobre la caja. Sonido cálido de época.
Voz masculina: barítono grave, suave, algo ronco, canta bajito.
La letra habla de montañas y de humo sobre el bosque, en español.
Save the result to file sound/music.mp3.
```

* La habilidad de la tarea es `audio-song`; un instrumental sin voz es `audio-music` y las
  palabras «sin voz».
* Escriba su propia letra directamente en la descripción: ACE-Step la recoge; si no la
  escribe, la compone él.
* Duración: en ACE-Step es `length` en el perfil del modelo, **en segundos**; en
  ElevenLabs-Music es el campo de duración de la sección `request` del perfil (30 s en la
  plantilla).
* La indicación de dónde guardar el resultado va **en una línea aparte**: se quita del prompt
  entera. Las palabras que la reconocen son **solo rusas e inglesas** (`save`, `result`,
  `file`…), por eso en el ejemplo está en inglés.
* El nombre de un artista real no sustituye una muestra y crea un riesgo legal: describa el
  carácter de la voz.

**Para que la voz de X sea la misma de una pista a otra:**

| Técnica | Qué da |
|---|---|
| la misma descripción de la voz | conviene guardarla como pasaporte de un objeto del proyecto (tipo «estilo» o «personaje») y poner una referencia `@obj:` |
| un `seed` fijo en el perfil (ACE-Step) | resultado repetible; vacío o 0, aleatorio en cada trabajo |
| un adaptador LoRA (ACE-Step) | se puede aplicar un adaptador ya hecho, pero no entrenarlo desde AI2P (documento del modelo, sección sobre el entrenamiento de LoRA) |

Si la conversación de Y y Z va sobre la canción, habrá que bajar la voz en la mezcla. Es más
sencillo pedir una pista en la que la voz no empiece enseguida: «introducción de 30 segundos
sin voz».

## 4. Las voces de Y y Z: síntesis de voz por muestra

**Sí, hay modelos de síntesis de voz**: tres registros del catálogo con la habilidad
`audio-speech`.

* **ElevenLabs-TTS-v3**: el mejor valorado, pero habla con una de las voces **predefinidas**
  del proveedor (campo `voice` de la sección `request` del perfil, `Rachel` por defecto) y no
  acepta muestras. Sirve si Y y Z no son personas concretas: cree dos ejecutores sobre el mismo
  modelo con distintos `voice` (cada ejecutor tiene su propio perfil).
* **Chatterbox-TTS** y **Zonos-2-TTS**: clonan una voz a partir de una **muestra** grabada
  (zero-shot clone); no hay que entrenar nada, bastan 10–20 segundos de grabación.

En el catálogo no hay modelo de voz local: los tres calculan en la nube y necesitan la clave de
la pasarela fal.ai.

### 4.1. Qué muestras hacen falta para Y y Z

**Una** grabación por persona:

| Requisito | Por qué |
|---|---|
| 10–20 s (se admiten 3–30 s) | más corta, el timbre no se capta; de más de 30 s, la pasarela la rechaza |
| un solo hablante, sin música, ruido ni eco | el modelo copia todo lo que oye, sala incluida |
| volumen uniforme, sin saturación | las distorsiones pasan a la síntesis |
| la misma manera de hablar que en la escena | charla tranquila, muestra tranquila, no gritos |
| el mismo idioma que las frases | la pronunciación sale más precisa |
| WAV o MP3 | los formatos que aceptan ambos registros del catálogo |
| **consentimiento del dueño de la voz** | la licencia del modelo no da derechos sobre la voz ajena |

Ponga los archivos en la carpeta del proyecto, por ejemplo `refs/voice_y.wav` y
`refs/voice_z.wav`. Conviene crearlos como objetos del proyecto del tipo **«grabación de
referencia»**, hijos de los personajes Y y Z: así las muestras se ven en la lista de objetos y
no se pierden.

### 4.2. Una frase, una tarea

Un trabajo tiene **una** muestra, así que el diálogo se arma con tareas separadas: frase de Y,
frase de Z, otra vez Y… La descripción de la tarea **se pronuncia en voz alta entera**, por eso
solo debe llevar el texto de la frase y las indicaciones al sistema, cada una en su línea:

```
Mira, allí, detrás de la segunda cresta: el humo ya cubre todo el valle.
Voice sample refs/voice_y.wav.
Save the result to file sound/dialog_01_y.wav.
```

* Una línea con el nombre de un archivo de audio y una palabra como `voice`, `sample`,
  `speaker`, `timbre` (en ruso, «образец», «голос») es una indicación: el archivo va al modelo
  y la línea se quita del texto. Las palabras de reconocimiento son **solo rusas e inglesas**;
  «muestra de voz» en español no se reconoce.
* **Mejor no poner una referencia `@obj:` en una tarea de locución.** Se expande en la ficha del
  objeto —nombre, tipo, número y pasaporte—; la ruta se recorta, pero el nombre y el pasaporte
  quedan en el texto y **se leerán en voz alta**. Nombre el archivo en una línea, como en el
  ejemplo. (Para el modelo de música de la sección 3 la referencia sí sirve: allí el pasaporte
  es la descripción del estilo.)
* La habilidad de la tarea es `audio-speech`, el ejecutor está sobre Chatterbox-TTS o
  Zonos-2-TTS. Conviene tener las tareas del diálogo como subtareas de una tarea madre,
  «Diálogo de Y y Z».
* La entonación la dan el texto de la frase y la muestra. Los campos de ajuste fino están en la
  sección `request` del perfil: Chatterbox tiene `exaggeration` (expresividad), Zonos tiene
  `accurate_mode` (más fiel a la muestra o más expresivo) y `language` (idioma de normalización
  del texto).
* Haga todas las frases **con el mismo modelo**: el formato de los archivos coincidirá y será
  más fácil unirlos.

Las frases terminadas se unen en orden con la acción `ffmpeg_concat` del complemento
[tool.ffmpeg](../plugins/tool.ffmpeg.md). No añade pausas entre frases: eso, igual que poner la
voz sobre la música, se hace en un editor.

## 5. El helicóptero de bomberos

**¿Hace falta un sonido de helicóptero aparte?** Sí. En el catálogo no hay modelo de efectos de
sonido, y los que hay no sirven para esto:

* los modelos de voz (Chatterbox, Zonos) clonan una **voz**, no un ruido: una grabación de
  helicóptero como muestra no se convertirá en sonido de helicóptero;
* los modelos de música (ACE-Step, ElevenLabs-Music) hacen **música**: las palabras «rugido de
  helicóptero» darán, como mucho, un color musical, no un paso realista.

Tres caminos que funcionan, del fiable al experimental:

1. **Una grabación ya hecha** de una biblioteca de sonidos con licencia que permita su uso:
   póngala en la carpeta del proyecto (`sound/helicopter.wav`) y en la mezcla. La mejor opción
   para un efecto corto y reconocible.
2. **El sonido de un modelo de vídeo.** Los modelos «texto → vídeo con sonido» (Veo-3.1,
   Seedance-2.5, Wan-3.0-Prime y otros) sonorizan la escena solos. Una tarea como «Un
   helicóptero de bomberos pasa bajo sobre un bosque en llamas, el rotor se oye crecer y
   alejarse» da un clip, y la acción `ffmpeg_extract_audio` del complemento `tool.ffmpeg` le
   saca el sonido. Es de pago, y la calidad del sonido de estos modelos no se ha comprobado
   aparte.
3. **Su propio registro del catálogo.** La pasarela fal.ai tiene modelos dedicados a efectos de
   sonido; no hay registros entregados para ellos, pero un modelo en la nube de la pasarela se
   da de alta como registro del catálogo sin tocar código. Compruebe el identificador y los
   campos de la petición con el catálogo `https://fal.ai/api/models` y el esquema del
   endpoint: un campo desconocido la pasarela lo rechaza con HTTP 422 ya en un trabajo de pago.

## 6. La mezcla de la escena

Tiene los archivos en la carpeta del proyecto:

```
sound/music.mp3          — country con el vocalista X
sound/dialog_01_y.wav …  — frases de Y y Z
sound/helicopter.wav     — el paso del helicóptero
```

Se mezclan en un editor de audio o de vídeo: la música de fondo y más baja que la voz, las
frases en orden y con pausas, el helicóptero encima, con entrada y salida graduales. Si la
escena va a un vídeo, conviene mezclar directamente en un editor de vídeo conectado por
complemento (OpenShot, Shotcut, DaVinci Resolve, Blender; ver
[Complementos y MCP](../plugins/README.md)).

## 7. Errores frecuentes

| Mensaje o síntoma | Qué hacer |
|---|---|
| **El modelo leyó en voz alta el nombre o el pasaporte del objeto** | la tarea de locución tiene una referencia `@obj:`; cámbiela por la línea «Voice sample ruta/al/archivo.wav» |
| **El modelo leyó en voz alta la indicación del resultado** | estaba en la misma línea que la frase; póngala en una línea aparte |
| **«No se ha pasado la grabación de referencia»** | la descripción no tiene nombre de archivo de audio con una palabra como `voice` o `sample`; Chatterbox y Zonos exigen la grabación |
| **El archivo de resultado fue al modelo como muestra** | la línea del resultado o el nombre del archivo lleva una palabra de reconocimiento (`voice`, `sample`, `speaker`…); nombre el archivo de otro modo, p. ej. `dialog_01_y.wav` |
| **«Este modelo no acepta grabaciones»** | la tarea fue a un modelo sin muestra (ElevenLabs-TTS-v3 o uno de música); indique el ejecutor de forma explícita |
| **HTTP 422 de la pasarela** | la muestra dura más de 30 s, el formato no vale o hay un campo de la petición que el endpoint no conoce |
| **La voz no se parece a la muestra** | la grabación tiene ruido, música o una segunda voz; use 10–20 s limpios |
| **La canción sale en otro idioma** | indique el idioma en la descripción; en ACE-Step, el código de idioma en el campo `language` del workflow |
| **La pista dura más o menos de lo esperado** | en ACE-Step `length` son segundos |

## 8. Conviene recordar

* Un modelo multimedia recibe **solo la descripción de la tarea**; en un modelo de voz se
  convierte entera en el texto que pronunciará.
* La voz constante de un modelo de voz la mantiene una **muestra**, no un entrenamiento: la
  misma grabación en todas las frases de una persona.
* Los modelos de música del catálogo no clonan voces por muestra: la voz del cantante se
  describe con palabras.
* El catálogo no hace efectos de sonido, y las capas no las mezcla AI2P, sino un editor.
* Los derechos sobre la voz y sobre los sonidos de bibliotecas son cosa suya: la licencia del
  modelo no los da.

## Véase también

* [Objetos del proyecto](objects.md): el tipo «grabación de referencia» y la referencia `@obj:`.
* [Vídeo a partir del fotograma de referencia de un personaje](sample_video1.md): instalación
  del modelo, ejecutor y puesta en marcha, paso a paso.
* [Modelos de IA](../models/README.md): el documento de cada modelo de audio, con precios,
  licencias y campos de la petición.

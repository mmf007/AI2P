# ACE-Step-1.5-XL-SFT

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `acestep_v1_5_xl_sft`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** texto → música y canciones, habilidades `audio-song` 90, `audio-music` 89

Modelo de composición musical del equipo ACE-Step, variante **SFT**, con ajuste fino al gusto
humano: 50 pasos de difusión y `cfg 7`. Según la evaluación de los autores, tiene **calidad
alta** y variedad media, y sigue el texto de la petición con más precisión que los demás. Es la
variante «para el resultado final»: cuando la formulación ya está ajustada y hace falta el mejor
resultado.

Los tres registros de ACE-Step 1.5 XL del catálogo se diferencian sólo en esto:

| Registro | Pasos | `cfg` | Calidad | Variedad |
|---|---|---|---|---|
| ACE-Step-1.5-XL-Turbo | 8 | 1 (sin CFG) | comercial | media |
| ACE-Step-1.5-XL-Base | 50 | 6 | media | **alta** |
| **ACE-Step-1.5-XL-SFT** | 50 | 7 | **alta** | media |

La particularidad de ACE-Step 1.5, común a los tres: dentro del modelo funciona un **modelo de
lenguaje planificador**. Él mismo descompone su petición en la estructura de la canción, la letra,
el tempo y la tonalidad. Por eso no hace falta escribir la letra de la canción en un campo
aparte: basta con describir la tarea con palabras, y la letra la compondrá el modelo (o cogerá la
suya, si la ha escrito). Entiende más de cincuenta idiomas, y el ruso está entre ellos.

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, le envía el workflow y
recoge el `.mp3` terminado en los artefactos de la tarea.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → ACE-Step-1.5-XL-SFT → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **cuatro archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo
  `ACE-Step-1.5`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `acestep_v1.5_xl_sft_bf16.safetensors` | ~9,3 GiB | `diffusion_models` |
| `qwen_4b_ace15.safetensors` | ~7,8 GiB | `text_encoders` |
| `qwen_0.6b_ace15.safetensors` | ~1,1 GiB | `text_encoders` |
| `ace_1.5_vae.safetensors` | ~322 MiB | `vae` |

**En total, unos 19,9 GB de descarga.** La descarga es reanudable: una instalación interrumpida
continuará desde donde se paró, y los archivos ya descargados no se vuelven a descargar.

Los tres registros de ACE-Step 1.5 XL comparten **un mismo grupo** y tres de los cuatro archivos.
Si ya ha instalado Turbo o Base, este registro sólo descargará sus propios pesos, unos 9,3 GiB.

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **MIT** |
| Uso comercial | permitido; los autores lo señalan como el rasgo principal del modelo |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/ACE-Step/acestep-v15-xl-sft> (ficha del modelo) |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

Sobre el uso comercial, en ACE-Step 1.5 se dice directamente: el modelo se ha entrenado con
grabaciones licenciadas, libres de regalías y sintéticas precisamente para que el resultado se
pueda usar con fines comerciales.

Los archivos que descarga AI2P son un reempaquetado de Comfy-Org
(<https://huggingface.co/Comfy-Org/ace_step_1.5_ComfyUI_files>), publicado bajo **Apache 2.0**.
Las licencias se cotejaron con las fichas de los modelos el 27.08.2026.

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto; pero si usted le lleva a
alguien un paquete con ComfyUI dentro, las condiciones de la GPL se aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **desde 8 GB de VRAM** (los autores declaran funcionamiento desde 4 GB) |
| Controlador NVIDIA | **580 o más nuevo**; con un controlador antiguo torch se cae sin mensaje |
| Espacio en disco | unos 22 GB (pesos + paquete ComfyUI) |
| Memoria RAM | desde 16 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

## Cuánto hay que esperar

Cincuenta pasos frente a los ocho de Turbo son aproximadamente **seis veces más**, es decir,
minutos por pista en lugar de decenas de segundos, más una segunda pasada sobre la condición
negativa (`cfg 7` activa la classifier-free guidance). El tiempo de espera del encargo está
puesto en el perfil en 60 minutos (`params.timeoutMinutes`).

La marcha de la generación se ve en la **consola del encargo** de la ficha de la tarea.

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `length` | 120 | **duración de la pista en SEGUNDOS** (no fotogramas, como en el vídeo) |
| `steps` | 50 | pasos de difusión |
| `width` / `height` | sin indicar | el sonido no tiene tamaño de fotograma; en el resumen del encargo se muestran los valores por defecto 768×512, que no tienen nada que ver con el sonido |
| `negative` | vacío | prompt negativo (a diferencia de Turbo, aquí sí tiene efecto) |
| `timeoutMinutes` | 60 | cuánto esperar el resultado |

**Sobre la duración.** La duración de cada pista la indica el **modelo apuntador**
a partir de la descripción de la tarea («minuto y medio» se convierte en `duration: 90`).
El campo `length` del perfil queda como reserva: actúa cuando no hay apuntador elegido,
cuando no ha respondido o cuando la tarea no dice nada sobre la duración. El valor se va a
dos sitios del grafo a la vez — al tamaño del latente vacío y al campo `duration` del
planificador — y sus límites son duros: de 1 a 1000 segundos (comprobado contra un ComfyUI
vivo). En el resumen del encargo `length` aparece rotulado «fotogramas», porque así están
rotulados todos los modelos de medios; léalo como «segundos».

**Sobre el idioma de la voz.** El idioma lo indica el apuntador con un código de
la lista del nodo (`ru`, `en`, `zh`, `ja`, …: 51 valores en total). Si nadie lo indica,
queda `unknown` y el modelo determina el idioma por la letra; en las plantillas oficiales
de ComfyUI ahí está `en`, lo que con letras no inglesas daría pronunciación inglesa. Un
idioma fijo también puede ponerse sin apuntador: con su código en la plantilla del workflow
del modelo.

Sin apuntador, la descripción entera de la tarea se va al prompt (el campo `tags` del modelo); con apuntador, a `tags` llegan las etiquetas de estilo que él ha compuesto. Una indicación del
tipo «результат положить в файл X.mp3» la ejecuta el conector: el archivo se copia a la carpeta
del proyecto y la propia línea se recorta del prompt. Las palabras de la indicación se reconocen
**sólo en ruso y en inglés** (en inglés, `Save the result to the file X.mp3`): están incrustadas
en el código y no dependen del idioma de la instalación.

## El apuntador

Este registro lleva en su perfil la marca **«necesita apuntador»**. El apuntador es OTRO
EJECUTOR: antes de la generación lee la descripción de la tarea y prepara el json de control
para ACE-Step en un encargo aparte. Sirve cualquier ejecutor de IA: una suscripción CLI, un
modelo local, una API en la nube; trabaja con su propio conector, igual que en una tarea
normal. Se asigna de dos maneras: con el campo **«Apuntador»** de la ficha del ejecutor de
IA (valor por defecto para todas sus tareas) y con el campo **«Apuntador»** del formulario
de la tarea, junto a la lista **«Pueden sustituir al apuntador»**, para cuando el asignado
está ocupado con otro trabajo.

El apuntador rellena siete campos del nodo `TextEncodeAceStepAudio1.5`:

| Campo | Qué es | Si no se indica |
|---|---|---|
| `tags` | etiquetas de estilo: género, tempo, instrumentos, ambiente, voz | la descripción entera de la tarea |
| `lyrics` | la letra de la canción | vacío: el modelo la compone él mismo |
| `duration` | duración en segundos (1…1000) | el `length` del perfil |
| `language` | código del idioma de la voz, de la lista del nodo | `unknown` |
| `bpm` | tempo, pulsaciones por minuto (10…300) | 120 |
| `keyscale` | tonalidad y modo (`C major` … `B minor`) | `C major` |
| `timesignature` | compás: 2, 3, 4 o 6 | 4 |

**Qué pasa si no se pone apuntador.** La tarea NO ARRANCA: se detiene con un error que
dice que el ejecutor necesita apuntador y no lo hay ni en la tarea ni en el propio ejecutor.
El sistema no puede seguir en silencio «como siempre»: los parámetros de la pista saldrían
de la nada y se vería media hora después, cuando la pista lista no sea la pedida. Lo mismo
ocurre con una respuesta sin json y con un encargo del apuntador caído. Los otros dos
desenlaces son más suaves: si el apuntador asignado está ocupado, el trabajo lo toma el
primero libre de «pueden sustituir al apuntador», y si todos están ocupados la tarea espera
en pausa hasta que alguien se libere; si el apuntador hizo una pregunta a la persona, la
tarea también queda en pausa y sigue con la respuesta.

**Nuevo arranque.** Una tarea en pausa o con error que ya tiene su json pasa directamente a
la generación: no se pregunta al apuntador dos veces. Una tarea en borrador, en espera o en
revisión empieza de cero: el apuntador prepara un json nuevo.

**Cómo influir en el resultado desde la descripción.** Escriba lo que debe llegar a los
campos: la duración («un minuto», «90 segundos»), el idioma de la voz, el tempo, el modo, el
compás; y la letra de la canción, palabra por palabra, que el apuntador la traslada tal cual.
El estilo descríbalo con palabras: de ellas saldrán las etiquetas. Lo que finalmente indicó
se ve en la consola del encargo y en el archivo `prompter.json` entre los archivos de la
tarea.

**Las reglas por las que trabaja** están junto al perfil del modelo: el archivo
`models/prompter_<identificador del registro>.md` del directorio de datos. Puede editarlo:
el texto se va entero al prompt del apuntador y el cambio actúa desde el encargo siguiente.
El archivo lo reescribe la instalación cuando sube su versión, y la replicación no lo lleva
a otros servidores.

## Cómo escribir la tarea

El modelo espera una descripción de la música, no una orden. Funcionan:

* el **estilo, el tempo, los instrumentos, el ambiente**: «ambient tranquilo, 72 pulsaciones por
  minuto, pads cálidos, crujido de vinilo»;
* la **voz**: «voz femenina, suave, con reverberación larga»;
* la **letra propia**: escríbala directamente en la descripción de la tarea, que el planificador
  la recogerá;
* el **instrumental**: escríbalo tal cual, «sin voz, sólo instrumentos».

SFT sigue el texto con más precisión que las demás variantes, así que aquí una descripción
detallada es lo que más rinde. Cada lanzamiento coge un **seed aleatorio**; si necesita un
resultado repetible, escriba `seed` como número en el perfil del modelo.

La descripción la lee el apuntador, así que escriba en ella los números y el
idioma directamente: «90 segundos», «voz en ruso», «120 pulsaciones por minuto», «en modo
menor». Lo que ha entendido se ve en la consola del encargo.

## Entrenamiento de LoRA

**Aplicar — sí; entrenar desde AI2P — no.**

El modelo acepta un adaptador ya entrenado: AI2P inserta el nodo `LoraLoaderModelOnly` en
el grafo sobre la marcha. Coloque el archivo en `<repositorio de modelos>/loras/` y
mencione el objeto-adaptador en la descripción de la tarea con `@obj:`.

Entrenar un adaptador desde AI2P no se puede, y el perfil lo dice con honestidad:
`lora.train.kind: external` con el comando vacío — el botón «Entrenar» rechaza de
inmediato en vez de gastar media hora. Verificado el 14.09.2026 sobre los archivos de los
repositorios:

* **el modelo sí tiene entrenador** — el oficial
  [ACE-Step-1.5](https://github.com/ace-step/ACE-Step-1.5), licencia MIT, y funciona en
  Windows con UNA sola tarjeta: `python -m acestep.training_v2.cli.train_fixed`, sin
  `torchrun`, `--num-devices` es 1 por defecto y los workers del DataLoader son 0 en
  Windows a propósito. Requiere 16 GB de VRAM como mínimo, 20 GB o más recomendados;
* **pero necesita pesos distintos de los que instalamos.** El entrenador lee un directorio
  de checkpoints en formato HuggingFace (`config.json` +
  `model-0000N-of-00004.safetensors`, unos 19,9 GB por variante, más el VAE y el modelo de
  lenguaje de etiquetado), mientras que AI2P instala el reempaquetado de Comfy-Org: otros
  archivos y otra disposición. La instalación no descarga una segunda copia;
* **y el formato del archivo entrenado no está verificado**: el entrenador produce un
  adaptador peft sobre su propio DiT, y la rama del «formato oficial de ACE-Step» en
  `comfy/lora.py` está bajo la clase `ACEStep`, mientras que 1.5 es la clase aparte
  `ACEStep15`;
* [musubi-tuner](https://github.com/kohya-ss/musubi-tuner), con el que AI2P entrena LoRA
  para los demás modelos locales, no conoce ACE-Step en absoluto (verificado el
  27.08.2026).

**Aun así los límites del conjunto están declarados**: AI2P compara el conjunto con ellos y
usted lo arma para un entrenamiento externo. El conjunto es de AUDIO (`media: audio`):
grabaciones de hasta 240 s, 48 000 Hz, 2 canales, formatos WAV, MP3, FLAC, OGG y Opus,
desde 10 grabaciones, con las descripciones en un archivo `.txt` junto a la grabación
(transcripción o etiquetas). El editor muestra justamente esos campos y guarda la
grabación tal cual: el audio no pasa por la compresión de imágenes.

Orden de trabajo del entrenador oficial: preparar las grabaciones con `<nombre>.lyrics.txt`
y sus descripciones → preprocesar a tensores → lanzar el entrenamiento (LoRA o LoKr, unas
diez veces más rápido). Detalles — [LoRA Training Tutorial](https://github.com/ace-step/ACE-Step-1.5/blob/main/docs/en/LoRA_Training_Tutorial.md).

## Errores frecuentes

* **El proceso desaparece sin mensaje**: casi siempre es un controlador NVIDIA antiguo (véase más
  arriba).
* **«El modelo no está instalado»**: los archivos no se han terminado de descargar; abra
  «Instalar» y la ventana mostrará el volumen que queda.
* **Tarda demasiado**: son 50 pasos con CFG; para borradores coja Turbo.
* **Resultados monótonos**: es una propiedad de SFT; si necesita dispersión, coja Base.
* **La voz canta en otro idioma**: indique el idioma en la descripción de la
  tarea, el apuntador lo transmitirá; sin apuntador, ponga el código del idioma en el campo
  `language` de la plantilla del workflow en lugar de `unknown`.
* **La pista es más corta o más larga de lo esperado**: indique la duración en la
  descripción de la tarea (el apuntador la entrega en segundos); sin apuntador actúa el
  `length` del perfil, que también va en segundos.
* **ComfyUI está ocupado por un proceso ajeno**: AI2P sólo descarga el servidor que ha arrancado
  él mismo; un ComfyUI ajeno que ya esté funcionando en el 8188 no lo toca.

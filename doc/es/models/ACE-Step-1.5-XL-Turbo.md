# ACE-Step-1.5-XL-Turbo

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `acestep_v1_5_xl_turbo`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** texto → música y canciones, habilidades `audio-song` 84, `audio-music` 86

Modelo de composición musical del equipo ACE-Step, variante **Turbo**: un destilado que calcula
una pista en **ocho pasos** en lugar de cincuenta y sin classifier-free guidance. Es la forma más
rápida de obtener una pista terminada, y una primera elección razonable mientras se ajusta la
formulación.

La particularidad de ACE-Step 1.5, por la que merece la pena cogerlo: dentro del modelo funciona
un **modelo de lenguaje planificador**. Él mismo descompone su petición en la estructura de la canción, la letra,
el tempo y la tonalidad. Por eso no hace falta escribir la letra de la canción en un campo
aparte: basta con describir la tarea con palabras, y la letra la compondrá el modelo (o cogerá la
suya, si la ha escrito). Entiende más de cincuenta idiomas, y el ruso está entre ellos.

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, le envía el workflow y
recoge el `.mp3` terminado en los artefactos de la tarea.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → ACE-Step-1.5-XL-Turbo → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **cuatro archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo
  `ACE-Step-1.5`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `acestep_v1.5_xl_turbo_bf16.safetensors` | ~9,3 GiB | `diffusion_models` |
| `qwen_4b_ace15.safetensors` | ~7,8 GiB | `text_encoders` |
| `qwen_0.6b_ace15.safetensors` | ~1,1 GiB | `text_encoders` |
| `ace_1.5_vae.safetensors` | ~322 MiB | `vae` |

**En total, unos 19,9 GB de descarga.** La descarga es reanudable: una instalación interrumpida
continuará desde donde se paró, y los archivos ya descargados no se vuelven a descargar.

Los tres registros de ACE-Step 1.5 XL del catálogo (Turbo, Base, SFT) comparten **un mismo
grupo** y tres de los cuatro archivos. Si ya ha instalado uno de ellos, el segundo sólo descargará
su propio archivo de pesos, unos 9,3 GiB, y no los 19,9 GB enteros.

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **MIT** |
| Uso comercial | permitido; los autores lo señalan como el rasgo principal del modelo |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/ACE-Step/Ace-Step1.5> (ficha del modelo) |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

Sobre el uso comercial, en ACE-Step 1.5 se dice directamente: el modelo se ha entrenado con
grabaciones licenciadas, libres de regalías y sintéticas precisamente para que el resultado se
pueda usar con fines comerciales. Eso la distingue de los modelos entrenados con datos de
origen poco claro.

Los archivos que descarga AI2P son un reempaquetado de Comfy-Org
(<https://huggingface.co/Comfy-Org/ace_step_1.5_ComfyUI_files>), publicado bajo **Apache 2.0**.
Las licencias se cotejaron con las fichas de los modelos el 27.08.2026; en los modelos abiertos
cambian poco, pero antes de un lanzamiento comercial compruebe la ficha otra vez.

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

Ocho pasos son **segundos o decenas de segundos** por pista: los autores declaran una canción
entera en menos de 10 segundos en una RTX 3090. El tiempo de espera del encargo está puesto en el
perfil en 60 minutos (`params.timeoutMinutes`), lo que basta de sobra incluso en equipos lentos.

La marcha de la generación se ve en la **consola del encargo** de la ficha de la tarea: allí se
vuelca la salida propia de ComfyUI junto con su barra de progreso.

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `length` | 120 | **duración de la pista en SEGUNDOS** (no fotogramas, como en el vídeo) |
| `steps` | 8 | pasos de difusión; en Turbo no tiene sentido pasar de ocho |
| `width` / `height` | sin indicar | el sonido no tiene tamaño de fotograma; en el resumen del encargo se muestran los valores por defecto 768×512, que no tienen nada que ver con el sonido |
| `negative` | vacío | prompt negativo: **en Turbo no tiene efecto** (véase más abajo) |
| `timeoutMinutes` | 60 | cuánto esperar el resultado |

**Sobre la duración.** El campo `length` de este registro significa segundos y se va a dos sitios
del grafo a la vez: al tamaño del latente vacío y al campo `duration` del planificador. En el
resumen del encargo aparece rotulado con la palabra «fotogramas», porque así están rotulados todos
los modelos de medios; léalo como «segundos». El modelo está pensado para pistas de hasta unos
diez minutos.

**Sobre el idioma de la voz.** En el grafo, el campo `language` está puesto en `unknown`: el
modelo determina el idioma por su texto. En las plantillas oficiales de ComfyUI ahí está `en`, lo
que con letras en ruso habría dado una pronunciación inglesa. Si usted canta siempre en un mismo
idioma, ponga su código (`ru`, `en`, `es`, `zh`, …) directamente en la plantilla del workflow del
modelo.

**Sobre el prompt negativo.** El modo Turbo calcula sin classifier-free guidance (`cfg = 1`), y
por eso la condición negativa está hecha en la plantilla con el nodo `ConditioningZeroOut`,
exactamente como en la plantilla oficial de ComfyUI. El valor de `negative` se puede escribir en
el perfil, pero no influirá en el sonido; si necesita negativo, coja **ACE-Step-1.5-XL-Base** o
**-SFT**.

La descripción entera de la tarea se va al prompt (el campo `tags` del modelo). Una indicación del
tipo «результат положить в файл X.mp3» la ejecuta el conector: el archivo se copia a la carpeta
del proyecto y la propia línea se recorta del prompt. Las palabras de la indicación se reconocen
**sólo en ruso y en inglés** (en inglés, `Save the result to the file X.mp3`): están incrustadas
en el código y no dependen del idioma de la instalación.

## Cómo escribir la tarea

El modelo espera una descripción de la música, no una orden. Funcionan:

* el **estilo, el tempo, los instrumentos, el ambiente**: «ambient tranquilo, 72 pulsaciones por
  minuto, pads cálidos, crujido de vinilo»;
* la **voz**: «voz femenina, suave, con reverberación larga»;
* la **letra propia**: escríbala directamente en la descripción de la tarea, que el planificador
  la recogerá;
* el **instrumental**: escríbalo tal cual, «sin voz, sólo instrumentos».

Cada lanzamiento coge un **seed aleatorio**, así que dos encargos con el mismo texto darán pistas
distintas. Si necesita un resultado repetible, escriba `seed` como número en el perfil del
modelo.

## Entrenamiento de LoRA

**La aplicación, sí; el entrenamiento, no.**

Un adaptador ya hecho el modelo lo acepta: ComfyUI conoce el formato oficial de LoRA de ACE-Step,
y AI2P inserta el nodo `LoraLoaderModelOnly` en el grafo sobre la marcha. Ponga el archivo en
`<repositorio de modelos>/loras/` y nombre el objeto adaptador en la descripción de la tarea con
una referencia `@obj:`.

En cambio, **entrenar un adaptador desde AI2P no se puede**, y en el perfil está señalado
honestamente (`lora.train.kind: external`). Hay dos motivos:

* [musubi-tuner](https://github.com/kohya-ss/musubi-tuner), con el que AI2P entrena LoRA para los
  demás modelos locales, no sabe nada de ACE-Step: no tiene ni un solo script de acestep
  (comprobado el 27.08.2026);
* el entrenador oficial de ACE-Step (<https://github.com/ace-step/ACE-Step-1.5>, `train.py`)
  aprende con **grabaciones de audio**, y el conjunto de datos de LoRA en AI2P son fotogramas, es
  decir, imágenes: tanto el editor del conjunto de datos como el control de tamaños están
  pensados para imágenes.

Por eso el adaptador se entrena fuera de AI2P con el entrenador oficial y aquí se pone ya como
archivo terminado.

## Errores frecuentes

* **El proceso desaparece sin mensaje**: casi siempre es un controlador NVIDIA antiguo (véase más
  arriba).
* **«El modelo no está instalado»**: los archivos no se han terminado de descargar; abra
  «Instalar» y la ventana mostrará el volumen que queda.
* **El prompt negativo no tiene efecto**: en Turbo está pensado así (véase «Parámetros de
  generación»).
* **La voz canta en otro idioma**: ponga el código del idioma en el campo `language` de la
  plantilla del workflow en lugar de `unknown`.
* **La pista es más corta o más larga de lo esperado**: es el `length` del perfil, y va en
  segundos.
* **ComfyUI está ocupado por un proceso ajeno**: AI2P sólo descarga el servidor que ha arrancado
  él mismo; un ComfyUI ajeno que ya esté funcionando en el 8188 no lo toca.

# LTX-2.5

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `ltx_2_5_distilled`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** texto → vídeo **con sonido**, habilidad `video-generate` 85

Modelo de vídeo de Lightricks, 22 000 millones de parámetros, variante destilada. Es el **único
modelo abierto que dibuja vídeo y sonido en una sola pasada**: la pista de audio se calcula con su
propio latente junto al vídeo y va a parar al mismo `.mp4`. A todos los demás modelos locales del
catálogo hay que ponerles el sonido aparte.

En calidad de imagen es más potente que Wan 2.2 y HunyuanVideo 1.5, pero también más pesado: 22B
frente a 14B, y los pesos están cerrados por un acuerdo (véase más abajo: los archivos habrá que
ponerlos a mano).

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, le envía el workflow y
recoge el `.mp4` terminado en los artefactos de la tarea.

## Instalación: los archivos habrá que ponerlos a mano

El repositorio de los pesos está **cerrado por un acuerdo** (gated): HuggingFace entrega los
archivos sólo a quien ha aceptado la licencia, y además con un token personal. El descargador de
AI2P va sin token y recibirá una negativa (comprobado con una petición el 27.08.2026), así que el
procedimiento es este:

1. abra <https://huggingface.co/Lightricks/LTX-2.5>, entre en su cuenta de HuggingFace y acepte
   las condiciones de la licencia;
2. descargue los cuatro archivos de la tabla de abajo;
3. póngalos en la subcarpeta `LTX-2.5` del repositorio de modelos (`storage.modelsRepo`), **en
   plano, sin carpetas anidadas** y sin cambiar los nombres de los archivos;
4. pulse **«Instalar»** en el formulario del modelo: **Ajustes → Catálogos → Modelos → LTX-2.5 →
   «Instalar»**. La instalación verá que los tamaños coinciden e instalará sólo el paquete
   ComfyUI, y el modelo pasará a estar instalado.

| Archivo | Tamaño | Adónde |
|---|---|---|
| `ltx-2.5-22b-distilled-transformer-comfy-int8-convrot.safetensors` | ~20 GiB | `diffusion_models` |
| `gemma4-12b-with-proj-ltx-2.5-comfy-int8-convrot.safetensors` | ~14,3 GiB | `text_encoders` |
| `ltx-2.5-video-vae-bf16.safetensors` | ~1,4 GiB | `vae` |
| `ltx-2.5-audio-vae-bf16.safetensors` | ~348 MiB | `vae` |

**En total, unos 38,7 GB.** Mientras los archivos no estén en su sitio, el modelo **no puede estar
activo**, y eso se comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **LTX-2 Community License Agreement** (no es Apache ni MIT) |
| Uso comercial | permitido en las condiciones del acuerdo, que hay que aceptar personalmente |
| Qué es obligatorio | aceptar la licencia en HuggingFace y conservar el aviso; las condiciones limitan los despliegues grandes |
| Texto de la licencia | <https://github.com/Lightricks/LTX-2/blob/main/LICENSE.md> |
| Ficha del modelo | <https://huggingface.co/Lightricks/LTX-2.5> |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

**No** es una licencia libre: el repositorio está cerrado por un acuerdo, y hasta aceptarlo los
archivos no están disponibles en absoluto. Antes de un lanzamiento comercial lea el texto entero.
La licencia se cotejó con la ficha del modelo el 27.08.2026
(`license_name: ltx-2-community-license-agreement`).

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto; pero si usted le lleva a
alguien un paquete con ComfyUI dentro, las condiciones de la GPL se aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **desde 24 GB de VRAM** (32 GB cómodo) |
| Controlador NVIDIA | **580 o más nuevo**; con un controlador antiguo torch se cae sin mensaje |
| Espacio en disco | unos 41 GB (pesos + paquete ComfyUI) |
| Memoria RAM | desde 32 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

Los archivos de pesos están tomados en la compilación `int8-convrot`, la misma que hay en la
plantilla oficial de ComfyUI: pesa la mitad que la bf16 (en la que sólo el transformador ocupa 39
GiB).

## Cuánto hay que esperar

El modelo es destilado y los pasos son sólo ocho, así que un clip de 1280×704 y 121 fotogramas (5
segundos a 24 fotogramas por segundo) se calcula **más rápido** que en Wan 2.2 con veinte pasos:
de unos pocos a unas decenas de minutos. El tiempo de espera del encargo está puesto en el perfil
en 240 minutos (`params.timeoutMinutes`).

## Parámetros de generación

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `width` / `height` | 1280 × 704 | resolución del fotograma; ambos números son múltiplos de 32, que es lo que exige el nodo del latente |
| `length` | 121 | fotogramas del clip; 24 fotogramas por segundo, es decir, 5 segundos |
| `steps` | 8 | **a título informativo**: el número de pasos lo fijan las sigmas de la plantilla, véase más abajo |
| `negative` | vacío | prompt negativo |
| `timeoutMinutes` | 240 | cuánto esperar el resultado |

**Sobre los pasos.** En el modelo destilado la planificación del ruido está dada con una lista de
números directamente en la plantilla (el nodo `ManualSigmas`) y no con un número de pasos: nueve
valores son ocho pasos. Cambiar `steps` en el perfil no influye en la generación, y el valor está
ahí para el resumen del encargo; para cambiar la planificación, edite las `sigmas` en el archivo
`models/workflow_….json` de la carpeta de la organización.

**Qué no tiene nuestra plantilla frente a la oficial.** La plantilla oficial de ComfyUI calcula en
dos pasadas: la primera a la mitad de tamaño, y la segunda sube la resolución con el nodo
`LTXVLatentUpsampler`. El conector de AI2P no sabe hacer aritmética con `width`/`height`, así que
no habría de dónde sacar «la mitad», y el resumen del encargo le diría a la persona un tamaño
equivocado: aquí hay una sola pasada, directamente a la resolución de destino. La mejora del
prompt con un modelo de lenguaje aparte (`TextGenerateLTX2Prompt`) también está desactivada: se
lleva otro archivo de pesos y reescribe el texto de la tarea en silencio.

## Entrenamiento de LoRA

El adaptador **se aplica** con normalidad: el nodo `LoraLoaderModelOnly` se inserta en el grafo
sobre la marcha y los archivos se cogen de `<repositorio de modelos>/loras`. LTX tiene un
ecosistema de LoRA ya hecho y rico: adaptadores de movimiento de cámara, de estilo y de control
los publica el propio Lightricks.

En cambio, **entrenar un adaptador desde AI2P no se puede**, y en el registro está señalado con
honestidad: [musubi-tuner](https://github.com/kohya-ss/musubi-tuner), con el que AI2P entrena LoRA
para los demás modelos locales, no tiene entrenador de LTX en absoluto: tiene Wan, HunyuanVideo,
Kandinsky, Qwen-Image y Z-Image, pero no LTX. Por eso en el perfil el entrenamiento tiene el modo
«externo»: el botón «entrenar» responde con una negativa clara y un enlace, en lugar de quedarse
colgado media hora y caerse.

El adaptador se puede entrenar aparte, con el entrenador oficial de Lightricks
(<https://github.com/Lightricks/LTX-Video-Trainer>), y poner el archivo `.safetensors` terminado
en `<repositorio de modelos>/loras`: de ahí AI2P lo recoge.

## Errores frecuentes

* **La instalación responde con una negativa en la descarga**: el repositorio está cerrado por un
  acuerdo y los archivos hay que traerlos a mano (véase «Instalación»).
* **«El modelo no está instalado» después de ponerlos a mano**: compruebe los nombres de los
  archivos y que estén directamente en la carpeta del grupo `LTX-2.5` y no en carpetas anidadas;
  el tamaño debe coincidir con la tabla hasta el byte.
* **El clip sale sin sonido**: el sonido se saca de esa misma pasada; si no lo hay, compruebe que
  en la plantilla siguen los nodos `LTXVEmptyLatentAudio` y `LTXVAudioVAEDecode`.
* **Falta de VRAM**: reduzca `width`/`height` o `length`.

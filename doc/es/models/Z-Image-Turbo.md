# Z-Image-Turbo

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `z_image_turbo`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** texto → imagen, habilidades `image-generate` 85, `image-photo` 84,
`image-concept` 82, `image-text` 78

Modelo de dibujo de imágenes de Tongyi-MAI (Alibaba), variante **Turbo**: 6000 millones de
parámetros y **ocho pasos** por fotograma en lugar de los veinte o cincuenta habituales. Es el más
rápido y el menos exigente de los modelos locales de imagen del catálogo: una primera elección
razonable si la tarjeta gráfica es modesta.

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, le envía el workflow y
recoge el `.png` terminado en los artefactos de la tarea.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → Z-Image-Turbo → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **tres archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo `Z-Image`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `z_image_turbo_bf16.safetensors` | ~11,5 GiB | `diffusion_models` |
| `qwen_3_4b.safetensors` | ~7,5 GiB | `text_encoders` |
| `ae.safetensors` | ~320 MiB | `vae` |

**En total, unos 21 GB de descarga.** La descarga es reanudable: una instalación interrumpida
continuará desde donde se paró, y los archivos ya descargados no se vuelven a descargar.

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **Apache 2.0** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/Tongyi-MAI/Z-Image-Turbo> (ficha del modelo) |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

Los archivos que descarga AI2P son un reempaquetado de Comfy-Org
(<https://huggingface.co/Comfy-Org/z_image_turbo>), que va bajo esa misma Apache 2.0. La licencia
se cotejó con la ficha del modelo el 27.08.2026; en los modelos abiertos cambia poco, pero antes
de un lanzamiento comercial compruebe la ficha otra vez.

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto; pero si usted le lleva a
alguien un paquete con ComfyUI dentro, las condiciones de la GPL se aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **desde 8 GB de VRAM** (16 GB cómodo) |
| Controlador NVIDIA | **580 o más nuevo**; con un controlador antiguo torch se cae sin mensaje |
| Espacio en disco | unos 23 GB (pesos + paquete ComfyUI) |
| Memoria RAM | desde 16 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

## Cuánto hay que esperar

Ocho pasos son **segundos o decenas de segundos** por fotograma de 1024×1024 en una tarjeta
actual, y no horas como en los modelos de vídeo. El tiempo de espera del encargo está puesto en el
perfil en 60 minutos (`params.timeoutMinutes`), lo que basta de sobra incluso en equipos lentos.

La marcha de la generación se ve en la **consola del encargo** de la ficha de la tarea: allí se
vuelca la salida propia de ComfyUI junto con su barra de progreso.

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `width` / `height` | 1024 × 1024 | resolución del fotograma |
| `steps` | 8 | pasos de difusión; en Turbo no tiene sentido pasar de ocho |
| `negative` | vacío | prompt negativo: **en Turbo no tiene efecto** (véase más abajo) |
| `timeoutMinutes` | 60 | cuánto esperar el resultado |

**Sobre el prompt negativo.** El modo Turbo calcula sin classifier-free guidance (`cfg = 1`), y
por eso la condición negativa está hecha en la plantilla con el nodo `ConditioningZeroOut`,
exactamente como en la plantilla oficial de ComfyUI. El valor de `negative` se puede escribir en
el perfil, pero no influirá en la imagen; si necesita negativo, coja el modelo normal (no Turbo).

La descripción entera de la tarea se va al prompt. Una indicación del tipo «результат положить в
файл X.png» la ejecuta el conector: el archivo se copia a la carpeta del proyecto y la propia
línea se recorta del prompt. Las palabras de la indicación se reconocen **sólo en ruso y en
inglés** (en inglés, `Save the result to the file X.png`): están incrustadas en el código y no
dependen del idioma de la instalación.

## Entrenamiento de LoRA

Está admitido por completo: el adaptador se **aplica** (el nodo `LoraLoaderModelOnly` se inserta
en el grafo sobre la marcha) y se **entrena** directamente desde AI2P, con el editor de LoRA de la
ficha del objeto del proyecto.

Entrena [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) con los scripts
`zimage_cache_latents.py`, `zimage_cache_text_encoder_outputs.py` y `zimage_train_network.py`
(`--network_module networks.lora_zimage`), una sola tarjeta gráfica y atención mediante `sdpa`.

El paquete `musubi-tuner` (código fuente, unos 30 MB) y **Python 3.12** (unos 45 MB) se instalan
junto con el modelo, con el botón «Instalar»; un Python 3.10–3.12 que ya esté en el ordenador se
toma tal cual. El entorno con `torch` (unos 3 GB) se lo crea el entrenador en el primer
lanzamiento del entrenamiento.

**Una particularidad propia de Turbo.** Turbo son pesos destilados, y entrenar LoRA sobre ellos no
lo recomienda el propio autor del entrenador, así que en el primer entrenamiento el `train.cmd`
descarga una vez los pesos **base** `z_image_bf16.safetensors` (unos 11,5 GB) a la subcarpeta
`train` del grupo `Z-Image` y entrena sobre ellos. El codificador de texto y el VAE se cogen de
los ya instalados: son los mismos en la versión base y en la turbo. El adaptador terminado
funciona con los pesos turbo.

El adaptador terminado va a
`<repositorio de modelos>/loras/<código del objeto>.safetensors`. Todas las opciones de
entrenamiento, el `dataset.toml` y el `train.cmd` están en el perfil del modelo (`lora.train`) y
se reescriben antes de cada lanzamiento: edítelos en el perfil, no en los archivos.

## Errores frecuentes

* **El proceso desaparece sin mensaje**: casi siempre es un controlador NVIDIA antiguo (véase más
  arriba).
* **«El modelo no está instalado»**: los archivos no se han terminado de descargar; abra
  «Instalar» y la ventana mostrará el volumen que queda.
* **El prompt negativo no tiene efecto**: en Turbo está pensado así (véase «Parámetros de
  generación»).
* **Falta de VRAM**: reduzca `width`/`height`.
* **ComfyUI está ocupado por un proceso ajeno**: AI2P sólo descarga el servidor que ha arrancado
  él mismo; un ComfyUI ajeno que ya esté funcionando en el 8188 no lo toca.

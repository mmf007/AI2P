# Qwen-Image-2512

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `qwen_image_2512`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** texto → imagen, habilidades `image-text` 94, `image-photo` 91,
`image-generate` 90, `image-concept` 85

Modelo de dibujo de imágenes de Alibaba, edición **2512** (20 000 millones de parámetros). Su
punto fuerte es el **texto dentro del cuadro**: rótulos, pies, portadas, carteles, interfaces, y
además en cirílico. Es el mejor de los modelos abiertos en rótulos y composición: es por eso por
lo que hay que elegirlo si en la imagen tiene que haber palabras.

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, le envía el workflow y
recoge el `.png` terminado en los artefactos de la tarea.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → Qwen-Image-2512 → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **tres archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo
  `Qwen-Image`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `qwen_image_2512_fp8_e4m3fn.safetensors` | ~19,0 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `qwen_image_vae.safetensors` | ~242 MiB | `vae` |

**En total, unos 30 GB de descarga.** La descarga es reanudable: una instalación interrumpida
continuará desde donde se paró, y los archivos ya descargados no se vuelven a descargar.

El grupo `Qwen-Image` es común con el modelo `Qwen-Image-Edit-2511`: el codificador de texto y el
VAE son los mismos, así que el segundo modelo sólo descargará su propio archivo de pesos (unos 19
GiB) y no los treinta enteros de nuevo.

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **Apache 2.0** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/Qwen/Qwen-Image> (ficha del modelo) |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

Los archivos que descarga AI2P son un reempaquetado de Comfy-Org
(<https://huggingface.co/Comfy-Org/Qwen-Image_ComfyUI>), que va bajo esa misma Apache 2.0. La
licencia se cotejó con la ficha del modelo el 27.08.2026.

Cuidado con el nombre vecino: **Qwen-Image-3.0 (agosto de 2026) es cerrado**, no tiene ni pesos,
ni licencia, ni informe técnico, y localmente no arranca. La línea abierta termina en las
ediciones 2512 (dibujo) y 2511 (edición).

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto; pero si usted le lleva a
alguien un paquete con ComfyUI dentro, las condiciones de la GPL se aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **desde 16 GB de VRAM** (24 GB cómodo) |
| Controlador NVIDIA | **580 o más nuevo**; con un controlador antiguo torch se cae sin mensaje |
| Espacio en disco | unos 32 GB (pesos + paquete ComfyUI) |
| Memoria RAM | desde 32 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

El modelo del manifiesto está en **fp8**: pesa la mitad que el bf16 original y está pensado
exactamente para una tarjeta de consumo. Para entrenar LoRA el fp8 no sirve; véase más abajo.

## Cuánto hay que esperar

Un fotograma de 1328×1328 en 20 pasos son **minutos** en una tarjeta del nivel de una 4090, y
hasta una decena de minutos con 8–16 GB descargando a la memoria RAM. El tiempo de espera del
encargo está puesto en el perfil en 90 minutos (`params.timeoutMinutes`).

La marcha de la generación se ve en la **consola del encargo** de la ficha de la tarea: allí se
vuelca la salida propia de ComfyUI junto con su barra de progreso.

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `width` / `height` | 1328 × 1328 | resolución del fotograma |
| `steps` | 20 | pasos de difusión; menos es más rápido y más basto |
| `negative` | vacío | prompt negativo (aquí **sí tiene efecto**, `cfg = 4`) |
| `timeoutMinutes` | 90 | cuánto esperar el resultado |

La descripción entera de la tarea se va al prompt. Una indicación del tipo «результат положить в
файл X.png» la ejecuta el conector: el archivo se copia a la carpeta del proyecto y la propia
línea se recorta del prompt. Las palabras de la indicación se reconocen **sólo en ruso y en
inglés** (en inglés, `Save the result to the file X.png`): están incrustadas en el código y no
dependen del idioma de la instalación.

El texto que deba aparecer dentro del cuadro escríbalo en el prompt **entre comillas y
literalmente**: el modelo reproduce precisamente lo que va entre comillas.

## Entrenamiento de LoRA

Está admitido por completo: el adaptador se **aplica** (el nodo `LoraLoaderModelOnly` se inserta
en el grafo sobre la marcha) y se **entrena** directamente desde AI2P, con el editor de LoRA de la
ficha del objeto del proyecto.

Entrena [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) con los scripts
`qwen_image_cache_latents.py`, `qwen_image_cache_text_encoder_outputs.py` y
`qwen_image_train_network.py` (`--model_version original`,
`--network_module networks.lora_qwen_image`), una sola tarjeta gráfica y atención mediante `sdpa`.

El paquete `musubi-tuner` (código fuente, unos 30 MB) y **Python 3.12** (unos 45 MB) se instalan
junto con el modelo, con el botón «Instalar»; un Python 3.10–3.12 que ya esté en el ordenador se
toma tal cual. El entorno con `torch` (unos 3 GB) se lo crea el entrenador en el primer
lanzamiento del entrenamiento.

**El entrenador necesita otros archivos de pesos.** musubi-tuner dice expresamente que las
compilaciones fp8 (esas mismas con las que calcula la generación) no sirven para el
entrenamiento, así que en el primer entrenamiento el `train.cmd` descarga una vez el par en
**bf16** —`qwen_image_2512_bf16.safetensors` (unos 38 GiB) y `qwen_2.5_vl_7b.safetensors` (unos
15,4 GiB)— a la subcarpeta `train` del grupo `Qwen-Image`. Eso son unos **57 GB por encima de la
instalación**, pero a cambio la generación en sí sigue funcionando con los archivos fp8 ligeros.
El VAE se coge del ya instalado.

El adaptador terminado va a
`<repositorio de modelos>/loras/<código del objeto>.safetensors`. Todas las opciones de
entrenamiento, el `dataset.toml` y el `train.cmd` están en el perfil del modelo (`lora.train`) y
se reescriben antes de cada lanzamiento: edítelos en el perfil, no en los archivos.

## Errores frecuentes

* **El proceso desaparece sin mensaje**: casi siempre es un controlador NVIDIA antiguo (véase más
  arriba).
* **«El modelo no está instalado»**: los archivos no se han terminado de descargar; abra
  «Instalar» y la ventana mostrará el volumen que queda.
* **Falta de VRAM**: reduzca `width`/`height`; 20B con 8 GB sólo funciona descargando a la memoria
  RAM y bastante más despacio.
* **El entrenamiento se cae al cargar los pesos**: compruebe el espacio libre, porque el
  entrenador necesita sus propios 57 GB (véase «Entrenamiento de LoRA»).
* **ComfyUI está ocupado por un proceso ajeno**: AI2P sólo descarga el servidor que ha arrancado
  él mismo; un ComfyUI ajeno que ya esté funcionando en el 8188 no lo toca.

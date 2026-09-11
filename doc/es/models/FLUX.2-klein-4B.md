# FLUX.2-klein-4B

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `flux2_klein_4b`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** texto → imagen, habilidades `image-generate` 88, `image-photo` 87,
`image-concept` 86, `image-text` 80

El modelo menor de la familia **FLUX.2** de Black Forest Labs: 4000 millones de parámetros y
**licencia libre Apache 2.0**, que en la familia sólo tiene él (en el 9B y en el [dev] la licencia
no es libre). Dibuja bastante mejor que SDXL y SD 3.5, y en requisitos de equipo está entre
Z-Image-Turbo y Qwen-Image.

Está creada la variante **base** de los pesos (`flux-2-klein-base-4b`): veinte pasos, `cfg 5` y
prompt negativo de verdad. Existe además el destilado (cuatro pasos, `cfg 1`), que es otro archivo
de pesos y no está en el manifiesto.

Ese mismo modelo sabe **corregir una imagen según una indicación**; para eso hay en el catálogo un
registro aparte, `FLUX.2-klein-4B-Edit`, con el mismo juego de pesos.

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, le envía el workflow y
recoge el `.png` terminado en los artefactos de la tarea.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → FLUX.2-klein-4B → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **tres archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo
  `FLUX.2-klein-4B`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `flux-2-klein-base-4b.safetensors` | ~7,2 GiB | `diffusion_models` |
| `qwen_3_4b.safetensors` | ~7,5 GiB | `text_encoders` |
| `flux2-vae.safetensors` | ~320 MiB | `vae` |

**En total, unos 16 GB de descarga.** La descarga es reanudable: una instalación interrumpida
continuará desde donde se paró, y los archivos ya descargados no se vuelven a descargar.

El grupo es común con el registro `FLUX.2-klein-4B-Edit`: instalado uno, el otro no habrá que
descargarlo. El codificador de texto `qwen_3_4b.safetensors` es el mismo archivo que el de
Z-Image, pero los grupos del repositorio de modelos no se cruzan y cada uno descarga su propio
ejemplar.

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **Apache 2.0** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/black-forest-labs/FLUX.2-klein-4B> (ficha del modelo) |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

La licencia se cotejó con la ficha del modelo el 27.08.2026 (`cardData.license: apache-2.0`).

**No lo confunda con el resto de la familia.** Apache 2.0 la tiene sólo el 4B. En
`FLUX.2-klein-9B` (incluidas las compilaciones base y fp8) y en `FLUX.2-dev` la licencia es la
**FLUX Non-Commercial License v2.1**, es decir, que el uso comercial está prohibido sin un
contrato aparte con Black Forest Labs.

Los archivos que descarga AI2P son un reempaquetado de Comfy-Org
(<https://huggingface.co/Comfy-Org/vae-text-encorder-for-flux-klein-4b>) bajo esa misma Apache
2.0. Hace falta además porque los repositorios originales de Black Forest Labs se reparten
mediante consentimiento con la licencia: sin un token de HuggingFace, la descarga desde ellos
responde `401 Unauthorized`.

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto; pero si usted le lleva a
alguien un paquete con ComfyUI dentro, las condiciones de la GPL se aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **desde 12 GB de VRAM** (16 GB cómodo) |
| Controlador NVIDIA | **580 o más nuevo**; con un controlador antiguo torch se cae sin mensaje |
| Espacio en disco | unos 19 GB (pesos + paquete ComfyUI) |
| Memoria RAM | desde 16 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

En una tarjeta de 8 GB el modelo también arranca —ComfyUI descarga partes del modelo a la memoria
RAM—, pero el fotograma se calcula varias veces más despacio. Si la tarjeta es floja, coja
`Z-Image-Turbo` o `SDXL-1.0`.

## Cuánto hay que esperar

Veinte pasos en un fotograma de 1024×1024 son **decenas de segundos** en una tarjeta actual. El
tiempo de espera del encargo está puesto en el perfil en 60 minutos (`params.timeoutMinutes`).

La marcha de la generación se ve en la **consola del encargo** de la ficha de la tarea: allí se
vuelca la salida propia de ComfyUI junto con su barra de progreso.

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `width` / `height` | 1024 × 1024 | resolución del fotograma |
| `steps` | 20 | pasos de difusión |
| `negative` | vacío | prompt negativo: **sí tiene efecto** (pesos base, `cfg 5`) |
| `timeoutMinutes` | 60 | cuánto esperar el resultado |

En FLUX.2 la planificación de los pasos depende del tamaño del fotograma, así que el número de
pasos no se le indica al muestreador, sino al nodo `Flux2Scheduler`; en la plantilla ya está
hecho.

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
`flux_2_cache_latents.py`, `flux_2_cache_text_encoder_outputs.py` y `flux_2_train_network.py`
(`--network_module networks.lora_flux_2`, `--model_version klein-base-4b`), una sola tarjeta
gráfica y atención mediante `sdpa`.

El paquete `musubi-tuner` (código fuente, unos 30 MB) y **Python 3.12** (unos 45 MB) se instalan
junto con el modelo, con el botón «Instalar»; un Python 3.10–3.12 que ya esté en el ordenador se
toma tal cual. El entorno con `torch` (unos 3 GB) se lo crea el entrenador en el primer
lanzamiento del entrenamiento.

**Diferencia con Z-Image y Qwen-Image.** Allí la generación calcula sobre compilaciones fp8 que el
entrenador no acepta, y el `train.cmd` descarga su propio par de pesos. Aquí no hay nada que
descargar: en el manifiesto está el propio checkpoint base, y el entrenamiento va con esos mismos
archivos que ya están instalados. El autor del entrenador aconseja expresamente entrenar
precisamente sobre los pesos base de klein.

Un adaptador entrenado con este registro sirve también para `FLUX.2-klein-4B-Edit`: el modelo que
hay detrás es el mismo.

El adaptador terminado va a
`<repositorio de modelos>/loras/<código del objeto>.safetensors`. Todas las opciones de
entrenamiento, el `dataset.toml` y el `train.cmd` están en el perfil del modelo (`lora.train`) y
se reescriben antes de cada lanzamiento: edítelos en el perfil, no en los archivos.

## Errores frecuentes

* **El proceso desaparece sin mensaje**: casi siempre es un controlador NVIDIA antiguo (véase más
  arriba).
* **«El modelo no está instalado»**: los archivos no se han terminado de descargar; abra
  «Instalar» y la ventana mostrará el volumen que queda.
* **Falta de VRAM**: reduzca `width`/`height` o coja un modelo más ligero.
* **`401 Unauthorized` al descargar usted a mano**: está descargando del repositorio de Black
  Forest Labs; en el manifiesto de AI2P están los reempaquetados abiertos de Comfy-Org.
* **ComfyUI está ocupado por un proceso ajeno**: AI2P sólo descarga el servidor que ha arrancado
  él mismo; un ComfyUI ajeno que ya esté funcionando en el 8188 no lo toca.

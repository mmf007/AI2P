# FLUX.2-klein-4B-Edit

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `flux2_klein_4b_edit`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** imagen + indicación → imagen, habilidades `image-edit` 86,
`image-inpaint` 83, `image-generate` 80, `image-text` 78

El mismísimo modelo de `FLUX.2-klein-4B`, pero en modo de **edición de imagen**: a la entrada van
la imagen de origen y una indicación con palabras («pinta la pared de azul», «quita los cables»,
«cambia el fondo por uno de atardecer»), y a la salida sale el fotograma corregido. Los pesos son
los mismos y sólo cambia el grafo de generación.

La licencia es **Apache 2.0**, la única libre de la familia FLUX.2.

Funciona a través de **ComfyUI**: AI2P sube la imagen de origen al motor
(`POST /upload/image`) y pone su nombre en la plantilla, y el `.png` terminado lo recoge en los
artefactos de la tarea.

## De dónde sale la imagen de origen

La ruta del archivo se indica en la descripción de la tarea relativa a la carpeta del proyecto, o
bien con una referencia a un objeto del proyecto (`@obj:OBJ-3`), y entonces se ponen sus
fotogramas de referencia. Sin imagen de origen el encargo de este modelo no se lanza: está
declarada **obligatoria** (`refImage.required`).

El tamaño del resultado se toma de la propia imagen (el nodo `GetImageSize`), así que el `width` y
el `height` del perfil no participan en este modo. Antes de la edición la imagen se comprime hasta
un megapíxel, tal como está hecho también en la plantilla oficial de ComfyUI.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → FLUX.2-klein-4B-Edit → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **tres archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo
  `FLUX.2-klein-4B`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `flux-2-klein-base-4b.safetensors` | ~7,2 GiB | `diffusion_models` |
| `qwen_3_4b.safetensors` | ~7,5 GiB | `text_encoders` |
| `flux2-vae.safetensors` | ~320 MiB | `vae` |

**En total, unos 16 GB de descarga**, pero el **grupo es común** con el registro
`FLUX.2-klein-4B`: si ese ya está instalado, no habrá que descargar nada y ambos registros pasarán
a estar activos de inmediato.

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
Libre es sólo la versión 4B: en `FLUX.2-klein-9B` y `FLUX.2-dev` es la **FLUX Non-Commercial
License v2.1** (el uso comercial está prohibido sin contrato con Black Forest Labs).

Los archivos se descargan del reempaquetado abierto de Comfy-Org
(<https://huggingface.co/Comfy-Org/vae-text-encorder-for-flux-klein-4b>) bajo esa misma Apache
2.0: los repositorios originales de Black Forest Labs, sin token de HuggingFace, responden
`401 Unauthorized`.

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

## Cuánto hay que esperar

Veinte pasos en un fotograma de alrededor de un megapíxel son **decenas de segundos** en una
tarjeta actual. La edición tarda un poco más que dibujar desde cero: la imagen de origen se
codifica además en latente. El tiempo de espera del encargo está puesto en el perfil en 60 minutos
(`params.timeoutMinutes`).

La marcha de la generación se ve en la **consola del encargo** de la ficha de la tarea.

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `steps` | 20 | pasos de difusión |
| `negative` | vacío | prompt negativo: sí tiene efecto (`cfg 5`) |
| `timeoutMinutes` | 60 | cuánto esperar el resultado |
| `width` / `height` | 1024 × 1024 | **en este modo no tienen efecto**: el tamaño se toma de la imagen de origen |

La descripción entera de la tarea se va al prompt. Escriba una indicación, no la descripción de
toda la escena: el modelo corrige lo que se nombra y procura no tocar el resto.

## Entrenamiento de LoRA

Está admitido por completo: el adaptador se **aplica** (el nodo `LoraLoaderModelOnly` se inserta
en el grafo sobre la marcha) y se **entrena** directamente desde AI2P, con el editor de LoRA de la
ficha del objeto del proyecto.

Entrena [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) con los scripts
`flux_2_cache_latents.py`, `flux_2_cache_text_encoder_outputs.py` y `flux_2_train_network.py`
(`--network_module networks.lora_flux_2`, `--model_version klein-base-4b`). No se descarga nada de
más: el entrenador aprende sobre ese mismo checkpoint base que está instalado para la generación.

El paquete `musubi-tuner` (código fuente, unos 30 MB) y **Python 3.12** (unos 45 MB) se instalan
junto con el modelo, con el botón «Instalar». El entorno con `torch` (unos 3 GB) se lo crea el
entrenador en el primer lanzamiento del entrenamiento.

El adaptador es común a ambos registros: el entrenado aquí funciona también en
`FLUX.2-klein-4B`, y al revés, porque el modelo que hay detrás es uno solo.

El adaptador terminado va a
`<repositorio de modelos>/loras/<código del objeto>.safetensors`. Todas las opciones de
entrenamiento, el `dataset.toml` y el `train.cmd` están en el perfil del modelo (`lora.train`) y
se reescriben antes de cada lanzamiento: edítelos en el perfil, no en los archivos.

## Errores frecuentes

* **«El modelo no acepta imagen»**: el encargo se ha lanzado sobre un registro sin edición; para
  editar coja precisamente `FLUX.2-klein-4B-Edit`.
* **No se encuentra el archivo de origen**: la ruta se cuenta **desde la carpeta del proyecto**, no
  desde la raíz del disco.
* **El modelo lo ha redibujado todo**: la indicación era una descripción de la escena; escriba qué
  hay que cambiar exactamente y añada «no cambiar el resto».
* **Falta de VRAM**: reduzca la imagen de origen; se comprime hasta un megapíxel, pero los
  archivos muy grandes siguen siendo más pesados.
* **ComfyUI está ocupado por un proceso ajeno**: AI2P sólo descarga el servidor que ha arrancado
  él mismo.

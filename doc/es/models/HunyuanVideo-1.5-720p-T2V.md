# HunyuanVideo-1.5-720p-T2V

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `hunyuanvideo15_720p_t2v`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** texto → vídeo, habilidad `video-generate` 79

Modelo de vídeo de Tencent, versión **1.5**. De las tres grandes líneas abiertas del catálogo
(Wan 2.2, HunyuanVideo 1.5, LTX-2.5) esta es la menos exigente en memoria de vídeo y la única que
**de serie calcula en 720p** y no en 480p.

El segundo codificador de texto (`byt5_small_glyphxl`) se encarga de los **rótulos dentro del
cuadro**; si en el clip hace falta un texto legible, es una diferencia notable, y por eso en el
grafo está `DualCLIPLoader` y no el cargador normal de un solo codificador.

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, le envía el workflow y
recoge el `.mp4` terminado en los artefactos de la tarea.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → HunyuanVideo-1.5-720p-T2V → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **cuatro archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo
  `HunyuanVideo-1.5`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `hunyuanvideo1.5_720p_t2v_fp16.safetensors` | ~15,5 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `byt5_small_glyphxl_fp16.safetensors` | ~418 MiB | `text_encoders` |
| `hunyuanvideo15_vae_fp16.safetensors` | ~2,3 GiB | `vae` |

**En total, unos 28,6 GB de descarga.** La descarga es reanudable: una instalación interrumpida
continuará desde donde se paró.

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **Tencent Hunyuan Community License** (no es Apache ni MIT) |
| Uso comercial | permitido, pero con salvedades del titular de los derechos |
| Qué es obligatorio | aceptar las condiciones de la licencia y conservar el aviso; la licencia tiene límites por número de usuarios del producto y por territorios |
| Texto de la licencia | <https://github.com/Tencent-Hunyuan/HunyuanVideo-1.5/blob/master/LICENSE> |
| Ficha del modelo | <https://huggingface.co/tencent/HunyuanVideo-1.5> |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

**No** es una licencia libre en el sentido de Apache/MIT: antes de un lanzamiento comercial lea su
texto entero, porque allí hay condiciones que no tienen ni Wan 2.2 ni Kandinsky. La licencia se
cotejó con la ficha del modelo el 27.08.2026 (`license_name: tencent-hunyuan-community`).

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto; pero si usted le lleva a
alguien un paquete con ComfyUI dentro, las condiciones de la GPL se aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **desde 12 GB de VRAM** (16 GB cómodo) |
| Controlador NVIDIA | **580 o más nuevo**; con un controlador antiguo torch se cae sin mensaje |
| Espacio en disco | unos 31 GB (pesos + paquete ComfyUI), más unos 50 GB si va a entrenar LoRA |
| Memoria RAM | desde 32 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

Si no basta la memoria de vídeo, en el perfil se puede cambiar el `weight_dtype` del nodo de carga
a `fp8_e4m3fn`; es lo que aconseja la propia plantilla oficial de ComfyUI.

## Cuánto hay que esperar

Un clip de 1280×720 y 121 fotogramas (5 segundos a 24 fotogramas por segundo) son **decenas de
minutos** en una tarjeta actual (20 pasos). El tiempo de espera del encargo está puesto en el
perfil en 240 minutos (`params.timeoutMinutes`). La marcha de la generación se ve en la **consola
del encargo** de la ficha de la tarea.

## Parámetros de generación

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `width` / `height` | 1280 × 720 | resolución del fotograma |
| `length` | 121 | fotogramas del clip; 24 fotogramas por segundo, es decir, 5 segundos |
| `steps` | 20 | pasos de difusión |
| `negative` | vacío | prompt negativo (la fuerza de seguimiento del texto en la plantilla es 6) |
| `timeoutMinutes` | 240 | cuánto esperar el resultado |

En la plantilla oficial de ComfyUI, junto a la generación básica, hay además un aumento a 1080p y
el acelerador EasyCache, que allí están **desactivados** y a nuestra plantilla no se han traído:
son archivos de pesos aparte que no están en el manifiesto.

La descripción entera de la tarea se va al prompt. Una indicación del tipo «результат положить в
файл X.mp4» la ejecuta el conector: el archivo se copia a la carpeta del proyecto y la propia
línea se recorta del prompt (esas palabras se reconocen sólo en ruso y en inglés).

## Entrenamiento de LoRA

Está admitido por completo: el adaptador se **aplica** (el nodo `LoraLoaderModelOnly` se inserta
en el grafo sobre la marcha) y se **entrena** directamente desde AI2P, con el editor de LoRA de la
ficha del objeto del proyecto.

Entrena [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) con los scripts
`hv_1_5_cache_latents.py`, `hv_1_5_cache_text_encoder_outputs.py` y `hv_1_5_train_network.py`
(`--task t2v`, `--network_module networks.lora_hv_1_5`), una sola tarjeta gráfica y atención
mediante `sdpa`.

El paquete `musubi-tuner` (código fuente, unos 30 MB) y **Python 3.12** (unos 45 MB) se instalan
junto con el modelo; el entorno con `torch` (unos 3 GB) se lo crea el entrenador en el primer
lanzamiento del entrenamiento.

**Lo que hay que saber de antemano.** La generación calcula sobre un reempaquetado fp16 y sobre un
codificador de texto aligerado en fp8, y al entrenador no le sirve ni lo uno ni lo otro
([su documentación](https://github.com/kohya-ss/musubi-tuner/blob/main/docs/hunyuan_video_1_5.md)
pide expresamente el DiT original y el codificador completo). Por eso, en el primer entrenamiento
el `train.cmd` descarga una vez el `diffusion_pytorch_model.safetensors` original (unos 31 GB) y
el `qwen_2.5_vl_7b.safetensors` completo (unos 15,5 GB) a la subcarpeta `train` del grupo
`HunyuanVideo-1.5`. En total, unos 50 GB por encima de la instalación: reserve espacio en el
disco. El VAE y el codificador de rótulos (`byt5`) se cogen de los ya instalados.

El adaptador terminado va a
`<repositorio de modelos>/loras/<código del objeto>.safetensors`. Todas las opciones de
entrenamiento, el `dataset.toml` y el `train.cmd` están en el perfil del modelo (`lora.train`) y
se reescriben antes de cada lanzamiento: edítelos en el perfil, no en los archivos.

## Errores frecuentes

* **Falta de VRAM**: cambie el `weight_dtype` a `fp8_e4m3fn` en la plantilla del workflow o reduzca
  `width`/`height`.
* **«El modelo no está instalado»**: los archivos no se han terminado de descargar; abra
  «Instalar» y la ventana mostrará el volumen que queda.
* **El proceso desaparece sin mensaje**: casi siempre es un controlador NVIDIA antiguo.
* **ComfyUI está ocupado por un proceso ajeno**: AI2P sólo descarga el servidor que ha arrancado
  él mismo; un ComfyUI ajeno que ya esté funcionando en el 8188 no lo toca.

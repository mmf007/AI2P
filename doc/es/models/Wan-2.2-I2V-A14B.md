# Wan-2.2-I2V-A14B

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `wan2.2_i2v_a14b`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** imagen + texto → vídeo, habilidades `video-animate` 80, `video-generate` 74

La misma línea abierta de Alibaba **Wan 2.2** que Wan-2.2-T2V-A14B, pero el clip se dibuja **a
partir de una imagen**: el fotograma inicial fija el personaje, el encuadre y el estilo, y el
prompt describe sólo el movimiento. Es la forma más predecible de obtener un clip con el héroe que
hace falta: el aspecto se toma del fotograma y no se recuenta con palabras.

El modelo es compuesto (MoE), con dos expertos de 14 000 millones de parámetros: el «ruidoso»
dibuja la primera mitad de los pasos y el «limpio» remata la segunda.

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, sube el fotograma inicial,
le envía el workflow y recoge el `.mp4` terminado en los artefactos de la tarea.

## De dónde sale el fotograma inicial

El encargo está obligado a nombrarlo: sin imagen el modelo no arranca en absoluto (la negativa
llega enseguida y no al cabo de media hora de cálculo). Hay dos formas:

* una **referencia a un objeto del proyecto**, `@obj:OBJ-3` en la descripción de la tarea: la ruta
  del fotograma de referencia se pone sola y el pasaporte del objeto se va al prompt;
* la **ruta del archivo** relativa a la carpeta del proyecto, en una línea aparte de la
  descripción.

Se aceptan `.png`, `.jpg` y `.webp`; el fotograma se escala al `width`×`height` del perfil.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → Wan-2.2-I2V-A14B → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **cuatro archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo
  `Wan-2.2-I2V`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `wan2.2_i2v_high_noise_14B_fp8_scaled.safetensors` | ~13,3 GiB | `diffusion_models` |
| `wan2.2_i2v_low_noise_14B_fp8_scaled.safetensors` | ~13,3 GiB | `diffusion_models` |
| `umt5_xxl_fp8_e4m3fn_scaled.safetensors` | ~6,3 GiB | `text_encoders` |
| `wan_2.1_vae.safetensors` | ~242 MiB | `vae` |

**En total, unos 35,6 GB de descarga.** El grupo es propio: los archivos de pesos de «texto →
vídeo» y de «imagen → vídeo» son distintos, mientras que el codificador y el VAE son los mismos,
así que si están instalados ambos registros, esos dos archivos están en el disco dos veces.

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **Apache 2.0** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/Wan-AI/Wan2.2-I2V-A14B> (ficha del modelo) |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

Los archivos que descarga AI2P son un reempaquetado de Comfy-Org
(<https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged>), que va bajo esa misma Apache 2.0.
La licencia se cotejó con la ficha del modelo el 27.08.2026.

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto; pero si usted le lleva a
alguien un paquete con ComfyUI dentro, las condiciones de la GPL se aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **desde 16 GB de VRAM** (24 GB cómodo) |
| Controlador NVIDIA | **580 o más nuevo**; con un controlador antiguo torch se cae sin mensaje |
| Espacio en disco | unos 38 GB (pesos + paquete ComfyUI), más unos 68 GB si va a entrenar LoRA |
| Memoria RAM | desde 32 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

## Cuánto hay que esperar

Un clip de 832×480 y 81 fotogramas (5 segundos a 16 fotogramas por segundo) son **decenas de
minutos** en una tarjeta actual. El tiempo de espera del encargo está puesto en el perfil en 240
minutos (`params.timeoutMinutes`). La marcha de la generación se ve en la **consola del encargo**
de la ficha de la tarea.

## Parámetros de generación

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `width` / `height` | 832 × 480 | resolución del fotograma; a ella se ajusta también el inicial |
| `length` | 81 | fotogramas del clip; 16 fotogramas por segundo, es decir, 5 segundos |
| `steps` | 20 | pasos de difusión para los dos expertos juntos |
| `negative` | vacío | prompt negativo |
| `timeoutMinutes` | 240 | cuánto esperar el resultado |

**Sobre el número de pasos.** La frontera entre expertos está incrustada en la plantilla del
workflow con el número 10, la mitad de veinte. Al cambiar `steps`, corrija el `end_at_step` del
primer muestreador y el `start_at_step` del segundo en el archivo `models/workflow_….json` de la
carpeta de la organización: el conector no hace aritmética con los parámetros.

## Entrenamiento de LoRA

Está admitido por completo: el adaptador se **aplica** (el nodo `LoraLoaderModelOnly` se inserta
en el grafo sobre la marcha) y se **entrena** directamente desde AI2P, con el editor de LoRA de la
ficha del objeto del proyecto.

Entrena [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) con los scripts
`wan_cache_latents.py` (con la opción `--i2v`), `wan_cache_text_encoder_outputs.py` y
`wan_train_network.py` (`--task i2v-A14B`, `--network_module networks.lora_wan`). El adaptador se
entrena a la vez sobre ambos expertos: `--dit` y `--dit_high_noise`.

El paquete `musubi-tuner` y **Python 3.12** se instalan junto con el modelo; el entorno con
`torch` (unos 3 GB) se lo crea el entrenador en el primer lanzamiento del entrenamiento.

**Lo que hay que saber de antemano.** La generación calcula sobre compilaciones `fp8_scaled`, y el
entrenador no las acepta
([lo dice expresamente su documentación](https://github.com/kohya-ss/musubi-tuner/blob/main/docs/wan.md)),
así que en el primer entrenamiento el `train.cmd` descarga una vez su propio par en **fp16** (unos
57 GB) y el codificador de texto original `models_t5_umt5-xxl-enc-bf16.pth` (unos 11 GB) a la
subcarpeta `train` del grupo `Wan-2.2-I2V`. En total, unos 68 GB por encima de la instalación.

El adaptador terminado va a
`<repositorio de modelos>/loras/<código del objeto>.safetensors`. Todas las opciones de
entrenamiento, el `dataset.toml` y el `train.cmd` están en el perfil del modelo (`lora.train`) y
se reescriben antes de cada lanzamiento: edítelos en el perfil, no en los archivos.

## Errores frecuentes

* **«El encargo exige un fotograma inicial»**: en la descripción de la tarea no hay ni referencia
  `@obj:` ni ruta de imagen; para dibujar desde cero coja el registro Wan-2.2-T2V-A14B.
* **El primer fotograma «se desdibuja»**: la imagen inicial se diferencia mucho en proporciones de
  `width`×`height`; ajústela de antemano o corrija los tamaños en el perfil.
* **Falta de VRAM**: reduzca `width`/`height` o `length`.
* **El proceso desaparece sin mensaje**: casi siempre es un controlador NVIDIA antiguo.

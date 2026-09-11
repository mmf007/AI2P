# Wan-2.2-T2V-A14B

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `wan2.2_t2v_a14b`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** texto → vídeo, habilidad `video-generate` 80

Modelo de vídeo de Alibaba, línea abierta **Wan 2.2** (las versiones 2.5 y superiores son
productos de API cerrados y no tienen pesos). Por calidad general del clip es el más potente de
los modelos abiertos del catálogo después de LTX-2.5, y bastante más potente que Kandinsky 5.0
Video Lite.

El modelo es **compuesto** (MoE): tiene dos expertos de 14 000 millones de parámetros cada uno. El
«ruidoso» dibuja la primera mitad de los pasos —la composición general y el movimiento— y el
«limpio» remata la segunda: los detalles y la nitidez. Por eso en el manifiesto de instalación hay
dos archivos de pesos, y en el grafo de ComfyUI, dos muestreadores que se pasan el latente el uno
al otro.

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, le envía el workflow y
recoge el `.mp4` terminado en los artefactos de la tarea.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → Wan-2.2-T2V-A14B → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **cuatro archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo
  `Wan-2.2-T2V`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `wan2.2_t2v_high_noise_14B_fp8_scaled.safetensors` | ~13,3 GiB | `diffusion_models` |
| `wan2.2_t2v_low_noise_14B_fp8_scaled.safetensors` | ~13,3 GiB | `diffusion_models` |
| `umt5_xxl_fp8_e4m3fn_scaled.safetensors` | ~6,3 GiB | `text_encoders` |
| `wan_2.1_vae.safetensors` | ~242 MiB | `vae` |

**En total, unos 35,6 GB de descarga.** La descarga es reanudable: una instalación interrumpida
continuará desde donde se paró, y los archivos ya descargados no se vuelven a descargar.

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **Apache 2.0** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/Wan-AI/Wan2.2-T2V-A14B> (ficha del modelo) |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

Los archivos que descarga AI2P son un reempaquetado de Comfy-Org
(<https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged>), que va bajo esa misma Apache 2.0.
La licencia se cotejó con la ficha del modelo el 27.08.2026; en los modelos abiertos cambia poco,
pero antes de un lanzamiento comercial compruebe la ficha otra vez.

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

Los expertos se cargan por turno, así que en memoria entra a la vez uno de los dos archivos: con
16 GB de VRAM un clip de 832×480 se calcula, aunque no rápido.

## Cuánto hay que esperar

Un clip de 832×480 y 81 fotogramas (5 segundos a 16 fotogramas por segundo) son **decenas de
minutos** en una tarjeta actual: 20 pasos por los dos expertos en cada fotograma. El tiempo de
espera del encargo está puesto en el perfil en 240 minutos (`params.timeoutMinutes`).

La marcha de la generación se ve en la **consola del encargo** de la ficha de la tarea: allí se
vuelca la salida propia de ComfyUI junto con su barra de progreso.

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `width` / `height` | 832 × 480 | resolución del fotograma, la nativa del 14B |
| `length` | 81 | fotogramas del clip; 16 fotogramas por segundo, es decir, 5 segundos |
| `steps` | 20 | pasos de difusión para los dos expertos juntos |
| `negative` | vacío | prompt negativo |
| `timeoutMinutes` | 240 | cuánto esperar el resultado |

**Sobre el número de pasos.** La frontera entre expertos (después de qué paso el «ruidoso» le pasa
el trabajo al «limpio») está incrustada en la plantilla del workflow con el número 10, la mitad de
veinte. Si cambia `steps`, corríjala también: el `end_at_step` del primer muestreador y el
`start_at_step` del segundo, en el archivo `models/workflow_….json` de la carpeta de la
organización. El conector no hace aritmética con los parámetros, así que la frontera no se
recalcula sola.

El modo turbo de la plantilla oficial de ComfyUI (4 pasos en lugar de 20) no se ha traído aquí:
necesita archivos aparte de Lightning-LoRA, que no están en el manifiesto de instalación.

La descripción entera de la tarea se va al prompt. Una indicación del tipo «результат положить в
файл X.mp4» la ejecuta el conector: el archivo se copia a la carpeta del proyecto y la propia
línea se recorta del prompt (esas palabras se reconocen sólo en ruso y en inglés).

## Entrenamiento de LoRA

Está admitido por completo: el adaptador se **aplica** (el nodo `LoraLoaderModelOnly` se inserta
en el grafo sobre la marcha) y se **entrena** directamente desde AI2P, con el editor de LoRA de la
ficha del objeto del proyecto.

Entrena [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) con los scripts
`wan_cache_latents.py`, `wan_cache_text_encoder_outputs.py` y `wan_train_network.py`
(`--task t2v-A14B`, `--network_module networks.lora_wan`), una sola tarjeta gráfica y atención
mediante `sdpa`. El adaptador se entrena a la vez sobre ambos expertos: el «limpio» se indica con
la opción `--dit` y el «ruidoso» con `--dit_high_noise`.

El paquete `musubi-tuner` (código fuente, unos 30 MB) y **Python 3.12** (unos 45 MB) se instalan
junto con el modelo, con el botón «Instalar»; un Python 3.10–3.12 que ya esté en el ordenador se
toma tal cual. El entorno con `torch` (unos 3 GB) se lo crea el entrenador en el primer
lanzamiento del entrenamiento.

**Lo que hay que saber de antemano.** La generación calcula sobre compilaciones `fp8_scaled`, que
pesan la mitad y son las que instala el botón «Instalar». El entrenador no acepta esas
compilaciones ([lo dice expresamente su documentación](https://github.com/kohya-ss/musubi-tuner/blob/main/docs/wan.md)),
así que en el primer entrenamiento el `train.cmd` descarga una vez **su propio par en fp16** (unos
57 GB) y el codificador de texto original `models_t5_umt5-xxl-enc-bf16.pth` (unos 11 GB) a la
subcarpeta `train` del grupo `Wan-2.2-T2V`. En total, unos 68 GB por encima de la instalación:
reserve espacio en el disco. Esos archivos no se pueden meter en el manifiesto: de su composición
se calcula el atributo «modelo instalado», y el modelo se habría apagado para todos los que no
entrenan adaptadores.

El adaptador terminado va a
`<repositorio de modelos>/loras/<código del objeto>.safetensors`. Todas las opciones de
entrenamiento, el `dataset.toml` y el `train.cmd` están en el perfil del modelo (`lora.train`) y
se reescriben antes de cada lanzamiento: edítelos en el perfil, no en los archivos.

## Errores frecuentes

* **El proceso desaparece sin mensaje**: casi siempre es un controlador NVIDIA antiguo (véase más
  arriba).
* **«El modelo no está instalado»**: los archivos no se han terminado de descargar; abra
  «Instalar» y la ventana mostrará el volumen que queda.
* **Falta de VRAM**: reduzca `width`/`height` o `length`; 81 fotogramas a 720p con el modelo 14B
  ya exigen 24 GB.
* **El clip sale turbio o «a saltos»**: compruebe que no se ha descuadrado la frontera entre
  expertos después de cambiar `steps` (véase «Parámetros de generación»).
* **ComfyUI está ocupado por un proceso ajeno**: AI2P sólo descarga el servidor que ha arrancado
  él mismo; un ComfyUI ajeno que ya esté funcionando en el 8188 no lo toca.

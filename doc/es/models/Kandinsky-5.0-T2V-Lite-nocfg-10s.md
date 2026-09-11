# Kandinsky-5.0-T2V-Lite-nocfg-10s

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `kandinsky5lite_t2v_nocfg_10s`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** texto → vídeo, habilidad `video-generate` 74

Variante del modelo de vídeo abierto ruso **Kandinsky 5.0 Video Lite** (2000 millones de
parámetros) de Kandinsky Lab. Entiende el prompt en ruso y dibuja **cirílico dentro del cuadro**,
algo que no sabe hacer ningún otro modelo del catálogo.

Variante **no-CFG**: los mismos 50 pasos, pero el modelo está entrenado para funcionar sin una segunda pasada sobre el texto negativo, y por eso calcula el doble de rápido que el original. El precio es una precisión algo menor al seguir una descripción compleja.

La duración del clip es de **10 segundos** (241 fotogramas a 24 fotogramas por segundo):
la duración está incrustada en los propios pesos, y por eso la línea tiene archivos separados
para 5 y para 10 segundos.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → Kandinsky-5.0-T2V-Lite-nocfg-10s → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **cuatro archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo
  `Kandinsky-5`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `kandinsky5lite_t2v_nocfg_10s.safetensors` | ~4,3 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `clip_l.safetensors` | ~235 MiB | `text_encoders` |
| `hunyuan_video_vae_bf16.safetensors` | ~470 MiB | `vae` |

**En total, unos 14,6 GB de descarga**: el más ligero de los modelos de vídeo del catálogo. El
grupo `Kandinsky-5` es común a toda la línea: si al lado hay otra variante suya, sólo se
descargará su propio archivo de pesos (unos 4,6 GB), porque los codificadores y el VAE ya están
en su sitio.

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **MIT** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/kandinskylab/Kandinsky-5.0-T2V-Lite-nocfg-10s> (ficha del modelo) |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

MIT es la más libre de las licencias de los modelos de vídeo del catálogo: en Wan 2.2 es Apache
2.0, y en HunyuanVideo 1.5 y LTX-2.5 son acuerdos propios de los titulares de derechos. La
licencia se cotejó con la ficha del modelo el 27.08.2026.

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto;
pero si usted le lleva a alguien un paquete con ComfyUI dentro, las condiciones de la GPL se
aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **desde 12 GB de VRAM** (24 GB cómodo, y para los clips de diez segundos, obligatorio) |
| Controlador NVIDIA | **580 o más nuevo**; con un controlador antiguo torch se cae sin mensaje |
| Espacio en disco | unos 17 GB (pesos + paquete ComfyUI) |
| Memoria RAM | desde 16 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

## Cuánto hay que esperar

Cincuenta pasos, pero sin la segunda pasada sobre el texto negativo: aproximadamente el doble de rápido que la variante original, **decenas de minutos** en una tarjeta actual.

El tiempo de espera del encargo está puesto en el perfil en 180 minutos
(`params.timeoutMinutes`). La marcha de la generación se ve en la **consola del encargo** de la
ficha de la tarea: allí se vuelca la salida propia de ComfyUI junto con su barra de progreso.

## Parámetros de generación

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `width` / `height` | 768 × 512 | resolución del fotograma |
| `length` | 241 | fotogramas del clip; 24 fotogramas por segundo, es decir, 10 s |
| `steps` | 50 | pasos de difusión: los mismos que tiene la tarea de entrenamiento de esta variante |
| `negative` | vacío | prompt negativo: **en esta variante no tiene efecto**, porque está entrenada para calcular sin la segunda pasada |
| `timeoutMinutes` | 180 | cuánto esperar el resultado |

La descripción entera de la tarea se va al prompt. Una indicación del tipo «результат положить в
файл X.mp4» la ejecuta el conector: el archivo se copia a la carpeta del proyecto y la propia
línea se recorta del prompt. Las palabras de la indicación se reconocen **sólo en ruso y en
inglés** (en inglés, `Save the result to the file X.mp4`): están incrustadas en el código y no
dependen del idioma de la instalación.

## Entrenamiento de LoRA

Está admitido por completo: el adaptador se **aplica** (el nodo `LoraLoaderModelOnly` se inserta
en el grafo sobre la marcha) y se **entrena** directamente desde AI2P, con el editor de LoRA de
la ficha del objeto del proyecto.

Entrena [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) con los scripts
`kandinsky5_cache_latents.py`, `kandinsky5_cache_text_encoder_outputs.py` y
`kandinsky5_train_network.py` (`--task k5-lite-t2v-10s-nocfg-sd`,
`--network_module networks.lora_kandinsky`), una sola tarjeta gráfica y atención mediante `sdpa`.
La tarea del entrenador es propia de cada variante —fija tanto el número de pasos como la
planificación—, así que no se puede poner la de otra.

El paquete `musubi-tuner` (código fuente, unos 30 MB) y **Python 3.12** (unos 45 MB) se instalan
junto con el modelo; el entorno con `torch` (unos 3 GB) se lo crea el entrenador en el primer
lanzamiento del entrenamiento. No hace falta descargar más pesos, a diferencia de Wan 2.2 y
HunyuanVideo 1.5: el archivo DiT no es fp8 de todos modos, y sus codificadores de texto se los
coge el entrenador de HuggingFace por su cuenta.

El adaptador terminado va a
`<repositorio de modelos>/loras/<código del objeto>.safetensors`. Todas las opciones de
entrenamiento, el `dataset.toml` y el `train.cmd` están en el perfil del modelo (`lora.train`) y
se reescriben antes de cada lanzamiento: edítelos en el perfil, no en los archivos.

## Errores frecuentes

* **El proceso desaparece sin mensaje**: casi siempre es un controlador NVIDIA antiguo (véase más
  arriba).
* **«El modelo no está instalado»**: los archivos no se han terminado de descargar; abra
  «Instalar» y la ventana mostrará el volumen que queda.
* **Falta de VRAM**: reduzca `width`/`height`; un clip de 241 fotogramas mantiene en memoria
  el latente entero.
* **ComfyUI está ocupado por un proceso ajeno**: AI2P sólo descarga el servidor que ha arrancado
  él mismo; un ComfyUI ajeno que ya esté funcionando en el 8188 no lo toca.

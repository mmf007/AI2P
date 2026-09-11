# Kandinsky-5.0-T2V-Lite-sft-5s

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`,
modelo `kandinsky5lite_t2v_sft_5s`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** texto → vídeo (5 segundos), habilidades `video-generate` 76, `video-animate` 72

Modelo de generación de vídeo de la familia Kandinsky 5.0 (variante Lite, con ajuste fino, 5
segundos). Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, le envía el
workflow y recoge el `.mp4` terminado en los artefactos de la tarea.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos de IA → Kandinsky-5.0-T2V-Lite-sft-5s → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **cuatro archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`, por defecto
  `C:\ai` en Windows y `~/ai` en Linux/macOS):

| Archivo | Tamaño | Adónde |
|---|---|---|
| `Kandinsky-5.0-T2V-Lite-sft-5s.safetensors` | ~4,3 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `clip_l.safetensors` | ~235 MiB | `text_encoders` |
| `hunyuan_video_vae_bf16.safetensors` | ~470 MiB | `vae` |

**En total, unos 16 GB de descarga.** La descarga es reanudable: una instalación interrumpida
continuará desde donde se paró, y los archivos ya descargados no se vuelven a descargar.

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **MIT** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/kandinskylab/Kandinsky-5.0-T2V-Lite-sft-5s> (ficha del modelo) |
| Pago por la generación | no lo hay: calcula su ordenador |

La licencia se tomó de la ficha del modelo el 27.08.2026 con una petición a HuggingFace (el campo
`cardData.license`), no de memoria. MIT es la más libre de las licencias del catálogo: el uso
comercial está permitido, y la única exigencia es conservar el texto de la licencia y el aviso de
derechos de autor.

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto; pero si usted le lleva a
alguien un paquete con ComfyUI dentro, las condiciones de la GPL se aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **mínimo 6 GB de VRAM** |
| Controlador NVIDIA | **580 o más nuevo**; véase la advertencia de abajo |
| Espacio en disco | unos 18 GB (pesos + paquete ComfyUI) |
| Memoria RAM | desde 16 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

**Sobre el controlador, algo importante.** En el paquete de ComfyUI va torch compilado para CUDA
13.0. Con un controlador antiguo (por ejemplo, el 527.99 = CUDA 12.0) no falla con un error
comprensible, sino con una **infracción de acceso**: el proceso simplemente desaparece. No es un
defecto de AI2P: actualice el controlador NVIDIA al 580 o superior (comprobado con el 610.x). Si
el modelo «no arranca en silencio», empiece por `nvidia-smi` y la versión del controlador.

## Cuánto hay que esperar

Con 6 GB de VRAM, generar un clip de **768×512, 121 fotogramas y 50 pasos lleva alrededor de una
hora** (comprobado en vivo: 58 minutos y 49 segundos). Es normal: el tiempo de espera del encargo
está puesto en el perfil en 180 minutos (`params.timeoutMinutes`).

Desde la versión 1.63 lo mismo se puede indicar **en el ejecutor**: el campo «Tiempo de espera de
respuesta, min» del formulario del ejecutor de IA. Si está relleno, es más fuerte que el
`timeoutMinutes` del perfil, y **0 significa esperar sin límite** (la generación dura lo que haga
falta; se puede cortar con el botón «detener»). Vacío es como antes: el valor del perfil.

La marcha de la generación se ve en la **consola del encargo** de la ficha de la tarea: allí se
vuelca la salida propia de ComfyUI junto con su barra de progreso.

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `width` / `height` | 768 × 512 | resolución; más grande es bastante más lento y usa más VRAM |
| `length` | 121 | fotogramas (unos 5 segundos) |
| `steps` | 50 | pasos de difusión; menos es más rápido y más basto |
| `negative` | vacío | prompt negativo |
| `timeoutMinutes` | 180 | cuánto esperar el resultado |

La descripción entera de la tarea se va al prompt. Una indicación del tipo «результат положить в
файл X.mp4» la ejecuta el conector: el archivo se copia a la carpeta del proyecto y la propia
línea se recorta del prompt. Las palabras de la indicación se reconocen **sólo en ruso y en
inglés** (en inglés, `Save the result to the file X.mp4`): están incrustadas en el código y no
dependen del idioma de la instalación.

## Entrenamiento de LoRA

Está hecho igual que en `Kandinsky-5.0-I2V-Lite-5s` y se diferencia en dos líneas de
configuración: la tarea del entrenador aquí es `k5-lite-t2v-5s-sd`, y los pesos son
`Kandinsky-5.0-T2V-Lite-sft-5s.safetensors`.

Entrena [musubi-tuner](https://github.com/kohya-ss/musubi-tuner): una sola tarjeta gráfica,
atención mediante `sdpa`, de 13 a 16 GB de VRAM. El oficial
[kandinsky-5-lora-train](https://github.com/kandinskylab/kandinsky-5-lora-train) requiere NCCL y
varias tarjetas gráficas, y en Windows no arranca.

El paquete `musubi-tuner` (código fuente, unos 30 MB) y **Python 3.12** (unos 45 MB) se instalan
junto con el modelo, con el botón «Instalar»: un Python 3.10–3.12 que ya esté en el ordenador se
toma tal cual y no se vuelve a descargar. El entorno con `torch` (unos 3 GB) se lo crea el
entrenador en el primer lanzamiento del entrenamiento.
El propio entrenador descarga además los originales `Qwen/Qwen2.5-VL-7B-Instruct` (unos 16 GB) y
`openai/clip-vit-large-patch14` (unos 1,7 GB): las compilaciones recortadas que usa la generación
no le sirven.

El adaptador terminado va a
`<repositorio de modelos>/loras/<código del objeto>.safetensors`. Todas las opciones de
entrenamiento, el `dataset.toml` y el `train.cmd` están en el perfil del modelo (`lora.train`) y
se reescriben antes de cada lanzamiento.

En AI2P el conjunto de datos son **imágenes**, así que lo que se entrena así es un adaptador de
aspecto; el adaptador de movimiento se entrena con vídeo, y en el editor de LoRA todavía no hay
conjunto de datos de vídeo.

## Errores frecuentes

* **El proceso desaparece sin mensaje**: casi siempre es un controlador NVIDIA antiguo (véase más
  arriba).
* **«El modelo no está instalado»**: los archivos no se han terminado de descargar; abra
  «Instalar» y la ventana mostrará el volumen que queda.
* **Falta de VRAM**: reduzca `width`/`height` o `length`.
* **ComfyUI está ocupado por un proceso ajeno**: AI2P sólo descarga el servidor que ha arrancado
  él mismo; un ComfyUI ajeno que ya esté funcionando en el 8188 no lo toca.

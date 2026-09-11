# SD-3.5-Large

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `sd3_5_large`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** texto → imagen, habilidades `image-generate` 80, `image-photo` 79,
`image-concept` 78, `image-text` 70

Stable Diffusion 3.5 Large de Stability AI, 8000 millones de parámetros. En el catálogo ocupa el
**escalón intermedio**: bastante mejor que SDXL y bastante más flojo que FLUX.2 y Qwen-Image, pero
se instala con **un solo archivo** y funciona en una tarjeta de 12 GB.

Se instala la compilación fp8 de Comfy-Org: **los codificadores de texto están dentro del propio
archivo**, y no hay que descargar aparte `clip_g`, `clip_l` ni `t5xxl`.

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, le envía el workflow y
recoge el `.png` terminado en los artefactos de la tarea.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → SD-3.5-Large → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **un archivo de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo `SD-3.5`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `sd3.5_large_fp8_scaled.safetensors` | ~13,9 GiB | `checkpoints` |

**En total, unos 15 GB de descarga.** La descarga es reanudable: una instalación interrumpida
continuará desde donde se paró.

La categoría es `checkpoints` y no `diffusion_models`: es un checkpoint completo del que ComfyUI
saca a la vez el modelo, los codificadores de texto y el VAE.

Mientras el archivo no esté en su sitio, el modelo **no puede estar activo**, y eso se comprueba
también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **Stability AI Community License** (no libre) |
| Uso comercial | permitido **mientras la facturación anual sea menor de 1 millón de dólares**; por encima hace falta una licencia Enterprise de Stability AI |
| Qué es obligatorio | adjuntar el texto de la licencia, conservar el aviso «This Stability AI Model is licensed under the Stability AI Community License, Copyright © Stability AI Ltd.» y mostrar «Powered by Stability AI» en el sitio o en la descripción del producto |
| Texto de la licencia | <https://huggingface.co/stabilityai/stable-diffusion-3.5-large/blob/main/LICENSE.md> |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

La licencia se cotejó con el texto del `LICENSE.md` del repositorio el 27.08.2026 (redacción del 5
de julio de 2024). **No** es una licencia libre: el uso investigador y no comercial es gratuito
siempre, y el comercial sólo por debajo del umbral de facturación indicado. Si ese umbral le queda
cerca, coja `FLUX.2-klein-4B` (Apache 2.0) o `Kandinsky-5.0-Image-Lite` (MIT).

El archivo que descarga AI2P es un reempaquetado de Comfy-Org
(<https://huggingface.co/Comfy-Org/stable-diffusion-3.5-fp8>) bajo esa misma licencia.

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto; pero si usted le lleva a
alguien un paquete con ComfyUI dentro, las condiciones de la GPL se aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **desde 10 GB de VRAM** (12 GB cómodo) |
| Controlador NVIDIA | **580 o más nuevo**; con un controlador antiguo torch se cae sin mensaje |
| Espacio en disco | unos 18 GB (pesos + paquete ComfyUI) |
| Memoria RAM | desde 16 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

## Cuánto hay que esperar

Veinte pasos en un fotograma de 1024×1024 son **decenas de segundos** en una tarjeta actual. El
tiempo de espera del encargo está puesto en el perfil en 60 minutos (`params.timeoutMinutes`).

La marcha de la generación se ve en la **consola del encargo** de la ficha de la tarea.

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `width` / `height` | 1024 × 1024 | resolución del fotograma |
| `steps` | 20 | pasos de difusión |
| `negative` | vacío | prompt negativo: sí tiene efecto (`cfg 4.01`) |
| `timeoutMinutes` | 60 | cuánto esperar el resultado |

El valor `cfg 4.01` está tomado tal cual de la plantilla oficial de ComfyUI.

La descripción entera de la tarea se va al prompt. Una indicación del tipo «результат положить в
файл X.png» la ejecuta el conector: el archivo se copia a la carpeta del proyecto y la propia
línea se recorta del prompt (esas palabras se reconocen sólo en ruso y en inglés).

## Entrenamiento de LoRA

**La aplicación, sí; el entrenamiento desde AI2P, no.**

Un adaptador ya hecho se conecta como en todos los registros de ComfyUI: el nodo
`LoraLoaderModelOnly` se inserta en el grafo sobre la marcha y el archivo se coge de
`<repositorio de modelos>/loras`. Hay muchos adaptadores para SD 3.5 en acceso abierto, y todos
sirven.

No hay con qué entrenar un adaptador directamente desde AI2P: `musubi-tuner`, con el que se
entrenan los demás modelos locales, no conoce en absoluto la familia Stable Diffusion, que es
terreno de la herramienta vecina del mismo autor, [sd-scripts](https://github.com/kohya-ss/sd-scripts).
Por eso en el registro está puesto `lora.train.kind = external` y el botón de entrenamiento
responde con una negativa enseguida, y no al cabo de media hora de cálculo. Un archivo entrenado
por su cuenta basta con ponerlo en `loras`.

## Errores frecuentes

* **El texto del cuadro no sale**: es el punto flojo de SD 3.5; para rótulos coja
  `Qwen-Image-2512`, y para cirílico, `Kandinsky-5.0-Image-Lite`.
* **«El modelo no está instalado»**: el archivo no se ha terminado de descargar; abra «Instalar» y
  la ventana mostrará el volumen que queda.
* **El botón de entrenamiento de LoRA se niega**: está pensado así (véase más arriba).
* **Falta de VRAM**: reduzca `width`/`height`.
* **ComfyUI está ocupado por un proceso ajeno**: AI2P sólo descarga el servidor que ha arrancado
  él mismo.

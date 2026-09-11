# Kandinsky-5.0-Image-Lite

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `kandinsky5lite_t2i`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** texto → imagen, habilidades `image-text` 88, `image-generate` 84,
`image-concept` 83, `image-photo` 82

Modelo de imagen de Kandinsky Lab (Sber), 6000 millones de parámetros, resolución de hasta 1K. Lo
principal por lo que merece la pena tenerlo: **entiende el prompt en ruso y las realidades rusas**
y **escribe en cirílico dentro del cuadro**: rótulos, textos de envases, pies. En los demás
modelos abiertos del catálogo el cirílico sale peor o no sale en absoluto.

La licencia es **MIT**, la más libre de todos los registros del catálogo.

Es pariente de los registros de vídeo `Kandinsky-5.0-*` que ya están en el catálogo: los
codificadores de texto son comunes, pero el grupo de instalación es propio (la versión de imagen
tiene su propio archivo de pesos y su propio VAE).

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, le envía el workflow y
recoge el `.png` terminado en los artefactos de la tarea.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → Kandinsky-5.0-Image-Lite → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **cuatro archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo
  `Kandinsky-5-Image`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `kandinsky5lite_t2i.safetensors` | ~11,2 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `clip_l.safetensors` | ~235 MiB | `text_encoders` |
| `ae.safetensors` | ~320 MiB | `vae` |

**En total, unos 22 GB de descarga.** La descarga es reanudable: una instalación interrumpida
continuará desde donde se paró.

Sobre `ae.safetensors`: es el VAE **de FLUX**, no uno propio. Así está hecho el propio modelo,
tanto en la plantilla oficial de ComfyUI como en las instrucciones de los autores (`weights/flux/vae`
se pone en `ComfyUI/models/vae`).

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **MIT** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/kandinskylab/Kandinsky-5.0-T2I-Lite> (ficha del modelo) |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

La licencia se cotejó con la ficha del modelo el 27.08.2026 (`cardData.license: mit`). MIT no
impone limitaciones al ámbito de aplicación, y en eso se diferencia de SDXL (que tiene una lista
de usos prohibidos) y de FLUX.2 [dev] (donde el uso comercial está prohibido).

Los pesos se descargan **directamente de los autores**; para este modelo no hay reempaquetado de
Comfy-Org: `kandinskylab/Kandinsky-5.0-T2I-Lite`, archivo
`model/kandinsky5lite_t2i.safetensors`, exactamente el nombre que espera la plantilla oficial de
ComfyUI.

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto; pero si usted le lleva a
alguien un paquete con ComfyUI dentro, las condiciones de la GPL se aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **desde 12 GB de VRAM** (16 GB cómodo) |
| Controlador NVIDIA | **580 o más nuevo**; con un controlador antiguo torch se cae sin mensaje |
| Espacio en disco | unos 25 GB (pesos + paquete ComfyUI) |
| Memoria RAM | desde 16 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

## Cuánto hay que esperar

Cincuenta pasos son **uno o dos minutos** por fotograma de 1024×1024 en una tarjeta actual (en una
H100 los autores miden 13 segundos). El tiempo de espera del encargo está puesto en el perfil en
90 minutos (`params.timeoutMinutes`).

La marcha de la generación se ve en la **consola del encargo** de la ficha de la tarea: allí se
vuelca la salida propia de ComfyUI junto con su barra de progreso.

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `width` / `height` | 1024 × 1024 | resolución del fotograma (el modelo está pensado también para 1280×768) |
| `steps` | 50 | pasos de difusión; por debajo de 30 la calidad cae bastante |
| `negative` | vacío | prompt negativo: sí tiene efecto (`cfg 3.5`) |
| `timeoutMinutes` | 90 | cuánto esperar el resultado |

La descripción entera de la tarea se va al prompt. El texto que deba aparecer dentro del cuadro
escríbalo **entre comillas**: así el modelo entiende que es un rótulo y no una descripción.

## Entrenamiento de LoRA

**La aplicación, sí; el entrenamiento desde AI2P, no.**

Un adaptador ya hecho se conecta como en todos los registros de ComfyUI: el nodo
`LoraLoaderModelOnly` se inserta en el grafo sobre la marcha y el archivo se coge de
`<repositorio de modelos>/loras`.

En cambio, no hay con qué entrenar un adaptador directamente desde AI2P:
[musubi-tuner](https://github.com/kohya-ss/musubi-tuner) admite sólo los modelos de VÍDEO de
Kandinsky 5 y escribe con todas las letras en su documentación (`docs/kandinsky5.md`) que los
modelos Image Lite no están admitidos. Por eso en el registro está puesto
`lora.train.kind = external`: el botón de entrenamiento responderá con una negativa enseguida y
no al cabo de media hora de cálculo. Si aparece un entrenador, bastará con añadir la sección
`lora.train` en el perfil del modelo, sin necesidad de código.

Entrenar el adaptador fuera de AI2P (por ejemplo, con los scripts originales de
<https://github.com/kandinskylab/kandinsky-5>) y poner el archivo en `loras` sí se puede: se
aplicará con normalidad.

## Errores frecuentes

* **El cirílico del cuadro sale torcido de todos modos**: aumente `steps` y escriba el rótulo
  entre comillas; las frases muy largas no le salen bien a ningún modelo abierto.
* **«El modelo no está instalado»**: los archivos no se han terminado de descargar; abra
  «Instalar» y la ventana mostrará el volumen que queda.
* **El botón de entrenamiento de LoRA se niega**: está pensado así, no hay entrenador para Image
  Lite (véase más arriba).
* **Falta de VRAM**: reduzca `width`/`height`.
* **ComfyUI está ocupado por un proceso ajeno**: AI2P sólo descarga el servidor que ha arrancado
  él mismo; un ComfyUI ajeno que ya esté funcionando en el 8188 no lo toca.

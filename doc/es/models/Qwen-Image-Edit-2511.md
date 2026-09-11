# Qwen-Image-Edit-2511

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`,
modelo `qwen_image_edit_2511`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** imagen + indicación → imagen modificada, habilidades `image-edit` 90,
`image-text` 90, `image-inpaint` 85, `image-generate` 82

Modelo de **edición de imágenes** de Alibaba, edición 2511. A diferencia de `Qwen-Image-2512`, que
dibuja desde cero, este coge una **imagen ya hecha** y ejecuta una indicación con palabras
normales: «cambia el cuero del sofá por piel», «quita los cables del cielo», «pon el rótulo en
ruso», «pinta la pared de azul». Es el único registro del catálogo que cubre las habilidades
`image-edit` e `image-inpaint`.

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, sube la imagen de origen,
le envía el workflow y recoge el `.png` terminado en los artefactos de la tarea.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → Qwen-Image-Edit-2511 → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **tres archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo
  `Qwen-Image`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `qwen_image_edit_2511_fp8mixed.safetensors` | ~19,1 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `qwen_image_vae.safetensors` | ~242 MiB | `vae` |

**En total, unos 30 GB de descarga.** El grupo `Qwen-Image` es común con el modelo
`Qwen-Image-2512`: si ese ya está instalado, sólo se descargará su propio archivo de pesos (unos
19 GiB).

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **Apache 2.0** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/Comfy-Org/Qwen-Image-Edit_ComfyUI> (ficha del modelo) |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

La licencia se cotejó con la ficha del modelo el 27.08.2026. Fíjese: libre es la licencia de los
**pesos**, no la de las imágenes que usted edita; los derechos sobre la imagen de origen siguen
dependiendo de dónde la haya sacado.

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto; pero si usted le lleva a
alguien un paquete con ComfyUI dentro, las condiciones de la GPL se aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **desde 16 GB de VRAM** (24 GB cómodo) |
| Controlador NVIDIA | **580 o más nuevo**; con un controlador antiguo torch se cae sin mensaje |
| Espacio en disco | unos 32 GB (pesos + paquete ComfyUI); con `Qwen-Image-2512` ya instalado, unos 21 GB |
| Memoria RAM | desde 32 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

## De dónde sale la imagen de origen

Igual que en `Kandinsky-5.0-I2V-Lite-5s`: la ruta del archivo **relativa a la carpeta del
proyecto** se escribe en la descripción de la tarea, el conector sube el archivo a ComfyUI y pone
su nombre en la plantilla. Lo más sencillo es dar una referencia a un objeto del proyecto,
`@obj:OBJ-3`: en un objeto del tipo «fotograma de referencia» el archivo ya está indicado y se va
al encargo solo.

La imagen de origen aquí es **obligatoria** (`refImage.required`): sin imagen, el encargo no
empieza, en lugar de dar un resultado vacío.

El tamaño del resultado lo fija **la propia imagen de origen**: el nodo `FluxKontextImageScale` la
ajusta al tamaño admitido más cercano. Los campos `width`/`height` del perfil no influyen en la
edición (se han quedado para el resumen del encargo y para el conjunto de datos del entrenamiento
de LoRA).

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `steps` | 40 | pasos de difusión; menos es más rápido y más basto |
| `negative` | vacío | prompt negativo (aquí sí tiene efecto, `cfg = 3`) |
| `timeoutMinutes` | 90 | cuánto esperar el resultado |
| `width` / `height` | 1328 × 1328 | no influyen en la edición, véase más arriba |

La descripción entera de la tarea se va al prompt. Escriba **qué cambiar**, no qué se ve:
«cambia el fondo por una ciudad al atardecer» funciona mejor que una descripción completa de la
escena.

## Entrenamiento de LoRA

Está admitido por completo: el adaptador se **aplica** (el nodo `LoraLoaderModelOnly` se inserta
en el grafo sobre la marcha) y se **entrena** directamente desde AI2P, con el editor de LoRA de la
ficha del objeto del proyecto.

Entrena [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) con los mismos scripts que
`Qwen-Image-2512`, pero con la opción `--model_version edit-2511`: le dice al entrenador que el
modelo tiene una imagen de control, y los prompts se cachean junto con ella.

**El entrenador necesita otros archivos de pesos.** Las compilaciones fp8 con las que calcula la
generación no sirven para el entrenamiento, así que en el primer entrenamiento el `train.cmd`
descarga una vez el par en **bf16** —`qwen_image_edit_2511_bf16.safetensors` (unos 38 GiB) y
`qwen_2.5_vl_7b.safetensors` (unos 15,4 GiB)— a la subcarpeta `train` del grupo `Qwen-Image`. Eso
son unos **57 GB por encima de la instalación**; la generación en sí sigue funcionando con los
archivos fp8 ligeros.

El conjunto de datos del editor de LoRA son imágenes con pies. Para un modelo de edición eso es
entrenar el **estilo del resultado**, no pares «antes/después»: en el editor de LoRA todavía no
hay imágenes de control, y es una limitación honesta, no un ajuste.

El adaptador terminado va a
`<repositorio de modelos>/loras/<código del objeto>.safetensors`. Todas las opciones de
entrenamiento, el `dataset.toml` y el `train.cmd` están en el perfil del modelo (`lora.train`) y
se reescriben antes de cada lanzamiento: edítelos en el perfil, no en los archivos.

## Errores frecuentes

* **«El modelo exige una imagen de origen»**: en la descripción de la tarea no hay ni ruta de
  archivo ni referencia `@obj:` a un objeto con archivo.
* **El proceso desaparece sin mensaje**: casi siempre es un controlador NVIDIA antiguo (véase más
  arriba).
* **«El modelo no está instalado»**: los archivos no se han terminado de descargar; abra
  «Instalar» y la ventana mostrará el volumen que queda.
* **La edición «no se ha enterado» de la indicación**: reduzca el volumen de la indicación; una
  corrección por encargo sale más fiable que una lista de cinco.
* **ComfyUI está ocupado por un proceso ajeno**: AI2P sólo descarga el servidor que ha arrancado
  él mismo; un ComfyUI ajeno que ya esté funcionando en el 8188 no lo toca.

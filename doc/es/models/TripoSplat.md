# TripoSplat

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `triposplat`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** imagen → SPLATS GAUSSIANOS, habilidad `3d-image` 80

Modelo abierto de Tripo AI (VAST-AI) que convierte UNA imagen no en una malla, sino en una nube
de gaussianas tridimensionales: **splats gaussianos**. Es el único registro del catálogo que da
ese resultado: un archivo `.spz`, que abren los visores de splats, Unreal, Unity y reproductores
web como Babylon.js.

Los splats NO son una malla: no tienen ni polígonos ni UV, así que en el canal de producción de un
juego se ponen tal cual o se convierten aparte. A cambio dan una imagen fotográfica con bordes
suaves y transparencia allí donde una malla queda basta (vegetación, pelo, humo, interiores).

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, le envía el workflow y
recoge el `.spz` terminado en los artefactos de la tarea.

**Se lee la IMAGEN, no el texto.** En el grafo del modelo no hay codificador de texto en absoluto:
la condición la da DINOv3 a partir del fotograma inicial. El modelo no ve la descripción de la
tarea: lo que esté dibujado en la imagen es lo que saldrá. El fotograma inicial se indica en la
descripción de la tarea con una referencia a un objeto del proyecto (`@obj:OBJ-3`) o con la ruta
del archivo relativa a la carpeta del proyecto; sin él, el encargo no arranca en absoluto.

El fondo del fotograma inicial se quita automáticamente (BiRefNet).

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → TripoSplat → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **cinco archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo
  `TripoSplat`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `dino_v3_vit_h.safetensors` | ~1,57 GiB | `clip_vision` |
| `triposplat_fp16.safetensors` | ~707 MiB | `diffusion_models` |
| `triposplat_vae_decoder_fp16.safetensors` | ~549 MiB | `vae` |
| `flux2-vae.safetensors` | ~321 MiB | `vae` |
| `birefnet.safetensors` | ~424 MiB | `background_removal` |

**En total, unos 3,8 GB de descarga**: el más ligero de los registros locales de 3D del catálogo.
La descarga es reanudable: una instalación interrumpida continuará desde donde se paró, y los
archivos ya descargados no se vuelven a descargar.

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **MIT** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/VAST-AI/TripoSplat> (ficha del modelo) |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

La retirada del fondo la hace BiRefNet (<https://huggingface.co/ZhengPeng7/BiRefNet>), también
MIT; el codificador del fotograma es DINOv3 (Meta), que está en ese mismo repositorio de VAST-AI
bajo esa misma licencia. Las licencias se cotejaron con las fichas de los modelos el 27.08.2026;
en los modelos abiertos cambian poco, pero antes de un lanzamiento comercial compruebe las fichas
otra vez.

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto; pero si usted le lleva a
alguien un paquete con ComfyUI dentro, las condiciones de la GPL se aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **desde 8 GB de VRAM** (12 GB cómodo) |
| Controlador NVIDIA | **580 o más nuevo**; con un controlador antiguo torch se cae sin mensaje |
| Espacio en disco | unos 6 GB (pesos + paquete ComfyUI) |
| Memoria RAM | desde 16 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

La cifra de memoria de vídeo es una **estimación**, no una medición. Si no basta la memoria, baje
`num_gaussians` en `VAEDecodeTripoSplat` en la plantilla del workflow (por defecto, 262144).

## Cuánto hay que esperar

Unos pocos minutos por modelo en una tarjeta actual: el modelo en sí es pequeño (unos 700 MB de
pesos) y la etapa es una, no cuatro como en TRELLIS-2. El tiempo de espera del encargo está puesto
en el perfil en 60 minutos (`params.timeoutMinutes`).

La marcha de la generación se ve en la **consola del encargo** de la ficha de la tarea: allí se
vuelca la salida propia de ComfyUI junto con su barra de progreso.

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `steps` | 20 | pasos de difusión |
| `timeoutMinutes` | 60 | cuánto esperar el resultado |
| `width` / `height` | 1024 | NO influyen en el resultado: el 3D no tiene fotograma |
| `negative` | vacío | no tiene efecto: en el grafo no hay codificador de texto |

El número de gaussianas (`num_gaussians`, 262144) y el formato del archivo (`spz`) los fija la
plantilla del workflow. Los formatos que sabe manejar el nodo `SplatToFile3D` son `spz`, `ply`,
`splat` y `ksplat`; se ha elegido `spz` por ser el más compacto.

**Qué no tiene la plantilla a propósito.** La plantilla oficial de ComfyUI dibuja además un giro
de cámara alrededor del objeto y lo guarda como clip (`RenderSplat` → `CreateVideo` →
`SaveVideo`). Nosotros no hemos traído esa rama: es un renderizado aparte en cada generación, y el
resultado del encargo ya es el archivo de splats. Allí mismo está `SplatToMesh`, el nodo que
convierte los splats en una malla normal; quien necesite la malla lo añade a su propio workflow o
coge [TRELLIS-2](https://huggingface.co/microsoft/TRELLIS.2-4B).

## Entrenamiento de LoRA

**No está admitido, y es una negativa honesta, no algo a medio hacer.** Hoy no hay un entrenador
público de LoRA para arquitecturas 3D: [musubi-tuner](https://github.com/kohya-ss/musubi-tuner),
sobre el que se sostiene todo el entrenamiento de adaptadores en AI2P, sólo sabe manejar imágenes
y vídeo. Por eso en el perfil está puesto `lora.supported: false`, y el formulario del modelo
muestra el motivo con palabras.

La constancia del objeto entre fotogramas se consigue aquí de otra manera: dele de entrada una
misma imagen de referencia del personaje (un objeto del proyecto, con la referencia `@obj:`); de
una misma imagen y un mismo `seed` saldrá un mismo resultado.

## Errores frecuentes

* **«El modelo no está instalado»**: los archivos no se han terminado de descargar; abra
  «Instalar» y la ventana mostrará el volumen que queda.
* **El encargo se niega a arrancar sin imagen**: está pensado así, el modelo funciona sólo a
  partir de una imagen. Dele una referencia `@obj:` a un fotograma de referencia o la ruta del
  archivo.
* **El archivo `.spz` no se abre con el programa de siempre**: son splats, no una malla; hace
  falta un visor de splats gaussianos o una conversión.
* **Falta de VRAM**: reduzca `num_gaussians` en la plantilla del workflow.
* **El proceso desaparece sin mensaje**: casi siempre es un controlador NVIDIA antiguo (véase más
  arriba).

# Hunyuan3D-2.1

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `hunyuan3d_2_1`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** imagen → modelo 3D, habilidad `3d-image` 86

Modelo abierto de Tencent que convierte UNA imagen en una malla tridimensional. Es el primer
registro local del catálogo que cubre el 3D: hasta él, las habilidades `3d-*` sólo las tenían los
Tripo y Meshy de la nube.

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, le envía el workflow y
recoge el `.glb` terminado en los artefactos de la tarea.

**Se lee la IMAGEN, no el texto.** En el grafo del modelo no hay codificador de texto en absoluto:
la condición la da CLIP Vision a partir del fotograma inicial. El modelo no ve la descripción de
la tarea: lo que esté dibujado en la imagen es lo que saldrá. El fotograma inicial se indica en la
descripción de la tarea con una referencia a un objeto del proyecto (`@obj:OBJ-3`) o con la ruta
del archivo relativa a la carpeta del proyecto; sin él, el encargo no arranca en absoluto.

**No habrá color.** ComfyUI calcula sólo la rama de la FORMA de Hunyuan3D
(`VAEDecodeHunyuan3D` → `VoxelToMesh` → `SaveGLB`). La rama de coloreado del repositorio de
Tencent —esa misma que necesita Linux, CUDA 12.4 y su propia rasterización CUDA— no está
implementada en el motor, y no se puede conectar con un paquete normal. Si necesita una malla en
color, coja [TRELLIS-2](https://huggingface.co/microsoft/TRELLIS.2-4B), que está creado en un
registro vecino.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → Hunyuan3D-2.1 → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **un archivo de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo
  `Hunyuan3D-2.1`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `hunyuan_3d_v2.1.safetensors` | ~6,86 GiB | `checkpoints` |

**En total, unos 7,4 GB de descarga.** La descarga es reanudable: una instalación interrumpida
continuará desde donde se paró, y los archivos ya descargados no se vuelven a descargar.

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **Tencent Hunyuan 3D 2.1 Community License** (no es una licencia abierta en el sentido habitual) |
| Uso comercial | permitido, pero con salvedades; véase más abajo |
| Dónde no se puede | Unión Europea, Reino Unido, Corea del Sur (esos territorios están excluidos de la licencia) |
| Umbral | más de 1 millón de usuarios activos al mes exige una licencia aparte de Tencent |
| Qué es obligatorio | marcar el producto con las palabras «Powered by Tencent Hunyuan» y adjuntar el archivo Notice al entregarlo a terceros |
| Texto de la licencia | <https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1/blob/main/LICENSE> |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

Los archivos que descarga AI2P son un reempaquetado de Comfy-Org
(<https://huggingface.co/Comfy-Org/hunyuan3D_2.1_repackaged>), que va bajo esa misma licencia de
Tencent y no bajo MIT. La licencia se cotejó con el texto del repositorio el 27.08.2026. Las
condiciones de esta licencia son bastante más estrictas que la Apache 2.0 de los demás modelos
locales del catálogo: **antes de un lanzamiento comercial léala entera.**

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto; pero si usted le lleva a
alguien un paquete con ComfyUI dentro, las condiciones de la GPL se aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **desde 8 GB de VRAM** (12 GB cómodo) |
| Controlador NVIDIA | **580 o más nuevo**; con un controlador antiguo torch se cae sin mensaje |
| Espacio en disco | unos 10 GB (pesos + paquete ComfyUI) |
| Memoria RAM | desde 16 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

La cifra de memoria de vídeo es una **estimación**, no una medición: sólo se calcula la rama de la
forma, y el requisito de «10–29 GB de VRAM» que aparece en las reseñas de Hunyuan3D 2.1 se refiere
al canal completo de Tencent junto con el horneado de la textura. Si no basta la memoria, baje
`octree_resolution` en la plantilla del workflow.

## Cuánto hay que esperar

Unos pocos minutos por modelo en una tarjeta actual. El tiempo de espera del encargo está puesto
en el perfil en 60 minutos (`params.timeoutMinutes`).

La marcha de la generación se ve en la **consola del encargo** de la ficha de la tarea: allí se
vuelca la salida propia de ComfyUI junto con su barra de progreso.

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `steps` | 30 | pasos de difusión; por debajo de 20 la forma se desmorona |
| `timeoutMinutes` | 60 | cuánto esperar el resultado |
| `width` / `height` | 1024 | NO influyen en el resultado: el 3D no tiene fotograma |
| `negative` | vacío | no tiene efecto: en el grafo no hay codificador de texto |

La densidad de la malla no la fija el perfil, sino la propia plantilla del workflow: el
`octree_resolution` de `VAEDecodeHunyuan3D` (256) y el `threshold` de `VoxelToMesh` (0,6).

## Entrenamiento de LoRA

**No está admitido, y es una negativa honesta, no algo a medio hacer.** Hoy no hay un entrenador
público de LoRA para arquitecturas 3D: [musubi-tuner](https://github.com/kohya-ss/musubi-tuner),
sobre el que se sostiene todo el entrenamiento de adaptadores en AI2P, sólo sabe manejar imágenes
y vídeo. Por eso en el perfil está puesto `lora.supported: false`, y el formulario del modelo
muestra el motivo con palabras.

La constancia del objeto entre fotogramas se consigue aquí de otra manera: dele de entrada una
misma imagen de referencia del personaje (un objeto del proyecto, con la referencia `@obj:`); de
una misma imagen y un mismo `seed` saldrá una misma malla.

## Errores frecuentes

* **«El modelo no está instalado»**: el archivo no se ha terminado de descargar; abra «Instalar» y
  la ventana mostrará el volumen que queda.
* **El encargo se niega a arrancar sin imagen**: está pensado así, el modelo funciona sólo a
  partir de una imagen. Dele una referencia `@obj:` a un fotograma de referencia o la ruta del
  archivo.
* **El modelo ha salido blanco**: en este registro no hay color en absoluto (véase el principio).
* **Falta de VRAM**: baje `octree_resolution` en la plantilla del workflow.
* **El proceso desaparece sin mensaje**: casi siempre es un controlador NVIDIA antiguo (véase más
  arriba).

# TRELLIS-2

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `trellis_2`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** imagen → modelo 3D CON COLOR, habilidad `3d-image` 85

Modelo abierto de Microsoft Research (4000 millones de parámetros) que convierte UNA imagen en una
malla tridimensional. Es el único de los tres registros locales de 3D del catálogo que entrega no
sólo la forma, sino también el color: después de la etapa de la forma viene una etapa de textura
aparte.

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, le envía el workflow y
recoge el `.glb` terminado en los artefactos de la tarea.

**Se lee la IMAGEN, no el texto.** En el grafo del modelo no hay codificador de texto en absoluto:
la condición la da DINOv3 a partir del fotograma inicial. El modelo no ve la descripción de la
tarea: lo que esté dibujado en la imagen es lo que saldrá. El fotograma inicial se indica en la
descripción de la tarea con una referencia a un objeto del proyecto (`@obj:OBJ-3`) o con la ruta
del archivo relativa a la carpeta del proyecto; sin él, el encargo no arranca en absoluto.

El fondo del fotograma inicial se quita automáticamente (BiRefNet) y el fotograma se recorta por el
objeto: el 3D a partir de una imagen con fondo sale bastante peor.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → TRELLIS-2 → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **cinco archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo
  `TRELLIS-2`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `trellis_2_int8_convrot.safetensors` | ~4,89 GiB | `diffusion_models` |
| `dino_v3_vit_l.safetensors` | ~1,13 GiB | `clip_vision` |
| `trellis_2_shape_vae_bf16.safetensors` | ~1,02 GiB | `vae` |
| `trellis_2_texture_vae_bf16.safetensors` | ~904 MiB | `vae` |
| `birefnet.safetensors` | ~424 MiB | `background_removal` |

**En total, unos 9,0 GB de descarga** (8,34 GiB). La descarga es reanudable: una instalación
interrumpida continuará desde donde se paró, y los archivos ya descargados no se vuelven a
descargar.

Los pesos se cogen en la compilación **int8**: pesa la mitad que la bf16 (unos 9,6 GiB) y está
pensada para una tarjeta de consumo. Quien necesite la máxima precisión cambia el `unet_name` de
la plantilla del workflow por `trellis_2_bf16.safetensors` y añade el archivo al manifiesto por su
cuenta.

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **MIT** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/microsoft/TRELLIS.2-4B> (ficha del modelo) |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

Los archivos que descarga AI2P son un reempaquetado de Comfy-Org
(<https://huggingface.co/Comfy-Org/TRELLIS.2>), que va bajo esa misma MIT. La retirada del fondo
la hace BiRefNet (<https://huggingface.co/ZhengPeng7/BiRefNet>), también MIT. Las licencias se
cotejaron con las fichas de los modelos el 27.08.2026; en los modelos abiertos cambian poco, pero
antes de un lanzamiento comercial compruebe las fichas otra vez.

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto; pero si usted le lleva a
alguien un paquete con ComfyUI dentro, las condiciones de la GPL se aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **desde 12 GB de VRAM** (16 GB cómodo) |
| Controlador NVIDIA | **580 o más nuevo**; con un controlador antiguo torch se cae sin mensaje |
| Espacio en disco | unos 13 GB (pesos + paquete ComfyUI) |
| Memoria RAM | desde 16 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

La cifra de memoria de vídeo es una **estimación**, no una medición: se calcula por el tamaño de
los pesos int8 y de los dos VAE. Si no basta la memoria, baje el `target_resolution` de
`Trellis2UpsampleStage` en la plantilla del workflow (por defecto, 1536).

## Cuánto hay que esperar

En el grafo hay cuatro etapas —estructura, forma, aumento de resolución y textura—, así que un
modelo se calcula bastante más despacio que en Hunyuan3D: de unos pocos a unas decenas de minutos
en una tarjeta actual. El tiempo de espera del encargo está puesto en el perfil en 90 minutos
(`params.timeoutMinutes`).

La marcha de la generación se ve en la **consola del encargo** de la ficha de la tarea: allí se
vuelca la salida propia de ComfyUI junto con su barra de progreso.

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `steps` | 20 | pasos de la etapa de la FORMA: la palanca principal de calidad |
| `timeoutMinutes` | 90 | cuánto esperar el resultado |
| `width` / `height` | 1024 | NO influyen en el resultado: el 3D no tiene fotograma |
| `negative` | vacío | no tiene efecto: en el grafo no hay codificador de texto |

Las otras tres etapas van con el número de pasos de la plantilla oficial (12): cambiarlos sólo se
puede editando el propio workflow, y cambian poco el resultado mientras se comen el mismo tiempo.

**Qué no tiene la plantilla a propósito.** La plantilla oficial de ComfyUI, después de la malla,
hace además el desplegado UV y hornea el juego completo de mapas PBR (`UnwrapMesh` →
`BakeTextureFromVoxel`, `BakeNormalMapFromMesh`, `BakeAmbientOcclusion` → `ApplyTextureToMesh`).
En nuestra plantilla esa rama no está: son otra decena de nodos y un horneado a 2048–4096 puntos,
y sin tarjeta gráfica no hay con qué comprobarlos. El color, mientras tanto, está en su sitio: se
transmite con los vértices de la malla (`PaintMesh`). Quien necesite los mapas PBR añade la rama a
su propio workflow: todos los nodos existen en ComfyUI.

## Entrenamiento de LoRA

**No está admitido, y es una negativa honesta, no algo a medio hacer.** Hoy no hay un entrenador
público de LoRA para arquitecturas 3D: [musubi-tuner](https://github.com/kohya-ss/musubi-tuner),
sobre el que se sostiene todo el entrenamiento de adaptadores en AI2P, sólo sabe manejar imágenes
y vídeo. Por eso en el perfil está puesto `lora.supported: false`, y el formulario del modelo
muestra el motivo con palabras.

La constancia del objeto entre fotogramas se consigue aquí de otra manera: dele de entrada una
misma imagen de referencia del personaje (un objeto del proyecto, con la referencia `@obj:`); de
una misma imagen y un mismo `seed` saldrá un mismo modelo.

## Errores frecuentes

* **«El modelo no está instalado»**: los archivos no se han terminado de descargar; abra
  «Instalar» y la ventana mostrará el volumen que queda.
* **El encargo se niega a arrancar sin imagen**: está pensado así, el modelo funciona sólo a
  partir de una imagen. Dele una referencia `@obj:` a un fotograma de referencia o la ruta del
  archivo.
* **Falta de VRAM**: baje el `target_resolution` de `Trellis2UpsampleStage`.
* **El objeto ha salido recortado**: la retirada del fondo ha elegido otra cosa; dé un fotograma
  en el que el objeto que hace falta esté solo y entero dentro del cuadro.
* **El proceso desaparece sin mensaje**: casi siempre es un controlador NVIDIA antiguo (véase más
  arriba).

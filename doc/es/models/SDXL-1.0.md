# SDXL-1.0

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `sdxl_base_1_0`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** texto → imagen, habilidades `image-generate` 70, `image-concept` 70,
`image-photo` 68

Stable Diffusion XL 1.0 de Stability AI: **el registro menos exigente del catálogo**, 6,9 GB en un
solo archivo y 8 GB de memoria de vídeo. Está creado precisamente como el escalón inferior: en
calidad va por detrás de todos sus vecinos y **no sabe escribir texto dentro del cuadro**, pero a
cambio funciona allí donde FLUX.2, Qwen-Image y Kandinsky no arrancarían en absoluto.

El segundo argumento a su favor: para SDXL hay escritos más adaptadores LoRA que para todos los
demás modelos juntos, y todos funcionan aquí sin retoques.

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, le envía el workflow y
recoge el `.png` terminado en los artefactos de la tarea.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → SDXL-1.0 → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **un archivo de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo `SDXL-1.0`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `sd_xl_base_1.0.safetensors` | ~6,5 GiB | `checkpoints` |

**En total, unos 7 GB de descarga**, menos que cualquier otro registro de imagen. La descarga es
reanudable.

La categoría es `checkpoints`: es un checkpoint completo del que ComfyUI saca a la vez el modelo,
los codificadores de texto y el VAE. La segunda etapa de SDXL (el Refiner) no entra en la entrega,
porque es un archivo aparte y el registro está creado como el más ligero.

Mientras el archivo no esté en su sitio, el modelo **no puede estar activo**, y eso se comprueba
también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **CreativeML Open RAIL++-M** (del 26 de julio de 2023) |
| Uso comercial | permitido, sin regalías y sin umbral de facturación |
| Qué es obligatorio | adjuntar el texto de la licencia, conservar el aviso de derechos de autor y **transmitir las restricciones de uso más adelante**, a todo aquel a quien le entregue el modelo o una obra derivada de él |
| Restricciones | la licencia prohíbe una serie de usos (la lista está en «Attachment A. Use Restrictions»): infringir la ley, dañar a menores, desinformar, discriminar, etcétera |
| Texto de la licencia | <https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0/blob/main/LICENSE.md> |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

La licencia se cotejó con el texto del `LICENSE.md` del repositorio el 27.08.2026. Es una licencia
«abierta, pero responsable»: los derechos que da son los de una permisiva, pero añade una lista de
usos prohibidos que usted está obligado a transmitir junto con el modelo. Sobre el resultado de la
generación en sí, Stability no reclama derechos.

El archivo se descarga directamente del repositorio de Stability AI
(<https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0>), sin necesidad de
reempaquetado.

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto; pero si usted le lleva a
alguien un paquete con ComfyUI dentro, las condiciones de la GPL se aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **desde 6 GB de VRAM** (8 GB cómodo) |
| Controlador NVIDIA | **580 o más nuevo**; con un controlador antiguo torch se cae sin mensaje |
| Espacio en disco | unos 10 GB (pesos + paquete ComfyUI) |
| Memoria RAM | desde 8 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

## Cuánto hay que esperar

Veinticinco pasos en un fotograma de 1024×1024 son **segundos o decenas de segundos** incluso en
una tarjeta modesta. El tiempo de espera del encargo está puesto en el perfil en 60 minutos
(`params.timeoutMinutes`).

La marcha de la generación se ve en la **consola del encargo** de la ficha de la tarea.

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `width` / `height` | 1024 × 1024 | resolución del fotograma; SDXL está entrenada precisamente a 1024 |
| `steps` | 25 | pasos de difusión |
| `negative` | vacío | prompt negativo: sí tiene efecto (`cfg 7`), y en SDXL es más útil que en los modelos nuevos |
| `timeoutMinutes` | 60 | cuánto esperar el resultado |

SDXL entiende mal las descripciones largas: el prompt es mejor escribirlo como enumeración
(«qué, dónde, en qué estilo, con qué plano») y no como un párrafo coherente. Los modelos más
nuevos (FLUX.2, Qwen-Image) se comportan justo al revés.

La descripción entera de la tarea se va al prompt. Una indicación del tipo «результат положить в
файл X.png» la ejecuta el conector (esas palabras se reconocen sólo en ruso y en inglés).

## Entrenamiento de LoRA

**La aplicación, sí; el entrenamiento desde AI2P, no.**

Un adaptador ya hecho se conecta como en todos los registros de ComfyUI: el nodo
`LoraLoaderModelOnly` se inserta en el grafo sobre la marcha y el archivo se coge de
`<repositorio de modelos>/loras`. Y este es justamente el caso en el que hay miles de adaptadores
ya hechos en acceso abierto.

No hay con qué entrenar un adaptador directamente desde AI2P: `musubi-tuner` no conoce la familia
Stable Diffusion; para SDXL está [sd-scripts](https://github.com/kohya-ss/sd-scripts), del mismo
autor. Por eso en el registro está puesto `lora.train.kind = external` y el botón de entrenamiento
responde con una negativa enseguida. Un archivo entrenado por su cuenta basta con ponerlo en
`loras`.

## Errores frecuentes

* **El texto del cuadro se convierte en un revoltijo**: SDXL no sabe hacer eso; coja
  `Qwen-Image-2512` o `Kandinsky-5.0-Image-Lite`.
* **La imagen sale «borrosa» en un tamaño no estándar**: SDXL está entrenada a 1024×1024, y los
  fotogramas mucho más pequeños se le dan mal.
* **«El modelo no está instalado»**: el archivo no se ha terminado de descargar; abra «Instalar».
* **El botón de entrenamiento de LoRA se niega**: está pensado así (véase más arriba).
* **ComfyUI está ocupado por un proceso ajeno**: AI2P sólo descarga el servidor que ha arrancado
  él mismo.

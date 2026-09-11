# Kandinsky-5.0-I2V-Lite-5s

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`,
modelo `kandinsky5lite_i2v_5s`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** imagen + texto → vídeo (5 segundos), habilidades `video-animate` 78,
`video-generate` 74

El mismo Kandinsky 5.0 Video Lite que `Kandinsky-5.0-T2V-Lite-sft-5s`, pero el clip se construye
**a partir de una imagen**: el fotograma inicial fija qué aspecto tienen el personaje y la escena,
y la descripción de la tarea dice qué ocurre en el plano. Es por eso por lo que se ha creado el
registro: **un mismo personaje en varios clips** no se sostiene con palabras, lo sostiene el
fotograma de referencia.

**No existe variante de 10 segundos de la versión Lite i2v**: en kandinskylab sólo está publicado
`Kandinsky-5.0-I2V-Lite-5s` (comprobado con la lista de repositorios de la organización el
2026-08-20). Los 10 segundos los tienen la `T2V-Lite` de texto y las versiones Pro, y estas
últimas piden otro equipo.

## Cómo darle el fotograma inicial

El archivo se busca **en la carpeta del proyecto**, con ruta relativa. Hay dos formas:

1. **Escribirlo en la descripción de la tarea**, con una línea que lleve la palabra de indicación
   y el nombre del archivo: «Взять за основу `refs/hero.png`», «Стартовый кадр `refs/hero.png`»,
   `based on refs/hero.png`. La línea se recorta del prompt: el nombre del archivo no llega al
   texto de la generación. Ojo: las palabras de indicación están incrustadas en el código y sólo
   existen en **ruso e inglés**, así que escríbalas en uno de esos dos idiomas.
2. **Hacer referencia a un objeto del proyecto**, con `@obj:OBJ-3` en la descripción. Al lanzar,
   la referencia se despliega en el pasaporte del personaje y las rutas de sus fotogramas de
   referencia; la primera ruta es precisamente el fotograma inicial, y las rutas en sí se quitan
   del prompt. Así resulta más cómodo: el pasaporte se edita en un solo sitio y se aplica a todos
   los fotogramas siguientes.

Un archivo nombrado con palabras es más fuerte que la ruta del objeto. Si no hay ninguna
indicación de archivo, el encargo se cae con un error comprensible: sin imagen, este modelo no
tiene nada que hacer.

La imagen se va a ComfyUI (`POST /upload/image`) a su carpeta `input` con el nombre del encargo y
se pone en el nodo `LoadImage` del grafo; después `ImageScale` la ajusta al `width`×`height` del
fotograma (encuadrando por el centro), así que conviene que las proporciones del fotograma
inicial sean ya cercanas a 768×512.

## Un mismo personaje en una serie de clips

| recurso | qué da |
|---|---|
| **el fotograma de referencia** (este modelo) | la cara, la ropa y el color se mantienen entre clips |
| **`seed` fijo** (`params.seed` en el perfil) | el mismo «carácter» de la generación en todos los planos; vacío es aleatorio, como antes |
| **el encadenado** | guardar el último fotograma de la escena N como imagen y dárselo de entrada a la escena N+1 |
| **el pasaporte literal** en la descripción | la descripción del aspecto se repite palabra por palabra, no se recuenta |

El primer fotograma de la serie resulta cómodo obtenerlo con un modelo t2i normal o dibujarlo a
mano; a partir de él se construye todo lo demás.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos de IA → Kandinsky-5.0-I2V-Lite-5s → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **cuatro archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`, por defecto
  `C:\ai` en Windows y `~/ai` en Linux/macOS):

| Archivo | Tamaño | Adónde |
|---|---|---|
| `kandinsky5lite_i2v_5s.safetensors` | ~4,3 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `clip_l.safetensors` | ~235 MiB | `text_encoders` |
| `hunyuan_video_vae_bf16.safetensors` | ~470 MiB | `vae` |

**En total, unos 16 GB de descarga**, pero tres de los cuatro archivos son **los mismos** que los
de `Kandinsky-5.0-T2V-Lite-sft-5s` y están en el mismo grupo `Kandinsky-5`. Si el modelo de texto
ya está instalado, sólo se descargará el propio DiT, **unos 4,3 GiB**. La descarga es reanudable:
una instalación interrumpida continuará desde donde se paró.

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **MIT** |
| Uso comercial | permitido |
| Qué es obligatorio | conservar el texto de la licencia y el aviso de derechos de autor |
| Texto de la licencia | <https://huggingface.co/kandinskylab/Kandinsky-5.0-I2V-Lite-5s> (ficha del modelo) |
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
| Controlador NVIDIA | **580 o más nuevo** |
| Espacio en disco | unos 18 GB (pesos + paquete ComfyUI); junto al modelo t2v, unos 4,3 GB más |
| Memoria RAM | desde 16 GB |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

**Sobre el controlador.** En el paquete de ComfyUI va torch compilado para CUDA 13.0. Con un
controlador antiguo (por ejemplo, el 527.99 = CUDA 12.0) no falla con un error comprensible, sino
con una **infracción de acceso**: el proceso simplemente desaparece. No es un defecto de AI2P:
actualice el controlador NVIDIA al 580 o superior. Si el modelo «no arranca en silencio», empiece
por `nvidia-smi`.

## Cuánto hay que esperar

Lo mismo que la variante de texto: con 6 GB de VRAM, un clip de **768×512, 121 fotogramas y 50
pasos** se calcula en alrededor de una hora (la medición se tomó en el modelo t2v: 58 minutos y 49
segundos; los pesos y el grafo aquí son del mismo tamaño). El tiempo de espera del encargo es de
180 minutos (`params.timeoutMinutes`); en el ejecutor, el campo «Tiempo de espera de respuesta,
min» es más fuerte, y 0 significa esperar sin límite.

La marcha de la generación se ve en la **consola del encargo** de la ficha de la tarea.

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `width` / `height` | 768 × 512 | resolución del fotograma; a ella se ajusta también la imagen inicial |
| `length` | 121 | fotogramas (unos 5 segundos) |
| `steps` | 50 | pasos de difusión; menos es más rápido y más basto |
| `negative` | vacío | prompt negativo |
| `seed` | no | **seed fijo**: vacío (o 0) es aleatorio en cada encargo |
| `timeoutMinutes` | 180 | cuánto esperar el resultado |

La descripción entera de la tarea se va al prompt, salvo la indicación del fotograma inicial y la
indicación del tipo «результат положить в файл X.mp4»: ambas las ejecuta el conector (y ambas se
reconocen sólo en ruso y en inglés).

## Entrenamiento de LoRA

El modelo sabe trabajar con adaptador LoRA, y **el adaptador se puede entrenar directamente desde
AI2P**: con el editor de LoRA (ficha del proyecto → «Objetos» → objeto personaje → «Conjunto de
datos y entrenamiento»).

**Con qué se entrena.** Con [musubi-tuner](https://github.com/kohya-ss/musubi-tuner): tiene una
tarea ya hecha, `k5-lite-i2v-5s-sd`, exactamente para estos pesos, funciona con **una sola**
tarjeta gráfica y sabe calcular la atención mediante `sdpa`. El oficial
[kandinsky-5-lora-train](https://github.com/kandinskylab/kandinsky-5-lora-train) no sirve para
esto: levanta `torchrun` e `init_device_mesh("cuda")`, es decir, exige NCCL y varias tarjetas
gráficas, y en Windows no arranca en absoluto.

**Qué se instala además.** El paquete `musubi-tuner` (código fuente, unos 30 MB) y **Python 3.12**
(unos 45 MB) llegan junto con el modelo, con el botón «Instalar», allí mismo donde se descargan
los pesos; un Python 3.10–3.12 que ya esté en el ordenador se toma tal cual y no se vuelve a
descargar. En el primer lanzamiento del entrenamiento, el entrenador se crea al lado un entorno
(`.venv`) y le instala `torch` para CUDA 12.4: eso son **unos 3 GB** más y de diez a veinte
minutos la primera vez. Después el entorno se reutiliza.

Otros dos archivos los descarga el propio entrenador en el primer cacheado del texto, y **no son
los mismos archivos que usa la generación**: la generación usa las compilaciones recortadas de
ComfyUI, mientras que el entrenamiento necesita los modelos originales de Hugging Face:

| Qué | Tamaño | De dónde |
|---|---|---|
| `Qwen/Qwen2.5-VL-7B-Instruct` | ~16 GB | caché de Hugging Face |
| `openai/clip-vit-large-patch14` | ~1,7 GB | caché de Hugging Face |

**Qué hace falta en el ordenador:** Python **3.10–3.12** en el `PATH` (si no, el entrenamiento se
detendrá con un mensaje al respecto) y una tarjeta gráfica NVIDIA. El entrenamiento del adaptador
de un modelo Lite cabe en **13–16 GB de VRAM**; el cacheado de latentes exige más: es el paso más
glotón. La ruta propia a Python se indica con la variable de entorno `AI2P_LORA_PYTHON`, y el
índice propio de ruedas de torch, con `AI2P_LORA_TORCH_INDEX` (por defecto,
`https://download.pytorch.org/whl/cu124`).

**Cómo va el entrenamiento.** AI2P deja los fotogramas del conjunto de datos en
`<carpeta del proyecto>/lora/<código del objeto>/dataset` (con un `.txt` del mismo nombre por cada
fotograma, con su pie), escribe al lado `dataset.toml` y `train.cmd` y lanza este último. El
script da tres pasos seguidos: caché de latentes → caché de las salidas de los codificadores de
texto → el entrenamiento en sí. El adaptador terminado va a
`<repositorio de modelos>/loras/<código del objeto>.safetensors` y pasa a ser el adaptador actual
del objeto; de ahí lo coge la generación (el nodo `LoraLoaderModelOnly`).

`dataset.toml` y `train.cmd` **se reescriben antes de cada lanzamiento**: pertenecen al perfil del
modelo (la sección `lora.train.files`), y hay que editarlos ahí, no en el disco. Allí mismo se
cambian el número de pasos (`lora.train.start.steps`, por defecto 2000), el rango y todas las
opciones del entrenador.

**Lo que este camino no da.** En AI2P el conjunto de datos está hecho de **imágenes**, no de
clips, así que lo que se entrena así es un adaptador de **aspecto** (personaje, estilo). El
adaptador de **movimiento** (como los oficiales `Arc-right`, `Dolly-in`) se entrena con vídeo, y
en el editor de LoRA todavía no hay conjunto de datos de vídeo.

## Errores frecuentes

* **«Este modelo necesita un fotograma inicial»**: en la descripción no hay ni archivo de imagen
  ni referencia a un objeto con fotograma de referencia.
* **«El archivo del fotograma inicial no se ha encontrado en la carpeta del proyecto»**: la ruta
  se cuenta desde la carpeta del proyecto; la salida hacia fuera (`..`, `C:\…`) está prohibida a
  propósito.
* **«El proyecto no tiene carpeta indicada en este ordenador»**: la carpeta del proyecto se indica
  en su ficha y es propia de cada servidor.
* **El personaje «se desdibuja» de todos modos**: compruebe que el fotograma inicial sea el mismo
  y que el `seed` esté fijado; los clips con `width`/`height` distintos también se separan.
* **El proceso desaparece sin mensaje**: casi siempre es un controlador NVIDIA antiguo.
* **Falta de VRAM**: reduzca `width`/`height` o `length`.
* **«cannot create the virtual environment»** al entrenar LoRA: en el ordenador no hay Python
  3.10–3.12 o no está en el `PATH`; instálelo o indique la ruta en `AI2P_LORA_PYTHON`.

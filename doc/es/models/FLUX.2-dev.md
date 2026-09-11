# FLUX.2-dev

> **Atención: la licencia no es libre.** Los pesos de FLUX.2 [dev] se han entregado bajo la
> **FLUX Non-Commercial License v2.1**: sólo se pueden usar para trabajo no comercial y no
> productivo. Para un uso comercial hace falta una licencia aparte de Black Forest Labs. Los
> detalles están en la sección «Licencia» de más abajo; la alternativa libre de esa misma familia
> es `FLUX.2-klein-4B` (Apache 2.0).

**Alojamiento:** LOCAL (funciona en este ordenador)
**Conexión:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `flux2_dev`
**Clave de API:** no hace falta; el servidor se levanta localmente sin autorización
**Qué hace:** texto → imagen, habilidades `image-generate` 94, `image-photo` 93,
`image-concept` 92, `image-text` 90

El modelo mayor de la familia FLUX.2 de Black Forest Labs, 32 000 millones de parámetros: **lo más
alto en calidad entre los pesos abiertos** y el registro más pesado del catálogo, con unos 54 GB
de descarga. Tiene sentido tenerlo si dispone de una tarjeta potente y el trabajo es no comercial;
en todos los demás casos coja `FLUX.2-klein-4B` o `Qwen-Image-2512`.

Funciona a través de **ComfyUI**: AI2P lo levanta como servidor local, le envía el workflow y
recoge el `.png` terminado en los artefactos de la tarea.

## No hace falta clave, hace falta instalación

Todo se instala con el botón **«Instalar»** del formulario del modelo: **Ajustes → Catálogos →
Modelos → FLUX.2-dev → «Instalar»**. Se instalan:

* el **paquete ComfyUI** (unos 2,1 GB), una compilación portátil con su propio Python;
* **tres archivos de pesos** en el repositorio de modelos (`storage.modelsRepo`), grupo
  `FLUX.2-dev`:

| Archivo | Tamaño | Adónde |
|---|---|---|
| `flux2_dev_fp8mixed.safetensors` | ~33,0 GiB | `diffusion_models` |
| `mistral_3_small_flux2_fp8.safetensors` | ~16,8 GiB | `text_encoders` |
| `flux2-vae.safetensors` | ~320 MiB | `vae` |

**En total, unos 54 GB de descarga.** La descarga es reanudable: una instalación interrumpida
continuará desde donde se paró. Ambas compilaciones pesadas ya están en fp8: las bf16 originales
pesan el doble y en una tarjeta de consumo no hacen falta.

El codificador de texto de dev es propio, **Mistral 3 Small**, y no Qwen3 como el de klein: los
archivos de la familia no son comunes entre registros.

Mientras los archivos no estén en su sitio, el modelo **no puede estar activo**, y eso se
comprueba también al arrancar la aplicación.

## Licencia

| | |
|---|---|
| Pesos del modelo | **FLUX Non-Commercial License v2.1**: NO LIBRE |
| Uso comercial | **prohibido** sin un contrato aparte con Black Forest Labs |
| Qué está permitido | investigación personal, experimentos, estudio, afición: todo aquello por lo que usted no reciba pago directo ni indirecto |
| Qué es obligatorio | conservar el texto de la licencia y los avisos, y no quitar los filtros de contenido |
| Resultado de la generación | según las condiciones de la licencia **no se considera obra derivada** del modelo, pero eso no anula la prohibición de uso comercial de los propios pesos |
| Texto de la licencia | <https://huggingface.co/black-forest-labs/FLUX.2-dev/blob/main/LICENSE.md> |
| Licencia comercial | <https://bfl.ai/> |
| Pago por la generación | no lo hay: calcula su tarjeta gráfica |

La licencia se cotejó con el texto del `LICENSE.md` del repositorio el 27.08.2026. La formulación
de la fuente original: los pesos, los parámetros y el código de inferencia se entregan «freely
available for your **non-commercial and non-production** use».

**Esa misma licencia tiene FLUX.2 [klein] 9B** (tanto la versión base como las compilaciones
fp8). En toda la familia sólo es libre la 4B, que tiene Apache 2.0.

Los archivos se descargan del reempaquetado abierto de Comfy-Org
(<https://huggingface.co/Comfy-Org/flux2-dev>): los repositorios originales de Black Forest Labs
se reparten mediante consentimiento con la licencia y, sin token de HuggingFace, responden
`401 Unauthorized`. El reempaquetado va bajo **esa misma** licencia no libre: que el archivo se
descargue sin token no quita ninguna limitación.

Aparte: el propio **ComfyUI se distribuye bajo GPL-3.0**. AI2P lo arranca como programa externa y
se comunica con él por HTTP, así que esa licencia no pasa a su producto; pero si usted le lleva a
alguien un paquete con ComfyUI dentro, las condiciones de la GPL se aplican a él.

## Requisitos de equipo

| | |
|---|---|
| Tarjeta gráfica | NVIDIA, **desde 24 GB de VRAM**; con 16 GB funciona descargando a la memoria RAM y bastante más despacio |
| Controlador NVIDIA | **580 o más nuevo**; con un controlador antiguo torch se cae sin mensaje |
| Espacio en disco | unos 57 GB (pesos + paquete ComfyUI) |
| Memoria RAM | **desde 48 GB**: al descargar capas, el modelo se mantiene entero en ella |
| SO | Windows (compilación portátil de ComfyUI); en Linux, ComfyUI se instala a mano |

Es el registro más exigente del catálogo en las tres medidas a la vez: memoria de vídeo, memoria
RAM y espacio en disco.

## Cuánto hay que esperar

Veinte pasos en un fotograma de 1024×1024 son **uno o dos minutos** en una tarjeta de 24 GB y
**mucho más** si el modelo se descarga a la memoria RAM. El tiempo de espera del encargo está
puesto en el perfil en 120 minutos (`params.timeoutMinutes`).

La marcha de la generación se ve en la **consola del encargo** de la ficha de la tarea.

## Parámetros de generación

Se editan en el perfil del modelo (botón «Perfil de conexión»):

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `width` / `height` | 1024 × 1024 | resolución del fotograma |
| `steps` | 20 | pasos de difusión |
| `negative` | vacío | **no tiene efecto**: dev no tiene condición negativa en absoluto (véase más abajo) |
| `timeoutMinutes` | 120 | cuánto esperar el resultado |

**Sobre el prompt negativo.** FLUX.2 [dev] es un modelo destilado con guiado dirigido: en el grafo
están `FluxGuidance` (fuerza 4) y `BasicGuider`, y ahí no hay condición negativa. El valor de
`negative` se puede escribir en el perfil, pero no influirá en la imagen. Si necesita negativo,
coja `FLUX.2-klein-4B`, que tiene pesos base y un `cfg` de verdad.

La descripción entera de la tarea se va al prompt. FLUX.2 entiende bien las descripciones largas y
coherentes: escriba con frases, no con una enumeración de palabras clave.

## Entrenamiento de LoRA

**La aplicación, sí; el entrenamiento desde AI2P, no.**

Un adaptador ya hecho se conecta como en todos los registros de ComfyUI: el nodo
`LoraLoaderModelOnly` se inserta en el grafo sobre la marcha y el archivo se coge de
`<repositorio de modelos>/loras`.

Entrenar un adaptador directamente desde AI2P no se puede, aunque el entrenador existe:
[musubi-tuner](https://github.com/kohya-ss/musubi-tuner) sabe manejar FLUX.2 [dev]
(`flux_2_train_network.py --model_version dev`), pero exige los pesos **originales** del
repositorio cerrado de Black Forest Labs —el `flux2-dev.safetensors` suelto y el Mistral 3
troceado— y no el reempaquetado que instala AI2P. Descargarlos sólo se puede con un token de
HuggingFace y tras aceptar la licencia, así que en el registro está puesto
`lora.train.kind = external`: el botón de entrenamiento responde con una negativa enseguida.

Además, el autor del entrenador aconseja expresamente entrenar los adaptadores no en dev, sino en
los pesos base de klein, que están hechos para eso. Un adaptador entrenado en `FLUX.2-klein-4B`
**no le vale** a dev: son modelos de tamaños distintos.

## Errores frecuentes

* **Falta de VRAM**: reduzca `width`/`height`; si no ayuda, este modelo no es para su tarjeta,
  coja `FLUX.2-klein-4B`.
* **La generación tarda decenas de minutos**: el modelo se está descargando a la memoria RAM;
  compruebe que hay suficiente (véase «Requisitos de equipo»).
* **El prompt negativo no tiene efecto**: así está hecho dev (véase «Parámetros de generación»).
* **`401 Unauthorized` al descargar usted a mano**: está descargando del repositorio de Black
  Forest Labs; en el manifiesto de AI2P está el reempaquetado abierto de Comfy-Org.
* **El botón de entrenamiento de LoRA se niega**: está pensado así (véase más arriba).

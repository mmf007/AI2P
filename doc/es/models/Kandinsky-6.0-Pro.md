# Kandinsky-6.0-Pro

**Alojamiento:** nube — pasarela [fal.ai](https://fal.ai/models)
**Conexión:** `provider: fal-ai`, `baseUrl: https://queue.fal.run`, modelo `fal-ai/kandinsky6-pro/text-to-video`
**Referencia a la clave:** `fal.apiKey`
**Qué hace:** texto → vídeo de 5 segundos con sonido, habilidades `video-generate` 90

Kandinsky Lab publicó este modelo el 05.10.2026; entró en el catálogo de AI2P con la tarea
T-466-S0, la revisión de proveedores del 13.10.2026. El identificador `fal-ai/kandinsky6-pro/text-to-video`, el precio y
las modalidades se han verificado con una consulta al catálogo público fal.ai.

La generación siguiente a **Kandinsky-5.0**. Precio de la pasarela: 1,35 $ por clip de 5 segundos 480p con 50 pasos (≈ 0,27 $ por segundo), escalado a 1080p 1,53 $, menos pasos cuestan menos (20 pasos 0,56 $). La variante Lite es la misma versión, endpoint `fal-ai/kandinsky6-lite/text-to-video` (0,16 $ por clip).

## Cómo conseguir la clave

1. Cree una cuenta en [fal.ai](https://fal.ai/) (acceso con GitHub o Google).
2. Recargue el saldo en la sección [Billing](https://fal.ai/dashboard/billing): la pasarela
   funciona con prepago, y sin dinero en la cuenta la petición se rechaza.
3. Abra [API Keys](https://fal.ai/dashboard/keys) y pulse **Add key**.
4. Copie el valor: se muestra **una sola vez**.
5. En AI2P: **Ajustes → Modelos → Kandinsky-6.0-Pro → «Clave de API»**.

La clave es **una sola** para todos los registros de fal.ai (referencia `fal.apiKey`): puesta una
vez, activa de golpe los doce modelos de medios en la nube. La clave pertenece a la organización:
se cifra con su clave y se replica a todos los servidores de la organización (cap. 10 de la
especificación). Sin clave, el registro del catálogo no puede estar activo y no se puede poner
como ejecutor.

## Límites y coste

| | |
|---|---|
| Duración | 5 segundos |
| Resolución | 480p, escalado a 1080p o 4x |
| Precio | 1,35 $ por clip con 50 pasos |

El identificador del modelo y los precios se cotejaron con el catálogo del proveedor el
**13.10.2026** (peticiones a `https://fal.ai/api/models` y al esquema de petición del endpoint).
**El modelo no se ha comprobado con una llamada real**: la generación es de pago. Coteje usted
mismo el id y el precio: el `GET /models` del catálogo de la pasarela
(`https://fal.ai/api/models`) y la [página del modelo](https://fal.ai/models/fal-ai/kandinsky6-pro/text-to-video).
Los precios de la pasarela cambian sin aviso.

La cuenta la lleva el proveedor. **En el coste del encargo de AI2P este precio no se tiene en
cuenta** y se queda en cero: en los modelos de medios no se paga por tokens, sino por imagen,
segundo o minuto, y en la respuesta de la pasarela no hay tokens en absoluto. La tarifa se escribe
con palabras en la consola del encargo y en el resumen del resultado.

## Licencia

Los pesos de Kandinsky 6.0 son **abiertos** con licencia MIT (`kandinskylab/Kandinsky-6.0-Pro-5s-Diffusers`, verificado
el 13.10.2026 con una consulta a la API de HuggingFace), pero el repositorio exige aceptar un acuerdo (`gated: auto`):
sin token personal no se descargan. Este registro no usa los pesos: calcula la pasarela fal.ai.

* El catálogo de la pasarela marca el registro `licenseType: commercial`: **se permite el uso comercial del resultado**.
* Condiciones: [página del modelo](https://fal.ai/models/fal-ai/kandinsky6-pro/text-to-video) y [condiciones de fal.ai](https://fal.ai/terms).
* Pago por generación: **sí**, lo cobra el proveedor; tarifa en la sección de precio.

## Requisitos de equipo

Ninguno: calcula el proveedor. Hace falta salida a internet hacia `queue.fal.run` (poner el
encargo y consultar su estado) y hacia el almacenamiento de archivos de la pasarela
(`*.fal.media`), de donde AI2P descarga el archivo terminado a los artefactos de la tarea.

## Cómo se plantea el encargo

Como prompt del modelo de medios sirve **la descripción entera de la tarea**: ni el título, ni los
criterios de aceptación, ni la experiencia del proyecto llegan hasta él.

* la referencia `@obj:OBJ-3` de la descripción se despliega en el pasaporte literal del objeto del
  proyecto (personaje, localización, estilo) en **cada** lanzamiento: corregir el pasaporte se
  aplica ya al siguiente fotograma;
* la línea «положить результат в файл `video/plano-1.mp4`» pone además el archivo terminado en la
  carpeta del proyecto; en los artefactos de la tarea está siempre. Las palabras de la indicación
  se reconocen **sólo en ruso y en inglés** (en inglés sería
  `Save the result to the file video/plano-1.mp4`): están incrustadas en el código y no dependen
  del idioma de la instalación;
* a este modelo no hay con qué pasarle un fotograma inicial; si en la descripción se nombra un
  archivo de imagen, AI2P lo dirá con una línea en la consola del encargo y no se callará;
* los campos de la petición (duración, resolución, voz, número de polígonos) se editan en el perfil
  del modelo, en la sección `request`; coja los valores del esquema del endpoint de la página del
  modelo, porque un valor desconocido la pasarela lo rechaza con un HTTP 422.

## LoRA

El entrenamiento y la conexión de un adaptador aquí **no están disponibles**, y en el catálogo
está anotado honestamente: `"lora": { "supported": false, "reason": "provider" }`. Los pesos del
modelo son cerrados y el proveedor no acepta un archivo de adaptador propio. Un encargo en el que
se nombre una referencia a un objeto con adaptador LoRA no llegará a la generación: AI2P se negará
enseguida y propondrá en el chat un ejecutor que sí sepa hacerlo (T-14-S1). Aquí la imagen
constante la mantienen el fotograma inicial (el registro **Seedance-2.5-I2V**) y un pasaporte
detallado del objeto en la descripción.

## Errores frecuentes

* **«No se ha encontrado la clave de API»**: la clave no está puesta en ningún registro de fal.ai;
  póngala una vez en cualquiera de ellos.
* **HTTP 401 / 403 de la pasarela**: la clave no sirve o está revocada.
* **HTTP 429**: el proveedor ha limitado la frecuencia de peticiones; repita el encargo más tarde.

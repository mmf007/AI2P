# FLUX-3-Max-Video

**Alojamiento:** nube — pasarela [fal.ai](https://fal.ai/models)
**Conexión:** `provider: fal-ai`, `baseUrl: https://queue.fal.run`, modelo `blackforestlabs/flux-3-max/text-to-video`
**Referencia a la clave:** `fal.apiKey`
**Qué hace:** texto → vídeo de 5 a 20 segundos con sonido, habilidades `video-generate` 93

Black Forest Labs publicó este modelo el 16.10.2026; entró en el catálogo de AI2P con la tarea
T-558-S0, la revisión de proveedores del 20.10.2026. El identificador `blackforestlabs/flux-3-max/text-to-video`, el precio y
las modalidades se han verificado con una consulta al catálogo público fal.ai.

El primer modelo de vídeo de **Black Forest Labs** en el catálogo (antes BFL solo tenía imágenes: **FLUX-3**). Precio de la pasarela: 0,17 $ por segundo en 720p y 0,29 $ en 1080p; del 15.10 al 07.11.2026 rige un descuento de lanzamiento del 75 % (0,0425 $ y 0,0725 $), la declaración guarda el precio normal. La pasarela ofrece también los modos imagen → vídeo y fotogramas clave → vídeo de la misma versión (`flux-3-max/image-to-video`, `flux-3-max/keyframes-to-video`).

## Cómo conseguir la clave

1. Cree una cuenta en [fal.ai](https://fal.ai/) (acceso con GitHub o Google).
2. Recargue el saldo en la sección [Billing](https://fal.ai/dashboard/billing): la pasarela
   funciona con prepago, y sin dinero en la cuenta la petición se rechaza.
3. Abra [API Keys](https://fal.ai/dashboard/keys) y pulse **Add key**.
4. Copie el valor: se muestra **una sola vez**.
5. En AI2P: **Ajustes → Modelos → FLUX-3-Max-Video → «Clave de API»**.

La clave es **una sola** para todos los registros de fal.ai (referencia `fal.apiKey`): puesta una
vez, activa de golpe los doce modelos de medios en la nube. La clave pertenece a la organización:
se cifra con su clave y se replica a todos los servidores de la organización (cap. 10 de la
especificación). Sin clave, el registro del catálogo no puede estar activo y no se puede poner
como ejecutor.

## Límites y coste

| | |
|---|---|
| Duración | 5-20 segundos (5 en la plantilla) |
| Resolución | 720p / 1080p (720p en la plantilla) |
| Sonido | nativo, campo `generate_audio` |
| Precio | 0,17 $ por segundo en 720p (0,0425 $ hasta el 07.11.2026) |

El identificador del modelo y los precios se cotejaron con el catálogo del proveedor el
**20.10.2026** (peticiones a `https://fal.ai/api/models` y al esquema de petición del endpoint).
**El modelo no se ha comprobado con una llamada real**: la generación es de pago. Coteje usted
mismo el id y el precio: el `GET /models` del catálogo de la pasarela
(`https://fal.ai/api/models`) y la [página del modelo](https://fal.ai/models/blackforestlabs/flux-3-max/text-to-video).
Los precios de la pasarela cambian sin aviso.

La cuenta la lleva el proveedor. **En el coste del encargo de AI2P este precio no se tiene en
cuenta** y se queda en cero: en los modelos de medios no se paga por tokens, sino por imagen,
segundo o minuto, y en la respuesta de la pasarela no hay tokens en absoluto. La tarifa se escribe
con palabras en la consola del encargo y en el resumen del resultado.

## Licencia

Los pesos del modelo son cerrados y no se reparten a nadie: aquí no hay nada que licenciar, así
que las condiciones se refieren al **resultado de la generación**, no a los pesos. El propietario
del modelo es Black Forest Labs; junto a ello rigen las condiciones de la propia pasarela.

* En el catálogo del proveedor este registro tiene `licenseType: commercial`: **el uso comercial
  del resultado está permitido** (cotejado el 20.10.2026 con una petición al catálogo).
* Condiciones exactas: la [página del modelo en la pasarela](https://fal.ai/models/blackforestlabs/flux-3-max/text-to-video)
  y las [condiciones de fal.ai](https://fal.ai/terms).
* El propietario del modelo puede cambiar las condiciones sobre el resultado; no se heredan con
  efecto retroactivo sobre lo que usted ya haya generado, pero antes de publicar una serie
  conviene releerlas.
* Pago por la generación: **sí lo hay**; lo cuenta el proveedor, y la tarifa está más arriba, en
  la sección del coste.

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

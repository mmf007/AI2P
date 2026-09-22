# Zonos-2-TTS

**Alojamiento:** nube — pasarela [fal.ai](https://fal.ai/models)
**Conexión:** `provider: fal-ai`, `baseUrl: https://queue.fal.run`, modelo `fal-ai/zonos2`
**Referencia a la clave:** `fal.apiKey`
**Qué hace:** texto + muestra de voz → voz sintetizada con ese timbre, habilidades `audio-speech` 86

Los primeros registros del catálogo que aceptan **audio de referencia** (mecanismo de
T-249-S0). Aquí la voz constante de un personaje no la sostiene un adaptador LoRA sino una
**muestra**: 3-30 segundos de grabación van al campo `reference_audio_url` de la petición y el modelo copia
el timbre (clonación zero-shot). El texto del encargo va al campo `text`.

El idioma de normalización del texto se fija con `language` (`en_us` en la plantilla; el esquema lista `en_us`, `en_gb`, `fr_fr`, `de` y otros). `accurate_mode: true` se acerca más a la voz de la muestra, `false` es más expresivo. El resultado llega como WAV 44,1 kHz mono.

## Cómo se usa

1. Cree en el proyecto un objeto del tipo **grabación de referencia** (pestaña «Objetos» de la
   ficha del proyecto) y ponga en el campo de archivo la ruta a un `.wav` o `.mp3` relativa a la
   carpeta del proyecto; normalmente como hijo del personaje.
2. Refiéralo desde la descripción de la tarea con `@obj:OBJ-N` o nombre el archivo con palabras
   («muestra de voz refs/vera.wav»).
3. Apunte el ejecutor a este registro del catálogo: la muestra de voz es **obligatoria**: en el esquema del endpoint `reference_audio_url` es el único campo requerido (`required: ['reference_audio_url']`). Sin grabación la pasarela responde HTTP 422, por eso AI2P detiene el encargo antes, de su lado.

## Cómo conseguir la clave

1. Cree una cuenta en [fal.ai](https://fal.ai/) (acceso con GitHub o Google).
2. Recargue el saldo en [Billing](https://fal.ai/dashboard/billing): la pasarela funciona con
   prepago y sin saldo la petición se rechaza.
3. Abra [API Keys](https://fal.ai/dashboard/keys) y pulse **Add key**.
4. Copie el valor: se muestra **una sola vez**.
5. En AI2P: **Ajustes → Modelos → Zonos-2-TTS → «Clave API»**.

Todos los registros de fal.ai comparten **una** clave (referencia `fal.apiKey`): al ponerla una
vez activa de golpe todos los modelos multimedia en la nube de la pasarela. La clave pertenece a
la organización: se cifra con su clave y se replica a todos sus servidores (cap. 10 del ETR).
Sin clave el registro no puede estar activo ni asignarse a un ejecutor.

## Límites y coste

| | |
|---|---|
| Texto | toda la descripción de la tarea va al campo `text` |
| Muestra de voz | campo `reference_audio_url`, **una** grabación, hasta **30 segundos**, `audio/wav` o `audio/mpeg` |
| Duración de la muestra | `maxSeconds: 30` se **guarda pero AI2P no lo comprueba**: un archivo demasiado largo lo rechaza la propia pasarela |
| Coste | la pasarela **no publica precio** para este registro: consúltelo en la página del modelo y en la factura |

El identificador del modelo, el nombre del campo de la muestra y el precio se cotejaron con el
catálogo del proveedor el **14.09.2026** (peticiones a `https://fal.ai/api/models` y al esquema
OpenAPI de la cola del endpoint). **El modelo no se ha comprobado con una llamada real**: la
generación es de pago. Coteje usted mismo el id y el precio: `GET /models` del catálogo
(`https://fal.ai/api/models`) y la [página del modelo](https://fal.ai/models/fal-ai/zonos2). Los
precios de la pasarela cambian sin aviso.

La facturación la lleva el proveedor. **El coste del encargo en AI2P no cuenta este precio** y
queda en cero: los modelos multimedia se pagan por carácter, segundo o minuto, no por tokens, y
en la respuesta de la pasarela no hay tokens en absoluto. La tarifa se escribe con palabras en la
consola del encargo y en el resumen del resultado.

## Licencia

Las condiciones se refieren al **resultado de la generación**, no a los pesos: calcula el
proveedor. El propietario del modelo es Zyphra; junto a él rigen las condiciones de la pasarela.

* En el catálogo del proveedor este registro lleva `licenseType: commercial`: **el uso comercial
  del resultado está permitido** (cotejado el 14.09.2026 con una petición al catálogo).
* Los pesos del propio modelo son abiertos bajo **Apache 2.0** — ficha [https://huggingface.co/Zyphra/ZONOS2](https://huggingface.co/Zyphra/ZONOS2), campo
  `cardData.license` comprobado con una petición a `https://huggingface.co/api/models` el
  14.09.2026. Esto importa también en la conexión en nube: la licencia de los pesos no prohíbe el
  uso comercial de una voz sintetizada a partir de su propia muestra.
* Condiciones exactas: [página del modelo en la pasarela](https://fal.ai/models/fal-ai/zonos2) y
  [condiciones de fal.ai](https://fal.ai/terms).
* Normas de uso del propietario del modelo: https://www.zyphra.com/terms-of-service
* **La licencia del modelo no otorga derechos sobre la voz en sí.** Una muestra de la voz de otra
  persona sin su consentimiento es un riesgo jurídico aparte, y ni MIT ni Apache 2.0 lo eliminan.
* Pago por la generación: **sí**, lo cuenta el proveedor; la tarifa está arriba, en el apartado de coste.

## Requisitos de equipo

Ninguno: calcula el proveedor. Hace falta salida a internet hacia `queue.fal.run` (envío del
encargo y consulta del estado) y hacia el almacenamiento de archivos de la pasarela
(`*.fal.media`), de donde AI2P descarga el archivo listo a los artefactos de la tarea. La muestra
de voz va **en el cuerpo de la petición** como dirección `data:`; la pasarela no tiene una carga
de archivos aparte.

## Cómo se plantea el encargo

El prompt de un modelo multimedia es **toda la descripción de la tarea**: ni el título, ni los
criterios de aceptación, ni la experiencia del proyecto llegan hasta él.

* la referencia `@obj:OBJ-3` de la descripción se despliega en el pasaporte literal del objeto del
  proyecto en **cada** ejecución: editar el pasaporte surte efecto en el siguiente encargo;
* una referencia a un objeto del tipo **grabación de referencia** OBLIGA a pasar el archivo;
* la línea «poner el resultado en el archivo `audio/replica-1.wav`» deja además el archivo listo en
  la carpeta del proyecto; en los artefactos de la tarea está siempre;
* a este modelo no se le puede pasar un fotograma inicial (`refImage.kind: none`): si la
  descripción nombra un archivo de imagen, AI2P lo dice en la consola del encargo;
* los campos de la petición se editan en el perfil del modelo, apartado `request`; tome los valores
  del esquema del endpoint, un valor desconocido lo rechaza la pasarela con HTTP 422.

## LoRA

Aquí el entrenamiento y la conexión de un adaptador **no están disponibles**, y en el catálogo está
escrito con honestidad: `"lora": { "supported": false, "reason": "provider" }`. Los pesos están del
lado del proveedor y no acepta su archivo de adaptador. Un encargo que nombre una referencia a un
objeto con adaptador LoRA no llegará a la generación: AI2P lo rechaza enseguida y ofrece en el chat
un ejecutor capaz (T-14-S1). Aquí la voz constante la sostiene la **grabación de referencia**, y eso
sale más barato que entrenar: un clon de diez segundos no es peor que un adaptador y cuesta cero.

## Errores frecuentes

* **«No se ha pasado la grabación de referencia»**: la descripción no nombra ni una referencia
  `@obj:` a un objeto del tipo «grabación de referencia» ni un archivo de audio; el marcador
  `{audio}` está en la plantilla y el encargo se detiene antes de enviarse.
* **«Este modelo no acepta grabación»**: el ejecutor apunta a otro registro; la sección `refAudio`
  solo la declaran los registros nombrados en el informe T-251-S0.
* **HTTP 422 de la pasarela**: en la plantilla ha aparecido un campo que el endpoint no tiene, o un
  valor fuera de la enumeración del esquema.
* **«No se encuentra la clave API»**: la clave no está puesta en ningún registro de fal.ai; póngala
  una vez en cualquiera de ellos.
* **HTTP 401 / 403 de la pasarela**: la clave no sirve o ha sido revocada.
* **HTTP 429**: el proveedor ha limitado la frecuencia de peticiones, repita el encargo más tarde.

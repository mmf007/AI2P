# Muse-Wonder

**Alojamiento:** nube (Meta, **a través de la pasarela OpenRouter**)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
**Referencia a la clave:** `openrouter.apiKey`

Meta publicó este modelo el 12.10.2026; entró en el catálogo de AI2P con la tarea
T-466-S0, la revisión de proveedores del 13.10.2026. El identificador `meta/muse-wonder`, el precio y
las modalidades se han verificado con una consulta al catálogo público OpenRouter.

Contexto de 1 048 576 tokens, respuesta de hasta 943 718 tokens, 4,00 $ y 20,00 $ por millón de
tokens, puntuaciones 91-94. El registro más cercano del catálogo es **Muse-Spark-1.3**.

Entrada admitida: texto y Markdown, código fuente, imágenes, PDF, audio, vídeo.

## Cómo conseguir la clave

**La clave es una sola para todos los modelos que van por la pasarela**: Inkling-975B,
Nemotron-3-Ultra, Ling-3.0-Flash y este hacen referencia a ese mismo `openrouter.apiKey`.
Introducida una vez, funcionan los cuatro.

1. Regístrese en [openrouter.ai](https://openrouter.ai/).
2. Recargue el saldo: **Credits → Add credits** (tarjeta o criptomoneda). Sin saldo sólo están
   disponibles las variantes gratuitas de los modelos, con cola.
3. Abra [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**, póngale un nombre y, si
   hace falta, un límite de gasto por clave.
4. Copie el valor: se muestra **una sola vez**. La clave empieza por `sk-or-`.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Muse-Wonder → «Establecer la clave de
   API»**.

La clave pertenece a la organización: se cifra con su clave y se replica a todos los servidores de
la organización (cap. 10 de la especificación).

## Límites y coste

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Máximo de respuesta | 943 718 tokens |
| Coste de entrada | 4,00 $ por 1 millón de tokens |
| Coste de salida | 20,00 $ por 1 millón de tokens |

**El identificador del modelo no se ha comprobado con una llamada real.** En los modelos que van
por la pasarela se pone con el **slug completo, con el prefijo del proveedor**
(`meta/muse-wonder`); sin el prefijo la pasarela responderá 404. Cotéjelo con una petición
`GET /models` a `https://openrouter.ai/api/v1` (esa lista la entrega la pasarela incluso sin
clave).

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | las fija el **propietario del modelo**, no la pasarela: «Your ownership rights in the Output are set forth in the Model Terms for each Model you use» |
| Uso comercial | consulte los Model Terms en la ficha del modelo de la pasarela: la pasarela responde de la entrega, no de los derechos |
| Qué es obligatorio | cumplir las condiciones tanto de la pasarela como del propietario del modelo; el propietario puede cambiar sus condiciones en cualquier momento |
| Texto de las condiciones | <https://openrouter.ai/terms> (redacción del 13.10.2026) más los Model Terms de la ficha del modelo |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Los pesos de este modelo no están en acceso abierto (en HuggingFace no se ha encontrado el
repositorio, comprobado el 13.10.2026): no hay nada que licenciar, y sólo rigen las condiciones
del propietario del modelo y de la pasarela.

La pasarela, por su parte, **renuncia en lo posible al entrenamiento con los datos** en los
proveedores conectados, pero no responde de la exactitud de las condiciones ajenas y lo dice
expresamente.

Las condiciones de la pasarela se cotejaron con su texto el 13.10.2026.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`openrouter.ai`.

## Errores frecuentes

* **404 «No endpoints found»**: en el campo «modelo» se ha perdido el prefijo del proveedor, o ha
  cambiado el slug en la pasarela.
* **402 «Insufficient credits»**: no se ha recargado el saldo de OpenRouter.
* **La respuesta ha salido más cara de lo calculado**: la pasarela cobra su comisión por encima
  del precio del proveedor.

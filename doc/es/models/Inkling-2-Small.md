# Inkling-2-Small

**Alojamiento:** nube (Thinking Machines, **a través de la pasarela OpenRouter**)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
**Referencia a la clave:** `openrouter.apiKey`

Thinking Machines publicó este modelo el 09.11.2026; entró en el catálogo de AI2P con la tarea
T-736-S0, la revisión de proveedores del 13.11.2026. El identificador `thinkingmachines/inkling-2-small`, el precio y
las modalidades se han verificado con una consulta al catálogo público OpenRouter.

Contexto de 1 048 576 tokens, respuesta de hasta 943 718 tokens, 0,30 $ y 1,00 $ por millón de
tokens, puntuaciones 81-86. El registro más cercano del catálogo es **Inkling-975B**.

Entrada admitida: texto y Markdown, código fuente, imágenes.

## Cómo conseguir la clave

**La clave es una sola para todos los modelos que van por la pasarela**: Muse-Spark-1.2,
Nemotron-3-Ultra, Ling-3.0-Flash y este hacen referencia a ese mismo `openrouter.apiKey`.

1. Regístrese en [openrouter.ai](https://openrouter.ai/).
2. Recargue el saldo: **Credits → Add credits**.
3. Abra [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**.
4. Copie el valor (se muestra una sola vez y empieza por `sk-or-`).
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Inkling-2-Small → «Establecer la clave de API»**.

## Límites y coste

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Máximo de respuesta | 943 718 tokens |
| Coste de entrada | 0,30 $ por 1 millón de tokens |
| Coste de salida | 1,00 $ por 1 millón de tokens |

**El identificador del modelo no se ha comprobado con una llamada real.** En los modelos que van
por la pasarela se pone con el slug completo, con el prefijo del proveedor
(`thinkingmachines/inkling-2-small`). Cotéjelo con una petición `GET /models` a
`https://openrouter.ai/api/v1`: la pasarela entrega esa lista incluso sin clave.

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | las fija el **propietario del modelo**, no la pasarela: «Your ownership rights in the Output are set forth in the Model Terms for each Model you use» |
| Uso comercial | consulte los Model Terms en la ficha del modelo de la pasarela: la pasarela responde de la entrega, no de los derechos |
| Qué es obligatorio | cumplir las condiciones tanto de la pasarela como del propietario del modelo; el propietario puede cambiar sus condiciones en cualquier momento |
| Texto de las condiciones | <https://openrouter.ai/terms> (redacción del 29.07.2026) más los Model Terms de la ficha del modelo |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Los pesos del modelo son abiertos: repositorio `thinkingmachines/Inkling-2-Small` en HuggingFace,
licencia **Apache 2.0** (campo `cardData.license`, `gated=false`, comprobado el 13.11.2026);
se permite el uso comercial, texto: https://www.apache.org/licenses/LICENSE-2.0.
A través de la pasarela rigen las condiciones del propietario del modelo y de la pasarela.

La pasarela, por su parte, **renuncia en lo posible al entrenamiento con los datos** en los
proveedores conectados, pero no responde de la exactitud de las condiciones ajenas y lo dice
expresamente.

Las condiciones de la pasarela se cotejaron con su texto el 13.11.2026.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`openrouter.ai`. Los pesos son abiertos (Apache 2.0), pero 594 000 millones de parámetros no se
levantan en un ordenador propio: para el trabajo local están en el catálogo modelos más pequeños.

## Errores frecuentes

* **404 «No endpoints found»**: se ha perdido el prefijo del proveedor o ha cambiado el slug en la
  pasarela.
* **402 «Insufficient credits»**: no se ha recargado el saldo de OpenRouter.
* **En el resultado han aparecido hechos inventados**: es el nicho del modelo; coteje el resultado
  o coja otro modelo para encargos de tipo factual.

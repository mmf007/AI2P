# Inkling-975B

**Alojamiento:** nube (Thinking Machines, **a través de la pasarela OpenRouter**)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
modelo `thinkingmachines/inkling`
**Referencia a la clave:** `openrouter.apiKey`

Modelo abierto de 975 000 millones de parámetros (41 000 millones activos) bajo licencia **Apache
2.0**. Sus autores lo concibieron como **base para el ajuste fino**, no como plusmarquista:
puntuaciones de habilidad de 81 a 87, contexto de 1 048 576 tokens y precio de 0,95 $ y 4,05 $ por
millón. La entrada acepta texto, código fuente, imágenes y sonido.

**Cuidado con los hechos.** En la prueba de conocimiento del mundo (AA Omniscience) el modelo da
alrededor de un 40 % de precisión con un 63 % de alucinaciones: se inventa con aplomo lo que le
falta. Para las habilidades `analyze-data` y `text-docs` cójalo sólo allí donde haya con qué
cotejar el resultado y donde los hechos lleguen en el encargo, no de la memoria del modelo.

Thinking Machines no tiene API pública propia, así que el registro va a través de la pasarela
**OpenRouter**.

## Cómo conseguir la clave

**La clave es una sola para todos los modelos que van por la pasarela**: Muse-Spark-1.2,
Nemotron-3-Ultra, Ling-3.0-Flash y este hacen referencia a ese mismo `openrouter.apiKey`.

1. Regístrese en [openrouter.ai](https://openrouter.ai/).
2. Recargue el saldo: **Credits → Add credits**.
3. Abra [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**.
4. Copie el valor (se muestra una sola vez y empieza por `sk-or-`).
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Inkling-975B → «Establecer la clave de API»**.

## Límites y coste

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Máximo de respuesta | 65 536 tokens |
| Coste de entrada | 0,95 $ por 1 millón de tokens |
| Coste de salida | 4,05 $ por 1 millón de tokens |

**El identificador del modelo no se ha comprobado con una llamada real.** En los modelos que van
por la pasarela se pone con el slug completo, con el prefijo del proveedor
(`thinkingmachines/inkling`). Cotéjelo con una petición `GET /models` a
`https://openrouter.ai/api/v1`: la pasarela entrega esa lista incluso sin clave.

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | las fija el **propietario del modelo**, no la pasarela: «Your ownership rights in the Output are set forth in the Model Terms for each Model you use» |
| Uso comercial | consulte los Model Terms en la ficha del modelo de la pasarela: la pasarela responde de la entrega, no de los derechos |
| Qué es obligatorio | cumplir las condiciones tanto de la pasarela como del propietario del modelo; el propietario puede cambiar sus condiciones en cualquier momento |
| Texto de las condiciones | <https://openrouter.ai/terms> (redacción del 29.07.2026) más los Model Terms de la ficha del modelo |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Los pesos de este modelo no están en acceso abierto (en HuggingFace no se ha encontrado el
repositorio, comprobado el 27.08.2026): no hay nada que licenciar, y sólo rigen las condiciones
del propietario del modelo y de la pasarela.

La pasarela, por su parte, **renuncia en lo posible al entrenamiento con los datos** en los
proveedores conectados, pero no responde de la exactitud de las condiciones ajenas y lo dice
expresamente.

Las condiciones de la pasarela se cotejaron con su texto el 27.08.2026.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`openrouter.ai`. Los pesos son abiertos (Apache 2.0), pero 975 000 millones de parámetros no se
levantan en un ordenador propio: para el trabajo local están en el catálogo modelos más pequeños.

## Errores frecuentes

* **404 «No endpoints found»**: se ha perdido el prefijo del proveedor o ha cambiado el slug en la
  pasarela.
* **402 «Insufficient credits»**: no se ha recargado el saldo de OpenRouter.
* **En el resultado han aparecido hechos inventados**: es el nicho del modelo; coteje el resultado
  o coja otro modelo para encargos de tipo factual.

# GPT-6-Astra

**Alojamiento:** nube (API de OpenAI, compatible con OpenAI)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://api.openai.com/v1`,
modelo `gpt-6-astra`
**Referencia a la clave:** `openai.apiKey`

OpenAI publicó este modelo el 04.09.2026; entró en el catálogo de AI2P con la tarea
T-216-S0, la revisión de proveedores del 11.09.2026. El identificador `openai/gpt-6-astra`, la longitud
del contexto, el precio y las modalidades se han verificado con una consulta al
catálogo público OpenRouter.

Continúa la línea **GPT-5.6-Sol**: contexto de 1 050 000 tokens, respuesta de hasta 128 000 tokens,
10,00 $ y 50,00 $ por millón de tokens. Puntuaciones de habilidades 93-97.

Entrada admitida: texto y Markdown, código fuente, imágenes, PDF.

## Cómo conseguir la clave

1. Regístrese en [platform.openai.com](https://platform.openai.com/).
2. Recargue el saldo: **Settings → Billing → Add to credit balance**. Sin saldo las peticiones se
   rechazan con un 429.
3. Abra [platform.openai.com/api-keys](https://platform.openai.com/api-keys) → **Create new secret
   key** y póngale un nombre.
4. Copie el valor: se muestra **una sola vez**. La clave empieza por `sk-`.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → GPT-6-Astra → «Establecer la clave de API»**.

La clave es común con GPT-5.6-Terra: ambos modelos hacen referencia a `openai.apiKey`. La clave
pertenece a la organización: se cifra con su clave y se replica a todos los servidores de la
organización (cap. 10 de la especificación).

## Límites y coste

| | |
|---|---|
| Contexto | 1 050 000 tokens |
| Máximo de respuesta | 128 000 tokens |
| Coste de entrada | 10,00 $ por 1 millón de tokens |
| Coste de salida | 50,00 $ por 1 millón de tokens |

**El identificador del modelo no se ha comprobado con una llamada real**: está tomado del catálogo
de modelos y puesto sin el prefijo del proveedor. Antes del primer encargo cotéjelo con una
petición `GET /models` a `https://api.openai.com/v1`. Los precios actuales están en
[OpenAI Pricing](https://openai.com/api/pricing/).

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | **el resultado es suyo**: «Customer owns all Output»; OpenAI le cede sus derechos sobre él |
| Uso comercial | permitido |
| Qué es obligatorio | cumplir las Usage Policies; recordar que no se le promete la unicidad del resultado, y que esa misma respuesta le puede tocar a otro |
| Texto de las condiciones | <https://openai.com/policies/services-agreement/> |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Los pesos son cerrados y no hay nada que licenciar: las condiciones se refieren al **resultado de
la generación**.

El contenido que va a la API OpenAI **no lo usa para el desarrollo de sus servicios** mientras
usted no lo permita explícitamente; en el ChatGPT de consumo no es así.

Cotejado el 11.09.2026: la formulación sobre la propiedad del resultado («you … own the Output. We
hereby assign to you all our right, title, and interest, if any, in and to Output») se ha leído
literalmente en las condiciones de OpenAI; esa misma formulación está en el acuerdo para la API
del enlace anterior.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`api.openai.com`.

## Errores frecuentes

* **404 «model not found»**: el id ha cambiado; cotéjelo con la lista de `GET /models`.
* **429 «insufficient_quota»**: no se ha recargado el saldo o se ha agotado el límite de la
  organización.
* **Respuesta vacía, motivo `length`**: el modelo ha gastado el límite de respuesta en
  razonamientos; aumente `params.maxTokens` en el perfil (por defecto 32000).

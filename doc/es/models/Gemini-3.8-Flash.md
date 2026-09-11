# Gemini-3.8-Flash

**Alojamiento:** nube (Google, a través de una capa compatible con OpenAI)
**Conexión:** `provider: openai-compatible`,
`baseUrl: https://generativelanguage.googleapis.com/v1beta/openai`, modelo `gemini-3.8-flash`
**Referencia a la clave:** `google.apiKey`

Google publicó este modelo el 02.09.2026; entró en el catálogo de AI2P con la tarea
T-216-S0, la revisión de proveedores del 11.09.2026. El identificador `google/gemini-3.8-flash`, la longitud
del contexto, el precio y las modalidades se han verificado con una consulta al
catálogo público OpenRouter.

Continúa la línea **Gemini-3.7-Flash**: contexto de 1 048 576 tokens, respuesta de hasta 65 536 tokens,
0,75 $ y 3,75 $ por millón de tokens. Puntuaciones de habilidades 81-88.

Entrada admitida: texto y Markdown, código fuente, imágenes, PDF, audio, vídeo.

## Cómo conseguir la clave

**La clave es la misma** que la de los demás Gemini (`google.apiKey`). Si ya está introducida,
este modelo funcionará solo.

Si todavía no hay clave:

1. Abra [Google AI Studio](https://aistudio.google.com/apikey) y entre con su cuenta de Google.
2. Pulse **Create API key** y elija un proyecto de Google Cloud.
3. Copie la clave (empieza por `AIza`).
4. Vincule la facturación del proyecto si los encargos son largos y frecuentes.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Gemini-3.8-Flash → «Establecer la clave de
   API»**.

## Límites y coste

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Máximo de respuesta | 65 536 tokens |
| Coste de entrada | 0,75 $ por 1 millón de tokens |
| Coste de salida | 3,75 $ por 1 millón de tokens |

En el catálogo se han anotado los valores del catálogo público de modelos a 11.09.2026. Antes de gastos serios coteje
con [Gemini API Pricing](https://ai.google.dev/pricing).

**El identificador del modelo no se ha comprobado con una llamada real**: cotéjelo con una
petición `GET /models` a `https://generativelanguage.googleapis.com/v1beta/openai`.

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | Google **no reclama derechos** sobre el contenido generado («Google won’t claim ownership over generated content») |
| Uso comercial | permitido |
| Qué es obligatorio | cumplir la Prohibited Use Policy; la ley puede exigir comunicar a sus usuarios que el contenido lo ha generado una IA |
| Texto de las condiciones | <https://ai.google.dev/gemini-api/terms> |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Los pesos son cerrados: las condiciones se refieren al **resultado de la generación**.

Una diferencia importante entre tarifas: en el acceso **de pago** (y AI2P llama con una clave
pagada) Google no usa sus peticiones ni sus respuestas para mejorar sus productos y las guarda un
tiempo limitado sólo para comprobar si hay infracciones; en la cuota gratuita sí las usa. Google
tiene derecho a dar ese mismo resultado a otro: no se le promete exclusividad.

Las condiciones se cotejaron el 11.09.2026.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`generativelanguage.googleapis.com`.

## Errores frecuentes

* **404 «model not found»**: el id ha cambiado; cotéjelo con la lista de `GET /models`.
* **429 «resource exhausted»**: se ha chocado con la frecuencia del nivel gratuito.
* **Calidad por debajo de lo esperado**: la tarea es más compleja que el nicho del modelo;
  desplace hacia la calidad el deslizador «precio ↔ calidad» del proyecto o ponga el ejecutor a
  mano.

# Mistral-Large-4

**Alojamiento:** nube (Mistral AI, Francia; compatible con OpenAI)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://api.mistral.ai/v1`,
**Referencia a la clave:** `mistral.apiKey`

Mistral AI publicó este modelo el 06.10.2026; entró en el catálogo de AI2P con la tarea
T-466-S0, la revisión de proveedores del 13.10.2026. El identificador `mistralai/mistral-large-4-0`, el precio y
las modalidades se han verificado con una consulta al catálogo público OpenRouter.

Contexto de 1 048 576 tokens, respuesta de hasta 262 144 tokens, 0,68 $ y 2,09 $ por millón de
tokens, puntuaciones 83-88. El registro más cercano del catálogo es **Mistral-Large-3**.

Entrada admitida: texto y Markdown, código fuente, imágenes.

## Cómo conseguir la clave

1. Regístrese en [console.mistral.ai](https://console.mistral.ai/).
2. Contrate la tarifa de pago: **Billing → Add payment method**. En el nivel gratuito la
   frecuencia de peticiones está muy limitada.
3. Abra [console.mistral.ai/api-keys](https://console.mistral.ai/api-keys/) → **Create new key** y
   póngale un nombre y una fecha de caducidad.
4. Copie el valor: se muestra **una sola vez**.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Mistral-Large-4 → «Establecer la clave de
   API»**.

La clave pertenece a la organización: se cifra con su clave y se replica a todos los servidores de
la organización (cap. 10 de la especificación).

## Límites y coste

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Máximo de respuesta | 262 144 tokens |
| Coste de entrada | 0,68 $ por 1 millón de tokens |
| Coste de salida | 2,09 $ por 1 millón de tokens |

**El identificador del modelo no se ha comprobado con una llamada real**: está tomado del catálogo
de modelos sin el prefijo del proveedor. Cotéjelo con una petición `GET /models` a
`https://api.mistral.ai/v1`: en los checkpoints con fecha eso es especialmente importante. Los
precios actuales están en [Mistral Pricing](https://mistral.ai/pricing).

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | **el resultado es suyo**: «Customer … owns all Output»; Mistral le cede sus derechos (Commercial ToS, p. 3.1) |
| Uso comercial | permitido |
| Qué es obligatorio | no hacer pasar el resultado por trabajo de una persona (p. 3.2); no entrenar con las imágenes resultantes un generador de imágenes competidor (p. 3.3); cumplir la Usage Policy |
| Texto de las condiciones | <https://legal.mistral.ai/terms/commercial-terms-of-service> |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Sobre el entrenamiento con sus datos se dice con precisión (p. 4.2): Mistral **no** entrena con
ellos sus modelos, salvo cuando usted mismo ha activado el entrenamiento, ha enviado una opinión o
ha cogido un modelo experimental (en AI Studio están marcados con el prefijo `labs`). Este
registro del catálogo apunta a un modelo normal, no a uno `labs`.

Las condiciones se cotejaron con su texto el 13.10.2026.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`api.mistral.ai`.

## Errores frecuentes

* **404 «model not found»**: ha salido un checkpoint nuevo y la fecha antigua ya no se atiende;
  coja el id actual de `GET /models`.
* **429 «rate limit exceeded»**: no está contratada la tarifa de pago.
* **La clave ha dejado de funcionar**: las claves de Mistral pueden tener fecha de caducidad;
  compruébela en la consola.

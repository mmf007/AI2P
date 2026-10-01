# Grok-4.7

**Alojamiento:** nube (API de xAI, compatible con OpenAI)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://api.x.ai/v1`,
modelo `grok-4.7`
**Referencia a la clave:** `xai.apiKey`

xAI publicó este modelo el 21.09.2026; entró en el catálogo de AI2P con la tarea
T-347-S0, la revisión de proveedores del 23.09.2026. El identificador `x-ai/grok-4.7`, la longitud
del contexto, el precio y las modalidades se han verificado con una consulta al
catálogo público OpenRouter.

Continúa la línea **Grok-4.6**: contexto de 500 000 tokens, respuesta de hasta 64 000 tokens,
1,60 $ y 4,80 $ por millón de tokens. Puntuaciones de habilidades 89-93.

Entrada admitida: texto y Markdown, código fuente, imágenes, PDF.

## Cómo conseguir la clave

1. Regístrese en la [consola de xAI](https://console.x.ai/).
2. Cree un equipo (team) y recargue el saldo: sin saldo las peticiones se rechazan.
3. Abra **API Keys → Create API Key**, póngale un nombre y permisos sobre los modelos de chat.
4. Copie el valor: se muestra **una sola vez**. La clave empieza por `xai-`.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Grok-4.7 → «Establecer la clave de API»**.

La clave pertenece a la organización: se cifra con su clave y se replica a todos los servidores de
la organización (cap. 10 de la especificación).

## Límites y coste

| | |
|---|---|
| Contexto | 500 000 tokens |
| Máximo de respuesta | 64 000 tokens |
| Coste de entrada | 1,60 $ por 1 millón de tokens |
| Coste de salida | 4,80 $ por 1 millón de tokens |

**El identificador del modelo no se ha comprobado con una llamada real**: está tomado del catálogo
de modelos sin el prefijo del proveedor. Cotéjelo con una petición `GET /models` a
`https://api.x.ai/v1`. Los precios actuales están en [xAI Pricing](https://x.ai/api).

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | **el resultado es suyo**: «Customer … owns all right, title, and interest in the Output»; xAI le cede sus derechos |
| Uso comercial | permitido |
| Qué es obligatorio | no entrenar otros modelos con el resultado sin un permiso aparte y no hacerlo pasar por creado por una persona |
| Texto de las condiciones | <https://x.ai/legal/terms-of-service-enterprise> (condiciones para el acceso por API) |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Los pesos son cerrados: se habla del **resultado de la generación**.

Según las condiciones para la API, xAI **no usa lo enviado ni lo recibido para entrenar sus
modelos**. En el Grok de consumo (el sitio y la aplicación) las condiciones son otras: allí los
datos van al entrenamiento, y al usar el resultado junto con el nombre y las marcas de xAI se
exige un enlace al servicio. AI2P llama con clave de API, así que rigen las condiciones del enlace
anterior.

Las condiciones se cotejaron con su texto el 23.09.2026.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`api.x.ai`.

## Errores frecuentes

* **La factura es mayor de lo calculado**: probablemente el encargo ha pasado el umbral de
  longitud a partir del cual xAI aplica una tarifa elevada (no publicada para la 4.7).
  Mantenga el historial del chat más corto o saque los materiales a archivos.
* **El modelo «no sabe» nada de un suceso reciente**: xAI no ha anunciado el límite de
  conocimiento de la 4.7; dé los datos recientes en el propio encargo.
* **403 / «no credits»**: no se ha recargado el saldo del equipo de xAI.

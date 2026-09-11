# Grok-4.6

**Alojamiento:** nube (API de xAI, compatible con OpenAI)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://api.x.ai/v1`,
modelo `grok-4.6`
**Referencia a la clave:** `xai.apiKey`

El modelo de frontera más barato: puntuaciones de habilidad de 86 a 92 (el nivel de Sonnet 5) con
un precio de 2 $ y 6 $ por millón de tokens. Contexto de 500 000 tokens. Buena elección cuando
hace falta casi la mejor calidad a un precio medio.

Dos limitaciones que conviene conocer de antemano:

* **Una petición de más de 200 000 tokens se tarifa entera al precio elevado**: 4 $ y 12 $ por
  millón, es decir, el doble. No es un error de la tabla de abajo, sino la tarifa de xAI: se
  encarece la petición **entera**, no la cola que pasa del umbral.
* **El conocimiento del mundo llega hasta el 01.02.2026.** De lo que ha ocurrido después el modelo
  no sabe nada; la información reciente désela en el encargo.

## Cómo conseguir la clave

1. Regístrese en la [consola de xAI](https://console.x.ai/).
2. Cree un equipo (team) y recargue el saldo: sin saldo las peticiones se rechazan.
3. Abra **API Keys → Create API Key**, póngale un nombre y permisos sobre los modelos de chat.
4. Copie el valor: se muestra **una sola vez**. La clave empieza por `xai-`.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Grok-4.6 → «Establecer la clave de API»**.

La clave pertenece a la organización: se cifra con su clave y se replica a todos los servidores de
la organización (cap. 10 de la especificación).

## Límites y coste

| | |
|---|---|
| Contexto | 500 000 tokens |
| Máximo de respuesta | 64 000 tokens |
| Coste de entrada | 2,00 $ por 1 millón de tokens (por encima de 200 000 tokens de petición, 4,00 $) |
| Coste de salida | 6,00 $ por 1 millón de tokens (por encima de 200 000 tokens de petición, 12,00 $) |

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

Las condiciones se cotejaron con su texto el 27.08.2026.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`api.x.ai`.

## Errores frecuentes

* **La factura es el doble de lo calculado**: el encargo ha pasado el umbral de 200 000 tokens de
  petición. Mantenga el historial del chat más corto o saque los materiales a archivos.
* **El modelo «no sabe» nada de un suceso reciente**: el límite de conocimiento es el 01.02.2026.
* **403 / «no credits»**: no se ha recargado el saldo del equipo de xAI.

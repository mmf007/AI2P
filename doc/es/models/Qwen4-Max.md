# Qwen4-Max

**Alojamiento:** nube (Alibaba DashScope, compatible con OpenAI)
**Conexión:** `provider: openai-compatible`,
`baseUrl: https://dashscope-intl.aliyuncs.com/compatible-mode/v1`, modelo `qwen4-max`
**Referencia a la clave:** `qwen.apiKey`

Alibaba publicó este modelo el 16.11.2026; entró en el catálogo de AI2P con la tarea
T-767-S0, la revisión de proveedores del 17.11.2026. El identificador `qwen/qwen4-max`, el precio y
las modalidades se han verificado con una consulta al catálogo público OpenRouter.

Contexto de 1 000 000 tokens, respuesta de hasta 131 072 tokens, 1,425 $ y 4,275 $ por millón de
tokens, puntuaciones 88-94. El registro más cercano del catálogo es **Qwen3.8-Max**.

Entrada admitida: texto y Markdown, código fuente, imágenes, vídeo.

## Cómo conseguir la clave

1. Regístrese en
   [Alibaba Cloud Model Studio](https://modelstudio.console.alibabacloud.com/) (la plataforma
   internacional).
2. Active el servicio de modelos y vincule un método de pago.
3. Abra **API-KEY → Create API Key** y elija un espacio de trabajo.
4. Copie el valor: se muestra **una sola vez**. La clave empieza por `sk-`.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Qwen4-Max → «Establecer la clave de API»**.

La clave pertenece a la organización: se cifra con su clave y se replica a todos los servidores de
la organización (cap. 10 de la especificación). Los modelos Qwen **locales** del catálogo no
tienen clave en absoluto: funcionan en su propio ordenador.

## Límites y coste

| | |
|---|---|
| Contexto | 1 000 000 de tokens |
| Máximo de respuesta | 131 072 tokens |
| Coste de entrada | 1,425 $ por 1 millón de tokens |
| Coste de salida | 4,275 $ por 1 millón de tokens |

**El identificador del modelo no se ha comprobado con una llamada real**: está tomado del catálogo
de modelos sin el prefijo del proveedor. Cotéjelo con una petición `GET /models` a
`https://dashscope-intl.aliyuncs.com/compatible-mode/v1`. Los precios actuales están en la consola
de Model Studio.

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | las fija el contrato de Model Studio: el acceso de pago con clave da derecho a usar el resultado, y la responsabilidad sobre él es suya |
| Uso comercial | permitido con el acceso de pago; el contenido obtenido en el modo **de prueba** de la consola sólo se puede usar para evaluar el modelo |
| Qué es obligatorio | cumplir las normas de Model Studio y responder de la legalidad de lo generado: el proveedor no da garantías sobre el contenido |
| Texto de las condiciones | <https://www.alibabacloud.com/help/en/model-studio/related-agreements> (lista de los acuerdos vigentes) |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Los pesos de `qwen4-max` son cerrados (a diferencia de los Qwen menores, que están bajo Apache
2.0), así que se habla del **resultado de la generación**.

Cotejado el 17.11.2026: se ha leído la página con la lista de acuerdos de Model Studio (última
actualización, 30.06.2026; están vigentes los Terms of Service, el Service Level Agreement y los
Open Source Model License Terms); el texto de los Terms of Service en sí no se ha dejado leer por
máquina: ábralo con el enlace anterior y léalo con los ojos, sobre todo la sección del modo de
prueba.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`dashscope-intl.aliyuncs.com`.

## Errores frecuentes

* **404 o «model not exist»**: o ha cambiado el id, o se ha cogido el host que no era (el
  internacional frente al chino).
* **401 «InvalidApiKey»**: la clave se ha creado en otro espacio de trabajo.
* **Respuesta vacía, motivo `length`**: aumente `params.maxTokens` en el perfil (por defecto
  32000).

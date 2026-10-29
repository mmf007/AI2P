# Kimi-K3.1-Pro

**Alojamiento:** nube (Moonshot AI, compatible con OpenAI)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://api.moonshot.ai/v1`,
**Referencia a la clave:** `moonshot.apiKey`

Moonshot AI publicó este modelo el 23.10.2026; entró en el catálogo de AI2P con la tarea
T-653-S0, la revisión de proveedores del 29.10.2026. El identificador `moonshotai/kimi-k3.1-pro`, el precio y
las modalidades se han verificado con una consulta al catálogo público OpenRouter.

Contexto de 1 048 576 tokens, respuesta de hasta 943 718 tokens, 2,00 $ y 10,00 $ por millón de
tokens, puntuaciones 87-92. El registro más cercano del catálogo es **Kimi-K3**.

Entrada admitida: texto y Markdown, código fuente, imágenes, vídeo.

## Cómo conseguir la clave

1. Regístrese en [platform.moonshot.ai](https://platform.moonshot.ai/).
2. Recargue el saldo en la sección **Billing**. Sin saldo las peticiones se rechazan.
3. Abra la [consola de claves](https://platform.moonshot.ai/console/api-keys) → **Create API key**
   y póngale un nombre.
4. Copie el valor: se muestra **una sola vez**. La clave empieza por `sk-`.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Kimi-K3.1-Pro → «Establecer la clave de API»**.

La clave pertenece a la organización: se cifra con su clave y se replica a todos los servidores de
la organización (cap. 10 de la especificación).

## Límites y coste

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Máximo de respuesta | 943 718 tokens |
| Coste de entrada | 2,00 $ por 1 millón de tokens |
| Coste de salida | 10,00 $ por 1 millón de tokens |

**El identificador del modelo no se ha comprobado con una llamada real**: está tomado del catálogo
de modelos sin el prefijo del proveedor. Cotéjelo con una petición `GET /models` a
`https://api.moonshot.ai/v1`. Los precios actuales están en la consola de Moonshot.

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | Moonshot **no reclama derechos** sobre el contenido («we do not claim ownership of it») |
| Uso comercial | permitido |
| Qué es obligatorio | recordar que, por defecto, lo enviado y lo recibido puede ir al desarrollo y al entrenamiento de los modelos de Moonshot; la limitación se pacta en un contrato corporativo aparte |
| Texto de las condiciones | <https://platform.kimi.ai/docs/agreement/modeluse> |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Sobre los pesos: Moonshot llama a K3.1 Pro un modelo de pesos abiertos, pero el 29.10.2026 la
ficha `moonshotai/Kimi-K3.1-Pro` de HuggingFace no aparece en la consulta (respuesta 401), así que
la licencia de los pesos NO está verificada. Su predecesor Kimi K3 tiene licencia propia
(`kimi-k3`), no MIT. Para un registro en la nube da igual: no recibe los pesos.

Las condiciones se cotejaron con su texto, y la licencia de los pesos con la ficha del modelo,
ambas el 29.10.2026.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`api.moonshot.ai`. Los pesos son abiertos, pero 2,8 billones de parámetros no se levantan en un
ordenador propio: para el trabajo local están en el catálogo los modelos de 27 000 a 35 000
millones.

## Errores frecuentes

* **401 «invalid api key»**: la clave se ha creado en la plataforma china (`moonshot.cn`) y el
  `baseUrl` apunta a la internacional; no son intercambiables.
* **404 «model not found»**: el id ha cambiado; cotéjelo con la lista de `GET /models`.
* **Respuesta vacía, motivo `length`**: aumente `params.maxTokens` en el perfil (por defecto
  32000).

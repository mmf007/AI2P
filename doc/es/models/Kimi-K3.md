# Kimi-K3

**Alojamiento:** nube (Moonshot AI, compatible con OpenAI)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://api.moonshot.ai/v1`,
modelo `kimi-k3`
**Referencia a la clave:** `moonshot.apiKey`

**El mejor modelo abierto según el índice conjunto de Artificial Analysis** en el momento de
ampliar el catálogo: 2,8 billones de parámetros (104 000 millones activos), puntuaciones de
habilidad de 85 a 90, pegado a los buques insignia cerrados. Contexto de 1 048 576 tokens, y la
entrada acepta imágenes y vídeo. El precio es de 3 $ y 15 $ por millón: más caro que sus vecinos
chinos, pero también con más calidad.

**La licencia es propia, Kimi K3 License, no MIT ni Apache.** Los pesos son abiertos, pero lea
aparte las condiciones de aplicación comercial; «abierto» aquí no significa «haz lo que quieras».

## Cómo conseguir la clave

1. Regístrese en [platform.moonshot.ai](https://platform.moonshot.ai/).
2. Recargue el saldo en la sección **Billing**. Sin saldo las peticiones se rechazan.
3. Abra la [consola de claves](https://platform.moonshot.ai/console/api-keys) → **Create API key**
   y póngale un nombre.
4. Copie el valor: se muestra **una sola vez**. La clave empieza por `sk-`.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Kimi-K3 → «Establecer la clave de API»**.

La clave pertenece a la organización: se cifra con su clave y se replica a todos los servidores de
la organización (cap. 10 de la especificación).

## Límites y coste

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Máximo de respuesta | 65 536 tokens |
| Coste de entrada | 3,00 $ por 1 millón de tokens |
| Coste de salida | 15,00 $ por 1 millón de tokens |

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

Aparte, sobre los pesos, porque aquí es fácil equivocarse: Kimi K3 tiene **licencia propia** (en
la ficha `moonshotai/Kimi-K3` se llama `kimi-k3`), no MIT ni Apache 2.0. El tópico de «modelo
abierto, o sea, MIT» aquí no es cierto. Para el registro en la nube eso da igual (los pesos no se
reciben), pero servirá si decide levantar el modelo en su equipo.

Las condiciones se cotejaron con su texto, y la licencia de los pesos con la ficha del modelo,
ambas el 27.08.2026.

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

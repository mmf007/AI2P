# DeepSeek-V4-Pro

**Alojamiento:** nube (API de DeepSeek, compatible con OpenAI)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://api.deepseek.com`,
modelo `deepseek-v4-pro`
**Referencia a la clave:** `deepseek.apiKey`

Un modelo barato de nivel decente: código y textos de 84 a 89 sobre 100 según las puntuaciones de
habilidad, con un precio unas veinte veces menor que el de Claude-Fable-5. Buena elección para la
rutina voluminosa.

## Cómo conseguir la clave

1. Regístrese en [platform.deepseek.com](https://platform.deepseek.com/).
2. Recargue el saldo: **Top up** (pago con tarjeta). Sin saldo las peticiones se rechazan.
3. Abra **API keys → Create new API key** y póngale un nombre.
4. Copie el valor: se muestra **una sola vez**. La clave empieza por `sk-`.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → DeepSeek-V4-Pro → «Establecer la clave de
   API»**.

La clave es común con DeepSeek-V4-Flash: ambos modelos hacen referencia a `deepseek.apiKey`.
Introducida una vez, funcionan los dos.

La clave pertenece a la organización: se cifra con su clave y se replica a todos los servidores de
la organización (cap. 10 de la especificación).

## Límites y coste

| | |
|---|---|
| Contexto | 1 000 000 de tokens |
| Máximo de respuesta | 65 536 tokens |
| Coste de entrada | 0,66 $ por 1 millón de tokens |
| Coste de salida | 1,98 $ por 1 millón de tokens |

Los números están actualizados para el checkpoint **V4-Pro-0813**: el contexto ha subido de
128 000 a un millón, el máximo de respuesta de 8 192 a 65 536 tokens, y la salida incluso se ha
abaratado (era de 0,55 $ y 2,19 $). La limitación anterior de «un contexto un orden de magnitud
menor que el de Anthropic» ya no existe: ahora es el mismo que el de Claude-Sonnet-5.

En el perfil está `params.maxTokens: 32768`: es el límite de **una respuesta**. Si el resultado se
corta con el motivo `length`, súbalo (el techo del modelo es 65 536) o divida la tarea.

**El identificador del modelo no se ha comprobado con una llamada real**: cotéjelo con una
petición `GET /models` a `https://api.deepseek.com`. Los precios actuales están en
[DeepSeek Pricing](https://api-docs.deepseek.com/quick_start/pricing).

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | **el resultado es suyo**: «We assign any rights, title, and interests — if any — in the Outputs … to you» (p. 4.2) |
| Uso comercial | permitido de forma directa y amplia: uso personal, investigación, desarrollo de productos derivados e incluso entrenamiento de otros modelos (destilación) |
| Qué es obligatorio | revelar a sus usuarios que el contenido lo ha generado una IA (p. 8.1); no usar la marca DeepSeek sin permiso |
| Texto de las condiciones | <https://cdn.deepseek.com/policies/en-US/deepseek-open-platform-terms-of-service.html> |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Los pesos del registro en la nube no se le entregan, así que se habla del **resultado de la
generación**.

Son las condiciones más generosas entre los registros en la nube del catálogo: el permiso para
entrenar otros modelos con el resultado está escrito directamente en el texto, mientras que en la
mayoría de los proveedores eso está justamente prohibido. La obligación es una sola y es fácil que
se escape: indicarle al usuario final que el texto lo ha hecho una IA.

Las condiciones se cotejaron con su texto el 27.08.2026.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`api.deepseek.com`.

## Errores frecuentes

* **402 / «Insufficient Balance»**: no se ha recargado el saldo.
* **Respuesta vacía, motivo `length`**: el modelo ha gastado el límite de respuesta en
  razonamientos; aumente `params.maxTokens` en el perfil del modelo.
* **Entrada demasiado larga**: un millón de tokens parece infinito, pero el historial del chat se
  va a la entrada en cada turno del agente; saque los materiales voluminosos a archivos y ponga
  enlaces en la descripción.

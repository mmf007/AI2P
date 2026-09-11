# DeepSeek-V4-Flash

**Alojamiento:** nube (API de DeepSeek, compatible con OpenAI)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://api.deepseek.com`,
modelo `deepseek-v4-flash`
**Referencia a la clave:** `deepseek.apiKey`

Rápido y muy barato: la calidad es menor que la de DeepSeek-V4-Pro (puntuaciones de habilidad de
76 a 84 frente a 84–89). Cójalo para el trabajo masivo sencillo: traducciones, resúmenes cortos,
borradores, correcciones menores. El registro más barato del catálogo ya no es este, sino
Ling-3.0-Flash: 0,021 $ por millón de tokens de entrada.

## Cómo conseguir la clave

**La clave es la misma** que la de DeepSeek-V4-Pro: ambos modelos hacen referencia a
`deepseek.apiKey`. Si ya está introducida, este modelo funcionará solo.

Si todavía no hay clave:

1. Regístrese en [platform.deepseek.com](https://platform.deepseek.com/).
2. Recargue el saldo (**Top up**).
3. **API keys → Create new API key**, y copie el valor (se muestra una sola vez).
4. En AI2P: **Ajustes → Catálogos → Modelos de IA → DeepSeek-V4-Flash → «Establecer la clave de
   API»**.

## Límites y coste

| | |
|---|---|
| Contexto | 1 000 000 de tokens |
| Máximo de respuesta | 32 768 tokens |
| Coste de entrada | 0,14 $ por 1 millón de tokens |
| Coste de salida | 0,28 $ por 1 millón de tokens |

Los números están actualizados: el contexto ha subido de 128 000 a un millón, el máximo de
respuesta de 8 192 a 32 768 tokens, y el precio ha caído entre la mitad y la cuarta parte (era de
0,27 $ y 1,10 $). La salida es ahora **siete veces más barata** que la de DeepSeek-V4-Pro, con el
mismo contexto.

En el perfil está `params.maxTokens: 32768`: es el techo de una respuesta en este modelo.

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

## Cuándo cogerlo

Si el deslizador **precio ↔ calidad** del proyecto está desplazado hacia el precio, la selección
automática de ejecutor elegirá este modelo por su cuenta (especificación, ap. 2.7). Si la tarea
exige esmero, ponga el ejecutor a mano o desplace el deslizador hacia la calidad.

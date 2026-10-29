# MiniMax-M3.1-Flash

**Alojamiento:** nube (MiniMax, compatible con OpenAI)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://api.minimax.io/v1`,
**Referencia a la clave:** `minimax.apiKey`

MiniMax publicó este modelo el 19.10.2026; entró en el catálogo de AI2P con la tarea
T-653-S0, la revisión de proveedores del 29.10.2026. El identificador `minimax/minimax-m3.1-flash`, el precio y
las modalidades se han verificado con una consulta al catálogo público OpenRouter.

Contexto de 1 048 576 tokens, respuesta de hasta 524 288 tokens, 0,15 $ y 0,60 $ por millón de
tokens, puntuaciones 80-84. El registro más cercano del catálogo es **MiniMax-M3**.

Entrada admitida: texto y Markdown, código fuente, imágenes, vídeo.

## Cómo conseguir la clave

1. Regístrese en [platform.minimax.io](https://platform.minimax.io/) (la plataforma
   internacional; la china tiene otro dominio y otras claves).
2. Recargue el saldo en la sección de facturación.
3. Abra **Account → API Keys → Create new secret key** y póngale un nombre.
4. Copie el valor: se muestra **una sola vez**.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → MiniMax-M3.1-Flash → «Establecer la clave de API»**.

La clave pertenece a la organización: se cifra con su clave y se replica a todos los servidores de
la organización (cap. 10 de la especificación).

## Límites y coste

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Máximo de respuesta | 524 288 tokens |
| Coste de entrada | 0,15 $ por 1 millón de tokens |
| Coste de salida | 0,60 $ por 1 millón de tokens |

**El identificador del modelo no se ha comprobado con una llamada real**: está tomado del catálogo
de modelos sin el prefijo del proveedor. Cotéjelo con una petición `GET /models` a
`https://api.minimax.io/v1`.

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | MiniMax **no reclama derechos** sobre el contenido generado («We do not claim ownership of User Contributions or User Generated Content») |
| Uso comercial | permitido |
| Qué es obligatorio | recordar que usted le da a MiniMax una licencia perpetua, irrevocable y no exclusiva para usar lo enviado y lo recibido |
| Texto de las condiciones | <https://www.minimax.io/platform/protocol/terms-of-service> |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Los pesos de MiniMax-M3.1-Flash están publicados bajo una **licencia community propia** (en la ficha
`MiniMaxAI/MiniMax-M3.1-Flash` se llama `minimax-community`), no bajo MIT ni Apache 2.0; si va a levantar
el modelo en su equipo, léala aparte.

Cotejado el 29.10.2026: la licencia de los pesos, con la ficha del modelo, y las condiciones sobre
el resultado, con el texto publicado de las condiciones de MiniMax
(<https://agent.minimax.io/doc/en/terms-of-service.html>).

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`api.minimax.io`.

## Errores frecuentes

* **401 «invalid api key»**: una clave de la plataforma china no funciona en la internacional (ni
  al revés); cree la clave en el mismo sitio al que apunta el `baseUrl`.
* **404 «model not found»**: el id ha cambiado; cotéjelo con la lista de `GET /models`.
* **Calidad por debajo de lo esperado en código complejo**: el nicho del modelo es otro; para
  código coja GLM-5.2 o Kimi-K3.

# MiniMax-M3

**Alojamiento:** nube (MiniMax, compatible con OpenAI)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://api.minimax.io/v1`,
modelo `minimax-m3`
**Referencia a la clave:** `minimax.apiKey`

Barato y con mucho contexto: 0,30 $ y 1,20 $ por millón de tokens con un contexto de 1 048 576,
cuatro veces más barato que GLM-5.2 y diez veces más barato que Kimi-K3. Puntuaciones de habilidad
de 80 a 85: el término medio de trabajo del catálogo.

Lo distingue su **entrada multimodal**: además de texto y código fuente, el modelo acepta imágenes
y vídeo, y cuesta lo que un modelo de texto barato. Vale para encargos en los que el material
llega como capturas de pantalla o grabación de pantalla.

## Cómo conseguir la clave

1. Regístrese en [platform.minimax.io](https://platform.minimax.io/) (la plataforma
   internacional; la china tiene otro dominio y otras claves).
2. Recargue el saldo en la sección de facturación.
3. Abra **Account → API Keys → Create new secret key** y póngale un nombre.
4. Copie el valor: se muestra **una sola vez**.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → MiniMax-M3 → «Establecer la clave de API»**.

La clave pertenece a la organización: se cifra con su clave y se replica a todos los servidores de
la organización (cap. 10 de la especificación).

## Límites y coste

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Máximo de respuesta | 65 536 tokens |
| Coste de entrada | 0,30 $ por 1 millón de tokens |
| Coste de salida | 1,20 $ por 1 millón de tokens |

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

Los pesos de MiniMax-M3 están publicados bajo una **licencia community propia** (en la ficha
`MiniMaxAI/MiniMax-M3` se llama `minimax-community`), no bajo MIT ni Apache 2.0; si va a levantar
el modelo en su equipo, léala aparte.

Cotejado el 27.08.2026: la licencia de los pesos, con la ficha del modelo, y las condiciones sobre
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

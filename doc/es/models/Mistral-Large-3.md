# Mistral-Large-3

**Alojamiento:** nube (Mistral AI, Francia; compatible con OpenAI)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://api.mistral.ai/v1`,
modelo `mistral-large-2512`
**Referencia a la clave:** `mistral.apiKey`

La opción europea del catálogo: los datos se procesan en la UE, lo que a veces resuelve por sí
solo la cuestión de la elección. Sus puntos fuertes son los **textos y la traducción**
(puntuaciones de 81 a 85); el código es bastante más flojo (78–82). El precio es moderado: 0,50 $
y 1,50 $ por millón de tokens, con un contexto de 262 144 tokens.

Fíjese en el **identificador**: en Mistral lleva fecha, `mistral-large-2512`, y no
`mistral-large-3`. Así marca el proveedor el checkpoint; cuando salga el siguiente habrá que
cambiar la línea del perfil.

## Cómo conseguir la clave

1. Regístrese en [console.mistral.ai](https://console.mistral.ai/).
2. Contrate la tarifa de pago: **Billing → Add payment method**. En el nivel gratuito la
   frecuencia de peticiones está muy limitada.
3. Abra [console.mistral.ai/api-keys](https://console.mistral.ai/api-keys/) → **Create new key** y
   póngale un nombre y una fecha de caducidad.
4. Copie el valor: se muestra **una sola vez**.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Mistral-Large-3 → «Establecer la clave de
   API»**.

La clave pertenece a la organización: se cifra con su clave y se replica a todos los servidores de
la organización (cap. 10 de la especificación).

## Límites y coste

| | |
|---|---|
| Contexto | 262 144 tokens |
| Máximo de respuesta | 65 536 tokens |
| Coste de entrada | 0,50 $ por 1 millón de tokens |
| Coste de salida | 1,50 $ por 1 millón de tokens |

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

Las condiciones se cotejaron con su texto el 27.08.2026.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`api.mistral.ai`.

## Errores frecuentes

* **404 «model not found»**: ha salido un checkpoint nuevo y la fecha antigua ya no se atiende;
  coja el id actual de `GET /models`.
* **429 «rate limit exceeded»**: no está contratada la tarifa de pago.
* **La clave ha dejado de funcionar**: las claves de Mistral pueden tener fecha de caducidad;
  compruébela en la consola.

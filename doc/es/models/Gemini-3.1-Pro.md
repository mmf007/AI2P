# Gemini-3.1-Pro

> **Registro desactivado el 11.09.2026 (tarea T-216-S0, revisión de proveedores).** Esta
> versión tiene más de tres versiones más nuevas de la misma familia (3.5, 3.6, 3.7, 3.8) y su identificador ya no está en el catálogo público de modelos. El registro
> se conserva: lo referencian ejecutores, tareas y informes anteriores. Puede volver a
> activarse con la casilla «activa»: **Ajustes → Catálogos → Modelos de IA**.

**Alojamiento:** nube (Google, a través de una capa compatible con OpenAI)
**Conexión:** `provider: openai-compatible`,
`baseUrl: https://generativelanguage.googleapis.com/v1beta/openai`, modelo `gemini-3.1-pro`
**Referencia a la clave:** `google.apiKey`

El modelo de trabajo de la línea Gemini: puntuaciones de habilidad de 86 a 91 con un precio de 2 $
y 12 $ por millón de tokens, cinco veces más barato que Gemini-3-Ultra y con un contexto de
1 048 576 tokens. Es fuerte en resúmenes, traducción y análisis de datos.

Acepta como entrada texto, código fuente, imágenes, PDF y también **sonido y vídeo** (`audio/*`,
`video/*`): el material del encargo puede llegar como grabación.

## Cómo conseguir la clave

**La clave es la misma** que la de los demás Gemini: los tres registros hacen referencia a
`google.apiKey`. Si ya está introducida, este modelo funcionará solo.

Si todavía no hay clave:

1. Abra [Google AI Studio](https://aistudio.google.com/apikey) y entre con su cuenta de Google.
2. Pulse **Create API key** y elija un proyecto de Google Cloud.
3. Copie la clave (empieza por `AIza`).
4. Vincule la facturación del proyecto: en el nivel gratuito los encargos largos chocan con un
   429.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Gemini-3.1-Pro → «Establecer la clave de
   API»**.

## Límites y coste

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Máximo de respuesta | 65 536 tokens |
| Coste de entrada | 2,00 $ por 1 millón de tokens |
| Coste de salida | 12,00 $ por 1 millón de tokens |

**El identificador del modelo no se ha comprobado con una llamada real**: está tomado del catálogo
de modelos sin el prefijo del proveedor. Cotéjelo con una petición `GET /models` a
`https://generativelanguage.googleapis.com/v1beta/openai`. Lo completa que sea la compatibilidad
con OpenAI de la capa de Google tampoco se ha comprobado con una llamada real: las llamadas
principales están cubiertas, pero algunos campos pueden comportarse de otra manera. Los precios
actuales están en [Gemini API Pricing](https://ai.google.dev/pricing).

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | Google **no reclama derechos** sobre el contenido generado («Google won’t claim ownership over generated content») |
| Uso comercial | permitido |
| Qué es obligatorio | cumplir la Prohibited Use Policy; la ley puede exigir comunicar a sus usuarios que el contenido lo ha generado una IA |
| Texto de las condiciones | <https://ai.google.dev/gemini-api/terms> |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Los pesos son cerrados: las condiciones se refieren al **resultado de la generación**.

Una diferencia importante entre tarifas: en el acceso **de pago** (y AI2P llama con una clave
pagada) Google no usa sus peticiones ni sus respuestas para mejorar sus productos y las guarda un
tiempo limitado sólo para comprobar si hay infracciones; en la cuota gratuita sí las usa. Google
tiene derecho a dar ese mismo resultado a otro: no se le promete exclusividad.

Las condiciones se cotejaron el 27.08.2026.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`generativelanguage.googleapis.com`.

## Errores frecuentes

* **404 «model not found»**: el id ha cambiado; cotéjelo con la lista de `GET /models`.
* **429 en un encargo largo**: no está vinculada la facturación del proyecto.
* **Respuesta vacía, motivo `length`**: aumente `params.maxTokens` en el perfil (por defecto
  32000).

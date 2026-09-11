# Gemini-3-Ultra

> **Registro desactivado el 11.09.2026 (tarea T-216-S0, revisión de proveedores).** Esta
> versión tiene más de tres versiones más nuevas de la misma familia (3.1, 3.5, 3.6,
> 3.7, 3.8) y su identificador ya no está en el catálogo público de modelos. El registro
> se conserva: lo referencian ejecutores, tareas y informes anteriores. Puede volver a
> activarse con la casilla «activa»: **Ajustes → Catálogos → Modelos de IA**.

**Alojamiento:** nube (Google, a través de una capa compatible con OpenAI)
**Conexión:** `provider: openai-compatible`,
`baseUrl: https://generativelanguage.googleapis.com/v1beta/openai`, modelo `gemini-3-ultra`
**Referencia a la clave:** `google.apiKey`

**El contexto más grande del catálogo: 2 000 000 de tokens**, el doble que el de Claude y el de
GPT. Es por eso por lo que se mantiene: un encargo con una descripción enorme, una conversación
larga o todo un lote de materiales adjuntos pasa entero. Puntuaciones de habilidad de 90 a 94, y
sus puntos fuertes son los resúmenes y el análisis de datos. El precio es alto: 10 $ y 30 $ por
millón de tokens.

Acepta como entrada no sólo texto e imágenes, sino también **sonido y vídeo** (`audio/*`,
`video/*`), así que vale para encargos en los que el material llega como grabación.

## Cómo conseguir la clave

1. Abra [Google AI Studio](https://aistudio.google.com/apikey) y entre con su cuenta de Google.
2. Pulse **Create API key** y elija o cree un proyecto de Google Cloud.
3. Copie la clave (empieza por `AIza`).
4. Para la tarifa de pago, vincule la facturación del proyecto: el nivel gratuito recorta la
   frecuencia de peticiones y en encargos largos el agente chocará con un 429.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Gemini-3-Ultra → «Establecer la clave de
   API»**.

La clave es común con todos los Gemini del catálogo: Gemini-3.1-Pro y Gemini-3.7-Flash hacen
referencia a ese mismo `google.apiKey`.

## Límites y coste

| | |
|---|---|
| Contexto | 2 000 000 de tokens |
| Máximo de respuesta | 65 536 tokens |
| Coste de entrada | 10,00 $ por 1 millón de tokens |
| Coste de salida | 30,00 $ por 1 millón de tokens |

Dos salvedades que importan más que las cifras:

* **El identificador del modelo no se ha comprobado con una llamada real**, y en el catálogo
  público de modelos, a 17.08.2026, la variante Ultra no estaba en absoluto: el id y el precio
  están tomados de un repaso del mercado. Cotéjelos con una petición `GET /models` a
  `https://generativelanguage.googleapis.com/v1beta/openai`.
* **Lo completa que sea la compatibilidad con OpenAI de la capa de Google tampoco se ha comprobado
  con una llamada real.** La capa `/v1beta/openai` cubre las llamadas principales, pero algunos
  campos (herramientas, streaming) pueden comportarse de otra manera que en OpenAI.

Los precios actuales están en [Gemini API Pricing](https://ai.google.dev/pricing).

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

* **404 «model not found»**: el id no es ese; cotéjelo con la lista de `GET /models`.
* **429 en un encargo largo**: está funcionando el nivel gratuito; vincule la facturación.
* **Error en las herramientas**: es una particularidad de la capa de compatibilidad; pruebe
  Gemini-3.1-Pro u otro proveedor.

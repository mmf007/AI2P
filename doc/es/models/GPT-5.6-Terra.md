# GPT-5.6-Terra

**Alojamiento:** nube (API de OpenAI, compatible con OpenAI)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://api.openai.com/v1`,
modelo `gpt-5.6-terra`
**Referencia a la clave:** `openai.apiKey`

La tarifa intermedia de la línea: puntuaciones de habilidad de 85 a 88 frente a las 91–95 de
GPT-5.6-Sol, y un precio cinco veces menor con el mismo contexto de 1 050 000 tokens. Buena
elección para el trabajo voluminoso, donde el buque insignia es excesivo pero la calidad tiene que
seguir siendo alta.

## Cómo conseguir la clave

**La clave es la misma** que la de GPT-5.6-Sol: ambos modelos hacen referencia a `openai.apiKey`.
Si ya está introducida, este modelo funcionará solo.

Si todavía no hay clave:

1. Regístrese en [platform.openai.com](https://platform.openai.com/).
2. Recargue el saldo: **Settings → Billing → Add to credit balance**.
3. Abra [platform.openai.com/api-keys](https://platform.openai.com/api-keys) → **Create new secret
   key**.
4. Copie el valor (se muestra una sola vez y empieza por `sk-`).
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → GPT-5.6-Terra → «Establecer la clave de
   API»**.

## Límites y coste

| | |
|---|---|
| Contexto | 1 050 000 tokens |
| Máximo de respuesta | 128 000 tokens |
| Coste de entrada | 1,00 $ por 1 millón de tokens |
| Coste de salida | 6,00 $ por 1 millón de tokens |

**Coteje el precio sin falta antes de gastos serios.** Las fuentes no coinciden: el repaso del
mercado (informe T-213) daba 2,50 $ y 15,00 $, y el catálogo público de modelos, a 17.08.2026,
1,00 $ y 6,00 $. En el catálogo se han anotado los segundos números. La tarifa actual está en
[OpenAI Pricing](https://openai.com/api/pricing/).

**El identificador del modelo no se ha comprobado con una llamada real**: está tomado del catálogo
de modelos sin el prefijo del proveedor. Antes del primer encargo cotéjelo con una petición
`GET /models` a `https://api.openai.com/v1`.

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | **el resultado es suyo**: «Customer owns all Output»; OpenAI le cede sus derechos sobre él |
| Uso comercial | permitido |
| Qué es obligatorio | cumplir las Usage Policies; recordar que no se le promete la unicidad del resultado, y que esa misma respuesta le puede tocar a otro |
| Texto de las condiciones | <https://openai.com/policies/services-agreement/> |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Los pesos son cerrados y no hay nada que licenciar: las condiciones se refieren al **resultado de
la generación**.

El contenido que va a la API OpenAI **no lo usa para el desarrollo de sus servicios** mientras
usted no lo permita explícitamente; en el ChatGPT de consumo no es así.

Cotejado el 27.08.2026: la formulación sobre la propiedad del resultado («you … own the Output. We
hereby assign to you all our right, title, and interest, if any, in and to Output») se ha leído
literalmente en las condiciones de OpenAI; esa misma formulación está en el acuerdo para la API
del enlace anterior.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`api.openai.com`.

## Errores frecuentes

* **404 «model not found»**: el id ha cambiado; cotéjelo con la lista de `GET /models`.
* **429 «insufficient_quota»**: no se ha recargado el saldo de la organización de OpenAI.
* **La factura ha salido el doble de lo calculado**: compruebe el precio en la tarifa del
  proveedor (véase más arriba) y corríjalo en la declaración del modelo.

# Claude-Haiku-5.5

**Alojamiento:** nube (API del proveedor Anthropic)
**Conexión:** `provider: anthropic`, modelo `claude-haiku-5-5`
**Referencia a la clave:** `anthropic.apiKey`

Anthropic publicó este modelo el 07.10.2026; entró en el catálogo de AI2P con la tarea
T-466-S0, la revisión de proveedores del 13.10.2026. El identificador `anthropic/claude-haiku-5.5`, el precio y
las modalidades se han verificado con una consulta al catálogo público OpenRouter.

Contexto de 1 000 000 tokens, respuesta de hasta 128 000 tokens, 0,10 $ y 0,50 $ por millón de
tokens, puntuaciones 84-90. El registro más cercano del catálogo es **Claude-Haiku-4.5**.

Entrada admitida: texto y Markdown, código fuente, imágenes, PDF.

## Cómo conseguir la clave

**La clave es la misma** que la de Claude-Fable-5: ambos modelos llaman a un mismo proveedor y
hacen referencia a un mismo secreto, `anthropic.apiKey`. Si la clave ya está introducida para uno
de ellos, el otro funcionará solo.

Si todavía no hay clave:

1. Regístrese en la [Anthropic Console](https://console.anthropic.com/).
2. Recargue el saldo: **Plan & Billing → Add credits**.
3. **Settings → API keys → Create Key**, y copie el valor (se muestra una sola vez y empieza por
   `sk-ant-`).
4. En AI2P: **Ajustes → Catálogos → Modelos de IA → Claude-Haiku-5.5 → «Establecer la clave de
   API»**.

La clave pertenece a la organización y se replica cifrada a sus servidores (cap. 10 de la
especificación).

## Límites y coste

| | |
|---|---|
| Contexto | 1 000 000 de tokens |
| Máximo de respuesta | 128 000 tokens |
| Coste de entrada | 0,10 $ por 1 millón de tokens |
| Coste de salida | 0,50 $ por 1 millón de tokens |

Los precios actuales están en
[Anthropic Pricing](https://www.anthropic.com/pricing).

**El identificador del modelo no se ha comprobado con una llamada real.** Antes del primer encargo
cotéjelo con una petición `GET /models` a la API del proveedor: en Anthropic los nombres de los
checkpoints cambian, y un id incorrecto da un 404 ya en el ejecutor.

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | **el resultado es suyo**: Anthropic le cede todos sus derechos sobre los Outputs (Commercial Terms, sec. B) |
| Uso comercial | permitido; son las condiciones para organizaciones, y las de consumo no se aplican a la API |
| Qué es obligatorio | cumplir la Usage Policy; no se puede construir sobre el servicio un producto competidor, ni entrenar con él modelos competidores, ni revender el acceso |
| Texto de las condiciones | <https://www.anthropic.com/legal/commercial-terms> (redacción del 17.06.2025) |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Los pesos del modelo son cerrados y no se reparten a nadie: aquí no hay nada que licenciar, así
que las condiciones se refieren al **resultado de la generación**, no a los pesos.

Por esas mismas condiciones, Anthropic **no entrena modelos con lo que va a la API** («Anthropic
may not train models on Customer Content from Services»). En la variante del mismo modelo por
suscripción (el registro con el sufijo `_cli`) las condiciones son OTRAS: allí rigen las de
consumo, y el entrenamiento con los materiales se hace hasta que usted lo rechaza en los ajustes
de la cuenta.

Las condiciones se cotejaron con su texto el 13.10.2026; el proveedor puede cambiarlas, así que
antes de un lanzamiento comercial abra el enlace otra vez.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`api.anthropic.com`.

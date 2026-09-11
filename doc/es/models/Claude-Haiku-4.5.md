# Claude-Haiku-4.5

**Alojamiento:** nube (API de Anthropic)
**Conexión:** `provider: anthropic`, modelo `claude-haiku-4-5`
**Referencia a la clave:** `anthropic.apiKey`

El Claude más barato del catálogo: 1 $ y 5 $ por millón de tokens frente a los 2 $ y 10 $ de
Claude-Sonnet-5. Las puntuaciones de habilidad de 70 a 80 son **rutina corta**: traducciones,
resúmenes, correcciones menores de texto, comprobaciones sencillas. Para código complejo y
análisis de requisitos no conviene cogerlo; para eso están Sonnet y Opus.

La segunda limitación es el contexto de **200 000 tokens** en lugar de un millón: una tarea con
una descripción muy grande o un historial de chat largo este modelo no la aguantará.

## Cómo conseguir la clave

1. Cree una organización en [console.anthropic.com](https://console.anthropic.com/).
2. Recargue el saldo: **Billing → Add credits**.
3. Abra **API keys → Create Key** y póngale un nombre.
4. Copie el valor: se muestra **una sola vez** (empieza por `sk-ant-`).
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Claude-Haiku-4.5 → «Establecer la clave de
   API»**.

La clave es común con todos los modelos de Anthropic (`anthropic.apiKey`): introducida una vez,
funcionan Fable, Opus, Sonnet y Haiku.

## Límites y coste

| | |
|---|---|
| Contexto | 200 000 tokens |
| Máximo de respuesta | 64 000 tokens |
| Coste de entrada | 1,00 $ por 1 millón de tokens |
| Coste de salida | 5,00 $ por 1 millón de tokens |

**El identificador del modelo no se ha comprobado con una llamada real.** Antes del primer encargo
cotéjelo con una petición `GET /models` a la API del proveedor. Los precios actuales están en
[Anthropic Pricing](https://www.anthropic.com/pricing).

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

Las condiciones se cotejaron con su texto el 27.08.2026; el proveedor puede cambiarlas, así que
antes de un lanzamiento comercial abra el enlace otra vez.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`api.anthropic.com`.

## Errores frecuentes

* **Entrada demasiado larga**: los 200 000 tokens se acaban antes de lo que parece; saque los
  materiales voluminosos a archivos y ponga enlaces en la descripción.
* **El resultado es bastante peor de lo esperado**: la tarea es más compleja que el nicho del
  modelo; ponga el ejecutor a mano o desplace hacia la calidad el deslizador «precio ↔ calidad»
  del proyecto.
* **401 / «invalid x-api-key»**: la clave se ha introducido con un espacio o cortada.

# Claude-Sonnet-5

**Alojamiento:** nube (API de Anthropic)
**Conexión:** `provider: anthropic`, modelo `claude-sonnet-5`
**Referencia a la clave:** `anthropic.apiKey`

El caballo de batalla de la línea Claude 5: la calidad está cerca de Claude-Opus-5.0
(puntuaciones de habilidad de 89 a 93 frente a 95–98), y el precio es la mitad y **constante**:
2 $ por 1 millón de tokens de entrada y 10 $ por 1 millón de salida, sin descuentos según la hora
del día. Una elección razonable cuando Opus es excesivo y Claude-Haiku-4.5 ya no da la talla.

## Cómo conseguir la clave

1. Cree una organización en [console.anthropic.com](https://console.anthropic.com/).
2. Recargue el saldo: **Billing → Add credits**. Sin saldo las peticiones se rechazan.
3. Abra **API keys → Create Key** y póngale un nombre.
4. Copie el valor: se muestra **una sola vez**. La clave empieza por `sk-ant-`.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Claude-Sonnet-5 → «Establecer la clave de
   API»**.

La clave es común con todos los modelos de Anthropic —Claude-Fable-5, Claude-Opus-5.0,
Claude-Haiku-4.5—: todos ellos hacen referencia a `anthropic.apiKey`. Introducida una vez,
funcionan todos.

La clave pertenece a la organización: se cifra con su clave y se replica a todos los servidores
de la organización (cap. 10 de la especificación).

## Límites y coste

| | |
|---|---|
| Contexto | 1 000 000 de tokens |
| Máximo de respuesta | 128 000 tokens |
| Coste de entrada | 2,00 $ por 1 millón de tokens |
| Coste de salida | 10,00 $ por 1 millón de tokens |

En el perfil están `params.maxTokens: 16000` y `effort: high`: es el límite de **una respuesta**,
no del contexto; súbalo si el resultado se corta con el motivo `length`.

**El identificador del modelo no se ha comprobado con una llamada real.** Antes del primer encargo
cotéjelo con una petición `GET /models` a la API del proveedor: en Anthropic los nombres de los
checkpoints cambian, y un id incorrecto da un 404 ya en el ejecutor. Los precios actuales están en
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

* **401 / «invalid x-api-key»**: la clave se ha introducido con un espacio o cortada.
* **400 «credit balance is too low»**: no se ha recargado el saldo de la organización de
  Anthropic.
* **Respuesta vacía, motivo `length`**: `params.maxTokens` del perfil del modelo es pequeño.
* **El encargo sale más caro de lo esperado**: cuente por ambos números: un historial de chat
  largo se va a la entrada en cada turno del agente.

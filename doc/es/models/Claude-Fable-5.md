# Claude-Fable-5

**Alojamiento:** nube (API del proveedor Anthropic)
**Conexión:** `provider: anthropic`, modelo `claude-fable-5`
**Referencia a la clave:** `anthropic.apiKey`

El más potente de los modelos conectados: código, textos, análisis de requisitos y planificación.
Con él es razonable plantear las tareas de cabecera del proceso y el análisis de requisitos, y
dejar la rutina a modelos más baratos; la relación «precio ↔ calidad» se configura en el
proyecto.

## Cómo conseguir la clave

1. Regístrese en la [Anthropic Console](https://console.anthropic.com/).
2. Recargue el saldo: **Plan & Billing → Add credits**. Sin saldo positivo la clave se crea, pero
   las peticiones se rechazan con un error de fondos insuficientes.
3. Abra **Settings → API keys → Create Key** y dele a la clave un nombre comprensible (por
   ejemplo, `AI2P-trabajo`).
4. Copie el valor: se muestra **una sola vez**. La clave empieza por `sk-ant-`.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Claude-Fable-5 → «Establecer la clave de
   API»**. El valor se introduce en un campo de contraseña y no se muestra en ningún otro sitio.

La clave pertenece a la **organización**: se cifra con la clave de la organización y se replica a
todos sus servidores, así que no hace falta introducirla en cada servidor. Los detalles, en el
cap. 10 de la especificación.

En cuanto la clave está establecida, el modelo pasa a estar activo. Sin clave, un modelo en la
nube no puede estar activo.

## Límites y coste

| | |
|---|---|
| Contexto | 1 000 000 de tokens |
| Máximo de respuesta | 128 000 tokens |
| Coste de entrada | 10,00 $ por 1 millón de tokens |
| Coste de salida | 50,00 $ por 1 millón de tokens |

El coste de cada encargo se calcula con esos números y llega a la facturación (especificación,
ap. 6.2-bis). Los precios actuales están en la página de
[Anthropic Pricing](https://www.anthropic.com/pricing); si han cambiado, corrija `cost` en la
declaración de capacidades del modelo.

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

Las condiciones se cotejaron con su texto el 27.08.2026; el proveedor puede cambiarlas, así que
antes de un lanzamiento comercial abra el enlace otra vez.

## Requisitos de equipo

Ninguno: los cálculos van del lado del proveedor. Sólo hace falta salida a internet hacia
`api.anthropic.com`.

## Errores frecuentes

* **«No se ha encontrado la clave de API»**: la clave no está introducida, o este servidor todavía
  no ha recibido la clave de la organización (que se entrega al confirmar la conexión del servidor
  a la organización).
* **401 del proveedor**: la clave está revocada o se ha copiado incompleta.
* **«37529 tokens exceeds context»**: una tarea con una descripción enorme; reduzca la entrada o
  suba `params.maxTokens` en el perfil.
* **Negativa de los clasificadores de seguridad**: en el perfil está puesto el respaldo
  `params.fallbacks: ["claude-opus-5"]`, y la petición se repite automáticamente con otro modelo.

## Alternativa sin clave

Existe una variante de la misma familia con conexión por CLI, **Claude-Fable-5_cli**: funciona con
la suscripción de Claude Code y no requiere clave de API. Consulte su documento.

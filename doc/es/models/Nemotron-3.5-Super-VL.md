# Nemotron-3.5-Super-VL

**Alojamiento:** nube (NVIDIA, **a través de la pasarela OpenRouter**)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
modelo `nvidia/nemotron-3.5-super-vl`
**Referencia a la clave:** `openrouter.apiKey`

NVIDIA publicó este modelo el 15.10.2026; entró en el catálogo de AI2P con la tarea
T-558-S0, la revisión de proveedores del 20.10.2026. El identificador `nvidia/nemotron-3.5-super-vl`, el precio y
las modalidades se han verificado con una consulta al catálogo público OpenRouter.

Contexto de 262 144 tokens, respuesta de hasta 235 929 tokens, 0,15 $ y 0,50 $ por millón de
tokens, puntuaciones 77-83. El registro más cercano del catálogo es **Nemotron-3-Ultra**.

Entrada admitida: texto y Markdown, código fuente, imágenes, vídeo.

## Cómo conseguir la clave

**La clave es una sola para todos los modelos que van por la pasarela**: Muse-Spark-1.2,
Inkling-975B, Ling-3.0-Flash y este hacen referencia a ese mismo `openrouter.apiKey`.

1. Regístrese en [openrouter.ai](https://openrouter.ai/).
2. Recargue el saldo: **Credits → Add credits**.
3. Abra [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**.
4. Copie el valor (se muestra una sola vez y empieza por `sk-or-`).
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Nemotron-3.5-Super-VL → «Establecer la clave de
   API»**.

## Límites y coste

| | |
|---|---|
| Contexto | 262 144 tokens |
| Máximo de respuesta | 235 929 tokens |
| Coste de entrada | 0,15 $ por 1 millón de tokens |
| Coste de salida | 0,50 $ por 1 millón de tokens |

**El identificador del modelo no se ha comprobado con una llamada real.** En los modelos que van
por la pasarela se pone con el slug completo, con el prefijo del proveedor
(`nvidia/nemotron-3.5-super-vl`). Cotéjelo con una petición `GET /models` a
`https://openrouter.ai/api/v1`.

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | las fija el **propietario del modelo**, no la pasarela: «Your ownership rights in the Output are set forth in the Model Terms for each Model you use» |
| Uso comercial | consulte los Model Terms en la ficha del modelo de la pasarela: la pasarela responde de la entrega, no de los derechos |
| Qué es obligatorio | cumplir las condiciones tanto de la pasarela como del propietario del modelo; el propietario puede cambiar sus condiciones en cualquier momento |
| Texto de las condiciones | <https://openrouter.ai/terms> (redacción del 29.07.2026) más los Model Terms de la ficha del modelo |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Aquí hay un caso especial: **los pesos están publicados abiertamente, bajo OpenMDW-1.1** (ficha
`nvidia/NVIDIA-Nemotron-3.5-Super-VL-120B-A12B-BF16`, texto de la licencia en
<https://openmdw.ai/license/1-1/>, cotejado el 20.10.2026). La licencia es abierta y permite el
uso comercial.
Es decir, que este modelo se puede levantar en su equipo y no depender en absoluto de las
condiciones de la pasarela.

La pasarela, por su parte, **renuncia en lo posible al entrenamiento con los datos** en los
proveedores conectados, pero no responde de la exactitud de las condiciones ajenas y lo dice
expresamente.

Las condiciones de la pasarela se cotejaron con su texto el 20.10.2026.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`openrouter.ai`. Los pesos son abiertos, pero 120 000 millones de parámetros requieren un rack de
servidores: para el ordenador propio coja los registros locales del catálogo.

## Errores frecuentes

* **404 «No endpoints found»**: se ha perdido el prefijo del proveedor o ha cambiado el slug.
* **El encargo se queda colgado y termina por tiempo de espera**: ha ido a parar a la variante
  gratuita con cola; compruebe que en el campo «modelo» está el slug de pago y que hay fondos en
  la cuenta.
* **402 «Insufficient credits»**: no se ha recargado el saldo de OpenRouter.

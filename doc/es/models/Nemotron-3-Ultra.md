# Nemotron-3-Ultra

**Alojamiento:** nube (NVIDIA, **a través de la pasarela OpenRouter**)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
modelo `nvidia/nemotron-3-ultra-550b-a55b`
**Referencia a la clave:** `openrouter.apiKey`

El registro más abierto del catálogo: NVIDIA ha publicado no sólo los pesos, sino también los
**datos y las recetas de entrenamiento** bajo la licencia OpenMDW-1.1. La arquitectura es híbrida,
Mamba-2 más Transformer, con 550 000 millones de parámetros (55 000 millones activos).
Puntuaciones de habilidad de 76 a 82, contexto de 512 288 tokens y precio de 0,60 $ y 3,60 $ por
millón.

Su nicho son los encargos en los que importan la reproducibilidad y la apertura, no el récord de
calidad. Por la pasarela está disponible también una **variante gratuita** de este modelo (con
cola y límite de frecuencia): tiene otro slug, y si quiere puede crear un registro propio en el
catálogo.

NVIDIA no tiene API pública propia para este modelo, así que el registro va a través de la
pasarela **OpenRouter**.

## Cómo conseguir la clave

**La clave es una sola para todos los modelos que van por la pasarela**: Muse-Spark-1.2,
Inkling-975B, Ling-3.0-Flash y este hacen referencia a ese mismo `openrouter.apiKey`.

1. Regístrese en [openrouter.ai](https://openrouter.ai/).
2. Recargue el saldo: **Credits → Add credits**.
3. Abra [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**.
4. Copie el valor (se muestra una sola vez y empieza por `sk-or-`).
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Nemotron-3-Ultra → «Establecer la clave de
   API»**.

## Límites y coste

| | |
|---|---|
| Contexto | 512 288 tokens |
| Máximo de respuesta | 65 536 tokens |
| Coste de entrada | 0,60 $ por 1 millón de tokens |
| Coste de salida | 3,60 $ por 1 millón de tokens |

**El identificador del modelo no se ha comprobado con una llamada real.** En los modelos que van
por la pasarela se pone con el slug completo, con el prefijo del proveedor
(`nvidia/nemotron-3-ultra-550b-a55b`). Cotéjelo con una petición `GET /models` a
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
`nvidia/NVIDIA-Nemotron-3-Ultra-550B-A55B-BF16`, texto de la licencia en
<https://openmdw.ai/license/1-1/>, cotejado el 27.08.2026). La licencia es abierta y permite el
uso comercial, y junto con los pesos NVIDIA ha publicado los datos y las recetas de entrenamiento.
Es decir, que este modelo se puede levantar en su equipo y no depender en absoluto de las
condiciones de la pasarela.

La pasarela, por su parte, **renuncia en lo posible al entrenamiento con los datos** en los
proveedores conectados, pero no responde de la exactitud de las condiciones ajenas y lo dice
expresamente.

Las condiciones de la pasarela se cotejaron con su texto el 27.08.2026.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`openrouter.ai`. Los pesos son abiertos, pero 550 000 millones de parámetros requieren un rack de
servidores: para el ordenador propio coja los registros locales del catálogo.

## Errores frecuentes

* **404 «No endpoints found»**: se ha perdido el prefijo del proveedor o ha cambiado el slug.
* **El encargo se queda colgado y termina por tiempo de espera**: ha ido a parar a la variante
  gratuita con cola; compruebe que en el campo «modelo» está el slug de pago y que hay fondos en
  la cuenta.
* **402 «Insufficient credits»**: no se ha recargado el saldo de OpenRouter.

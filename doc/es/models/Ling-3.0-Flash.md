# Ling-3.0-Flash

**Alojamiento:** nube (Ant Group, **a través de la pasarela OpenRouter**)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
modelo `inclusionai/ling-3.0-flash`
**Referencia a la clave:** `openrouter.apiKey`

**El registro más barato del catálogo: 0,021 $ por 1 millón de tokens de entrada**, unas ciento
cincuenta veces más barato que Kimi-K3. Es un modelo de 124 000 millones de parámetros (5 100
millones activos) que apunta a escenarios de agente de alta frecuencia: muchos turnos cortos
seguidos.

Su nicho en AI2P es el **fondo barato** para encargos de muchos pasos: borradores, correcciones
menores masivas, etiquetado previo del material. Las puntuaciones de habilidad son de 74 a 82, y
el contexto es menor que el de sus vecinos: 262 144 tokens.

Ant Group no tiene API pública propia, así que el registro va a través de la pasarela
**OpenRouter**.

## Cómo conseguir la clave

**La clave es una sola para todos los modelos que van por la pasarela**: Muse-Spark-1.2,
Inkling-975B, Nemotron-3-Ultra y este hacen referencia a ese mismo `openrouter.apiKey`.

1. Regístrese en [openrouter.ai](https://openrouter.ai/).
2. Recargue el saldo: **Credits → Add credits**. La recarga mínima dará para mucho tiempo: un
   millón de tokens de entrada cuesta un par de céntimos.
3. Abra [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**.
4. Copie el valor (se muestra una sola vez y empieza por `sk-or-`).
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Ling-3.0-Flash → «Establecer la clave de
   API»**.

## Límites y coste

| | |
|---|---|
| Contexto | 262 144 tokens |
| Máximo de respuesta | 32 768 tokens |
| Coste de entrada | 0,021 $ por 1 millón de tokens |
| Coste de salida | 0,063 $ por 1 millón de tokens |

**El identificador del modelo no se ha comprobado con una llamada real.** En los modelos que van
por la pasarela se pone con el slug completo, con el prefijo del proveedor
(`inclusionai/ling-3.0-flash`). Cotéjelo con una petición `GET /models` a
`https://openrouter.ai/api/v1`.

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | las fija el **propietario del modelo**, no la pasarela: «Your ownership rights in the Output are set forth in the Model Terms for each Model you use» |
| Uso comercial | consulte los Model Terms en la ficha del modelo de la pasarela: la pasarela responde de la entrega, no de los derechos |
| Qué es obligatorio | cumplir las condiciones tanto de la pasarela como del propietario del modelo; el propietario puede cambiar sus condiciones en cualquier momento |
| Texto de las condiciones | <https://openrouter.ai/terms> (redacción del 29.07.2026) más los Model Terms de la ficha del modelo |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Los pesos de este modelo también son abiertos: **MIT** (ficha `inclusionAI/Ling-3.0-flash`,
cotejado el 27.08.2026); el uso comercial está permitido y hay que conservar el texto de la
licencia y el aviso de derechos de autor. Es decir, que el modelo se puede levantar también en su
equipo; el registro del catálogo va por la pasarela, y al resultado se le aplican las condiciones
del propietario del modelo.

La pasarela, por su parte, **renuncia en lo posible al entrenamiento con los datos** en los
proveedores conectados, pero no responde de la exactitud de las condiciones ajenas y lo dice
expresamente.

Las condiciones de la pasarela se cotejaron con su texto el 27.08.2026.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`openrouter.ai`.

## Errores frecuentes

* **La selección automática elige siempre este modelo**: el deslizador «precio ↔ calidad» del
  proyecto está desplazado hacia el precio (especificación, ap. 2.7). Despácelo hacia la calidad o
  ponga el ejecutor a mano.
* **Entrada demasiado larga**: el contexto es de 262 144 tokens, cuatro veces menos que el de los
  buques insignia.
* **404 «No endpoints found»**: se ha perdido el prefijo del proveedor o ha cambiado el slug en la
  pasarela.

# GLM-5.3

**Alojamiento:** nube (Z.ai / Zhipu, compatible con OpenAI)
**Conexión:** `provider: openai-compatible`, `baseUrl: https://api.z.ai/api/paas/v4`,
modelo `glm-5.3`
**Referencia a la clave:** `zai.apiKey`

Z.ai (Zhipu) publicó este modelo el 18.08.2026; entró en el catálogo de AI2P con la tarea
T-216-S0, la revisión de proveedores del 11.09.2026. El identificador `z-ai/glm-5.3`, la longitud
del contexto, el precio y las modalidades se han verificado con una consulta al
catálogo público OpenRouter.

Continúa la línea **GLM-5.2**: contexto de 1 048 576 tokens, respuesta de hasta 65 536 tokens,
1,40 $ y 4,40 $ por millón de tokens. Puntuaciones de habilidades 84-93.

Entrada admitida: texto y Markdown, código fuente.

## Cómo conseguir la clave

1. Regístrese en [z.ai](https://z.ai/) (la plataforma internacional de Zhipu).
2. Recargue el saldo en la sección de facturación: sin saldo las peticiones se rechazan.
3. Abra la [lista de claves](https://z.ai/manage-apikey/apikey-list) → **Create API key** y
   póngale un nombre.
4. Copie el valor: se muestra **una sola vez**.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → GLM-5.3 → «Establecer la clave de API»**.

La clave pertenece a la organización: se cifra con su clave y se replica a todos los servidores de
la organización (cap. 10 de la especificación).

## Límites y coste

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Máximo de respuesta | 65 536 tokens |
| Coste de entrada | 1,40 $ por 1 millón de tokens |
| Coste de salida | 4,40 $ por 1 millón de tokens |

**El identificador del modelo no se ha comprobado con una llamada real**: está tomado del catálogo
de modelos sin el prefijo del proveedor. Cotéjelo con una petición `GET /models` a
`https://api.z.ai/api/paas/v4`.

Fíjese en el `baseUrl`, que es inusual: la ruta es `/api/paas/v4`, no `/v1`. Si ha copiado la
dirección de una instrucción ajena y obtiene un 404 en todas las peticiones, lo más probable es
que sea por eso.

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | **los derechos sobre las peticiones y el resultado se quedan con usted** (Z.AI Terms of Use, sec. IV) |
| Uso comercial | permitido |
| Qué es obligatorio | no quitar las marcas de IA puestas por Z.ai, señalar lo generado como creado por una IA y no hacerlo pasar por trabajo de una persona |
| Texto de las condiciones | <https://docs.z.ai/legal-agreement/terms-of-use> |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

En los usuarios individuales (no corporativos), Z.ai tiene derecho a usar lo enviado y lo recibido
para el desarrollo del servicio; si eso no es aceptable, hace falta un contrato corporativo.

Aparte, sobre los pesos: **GLM-5.3 está publicado bajo MIT** (ficha `zai-org/GLM-5.3`, cotejado el
11.09.2026), es decir, que el modelo se puede levantar también en su equipo. Este registro del
catálogo llama a la nube de Z.ai, y allí rigen las condiciones de arriba, no la MIT.

Las condiciones se cotejaron con su texto el 11.09.2026.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia `api.z.ai`.

## Errores frecuentes

* **404 en cualquier petición**: la ruta del `baseUrl` está cortada o le sobra algo (hace falta
  `/api/paas/v4`).
* **401 «invalid api key»**: la clave se ha creado en la plataforma china (`bigmodel.cn`) y la
  dirección es la internacional.
* **El modelo se niega a aceptar una imagen**: la entrada es sólo de texto; para encargos
  multimodales coja MiniMax-M3 o Gemini.

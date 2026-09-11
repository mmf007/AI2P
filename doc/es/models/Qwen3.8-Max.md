# Qwen3.8-Max

**Alojamiento:** nube (Alibaba DashScope, compatible con OpenAI)
**Conexión:** `provider: openai-compatible`,
`baseUrl: https://dashscope-intl.aliyuncs.com/compatible-mode/v1`, modelo `qwen3.8-max`
**Referencia a la clave:** `qwen.apiKey`

Líder de la prueba **OSWorld-Verified (86,1)**, es decir, del «trabajo con el ordenador»: acciones
de varios pasos con archivos, herramientas e interfaces. Ese es exactamente el escenario del
agente de AI2P, así que el modelo es más útil de lo que muestran sus puntuaciones generales de
habilidad (86–92). El precio es de 2 $ y 6 $ por millón de tokens con un contexto de 1 000 000; la
entrada acepta imágenes y vídeo.

**La dirección del catálogo es la internacional** (`dashscope-intl`). Dentro de China funciona
otro host, `dashscope.aliyuncs.com`; si hace falta, cambie el `baseUrl` en el perfil del modelo.

## Cómo conseguir la clave

1. Regístrese en
   [Alibaba Cloud Model Studio](https://modelstudio.console.alibabacloud.com/) (la plataforma
   internacional).
2. Active el servicio de modelos y vincule un método de pago.
3. Abra **API-KEY → Create API Key** y elija un espacio de trabajo.
4. Copie el valor: se muestra **una sola vez**. La clave empieza por `sk-`.
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → Qwen3.8-Max → «Establecer la clave de API»**.

La clave pertenece a la organización: se cifra con su clave y se replica a todos los servidores de
la organización (cap. 10 de la especificación). Los modelos Qwen **locales** del catálogo no
tienen clave en absoluto: funcionan en su propio ordenador.

## Límites y coste

| | |
|---|---|
| Contexto | 1 000 000 de tokens |
| Máximo de respuesta | 65 536 tokens |
| Coste de entrada | 2,00 $ por 1 millón de tokens |
| Coste de salida | 6,00 $ por 1 millón de tokens |

**El identificador del modelo no se ha comprobado con una llamada real**: está tomado del catálogo
de modelos sin el prefijo del proveedor. Cotéjelo con una petición `GET /models` a
`https://dashscope-intl.aliyuncs.com/compatible-mode/v1`. Los precios actuales están en la consola
de Model Studio.

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | las fija el contrato de Model Studio: el acceso de pago con clave da derecho a usar el resultado, y la responsabilidad sobre él es suya |
| Uso comercial | permitido con el acceso de pago; el contenido obtenido en el modo **de prueba** de la consola sólo se puede usar para evaluar el modelo |
| Qué es obligatorio | cumplir las normas de Model Studio y responder de la legalidad de lo generado: el proveedor no da garantías sobre el contenido |
| Texto de las condiciones | <https://www.alibabacloud.com/help/en/model-studio/related-agreements> (lista de los acuerdos vigentes) |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Los pesos de `qwen3.8-max` son cerrados (a diferencia de los Qwen menores, que están bajo Apache
2.0), así que se habla del **resultado de la generación**.

Cotejado el 27.08.2026: se ha leído la página con la lista de acuerdos de Model Studio (última
actualización, 30.06.2026; están vigentes los Terms of Service, el Service Level Agreement y los
Open Source Model License Terms); el texto de los Terms of Service en sí no se ha dejado leer por
máquina: ábralo con el enlace anterior y léalo con los ojos, sobre todo la sección del modo de
prueba.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`dashscope-intl.aliyuncs.com`.

## Errores frecuentes

* **404 o «model not exist»**: o ha cambiado el id, o se ha cogido el host que no era (el
  internacional frente al chino).
* **401 «InvalidApiKey»**: la clave se ha creado en otro espacio de trabajo.
* **Respuesta vacía, motivo `length`**: aumente `params.maxTokens` en el perfil (por defecto
  32000).

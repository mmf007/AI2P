# YandexGPT-5.1-Pro

**Alojamiento:** nube (Yandex Cloud, Rusia; compatible con OpenAI)
**Conexión:** `provider: openai-compatible`,
`baseUrl: https://llm.api.cloud.yandex.net/v1`, modelo
`gpt://<folder-id>/yandexgpt/latest`
**Referencia a la clave:** `yandex.apiKey`

Texto en ruso potente: `text-write` 86, `text-translate` 86, `text-edit` 85. El código es su punto
flojo (60–64), y para programar no conviene coger este modelo. El contexto es modesto para lo que
hay en el catálogo: 32 000 tokens.

## En el campo «modelo» hay que poner la dirección propia

Yandex Cloud no espera un identificador corto, sino el **recurso completo**:

```
gpt://<folder-id>/yandexgpt/latest
```

**El identificador de la carpeta (folder ID) es propio de cada usuario**, así que en el catálogo
se ha dejado como marcador de posición: el registro «tal cual» no funcionará. Abra **Ajustes →
Catálogos → Modelos de IA → YandexGPT-5.1-Pro → «Perfil de conexión»** y sustituya `<folder-id>`
por el suyo (del tipo `b1g...`, visible en la consola de Yandex Cloud en la dirección de la
carpeta). En lugar de `latest` se puede indicar una versión concreta del modelo.

## Cómo conseguir la clave

1. Cree una carpeta en la [consola de Yandex Cloud](https://console.yandex.cloud/) y vincúlela a
   una cuenta de facturación.
2. Cree una **cuenta de servicio** y dele el rol `ai.languageModels.user` sobre la carpeta.
3. Cree una **clave de API** de la cuenta de servicio: **Cuentas de servicio → su cuenta → Crear
   una clave nueva → Crear clave de API**.
4. Copie la parte secreta: se muestra **una sola vez**.
5. Copie el **identificador de la carpeta** y póngalo en el campo «modelo» del perfil (véase más
   arriba).
6. En AI2P: **Ajustes → Catálogos → Modelos de IA → YandexGPT-5.1-Pro → «Establecer la clave de
   API»**.

## Límites y coste

| | |
|---|---|
| Contexto | 32 000 tokens |
| Máximo de respuesta | 16 000 tokens |
| Coste | según la tarifa de Yandex Cloud (en el catálogo está puesto cero) |

La tarifa se cuenta en rublos por mil tokens, así que en la declaración el coste se ha dejado a
cero: **la facturación de AI2P no mostrará el gasto de este modelo**; consúltelo en la consola de
Yandex Cloud. El contexto de 32 000 tokens es el más pequeño del catálogo: una descripción de
tarea larga o un historial de chat grande no caben en él.

**El identificador del modelo no se ha comprobado con una llamada real**: coteje la lista de
modelos disponibles con una petición `GET /models` a `https://llm.api.cloud.yandex.net/v1` con su
clave.

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | el cliente **tiene derecho a usar el Contenido Generado de cualquier forma** que no contradiga las condiciones ni la ley (p. 4.1); no se le prometen ni derecho exclusivo ni unicidad |
| Uso comercial | permitido |
| Qué es obligatorio | no hacer pasar lo generado por resultado del trabajo humano (p. 3.11.4); Yandex tiene derecho a imponer la obligación de indicar que se ha empleado el servicio (p. 3.8) |
| Texto de las condiciones | <https://yandex.ru/legal/cloud_terms_yandex_foundation_models/ru/> |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Sobre el entrenamiento se dice directamente (p. 5.4.1): la información de las peticiones **puede
usarse para la depuración y el entrenamiento de los modelos** mientras usted no ponga el parámetro
especial de prohibición. Para trabajar con datos ajenos conviene hacerlo antes del primer encargo.

Las condiciones se cotejaron con su texto el 27.08.2026.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hace falta salida a internet hacia
`llm.api.cloud.yandex.net`.

## Errores frecuentes

* **400 / «model not found»**: en el campo «modelo» se ha quedado el marcador de posición
  `<folder-id>`.
* **403 «permission denied»**: a la cuenta de servicio no se le ha dado el rol
  `ai.languageModels.user` sobre la carpeta que corresponde.
* **El encargo se corta con una descripción larga**: no bastan los 32 000 tokens de contexto;
  saque los materiales a archivos y ponga enlaces en la descripción.

# GigaChat-3.5-Ultra

**Alojamiento:** nube (Sber, Rusia; compatible con OpenAI)
**Conexión:** `provider: openai-compatible`,
`baseUrl: https://gigachat.devices.sberbank.ru/api/v1`, modelo `GigaChat-3.5-Ultra`
**Referencia a la clave:** `gigachat.accessToken`

Modelo ruso de 432 000 millones de parámetros (MoE) con pesos abiertos bajo MIT. **El mejor texto
en ruso del catálogo**: puntuaciones de `text-write` 85, `text-edit` 84, `text-translate` 84. El
código es bastante más flojo (61–66): para programar coja otros registros.

## La conexión es incompleta: léalo antes de crear una clave

El registro está en el catálogo, pero **no funciona del todo de serie**, y es una limitación
conocida, no un error de instalación:

* Sber no entrega una clave permanente, sino un **token de acceso por OAuth que vive 30 minutos**.
  El conector de AI2P sólo sabe manejar una clave permanente, así que el token pegado deja de
  funcionar al cabo de media hora y habrá que volver a introducirlo. El campo del catálogo se
  llama precisamente así: `gigachat.accessToken`.
* Hace falta el **certificado raíz ruso** (del Ministerio de Desarrollo Digital) en el almacén de
  certificados de confianza del ordenador; si no, la conexión con
  `gigachat.devices.sberbank.ru` se cortará en la verificación TLS.

El soporte completo (un conector propio con renovación del token, o un adaptador proxy externo) es
una tarea aparte del proyecto. Mientras no exista, este registro vale para pruebas puntuales, no
para el trabajo de fondo de un agente.

## Cómo conseguir la clave

1. Regístrese en el [Studio de Sber](https://developers.sber.ru/studio/) y cree un proyecto
   **GigaChat API**.
2. Consiga el par **Client ID / Client Secret** («clave de autorización» en la interfaz).
3. Instale el certificado raíz ruso del Ministerio de Desarrollo Digital en el ordenador donde
   funciona AI2P.
4. Cambie la clave de autorización por un **access token** (una petición `POST` a
   `https://ngw.devices.sberbank.ru:9443/api/v2/oauth`, con la cabecera `RqUID` y el campo
   `scope=GIGACHAT_API_PERS` o `GIGACHAT_API_CORP`).
5. En AI2P: **Ajustes → Catálogos → Modelos de IA → GigaChat-3.5-Ultra → «Establecer la clave de
   API»**: pegue el token obtenido. **Al cabo de 30 minutos repita los pasos 4 y 5**: el tiempo de
   vida del token no se prorroga.

## Límites y coste

| | |
|---|---|
| Contexto | 262 144 tokens |
| Máximo de respuesta | 32 768 tokens |
| Coste | según la tarifa de Sber (en el catálogo está puesto cero) |

La tarifa se cuenta en unidades propias de Sber, no en dólares por millón de tokens, así que en la
declaración el coste se ha dejado a cero: **la facturación de AI2P no mostrará el gasto de este
modelo**. Consúltelo en el área personal de Studio.

**El identificador del modelo no se ha comprobado con una llamada real**: cotéjelo con una
petición `GET /models` a `https://gigachat.devices.sberbank.ru/api/v1`.

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | **los derechos sobre el Contenido Generado pertenecen al Usuario** (Acuerdo de GigaChat, p. 1.9) |
| Uso comercial | permitido |
| Qué es obligatorio | por el p. 5.2 usted le da al Banco una licencia irrevocable, gratuita y no exclusiva para usar el Contenido Generado, hasta la transformación (los derechos sobre la transformación se quedan con el Banco) y la publicidad |
| Texto de las condiciones | <https://developers.sber.ru/docs/ru/policies/gigachat-agreement/beta> |
| Pago por la generación | por tokens; la tarifa está en la sección «Límites y coste» |

Los derechos sobre el resultado son formalmente suyos, pero **la licencia que usted le da a Sber
es la más amplia entre los registros del catálogo**: reproducción, puesta a disposición del
público, transformación y uso publicitario. Si el resultado es un secreto comercial o no se puede
mostrar a terceros, coja otro modelo.

Las condiciones se cotejaron con el texto del acuerdo el 27.08.2026. El acuerdo está marcado como
beta: léalo antes de conectarlo, porque cambia más a menudo que los demás.

## Requisitos de equipo

Ninguno: los cálculos son del lado del proveedor. Hacen falta salida a internet hacia
`gigachat.devices.sberbank.ru` y el certificado raíz ruso instalado.

## Errores frecuentes

* **401 media hora después de un arranque correcto**: ha caducado el token de acceso; consiga uno
  nuevo.
* **Error de TLS / «no se ha podido establecer una relación de confianza»**: no está instalado el
  certificado raíz ruso.
* **Un encargo con código se ha hecho mal**: el nicho del modelo es el texto en ruso, no la
  programación.

# Claude-Fable-5_cli

**Alojamiento:** nube, pero la conexión **no es por API, sino por CLI**
**Conexión:** `transport: cli`, comando `claude --permission-mode acceptEdits`,
modelo `claude-fable-5`
**Referencia a la clave:** vacía — **no hace falta clave de API**

El mismo modelo que Claude-Fable-5, pero se lanza a través de **Claude Code CLI** en modo
headless. No se paga por tokens, sino **por suscripción**, y por eso en la facturación de AI2P el
coste de esos encargos es cero.

## No hace falta clave de API

La autorización es por la sesión del CLI, no por clave. Por eso en el perfil `secretRef` está
vacío, y este modelo no tiene el botón «Establecer la clave de API».

## Qué hace falta en lugar de la clave

1. **Instalar Claude Code**:
   [instrucciones oficiales](https://docs.claude.com/en/docs/claude-code/overview). Comprobación:
   en la consola, `claude --version` debe imprimir algo.
2. **Entrar con su suscripción**: `claude login` (el navegador se abrirá solo). La sesión se
   guarda en el perfil de usuario del SO.
3. Asegurarse de que `claude` está accesible **desde el PATH del usuario bajo el que funciona
   AI2P**. Si AI2P se ejecuta como servicio con otra cuenta, el acceso hay que hacerlo con ella;
   de lo contrario el agente chocará con un «no autorizado».
4. Si el ejecutable no está en el PATH, escriba la ruta completa en el campo `cliCommand` del
   perfil del modelo (el botón «Perfil de conexión» del formulario del modelo).

## En qué se diferencia de la variante por API

| | por API | por CLI |
|---|---|---|
| Pago | por tokens | por suscripción |
| Coste en la facturación | se cuenta | 0 |
| Herramientas | las herramientas de AI2P (lectura/escritura de archivos, etc.) | **las herramientas propias del CLI** |
| Carpeta de trabajo | no importa | **la carpeta del proyecto**: el agente trabaja directamente en ella |
| Preguntas a la persona | de serie | con el marcador `AI2P_QUESTION` en la respuesta, y la continuación con `--resume` |
| Creación de subtareas | con la herramienta `create_task` | con el marcador `AI2P_SUBTASK` en la respuesta |
| Lectura de otros encargos | con las herramientas `get_task_by_code`, `get_task_by_url`, `get_task_chat` | con el marcador `AI2P_GET_TASK` en la respuesta |
| Archivos externos del encargo (imágenes adjuntas) | con la herramienta `fetch_file` | con el marcador `AI2P_GET_FILE` en la respuesta |
| Traslado de una tarea por la jerarquía | con la herramienta `move_task` | con el marcador `AI2P_MOVE_TASK` en la respuesta |

Una consecuencia importante: en la conexión por CLI las herramientas de AI2P **no se le publican**
al agente, que usa las suyas. Las reglas de seguridad de AI2P (cap. 12 de la especificación) no se
aplican a sus acciones dentro de la carpeta del proyecto, así que indique esa carpeta de forma
consciente.

### Subtareas con el marcador `AI2P_SUBTASK`

El agente CLI no tiene la herramienta `create_task`, y la API de AI2P está cerrada con acceso por
cookie, así que antes no podía dividir una tarea en subtareas en absoluto. Ahora una subtarea se
crea con **una línea en la respuesta**:

```
AI2P_SUBTASK: {"title": "título", "description": "descripción del trabajo", "skills": ["code-write"], "priority": 20, "acceptance": "criterios de aceptación"}
```

Una línea, una subtarea; `title` y `description` son obligatorios. En cuanto el agente termina el
trabajo, AI2P ejecuta esas líneas ella misma: las quita del texto de la respuesta, crea las
subtareas (el ejecutor se elige automáticamente, primero la IA, luego una persona), marca la
tarea padre como dividida y añade al resultado una sección «Subtareas» con la lista de lo creado.
Después las subtareas se lanzan con la cola normal de división automática, por prioridad numérica
descendente.

Es esa misma acción **`AI2P.Tasks.Create`** que la herramienta, así que las reglas de seguridad
sobre ella funcionan: una prohibición (deny) o una exigencia de confirmación (confirm), y la
subtarea no se crea, mientras que el motivo de la negativa va al resultado del encargo y al
registro de trabajos. El marcador cuenta sólo **desde el principio de la línea**: una mención
dentro de una frase o en el informe no se convertirá en subtarea.

### Lectura de otros encargos con el marcador `AI2P_GET_TASK`

Las tareas de AI2P están en la base de la organización, en los archivos del proyecto no están, y
`/api` está cerrada con acceso por cookie; por eso el agente CLI sólo veía el texto de su encargo
y el bloque de la tarea padre. Si la persona daba en el enunciado un enlace a otra tarea («coge el
texto del resultado de la tarea …»), el agente no podía leerla. Ahora la pide con **una línea en
la respuesta**:

```
AI2P_GET_TASK: {"code": "T-15"}
```

En lugar de `code` se puede indicar `"url"` (un enlace del tipo `…/task/<id>`) o `"title"`
(búsqueda por parte del título, que devolverá la lista de encargos encontrados sin sus textos).
AI2P ejecuta la petición y envía la respuesta al agente en **el siguiente mensaje de esa misma
sesión del CLI** (`--resume`, como cuando una persona responde a una pregunta), tras lo cual él
continúa el trabajo desde el mismo punto. La respuesta es siempre completa: la ficha del encargo
(enunciado, criterios de aceptación, estado, artefactos de resultado) **y el chat entero**; no
hace falta pedir la conversación por separado. Se pueden emitir varios marcadores a la vez; el
total de peticiones por encargo no puede pasar de 10 (protección contra bucles), y a partir de ahí
el sistema le comunica al agente que se ha agotado el límite.

Son esas mismas acciones **`AI2P.Tasks.GetByCode` / `GetByUrl` / `FindByTitle` / `GetChat`** que
las herramientas del mismo nombre, así que las reglas de seguridad funcionan como siempre: una
prohibición o una exigencia de confirmación, y el encargo no se lee y la negativa se va al agente
y al registro de trabajos. Sólo están disponibles las tareas del mismo proyecto.

## Límites

| | |
|---|---|
| Contexto | 1 000 000 de tokens |
| Máximo de respuesta | 128 000 tokens |
| Coste | 0 (pagado por suscripción) |

**El CLI no informa del saldo del límite de la suscripción** (corrección de la v1.62), así que
AI2P cuenta ella misma el gasto, por los tokens de los encargos de una ventana deslizante.
Rellene en el ejecutor de IA los campos **«límite de tokens por ventana»** y **«ventana del
límite, h»** (en la suscripción de Claude la ventana es de 5 horas): entonces, antes de lanzar una
tarea, el sistema avisará de que casi no queda límite y aplazará el arranque hasta el momento en
que se libere la ventana (en lugar de eso se puede dividir la tarea o lanzarla a la fuerza). Con
los campos vacíos no hay comprobaciones y todo funciona como antes. Y si el límite llega ya en
mitad del encargo, AI2P reconoce el mensaje del CLI (`5-hour limit reached ∙ resets 4:10pm (…)`),
toma de él la hora del reinicio y pasa la tarea a **«en espera»** hasta esa hora en lugar de a
«detenida con error»; luego se lanza sola. Más detalles, en el documento de Claude-Opus-5.0_cli.

**Cuánto esperar la respuesta** (corrección de la v1.63) lo indica el campo **«Tiempo de espera de
respuesta, min»** del formulario del ejecutor: vacío son 30 minutos, un número son esos minutos y
**0 es sin límite**. Si se agota el tiempo de espera, el encargo se cae con un error y una
indicación; al ejecutor, en cambio, no se le marca como ocupado (el silencio del agente no se
toma por un límite).

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado de la generación | **el resultado es suyo**: Anthropic le cede sus derechos sobre los Outputs (Consumer Terms, p. 4) |
| Uso comercial | permitido; la salvedad de «sólo personal no comercial» se refiere al acceso de prueba, no a la suscripción de pago |
| Qué es obligatorio | cumplir la Usage Policy; recordar que, según las condiciones de consumo, los materiales van al entrenamiento de los modelos hasta que se rechace en los ajustes de la cuenta |
| Texto de las condiciones | <https://www.anthropic.com/legal/consumer-terms> (redacción del 08.10.2025) |
| Pago por la generación | por suscripción, no por tokens; en la facturación de AI2P estos encargos cuestan 0 |

Aquí es importante no confundir dos contratos. La suscripción de Claude son las condiciones **de
consumo**; la clave de API, las **comerciales**
(<https://www.anthropic.com/legal/commercial-terms>), y en ellas no hay entrenamiento con sus
datos en absoluto.

Consecuencia práctica: si el encargo va sobre código o datos ajenos bajo acuerdo de
confidencialidad, la variante por clave de API es más segura que la de suscripción; o bien
rechace el entrenamiento en los ajustes de la cuenta de Claude.

Las condiciones se cotejaron con su texto el 27.08.2026.

## Requisitos de equipo

Ninguno en especial: calcula el proveedor. Hacen falta Claude Code instalado, salida a internet y
espacio suficiente en la carpeta del proyecto: el agente trabaja con los archivos directamente.

## Errores frecuentes

* **«claude no encontrado»**: el CLI no está instalado o no está en el PATH del usuario de AI2P.
* **Negativa silenciosa o petición de acceso**: la sesión del CLI no se ha creado o se ha creado
  con otra cuenta del SO; repita `claude login` con el usuario que corresponda.
* **El agente no ve los archivos de la tarea**: el proyecto no tiene carpeta indicada (el campo
  «Carpeta del proyecto»), y esa carpeta es precisamente la carpeta de trabajo del proceso del
  CLI.

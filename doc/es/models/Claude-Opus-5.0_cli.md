# Claude-Opus-5.0_cli

**Alojamiento:** nube, conexión **por CLI**
**Conexión:** `transport: cli`, comando `claude --permission-mode acceptEdits`,
modelo `claude-opus-5`
**Referencia a la clave:** vacía — **no hace falta clave de API**

Claude-Opus-5.0 lanzado a través de **Claude Code CLI** en modo headless. El pago es por
suscripción, y en la facturación de AI2P el coste de esos encargos es cero.

## No hace falta clave de API

La autorización va por la sesión del CLI. La configuración es exactamente la misma que en
Claude-Fable-5_cli:

1. instalar [Claude Code](https://docs.claude.com/en/docs/claude-code/overview);
2. ejecutar `claude login` **con el usuario del SO bajo el que funciona AI2P**;
3. asegurarse de que `claude` está accesible desde su PATH (si no, escribir la ruta completa en
   `cliCommand` del perfil del modelo).

La configuración es común a los dos modelos CLI: hecha una vez, activa los dos.

## Límites

| | |
|---|---|
| Contexto | 1 000 000 de tokens |
| Máximo de respuesta | 128 000 tokens |
| Coste | 0 (pagado por suscripción) |

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

Ninguno en especial: calcula el proveedor. Hacen falta Claude Code instalado y salida a internet.

## El límite de la suscripción (corrección de la v1.62)

El CLI no informa del saldo del límite de la suscripción: no está ni en `claude --help` ni en la
salida JSON del encargo, y `/usage` sólo funciona en una sesión interactiva. Por eso el gasto lo
cuenta la propia AI2P, por los tokens de los encargos de una ventana deslizante. Para que eso
funcione, rellene en el ejecutor de IA (Ejecutores → formulario del ejecutor) dos campos:

* **límite de tokens por ventana**: cuántos tokens está dispuesto a gastar por ventana; el valor
  se ajusta empíricamente según su suscripción;
* **ventana del límite, h**: en la suscripción de Claude la ventana es de **5 horas**.

Con los campos vacíos todo funciona como antes: el saldo no se cuenta y no hay avisos. Rellenos,
antes de lanzar una tarea AI2P comprueba el saldo y, si casi no queda, aplaza el arranque hasta
el momento en que se libere la ventana con un mensaje en el chat de la tarea (en lugar de eso, la
tarea se puede dividir en subtareas o lanzar a la fuerza). El arranque aplazado se guarda en la
tarea: el ordenador se puede apagar, y al encenderlo la tarea se lanzará sola.

**Si el límite llega igualmente en mitad del trabajo** (corrección de la v1.62), el CLI responde
con un mensaje corto del tipo `5-hour limit reached ∙ resets 4:10pm (Europe/Moscow)`. AI2P lo
reconoce, toma de él la hora del reinicio y **no lo considera un error de la tarea**: el encargo
se cierra con un artefacto de explicación, al ejecutor se le marca «ocupado hasta» y la tarea pasa
a **«en espera»**; en la cabecera de la ficha se ve la ficha «Espera hasta <hora>» y en el chat
aparece un mensaje. Cuando llegue el momento, la tarea se lanzará sola, como **encargo nuevo,
desde el principio** (todo lo que el agente haya llegado a escribir en los archivos del proyecto
se queda en su sitio). No hay que esperar delante del ordenador: se puede apagar. Si en el mensaje
no había hora de reinicio, se espera una hora.

## Tiempo de espera de respuesta (corrección de la v1.63)

El agente CLI lleva su ciclo por su cuenta y durante el trabajo **calla**: AI2P sólo ve el momento
en que el proceso ha terminado. Cuánto esperarlo se indica en el campo **«Tiempo de espera de
respuesta, min»** del formulario del ejecutor:

* **vacío**: 30 minutos (el valor por defecto del sistema, tal como estaba incrustado en el código
  antes de la 1.63);
* **un número**: esos minutos;
* **0**: esperar sin límite; el encargo sólo lo cortará el botón «detener».

Si se agota el tiempo de espera, el encargo termina **con error** y una indicación de dónde subir
el valor, y la tarea pasa a «detenida con error». Al ejecutor, en cambio, **no se le marca como
ocupado**: hasta la 1.63 el silencio del agente se tomaba por un límite de suscripción agotado y
el ejecutor quedaba fuera de juego durante una hora, aunque el límite pudiera estar gastado sólo
en un tercio. El límite de verdad AI2P lo sigue reconociendo por el mensaje del propio CLI (véase
la sección anterior).

## Qué recordar de la conexión por CLI

* el agente trabaja con las **herramientas propias del CLI**, y las herramientas de AI2P no se le
  publican;
* la carpeta de trabajo del proceso es la **carpeta del proyecto**, así que tiene que estar
  indicada;
* las reglas de seguridad de AI2P no se aplican a las acciones del agente dentro de esa carpeta;
* las preguntas a la persona se transmiten con el marcador `AI2P_QUESTION` en la respuesta, y la
  continuación del diálogo va con esa misma sesión del CLI (`--resume`);
* las subtareas el agente las crea con el marcador `AI2P_SUBTASK` en la respuesta, en lugar de con
  la herramienta `create_task`, que no tiene (véase Claude-Fable-5_cli);
* trasladar una tarea por la jerarquía (cambiar de padre, sacarla a la raíz) el agente lo puede
  hacer con el marcador `AI2P_MOVE_TASK`, en lugar de con la herramienta `move_task`;
* en una tarea «Condición» / de bucle el agente devuelve la decisión con el marcador
  `AI2P_CONDITION` (`{"value": true}`) o `AI2P_LOOP` (`{"continue": false}`), en lugar de las
  herramientas `set_condition_result` / `set_loop_result`; tareas de la rama desde plantilla —
  `AI2P_FROM_TEMPLATE`, detener la jerarquía — `AI2P_STOP_HIERARCHY` (o comandos `ai2p`);
* los textos de otros encargos (descripción, resultado y todo el chat) el agente los pide con el
  marcador `AI2P_GET_TASK`, en lugar de con las herramientas `get_task_by_code` /
  `get_task_by_url` / `get_task_chat`, que tampoco tiene.

Más sobre las diferencias entre API y CLI, en el documento de Claude-Fable-5_cli.

# Claude-Sonnet-5_cli

**Alojamiento:** nube, pero la conexión **no es por API, sino por CLI**
**Conexión:** `provider: anthropic`, `transport: cli`, comando
`claude --permission-mode acceptEdits`, modelo `claude-sonnet-5`
**Referencia a la clave:** vacía — **no hace falta clave de API**

El mismo modelo que Claude-Sonnet-5, pero se lanza a través de **Claude Code CLI** en modo
headless. No se paga por tokens, sino **por suscripción**, y por eso en la facturación de AI2P el
coste de esos encargos es cero. Su nicho es el trabajo voluminoso con los archivos del proyecto,
cuando no apetece pagar por tokens.

## No hace falta clave de API

La autorización es por la sesión del CLI, no por clave. Por eso en el perfil `secretRef` está
vacío, y este modelo no tiene el botón «Establecer la clave de API».

Qué hace falta en lugar de la clave:

1. **Instalar Claude Code**:
   [instrucciones oficiales](https://docs.claude.com/en/docs/claude-code/overview). Comprobación:
   en la consola, `claude --version` debe imprimir algo.
2. **Entrar con su suscripción**: `claude login`. La sesión se guarda en el perfil de usuario del
   SO.
3. Asegurarse de que `claude` está accesible **desde el PATH del usuario bajo el que funciona
   AI2P**. Si AI2P se ejecuta como servicio con otra cuenta, el acceso hay que hacerlo con ella.
4. Si el ejecutable no está en el PATH, escriba la ruta completa en el campo `cliCommand` del
   perfil del modelo.

## En qué se diferencia de la variante por API

| | por API | por CLI |
|---|---|---|
| Pago | por tokens | por suscripción |
| Coste en la facturación | se cuenta | 0 |
| Herramientas | las herramientas de AI2P | **las herramientas propias del CLI** |
| Carpeta de trabajo | no importa | **la carpeta del proyecto** |
| Preguntas a la persona | de serie | con el marcador `AI2P_QUESTION` en la respuesta |
| Creación de subtareas | con la herramienta `create_task` | con el marcador `AI2P_SUBTASK` |

El análisis detallado de las diferencias está en el documento de Claude-Fable-5_cli; en Sonnet
todo está hecho exactamente igual.

## Límites y coste

| | |
|---|---|
| Contexto | 1 000 000 de tokens |
| Máximo de respuesta | 128 000 tokens |
| Coste | 0 (pagado por suscripción) |

**AI2P reconoce el límite de la suscripción y espera a que se reinicie** (corrección de la
v1.63): al ver una negativa del CLI con el código 429, el sistema toma la hora del reinicio y pasa
la tarea a «en espera» en lugar de a «detenida con error», y al reiniciarse **continúa esa misma
sesión** (`--resume`) en vez de empezar el encargo de nuevo. Rellene en el ejecutor de IA los
campos «límite de tokens por ventana» y «ventana del límite, h» (en la suscripción de Claude la
ventana es de 5 horas) para que el sistema avise con antelación.

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
espacio en la carpeta del proyecto: el agente trabaja con los archivos directamente.

## Errores frecuentes

* **«claude no encontrado»**: el CLI no está instalado o no está en el PATH del usuario de AI2P.
* **Negativa silenciosa o petición de acceso**: la sesión se ha creado con otra cuenta del SO.
* **El agente no ve los archivos de la tarea**: el proyecto no tiene carpeta indicada («Carpeta del
  proyecto»).

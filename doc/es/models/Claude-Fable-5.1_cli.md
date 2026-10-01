# Claude-Fable-5.1_cli

**Alojamiento:** nube, pero la conexión es **por CLI, no por API**
**Conexión:** `provider: anthropic`, `transport: cli`, comando
`claude --permission-mode acceptEdits`, modelo `claude-fable-5-1`
**Referencia de clave:** vacía — **no hace falta clave de API**

El mismo modelo que Claude-Fable-5.1, pero lanzado a través del **Claude Code
CLI** en modo headless. Se paga por **suscripción**, no por tokens, así que en la
facturación de AI2P estas tareas cuestan cero.

El modelo más potente de la familia a 24.09.2026. Su nicho: código difícil, análisis de requisitos y tareas largas sobre los ficheros del proyecto.

## Por qué una entrada por versión

En el Claude Code CLI la versión del modelo la elige el campo **«modelo»** del perfil: se
pasa al CLI como el flag `--model`. Por eso cada versión que quiera poder elegir para un
ejecutor tiene **su propia entrada del catálogo**, y las entradas conviven: Claude-Fable-5_cli
conserva su identificador y esta funciona con `claude-fable-5-1`.

Se admiten dos formas del valor (`claude --help`):

* el **nombre completo de la versión** — `claude-fable-5-1`;
* un **alias de la última versión** — `fable`.

Las entradas de la distribución llevan el nombre completo: con el tiempo el alias pasa en
silencio a una versión nueva (comprobado con una llamada real el 24.09.2026: `fable` ya
significa `claude-fable-5-1`) y la entrada deja de significar lo que dice su nombre.

## No hace falta clave de API

La autorización es por la sesión del CLI: `secretRef` está vacío y la entrada no tiene botón
«Establecer clave de API». Lo que hace falta en su lugar:

1. **Instalar Claude Code** — [guía oficial](https://docs.claude.com/en/docs/claude-code/overview).
   Comprobación: `claude --version` debe imprimir algo.
2. **Iniciar sesión con su suscripción**: `claude login`.
3. Asegurarse de que `claude` esté en el **PATH del usuario con el que corre AI2P**.
4. Si el ejecutable no está en el PATH, escriba su ruta completa en el campo `cliCommand`.

El análisis detallado de las diferencias «por API / por CLI» está en el documento
Claude-Fable-5_cli; aquí todo funciona igual.

## La prueba comprueba también el modelo

El botón de prueba (cambio v1.144) hace tres cosas: ejecuta `claude --version`, consulta el
estado de la sesión y **comprueba el identificador del modelo con una llamada corta**. Un id
desconocido o al que la suscripción no da acceso se ve enseguida como fallo de la prueba, y
no como una tarea que cae minutos después. Si el CLI responde con otro modelo, AI2P lo
indica con un **evento del registro de la tarea** y con una línea en el resumen del resultado.

## Límites y coste

| | |
|---|---|
| Contexto | 1 000 000 tokens |
| Salida máxima | 128 000 tokens |
| Coste | 0 (pagado por suscripción) |

**AI2P reconoce el límite de la suscripción y espera su reinicio**: ante un 429 toma la hora
de reinicio, pasa la tarea a «esperando» y después continúa la misma sesión.

## Licencia

| | |
|---|---|
| Condiciones sobre el resultado | **el resultado es suyo**: Anthropic le cede sus derechos sobre los Outputs (Consumer Terms, §4) |
| Uso comercial | permitido |
| Qué es obligatorio | cumplir la Usage Policy; recordar que con las condiciones de consumo su material se usa para entrenar modelos hasta que lo desactive en la cuenta |
| Texto de las condiciones | <https://www.anthropic.com/legal/consumer-terms> |
| Pago por la generación | por suscripción, no por tokens — en la facturación de AI2P cuestan 0 |

La suscripción de Claude se rige por las condiciones **de consumo**; la clave de API, por las
**comerciales** (<https://www.anthropic.com/legal/commercial-terms>), que no incluyen
entrenamiento con sus datos.

## Requisitos de hardware

Ninguno especial: calcula el proveedor. Hacen falta Claude Code instalado, salida a internet
y espacio en la carpeta del proyecto.

## Errores frecuentes

* **«claude no encontrado»** — el CLI no está instalado o no está en el PATH del usuario de AI2P.
* **«Claude CLI no aceptó el modelo»** — el identificador está obsoleto o la suscripción no da
  acceso a esa versión; contraste el campo «modelo» con `claude --help`.
* **El agente no ve los ficheros de la tarea** — el proyecto no tiene carpeta asignada.

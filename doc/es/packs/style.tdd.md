# Desarrollo guiado por pruebas

**Código:** `style.tdd` · **Registros:** 10 · **Habilidades:** `code-test`, `code-write`,
`code-debug`, `code-refactor`

## Qué es este estilo

Desarrollo en el que la prueba se escribe antes que el código: primero la prueba en rojo,
después el cambio mínimo que la pone en verde, después la refactorización en verde. Se eligió
esta metodología porque cambia el **orden de las acciones** del ejecutor, no la forma del
informe, y ahí es donde un paquete de reglas de texto rinde más.

## Para quién es

Para proyectos con código ejecutable y algún conjunto de pruebas. Si no hay dónde ejecutarlas,
o la ejecución dura horas y nadie la lanza, el conjunto se queda en una lista de deseos:
consigue antes una ejecución rápida.

## Qué cambia tras la instalación

* La prueba se escribe primero y debe fallar por el motivo para el que fue escrita; el mensaje
  de fallo de la ejecución en rojo va al informe como prueba.
* Una prueba, una afirmación; el nombre dice la condición y el resultado esperado.
* A una prueba en rojo le corresponde el cambio mínimo; lo que «servirá luego» va a la vuelta
  siguiente.
* Corregir un defecto empieza por una prueba que lo reproduce.
* Se comprueba el comportamiento observable, no la construcción interna: una prueba no debe
  prohibir la refactorización.
* La prueba es reproducible: sin hora actual, sin azar, sin red, sin depender del orden.
* Una ejecución en verde solo cuenta junto con el número de pruebas superadas.
* La refactorización se hace en verde y no toca ninguna prueba.
* Una prueba ajena que se pone roja se investiga, no se desactiva en silencio.
* Un caso sin cubrir se nombra en el informe en una línea aparte.

## Qué conviene ajustar

* **La exigencia del «rojo en el informe»** sale cara si la ejecución dura horas: cámbiala por
  «nombra la prueba y el motivo del fallo», o el ejecutor buscará la vuelta.
* **La regla del comportamiento observable** gana si añades tu lista de lo que cuenta como
  observable (respuesta HTTP, fila en la base, evento del registro).
* **La regla del número de pruebas superadas** es la única que atrapa una «ejecución verde sin
  ninguna prueba». Si tus filtros son distintos, escribe tu forma del comando en el registro.
* **El ámbito de instalación.** Los registros `code-test` sirven a toda la organización; la
  regla del cambio mínimo choca con proyectos de pasos grandes: instálala por proyecto.

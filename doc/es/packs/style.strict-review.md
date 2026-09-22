# Revisión de código estricta

**Código:** `style.strict-review` · **Registros:** 9 · **Habilidades:** `code-review`,
`code-write`

## Qué es este estilo

La disciplina de revisar el trabajo ajeno: por dónde empieza la revisión, qué debe contener
cada observación y cómo termina. Se eligió esta metodología porque la revisión es el único
punto del proceso donde la calidad depende por completo de un acuerdo y no de una herramienta:
ningún analizador notará que se hizo algo distinto de lo que se pidió.

## Para quién es

Para equipos donde uno escribe el código y otro lo acepta, y sobre todo para los mixtos, donde
escribe la IA y acepta la persona (o al revés). Si la revisión no existe como paso aparte,
primero añádela al proceso y después instala el conjunto.

## Qué cambia tras la instalación

* La revisión empieza por el encargo y los criterios de aceptación, no por el diff.
* El informe abre con la lista de lo que se romperá: escenario de fallo con entradas concretas;
  las observaciones de estilo van después, en una sección aparte.
* Cada observación indica lugar (archivo, línea) y peso: bloqueante, importante, a criterio.
* Cada defecto nombra la prueba que lo detecta, existente o por escribir; sin ella la
  observación se considera no demostrada.
* Los límites se examinan siempre: entrada vacía, cero, el máximo, llamada repetida, llamada
  concurrente, caída de un servicio externo.
* La revisión se limita al código modificado; un problema antiguo al lado va a otra tarea.
* Quien revisa no edita el código ajeno y termina con un veredicto inequívoco: aceptado,
  aceptado con correcciones, devuelto.
* Antes de entregar, el autor recorre su cambio y quita todo lo que no pueda explicar.

El conjunto trae un nodo de plantilla, **«Revisión de los cambios»**, con el criterio de
aceptación «cada defecto indica un escenario de fallo y una prueba».

## Qué conviene ajustar

* **La escala de pesos.** «Bloqueante / importante / a criterio» es la más simple; si usas
  otra, reescribe el registro o el ejecutor se inventará una.
* **La regla «no toques el código ajeno».** En equipos pequeños donde la corrección de quien
  revisa es normal, suavízala: si no, el agente rechazará una petición directa del autor.
* **El ámbito de instalación.** La regla del veredicto encaja en las reglas generales de la
  organización; el resto, en la experiencia de los proyectos que sí tienen revisión.
* **El registro para el autor** (`code-write`) sirve por sí solo: basta él para que no lleguen
  a revisión trazas de depuración ni reformateos accidentales.

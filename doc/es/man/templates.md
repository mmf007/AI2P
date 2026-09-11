# Plantillas

**Una plantilla es el esbozo de un proceso de trabajo: un árbol de tareas con descripciones,
habilidades, orden y experiencia acumulada.** Con una sola acción se despliega de ella un
conjunto nuevo de tareas reales.

La lista se abre con el botón **«Plantillas»** de la barra izquierda; la lista de plantillas de
**este proyecto** existe además como pestaña en la ficha del proyecto.

---

## Para qué sirven

Para lo mismo que las listas de comprobación: el trabajo típico se repite, y formularlo cada vez
de nuevo es lento y sale peor. «Añadir un personaje al juego», «publicar una versión», «rodar un
vídeo» son un árbol de una decena de tareas con las descripciones y los criterios de aceptación
ya escritos.

Pero la plantilla de AI2P tiene un segundo papel, menos evidente, y más importante que el
primero. Un nodo de plantilla tiene su propia **experiencia**: las lecciones obtenidas en pasadas
anteriores de ese proceso se insertan en el encargo de la tarea creada a partir de ese nodo. Es
decir, la plantilla **se vuelve más lista con el uso**: de «formulario para rellenar» pasa a ser
el lugar donde vive el conocimiento sobre cómo hacer bien ese trabajo.

---

## Qué es una plantilla técnicamente

En el sistema no existe una entidad «plantilla» aparte. Una plantilla es **la misma tarea** con
el atributo «plantilla»; sus tareas hijas también están marcadas como plantilla. De ahí se sigue
todo lo demás:

* una plantilla **no se ejecuta**: no aparece en el tablero, ni en la cola de encargos, ni en la
  selección de ejecutores;
* una plantilla puede tener **sin rellenar** el proyecto, el equipo, los ejecutores, el
  responsable;
* tiene los mismos campos que una tarea, incluidos las habilidades, la prioridad, los criterios
  de aceptación, las tareas bloqueantes, las etiquetas, «tiempo ↔ calidad» y «estado al
  terminar»; todo ello **se copia** a la tarea creada.

**Una plantilla con proyecto pertenece al proyecto; una plantilla sin proyecto es común.** Las
listas de selección (el formulario de tarea nueva, el formulario de programación) muestran las
plantillas de su proyecto **y** las comunes; las ajenas no aparecen en ellas.

---

## La ficha de la plantilla

Es la ficha de una tarea con cuatro diferencias:

| Diferencia | Por qué |
|---|---|
| no hay pestaña **«Chat»** | una plantilla no tiene correspondencia |
| no hay pestaña **«Resultado»** | ni resultados tampoco |
| en lugar del botón «Lanzar» está **«Crear tarea»** | una plantilla no se ejecuta, se despliega |
| se han añadido las pestañas **«Experiencia»** y **«Estadísticas»** | eso es precisamente aquello por lo que una plantilla vive mucho tiempo |

### La pestaña «Experiencia»

Los registros de experiencia del nodo y de **todo su subárbol**: código del nodo, habilidad,
etiquetas, texto, quién lo creó y lo modificó y cuándo. Se edita a mano (añadir / modificar /
eliminar), y aquí escribe también el propio agente.

Lo que conviene entender sobre la selección, porque si no la experiencia o bien no llega, o bien
hincha el prompt:

* la **habilidad** del registro significa «este registro sólo lo leerá el ejecutor con esta
  habilidad»; los registros **sin habilidad** son comunes y se muestran con cualquier filtro;
* las **etiquetas** son un segundo filtro independiente (con la regla «o», como en las tareas);
* la casilla **«cargar siempre»** lleva el registro al encargo al margen de la selección; en la
  lista, un registro así está marcado con una ficha de **«siempre»**, la primera en la columna de
  etiquetas;
* todo junto está limitado por el ajuste del proyecto **«límite de inserción de experiencia»**;
  cuando el límite no da para todo, primero se toman los marcados como «siempre» y luego los más
  certeros: **la experiencia del nodo de la plantilla importa más que la del proyecto, y esta más
  que las reglas generales de la organización**.

### La pestaña «Estadísticas»

Los cambios de estado de las tareas **creadas a partir de esta plantilla**: tarea, estado, fecha
y hora, ejecutor. Al pulsar una fila se abre la tarea. Es la forma de ver dónde suele tropezar el
proceso.

---

## Cómo desplegar una plantilla

De dos maneras.

**Desde la ficha de la plantilla**, con el botón **«Crear tarea»**. Se copia toda la plantilla
junto con la jerarquía de tareas hijas, y a las copias se les quita el atributo «plantilla».

**Desde el formulario de tarea nueva**: el botón «añadir» de la lista de tareas pregunta primero
si **tarea vacía** o **a partir de una plantilla**.

Antes de copiar, el sistema vuelve a preguntar:

* la **fecha base**, si la cabecera de la plantilla tiene plazo indicado; los plazos de todas las
  tareas del nuevo proceso se desplazarán a partir de ella;
* la **selección automática de ejecutores**, la misma elección que en el formulario de tarea
  nueva.

Qué ocurre con los vínculos al copiar: las **tareas bloqueantes** de la plantilla apuntan a nodos
de la plantilla, y al desplegarla las referencias se sustituyen por las tareas creadas a partir
de esos nodos (una referencia a un nodo que no se copia se descarta). El **responsable por
defecto** del proyecto se pone sólo en aquellos nodos donde no haya responsable indicado: lo
escrito en la plantilla es más fuerte.

La tercera forma de despliegue es la **programación**: el disparo de una programación crea una
copia de la plantilla por sí solo, según el calendario (véase [Programación](schedule.md)).

---

## Las plantillas también las edita el agente

El agente de IA tiene las acciones `list_templates`, `create_template` y `update_template` sobre
los nodos de plantilla **de su proyecto**: ver la lista, crear un nodo nuevo (título,
descripción, criterios, habilidades, padre), corregir uno existente por su código (un campo que
no se pase no cambia).

Dos reglas que conviene conocer:

* la escritura va **en el servidor propietario del nodo de plantilla**; desde otro servidor
  llegará una negativa comprensible;
* las acciones se cierran con reglas de seguridad, como cualquier otra (`AI2P.Templates.List`,
  `AI2P.Templates.Create`, `AI2P.Templates.Update`).

Un nodo de plantilla se lee también con las herramientas normales de lectura de encargos: en la
ficha está marcado como «NODO DE PLANTILLA».

---

## Detalles que ahorran tiempo

* **Dos vistas de la lista**: tabla y jerarquía, con las mismas ordenaciones y la misma búsqueda
  que la lista de tareas.
* En el **explorador** las plantillas están por dos caminos: dentro del proyecto (la rama
  «Plantillas» del proyecto) y en la rama raíz «Plantillas» → «Proyectos», donde las plantillas
  comunes están directamente en la rama.
* Una plantilla resulta cómodo hacerla **a partir de una tarea que ha salido bien**: copiar su
  descripción y sus criterios a un nodo nuevo mientras se recuerda qué fue lo que funcionó, y
  anotar la lección en la experiencia del nodo.

## Y después

* [Tareas](tasks.md): lo que sale de una plantilla.
* [Proyectos](progects.md): la experiencia del proyecto y el límite de su inserción.
* [Programación](schedule.md): desplegar una plantilla según el calendario.

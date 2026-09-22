# Algoritmo de ejecución de tareas

Este capítulo explica **en qué orden AI2P ejecuta las tareas** cuando se lanza una jerarquía
completa con el botón «Lanzar la jerarquía» (las tres flechas en la ficha de una tarea con
subtareas). También muestra cómo encajan en ese orden las tareas de tipo «Condición», «Bucle
(comprobar antes)» y «Bucle (comprobar después)». Cómo configurar esas tareas y qué muestra el
diagrama se explica en el capítulo [Tareas](tasks.md).

## Lo esencial en tres líneas

* La cola avanza **de abajo arriba**: primero las subtareas más profundas, después sus padres y
  **la raíz al final**. Entre hermanas va primero la de mayor prioridad numérica.
* Un padre se lanza solo cuando **todas sus subtareas han terminado**.
* Las condiciones y los bucles **los decide el agente** a partir de la descripción de la tarea y
  del chat. AI2P no evalúa condiciones por sí mismo: guarda el tipo de tarea, ejecuta la decisión
  del agente y cuenta las vueltas del bucle.

## Qué significa «terminada»

Para la cola, una tarea está **terminada** si está en el estado **«revisión»**, **«terminada»** o
**«cancelada»**. La cola se salta esas tareas y no las vuelve a lanzar.

Para las tareas **bloqueantes** la regla es más estricta: una bloqueante cuenta como terminada solo
en el estado **«terminada»** o **«cancelada»**. Una tarea que espera a una bloqueante en «revisión»
seguirá esperando hasta que una persona acepte el resultado.

## Cómo es una pasada de la cola

Lanzar la jerarquía marca la raíz como «cola abierta». A partir de ahí el sistema hace
**pasadas**: recorre todo el árbol y lanza todo lo que se puede lanzar en ese momento. La pasada
se repite sola:

* cuando termina cualquier tarea de esta jerarquía;
* cuando aparece una subtarea nueva en la jerarquía (por ejemplo, la creó el agente);
* cuando una tarea bloqueante pasa a «terminada» o «cancelada»;
* una vez por minuto, por el vigilante, por si algo quedó libre sin evento (por ejemplo, llegó
  un inicio aplazado).

En una pasada, empezando por la raíz, se hace lo mismo con cada tarea:

1. **Primero los hijos.** Las subtareas se recorren por prioridad descendente y, a igual
   prioridad, por orden de creación. Cada subtarea se recorre con la misma regla, así que la
   cola baja hasta las tareas más profundas. Se recorren **todas** las ramas: los encargos se
   reparten a distintos ejecutores a la vez, no rama por rama.
2. **Después la propia tarea.** Si ya está terminada, se salta. Si no todas sus subtareas han
   terminado, espera. Si no, la cola intenta lanzarla.

Las tareas plantilla y las tareas eliminadas la cola no las ve en absoluto.

### Cuándo una tarea no se lanza

Antes de lanzarla, la cola comprueba la tarea. La tarea **espera** (la cola sigue abierta y
vuelve a ella en la siguiente pasada) si:

* ya se está ejecutando, espera respuesta a una pregunta o está en pausa;
* se **detuvo con error** o pasó a **«corrección»**, y al lanzar no se marcó «Lanzar también las
  tareas detenidas con error» / «Lanzar también las tareas en estado «corrección»». La casilla
  marcada da a esa tarea **un** reintento por cada pulsación del botón;
* no todas sus tareas **bloqueantes** han terminado;
* tiene un **inicio aplazado** (por ejemplo, su ejecutor agotó el límite);
* su ejecutor está **ocupado** con otro encargo y no hay un sustituto libre de la lista «Pueden
  sustituir al ejecutor». Un ejecutor de IA lleva un solo encargo a la vez;
* la tarea pertenece a **otro servidor**: allí se envía una solicitud de lanzamiento, y la cola
  espera a que el resultado llegue por replicación.

Una tarea **se salta** si no tiene asignado exactamente un ejecutor, o si el ejecutor no existe o
está desactivado. Esa tarea nunca arrancará sola y su padre esperará: asigne un ejecutor y la
siguiente pasada la recogerá.

### Cuándo se cierra la cola

* **No hay nada que lanzar ni nada que esperar**: todo el árbol ha terminado. Es el final normal.
* **La raíz se ha entregado** («revisión», «terminada», «cancelada»): la cola llevaba al
  lanzamiento de la raíz, y este ya ocurrió. Quede lo que quede en el subárbol, la cola se cierra.
  Si devuelve la raíz al trabajo, abra la cola de nuevo con el botón.
* **Una detención**: la ordena una tarea condición o bucle (véase más abajo), el agente con la
  acción `stop_hierarchy` o una persona con el botón «detener el lanzamiento de la jerarquía».

## Ejemplo: un árbol lineal

```
T-1 Raíz
├── T-2 Análisis          prioridad 20
│   ├── T-4 Recoger datos prioridad 10
│   └── T-5 Revisión      prioridad 15
└── T-3 Borrador          prioridad 10
```

Orden: T-5 y T-4 (T-5 tiene más prioridad, así que se toma primero; si los ejecutores son
distintos, arrancan las dos a la vez) → T-3 (su rama se recorre en la misma pasada, así que con
un ejecutor libre arranca junto con T-5) → T-2, cuando T-4 y T-5 han terminado → T-1, cuando T-2
y T-3 han terminado.

## Tarea «Condición»

Cuando la cola llega a una condición, **lanza la propia tarea enseguida**, sin esperar a los
hijos, y todavía no entra en ellos.

1. A partir de la descripción y del chat, el agente decide **«Sí»** (`true`) o **«No»** (`false`)
   y comunica la decisión con la acción `set_condition_result` (un agente CLI usa
   `ai2p condition` o el marcador `AI2P_CONDITION`). No hay tercera respuesta.
2. Cuando el encargo de la condición termina, la cola lee la decisión:
   * la **rama no elegida** —la subtarea indicada para la otra respuesta— se cancela junto con
     todo su subárbol (las tareas ya terminadas no se tocan);
   * la **rama elegida** tiene tarea: se ejecuta en el orden normal;
   * la rama elegida no tiene tarea, pero está marcado **«Crear tareas»**: el agente debía crear
     las subtareas antes de entregar (`create_task` o `create_tasks_from_template`), y se ejecutan
     en el orden normal;
   * la rama elegida no tiene tarea y está marcado **«Terminar la ejecución de la jerarquía»**: la
     cola se detiene.
3. Los **demás hijos** de la condición (no indicados en ninguna rama) se ejecutan en el orden
   normal. Tras la condición, la cola sigue por el árbol como siempre.

Si el agente **no comunicó la decisión**, la cola **se detiene**: el sistema no elige la rama por
el agente, decide una persona. Si la condición vuelve a «en espera» o «borrador», la decisión
anterior se olvida y la condición se evalúa de nuevo.

## Tarea «Bucle (comprobar antes)»

La condición se comprueba **antes** de cada vuelta de subtareas.

1. La cola **lanza la propia tarea enseguida**, como una condición: es la tarea analizadora. El
   agente comprueba las condiciones del bucle indicadas en la descripción y comunica el resultado
   con la acción `set_loop_result` (`ai2p loop`, el marcador `AI2P_LOOP`): `true`, hace falta una
   vuelta; `false`, salida.
2. **`true`**: la tarea pasa a pausa con el motivo **«Espera a que termine el ciclo»**, y sus
   subtareas se ejecutan en el orden normal. Mientras tanto, el ejecutor de la analizadora está
   libre y puede tomar subtareas.
3. Cuando **todas** las subtareas han terminado, la vuelta cuenta. La analizadora **se relanza** y
   vuelve a comprobar las condiciones. En una vuelta nueva todo el subárbol vuelve a «en espera» y
   se ejecuta otra vez.
4. **`false`**: el bucle termina, la tarea analizadora pasa a «terminada» y la cola sigue. Si la
   condición no se cumple **ya en la primera vuelta**, las subtareas en «en espera» y «borrador»
   (con todos sus descendientes) se cancelan.

## Tarea «Bucle (comprobar después)»

La condición se comprueba **después** de cada vuelta de subtareas.

1. La cola la trata como a un padre normal: **primero las subtareas**, la analizadora espera.
2. Cuando todas las subtareas han terminado, se lanza la **analizadora** y el agente comunica el
   resultado (`set_loop_result`).
3. **`true`**: todo el subárbol vuelve a «en espera», la tarea pasa a pausa con «Espera a que
   termine el ciclo», y las subtareas hacen una vuelta nueva. Tras la vuelta, la analizadora se
   relanza.
4. **`false`**: el bucle termina, la tarea pasa a «terminada» y la cola sigue.

## Límite de vueltas

Los dos bucles tienen un **límite del bucle**, un campo de la tarea; si está vacío, se usa el
ajuste del proyecto «Rondas de nueva comprobación». Cuando el número de vueltas llega al límite,
el bucle termina como con la respuesta `false`, y la tarea pasa a «terminada». Si la tarea tiene
marcado **«Detener la ejecución de toda la jerarquía al superar el límite»**, se detiene además
toda la cola. Una tarea **lineal** no tiene bucle: aunque su descripción contenga una condición de
repetición, la cola la ejecuta una sola vez.

Si la analizadora de un bucle **no comunicó el resultado**, la cola se detiene, igual que con una
condición.

En una vuelta nueva, las condiciones y bucles anidados **olvidan** sus decisiones anteriores: en
cada vuelta se toman de nuevo.

## Detener y reanudar

* Cuando una condición o un bucle detiene la cola, **no se lanzan tareas nuevas**, y los encargos
  que ya están en marcha terminan su trabajo. En el diagrama esa tarea recibe la señal **STOP**.
* El botón **«detener el lanzamiento de la jerarquía»** cierra la cola; **«detener»** en una tarea
  dentro de la cola pregunta si cerrar también la cola.
* **Para continuar**, pulse de nuevo «Lanzar la jerarquía». Las tareas terminadas se saltan y la
  cola sigue desde donde se detuvo.

## Qué se ve en pantalla

* La raíz de una cola abierta muestra en su ficha el motivo **«lanzamiento de la jerarquía»** y,
  al lado, el botón que detiene la cola.
* Una tarea bucle, durante una vuelta, está en pausa con el motivo **«Espera a que termine el
  ciclo»**.
* En el **diagrama** de subtareas la disposición sigue el orden de la cola, y las transiciones de
  las condiciones y la cuenta de vueltas de los bucles se ven por el color de las flechas y el
  óvalo «hechas / límite»; los detalles, en el capítulo [Tareas](tasks.md).

## Más adelante

* [Tareas](tasks.md): el formulario de la tarea, el campo «Tipo de tarea», el lanzamiento y el
  diagrama.
* [Plantillas](templates.md): cómo tener listo un árbol con condiciones y bucles.
* [Ejecutores](performers.md): quién ejecuta las tareas y cuándo un ejecutor está ocupado.

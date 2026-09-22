# Tareas

**La tarea es la unidad de trabajo y, a la vez, el encargo para la IA.** Esto es lo principal
que conviene entender de AI2P: esa misma descripción que la persona lee con los ojos, el agente
la recibe como prompt. Por eso una tarea se escribe como se le escribiría a un ejecutor de carne
y hueso, y de cómo esté escrita depende el resultado.

La lista se abre con el botón **«Tareas»** de la barra izquierda; la lista de tareas del
**proyecto** existe además como pestaña en su ficha. Al pulsar una fila se abre la
**pestaña-ficha** de la tarea.

---

## Qué guarda una tarea

Además de lo evidente (título, descripción, plazo, estado, prioridad), tiene aquello que mueve
el trabajo por sí solo:

* el **ejecutor**: uno, entre los integrantes del equipo de la tarea; más la lista de
  **«pueden sustituir»** para cuando el asignado esté ocupado (el orden de la lista es el orden
  de preferencia);
* el **responsable**: sólo una persona; a él se dirigen las preguntas del agente y las peticiones
  de confirmación;
* las **habilidades**: qué hay que hacer en la práctica; con ellas funciona la selección
  automática;
* los **criterios de aceptación**: cómo saber que está hecho; se van al agente junto con la
  descripción;
* las **tareas bloqueantes**: sin que terminen, la tarea no se lanza automáticamente;
* las **etiquetas**: palabras libres, sin catálogo: una etiqueta nace por el hecho de haberla
  escrito y desaparece cuando no le queda ninguna tarea;
* el **estado al terminar**: a qué estado pasará la tarea el agente que termine **con
  normalidad**; por defecto, «revisión»;
* **«tiempo ↔ calidad»**: 0.0 «hazlo rápido» … 1.0 «hazlo a conciencia»; se imprime en el encargo
  del agente, y un valor vacío se toma de los ajustes del proyecto en el **momento del
  lanzamiento**.

---

## Cómo mirar la lista

Cuatro vistas, que se alternan en la barra de herramientas de la lista; la elegida se recuerda,
incluso entre arranques.

* **Tablero**: columnas por los estados del catálogo, con el fondo del encabezado en el color del
  estado. La tarjeta muestra una vista previa de la descripción. Las columnas **se reordenan
  arrastrando el encabezado**, y el orden se recuerda por cuenta y proyecto. En cambio, **las
  tarjetas no se arrastran por el tablero en absoluto**: el estado se cambia en la ficha de la
  tarea, con el botón que hay junto al estado.
* **Tabla**: ordenación pulsando en la cabecera de la columna (un segundo clic cambia el
  sentido).
* **Jerarquía**: el árbol por subordinación. Aquí **funciona el arrastre** de la fila con el
  ratón y con el dedo (a la izquierda de la fila está el asa de arrastre) y existe
  «contraer/expandir todo». Sólo se ordena el nivel superior: el orden de las tareas hijas no
  cambia.
* **Etiquetas**: agrupación por etiquetas.

El **filtro** está contraído por defecto y se despliega con el botón del embudo. Si está puesto,
al lado se ve su descripción textual y un botón «X» para limpiarlo todo de una vez. La selección
por etiquetas se suma con un **O** («muéstrame todo lo de la interfaz o lo de la compilación»):
la intersección casi siempre está vacía y parecería una avería.

El orden de las listas en el servidor es **por prioridad numérica descendente**. En ese mismo
orden coge la cola las tareas para trabajar, así que en la lista se ve lo que va a ocurrir.

Una tarea con preguntas del agente sin responder lleva en todas las vistas una ficha bien
visible con **«?»** y la cantidad.

---

## El formulario de la tarea

Tres secciones; las dos de abajo están contraídas al abrirlo.

**General**: título, descripción, responsable, ejecutor y «pueden sustituir».

**Avanzado**: plazo y duración prevista, prioridad (el nivel y el número en una misma línea, de
forma sincronizada), criterios de aceptación, tareas bloqueantes, enlace de importación, el
atributo «no dividir en subtareas» y el **estado al terminar** (que se ve sólo cuando el ejecutor
es una IA).

**Optimización**: lo que gobierna el volumen del encargo y la selección de la experiencia: las
habilidades, las etiquetas, el deslizador «tiempo ↔ calidad» y las dos casillas de **«incluir el
encargo padre en el prompt»** e **«incluir los encargos vecinos en el prompt»**. Ambas están
**desmarcadas** por defecto, y no es un ahorro en menudencias: los bloques del padre y de los
vecinos pesan hasta 60 000 caracteres. El agente no se queda por ello sin contexto: recibe una
línea con el código y el título del padre y lo lee él mismo cuando de verdad hace falta.

Dos cosas que conviene saber del formulario:

* **al pulsar fuera de la ventana no se cierra**: lo tecleado no se pierde; cerrar sin guardar
  sólo se puede con el botón «Cancelar»;
* las **tareas bloqueantes** se buscan con el campo «Búsqueda» que hay al lado: el servidor busca
  a la vez por el título y por la descripción (la descripción es un archivo, por eso quien busca
  es precisamente el servidor). En la lista no están las tareas terminadas ni las canceladas, ni
  la que se está editando; una bloqueante ya elegida se mantiene siempre como opción, porque si
  no, no habría con qué quitarla.

### Tipo de tarea: lineal, condición, bucle

La sección **Avanzado** tiene el campo **Tipo de tarea**. Cada tarea y cada nodo de plantilla
tiene uno de cuatro tipos:

* **Lineal**: una tarea normal, como siempre. Es el tipo por defecto, y lo reciben todas las
  tareas y plantillas creadas antes de que apareciera el campo.
* **Condición**: según el resultado de la tarea se ejecuta una de dos ramas. Para cada rama se
  indica la **tarea si «Sí»** y la **tarea si «No»**; solo se puede elegir una **subtarea directa**
  de esta tarea (por eso una tarea recién creada tiene la lista vacía: cree antes las subtareas).
  Si la rama no tiene tarea, tiene la casilla **Crear tareas** y, cuando está desmarcada, la
  casilla **Terminar la ejecución de la jerarquía**.
* **Bucle (comprobar antes)**: la condición se comprueba antes de cada vuelta de subtareas.
* **Bucle (comprobar después)**: la condición se comprueba después de cada vuelta de subtareas.

Los dos bucles tienen un **límite del bucle** —cuántas vueltas se permiten (una tarea nueva lo
toma del ajuste del proyecto «Rondas de nueva comprobación»; una copia de plantilla, del
nodo de la plantilla)— y la casilla **Detener la ejecución de toda la jerarquía al superar el
límite**.

Los campos aparecen solo para su tipo: una tarea lineal no muestra nada nuevo en el formulario.
Al crear una tarea desde una plantilla, el tipo y todos sus parámetros pasan a la copia, y los
enlaces de las ramas de la condición se apuntan a las tareas creadas a partir de los nodos.

**Qué hace el agente.** Las condiciones y los bucles los evalúa el ejecutor según la
descripción de la tarea y el chat: AI2P no los analiza. Una tarea «Condición» o de bucle
recibe en su encargo un bloque aparte: qué devolver y con qué acción. La decisión de una
condición es estrictamente `true` («Sí») o `false` («No») con `set_condition_result`; el
resultado de la comprobación de un bucle es `true` (otra vuelta) o `false` (salir) con
`set_loop_result`. No hay tercer resultado: «sí», «1» o nada es un error de la acción, y
entonces la ejecución de la jerarquía se detiene; el sistema nunca elige la rama por el
agente. La rama no elegida, las vueltas y el límite del bucle los lleva la propia cola de la
jerarquía. Si la rama elegida no tiene tarea y está marcado «Crear tareas», el agente las crea
antes de entregar, con `create_task` o `create_tasks_from_template` (desde un nodo de
plantilla); y si decide que no se puede seguir en absoluto, finaliza la ejecución de la
jerarquía con `stop_hierarchy`. Un agente CLI hace lo mismo con los comandos
`ai2p condition`, `ai2p loop`, `ai2p from-template`, `ai2p stop-hierarchy` o con los
marcadores `AI2P_CONDITION`, `AI2P_LOOP`, `AI2P_FROM_TEMPLATE`, `AI2P_STOP_HIERARCHY`. Cómo se recorren la condición y los bucles al lanzar la jerarquía se explica en el capítulo [Algoritmo de ejecución de tareas](TaskDo.md).

### La descripción es el prompt

El campo de la descripción (y el de los criterios de aceptación, y el del chat, y el de la
respuesta en el Inbox) es un **editor de Markdown único**: botones de formato, vista previa
editable, inserción de imágenes desde el portapapeles, desde el disco y por dirección, inserción
de vídeo, ancho de la imagen.

Dos botones de este editor merecen mención aparte:

* **«@», la referencia a un objeto del proyecto.** Abre la selección de objeto (con vista previa y
  búsqueda por nombre y número) y pone en la posición del cursor una referencia `@obj:`. En
  **cada** lanzamiento del encargo la referencia se despliega en el pasaporte del objeto y las
  rutas de sus archivos, y por eso no hay que recontar el aspecto del personaje en cada fotograma
  (véase [Proyectos](progects.md)).
* **«Todos los archivos»** (en la cabecera de la ficha): la lista de todos los archivos a los que
  hay referencias en la descripción, los criterios, el chat y los resultados, más los propios
  archivos de los resultados: vista previa, descripción, copia del enlace, descarga y
  eliminación. Sólo está permitido eliminar los archivos propios de la tarea: es la única forma
  de quitar gigabytes de resultados de vídeo.

---

## La ficha de la tarea

Una cabecera de dos líneas: el código y el título, y debajo la barra de herramientas: las fichas
de estado, plantilla, servidor, «quién tiene el encargo», «ocupado hasta», el contador de
preguntas, y después los botones de icono: lanzar/detener, modificar, división automática,
eliminar, todos los archivos, enlace a la tarea, actualizar, «a la tarea padre».

Pestañas: **Descripción**, **Subtareas**, **Chat**, **Resultado**, **Encargos**, **Historial**.

Mientras haya un encargo activo sobre la tarea, la ficha **se relee sola** cada 2 segundos.

### Detrás del estado hay un motivo

El estado «pausa» por sí solo no explica nada, así que junto a él el sistema escribe el motivo
con las mismas palabras que en la vista «en manos de la IA»:

| Marca | Qué ha ocurrido |
|---|---|
| **«preguntas en el chat: N»** | el agente ha preguntado a una persona o al agente de una tarea vecina |
| **«Espera hasta <hora>»** | al ejecutor se le ha agotado el límite y el arranque se ha aplazado: la tarea se lanzará sola |
| **«espera la ejecución de las subtareas»** | la tarea se ha repartido en subtareas |
| **«lanzamiento de la jerarquía»** | sobre la tarea hay abierta una cola de lanzamiento jerárquico |
| **«espera las tareas bloqueantes»** | no todas las bloqueantes están en estado «terminada» |
| **«tarea bloqueante cancelada»** | el trabajo que la tarea esperaba no va a existir: decide la persona |

### La pestaña «Encargos»

La lista de encargos (cada lanzamiento del agente es un encargo) y debajo la **consola del
encargo seleccionado**: la salida en vivo, línea a línea, de lo que el ejecutor está haciendo
**ahora mismo**. En los modelos de medios allí se vuelca la consola de ComfyUI con el progreso de
la generación; en los de texto, el arranque, cada llamada a una herramienta y el resultado; en
los modelos locales, además, la salida del propio servidor del modelo. Es precisamente por la
consola por donde se ve si el trabajo avanza o se ha parado.

El búfer es de las 1000 últimas líneas por encargo y vive en memoria: tras reiniciar la
aplicación, la consola de los encargos terminados no se restaura. El historial permanente está
en la pestaña «Historial» y en los resultados.

### La pestaña «Chat»: ahí mismo habla usted con el agente

El chat no son comentarios, sino un canal de comunicación. Tres cosas por las que existe:

* **el agente pregunta.** La pregunta está destacada con un marco y el icono «?», y puede tener
  botones con opciones de respuesta y un campo de respuesta libre. Mientras no haya respuesta, la
  tarea está en pausa y la pregunta se ve en el Inbox. Si se responde, el trabajo continuará
  **desde el mismo punto**.
* **usted interrumpe al agente.** En una tarea en curso, encima del campo de entrada hay un
  rótulo: el mensaje irá al ejecutor **justo en mitad de su trabajo**; él se interrumpirá, lo
  leerá y responderá ahí mismo. No hay que pulsar nada más.
* **la pregunta se puede retirar.** Si el encargo que hizo la pregunta ya no espera respuesta (se
  ha caído, ha terminado, se ha relanzado), no hay quien responda; entonces, en lugar de los
  campos de respuesta está el botón **«retirar la pregunta»**. La pregunta desaparece del
  contador, del Inbox y del motivo de la pausa de inmediato, y en la conversación queda con el
  rótulo honesto «Pregunta retirada: no habrá respuesta».

---

## Cómo se lanzan las tareas

**A mano**, con el botón «lanzar» de la ficha. Para la IA es el arranque del agente; para una
persona, un encargo en su Inbox. Cuando hay un encargo activo, en su lugar está «detener».

**Automáticamente**: cuando termina el padre (paso a «revisión», «corrección» o «terminada») se
lanzan sus tareas hijas directas con el tipo de lanzamiento «automáticamente»; cuando la última
bloqueante pasa a **«terminada»**, arrancan enseguida las tareas que la esperaban.

**Por programación**: véase [Programación](schedule.md).

**Toda la jerarquía**, con el botón de las tres flechas de una tarea con subtareas. La cola va
**de abajo arriba**, desde las más profundas y prioritarias; los ejecutores ocupados esperan su
turno, las subtareas ya hechas se omiten y la propia tarea se lanza la última. En la
reconfirmación hay dos casillas independientes: **«lanzar también las tareas detenidas con
error»** y **«lanzar también las tareas en estado corrección»**; ambas están desmarcadas por
defecto y no se recuerdan entre pulsaciones, porque es una decisión de aquí y ahora, no un ajuste
de la ficha. Un intento por lanzamiento: una tarea que vuelva a caer en corrección ya no la coge
la cola y se le deja a la persona.

Mientras la cola está abierta, al lado aparece el botón de **«detener el lanzamiento de la
jerarquía»**. Y el botón «detener» de una tarea que está **dentro** de una cola abierta vuelve a
preguntar qué hay que detener exactamente: todo junto con las tareas hijas, o sólo esta tarea y
la cola. Sin esa reconfirmación, la propia cola habría vuelto a levantar en la siguiente pasada
el encargo retirado.

El orden de la pasada paso a paso, y las condiciones y los bucles en la cola, se explican en el capítulo [Algoritmo de ejecución de tareas](TaskDo.md).

**«Desactivar el arranque automático de subtareas»** es un botón que está ahí mismo. La marca se
aplica a la tarea **y a todo su subárbol** y cierra los tres arranques automáticos (el de las
hijas, el de las que esperaban a una bloqueante y el de la cola de división automática). No
limita el lanzamiento manual, y «lanzar la jerarquía» **la quita**: la persona ha dicho
expresamente «corre». Si el arranque automático está desactivado más arriba en el árbol, en la
ficha hay un icono con una indicación de en qué tarea está desactivado: callárselo no se puede,
porque si no saldría un «lo he pulsado y no pasa nada».

### Cuando al ejecutor se le acaba el límite

Al pulsar «lanzar» se pregunta primero el saldo. Si queda poco, se abre la ventana **«El límite
del ejecutor está casi agotado»**: cuánto se ha consumido, cuándo se liberará la ventana y
cuatro salidas: **aplazar el arranque** (la tarea se lanzará sola, el ordenador se puede
apagar), **dividir en subtareas** (una parte se ejecutará con el saldo restante), **lanzar
ahora** a la fuerza, y cancelar.

Si el límite ha cortado un encargo ya en marcha, la tarea no «se detiene con error», sino que
pasa a «en espera» con el arranque aplazado; al llegar el momento se lanza como un **encargo
nuevo, desde el principio**: lo que el agente ya haya hecho en los archivos del proyecto no
desaparece.

---

## Subtareas

La pestaña «Subtareas» son las hijas de esta tarea: ID, título, estado, prioridad numérica,
ejecutor, plazo. El estado se cambia directamente en la fila. El botón «+» abre la misma
elección de «tarea vacía o plantilla» que «nueva tarea»: la vacía se crea con el padre ya puesto,
y la plantilla elegida **se despliega como subtarea** y se traslada entera al proyecto del padre.

La **división automática** es un botón de icono aparte en la cabecera (visible mientras esté
desmarcado el atributo «no dividir» y no haya encargo activo). El agente divide él mismo el
trabajo en subtareas, indicando a cada una sus habilidades y su prioridad numérica, y a partir
de ahí las lleva el orquestador: las subtareas se lanzan por prioridad descendente, cada ejecutor
de IA lleva **un encargo cada vez**, y el final de cualquier subtarea libera al ejecutor y lanza
la siguiente.

La primera llamada marca la tarea como dividida, así que ya no se volverá a dividir.

### Diagrama: condición y ciclos

En el diagrama de subtareas (la tercera vista de la pestaña) las tareas de tipo «Condición» y
«Ciclo» se reconocen por la forma, y su avance por el color. Solo se dibuja lo que el sistema
sabe con certeza: el tipo de tarea, las ramas indicadas en ella, la transición realizada y el
número de vueltas. Nada se adivina por el texto de la descripción.

* **Condición** — un triángulo encima del rectángulo de la tarea y otro debajo. Del superior
  sale la flecha de la rama «Sí», del inferior la de «No». Mientras la transición no se ha
  hecho, ambas flechas son **amarillas**; después, la de la rama recorrida es **verde** y la
  otra **gris**. Si una rama no tiene tarea y está marcado «terminar la ejecución», su flecha
  lleva a una señal redonda **STOP** rojo oscuro, con el color según las mismas reglas.
* **Ciclo antes** — el marco de la tarea se repite dos veces abajo y a la derecha; **ciclo
  después** — arriba y a la izquierda. El óvalo abajo a la derecha muestra vueltas hechas /
  límite de vueltas (el de la tarea o, si no lo tiene, el del proyecto). Si la ejecución de
  toda la jerarquía se detuvo en el ciclo, a su derecha aparece la señal **STOP** con una
  flecha roja.
* Una tarea **lineal** se ve como antes.

---

## Una tarea en otro servidor

En un clúster, la tarea tiene un **servidor propietario**: allí se edita y allí se ejecutan sus
encargos. En los demás servidores se ve en modo lectura, pero **«lanzar», «lanzar la jerarquía» y
el chat sí están disponibles**: el lanzamiento se va al propietario como solicitud y el mensaje
llegará por replicación; las indicaciones lo dicen expresamente. La edición, el cambio de estado,
la eliminación, la división y la retirada de una pregunta son sólo del propietario. Los detalles,
en [Varios servidores](servers.md).

---

## Detalles que ahorran tiempo

* **Una tarea llegada de un sistema externo** muestra en la cabecera el enlace de importación, y
  su botón «actualizar» vuelve a preguntar: releer **desde la fuente** (título, plazo, descripción
  y mensajes nuevos de la discusión) o sólo aquí.
* **«Trabaja: <alias>»** aparece junto al ejecutor sólo cuando quien trabaja no es el asignado;
  cada sustitución así se escribe además como mensaje en el chat.
* **El enlace a la tarea** (botón de la cabecera) abre una ventana con la dirección local y la
  externa y botones de copia.
* **La lista de tareas se relee sola** ante cualquier cambio: una tarea creada en otro sitio de
  la interfaz aparece en el tablero sin pulsar «actualizar».
* **Una tarea no se puede trasladar entre proyectos**: sus archivos están en la carpeta de su
  proyecto. El sistema dirá «cambie primero el proyecto de la tarea»; en el árbol general de
  «todas las tareas» las filas vecinas suelen ser de proyectos distintos, y el intento de
  arrastre parece un gesto que no funciona.

## Y después

* [Algoritmo de ejecución de tareas](TaskDo.md): el orden del lanzamiento de toda la jerarquía.
* [Proyectos](progects.md): la carpeta, los objetos, la experiencia y los ajustes que influyen en
  las tareas.
* [Plantillas](templates.md): para no teclear dos veces el mismo árbol de tareas.
* [Ejecutores](performers.md): quién ejecutará la tarea y a qué precio.
* [Programación](schedule.md): el lanzamiento según el calendario.

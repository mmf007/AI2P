# Objetos del proyecto

**Un objeto es aquello a lo que se refiere una tarea:** el personaje de un clip, una
localización, un accesorio, un estilo, un fotograma de referencia, un adaptador LoRA. Cada
proyecto tiene su propia lista de objetos; la organización no tiene una lista general de objetos,
y es a propósito: un objeto pertenece a un proyecto.

La lista se abre con la pestaña **«Objetos»** de la ficha del proyecto. Al pulsar en una fila se
abre la **pestaña «Objeto»**: una ficha con pestañas, igual que la de una tarea.

---

## Para qué sirven

Se explica del modo más sencillo con el personaje fijo de un vídeo. Si se describe el aspecto del
héroe con palabras propias en cada fotograma, «se irá desdibujando» de un fotograma a otro: **el
recuento es precisamente la causa**.

Por eso el objeto tiene un **pasaporte** —el texto literal para el prompt— y archivos de
referencia, y en la descripción de la tarea se pone una **referencia `@obj:OBJ-3`**. En **cada**
lanzamiento del encargo la referencia se despliega en el pasaporte y las rutas de los archivos:
si se corrige el pasaporte, el siguiente fotograma ya tiene en cuenta la corrección, y no hay que
reescribir las tareas.

Tres reglas sobre la referencia que ahorran tiempo:

* se leen **ambas** formas, `@obj:OBJ-3` y `@obj:[Héroe Vasya]`; cuál de ellas pone en el
  portapapeles el botón de la interfaz lo fija el ajuste del proyecto «formato de la referencia al
  objeto»;
* una referencia desconocida se queda en el texto tal cual, y una referencia **no alcanza a un
  objeto de otro proyecto**;
* una referencia dentro de un pasaporte no se despliega: no hay anidamiento.

## Tipos de objeto

| Tipo | Para qué |
|---|---|
| **personaje** | el héroe del clip; su aspecto está en el pasaporte y en los fotogramas de referencia |
| **localización** | el lugar de la acción |
| **accesorio** | un objeto en el fotograma |
| **estilo** | la manera de dibujar, las restricciones del acabado |
| **fotograma de referencia** | una imagen modelo; normalmente hijo de un personaje o de una localización |
| **grabación de referencia** | una muestra de voz o de sonido: el conector pasa su archivo al modelo igual que un fotograma de referencia |
| **conjunto de datos** | carpeta de fotogramas con descripciones para entrenar un adaptador; los fotogramas son sus hijos |
| **adaptador LoRA** | un añadido entrenado a los pesos del modelo ([Editor de LoRA](LoRAEditor.md)) |
| **archivo** | un archivo al que se refieren las tareas |
| **equipamiento** | el hardware ligado al trabajo |
| **recurso multimedia** | una toma grabada, una pista de audio, un subtítulo, un archivo de proyecto de montaje: la mediateca del proyecto es una selección de objetos de este tipo |

## La lista de objetos

**Cuatro vistas**: tabla (con cuadros de vista previa), tabla breve, jerarquía y etiquetas; la
elegida se recuerda por proyecto. Hay filtros por tipos y por etiquetas, y búsqueda.

* **la jerarquía está hecha con el mismo motor que el árbol de tareas**, así que en ella
  funcionan el arrastre con el ratón y con el dedo, el desplazamiento automático en los bordes y
  la franja adhesiva **«A la raíz»**;
* en un objeto **sin imagen propia** (personaje, localización, estilo) la vista previa se toma de
  sus hijos hacia abajo: el aspecto está en los fotogramas de referencia;
* el filtro selecciona **filas**, no lo que la fila muestra: la selección «sólo personajes» no
  deja al personaje sin cara;
* al final de la fila están los botones «editar» (lápiz) y «eliminar»: editar desde la lista
  sigue siendo un solo movimiento, aunque al pulsar en la fila se abra la pestaña;
* **los objetos tienen etiquetas propias**, un grupo aparte de las etiquetas de las tareas.

## La pestaña del objeto

El objeto se **lee en una pestaña**, y se crea y se edita en un formulario de ventana.

* **La cabecera** es como la de la ficha de una tarea: el rótulo con el nombre del proyecto, el
  código `OBJ-N` y el nombre; debajo, el tipo del objeto, la marca «inactivo» y el estado del
  adaptador.
* **El título de la pestaña** es «Objeto» más el código o el comienzo del nombre; se calcula con
  el **mismo** ajuste que en la tarea y en el proyecto (Ajustes → General, un ajuste para todos).
* **La barra de herramientas**, en iconos: editar (abre el formulario de ventana), cambiar de
  servidor, referencia, configuración de LoRA (sólo en un adaptador), «qué irá al modelo»,
  eliminar, actualizar. Cada uno es la misma acción que el botón de dentro del formulario.
* **Hay dos pestañas**: «General» (los mismos campos, sólo lectura, el pasaporte dibujado como
  Markdown) y **«Subobjetos»**: la lista de los hijos con vistas previas, los botones «editar» y
  «eliminar» al final de la fila, un clic abre una pestaña nueva, y el botón «añadir» crea un
  objeto directamente dentro del abierto.

## El formulario del objeto

Nombre, tipo, padre («forma parte del objeto»), etiquetas, archivo de referencia, pasaporte,
«activo», y en un objeto del tipo «adaptador LoRA», la sección del adaptador y el botón
**«Configuración de LoRA»** ([Editor de LoRA](LoRAEditor.md)).

El botón del diálogo de archivos se mueve **sólo dentro de la carpeta del proyecto** y devuelve la
ruta relativa a ella. El nombre del objeto es único dentro del proyecto.

### Dos referencias, y son cosas distintas

El formulario da **dos** referencias en dos campos:

* **`@obj:OBJ-3`**, para insertarla en la descripción de una tarea; se despliega en el pasaporte
  al lanzar el encargo;
* **la dirección de la pestaña** `…/object/{id}`, para enlaces externos. El objeto se direcciona
  por su uuid, así que ese enlace funciona **entre proyectos** y no cambia el proyecto actual.
  Hay dos direcciones, como en la tarea: la local y la externa.

### «Qué irá al modelo»

El botón muestra el despliegue completo, exactamente lo que recibirá la generación. En un
**adaptador LoRA** los datos son dos, y distintos, así que la ventana tiene dos campos:

* **«Al usarlo»**: el pasaporte con las rutas de los archivos; esto es lo que va al prompt;
* **«Al entrenar»**: los fotogramas del conjunto de datos actual con sus descripciones,
  exactamente lo que recibirá el entrenador. Un fotograma sin descripción se ve como una línea
  vacía, antes de que el cálculo lleve horas.

Un objeto corriente no tiene el segundo campo.

## El servidor propietario del objeto

En un clúster el objeto, igual que la tarea, tiene **exactamente un servidor propietario**: en él
se edita y en él se entrena su adaptador LoRA. En la ficha hay un rótulo «Objeto del servidor …»
y en la barra de herramientas el botón **«Cambiar de servidor»** (el rótulo y el botón sólo se
ven cuando la organización tiene más de un servidor).

* **en un servidor ajeno el objeto es de sólo lectura**: están apagados la edición, la
  eliminación, la configuración de LoRA, la creación de subobjetos, los botones de la fila de la
  lista y el asa de arrastre del árbol;
* el objeto se traslada **con todo su subárbol**: subobjetos, conjuntos de datos y fotogramas,
  que son el contenido del objeto;
* **el número no cambia** al cambiar de servidor, y un objeto nuevo lo recibe con el código del
  servidor (`OBJ-5-S1`; en el director no hay sufijo); si no, dos servidores crearían sendos
  `OBJ-5` y la referencia `@obj:OBJ-5` apuntaría a cosas distintas;
* un objeto nuevo recibe su servidor solo: un hijo, el del padre; uno de nivel superior, este
  servidor;
* los objetos que llegan sin servidor desde un socio de una versión anterior los recoge el
  director al abrir la organización.

**El adaptador entrenado viaja al vecino.** Una copia del archivo LoRA terminado se coloca en la
carpeta de datos de la organización, y esa carpeta se replica entera; en el servidor receptor el
archivo se encuentra solo y, antes de la generación, se coloca en el repositorio de modelos de
ese servidor. Es decir, «entrenado en un servidor, usado en otro» funciona sin copiar a mano. Los
adaptadores entrenados antes de la actualización no se trasladan: la copia aparece en los
entrenados después.

Más sobre la propiedad de las filas y la replicación: [Varios servidores](servers.md).

## Y después

* [Proyectos](progects.md): dónde viven los objetos y qué ajustes del proyecto les afectan.
* [Tareas](tasks.md): dónde se pone la referencia `@obj:`.
* [Editor de LoRA](LoRAEditor.md): el entrenamiento de un adaptador a partir de un objeto del
  proyecto.
* [Vídeo a partir del fotograma de referencia de un personaje](sample_video1.md): un ejemplo
  completo con un objeto.

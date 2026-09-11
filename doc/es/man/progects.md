# Proyectos

**El proyecto es el contenedor de todo el trabajo:** tareas, plantillas, objetos, reglas de
seguridad, experiencia y —lo más importante— la **carpeta del disco** en la que el agente de IA
lee y escribe archivos.

La lista se abre con el botón **«Proyectos»** de la barra izquierda. Es también lo primero que
ve quien todavía no ha elegido proyecto. Al pulsar en una fila se abre la **pestaña
«Proyecto»**, la ficha con sus solapas; el proyecto elegido se recuerda y se pone en todo lo
nuevo.

---

## Para qué sirve

El proyecto responde a preguntas que, de otro modo, habría que repetir en cada tarea:

* **dónde están los archivos**: la carpeta del proyecto; sin ella al agente no se le dan las
  herramientas de archivos en absoluto;
* **quién trabaja**: el equipo del proyecto, y de él el círculo de ejecutores de la tarea;
* **cómo elegir ejecutor**: el deslizador «precio ↔ calidad»;
* **qué sabe ya el agente**: la experiencia del proyecto, que se inserta en cada encargo;
* **qué no puede hacer el agente**: las reglas de seguridad del proyecto.

## La carpeta del proyecto: por qué es propia de cada servidor

El proyecto entero se replica entre los servidores del clúster, pero **la carpeta es propia de
cada ordenador**: en uno `D:\work\game`, en otro `~/projects/game`. Por eso la ruta se guarda en
una **fila aparte por cada servidor** y se edita sólo desde ese servidor. En el formulario del
proyecto las carpetas de los demás servidores se muestran en modo lectura: se ve dónde está ya
desplegado el proyecto.

De esa misma regla se sigue algo que a veces sorprende: **el atributo «activo» también es propio
de cada servidor** (en el formulario lo pone tal cual: «activo en este servidor»), y un proyecto
que aquí no tiene carpeta indicada no se puede activar aquí. De lo contrario las tareas se
lanzarían «a ninguna parte».

La ruta se puede escribir desde la carpeta personal (`~/work/proyecto`): la «~» la expanden tanto
el diálogo de selección de carpeta como el guardado; en la base se guarda ya expandida, porque la
reciben las herramientas del agente, la caja de arena del CLI y la replicación de archivos.

### La carpeta `Common`

Es una ruta **dentro** de la carpeta del proyecto, cuyo contenido se replica entre servidores:
una forma rápida de pasarle al vecino los resultados, sobre todo los de medios. Se configura en
cada servidor por separado; vacío significa que no se replica.

Aquí la ruta es **sólo relativa** (`media`, `doc/common`): una ruta completa, `~/…` y la salida
hacia arriba `..` se rechazan con un error claro al guardar. Dentro funciona el filtro
**`.repignore`**, con la sintaxis de `.gitignore`, que se edita con el botón del filtro que hay
junto al campo.

### El nombre y la carpeta de almacenamiento

El nombre del proyecto es único (guardar con un nombre ocupado se bloquea) y se propone a partir
de la ruta de la carpeta. En cambio, la carpeta del **almacenamiento de archivos** del proyecto
se llama con su código externo (`projects/PRJ-3`), y renombrar el proyecto no la toca: los
archivos y las rutas relativas se quedan en su sitio.

---

## Pestañas de la ficha

### General

El formulario del proyecto: la carpeta, `Common`, el nombre, el equipo, «activo», los
deslizadores de los ajustes. Se abre **en modo lectura**: la edición se activa con el lápiz de
la esquina superior derecha, y en su lugar aparece «guardar».

Los ajustes que viven aquí merecen la pena entenderse:

| Ajuste | Qué hace |
|---|---|
| **precio ↔ calidad** | 0.0: la selección automática coge a los más baratos; 1.0: a los mejores; por defecto, 0.5 |
| **tiempo ↔ calidad** | el valor por defecto del campo homónimo de la tarea: 0.0 es «hazlo rápido, la calidad puede resentirse»; 1.0, «no escatimes tiempo»; por defecto, 0.5 |
| **límite de inserción de experiencia** | cuántos caracteres de experiencia irán como máximo al encargo, sumando las reglas generales, la experiencia del proyecto y la del nodo de la plantilla; por defecto, 100 000 |
| **rondas de nueva comprobación** | cuántas veces una tarea de ejecución de pruebas puede devolver a sus vecinas a corrección y esperarlas; por defecto, 3 |
| **responsable por defecto** | la persona del equipo que el formulario pondrá en una tarea nueva; se puede dejar vacío |
| **formato de la referencia a un objeto** | qué pone en el portapapeles el botón de la interfaz: `@obj:OBJ-3` o `@obj:[Héroe Vasia]`. **Ambas** formas se leen siempre |

Dos matices sobre el «responsable por defecto»: lo pone el **formulario**, no el servidor, así
que una tarea creada al margen del formulario (una subtarea del agente, una importación) no
recibe responsable; y cambiar de equipo quita al responsable que no forme parte del equipo
nuevo.

### Tareas

La misma lista de tareas (tablero / tabla / jerarquía, filtro, búsqueda), filtrada por este
proyecto. Una tarea nueva creada desde aquí recibe **este** proyecto y su equipo. La vista, la
ordenación y el filtro se recuerdan, incluso entre arranques. Sobre la tarea en sí:
[Tareas](tasks.md).

### Equipo

Los integrantes del equipo del proyecto con sus estados de trabajo y los botones
«iniciar»/«detener», los mismos que en la lista de equipos (véase [Equipos](teams.md)), sólo que
aquí, a mano. Dos vistas: jerarquía (por defecto) y tabla.

### Objetos

La lista de objetos de **este** proyecto: personajes, localizaciones, accesorios, estilos,
fotogramas de referencia, adaptadores LoRA. La organización no tiene una lista general de
objetos, y es a propósito: un objeto pertenece a un proyecto. El objeto guarda un **pasaporte**
—el texto literal para el prompt— y en la descripción de la tarea se pone una **referencia
`@obj:OBJ-3`** que en cada lanzamiento del encargo se despliega en ese pasaporte y en las rutas
de los archivos de referencia.

Todo lo demás —los tipos de objeto, las cuatro vistas de la lista, la pestaña del objeto y sus
subobjetos, las dos referencias, «qué irá al modelo» y el servidor propietario— tiene un capítulo
propio: **[Objetos del proyecto](objects.md)**.

### Seguridad

Las reglas de seguridad de **este proyecto**: qué se le permite al agente, qué exige la
confirmación de una persona, qué está prohibido. Las reglas del proyecto precisan las de la
organización (Ajustes → Seguridad), y las de la tarea, las del proyecto.

### Plantillas

La misma lista de plantillas que la general, pero limitada a este proyecto; el botón «nueva
plantilla» pone el proyecto solo. Los detalles, en [Plantillas](templates.md).

### Experiencia

La **experiencia del proyecto** es la generalización del trabajo que recibe **cualquier** tarea
suya. La tabla: habilidad, texto, quién la creó y la modificó y cuándo. El filtro por habilidades
es múltiple, y los registros **sin habilidad se muestran con cualquier filtro**: son comunes.

La habilidad de un registro significa literalmente «este registro sólo lo leerá el ejecutor con
esta habilidad», y es la principal herramienta contra el hinchamiento del prompt: una lección
estrecha no debe irle a todos. Los registros de experiencia los escribe también el propio agente,
cuando averigua algo importante para tareas futuras.

Tres niveles de experiencia, de lo general a lo particular: las **reglas generales de la
organización** (Ajustes → Experiencia general) → la **experiencia del proyecto** (aquí) → la
**experiencia del nodo de la plantilla** ([Plantillas](templates.md)). Cuando todo junto no cabe
en el límite, la selección va por lo certero: el nodo de la plantilla importa más que el
proyecto, y el proyecto más que las reglas generales.

### Historial

El mismo registro de trabajos que la pantalla general «Historial de trabajos», pero limitado
estrictamente a este proyecto: filtros por ejecutor y tipo de evento; al pulsar el código de la
tarea se abre su ficha, y al pulsar la celda «Detalles» se muestra el evento entero con un botón
de «copiar». Aquí llegan también la puesta en marcha del trabajo del equipo y el resultado de la
conexión de cada integrante con el texto del error.

---

## Detalles que ahorran tiempo

* **El proyecto actual** se cambia en la barra superior («Proyecto: …»), no con un botón
  «elegir» de la lista. Él determina adónde irá una tarea nueva.
* Una misma persona trabaja con varios proyectos, y por eso la lista de proyectos se ha quedado
  siendo una simple lista, mientras que el trabajo va en pestañas.
* **Un proyecto no activo** no se pone en tareas nuevas.
* El título de la pestaña tiene dos líneas: el tipo («Proyecto») y, o bien el principio del
  nombre, o bien el código corto (`PRJ-2`); se cambia en Ajustes → General.

## Y después

* [Tareas](tasks.md): aquello por lo que se crea un proyecto.
* [Objetos del proyecto](objects.md): personajes, localizaciones, estilos y adaptadores LoRA.
* [Equipos](teams.md): quién trabaja en el proyecto.
* [Plantillas](templates.md): cómo desplegar un proceso típico en este proyecto.
* [Editor de LoRA](LoRAEditor.md): el entrenamiento de un adaptador sobre un objeto del proyecto.

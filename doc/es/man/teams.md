# Equipos

**Un equipo es el círculo de ejecutores del que se elige el ejecutor de una tarea.** No es un
departamento ni un chat: es la lista que responde a la pregunta «¿a quién se le puede encargar
este trabajo, en general?».

La lista se abre con el botón **«Equipos»** de la barra izquierda (y con la entrada del menú del
mismo nombre).

---

## Para qué sirve

Tres cosas por las que existe un equipo:

1. **Limita la elección.** Una tarea tiene un equipo (que se toma del proyecto), y su ejecutor se
   elige **sólo entre sus integrantes**, tanto a mano como por selección automática. Así el
   trabajo no acaba en manos de quien no sabe nada de ese proyecto.
2. **Fija el idioma de los agentes.** El equipo tiene un **idioma de comunicación de los
   agentes**, y puede ser distinto del idioma de la interfaz. El campo funciona literalmente: en
   el idioma del equipo el agente recibe **todo el texto que se le dirige**: el prompt del
   sistema, el encargo, las descripciones de las herramientas, las respuestas e incluso las
   negativas de las reglas de seguridad. Lo que lee la persona sigue en el idioma de la
   instalación.
3. **Mantiene las conexiones.** El equipo tiene botones de «iniciar» y «detener» el trabajo: el
   arranque levanta a los integrantes de IA (comprueba las claves y, si hace falta, arranca los
   servidores locales de los modelos), y la detención los apaga.

---

## Composición

Cada fila de la composición es un ejecutor más cuatro cosas:

| Campo | Para qué |
|---|---|
| **Rol profesional** | del catálogo de roles (director de arte, programador, tester…). Es el rol **en este equipo**: una misma persona puede ser distinta en equipos distintos |
| **Responsable** | otro integrante de ese mismo equipo; vacío significa nivel superior |
| **Jefe de equipo** | quién manda cuando el nivel superior no es uno solo |
| **Activo en este equipo** | su propia actividad, aparte de la actividad del ejecutor en general |

### Jerarquía y jefe de equipo

La jerarquía no es un adorno del organigrama. Responde a la pregunta **«a quién se le pueden
encargar tareas sin un permiso aparte»**: el superior encarga a sus subordinados por su cuenta.
Esto es especialmente importante cuando a una IA le están subordinadas otras IA. Los ciclos
están prohibidos: el núcleo no los deja pasar y el formulario los resalta.

El jefe de equipo es uno por equipo, y sólo puede serlo un integrante del nivel superior; si
arriba hay un único ejecutor, se le marca como jefe de equipo automáticamente. En un
subordinado el atributo no está disponible.

### Dos actividades, y no es una duplicación

* La **actividad del ejecutor** (en el catálogo de ejecutores) significa «trabaja, en general».
* La **actividad de la participación** significa «trabaja **en este** equipo».

Un integrante se considera activo sólo cuando están puestas las dos. Un integrante desactivado
en el equipo no se conecta al arrancarlo, no se levanta con el botón individual y **no se elige
por selección automática**, pero en otros equipos sigue activo. Es con esto con lo que se apaga
a un ejecutor en un proyecto sin tocar los demás.

---

## Dos vistas de la lista

* **Tabla**: los equipos en filas, con el jefe de equipo marcado con un asterisco junto al
  alias.
* **Jerarquía**: un bloque por equipo, con los integrantes en árbol según la subordinación.

Se alternan con el botón que hay encima de la lista; la vista elegida se recuerda.

---

## Puesta en marcha del trabajo del equipo

Los botones «iniciar»/«detener» existen **para el equipo entero** y **para cada integrante de IA
por separado**. En una persona no los hay: no se «conecta», su estado se calcula solo.

Cada integrante muestra un estado en color:

| Estado | Qué significa |
|---|---|
| **no conectado** | el trabajo del equipo no se ha iniciado o el integrante ha sido detenido |
| **conectándose** | se está comprobando; en un modelo local eso son **minutos**: los pesos se cargan del disco |
| **conectado** | la clave se ha aceptado y el modelo responde |
| **error de conexión** | no responde; el texto del error está en la indicación emergente |
| **no activo** | el integrante está desactivado, en el catálogo o en este equipo |

Mientras alguien se está conectando, los estados se actualizan solos hasta el final de la
conexión.

**El texto del error se puede leer entero y copiar**: la etiqueta de estado con error se puede
pulsar y abre una ventana con el texto completo y un botón de «copiar al portapapeles». No es
un detalle menor: en el error están el código de salida del proceso, las últimas líneas de la
salida del modelo y el propio comando de arranque, es decir, todo con lo que ese error se
analiza.

### Cuándo hace falta el arranque individual

Dos casos, y ambos frecuentes: se ha añadido un integrante a un equipo **que ya está
funcionando** (no hay por qué reiniciarlos a todos) y un integrante **se ha caído con error**
(hay que levantar sólo a ese). El arranque individual hace exactamente lo mismo que el general,
pero para un solo ejecutor, y no toca el atributo «el equipo está funcionando».

### Servidores locales de modelos

Si el modelo de un integrante tiene indicado un comando de arranque, al conectarse el sistema
**levanta él mismo el servidor del modelo como proceso del SO** y espera a que responda (una
prueba cada 5 segundos, con un límite de 10 minutos). Reglas que conviene conocer:

* **un servidor que ya responde se considera externo**: no se arranca una segunda instancia y al
  detener no se le toca, porque puede haberlo levantado usted a mano;
* **un comando de arranque, un proceso** para todos los integrantes y todos los equipos; se
  apaga cuando han detenido el trabajo todos los que lo usaban;
* al detener la aplicación se descargan todos los servidores que ella ha levantado.

Si el servidor del modelo ha muerto durante el arranque, en el error aparecen la interpretación
del código de salida, el tiempo de vida del proceso y la cola de su salida; las causas típicas
(un controlador NVIDIA antiguo con una compilación CUDA de torch, falta de memoria de vídeo) se
añaden con palabras.

---

## Detalles que ahorran tiempo

* **El nombre del equipo es único**: guardar con un nombre ocupado se bloquea.
* **Un equipo no activo** no se pone en proyectos ni tareas nuevos, y su arranque no está
  disponible.
* Un encargo lanzado con el botón **desde la ficha de la tarea** también se refleja en los
  estados del equipo: no hay que reiniciar el equipo a propósito para tener la lista al día.
* La composición del equipo se ve y se edita también en la pestaña **«Equipo»** de la ficha del
  proyecto, allí mismo donde usted suele trabajar (véase [Proyectos](progects.md)).

## Y después

* [Ejecutores](performers.md): de quién se compone la plantilla.
* [Proyectos](progects.md): el equipo se asigna al proyecto, y del proyecto pasa a las tareas.
* [Tareas](tasks.md): cómo el equipo limita la elección del ejecutor.

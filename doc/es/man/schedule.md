# Programación

**La programación es el calendario con el que el sistema pone trabajo en marcha por su
cuenta.**
Por ejemplo:
* Una vez por semana, desplegar la plantilla «informe semanal»
* una vez al mes, «cerrar el mes»; una vez por trimestre, «revisión trimestral»
* una vez por noche, llevarse las tareas antiguas al archivo.

Se abre con el botón del calendario de la barra izquierda (y con la entrada del menú del mismo
nombre).

---

## Qué sabe lanzar

Dos tipos distintos de disparo, y se ve directamente en el formulario:

1. **Una copia de una plantilla de tareas.** El disparo despliega la plantilla exactamente igual
   que el botón «Crear tarea» de su ficha: con toda la jerarquía de subtareas, con la selección
   de ejecutores y con una fecha base.
2. **Una acción del sistema.** En ese caso no se crea ninguna tarea y no se elige ejecutor, por
   lo que los campos de la plantilla, del proyecto, del desplazamiento y de la regla de selección
   se ocultan por completo en el formulario. Hoy la acción es una sola: el **archivado
   automático** (véase [Archivado](Archives.md)).

---

## Campos de una programación

| Campo | Para qué |
|---|---|
| **Proyecto** | por defecto, el actual; limita la elección de la plantilla y se muestra como columna. Puede estar vacío |
| **Plantilla** | de nivel superior; el formulario tiene búsqueda por el nombre. Se ven las plantillas del proyecto elegido **y** las plantillas sin proyecto |
| **Desplazamiento de la fecha base** | horas y minutos; la fecha base de las tareas creadas = momento del disparo + desplazamiento |
| **Regla de búsqueda de ejecutores** | «primero la IA, luego una persona», «primero una persona, luego la IA» o sin selección automática |
| **Una sola vez / periódicamente** | en la de una sola vez, la fecha y la hora de lanzamiento |
| **Periodo** | semanal (los días de la semana con fichas + la hora, como el despertador del teléfono), mensual (día del mes + hora), trimestral (mes del trimestre 1–3 + día + hora, con los trimestres empezando en enero) |
| **Duración prevista, min** | para la comprobación de solapamientos |
| **Servidor** | en él se edita la programación y en él se dispara |
| **Activa** | una programación desactivada no se dispara |

Las horas de los periodos son la **hora local del ordenador**. En un mes corto el día se
comprime hasta el último: el «31» en febrero se disparará el 28 (o el 29).

### El servidor no es un formalismo

La programación se replica entre los servidores del clúster, y **sin el campo «servidor» una
misma programación se dispararía a la vez en cada servidor**. Por eso tiene un propietario; si
no se indica, la programación la lleva el director de la organización. En la lista de
programaciones hay un filtro por servidores y **por defecto muestra sólo el actual**: de otro
modo el panorama parecería el doble de denso de lo que es.

### Comprobación de solapamientos

Si en la plantilla (en cualquiera de sus nodos) hay un ejecutor indicado, al guardar el sistema
comprueba que no esté ocupado por otra programación activa a la misma hora: se comparan las
ventanas de ocupación (disparo + desplazamiento, con la duración prevista como longitud) de los
62 días siguientes. Un solapamiento es un **error de guardado** que indica el ejecutor, la
programación y la hora, no una superposición silenciosa.

---

## Dos vistas

Se alternan en la barra de herramientas y la elección se recuerda entre arranques.

* **Tabla**: ID, proyecto, plantilla (o nombre de la acción), «cuándo» (resumen del tipo y del
  periodo), desplazamiento, duración, regla de selección, «activa». Ordenación pulsando en
  cualquier columna.
* **Calendario**: la cuadrícula mensual con paso de mes. En la celda del día están las fichas de
  los disparos: primero la hora y luego el código de la plantilla o el principio de su título (lo
  que sea lo decide el ajuste «celda del calendario de programaciones» en Ajustes → General). La
  indicación emergente de la ficha es «Proyecto — Nombre de la plantilla», y al pulsarla se abre
  el formulario.

---

## Disparos vencidos

La particularidad principal de la programación en AI2P, y conviene entenderla enseguida:
**AI2P es un programa de su ordenador, no un servicio en la nube.** Mientras el ordenador está
apagado, no hay nada que se dispare.

Por eso, al arrancar la aplicación, el sistema reúne todo lo que se ha perdido en una lista de
**disparos vencidos**, que se muestra **arriba** de la pestaña: programación, plantilla, cuántos
se han omitido, la última hora. Sobre cada uno decide la persona: **«lanzar ahora»** o **«quitar
de la lista»**.

Dos consecuencias, sin las cuales a veces no se entiende nada:

* los disparos que llegan **mientras la aplicación funciona** se lanzan automáticamente (una
  comprobación por minuto): no hay que decidir nada;
* **mientras una programación tenga vencidos pendientes, los disparos nuevos esperan la decisión
  de la persona**. Si una programación «se ha callado», mire primero la parte de arriba de esta
  pestaña.

Se puede tener AI2P arrancado todo el tiempo: para eso se convierte en [servicio del sistema
operativo](service.md); entonces normalmente no hay vencidos en absoluto.

---

## Detalles que ahorran tiempo

* El evento del disparo se escribe en el registro (`schedule.triggered`): en el «Historial de
  trabajos» se ve qué se desplegó y cuándo.
* Las tareas bloqueantes de los nodos copiados funcionan como siempre: el lanzamiento esperará a
  que terminen.
* Una programación con el proyecto vacío ve todas las plantillas sin proyecto: así resulta cómodo
  crear procesos regulares comunes que no estén atados a un único proyecto.

## Y después

* [Plantillas](templates.md): lo que despliega la programación.
* [Archivado](Archives.md): hoy, la única acción del sistema por programación.
* [Ejecutar AI2P como servicio del sistema operativo](service.md): para que se dispare sin
  usted.

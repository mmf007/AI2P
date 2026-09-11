# Archivado

El **archivado** es el traslado de datos del entorno de trabajo al entorno de archivo. Sirve
para reducir el volumen operativo: los proyectos cerrados, las jerarquías de tareas acabadas,
las plantillas ya usadas y los objetos se apartan, y las listas, el árbol de tareas y la
búsqueda dejan de mostrarlos.

El archivado se lleva **por cada organización por separado**. Los datos del archivo **no pueden
modificarse**: sólo se pueden consultar o copiar de vuelta al entorno de trabajo.

Un archivo está hecho de forma sencilla. Todos los archivos de una organización están en su
subcarpeta `arc`:

* un archivo **abierto** es la carpeta `arc/<código>/`. Dentro está su propia base SQLite **con
  el mismo esquema** que la de la organización, y la subcarpeta `projects` con los archivos de
  los proyectos. Es decir, la carpeta del archivo está hecha como la carpeta de la organización,
  y por eso se puede consultar con las pantallas normales del programa;
* un archivo **cerrado** es un único archivo `arc/<código>.zip`.

---

## 1. Estados de un archivo

En la columna «Activo» de la fila del archivo hay una de cuatro palabras, y están hechas de
**dos cosas distintas**:

| Estado | Qué significa | Uno para el clúster o propio de cada servidor |
|---|---|---|
| **actual** | el archivo al que ahora mismo van los datos trasladados | propiedad del propio archivo, una por organización, **se replica** |
| **abierto** | está en este ordenador descomprimido: se puede cambiar a él y consultarlo | propio de cada servidor, **no se replica** |
| **cerrado** | está en este ordenador como un único `.zip`: no hay nada que consultar | propio de cada servidor, **no se replica** |
| **eliminado** | se ha quitado de este servidor (o nunca llegó aquí) | propio de cada servidor, **no se replica** |

De ahí salen casi todas las reglas que se ven en pantalla:

* el archivo actual está **siempre abierto** en el servidor donde se trabaja: no se puede cerrar
  ni eliminar, porque los datos van a él («El archivo actual ARC-2 no se puede cerrar: se están
  trasladando datos a él»);
* el registro del archivo es **común a todo el clúster**, y los archivos no. Un archivo
  eliminado aquí sigue tranquilamente en los vecinos, y se puede **descargar de vuelta**;
* trasladar datos sólo se puede **al archivo actual** y sólo mientras esté **abierto en este
  servidor**.

---

## 2. Cómo crear un archivo

**Ajustes → Organizaciones**, la fila de la organización **abierta en ese momento**, botón
**«Añadir archivo»** (el icono de la caja). El botón sólo existe en la organización actual —los
archivos se leen en su contexto— y se pulsa **sólo en el servidor director**: el registro de
archivos es común, y si cada servidor creara su propio archivo, habría tantos «actuales» como
máquinas hay en el clúster. En los demás servidores la indicación lo dice tal cual: «El archivo
se crea sólo en el servidor director de la organización».

En el formulario se preguntan tres cosas:

* el **Nombre del archivo**, para la persona, cualquiera;
* el **Código del archivo**: letras latinas, cifras, guión y guión bajo, hasta 64 caracteres.
  Con el código se nombran la carpeta del archivo y su fichero `.zip`, y por eso no admite
  cirílico ni espacios, a propósito;
* las **Reglas de archivado del nuevo archivo**: de dónde tomarlas: «copiar las reglas generales
  de la organización» (por defecto), «copiar las reglas del archivo que era el actual» (esta
  opción sólo existe si había un archivo anterior) o «no copiar nada».

Mientras el formulario está abierto, **comprueba el archivo actual anterior**: si se han
cumplido sus reglas de archivado. Si en el entorno de trabajo queda algo que según las reglas ya
tocaba llevarse, el formulario dirá «En el archivo «…» no se han cumplido las reglas de
archivado: quedan N registros», enumerará los diez primeros y ofrecerá el botón **«Lanzar el
archivado ahora»**. Después de él la comprobación se repite. No está hecho como prohibición: las
reglas son una indicación, no una barrera, y crear un archivo nuevo siempre se puede.

Qué ocurre al guardar:

1. se crea la carpeta del archivo con una base vacía del mismo esquema que la de la
   organización;
2. el registro recibe un número del tipo **ARC-1**, **ARC-2**, …;
3. **el nuevo archivo pasa a ser el actual**, y el actual anterior deja de serlo: queda como
   archivo abierto del que ya sólo se puede leer;
4. al nuevo archivo se copian las reglas de la forma elegida;
5. por el clúster se difunde la orden «cambia de archivo actual». No se ejecuta en el momento en
   que llega el registro, sino al final de la sesión de replicación: primero el archivo actual
   **anterior** termina de enviar sus datos por última vez, y sólo después cede su sitio.

---

## 3. Abrir, cerrar, eliminar, descargar de vuelta

Los archivos se muestran como **filas hijas** del registro de la organización: `└ ARC-2`, el
nombre, el código, los servidores que tienen el archivo y el estado. Los botones de la fila
aparecen según el estado:

| Botón | Cuándo se ve | Qué hace |
|---|---|---|
| **Abrir el archivo (descomprimirlo en este servidor)** | el archivo no es el actual y está cerrado | despliega el `.zip` en la carpeta `arc/<código>/` |
| **Cerrar el archivo (comprimirlo en .zip)** | el archivo no es el actual y está abierto | comprime la carpeta en `arc/<código>.zip` y quita la carpeta |
| **Eliminar el archivo de este servidor** | el archivo no es el actual y está aquí | quita los ficheros **sólo de esta máquina**. El registro se mantiene |
| **Descargar el archivo de otro servidor** | el archivo está eliminado aquí | una ventana con la lista de servidores que lo tienen |
| **Modificar las reglas de archivado** | el archivo es el actual | las reglas de ese archivo concreto (véase la sección 5) |

La eliminación vuelve a preguntar con todas las letras: «¿Eliminar el archivo ARC-2 «…» de ESTE
servidor? El registro se mantendrá: el archivo está en otros servidores de la organización y se
puede descargar de vuelta».

La **descarga** va en segundo plano: un archivo puede ocupar gigabytes y el formulario tiene que
responder enseguida. En la ventana se enumeran los servidores que tienen el archivo (salvo usted
mismo); al pulsar sobre un servidor responde «Se ha solicitado la descarga del archivo desde el
servidor …», y a partir de ahí la transferencia la lleva la replicación. El archivo llega
**siempre cerrado** (`.zip`), aunque en el origen esté abierto: el estado del archivo es propio
de cada servidor. Una interrupción de la transferencia va al registro y a la línea de error del
servidor de origen.

Qué viaja entre servidores por sí solo:

* el archivo **actual** se replica igual que el entorno de trabajo, y sólo él: tiene su propia
  base del mismo esquema, es decir, su propio registro de cambios, sus cursores y sus archivos
  de proyectos;
* los demás archivos no se replican en absoluto: se llevan a mano, con el botón «Descargar»;
* que un archivo esté aquí abierto, cerrado o eliminado es cosa de cada servidor y no se
  replica. Sólo viaja el «lo tengo / no lo tengo», porque si no, no habría de dónde sacar la
  lista de servidores para descargarlo.

---

## 4. Traslado manual: tarea, plantilla, objeto, experiencia, proyecto

No hay un botón «al archivo» aparte: el traslado se ofrece **allí mismo donde la eliminación**.
Al pulsar la papelera de una tarea, un nodo de plantilla, un objeto de proyecto, un registro de
experiencia o un proyecto entero, obtendrá el formulario **«¿Qué hacer con «…»?»** con tres
salidas:

* **Trasladar al archivo**,
* **Eliminar del todo**,
* Cancelar.

Antes de pulsar el botón, el formulario dice tres cosas.

**Qué se irá junto con el registro.** La jerarquía se archiva entera: «Junto con la tarea irán al
archivo todas sus subtareas: …», «…todos sus nodos hijos», «…todos sus objetos hijos», y en un
proyecto: «Junto con el proyecto se irán sus tareas y plantillas (N), objetos (N), registros de
experiencia (N), además de los registros y los ajustes del propio proyecto». La composición no se
la inventa el formulario: la calcula el propio motor de traslado con las mismas comprobaciones
que el traslado real.

**Por qué exactamente no se puede**, si no se puede llevar. La negativa llega con el texto ya
hecho y se muestra junto a la opción desactivada:

* «La tarea T-15 no es de nivel superior: la jerarquía se archiva entera, no por partes»: sólo
  se archiva la tarea raíz;
* «La tarea T-15 no se puede archivar: el estado es «en curso», el trabajo no ha terminado». Al
  archivo van las tareas en estado **borrador, revisión, terminada, cancelada**; cualquier otro
  estado en la raíz o en **cualquier** tarea hija a cualquier profundidad anula el traslado de
  toda la rama;
* «La plantilla está ocupada por la programación futura SCH-3: desactívela o elimínela primero».

**Que todavía no hay archivo**: «En la organización todavía no hay archivo actual: no hay adónde trasladar. El archivo
se crea en «Ajustes → Organizaciones → Añadir archivo».». También se
comprueban los permisos: «Trasladar datos al archivo es competencia del administrador de la
organización».

El traslado va siempre **al archivo actual** y exige que esté **abierto en este servidor**; si
no, «El archivo actual … no está abierto en este servidor: no se pueden trasladar datos».
Al terminar llega un resumen del tipo «Trasladado al archivo ARC-2: 128 registros, 40 archivos
trasladados, 3 copiados».

**Los enlaces dentro de los textos trasladados** se reescriben una vez, al trasladar: a la
dirección de la tarea o del archivo se le añade el parámetro `arc=<código del archivo>`. Por eso
un enlace de una tarea archivada lleva al archivo y no al entorno de trabajo, donde esa tarea ya
no está. Al restaurar, el parámetro se quita con ese mismo código.

---

## 5. Reglas de archivado

Una regla responde a una sola pregunta: **qué, y con qué antigüedad, toca llevarse al archivo**.
Hay dos grupos de reglas, y el conjunto de campos es el mismo en ambos:

* las **reglas generales de la organización**: **Ajustes → Catálogos**, sección «Reglas
  generales de archivado». Son un **modelo**: de ellas se copian las reglas de cada archivo
  nuevo;
* las **reglas del archivo**: propias de cada archivo, se abren con el botón «Modificar las
  reglas de archivado» de su fila. Tienen un botón de **«Restablecer el valor por defecto»**:
  eliminar todas las reglas de ese archivo y copiar las reglas generales de la organización.
  Se configuran **solo en el archivo actual**: los datos se trasladan únicamente a él, así que
  en los demás archivos las reglas ya han cumplido su función y no hay nada que cambiar; en sus
  filas no hay botón de reglas.

Campos del registro de una regla (los números de las reglas son del tipo **ARR-1**, **ARR-2**,
…):

| Campo | Valores |
|---|---|
| **Se aplica a** | Tareas, Plantillas, Objetos, Experiencia, Reglas de seguridad, Registros |
| **Estado de la tarea** | se pregunta **sólo** en las reglas sobre tareas |
| **Estados de las tareas hijas** | una lista; si no se elige nada, vale cualquiera |
| **Actividad de los datos** | cualquiera, activos, inactivos; en todos los tipos salvo las tareas |
| **Fecha** | fecha de creación o fecha de modificación |
| **Plazo** | temporal (días) o de calendario (meses y años) |
| **Activa** | una regla desactivada no participa en la selección |

Sobre las fechas conviene recordar: una tarea creada hace un año y editada ayer se va al archivo
**por fecha de creación**, pero **por fecha de modificación** no. En el plazo de calendario los
meses se pueden no indicar, y entonces sólo se tienen en cuenta los años.

Cómo selecciona las tareas una regla (lo mismo dice la indicación del formulario): «La jerarquía
de tareas se archiva entera: en la tarea raíz se comprueban el estado y la antigüedad, y en las
hijas, sólo el estado». Es decir, la regla mira **sólo las tareas raíz**; en cada tarea hija, a
cualquier profundidad, se comprueba que su estado esté en la lista «Estados de las tareas hijas»,
y **una sola hija que no encaje anula el archivado de toda la rama**. Una lista vacía significa
«cualquier estado», no «ninguno».

Dos tipos —las **reglas de seguridad** y los **registros**— se pueden seleccionar con una regla,
pero el motor no los traslada de uno en uno: no tienen ni jerarquía ni archivos. El archivado
automático lo dirá con claridad —«El tipo de datos «security» no se traslada al archivo de uno en
uno»— y contará ese registro como omitido. Al archivo van dentro de un **proyecto entero**.

---

## 6. Archivado automático por programación

El archivado automático es «seleccionar según las reglas del archivo actual y trasladar». No
tiene ni selección ni traslado propios: usa las mismas reglas y el mismo motor que el traslado
manual.

Se pone con una **programación**. Menú **«Programación»** → **«Añadir programación»**, y luego:

1. en **«Qué se lanza»**, elegir **«acción del sistema»** en lugar de «tarea a partir de una
   plantilla»;
2. en **«Acción»**, **«Archivado automático»**. La indicación del campo: «La acción se ejecuta en
   el servidor director de la organización; en tal caso no se crea ninguna tarea»;
3. luego, los campos normales de una programación: una sola vez o periódicamente, fecha, hora,
   periodo.

La plantilla no se indica en absoluto, no se crea ninguna tarea y no se elige ejecutor: es un
trabajo del propio sistema sobre su base.

El archivado va **sólo en el servidor director** de la organización: el archivo actual es uno, y
si se lanzara en cada máquina, unas mismas ramas irían al archivo desde varios servidores a la
vez. La negativa no llega como error, sino como una línea comprensible: «El archivado automático se realiza sólo en el servidor director de la organización», «No hay archivo actual: los datos sólo se trasladan al archivo actual», «El archivo actual … no está abierto en este servidor».

El resultado de la pasada se escribe en el registro de la organización como resumen: «Archivado
automático al archivo ARC-2: seleccionados 30, trasladados 28, omitidos 1, fallidos 1». Cada
fallo tiene su motivo y se nombra uno por uno; los demás candidatos, mientras tanto, van igual.

Esa misma acción se invoca con el botón **«Lanzar el archivado ahora»** del formulario de añadir
archivo (sección 2); allí responde no con el registro, sino con la línea «Trasladado al archivo:
N; fallidos: N».

---

## 7. Cómo consultar un archivo: el campo «entorno»

En la barra superior, entre la **organización** y el **proyecto**, está el campo **«entorno»**:

> Organización: **Mi empresa**  ·  entorno: **entorno de trabajo**  ·  Proyecto: **…**

Al lado está la flecha de **«Cambiar de entorno»**, y en el menú están «entorno de trabajo» y
todos los archivos **abiertos en este servidor** (el actual está marcado con la palabra
«(actual)»). Un archivo cerrado es un `.zip`, no hay nada que leer en él, y no aparece en la
lista en absoluto. Si no hay ningún archivo creado, el campo «entorno» no existe entero, junto
con su rótulo.

Elegir un archivo **cierra todas las pestañas**: muestran datos del entorno anterior. Después
usted trabaja con las pantallas normales —el árbol de tareas, el tablero, la ficha, los objetos,
la experiencia—, sólo que los datos se toman del archivo.

Un archivo está abierto **sólo para consulta**. La interfaz no muestra botones de edición en un
archivo, y si aun así llegara una petición de modificación (un complemento, un cliente externo),
el servidor respondería con una negativa: «El archivo está abierto sólo para consulta: no se
pueden modificar sus datos». Las excepciones son exactamente dos: la sección de gestión de los
propios archivos (el registro, las reglas, el traslado y la restauración) y los ajustes de la
pantalla de la propia persona (el proyecto elegido, la disposición de las pestañas): ambas cosas
se escriben en el entorno de trabajo, no en el archivo.

Volver al entorno de trabajo se hace con ese mismo selector, en la opción «entorno de trabajo».
Si el archivo elegido se cierra o se elimina de este servidor, el programa volverá al entorno de
trabajo por su cuenta.

---

## 8. Restauración desde un archivo

La restauración es la operación inversa al archivado: el registro, con todas sus tareas hijas,
archivos y enlaces, vuelve al entorno de trabajo y sale del archivo.

Sólo es posible **desde el archivo actual**: en los demás los datos ya no cambian. Por eso el
botón **«Restaurar»** (el icono de la caja abierta) está en la ficha de una tarea o de un nodo de
plantilla **sólo cuando está abierto el archivo actual**. Vuelve a preguntar: «¿Restaurar «T-15
…» del archivo al entorno de trabajo? Se irá toda la jerarquía de la tarea junto con los
archivos», y al terminar responde «Restaurado del archivo». La ficha se cierra entonces: esa
tarea ya no está en el archivo.

La marca `arc=<código>` se quita de los enlaces con ese mismo código que la puso: los enlaces
vuelven a su forma de trabajo.

Un botón «Restaurar» aparte para un objeto de proyecto, un registro de experiencia o un proyecto
entero **hoy no existe**: el motor sabe hacerlo y la llamada `POST /api/archives/restore` los
acepta (tipos `object`, `experience`, `project`), pero el botón no está puesto en pantalla.

---

## 9. Preguntas frecuentes

**¿Por qué no se puede pulsar el botón «Añadir archivo»?** No está en el servidor director de la
organización o no tiene permisos de administrador de la organización.

**¿Por qué no está mi archivo en el menú «entorno»?** En la lista sólo están los archivos
**abiertos en este servidor**. Uno cerrado hay que abrirlo primero (descomprimirlo) en «Ajustes
→ Organizaciones»; uno eliminado aquí, descargarlo de otro servidor.

**¿Por qué no se va la tarea al archivo?** No es raíz, o su estado (o el estado de alguna de sus
tareas hijas) significa trabajo sin terminar. Al archivo sólo van los borradores y las ramas
entregadas: «revisión», «terminada», «cancelada».

**¿Dónde ha ido a parar el archivo actual después de crear uno nuevo?** Se ha quedado como
archivo abierto en su sitio: ha dejado de ser el actual, se puede leer, pero escribir en él ya
no.

**¿Se pueden editar los datos de un archivo?** No. Un archivo es sólo de lectura; para
corregirlo hay que restaurar el registro al entorno de trabajo (y eso sólo es posible desde el
archivo actual).

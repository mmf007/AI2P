# Experiencia

**La experiencia es la memoria del sistema sobre cómo hay que trabajar.** Una lección breve
obtenida por una tarea se inserta en el texto del encargo de otras tareas, y así el siguiente
ejecutor no tropieza con la misma piedra.

Llevan registros de experiencia tanto las personas como los agentes de IA. La persona, en las
listas de experiencia (pestaña «Experiencia» de la ficha del proyecto, pestaña «Experiencia» del
nodo de plantilla, **Ajustes → Experiencia general**); el agente, con las herramientas
`create_experience` y `update_experience` mientras trabaja.

---

## Los tres ámbitos

Un registro tiene exactamente un ámbito, y de él depende quién lo recibe.

| Ámbito | Quién lo recibe | De qué trata |
|---|---|---|
| **Reglas generales de la organización** | toda tarea de la organización, en cualquier proyecto | cómo trabajamos: proceso, informes, disciplina de comprobaciones |
| **Experiencia del proyecto** | tareas de este proyecto | este producto: su estructura, sus trampas, sus decisiones, sus nombres |
| **Experiencia del nodo de plantilla** | tareas creadas desde ese nodo (y sus descendientes) | este paso del proceso |

La regla de separación en una frase — el mismo texto aparece en el formulario del registro y en la
ventana de traslado:

> Una regla general trata de **cómo trabajamos**: es cierta en cualquier proyecto de la
> organización y no nombra ni un archivo, ni un código de tarea, ni un producto. La experiencia
> **del proyecto** trata de este producto. La experiencia **del nodo de plantilla** trata de este
> paso del proceso.

La regla no es decorativa: las reglas generales llegan a **todas** las tareas de la organización, y
un registro de proyecto colado ahí por error le quita sitio a todos los demás. Por eso, al escribir
en la experiencia general el texto se comprueba con cuatro indicios: **código de tarea**
(`T-241`, `T-12-S0`), **ruta de archivo**, **extensión de código fuente** y **nombre de proyecto**:

* al agente de IA se le **deniega**, y se le propone crear el registro en la experiencia del
  proyecto. Cambiar el ámbito en silencio despistaría al agente que luego busca el registro por su
  identificador;
* a la persona se le muestra una **advertencia** en el formulario y el botón **«Guardar de todos
  modos»**: usted puede saber más que la comprobación;
* los registros que llegaron con un complemento no pasan la comprobación: la decisión ya la tomó la
  persona que instaló el complemento.

La comprobación se aplica al crear un registro general, al editarlo y al trasladar algo **hacia** las
reglas generales.

### Revisión de la experiencia general

La basura acumulada no se limpia registro a registro. Sobre la lista de la pestaña
**Ajustes → Experiencia general** hay un bloque **«Revisión de la experiencia general»**: indica
«parece experiencia de proyecto: N registros», señala línea a línea **qué indicio lo delató** y
traslada en bloque los registros marcados al proyecto elegido. El traslado se hace de uno en uno, de
modo que una negativa (registro de otro servidor, regla del distributivo) no cancela el resto.

### Trasladar un registro entre ámbitos

El botón **«Mover a otro ámbito»** en la fila de la lista abre una ventana: primero el ámbito, luego
el proyecto o el nodo de plantilla.

El traslado cambia **solo el vínculo**. El identificador, el texto, las etiquetas, la habilidad, la
autoría, la fecha de creación y las marcas «cargar siempre» y «activo» se mantienen: el registro no
se vuelve a crear y las referencias a su identificador no se rompen.

Lo que no se puede trasladar:

* **un registro ajeno**, creado por otro servidor del clúster: la basura de otro servidor se limpia
  allí;
* **una regla suministrada con el distributivo**: cada servidor las siembra por su cuenta, y el
  traslado daría una segunda copia de la misma línea. Una regla suministrada que estorba se
  **desactiva**, no se traslada ni se borra.

---

## Qué tiene un registro

* **Texto** — la lección. Una sola idea, en imperativo, sin repetir lo evidente.
* **Habilidad** — a quién va dirigido (`code-test`, `text-docs`, …). **Un registro sin habilidad y
  sin la marca «cargar siempre» no llega a ningún encargo**, salvo en la experiencia del nodo de
  plantilla, donde un registro sin habilidad sigue siendo común a todas las tareas de ese nodo.
* **Etiquetas** — el tema. Desde la versión 1.135 las etiquetas ya **no descartan** registros (véase
  más abajo): adelantan el registro al repartir el sitio.
* **«Cargar siempre»** — el registro entra en el encargo sin importar las habilidades del ejecutor.
  La marca es cara: grava el prompt de **todas** las tareas, así que resérvela para reglas válidas
  para cualquier ejecutor.
* **«Activo»** — el interruptor del registro (véase «Actividad y archivado»).
* **Autor y servidor propietario** — solo su propio servidor puede editar, trasladar o desactivar el
  registro.

---

## Cómo llega un registro al encargo

Al componer el texto del encargo, el sistema imprime los bloques de experiencia: primero las reglas
generales de trabajo, luego la experiencia del proyecto, luego la del nodo de plantilla. No entra
todo: la selección tiene tres pasos.

### Paso 1. Quién sirve

1. el registro está **activo**: uno desactivado nunca llega al encargo, ni siquiera con la marca
   «cargar siempre»;
2. el registro lleva la marca **«cargar siempre»**: se toma sin más condiciones;
3. en otro caso necesita una **habilidad**, y debe coincidir con las del ejecutor.

**Las etiquetas ya no intervienen aquí.** Antes un registro con etiquetas no coincidentes se
descartaba antes de cualquier cuenta; y como el agente marca los registros nuevos con las etiquetas
de la tarea, ese filtro habría acabado descartando justo lo necesario por una discrepancia formal de
una palabra. Ahora las etiquetas son una **señal**: la coincidencia añade medio paso de relevancia,
la falta de coincidencia no quita nada.

### Paso 2. Experiencia de los nodos antecesores

Una tarea creada desde un nodo de plantilla recibe la experiencia **no solo de su nodo**, sino de
todos los nodos hacia arriba hasta la raíz. La experiencia de ramas vecinas de la plantilla sigue sin
tomarse.

### Paso 3. El límite y las cuotas

El volumen de experiencia en un encargo está limitado por un ajuste del proyecto (por omisión,
**100 000 caracteres**): de lo contrario el bloque de experiencia expulsaría de la ventana del
modelo a la propia tarea. El sitio se reparte así:

1. los registros **«cargar siempre»** se toman primero, fuera de concurso y fuera de cuotas;
2. el resto se divide en **cuotas por nivel**: **50 %** a la experiencia de los nodos de plantilla,
   **30 %** a la del proyecto, **20 %** a las reglas generales. Dentro de un nivel mandan la
   relevancia: nodo propio → padre → abuelo → proyecto → reglas generales, más medio paso por
   coincidencia de etiquetas;
3. **la cuota no usada pasa** a los vecinos: un nivel vacío no roba sitio.

Las cuotas existen para que un nivel prolijo no se coma el presupuesto entero: sin ellas la
experiencia del proyecto desplazaría tanto a las reglas generales como a la experiencia más precisa
del nodo.

El registro se inserta **entero**: media lección se lee como otra lección. El orden de impresión es
cronológico, como en la lista.

**Qué no arregla esto.** Con el límite lleno, un registro fuera de tema sigue sin llegar: ya no se
descarta por una regla, simplemente lo adelantan otros más relevantes. Ese registro se obtiene con la
búsqueda (más abajo).

Si algo no cupo, el sistema lo dice en el propio encargo y le nombra al ejecutor la herramienta de
búsqueda: lo descartado no se pierde, queda disponible bajo petición.

---

## Experiencia utilizada por la tarea

Qué registros insertó el sistema **realmente** se ve en la ficha de la tarea, en la pestaña
**«Experiencia utilizada»**. La pestaña aparece solo cuando la lista no está vacía (en una tarea que
nunca se ejecutó no hay nada que mostrar).

Arriba hay un **resumen**: cuántos registros entraron en el encargo y cuántos caracteres son, como
número y como parte del límite de inserción de experiencia (un ajuste del proyecto). Así se ve si el
límite está bien elegido.

Las columnas son las de la pestaña «Experiencia» (texto, ámbito, habilidad, etiquetas, actividad,
fecha del último uso) y a la derecha de cada fila hay un botón **«Modificar la entrada»**: aquí se
guarda un enlace al registro, así que el cambio va al propio registro y no hace falta volver a
buscarlo en las listas de experiencia. Un registro borrado o llevado al archivo se muestra como «el
registro … no está disponible» y no hay nada que modificar: el rastro de uso guarda solo
identificadores y sobrevive al propio registro.

La otra cara del mismo diario son las **estadísticas del registro**: cuántas **tareas** lo recibieron
y cuándo fue la última vez. Así se ve qué experiencia funciona y cuál solo ocupa sitio en el prompt.

---

## Búsqueda en la experiencia

Sobre los filtros de cada lista de experiencia hay una línea **«Buscar en el texto»**. A diferencia
de los filtros por habilidad, etiquetas y actividad, la búsqueda también fija el **orden**: los
resultados salen por rango, no por fecha. El rango funde tres señales — coincidencia léxica, novedad
del registro y número de etiquetas coincidentes con la tarea —, pero **encuentra solo la coincidencia
de palabras**: la novedad y las etiquetas únicamente reordenan lo hallado.

El agente de IA usa la misma búsqueda: la herramienta `search_experience` (ámbito
`project` / `template` / `general` / `all`, por omisión solo registros activos) y el comando

```
ai2p experience-find "palabras de la consulta" --scope project --limit 10
```

Precisamente con la búsqueda se obtiene lo que no cupo en el encargo por el límite.

### Qué no encuentra la búsqueda — con franqueza

1. **El sentido.** «cómo montar el paquete» no encontrará un registro que dice «MakePackage» e
   «Inno Setup» si no contiene esas palabras.
2. **Sinónimos y abreviaturas.** «BD» ≠ «base de datos».
3. **Morfología compleja.** Las terminaciones se recortan de forma tosca y los prefijos no se quitan
   («reindexar» ≠ «índice»).
4. **Erratas.**

### Protección contra duplicados

Cuando el agente crea un registro nuevo, el sistema lo compara con los ya existentes **del mismo
ámbito**:

* con una coincidencia **muy fuerte** el registro no se crea: al agente se le devuelven el
  identificador y el texto del hallado, con la propuesta de corregirlo;
* con una coincidencia **apreciable** el registro se crea pero recibe la etiqueta `similar:<id>`,
  una marca para la revisión humana.

La misma idea contada **con otras palabras** no la detecta la comparación de palabras: es el límite de
cualquier comparación léxica.

---

## Actividad y archivado

Un registro se puede **desactivar** sin borrarlo. El registro desactivado sigue en las listas, se ve
y se puede recuperar, pero no entra en los encargos.

* El filtro **«Actividad»** en las listas: **Activos / Inactivos / Todos**, por omisión «Activos».
* La columna **«Activo»** y el botón de conmutación en la fila: el mismo derecho que editar y borrar.
* El interruptor **«Activo»** en el formulario del registro.

También se pueden desactivar las **reglas suministradas con el distributivo**: de otro modo no habría
cómo quitarlas, porque la siembra no devuelve lo borrado, mientras que lo desactivado vuelve con un
clic.

**El agente de IA no borra experiencia.** El análisis solo **desactiva** registros
(`set_experience_active`), y luego el archivado se lleva lo desactivado. Es deliberado: lo
desactivado siempre se puede restaurar, lo borrado no. El agente no puede desactivar las reglas
suministradas: son las reglas por las que él mismo trabaja.

### La regla de archivado «todos los inactivos»

En las reglas de archivado, para el tipo de datos **«experiencia»** funciona la selección «solo
inactivos»: entran tanto los desactivados como los borrados.

Junto a ella existe un tercer tipo de plazo: **«la edad no importa»**. El formulario anterior exigía
un plazo estrictamente mayor que cero, así que la regla «archivar **todos** los registros inactivos»
no se podía escribir. Ahora sí: elija el tipo de datos «experiencia», la selección «solo inactivos» y
el plazo «la edad no importa»; el formulario esconde solo los campos numéricos y la lista de reglas
muestra ese plazo como «cualquiera».

Esta regla **no** se pone en los valores por omisión de un archivo nuevo a propósito: el sistema no
debe llevarse al archivo la experiencia de nadie sin permiso. Créela usted si quiere una limpieza
periódica.

Más sobre archivos, reglas y archivado automático por horario — el capítulo
**[Archivado](Archives.md)**.

---

## Conjuntos de experiencia: estilos de trabajo

**Un conjunto es un paquete listo de registros de experiencia que se instala y se retira con un
botón.** Así la disciplina de trabajo («cómo revisamos», «cómo escribimos pruebas», «cómo hacemos
informes») viene de fábrica en lugar de inventarse otra vez en cada organización.

Pestaña **Ajustes → Conjuntos de experiencia**.

### Instalación

1. Elija el **ámbito de instalación**: reglas generales de la organización, experiencia del proyecto
   o nodo de plantilla (en los dos últimos casos, además, el propio proyecto o nodo). El ámbito no se
   guarda en el archivo del conjunto: es su elección, y la interfaz no deja omitirla.
2. Pulse **«Instalar»**. El sistema crea los registros y dice cuántos son.
3. El botón **«i»** de la fila abre el **documento del conjunto**: qué estilo es, para quién y qué
   cambia en el trabajo de los agentes tras instalarlo. Los documentos de los conjuntos suministrados
   están en `doc/<idioma>/packs/`.

### Retirada y reinstalación

El botón **«Retirar»** quita exactamente los registros de ese conjunto: la instalación los marca con
la etiqueta de servicio `pack:<código>`. Sus propios registros y sus ediciones de los textos no se
tocan: un registro que usted editó sigue siendo suyo, y una reinstalación **no lo sobrescribe**.

### Exportar un conjunto propio

El bloque **«Exportar registros a un conjunto»** hace un conjunto con **sus** registros: selecciónelos
con el filtro y las casillas, indique código, nombre y descripción, y obtendrá un archivo de conjunto
instalable en otra instalación. Los identificadores se conservan, así que la instalación inversa da
**las mismas filas**, no copias.

### Lo que hay que saber sobre el idioma

Los textos de los conjuntos suministrados están escritos en los cinco idiomas de la interfaz, pero
**un registro vive como una sola cadena**: al instalar se toma el idioma del servidor. Por eso en un
encargo en otro idioma el título del bloque estará traducido y los textos de los registros seguirán
en el idioma de la instalación. Así están hechos los conjuntos; no es un fallo de traducción.

### Qué trae el distributivo

| Código | Nombre | De qué trata |
|---|---|---|
| `style.strict-review` | Revisión de código estricta | primero el encargo, después el diff; cada observación tiene peso; el veredicto es obligatorio |
| `style.tdd` | Desarrollo guiado por pruebas | primero la prueba en rojo, después el cambio mínimo |
| `style.research-report` | Investigación e informe | disciplina de fuentes, de cifras y de la sección «qué no se hizo» |
| `style.experience-analysis` | Análisis de la experiencia | no es un estilo, sino una **plantilla de tareas** para revisar la experiencia acumulada |

Los archivos de los conjuntos están en el directorio `packs/` del directorio de datos, junto a
`plugins/` y `models/`; los conjuntos suministrados los coloca allí el propio programa al abrir la
organización.

Panorama de los conjuntos y sus documentos — **[Conjuntos de experiencia: estilos de
trabajo](../packs/README.md)**.

---

## La plantilla «Análisis de la experiencia»

La experiencia se acumula más rápido de lo que envejece: los registros se duplican, quedan obsoletos,
viven en el ámbito equivocado. Nadie va a ordenar eso a mano, así que la revisión se plantea como
**una tarea para un agente de IA**, por horario.

El conjunto `style.experience-analysis` trae consigo **nodos de plantilla de tareas**:

* **«Análisis de la experiencia»** — nodo raíz con la instrucción completa: qué leer (estadísticas de
  uso → listado de registros por páginas → búsqueda cuando haga falta), qué hacer (generalizar,
  dividir, etiquetar, trasladar, desactivar por estadística), qué **no** hacer (no borrar, no tocar
  registros ajenos, no desactivar reglas suministradas, no reescribir el sentido al generalizar) y qué
  poner en el informe: identificadores y las cifras «había / quedan activos»;
* **«Análisis de la experiencia general de la organización»** — nodo hijo para las reglas generales;
* **«Análisis de la experiencia del proyecto»** — nodo hijo para un proyecto; cópielo tantas veces
  como proyectos tenga.

La regla de desactivación por estadística está escrita **con palabras** en el encargo del nodo raíz,
no fijada en el código: un registro se desactiva si no llegó a ningún encargo durante tres meses
**habiendo** habido tareas de su tema en ese tiempo. «No llegó porque no hubo tareas así» no es motivo
para desactivarlo. Ajuste esa redacción en la propia plantilla a su proceso.

### Cómo poner la revisión en el horario

1. **Ajustes → Conjuntos de experiencia → «Análisis de la experiencia» → «Instalar»**, ámbito
   **proyecto** o **nodo de plantilla**. El ámbito «reglas generales» no tiene proyecto alguno, y una
   plantilla de tareas sin proyecto no vive en AI2P: entonces los nodos no se crean (los registros se
   instalan como siempre).
2. **Plantillas del proyecto**: ha aparecido el nodo raíz «Análisis de la experiencia» con dos hijos.
   Copie el hijo «Análisis de la experiencia del proyecto» tantas veces como proyectos tenga y asigne
   a cada copia su proyecto.
3. **Ajustes → Horarios**: cree un horario periódico (semanal o mensual) y elija como plantilla el
   nodo **«Análisis de la experiencia»**.

Al horario solo sirve el nodo **raíz** de la plantilla: los hijos llegan copiados con él y no
necesitan horario propio. La instalación del conjunto **no** crea horario a propósito: ejecutar tareas
gasta dinero en el modelo, y eso lo decide una persona.

Más sobre periodos, el servidor del horario y los disparos vencidos — el capítulo
**[Horario](schedule.md)**.

---

## Qué sabe hacer el agente de IA con la experiencia

| Herramienta | Qué hace | Comando CLI |
|---|---|---|
| `create_experience` | crear un registro | `ai2p experience` |
| `update_experience` | corregir texto, habilidad, etiquetas, «cargar siempre», actividad | `ai2p experience-update` |
| `search_experience` | encontrar un registro por palabras | `ai2p experience-find` |
| `list_experience` | listar los registros de un ámbito por páginas | `ai2p experience-list` |
| `experience_usage` | estadísticas de uso | `ai2p experience-usage` |
| `move_experience` | trasladar un registro a otro ámbito | `ai2p experience-move` |
| `set_experience_active` | activar o desactivar un registro | `ai2p experience-active` |

En `update_experience` **un campo no transmitido no cambia**: editar el texto no quita las marcas
puestas por una persona.

**El agente no tiene borrado de experiencia y no lo tendrá.** Todas estas acciones figuran en el
catálogo de acciones, de modo que las reglas de seguridad de la tarea y del equipo las cubren; véase
el capítulo **[Configuración](config.md)**, pestaña «Acciones».

---

## Preguntas frecuentes

**El registro existe pero no está en el encargo.** Compruebe por orden: ¿está **activo**? ¿tiene
**habilidad**, y la tiene el ejecutor? si no tiene habilidad, ¿lleva la marca «cargar siempre»? si
todo eso se cumple, probablemente no cupo en el límite: mire la pestaña «Experiencia utilizada» de la
última ejecución.

**El agente se negó a crear una regla general.** En el texto hay un código de tarea, una ruta de
archivo, una extensión de código fuente o un nombre de proyecto: eso es experiencia **del proyecto**,
no una regla general. O se crea en la experiencia del proyecto, o se reformula sin esos indicios.

**En la experiencia general se acumuló basura del proyecto.** Ajustes → Experiencia general → bloque
«Revisión de la experiencia general»: marque y traslade en bloque al proyecto correspondiente.

**Una regla suministrada estorba.** Desactívela. No hace falta borrarla: la siembra no devuelve lo
borrado.

**Hay demasiada experiencia.** Ponga la revisión en el horario (plantilla «Análisis de la
experiencia») y cree una regla de archivado «experiencia + solo inactivos + la edad no importa».

---

## Capítulos vecinos

* **[Proyectos](progects.md)** — la pestaña «Experiencia» de la ficha del proyecto y los tres niveles
  de experiencia.
* **[Plantillas](templates.md)** — la experiencia del nodo de plantilla y el lugar donde vive el saber
  sobre el proceso.
* **[Tareas](tasks.md)** — la descripción de la tarea como prompt y la pestaña «Experiencia
  utilizada».
* **[Archivado](Archives.md)** — reglas de archivado y limpieza automática.
* **[Horario](schedule.md)** — ejecución periódica de la plantilla «Análisis de la experiencia».
* **[Configuración](config.md)** — pestañas «Experiencia general», «Conjuntos de experiencia» y
  «Acciones».
* **[Conjuntos de experiencia: estilos de trabajo](../packs/README.md)** — documentos de los conjuntos
  suministrados.

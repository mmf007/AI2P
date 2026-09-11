# Editor de LoRA

Un adaptador **LoRA** es un archivo pequeño con un añadido a los pesos del modelo. Le enseña al
modelo una cosa concreta: la cara de un personaje, la textura de un material, una manera de
dibujar. Después del entrenamiento, el adaptador se conecta al modelo durante la generación, y
el personaje deja de «desdibujarse» de un fotograma a otro incluso allí donde un pasaporte de
texto ya no basta.

El **editor de LoRA** es el formulario en el que se compone el conjunto de datos (los fotogramas
con sus pies), se lleva la lista de modelos para los que se entrena el adaptador y se lanza el
propio entrenamiento. Esta página trata de él.

Esta ayuda se puede abrir directamente desde el editor: el botón del libro en la esquina
superior derecha del formulario.

---

## 1. Antes de empezar

El entrenamiento no es un botón, sino el trabajo de un programa de entrenamiento que vive
**fuera de AI2P**. Para que el editor pueda lanzarlo hacen falta cuatro cosas:

| Qué | Dónde se indica | Cómo comprobarlo |
|---|---|---|
| La carpeta del proyecto en este servidor | ficha del proyecto, pestaña «General» | sin ella, la negativa «El proyecto no tiene carpeta en este servidor: no hay dónde componer el conjunto de datos de entrenamiento» |
| Un modelo que trabaje con adaptadores | Ajustes → Modelos, la marca LoRA del registro | en la pestaña «Modelos» del editor, en un registro no apto el botón «Entrenar» está apagado |
| El comando de entrenamiento de ese modelo | perfil del modelo, sección `lora.train.start.command` | sin él, la negativa «En el catálogo de modelos no se dice con qué entrenar: el comando de entrenamiento no está indicado» |
| El repositorio de modelos | Ajustes → Servidores → formulario del servidor local, campo «Repositorio de modelos» | ahí, en la subcarpeta `loras`, quedará el adaptador terminado |

No necesitan un ajuste aparte, pero participan en el trabajo otras tres cosas: el
**complemento entrenador**, el **ejecutor «software automático»** y la **plantilla de la tarea
de entrenamiento**. El entrenador llega con la instalación del modelo, y el ejecutor y la
plantilla los crea con un solo botón la propia ventana de lanzamiento del entrenamiento; véase
el apartado 5, «El entrenamiento va como tarea».

Qué es lo que sabe hacer su modelo se ve en su registro del catálogo: **Ajustes → Modelos**,
columna «LoRA» (una marca significa que funciona; una raya con una indicación, por qué no), y en
el propio formulario del registro, las líneas «El adaptador se conecta», «Entrenamiento», «El
adaptador terminado se guarda» y el enlace **«Cómo entrenar el adaptador de este modelo»**. Por
ahí conviene empezar: ahí está la instrucción de quien publicó el modelo.

Sobre el comando de entrenamiento hay que decir algo aparte. En **ambos registros de Kandinsky
5** viene **puesto** en la distribución y no hay que escribir nada: el entrenamiento lo hace
musubi-tuner, y el propio entrenador, junto con Python, llega **al instalar el modelo**, con el
botón «Instalar» de su formulario, allí mismo donde se descargan los pesos (los detalles, en el
documento del modelo, botón «i»). En el primer lanzamiento del entrenamiento sólo se preparan
los archivos de configuración (`dataset.toml`, `train.cmd`): el entorno `.venv` con torch, las
dependencias del entrenador y los codificadores de texto Qwen2.5-VL-7B y CLIP (unos 18 GB)
llegan con la instalación del modelo, en una ventana que muestra el avance. Si en
este ordenador ya está instalado Python 3.10–3.12, la instalación no lo descarga, sino que usa
el suyo. En los demás registros el comando está **vacío a propósito**: la carpeta del
repositorio de entrenamiento, el entorno y las opciones de lanzamiento son propios de cada uno,
y un comando inventado sería peor que uno vacío; siguiendo el enlace del formulario del modelo
se compone y se escribe en el perfil una sola vez (`lora.train.start.command`).

Junto con el comando, el perfil puede llevar también **archivos de configuración del
entrenador** (`lora.train.files`): la configuración del conjunto de datos, el script de
lanzamiento. Se ponen junto al conjunto de datos antes de cada lanzamiento y **se reescriben**
cada vez: hay que editarlos en el perfil del modelo, no en el disco; los archivos propios
póngalos con nombres propios, que nadie los tocará.

En los registros del catálogo que se entrenan **fuera del sistema** (los modelos de texto
locales de llama.cpp), el lanzamiento del entrenamiento desde AI2P responde con la negativa «El
adaptador del modelo … se entrena fuera del sistema: ponga el archivo terminado a mano y escriba
su ruta». No es una avería: en esos modelos, en el editor se hace el conjunto de datos y se
lleva la lista, pero se entrenan con medios propios y se escribe la ruta del archivo terminado
en el campo «Archivo del adaptador» del formulario del objeto.

---

## 2. Cómo abrir el editor

1. Abra la ficha del proyecto → pestaña **«Objetos»**.
2. Cree un objeto del tipo **«adaptador LoRA»** (botón «Objeto nuevo», campo «Tipo») y
   **guárdelo**. Mientras el objeto no esté guardado no existe siquiera el botón de
   configuración: los fotogramas del conjunto de datos y las filas de entrenamiento necesitan un
   propietario, y un objeto sin crear todavía no tiene número.
3. Abra el objeto y pulse **«Configuración de LoRA»**.

La sección «adaptador LoRA» y ese botón sólo existen en un objeto del tipo «adaptador LoRA». Un
personaje, una localización y un accesorio no tienen adaptador propio, pero pueden tener uno
**ya hecho**: la ruta de su archivo se escribe en el campo «Archivo del adaptador» de ese mismo
formulario, y entonces el adaptador se conecta al modelo siempre que la descripción de una tarea
haga referencia a ese objeto.

El formulario del editor se compone del campo **«Descripción»** y de dos pestañas: **«Conjunto
de datos»** y **«Modelos»**.

---

## 3. El campo «Descripción»: qué escribir

Es ese mismo texto que en el formulario del objeto se llama **«Pasaporte (va al prompt)»**: el
objeto no tiene una segunda descripción. **No participa en el entrenamiento**, sino que va al
prompt de la **generación**: cuando en la descripción de una tarea hay una referencia
`@obj:OBJ-7`, en su lugar se pone este texto.

Qué escribir:

* una **descripción literal de aquello para lo que se ha entrenado el adaptador**, no una nota
  para uno mismo: «mujer de 30 años, pelo pelirrojo hasta los hombros, ojos verdes, pecas,
  chaqueta gris», y no «Masha, nuestro personaje principal, véanse los fotogramas»;
* la **palabra clave (el disparador)**, si se ha usado en los pies de los fotogramas: con ella el
  modelo recuerda lo aprendido: «`m4sha_rf`, mujer de 30 años, …»;
* la **fuerza y los límites**: qué NO sabe hacer el adaptador («sólo de cintura para arriba, no
  hubo fotogramas de cuerpo entero»); eso ahorra unas cuantas generaciones fallidas;
* hay que escribirlo **en el idioma en el que se escriben las descripciones de las tareas**: el
  texto irá al prompt tal cual, mezclado con el resto de la descripción del fotograma.

Lo que no hay que escribir: notas de servicio del tipo «entrenado 2000 pasos con 40 fotogramas»;
no ayudan a la generación y ocupan sitio en el prompt. Para eso está la pestaña «Modelos»: allí
se ve para qué y cuándo se entrenó.

La descripción no se guarda **aquí**. El botón «Aplicar» la devuelve al formulario del objeto, y
quien la guarda es él, junto con todo el resto del formulario. Los fotogramas y las filas de
entrenamiento, en cambio, se guardan de inmediato, con cada acción: el entrenamiento dura horas
y no se puede perder por un «Cancelar» accidental.

---

## 4. La pestaña «Conjunto de datos»

El conjunto de datos es un juego de fotogramas: **imagen + pie**. Con ellos aprende el
adaptador.

**El conjunto de datos es un objeto aparte** (versión 1.98). Se crea solo cuando usted abre el
editor, se llama «conjunto de datos» y es un objeto hijo de su adaptador; los fotogramas ya son
hijos suyos. Está hecho así por la jerarquía: mientras los fotogramas eran hijos directos del
objeto, medio centenar de imágenes se mezclaban con los objetos hijos con sentido, y no había
manera de entender la lista.

### 4.0. El conjunto de datos actual y por qué son varios

Arriba de la pestaña están el campo **«Conjunto de datos actual»** y el botón **«+»**.

* Todo lo de la pestaña se refiere al conjunto de datos **elegido**: tanto la lista de
  fotogramas como los ajustes de control de las imágenes. Un fotograma nuevo va a ese mismo.
* El botón **«+»** pregunta el nombre y crea otro conjunto de datos, haciéndolo actual
  enseguida.
* Los conjuntos de datos son varios porque **los distintos modelos tienen requisitos distintos
  para las imágenes**: uno aprende con PNG de 768×512 y otro pide 1024×1024. Mantener un único
  juego de fotogramas para todos significa recomponerlo cada vez que se cambia de modelo.

La elección se recuerda **en el objeto**, no en la ventana: con el conjunto de datos actual irá
también el entrenamiento que se lance después.

### 4.0.1. Los ajustes de control de las imágenes

La sección **«Control de las imágenes del conjunto de datos»** son los límites hasta los que se
comprime un fotograma al añadirlo **a este conjunto de datos**: anchura, altura, peso en KB,
formato, y también cuántos fotogramas hacen falta como mínimo y cuántos tiene sentido coger como
máximo.

* Al crear el conjunto de datos **se copian de los ajustes generales** de la aplicación
  (**Ajustes → General → «Fotograma del conjunto de datos LoRA»**) y a partir de ahí viven su
  propia vida.
* El botón **«Tomar del modelo»** pone lo que el modelo ha declarado en el catálogo. En la lista
  están sólo aquellos modelos de la pestaña «Modelos» de este objeto que tienen rellenos los
  ajustes de control en el catálogo: de un modelo que calla no hay nada que tomar. Si el modelo
  nombra varios formatos, se pone **PNG**, que es sin pérdidas.
* Los valores modificados se escriben con el botón **«Guardar»**; los fotogramas ya añadidos no
  se rehacen con ello: los nuevos límites se aplican al siguiente fotograma.

### 4.1. Cómo añadir un fotograma

El botón «+» abre el formulario del fotograma. Después vienen dos pasos, y no sobran.

**Paso 1. Coger el original.** O bien **«Por dirección»** (`http://…`, la imagen la descargará el
servidor), o bien **«Archivo del ordenador»**: el botón de la carpeta abre el recorrido por los
discos de este ordenador. El recorrido empieza:

* por la carpeta que ya esté escrita en el campo de la ruta, si la hay;
* si no, por la carpeta **de la que se cogió el archivo la última vez**;
* si no, por la **carpeta del proyecto**;
* y sólo si no hay nada de eso, por la lista de discos.

La carpeta de la última elección se recuerda para siempre (sobrevive tanto al cierre de la
ventana como a la siguiente entrada), así que un lote de fotogramas de una misma carpeta se
añade sin volver a deambular por los discos. La carpeta puede haber dejado de existir: entonces
el recorrido empezará por el padre existente más cercano, en lugar de mostrar una lista vacía.

Pulse **«Cargar»** y el original irá al almacenamiento, y aparecerá el marco de recorte.

**Paso 2. Recortar el fotograma.** Tire de la esquina superior izquierda y de la inferior
derecha del marco: al fotograma irá lo que quede dentro de él. El recorte, la compresión y la
conversión de formato se hacen **en el navegador**: al servidor se va ya la imagen terminada.
Debajo del marco está escrito hasta qué punto se comprimirá el fotograma: los límites se toman
de los **ajustes de control de las imágenes de ESTE conjunto de datos** (véase 4.0.1), no de los
ajustes generales de la aplicación, que sólo fueron una instantánea suya el día de su creación.
La relación de aspecto no cambia, y se toma el menor de los dos coeficientes. Si no entra en el
límite de tamaño, reduzca el marco o suba el límite en los ajustes del conjunto de datos.

El fotograma va a la **carpeta de datos de la organización**, a la subcarpeta
`projects/<código del proyecto>/objects/<código del objeto>/<código del conjunto de datos>/`, y
se crea como objeto del tipo «fotograma de referencia», hijo del **conjunto de datos**. Por eso
los fotogramas se ven también en la lista general de objetos del proyecto.

El sitio no está elegido por casualidad: la carpeta de datos de la organización **se replica**,
así que el conjunto de datos compuesto aquí se ve también en los demás servidores del clúster,
donde se puede abrir, mirar los fotogramas y lanzar con ellos un entrenamiento. La carpeta del
proyecto es propia de cada servidor y no se difunde por el clúster, así que los fotogramas que
estaban en ella (como ocurría hasta la versión 1.112) no se mostraban en absoluto en un servidor
vecino. Los fotogramas acumulados por versiones anteriores se trasladan solos en el primer
arranque de la versión nueva; los archivos originales de la carpeta del proyecto no se eliminan
con ello: esa carpeta le pertenece a usted.

### 4.2. El pie del fotograma: qué escribir

El campo **«Descripción del fotograma»** es el **único texto que ve el entrenamiento**. Junto a
cada imagen de la carpeta del conjunto de datos se pone como archivo `.txt` con el mismo nombre
(en algunos modelos, en un archivo común `captions.json`; cuál de las dos cosas se dice en el
perfil del modelo).

Recomendaciones comprobadas en la práctica del entrenamiento de LoRA:

* **describa el fotograma, no al personaje en general.** El entrenamiento aprenderá aquello que
  es igual en todos los fotogramas, y eso no hace falta describirlo. Se describe lo que
  **cambia** de un fotograma a otro: el ángulo, el plano, la luz, el fondo, la ropa, la expresión
  de la cara;
* **empiece por la palabra clave**, si la tiene: `m4sha_rf, primer plano, de tres cuartos hacia
  la izquierda, luz de día, fondo de calle desenfocado`;
* **no escriba aquello que no quiere que se aprenda.** Todo lo que se nombra en el pie el modelo
  lo toma por parte variable y aprende a manejarlo; todo lo que no se nombra lo funde en el
  propio adaptador. De ahí la regla habitual: el aspecto permanente del personaje no se describe,
  y el fondo y la ropa sí, porque si no el adaptador aprenderá a machamartillo tanto la chaqueta
  como la pared de ladrillo;
* **escriba en enumeraciones cortas separadas por comas**, no en párrafos: los scripts de
  entrenamiento cortan el pie por longitud, y una frase larga pierde la cola;
* **un solo idioma en todo el conjunto de datos.** Mezclar idiomas en los pies es una forma
  segura de obtener un adaptador que responde una de cada dos veces;
* **no deje el pie vacío**: un `.txt` vacío significa «no cambia nada», y el entrenamiento fundirá
  en el adaptador el fotograma entero, con el fondo incluido.

El campo **«Nombre»** del formulario de edición del fotograma es el nombre del **objeto**, no el
nombre del archivo: el archivo ya está en la carpeta del proyecto con su nombre y no se
renombra. Al añadirlo, en cambio, el nombre se convierte en el nombre del archivo (al que se le
añade el código del objeto para que no coincidan los nombres de personajes distintos).

El botón «modificar» edita sólo el nombre y el pie. No hay con qué recortar de nuevo un
fotograma ya añadido: para eso se elimina y se vuelve a añadir a partir del mismo original. El
botón «eliminar» quita el fotograma del conjunto de datos, y **el archivo de la carpeta del
proyecto se queda**: podría haber llegado ahí sin pasar por el editor.

### 4.3. ¿Importa el orden de los fotogramas?

La respuesta corta: **para el entrenamiento en sí, no; para la selección, sí.**

El orden de la lista es el orden de **adición** (los fotogramas están numerados como objetos:
OBJ-12, OBJ-13, …), y en ese mismo orden llegan a la carpeta del conjunto de datos con los
nombres `001.png`, `002.png`, `003.png`… No hay con qué reordenar los fotogramas en el editor:
para que un fotograma pase a ser el primero, habrá que eliminarlo y volver a añadirlo.

Dónde importa el orden de verdad:

1. **En el corte por el límite superior.** El modelo tiene indicado en el perfil cuántos
   fotogramas coge (`dataset.maxItems`; en los registros de Kandinsky que vienen incluidos, 60).
   Si hay más fotogramas, se cogen los **primeros**, y los demás no participan, en silencio. Es
   decir, los mejores y más característicos deben añadirse **antes**.
2. **En la numeración de los archivos.** Por el número `001…NNN` es como después se relaciona un
   fotograma con lo que se ha visto en el registro del script de entrenamiento. El orden de los
   números es el orden de adición.

Dónde no importa el orden: el propio proceso de entrenamiento recorre el conjunto de datos
muchas veces y lo baraja, así que el «primer» fotograma no influye en el resultado más que el
«último». Aparte, conviene recordar el límite inferior (`dataset.minItems`; en Kandinsky, 10): si
hay menos, la negativa «Hay N fotogramas en el conjunto de datos, y el entrenamiento de este
modelo exige no menos de M».

Otra regla de selección: al entrenamiento sólo van los fotogramas **activados**. Un fotograma se
puede desactivar sin eliminarlo: abrirlo como objeto (pestaña «Objetos» del proyecto) y
desmarcar la casilla «activo». Es la única forma de sacar del entrenamiento un ángulo fallido
sin perder el archivo.

---

## 5. La pestaña «Modelos» y el lanzamiento del entrenamiento

Un adaptador se entrena **para un modelo concreto**: los pesos de los modelos son distintos, y
un archivo entrenado para uno no le vale a otro. Por eso hay tantas filas de entrenamiento como
modelos para los que entrena usted el adaptador.

El entrenamiento va siempre **con el conjunto de datos actual**, el que está elegido en la
pestaña «Conjunto de datos».

1. El botón «+» sirve para elegir el modelo. En la lista están los registros **activos** del
   catálogo que tengan declarado en el perfil el trabajo con LoRA; las filas ya creadas no se
   ofrecen por segunda vez. Una lista vacía significa exactamente una cosa: en este servidor no
   hay ningún registro activado que sirva.
2. El botón **«Entrenar»** de la fila es el comienzo. Desde el formulario el entrenamiento
   **no se lanza**: se plantea como una **tarea** aparte. Primero el sistema hace las preguntas
   sobre el conjunto de datos, luego propone elegir una **plantilla de tarea** y pregunta qué
   hacer después: «crear y lanzar», «solo crear» o «cancelar». El orden detallado está más
   abajo.
3. La fila pasa al estado **«entrenándose»**, y el entrenamiento en sí dura horas. El
   formulario se puede cerrar, que el entrenamiento no se interrumpirá; al volver a abrirlo
   verá el estado actual (mientras haya en la lista una fila entrenándose, se relee sola cada
   cinco segundos).
4. **El estado de la fila es un enlace a la ficha de la tarea**, y no solo en «entrenándose»: a
   un entrenamiento caído se viene un día después justamente a por el registro. Allí está
   también la parada: detener el entrenamiento ahora significa detener la tarea.
5. Una fila ya entrenada se reentrena tras una reconfirmación: el reentrenamiento **machaca el
   archivo terminado** del adaptador, que podría estar ya puesto en el modelo, y eso no hay con
   qué deshacerlo.

Estados de la fila: **sin entrenar → entrenándose → entrenado**, o bien **error**. En la
indicación del error (ponga el ratón sobre la palabra «error») está lo que han dicho el programa
de entrenamiento o el proveedor; sin eso, la palabra «error» no sirve de nada.

### Sobre qué vuelve a preguntar el sistema antes del lanzamiento

Además de la reconfirmación del reentrenamiento son dos, y ambas van de que, si no, el
entrenamiento haría en silencio algo distinto de lo que usted espera.

* **«El conjunto de datos no es ese».** La fila de entrenamiento recuerda con qué conjunto de
  datos se obtuvo el archivo que tiene. Si ahora hay elegido otro, el sistema nombrará los dos y
  preguntará con cuál lanzarlo: «con el actual» o «con el anterior». Un adaptador entrenado con
  otros fotogramas es, en el fondo, otro personaje, y no se puede sustituir en silencio.
* **«Los ajustes son mayores que los declarados por el modelo».** Los ajustes de control de las
  imágenes del conjunto de datos se cotejan con lo que el modelo ha declarado en el catálogo, y
  sólo **al alza**: un fotograma mayor, más pesado, más fotogramas, o un formato que el modelo no
  admite. Un conjunto de datos con valores **menores** que los declarados es legítimo y no se
  pregunta por él. En la pregunta se enumera qué es exactamente lo que no cuadra, y las
  respuestas son **tres**: «Entrenar» (tal cual), «Cancelar» y **«Crear/cambiar al conjunto de
  datos adecuado»**.

Ambas condiciones las comprueba el **servidor**, no sólo el formulario: el entrenamiento se lanza
también desde fuera de esta ventana.

### «Crear/cambiar al conjunto de datos adecuado»

El botón evita el trabajo manual de «crea un conjunto de datos, cópiale los ajustes del modelo,
comprime medio centenar de imágenes». Se hace así:

1. **Primero se busca uno adecuado entre los conjuntos de datos ya creados** del objeto: que sus
   ajustes quepan en los declarados por el modelo Y que contenga los mismos fotogramas (cotejo
   por los nombres de archivo sin extensión). Si se encuentra, simplemente pasa a ser el actual y
   no se comprime nada.
2. Si no se encuentra, se crea un conjunto de datos **nuevo** con los ajustes tomados del modelo
   y con un nombre del tipo «Personaje 768×512 PNG». Pasa a ser el actual de inmediato.
3. Los fotogramas del conjunto de datos original **se comprimen** hacia él. La regla de
   compresión: la relación de aspecto no cambia y **no se puede recortar la imagen**; se reduce
   por el lado mayor, se coloca en el centro del fotograma y los márgenes restantes se rellenan
   con el color del ajuste «Ajustes → Fotograma del conjunto de datos LoRA → Color de los
   márgenes» («#RRGGBBAA»; por defecto `#FFFFFF00`, blanco con transparencia total: en PNG los
   márgenes salen transparentes, y en JPEG, donde no hay canal alfa en absoluto, blancos). El
   sistema no ampliará un original pequeño.
4. Después de eso el entrenamiento se lanza con el conjunto de datos nuevo; sobre el hecho de que
   el conjunto de datos haya cambiado no se pregunta por segunda vez: eso ya fue su respuesta.

Los fotogramas que no se han podido comprimir (no está el archivo, no han cabido en el límite de
kilobytes) se nombran en un mensaje aparte: el conjunto de datos con los demás fotogramas se
compone igualmente.

### El entrenamiento va como tarea: plantilla, consola y registro

El entrenamiento no es trabajo de un minuto, y en el sistema tiene el aspecto de una tarea
normal: tiene ejecutor, consola, registro, historia y resultado. Su ejecutor es del tercer
tipo, **«software automático»** (véase [Ejecutores](performers.md)): el par «complemento
entrenador + operación de entrenamiento».

Qué ocurre al pulsar «Entrenar», por orden:

1. **Las preguntas sobre el conjunto de datos**, las descritas arriba: son sobre «con qué
   entrenar» y van primero.
2. **El sistema mira con qué y con qué plantilla puede lanzarlo**: en una sola consulta averigua
   si hay complemento entrenador, si hay ejecutor de «software automático» y si hay plantillas
   de tarea aptas. De la respuesta depende lo que verá usted:

| Qué falta | Qué propone el formulario |
|---|---|
| no hay entrenador en absoluto | negativa con palabras: instale el programa en «Ajustes → Complementos y MCP» |
| el complemento está, pero no listo | abrir la ventana de instalación del complemento y traer el programa |
| no hay ejecutor | crearlo a partir del registro de «Complementos y MCP», con un solo botón |
| no hay plantilla de tarea | crear un nodo de plantilla con ese ejecutor, con un solo botón |
| todo está en su sitio | la ventana de elección de plantilla y tres respuestas |

3. **La ventana de elección**: la plantilla de tarea y una de tres respuestas: **«crear y
   lanzar»**, **«solo crear»** o **«cancelar»**. «Solo crear» hace falta cuando el
   entrenamiento debe ir de noche o después de otro trabajo: la tarea se crea y espera al
   lanzamiento habitual.
4. **El título, la descripción, el ejecutor y las etiquetas no los escribe usted en ninguna
   parte**: los trae el nodo de plantilla junto con su experiencia. Se corrigen en la
   plantilla, una vez para todos los entrenamientos.
5. El complemento entrenador lo busca el sistema **por los paquetes de entrenamiento**
   nombrados por el modelo, con la misma regla con la que se crea el registro del complemento
   al instalar el modelo. La operación la elige él solo: la marcada como «una sola instancia»
   (en el entrenador es justamente el entrenamiento, la tarjeta gráfica es una).

Esa tarea no necesita campos propios de parámetros: la fila de entrenamiento recuerda su tarea,
y por la tarea se encuentra la fila, en la que ya están el objeto, el conjunto de datos, el
modelo y toda la configuración del entrenamiento.

**Dónde mirar cómo va la cosa.** En la ficha de la tarea: la consola muestra la salida del
entrenador en directo y el registro del trabajo la guarda entera. El error queda después en
tres sitios a la vez: en la consola, en el artefacto del trabajo y en la indicación de la
palabra «error» de la fila de entrenamiento.

**Los guardianes contra «dos veces lo mismo» son dos, y son de cosas distintas**: a una fila
que ya se está entrenando el botón le dice que no en el acto, mientras que una segunda tarea
sobre la misma operación del entrenador **espera** en la cola mientras la tarjeta gráfica está
ocupada.

### Qué hace el sistema mientras la fila está «entrenándose»

1. **Compone el conjunto de datos** en la carpeta indicada en el perfil del modelo (en Kandinsky
   es `lora/<código del objeto>/dataset` desde la carpeta del proyecto). La carpeta **se vacía**
   antes de componerlo: de lo contrario, un fotograma retirado seguiría participando en el
   entrenamiento. Las imágenes se copian como `001.<ext>`, `002.<ext>`…, y los pies, al lado, como
   `.txt` del mismo nombre. Se cogen los fotogramas **del conjunto de datos con el que se
   lanzó**; en el comando se puede poner también su código: `{datasetId}`.
2. **Comprueba los paquetes de entrenamiento** nombrados por el modelo (`lora.train.packages`);
   en Kandinsky son `musubi-tuner` y `python`. El entrenamiento no se pone a instalarlos: llegan
   con la instalación del modelo, donde hay para ello una ventana con la marcha del trabajo. Si
   falta algo, la negativa llega enseguida, en el momento de pulsar el botón, y en ella se dice
   qué hay que abrir y qué hay que pulsar.
3. **Escribe los archivos de configuración** del entrenador (`lora.train.files`) junto al
   conjunto de datos; en Kandinsky son `dataset.toml` y `train.cmd`. Se reescriben en cada
   lanzamiento.
4. **Lanza** el comando de entrenamiento del perfil del modelo (en un proceso oculto aparte) o
   bien llama al servicio de entrenamiento por HTTP. En el comando y en los archivos de
   configuración se sustituyen: `{object}`, el código del objeto; `{name}`, su nombre;
   `{dataset}`, la carpeta del conjunto de datos; `{output}`, la carpeta del resultado;
   `{steps}`, el número de pasos; `{width}`/`{height}`, el tamaño del fotograma según el ajuste
   del conjunto de datos; y también las rutas de instalación del modelo: `{modelsRepo}`,
   `{groupDir}`, `{model:<archivo>}`, el archivo de pesos, y
   `{package:<código>:<archivo>}`, el archivo de un paquete instalado. **La descripción del
   objeto no se sustituye**: todo el texto que ve el entrenamiento está en los pies de los
   fotogramas.
5. **Espera** el final del trabajo. No se espera más allá del plazo del perfil (por defecto 720
   minutos, es decir, 12 horas): un entrenamiento colgado tiene que convertirse alguna vez en un
   error, y no quedarse para siempre en el estado «entrenándose». Un código de retorno distinto
   de cero también es un error, y las últimas líneas de la salida entran en su texto.
6. **Recoge el archivo** del adaptador y lo pone en el repositorio de modelos, de donde lo toma el
   propio motor (en ComfyUI es la subcarpeta `loras`, con el nombre por defecto
   `<código del objeto>.safetensors`).
7. **Le pone al objeto** la ruta de ese archivo y el estado «listo», es decir, que desde ese
   momento el adaptador se conecta al modelo por sí solo.

Los pasos 4–7 van **dentro del trabajo de la tarea**: la salida del entrenador se lee sin parar
y llega a la consola de la ficha en lugar de acumularse en la tubería. Los límites de espera
son dos: el general (720 minutos por defecto) y el **tiempo de espera del silencio** (30
minutos por defecto: eso es lo que puede tardar la lectura de los pesos de un codificador de
texto). Un programa que no ha dicho ni una línea durante más tiempo se considera colgado y se
apaga: doce horas de silencio ya no ocurren con ninguna configuración.

---

## 6. Qué campos van adónde

| Campo | Dónde se rellena | Adónde va |
|---|---|---|
| «Descripción» del adaptador (o sea, el «Pasaporte») | formulario del editor, formulario del objeto | al **prompt de la generación**, en lugar de la referencia `@obj:`; al entrenamiento NO va |
| «Descripción del fotograma» | formulario del fotograma del conjunto de datos | al **entrenamiento**: el archivo `NNN.txt` junto a la imagen (o el `captions.json` común) |
| «Nombre» del objeto adaptador | formulario del objeto | la sustitución `{name}` en el comando de entrenamiento; la referencia `@obj:[Nombre]` |
| Código del objeto (OBJ-N) | se asigna solo | la sustitución `{object}`: la carpeta del conjunto de datos, el nombre del archivo del adaptador |
| «Nombre» del fotograma | formulario del fotograma | el nombre del archivo en la carpeta del proyecto (al añadirlo) y el nombre del objeto fotograma |
| «Archivo del adaptador» y «Estado del adaptador» | formulario del objeto | la conexión del adaptador al modelo durante la generación; tras un entrenamiento correcto se rellenan solos |

---

## 7. Cómo llega el adaptador entrenado a la generación

No hay una acción «conectar el adaptador» aparte. Funciona la regla de la referencia: ponga en la
descripción de una tarea de medios una referencia al objeto (`@obj:OBJ-7` o `@obj:[Nombre]`), y
antes de lanzar el encargo el sistema decidirá qué irá al modelo:

* una referencia a un objeto **del tipo «adaptador LoRA»**: el adaptador es **obligatorio**; el
  modelo tiene que admitirlo, y él mismo tiene que estar entrenado. Si no, el encargo falla con
  error;
* una referencia a **cualquier objeto con adaptador terminado** (un personaje con el «Archivo del
  adaptador» relleno): el adaptador se conecta, que para eso lo entrenó la persona;
* un adaptador **en curso** («planificado», «entrenándose»): sólo una observación en la consola
  del encargo; el fotograma se hace según el pasaporte;
* un personaje, una localización o un estilo sin adaptador: como antes, el pasaporte va al prompt
  y los fotogramas de referencia de los hijos son candidatos a fotograma inicial.

Está hecho como error a propósito: un fotograma generado en silencio sin adaptador tiene buen
aspecto y se entrega como resultado terminado, pero el personaje que hay en él es otro.

---

## 8. Errores frecuentes

| Mensaje | Qué ha pasado y qué hacer |
|---|---|
| «El proyecto no tiene carpeta en este servidor» | el proyecto no tiene carpeta indicada **en el servidor donde va el entrenamiento**; la carpeta es propia de cada servidor y se indica en la ficha del proyecto |
| «En el catálogo de modelos no se dice con qué entrenar» | en el perfil del modelo está vacío el comando de entrenamiento; escríbalo (el enlace a la instrucción está en el perfil) |
| «El adaptador del modelo … se entrena fuera del sistema» | en ese registro el entrenamiento es externo: entrénelo con sus medios y escriba la ruta del archivo en el campo «Archivo del adaptador» |
| «Hay N fotogramas en el conjunto de datos, y el entrenamiento exige no menos de M» | añada fotogramas: en los registros de Kandinsky que vienen incluidos el límite inferior es 10 |
| «El archivo del fotograma no está en la carpeta del proyecto» | el archivo del fotograma se ha eliminado o renombrado al margen de AI2P; elimine el fotograma y añádalo de nuevo |
| «El fotograma pesa N KB / el fotograma es de N×M puntos» | los límites de este conjunto de datos son menores que lo enviado; reduzca el marco o suba los límites en «Control de las imágenes del conjunto de datos» |
| «El conjunto de datos no se ha encontrado o pertenece a otro objeto» | el conjunto de datos elegido se ha eliminado o es ajeno: elija de nuevo el conjunto de datos |
| «La última vez se entrenó con el conjunto de datos …» | el conjunto de datos ha cambiado desde el entrenamiento anterior; responda con cuál lanzarlo |
| «Los ajustes del conjunto de datos son mayores que los declarados por el modelo» | el cotejo con el catálogo ha encontrado un exceso o un formato ajeno: entrenar de todos modos, pulsar «Crear/cambiar al conjunto de datos adecuado» o bien tomar los ajustes del modelo y recomponer los fotogramas a mano |
| «Un fotograma del conjunto de datos sólo puede ser una imagen» | en la ruta indicada no hay una imagen |
| «El entrenamiento no ha terminado en N min y se ha interrumpido» | no ha bastado el plazo del perfil; suba `wait.timeoutMinutes` o reduzca el número de pasos |
| «El entrenamiento ha terminado con el código N» | se ha caído el propio programa de entrenamiento; sus últimas líneas están en la indicación de la palabra «error» |
| «El entrenamiento no ha dejado archivo de adaptador» | el comando ha funcionado, pero en la ruta `result.path` no hay archivo: coteje la ruta del perfil con el sitio donde deja el resultado su script |
| «El entrenamiento no ha impreso ni una línea en N min» | ha saltado el tiempo de espera del silencio: el programa se ha colgado. Mire las últimas líneas en la consola de la tarea y en el registro del trabajo; el plazo se indica en `wait.idleMinutes` del perfil del modelo |

---

## 9. Lo que el editor no hace

La lista honesta de las limitaciones de esta versión, para no buscar lo que no existe:

* **el orden de los fotogramas no se cambia**: sólo eliminando y volviendo a añadir;
* **no se puede recortar de nuevo un fotograma añadido**: el marco de recorte sólo está
  disponible al añadirlo;
* **no se puede desactivar un fotograma desde el editor**: la casilla «Activo» vive en el
  formulario del objeto fotograma, y un fotograma desactivado no está marcado de ninguna manera
  en la lista del editor (aunque al entrenamiento no va);
* **el envío del conjunto de datos al proveedor** (el entrenamiento en la nube con un archivo
  comprimido de fotogramas) no está hecho: el entrenamiento va con un proceso propio o por HTTP a
  un servicio de entrenamiento propio;
* **los fotogramas no se trasladan entre conjuntos de datos con un botón**: un conjunto de datos
  es un objeto normal, y un fotograma se traslada arrastrándolo en la vista «jerarquía» de la
  pestaña «Objetos»;
* **cambiar los ajustes de control NO rehace los fotogramas ya añadidos**: los nuevos límites se
  aplican al siguiente fotograma;
* **no hay progreso del entrenamiento en porcentaje**: hay el estado de la fila, el texto del
  error y la salida en directo del propio programa de entrenamiento en la consola de la ficha
  de la tarea (allí mismo está el registro del trabajo entero);
* **varios adaptadores a la vez** normalmente el modelo no los coge (`apply.maxCount`; en los
  registros que vienen incluidos, 1): si en la descripción de la tarea se nombran más objetos con
  adaptador, el encargo responde con un error en lugar de coger el primero en silencio.

---

## Véase también

* [Vídeo a partir del fotograma de referencia de un personaje](sample_video1.md): por dónde
  empieza el trabajo con un modelo de medios: instalación de un modelo local, ejecutor de IA,
  objeto personaje y referencia `@obj:`.
* [Proyectos](progects.md): los objetos del proyecto, el pasaporte del objeto y la carpeta del
  proyecto.
* [Índice de la sección](README.md): los demás capítulos del manual.

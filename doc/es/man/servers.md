# Varios servidores

**Un servidor es una instalación de AI2P en un ordenador aparte.** Varias instalaciones así se
unen en un **clúster**: tienen datos de la organización comunes, pero cada una trabaja con sus
propias manos.

La lista se abre en **Ajustes → Servidores**. La pestaña la ve el rol **admin** (el dueño de
este ordenador) y el **administrador del servidor** por acceso local.

> Para una `instalación única` este capítulo no hace ninguna falta: mientras el servidor sea
> uno, es su propio director, y nada de lo descrito más abajo ocurre.

---

## 1. Para qué varios

Cuatro motivos, y todos son de física, no de escalado.

**La tarjeta gráfica es una y el trabajo es variado.** Un modelo local son gigabytes de pesos y
un proceso que calcula en una tarjeta gráfica concreta. No se muda. Por eso la máquina con la
tarjeta potente se convierte en el servidor donde se ejecutan las tareas de medios y el
entrenamiento de LoRA, y las tareas de texto ligeras van a cualquier parte.

**Las personas están sentadas ante ordenadores distintos.** Cada uno trabaja en su máquina, con
sus archivos de proyecto y sus rutas; a la vez, las tareas, el chat, las plantillas y la
experiencia son comunes para todos.

**El trabajo continúa mientras el ordenador ajeno está apagado.** Los datos se replican, no
están en un solo servidor: un vecino apagado no detiene a nadie, ya se pondrá al día cuando se
encienda.

**Los archivos deben estar junto a quien los hace.** El encargo se ejecuta allí donde están los
archivos de la tarea, y el resultado va a ese mismo sitio. No hay lanzamiento entre servidores
en el sistema, y es a propósito.

Lo que el clúster **no** hace: no es tolerancia a fallos ni reparto de carga. La tarea se
ejecuta en su propietario y nadie la recogerá.

---

## 2. De qué se compone

### El director

En cada organización hay **exactamente un** servidor director. Reparte los códigos a los
servidores, lleva los catálogos de la organización, siembra las reglas generales de trabajo y
ejecuta las programaciones cuyo servidor no está indicado. (En una **tarea** el servidor está
siempre puesto: una tarea nunca es «de nadie».) La topología es de **estrella**: todos los
servidores se replican **sólo con el director** y entre sí no hablan.

Un mismo servidor puede ser director de una organización y participante corriente de otra: la
dirección pertenece a la **relación** «organización ↔ servidor», no al servidor en sí.

### Servidor y organización, «muchos a muchos»

Un servidor atiende a varias organizaciones y una organización vive en varios servidores. Por
eso los registros de los servidores están en la base **del servidor**, fuera de las
organizaciones, y la relación guarda el **código del servidor en la organización**: `S0`, `S1`,
… El primero recibe `S0`, y a los demás les da el código el director.

El código forma parte de los números de las tareas (`T-18-S1`) y por eso **no se reutiliza**
después de sacar un servidor del clúster: de lo contrario los números serían ambiguos.

### Los servidores se reconocen por la clave, no por la dirección

Cada servidor tiene una **clave interna**, entregada una sola vez en el primer arranque y que no
cambia jamás. La dirección es sólo una forma de llamar, y no siempre es única: detrás de un NAT
suele tener dirección pública un solo servidor, y los demás se llaman a sí mismos `localhost`.
Por eso **el sistema no prohíbe dos registros con la misma dirección**: sólo cuenta la clave.

Un registro puede tener también una **segunda dirección, la externa**: un nombre público o la
dirección del router con el puerto redirigido. Se replica junto con el registro (la dirección
externa propia sólo la conoce el propio servidor, y quienes tienen que llamar por ella son los
vecinos), y al establecer contacto las direcciones se prueban por turno.

---

## 3. Propiedad: quién edita qué

Los datos son comunes, pero los edita el **propietario de la fila**.

* La **tarea** tiene un servidor propietario: allí se edita, allí se ejecutan sus encargos y
  allí están sus archivos. En los demás servidores se ve en modo lectura, pero **«lanzar»,
  «lanzar la jerarquía» y «escribir una entrada nueva en el chat» sí están disponibles**: el
  lanzamiento se va al propietario como solicitud y el mensaje llegará por replicación.
* Los **lanzamientos automáticos** (de las hijas, el desbloqueo, la división) los lleva el
  propietario de la tarea, y sólo él. Sin esa regla un solo disparo habría generado un encargo en
  cada servidor. Una fila llegada de un interlocutor de versión antigua sin servidor la coge para
  sí el director al abrir la organización.
* La **programación** también tiene su propio servidor, y sólo se dispara en él.
* **La carpeta del proyecto y el atributo «activo» del proyecto son propios de cada servidor**:
  las carpetas son distintas en ordenadores distintos (véase [Proyectos](progects.md)).
* **La lista de ejecutores se edita sólo en el director**: los ejecutores se asignan a tareas de
  todos los servidores y la lista tiene que ser una sola.

Se puede cambiar el propietario de una tarea con el botón **«cambiar de servidor»** de su ficha;
el número de la tarea **no cambia** con ello: es su identificador y el nombre de la carpeta de
sus archivos.

---

## 4. Cómo conectar un segundo ordenador

Siempre se conectan **al director**, y la decisión la toma la persona que está de su lado.

**En la máquina nueva.** Instale AI2P y en el primer paso del asistente **desmarque la casilla
«servidor principal del clúster»**. Entonces la organización no se crea aquí (llegará por
replicación con sus propios identificadores) y en el último paso del asistente se abre la
pantalla **«Conexión al clúster»**: la dirección del director (`host:5480/ai2p`; el protocolo y
el prefijo se pueden omitir), el código de su organización (opcional: si allí hay una sola, se
toma esa), su firma y una nota para quien la recibe.

Si el director tiene varias organizaciones, responderá con la lista de ellas, y se pueden elegir
**varias a la vez**.

**En un servidor que ya funciona** se hace lo mismo desde el formulario del servidor remoto en la
pestaña «Servidores».

**En el director.** La solicitud aparece como una fila aparte al final de la lista de servidores
y de forma modal al propietario o al administrador que esté trabajando en ese momento. Tres
decisiones: **ACEPTAR**, **APLAZAR**, **RECHAZAR**. En el formulario se ve la **participación
del solicitante en la organización**: si aún no es ejecutor, hay marcada una casilla **«crear un
ejecutor»**, y no conviene desmarcarla, porque si no el servidor se conectará pero la persona se
encontrará en su lado con un «usted no pertenece a ninguna organización».

Tras la confirmación, el servidor conectado recibe un token de acceso y la clave de la
organización, crea la organización en su lado y la llena por replicación. **La primera sesión la
inicia él mismo**: el director no llama a un servidor que está detrás de un NAT.

Lo que conviene saber de antemano:

* **una cuenta con contraseña vacía no puede conectarse por la red**: la contraseña se pone de
  antemano;
* la solicitud es **anónima**: no pide ni correo ni contraseña en el director. La única excepción
  es que la persona con ese correo **ya exista** en el director: entonces se pide la contraseña
  **del director**, y con ella se entrega la misma clave interna, para que en el clúster no haya
  dos cuentas con un mismo correo;
* un **correo o un nombre duplicados** los indica el director **en respuesta a la solicitud**, no
  al confirmarla: hay que corregirlo allí donde la persona está delante de la pantalla, con el
  botón «Atrás» hasta el paso «Usuario»;
* la **casilla «mostrar todas las solicitudes»** que hay encima de la lista añade el historial de
  ambos tipos de solicitud; sin ella sólo se ve aquello con lo que todavía hay que hacer algo.

### Sacar un servidor del clúster

No se traslada nada automáticamente. El servidor se marca como **no activo**, y después, en el
director, aparece junto a él el botón **«transferir las tareas al director»**: de lo contrario
las tareas del servidor retirado quedarían sin poder editarse para siempre.

**Eliminar** el registro del todo sólo se puede mientras no haya habido ni una sola replicación
correcta con ese servidor (así se quita un primer intento fallido); con ello se libera también el
código entregado.

### Cambiar de director

Es una **solicitud bilateral**, no un botón. Puede enviarla el servidor que se nombra o el
director actual, y sólo con el **acceso local de administrador del servidor**; un tercer servidor
no se mete en un traspaso ajeno. Un servidor con la dirección `localhost` no puede ser director:
los demás, con esa dirección, se llamarían a sí mismos.

La solicitud va a la otra parte por la replicación normal y la decisión vuelve igual. La
solicitud simultánea en una organización es **una sola**.

Aparte está previsto el caso del **director perdido**: en una solicitud enviada por el propio
servidor que se nombra corre una cuenta atrás de **12 horas**, tras la cual aparece el botón
«Aceptar de forma unilateral». Sin él, una organización cuyo director hubiera muerto
físicamente se habría quedado sin director para siempre. Lo aceptado así se marca aparte: no
hubo acuerdo de la otra parte.

El nuevo director **se anuncia a los demás directamente** (la firma es con la clave de la
organización), porque puede que con él no hayan hablado ni una vez; el anuncio se repite una vez
por minuto mientras queden servidores sin el par de tokens: el vecino apagado se enterará cuando
se encienda.

---

## 5. Cómo se replican

### La base, con un registro de cambios

Cada edición de una tabla replicable pone una fila en el registro de cambios: qué tabla, la clave
de la fila, la operación, la **hora en el autor** y el **nodo autor**. La replicación se reduce
después a «dame los cambios posteriores a mi cursor».

El registro lo escriben los **disparadores de SQLite**, de modo que los servicios no saben nada
de la replicación y una entidad nueva se recoge sola.

**El conflicto se resuelve por la regla de «gana el último»**: el cambio que llega se compara con
nuestro último cambio de esa misma fila por el par (hora, autor); si no es más nuevo, no se
aplica. Eso mismo detiene que un cambio dé vueltas en círculo. Con la propiedad repartida, un
conflicto de verdad es raro, así que se muestra en la pantalla de diagnóstico como motivo para
investigar.

**El eco se apaga por la autoría**: al autor no se le devuelven sus propios cambios, mientras que
los cambios de un tercer servidor sí pasan a través del director.

### La sesión

La sesión va **por una organización entera**: primero una, luego otra. Es bilateral (coger lo
ajeno, entregar lo propio) y la lleva la parte a la que se le ha agotado antes la cuenta atrás;
la segunda recibe un «la sesión ya está en curso» y se salta su propio arranque. El intercambio
va por lotes: de ahí la barra de progreso y el porcentaje.

**Los intervalos son un ajuste de la pareja, no propio.** Son dos: el intervalo normal y el
intervalo de reintento tras un error (mínimo 30 y 15 segundos; vacío significa no replicar
automáticamente, sólo con el botón). Se indican en el formulario **del servidor con el que se
hace el intercambio**: no hay pareja consigo mismo, así que en el formulario del servidor propio
no están. La confirmación de una solicitud pone el intervalo ella sola (300 segundos), porque de
lo contrario una pareja recién conectada estaría callada hasta que la persona abriera el
formulario.

### Los archivos

Junto con la base viajan los archivos: los datos de la organización y la carpeta **`Common`** del
proyecto (su ruta se indica en cada servidor por separado, y sólo respecto a la carpeta del
proyecto). Dentro de `Common` funciona el filtro **`.repignore`**, con la sintaxis de
`.gitignore`.

### El servidor detrás de un NAT llama él mismo

Si el registro del interlocutor indica una **dirección de bucle local** con nuestro mismo puerto,
no se puede llamar allí: iríamos a parar a nosotros mismos. El recorrido automático **omite en
silencio** esas parejas (no es un error), el lanzamiento manual responde con una explicación y
las sesiones las inicia ese propio servidor. La hora de la sesión la marca entonces también la
parte que recibe: de lo contrario «Último intento» quedaría con una raya aunque el intercambio
esté en marcha.

---

## 6. Qué se ve en pantalla y con qué se arregla

### La lista de servidores

El servidor local es **siempre el primero**. Columnas: código en la organización, nombre,
dirección, rol (las etiquetas «local» y «director»), organizaciones, «activo» y **estado de la
replicación**.

En el estado: **«manual»** si no hay intervalo indicado, y si no, cuánto falta para la siguiente
sesión; durante la sesión, la barra de progreso y el porcentaje; en caso de error, «error» y el
tiempo que falta para el reintento, con los detalles en la indicación emergente. El propio
director no tiene ese campo: no tiene pareja propia; en el director se ven los estados de todos
los servidores, y en uno corriente, sólo su propia fila.

Al lado están el **botón de lanzamiento manual** (la flecha circular) y el botón de la **pantalla
de diagnóstico**.

### La pantalla de diagnóstico de la replicación

Una fila por cada organización en la que exista esa pareja: estado de la sesión, intervalos,
**cursores de ambas bases**, **retraso** (cuántos cambios nuestros no se ha llevado todavía el
interlocutor), recibido/entregado en la última sesión, archivos y bytes, número de conflictos y
el último de ellos, hora del último intento y de la última sesión correcta, texto del último
error y la **composición de la cola de reintentos** fila por fila (ámbito, tabla, clave, motivo,
número de intentos).

Abajo hay dos botones:

* **«lanzar la replicación»**: una sesión ahora mismo;
* **«replicación inicial»**: olvidar los cursores de la pareja, la base de comparación de
  archivos **y la cola de reintentos**; la siguiente sesión volverá a leer y cotejar todo desde
  cero. Es además la **palanca manual para una sesión colgada**: el botón suelta la pareja
  ocupada.

Con el segundo botón se cura casi todo lo que parece «las réplicas se han desincronizado»:
releer el registro desde cero es seguro, porque lo aplazado volverá a llegar, esta vez ya junto
con sus filas padre.

### Conflictos

El botón de conflictos aparece en un servidor cuando hay algo que resolver. **La versión
izquierda es la del director, y la derecha, la del servidor corriente** (la pareja se ve igual en
cualquier servidor desde el que se abra). Hay tres tipos:

* **archivo**: ruta, carpeta, tamaño y hora de cada versión; aceptar el izquierdo o el derecho, o
  renombrar cualquiera de los dos (la decisión se aplica en la siguiente replicación);
* **clave de API de la organización**: la referencia a la clave, la hora de edición y la huella
  (**los valores no se muestran**); aceptar la izquierda o la derecha, o introducir una clave
  nueva;
* **registro del catálogo de modelos de IA**: el nombre del modelo y la hora de edición; aceptar
  el izquierdo o el derecho.

---

## 7. Limitaciones que hay que conocer de antemano

* **HTTPS se activa, pero no solo.** Mientras el servidor tenga el protocolo `http`, el tráfico
  entre servidores es abierto y el clúster hay que desplegarlo en una red de confianza o a través
  de una VPN. Pasar a `https` es un trabajo aparte, con un certificado en cada máquina, y el
  certificado de una autoridad de certificación propia exige la casilla «Confiar en el
  certificado de los vecinos del clúster» o repartir el certificado raíz por todas las máquinas:
  véase [HTTPS](https.md).
* **El lanzamiento manual de una tarea sólo está disponible en su servidor** (desde otro se va
  como solicitud): el encargo se ejecuta allí donde están los archivos.
* **Los perfiles de los modelos no se replican**: en un registro personalizado del catálogo, en
  el interlocutor la columna de la configuración estará vacía.
* **Los secretos no se replican nunca**: la clave de la organización, la cuenta del administrador
  del servidor, los tokens de los servidores y la subcarpeta `secrets/` se quedan en su
  ordenador.

## Y después

* [Configuración](config.md): el formulario del servidor local, las direcciones y las carpetas.
* [HTTPS](https.md): el certificado del servidor, la configuración del navegador y el clúster por
  https.
* [Tareas](tasks.md): el servidor propietario de una tarea y el trabajo con una tarea ajena.
* [Archivado](Archives.md): los archivos también se replican, pero ni todos ni siempre.

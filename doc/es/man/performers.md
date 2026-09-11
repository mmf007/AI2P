# Ejecutores

**El ejecutor es quien hace la tarea.** Una persona o una IA: para el sistema es un mismo
concepto con el mismo conjunto de campos, y la tarea no distingue a quién se le ha encargado.
Por eso precisamente el trabajo se puede pasar de la IA a una persona y al revés sin reescribir
nada.

La lista se abre con el botón **«Ejecutores»** de la barra izquierda (y con la entrada del menú
del mismo nombre).

---

## Para qué sirve

El ejecutor responde a tres preguntas del sistema:

1. **A quién encargarlo.** Una tarea tiene exactamente un ejecutor, y la elección se limita a
   los integrantes de su equipo (véase [Equipos](teams.md)).
2. **Quién sabe hacerlo.** El ejecutor tiene una **declaración de capacidades**: qué habilidades
   domina y con qué nivel. Con ella funciona la selección automática.
3. **Con qué y a qué precio trabaja.** En la IA: el modelo, la clave, los parámetros, el precio
   por millón de tokens, los límites y el tiempo de espera. En la persona: el correo, el teléfono
   y la ocupación.

La cuenta y el ejecutor son **cosas distintas**. La cuenta (Ajustes → Usuarios) sirve para
entrar; el ejecutor sirve para trabajar. El ejecutor persona tiene un campo «cuenta» que los
enlaza; un ejecutor sin cuenta no entra en el sistema, pero puede tener tareas a su nombre: así
se da de alta a un contratista que no trabaja dentro de AI2P.

---

## Qué hay en la lista

La lista muestra aquello por lo que se elige a un ejecutor, no todo de golpe:

| Columna | Qué significa |
|---|---|
| **Alias** | el nombre externo, único en toda la organización; con él firma el ejecutor en todas partes |
| **Tipo** | persona, IA o «software automático» (un programa) |
| **Activo** | a uno no activo no se le pone en equipos ni tareas nuevos; los vínculos antiguos se mantienen |
| **Ocupado hasta** | hasta qué momento está ocupado el ejecutor (el tiempo ya pasado no se muestra) |
| **% del límite** | el gasto actual del límite del proveedor, si el proveedor lo envía |
| **Límite de la ventana** | la cuenta propia: `consumido/límite` en la ventana deslizante y cuándo se liberará |

La ordenación se hace pulsando en la cabecera de la columna (un segundo clic cambia el
sentido). Los ejecutores sin límites, al ordenar por «límite de la ventana», se van al final.

---

## El formulario del ejecutor

El formulario es más ancho que un diálogo normal y cambia según el tipo. No hay por qué
desmenuzarlo campo por campo: lo que importa son cuatro sitios donde es fácil equivocarse.

### Nombre interno

* **En la IA no es texto, sino una elección del catálogo de modelos**, y sólo entre los
  registros **activos**. Junto con el modelo se ponen de forma sincronizada el perfil (los
  parámetros de conexión) y la declaración de capacidades: en el ejecutor son «los mismos que en
  el modelo». Si se vuelve a elegir el modelo, ambos archivos cambian.
* **En la persona** es el nombre completo, y su unicidad se controla.

De ahí una regla que ahorra tiempo: **si el modelo que hace falta no está en la lista, es que no
está activo.** Un modelo en la nube sin clave de API y uno local sin los archivos descargados no
pueden estar activos. Hay que ir a Ajustes → Modelos, no buscar el error en el formulario del
ejecutor.

### El perfil y la declaración de capacidades

Ambos son archivos JSON, pero el formulario no muestra sus rutas: se editan con botones.

* El **perfil** dice cómo conectarse: proveedor, modelo, dirección, referencia a la clave,
  temperatura, tiempos de espera, configuración del entrenamiento de LoRA. En la persona el
  perfil es otro: correo, teléfono y demás datos humanos.
* La **declaración de capacidades** dice qué sabe hacer el ejecutor: la lista de habilidades con
  su puntuación de dominio, los formatos de entrada y salida, el precio (`in_per_1m` +
  `out_per_1m`). Su editor es **el mismo** para la IA, la persona y el registro del catálogo de
  modelos.

La declaración no es un adorno: una tarea con una habilidad que no está en ninguna declaración
no encontrará ejecutor jamás, y la selección automática calcula la puntuación precisamente a
partir de ella.

### Ocupación, límites y tiempo de espera (sólo en la IA)

* **«Ocupado hasta»** en la IA **no se edita**: lo pone el propio sistema cuando el proveedor
  responde «límite agotado». En la persona son una fecha y una hora normales, y borrar la fecha
  quita la ocupación.
* **«Límite de tokens por ventana» y «ventana del límite, h»**: la cuenta propia del gasto, que
  hace falta allí donde el proveedor no informa del saldo (la suscripción de Claude Code: una
  ventana de 5 horas). Ambos campos los indica la persona de forma empírica. **Vacío o 0
  significa que no hay límites indicados**, y toda la mecánica se apaga por completo: el saldo
  no se cuenta, no hay avisos y el lanzamiento va como antes.
* **«Tiempo de espera de respuesta, min»**: cuánto esperar al modelo. Vacío son 30 minutos, y
  **0 es sin límite**. Un tiempo de espera agotado es un error del encargo, no un límite: al
  ejecutor no se le marca como ocupado y el arranque no se aplaza.

Qué ocurre cuando casi no queda límite: el encargo **no se crea**, el arranque de la tarea se
aplaza hasta el momento en que se libere la ventana y al chat de la tarea va un aviso con
cuánto se ha consumido y a cuándo se ha aplazado el arranque. El aplazamiento sobrevive al
apagado del ordenador, y la persona siempre puede lanzar la tarea a la fuerza.

### El correo para las notificaciones

La casilla **«tomar el correo del usuario de acceso»** está marcada por defecto, y entonces el
campo de la dirección no existe: en su lugar se dice qué dirección se usará. Si se desmarca la
casilla y se deja el campo vacío, eso significa **«a este ejecutor no le llegan
notificaciones»**, y el formulario lo dice tal cual.

---

## El tercer tipo de ejecutor: «software automático»

Además de la persona y de la IA hay un tercer tipo: **«software automático»**. El trabajo no lo
hace una persona ni un modelo de lenguaje, sino **un programa de este equipo**: el entrenador
de adaptadores LoRA, un conversor, una utilidad de cálculo. La tarea sigue siendo una tarea
normal: con plazo, aceptación, consola, historia y resultado.

El nombre interno de ese ejecutor no es un modelo ni un nombre propio, sino un **par
«complemento + operación»**:

* el **complemento** es un registro de la sección [Complementos y MCP](plugins.md) **de este
  servidor**: es el que aporta el programa y su ruta;
* la **operación** es una ejecución larga con nombre del manifiesto del complemento (en el
  entrenador musubi es «Entrenamiento LoRA»). La operación marcada como **«Una sola
  instancia»** lleva esa marca en el formulario: mientras está ocupada, la segunda tarea
  **espera** en la cola en lugar de fallar.

En qué se diferencia del ejecutor de IA, y por qué:

| La diferencia | Por qué es así |
|---|---|
| **No hay prompt** | el programa no lee texto: todo lo que necesita llega por los ajustes declarados del registro del complemento y por rutas dentro de la carpeta del proyecto. Una línea de comandos libre no la tiene nadie |
| **Ni tokens, ni precio, ni límites** | no hay nada que contar ni a quién pagar: el formulario no tiene bloque de límites ni línea de gasto, y en «Gastos» este ejecutor no participa |
| **El servidor es obligatorio** | el programa está en un equipo concreto. El campo «servidor» se rellena con el servidor donde se dio de alta el ejecutor y nunca queda vacío: vacío significaría «en el director», y al cambiar de director todo ese trabajo se iría a otra máquina. Una tarea de otro servidor no se le puede encargar: el sistema lo rechaza con palabras |
| **Un trabajo a la vez** | mientras el programa calcula no se le da un segundo trabajo; la tarea espera igual que espera a una IA ocupada |
| **La selección automática no lo toma** | véase abajo |

**Por qué la selección automática pasa de largo.** Los dos botones —«primero la IA, luego la
persona» y «primero la persona, luego la IA»— descartan a este ejecutor **por el tipo**, antes
incluso de contar habilidades y precio. El tercer tipo no se expresa con el orden «IA →
persona», y la declaración de un programa puede decir cualquier cosa: si no, la tarea «dibuja
una imagen» se iría en silencio al entrenador LoRA. Por eso el «software automático» se asigna
**a mano**, o la tarea la crea desde una plantilla el propio botón que necesita ese trabajo
(así hace «Entrenar» en el [editor de LoRA](LoRAEditor.md)).

Se da de alta con el botón habitual «nuevo ejecutor»: tipo «software automático» y después el
complemento y la operación de las listas desplegables. No tiene ni perfil ni botón de
declaración.

---

## Cómo se elige un ejecutor

A mano, de la lista desplegable del formulario de la tarea (sólo los integrantes de su equipo).

Automáticamente, con dos botones junto al campo del ejecutor: **«primero la IA, luego una
persona»** y **«primero una persona, luego la IA»**. Los candidatos son los integrantes
**activos** del equipo de la tarea, y la actividad es doble: la del propio ejecutor y la de su
participación en ese equipo concreto. La puntuación se calcula así:

```
puntuación = bias × calidad + (1 − bias) × baratura
```

donde *calidad* es el dominio medio de las habilidades requeridas según la declaración,
*baratura* es la inversa del precio de esa misma declaración, y `bias` es el ajuste del proyecto
**«precio ↔ calidad»** (véase [Proyectos](progects.md)). Un ejecutor ocupado se omite; si están
ocupados todos los adecuados, el sistema dice el motivo y cuándo se liberarán. La elección se
explica directamente en la interfaz: calidad, precio, puntuación.

Hay también **ejecutores suplentes**: la lista «pueden sustituir al ejecutor» del formulario de
la tarea. El orden en ella es el orden de preferencia; se toma el primero que esté libre. El
ejecutor asignado no cambia con la sustitución: en la ficha aparece la línea «Trabaja:
<alias>».

---

## Las IA que conviene dar de alta enseguida

Tras el primer arranque el sistema da de alta a tres —`Jon`, `Bob` y `Stiv`— sobre los modelos
con mejores habilidades para el uso típico elegido. Es un punto de partida razonable, y he aquí
por qué son tres y no uno: **cada ejecutor de IA lleva un encargo cada vez**. Un mismo modelo
puede trabajar en varias tareas a la vez sólo a través de **ejecutores distintos**. Por eso aquí
«añadir potencia» significa «añadir un ejecutor», no «comprar una clave más grande».

---

## Casos particulares

* **Un modelo local ata el ejecutor a un servidor.** Los pesos y el comando de arranque están en
  un ordenador concreto, por eso ese ejecutor tiene un campo «servidor»; no se le puede asignar
  a la tarea de otro servidor, y el sistema se negará con palabras («trabaja sobre un modelo del
  servidor S1: pase la tarea allí»). Si desactiva un modelo local en su equipo, sus ejecutores de
  ese modelo dejarán de estar activos también; a los ejecutores de otro servidor eso no les
  afecta.
* **La lista de ejecutores se edita sólo en el director** de la organización: los ejecutores se
  asignan a tareas de todos los servidores, y la lista tiene que ser una sola. La excepción es
  la creación de un integrante cuando una cuenta entra en la organización.
* **Una cuenta, un ejecutor.** No se puede vincular una cuenta a dos ejecutores; el guardado se
  bloquea con un error comprensible.

## Y después

* [Equipos](teams.md): de los ejecutores se forman los equipos, y el círculo de ejecutores de una
  tarea lo fija precisamente el equipo.
* [Proyectos](progects.md): el ajuste «precio ↔ calidad», del que depende la selección
  automática.
* [Modelos de IA](../models/README.md): qué modelos hay detrás de los ejecutores de IA.

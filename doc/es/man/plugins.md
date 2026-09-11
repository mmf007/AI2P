# Complementos y MCP

La pestaña **«Ajustes → Complementos y MCP»** es el lugar donde se conecta a AI2P todo lo
externo: los programas instalados en su equipo (editores de vídeo, el conversor ffmpeg) y los
servidores MCP, que traen sus propias herramientas.

El sentido de la sección en una frase: **el agente de IA obtiene acciones nuevas, y sobre esas
acciones se conceden permisos**. Un complemento no es una «extensión» que pueda hacer lo que
quiera: todo lo que trae pasa por el catálogo de acciones y por las reglas de seguridad de la
tarea.

## De qué se compone un complemento

El registro de un complemento son cuatro cosas a la vez:

| Parte | Dónde vive | Qué aporta |
|---|---|---|
| **Acciones** | el catálogo de acciones, las reglas de seguridad | herramientas nuevas para el agente de IA |
| **Registros de experiencia** | la experiencia general de la organización | el agente sabe cómo usar esas herramientas |
| **Programa** | el catálogo de paquetes y el `config.json` de este servidor | el programa externo al que llama el complemento |
| **Documento** | `doc/<idioma>/plugins/<código>.md` | la descripción, el botón **«i»** de la fila |

Las dos primeras partes son **propiedad de la organización**: están en la base de datos y se
replican a todos los servidores. La tercera es **propiedad de este equipo**: el programa
encontrado y su ruta se guardan en el `config.json` del servidor y no se replican nunca. En el
equipo vecino del clúster ese mismo programa está en otra ruta, y la mitad de los servidores no
lo tienen en absoluto.

La descripción del propio complemento llega como **archivo de manifiesto**,
`plugins/<código>/plugin.json` en el directorio de datos. El manifiesto enumera las acciones,
nombra el programa que hace falta y declara los ajustes del registro; no se edita a mano en la
sección: las acciones y sus parámetros cambian solo junto con el manifiesto.

La distribución trae seis registros: pasarelas a **Shotcut / Kdenlive**, **DaVinci Resolve**,
**Blender VSE** y **OpenShot**, el **conversor de vídeo (ffmpeg)** y el **entrenador de
adaptadores LoRA (musubi-tuner)**.

## Dos tipos de registro: pasarela y conexión MCP

| | **Pasarela** (`gateway`) | **Conexión MCP** (`mcp`) |
|---|---|---|
| Qué hay al otro lado | un programa de este equipo | un servidor MCP (proceso propio o dirección de red) |
| De dónde sale la lista de acciones | del manifiesto, es fija | **se obtiene del propio servidor**, la lista es dinámica |
| Qué hace falta para que funcione | encontrar o instalar el programa | conectarse y obtener la lista de herramientas |
| Secreto | no hace falta | el token del servidor MCP, si lo exige |
| Dónde se calcula el trabajo | en nuestro lado: escribimos un archivo y llamamos al programa | al otro lado |

La diferencia que conviene recordar: en una pasarela el conjunto de posibilidades se conoce de
antemano y cambia solo con una versión nueva de AI2P, mientras que **en un servidor MCP puede
cambiar cualquier día**. Por eso la lista de herramientas de MCP se obtiene con una **acción
explícita de la persona** —el botón «Actualizar la lista de herramientas»— y esa acción muestra
qué se ha añadido y qué ha desaparecido. Un servidor MCP no puede concederse en silencio una
posibilidad nueva.

## Estados en este servidor

| Estado | Qué significa |
|---|---|
| **declarado** | el manifiesto está en el disco, todavía no hay registro de la organización: no se ha inicializado |
| **buscando el software** | el registro existe, pero el programa necesario no se ha encontrado en este servidor y no se ha indicado ninguna ruta |
| **inicializado** | todo está en su sitio: las acciones registradas, la experiencia puesta, el programa encontrado; las herramientas se publican al agente |
| **desactivado** | la persona ha apagado el complemento un tiempo; las herramientas no se publican, los registros se quedan |
| **retirado** | las acciones y los registros de experiencia se han eliminado en toda la organización |

El estado es **propio de cada servidor**: en el equipo donde está Blender el complemento aparece
inicializado, y en el de al lado, «buscando el software». En eso consiste el trabajo distribuido:
uno graba y otro monta.

Aparte, puede haber una fila **sin manifiesto**: el registro llegó por replicación mientras que
el archivo `plugins/<código>/plugin.json` no está en este equipo. Esa fila se muestra a
propósito: de lo contrario no habría forma de enterarse aquí de un complemento que funciona en el
equipo vecino.

## Cómo inicializar

1. Abra **«Ajustes → Complementos y MCP»**. La lista se arma con los manifiestos del disco de
   este servidor y con los registros de la organización.
2. Pulse **«Inicializar»** en la fila del complemento. Se crea el registro de la organización, se
   da de alta una entrada del catálogo de acciones por cada acción y los registros de experiencia
   del complemento pasan a la experiencia general de la organización. En un registro de tipo MCP
   lo primero que se hace es obtener la lista de herramientas: al agente solo se le puede publicar
   aquello que tiene entrada en el catálogo.
3. Dele al complemento su programa, de una de estas tres maneras:
   * el complemento **lo ha encontrado solo** en el `PATH` y ha comprobado la versión: no hay
     nada que hacer;
   * **«Instalar»**, si el complemento tiene paquete (así se instala el Shotcut portátil y así se
     instala ffmpeg);
   * **«Indicar dónde ya está instalado el programa»**: la ruta a mano; sirve tanto el archivo
     del programa como la carpeta en la que se instaló. Así se conecta DaVinci Resolve: su
     distribución se entrega tras un formulario de registro y no podemos descargarla por usted.
4. El botón **«Comprobar»** vuelve a lanzar la búsqueda y muestra qué ha encontrado.

Inicializar es una **acción del administrador del servidor**. El lector ve la sección pero no
tiene botones en ella: dar de alta acciones es conceder permisos, no ajustar el aspecto.

Hay que hacerlo **en cada servidor donde el complemento deba funcionar**: el registro de la
organización llegará solo al vecino por replicación, pero el programa y su ruta no.

## «Programa no encontrado en este servidor»

Esa línea no es un error. Significa exactamente lo que dice: el programa no está en este equipo y
no se ha indicado ninguna ruta. Mientras sea así, **las acciones del complemento no se publican
al agente**: una herramienta que de antemano va a responder con una negativa gastaría el turno
del agente para nada.

Qué hacer:

* instalar el programa con el botón **«Instalar»**, si hay paquete;
* instalarlo usted e indicar la ruta con **«Indicar dónde ya está instalado el programa»**;
* no hacer nada, si en este equipo no se monta: deje que la tarea se vaya adonde el complemento
  está inicializado.

La comprobación de la versión forma parte de la búsqueda: un programa más antiguo que la versión
mínima válida cuenta como no encontrado. No es tiquismiquis, es protección frente a una negativa
media hora después de empezar: el juego de claves de ffmpeg cambió entre las versiones 4.x y 7.x,
y el de `melt`, entre la sexta y la séptima.

## MCP se conecta únicamente mediante nuestro registro

La regla es simple y estricta: **un servidor MCP se conecta con un registro de complemento y de
ninguna otra manera.**

Un servidor MCP escrito directamente en la configuración del agente CLI (por fuera de AI2P) elude
toda nuestra seguridad: sus herramientas no están ni en el catálogo de acciones, ni en las reglas
de la tarea, ni en el registro de sucesos. El agente las usa y usted ni se entera ni puede
prohibirlas. Por eso esa conexión no es «otra forma de configurarlo»: es un agujero.

Cómo está hecho en nuestro lado:

* las herramientas del servidor MCP se obtienen como lista y cada una recibe una **entrada del
  catálogo de acciones**: desde ese momento se puede permitir o prohibir con una regla de
  seguridad normal de la tarea o del proyecto;
* **en las herramientas de complemento la política es la inversa**: una herramienta sin entrada
  en el catálogo está **prohibida**. En las herramientas normales del sistema la regla es la otra
  (sin entrada no hay prohibición), y justamente por eso para los complementos se invierte: un
  servidor MCP que añadiera una herramienta en una versión nueva la colaría en silencio por
  delante de las reglas;
* a la herramienta que desaparece de la lista se le retira su entrada del catálogo: ni se publica
  ni está permitida;
* el **token** del servidor MCP se guarda en el subdirectorio `secrets/` de este servidor, no se
  replica y nunca llega al `config.json` ni al registro de sucesos;
* la **lista de herramientas es propia de cada servidor** (vive en el `config.json`): un servidor
  MCP arranca donde está instalado su programa, y el vecino puede tener otra versión. Lo que se
  replica no es la lista, sino su consecuencia: las entradas del catálogo de acciones.

## Ejecución larga: un programa como ejecutor de la tarea

Las acciones de un complemento son herramientas del agente de IA: el agente llama a la acción y
recibe la respuesta en segundos. El entrenamiento de un adaptador LoRA no funciona así: dura
horas y ocupa la tarjeta gráfica entera. Por eso el complemento tiene un segundo tipo de
capacidades: las **operaciones de ejecución larga** (el bloque `run` del manifiesto). No las
llama el agente, sino una **tarea**, con un ejecutor del tercer tipo, el **«software
automático»** (véase [Ejecutores](performers.md)).

Qué tiene esa operación por encima de una acción normal:

* una **carpeta de trabajo**: si es relativa, se cuenta desde la carpeta del proyecto y no se
  deja salir de ella;
* **dos límites en vez de uno**: el tiempo de espera del **silencio**, aparte del límite
  general de duración. Mientras el programa imprime, está vivo; el que se ha colgado debe
  convertirse en error en media hora, no en doce horas;
* una **regla de lectura del resultado**: lo que se indicó como salida, un archivo por la ruta
  del manifiesto o «el archivo más reciente de una carpeta por máscara». Lo tercero existe
  justo para los entrenadores: el nombre del archivo de la época
  (`epoch-0007.safetensors`) no se puede nombrar de antemano;
* la marca **«Una sola instancia»**, una columna del formulario del complemento. Mientras esa
  operación está ocupada, la segunda tarea con ella **espera** en la cola. No es «más lento»:
  dos entrenamientos en una misma tarjeta gráfica son un fallo de memoria en un minuto.

**Qué se ve mientras el programa calcula.** Su salida se lee sin parar y va a la vez a dos
sitios: a la **consola de la ficha de la tarea**, en directo, línea a línea, y al **registro
del trabajo**, para siempre. El registro se puede mirar sin esperar al final. Detener es el
botón habitual de detener la tarea, y con él se apaga todo el árbol de procesos. Un código de
retorno distinto de cero, un silencio más largo que el límite y el tiempo total agotado se
convierten igualmente en un error del trabajo, y las últimas líneas de la salida quedan en su
texto: sin ellas, «se cayó» no se arregla.

La operación no tiene línea de comandos propia, igual que la acción: los argumentos están
escritos en el manifiesto, y la tarea solo sustituye en ellos los ajustes declarados del
registro y rutas dentro de la carpeta del proyecto.

**El registro del complemento no se crea solo a mano.** La instalación de un modelo que declara
paquetes de entrenamiento LoRA crea por sí misma el registro del **complemento entrenador** que
lleva a todos esos paquetes, y anota la ruta encontrada del programa, pero solo **en un campo
vacío**: una ruta indicada por la persona no la machaca una reinstalación. El registro aparece
en el estado **«declarado»**: las acciones del catálogo y los registros de experiencia los sigue
creando el botón «Inicializar», porque publicar herramientas al agente es una decisión de la
persona y no una consecuencia de una instalación.

## La regla de seguridad «Complementos y MCP»

Las reglas de seguridad tienen un tipo aparte para esta sección: **acceso a los complementos y
al MCP**. Una regla normal responde a «qué se le permite hacer al agente» (el código de la
acción), y esta responde a «**qué complementos se pueden usar siquiera**».

* El **patrón** es el código del complemento (`trainer.musubi`) o el código con la operación
  (`trainer.musubi:lora.train`). Las máscaras y las expresiones regulares funcionan igual que
  en los demás tipos de regla.
* Hay **dos operaciones, y hay que marcar al menos una**: «**usar**», el agente llama a una
  herramienta del complemento; «**ejecutar**», una tarea de «software automático» arranca el
  programa. Lo segundo se comprueba **antes** del arranque del programa y cubre todos los
  caminos a la vez: el agente, la persona, la cola de la jerarquía y el arranque diferido.
* **Por defecto está permitido todo.** No hay reglas de este tipo, luego se puede; la
  prohibición se crea de forma explícita. La regla se pone en cualquiera de los tres niveles
  (organización, proyecto, tarea) y, al superponerse, gana la más estricta.
* «**Preguntar**» en el arranque de un programa significa **negativa**: el arranque viene de la
  cola y allí no hay a quién preguntar.

Esta regla se crea en los mismos formularios que las demás: Ajustes → Seguridad (toda la
organización), ficha del proyecto → «Seguridad», ficha de la tarea → «Seguridad».

## Desactivar, activar, retirar

* **«Desactivar»**, temporalmente: las herramientas dejan de publicarse al agente, y las entradas
  del catálogo y de experiencia se quedan donde están. Para volver, **«Activar»**.
* **«Retirar»**, para siempre: las acciones y los registros de experiencia del complemento se
  eliminan **en toda la organización**. Los ajustes del registro y la ruta al programa se
  conservan, para que una segunda inicialización no empiece de cero.

Los registros de experiencia que el complemento ha puesto en la experiencia general de la
organización se ven y se editan en la pestaña **«Ajustes → Experiencia general»**; el botón
«Abrir la experiencia general» del formulario del complemento lleva al mismo sitio.

## Capítulos vecinos

* [Configuración](config.md) — la pantalla de ajustes entera y qué hay en sus demás pestañas.
* [Tareas](tasks.md) — las reglas de seguridad de la tarea, que son las que permiten y prohíben
  acciones.
* [Objetos del proyecto](objects.md) — la biblioteca multimedia del proyecto: de ahí sacan las
  pasarelas la línea de tiempo.
* La sección [Complementos y MCP](../plugins/README.md) — un documento por complemento: qué sabe
  hacer, qué necesita para funcionar y las limitaciones por sistema operativo.

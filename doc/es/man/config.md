# Configuración

Los ajustes de AI2P viven en dos lugares, y eso no es descuido, sino una regla.

* **La pantalla «Ajustes»** contiene lo que pertenece a la **organización** y a **este
  ordenador**: el idioma, los catálogos, los modelos de IA, las reglas de seguridad, los
  usuarios, los servidores, las notificaciones.
* **El archivo `config.json`** contiene aquello de lo que depende el **arranque mismo** del
  programa: el puerto, la dirección, las carpetas, el correo. Se edita desde fuera del sistema,
  cuando no se puede entrar en él: por ejemplo, si el puerto está ocupado y el servidor no
  levanta.

Todo lo que se edita en la pantalla acaba en ese mismo `config.json`, pero lo contrario no es
cierto: parte de los parámetros no se muestra en la interfaz a propósito (véase
[§ 3](#3-qué-se-edita-sólo-en-configjson)).

---

## 1. Cómo abrirlos

El icono de la rueda dentada, al final de la barra izquierda (la barra vertical de acciones),
debajo del botón de documentación. Esos mismos «Ajustes» están en el menú **AI2P → Vistas** y en
la última línea del explorador. La pantalla no tiene dirección propia: por enlace no se abre.

Los ajustes se abren como **pestaña del área de trabajo**, igual que una tarea o un proyecto: se
pueden dejar abiertos junto al trabajo e ir alternando.

Quién ve qué: las secciones de la organización (catálogos, acciones, seguridad, usuarios) las
edita el **propietario** o el **administrador**, y el formulario del propio servidor requiere un
**acceso local de administrador del servidor** aparte (el botón del usuario en la esquina
superior derecha → «Acceso local de administrador del servidor»). Es local de verdad: sólo se
acepta desde este ordenador.

---

## 2. Pestañas

### General

Los ajustes de esta instalación y de esta organización:

| Qué | Particularidades |
|---|---|
| **Idioma de la interfaz** | cambia tanto la interfaz como el **idioma de los mensajes del sistema**; no afecta al idioma en el que el agente recibe el encargo, que se indica en el equipo (véase [Equipos](teams.md)) |
| **Puerto de la UI/API** | se aplica **tras reiniciar**: el proceso ya está escuchando el puerto anterior |
| **Abrir el navegador al arrancar** | no funciona en el servicio del SO: un servicio no tiene escritorio |
| **Segunda línea de las pestañas** | qué escribir debajo de la palabra «Tarea»/«Proyecto»: el principio del nombre o el código corto (`T-17`, `PRJ-2`) |
| **Nivel de detalle del registro** | Debug / Info / Warning; se aplica **de inmediato** tanto al registro técnico como al historial de trabajos |
| **Celda del calendario de programaciones** | hora + código de la plantilla, u hora + principio del título |
| **Repositorio de modelos**, **carpeta de los distributivos**, **carpeta de instalación de paquetes** | carpetas **de este ordenador**; las edita el administrador del servidor |
| **Fotograma del conjunto de datos LoRA** | hasta qué límites comprimir la imagen y con qué rellenar los márgenes |
| **Versión de la aplicación**, **Arranque** | sólo de lectura: el número de compilación y si el servidor está levantado por consola o como servicio del SO |

Conviene entender enseguida las tres carpetas del equipo, porque de otro modo resultan
enigmáticas. Sus valores por defecto son **relativos** (`./models`, `./distribs`, `./packages`)
y se cuentan **un nivel por encima de la carpeta del programa**: AI2P en `C:\ai\AI2P` → los
pesos de los modelos en `C:\ai\models`, los paquetes en `C:\ai\packages`. Está hecho así a
propósito: los pesos de los modelos locales son decenas de gigabytes, y no se pueden poner
dentro de la carpeta del programa, que se borra al reinstalar. El formulario muestra qué ruta ha
salido en realidad («Ahora: …»): un valor relativo por sí solo no dice nada. Un valor vacío en
los distributivos significa «carpeta temporal», y en los paquetes,
«`<repositorio de modelos>/packages`».

### Modelos

El catálogo de **modelos de IA**: aquello de lo que se hacen, en general, los ejecutores de IA.
Un registro = un modelo de un proveedor: nombre, proveedor, dirección de la API, referencia a la
clave, precio, habilidades, perfil de parámetros y configuración del entrenamiento de LoRA.

Particularidades que conviene conocer antes de que algo deje de funcionar:

* **Un modelo en la nube no puede estar activo sin clave de API, y uno local, sin los archivos
  descargados.** Esta regla elimina toda una clase de negativas incomprensibles del tipo «hay
  modelo, pero el encargo no avanza». La clave se introduce con el botón de la clave de la fila
  del modelo, y los archivos, con el botón «Instalar».
* El botón **«i»** de la fila abre el documento del modelo: qué sabe hacer, qué hace falta para
  conectarlo, cuánto cuesta. La lista completa está en [Modelos de IA](../models/README.md).
* El **Acceso a Claude CLI** es un botón aparte, encima de la lista. Los modelos «por
  suscripción» (`*_cli`) no requieren clave, pero sí requieren que el acceso se haya hecho;
  cuando caduca, los encargos no fallan con error, sino que se quedan en pausa «espera acceso»,
  y eso se cura aquí.
* Un modelo local **se activa en un servidor concreto**: físicamente está en un solo ordenador.
  El ejecutor de ese modelo queda atado a ese mismo servidor.

### Catálogos

Las listas pequeñas de las que se compone todo lo demás: **habilidades** (skills), **estados de
las tareas**, **roles en los equipos**, **formatos de entrada y salida**, **tipos de fuentes de
importación**, **reglas de archivado por defecto**. Los registros se dividen en **integrados**
(vienen con el distributivo y no se pueden eliminar) y **de usuario**.

Las habilidades son lo más importante de aquí: con ellas funciona la selección automática del
ejecutor. Al crear una habilidad propia, compruebe que esté declarada en la declaración de
capacidades de alguien; de lo contrario, una tarea con esa habilidad no encontrará ejecutor
jamás.

### Acciones

El catálogo de lo que se le permite al agente de IA hacer con las manos del sistema: leer y
escribir archivos del proyecto, leer los encargos vecinos, crear subtareas, preguntar a una
persona, anotar experiencia, editar plantillas, trasladar una tarea, etcétera.

Lo principal de esta pestaña: **aquí están los prompts**, esos textos con los que la herramienta
se le describe al agente. No están en el código. De una acción integrada sólo se edita el
prompt, y la edición se deshace con un botón que devuelve el texto de fábrica; una acción de
usuario se edita entera. Los textos se llevan en varios idiomas y se insertan según el idioma
del equipo.

Los códigos de las acciones (`AI2P.Files.Write`, `AI2P.Tasks.Create`, …) son jerárquicos,
separados por puntos. Es con ellos con lo que operan las reglas de seguridad, así que conviene
asomarse aquí antes de escribir una regla.

### Seguridad

Las reglas de «qué puede hacer el agente»: **permitido / preguntar / prohibido** para una acción
(o para toda una rama de acciones) en un ámbito: toda la organización, un proyecto, una tarea.
La regla «preguntar» significa que, antes de la llamada, el sistema pedirá confirmación al
responsable de la tarea.

Un detalle que ahorra horas de averiguaciones: una regla cierra una acción sólo si la
herramienta **tiene un registro en el catálogo de acciones**. A una herramienta que no está en
el catálogo, la regla no la afecta en absoluto.

Además de las acciones, la regla tiene el tipo **«complementos y MCP»**: no responde a «qué
hacer», sino a «qué complementos se pueden usar». El patrón es `trainer.musubi` o
`trainer.musubi:lora.train`, y las operaciones son «usar» (el agente llama a una herramienta
del complemento) y «ejecutar» (una tarea de «software automático» arranca el programa). **Por
defecto está permitido todo**, la prohibición se crea de forma explícita. Los detalles están en
[Complementos y MCP](plugins.md).

### Usuarios

Las cuentas de acceso y sus roles: `owner` (propietario de toda la organización), `admin` (dueño
de un ordenador del clúster), `project_admin`, `editor`, `reader`. Una cuenta es un acceso; una
persona **trabaja** no con la cuenta, sino con el ejecutor que hace referencia a esa cuenta
(véase [Ejecutores](performers.md)).

### Organizaciones

La lista de organizaciones de este servidor y el cambio entre ellas. Una organización es una
base aparte, una carpeta aparte, catálogos aparte y su propia clave de cifrado. Aquí viven
también los **archivos**: se muestran como filas hijas del registro de la organización (véase
[Archivado](Archives.md)).

### Servidores

El servidor propio, los vecinos del clúster y las solicitudes de conexión. El **formulario del
servidor local** son precisamente los ajustes del propio servidor: dirección, puerto, segunda
dirección (externa), interfaz de escucha, carpetas de este ordenador, contraseña del
administrador del servidor. Los edita sólo el administrador del servidor, y los demás los ven en
modo lectura; **la dirección y el puerto se aplican tras reiniciar**. Si se elige el protocolo
`https`, más abajo aparece la sección **«Certificado HTTPS»** (véase [HTTPS](https.md)). En
detalle, en [Varios servidores](servers.md).

### Notificaciones

Las reglas de «sobre qué escribir a una persona»: la tarea pasa a un estado, la tarea requiere
revisión, se acerca el plazo. Encima de la lista está la configuración del **servidor de
correo** (que es un ajuste del ordenador, no de la organización) y el botón del **mensaje de
prueba**: sin él, «lo he configurado y espero» se convierte en «espero y no sé si funciona».

Una lista vacía de ejecutores o de proyectos en una regla significa **«todos»**, no «ninguno».

### Experiencia general

Los registros de experiencia que se aplican a **todos** los proyectos de la organización: las
reglas generales de trabajo que se insertan en cada encargo del agente. La experiencia de un
proyecto concreto vive en la ficha del proyecto, y la de un nodo de plantilla, en la plantilla
(véase [Proyectos](progects.md), [Plantillas](templates.md)).

---

## 3. Qué se edita sólo en `config.json`

El archivo está en el mismo sitio que los archivos de trabajo de la instalación (`data/`,
`logs/`, `secrets/`); dónde exactamente se dice en [Instalación](install.md). Antes de editarlo
hay que **detener** el programa: él escribe en ese mismo archivo.

```json
{
  "ui": {
    "protocol": "http",
    "port": 5480,
    "basePath": "/ai2p",
    "hostname": "localhost",
    "hostname2": "",
    "port2": null,
    "bindAddress": "0.0.0.0",
    "https": {
      "source": "file",
      "certFile": "",
      "keyFile": "",
      "passwordRef": "https.certPassword",
      "storeLocation": "CurrentUser",
      "storeName": "My",
      "subject": "",
      "thumbprint": "",
      "trustAnyPeer": true
    },
    "openBrowserOnStart": true
  },
  "storage": {
    "dataDir": "./data",
    "dbFile": "ai2p.db",
    "distDir": "./distribs",
    "packagesDir": "./packages",
    "docDir": "",
    "modelsRepo": "./models",
    "serverDbFile": "server.db"
  },
  "logging": { "level": "Warning", "dir": "./logs", "rotation": "day" },
  "language": "ru",
  "serviceMode": false
}
```

Este es el archivo **tal como llega con el distributivo**. Con el tiempo se le añaden secciones:
el correo (`mail`) aparece cuando se configura en la pestaña «Notificaciones»; no hay que
crearlo a mano.

Qué es importante aquí y no está en la pantalla:

* **`basePath`**: el prefijo de la dirección después del puerto (`/ai2p`). De él dependen todos
  los enlaces a las tareas; se cambia cuando AI2P se pone detrás de un proxy inverso común.
* **`bindAddress`**: qué interfaz de red escuchar. `0.0.0.0` son todas (así funciona el
  clúster), `127.0.0.1` es sólo este ordenador. Para una instalación única, lo segundo es más
  seguro.
* **`hostname2` / `port2`**: la **segunda dirección, la externa** del servidor: un nombre público
  o la dirección del router con el puerto redirigido. Vacío significa que no hay segunda
  dirección.
* **`https`**: el certificado del servidor; junto con `protocol: "https"` eso es precisamente el
  paso a HTTPS. También se edita en pantalla: en el formulario del servidor local la sección
  aparece en cuanto se elige el protocolo `https`. En detalle (de dónde sacar el certificado,
  cómo permitirlo en el navegador, qué hacer con el clúster), en [HTTPS](https.md).
* **`dataDir`, `logging.dir`**: una ruta relativa se cuenta **desde `config.json`**, y `~/…`,
  desde la carpeta personal. La ruta se escribe tal como se ha tecleado: el archivo sigue siendo
  portable.
* **`docDir`**: la carpeta de la documentación; si está vacío (el caso normal), el programa la
  encuentra solo. Si se indica una inexistente, los documentos no se muestran y la interfaz lo
  dice con todas las letras.
* **`serviceMode`**: la marca «esta instalación funciona como servicio del SO»; la pone
  `makeAsServise` y la lee el instalador para detener el servicio durante la actualización.

Tres reglas que conviene conocer de antemano:

1. **En `config.json` no hay secretos nunca.** Las claves de API están cifradas en la base de la
   organización, la clave de la organización y la cuenta del administrador del servidor están en
   `secrets.json`, y la contraseña del correo, en `secrets/mail.password.json`. En la
   configuración sólo queda una **referencia** (`mail.passwordRef`). La subcarpeta `secrets/` no
   va ni a la replicación, ni a la publicación, ni a la actualización; dentro de ella hay un
   `readme.txt` con la explicación en el idioma de la instalación.
2. **Las carpetas se ajustan a la plataforma al arrancar.** Una configuración traída de Windows
   a Linux no dejará en el sistema la ruta `C:\ai`: una ruta ajena se sustituye por el valor por
   defecto, y el cambio se explica con una línea en la consola. Una ruta del tipo `~/ai` no se
   considera ajena nunca.
3. **La actualización de versión no pierde sus valores.** Al lado se deja `config.new.json` con
   los nuevos valores por defecto, en el primer arranque los archivos se fusionan (nueva base +
   sus valores) y el archivo aplicado queda como copia `config.new.json.applied`.

---

## Y después

* [Instalación de AI2P y dónde están sus datos](install.md): dónde está puesto todo esto.
* [Inicio rápido](quickstart.md): si la configuración hace falta para el primer arranque.
* [Varios servidores](servers.md): el formulario del servidor local y el clúster.
* [HTTPS](https.md): el certificado del servidor y la configuración del navegador.

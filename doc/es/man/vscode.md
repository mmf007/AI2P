# AI2P en VS Code

AI2P puede trabajar dentro de VS Code: la ficha del proyecto, el entorno de trabajo, los
ajustes, la documentación y las fichas de tareas se abren como paneles del editor, junto al
código del proyecto. Es la misma interfaz de AI2P que en el navegador, pero sin el menú
superior, las pestañas, la barra lateral izquierda ni el navegador; el tema claro u oscuro se
toma de VS Code.

Todo funciona mediante la extensión **AI2P** para VS Code. Se distribuye con AI2P (el archivo
`ai2p-<versión>.vsix` en la carpeta del programa), su versión siempre coincide con la de AI2P y
la instala el propio AI2P: no hay que descargar nada del Marketplace.

## Requisitos

* AI2P y VS Code en el **mismo equipo**: los paneles acceden al servidor por `http://localhost`.
* El proyecto tiene una **carpeta de proyecto** en este servidor. Cada servidor tiene la suya: si en este servidor no la hay, aquí no se puede
  conectar VS Code al proyecto y no se muestran los botones.
* VS Code instalado de la forma habitual (véase «Limitaciones»).

## Conectar un proyecto

1. En AI2P abra la ficha del proyecto, pestaña **«Configuración»**, en modo edición.
2. Pulse **«Conectar VS Code»**. Si en el equipo hay varias copias de VS Code, primero aparece
   un diálogo para elegir una. La elección se guarda para este proyecto **en este servidor** y
   no pasa a otros servidores del clúster: cada proyecto puede usar una copia distinta.
3. AI2P entonces:
   * instala o actualiza la extensión en la copia elegida;
   * crea `.ai2p/link.json` en la carpeta del proyecto: dirección del servidor, código de la
     organización, proyecto y ruta del servidor. **Este archivo no contiene secretos**;
   * emite un **token de dispositivo** y lo entrega a VS Code con un enlace
     `vscode://ai2p.ai2p/link`. VS Code pregunta si la extensión puede abrir el enlace:
     permítalo. El token se guarda en el almacén seguro de VS Code (SecretStorage), no en los
     archivos del proyecto;
     El enlace usa el esquema de la copia elegida (`vscode-insiders://`, `vscodium://`,
     `cursor://`, `windsurf://`), así que el token llega justo a esa copia;
   * según las casillas del diálogo de conexión — añadir `.ai2p/` a `.gitignore` y abrir el
     proyecto en VS Code — actualiza `.gitignore` y abre la carpeta del proyecto.

También se puede conectar al crear el proyecto: si se encuentra VS Code y la carpeta está
indicada, el formulario del nuevo proyecto tiene la casilla «Conectar VS Code».

La extensión se activa **solo** en carpetas con `.ai2p/link.json`; en otros proyectos no se ve.

## Primera apertura de la carpeta: «Trust Folder»

Cuando VS Code abre la carpeta del proyecto **por primera vez**, pregunta si confía en sus autores
(la ventana **«Do you trust the authors of the files in this folder?»** con los botones **«Yes,
I trust the authors»** / **«Trust Folder»** y **«No, I don't trust the authors»**). Mientras la
carpeta no sea de confianza, VS Code la mantiene en modo restringido (Restricted Mode) y las
extensiones no funcionan del todo en ella.

Por eso, en la primerísima conexión el enlace con el token que AI2P pasa a VS Code **se pierde**:
mientras usted lee la ventana de confianza, la extensión todavía no puede recibirlo y los paneles
de AI2P no se abren. Qué hacer — **una sola vez**:

1. En la ventana de confianza pulse **«Yes, I trust the authors»** (**«Trust Folder»**).
2. **Cierre** esa ventana de VS Code (o la carpeta: **File → Close Folder**).
3. Vuelva a pulsar en AI2P **«Abrir el proyecto en VS Code»** (o **«Conectar VS Code»**).

Ahora la carpeta es de confianza, VS Code ya no pregunta, el enlace llega y el proyecto se abre
enseguida. No hace falta repetirlo para la misma carpeta, solo para cada carpeta de proyecto nueva.

## Menú y paneles

En la Activity Bar de la izquierda aparece el icono **AI2P**. Su menú tiene cuatro elementos:

| Elemento | Abre |
|---|---|
| **Proyecto** | la ficha del proyecto |
| **Entorno** | el entorno de trabajo y los archivos (solo si hay archivos) |
| **Ajustes** | los ajustes de AI2P |
| **Documentos** | esta documentación |

Las mismas acciones están en la paleta de comandos (`Ctrl+Shift+P`) con el prefijo **«AI2P:»**:
«AI2P: Proyecto», «AI2P: Entorno», «AI2P: Ajustes», «AI2P: Documentos», «AI2P: Abrir tarea…»,
«AI2P: Conectar proyecto desde AI2P», «AI2P: Desconectar VS Code», «AI2P: Iniciar servidor»,
«AI2P: Buscar actualización de la extensión». Los textos están traducidos a los cinco idiomas
de AI2P y siguen el idioma de VS Code.

Cada elemento abre un solo panel: elegirlo otra vez trae al frente el ya abierto. Una **tarea**
elegida en cualquier panel (árbol, tablero, tabla, diagrama, Inbox, enlace en un texto) se abre
en un **panel aparte**, como ficha de una sola tarea. VS Code restaura los paneles abiertos tras
reiniciarse.

<img src="../../images/Vs_code_AI2P_tasks_diagram_2.png" alt="Diagrama de subtareas en un panel de VS Code: el menú «…» de la tarjeta de la tarea" width="900">

**Tema.** El tema claro u oscuro viene de VS Code y cambia al vuelo en todos los paneles al
cambiar el tema del editor. No cambia su tema de AI2P en el navegador.

**Idioma.** Los paneles usan el idioma de VS Code si es uno de los de AI2P (ruso, inglés,
español, portugués, chino); si no, el idioma de la instalación de AI2P.

## El servidor AI2P

Si el servidor no está en marcha, la extensión lo inicia al abrir el primer panel: una sola vez
por equipo, aunque haya varias ventanas de VS Code. El servidor vive aparte de VS Code: cerrar
el editor no lo detiene.

Si el servidor se reinicia, los paneles lo esperan y se recargan solos.

## Inicio de sesión y revocación del token

Los paneles no tienen formulario de inicio de sesión: VS Code entra con el **token de
dispositivo** emitido al conectar, en nombre de quien pulsó «Conectar VS Code» y con sus
permisos. El token solo vale en este servidor.

Sus tokens se ven en AI2P: **Ajustes → «Notificaciones» → «Mis dispositivos (VS Code)»** —
dispositivo, proyecto, fecha de emisión y último acceso. **«Revocar»** cierra el acceso: en la
siguiente petición el panel mostrará «Hay que conectar VS Code». Cada uno ve y revoca solo sus
propios tokens.

## Desconexión

En la pestaña «Configuración» del proyecto pulse **«Desconectar VS Code»**: AI2P borra
`.ai2p/link.json` y revoca todos sus tokens de este proyecto. La extensión se queda: es común
para todos los proyectos.

El comando **«AI2P: Desconectar VS Code»** de la paleta borra el token solo en este equipo; para
cerrarlo también en el servidor, revóquelo en «Mis dispositivos».

## Actualización de la extensión

La extensión se actualiza junto con AI2P:

* al arrancar, el servidor comprueba la versión de la extensión en todas las copias de VS Code
  recordadas y, si está atrasada, instala la nueva en segundo plano;
* la propia extensión compara su versión con la del servidor al arrancar y, si está atrasada,
  ofrece **«Actualizar»**; después VS Code pide recargar la ventana (Reload Window).

Para comprobarlo a mano: «AI2P: Buscar actualización de la extensión».

## Limitaciones

* **Solo localhost por HTTP.** Los paneles de VS Code no aceptan un certificado autofirmado, así
  que incluso con un servidor HTTPS el modo integrado usa `http://localhost`. No se puede
  conectar VS Code a un servidor de otro equipo.
* **Derivados de VS Code** (VS Code Insiders, VSCodium, Cursor, Windsurf): AI2P los detecta y
  permite elegirlos, pero solo se ha probado VS Code normal.
* **macOS y Linux.** La extensión se ha probado en Windows, macOS y Linux.
* Si el equipo entró en suspensión o se cayó la red sin que el servidor se detuviera, el panel
  puede mostrar la ventana de reconexión más de lo normal; ciérrelo y vuelva a abrirlo.
* Dentro de los paneles no funcionan las ventanas de inicio de sesión ni los formularios que
  exigen el inicio de sesión normal: solo se entra por token.

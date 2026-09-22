# Ejecutar AI2P como servicio del sistema operativo

Por defecto AI2P es un **programa de consola normal**: se arranca `AI2P.Server.exe` y funciona;
se cierra la ventana y se detiene. Así resulta cómodo ver qué ocurre, y así funciona también el
primer arranque.

Pero un servidor de tareas suele necesitarse **todo el tiempo**: lleva la cola de encargos,
levanta agentes de IA, se replica con los demás servidores del clúster y vigila las
programaciones. No tiene por qué esperar a que el dueño del ordenador entre en el sistema y
abra una ventana. Para eso la aplicación sabe funcionar como **servicio del sistema
operativo**.

El servicio se llama **`AI2P`**, igual en todos los sistemas.

---

## 1. Cómo convertir la instalación en servicio

El servicio se crea **a mano y una sola vez**, con el script `makeAsServise` de la carpeta de
instalación (es esa carpeta donde está `AI2P.Server.exe`; el script lo deja ahí la
instalación).

### Windows

Abra la consola **en nombre del administrador** (sólo él crea servicios) y ejecute:

```powershell
cd D:\AI2P
.\makeAsServise.cmd
```

El script creará el servicio `AI2P`, le pondrá arranque automático al encender el ordenador,
activará el reinicio tras un fallo y lo arrancará enseguida. Al final imprimirá la dirección
por la que se abre la interfaz.

Opciones:

| Opción | Qué hace |
|---|---|
| `-WhatIf` | sólo mostrar qué se va a hacer; no cambiar nada |
| `-NoStart` | crear el servicio, pero no arrancarlo |
| `-Manual` | arranque manual del servicio, no al encender el sistema |
| `-Remove` | quitar el servicio (los archivos y los datos no se tocan) |
| `-Target D:\AI2P` | configurar otra instalación, no aquella desde la que se ha lanzado el script |
| `-Account .\mike -Password ***` | el servicio funciona en nombre de un usuario, no de LocalSystem |
| `-UnprotectSecrets` | quitar la protección DPAPI de las claves de las organizaciones (véase la sección 4) |
| `-Force` | hacerlo a pesar de las advertencias |

### Linux

```sh
cd ~/ai/AI2P
./makeAsServise.sh
```

Se crea una unidad **de usuario** de systemd, `~/.config/systemd/user/AI2P.service`, y no es
casualidad: el servidor de AI2P funciona con un usuario normal, no con root, y todo lo suyo
—datos, registros, ajustes y secretos— lo guarda en su propia carpeta. Para que ese servicio se
levante incluso sin que la persona entre en el sistema, el script ejecuta él mismo
`loginctl enable-linger`.

La unidad de sistema (`/etc/systemd/system/AI2P.service`, hace falta `sudo`) se crea con la
opción `--system`. Las demás opciones: `--no-start`, `--manual`, `--remove`,
`--target <carpeta>`.

### macOS

```sh
cd ~/ai/AI2P
./makeAsServise.sh
```

Se crea la tarea de launchd `~/Library/LaunchAgents/AI2P.plist`. Las opciones son las mismas,
salvo `--system`.

---

## 2. En qué se diferencia el arranque como servicio del arranque en consola

El programa es el mismo, no hay una compilación «de servidor» aparte. La aplicación **se entera
sola** de cómo la han arrancado: bajo el gestor de servicios de Windows y bajo systemd se
reconoce a sí misma, mientras que launchd no da esa señal, y allí el modo se indica con la
opción `--service` directamente en la tarea.

Las diferencias son sólo tres:

* **el navegador no se abre al arrancar.** Un servicio no tiene escritorio y no tiene dónde
  abrir una ventana: entre usted mismo por la dirección;
* **un puerto ocupado es un error.** El arranque en consola sobre un puerto ocupado abre el
  navegador en la instancia que ya funciona y sale tranquilamente; el servicio, en ese caso,
  sale **con error**, porque de lo contrario el gestor de servicios habría tomado la salida
  silenciosa por un funcionamiento normal y no habría dicho nada;
* **la detención va por orden del sistema**, no por Ctrl+C: la aplicación llega a cerrar la
  base, a descargar los modelos locales que ha levantado y a terminar las sesiones de
  replicación.

Todo lo demás —datos, ajustes, puerto, interfaz, clúster— no cambia.

Se puede comprobar con qué está levantado el servidor desde la propia interfaz:
**Ajustes → General**, la línea **«Arranque»** junto a la versión de la aplicación.

---

## 3. Gestión del servicio

**Windows**

```powershell
Get-Service AI2P            # estado
Start-Service AI2P
Stop-Service AI2P
.\makeAsServise.ps1 -Remove # quitar el servicio
```

**Linux** (unidad de usuario)

```sh
systemctl --user status AI2P
systemctl --user start AI2P
systemctl --user stop AI2P
journalctl --user -u AI2P -f     # qué escribe el servidor
./makeAsServise.sh --remove
```

**macOS**

```sh
launchctl list | grep AI2P
launchctl unload ~/Library/LaunchAgents/AI2P.plist
launchctl load   ~/Library/LaunchAgents/AI2P.plist
./makeAsServise.sh --remove
```

Sus propios registros la aplicación los escribe en cualquier modo en la carpeta `logs/` de la
instalación (`ai2p-<fecha>.jsonl`); allí mismo se ve también el motivo de un arranque fallido.

---

## 4. Windows: en nombre de quién funciona el servicio

Este es el único sitio donde la elección importa de verdad.

El servicio funciona por defecto en nombre de **LocalSystem**, la cuenta del ordenador. Y la
**clave de la organización** (con ella están cifradas las claves de API de los modelos) está
protegida en Windows por el mecanismo DPAPI **del ámbito del usuario**: sólo puede descifrarla
la misma cuenta que la escribió. Es decir, el servicio bajo LocalSystem no leerá las claves de
API y los modelos en la nube dejarán de funcionar; desde fuera eso parece «hay modelo, pero el
encargo no avanza».

Por eso `makeAsServise` mira dentro de `secrets.json` y, si encuentra allí valores protegidos,
**no crea el servicio en silencio**, sino que propone elegir:

* **`-Account <cuenta> -Password <contraseña>`**: el servicio funciona con el mismo usuario y
  las claves le siguen siendo accesibles. La cuenta necesita el derecho **«Iniciar sesión como
  servicio»** (`secpol.msc` → Directivas locales → Asignación de derechos de usuario); sin él el
  servicio no arranca y lo dice con el error 1069;
* **`-UnprotectSecrets`**: quitar DPAPI; las claves de las organizaciones quedarán en
  `secrets.json` como base64 normal, exactamente igual que en Linux y macOS, y las protegerán
  los permisos del archivo. El archivo anterior se conserva al lado como
  `secrets.json.dpapi.bak`;
* **`-Force`**: crear el servicio tal cual, sabiendo que las claves de API no le son
  accesibles.

Lo mismo vale para el **acceso a Claude CLI**: pertenece al perfil del usuario y el servicio
bajo LocalSystem no lo verá. Si en ese servidor trabajan agentes a través de Claude CLI, el
servicio hay que crearlo con ese mismo usuario.

En Linux y macOS esta elección no existe en absoluto: allí la clave de la organización está en
base64 normal y el servicio ya funciona con el mismo usuario.

### Si los archivos de trabajo no están junto al programa

La instalación **en la carpeta de programas del sistema** (`C:\Program Files\AI2P`, `/usr`,
`/opt`, `/Applications`) mantiene `config.json`, `data`, `logs` y `secrets` no junto al
programa, sino en la carpeta de datos: en Windows es `C:\ProgramData\AI2P`, y en Linux y macOS,
`/var/lib/ai2p`; si tampoco ahí se puede escribir, la aplicación se va a la carpeta de datos del
usuario.

`makeAsServise` lo tiene en cuenta él solo: encuentra el `config.json` de trabajo, allí mismo
pone la marca `serviceMode`, allí mismo busca las claves protegidas y **le indica esa ruta al
servicio con la opción `--config`**. Esto último es importante: el servicio funciona con otra
cuenta, y la carpeta de datos del usuario habría sido **la suya**, de modo que sin una ruta
explícita se habría creado una base vacía en lugar de la de trabajo. En la salida del script ese
caso se ve con la línea «Archivos de trabajo: …».

La carpeta se puede indicar también uno mismo, con la variable de entorno `AI2P_HOME` o con la
opción `--config` del propio programa; entonces todos los scripts toman precisamente esa.

---

## 5. Actualización de versión

**No hay que hacer nada especial.** La instalación recuerda que es de servicio, y las tres
formas de actualizar lo tienen en cuenta:

* `install.cmd` / `install.ps1` (Windows) y `./install.sh` (Linux, macOS) detienen ellos mismos
  el servicio antes de copiar los archivos y lo vuelven a arrancar después;
* el **paquete de instalación** (`AI2P_v_1_NN_windows_x64.exe`) hace lo mismo y, al desinstalar el
  programa, además quita el servicio.

El servicio se busca **por el sistema** —por el registro del gestor de servicios, la unidad de
systemd o la tarea de launchd— y sólo aquel que lleva **precisamente a esta instalación**:
ningún script toca uno ajeno que lleve a otra carpeta.

El paquete de instalación en Windows exige para ello permisos de administrador: no hay otra
manera de detener el servicio, y lo dice honestamente en lugar de caerse después por archivos
ocupados.

En el `config.json` de la instalación, al crear el servicio, se pone la marca
`"serviceMode": true`. Es una **nota**, no un interruptor: sobrevive a la actualización de
versión (la instalación no toca el `config.json` de trabajo) y sirve para que la aplicación y
los scripts puedan decir «esta instalación está configurada como servicio, pero ahora mismo no
hay ningún servicio en el sistema»: por ejemplo, cuando lo han quitado a mano o la carpeta se ha
trasladado a otra máquina.

---

## 6. Preguntas frecuentes

**¿Se puede arrancar el programa a mano mientras funciona el servicio?**
Sí, pero en otro puerto: dos instancias en un mismo puerto no funcionan. El arranque en consola
sobre un puerto ocupado simplemente abrirá el navegador en el servidor que ya funciona y
terminará.

**¿Cómo ver qué hace el servicio si no arranca?**
Primero, `logs/ai2p-<fecha>.jsonl` en la carpeta de instalación: la aplicación escribe ahí en
cualquier modo. Si el registro está vacío, es que el proceso no llegó al arranque: en Windows
mire el registro de eventos y el texto del error de `Start-Service`; en Linux,
`systemctl --user status AI2P`.

**He cambiado el puerto en `config.json`, ¿hay que hacer algo con el servicio?**
No. El servicio arranca el mismo programa de la misma carpeta, y los ajustes los lee al
arrancar: basta con reiniciar el servicio.

**He trasladado la instalación a otra carpeta.**
Cree el servicio de nuevo desde la nueva carpeta: `makeAsServise` verá que el servicio lleva a
otro sitio y pedirá confirmar la reasignación con la opción `-Force`.

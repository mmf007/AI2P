# Instalación de AI2P y dónde están sus datos

Este capítulo responde a dos preguntas: **dónde se instala el programa** y **dónde quedan
después sus archivos de trabajo**: la configuración, la base de tareas, los registros y las
claves. La segunda pregunta importa más de lo que parece: los archivos de trabajo sobreviven a
la actualización de versión, se copian al pasar a otro ordenador y son justamente los que no se
pueden perder.

---

## 1. Tres formas de instalarlo

| Forma | Qué es | Cuándo va bien |
|---|---|---|
| **Paquete de instalación** | un solo archivo `AI2P_v_1_NN_…` (o `AI2P_v_1_NN_full_…`, con el runtime dentro), se instala con doble clic | el caso normal en Windows |
| **Script de instalación** | `install.cmd` / `install.sh` de la carpeta de publicación | cuando la publicación ya está descargada y la carpeta se elige a mano |
| **La publicación sin más** | la carpeta descomprimida, se arranca `AI2P.Server.exe` | una prueba, una instalación portátil en un pendrive |

El paquete de instalación en Windows pregunta si instalar **para todos los usuarios** o **sólo
para mí**. De ello depende la carpeta del programa:

* **para todos**: `C:\Program Files\AI2P` (hacen falta permisos de administrador);
* **sólo para mí**: `C:\Users\<usted>\AppData\Local\Programs\AI2P`.

En Linux y macOS la instalación va a la carpeta personal: por defecto `~/ai/AI2P`.

---

## 2. Dónde están los archivos de trabajo

La regla es una: **los archivos de trabajo están junto al programa si se puede escribir en su
carpeta.**

Junto al programa significa `config.json`, la carpeta de datos `data/` (la base del servidor,
las bases de las organizaciones, los archivos de los proyectos), los registros `logs/` y las
claves `secrets/`.

En la carpeta de programas de Windows (`C:\Program Files`) un usuario normal **no puede**
escribir, y eso no es una avería, sino una regla del sistema: de lo contrario cualquier usuario
del ordenador podría sustituir `AI2P.Server.exe`, que luego arranca el administrador. Por eso en
la instalación «para todos» los archivos de trabajo están aparte:

| Instalación | Programa | Archivos de trabajo |
|---|---|---|
| Windows, «para todos» | `C:\Program Files\AI2P` | **`C:\ProgramData\AI2P`** |
| Windows, «sólo para mí» | `…\AppData\Local\Programs\AI2P` | junto al programa |
| Publicación en una carpeta propia (`D:\AI2P`) | `D:\AI2P` | junto al programa |
| Linux/macOS, `~/ai/AI2P` | `~/ai/AI2P` | junto al programa |
| Linux/macOS, `/opt/ai2p` | `/opt/ai2p` | `/var/lib/ai2p`, y sin permisos sobre ella, `~/.local/share/ai2p` |

`C:\ProgramData\AI2P` es la carpeta común **de este ordenador**: la instalación es una, los
datos son comunes, y el servicio (que funciona en nombre del sistema) ve los mismos datos que la
persona. El instalador crea esa carpeta y la abre a escritura a todos los usuarios del
ordenador; al desinstalar el programa **se queda**: son sus datos.

### Cómo saberlo con seguridad

Al arrancar, el programa dice él mismo dónde están sus archivos de trabajo:

```
AI2P: la carpeta del programa C:\Program Files\AI2P\ está cerrada a escritura; los archivos
de trabajo (config.json, data, logs, secrets) están en C:\ProgramData\AI2P
```

Esa misma línea va al registro técnico (`logs/ai2p-<fecha>.jsonl`), y las rutas completas de la
configuración y de la carpeta de datos se ven en el registro justo después del arranque.

### Cómo indicar la carpeta uno mismo

| Forma | Qué hace |
|---|---|
| `AI2P.Server.exe --config D:\miAI2P\config.json` | tomar exactamente ese `config.json`; `data/`, `logs/` y `secrets/` estarán junto a él |
| la variable de entorno `AI2P_HOME=D:\miAI2P` | lo mismo, pero se indica una sola vez para el servicio, el contenedor o el acceso directo |

Una carpeta indicada así es más fuerte que cualquier regla: el programa no se mete en ella.

---

## 3. Actualización de versión

La instalación encima de la anterior **no toca** los archivos de trabajo: `config.json`, `data/`
y `secrets/` quedan como estaban. Junto al programa se deja `config.new.json`, la configuración
de la nueva versión, y en el primer arranque el programa las fusiona: se toma como base la nueva
(en ella están todos los parámetros nuevos con sus valores por defecto) y encima se ponen los
valores de usted. De la fusión avisa con una línea en la consola, y junto al `config.json` de
trabajo queda una copia del archivo aplicado (`config.new.json.applied`), por la que se ve qué
llegó y cuándo.

La fusión se hace **exactamente una vez por versión**: un nuevo arranque no reescribe nada.

### Los datos que han quedado junto al programa

Si antes instalaba AI2P en `C:\Program Files\AI2P` y lo arrancaba **en nombre del
administrador**, los datos podían haberse creado ahí mismo. Entonces, después de actualizar, el
programa dirá:

```
AI2P: junto al programa ha quedado la carpeta de datos C:\Program Files\AI2P\data de un
funcionamiento anterior; ahora los datos están en C:\ProgramData\AI2P; si hace falta,
tráslodela allí a mano
```

Él mismo **no traslada** esos datos: no se puede mover la base a espaldas de la persona. Los
ajustes (el `config.json` de la instalación anterior), en cambio, sí se llevan consigo: el
puerto, el nombre de host y las carpetas los indicó usted y no hay por qué perderlos. Para
trasladar también los datos: detenga AI2P, copie la carpeta `data` (y también `secrets`, si está
ahí) a la nueva carpeta de archivos de trabajo y arránquelo de nuevo.

### `AI2P_HOME: parameter not set` al actualizar en Linux/macOS

Las versiones **1.100 y 1.101** interrumpían la actualización en esta línea:

```
./install.sh: 147: AI2P_HOME: parameter not set
```

No se copiaba absolutamente nada y la versión instalada seguía siendo la anterior. La culpa era
del propio instalador: leía la variable `AI2P_HOME`, que una instalación normal no tiene. Desde
la versión **1.102** eso ya no ocurre.

Si sólo tiene a mano la publicación 1.100 o 1.101, la actualización con ella se puede hacer de
todos modos: basta con dar a la variable un valor vacío:

```sh
AI2P_HOME= ./install.sh ~/ai/AI2P
```

El mismo remedio vale para `makeAsServise.sh` de esas mismas versiones.

---

### Actualización desde el propio programa

«Ajustes → General», junto al número de versión, tiene el botón **«Comprobar actualizaciones»**.
Consulta el repositorio de publicaciones (por omisión `https://github.com/mmf007/ai2p`, la dirección
se edita ahí mismo) y busca el archivo **para esta instalación**: el mismo sistema, la misma
arquitectura y el mismo modo de instalación: publicación completa (con el runtime dentro) o normal.
El modo de instalación se lee del `version.json` que está junto al programa.

Si salió una versión más nueva, se enciende el botón **«Actualizar»**. Antes de instalar, el programa
pregunta quién está ocupado: la actualización **reinicia el servidor** y los agentes en marcha de
todas las organizaciones abiertas se detendrán; la pregunta muestra su lista. Después se descarga el
paquete, el programa termina y un script aparte completa la instalación: espera el fin del proceso,
instala el paquete en silencio y vuelve a levantar el servidor (el servicio, con `net start` /
`systemctl`; el arranque en consola, iniciando el programa de nuevo). Habrá que recargar la página
en el navegador.

Dos casillas al lado:

* **comprobación automática**: solo mirar si salió una versión nueva (la respuesta va al registro);
* **actualización automática**: comprobar e instalar enseguida.

Cuando el programa se ejecuta en **consola**, la comprobación automática ocurre al iniciar. Cuando
funciona como **servicio del sistema**, no hay inicio durante semanas: entonces la casilla crea una
entrada en la **programación** (una vez al día, a las 2:00 hora local por omisión) y los ajustes
muestran su código; la hora se cambia en la propia entrada, como en cualquier programación. Al
quitar la casilla se elimina la entrada.

## 4. Servicio del sistema operativo

El servicio y el arranque normal usan **la misma** carpeta de archivos de trabajo: se elige por
la carpeta del programa, no por los permisos de quien lo ha arrancado. Es decir, al convertir la
instalación en servicio (`makeAsServise`, véase [service](service.md)) no obtendrá una segunda
base «para el sistema».

---

## 5. Si el programa no arranca

Arrancado con el acceso directo, AI2P es un programa de consola normal: si falla al arrancar,
**escribe el motivo con palabras y mantiene la ventana** hasta que pulse una tecla (la ventana
se cierra sola al cabo de un minuto). Lo que puede pasar:

| Qué se ve | Qué es | Qué hacer |
|---|---|---|
| `AI2P: el arranque ha fallado — Access to the path … is denied` | los archivos de trabajo se han indicado en una carpeta en la que no se puede escribir | indicar otra carpeta (`AI2P_HOME`, `--config`) |
| `Los archivos de trabajo de AI2P no tienen dónde ir` | están cerradas tanto la carpeta del programa como las dos carpetas de datos | lo mismo: indicar la carpeta explícitamente |
| `AI2P ya está en marcha (…): abro el navegador y salgo.` | no es un error: la instancia en este ordenador es una sola y ya está funcionando | nada |
| la ventana se cierra enseguida y en silencio | la versión es **anterior a la 1.100**: allí un fallo en el arranque se llevaba la ventana junto con el mensaje | actualizar o arrancar `AI2P.Server.exe` desde la consola y leer la salida |

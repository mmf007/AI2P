# Inicio rápido

El camino mínimo desde la descarga hasta el primer resultado de un agente de IA. Nada
superfluo: los ajustes, las plantillas, los equipos y el clúster quedan para después; cada uno
tiene su propio capítulo.

Hacen falta de 10 a 15 minutos, y la mayor parte es la descarga.

---

## Paso 1. Descargar

[Los distributivos están aquí](https://github.com/mmf007/AI2P/releases)  

Hay dos archivos para cada sistema, y elegir entre ellos es elegir «si hace falta el runtime
aparte»:

| Archivo | Qué lleva dentro | Cuándo cogerlo |
|---|---|---|
| `AI2P_full_v_1_NN_win64.exe` | el programa **junto con el runtime** | el caso normal: no hay que añadir nada |
| `AI2P_v_1_NN_win64.exe` | sólo el programa | si en el ordenador ya está instalado el runtime **ASP.NET Core 8.x** |

En Linux y macOS es lo mismo, pero con la extensión `.run`
(`AI2P_full_v_1_NN_Linux.run`). `NN` es el número de versión; coja el mayor.

> Si duda, coja el **`full`**. Es más grande, pero no necesita nada más que a sí mismo.

## Paso 2. Instalar

### Windows
Ejecute el archivo descargado. El instalador preguntará si instalar **para todos los usuarios**
o **sólo para mí**; de ello dependen tanto la carpeta del programa como el lugar donde acabarán
sus archivos de trabajo; los detalles están en el capítulo [Instalación](install.md). Para
probar vale «sólo para mí»: no harán falta permisos de administrador.

### Linux

```sh
chmod +x AI2P_full_v_1_NN_Linux.run
./AI2P_full_v_1_NN_Linux.run
```

Se instala en `~/ai/AI2P`. No hacen falta permisos de `root`: el servidor funciona con un
usuario normal.

### macOS

```sh
chmod +x AI2P_full_v_1_NN_macos.run
./AI2P_full_v_1_NN_macos.run
```

Se instala en `~/ai/AI2P`. No hacen falta permisos de `root`: el servidor funciona con un
usuario normal.

## Paso 3. Arrancar

El acceso directo **AI2P** del escritorio (o `AI2P.Server.exe` / `./AI2P.Server` desde la
carpeta de instalación). El programa abrirá él mismo el navegador en
`http://localhost:5480/ai2p`.

Si no lo ha abierto, abra esa dirección a mano. Si la ventana con el texto del error se ha
cerrado demasiado rápido, mire
[«Si el programa no arranca»](install.md#5-si-el-programa-no-arranca).

## Paso 4. Pasar el asistente de primer arranque

En el primerísimo arranque AI2P no deja entrar en la interfaz, sino que lleva paso a paso. Son
seis:

| Paso | Qué pregunta | Qué conviene saber |
|---|---|---|
| **1. Idioma** | el idioma de la interfaz | se propone el idioma del navegador; el elegido se aplica **de inmediato** a todas las pantallas siguientes |
| **2. Servidor** | nombre del servidor, protocolo, puerto y **tres carpetas del equipo** | el nombre del servidor es su nombre **en la red**; para una instalación única vale `localhost`, basta con escribirlo explícitamente. Las carpetas (pesos de los modelos, distributivos, paquetes) se pueden dejar como se proponen |
| **3. Usuario** | nombre, correo, teléfono, contraseña | la casilla **«La contraseña coincide con la del administrador del servidor»** está marcada: déjela, así los ajustes del servidor estarán disponibles enseguida. La contraseña se puede dejar vacía, pero entonces sólo se podrá entrar desde este ordenador |
| **4. Organización** | nombre y código en la dirección | el código pasa a formar parte de la dirección (`/ai2p/<código>/…`). Deje activada la casilla **«servidor principal del clúster»**: desmarcarla significa «conectarse a un servidor ajeno» |
| **5. Uso típico** | código y analítica / vídeo / imágenes / sin uso típico | según la respuesta el sistema montará el puesto de trabajo |
| **6. Primer proyecto** | nombre y **carpeta** | la carpeta es aquella en la que el agente leerá y escribirá archivos. Sin ella no se le darán al agente las herramientas de archivos |

Después del último paso ya tiene: la organización, el servidor `S0` (que además es el
director), usted como propietario, **tres ejecutores de IA `Jon`, `Bob` y `Stiv`**, el equipo
`<organización>_team` y un proyecto con la carpeta indicada.

> La opción «código y analítica» en una instalación limpia da tres ejecutores sobre modelos
> **por suscripción de Claude**. Para vídeo e imágenes no hay modelos activos en una instalación
> limpia; el asistente lo dirá honestamente, pero creará el equipo y el proyecto de todos modos.

## Paso 5. Dar un modelo al sistema

Hay ejecutores, pero sólo podrán trabajar cuando su modelo tenga con qué pagar. Abra
**Ajustes** (la rueda dentada al final de la barra izquierda) → pestaña **«Modelos»**.

Elija **uno** de los caminos:

**a) Suscripción de Claude (lo más rápido, no hace falta clave).** El botón **«Acceso a Claude
CLI»** encima de la lista. Así funcionan los registros con el sufijo `_cli`, que son
precisamente los que ha puesto el asistente.

**b) Clave de API.** Busque el modelo que necesita, pulse el botón de la clave en su fila y
pegue la clave del proveedor. El registro se activará solo: **un modelo en la nube no puede
estar activo sin clave**.

**c) Modelo local.** El botón «Instalar» de la fila del modelo descargará los pesos e instalará
los paquetes necesarios. Eso es largo (gigabytes) y requiere tarjeta gráfica: no es el camino
más rápido para el primer arranque.

Qué hace un registro concreto, cuánto cuesta y qué necesita se ve con el botón **«i»** de su
fila (y en la [lista de modelos](../models/README.md)).

## Paso 6. Plantear una tarea

Abra **Tareas** (barra izquierda) → botón **«añadir»** → **«tarea vacía»**.

Rellene:

* **Título**: en pocas palabras, de qué va el trabajo.
* **Descripción**: como para una persona: qué hacer, dónde, con qué comprobarlo. Esto es el
  prompt; todo lo que el agente necesita saber debe estar aquí.
* **Criterios de aceptación**: cómo saber que está hecho.
* **Habilidades**: qué hay que hacer en la práctica (por ejemplo `code-write-cs`,
  `text-write`). Con ellas funciona la selección automática.
* **Ejecutor**: o bien elíjalo de la lista (`Jon`, `Bob`, `Stiv`), o bien pulse el botón de
  selección automática que hay junto al campo: **«primero la IA, luego una persona»**.

El proyecto y el equipo se ponen solos: los que están abiertos ahora.

## Paso 7. Lanzar y obtener el resultado

En la ficha de la tarea está el botón **«lanzar»**. A continuación:

* el estado pasa a **«en curso»** y al lado se ve quién está trabajando exactamente;
* la marcha del trabajo se ve en el **«Historial de trabajos»**: llamadas a herramientas,
  lectura y escritura de archivos, gasto de tokens;
* si al agente le falta algo, **preguntará en el chat de la tarea**: la tarea pasará a «pausa» y
  en el Inbox aparecerá la pregunta. Responda en el chat y el trabajo continuará desde el mismo
  punto;
* al terminar, el agente deja el resultado como **artefacto de la tarea** (el texto del informe,
  archivos, imágenes) y la tarea pasa a **«revisión»**: aceptar el trabajo le toca a usted.

Todo lo que el agente ha hecho con los archivos está en la carpeta del proyecto, allí mismo
donde usted los espera.

---

## Si algo ha salido mal

| Qué se ve | Por qué | Qué hacer |
|---|---|---|
| El botón «lanzar» no hace nada | la tarea no tiene ejecutor | asígnelo o pulse la selección automática |
| La tarea se ha quedado enseguida «con error» | el modelo no tiene clave o no se ha hecho el acceso | Ajustes → Modelos: la clave o «Acceso a Claude CLI» |
| La tarea está en «pausa» | el agente ha hecho una pregunta o el ejecutor ha agotado su límite | mire el chat de la tarea y el Inbox |
| El agente no ve los archivos del proyecto | el proyecto no tiene carpeta indicada | ficha del proyecto → campo de la carpeta |
| El agente se ha negado a hacer algo | ha saltado una regla de seguridad | Ajustes → Seguridad |

## Y después

* [Proyectos](progects.md): la carpeta del proyecto, los objetos, la experiencia,
  «precio ↔ calidad».
* [Tareas](tasks.md): el tablero, la jerarquía, las subtareas, las bloqueantes, el chat y los
  artefactos.
* [Ejecutores](performers.md): IA y personas, habilidades, límites, precio.
* [Equipos](teams.md): quién trabaja con quién y en qué idioma habla el agente.
* [Plantillas](templates.md): cómo no teclear dos veces el mismo proceso.
* [Configuración](config.md): todo lo demás.

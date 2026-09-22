# AI2P: compilación, publicación y estructura del repositorio

> La página principal del repositorio es `Readme.md`, en su raíz; la documentación para el
> usuario son la [descripción breve](../README.md) y el [índice](../index.md). Aquí está todo
> para quien compila AI2P a partir del código fuente.

Sistema de trabajo conjunto de agentes de IA y personas sobre tareas: planificador de trabajos, orquestador de agentes de IA, acumulador de experiencia. Funciona localmente (Windows 10–11, Linux, macOS), y la interfaz va por navegador web.

La documentación se divide en dos: la **del proyecto**, en la carpeta `doc/` de la raíz del repositorio (`doc/AI2P_ТЗ_v1.NN.md` es la descripción del sistema tal como es, y las versiones anteriores están allí mismo; desde la versión 1.60 el número de la especificación coincide con la versión de la aplicación; `doc/AI2P_release.md` trata de las versiones, la publicación y la actualización), y la **que se distribuye junto con el programa**, en `AI2P_app/doc/`, junto al código: los documentos de los modelos ([`models/`](../models/README.md)) y de los tipos de importación ([`import/`](../import/README.md)), que se abren en la interfaz con el botón «i». La carpeta `AI2P_app/doc` se copia entera a la publicación de la entrega.

> En un documento distribuido no se ponen enlaces a archivos que estén fuera de `AI2P_app/doc`:
> se dibuja dentro de la aplicación, y allí un enlace relativo hacia fuera no lleva a ninguna
> parte (especificación, cap. 14).

**Estado: etapa 1, planificador local de tareas.** Implementado: SQLite + event log + almacenamiento de archivos (especificación, cap. 6), entidades (cap. 2), API HTTP (API-first, cap. 3), interfaz al estilo de VS Code (cap. 11): tablero de tareas (kanban, arrastrar y soltar), ficha de la tarea (descripción .md, historial, chat, artefactos, encargos), listas y formularios de proyectos, equipos y ejecutores, historial de trabajos con filtros, Inbox de la persona, ajustes; HumanConnector (la persona como ejecutor a través de la cola de encargos); multilingüismo ru/en (diccionarios JSON en `i18n/`).

## Instalación del entorno de compilación

Los scripts `install_required` comprueban e instalan las herramientas de compilación: **.NET SDK 8+**, el **runtime de ASP.NET Core 8.x**, **CMake** y un **compilador de C/C++**, y después inicializan los submódulos de git (si hay `.gitmodules`). Los scripts son idempotentes: lo ya instalado no se reinstala, y se pueden ejecutar de nuevo para comprobar.

> **Por qué hace falta precisamente el runtime 8.x, aunque esté instalado el SDK 9/10.** La aplicación se compila para net8.0; un SDK más nuevo la compila, pero no se puede ejecutar sobre el runtime 9/10 (roll-forward): el script cliente de Blazor (`blazor.web.js`) se toma en la versión 8, y el servidor funcionaría sobre el 9/10; la interactividad (botones, eventos) deja de funcionar en silencio. Un ejemplo: el cask de brew `dotnet-sdk` en macOS instala ahora .NET 10, y el script añadirá al lado el runtime 8.0, de modo que la aplicación arrancará sobre él.

### Windows

Hay que ejecutarlo en una consola normal (cmd o PowerShell); es posible que aparezcan peticiones de confirmación de UAC. Hace falta winget («App Installer» de la Microsoft Store, que viene por defecto en Windows 10/11).

```bat
cd AI2P_app
install_required.bat
```
Pedirá permisos de administrador. Pero no de inmediato. Puede que el diálogo no aparezca en primer plano: hay que buscarlo entre las aplicaciones activas.

Qué hace:

1. **.NET SDK 8+**: si no hay una versión ≥ 8, instala `Microsoft.DotNet.SDK.8` con winget; después comprueba el **runtime de ASP.NET Core 8.x** y, si no está, instala `Microsoft.DotNet.AspNetCore.8`;
2. **CMake**: si no está, instala `Kitware.CMake`;
3. **Compilador de C/C++**: busca MSVC con vswhere; si no está, instala VS 2022 Build Tools con la carga de trabajo VCTools (una descarga grande, de varios GB);
4. **Submódulos de git**: `git submodule update --init --recursive` (se omite mientras no haya `.gitmodules`).

Después de la instalación abra una consola **nueva** (para que se actualice el PATH) y ejecute el script otra vez: debería mostrar todos los `[OK]`.

### Linux (apt / dnf) y macOS (brew)

```sh
cd AI2P_app
chmod +x install_required.sh
./install_required.sh
```

Qué hace: determina el sistema operativo y el gestor de paquetes (apt-get / dnf / brew) y luego da los mismos pasos:

1. **.NET SDK 8+**: `dotnet-sdk-8.0` (en macOS, `brew install --cask dotnet-sdk`); después el **runtime de ASP.NET Core 8.x**: en Linux, el paquete `aspnetcore-runtime-8.0`; en macOS, el `dotnet-install.sh` oficial instala el runtime 8.0 junto al SDK que haya, en la misma carpeta de dotnet (puede pedir sudo);
2. **CMake**;
3. **Compilador de C/C++**: en Linux, `build-essential` (apt) o `gcc gcc-c++ make` (dnf); en macOS, las Xcode Command Line Tools (`xcode-select --install`; aparecerá un diálogo, y tras la instalación hay que ejecutar el script de nuevo);
4. **Submódulos de git**: como en Windows.

La instalación de paquetes puede pedir la contraseña de sudo. En macOS hace falta tener instalado de antemano [Homebrew](https://brew.sh/).

## Instalación de paquetes

Las dependencias se dividen en dos tipos (especificación, cap. 13.1):

1. **Binarias (NuGet)**: MudBlazor, Microsoft.Data.Sqlite, Serilog, Microsoft.Extensions.AI, el SDK de OpenAI, Anthropic.SDK, ModelContextProtocol y otras. **No hay que instalarlas aparte**: `dotnet restore` (parte de `dotnet build`) las descarga automáticamente por las referencias de los `.csproj`.
2. **Paquetes-subproyecto (código fuente)** en `packages/`: sqlite-vec, gigachat-adapter (candidatos, harán falta en las etapas 3 y siguientes). Ya no tienen scripts propios: `install_packages.bat` e `install_packages.sh` se eliminaron en T-65-S0 por innecesarios, porque en toda la historia del proyecto nunca llegó a aparecer en ellos una lista de paquetes, y el trabajo que se les atribuía lo hacen otros: las herramientas de compilación las instala `install_required.*`, y los paquetes de los modelos (ComfyUI, musubi-tuner, Python) los instala el propio programa al instalar el modelo. Si hiciera falta un subproyecto, se añade con un solo comando:

```sh
git submodule add <dirección> packages/<nombre>
```

Ahora la carpeta `packages/` ya no existe en el repositorio (la vacía se eliminó en T-207): todas
las dependencias actuales son de NuGet, y `git submodule add` crea la carpeta por sí mismo. También
se eliminó la carpeta vacía `native/`, el preparativo para los plugins de C/C++ (CMake).

## Idioma de la salida de los scripts

Los scripts de compilación e instalación hablan **en inglés por defecto** (T-65-S0). El script
no se duplica por idiomas: los textos están sacados fuera, a la carpeta `i18n/`:

```
i18n/scripts.en.txt          mensajes, idioma base
i18n/scripts.ru.txt          el mismo juego de claves en ruso
i18n/loc.ps1                 cargador para PowerShell
i18n/loc.sh                  cargador para POSIX sh
i18n/help/<script>.<idioma>.txt  el texto que imprime --help
```

El idioma se elige por una escalera, y gana el primero que no esté vacío:

1. la opción del script: `-Lang ru` (PowerShell) o `--lang ru` (sh);
2. la variable de entorno `AI2P_LANG`;
3. `en`.

La clave `"language"` de `config.json` NO se consulta a propósito: el idioma de la interfaz de
la aplicación y el idioma de la consola de instalación son cosas distintas.

La ayuda la imprime `--help` (en los `.ps1`, `-Help`), y se toma de un archivo aparte:

```powershell
install.cmd --help
install.cmd D:\AI2P -Lang ru
.\makeAsServise.cmd --help
.\MakePackage.cmd --help
```

```sh
./install.sh --help
./install.sh ~/ai/AI2P --lang ru
./makeAsServise.sh --help
./MakePackage.sh --help
```

Los envoltorios `.cmd`/`.bat` no traducen nada por sí mismos y están escritos en **ASCII puro,
en inglés**: `cmd.exe` decodifica el archivo en la codificación de la consola pero mantiene la
posición de lectura en bytes, y un solo carácter multibyte desplaza el análisis de todo lo que
viene después (visto en vivo en T-34-S0). Todo el texto, incluido el de `--help`, lo pasan a su
`.ps1`.

Un idioma nuevo son **dos archivos y ni una línea de código**: una copia de `scripts.en.txt` con
los valores traducidos y una copia de los archivos necesarios de `help/`. La composición de las
claves tiene que coincidir una por una, y eso lo vigila `T65S0Tests`.

La salida propia de `build.*`, `buildRelease.*` e `install_required.*` no está traducida a
propósito (nivel B del análisis de T-64-S0): se ejecutan en la máquina de compilación y ya están
en inglés o mezcladas. La ayuda `--help` sí la tienen.

## Compilación y ejecución

La compilación de toda la solución (por ahora solo C#; el C/C++ se añadirá más adelante) con un solo script:

```powershell
# Windows
cd AI2P_app
.\build.ps1              # Debug; variante: .\build.ps1 -Configuration Release
build.cmd                # lo mismo; se ejecuta con Enter desde el explorador o FAR
                         # (en Windows los .ps1 tienen la asociación «editar», no «ejecutar»);
                         # variante: build.cmd Release
```

```sh
# Linux / macOS
cd AI2P_app
chmod +x build.sh
./build.sh               # Debug; variante: ./build.sh Release
```

Ejecución:

```sh
dotnet run --project src/AI2P.Server
```

El servidor levanta la interfaz en `http://localhost:5480` (el puerto y lo demás están en `config.json`) y abre el navegador (`openBrowserOnStart`). La ruta de la configuración se puede redefinir: `dotnet run --project src/AI2P.Server -- --config <ruta>`.

El `config.json` con los ajustes por defecto se copia a la carpeta de compilación **sólo si todavía no está allí**: las ediciones locales no se pierden al recompilar (especificación, cap. 10).

## Publicación de entrega en una carpeta aparte

La publicación es un conjunto autosuficiente de archivos que se ejecuta sin el código fuente y sin `dotnet run`.
Hace falta para que, junto a la versión en desarrollo, quede una versión antigua que funcione.

```powershell
# Windows
cd AI2P_app
buildRelease.cmd                          # Release en builds\windows\release
buildRelease.cmd D:\AI2P_v1.45            # en la carpeta indicada
.\buildRelease.ps1 -Clean                 # limpiar la carpeta (salvo los datos) y publicar de nuevo
.\buildRelease.ps1 -SelfContained         # con el runtime dentro: no hace falta ASP.NET Core 8 en la máquina
```

```sh
# Linux / macOS
cd AI2P_app
chmod +x buildRelease.sh
./buildRelease.sh                         # Release en builds/linux/release
./buildRelease.sh ~/ai/AI2P --clean
```

Hasta T-285 esos scripts se llamaban `publish.cmd` / `publish.ps1` / `publish.sh`: el nombre ha
cambiado, el comportamiento es el de antes.

La propia carpeta `builds` está **dentro de la carpeta de trabajo** `AI2P_app`, junto a `AI2P.sln`
(T-131-S0); antes de 1.114 se creaba un nivel más arriba, fuera de la carpeta de trabajo.

Entre `builds` y `release`/`releasefull` está la **carpeta del sistema operativo** (T-243):
`windows`, `linux` o `macos`. El sistema lo fija el RID de la publicación completa
(`-Runtime`/`--runtime`), y en la normal, aquel en el que se compila; ese mismo determina qué
scripts de instalación se ponen en la publicación. Lo compilado para sistemas distintos ya no se
pisa entre sí.

Para ejecutar la copia publicada se usa `AI2P.Server.exe` (Windows) o `./AI2P.Server` desde su
carpeta; la carpeta actual del proceso no importa.

**El servidor funciona con un usuario NORMAL, no con root** (T-135). La carpeta de instalación en
Linux y macOS es `~/ai/AI2P`: los datos (`data/`), los registros (`logs/`), los ajustes y los
secretos están dentro de ella, el servidor no escribe fuera de la carpeta personal y el puerto
(5480) no es privilegiado. Los permisos de root sólo los necesita el instalador de dependencias
(`install_required.sh`), que es una operación puntual de nivel de sistema. El archivo
`secrets.json` la aplicación lo cierra con permisos `0600`: en él están la contraseña del
administrador del servidor y las claves de las organizaciones.

Una ruta con `~` la entienden tanto los scripts como la propia aplicación: `./install.sh
~/ai/AI2P`, las carpetas de `config.json` y del formulario del servidor local (`~/ai`), la
carpeta del proyecto, las reglas de seguridad.

Lo que la publicación **no copia** y en una ejecución posterior **no sobrescribe**, y hay que trasladar a mano:

* `data/`: la base y los archivos de los proyectos;
* `logs/`: si hace falta;
* `secrets.json`: las claves de API (sin él los modelos en la nube no están activos; al lado hay un `secrets.example.json`);
* `config.json`: se crea a partir de los valores por defecto sólo si todavía no está en la carpeta.

Dos versiones a la vez en un mismo puerto no funcionan (comprobación de instancia única, especificación, cap. 3):
la segunda verá el puerto ocupado, abrirá el navegador en la primera y terminará. Para tener las dos en marcha,
cambie `ui.port` en el `config.json` de la copia publicada.

## Paquete de instalación en un solo archivo (T-285)

La publicación es una carpeta, y dársela así a una persona es incómodo. `MakePackage` hace de una
publicación terminada **un único archivo instalador**. El script lo pone en la publicación el
propio `buildRelease`, y hay que ejecutarlo **desde la carpeta de la publicación**; el resultado
va a `../../packages` (es decir, `builds/packages`).

```powershell
# Windows: hace falta Inno Setup 6 (lo instala install_required.bat)
cd builds\windows\releasefull
.\MakePackage.cmd                    # -> ..\..\packages\AI2P_v_1_99_full_windows_x64.exe
cd ..\release
.\MakePackage.cmd                    # -> ..\..\packages\AI2P_v_1_99_windows_x64.exe
```

```sh
# Linux / macOS: hace falta makeself (lo instala install_required.sh)
cd builds/linux/releasefull
./MakePackage.sh                     # -> ../../packages/AI2P_v_1_99_full_linux_x64.run
```

El nombre del archivo se compone solo a partir del `version.json` de la publicación: el número de
compilación `NN` no hay que preguntarlo:

| publicación | Windows | Linux | macOS |
|---|---|---|---|
| `releasefull` | `AI2P_v_1_NN_full_windows_x64.exe` | `AI2P_v_1_NN_full_linux_x64.run` | `AI2P_v_1_NN_full_macos_arm64.run` |
| `release` | `AI2P_v_1_NN_windows_x64.exe` | `AI2P_v_1_NN_linux_x64.run` | `AI2P_v_1_NN_macos_arm64.run` |

Las partes del nombre van en este orden: `AI2P_v_` + el número de versión + `_full` en la
publicación completa + el sistema (`windows`, `linux`, `macos`) + la arquitectura (`x64`, `arm64`,
`arm`, `x86`). En la publicación completa el sistema y la arquitectura los indica su runtime
(`win-x64`, `linux-arm64`, `osx-arm64`); en la normal, la carpeta del SO y la arquitectura del
compilador actual (T-234-S0).

La extensión la fija el tipo de instalador: en Windows es Inno Setup (`.exe`), y en Linux y
macOS, un archivo autoextraíble de makeself (`.run`) que por dentro ejecuta ese mismo
`install.sh`. El paquete se compila en el sistema para el que está compilada la publicación: no
hay con qué compilar un `.run` desde Windows, ni al revés, y el script lo dirá honestamente.

Los datos del usuario (`data/`, `logs/`, `secrets/`, `secrets.json`, el inventario
`installed.json`) no llegan al paquete, y al instalar encima de una instalación anterior no se
tocan: el `config.json` se pone sólo si no está, y al lado se deja siempre un `config.new.json`
que la aplicación fusionará en el primer arranque (`ConfigMerge`).

**Dónde están los archivos de trabajo (T-287).** Junto al programa, pero sólo si se puede
escribir en su carpeta. La instalación «para todos los usuarios» (`C:\Program Files\AI2P`) está
cerrada a escritura para un usuario normal, así que en ella `config.json`, `data/`, `logs/` y
`secrets/` están en la carpeta de datos común del ordenador: en Windows, `C:\ProgramData\AI2P`;
en Linux y macOS, `/var/lib/ai2p` (y si tampoco ahí se puede, en la carpeta de datos del
usuario). La regla vive en un solo sitio, `AppHome` (`src/AI2P.Server/AppHome.cs`); los scripts
no la repiten, sólo buscan un `config.json` ya creado en esos mismos sitios. La carpeta se puede
indicar también a mano: con la variable `AI2P_HOME` o con la opción `--config`. Los detalles, en
`doc/es/man/install.md`.

## Estructura de la solución

```
AI2P.sln
src/
├── AI2P.Core/        — núcleo: entidades (cap. 2), eventos (ap. 6.4.3), contratos de la API,
│                       interfaz IAgentConnector (ap. 7.2)
├── AI2P.Storage/     — SQLite (esquema ap. 6.4.2), event log, almacenamiento de archivos (ap. 6.4.4),
│                       servicios: proyectos, equipos, ejecutores, tareas, encargos, chat
├── AI2P.Connectors/  — HumanConnector + orquestador de lanzamiento de tareas; los conectores de IA son la etapa 2
├── AI2P.UI/          — componentes de Blazor (MudBlazor): el armazón de VS Code (cap. 11), el tablero,
│                       la ficha de la tarea, el Inbox, el historial, los catálogos; ApiClient, i18n
└── AI2P.Server/      — host de ASP.NET Core: API HTTP (/api/...) + interfaz Blazor Server,
                        config.json, Serilog (registros .jsonl)
tests/     — AI2P.Tests: pruebas del almacenamiento, del event log y del ciclo persona-ejecutor
i18n/      — diccionarios de localización (ru.json, en.json) y textos de los scripts
              de compilación e instalación (scripts.<idioma>.txt, loc.ps1, loc.sh,
              help/<script>.<idioma>.txt, T-65-S0); se copian a la carpeta de compilación
doc/       — documentación junto al código
```

## Pruebas

```sh
cd AI2P_app
dotnet test
```

## Almacenamiento de datos

La carpeta `storage.dataDir` de `config.json` (por defecto `./data`, junto a la aplicación):
`ai2p.db` (SQLite, WAL) + `projects/<slug>/tasks/<T-N>/description.md`, `artifacts/`, `.trash/`.
La fuente primaria de verdad sobre el historial es el registro de eventos (la tabla `events`); los registros técnicos están en
`logging.dir`, en archivos `ai2p-YYYYMMDD.jsonl`. Una segunda instancia de la aplicación detecta el puerto
ocupado, abre el navegador en la instancia que funciona y termina (especificación, cap. 3, principio 5).

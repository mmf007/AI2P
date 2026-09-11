# Manual del usuario de AI2P — índice de la sección

La sección `man/` es el **manual del usuario**: cómo hacer en AI2P aquello para lo que se
instala. A diferencia de las secciones `models/` e `import/`, los documentos de aquí no están
ligados a los registros de los catálogos: es texto continuo por capítulos, y su nombre de
archivo puede ser cualquiera.

El manual se lee desde la ventana de documentación (el icono de documentación en la barra
izquierda y en la parte inferior del navegador) o directamente desde la carpeta `doc/` que está
junto a la aplicación instalada.

## Índice de la sección

* [aboutdoc](aboutdoc.md) — Cómo está hecha la documentación de AI2P
* [Archives](Archives.md) — Archivado
* [build](build.md) — AI2P: compilación, publicación y estructura del repositorio
* [config](config.md) — Configuración
* [https](https.md) — HTTPS
* [install](install.md) — Instalación de AI2P y dónde están sus datos
* [LoRAEditor](LoRAEditor.md) — Editor de LoRA
* [objects](objects.md) — Objetos del proyecto
* [performers](performers.md) — Ejecutores
* [plugins](plugins.md) — Complementos y MCP
* [progects](progects.md) — Proyectos
* [quickstart](quickstart.md) — Inicio rápido
* [sample_video1](sample_video1.md) — Vídeo a partir del fotograma de referencia de un personaje. Ejemplo
* [schedule](schedule.md) — Programación
* [servers](servers.md) — Varios servidores
* [service](service.md) — Ejecutar AI2P como servicio del sistema operativo
* [tasks](tasks.md) — Tareas
* [teams](teams.md) — Equipos
* [templates](templates.md) — Plantillas

## Qué hay dentro

La sección se lee en dos órdenes. **Para el principiante**, por orden: inicio rápido,
proyectos, tareas. **Para ir al grano**, por el capítulo que haga falta; cada uno es
autosuficiente y al final nombra a sus vecinos.

### Por dónde empezar

**[Inicio rápido](quickstart.md)**

* El camino mínimo desde la descarga del distributivo hasta el primer resultado de un agente de
  IA: cuál de los dos paquetes coger, los seis pasos del asistente de primer arranque, cómo dar
  un modelo al sistema (suscripción, clave de API o pesos locales), cómo plantear la primera
  tarea y qué ocurre después de pulsar «lanzar».

### Manual del administrador

**[Instalación de AI2P y dónde están sus datos](install.md)**

* Tres formas de instalarlo, qué cambia la respuesta «para todos los usuarios», dónde están los
  archivos de trabajo (`config.json`, `data/`, `logs/`, `secrets/`) y por qué en la instalación
  «para todos» se van a `C:\ProgramData\AI2P`, cómo indicar esa carpeta uno mismo (`AI2P_HOME`,
  `--config`), qué hace con la configuración la actualización de versión y qué hacer con los
  datos que quedan junto al programa.

**[Ejecutar AI2P como servicio del sistema operativo](service.md)**

* Cómo convertir la instalación en un servicio del SO (`makeAsServise` en Windows, Linux y
  macOS; el servicio se llama `AI2P`), en qué se diferencia el arranque como servicio del
  arranque en consola, cómo gestionar el servicio, en nombre de quién debe funcionar en Windows
  (DPAPI y claves de las organizaciones) y qué ocurre al actualizar la versión.

**[Configuración](config.md)**

* Los dos lugares donde viven los ajustes y por qué son dos; cómo abrir la pantalla de ajustes y
  qué hay en cada una de sus pestañas (general, modelos, catálogos, acciones, seguridad,
  usuarios, organizaciones, servidores, notificaciones, experiencia general); qué se edita sólo
  en `config.json` —el prefijo de la dirección, la interfaz de escucha, la segunda dirección,
  HTTPS— y tres reglas sobre los secretos, las carpetas de la plataforma y la actualización de
  versión.

**[HTTPS](https.md)**

* Cómo pasar el servidor a https: de dónde sacar el certificado (un archivo `.pfx`, un par PEM o
  el almacén de certificados del equipo), cómo hacerlo uno mismo con PowerShell y openssl, qué
  comprueba el propio programa antes de guardar y al arrancar, cómo **permitir una sola vez** el
  certificado propio en el navegador (Windows, macOS, Linux, Firefox, teléfonos), cómo vivir un
  clúster con el certificado de su propia AC y qué hacer cuando el servidor no ha arrancado.

**[Ejecutores](performers.md)**

* La persona y la IA como un mismo concepto; en qué se diferencia la cuenta del ejecutor; el
  perfil y la declaración de capacidades; la actividad, la ocupación, el límite de la ventana y
  el tiempo de espera de respuesta; cómo funcionan la selección automática y los ejecutores
  suplentes; por qué un modelo local ata el ejecutor a un servidor.

**[Equipos](teams.md)**

* El círculo de ejecutores de una tarea, el idioma de comunicación de los agentes, la jerarquía
  y el jefe de equipo, las dos actividades de participación, la puesta en marcha del trabajo del
  equipo y el arranque individual de un integrante, los servidores locales de modelos y cómo
  leer un error de conexión.

**[Programación](schedule.md)**

* Copia de una plantilla o acción del sistema, los campos de una programación y los periodos,
  por qué una programación tiene su propio servidor, la comprobación de solapamientos, las dos
  vistas (tabla y calendario) y, lo principal, la lista de disparos vencidos.

**[Varios servidores](servers.md)**

* Para qué sirve un clúster y qué no hace; el director y los códigos de los servidores; la
  propiedad de las filas; cómo conectar un segundo ordenador, sacarlo del clúster y cambiar de
  director; cómo está hecha la replicación de la base y de los archivos; la pantalla de
  diagnóstico, la cola de reintentos y los conflictos.

**[Archivado](Archives.md)**

* El traslado de datos del entorno de trabajo al entorno de archivo: para qué sirve, los cuatro
  estados de un archivo (actual, abierto, cerrado, eliminado), cómo crear un archivo y cómo
  abrirlo, cerrarlo, eliminarlo y volver a descargarlo desde otro servidor, el traslado manual
  de una tarea, una plantilla, un objeto, un registro de experiencia y un proyecto entero, las
  reglas de archivado (generales y del archivo), el archivado automático mediante una acción de
  la programación, la consulta del archivo con el campo «entorno» de la barra superior y la
  restauración de datos desde el archivo actual.

**[Complementos y MCP](plugins.md)**

* Cómo se conectan los programas externos y los servidores MCP: de qué se compone el registro de
  un complemento (acciones, experiencia, programa, documento), en qué se diferencia una pasarela
  de una conexión MCP, los cinco estados del complemento y por qué son propios de cada servidor,
  el orden de la inicialización, qué significa «programa no encontrado en este servidor» y por
  qué un MCP configurado por fuera de AI2P elude todas las reglas de seguridad.

### Manual del usuario

**[Proyectos](progects.md)**

* La carpeta del proyecto y por qué es propia de cada servidor, la carpeta `Common` y
  `.repignore`, las pestañas de la ficha (general, tareas, equipo, objetos, seguridad,
  plantillas, experiencia, historial), los ajustes «precio ↔ calidad» y «tiempo ↔ calidad», los
  objetos del proyecto y la referencia `@obj:`, los tres niveles de experiencia.

**[Objetos del proyecto](objects.md)**

* Para qué sirve un objeto y por qué la referencia `@obj:` es mejor que el recuento; los tipos de
  objeto; la lista y sus cuatro vistas; la pestaña del objeto, su barra de herramientas y la
  pestaña «Subobjetos»; el formulario del objeto, las dos referencias a él y la ventana «qué irá
  al modelo» con dos campos en un adaptador LoRA; el servidor propietario del objeto, el modo de
  sólo lectura en un servidor ajeno y el viaje del adaptador entrenado al vecino.

**[Plantillas](templates.md)**

* El esbozo de un proceso de trabajo y el lugar donde vive la experiencia; en qué se diferencia
  una plantilla de una tarea; las pestañas «Experiencia» y «Estadísticas», la selección de la
  experiencia por habilidades y etiquetas; las tres formas de desplegar una plantilla y qué
  ocurre con las tareas bloqueantes al copiarla.

**[Tareas](tasks.md)**

* La descripción de una tarea como prompt; las cuatro vistas de la lista, el filtro y la
  ordenación; las tres secciones del formulario; la ficha, la consola del encargo y los motivos
  de pausa; el chat, las preguntas del agente y la interrupción durante el trabajo; el
  lanzamiento manual, automático y jerárquico; las subtareas y la división automática; el
  trabajo con la tarea de otro servidor.

**[Editor de LoRA](LoRAEditor.md)**

* Cómo usar el editor de LoRA: qué hay que crear antes del entrenamiento, qué escribir en la
  descripción del adaptador y en los pies de los fotogramas, cuál de esos textos va al
  entrenamiento y cuál al prompt de generación, cómo lanzar y detener el entrenamiento, si
  importa el orden de los fotogramas del conjunto de datos, los errores frecuentes y lo que el
  editor no hace. Esta misma página se abre con el botón del libro desde el propio editor.

### Ejemplos y material auxiliar

**[Vídeo a partir del fotograma de referencia de un personaje](sample_video1.md)**

* Ejemplo de principio a fin: cómo obtener un vídeo a partir de una imagen de la carpeta del
  proyecto y cómo mantener al mismo personaje en una serie de fotogramas: instalación de un
  modelo de medios local, ejecutor de IA y puesta en marcha del trabajo del equipo, el objeto
  personaje y su pasaporte, la referencia `@obj:` en la descripción del fotograma, los
  parámetros de generación, los errores frecuentes.


### Manual del desarrollador
** [compilación, publicación y estructura del repositorio](build.md)**

* Cómo compilar el sistema a partir del código fuente

**[Cómo está hecha la documentación de AI2P](aboutdoc.md)**

* Sobre la propia documentación: dónde está la carpeta `doc/` y qué se distribuye de ella, de
  qué secciones consta, cómo se llaman los archivos de los documentos, cómo añadir un documento
  nuevo y cómo mantener los idiomas de acuerdo.

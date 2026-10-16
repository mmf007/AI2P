# Política de seguridad de AI2P

## Versiones con soporte

Las correcciones de seguridad se publican solo para la **última versión publicada** de AI2P
(la página Releases del repositorio). Las versiones anteriores no se corrigen: actualice a la última;
instalar encima de la anterior conserva los datos, los ajustes y las claves.

| Versión | Con soporte |
|---|---|
| la última publicada | sí |
| todas las anteriores | no |

## Cómo comunicar una vulnerabilidad

**No abra un issue público** ni escriba sobre la vulnerabilidad en Discussions: mientras no haya
corrección, una descripción pública ayuda a quien quiera aprovecharla.

Comuníquela de forma privada por una de estas vías:

1. **GitHub → pestaña Security → Report a vulnerability** (Private vulnerability reporting):
   la vía principal. El mensaje solo lo ven los mantenedores del repositorio.
2. **Correo**: a la dirección indicada en el perfil de GitHub del propietario del repositorio, con el asunto
   «AI2P security».

Qué adjuntar al mensaje:

* la versión de AI2P (el archivo `version.json` de la carpeta de instalación) y el sistema operativo;
* qué es exactamente vulnerable y qué riesgo supone;
* los pasos para reproducirlo o un ejemplo (una petición, un archivo, el texto de una tarea);
* si la conoce, la corrección que propone.

## Plazos de respuesta

* confirmación de recepción: en un plazo de **7 días**;
* primera valoración (si es una vulnerabilidad o no, y lo grave que es): en un plazo de **30 días**;
* la corrección sale en la siguiente versión; el plazo lo acordaremos con usted, y hasta que salga
  la corrección le pedimos que no publique los detalles. Si lo desea, le mencionaremos en la descripción de la versión.

## Modelo de amenazas: qué protege AI2P y qué no

AI2P es un programa local: el servidor funciona en su ordenador, y los agentes de IA que
lanza funcionan en ese mismo ordenador. De ahí vienen unos límites de la protección que conviene conocer de antemano.

**El agente CLI funciona con el usuario del servidor.** El agente de línea de comandos (Claude Code y
otros ejecutores CLI) se lanza como un proceso aparte con la misma cuenta del sistema operativo
que el servidor de AI2P, sin sandbox y sin aislamiento propio. Las reglas de seguridad de la tarea
(carpetas y acciones permitidas y prohibidas) se aplican a las herramientas del propio AI2P y
se le pasan al agente en el encargo, pero **el sistema operativo no las impone**: los medios propios del agente
(lectura de archivos, búsqueda, shell) no están limitados a la carpeta de trabajo del proyecto. Es decir, el agente
**puede leer archivos fuera del proyecto**: todo lo que sea accesible para esa cuenta,
incluidas las claves y los tokens de la carpeta personal. Los perfiles de la distribución lanzan el agente en modo
de edición sin confirmaciones.

Qué se deduce de esto en la práctica:

* ejecute el servidor de AI2P con una **cuenta del sistema operativo aparte**, sin acceso a datos ajenos ni personales,
  si no se le confía al agente todo lo que hay en la máquina; en Linux se puede ejecutar en un contenedor,
  montando dentro solo las carpetas de los proyectos;
* no guarde en esa máquina secretos que el agente no deba ver;
* las reglas «prohibir»/«preguntar» para un agente CLI son un acuerdo, no una barrera; la barrera son
  los permisos del sistema operativo;
* el texto de una tarea importada de un sistema externo (Trello, GitHub, GitLab, Figma) es
  una entrada de un tercero: puede contener indicaciones que el agente aceptará para ejecutarlas
  (prompt injection). Revise las tareas de fuentes ajenas antes de dárselas a un agente.

**El servidor y la red.** Por defecto, la interfaz y la API escuchan en `localhost`. El acceso a `/api` está cerrado con inicio de sesión.
Si abre el servidor a la red (varios servidores, acceso desde el teléfono), active HTTPS (ver «HTTPS» en el
manual del administrador) y no publique el servidor en internet sin un proxy inverso con
protección propia.

**Los secretos.** Las claves de API y la contraseña del administrador se guardan en el disco, en la carpeta de instalación
(`secrets.json`, `secrets/`), en texto plano con los permisos del usuario del servidor (en Linux y
macOS, `0600`). Su protección son los permisos del sistema de archivos y el cifrado del disco.

Consideramos vulnerabilidad saltarse estos límites: acceso a la API sin iniciar sesión, que el agente se salga de las reglas
allí donde AI2P promete hacerlas cumplir con sus propias herramientas, una fuga de secretos a los registros, a las tareas
o a la réplica en otro servidor, la ejecución de código a partir de datos de importación o de replicación, etc.
Que un agente CLI lea los archivos de su cuenta es la propiedad descrita más arriba, no una vulnerabilidad.

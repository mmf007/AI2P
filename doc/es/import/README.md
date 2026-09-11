# Importación de tareas: cómo está hecho el catálogo de fuentes

Carpeta con los documentos de los tipos de fuente: un archivo por cada **tipo**, y el nombre del
archivo es el **código del tipo** (`trello.md`, `github.md`, `gitlab.md`). Se abre con el botón
**«i»** que hay junto al campo «Tipo» del formulario de la fuente: **Ajustes → Catálogos →
Importaciones**.

El documento está ligado al **tipo**, no al registro del catálogo: cómo conseguir las claves del
sistema externo es igual para todas las fuentes de ese tipo, y hay que saberlo incluso antes de
que el registro esté guardado.

## Índice de la sección

* [github](github.md) — Importación de tareas desde GitHub: instrucciones de conexión
* [gitlab](gitlab.md) — Importación de tareas desde GitLab: instrucciones de conexión
* [trello](trello.md) — Importación de tareas desde Trello: instrucciones de conexión

## Qué tienen en común todos los tipos

**Los secretos pueden ser uno o dos.** Trello tiene dos, la **API key** y el **Token**, y por eso
en el formulario de la fuente están tanto el botón «Establecer la clave de API» como el botón del
token. GitHub y GitLab tienen **un** solo secreto, el token personal de acceso; el botón de la
clave de API no lo tienen en absoluto, y la fuente se activa con el token solo. Las referencias a
secretos del tipo `trello.*` pertenecen únicamente a Trello.

**El secreto pertenece a la organización**: se cifra con su clave y se replica a los servidores
de la organización; no hace falta introducirlo en cada ordenador.

**Hay dos tipos de importación:** la masiva (por registro del catálogo: un tablero, un
repositorio, un proyecto) y la individual, por el enlace a una tarjeta o a una incidencia. La
individual elige el procedimiento **por el propio enlace**, y por eso la dirección de una
incidencia de GitHub y la forma antigua de la dirección de GitLab (sin el separador `/-/`) se
distinguen por el nombre del servidor: GitLab acepta la forma antigua sólo si en el nombre de
host aparece la palabra `gitlab`.

**La actualización desde la fuente** también va por el enlace de la tarea importada, con el botón
de su ficha. Una tarea importada recuerda de dónde llegó.

## Cómo añadir el documento de un tipo nuevo

Ponga aquí un archivo `<código del tipo>.md`, con exactamente el código con el que el tipo esté
nombrado en el catálogo (`trello`, `github`, `gitlab`): el nombre del archivo lo compone la
aplicación, y no se puede inventar. Cree un archivo igual en `../../ru/import/` y escríbalos
ambos en el índice de las secciones (con el script `test/t18s1/mktoc.py`).

Las direcciones externas de estos documentos escríbalas **completas**, con el esquema `https://`:
se abren con el botón «i» dentro de una página de la aplicación, y allí un enlace relativo está
muerto.

## Qué no hay en estos documentos

El procedimiento para conseguir las claves está descrito tal como era el día en que se escribió
el documento. Los sistemas externos cambian la interfaz de sus ajustes en silencio: si los
nombres de los botones no coinciden con el texto, busque la sección de los tokens personales
(Personal access tokens); la secuencia de acciones en sí es más estable que los rótulos.

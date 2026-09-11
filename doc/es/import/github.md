# Importación de tareas desde GitHub: instrucciones de conexión

La importación pasa las **incidencias (issues) de GitHub** a tareas de AI2P (especificación, ap.
2.10, cap. 11). Para conectarlo hace falta una cadena de GitHub —un **token personal de
acceso**— y un registro en el catálogo de importaciones. Paso a paso:

## 1. Conseguir el token

GitHub entrega los tokens en los ajustes de la cuenta. Los tokens son de dos clases, y sirven
ambas.

**Token de grano fino (fine-grained, recomendado, porque se le pueden dar exactamente los
permisos necesarios):**

1. Entre en GitHub y abra <https://github.com/settings/personal-access-tokens/new>.
2. **Token name**: cualquiera, por ejemplo `AI2P import`; **Expiration**: el tiempo de vida.
3. **Repository access**: «All repositories», o bien «Only select repositories» y enumere
   aquellos de los que vaya a importar.
4. **Permissions → Repository permissions → Issues**: ponga **Read-only**. (Al campo **Metadata**
   GitHub le dará acceso de lectura por su cuenta: es obligatorio.)
5. **Generate token** y copie la cadena que se muestra. Empieza por `github_pat_` y se muestra
   **una sola vez**.

**Token clásico (classic):**

1. Abra <https://github.com/settings/tokens> → **Generate new token (classic)**.
2. Marque el ámbito **`repo`** (da acceso a las incidencias de los repositorios privados); para
   los repositorios públicos basta con **`public_repo`**.
3. **Generate token** y copie la cadena: empieza por `ghp_`.

El token da acceso a sus repositorios: guárdelo como una contraseña. **Para los repositorios
públicos el token hace falta igualmente**: sin él GitHub permite sólo 60 peticiones por hora y
dirección, y la importación chocará con ese límite.

## 2. Crear la fuente en el catálogo de importaciones

**Ajustes → pestaña «Catálogos» → «Catálogo de importaciones» → «Añadir»:**

| Campo | Qué escribir |
| --- | --- |
| Nombre de la fuente | cualquiera, por ejemplo `GitHub de trabajo` |
| Tipo | GitHub |
| Propietario de los repositorios | el nombre de usuario **o de la organización**, lo que está en la dirección `github.com/<propietario>` |
| Filtro | subcadena del nombre del repositorio, por ejemplo `planner`; vacío significa todos los repositorios del propietario |
| Activa | de momento no disponible: se activará sola después del paso 3 |

Pulse **«Guardar»**. El botón para introducir el token aparece en un registro **ya guardado**: el
valor se pone en el almacenamiento de inmediato, así que antes de guardar no se sabe de quién
es.

## 3. Introducir el token

Abra la fuente guardada con el botón **«modificar»** y pulse **«Establecer el token»**: pegue la
cadena del paso 1 y guarde.

GitHub no tiene clave de API: el secreto es uno solo, y por eso en el formulario hay un solo
botón (Trello tiene dos). El valor anterior no se muestra nunca: para sustituir el token se
introduce uno nuevo. Debajo del formulario se ve el estado: «Token: establecido, origen: la
organización».

El formulario coteja la **forma** del valor y avisa si no se parece a un token de GitHub: los
actuales empiezan por `github_pat_` o `ghp_`, y los anteriores son 40 caracteres `0–9 a–f`.

**El botón «Comprobar la conexión»** le pregunta a GitHub «quién soy» y responde enseguida con
palabras: si se ha aceptado el token, a qué cuenta se ha entregado y si esa cuenta coincide con
el propietario indicado en la fuente. Comprobar aquí la conexión sale más barato que llevarse la
negativa en la primera importación.

**Dónde está el token.** En la base de datos de su organización, **cifrado** con la clave de la
organización (igual que las claves de API de los modelos): llega solo a todos los servidores de
la organización, y descifrarlo sólo puede el servidor al que se le ha entregado la clave de la
organización. No hace falta escribir nada en `secrets.json`; si aun así se pone el valor como
archivo, también se leerá: el orden de lectura es «organización → `secrets.json` → variable de
entorno».

**Mientras el token no esté introducido, la fuente no puede estar activa**: el interruptor
«Activa» está bloqueado. En cuanto se introduce el token, la fuente **se activa sola**; a partir
de ahí la actividad la cambia usted.

**Dos cuentas de GitHub.** Cada fuente tiene **su propia** referencia al token: cree una segunda
fuente e introduzca en ella otro valor.

## 4. Lanzar la importación

1. Elija el **proyecto**: la importación va siempre al proyecto actual.
2. En la lista de tareas pulse el botón de icono de la **nube con la flecha** («Importación de
   tareas»); sólo se ve cuando hay un proyecto elegido.
3. En el diálogo elija la fuente y las casillas:
   * **añadir nuevas** (activada por defecto): las incidencias que todavía no existan se
     convertirán en tareas de AI2P;
   * **actualizar las existentes**: en las tareas importadas anteriormente se actualizarán el
     título, la descripción y el plazo (las ediciones locales de esos campos se sobrescribirán).
4. Pulse **«Importar»**. El resultado se mostrará con un mensaje: «añadidas X, actualizadas Y,
   omitidas Z, mensajes de discusión N»; el registro `import.run` aparecerá en el «Historial de
   trabajos».

## Qué se importa exactamente

* Se cogen las **incidencias abiertas** de los repositorios del propietario; los repositorios se
  seleccionan por una subcadena del nombre (un filtro vacío significa todos).
* **Las solicitudes de fusión (pull requests) no se importan.** En la API de GitHub parecen
  incidencias, pero no se consideran incidencias y se omiten.
* Incidencia de GitHub → tarea de AI2P con estado **«borrador»**: el título, **el texto entero de
  la incidencia en la descripción** más una nota de origen abajo («Importado de GitHub:
  repositorio …, incidencia #N» con el enlace). Los ejecutores y la prioridad los rellena ya en
  AI2P.
* **La discusión de la incidencia se traslada al chat de la tarea de AI2P**: cada comentario se
  convierte en un mensaje, el autor se nombra como está nombrado en GitHub —`github:<usuario>`— y
  la hora del mensaje es la del comentario, de modo que se conserva la cronología. Una importación
  repetida añade **sólo los mensajes nuevos**: los ya trasladados se reconocen por su id externo.
* Una incidencia de GitHub no tiene **plazo**. Si la incidencia está asignada a un **hito
  (milestone)** con fecha, esa fecha pasa a ser el plazo de la tarea de AI2P.
* **No se crean duplicados**: el id interno de la incidencia se guarda en la tarea de AI2P
  (`github:<id>`); una importación repetida omitirá o actualizará esa misma incidencia, según la
  casilla.
* **Los archivos adjuntos a una incidencia de GitHub no se descargan.** GitHub no tiene una lista
  de adjuntos aparte: las imágenes y los archivos viven como enlaces dentro del propio texto, y
  como enlaces se quedan. En un repositorio público ese enlace se abre y la imagen se ve; un
  enlace a un archivo de un repositorio **privado** no se abrirá sin haber entrado en GitHub.
* Las etiquetas, los asignados y el estado de la incidencia de GitHub **no se trasladan**: se
  llevan en AI2P.

## 5. Importación de una sola incidencia por enlace

Junto al botón de la importación masiva, en la lista de tareas está el botón **«Importar un
encargo por URL»**:

1. Pegue un enlace del tipo `https://github.com/<propietario>/<repositorio>/issues/<número>`
   (sirve también una dirección con cola: `#issuecomment-…` y `?…` se descartan). La fuente se
   vuelve a preguntar sólo si hay varias fuentes de GitHub activas que sirvan: **el tipo de fuente
   lo determina el propio enlace**, así que una dirección de GitHub pegada no acabará en el
   importador de Trello.
2. El contenido de la incidencia se pondrá **en el formulario de tarea nueva**: título,
   descripción, plazo.
3. Termine de rellenar el formulario (ejecutor, prioridad) y guárdelo: la discusión se irá al chat
   de la tarea justo después de guardar. Una importación repetida de esa misma incidencia no
   creará un duplicado.

Para los agentes de IA esa misma acción está disponible con la herramienta
`import_task_from_url` (código de acción `AI2P.Tasks.ImportFromUrl`; las reglas de seguridad
deny/confirm se aplican como siempre). Qué procedimiento se hará cargo del enlace lo decide su
forma, así que al agente le basta con una sola herramienta para todas las fuentes.

## 6. Actualización de una tarea desde GitHub

Una tarea que ha llegado por importación tiene relleno el campo **«Enlace de importación»** (se ve
en la cabecera de la ficha y en la sección «Avanzado» del formulario de la tarea). Con ese enlace
la tarea se puede **releer desde la fuente**:

1. Abra la ficha de la tarea y pulse **«actualizar»**.
2. Aparecerá una reconfirmación con tres salidas:
   * **«Desde la fuente de importación»**: la incidencia de GitHub se lee de nuevo, y en la tarea
     de AI2P se ponen el título, el plazo y la **descripción entera** actualizados, y al chat
     llegan los mensajes nuevos de la discusión;
   * **«Sólo aquí»**: la relectura normal de la tarea desde la base, sin llamar a GitHub;
   * **«Cancelar»**.

Lo que la actualización **no** toca: los criterios de aceptación, los ejecutores, el estado, la
prioridad, las etiquetas, las subtareas y los vínculos; se llevan en AI2P, no en GitHub. **La
descripción se reescribe entera**: la actualización desde la fuente se lanza precisamente para
que en la tarea quede lo que hay ahora en GitHub. Los mensajes de discusión ya trasladados no se
duplican.

El enlace de importación se puede **escribir a mano**, y entonces se actualizará también una tarea
creada en AI2P sin importación; también se puede **vaciar el campo** para desligar la tarea de la
fuente (el botón «actualizar» pasará a ser una relectura normal).

## Si algo no funciona

| Mensaje | Causa y qué hacer |
| --- | --- |
| «No se ha encontrado el token de GitHub en la referencia …» | El token no está introducido: abra la fuente y pulse «Establecer el token» (paso 3). La ruta del archivo de secretos está indicada en el propio mensaje. |
| «Este servidor todavía no ha recibido la clave de la organización…» | El servidor está conectado a una organización ajena, pero la solicitud no la ha confirmado su director, y sin la clave de la organización no hay con qué cifrar el valor. Confirme la conexión (Ajustes → Servidores). |
| «GitHub no ha aceptado el TOKEN…» (respuesta `Bad credentials`) | El token es incorrecto, está revocado o ha caducado. El mensaje muestra la longitud y el prefijo del valor introducido, y por ellos se ve si en el campo ha ido a parar otra cosa. Repita el paso 1. |
| «GitHub ha denegado el acceso…» (403) | El token se ha aceptado, pero no tiene permisos sobre el repositorio: uno de grano fino necesita el acceso **Issues: Read** y el propio repositorio en la lista «Repository access»; uno clásico, el ámbito **repo**. |
| «GitHub deniega temporalmente: se ha agotado el límite de peticiones» | Ha saltado el rate limit de la API. Espere unos minutos; compruebe que la importación va **con token**, porque sin él el límite es 80 veces menor. |
| «GitHub no ha encontrado lo solicitado (404…)» | El propietario, el nombre del repositorio o el número de la incidencia son incorrectos. Un repositorio privado sobre el que el token no tiene permisos GitHub lo muestra como inexistente: es ese mismo 404. |
| «El enlace no parece una incidencia de GitHub…» | La dirección no tiene esa forma: hace falta `https://github.com/<propietario>/<repositorio>/issues/<número>`. Un enlace a un **pull request** (`/pull/<número>`) no sirve: sólo se importan incidencias. |
| La importación ha ido bien, pero «añadidas 0» | El filtro no ha coincidido con ningún repositorio, los repositorios no tienen incidencias abiertas, o todas las entradas abiertas han resultado ser solicitudes de fusión. Pruebe con el filtro vacío. |
| No se han importado todos los repositorios de una cuenta privada | El token está entregado a **otra** cuenta: no ve los repositorios privados ajenos. Compruébelo con el botón «Comprobar la conexión», que nombra la cuenta del token y avisa de la discrepancia con el propietario. |

Los mensajes llegan enteros a la propia ventana de importación. Los detalles de las peticiones
están en los registros de la aplicación (`logs/*.jsonl`, entradas de `GitHubImporter`): con qué
referencia y de qué almacenamiento se ha tomado el token, qué longitud y qué prefijo tiene (el
valor en sí no se escribe nunca en el registro) y qué respuesta ha devuelto GitHub.

## Límites

* En una importación se cogen hasta **10 páginas de 100 entradas** de cada clase: repositorios,
  incidencias del repositorio, comentarios de la incidencia. Eso alcanza para 1000 repositorios,
  1000 incidencias abiertas por repositorio y 1000 comentarios por incidencia; si se choca con el
  límite, se ve en el registro.
* Se admite **github.com**. GitHub Enterprise con su propia dirección de servidor no se admite por
  ahora.

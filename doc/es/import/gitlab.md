# Importación de tareas desde GitLab: instrucciones de conexión

La importación pasa las **incidencias de GitLab** (issue) a tareas de AI2P (especificación, ap.
2.10, cap. 11). Para conectarlo hace falta una cadena de GitLab —un **token personal de
acceso**— y un registro en el catálogo de importaciones. Funciona tanto con el `gitlab.com` en la
nube como con un servidor GitLab propio.

## 1. Conseguir el token de acceso

GitLab entrega los tokens en los ajustes del perfil:

1. Entre en GitLab en el navegador y abra
   <https://gitlab.com/-/user_settings/personal_access_tokens>
   (en un servidor propio, la misma ruta desde su dirección:
   `https://git.example.com/-/user_settings/personal_access_tokens`).
   La ruta de la página está indicada también en la propia interfaz: **avatar → Edit profile →
   Access tokens**.
2. Pulse **«Add new token»**. Rellene:
   * **Token name**: cualquiera, por ejemplo `AI2P import`;
   * **Expiration date**: la fecha de caducidad; una vez pasada, el token dejará de funcionar y la
     importación responderá «GitLab no ha aceptado el TOKEN»;
   * **Select scopes**: basta con **`read_api`**. El `api` completo también sirve, pero da derecho
     a modificar datos, y la importación sólo lee.
3. Pulse **«Create personal access token»** y **copie enseguida** el valor que se muestra: GitLab
   no lo mostrará una segunda vez. Los tokens actuales empiezan por `glpat-`.

El token da acceso a sus proyectos: guárdelo como una contraseña.

Un **token de proyecto o de grupo** (Settings → Access tokens dentro del proyecto) también sirve
si tiene el derecho `read_api`; resulta más cómodo porque ve exactamente un proyecto.

## 2. Crear la fuente en el catálogo de importaciones

**Ajustes → pestaña «Catálogos» → «Catálogo de importaciones» → «Añadir»:**

| Campo | Qué escribir |
| --- | --- |
| Nombre de la fuente | cualquiera, por ejemplo `GitLab principal` |
| Tipo | GitLab |
| Servidor GitLab | vacío es el `https://gitlab.com` en la nube; un servidor propio se indica completo, por ejemplo `https://git.example.com` |
| Proyecto de GitLab | la ruta del proyecto, del tipo `grupo/proyecto`, exactamente como se ve en la barra de direcciones; los subgrupos se escriben con barra (`grupo/subgrupo/proyecto`) |
| Etiquetas | selección de incidencias por etiquetas separadas por comas, por ejemplo `bug,ui`; vacío significa todas las incidencias abiertas del proyecto |
| Activa | de momento no disponible: se activará sola después del paso 3 |

Pulse **«Guardar»**. El botón para introducir el token aparece en un registro **ya guardado**: el
valor se pone en el almacenamiento de inmediato, así que antes de guardar no se sabe de quién
es.

**GitLab no tiene una «clave de API» aparte**, a diferencia de Trello, donde la clave y el token
son dos valores distintos. Por eso en el formulario de la fuente de GitLab hay un solo botón de
secreto, y la fuente pasa a estar activa con un solo token introducido.

## 3. Introducir el token

Abra la fuente guardada con el botón **«modificar»**: en el formulario ha aparecido el botón
**«Establecer el token»**. Pegue la cadena del paso 1 y pulse «Guardar».

El valor anterior no se muestra nunca: para sustituir el token se introduce uno nuevo. Debajo del
formulario se ve el estado: «Token: establecido, origen: la organización». Si el valor no se
parece a un token de GitLab (no empieza por `glpat-`), el formulario avisará: es una indicación,
no una prohibición, porque los tokens de proyecto y los de OAuth tienen otro aspecto y funcionan.

**El botón «Comprobar la conexión»** (al lado; funciona cuando el token está introducido) le
pregunta a GitLab «quién soy» y, si en la fuente hay un proyecto indicado, si ese token lo ve. La
respuesta llega con palabras: si se ha aceptado el token, a qué cuenta se ha entregado y si se ve
el proyecto. Comprobar aquí la conexión sale más barato que llevarse la negativa en la primera
importación.

**Dónde está ese valor.** En la base de datos de su organización, **cifrado** con la clave de la
organización (igual que las claves de API de los modelos): llega solo a todos los servidores de
la organización, y descifrarlo sólo puede el servidor al que se le ha entregado la clave de la
organización. No hace falta escribir nada en `secrets.json`.

**Dos cuentas o dos servidores de GitLab.** Cada fuente tiene **su propia** referencia al token:
cree una segunda fuente (por ejemplo, para su servidor GitLab) e introduzca en ella otro valor.
La importación de una sola incidencia por enlace elige la fuente ella misma, por la dirección del
servidor que haya en el enlace.

## 4. Lanzar la importación

1. Elija el **proyecto** (en la cabecera o en la pestaña del proyecto): la importación va siempre
   al proyecto actual de AI2P.
2. En la lista de tareas pulse el botón de icono de la **nube con la flecha** («Importación de
   tareas»); sólo se ve cuando hay un proyecto elegido.
3. En el diálogo elija la fuente y las casillas:
   * **añadir nuevas** (activada por defecto): las incidencias que todavía no existan se
     convertirán en tareas de AI2P;
   * **actualizar las existentes**: en las tareas importadas anteriormente se actualizarán el
     título, la descripción y el plazo desde GitLab (las ediciones locales de esos campos se
     sobrescribirán).
4. Pulse **«Importar»**. El resultado se mostrará con un mensaje: «añadidas X, actualizadas Y,
   omitidas Z»; el registro `import.run` aparecerá en el «Historial de trabajos».

## Qué se importa exactamente

* Se cogen las **incidencias abiertas** (`state=opened`) del proyecto indicado; si hay etiquetas
  indicadas, sólo las marcadas con ellas.
* Incidencia de GitLab → tarea de AI2P con estado **«borrador»**: el título, el plazo
  (`due_date`), **el propio texto** de la issue en la descripción y una nota de origen abajo
  («Importado de GitLab: proyecto …, incidencia #N» con el enlace). Los ejecutores y la prioridad
  los rellena ya en AI2P.
* **Los archivos adjuntos.** Una issue de GitLab no tiene una lista de adjuntos aparte: un archivo
  subido vive como enlace `/uploads/<hash>/<nombre>` **dentro del propio texto**. La importación
  encuentra esos enlaces, **descarga los archivos** al almacenamiento del proyecto de AI2P y
  reescribe los enlaces a los locales; las imágenes se quedan como imágenes. Un archivo que no se
  haya podido descargar (sin permisos, de más de 200 MB) se queda como enlace a GitLab: completo y
  no relativo, para que al menos se pueda ir a él.
* **La discusión.** Los comentarios de la incidencia se trasladan al **chat de la tarea** de AI2P:
  el autor se muestra como `gitlab:<usuario>`, y la hora y el orden se conservan. Las entradas de
  servicio de GitLab («ha cambiado la etiqueta», «ha asignado el responsable», «ha cerrado») se
  omiten: no son una conversación entre personas. Una importación repetida añade sólo los mensajes
  **nuevos** y no hace duplicados.
* **No se crean duplicados**: la clave externa de la incidencia se guarda con la forma
  `gitlab:<servidor>:<proyecto>:<número>`; una importación repetida omitirá o actualizará esa
  misma incidencia, según la casilla. El servidor forma parte de la clave a propósito: la
  incidencia n.º 42 de su GitLab y la n.º 42 del de la nube son incidencias distintas.

## Importación de una sola incidencia por enlace

Junto al botón de la importación masiva, en la lista de tareas está el botón **«Importar un
encargo por URL»**:

1. Pegue un enlace del tipo `https://gitlab.com/<grupo>/<proyecto>/-/issues/<número>`. Se
   entienden los subgrupos, la forma antigua de la dirección sin `/-/`, las colas `?…` y
   `#note_…`, y también la dirección de un servidor GitLab propio. La fuente se vuelve a preguntar
   sólo si hay varias fuentes gitlab activas y por la dirección del servidor no se puede elegir
   una.
2. El contenido de la incidencia se pondrá **en el formulario de tarea nueva**: título, el texto
   en la descripción, el plazo, los archivos descargados. La discusión llegará al chat justo
   después de guardar la tarea.
3. Termine de rellenar el formulario (ejecutor, prioridad) y guárdelo. Una importación repetida de
   esa misma incidencia no creará un duplicado.

Para los agentes de IA esa misma acción está disponible con la herramienta
`import_task_from_url` (código de acción `AI2P.Tasks.ImportFromUrl`; las reglas de seguridad
deny/confirm se aplican como siempre). Con qué procedimiento importar lo elige la **forma del
enlace**: el agente no necesita saber qué fuentes hay creadas.

## Actualización de una tarea desde GitLab

Una tarea importada tiene relleno el **enlace de importación** (sección «Avanzado» del formulario
de la tarea, y una línea en la cabecera de la ficha). El botón **«actualizar»** de la ficha de una
tarea así **vuelve a preguntar**:

* **«Desde la fuente de importación»**: releer la issue y poner en la tarea el título, el plazo, la
  descripción (con los archivos descargados actualizados) y los mensajes **nuevos** de la
  discusión;
* **«Sólo aquí»**: simplemente releer la tarea, sin preguntarle nada a GitLab;
* **«Cancelar»**.

La descripción, al actualizar, se reescribe **entera**, exactamente igual que en la importación
masiva con la casilla «actualizar las existentes». Los criterios de aceptación, los ejecutores, el
estado, la prioridad, las etiquetas y los vínculos de la tarea la actualización **no los toca**:
se llevan aquí, no en GitLab.

Si vacía el campo «Enlace de importación», la tarea se desligará de la fuente y ya no habrá
reconfirmación.

## Si algo no funciona

| Mensaje | Causa y qué hacer |
| --- | --- |
| «No se ha encontrado el token de GitLab (…)» | El token no está introducido: abra la fuente y pulse «Establecer el token» (paso 3). En una instalación antigua, compruebe `secrets.json`: la ruta del archivo está indicada en el propio mensaje. |
| «Este servidor todavía no ha recibido la clave de la organización…» | El servidor está conectado a una organización ajena, pero la solicitud no la ha confirmado su director, y sin la clave de la organización no hay con qué cifrar el valor. Confirme la conexión (Ajustes → Servidores). |
| «GitLab no ha aceptado el TOKEN (HTTP 401…)» | El token es incorrecto, está revocado o **caducado**, o no tiene el derecho `read_api`. Emita uno nuevo (paso 1). Una causa frecuente es haber copiado otro valor: el mensaje muestra la longitud de lo introducido (el valor en sí no se muestra nunca). |
| «GitLab ha denegado el acceso (HTTP 403…)» | El token se ha aceptado, pero su propietario no tiene permisos sobre ese proyecto, o el token no tiene `read_api`. |
| «GitLab (…) no ha encontrado lo solicitado (HTTP 404…)» | La ruta del proyecto o el número de la incidencia son incorrectos. **Un proyecto cerrado, invisible para el propietario del token, también responde 404**: GitLab no informa a propósito de la existencia de proyectos ajenos. |
| «GitLab responde “demasiado a menudo” (HTTP 429)» | Límite de frecuencia de peticiones. Espere un minuto y repita. |
| «La fuente de importación no tiene indicado el proyecto de GitLab» | El campo «Proyecto de GitLab» está vacío, y la importación masiva no tiene de dónde coger las incidencias. Escriba una ruta del tipo `grupo/proyecto`. |
| «El enlace … no parece una incidencia de GitLab» | La dirección no lleva a una issue: hace falta la forma `.../-/issues/<número>`. Un enlace a un merge request, a un tablero o a una épica no sirve. |
| La importación ha ido bien, pero «añadidas 0» | Las etiquetas no han coincidido con ninguna incidencia, o el proyecto no tiene incidencias abiertas; pruebe con el campo «Etiquetas» vacío. |
| Un archivo de la descripción no se abre | El archivo no se ha descargado (sin permisos, de más de 200 MB): en la descripción ha quedado el enlace a GitLab, y sólo se abrirá para quien haya entrado en GitLab. La causa está anotada en el registro. |

Los mensajes llegan enteros a la propia ventana de importación: no hay que pescarlos de una
indicación emergente. Los detalles de las peticiones están en los registros de la aplicación
(`logs/*.jsonl`, entradas de `GitLabImporter`): qué dirección se ha pedido, de dónde se ha tomado
el token y qué longitud tiene (el valor en sí no se escribe nunca en el registro) y qué ha
respondido GitLab.

## En qué se diferencia GitLab de Trello en esta importación

| | Trello | GitLab |
| --- | --- | --- |
| Secretos | dos: clave de API + token | uno: el token personal |
| Cómo viaja el secreto | como parámetros de la cadena de consulta | en la cabecera `PRIVATE-TOKEN` |
| Qué define la fuente | el usuario y el filtro por nombre de tablero | la dirección del servidor, la ruta del proyecto, las etiquetas |
| Unidad de importación | la tarjeta de un tablero | la incidencia (issue) de un proyecto |
| Adjuntos | en una lista aparte de la tarjeta | como enlaces dentro del texto |
| Clave externa de la tarea | `trello:<id de la tarjeta>` | `gitlab:<servidor>:<proyecto>:<número>` |

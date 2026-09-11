# Importación de tareas desde Trello: instrucciones de conexión

La importación pasa las tarjetas de Trello a tareas de AI2P (especificación, ap. 2.10, cap. 11).
Para conectarlo hacen falta dos cadenas de Trello —la **API key** y el **Token**— y un registro
en el catálogo de importaciones. Paso a paso:

## 1. Conseguir la API key

Trello entrega las claves a través de la página de integraciones (Power-Ups):

1. Entre en Trello en el navegador y abra <https://trello.com/power-ups/admin>.
2. Pulse **«New»** (crear una integración nueva). Rellene los campos obligatorios:
   * **Name**: cualquiera, por ejemplo `AI2P import`;
   * **Workspace**: su espacio de trabajo;
   * **Email / Author**: los suyos.
   El campo «Iframe connector URL» se puede dejar vacío.
3. Abra la integración creada → pestaña **«API key»** → botón **«Generate a new API key»**.
4. Copie el valor del campo **API key** (una cadena de letras y cifras).

## 2. Conseguir el Token

En esa misma página, a la derecha del campo de la API key, hay un enlace **«Token»** (en el texto
«you can manually generate a Token»):

1. Púlselo y se abrirá la página de autorización de Trello.
2. Pulse **«Permitir» (Allow)** al final de la página.
3. Copie el **token** que se muestra (una cadena larga).

El token da acceso a los tableros de su cuenta: guárdelo como una contraseña.

## 3. Crear la fuente en el catálogo de importaciones

**Ajustes → pestaña «Catálogos» → «Catálogo de importaciones» → «Añadir»:**

| Campo | Qué escribir |
| --- | --- |
| Nombre de la fuente | cualquiera, por ejemplo `Trello principal` |
| Tipo | Trello |
| Usuario de Trello | su **username**, que se ve en el perfil de Trello como `@nombre` (escríbalo sin la `@`) |
| Filtro | subcadena del nombre del tablero, por ejemplo `AI2P`; vacío significa todos los tableros |
| Activa | de momento no disponible: se activará sola después del paso 4 |

Pulse **«Guardar»**. Los botones para introducir la clave y el token aparecen en un registro **ya
guardado**: el valor se pone en el almacenamiento de inmediato, así que antes de guardar no se
sabe de quién es.

## 4. Introducir la clave de API y el token (v1.65)

Abra la fuente guardada con el botón **«modificar»**: en el formulario han aparecido dos botones:

1. **«Establecer la clave de API»**: pegue la cadena del paso 1 y «Guardar».
2. **«Establecer el token»**: pegue la cadena del paso 2 y «Guardar».

Cada botón pregunta **exactamente un campo**. El valor anterior no se muestra nunca: para
sustituir la clave se introduce una nueva. Debajo del formulario se ve el estado: «Clave de API:
establecido, origen: la organización», y lo mismo para el token.

**La clave y el token se confunden fácilmente**, y Trello responde a eso con un poco claro
«invalid key». Por eso el formulario coteja su forma y avisa: la **clave de API son exactamente
32 caracteres** `0–9 a–f`, y el **token, 64 caracteres iguales** o bien una cadena que empieza
por `ATTA`. Ni lo uno ni lo otro es el **«Secret»**: el secreto de la integración de esa misma
página no es un token y en AI2P no hace falta.

**El botón «Comprobar la conexión»** (junto a los botones de introducción, funciona cuando ambos
valores están rellenos) le pregunta a Trello «quién soy» y responde enseguida con palabras: si se
ha aceptado la clave, si se ha aceptado el token, a qué cuenta se ha entregado el token y si
coincide con el usuario de la fuente. Comprobar aquí la conexión sale más barato que llevarse la
negativa en la primera importación.

**Dónde están esos valores.** En la base de datos de su organización, **cifrados** con la clave de
la organización (igual que las claves de API de los modelos): llegan solos a todos los servidores
de la organización, y descifrarlos sólo puede el servidor al que se le ha entregado la clave de
la organización. Ya no hay que escribir nada en `secrets.json`.

**Mientras no se hayan introducido los dos valores, la fuente no puede estar activa**: el
interruptor «Activa» está bloqueado. En cuanto se introduce el último de los dos, la fuente **se
activa sola**; a partir de ahí la actividad la cambia usted.

**Las instalaciones en las que las claves ya están escritas en `secrets.json`** siguen
funcionando: los valores se leen en el orden «organización → `secrets.json` → variable de
entorno», y en el primer arranque de la versión nueva se trasladan del archivo a la organización,
de modo que el formulario mostrará enseguida «establecido». Las variables de entorno
`TRELLO_API_KEY` / `TRELLO_TOKEN` también siguen funcionando y no se trasladan a ninguna parte:
se ponen desde fuera a propósito.

**Dos cuentas de Trello.** Cada fuente creada a partir de la v1.65 tiene **su propio** par
clave/token: cree una segunda fuente e introduzca en ella otros valores. En las fuentes que
existían antes de la v1.65 el par es común (las referencias `trello.apiKey` / `trello.token` en
los parámetros del registro): para separarlas, cree la fuente de nuevo.

## 5. Lanzar la importación

1. Elija el **proyecto** (en la cabecera o en la pestaña del proyecto): la importación va siempre
   al proyecto actual.
2. En la lista de tareas pulse el botón de icono de la **nube con la flecha** («Importación de
   tareas»); sólo se ve cuando hay un proyecto elegido.
3. En el diálogo elija la fuente y las casillas:
   * **añadir nuevas** (activada por defecto): las tarjetas que todavía no existan se convertirán
     en tareas;
   * **actualizar las existentes**: en las tareas importadas anteriormente se actualizarán el
     título, la descripción y el plazo desde Trello (las ediciones locales de esos campos se
     sobrescribirán).
4. Pulse **«Importar»**. El resultado se mostrará con un mensaje: «añadidas X, actualizadas Y,
   omitidas Z»; el registro `import.run` aparecerá en el «Historial de trabajos».

## Qué se importa exactamente

* Se cogen las **tarjetas abiertas de los tableros abiertos** del usuario indicado; los tableros
  se filtran por una subcadena del nombre (un filtro vacío significa todos).
* Tarjeta → tarea con estado **«borrador»**: título, plazo (due), descripción de la tarjeta más
  una nota de origen abajo («Importado de Trello: tablero …, tarjeta» con el enlace). Los
  ejecutores y la prioridad los rellena ya en AI2P.
* **No se crean duplicados**: el id externo de la tarjeta se guarda en la tarea
  (`trello:<id>`); una importación repetida omitirá o actualizará esa misma tarjeta, según la
  casilla.

## Si algo no funciona

| Mensaje | Causa y qué hacer |
| --- | --- |
| «No se ha encontrado la clave de Trello (…)» | La clave de API o el token no están introducidos: abra la fuente y pulse «Establecer la clave de API» / «Establecer el token» (paso 4). En una instalación antigua, compruebe `secrets.json`: la ruta del archivo está indicada en el propio mensaje. |
| «Este servidor todavía no ha recibido la clave de la organización…» | El servidor está conectado a una organización ajena, pero la solicitud no la ha confirmado su director, y sin la clave de la organización no hay con qué cifrar los valores. Confirme la conexión (Ajustes → Servidores). |
| «Trello no ha aceptado la CLAVE de API…» (respuesta de Trello «invalid key») | La clave no es esa: lo más frecuente es que en el campo de la clave acabe el **token**; el mensaje lo dice directamente y muestra la longitud del valor introducido. Repita el paso 1 e introduzca la clave de nuevo. |
| «Trello no ha aceptado el TOKEN…» («invalid token») | El token es incorrecto, está revocado, o en su campo ha ido a parar la clave o el «Secret»: repita el paso 2. |
| «Trello ha denegado el acceso…» | La clave y el token se han aceptado, pero la cuenta a la que se ha entregado el token no tiene permisos sobre ese tablero o esa tarjeta. |
| «Trello no ha encontrado lo solicitado (HTTP 404…)» | El usuario (username) no existe o la tarjeta no es accesible para la cuenta del token: coteje con el perfil de Trello. |
| La importación ha ido bien, pero «añadidas 0» | El filtro no ha coincidido con ningún tablero, o el integrante no tiene tarjetas abiertas; pruebe con el filtro vacío. |

Los mensajes llegan enteros a la propia ventana de importación: no hay que pescarlos de una
indicación emergente. Los detalles de las peticiones están en los registros de la aplicación
(`logs/*.jsonl`, entradas de `TrelloImporter`): con qué referencia y de qué almacenamiento se han
tomado la clave y el token, qué longitud tienen (los valores en sí no se escriben nunca en el
registro) y qué respuesta ha devuelto Trello.


## Importación de una sola tarjeta por enlace (v1.50)

Junto al botón de la importación masiva, en la lista de tareas está el botón **«Importar un
encargo por URL»**:

1. Pegue el enlace a la tarjeta, del tipo `https://trello.com/c/<código>` (el botón «Compartir»
   de la tarjeta de Trello). La fuente se vuelve a preguntar sólo si hay varias fuentes trello
   activas: de ella se toman las claves.
2. El contenido de la tarjeta se pondrá **en el formulario de tarea nueva**: título, descripción,
   plazo. **Los archivos adjuntos de la tarjeta se descargan** al almacenamiento del proyecto y se
   insertan en la descripción como enlaces (las imágenes, como vista previa); los adjuntos que son
   enlaces se quedan como enlaces. Un archivo de más de 200 MB no se descarga: se queda como
   enlace con una nota.
3. Termine de rellenar el formulario (ejecutor, prioridad) y guárdelo. Una importación repetida de
   esa misma tarjeta no creará un duplicado.

Para los agentes de IA esa misma acción está disponible con la herramienta
`import_task_from_url` (código de acción `AI2P.Tasks.ImportFromUrl`; las reglas de seguridad
deny/confirm se aplican como siempre).

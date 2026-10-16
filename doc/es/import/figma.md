# Importar un diseño de Figma — guía de conexión

La importación convierte un **área de diseño de Figma** en una tarea de AI2P: las pantallas
(y, si se desea, los elementos de diseño) llegan como **imágenes**, y bajo cada imagen la
descripción de la tarea lista **todos los textos** de ese marco — botones, títulos, campos,
de arriba abajo. Así una tarea «haz esta pantalla» recibe el aspecto y los textos exactos.
Se necesita un token personal de Figma y un registro en el directorio de importaciones.

## 1. Obtener un token

**La API de Figma no responde sin token — ni siquiera para un archivo público** que se abre
en el navegador sin iniciar sesión. El token es siempre necesario.

1. Inicie sesión en Figma y abra <https://www.figma.com/settings> → pestaña **Security**.
2. En **Personal access tokens** pulse **Generate new token**.
3. Nombre libre, por ejemplo `AI2P import`; caducidad a su gusto.
4. Permisos (scopes): **File content → Read only**. No hace falta nada más.
5. **Generate token** y copie la cadena. Empieza por `figd_` y se muestra **una sola vez**.

El token lee todo lo que ve su cuenta: guárdelo como una contraseña.

## 2. Crear una fuente en el directorio de importaciones

**Ajustes → pestaña «Directorios» → «Directorio de importaciones» → «Añadir»:**

| Campo | Valor |
| --- | --- |
| Nombre de la fuente | libre, por ejemplo `Diseño Figma` |
| Tipo | Figma |
| Activa | aún no disponible: se activa sola tras el paso 3 |

Una fuente de Figma no tiene más campos: la importación funciona **solo por enlace** a un
área del diseño. Pulse **«Guardar»**.

## 3. Introducir el token

Abra la fuente guardada con **«editar»**, pulse **«Establecer token»** y pegue la cadena del
paso 1. Figma no tiene clave de API: un secreto, un botón. El formulario avisa si el valor
no empieza por `figd_`. En cuanto se introduce el token, la fuente **se activa sola**.

**«Comprobar conexión»** pregunta a Figma «quién soy» y responde con palabras: si el token
se acepta y a qué cuenta pertenece.

El token se guarda **cifrado** en la base de datos de la organización y llega a todos sus
servidores.

## 4. Importar un área del diseño

1. En Figma **seleccione el área** — una pantalla, una sección con pantallas o una página — y
   copie el enlace a la selección: clic derecho → **Copy link to selection** (o `Ctrl+L`).
   El enlace recibe el parámetro `node-id=…`: esa es el área.
2. En AI2P elija el proyecto y pulse **«Importar tarea por URL»** en la lista de tareas.
3. Pegue el enlace, por ejemplo
   `https://www.figma.com/design/rqN1QJdU35Q18H5mpUmlFd/Home-Planner?node-id=54965-55076`.
   Aparecen dos opciones:
   * **«Elementos de diseño como imágenes separadas»** — desactivado: solo las pantallas
     principales; activado: además cada elemento de la pantalla como imagen aparte con sus
     textos;
   * **«Importar todo el archivo»** — solo aparece si el enlace **no** tiene `node-id`. Sin
     esta marca el enlace se rechaza: un archivo completo puede ser enorme.
4. Pulse **«Importar»**: las imágenes se descargan al proyecto y el contenido rellena el
   formulario de la nueva tarea. Complételo y guarde.

### Qué es una pantalla y un elemento

* **Un enlace a un marco (frame)** es **una pantalla**: sus marcos anidados son partes de ella.
* **Un enlace a una página, sección o grupo**: las pantallas son los marcos de nivel superior
  dentro (secciones y grupos se atraviesan).
* **Elementos** (si se activan): componentes y sus instancias (sin entrar en ellas) y marcos
  y grupos anidados. Una instancia repetida del mismo componente con los mismos textos se toma
  una vez. Las capas muy pequeñas (menos de 8 px) se omiten.
* Las capas ocultas no se importan.

### Cómo es la descripción

Una cabecera de origen (archivo, área, enlace) y una sección por pantalla: `## 1. Nombre`,
la imagen y la lista **«Textos:»**. Con elementos, una subsección «Elementos de la pantalla
…» con la misma disposición por elemento. Imágenes PNG a 2× (un marco muy grande, a 1×).

## 5. Agentes de IA

Los agentes tienen la misma acción con la herramienta `import_task_from_url` (código
`AI2P.Tasks.ImportFromUrl`); el agente CLI, con
`ai2p import <enlace> --elements true|false [--wholeFile]`. **Ambas opciones son preguntas
para la persona**: hasta que el agente las indique no se importa nada y se le pide
preguntar. Un enlace sin `node-id` solo se importa con el consentimiento explícito de la
persona.

## 6. Actualizar una tarea desde Figma

La tarea importada tiene el **«Enlace de importación»**, que recuerda el área y la opción
«elementos» (`ai2p-elements=1`) o «todo el archivo» (`ai2p-whole=1`). **«Actualizar» →
«Desde la fuente de importación»** vuelve a leer el diseño igual: el título y la
**descripción completa se reescriben** con imágenes nuevas. Criterios de aceptación,
ejecutores, estado y vínculos no se tocan. Las imágenes anteriores quedan en uploads/.

## Si algo no funciona

| Mensaje | Causa y solución |
| --- | --- |
| «No se encontró el token de Figma por la referencia …» | No se introdujo el token: paso 3. |
| «Figma no aceptó el TOKEN…» (`Invalid token`, 403) | Token erróneo, revocado o caducado. Genere uno nuevo (paso 1). |
| «Figma denegó el acceso…» (403) | Falta el permiso **File content: Read** o el archivo no es visible para la cuenta del token. |
| «Figma no encontró el archivo o el nodo…» (404) | Enlace erróneo: clave del archivo o `node-id`. |
| «El enlace no indica un nodo (node-id)…» | Copie el enlace a la selección o active «Importar todo el archivo». |
| «Sin imagen…» bajo una pantalla | Figma no renderizó el nodo (vacío o demasiado grande). Los textos sí están. |
| «Se alcanzó el límite de imágenes por importación (300)…» | Demasiados elementos: importe partes del diseño con enlaces separados. |
| «Una fuente de Figma no tiene importación masiva» | Use «Importar tarea por URL». |

Los detalles están en los registros (`logs/*.jsonl`, entradas `FigmaImporter`); el valor del
token nunca se registra, solo su longitud y prefijo.

## Límites

* Como máximo **300 imágenes** por importación.
* Los comentarios del diseño no se pasan al chat de la tarea.
* No hay importación masiva, solo por enlace.

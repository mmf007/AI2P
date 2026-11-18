# Importar una página HTML («HTML público») — guía

La entrada **«HTML público»** ya está en el catálogo de importaciones, en la primera fila. No
tiene ajustes ni secretos: no hay que crearla, introducir claves ni comprobar la conexión.

## Cómo importar

Lista de tareas del proyecto → **«Importar tarea por URL»** → pegue la dirección de la página → **«Importar»**.
La dirección decide qué ocurre:

| Dirección | Qué se importa |
| --- | --- |
| `file:///C:/carpeta/pagina.htm` o una ruta completa `C:\carpeta\pagina.htm` | Una página guardada en disco. Las imágenes de la carpeta `…_files` contigua (la crea el navegador con «Guardar como… → completa») se copian a la tarea. Los marcos con archivos locales se despliegan. |
| `https://…` o `http://…` | Una página web pública: **solo esa página**, la importación no sigue los enlaces. Las imágenes se descargan y se copian a la tarea; todos los enlaces siguen activos y llevan al sitio de origen. |

La tarea se crea como borrador: el título sale de `<title>` (si no, del primer `<h1>`) y la
descripción es el texto de la página en Markdown. Si la página de origen se borra, la tarea queda intacta.

Las direcciones de Trello, GitHub, GitLab y Figma siguen yendo a sus importadores: «HTML público»
solo toma lo que nadie más reconoció.

## Limitaciones

* Una página dibujada por un script (aplicaciones de una sola página) llega casi vacía: la
  importación lee el HTML tal cual y no ejecuta scripts. Guarde esa página desde el navegador
  («Guardar como… → completa») e importe el archivo.
* Una página protegida por inicio de sesión no se puede leer: la importación no conoce las cookies del navegador.
* Límites: 64 MB por página, 50 MB por imagen, no más de 300 imágenes.
* Los estilos, formularios, vídeo y audio no se transfieren.
* Una página web no puede tomar archivos de este equipo: sus imágenes `file:` se omiten.
* Para un agente, un archivo en disco solo es legible en carpetas abiertas por las reglas de seguridad de la tarea.

## Actualización

El botón «actualizar» de la ficha de la tarea vuelve a leer la página por su enlace de importación.

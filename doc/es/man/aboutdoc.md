# Cómo está hecha la documentación de AI2P

Esta página trata de la documentación misma: dónde está, de qué se compone, cómo añadirle un
documento nuevo y cómo mantener los idiomas de acuerdo. La lista de documentos está en el
[índice](../index.md).

## Dónde está y qué se distribuye

La carpeta vive en `AI2P_app/doc/`, junto al código, porque **se distribuye junto con el
programa**: la compilación de la publicación copia `AI2P_app/doc` entero a la raíz de la
entrega, y la aplicación muestra esos documentos directamente desde la interfaz. La disposición
es igual en todos los idiomas (`doc/<idioma>/<sección>/…`).

**Un idioma tiene dos primeras páginas, y eso no es una duplicación:**

* **`index.md`, el índice.** Es adonde lleva el botón «Documentación»: la lectura empieza por la
  lista de páginas. El índice está escrito **a mano** y agrupado por sentido (manual del
  administrador, manual del usuario, ejemplos), por lo que el script que construye los índices no
  lo toca. Desde ahí son alcanzables por enlaces todos los documentos que haya en la entrega.
* **`README.md`, la descripción breve del sistema.** Lo que una persona ve primero en GitHub: qué
  es esto, para qué sirve y adónde ir después. También sigue siendo el punto de entrada de un
  idioma cuyo `index.md` todavía no se ha creado: el botón de documentación cae en él por su
  cuenta.

Los idiomas están enumerados en `AI2P_app/readme.md`: es el distribuidor de idiomas, y en él no
hay nada más que el logotipo, el nombre y la lista de enlaces.

Las imágenes están en `doc/images/` (comunes a todos los idiomas) y en `doc/<idioma>/images/`
(las dependientes del idioma: capturas de pantalla con rótulos). Una carpeta sin un solo `.md` no
se considera un idioma: no se le exige ni índice ni traducción.

Los documentos del proyecto (la especificación, los informes de investigación, el procedimiento
de publicación) no entran aquí: están en `doc/`, en la raíz del repositorio, y no van a la
entrega.

## Cómo están hechas las secciones

Una sección es una subcarpeta dentro de un idioma. Cada sección tiene **su propio `README.md`**,
que es el índice de la sección: enumera todos sus documentos y explica qué tienen en común.

Hoy hay tres secciones:

* `man/`: el [manual del usuario](README.md), texto continuo por capítulos, con cualquier nombre
  de archivo;
* `models/`: [un documento por cada registro del catálogo de modelos de IA](../models/README.md);
* `import/`: [un documento por cada tipo de fuente de tareas](../import/README.md).

El nombre del archivo de un documento lo deduce la aplicación **ella misma**: en los modelos es
el nombre del modelo del catálogo, y en las importaciones, el código del tipo de fuente. El
nombre se toma tal cual, y los caracteres no admitidos en un nombre de archivo se sustituyen por
`_`. Si el documento no existe en ningún idioma, el botón «i» abre una indicación con la ruta
completa donde hay que ponerlo.

## Cómo añadir un documento

1. Ponga el archivo en `<sección>/<nombre>.md`, con el nombre según la regla de la sección (véase
   su `README.md`).
2. Escríbalo **a mano** en el índice del idioma (`index.md`): allí los documentos están agrupados
   por sentido, y un script no puede adivinar el grupo. Al índice de la **sección** llegará por su
   cuenta: los índices de las secciones los construye el script `test/t18s1/mktoc.py` según lo que
   contienen realmente las carpetas; `python test/t18s1/mktoc.py --check` muestra si se han
   desviado de los archivos.
3. Enlace a los documentos vecinos con enlaces **relativos** (`models/GLM-5.2.md`,
   `../import/trello.md`): la ventana de documentación los resuelve. En los documentos que se
   abren con el botón **«i»** (modelos, importaciones) escriba las direcciones externas completas,
   con el esquema `https://`.

El archivo del índice se llama `README.md` en todas las carpetas: no hay que cambiar las
mayúsculas del nombre.

## Dónde poner los capítulos nuevos

Un capítulo nuevo es un archivo `man/<nombre>.md` aparte. Escríbalo en el [índice del
idioma](../index.md) (está escrito a mano y agrupado por sentido), y aquí, al índice de la
sección, llegará por su cuenta: esta lista la construye un script. Cree de paso una **traducción
con el mismo nombre** en `../../ru/man/`: la composición de los idiomas tiene que coincidir
archivo por archivo.

Una regla que conviene seguir: las cifras, los nombres de los campos y los rótulos de los botones
de un manual quedan obsoletos en silencio. Compruébelos no de memoria, sino con el diccionario de
la interfaz (`i18n/es.json`) y con el código; por ejemplo, la pestaña del catálogo de modelos se
llama **«Modelos»** y no «Modelos de IA».

## Traducciones

Los documentos de los demás idiomas están en las carpetas vecinas (`doc/ru/…`) y repiten la
disposición uno a uno: tantos archivos en `es` como haya en `ru`. Eso lo comprueba el script
`test/t18s1/cmp.py`, que imprime qué falta en cada idioma.

Si un documento no existe en el idioma de la interfaz, la aplicación muestra la versión rusa, y
si tampoco existe esa, la inglesa.

# Conjuntos de experiencia: estilos de trabajo

Documentos de los **conjuntos de experiencia** — un archivo por **código de conjunto**
(`style.tdd.md`). El documento se abre con el botón **«i»** en la fila del conjunto:
**Configuración → Experiencia → Conjuntos**.

Un conjunto de experiencia es un paquete listo de registros de experiencia que la persona
instala con un botón y obtiene la disciplina de trabajo de los agentes de fábrica: cómo
revisamos, cómo escribimos pruebas, cómo redactamos informes. El archivo del conjunto es
`packs/<código>/pack.json` en el directorio de datos; los registros van al ámbito que elijas al
instalar (reglas generales de la organización, experiencia de un proyecto o nodo de plantilla)
y se retiran enteros con un botón.

## Contenido de la sección

* [style.strict-review](style.strict-review.md) — Revisión de código estricta
* [style.tdd](style.tdd.md) — Desarrollo guiado por pruebas
* [style.research-report](style.research-report.md) — Investigación e informe
* [style.experience-analysis](style.experience-analysis.md) — Análisis de la experiencia

El último está aparte: `style.experience-analysis` no es un estilo de trabajo,
sino una plantilla de tareas para la revisión periódica de la experiencia acumulada.

## Qué tienen en común todos los conjuntos

**Los registros son reglas, no razonamientos.** Cada registro es corto, dice una sola cosa y
cambia el comportamiento del ejecutor. Repetir lo sabido no entra en un conjunto: consume el
límite de experiencia del trabajo y desplaza lo que de verdad hace falta.

**El registro lleva una habilidad.** La regla sobre pruebas llega a quien escribe pruebas; la
regla sobre informes, a quien escribe informes. Un registro sin habilidad no llega a ningún
trabajo, salvo los marcados «cargar siempre», y en la distribución hay exactamente uno.

**Nada propio de un proyecto.** Los conjuntos de la distribución no nombran productos, rutas de
archivos ni códigos de tareas: un conjunto se instala en cualquier organización. Lo tuyo va al
lado, como registros normales de la experiencia del proyecto.

**El conjunto se instala y se retira entero.** La instalación marca sus registros con la
etiqueta de servicio `pack:<código>`; la retirada se lleva exactamente esos y no toca ni tus
registros ni tus ediciones. Un registro que hayas editado sigue siendo tuyo: una segunda
instalación no lo sobrescribe.

## Cómo añadir el documento de un conjunto nuevo

Pon aquí un archivo `<código del conjunto>.md`, con el mismo código que lleva en `pack.json`.
Crea el mismo archivo en los demás idiomas: el conjunto de documentos debe coincidir en todos.

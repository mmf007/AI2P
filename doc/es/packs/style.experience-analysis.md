# Análisis de la experiencia

**Código del conjunto:** `style.experience-analysis` · **Registros:** 1 · **Nodos de plantilla:** 3

## Qué es este conjunto

Esto **no es un estilo de trabajo**, sino un **proceso de trabajo**: el conjunto trae consigo un
árbol listo de nodos de plantilla de tareas para la revisión periódica de la experiencia
acumulada. Tiene un solo registro — la regla general «la experiencia no se borra, se desactiva»;
todo lo demás vive en los encargos de los nodos.

## Para qué hace falta

La experiencia se acumula más rápido de lo que envejece. Tras medio año de trabajo la lista guarda
cientos de registros: unos se duplican, otros están obsoletos, otros acabaron en el ámbito
equivocado. Todo eso le quita sitio a lo necesario: el límite de experiencia de un encargo es uno
para todos, y un registro de más desplaza a uno útil.

Nadie va a ordenar eso a mano. Por eso la revisión se plantea como **una tarea para un agente de
IA**, por horario: una vez por semana o por mes.

## Qué hay dentro

| Nodo | Ámbito | Para qué |
|---|---|---|
| **Análisis de la experiencia** | raíz | la instrucción completa: qué leer, qué hacer, qué no hacer, qué poner en el informe |
| **Revisión de la experiencia general de la organización** | `general` | revisión de las reglas generales de trabajo |
| **Revisión de la experiencia del proyecto** | `project` | revisión de la experiencia de un proyecto; se copia por cada proyecto |

Los hijos reciben el texto del padre entero: el orden común de trabajo está escrito una sola vez en
la raíz, y el hijo solo le añade su ámbito.

**Qué le manda hacer el encargo al ejecutor:** leer las estadísticas de uso (`experience_usage`),
recorrer los registros por páginas (`list_experience`), luego **fundir** los duplicados en un
registro resumido, **dividir** los registros que mezclan dos lecciones, **etiquetarlos** por tema y
habilidad, **trasladarlos** a su ámbito (`move_experience`) y **desactivar** lo obsoleto
(`set_experience_active`).

**Qué está prohibido:** borrar registros, tocar registros de otro servidor, desactivar las reglas
suministradas con el distributivo y reescribir el sentido al fundir.

## La regla de desactivación por estadística

Está escrita **con palabras** en el encargo del nodo raíz, no fijada en el código:

> Un registro se desactiva si no llegó a ningún encargo durante tres meses **habiendo** habido
> tareas de su tema en ese tiempo (el tema son las etiquetas del registro). «No llegó porque no
> hubo tareas así» — no desactivar. Para un registro sin estadística alguna, el plazo se cuenta
> desde su fecha de creación.

Es la línea más importante del conjunto y lo primero que conviene ajustar: el plazo, la definición
de tema y la propia condición se editan directamente en la plantilla, sin publicar una versión
nueva del programa.

## Cómo usarlo

1. **Ajustes → Conjuntos de experiencia → «Análisis de la experiencia» → «Instalar»**, ámbito
   **proyecto** o **nodo de plantilla**. El ámbito «reglas generales de la organización» no tiene
   proyecto alguno, y una plantilla de tareas sin proyecto no vive en AI2P: entonces los nodos no
   se crean (el registro se instala como siempre).
2. **Plantillas del proyecto**: ha aparecido el nodo raíz «Análisis de la experiencia» con dos
   hijos. Copie el hijo «Revisión de la experiencia del proyecto» tantas veces como proyectos tenga
   y asigne a cada copia su proyecto.
3. **Ajustes → Horarios**: cree un horario periódico (semanal o mensual) y elija como plantilla el
   nodo **«Análisis de la experiencia»**. La instalación del conjunto no crea horario a propósito:
   ejecutar tareas gasta dinero en el modelo.

Al horario solo sirve el nodo **raíz**: los hijos llegan copiados con él.

## Qué conviene ajustar

* **El plazo de tres meses** es una cifra a ojo. Si publica una vez por trimestre, en tres meses
  puede no haber ni una tarea de un tema dado; tome medio año.
* **La periodicidad.** Semanal tiene sentido solo con mucho flujo de tareas; en un proyecto
  tranquilo basta una revisión mensual, y la semanal quemará dinero en balde.
* **El informe.** Los criterios de aceptación exigen los números «había / quedan activos» y los
  identificadores de los registros; sin ellos no hay cómo comprobar el trabajo. Si tiene sus
  propios requisitos de informe, póngalos en los criterios de aceptación del nodo, no en la
  descripción.
* **El ámbito de instalación.** El único registro del conjunto encaja en las reglas generales; los
  nodos de plantilla viven en un proyecto. Por eso lo habitual es instalar el conjunto en el ámbito
  «proyecto» del proyecto donde lleva sus tareas de servicio.
* **La limpieza tras la desactivación.** La revisión no archiva nada por sí misma. Cree una regla
  de archivado «experiencia + solo inactivos + la edad no importa»; si no, los registros
  desactivados se quedarán en las listas.

## Dónde seguir leyendo

* El contenido de la sección `packs/` — qué es un conjunto en general.
* El capítulo del manual **«Experiencia»** (`man/experience.md`) — los ámbitos, la selección para
  el encargo, la búsqueda, el traslado de un registro, la actividad y el archivado.

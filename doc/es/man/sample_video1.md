
# Vídeo a partir del fotograma de referencia de un personaje. Ejemplo

**El objetivo del capítulo:** en el proyecto hay una imagen de un personaje y hace falta que un
modelo de medios haga con ella un vídeo, y que en el vídeo siguiente el personaje tenga el mismo
aspecto.

Lo vemos con el modelo local **`Kandinsky-5.0-I2V-Lite-5s`** (imagen + texto → vídeo, 5
segundos). Todo lo que sigue vale para cualquier modelo del modo «imagen → vídeo»; en un modelo
«texto → vídeo» (`Kandinsky-5.0-T2V-Lite-sft-5s`) no hay dónde pasar el fotograma inicial: sólo
recibirá el texto.

## 1.1. Por qué la imagen no se puede describir simplemente con palabras

El modelo de medios recibe como prompt **sólo la descripción de la tarea** y nada más: ni el
título, ni los criterios de aceptación, ni la experiencia del proyecto, ni el chat; nada de eso
le llega. Es decir, el aspecto del personaje tiene que estar en la descripción del fotograma
literalmente.

Reescribirlo a mano en cada fotograma no vale: el recuento con palabras propias de un fotograma
a otro es precisamente la causa de que el personaje «se desdibuje». Por eso el aspecto se guarda
**una sola vez**, en un objeto del proyecto, y en la descripción del fotograma se pone una
**referencia** al objeto. En cada lanzamiento del encargo la referencia se despliega en el
pasaporte y las rutas de los archivos de referencia, y la primera ruta se le va al modelo como
fotograma inicial.

Corregir el pasaporte se aplica ya al siguiente fotograma: no hay que reescribir las
descripciones de las tareas.

## 1.2. Qué hará falta

| | |
|---|---|
| Permisos | el rol **admin** en la organización (el catálogo de modelos es un ajuste del sistema). Para **editar el registro de un modelo local** (activarlo o desactivarlo en este ordenador) hace falta además el **acceso local de administrador del servidor**: el enlace está en el menú del usuario de la cabecera y en «Ajustes → Servidores»; para la instalación en sí no se necesita |
| Equipo | NVIDIA, mínimo 6 GB de VRAM, controlador 580+, unos 18 GB en disco, 16 GB de RAM (los detalles, en el botón **«i»** del formulario del modelo) |
| Tiempo | un vídeo de 768×512, 121 fotogramas y 50 pasos con 6 GB de VRAM se calcula en aproximadamente una hora |
| Imagen | un archivo de referencia **dentro de la carpeta del proyecto**, con las proporciones mejor ya cercanas a 768×512 |

## 1.3. Paso 1. Instalar el modelo

1. **Ajustes** (el icono de la rueda dentada de la barra izquierda) → pestaña **«Modelos»**.
2. La fila `Kandinsky-5.0-I2V-Lite-5s` → se abrirá el formulario del modelo.
3. El botón **«i»** es el documento del modelo: requisitos de equipo, tamaños de los archivos,
   errores frecuentes.
4. El botón **«Instalar»** abre la ventana de instalación. Se instalan la compilación portátil de
   ComfyUI (unos 2,1 GB) y cuatro archivos de pesos (unos 16 GB; tres de ellos son comunes con el
   modelo de texto Kandinsky, así que si ya está instalado sólo se descargarán unos 4,3 GiB). La
   descarga es reanudable: una instalación interrumpida continuará desde donde se paró.

**Tras una instalación correcta el modelo se activa solo.** Mientras los archivos no estén en su
sitio no puede estar activo, y eso se comprueba también al arrancar la aplicación. La actividad
de un modelo local es **propia de cada ordenador del clúster** (en el formulario, «Activo en este
servidor»): en el servidor vecino se activa por separado, allí donde estén sus archivos.

La clave de API de este modelo no hace falta: ComfyUI se levanta localmente, sin autorización.

## 1.4. Paso 2. Crear un ejecutor de IA y ponerlo a trabajar

1. **Ejecutores** (el icono de las personas de la barra izquierda) → botón **«+»** («Añadir
   ejecutor»):
   * *Nombre externo (alias)*: cómo se le llamará en las tareas, por ejemplo `kandinsky-i2v`;
   * *Tipo*: **IA**;
   * *Nombre interno*: `Kandinsky-5.0-I2V-Lite-5s` (en la lista sólo están los modelos
     **activos**);
   * *Activo*: activarlo.
   El perfil de conexión y la declaración de capacidades se toman del registro del catálogo; no
   hace falta editarlos ahora.
2. **Equipos** → el equipo del proyecto (o «Añadir equipo») → sección **«Integrantes»** → añadir a
   ese ejecutor; asegúrese de que tiene puesto **«Activo en el equipo»**: un integrante no activo
   no se conecta al arrancar y no lo coge la selección automática.
3. **Pulsar «Iniciar»**, el equipo entero, o bien **«Iniciar el integrante»**.

El tercer paso es obligatorio, y he aquí por qué: **quien levanta ComfyUI es precisamente la
puesta en marcha del trabajo del equipo** (el comando de arranque se escribió en el perfil
durante la instalación). Si el trabajo no está en marcha y ComfyUI no se ha levantado a mano, el
encargo se caerá con el mensaje *«Tiempo de espera agotado al conectar con ComfyUI (¿el servidor
no está en marcha?)»*.

Mientras el modelo carga los pesos, el integrante está en estado «conectándose»: es normal, la
primera conexión lleva minutos.

## 1.5. Paso 3. La carpeta del proyecto y el archivo de referencia

El fotograma inicial se busca **en la carpeta del proyecto**, y la ruta es siempre relativa. La
salida hacia fuera (`..`, `C:\…`) está prohibida a propósito.

1. Ficha del proyecto → pestaña **«General»** → campo **«Ruta a la carpeta del proyecto»** (al
   lado está el botón de examinar). La carpeta es **propia de cada servidor del clúster**: en otro
   ordenador ese mismo proyecto vive en otra carpeta, pero las rutas relativas de dentro son las
   mismas.
2. Poner la referencia en esa carpeta, por ejemplo `refs/hero.png`.

El primer fotograma de la serie resulta cómodo obtenerlo con un modelo normal de «texto →
imagen» o dibujarlo a mano; a partir de él se construye todo lo demás.

## 1.6. Paso 4. Crear el objeto personaje

Ficha del proyecto → pestaña **«Objetos»** → botón **«+»** («Objeto nuevo»).

| Campo | Qué escribir |
|---|---|
| *Nombre* | cómo llaman las personas al personaje: `Héroe Vasia` |
| *Tipo* | **personaje** (tipos: personaje, localización, accesorio, estilo, fotograma de referencia, adaptador LoRA, archivo, equipamiento, recurso multimedia) |
| *Forma parte del objeto* | vacío en el propio personaje; en sus fotogramas de referencia, el personaje mismo |
| *Etiquetas* | un grupo propio, aparte de las etiquetas de las tareas |
| *Archivo o dirección* | `refs/hero.png`, la ruta **relativa a la carpeta del proyecto**; al lado está el botón de selección de archivo (se mueve por la carpeta del proyecto y pone la ruta relativa), y a la izquierda, la ventana de vista previa |
| *Pasaporte (va al prompt)* | la descripción **literal** del aspecto: este texto se pondrá en lugar de la referencia |
| *Activo* | activado |

El pasaporte es el campo principal del formulario. Escríbalo tal como quiere verlo en el prompt:

> Hombre de 35 años, barba oscura corta, cicatriz sobre la ceja izquierda, ojos verde grisáceo.
> Chaqueta de cuero marrón desgastada, bufanda gris, vaqueros. Iluminación fría, de atardecer.

Después de guardar, en el formulario aparecen dos botones:

* **«Referencia al objeto»**, lo que se pega en la descripción de la tarea (`@obj:OBJ-1`);
* **«Qué irá al modelo»**, el texto en el que se desplegará la referencia. Compruebe el pasaporte
  aquí, **antes** de la generación, y no por un fotograma que no se le parece.

**Varias referencias de un mismo personaje** se crean como hijos suyos: un objeto nuevo del tipo
«fotograma de referencia» con el personaje en el campo *«Forma parte del objeto»*. En la lista
aparecen debajo de él con sangría, y a la sustitución van todas sus rutas.

## 1.7. Paso 5. La descripción del fotograma

Creamos una tarea (el botón «nueva tarea» de la lista de tareas) y rellenamos:

| Campo | Valor |
|---|---|
| *Título* | `Escena 1: el héroe se da la vuelta` (al prompt **no** llegará) |
| *Descripción (.md)* | el texto del prompt + la referencia al objeto; véase más abajo |
| *Habilidades* | **`video-animate`**: eso es precisamente el modo «imagen → vídeo» (i2v) |
| *Ejecutor* | el ejecutor de IA creado; o bien dejarlo vacío y confiar en la selección automática, pero entonces la tarea tiene que tener **equipo**: se elige entre sus integrantes |
| *Proyecto* y *Equipo* | en la sección **«Avanzado»**; el proyecto es obligatorio, porque desde él se cuentan las rutas |

La descripción:

```
@obj:OBJ-1 se da la vuelta despacio por encima del hombro izquierdo y mira a la cámara.
Llovizna, reflejos de neón sobre el asfalto mojado, calle nocturna.
La cámara se acerca lentamente.
Результат положить в файл scenes/s01.mp4.
```

> **Ojo con la última línea.** Las indicaciones al modelo de medios («dónde poner el resultado»,
> «qué tomar como base») las reconoce el sistema por una lista de palabras que está **incrustada
> en el código** (`MediaOutputDirective.cs`, `MediaInputDirective.cs`) y que sólo tiene palabras
> **rusas e inglesas**. No dependen del idioma de la instalación ni del idioma del equipo. Por
> eso, en el ejemplo, el texto de la escena está traducido y la línea de la indicación está en
> ruso: escrita en español, no se reconocería, se iría al prompt de la generación como texto y
> estropearía el fotograma. En inglés esa misma línea sería
> `Save the result to the file scenes/s01.mp4.`

Qué ocurre aquí al lanzarlo:

1. `@obj:OBJ-1` se despliega en la ficha del objeto: nombre, tipo, número, el pasaporte literal y
   las rutas de los archivos de referencia, cada una en una línea nueva;
2. las rutas **se recortan** del prompt (son rutas, no texto de la escena), y la **primera** de
   ellas pasa a ser el fotograma inicial;
3. la línea «Результат положить в файл `scenes/s01.mp4`» también se recorta: la ejecuta el
   sistema, copiando el vídeo terminado a la carpeta del proyecto con ese nombre;
4. todo lo demás, incluido el pasaporte, se le va al modelo como prompt.

> **Escriba las indicaciones en una línea aparte.** En una descripción de varias líneas, la
> indicación («результат положить в …», «взять за основу …») se quita **con toda la línea**,
> junto con lo que usted haya escrito al lado. La línea «La cámara se acerca lentamente.
> Результат положить в файл scenes/s01.mp4.» le habría costado al prompt el acercamiento de la
> cámara. (Una descripción de una sola línea es la excepción: allí se quita sólo la frase con la
> indicación.)

**La referencia resulta cómodo no teclearla a mano:** en la lista de objetos cada fila tiene un
botón de «referencia», y en el editor de la descripción está el botón **«Referencia a un objeto
del proyecto»**, que inserta la referencia en la posición del cursor con la forma que fije el
ajuste del proyecto *«Formato de la referencia a un objeto»*: por número (`@obj:OBJ-1`) o por
nombre (`@obj:[Héroe Vasia]`). **Ambas formas se leen siempre**, así que cambiar el ajuste no
rompe las descripciones ya escritas.

### Si hace falta un fotograma concreto y no el primero

Nombre el archivo con palabras: esa indicación es **más fuerte** que la ruta del objeto:

```
Взять за основу refs/hero_side.png.
@obj:OBJ-1 se da la vuelta despacio por encima del hombro izquierdo…
```

Se considera indicación una línea en la que haya un nombre de archivo de imagen y una palabra
como «исходный», «за основу», «стартовый», «эталон», «референс», «оживи», `source`, `start`,
`based on`. Esa línea se quita entera del prompt. Insistimos: esas palabras son las que hay en el
código, y no tienen equivalentes en español.

### Una limitación importante

El fotograma inicial del modelo es **uno solo**. Las demás rutas de referencias simplemente se
quitan del prompt: el modelo no las ve. Tener varios fotogramas de referencia en un objeto es
útil para la persona y para el futuro entrenamiento de un adaptador LoRA, pero en la generación
i2v sólo influye la primera ruta: la del propio objeto, y si está vacía, la del primer hijo
activo.

## 1.8. Paso 6. Lanzamiento y observación

El botón **«Lanzar»** de la ficha de la tarea.

* La pestaña **«Encargos»** es la lista de encargos y la **consola**: allí se vuelca la salida del
  propio ComfyUI, incluido el contador de pasos. Por ella se ve si la generación avanza o se ha
  parado.
* La primera línea de la consola es el resumen del lanzamiento: la dirección de ComfyUI, el
  modelo, el tamaño del fotograma, el número de fotogramas y de pasos, el `seed`, la longitud del
  prompt, el fotograma inicial y el archivo del resultado.
* Hay que esperar mucho: alrededor de una hora con 6 GB de VRAM. El límite de espera es el
  «Tiempo de espera de respuesta» del ejecutor, y si no está indicado, `params.timeoutMinutes`
  del perfil (por defecto, 180 minutos).
* Se puede detener con el botón **«Detener»** de la ficha de la tarea: la generación en ComfyUI se
  interrumpe y el encargo se retira de su cola.

**Qué se obtiene:**

* la pestaña **«Resultado»**: los archivos del encargo (`J-12-Kandinsky_00001.mp4`) y el resumen
  `J-12-result.md`: el modelo, el tamaño del fotograma, el número de fotogramas y de pasos, el
  **`seed`** y el tiempo de generación;
* una copia del vídeo en la carpeta del proyecto, si en la descripción había una indicación de
  dónde ponerlo;
* la petición exacta, en el archivo `J-12-request.json` (el prompt después de todas las
  sustituciones, `startImage`, `seed`, el grafo entero). Su ruta está anotada en el registro de
  trabajos, pestaña **«Historial»** de la tarea. Es la mejor forma de comprobar que el modelo ha
  recibido exactamente lo que usted pensaba.

## 1.9. El mismo personaje en una serie de fotogramas

| recurso | qué da |
|---|---|
| **el mismo objeto** en todos los fotogramas | la cara, la ropa y el color se mantienen entre vídeos |
| **`seed` fijo** | el mismo «carácter» de la generación; vacío o 0 es aleatorio en cada encargo |
| **el encadenado** | guardar el último fotograma de la escena N como imagen en la carpeta del proyecto y dárselo de entrada a la escena N+1 |
| **un mismo tamaño de fotograma** | los vídeos con `width`/`height` distintos se separan entre sí |

El `seed` y los demás parámetros de generación se editan en el perfil del modelo, con el botón
**«Perfil…»** del formulario del modelo o del formulario del ejecutor:

| Parámetro | Por defecto | Sentido |
|---|---|---|
| `width` / `height` | 768 × 512 | resolución del fotograma; a ella se ajusta también la imagen inicial (se encuadra por el centro) |
| `length` | 121 | fotogramas, unos 5 segundos |
| `steps` | 50 | pasos de difusión; menos es más rápido y más basto |
| `negative` | vacío | prompt negativo |
| `seed` | no | seed fijo |
| `timeoutMinutes` | 180 | cuánto esperar el resultado |

El perfil del ejecutor es suyo propio: se pueden crear dos ejecutores sobre un mismo modelo con
`seed` y resolución distintos.

## 1.10. Errores frecuentes

| Mensaje o síntoma | Qué hacer |
|---|---|
| **«Este modelo necesita un fotograma inicial…»** | en la descripción no hay ni archivo de imagen ni referencia a un objeto con archivo de referencia |
| **«El archivo del fotograma inicial no se ha encontrado en la carpeta del proyecto: …»** | la ruta se cuenta desde la carpeta del proyecto; compruebe la escritura y que el archivo esté precisamente ahí |
| **«El proyecto no tiene carpeta indicada en este ordenador…»** | indique la «Ruta a la carpeta del proyecto» en la ficha del proyecto: la carpeta es propia de cada servidor |
| **«El fotograma inicial … lleva fuera de la carpeta del proyecto»** | los `..` y las rutas absolutas están prohibidos a propósito |
| **«Tiempo de espera agotado al conectar con ComfyUI (¿el servidor no está en marcha?)»** | no se ha puesto en marcha el trabajo del equipo (paso 2), que es justo lo que levanta ComfyUI |
| **«La descripción de la tarea está vacía: un modelo multimedia necesita en la descripción el texto del prompt de generación»** | después de recortar las indicaciones no ha quedado nada de la descripción; escriba qué ocurre en el fotograma |
| **«el workflow de este modelo no admite fotograma inicial: el archivo … no se usa»** | la tarea se le ha ido a un modelo «texto → vídeo»; póngale a la tarea la habilidad `video-animate` o indique explícitamente el ejecutor que hace falta |
| **La referencia `@obj:…` se ha quedado en el prompt como texto** | el objeto no se ha encontrado: número o nombre equivocado, u objeto de otro proyecto. La referencia no se borra a propósito: de lo contrario el fotograma habría salido sin personaje y el prompt habría parecido correcto |
| **El personaje «se desdibuja»** | compruebe que los fotogramas hagan referencia a un mismo objeto, que el `seed` esté fijado, que el tamaño del fotograma sea el mismo y que el pasaporte no se haya vuelto a recontar en la descripción |
| **El proceso desaparece sin mensaje** | casi siempre es un controlador NVIDIA antiguo; empiece por `nvidia-smi`, hace falta el 580+ |
| **Falta VRAM** | reduzca `width`/`height` o `length` |

## 1.11. Qué conviene recordar

* El modelo de medios **no mantiene un diálogo**: no tiene ni herramientas ni chat. Escribirle al
  chat de la tarea no sirve de nada: un fotograma nuevo es un lanzamiento nuevo del encargo.
* El título, los criterios de aceptación, la experiencia del proyecto y la experiencia del nodo de
  la plantilla **no llegan** al prompt. Todo lo que deba llegar a la generación está en la
  descripción de la tarea o en el pasaporte del objeto.
* Una referencia dentro de un pasaporte **no se despliega**: un pasaporte que haga referencia a
  otro objeto dejará la referencia como texto. Es una protección contra objetos que se referencian
  unos a otros.
* Un objeto pertenece a un proyecto: una referencia no alcanzará a un objeto de un proyecto
  vecino.
* Un objeto desactivado se queda en la lista y las referencias a él no se rompen: no es una
  eliminación; pero los **hijos** desactivados no van a la sustitución de rutas.

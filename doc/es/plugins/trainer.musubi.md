# Musubi Tuner (LoRA) — el complemento `trainer.musubi`

**Qué hace:** entrena adaptadores **LoRA** con el entrenador musubi-tuner (kohya-ss): caché de
latentes, caché de las salidas de los codificadores de texto y el entrenamiento en sí. El
trabajo dura horas, por eso lo realiza una **tarea aparte** con un ejecutor de «software
automático» y no una herramienta del agente de IA.

**Para qué hace falta.** El entrenador oficial de Kandinsky exige varias tarjetas gráficas y
Linux; musubi-tuner se apaña con una sola tarjeta, funciona en Windows y guarda el archivo del
adaptador con los nombres de clave que ComfyUI entiende. El botón «Entrenar» del editor de LoRA
crea la tarea justamente sobre este complemento.

Las fuentes del entrenador: https://github.com/kohya-ss/musubi-tuner

---

## Los programas

El complemento tiene **dos** programas, y ambos son obligatorios:

| Registro | Qué es | Cómo se instala |
|---|---|---|
| **Python 3.10–3.12** | con él se lanzan los tres pasos | con el botón «Instalar» (nuestro paquete `python`) o con la ruta a un Python ya instalado. En 3.13 musubi-tuner no compila, y un 3.13 encontrado se considera no apto |
| **Musubi Tuner** | la carpeta de scripts del entrenador | con el botón «Instalar» (las fuentes v0.3.4) o con la ruta a la carpeta donde las descomprimió: allí está `kandinsky5_train_network.py`. En `PATH` no se busca: es una carpeta, no un programa |

El entorno con torch se lo crea el propio entrenador, en la subcarpeta `.venv`, en el primer
entrenamiento.

**El registro del complemento suele crearse solo**, al instalar un modelo que declara paquetes
de entrenamiento (las dos entradas de Kandinsky 5 los declaran). En ese mismo momento se anota
en el campo de ruta vacío el programa encontrado. La inicialización la sigue haciendo la
persona con el botón «Inicializar»: crea los registros del catálogo de acciones y, con ellos,
los puntos a los que se enganchan las reglas de seguridad.

---

## Las operaciones de ejecución larga

Tres pasos, exactamente en este orden:

| Operación | Qué hace |
|---|---|
| `lora.cacheLatents` | pasa los cuadros del conjunto de datos por el VAE y guarda los latentes en la caché |
| `lora.cacheText` | calcula las salidas de los codificadores de texto (Qwen2.5-VL y CLIP) sobre los pies de los cuadros; el paso es obligatorio, sin él el entrenamiento no arranca |
| `lora.train` | el entrenamiento del adaptador en sí; marcado como **«Una sola instancia»** |

La marca «Una sola instancia» en el entrenamiento no es un adorno: la tarjeta gráfica es una, y
un segundo entrenamiento lanzado a la vez acabaría en un fallo de memoria en un minuto. La
segunda tarea **espera** en la cola.

El resultado del entrenamiento es **el archivo `.safetensors` más reciente** de la carpeta de
trabajo según la máscara del nombre del adaptador: el nombre del archivo de la época no se
conoce de antemano. Los límites: el tiempo de espera del silencio es de 15 minutos y el límite
general, de un día.

---

## Los ajustes del registro

Se indican en **«Ajustes → Complementos y MCP»**, en el formulario del registro; en el texto de
una tarea no se pueden cambiar.

| Clave | Por defecto | Qué significa |
|---|---|---|
| `scripts` | vacío | la carpeta de scripts de musubi-tuner: hay que rellenarla |
| `dit`, `vae` | vacío | los archivos de pesos del modelo |
| `task` | `k5-lite-t2v-5s-sd` | la tarea del entrenador; la otra opción es `k5-lite-i2v-5s-sd` |
| `textEncoderQwen` | `Qwen/Qwen2.5-VL-7B-Instruct` | el codificador de texto |
| `textEncoderClip` | `openai/clip-vit-large-patch14` | el segundo codificador de texto |
| `steps` | `2000` | el número de pasos de entrenamiento |
| `networkDim`, `networkAlpha` | `32`, `32` | el tamaño de la red LoRA |
| `learningRate` | `1e-4` | la tasa de aprendizaje |
| `mixedPrecision` | `bf16` | la precisión de cálculo (`bf16`, `fp16`, `no`) |
| `outputName` | `lora` | el nombre del archivo del adaptador |

El entrenador necesita los pesos **originales** de los codificadores de texto (Qwen2.5-VL son
unos 16 GB), no las compilaciones recortadas de ComfyUI: con ellas el entrenamiento no irá.

---

## Limitaciones por sistema operativo

**Windows**: para él están hechos nuestros paquetes de Python y de musubi-tuner. **Linux y
macOS**: el entrenador los admite, pero nosotros no lo hemos comprobado en vivo; allí es más
seguro instalar Python y los scripts por sus propios medios e indicar las rutas a mano.

La tarjeta gráfica hace falta en cualquier caso: en el procesador el entrenamiento no se
calcula en un tiempo razonable.

---

## Lo que este complemento no hace

* **no publica herramientas al agente de IA**: el entrenamiento se plantea como tarea, no como
  llamada del agente;
* **no compone el conjunto de datos**: los cuadros, los pies y los archivos de configuración
  del entrenador los prepara el propio AI2P según los ajustes del modelo;
* **el entorno `.venv`** lo crea el entrenador, no el complemento;
* hoy las tres operaciones están descritas en el manifiesto, pero el arranque real del
  entrenamiento de Kandinsky lo lleva el comando del perfil del modelo; el complemento aporta
  de todos modos la atadura al servidor, la marca «Una sola instancia» y el punto al que se
  engancha la regla de seguridad.

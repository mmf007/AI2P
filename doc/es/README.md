<img src="../images/ai2p-logo.png" alt="AI2P" width="256">

# AI2P — AI to People

### Un planificador de tareas autoalojado en el que el ejecutor puede ser un agente de IA.<br>Su Jira, donde parte de las tarjetas se hacen solas.

<p align="center"><img src="../images/ai2p-demo.gif" alt="Demo de AI2P: crear una tarea, asignar un agente de IA, pulsar Iniciar, obtener el resultado" width="960"></p>

* **Autoalojado: los datos se quedan con usted.** La base, los archivos de los proyectos y las claves están junto al programa. Windows, Linux, macOS.
* **Cualquier modelo, también los locales.** Claude, GPT, Gemini, DeepSeek, Qwen por API, Claude Code por suscripción, modelos locales en su propia tarjeta de vídeo, y un clúster de varios ordenadores.
* **Personas e IA en una sola cola.** No es «un agente de IA en lugar del equipo»: la IA toma lo que sabe hacer y lo demás espera a las personas, como en cualquier planificador.

**[Inicio rápido](man/quickstart.md)** · **[Sitio web](https://ai2p.org/ai2p)** · **[Descargar](https://github.com/mmf007/AI2P/releases)** · Sin clave de API, AI2P funciona como un planificador corriente para personas.

<sub>Descripción completa más abajo ↓</sub>

<br>
<br>
<br>
<br>
<br>
<br>
<br>
<br>
<br>
<br>
<br>
<br>
<br>
<br>

---

AI2P es lo que suele ser Trello o Jira: proyectos, tareas, ejecutores, tablero e historial. La
diferencia es una sola y lo cambia todo: **el ejecutor de una tarea puede ser una IA**. Lo que la
IA sabe hacer sola lo hace sola, en el momento oportuno y sin que haya que recordárselo; lo demás
queda para las personas y las espera, como en cualquier planificador.

El sistema funciona **en su ordenador** (Windows 10–11, Linux, macOS) y la interfaz está en el
navegador. Los datos no se van a ninguna parte: la base, los archivos de los proyectos y las
claves están junto al programa.

## Índice

* [Qué hace](#qué-hace)
* [Cómo se ve en la práctica](#cómo-se-ve-en-la-práctica)
* [AI2P se construye a sí mismo](#ai2p-se-construye-a-sí-mismo)
* [Granja de IA: una tarea, muchos ordenadores](#granja-de-ia-una-tarea-muchos-ordenadores)
* [Qué hace falta](#qué-hace-falta)
* [Inicio rápido](#inicio-rápido)
* [Documentación](#documentación)
* [Para socios e inversores](#para-socios-e-inversores)
* [Licencia](#licencia)

## Qué hace

* **Lleva el trabajo, no sólo una lista de tareas pendientes.** Una tarea tiene ejecutor, plazo,
  prioridad, criterios de aceptación, tareas bloqueantes y subtareas. Terminar una tarea lanza por
  sí solo las siguientes, las que la esperaban.
* **Conecta IA de cualquier tipo.** Modelos de texto por API (Claude, GPT, Gemini, DeepSeek,
  Qwen, GigaChat, YandexGPT y otros), Claude Code por suscripción, modelos locales en su tarjeta
  gráfica, y también imágenes, vídeo, sonido y 3D. Todos ellos son ejecutores normales: cada uno
  con su alias, sus habilidades, su precio y sus límites.
* **Elige el ejecutor por su cuenta.** Según las habilidades de la tarea y el ajuste
  «precio ↔ calidad» del proyecto, el sistema decide a quién dársela: primero la IA, luego una persona,
  o al revés. Un ejecutor ocupado o que ha agotado su límite se omite, y la tarea espera
  a que se libere.
* **Divide el trabajo grande en partes.** El agente puede crear subtareas y lanzarlas por
  prioridad; la última tarea del grupo hace el balance y, si algo no cuadra, devuelve a sus
  vecinas a corrección.
* **Recuerda la experiencia.** Las lecciones obtenidas en el trabajo se anotan en el proyecto, en
  el nodo de una plantilla o en toda la organización, y se insertan solas en el siguiente encargo,
  cada una para su habilidad.
* **Repite los procesos habituales.** Una plantilla es un árbol de tareas con descripciones,
  habilidades y orden; a partir de ella se despliega en un solo paso un nuevo proceso de trabajo.
* **Funciona con programación**, desde «cada lunes» hasta acciones propias del sistema, como el
  archivado automático.
* **Vive en varios ordenadores.** Los servidores se unen en un clúster y replican los datos entre
  sí; cada tarea tiene un servidor propietario, en el que se ejecuta, de modo que un modelo local
  calcula allí donde está la tarjeta gráfica.
* **Toma tareas de sistemas externos**: Trello, GitHub, GitLab.
* **Mantiene la IA dentro de unos límites.** Las reglas de seguridad deciden qué se le permite al
  agente, qué exige la confirmación de una persona y qué está prohibido; fuera de la carpeta del
  proyecto el agente no sale.
* **Habla tu idioma**: la interfaz, los mensajes y las indicaciones del agente se toman de diccionarios; el idioma se elige en el archivo `README.md` que está junto a la carpeta `doc/`

## Cómo se ve en la práctica
<img src="../images/Demo_diagram_night.png" alt="Ejemplo de pantalla" width="900">

1. Crea un proyecto e indica su carpeta: en ella el agente leerá y escribirá archivos.
2. Escribe la tarea igual que se la escribiría a una persona: título, descripción, criterios de
   aceptación.
3. Pulsa «lanzar». La tarea va a un ejecutor de IA, su trabajo se ve en el historial y las
   preguntas las hace en el chat de la tarea, donde usted mismo le responde.
4. El resultado terminado queda como artefacto de la tarea, y la tarea pasa a revisión.

## AI2P se construye a sí mismo

AI2P se desarrolla en AI2P. Más de 140 versiones y cientos de tareas: especificaciones,
código, pruebas, documentación y compilación de versiones los hacen ejecutores de IA; una
persona plantea las tareas y acepta el resultado. Cada versión es un árbol de tareas: un
agente divide el trabajo en subtareas, las reparte entre ejecutores y la última tarea hace
el balance y devuelve a revisión lo que no cuadra. No es una demo sobre una lista de la
compra, sino un producto que cada día demuestra que funciona sobre sí mismo, con los
errores honestos de los agentes y su corrección incluidos.

## Granja de IA: una tarea, muchos ordenadores

<img src="../images/ai2p-farm.gif" alt="Granja de IA: la tarea padre en el director; las subtareas viajan por replicación a ordenadores con modelos locales y vuelven con el resultado" width="960">

Un modelo local vive donde están sus pesos y su tarjeta gráfica: el vídeo lo calcula un PC
de juegos con una tarjeta potente, los textos un modelo llama.cpp en una máquina de oficina,
las imágenes un Mac. El modelo no se puede trasladar, pero el **trabajo** sí. AI2P no
necesita para ello un «repartidor de trabajos» aparte: basta con la replicación que el
clúster ya tiene.

Cómo funciona:

1. **Los ordenadores se unen en un clúster.** Uno de ellos es el director de la
   organización y los demás se conectan a él ([Varios servidores](man/servers.md)). Las
   tareas, el chat, los ejecutores y la experiencia son comunes: cada servidor los replica con
   el director (la topología es una estrella).
2. **Cada máquina crea SUS PROPIOS ejecutores.** Un ejecutor de IA con un modelo local está
   ligado al servidor donde ese modelo está instalado: «Kandinsky en S1», «Qwen en S2»,
   «FLUX en S3».
3. **La tarea padre se divide en subtareas.** Un agente (o una plantilla) parte el trabajo
   en piezas —guion, guion gráfico, vídeo— y a cada una le asigna su ejecutor y un
   **servidor propietario**: el ordenador donde está el modelo adecuado. Una tarea también se
   puede pasar a otro servidor con el botón «cambiar servidor».
4. **La replicación reparte las subtareas.** La subtarea llega a su servidor por el registro
   de cambios, y el lanzamiento de la jerarquía va al propietario como solicitud. Cada
   ordenador ejecuta solo sus tareas y solo con su modelo, todos a la vez.
5. **Los resultados vuelven.** El resultado de la subtarea, sus artefactos y archivos se
   replican al resto de servidores; cuando las subtareas están entregadas, la tarea padre las
   reúne en un resultado común y devuelve a corrección lo que no cuadra.

Lo que se obtiene: una sola tarea «haz un vídeo» ocupa varias tarjetas gráficas a la vez, el
modelo en la nube solo planifica y el cálculo caro corre gratis en su propio hardware. Un
ordenador apagado no detiene a nadie: sus subtareas esperan y él alcanza a los demás al
encenderse.

Lo que la granja **no** hace: no es balanceo de carga. Una tarea se ejecuta solo en su
propietario y, si este está apagado, ningún otro servidor la toma por sí solo; el reparto lo
decide usted (o el agente) al elegir el ejecutor y el servidor de cada subtarea.

## Qué hace falta

* Windows 10/11, Linux o macOS; permisos de administrador, sólo para la instalación.
* Nada más: coja el **paquete completo** — `AI2P_v_1_NN_full_windows_x64.exe` para Windows,
  `AI2P_v_1_NN_full_linux_x64.run` para Linux, `AI2P_v_1_NN_full_macos_arm64.run` para macOS.
  Todo lo necesario para arrancar va dentro, no hace falta instalar .NET aparte.
  (El paquete sin `full` en el nombre es más pequeño, pero espera que el runtime ASP.NET Core 8.x ya esté instalado.)
* La clave de API de la IA que vaya a usar, o una suscripción de Claude Code, o una tarjeta
  gráfica para los modelos locales. Sin modelo, el sistema funciona como un planificador normal
  para personas.

## Inicio rápido  
Para el primer arranque en unos minutos consulte [Inicio rápido](man/quickstart.md)  
[Los distributivos están aquí](https://github.com/mmf007/AI2P/releases) — coja el paquete con `full` en el nombre  

## Documentación
Para la documentación detallada consulte [Documentación](index.md)

Secciones: [Manual](man/README.md) · [Modelos de IA](models/README.md) ·
[Importación de tareas](import/README.md) · [Complementos y MCP](plugins/README.md) ·
[Conjuntos de experiencia](packs/README.md)

## Para socios e inversores

Si quiere contribuir al desarrollo, lanzar una solución comercial basada en el proyecto o tratar una
inversión, consulte nuestra [página para socios e inversores](PARTNERS.md).

## Licencia

[![Licencia: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](../../LICENSE)

Este proyecto se distribuye bajo la licencia libre **Apache License 2.0**: el texto completo de las
condiciones está disponible en el archivo [LICENSE](../../LICENSE).

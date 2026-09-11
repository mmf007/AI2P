<img src="../images/ai2p-logo.png" alt="AI2P" width="256">

# AI2P — AI to People

**Un planificador de trabajos en el que las tareas no las ejecutan sólo las personas, sino también los agentes de IA.**

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

## Qué hace falta

* Windows 10/11, Linux o macOS; permisos de administrador, sólo para la instalación.
* El runtime **ASP.NET Core 8.x**: o bien se instala aparte, o bien se toma del paquete de
  instalación completo (`AI2P_full_…`), que ya lo lleva dentro.
* La clave de API de la IA que vaya a usar, o una suscripción de Claude Code, o una tarjeta
  gráfica para los modelos locales. Sin modelo, el sistema funciona como un planificador normal
  para personas.

## Inicio rápido  
Para el primer arranque en unos minutos consulte [Inicio rápido](man/quickstart.md)  
[Los distributivos están aquí](https://github.com/mmf007/AI2P/releases) 

## Documentación
Para la documentación detallada consulte [Documentación](index.md)

## Para socios e inversores

Si quiere contribuir al desarrollo, lanzar una solución comercial basada en el proyecto o tratar una
inversión, consulte nuestra [página para socios e inversores](PARTNERS.md).

## Licencia

[![Licencia: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](../../LICENSE)

Este proyecto se distribuye bajo la licencia libre **Apache License 2.0**: el texto completo de las
condiciones está disponible en el archivo [LICENSE](../../LICENSE).

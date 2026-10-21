# Plugin VS Code

El plugin prepara este servidor para vincular proyectos con VS Code (tarjeta del proyecto → «Conectar VS Code»).
No tiene acciones para el agente: se ocupa de los programas.

| Programa | Para qué | Cómo se encuentra |
|---|---|---|
| Visual Studio Code | el editor al que se vincula el proyecto | primero se busca uno instalado (comando `code` en PATH); si no hay, Instalar descarga la **última** versión estable como archivo portable en la carpeta de paquetes |

## Varias copias de VS Code

Si hay varias copias, indique en la fila «Visual Studio Code» del plugin la que se vincula a los proyectos
(Windows: `<carpeta de VS Code>\bin\code.cmd`). Esa copia es la **interna**: el selector de copia del proyecto
la marca como «interna» y la elige por defecto.

## Activación

Cuando VS Code se encuentra o se instala el plugin se activa solo. Un plugin desactivado a mano no se reactiva solo.

## Vínculo con el proyecto

«Conectar VS Code» primero comprueba el plugin; si no está conectado, se inicia la conexión y lo que falta
se instala en segundo plano. Sin VS Code el proyecto no se puede conectar.

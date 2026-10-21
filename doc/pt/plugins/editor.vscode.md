# Plugin VS Code

O plugin prepara este servidor para ligar projetos ao VS Code (cartão do projeto → «Conectar VS Code»).
Não tem ações para o agente: cuida dos programas.

| Programa | Para quê | Como é encontrado |
|---|---|---|
| Visual Studio Code | o editor ao qual o projeto é ligado | primeiro procura-se um instalado (comando `code` no PATH); se não houver, Instalar baixa a versão estável **mais recente** como arquivo portátil na pasta de pacotes |

## Várias cópias do VS Code

Se houver várias cópias, informe na linha «Visual Studio Code» do plugin a que será ligada aos projetos
(Windows: `<pasta do VS Code>\bin\code.cmd`). Essa cópia é a **interna**: o seletor de cópia do projeto
a marca como «interna» e a escolhe por padrão.

## Ativação

Quando o VS Code é encontrado ou instalado o plugin se ativa sozinho. Um plugin desativado à mão não volta sozinho.

## Ligação com o projeto

«Conectar VS Code» primeiro verifica o plugin; se não estiver conectado, a conexão é iniciada e o que falta
é instalado em segundo plano. Sem VS Code o projeto não pode ser conectado.

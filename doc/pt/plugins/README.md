# Plugins e MCP: os documentos dos plugins

O diretório com os documentos dos plugins: um arquivo por **código de plugin**
(`tool.ffmpeg.md`). O documento é aberto pelo botão **«i»** na linha do plugin:
**Configurações → Plugins e MCP**.

Um plugin é um registro da organização mais um arquivo de manifesto
`plugins/<código>/plugin.json` no diretório de dados. O manifesto descreve o que o plugin sabe
fazer (as suas ações, que são as ferramentas do agente de IA), onde obter o programa externo e
quais ajustes o registro tem; o programa em si e o caminho até ele pertencem a **este
computador** e não são replicados.

## Índice da seção

* [editor.blender](editor.blender.md) — Blender VSE — gateway `editor.blender`
* [editor.openshot](editor.openshot.md) — OpenShot — gateway `editor.openshot` (reserva)
* [editor.resolve](editor.resolve.md) — DaVinci Resolve (OTIO)
* [editor.shotcut](editor.shotcut.md) — Shotcut / Kdenlive (MLT XML)
* [tool.ffmpeg](tool.ffmpeg.md) — Conversor de vídeo (ffmpeg) — plugin `tool.ffmpeg`
* [trainer.musubi](trainer.musubi.md) — Musubi Tuner (LoRA) — o plugin `trainer.musubi`

## O que todos os plugins têm em comum

**As ações são estreitas e nomeadas.** Uma ferramenta do agente faz exatamente um trabalho
nomeado, e os seus parâmetros ficam no manifesto e nos ajustes do registro. Ações do tipo
«executar uma linha de comando» não existem de propósito: isso é execução de código arbitrário
com direito de escrever arquivos, e nenhuma regra de segurança a estreita.

**Uma ferramenta de plugin é coberta por uma regra de segurança**: cada ação tem o seu próprio
registro no catálogo de ações, criado na inicialização do plugin. Uma ferramenta sem esse
registro não é publicada de forma alguma.

**Os caminhos são limitados.** Um plugin pode ler e escrever na pasta do projeto e nos
diretórios externos abertos pelas regras de segurança da tarefa — em nenhum outro lugar.

**A instalação é local.** A descrição do plugin é replicada para todos os servidores da
organização, mas o programa encontrado e o seu caminho ficam no `config.json` do computador
onde ele está instalado.

## Como adicionar o documento de um plugin novo

Coloque aqui um arquivo `<código do plugin>.md` — exatamente com o código pelo qual o plugin é
nomeado no manifesto (`tool.ffmpeg`). Crie o mesmo arquivo em todos os outros idiomas: o conjunto
de documentos precisa coincidir em todos eles. O índice acima não se edita à mão — ele é montado
a partir do diretório pelo script `test/t18s1/mktoc.py`, que precisa ser rodado depois de o
arquivo ser acrescentado.

Escreva os endereços externos destes documentos **por inteiro**, com o esquema `https://`: o
documento é aberto pelo botão «i» dentro de uma página do aplicativo, e ali um link relativo
está morto.

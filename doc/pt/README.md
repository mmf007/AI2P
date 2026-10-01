<img src="../images/ai2p-logo.png" alt="AI2P" width="256">

# AI2P — AI to People

### Um planejador de tarefas auto-hospedado em que o executor pode ser um agente de IA.<br>O seu Jira, onde parte dos cartões se resolve sozinha.

<p align="center"><img src="../images/ai2p-demo.gif" alt="Demo do AI2P: criar uma tarefa, atribuir um agente de IA, clicar em Iniciar, receber o resultado" width="960"></p>

* **Auto-hospedado: os dados ficam com você.** O banco, os arquivos dos projetos e as chaves ficam ao lado do programa. Windows, Linux, macOS.
* **Qualquer modelo, inclusive os locais.** Claude, GPT, Gemini, DeepSeek, Qwen por API, Claude Code por assinatura, modelos locais na sua própria placa de vídeo, e um cluster de vários computadores.
* **Pessoas e IA na mesma fila.** Não é "um agente de IA no lugar da equipe": a IA pega o que sabe fazer, o resto espera pelas pessoas, como em qualquer planejador.

**[Início rápido](man/quickstart.md)** · **[Site](https://ai2p.org/ai2p)** · **[Baixar](https://github.com/mmf007/AI2P/releases)** · Sem chave de API, o AI2P funciona como um planejador comum para pessoas.

<sub>Descrição completa mais abaixo ↓</sub>

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

O AI2P é aquilo que costumam ser o Trello ou o Jira: projetos, tarefas, executores, quadro e
histórico. A diferença é uma só, e ela muda tudo: **o executor de uma tarefa pode ser uma IA**. O
que a IA sabe fazer sozinha, ela faz sozinha, na hora certa e sem precisar ser lembrada; o resto
fica para as pessoas e espera por elas, como em qualquer planejador.

O sistema funciona **no seu computador** (Windows 10–11, Linux, macOS) e a interface fica no
navegador. Os dados não vão para lugar nenhum: o banco, os arquivos dos projetos e as chaves ficam
ao lado do programa.

## Índice

* [O que ele faz](#o-que-ele-faz)
* [Como isso se parece na prática](#como-isso-se-parece-na-prática)
* [O AI2P constrói a si mesmo](#o-ai2p-constrói-a-si-mesmo)
* [O que é preciso](#o-que-é-preciso)
* [Início rápido](#início-rápido)
* [Documentação](#documentação)
* [Para parceiros e investidores](#para-parceiros-e-investidores)
* [Licença](#licença)

## O que ele faz

* **Conduz o trabalho, e não apenas uma lista de pendências.** Uma tarefa tem executor, prazo,
  prioridade, critérios de aceitação, tarefas bloqueadoras e subtarefas. Concluir uma tarefa inicia
  sozinho as seguintes — aquelas que estavam esperando por ela.
* **Conecta IA de qualquer tipo.** Modelos de texto por API (Claude, GPT, Gemini, DeepSeek, Qwen,
  GigaChat, YandexGPT e outros), Claude Code por assinatura, modelos locais na sua placa de vídeo
  e, além disso, imagem, vídeo, som e 3D. Todos eles são executores comuns: cada um com apelido,
  habilidades, preço e limites.
* **Escolhe o executor sozinho.** Pelas habilidades da tarefa e pelo ajuste «preço ↔ qualidade» do
  projeto, o sistema decide a quem entregá-la — primeiro a IA e depois a pessoa, ou o contrário. Um
  executor ocupado ou que esgotou o limite é pulado, e a tarefa espera até que ele fique livre.
* **Divide um trabalho grande em partes.** O agente pode criar subtarefas e iniciá-las por
  prioridade; a última tarefa do grupo faz o balanço e, se algo não fechar, devolve as vizinhas
  para ajuste.
* **Guarda a experiência.** As lições obtidas no trabalho são anotadas no projeto, no nó de um
  modelo ou na organização inteira, e entram sozinhas na próxima tarefa — cada uma para a sua
  habilidade.
* **Repete processos típicos.** Um modelo é uma árvore de tarefas com descrições, habilidades e
  ordem; a partir dele um novo processo de trabalho é criado com uma única ação.
* **Funciona por agenda** — de «toda segunda-feira» até ações próprias do sistema, por exemplo o
  arquivamento automático.
* **Vive em vários computadores.** Os servidores se unem em um cluster e replicam os dados entre
  si; cada tarefa tem um servidor dono, no qual ela é executada — assim um modelo local calcula
  onde está a placa de vídeo.
* **Traz tarefas de sistemas externos** — Trello, GitHub, GitLab.
* **Mantém a IA dentro de limites.** As regras de segurança decidem o que é permitido ao agente, o
  que exige confirmação de uma pessoa e o que é proibido; para fora da pasta do projeto o agente
  não sai.
* **Fala a sua língua** - a interface, as mensagens e os comandos do agente são retirados de dicionários; o idioma é escolhido no arquivo `README.md` ao lado da pasta `doc/`

## Como isso se parece na prática
<img src="../images/Demo_diagram_night.png" alt="Exemplo de tela" width="900">

1. Você cria um projeto e indica a pasta dele — é nela que o agente vai ler e escrever arquivos.
2. Escreve a tarefa do mesmo jeito que a escreveria para uma pessoa: título, descrição, critérios
   de aceitação.
3. Aperta «iniciar». A tarefa vai para um executor de IA, o trabalho dele aparece no histórico e as
   perguntas ele faz no chat da tarefa — e é ali mesmo que você responde.
4. O resultado pronto fica como artefato da tarefa, e a própria tarefa passa para revisão.

## O AI2P constrói a si mesmo

O AI2P é desenvolvido no próprio AI2P. Mais de 140 versões e centenas de tarefas:
especificações, código, testes, documentação e a montagem das versões são feitos por
executores de IA; uma pessoa define as tarefas e aceita o resultado. Cada versão é uma
árvore de tarefas: um agente divide o trabalho em subtarefas, distribui-as entre
executores, e a última tarefa faz o balanço e devolve para retrabalho o que não fechou.
Não é uma demo sobre uma lista de compras, e sim um produto que todo dia prova que funciona
em si mesmo — incluindo os erros honestos dos agentes e como foram corrigidos.

## O que é preciso

* Windows 10/11, Linux ou macOS; direitos de administrador apenas para a instalação.
* Nada mais: use o **pacote completo** — `AI2P_v_1_NN_full_windows_x64.exe` para Windows,
  `AI2P_v_1_NN_full_linux_x64.run` para Linux, `AI2P_v_1_NN_full_macos_arm64.run` para macOS.
  Tudo o que é preciso para rodar vem dentro dele, não é preciso instalar o .NET à parte.
  (O pacote sem `full` no nome é menor, mas espera que o runtime ASP.NET Core 8.x já esteja instalado.)
* A chave de API da IA que você for usar, ou uma assinatura do Claude Code, ou uma placa de vídeo
  para os modelos locais. Sem modelo, o sistema funciona como um planejador comum para pessoas.

## Início rápido
Para o primeiro início em poucos minutos veja [Início rápido](man/quickstart.md)  
[As distribuições ficam aqui](https://github.com/mmf007/AI2P/releases) — use o pacote com `full` no nome  

## Documentação
Para a documentação detalhada veja [Documentação](index.md)

Secções: [Manual](man/README.md) · [Modelos de IA](models/README.md) ·
[Importação de tarefas](import/README.md) · [Plugins e MCP](plugins/README.md) ·
[Conjuntos de experiência](packs/README.md)

## Para parceiros e investidores

Se você quiser contribuir com o desenvolvimento, lançar uma solução comercial baseada no projeto ou
conversar sobre investimentos, veja a nossa
[página para parceiros e investidores](PARTNERS.md).

## Licença

[![Licença: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](../../LICENSE)

Este projeto é distribuído sob a licença livre **Apache License 2.0** — o texto completo das
condições está disponível no arquivo [LICENSE](../../LICENSE).

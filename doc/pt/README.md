<img src="../images/ai2p-logo.png" alt="AI2P" width="256">

# AI2P — AI to People

**Um planejador de trabalhos em que as tarefas são executadas não só por pessoas, mas também por agentes de IA.**

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

## O que é preciso

* Windows 10/11, Linux ou macOS; direitos de administrador apenas para a instalação.
* O runtime **ASP.NET Core 8.x** — ou é instalado à parte, ou vem do pacote de instalação completo
  (`AI2P_full_…`), que já o traz dentro.
* A chave de API da IA que você for usar, ou uma assinatura do Claude Code, ou uma placa de vídeo
  para os modelos locais. Sem modelo, o sistema funciona como um planejador comum para pessoas.

## Início rápido
Para o primeiro início em poucos minutos veja [Início rápido](man/quickstart.md)  
[As distribuições ficam aqui](https://github.com/mmf007/AI2P/releases)     

## Documentação
Para a documentação detalhada veja [Documentação](index.md)

## Para parceiros e investidores

Se você quiser contribuir com o desenvolvimento, lançar uma solução comercial baseada no projeto ou
conversar sobre investimentos, veja a nossa
[página para parceiros e investidores](PARTNERS.md).

## Licença

[![Licença: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](../../LICENSE)

Este projeto é distribuído sob a licença livre **Apache License 2.0** — o texto completo das
condições está disponível no arquivo [LICENSE](../../LICENSE).

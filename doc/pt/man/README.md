# Manual do usuário do AI2P — índice da seção

A seção `man/` é o **manual do usuário**: como fazer no AI2P aquilo para o que ele é instalado.
Ao contrário das seções `models/` e `import/`, os documentos daqui não estão presos a registros
de catálogos — é texto corrido por capítulos, e o nome do arquivo é livre.

O manual é lido pela janela da documentação (o ícone da documentação na barra da esquerda e no
fim do navegador) ou direto do diretório `doc/` ao lado do aplicativo instalado.

## Índice da seção

* [aboutdoc](aboutdoc.md) — Como a documentação do AI2P é organizada
* [Archives](Archives.md) — Arquivamento
* [audio](audio.md) — Trabalho com modelos de áudio
* [build](build.md) — AI2P — compilação, distribuição e estrutura do repositório
* [config](config.md) — Configuração
* [experience](experience.md) — Experiência
* [https](https.md) — HTTPS
* [install](install.md) — Instalação do AI2P e onde ficam os dados dele
* [LoRAEditor](LoRAEditor.md) — Editor de LoRA
* [objects](objects.md) — Objetos do projeto
* [performers](performers.md) — Executores
* [plugins](plugins.md) — Plugins e MCP
* [progects](progects.md) — Projetos
* [quickstart](quickstart.md) — Início rápido
* [sample_video1](sample_video1.md) — Vídeo a partir do quadro de referência de um personagem. Exemplo
* [schedule](schedule.md) — Agenda
* [servers](servers.md) — Vários servidores
* [service](service.md) — Executar o AI2P como serviço do sistema operacional
* [tasks](tasks.md) — Tarefas
* [TaskDo](TaskDo.md) — Algoritmo de execução das tarefas
* [teams](teams.md) — Equipes
* [templates](templates.md) — Modelos de processo

## O que há dentro

A seção é lida em duas ordens. **Para quem está começando**, na ordem: início rápido, projetos,
tarefas. **Para resolver algo**, a partir do capítulo desejado; cada um é autossuficiente e, no
fim, indica os vizinhos.

### Por onde começar

**[Início rápido](quickstart.md)**

* O caminho mínimo do download da distribuição até o primeiro resultado de um agente de IA: qual
  dos dois pacotes pegar, as seis etapas do assistente de primeiro início, como dar um modelo ao
  sistema (assinatura, chave de API ou pesos locais), como criar a primeira tarefa e o que
  acontece depois do «iniciar».

### Manual do administrador

**[Instalação do AI2P e onde ficam os dados dele](install.md)**

* As três formas de instalar, o que muda com a resposta «para todos os usuários», onde ficam os
  arquivos de trabalho (`config.json`, `data/`, `logs/`, `secrets/`) e por que na instalação
  «para todos» eles vão para `C:\ProgramData\AI2P`, como definir esse diretório você mesmo
  (`AI2P_HOME`, `--config`), o que a atualização de versão faz com a configuração e o que fazer
  com os dados que ficaram junto ao programa.

**[Executar o AI2P como serviço do sistema operacional](service.md)**

* Como transformar a instalação em serviço do SO (`makeAsServise` no Windows, Linux e macOS; o
  serviço se chama `AI2P`), em que o início como serviço difere do de console, como gerenciar o
  serviço, em nome de quem ele deve funcionar no Windows (DPAPI e as chaves das organizações) e
  o que acontece na atualização de versão.

**[Configuração](config.md)**

* Os dois lugares em que vivem as configurações e por que são dois; como abrir a tela de
  configurações e o que há em cada aba dela (principal, modelos, catálogos, ações, segurança,
  usuários, organizações, servidores, notificações, experiência geral); o que só é editado no
  `config.json` — o prefixo do endereço, a interface de escuta, o segundo endereço, o HTTPS — e
  três regras sobre segredos, diretórios de plataforma e atualização de versão.

**[HTTPS](https.md)**

* Como passar o servidor para https: de onde tirar o certificado (arquivo `.pfx`, par PEM ou o
  repositório de certificados do computador), como fazê-lo você mesmo no PowerShell e no
  openssl, o que o próprio programa verifica antes de salvar e no início, como **autorizar uma
  vez** o seu certificado no navegador (Windows, macOS, Linux, Firefox, celulares), como o
  cluster vive com um certificado de AC própria e o que fazer quando o servidor não sobe.

**[Executores](performers.md)**

* Pessoa e IA como um único conceito; em que a conta difere do executor; o perfil e a declaração
  de capacidades; atividade, ocupação, limite de janela e tempo de espera da resposta; como
  funciona a escolha automática e os executores reservas; por que um modelo local prende o
  executor a um servidor.

**[Equipes](teams.md)**

* O círculo de executores de uma tarefa, o idioma de comunicação dos agentes, a hierarquia e o
  líder, as duas atividades de participação, o início do trabalho da equipe e o início
  individual de um integrante, os servidores locais de modelos e como ler um erro de conexão.

**[Agenda](schedule.md)**

* Cópia de um modelo ou ação do sistema, os campos da agenda e os períodos, por que a agenda tem
  um servidor próprio, a verificação de sobreposição, as duas visualizações (tabela e
  calendário) e, o principal, a lista de disparos atrasados.

**[Vários servidores](servers.md)**

* Para que serve um cluster e o que ele não faz; o maestro e os códigos dos servidores; a
  propriedade das linhas; como conectar um segundo computador, retirá-lo do cluster e trocar o
  maestro; como funcionam a replicação do banco e a dos arquivos; a tela de diagnóstico, a fila
  de repetição e os conflitos.

**[Arquivamento](Archives.md)**

* A transferência de dados do ambiente de trabalho para o de arquivo: para que ela serve, os
  quatro estados do arquivo (atual, aberto, fechado, removido), como criar um arquivo e como
  abri-lo, fechá-lo, removê-lo e baixá-lo de volta de outro servidor, a transferência manual de
  uma tarefa, de um modelo, de um objeto, de um registro de experiência e de um projeto inteiro,
  as regras de arquivamento (as gerais e as do arquivo), o arquivamento automático por uma ação
  da agenda, a consulta ao arquivo pelo campo «ambiente» da barra superior e a restauração de
  dados a partir do arquivo atual.

**[Plugins e MCP](plugins.md)**

* Como se ligam os programas externos e os servidores MCP: de que se compõe o registro de um
  plugin (ações, experiência, programa, documento), em que um gateway difere de uma conexão MCP,
  os cinco estados do plugin e por que eles são próprios de cada servidor, a ordem da
  inicialização, o que significa «programa não encontrado neste servidor» e por que um MCP
  configurado por fora do AI2P passa ao lado de todas as regras de segurança.

### Manual do usuário

**[Projetos](progects.md)**

* A pasta do projeto e por que ela é por servidor, a pasta `Common` e o `.repignore`, as guias
  do cartão (principal, tarefas, equipe, objetos, segurança, modelos, experiência, histórico),
  as configurações «preço ↔ qualidade» e «tempo ↔ qualidade», os objetos do projeto e o link
  `@obj:`, os três níveis de experiência.

**[Objetos do projeto](objects.md)**

* Para que serve o objeto e por que o link `@obj:` é melhor que a paráfrase; os tipos de objeto;
  a lista e as suas quatro visualizações; a guia do objeto, a barra de ferramentas dela e a guia
  «Subobjetos»; o formulário do objeto, os dois links para ele e a janela «o que irá para o
  modelo» com dois campos no adaptador LoRA; o servidor dono do objeto, o modo somente leitura em
  um servidor alheio e a viagem do adaptador treinado até o vizinho.

**[Modelos de processo](templates.md)**

* O esboço de um processo de trabalho e o lugar em que vive a experiência; em que um modelo
  difere de uma tarefa; as guias «Experiência» e «Estatística», a seleção da experiência por
  habilidades e etiquetas; as três formas de implantar um modelo e o que acontece com as tarefas
  bloqueadoras na cópia.

**[Tarefas](tasks.md)**

* A descrição da tarefa como prompt; as quatro visualizações da lista, o filtro e a ordenação;
  as três seções do formulário; o cartão, o console do trabalho e os motivos da pausa; o chat,
  as perguntas do agente e a interrupção durante o trabalho; o início manual, o automático e o
  hierárquico; as subtarefas e a divisão automática; o trabalho com uma tarefa de servidor
  alheio.

**[Algoritmo de execução das tarefas](TaskDo.md)**

* A ordem da execução da hierarquia inteira: a passagem da fila de baixo para cima por
  prioridade, quando uma tarefa espera, é pulada ou é iniciada, quando a fila se fecha; como
  «Condição», «Ciclo (verificar antes)» e «Ciclo (verificar depois)» entram nessa ordem, o limite
  de voltas e a parada.

**[Experiência](experience.md)**

* A memória do sistema sobre como o trabalho deve ser feito: os três âmbitos (regras gerais da
  organização, experiência do projeto, experiência do nó de modelo) e a regra que os separa; o que
  tem um registo e como ele chega ao encargo — atividade, competência, «carregar sempre», as
  etiquetas como sinal, as quotas de nível e o limite de inserção; a aba «Experiência utilizada» e
  as estatísticas de uso; a busca na experiência e o que ela não encontra; a transferência de um
  registo entre âmbitos e a revisão da experiência geral; desativar um registo e a regra de
  arquivamento «todos os inativos»; os conjuntos de experiência (estilos de trabalho) e o modelo
  «Análise da experiência» no agendamento.

**[Editor de LoRA](LoRAEditor.md)**

* Como usar o editor de LoRA: o que é preciso ter antes do treinamento, o que escrever na
  descrição do adaptador e nas legendas dos quadros, qual desses textos vai para o treinamento e
  qual vai para o prompt da geração, como disparar e parar o treinamento, se a ordem dos quadros
  do dataset importa, os erros frequentes e o que o editor não faz. Esta mesma página é aberta
  pelo botão do livro dentro do próprio editor.

**[Trabalho com modelos de áudio](audio.md)**

* Como montar uma cena sonora com música, uma canção, a fala de pessoas específicas e ruídos:
  quais modelos de áudio o catálogo tem e o que cada um sabe fazer, por que uma tarefa é uma
  camada de som, quais amostras de voz a síntese de fala precisa e como passá-las, por que a voz
  do cantor é descrita em palavras, de onde tirar um efeito sonoro, como juntar e mixar as
  camadas, os erros frequentes.

### Exemplos e material de serviço

**[Vídeo a partir do quadro de referência de um personagem](sample_video1.md)**

* Um exemplo de ponta a ponta: como transformar uma imagem da pasta do projeto em vídeo e como
  manter o mesmo personagem em uma série de quadros — instalação do modelo local de mídia, o
  executor de IA e o início do trabalho da equipe, o objeto-personagem e o passaporte dele, o
  link `@obj:` na descrição do quadro, os parâmetros de geração, os erros frequentes.

### Manual do desenvolvedor

**[compilação, distribuição e estrutura do repositório](build.md)**

* Como compilar o sistema a partir do código-fonte.

**[Como a documentação do AI2P é organizada](aboutdoc.md)**

* Sobre a própria documentação: onde fica o diretório `doc/` e o que dele é distribuído, de que
  seções ele é composto, como os arquivos dos documentos são nomeados, como acrescentar um
  documento novo e como manter os idiomas em harmonia.

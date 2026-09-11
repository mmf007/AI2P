# Projetos

**O projeto é o contêiner de todo o trabalho:** tarefas, modelos de processo, objetos, regras de
segurança, experiência e — o mais importante — a **pasta em disco** na qual o agente de IA lê e
escreve arquivos.

A lista abre pelo botão **«Projetos»** na barra da esquerda. É também a primeira coisa que vê
quem ainda não escolheu um projeto. Um clique na linha abre a **aba «Projeto»** — o cartão com
suas guias; o projeto escolhido é memorizado e entra em tudo o que for novo.

---

## Para que ele serve

O projeto responde a perguntas que, de outro modo, teriam de ser repetidas em cada tarefa:

* **onde estão os arquivos** — a pasta do projeto; sem ela o agente não recebe ferramenta de
  arquivo nenhuma;
* **quem trabalha** — a equipe do projeto e, a partir dela, o círculo de executores da tarefa;
* **como escolher o executor** — o controle deslizante «preço ↔ qualidade»;
* **o que o agente já sabe** — a experiência do projeto, que entra em cada tarefa;
* **o que o agente não pode** — as regras de segurança do projeto.

## A pasta do projeto — por que ela é por servidor

O projeto inteiro é replicado entre os servidores do cluster, mas o **diretório é próprio de
cada computador**: em um é `D:\work\game`, em outro é `~/projects/game`. Por isso o caminho é
guardado em uma **linha separada para cada servidor** e só é editável a partir daquele servidor.
No formulário do projeto os diretórios dos demais servidores aparecem para leitura — dá para ver
onde o projeto já está implantado.

Da mesma regra decorre algo que às vezes surpreende: **a marca «ativo» também é por servidor**
(no formulário ela está escrita assim mesmo — «ativo neste servidor»), e um projeto que aqui não
tem diretório indicado não pode ser ativado aqui. Caso contrário as tarefas seriam iniciadas
«para lugar nenhum».

O caminho pode ser digitado a partir do diretório do usuário (`~/work/projeto`) — o «~» é
expandido tanto pelo diálogo de escolha de pasta quanto pela gravação; no banco ele já fica
expandido, porque quem o recebe são as ferramentas do agente, a caixa de areia do CLI e a
replicação de arquivos.

### A pasta `Common`

É um caminho **dentro** do diretório do projeto cujo conteúdo é replicado entre os servidores:
uma forma rápida de mandar resultados ao vizinho, sobretudo mídia. É configurada separadamente
em cada servidor; vazia significa «não replica».

Aqui o caminho é **apenas relativo** (`media`, `doc/common`): caminho completo, `~/…` e a saída
para cima `..` são rejeitados com um erro claro na gravação. Dentro funciona o filtro
**`.repignore`** — a sintaxe é a do `.gitignore` e ele é editado pelo botão de filtro ao lado do
campo.

### Nome e diretório de armazenamento

O nome do projeto é único (gravar com um nome já em uso é bloqueado) e é sugerido pelo caminho
da pasta. Já o diretório do **armazenamento de arquivos** do projeto recebe o código externo
dele (`projects/PRJ-3`), e renomear o projeto não o altera: os arquivos e os caminhos relativos
ficam onde estão.

---

## As guias do cartão

### Principal

O formulário do projeto: a pasta, `Common`, o nome, a equipe, «ativo» e os controles das
configurações. Abre **em modo de leitura** — a edição é ligada pelo lápis no canto superior
direito, e no lugar dele aparece «salvar».

As configurações que vivem aqui merecem ser entendidas:

| Configuração | O que faz |
|---|---|
| **preço ↔ qualidade** | 0.0 — a escolha automática pega os mais baratos, 1.0 — os melhores; por padrão 0.5 |
| **tempo ↔ qualidade** | o padrão do campo de mesmo nome da tarefa: 0.0 — «faça rápido, a qualidade pode ceder», 1.0 — «não economize tempo»; por padrão 0.5 |
| **limite de inserção de experiência** | quantos caracteres de experiência no máximo vão para a tarefa — somando as regras gerais, a experiência do projeto e a do nó de modelo; por padrão 100 000 |
| **rodadas de nova verificação** | quantas vezes a tarefa que executa as verificações pode devolver as vizinhas para ajuste e esperar por elas; por padrão 3 |
| **responsável padrão** | a pessoa da equipe que o formulário coloca em uma tarefa nova; pode ficar vazio |
| **formato do link de objeto** | o que os botões da interface põem na área de transferência: `@obj:OBJ-3` ou `@obj:[Herói Vasya]`. **As duas** formas são sempre reconhecidas |

Duas sutilezas sobre o «responsável padrão»: quem o insere é o **formulário**, e não o servidor —
por isso uma tarefa criada fora do formulário (subtarefa de agente, importação) não recebe
responsável; e trocar a equipe remove um responsável que não pertença à nova equipe.

### Tarefas

A mesma lista de tarefas (quadro / tabela / hierarquia, filtro, busca), filtrada por este
projeto. Uma tarefa nova criada daqui recebe **este** projeto e a equipe dele. A visualização, a
ordenação e o filtro são memorizados, inclusive entre execuções. Sobre a tarefa em si —
[Tarefas](tasks.md).

### Equipe

Os integrantes da equipe do projeto com as situações de trabalho e os botões
«iniciar»/«parar» — os mesmos da lista de equipes (veja [Equipes](teams.md)), só que aqui, à
mão. Duas visualizações: hierarquia (padrão) e tabela.

### Objetos

A lista de objetos **deste** projeto: personagens, locações, adereços, estilos, quadros de
referência, adaptadores LoRA. Não existe de propósito uma lista de objetos comum à organização —
o objeto pertence ao projeto. O objeto guarda um **perfil descritivo** — o texto literal para o
prompt — e na descrição da tarefa entra um **link `@obj:OBJ-3`**, que a cada início do trabalho é
expandido nesse perfil e nos caminhos dos arquivos de referência.

Todo o resto — os tipos de objeto, as quatro visualizações da lista, a guia do objeto e os
subobjetos dele, os dois links, «o que irá para o modelo» e o servidor dono — tem um capítulo
próprio: **[Objetos do projeto](objects.md)**.

### Segurança

As regras de segurança **deste projeto**: o que é permitido ao agente, o que exige confirmação
de uma pessoa e o que é proibido. As regras do projeto refinam as regras da organização
(Configurações → Segurança), e as regras da tarefa refinam as do projeto.

### Modelos de processo

A mesma lista de modelos que a geral, mas limitada a este projeto; o botão «novo modelo» já
preenche o projeto sozinho. Detalhes em [Modelos de processo](templates.md).

### Experiência

A **experiência do projeto** é a generalização do trabalho que **qualquer** tarefa dele recebe.
Uma tabela: habilidade, texto, por quem e quando foi criada e alterada. O filtro por habilidades
é múltiplo, e os registros **sem habilidade aparecem com qualquer filtro** — eles são gerais.

A habilidade de um registro significa literalmente «este registro só será lido por um executor
com esta habilidade», e é a principal ferramenta contra o inchaço do prompt: uma lição
específica não deve ir para todo mundo. Registros de experiência também são escritos pelo
próprio agente, quando ele descobre algo importante para tarefas futuras.

São três níveis de experiência, do geral ao específico: **regras gerais da organização**
(Configurações → Experiência geral) → **experiência do projeto** (aqui) → **experiência do nó de
modelo** ([Modelos de processo](templates.md)). Quando tudo junto não cabe no limite, a seleção
é feita pela pontaria: o nó de modelo é mais importante que o projeto, e o projeto é mais
importante que as regras gerais.

### Histórico

O mesmo diário de trabalhos da tela geral «Histórico de trabalhos», mas restrito a este
projeto: filtros por executor e por tipo de evento, um clique no código da tarefa abre o cartão
dela, e um clique na célula «Detalhes» mostra o evento inteiro com um botão «copiar». Aqui
também entram o início do trabalho da equipe e o resultado da conexão de cada integrante com o
texto do erro.

---

## Miudezas que economizam tempo

* **O projeto atual** é trocado na barra superior («Projeto: …»), e não pelo botão «escolher» da
  lista. É ele que determina para onde vai uma tarefa nova.
* Uma pessoa trabalha com vários projetos — por isso a lista de projetos continuou sendo apenas
  uma lista, e o trabalho acontece nas abas.
* **Um projeto inativo** não é inserido em tarefas novas.
* O título da aba tem duas linhas: o tipo («Projeto») e ou o começo do nome ou o código curto
  (`PRJ-2`) — isso se troca em Configurações → Principal.

## Depois

* [Tarefas](tasks.md) — aquilo para o que o projeto é criado.
* [Objetos do projeto](objects.md) — personagens, locações, estilos e adaptadores LoRA.
* [Equipes](teams.md) — quem trabalha no projeto.
* [Modelos de processo](templates.md) — como implantar um processo típico neste projeto.
* [Editor de LoRA](LoRAEditor.md) — treinar um adaptador a partir de um objeto do projeto.

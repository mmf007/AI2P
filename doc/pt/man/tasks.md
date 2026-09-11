# Tarefas

**A tarefa é a unidade de trabalho e, ao mesmo tempo, o pedido para a IA.** É o principal a se
entender sobre o AI2P: a mesma descrição que a pessoa lê com os olhos, o agente recebe como
prompt. Por isso a tarefa é escrita como seria escrita para um executor de carne e osso — e do
modo como ela é escrita depende o resultado.

A lista abre pelo botão **«Tarefas»** na barra da esquerda; a lista de tarefas do **projeto**
também existe como guia no cartão dele. Um clique na linha abre a **aba-cartão** da tarefa.

---

## O que a tarefa guarda

Além do óbvio (título, descrição, prazo, situação, prioridade), ela tem o que move o trabalho
sozinho:

* **executor** — um só, entre os integrantes da equipe da tarefa; mais a lista **«podem
  substituir»** para o caso de o designado estar ocupado (a ordem da lista = a ordem de
  preferência);
* **responsável** — apenas uma pessoa; é a ela que se dirigem as perguntas do agente e os
  pedidos de confirmação;
* **habilidades** — o que de fato precisa ser feito; é por elas que funciona a escolha
  automática;
* **critérios de aceitação** — como saber que está pronto; vão para o agente junto com a
  descrição;
* **tarefas bloqueadoras** — sem que elas se concluam a tarefa não é iniciada automaticamente;
* **etiquetas** — palavras livres, sem catálogo: a etiqueta nasce do fato de alguém a escrever e
  desaparece quando não resta em nenhuma tarefa;
* **situação ao concluir** — para que estado o agente que terminou **normalmente** vai levar a
  tarefa; por padrão «revisão»;
* **«tempo ↔ qualidade»** — 0.0 «faça rápido» … 1.0 «faça com cuidado»; é impresso na tarefa do
  agente, e o valor vazio vem das configurações do projeto **no momento do início**.

---

## Como olhar a lista

Quatro visualizações, trocadas na barra de ferramentas da lista; a escolhida é memorizada,
inclusive entre execuções.

* **Quadro** — colunas por estados do catálogo, o fundo do título é a cor do estado. O cartão
  mostra uma prévia da descrição. As colunas são **reordenadas arrastando o título**, e a ordem é
  memorizada por conta e projeto. Já **os cartões não são arrastados pelo quadro de jeito
  nenhum** — a situação é trocada no cartão da tarefa, no botão ao lado da situação.
* **Tabela** — ordenação por clique no título da coluna (um segundo clique inverte o sentido).
* **Hierarquia** — a árvore por subordinação. Aqui **funciona o arrastar** da linha com o mouse e
  com o dedo (à esquerda da linha há a alça de arraste) e existe «recolher/expandir tudo». Só o
  nível superior é ordenado: a ordem dos descendentes não muda.
* **Etiquetas** — agrupamento por etiquetas.

O **filtro** vem recolhido e é aberto pelo botão do funil. Se ele estiver definido, ao lado
aparece a descrição em texto dele e um botão «X» — limpar tudo de uma vez. A seleção por
etiquetas se soma por **OU** («mostre tudo sobre UI ou sobre compilação»): a interseção é quase
sempre vazia e pareceria um defeito.

A ordem das listas no servidor é **por prioridade numérica decrescente**. É nessa mesma ordem
que a fila pega as tarefas para trabalhar, por isso na lista se vê o que vai acontecer.

Uma tarefa com perguntas do agente não respondidas exibe em todas as visualizações uma marca
visível **«?»** com a quantidade.

---

## O formulário da tarefa

Três seções; as duas de baixo vêm recolhidas.

**Principal** — título, descrição, responsável, executor e «podem substituir».

**Avançado** — prazo e duração prevista, prioridade (nível e número na mesma linha, sincronizados),
critérios de aceitação, tarefas bloqueadoras, link de importação, a marca «não dividir em
subtarefas» e a **situação ao concluir** (visível apenas quando o executor é uma IA).

**Otimização** — o que controla o tamanho da tarefa e a seleção da experiência: habilidades,
etiquetas, o controle «tempo ↔ qualidade» e as duas marcas **«inserir a tarefa pai no prompt»** e
**«inserir as tarefas vizinhas no prompt»**. As duas vêm **desmarcadas** por padrão, e isso não é
economia de miudezas: os blocos do pai e dos vizinhos pesam até 60 000 caracteres. O agente não
fica sem contexto por causa disso — ele recebe uma linha com o código e o título do pai e o lê
por conta própria quando realmente precisa.

Duas coisas que vale saber sobre o formulário:

* **um clique fora da janela não a fecha** — o que foi digitado não se perde; fechar sem salvar
  só é possível pelo botão «Cancelar»;
* **as tarefas bloqueadoras** são procuradas pelo campo «Busca» ao lado: o servidor procura ao
  mesmo tempo no título e na descrição (a descrição é um arquivo, por isso quem procura é o
  servidor). Na lista não há tarefas concluídas nem canceladas, nem a própria tarefa em edição;
  uma bloqueadora já escolhida continua sendo um item sempre — caso contrário não haveria como
  removê-la.

### A descrição é o prompt

O campo da descrição (e o dos critérios de aceitação, e o do chat, e o da resposta na Caixa de
entrada) é um **editor Markdown único**: botões de formatação, prévia editável, colagem de
imagens da área de transferência, do disco e por endereço, inserção de vídeo e largura da
imagem.

Dois botões desse editor merecem menção à parte:

* **«@» — link para um objeto do projeto.** Abre a escolha de objeto (com prévia e busca por
  nome e número) e coloca na posição do cursor um link `@obj:`. A **cada** início do trabalho o
  link é expandido no passaporte do objeto e nos caminhos dos arquivos dele — por isso a
  aparência do personagem não precisa ser reescrita em cada quadro (veja
  [Projetos](progects.md)).
* **«Todos os arquivos»** (no cabeçalho do cartão) — a lista de todos os arquivos referenciados
  na descrição, nos critérios, no chat e nos resultados, mais os próprios arquivos de resultado:
  prévia, descrição, cópia do link, download e exclusão. Só é permitido excluir os arquivos
  próprios da tarefa — é a única forma de tirar gigabytes de resultados em vídeo.

---

## O cartão da tarefa

Um cabeçalho de duas linhas: código e título e, abaixo, a barra de ferramentas — marcas de
situação, de modelo, de servidor, «com quem está o trabalho», «ocupado até», o contador de
perguntas e, em seguida, os botões-ícone: iniciar/parar, alterar, divisão automática, excluir,
todos os arquivos, link da tarefa, atualizar e «ir para a tarefa pai».

Guias: **Descrição**, **Subtarefas**, **Chat**, **Resultado**, **Trabalhos**, **Histórico**.

Enquanto houver um trabalho ativo na tarefa, o cartão **se relê sozinho** a cada 2 segundos.

### Por trás da situação há um motivo

A situação «pausa» por si só não explica nada, por isso ao lado dela o sistema escreve o motivo
com as mesmas palavras usadas na visualização «em trabalho na IA»:

| Marca | O que aconteceu |
|---|---|
| **«perguntas no chat: N»** | o agente perguntou a uma pessoa ou ao agente de uma tarefa vizinha |
| **«Aguardando até <hora>»** | o limite do executor acabou e o início foi adiado — a tarefa vai iniciar sozinha |
| **«aguarda a execução das subtarefas»** | a tarefa se dividiu em subtarefas |
| **«início da hierarquia»** | há uma fila de início hierárquico aberta nesta tarefa |
| **«aguarda as tarefas bloqueadoras»** | nem todas as bloqueadoras estão em «pronto» |
| **«tarefa bloqueadora cancelada»** | o trabalho que a tarefa esperava não vai acontecer — quem decide é a pessoa |

### A guia «Trabalhos»

A lista de trabalhos (cada início do agente é um trabalho) e, abaixo dela, o **console do
trabalho selecionado**: a saída ao vivo, linha a linha, do que o executor está fazendo **agora
mesmo**. Nos modelos de mídia ali é despejado o console do ComfyUI com o progresso da geração;
nos de texto, o início, cada chamada de ferramenta e o resultado; nos modelos locais, também a
saída do próprio servidor do modelo. É justamente pelo console que se vê se o trabalho anda ou
travou.

O buffer é de 1000 linhas por trabalho e vive na memória: depois de reiniciar o aplicativo, o
console dos trabalhos concluídos não é restaurado. O histórico permanente está na guia
«Histórico» e nos resultados.

### A guia «Chat» — é ali que você conversa com o agente

O chat não é uma seção de comentários, é um canal de comunicação. Três coisas pelas quais ele
existe:

* **o agente pergunta.** A pergunta vem destacada por uma moldura e pelo ícone «?», e pode ter
  botões de opções de resposta e um campo de resposta livre. Enquanto não houver resposta, a
  tarefa fica em pausa e a pergunta aparece na Caixa de entrada. Respondeu, o trabalho continua
  **do mesmo ponto**.
* **você interrompe o agente.** Em uma tarefa em andamento, acima do campo de entrada há um
  aviso: a mensagem vai para o executor **em pleno trabalho** — ele interrompe, lê e responde
  ali mesmo. Não é preciso apertar mais nada.
* **a pergunta pode ser retirada.** Se o trabalho que fez a pergunta já não espera resposta
  (falhou, terminou, foi reiniciado), não há a quem responder — então, no lugar dos campos de
  resposta, fica o botão **«retirar a pergunta»**. A pergunta sai do contador, da Caixa de
  entrada e do motivo da pausa na hora, e na conversa fica com a legenda honesta «Pergunta
  retirada — não haverá resposta».

---

## Como as tarefas são iniciadas

**Manualmente** — o botão «iniciar» do cartão. Para uma IA isso é o início do agente; para uma
pessoa, um item na Caixa de entrada dela. Havendo um trabalho ativo, no lugar dele fica «parar».

**Automaticamente** — quando o pai se conclui (passa para «revisão», «ajuste» ou «pronto»),
iniciam os descendentes diretos com o tipo de início «automático»; quando a última bloqueadora
passa para **«pronto»**, as tarefas que a esperavam começam de imediato.

**Por agenda** — veja [Agenda](schedule.md).

**Pela hierarquia inteira** — o botão de três setas em uma tarefa com subtarefas. A fila anda **de
baixo para cima**, das mais profundas e prioritárias; executores ocupados esperam a vez deles,
subtarefas já executadas são puladas, e a própria tarefa é iniciada por último. Na pergunta de
confirmação há duas marcas independentes: **«iniciar também as tarefas que pararam com erro»** e
**«iniciar também as tarefas em ajuste»** — as duas vêm desmarcadas e não são memorizadas entre
os usos, porque é uma decisão do aqui e agora, e não uma configuração do cartão. Uma tentativa
por início: a tarefa que voltou a cair em ajuste não é mais pega pela fila e é deixada para a
pessoa.

Enquanto a fila estiver aberta, ao lado aparece o botão **«parar o início da hierarquia»**. E o
botão «parar» de uma tarefa **dentro** de uma fila aberta pergunta o que exatamente parar: tudo
junto com os descendentes ou apenas esta tarefa e a fila. Sem essa pergunta, o trabalho
encerrado seria levantado de volta pela própria fila na passagem seguinte.

**«Desligar o início automático das subtarefas»** — o botão fica no mesmo lugar. A marca vale
para a tarefa **e para toda a subárvore dela** e fecha os três inícios automáticos (dos
descendentes, dos que esperavam uma bloqueadora e da fila da divisão automática). O início
manual ela não limita, e «iniciar a hierarquia» **a remove** — a pessoa disse claramente «vai».
Se o início automático estiver desligado mais acima na árvore, o cartão exibe um ícone com a
dica de em qual tarefa ele foi desligado: calar isso não se pode, senão vira «apertei e nada
acontece».

### Quando o limite do executor acaba

Apertar «iniciar» primeiro consulta o saldo. Se ele for pequeno, abre a janela **«O limite do
executor está quase esgotado»** — quanto foi gasto, quando a janela se libera e quatro saídas:
**adiar o início** (a tarefa inicia sozinha, o computador pode ser desligado), **dividir em
subtarefas** (uma parte é executada com o saldo), **iniciar agora** à força, e cancelar.

Se o limite interromper um trabalho já em andamento, a tarefa não «para com erro»: ela vai para
«aguardando» com início adiado; quando chegar a hora, ela inicia como **um trabalho novo, do
começo** — o que o agente fez nos arquivos do projeto não se perde.

---

## Subtarefas

A guia «Subtarefas» traz os filhos desta tarefa: ID, título, situação, prioridade numérica,
executor e prazo. A situação é alterada direto na linha. O botão «+» abre a mesma escolha
«tarefa vazia ou modelo» de «nova tarefa»: a vazia é criada já com o pai preenchido, e o modelo
escolhido é **implantado como subtarefa** e migra inteiro para o projeto do pai.

**A divisão automática** é um botão-ícone à parte no cabeçalho (visível enquanto a marca «não
dividir» estiver desmarcada e não houver trabalho ativo). O agente divide o trabalho em
subtarefas sozinho, indicando para cada uma as habilidades e a prioridade numérica, e daí em
diante quem as conduz é o orquestrador: as subtarefas iniciam por prioridade decrescente, cada
executor de IA conduz **um trabalho por vez**, e a conclusão de qualquer subtarefa libera o
executor e inicia a seguinte.

A primeira chamada marca a tarefa como dividida, por isso ela não se divide de novo.

---

## Uma tarefa em servidor alheio

No cluster a tarefa tem um **servidor dono**: é ali que ela é editada e ali que os trabalhos dela
são executados. Nos demais servidores ela é visível para leitura, mas **«iniciar», «iniciar a
hierarquia» e o chat estão disponíveis**: o início vai para o dono como uma solicitação, e a
mensagem chega pela replicação — as dicas dizem isso claramente. Edição, mudança de situação,
exclusão, divisão e retirada de pergunta só no dono. Detalhes em
[Vários servidores](servers.md).

---

## Miudezas que economizam tempo

* **Uma tarefa vinda de um sistema externo** mostra no cabeçalho o link de importação, e o botão
  «atualizar» dela pergunta: reler **da fonte** (título, prazo, descrição e novas mensagens da
  discussão) ou apenas aqui.
* **«Trabalhando: <apelido>»** aparece ao lado do executor apenas quando quem trabalha não é o
  designado; cada substituição dessas também é registrada como mensagem no chat.
* **O link da tarefa** (botão do cabeçalho) abre uma janela com o endereço local e o externo e
  botões de cópia.
* **A lista de tarefas se relê sozinha** a qualquer alteração: uma tarefa criada em outro lugar
  da interface aparece no quadro sem precisar de «atualizar».
* **Não se pode mover uma tarefa entre projetos** — os arquivos dela ficam no diretório do seu
  projeto. O sistema dirá «primeiro troque o projeto da tarefa»; na árvore geral de «todas as
  tarefas» as linhas vizinhas costumam ser de projetos diferentes, e a tentativa de mover parece
  um gesto que não funciona.

## Depois

* [Projetos](progects.md) — a pasta, os objetos, a experiência e as configurações que afetam as
  tarefas.
* [Modelos de processo](templates.md) — para não digitar duas vezes a mesma árvore de tarefas.
* [Executores](performers.md) — quem executa a tarefa e por quanto.
* [Agenda](schedule.md) — o início pelo calendário.

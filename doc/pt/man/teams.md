# Equipes

**A equipe é o círculo de executores a partir do qual o executor de uma tarefa é escolhido.** Não
é um departamento nem um chat: é a lista que responde à pergunta «a quem, afinal, esse trabalho
pode ser confiado».

A lista abre pelo botão **«Equipes»** na barra da esquerda (e pelo item de menu de mesmo nome).

---

## Para que ela serve

Três coisas pelas quais a equipe existe:

1. **Limita a escolha.** A tarefa tem uma equipe (herdada do projeto), e o executor dela é
   escolhido **somente entre os integrantes dessa equipe** — tanto manualmente quanto pela
   escolha automática. Assim o trabalho não vai parar em quem não sabe nada sobre o projeto.
2. **Define o idioma dos agentes.** A equipe tem um **idioma de comunicação dos agentes**, e ele
   pode ser diferente do idioma da interface. O campo funciona ao pé da letra: no idioma da
   equipe o agente recebe **todo o texto dirigido a ele** — o prompt do sistema, a tarefa, as
   descrições das ferramentas, as respostas e até as recusas das regras de segurança. O que a
   pessoa lê continua no idioma da instalação.
3. **Mantém as conexões.** A equipe tem os botões «iniciar» e «parar» o trabalho: iniciar
   levanta os integrantes de IA (verifica as chaves e, se necessário, sobe os servidores locais
   dos modelos), e parar os desliga.

---

## A composição

Cada linha da composição é um executor mais quatro coisas:

| Campo | Para quê |
|---|---|
| **Papel profissional** | do catálogo de papéis (diretor de arte, programador, testador…). É o papel **nesta equipe**: a mesma pessoa pode ter papéis diferentes em equipes diferentes |
| **Chefe** | outro integrante desta mesma equipe; em branco significa o nível de cima |
| **Líder da equipe** | quem manda quando o nível de cima tem mais de um integrante |
| **Ativo nesta equipe** | uma atividade própria, separada da atividade do executor em geral |

### Hierarquia e líder

A hierarquia não é enfeite de organograma. Ela responde à pergunta **«a quem se pode confiar
tarefas sem uma autorização à parte»**: quem está acima confia tarefas aos seus subordinados por
conta própria. Isso é especialmente importante quando uma IA tem outras IAs subordinadas. Ciclos
são proibidos — o núcleo não os deixa passar e o formulário os destaca.

O líder é um só por equipe e só pode ser um integrante do nível de cima; se no topo houver um
único executor, ele é marcado como líder sozinho. Em um subordinado a marca fica indisponível.

### Estrutura organizacional: departamentos e cargos

No formulário, a composição da equipe é definida como uma **árvore de unidades
organizacionais**. Uma unidade é de um de dois tipos:

| Tipo | Executor | Exemplo |
|---|---|---|
| **Departamento** | o seu chefe (pode ficar vazio) | «Marketing» — chefe Mark_manager |
| **Cargo** | quem o ocupa; vazio significa **vago** | «Profissional de marketing 1» — Mark1, «SMM» — vago |

Uma unidade tem nome, unidade superior (o campo «Pertence a», com o qual a unidade também é
movida na hierarquia) e **habilidades**. As habilidades da unidade somam-se às do executor que
a ocupa quando o executor é escolhido automaticamente nesta equipe: se a declaração do
executor não tem essa habilidade, ela conta como dominada em 70 de 100.

O chefe de um membro não é mais escolhido à parte: vem da árvore, é o executor da unidade
superior mais próxima que tenha um. O líder técnico é o executor de uma unidade de nível
superior; as regras acima não mudaram. Um executor ocupa uma só unidade numa equipe.

As equipes criadas antes das unidades organizacionais aparecem como antes: cada membro é
mostrado como um cargo sem nome no seu lugar anterior da hierarquia. Essas unidades são
gravadas no banco de dados na primeira vez que a equipe é salva.

### Duas atividades, e isso não é duplicação

* **A atividade do executor** (no catálogo de executores) é «ele trabalha, em geral».
* **A atividade de participação** é «ele trabalha **nesta** equipe».

Um integrante só é considerado ativo quando as duas estão ligadas. Um integrante desligado na
equipe não se conecta quando ela é iniciada, não sobe pelo botão individual e **não é escolhido
pela escolha automática**, mas continua ativo em outras equipes. É assim que se desliga um
executor em um projeto sem mexer nos demais.

---

## Duas visualizações da lista

* **Tabela** — cada unidade organizacional (e, portanto, cada executor) em sua própria linha:
  nome, executor ou «vago», gestor, papel profissional, atividade. A equipe é uma linha de
  cabeçalho do grupo com os botões de iniciar, «+» e editar a equipe.
* **Hierarquia** — um bloco por equipe, com as unidades organizacionais em árvore por
  subordinação.

Uma unidade é editada sem abrir o formulário da equipe inteira:

* **«+»** na equipe adiciona uma unidade de nível superior; **«+»** num nó da hierarquia, uma
  subordinada a ele. Abre-se um formulário separado para uma unidade: tipo, nome, executor,
  unidade superior, habilidades, papel profissional, líder, atividade.
* **O menu «…»** da linha: **Editar** (o mesmo formulário), **Alternar atividade** (ativo ou não
  nesta equipe), **Excluir** (com confirmação; as unidades aninhadas sobem para o nível dela).
* **Arrastar na hierarquia** muda a unidade superior: uma linha sobre outra entra nela; sobre a
  faixa «Para o nível superior», no topo da lista, fica sem unidade superior. O mais cômodo é
  puxar pela alça à esquerda; no celular, com o dedo pela alça de imediato ou mantendo a linha
  pressionada. Uma unidade não pode ser movida para dentro da sua própria subordinada.

Cada uma dessas ações altera uma unidade, sem regravar a equipe inteira. Quem não pode editar
equipes (é preciso o papel de administrador de projetos) não vê os botões de edição.

A troca é pelo botão acima da lista; a visualização escolhida é memorizada.

---

## Iniciar o trabalho da equipe

Os botões «iniciar»/«parar» existem **para a equipe inteira** e **para cada integrante de IA
separadamente**. As pessoas não os têm: elas não se «conectam», e a situação delas é calculada
sozinha.

Cada integrante mostra uma situação colorida:

| Situação | O que significa |
|---|---|
| **não conectado** | o trabalho da equipe não foi iniciado ou o integrante foi parado |
| **conectando** | a verificação está em curso; em um modelo local isso leva **minutos** — os pesos são carregados do disco |
| **conectado** | a chave foi aceita e o modelo responde |
| **erros de conexão** | não responde; o texto do erro está na dica |
| **não ativo** | o integrante está desligado — no catálogo ou nesta equipe |

Enquanto alguém estiver conectando, as situações se atualizam sozinhas, até o fim da conexão.

**O texto do erro pode ser lido por inteiro e copiado**: o rótulo da situação com erro é
clicável e abre uma janela com o texto completo e um botão «copiar para a área de transferência».
Não é detalhe — no erro estão o código de saída do processo, as últimas linhas da saída do
modelo e o próprio comando de início, ou seja, tudo o que se usa para analisar aquele erro.

### Quando o início individual é necessário

Dois casos, e ambos frequentes: o integrante foi adicionado a uma equipe **que já estava
trabalhando** (não faz sentido reiniciar todo mundo) e o integrante **caiu por erro** (é preciso
levantar só ele). O início individual faz exatamente o mesmo que o geral, mas para um único
executor, e não mexe na marca «a equipe está trabalhando».

### Servidores locais de modelos

Se o modelo do integrante tiver um comando de início definido, na conexão o sistema **sobe
sozinho o servidor do modelo como processo do SO** e espera até que ele responda (uma sondagem a
cada 5 segundos, com limite de 10 minutos). Regras que vale conhecer:

* **um servidor que já responde é considerado externo**: uma segunda instância não é iniciada e,
  na parada, ele não é tocado — você pode tê-lo levantado à mão;
* **um comando de início é um processo** para todos os integrantes e todas as equipes; ele é
  desligado quando todos os que o usavam pararam o trabalho;
* ao encerrar o aplicativo, todos os servidores levantados por ele são descarregados.

Se o servidor do modelo morreu no início, entram no erro a decifração do código de saída, o
tempo de vida do processo e o final da saída dele; as causas típicas (driver NVIDIA antigo com
uma compilação CUDA do torch, falta de memória de vídeo) são acrescentadas por extenso.

---

## Miudezas que economizam tempo

* **O nome da equipe é único** — gravar com um nome já em uso é bloqueado.
* **Uma equipe inativa** não é inserida em projetos e tarefas novos, e o início dela fica
  indisponível.
* Um trabalho iniciado pelo botão **do cartão da tarefa** também se reflete nas situações da
  equipe: não é preciso reiniciar a equipe de propósito para ter a lista atualizada.
* A composição da equipe também é vista e editada na guia **«Equipe»** do cartão do projeto — no
  mesmo lugar em que você normalmente trabalha (veja [Projetos](progects.md)).

## Depois

* [Executores](performers.md) — de quem a composição é formada.
* [Projetos](progects.md) — a equipe é atribuída ao projeto, e do projeto ela chega às tarefas.
* [Tarefas](tasks.md) — como a equipe limita a escolha do executor.

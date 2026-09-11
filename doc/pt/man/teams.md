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

### Duas atividades, e isso não é duplicação

* **A atividade do executor** (no catálogo de executores) é «ele trabalha, em geral».
* **A atividade de participação** é «ele trabalha **nesta** equipe».

Um integrante só é considerado ativo quando as duas estão ligadas. Um integrante desligado na
equipe não se conecta quando ela é iniciada, não sobe pelo botão individual e **não é escolhido
pela escolha automática**, mas continua ativo em outras equipes. É assim que se desliga um
executor em um projeto sem mexer nos demais.

---

## Duas visualizações da lista

* **Tabela** — as equipes em linhas, com o líder marcado por uma estrela ao lado do apelido.
* **Hierarquia** — um bloco por equipe, com os integrantes em árvore por subordinação.

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

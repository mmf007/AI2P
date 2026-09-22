# Algoritmo de execução das tarefas

Este capítulo explica **em que ordem o AI2P executa as tarefas** quando você inicia uma hierarquia
inteira com o botão «Executar a hierarquia» (as três setas no cartão de uma tarefa com
subtarefas). Mostra também como as tarefas dos tipos «Condição», «Ciclo (verificar antes)» e
«Ciclo (verificar depois)» entram nessa ordem. Como configurar essas tarefas e o que o diagrama
mostra está no capítulo [Tarefas](tasks.md).

## O essencial em três linhas

* A fila anda **de baixo para cima**: primeiro as subtarefas mais profundas, depois os pais delas,
  **a raiz por último**. Entre irmãs vai primeiro a de maior prioridade numérica.
* Um pai só é iniciado quando **todas as suas subtarefas estão concluídas**.
* Condições e ciclos são **decididos pelo agente** a partir da descrição da tarefa e do chat. O
  AI2P não avalia condições sozinho: ele guarda o tipo da tarefa, executa a decisão do agente e
  conta as voltas do ciclo.

## O que significa «concluída»

Para a fila, uma tarefa está **concluída** se estiver no estado **«em revisão»**, **«concluída»**
ou **«cancelada»**. A fila pula essas tarefas e não as inicia de novo.

Para as tarefas **bloqueantes** a regra é mais rígida: uma bloqueante conta como concluída apenas
no estado **«concluída»** ou **«cancelada»**. Uma tarefa que espera por uma bloqueante em «em
revisão» continua esperando até que uma pessoa aceite o resultado.

## Como funciona uma passagem da fila

Executar a hierarquia marca a raiz como «fila aberta». Daí em diante o sistema faz
**passagens**: percorre a árvore inteira e inicia tudo o que pode ser iniciado agora. A passagem
se repete sozinha:

* quando qualquer tarefa desta hierarquia termina;
* quando surge uma nova subtarefa na hierarquia (por exemplo, criada pelo agente);
* quando uma tarefa bloqueante passa para «concluída» ou «cancelada»;
* uma vez por minuto, pelo vigia — caso algo tenha ficado livre sem evento (por exemplo, chegou
  a hora de um início adiado).

Em uma passagem, a partir da raiz, faz-se o mesmo com cada tarefa:

1. **Primeiro os filhos.** As subtarefas são percorridas por prioridade decrescente e, com
   prioridade igual, pela ordem de criação. Cada subtarefa é percorrida pela mesma regra, então a
   fila desce até as tarefas mais profundas. **Todos** os ramos são percorridos: os trabalhos são
   distribuídos a executores diferentes ao mesmo tempo, e não ramo a ramo.
2. **Depois a própria tarefa.** Se já está concluída, é pulada. Se nem todas as suas subtarefas
   estão concluídas, ela espera. Caso contrário, a fila tenta iniciá-la.

Tarefas-modelo e tarefas excluídas a fila não enxerga de forma alguma.

### Quando uma tarefa não é iniciada

Antes de iniciar, a fila verifica a tarefa. A tarefa **espera** (a fila continua aberta e volta a
ela na próxima passagem) se:

* ela já está em execução, aguarda resposta a uma pergunta ou está pausada;
* ela **parou com erro** ou foi para **«requer correção»**, e ao iniciar não foi marcado
  «Executar também as tarefas que pararam com erro» / «Executar também as tarefas no estado
  «requer correção»». A marca dá a essa tarefa **uma** nova tentativa por clique no botão;
* nem todas as suas tarefas **bloqueantes** estão concluídas;
* ela tem um **início adiado** (por exemplo, o executor esgotou o limite);
* o executor está **ocupado** com outro trabalho e não há um substituto livre na lista «Podem
  substituir o executor». Um executor de IA conduz um trabalho por vez;
* a tarefa pertence a **outro servidor**: para lá vai uma solicitação de início, e a fila espera
  o resultado chegar por replicação.

Uma tarefa **é pulada** se não tiver exatamente um executor atribuído, ou se o executor não for
encontrado ou estiver desativado. Essa tarefa nunca vai andar sozinha, e o pai dela vai esperar:
atribua um executor, e a próxima passagem a pega.

### Quando a fila se fecha

* **Nada a iniciar e nada a esperar** — a árvore inteira está concluída. É o fim normal.
* **A raiz foi entregue** («em revisão», «concluída», «cancelada») — a fila levava ao início da
  raiz, e ele aconteceu. O que quer que reste na subárvore, a fila se fecha. Se devolver a raiz ao
  trabalho, abra a fila de novo pelo botão.
* **Uma parada** — ordenada por uma tarefa condição ou ciclo (veja abaixo), pelo agente com a ação
  `stop_hierarchy` ou por uma pessoa com o botão «parar a execução da hierarquia».

## Exemplo: uma árvore linear

```
T-1 Raiz
├── T-2 Análise          prioridade 20
│   ├── T-4 Coletar dados prioridade 10
│   └── T-5 Revisão      prioridade 15
└── T-3 Rascunho         prioridade 10
```

Ordem: T-5 e T-4 (T-5 tem prioridade maior, por isso é pega primeiro; se os executores forem
diferentes, as duas começam juntas) → T-3 (o ramo dela é percorrido na mesma passagem, então, com
um executor livre, ela começa junto com T-5) → T-2, quando T-4 e T-5 estão concluídas → T-1,
quando T-2 e T-3 estão concluídas.

## Tarefa «Condição»

Quando a fila chega a uma condição, ela **inicia a própria tarefa de imediato**, sem esperar os
filhos, e ainda não entra neles.

1. A partir da descrição e do chat, o agente decide **«Sim»** (`true`) ou **«Não»** (`false`) e
   informa a decisão com a ação `set_condition_result` (um agente CLI usa `ai2p condition` ou o
   marcador `AI2P_CONDITION`). Não há terceira resposta.
2. Quando o trabalho da condição termina, a fila lê a decisão:
   * o **ramo não escolhido** — a subtarefa indicada para a outra resposta — é cancelado junto com
     toda a sua subárvore (tarefas já concluídas não são tocadas);
   * o **ramo escolhido** tem tarefa — ela é executada na ordem normal;
   * o ramo escolhido não tem tarefa, mas está marcado **«Criar tarefas»** — o agente devia criar
     as subtarefas antes de entregar (`create_task` ou `create_tasks_from_template`), e elas são
     executadas na ordem normal;
   * o ramo escolhido não tem tarefa e está marcado **«Encerrar a execução da hierarquia»** — a
     fila para.
3. Os **demais filhos** da condição (não indicados em nenhum ramo) são executados na ordem normal.
   Depois da condição a fila segue pela árvore como de costume.

Se o agente **não informou a decisão**, a fila **para**: o sistema não escolhe o ramo pelo agente —
quem decide é uma pessoa. Se a condição voltar a «pendente» ou «rascunho», a decisão anterior é
esquecida e a condição é avaliada de novo.

## Tarefa «Ciclo (verificar antes)»

A condição é verificada **antes** de cada volta de subtarefas.

1. A fila **inicia a própria tarefa de imediato**, como uma condição — é a tarefa analisadora. O
   agente verifica as condições do ciclo descritas na tarefa e informa o resultado com a ação
   `set_loop_result` (`ai2p loop`, o marcador `AI2P_LOOP`): `true` — é preciso uma volta, `false`
   — saída.
2. **`true`** — a tarefa fica pausada com o motivo **«Aguarda o fim do ciclo»**, e as subtarefas
   são executadas na ordem normal. Enquanto isso, o executor da analisadora está livre e pode
   pegar subtarefas.
3. Quando **todas** as subtarefas estão concluídas, a volta é contada. A analisadora é
   **reiniciada** e verifica as condições de novo. Numa volta nova, toda a subárvore volta a
   «pendente» e é executada outra vez.
4. **`false`** — o ciclo termina, a analisadora fica «concluída», a fila segue. Se a condição não
   for atendida **já na primeira volta**, as subtarefas em «pendente» e «rascunho» (com todos os
   descendentes) são canceladas.

## Tarefa «Ciclo (verificar depois)»

A condição é verificada **depois** de cada volta de subtarefas.

1. A fila a trata como um pai comum: **primeiro as subtarefas**, a analisadora espera.
2. Quando todas as subtarefas estão concluídas, a **analisadora** é iniciada e o agente informa o
   resultado (`set_loop_result`).
3. **`true`** — toda a subárvore volta a «pendente», a tarefa fica pausada com «Aguarda o fim do
   ciclo», e as subtarefas fazem uma volta nova. Depois da volta, a analisadora é reiniciada.
4. **`false`** — o ciclo termina, a tarefa fica «concluída», a fila segue.

## Limite de voltas

Os dois ciclos têm um **limite do ciclo** — um campo da tarefa; se estiver vazio, vale a
configuração do projeto «Rodadas de reverificação». Quando o número de voltas chega ao limite, o
ciclo termina como se a resposta fosse `false`, e a tarefa fica «concluída». Se a tarefa tiver
marcado **«Parar a execução de toda a hierarquia ao exceder o limite»**, a fila inteira também
para. Uma tarefa **linear** não tem ciclo: mesmo que a descrição traga uma condição de repetição, a
fila a executa uma única vez.

Se a analisadora de um ciclo **não informou o resultado**, a fila para, como no caso da condição.

Numa volta nova, as condições e os ciclos aninhados **esquecem** as decisões anteriores: a cada
volta elas são tomadas de novo.

## Parar e retomar

* Quando uma condição ou um ciclo para a fila, **nenhuma tarefa nova é iniciada**, e os trabalhos
  já em andamento terminam. No diagrama essa tarefa recebe o sinal **STOP**.
* O botão **«parar a execução da hierarquia»** fecha a fila; **«parar»** em uma tarefa dentro da
  fila pergunta se deve fechar a fila também.
* **Para continuar**, clique de novo em «Executar a hierarquia». As tarefas concluídas são puladas,
  e a fila continua de onde parou.

## O que se vê na tela

* A raiz de uma fila aberta mostra no cartão o motivo **«execução da hierarquia»** e, ao lado, o
  botão que para a fila.
* Uma tarefa ciclo, durante uma volta, fica pausada com o motivo **«Aguarda o fim do ciclo»**.
* No **diagrama** de subtarefas a disposição segue a ordem da fila, e as transições das condições
  e a contagem de voltas dos ciclos aparecem pela cor das setas e pelo oval «feitas / limite» —
  detalhes no capítulo [Tarefas](tasks.md).

## A seguir

* [Tarefas](tasks.md) — o formulário da tarefa, o campo «Tipo de tarefa», o início e o diagrama.
* [Modelos de processo](templates.md) — como manter pronta uma árvore com condições e ciclos.
* [Executores](performers.md) — quem executa as tarefas e quando um executor está ocupado.

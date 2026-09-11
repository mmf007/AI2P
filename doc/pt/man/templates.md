# Modelos de processo

**Um modelo é o esboço de um processo de trabalho: uma árvore de tarefas com descrições,
habilidades, ordem e a experiência acumulada.** Com uma única ação, dele se implanta um novo
conjunto de tarefas reais.

A lista abre pelo botão **«Modelos»** na barra da esquerda; a lista de modelos **deste projeto**
também existe como guia no cartão do projeto.

---

## Para que servem

Para o mesmo que servem as listas de conferência: um trabalho típico se repete, e formulá-lo de
novo a cada vez é demorado e pior. «Adicionar um personagem ao jogo», «lançar uma versão»,
«gravar um vídeo» são árvores de uma dezena de tarefas com descrições e critérios de aceitação
já escritos.

Mas o modelo do AI2P tem um segundo papel, menos evidente, e ele é mais importante que o
primeiro. Cada nó de modelo tem a sua **experiência**: as lições obtidas nas execuções
anteriores desse processo entram na tarefa criada a partir desse nó. Ou seja, o modelo **fica
mais inteligente com o uso** — de um «formulário para preencher» ele passa a ser o lugar onde
vive o conhecimento de como fazer aquele trabalho direito.

---

## O que é um modelo, tecnicamente

Não existe no sistema uma entidade «modelo» separada. Um modelo é **a mesma tarefa** com a marca
«modelo»; as tarefas filhas dele também são marcadas como modelo. Disso decorre todo o resto:

* o modelo **não é executado**: não vai para o quadro, nem para a fila de trabalhos, nem para a
  escolha de executores;
* o modelo pode ter **em branco** o projeto, a equipe, os executores e o responsável;
* ele tem os mesmos campos de uma tarefa — inclusive habilidades, prioridade, critérios de
  aceitação, tarefas bloqueadoras, etiquetas, «tempo ↔ qualidade» e «situação ao concluir»; tudo
  isso é **copiado** para a tarefa criada.

**Um modelo com projeto pertence ao projeto; um modelo sem projeto é geral.** As listas de
escolha (formulário de nova tarefa, formulário de agenda) mostram os modelos do próprio projeto
**e** os gerais; os alheios não entram nelas.

---

## O cartão do modelo

É o cartão de uma tarefa com quatro diferenças:

| Diferença | Por quê |
|---|---|
| não há a guia **«Chat»** | um modelo não tem conversa |
| não há a guia **«Resultado»** | nem resultados |
| em vez do botão «Iniciar» há **«Criar tarefa»** | o modelo não é executado, ele é implantado |
| foram acrescentadas as guias **«Experiência»** e **«Estatística»** | é justamente por isso que o modelo vive muito tempo |

### A guia «Experiência»

Os registros de experiência do nó e de **toda a subárvore dele**: código do nó, habilidade,
etiquetas, texto, por quem e quando foram criados e alterados. É editável à mão (adicionar /
alterar / excluir), e o próprio agente também escreve aqui.

O que vale entender sobre a seleção — caso contrário a experiência ou não chega, ou incha o
prompt:

* a **habilidade** do registro significa «este registro só será lido por um executor com esta
  habilidade»; os registros **sem habilidade** são gerais e aparecem com qualquer filtro;
* as **etiquetas** são um segundo filtro, independente (regra «ou», como nas tarefas);
* a marca **«carregar sempre»** leva o registro para a tarefa por fora da seleção; na lista esse
  registro é marcado com a etiqueta **«sempre»** em primeiro lugar na coluna de etiquetas;
* tudo junto é limitado pela configuração do projeto **«limite de inserção de experiência»**;
  quando o limite não basta, primeiro entram os marcados com «sempre» e depois os mais
  pontuais — **a experiência do nó de modelo é mais importante que a do projeto, e esta é mais
  importante que as regras gerais da organização**.

### A guia «Estatística»

As mudanças de estado das tarefas **criadas a partir deste modelo**: tarefa, estado, data e
hora, executor. Um clique na linha abre a tarefa. É a forma de ver onde o processo costuma
tropeçar.

---

## Como implantar um modelo

De duas formas.

**Pelo cartão do modelo** — o botão **«Criar tarefa»**. É copiado o modelo inteiro junto com a
hierarquia de tarefas filhas, e a marca «modelo» é retirada das cópias.

**Pelo formulário de nova tarefa** — o botão «adicionar» da lista de tarefas pergunta primeiro:
**tarefa vazia** ou **a partir de um modelo**.

Antes de copiar, o sistema pergunta:

* a **data base** — se a cabeça do modelo tiver prazo definido; os prazos de todas as tarefas do
  novo processo serão deslocados a partir dela;
* a **escolha automática de executores** — a mesma opção do formulário de nova tarefa.

O que acontece com os vínculos na cópia: as **tarefas bloqueadoras** do modelo apontam para nós
do modelo, e na implantação as referências são trocadas pelas tarefas criadas a partir desses
nós (a referência a um nó que não é copiado é descartada). O **responsável padrão** do projeto é
colocado apenas nos nós em que o responsável não estiver definido — o que está escrito no modelo
é mais forte.

A terceira forma de implantar é a **agenda**: o disparo da agenda cria a cópia do modelo
sozinho, pelo calendário (veja [Agenda](schedule.md)).

---

## O agente também edita modelos

O agente de IA tem as ações `list_templates`, `create_template` e `update_template` para os nós
de modelo **do seu projeto**: ver a lista, criar um nó novo (título, descrição, critérios,
habilidades, pai) e corrigir um existente pelo código (o campo não informado não muda).

Duas regras que vale conhecer:

* a gravação acontece **no servidor dono do nó de modelo**; de um servidor alheio virá uma
  recusa clara;
* as ações são fechadas por regras de segurança, como quaisquer outras (`AI2P.Templates.List`,
  `AI2P.Templates.Create`, `AI2P.Templates.Update`).

Um nó de modelo também é lido pelas ferramentas comuns de leitura de tarefas — no cartão ele vem
marcado como «NÓ DE MODELO».

---

## Miudezas que economizam tempo

* **Duas visualizações da lista** — tabela e hierarquia, com as mesmas ordenações e buscas da
  lista de tarefas.
* No **explorador** os modelos aparecem por dois caminhos: dentro do projeto (o ramo «Modelos»
  do projeto) e no ramo raiz «Modelos» → «Projetos», onde os modelos gerais ficam direto no
  ramo.
* É cômodo fazer um modelo **a partir de uma tarefa bem-sucedida**: copiar a descrição e os
  critérios dela para um nó novo enquanto ainda se lembra do que funcionou, e anotar a lição na
  experiência do nó.

## Depois

* [Tarefas](tasks.md) — o que sai de um modelo.
* [Projetos](progects.md) — a experiência do projeto e o limite de inserção dela.
* [Agenda](schedule.md) — implantar um modelo pelo calendário.

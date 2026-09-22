# Análise da experiência

**Código do conjunto:** `style.experience-analysis` · **Registos:** 1 · **Nós de modelo:** 3

## O que é este conjunto

Isto **não é um estilo de trabalho**, mas um **processo de trabalho**: o conjunto traz consigo uma
árvore pronta de nós de modelo de tarefas para a revisão periódica da experiência acumulada. Tem um
único registo — a regra geral «a experiência não se apaga, desliga-se»; tudo o resto vive nos
encargos dos nós.

## Para que serve

A experiência acumula-se mais depressa do que envelhece. Ao fim de meio ano de trabalho a lista
guarda centenas de registos: uns duplicam-se, outros estão obsoletos, outros foram parar ao âmbito
errado. Tudo isso rouba espaço ao que é necessário — o limite de experiência de um encargo é um só
para todos, e um registo a mais expulsa um útil.

Ninguém vai arrumar isso à mão. Por isso a revisão é colocada como **uma tarefa para um agente de
IA**, por agendamento: uma vez por semana ou por mês.

## O que há dentro

| Nó | Âmbito | Para quê |
|---|---|---|
| **Análise da experiência** | raiz | a instrução completa: o que ler, o que fazer, o que não fazer, o que escrever no relatório |
| **Revisão da experiência geral da organização** | `general` | revisão das regras gerais de trabalho |
| **Revisão da experiência do projeto** | `project` | revisão da experiência de um projeto; copia-se por cada projeto |

Os filhos recebem o texto do pai por inteiro: a ordem comum de trabalho está escrita uma só vez na
raiz, e o filho apenas lhe acrescenta o seu âmbito.

**O que o encargo manda o executor fazer:** ler as estatísticas de uso (`experience_usage`),
percorrer os registos por páginas (`list_experience`), depois **fundir** os duplicados num registo
resumido, **dividir** os registos que misturam duas lições, **etiquetá-los** por tema e
competência, **transferi-los** para o seu âmbito (`move_experience`) e **desativar** o obsoleto
(`set_experience_active`).

**O que é proibido:** apagar registos, mexer em registos de outro servidor, desativar as regras
fornecidas com o distribuível e reescrever o sentido ao fundir.

## A regra de desativação por estatística

Está escrita **por palavras** no encargo do nó raiz, não fixada no código:

> Um registo é desativado se não chegou a nenhum encargo durante três meses **tendo** havido
> tarefas do seu tema nesse período (o tema são as etiquetas do registo). «Não chegou porque não
> houve tais tarefas» — não desativar. Para um registo sem estatística alguma, o prazo conta-se
> desde a data de criação.

É a linha mais importante do conjunto e a primeira que vale a pena ajustar: o prazo, a definição de
tema e a própria condição editam-se diretamente no modelo, sem publicar uma nova versão do
programa.

## Como usar

1. **Configurações → Conjuntos de experiência → «Análise da experiência» → «Instalar»**, âmbito
   **projeto** ou **nó de modelo**. O âmbito «regras gerais da organização» não tem projeto nenhum,
   e um modelo de tarefas sem projeto não vive no AI2P: então os nós não são criados (o registo
   instala-se como sempre).
2. **Modelos do projeto**: apareceu o nó raiz «Análise da experiência» com dois filhos. Copie o
   filho «Revisão da experiência do projeto» tantas vezes quantos projetos tiver e indique a cada
   cópia o seu projeto.
3. **Configurações → Agendamentos**: crie um agendamento periódico (semanal ou mensal) e escolha
   como modelo o nó **«Análise da experiência»**. A instalação do conjunto não cria agendamento de
   propósito: executar tarefas gasta dinheiro no modelo.

Para o agendamento só serve o nó **raiz**: os filhos chegam copiados com ele.

## O que vale a pena ajustar

* **O prazo de três meses** é um número a olho. Se publica uma vez por trimestre, em três meses
  pode não haver nenhuma tarefa de um dado tema; tome meio ano.
* **A periodicidade.** Semanal só faz sentido com muito fluxo de tarefas; num projeto tranquilo
  basta uma revisão mensal, e a semanal queimará dinheiro em vão.
* **O relatório.** Os critérios de aceitação exigem os números «havia / ficaram ativos» e os
  identificadores dos registos; sem eles não há como verificar o trabalho. Se tiver requisitos de
  relatório próprios, ponha-os nos critérios de aceitação do nó, não na descrição.
* **O âmbito de instalação.** O único registo do conjunto cabe nas regras gerais; os nós de modelo
  vivem num projeto. Por isso o habitual é instalar o conjunto no âmbito «projeto» do projeto onde
  mantém as suas tarefas de serviço.
* **A limpeza depois da desativação.** A revisão não arquiva nada por si. Crie uma regra de
  arquivamento «experiência + apenas inativos + a idade não importa»; caso contrário os registos
  desativados ficarão nas listas.

## Onde ler a seguir

* O conteúdo da secção `packs/` — o que é um conjunto em geral.
* O capítulo do manual **«Experiência»** (`man/experience.md`) — os âmbitos, a seleção para o
  encargo, a busca, a transferência de um registo, a atividade e o arquivamento.

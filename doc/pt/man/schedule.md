# Agenda

**A agenda é o calendário pelo qual o sistema cria trabalho sozinho.**
Por exemplo:
* uma vez por semana, implantar o modelo «relatório semanal»;
* uma vez por mês, «fechar o mês»; uma vez por trimestre, «revisão trimestral»;
* uma vez por noite, levar as tarefas antigas para o arquivo.

Abre pelo botão do calendário na barra da esquerda (e pelo item de menu de mesmo nome).

---

## O que ela é capaz de disparar

Dois tipos diferentes de disparo, e isso se vê direto no formulário:

1. **Uma cópia de um modelo de tarefas.** O disparo implanta o modelo exatamente como o botão
   «Criar tarefa» do cartão dele: com toda a hierarquia de subtarefas, com a escolha de
   executores e com a data base.
2. **Uma ação do sistema.** Nesse caso nenhuma tarefa é criada e nenhum executor é escolhido,
   por isso os campos de modelo, projeto, deslocamento e regra de escolha somem por completo do
   formulário. Hoje há uma única ação — o **arquivamento automático** (veja
   [Arquivamento](Archives.md)).

---

## Os campos da agenda

| Campo | Para quê |
|---|---|
| **Projeto** | por padrão, o atual; limita a escolha do modelo e aparece como coluna. Pode ficar vazio |
| **Modelo** | de nível superior; o formulário tem busca por nome. São vistos os modelos do projeto escolhido **e** os modelos sem projeto |
| **Deslocamento da data base** | horas e minutos; a data base das tarefas criadas = o momento do disparo + o deslocamento |
| **Regra de busca de executores** | «primeiro a IA, depois a pessoa», «primeiro a pessoa, depois a IA» ou sem escolha automática |
| **Uma vez / periodicamente** | no de uma vez, a data e a hora do início |
| **Período** | semanal (dias da semana em marcas + horário, como o despertador do celular), mensal (dia do mês + horário), trimestral (mês do trimestre 1–3 + dia + horário, com trimestres a partir de janeiro) |
| **Duração prevista, min** | para a verificação de sobreposição |
| **Servidor** | é nele que a agenda é editada e é nele que ela dispara |
| **Ativa** | uma agenda desligada não dispara |

Os horários dos períodos são o **horário local do computador**. Em um mês curto o dia é
comprimido até o último: «dia 31» em fevereiro dispara no dia 28 (ou 29).

### O servidor não é formalidade

A agenda é replicada entre os servidores do cluster e, **sem o campo «servidor», uma mesma
agenda dispararia em todos os servidores de uma vez**. Por isso ela tem um dono; se ele não for
indicado, quem conduz a agenda é o maestro da organização. Na lista de agendas há um filtro por
servidores, e **por padrão ele mostra apenas o atual** — do contrário o panorama pareceria duas
vezes mais cheio do que é.

### A verificação de sobreposição

Se no modelo (em qualquer nó dele) houver um executor indicado, na gravação o sistema verifica
se ele não está ocupado por outra agenda ativa no mesmo horário: são comparadas as janelas de
ocupação (disparo + deslocamento, com a duração prevista como comprimento) dos 62 dias
seguintes. Uma sobreposição é um **erro de gravação**, com a indicação do executor, da agenda e
do horário, e não uma sobreposição silenciosa.

---

## Duas visualizações

São trocadas na barra de ferramentas, e a escolha é memorizada entre execuções.

* **Tabela** — ID, projeto, modelo (ou o nome da ação), «quando» (o resumo do tipo e do
  período), deslocamento, duração, regra de escolha e «ativa». Ordenação por clique em qualquer
  coluna.
* **Calendário** — a grade mensal, com navegação. Na célula do dia ficam as marcas dos disparos:
  primeiro o horário e depois o código do modelo ou o começo do título dele (qual dos dois é
  definido pela configuração «célula do calendário de agendas» em Configurações → Principal). A
  dica da marca é «Projeto — Nome do modelo», e o clique abre o formulário.

---

## Disparos atrasados

A principal particularidade da agenda no AI2P, e vale entendê-la de imediato: **o AI2P é um
programa no seu computador, e não um serviço na nuvem.** Enquanto o computador estiver
desligado, não há o que disparar.

Por isso, ao iniciar o aplicativo, o sistema reúne tudo o que foi perdido em uma lista de
**disparos atrasados** — ela é mostrada **no alto** da aba: agenda, modelo, quantos foram
perdidos e o último horário. Sobre cada um a pessoa decide: **«iniciar agora»** ou **«remover da
lista»**.

Duas consequências que, de outro modo, geram confusão:

* os disparos que ocorrem **durante o funcionamento** do aplicativo são iniciados
  automaticamente (verificação a cada minuto) — não há nada a decidir;
* **enquanto houver atrasados pendentes em uma agenda, os disparos novos esperam a decisão da
  pessoa**. Se a agenda «emudeceu», olhe primeiro o alto desta aba.

Dá para manter o AI2P sempre em execução — para isso ele é transformado em [serviço do sistema
operacional](service.md); nesse caso normalmente não há atrasados de forma alguma.

---

## Miudezas que economizam tempo

* O evento do disparo é registrado no diário (`schedule.triggered`) — no «Histórico de
  trabalhos» se vê o que foi implantado e quando.
* As tarefas bloqueadoras dos nós copiados funcionam como sempre: o início espera a conclusão
  delas.
* Uma agenda com o projeto vazio enxerga todos os modelos sem projeto — assim é cômodo criar
  processos regulares gerais, não presos a um único projeto.

## Depois

* [Modelos de processo](templates.md) — aquilo que a agenda implanta.
* [Arquivamento](Archives.md) — a única ação do sistema por agenda existente hoje.
* [Executar o AI2P como serviço do sistema operacional](service.md) — para que dispare sem você.

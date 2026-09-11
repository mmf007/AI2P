# Executores

**O executor é quem faz a tarefa.** Pessoa ou IA: para o sistema é o mesmo conceito, com o mesmo
conjunto de campos, e a tarefa não distingue a quem ela foi confiada. É exatamente por isso que
o trabalho pode passar da IA para uma pessoa e de volta sem reescrever nada.

A lista abre pelo botão **«Executores»** na barra da esquerda (e pelo item de menu de mesmo
nome).

---

## Para que ele serve

O executor responde a três perguntas do sistema:

1. **A quem confiar.** A tarefa tem exatamente um executor, e a escolha é limitada aos
   integrantes da equipe dela (veja [Equipes](teams.md)).
2. **Quem sabe fazer isso.** O executor tem uma **declaração de capacidades**: quais habilidades
   ele domina e quão bem. É por ela que funciona a escolha automática.
3. **Com o quê e por quanto ele trabalha.** Na IA: o modelo, a chave, os parâmetros, o preço por
   milhão de tokens, os limites e o tempo de espera. Na pessoa: e-mail, telefone e ocupação.

Conta e executor são **coisas diferentes**. A conta (Configurações → Usuários) serve para
entrar; o executor serve para trabalhar. O executor-pessoa tem o campo «conta», que liga os
dois; um executor sem conta não entra no sistema, mas pode ter tarefas em seu nome — é assim que
se cadastra um prestador que não trabalha dentro do AI2P.

---

## O que há na lista

A lista mostra aquilo pelo que o executor é escolhido, e não tudo indiscriminadamente:

| Coluna | O que significa |
|---|---|
| **Apelido** | o nome externo, único em toda a organização; é com ele que o executor assina em toda parte |
| **Tipo** | pessoa, IA ou «software automático» (um programa) |
| **Ativo** | um executor inativo não entra em equipes e tarefas novas; os vínculos antigos permanecem |
| **Ocupado até** | até que momento o executor está ocupado (um horário já passado não é exibido) |
| **% do limite** | o gasto atual do limite do provedor — se o provedor o informar |
| **Limite da janela** | a contagem própria: `gasto/limite` na janela deslizante e quando ela se libera |

A ordenação é por clique no título da coluna (um segundo clique inverte o sentido). Executores
sem limites vão para o fim quando se ordena por «limite da janela».

---

## O formulário do executor

O formulário é mais largo do que um diálogo comum e muda conforme o tipo. Não vale a pena
percorrê-lo campo a campo — importam quatro pontos em que é fácil errar.

### Nome interno

* **Na IA isso não é um texto, e sim uma escolha no catálogo de modelos** — e só entre os
  registros **ativos**. Junto com o modelo entram, de forma sincronizada, o perfil (parâmetros
  de conexão) e a declaração de capacidades: no executor eles são «os mesmos do modelo».
  Escolheu outro modelo, os dois arquivos mudaram.
* **Na pessoa** é o nome completo, e a unicidade dele é acompanhada.

Daí uma regra que economiza tempo: **se o modelo desejado não está na lista, é porque ele está
inativo.** Um modelo em nuvem sem chave de API e um local sem os arquivos baixados não podem
estar ativos. O caminho é ir em Configurações → Modelos, e não procurar o erro no formulário do
executor.

### Perfil e declaração de capacidades

Os dois são arquivos JSON, mas o formulário não mostra os caminhos deles: são editados por
botões.

* **O perfil** diz como conectar: provedor, modelo, endereço, referência da chave, temperatura,
  tempos de espera, configuração do treinamento de LoRA. Na pessoa o perfil é outro — e-mail,
  telefone e demais dados humanos.
* **A declaração de capacidades** diz o que o executor sabe fazer: a lista de habilidades com a
  nota de domínio, os formatos de entrada e saída e o preço (`in_per_1m` + `out_per_1m`). O
  editor dela é **exatamente o mesmo** para a IA, a pessoa e o registro do catálogo de modelos.

A declaração não é enfeite: uma tarefa com uma habilidade que não exista em nenhuma declaração
nunca encontra executor, e a escolha automática calcula a nota justamente por ela.

### Ocupação, limites e tempo de espera (só na IA)

* **«Ocupado até»** na IA **não é editável**: o sistema o define sozinho, quando o provedor
  responde «limite esgotado». Na pessoa são data e hora comuns, e limpar a data remove a
  ocupação.
* **«Limite de tokens por janela» e «janela do limite, h»** — a contagem própria do gasto,
  necessária onde o provedor não informa o saldo (assinatura do Claude Code: janela de 5 horas).
  Os dois campos são definidos pela pessoa, empiricamente. **Vazio ou 0 significa que os limites
  não foram informados**, e toda a mecânica é desligada por inteiro: o saldo não é contado, não
  há avisos e o início ocorre como antes.
* **«Tempo de espera da resposta, min»** — quanto esperar pelo modelo. Vazio são 30 minutos, e
  **0 é sem limite**. Um tempo de espera esgotado é um erro do trabalho, e não um limite: o
  executor não é marcado como ocupado e o início não é adiado.

O que acontece quando quase não resta limite: o trabalho **não é criado**, o início da tarefa é
adiado para o momento em que a janela se libera, e vai para o chat da tarefa um aviso — quanto
foi gasto e para quando o início foi adiado. O adiamento sobrevive ao desligamento do
computador, e a pessoa sempre pode iniciar a tarefa à força.

### E-mail para notificações

A marca **«usar o e-mail do login do usuário»** vem ligada por padrão e, nesse caso, o campo de
endereço nem aparece — em lugar dele fica escrito qual endereço será usado. Se a marca for
desligada e o campo ficar vazio, isso significa **«as notificações não vão para este
executor»**, e o formulário diz isso mesmo.

---

## O terceiro tipo de executor: «software automático»

Além da pessoa e da IA existe um terceiro tipo: **«software automático»**. O trabalho não é
feito por uma pessoa nem por um modelo de linguagem, e sim por **um programa deste
computador**: o treinador de adaptadores LoRA, um conversor, um utilitário de cálculo. A tarefa
continua sendo uma tarefa comum — com prazo, aceitação, console, histórico e resultado.

O nome interno desse executor não é um modelo nem um nome de pessoa, e sim um **par «plugin +
operação»**:

* o **plugin** é um registro da seção [Plugins e MCP](plugins.md) **deste servidor**: é ele que
  traz o programa e o caminho até ele;
* a **operação** é uma execução longa nomeada do manifesto do plugin (no treinador musubi é
  «Treinamento LoRA»). A operação marcada como **«Uma instância»** exibe essa marca no
  formulário: enquanto ela está ocupada, a segunda tarefa **espera** na fila em vez de falhar.

Em que ele difere do executor de IA — e por quê:

| A diferença | Por que é assim |
|---|---|
| **Não há prompt** | o programa não lê texto: tudo de que precisa chega pelos ajustes declarados do registro do plugin e por caminhos dentro da pasta do projeto. Linha de comando livre ninguém tem |
| **Nem tokens, nem preço, nem limites** | não há o que contar nem a quem pagar: o formulário não tem bloco de limites nem linha de gasto, e em «Gastos» esse executor não entra |
| **O servidor é obrigatório** | o programa está num computador concreto. O campo «servidor» é preenchido com o servidor onde o executor foi criado e nunca fica vazio: vazio significaria «no maestro», e ao trocar o maestro todo esse trabalho migraria para outra máquina. Uma tarefa de outro servidor não pode ser atribuída a ele — o sistema recusa com palavras |
| **Um trabalho por vez** | enquanto o programa calcula, um segundo trabalho não lhe é dado; a tarefa espera igual a como espera uma IA ocupada |
| **A escolha automática não o pega** | veja abaixo |

**Por que a escolha automática passa longe dele.** Os dois botões — «primeiro a IA, depois a
pessoa» e «primeiro a pessoa, depois a IA» — descartam esse executor **pelo tipo**, antes mesmo
de contar habilidades e preço. O terceiro tipo não se expressa pela ordem «IA → pessoa», e a
declaração de um programa pode dizer qualquer coisa: senão a tarefa «desenhe uma imagem» iria
em silêncio para o treinador de LoRA. Por isso o «software automático» é atribuído **à mão** —
ou a tarefa é criada a partir de um modelo pelo próprio botão que precisa desse trabalho (é o
que faz «Treinar» no [editor de LoRA](LoRAEditor.md)).

Ele é criado pelo botão comum «novo executor»: tipo «software automático» e depois o plugin e a
operação nas listas suspensas. Ele não tem perfil nem botão de declaração.

---

## Como o executor é escolhido

Manualmente — na lista suspensa do formulário da tarefa (somente os integrantes da equipe dela).

Automaticamente — por dois botões ao lado do campo do executor: **«primeiro a IA, depois a
pessoa»** e **«primeiro a pessoa, depois a IA»**. Os candidatos são os integrantes **ativos** da
equipe da tarefa, e a atividade é dupla: a do próprio executor e a da participação dele nesta
equipe. A nota é calculada assim:

```
nota = bias × qualidade + (1 − bias) × economia
```

onde *qualidade* é o domínio médio das habilidades exigidas, tirado da declaração, *economia* é
o inverso do preço da mesma declaração, e `bias` é a configuração do projeto **«preço ↔
qualidade»** (veja [Projetos](progects.md)). Um executor ocupado é pulado; se todos os adequados
estiverem ocupados, o sistema informa o motivo e a hora em que se liberam. A escolha é explicada
direto na interface — qualidade, preço, nota.

Existem também os **executores reservas**: a lista «podem substituir o executor» do formulário da
tarefa. A ordem nela é a ordem de preferência, e é tomado o primeiro livre. O executor designado
não muda com a substituição — no cartão aparece a linha «Trabalhando: <apelido>».

---

## IAs que vale a pena cadastrar logo

Depois do primeiro início, o sistema cadastra três — `Jon`, `Bob` e `Stiv` — em modelos com as
melhores habilidades para o uso típico escolhido. É um ponto de partida razoável, e eis por que
são três, e não um: **cada executor de IA conduz um trabalho por vez**. Um mesmo modelo só pode
trabalhar em várias tarefas ao mesmo tempo por meio de **executores diferentes**. Por isso
«acrescentar capacidade» aqui significa «acrescentar um executor», e não «comprar uma chave
maior».

---

## Casos especiais

* **Um modelo local prende o executor a um servidor.** Os pesos e o comando de início estão em
  um computador específico, por isso esse executor tem o campo «servidor»; atribuí-lo a uma
  tarefa de outro servidor não é possível — o sistema recusa por extenso («trabalha em um modelo
  do servidor S1 — passe a tarefa para lá»). Se você desligar o modelo local aqui, os seus
  executores desse modelo também deixam de ser ativos; nos executores de outro servidor isso não
  interfere.
* **A lista de executores só é editada no maestro** da organização: os executores são atribuídos
  a tarefas de todos os servidores, e a lista tem de ser uma só. A exceção é o cadastro de um
  integrante quando uma conta entra na organização.
* **Uma conta, um executor.** Ligar uma conta a dois executores não é possível: a gravação é
  bloqueada com um erro claro.

## Depois

* [Equipes](teams.md) — as equipes são formadas por executores, e quem define o círculo de
  executores de uma tarefa é justamente a equipe.
* [Projetos](progects.md) — a configuração «preço ↔ qualidade», da qual depende a escolha
  automática.
* [Modelos de IA](../models/README.md) — que modelos estão por trás dos executores de IA.

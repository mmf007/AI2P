# Arquivamento

**Arquivar** é mover dados do ambiente de trabalho para o ambiente de arquivo. Serve para
reduzir o volume operacional: projetos encerrados, hierarquias de tarefas concluídas, modelos de
processo já cumpridos e objetos saem de cena, e as listas, a árvore de tarefas e a busca deixam
de mostrá-los.

O arquivamento é feito **separadamente por organização**. Os dados no arquivo **não podem ser
alterados**: só é possível vê-los ou copiá-los de volta para o ambiente de trabalho.

O arquivo é organizado de forma simples. Todos os arquivos de uma organização ficam no
subdiretório `arc` dela:

* um arquivo **aberto** é o diretório `arc/<código>/`. Dentro dele há um banco SQLite próprio,
  **do mesmo esquema** da organização, e o subdiretório `projects` com os arquivos dos projetos.
  Ou seja, o diretório do arquivo é organizado como o diretório da organização — e é por isso que
  ele pode ser visto pelas telas comuns do programa;
* um arquivo **fechado** é um único arquivo `arc/<código>.zip`.

---

## 1. Os estados do arquivo

Na coluna «Ativo» da linha do arquivo aparece uma de quatro palavras, e elas são a junção de
**duas coisas diferentes**:

| Estado | O que significa | Um por cluster ou próprio de cada servidor |
|---|---|---|
| **atual** | o arquivo para o qual a transferência de dados vai neste momento | propriedade do próprio arquivo, uma por organização, **replicada** |
| **aberto** | está neste computador descompactado — dá para mudar para ele e olhar | próprio de cada servidor, **não replicado** |
| **fechado** | está neste computador como um único `.zip` — não há o que olhar | próprio de cada servidor, **não replicado** |
| **removido** | foi retirado deste servidor (ou nunca chegou aqui) | próprio de cada servidor, **não replicado** |

Daí decorrem quase todas as regras que se veem na tela:

* o arquivo atual está **sempre aberto** no servidor em que o trabalho acontece: fechá-lo e
  removê-lo não é possível — a transferência vai para ele («O arquivo atual ARC-2 não pode ser
  fechado: a transferência de dados vai para ele»);
* o registro do arquivo é **comum a todo o cluster**, mas os arquivos em disco não. Um arquivo
  removido aqui continua tranquilamente nos vizinhos, e dá para **baixá-lo de volta**;
* os dados só podem ser transferidos **para o arquivo atual** e apenas enquanto ele estiver
  **aberto neste servidor**.

---

## 2. Como criar um arquivo

**Configurações → Organizações**, na linha da organização **aberta no momento**, o botão
**«Adicionar arquivo»** (o ícone de caixa). O botão só existe na organização atual — os arquivos
são lidos no contexto dela — e ele só é acionável **no servidor maestro**: o registro de
arquivos é comum, e se cada servidor criasse arquivos por conta própria haveria tantos «atuais»
quantas máquinas houvesse no cluster. Nos demais servidores a dica diz exatamente isso: «Um
arquivo só é criado no servidor maestro da organização».

O formulário pergunta três coisas:

* **Nome do arquivo** — para a pessoa, qualquer um;
* **Código do arquivo** — letras latinas, dígitos, hífen e sublinhado, até 64 caracteres. É pelo
  código que o diretório do arquivo e o `.zip` dele são nomeados, por isso não há nele nem
  cirílico nem espaços, e isso é proposital;
* **Regras de arquivamento do novo arquivo** — de onde tirá-las: «copiar as regras gerais da
  organização» (padrão), «copiar as regras do arquivo atual anterior» (esta opção só existe se
  tiver havido um arquivo anterior) ou «não copiar nada».

Enquanto o formulário está aberto, ele **verifica o arquivo atual anterior**: se as regras de
arquivamento dele foram cumpridas. Se no ambiente de trabalho restar aquilo que, pelas regras,
já era hora de levar, o formulário dirá «As regras de arquivamento do arquivo «…» não foram
cumpridas: restam N registros», listará os dez primeiros e oferecerá o botão **«Executar o
arquivamento agora»**. Depois dele a verificação recomeça. Não foi feito como proibição: as
regras são uma dica, não uma barreira, e criar um arquivo novo é sempre possível.

O que acontece na gravação:

1. é criado o diretório do arquivo com um banco vazio do mesmo esquema da organização;
2. o registro recebe um número do tipo **ARC-1**, **ARC-2**, …;
3. **o novo arquivo passa a ser o atual**, e o atual anterior deixa de sê-lo — ele permanece como
   arquivo aberto, do qual agora só se pode ler;
4. para o novo arquivo são copiadas as regras da forma escolhida;
5. pelo cluster se espalha a ordem «troque o arquivo atual». Ela é cumprida não no momento em que
   o registro chega, e sim no fim da sessão de replicação — primeiro o arquivo atual **anterior**
   envia os dados dele uma última vez, e só então cede o lugar.

---

## 3. Abrir, fechar, remover, baixar de volta

Os arquivos aparecem como **linhas filhas** do registro da organização: `└ ARC-2`, nome, código,
servidores em que o arquivo existe e o estado. Os botões da linha aparecem conforme o estado:

| Botão | Quando aparece | O que faz |
|---|---|---|
| **Abrir o arquivo (descompactar neste servidor)** | o arquivo não é o atual e está fechado | expande o `.zip` no diretório `arc/<código>/` |
| **Fechar o arquivo (compactar em .zip)** | o arquivo não é o atual e está aberto | empacota o diretório em `arc/<código>.zip` e remove o diretório |
| **Remover o arquivo deste servidor** | o arquivo não é o atual e existe aqui | retira os arquivos **somente desta máquina**. O registro permanece |
| **Baixar o arquivo de outro servidor** | o arquivo foi removido aqui | uma janela com a lista dos servidores em que ele existe |
| **Alterar as regras de arquivamento** | o arquivo é o atual | as regras deste arquivo específico (veja a seção 5) |

A remoção pergunta com todas as letras: «Remover o arquivo ARC-2 «…» DESTE servidor? O registro
permanece: o arquivo existe em outros servidores da organização, e dá para baixá-lo de volta».

**O download** acontece em segundo plano — um arquivo pode ter gigabytes, e o formulário precisa
responder de imediato. Na janela estão listados os servidores em que o arquivo existe (exceto
você mesmo); clicar em um servidor responde «Solicitado o download do arquivo do servidor …», e
daí em diante a transferência é conduzida pela replicação. O arquivo chega **sempre fechado**
(`.zip`), mesmo que na origem ele esteja aberto: o estado do arquivo é próprio de cada servidor.
Uma interrupção da transferência vai para o diário e para a linha de erro do servidor de origem.

O que viaja entre os servidores sozinho:

* o arquivo **atual** é replicado do mesmo jeito que o ambiente de trabalho — e só ele: ele tem
  banco próprio, do mesmo esquema, logo diário de alterações próprio, cursores próprios e
  arquivos de projeto próprios;
* os demais arquivos não são replicados de forma alguma — são levados à mão, pelo botão
  «Baixar»;
* se um arquivo está aberto, fechado ou removido aqui é assunto de cada servidor e não é
  replicado. Viaja apenas o «existe/não existe», senão não haveria de onde tirar a lista de
  servidores para o download.

---

## 4. Transferência manual: tarefa, modelo, objeto, experiência, projeto

Não existe um botão «para o arquivo» à parte: a transferência é oferecida **no mesmo lugar da
exclusão**. Ao apertar a lixeira em uma tarefa, em um nó de modelo, em um objeto de projeto, em
um registro de experiência ou em um projeto inteiro, você recebe o formulário **«O que fazer com
«…»?»** com três desfechos:

* **Transferir para o arquivo**,
* **Excluir de vez**,
* Cancelar.

Antes de o botão ser apertado, o formulário diz três coisas.

**O que vai junto com o registro.** A hierarquia é arquivada por inteiro: «Junto com a tarefa vão
para o arquivo todas as subtarefas dela: …», «…todos os nós filhos dele», «…todos os objetos
filhos dele» e, em um projeto, «Junto com o projeto vão as tarefas e os modelos dele (N), os
objetos (N), os registros de experiência (N), além dos logs e das configurações do próprio
projeto». A composição não é inventada pelo formulário: quem a calcula é o próprio motor de
transferência, com as mesmas verificações da transferência de verdade.

**Por que exatamente não pode**, se não for possível levar. A recusa vem com o texto pronto e é
mostrada ao lado da opção desativada:

* «A tarefa T-15 não é de nível superior: a hierarquia é arquivada por inteiro, não em partes» —
  arquiva-se apenas a tarefa raiz;
* «A tarefa T-15 não pode ser arquivada: o estado é «em andamento» — o trabalho não terminou».
  Vão para o arquivo as tarefas nos estados **rascunho, revisão, pronto e cancelado**; qualquer
  outro estado, na raiz ou em **qualquer** descendente em qualquer profundidade, cancela a
  transferência do ramo inteiro;
* «O modelo está ocupado pela agenda futura SCH-3: primeiro desligue-a ou exclua-a».

**Que ainda não há arquivo**: «A organização ainda não tem arquivo atual — não há para onde
transferir. Um arquivo é criado em «Configurações → Organizações → Adicionar arquivo»». Os
direitos também são verificados: «Transferir dados para o arquivo é prerrogativa do
administrador da organização».

A transferência vai sempre **para o arquivo atual** e exige que ele esteja **aberto neste
servidor**; do contrário: «O arquivo atual … não está aberto neste servidor: a transferência de
dados não é possível». No fim vem um resumo do tipo «Transferido para o arquivo ARC-2: 128
registros, 40 arquivos transferidos, 3 copiados».

**Os links dentro dos textos transferidos** são reescritos uma única vez, na transferência: ao
endereço da tarefa ou do arquivo é acrescentado o parâmetro `arc=<código do arquivo>`. Por isso
um link de uma tarefa arquivada leva ao arquivo, e não ao ambiente de trabalho, onde essa tarefa
já não existe. Na restauração o parâmetro é retirado pelo mesmo código.

---

## 5. Regras de arquivamento

Uma regra responde a uma única pergunta: **o que e com que idade já é hora de levar para o
arquivo**. Há dois grupos de regras, e o conjunto de campos deles é o mesmo:

* as **regras gerais da organização** — **Configurações → Catálogos**, seção «Regras gerais de
  arquivamento». Elas são um **modelo**: é delas que são copiadas as regras de cada arquivo
  novo;
* as **regras do arquivo** — próprias de cada arquivo, abertas pelo botão «Alterar as regras de
  arquivamento» na linha dele. Elas têm o botão **«Restaurar o padrão»**: excluir todas as
  regras deste arquivo e copiar as regras gerais da organização. Elas são configuradas **somente
  no arquivo atual**: os dados são levados só para ele, portanto nos demais arquivos as regras já
  cumpriram seu papel e não há o que mudar — nas linhas deles não há botão de regras.

Os campos do registro de uma regra (os números das regras são do tipo **ARR-1**, **ARR-2**, …):

| Campo | Valores |
|---|---|
| **Refere-se a** | Tarefas, Modelos, Objetos, Experiência, Regras de segurança, Logs |
| **Estado da tarefa** | perguntado **apenas** nas regras sobre tarefas |
| **Estados dos descendentes** | uma lista; nada selecionado significa que qualquer um serve |
| **Atividade dos dados** | qualquer, ativos, inativos — em todos os tipos, menos nas tarefas |
| **Data** | data de criação ou data de alteração |
| **Prazo** | por tempo (dias) ou por calendário (meses e anos) |
| **Ativa** | uma regra desligada não participa da seleção |

Sobre as datas vale lembrar: uma tarefa criada há um ano e editada ontem vai para o arquivo
**pela data de criação**, mas **pela data de alteração** não vai. No prazo por calendário os
meses podem não ser definidos; nesse caso só os anos são considerados.

Como a regra seleciona as tarefas (o mesmo está dito na dica do formulário): «A hierarquia de
tarefas é arquivada por inteiro: na tarefa raiz são verificados o estado e a idade, e nos
descendentes apenas o estado». Ou seja, a regra olha **somente as tarefas raiz**; em cada
descendente, em qualquer profundidade, é verificado se o estado está na lista «Estados dos
descendentes», e **um único descendente inadequado cancela o arquivamento do ramo inteiro**. Uma
lista vazia significa «qualquer estado», e não «nenhum».

Dois tipos — as **regras de segurança** e os **logs** — podem ser selecionados por uma regra, mas
o motor não os transfere isoladamente: eles não têm nem hierarquia nem arquivos. O arquivamento
automático diz isso claramente — «O tipo de dados «security» não é transferido isoladamente para
o arquivo» — e conta esse registro como ignorado. Para o arquivo eles vão como parte de um
**projeto inteiro**.

---

## 6. Arquivamento automático por agenda

O arquivamento automático é «selecionar pelas regras do arquivo atual e transferir». Ele não tem
seleção própria nem transferência própria: usa as mesmas regras e o mesmo motor da transferência
manual.

Ele é configurado por **agenda**. Menu **«Agenda»** → **«Adicionar agenda»**, e depois:

1. **«O que é disparado»** — escolher **«ação do sistema»** em vez de «tarefa a partir de um
   modelo»;
2. **«Ação»** — **«Arquivamento automático»**. A dica do campo: «A ação é executada no servidor
   maestro da organização; nenhuma tarefa é criada»;
3. em seguida, os campos comuns da agenda — uma vez ou periodicamente, data, hora, período.

O modelo não é indicado de forma alguma, nenhuma tarefa é criada e nenhum executor é escolhido:
é o trabalho do próprio sistema sobre o banco dele.

O arquivamento acontece **somente no servidor maestro** da organização: o arquivo atual dela é
um só e, se ele fosse executado em cada máquina, os mesmos ramos iriam para o arquivo a partir
de vários servidores ao mesmo tempo. A recusa não vem como erro, e sim como uma linha clara — «O
arquivamento automático só acontece no servidor maestro da organização», «Não há arquivo atual:
a transferência de dados só vai para o arquivo atual», «O arquivo atual … não está aberto neste
servidor».

O resultado da execução é escrito no diário da organização como um resumo: «Arquivamento
automático para o arquivo ARC-2: 30 selecionados, 28 transferidos, 1 ignorado, 1 falhou». Cada
falha tem o seu motivo e é nomeada individualmente; os demais candidatos, mesmo assim, seguem
viagem.

A mesma ação é chamada pelo botão **«Executar o arquivamento agora»** do formulário de criação de
arquivo (seção 2) — ali ela responde não com o diário, e sim com a linha «Transferido para o
arquivo: N; falharam: N».

---

## 7. Como ver um arquivo: o campo «ambiente»

Na barra superior, entre a **organização** e o **projeto**, fica o campo **«ambiente»**:

> Organização: **Minha empresa**  ·  ambiente: **ambiente de trabalho**  ·  Projeto: **…**

Ao lado há a seta **«Trocar de ambiente»** e, no menu, «ambiente de trabalho» e todos os
arquivos **abertos neste servidor** (o atual vem marcado com a palavra «(atual)»). Um arquivo
fechado é um `.zip`, não há o que ler dele, e ele nem aparece na lista. Se não houver nenhum
arquivo criado, o campo «ambiente» não existe por completo, junto com o rótulo.

Escolher um arquivo **fecha todas as abas**: elas mostram dados do ambiente anterior. Daí em
diante você trabalha com as telas de sempre — árvore de tarefas, quadro, cartão, objetos,
experiência —, só que os dados vêm do arquivo.

O arquivo é aberto **apenas para leitura**. A interface não mostra os botões de edição no
arquivo e, se ainda assim chegar uma requisição de alteração (um plugin, um cliente externo), o
servidor responde com recusa: «O arquivo está aberto apenas para leitura: não é possível alterar
os dados dele». As exceções são exatamente duas — a seção de gerência dos próprios arquivos
(registro, regras, transferência e restauração) e as preferências de tela da própria pessoa (o
projeto escolhido, a disposição das abas): as duas coisas são gravadas no ambiente de trabalho, e
não no arquivo.

A volta ao ambiente de trabalho é feita pelo mesmo seletor, no item «ambiente de trabalho». Se o
arquivo escolhido for fechado ou removido deste servidor, o programa volta ao ambiente de
trabalho sozinho.

---

## 8. Restauração a partir do arquivo

A restauração é a operação inversa do arquivamento: o registro, com todos os descendentes, os
arquivos e os links, volta para o ambiente de trabalho e sai do arquivo.

Ela só é possível **a partir do arquivo atual**: nos demais os dados já não mudam. Por isso o
botão **«Restaurar»** (o ícone de caixa aberta) existe no cartão de uma tarefa ou de um nó de
modelo **apenas quando o arquivo atual está aberto**. Ele pergunta: «Restaurar «T-15 …» do
arquivo para o ambiente de trabalho? Vai a hierarquia inteira da tarefa, junto com os arquivos»
— e, ao terminar, responde «Restaurado do arquivo». O cartão, nesse momento, se fecha: no
arquivo essa tarefa já não existe.

A marca `arc=<código>` é retirada dos links pelo mesmo código que a colocou — os links voltam ao
formato de trabalho.

Um botão «Restaurar» à parte para um objeto de projeto, um registro de experiência ou um projeto
inteiro **hoje não existe**: o motor sabe fazer isso e a chamada `POST /api/archives/restore` os
aceita (tipos `object`, `experience`, `project`), mas o botão não foi levado à tela.

---

## 9. Perguntas frequentes

**Por que o botão «Adicionar arquivo» não funciona?** Você não está no servidor maestro da
organização ou não tem direitos de administrador da organização.

**Por que o meu arquivo não está no menu «ambiente»?** Na lista há apenas os arquivos **abertos
neste servidor**. Um fechado precisa primeiro ser aberto (descompactado) em «Configurações →
Organizações»; um removido aqui precisa ser baixado de outro servidor.

**Por que a tarefa não vai para o arquivo?** Ou ela não é raiz, ou o estado dela (ou o estado de
algum dos descendentes) significa trabalho inacabado. Vão para o arquivo apenas os rascunhos e
os ramos entregues — «revisão», «pronto», «cancelado».

**Para onde foi o arquivo atual depois que criei um novo?** Ele continuou no lugar dele como
arquivo aberto: deixou de ser o atual, lê-se dele, mas já não se escreve nele.

**Dá para editar os dados de um arquivo?** Não. O arquivo é só de leitura; para corrigir algo, é
preciso restaurar o registro para o ambiente de trabalho (e isso só é possível a partir do
arquivo atual).

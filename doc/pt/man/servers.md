# Vários servidores

**Um servidor é uma instalação do AI2P em um computador separado.** Várias instalações dessas se
unem em um **cluster**: os dados da organização são comuns, mas cada uma trabalha com as
próprias mãos.

A lista abre em **Configurações → Servidores**. A aba é vista pelo papel **admin** (dono deste
computador) e pelo **administrador do servidor** com logon local.

> Para uma `instalação isolada` este capítulo não é necessário de forma alguma: enquanto o
> servidor for um só, ele é o maestro de si mesmo, e nada do que está descrito abaixo acontece.

---

## 1. Para que ter vários

Quatro motivos, e todos são de física, não de escalabilidade.

**A placa de vídeo é uma, e os trabalhos são vários.** Um modelo local são gigabytes de pesos e
um processo que calcula em uma placa de vídeo específica. Ele não se muda de lugar. Por isso a
máquina com a placa potente vira o servidor em que se executam as tarefas de mídia e o
treinamento de LoRA, enquanto as tarefas leves de texto correm em qualquer lugar.

**As pessoas sentam em computadores diferentes.** Cada uma trabalha na sua máquina, com os seus
arquivos de projeto e os seus caminhos; e mesmo assim as tarefas, o chat, os modelos de processo
e a experiência são comuns a todos.

**O trabalho continua enquanto o computador alheio está desligado.** Os dados são replicados, e
não guardados em um único servidor: um vizinho desligado não para ninguém, ele se atualiza
quando for ligado.

**Os arquivos precisam ficar junto de quem os faz.** O trabalho é executado onde estão os
arquivos da tarefa, e o resultado é colocado ali mesmo. Não existe início entre servidores no
sistema, e isso é proposital.

O que o cluster **não** faz: ele não é tolerância a falhas nem balanceamento de carga. A tarefa é
executada no seu dono, e ninguém a assume no lugar dele.

---

## 2. Do que ele é feito

### O maestro

Em cada organização há **exatamente um** servidor maestro. Ele emite os códigos dos servidores,
conduz os catálogos da organização, semeia as regras gerais de trabalho e executa as agendas em
que o servidor não está indicado. (Em uma **tarefa** o servidor está sempre definido — tarefa
«de ninguém» não existe.) A topologia é uma **estrela**: todos os servidores replicam **apenas
com o maestro**; entre si eles não conversam.

Um mesmo servidor pode ser maestro de uma organização e participante comum de outra: a regência
pertence ao **vínculo** «organização ↔ servidor», e não ao servidor em si.

### Servidor e organização são «muitos para muitos»

Um servidor atende várias organizações, e uma organização vive em vários servidores. Por isso os
registros dos servidores ficam no banco **do servidor**, fora das organizações, e o vínculo
guarda o **código do servidor na organização** — `S0`, `S1`, … O primeiro recebe `S0`, e os
códigos dos demais o maestro emite.

O código entra nos números das tarefas (`T-18-S1`) e por isso **não é reaproveitado** depois que
um servidor sai do cluster: caso contrário os números ficariam ambíguos.

### Os servidores se reconhecem pela chave, não pelo endereço

Cada servidor tem uma **chave interna**, emitida uma única vez no primeiro início e que nunca
muda. O endereço é apenas um jeito de ligar para ele, e ele não é único: atrás de um NAT, o
endereço público costuma pertencer a um único servidor, e os demais se apresentam como
`localhost`. Por isso **o sistema não proíbe dois registros com o mesmo endereço** — o que
importa é somente a chave.

O registro também pode ter um **segundo endereço, externo** — o nome público ou o endereço do
roteador com a porta encaminhada. Ele é replicado junto com o registro (o próprio servidor é o
único que conhece o endereço externo dele, e quem precisa ligar por ele são os vizinhos), e na
comunicação os endereços são tentados um após o outro.

---

## 3. Propriedade: quem edita o quê

Os dados são comuns, mas quem os edita é o **dono da linha**.

* A **tarefa** tem um servidor dono: é ali que ela é editada, ali que os trabalhos dela são
  executados e ali que ficam os arquivos dela. Nos demais servidores ela é visível para leitura
  — mas **«iniciar», «iniciar a hierarquia» e «escrever no chat» estão disponíveis**: o início
  vai para o dono como solicitação, e a mensagem chega pela replicação.
* Os **inícios automáticos** (dos descendentes, do desbloqueio, da divisão) são conduzidos pelo
  dono da tarefa — e só por ele. Sem essa regra, um disparo geraria um trabalho em cada
  servidor. Uma linha vinda de um parceiro de versão antiga, sem servidor, o maestro assume para
  si ao abrir a organização.
* A **agenda** também tem o seu servidor, e ela dispara apenas nele.
* **O diretório do projeto e a marca «ativo» do projeto são por servidor**: os diretórios são
  diferentes em computadores diferentes (veja [Projetos](progects.md)).
* **A lista de executores só é editada no maestro** — os executores são atribuídos a tarefas de
  todos os servidores, e a lista tem de ser uma só.

Trocar o dono de uma tarefa é possível pelo botão **«trocar o servidor»** no cartão dela; o
número da tarefa, nesse caso, **não muda** — ele é o identificador e o nome do diretório dos
arquivos dela.

---

## 4. Como conectar um segundo computador

A conexão é sempre **ao maestro**, e a decisão é tomada por uma pessoa do lado dele.

**Na máquina nova.** Instale o AI2P e, na primeira etapa do assistente, **desmarque a opção
«servidor primário do cluster»**. Assim a organização não é criada aqui (ela chega pela
replicação, com os identificadores dela), e na última etapa do assistente abre a tela
**«Conexão ao cluster»**: o endereço do maestro (`host:5480/ai2p` — protocolo e prefixo podem
ser omitidos), o código da organização dele (opcional: se lá houver apenas uma, é ela que é
tomada), a sua assinatura e uma observação para quem receber.

Se o maestro tiver várias organizações, ele responde com a lista delas, e dá para escolher
**várias de uma vez**.

**Em um servidor que já funciona** o mesmo é feito pelo formulário do servidor remoto, na aba
«Servidores».

**No maestro.** A solicitação aparece como uma linha à parte no fim da lista de servidores e
também de forma modal para o dono ou o administrador que estiver trabalhando naquele momento.
Três decisões: **ACEITAR**, **ADIAR**, **RECUSAR**. No formulário se vê a **participação do
solicitante na organização**: se ele ainda não for executor, fica ligada a marca **«criar um
executor»** — não convém desmarcá-la, senão o servidor se conecta e a pessoa, do lado dela,
esbarra em «você não faz parte de nenhuma organização».

Depois da confirmação, o servidor conectado recebe o token de acesso e a chave da organização,
cria a organização nele mesmo e a preenche por replicação. **A primeira sessão quem inicia é
ele** — o maestro não liga para um servidor atrás de NAT.

O que vale saber de antemão:

* **uma conta com senha vazia não consegue se conectar pela rede** — a senha é definida antes;
* a solicitação é **anônima**: ela não pede e-mail nem senha no maestro. A única exceção é
  quando a pessoa com aquele e-mail **já existe** no maestro: nesse caso é pedida a senha **do
  maestro**, e por ela é emitida a mesma chave interna, para que não haja no cluster duas contas
  com o mesmo e-mail;
* uma **duplicidade de e-mail ou de nome** o maestro informa **em resposta à solicitação**, e não
  na confirmação: corrigi-la deve ser feito onde a pessoa está diante da tela — pelo botão
  «Voltar» até a etapa «Usuário»;
* a **marca «mostrar todas as solicitações»** acima da lista acrescenta o histórico dos dois
  tipos de solicitação; sem ela se vê apenas aquilo com que ainda é preciso fazer algo.

### Retirar um servidor do cluster

Nada é transferido automaticamente. O servidor é marcado como **inativo** e, depois disso, no
maestro aparece ao lado dele o botão **«passar as tarefas ao maestro»** — sem isso as tarefas do
servidor que saiu ficariam não editáveis para sempre.

**Excluir** o registro para valer só é possível enquanto não tiver havido nenhuma replicação
bem-sucedida com aquele servidor (é assim que se remove uma primeira tentativa malsucedida);
nesse caso o código emitido também é liberado.

### Trocar o maestro

Isso é uma **solicitação bilateral**, e não um botão. Podem apresentá-la o servidor a ser
designado ou o maestro de hoje, e apenas sob o **logon local de administrador do servidor**; um
terceiro servidor não se intromete numa transferência alheia. Um servidor com o endereço
`localhost` não pode ser maestro — os demais, com esse endereço, ligariam para si próprios.

A solicitação viaja para a outra parte pela replicação comum, e a decisão volta do mesmo jeito.
Só existe **uma** solicitação simultânea por organização.

Está previsto à parte o caso do **maestro perdido**: uma solicitação apresentada pelo próprio
servidor a ser designado tem uma contagem regressiva de **12 horas**, depois da qual aparece o
botão «Aceitar unilateralmente». Sem ele, uma organização cujo maestro morreu fisicamente
ficaria sem maestro para sempre. O que for aceito assim é marcado à parte — não houve
concordância da outra parte.

O novo maestro **anuncia-se aos demais diretamente** (a assinatura é a chave da organização),
porque pode ser que eles nunca tenham conversado com ele; o anúncio se repete a cada minuto
enquanto restarem servidores sem o par de tokens — um vizinho desligado fica sabendo quando for
ligado.

---

## 5. Como eles replicam

### O banco, por um diário de alterações

Cada edição de uma tabela replicada coloca uma linha no diário de alterações: qual tabela, a
chave da linha, a operação, o **horário no autor** e o **nó autor**. Depois disso a replicação
se reduz a «me dê as alterações posteriores ao meu cursor».

Quem escreve o diário são os **gatilhos do SQLite**, por isso os serviços não sabem nada sobre
replicação, e uma entidade nova é incorporada sozinha.

**O conflito é resolvido pela regra do «vence o último»**: a alteração recebida é comparada com
a nossa última alteração da mesma linha pelo par (horário, autor); se não for mais nova, não é
aplicada. É isso também que impede que uma alteração fique circulando. Com a propriedade
separada, um conflito de verdade é raro, por isso ele é mostrado na tela de diagnóstico como um
motivo para investigar.

**O eco é apagado pela autoria**: as alterações próprias do autor não lhe são devolvidas, mas as
alterações de um terceiro servidor passam pelo maestro.

### A sessão

A sessão corre **por uma organização inteira**: primeiro uma, depois outra. Ela é bilateral
(buscar o que é do outro, entregar o que é nosso), e quem a conduz é o lado cuja contagem
expirou antes; o segundo recebe «a sessão já está em andamento» e pula o início dele. A troca é
feita em lotes — daí a barra de progresso e o percentual.

**Os intervalos são uma configuração do par, e não própria.** São dois: o intervalo comum e o de
repetição após erro (mínimos de 30 e 15 segundos; vazio significa não replicar automaticamente,
apenas pelo botão). Eles são definidos no formulário **do servidor com quem a troca acontece**:
não existe par consigo mesmo, por isso eles não aparecem no formulário do próprio servidor. A
confirmação da solicitação define o intervalo sozinha (300 segundos), senão um par recém-conectado
ficaria calado até que alguém abrisse o formulário.

### Os arquivos

Junto com o banco viajam os arquivos: os dados da organização e a pasta **`Common`** do projeto
(o caminho dela é definido em cada servidor separadamente, sempre relativo ao diretório do
projeto). Dentro de `Common` funciona o filtro **`.repignore`** — a sintaxe é a do `.gitignore`.

### Um servidor atrás de NAT liga sozinho

Se o registro do parceiro indicar um **endereço de loopback** com a nossa mesma porta, não se
pode ligar para lá — cairíamos em nós mesmos. Esses pares a varredura automática **pula em
silêncio** (não é um erro), o início manual responde com uma explicação, e as sessões quem
inicia é esse próprio servidor. O horário da sessão, nesse caso, também é registrado pelo lado
receptor: caso contrário «Última tentativa» ficaria com um traço, embora a troca esteja
acontecendo.

---

## 6. O que se vê na tela e com o que consertar

### A lista de servidores

O servidor local é **sempre o primeiro**. Colunas: código na organização, nome, endereço, papel
(as marcas «local» e «maestro»), organizações, «ativo» e a **situação da replicação**.

Na situação: **«manual»**, se o intervalo não estiver definido; do contrário, quanto falta para
a próxima sessão; durante a sessão, a barra de progresso e o percentual; em caso de erro, «erro»
e o tempo até a repetição, com os detalhes na dica. No próprio maestro esse campo não existe —
ele não tem um par próprio; no maestro se veem as situações de todos os servidores e, em um
servidor comum, apenas a linha dele.

Ao lado ficam o **botão de início manual** (a seta circular) e o botão da **tela de
diagnóstico**.

### A tela de diagnóstico da replicação

Uma linha para cada organização em que este par existe: o estado da sessão, os intervalos, os
**cursores dos dois bancos**, o **atraso** (quantas alterações nossas o parceiro ainda não
buscou), recebido/entregue na última sessão, arquivos e bytes, o número de conflitos e o último
deles, o horário da última tentativa e da última sessão bem-sucedida, o texto do último erro e a
**composição da fila de repetição** linha a linha (área, tabela, chave, motivo, número de
tentativas).

Embaixo há dois botões:

* **«executar a replicação»** — uma sessão agora mesmo;
* **«replicação inicial»** — esquecer os cursores do par, a base de comparação de arquivos **e a
  fila de repetição**: a próxima sessão vai reler e reconferir tudo do zero. Essa é também a
  **alavanca manual para uma sessão travada** — o botão solta o par ocupado.

O segundo botão cura quase tudo o que parece «as réplicas se desencontraram»: reler o diário do
zero é seguro, porque o que ficou pendente chega de novo, já junto com as linhas pai.

### Conflitos

O botão de conflitos aparece em um servidor quando há algo a resolver. **A versão da esquerda é
a do maestro, e a da direita é a do servidor comum** (o par tem a mesma aparência em qualquer
servidor em que seja aberto). Há três tipos:

* **arquivo** — caminho, diretório, tamanho e horário de cada versão; aceitar a da esquerda ou a
  da direita, ou renomear qualquer uma delas (a decisão é aplicada na replicação seguinte);
* **chave de API da organização** — a referência da chave, o horário da edição e a impressão
  digital (**os valores não são exibidos**); aceitar a da esquerda ou a da direita, ou informar
  uma chave nova;
* **registro do catálogo de modelos de IA** — o nome do modelo e o horário da edição; aceitar o
  da esquerda ou o da direita.

---

## 7. Limitações que é preciso conhecer de antemão

* **O HTTPS se liga, mas não por conta própria.** Enquanto o protocolo do servidor for `http`, o
  tráfego entre os servidores é aberto, e o cluster deve ser implantado em uma rede confiável ou
  por VPN. A passagem para `https` é um trabalho à parte, com um certificado em cada máquina, e
  um certificado de autoridade certificadora própria exige a marca «Confiar no certificado dos
  vizinhos de cluster» ou a distribuição do certificado raiz para todas as máquinas: veja
  [HTTPS](https.md).
* **O início manual de uma tarefa só está disponível no servidor dela** (de um servidor alheio
  ele vai como solicitação): o trabalho é executado onde estão os arquivos.
* **Os perfis dos modelos não são replicados** — em um registro personalizado do catálogo, no
  parceiro, a coluna de configuração ficará vazia.
* **Os segredos nunca são replicados**: a chave da organização, a conta do administrador do
  servidor, os tokens dos servidores e o subdiretório `secrets/` ficam no computador deles.

## Depois

* [Configuração](config.md) — o formulário do servidor local, os endereços e os diretórios.
* [HTTPS](https.md) — o certificado do servidor, a configuração do navegador e o cluster por
  https.
* [Tarefas](tasks.md) — o servidor dono da tarefa e o trabalho com uma tarefa alheia.
* [Arquivamento](Archives.md) — os arquivos também são replicados, mas nem todos e nem sempre.

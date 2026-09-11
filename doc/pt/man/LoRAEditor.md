# Editor de LoRA

Um adaptador **LoRA** é um arquivo pequeno com um acréscimo aos pesos do modelo. Ele ensina ao
modelo uma coisa bem específica: o rosto de um personagem, a textura de um material, um jeito de
desenhar. Depois do treinamento, o adaptador é acoplado ao modelo no momento da geração — e o
personagem deixa de «derreter» de quadro para quadro, mesmo onde um passaporte de texto já não
basta.

O **editor de LoRA** é o formulário em que se monta o dataset (quadros com legendas), se mantém
a lista de modelos para os quais o adaptador é treinado e se dispara o próprio treinamento. Esta
página trata dele.

Esta ajuda pode ser aberta direto do editor: o botão com o livro no canto superior direito do
formulário.

---

## 1. Antes de começar

O treinamento não é um botão, é o trabalho de um programa de treinamento que vive **fora do
AI2P**. Para que o editor consiga executá-lo, são precisas quatro coisas:

| O quê | Onde se define | Como conferir |
|---|---|---|
| A pasta do projeto neste servidor | cartão do projeto, guia «Principal» | sem ela vem a recusa «O projeto não tem pasta neste servidor: não há onde montar o dataset de treinamento» |
| Um modelo que trabalhe com adaptadores | Configurações → Modelos, a marca LoRA no registro | na guia «Modelos» do editor, em um registro inadequado o botão «Treinar» fica apagado |
| O comando de treinamento desse modelo | perfil do modelo, seção `lora.train.start.command` | sem ele vem a recusa «O catálogo de modelos não diz com o que treinar: o comando de treinamento não está definido» |
| O repositório de modelos | Configurações → Servidores → formulário do servidor local, campo «Repositório de modelos» | é lá, no subdiretório `loras`, que o adaptador pronto será colocado |

Não exigem um ajuste à parte, mas participam do trabalho mais três coisas: o **plugin
treinador**, o **executor «software automático»** e o **modelo da tarefa de treinamento**. O
treinador vem com a instalação do modelo, e o executor e o modelo de tarefa são criados com um
botão só pela própria janela de disparo do treinamento — veja a seção 5, «O treinamento corre
como tarefa».

O que exatamente o seu modelo sabe fazer se vê no registro dele do catálogo: **Configurações →
Modelos**, coluna «LoRA» (um visto significa que funciona; um traço com dica explica por que
não), e no próprio formulário do registro há as linhas «O adaptador é acoplado», «Treinamento»,
«O adaptador pronto é colocado em» e o link **«Como treinar o adaptador deste modelo»**. É por
ele que vale começar: ali está a instrução de quem lançou o modelo.

Sobre o comando de treinamento vale dizer algo à parte. Nos **dois registros do Kandinsky 5**
ele **já vem** na distribuição, e não é preciso escrever nada: quem treina é o musubi-tuner, e o
próprio treinador, junto com o Python, chega **na instalação do modelo** — pelo botão «Instalar»
do formulário dele, no mesmo lugar em que os pesos são baixados (detalhes no documento do
modelo, botão «i»). No primeiro treinamento são preparados apenas os arquivos de configuração
(`dataset.toml`, `train.cmd`): o ambiente `.venv` com o torch, as dependências do treinador e os
codificadores de texto Qwen2.5-VL-7B e CLIP (cerca de 18 GB) vêm com a instalação do modelo,
em uma janela que mostra o andamento. Se o Python 3.10–3.12 já estiver
neste computador, a instalação não o baixa: usa o seu. Nos demais registros o comando está
**propositalmente vazio**: o diretório do repositório de treinamento, o ambiente e as opções de
execução são próprios de cada um, e um comando inventado seria pior do que um vazio — pelo link
do formulário do modelo ele é montado e escrito no perfil uma única vez
(`lora.train.start.command`).

Junto com o comando, o perfil pode trazer também os **arquivos de configuração do treinador**
(`lora.train.files`): a configuração do dataset, o script de execução. Eles são colocados ao
lado do dataset antes de cada execução e são **reescritos** todas as vezes — corrigi-los deve
ser feito no perfil do modelo, e não em disco; coloque os seus arquivos com nomes próprios, que
ninguém vai tocá-los.

Nos registros do catálogo que são treinados **fora do sistema** (modelos locais de texto do
llama.cpp), disparar o treinamento pelo AI2P responde com a recusa «O adaptador para o modelo …
é treinado fora do sistema — coloque o arquivo pronto à mão e informe o caminho dele». Não é um
defeito: nesses modelos o editor serve para montar o dataset e manter a lista, e o treinamento é
feito com meios próprios, informando o caminho do arquivo pronto no campo «Arquivo do adaptador»
do formulário do objeto.

---

## 2. Como abrir o editor

1. Abra o cartão do projeto → guia **«Objetos»**.
2. Crie um objeto do tipo **«adaptador LoRA»** (botão «Novo objeto», campo «Tipo») e
   **salve-o**. Enquanto o objeto não estiver salvo, o botão de configuração nem existe: os
   quadros do dataset e as linhas de treinamento precisam de um dono, e um objeto ainda não
   criado não tem número.
3. Abra o objeto e aperte **«Configuração de LoRA»**.

A seção «adaptador LoRA» e esse botão só existem no objeto do tipo «adaptador LoRA». Um
personagem, uma locação e um adereço não têm adaptador próprio — mas podem ter um adaptador
**pronto**: o caminho do arquivo dele é escrito no campo «Arquivo do adaptador» do mesmo
formulário e, então, o adaptador é acoplado ao modelo sempre que a descrição de uma tarefa fizer
referência a esse objeto.

O formulário do editor é composto pelo campo **«Descrição»** e por duas guias — **«Dataset»** e
**«Modelos»**.

---

## 3. O campo «Descrição»: o que escrever

É exatamente o mesmo texto que no formulário do objeto se chama **«Passaporte (vai para o
prompt)»** — o objeto não tem uma segunda descrição. Ele **não participa do treinamento**: vai
para o prompt da **geração**; quando na descrição da tarefa houver o link `@obj:OBJ-7`, é esse
texto que entra no lugar dele.

O que escrever:

* a **descrição literal daquilo em que o adaptador foi treinado**, e não uma anotação para você
  mesmo: «mulher de 30 anos, cabelo ruivo até os ombros, olhos verdes, sardas, jaqueta cinza» —
  e não «Masha, nossa personagem principal, ver os quadros»;
* a **palavra-chave (gatilho)**, se ela tiver sido usada nas legendas dos quadros — é com ela que
  o modelo se lembra do que aprendeu: «`m4sha_rf`, mulher de 30 anos, …»;
* a **força e os limites**: o que o adaptador NÃO sabe fazer («só até a cintura, não houve
  quadros de corpo inteiro») — isso poupa algumas gerações malsucedidas;
* é preciso escrever **no idioma em que as descrições das tarefas são escritas**: o texto vai
  para o prompt como está, misturado ao resto da descrição do quadro.

O que não é preciso escrever: anotações de serviço do tipo «treinado com 2000 passos em 40
quadros» — elas não ajudam a geração e ocupam espaço no prompt. Para isso existe a guia
«Modelos»: ali se vê para quê e quando o treinamento foi feito.

A descrição **não é salva aqui**. O botão «Aplicar» a devolve ao formulário do objeto, e quem a
salva é ele, junto com todo o resto do formulário. Os quadros e as linhas de treinamento, ao
contrário, são salvos de imediato, a cada ação: o treinamento leva horas, e perdê-lo por um
«Cancelar» acidental não se pode.

---

## 4. A guia «Dataset»

O dataset é um conjunto de quadros: **imagem + legenda**. É por eles que o adaptador aprende.

**O dataset é um objeto à parte** (versão 1.98). Ele é criado sozinho quando você abre o editor,
chama-se «dataset» e fica como objeto filho do seu adaptador; os quadros já são filhos dele. Foi
feito assim por causa da hierarquia: enquanto os quadros eram filhos diretos do objeto, meia
centena de imagens ficava misturada aos objetos filhos que tinham sentido, e não havia como
entender a lista.

### 4.0. O dataset atual e por que ter vários

No alto da guia há o campo **«Dataset atual»** e o botão **«+»**.

* Tudo na guia se refere ao dataset **selecionado**: tanto a lista de quadros quanto as
  configurações de controle das imagens. Um quadro novo vai para ele.
* O botão **«+»** pergunta o nome e cria mais um dataset, tornando-o de imediato o atual.
* Há vários datasets porque **modelos diferentes têm exigências diferentes quanto às imagens**:
  um treina com 768×512 PNG, outro pede 1024×1024. Manter um único conjunto de quadros para
  ambos significaria remontá-lo a cada troca de modelo.

A escolha é memorizada **no objeto**, e não na janela: é pelo dataset atual que vai também o
treinamento disparado depois.

### 4.0.1. As configurações de controle das imagens

A seção **«Controle das imagens do dataset»** são os limites até os quais o quadro é comprimido
ao ser acrescentado **a este dataset**: largura, altura, peso em KB, formato e ainda quantos
quadros são necessários no mínimo e quantos faz sentido usar no máximo.

* Ao criar o dataset, elas são **copiadas das configurações gerais** do aplicativo
  (**Configurações → Principal → «Quadro do dataset de LoRA»**) e daí em diante vivem a vida
  delas.
* O botão **«Copiar do modelo»** coloca o que o modelo declarou no catálogo. Na lista aparecem
  apenas os modelos da guia «Modelos» deste objeto cujas configurações de controle estejam
  preenchidas no catálogo: de um modelo calado não há o que copiar. Se o modelo indicar vários
  formatos, é copiado o **PNG** — ele é sem perdas.
* Os valores alterados são gravados pelo botão **«Salvar»**; os quadros já acrescentados não são
  refeitos com isso — os novos limites valem para o próximo quadro.

### 4.1. Como acrescentar um quadro

O botão «+» abre o formulário do quadro. Depois vêm dois passos, e eles não são supérfluos.

**Passo 1. Pegar o original.** Ou **«Por endereço»** (`http://…`, a imagem é baixada pelo
servidor), ou **«Arquivo no computador»** — o botão com a pasta abre a navegação pelos discos
deste computador. A navegação começa:

* pelo diretório já digitado no campo do caminho, se houver;
* senão, pelo diretório **de onde o arquivo foi pego da última vez**;
* senão, pela **pasta do projeto**;
* e só se nada disso existir, pela lista de discos.

O diretório da última escolha é memorizado para valer (sobrevive ao fechamento da janela e ao
próximo acesso), por isso um lote de quadros de uma mesma pasta é acrescentado sem vaguear pelos
discos de novo. O diretório pode ter deixado de existir — então a navegação começa pelo pai
existente mais próximo, em vez de mostrar uma lista vazia.

Aperte **«Carregar»** — o original vai para o armazenamento e aparece a moldura de recorte.

**Passo 2. Recortar o quadro.** Puxe os cantos superior esquerdo e inferior direito da moldura:
entra no quadro o que estiver dentro dela. O recorte, a compressão e a conversão de formato são
feitos **no navegador** — para o servidor vai a imagem já pronta. Sob a moldura está escrito até
onde o quadro será comprimido: os limites vêm das **configurações de controle das imagens DESTE
dataset** (veja 4.0.1), e não das configurações gerais do aplicativo — as gerais foram apenas um
retrato delas no dia da criação. A proporção não muda, e o coeficiente usado é o menor dos dois.
Não coube no limite de tamanho? Reduza a moldura ou aumente o limite nas configurações do
dataset.

O quadro é colocado na **pasta de dados da organização**, no subdiretório
`projects/<código do projeto>/objects/<código do objeto>/<código do dataset>/`, e é criado como
objeto do tipo «quadro de referência», filho do **dataset**. Por isso os quadros também aparecem
na lista geral de objetos do projeto.

O lugar não é casual: a pasta de dados da organização é **replicada**, por isso um dataset
montado aqui também é visto nos outros servidores do cluster — lá é possível abri-lo, ver os
quadros e iniciar o treinamento por eles. A pasta do projeto é própria de cada servidor e não
viaja pelo cluster, por isso os quadros guardados nela (como era até a versão 1.112) não
apareciam no servidor vizinho. Os quadros acumulados pelas versões anteriores são movidos pelo
próprio programa na primeira execução da nova versão; os arquivos originais na pasta do projeto
não são apagados — ela pertence a você.

### 4.2. A legenda do quadro: o que escrever

O campo **«Descrição do quadro»** é **o único texto que o treinamento vê**. Ao lado de cada
imagem no diretório do dataset ele é colocado como um arquivo `.txt` de mesmo nome (em alguns
modelos, em um arquivo comum `captions.json`; qual dos dois está dito no perfil do modelo).

Recomendações comprovadas pela prática de treinamento de LoRA:

* **descreva o quadro, e não o personagem em geral.** O treinamento vai aprender aquilo que é
  igual em todos os quadros — e isso não precisa ser legendado. Legenda-se o que **muda** de
  quadro para quadro: o ângulo, o enquadramento, a luz, o fundo, a roupa, a expressão do rosto;
* **comece pela palavra-chave**, se você tiver uma: `m4sha_rf, primeiro plano, três quartos para
  a esquerda, luz do dia, fundo de rua desfocado`;
* **não escreva aquilo que não quer que seja aprendido.** Tudo o que for nomeado na legenda o
  modelo considera a parte variável e aprende a controlar; tudo o que não for nomeado ele funde
  no próprio adaptador. Daí a regra habitual: a aparência fixa do personagem não se descreve, e
  o fundo e a roupa se descrevem — senão o adaptador aprende para sempre tanto a jaqueta quanto a
  parede de tijolos;
* **escreva em enumerações curtas separadas por vírgula**, e não em parágrafos: os scripts de
  treinamento cortam a legenda pelo comprimento, e uma frase longa perde o final;
* **um idioma para o dataset inteiro.** Uma mistura de idiomas nas legendas é o jeito certo de
  obter um adaptador que responde uma vez sim, uma vez não;
* **não deixe a legenda vazia**: um `.txt` vazio significa «nada muda», e o treinamento funde no
  adaptador o quadro inteiro, junto com o fundo.

O campo **«Nome»** do formulário de edição do quadro é o nome do **objeto**, e não o nome do
arquivo: o arquivo já está na pasta do projeto com o nome dele e não é renomeado. No acréscimo,
ao contrário, o nome se torna o nome do arquivo (a ele é acrescentado o código do objeto, para
que os nomes não coincidam entre personagens diferentes).

O botão «alterar» edita apenas o nome e a legenda. Não há com que recortar de novo um quadro já
acrescentado: para isso ele é excluído e acrescentado de novo a partir do mesmo original. O
botão «excluir» tira o quadro do dataset, mas **o arquivo em si permanece na pasta do projeto** —
ele pode ter ido parar ali por fora do editor.

### 4.3. A ordem dos quadros importa?

Resposta curta: **para o treinamento em si, não; para a seleção, sim.**

A ordem na lista é a ordem do **acréscimo** (os quadros são numerados como objetos: OBJ-12,
OBJ-13, …), e é nessa mesma ordem que eles vão para o diretório do dataset com os nomes
`001.png`, `002.png`, `003.png`… Não há com que reordenar os quadros no editor: para que um
quadro seja o primeiro, é preciso excluí-lo e acrescentá-lo de novo.

Onde a ordem realmente importa:

1. **O corte pelo limite superior.** No perfil do modelo está definido quantos quadros ele usa
   (`dataset.maxItems`; nos registros distribuídos do Kandinsky são 60). Se houver mais quadros,
   são usados os **primeiros** — os demais silenciosamente não participam. Ou seja, os melhores
   e mais característicos quadros devem ser acrescentados **antes**.
2. **A numeração dos arquivos.** É pelo número `001…NNN` que depois você associa o quadro ao que
   viu no log do script de treinamento. A ordem dos números = a ordem do acréscimo.

Onde a ordem não importa: o processo de treinamento em si percorre o dataset muitas vezes e o
embaralha — o «primeiro» quadro não influi mais no resultado do que o «último». Vale lembrar à
parte do limite inferior (`dataset.minItems`; no Kandinsky são 10): menos que isso dá a recusa
«O dataset tem N quadros, e o treinamento deste modelo exige no mínimo M».

Mais uma regra de seleção: vão para o treinamento apenas os quadros **ligados**. Um quadro pode
ser desligado sem ser excluído — abra-o como objeto (guia «Objetos» do projeto) e desmarque
«ativo». É a única forma de tirar um ângulo ruim do treinamento sem perder o arquivo.

---

## 5. A guia «Modelos» e o disparo do treinamento

O adaptador é treinado **para um modelo específico**: os pesos dos modelos são diferentes, e um
arquivo treinado para um não serve para outro. Por isso há tantas linhas de treinamento quantos
forem os modelos para os quais você treina o adaptador.

O treinamento vai sempre **pelo dataset atual** — aquele que está selecionado na guia «Dataset».

1. O botão «+» serve para escolher o modelo. Na lista estão os registros **ativos** do catálogo
   que declaram no perfil o trabalho com LoRA; as linhas já criadas não são oferecidas uma
   segunda vez. Uma lista vazia significa exatamente uma coisa: não há neste servidor um
   registro adequado e ligado.
2. O botão **«Treinar»** da linha é o começo. Direto do formulário o treinamento **não é
   disparado**: ele é montado como uma **tarefa** separada. Primeiro o sistema faz as perguntas
   sobre o dataset, depois oferece escolher um **modelo de tarefa** e pergunta o que fazer em
   seguida — «criar e disparar», «somente criar» ou «cancelar». A ordem detalhada está abaixo.
3. A linha passa ao estado **«treinando»**, e o treinamento em si leva horas. O formulário pode
   ser fechado — o treinamento não é interrompido por isso; ao abri-lo de novo você verá o
   estado atual (enquanto houver na lista uma linha em treinamento, ela se relê sozinha a cada
   cinco segundos).
4. **O estado da linha é um link para o cartão da tarefa**, e não só em «treinando»: a um
   treinamento que caiu a pessoa volta um dia depois justamente atrás do log. A parada está lá
   também: parar o treinamento agora significa parar a tarefa.
5. Uma linha já treinada é retreinada mediante confirmação: o retreinamento **sobrescreve o
   arquivo pronto** do adaptador, que já pode ter sido colocado no modelo, e não há como
   desfazer isso.

Os estados da linha: **não treinada → treinando → treinada**, ou então **erro**. No erro, a dica
(passe o mouse sobre a palavra «erro») traz o que o programa de treinamento ou o provedor
disseram — sem isso a palavra «erro» não tem serventia nenhuma.

### Sobre o que o sistema pergunta antes do disparo

Além da confirmação do retreinamento, são duas — e as duas tratam do que o treinamento faria em
silêncio de errado.

* **«O dataset não é aquele».** A linha de treinamento lembra por qual dataset foi obtido o
  arquivo que está nela. Se agora estiver selecionado outro, o sistema nomeia os dois e pergunta
  por qual disparar: «pelo atual» ou «pelo anterior». Um adaptador treinado com outros quadros
  é, na prática, outro personagem, e substituí-lo em silêncio não se pode.
* **«As configurações são maiores do que as declaradas pelo modelo».** As configurações de
  controle das imagens do dataset são comparadas com o que o modelo declarou no catálogo, e
  apenas **para mais**: o quadro é maior, mais pesado, há mais quadros — ou o formato não é o
  que o modelo aceita. Um dataset com valores **menores** do que os declarados é legítimo, e
  sobre ele não se pergunta. Na pergunta está listado o que exatamente divergiu, e há **três**
  respostas: «Treinar» (como está), «Cancelar» e **«Criar/mudar para o dataset adequado»**.

As duas condições são verificadas pelo **servidor**, e não apenas pelo formulário: o treinamento
também é disparado de fora desta janela.

### «Criar/mudar para o dataset adequado»

O botão dispensa o trabalho manual de «criar um dataset — copiar para ele as configurações do
modelo — comprimir meia centena de imagens». Isso é feito assim:

1. **Primeiro é procurado um adequado entre os datasets já criados** do objeto: as configurações
   cabem nas declaradas pelo modelo E nele estão os mesmos quadros (conferência pelos nomes dos
   arquivos, sem a extensão). Se for encontrado, ele simplesmente passa a ser o atual, e nada é
   comprimido.
2. Se não for encontrado, é criado um dataset **novo**, com as configurações copiadas do modelo
   e com um nome do tipo «Personagem 768×512 PNG». Ele se torna o atual de imediato.
3. Os quadros do dataset de origem são **comprimidos** para ele. A regra da compressão: a
   proporção não muda e **não se pode cortar a imagem** — ela é reduzida pelo lado maior, é
   colocada no centro do quadro, e as bordas restantes são preenchidas com a cor da configuração
   «Configurações → Quadro do dataset de LoRA → Cor das bordas» («#RRGGBBAA», padrão `#FFFFFF00`
   — branco totalmente transparente: em PNG as bordas ficam transparentes e, em JPEG, onde não
   há alfa, ficam brancas). O sistema não vai ampliar um original pequeno.
4. Depois disso o treinamento é disparado pelo novo dataset; sobre a troca de dataset não se
   pergunta uma segunda vez — essa foi justamente a sua resposta.

Os quadros que não puderam ser comprimidos (o arquivo não existe, não couberam no limite de
kilobytes) são nomeados em uma mensagem à parte: o dataset com os demais quadros é montado assim
mesmo.

### O treinamento corre como tarefa: modelo, console e log

Treinar não é trabalho de um minuto, e no sistema isso tem a cara de uma tarefa comum: ela tem
executor, console, log, histórico e resultado. O executor dela é do terceiro tipo — **«software
automático»** (veja [Executores](performers.md)): o par «plugin treinador + operação de
treinamento».

O que acontece ao apertar «Treinar», por ordem:

1. **As perguntas sobre o dataset** — as descritas acima: elas são sobre «com o que treinar» e
   vêm primeiro.
2. **O sistema olha com o que e por qual modelo dá para disparar**: numa única consulta ele
   descobre se há plugin treinador, se há executor «software automático» e se há modelos de
   tarefa adequados. Da resposta depende o que você verá:

| O que falta | O que o formulário oferece |
|---|---|
| não há treinador nenhum | recusa com palavras: instale o programa em «Configurações → Plugins e MCP» |
| o plugin existe, mas não está pronto | abrir a janela de instalação do plugin e trazer o programa |
| não há executor | criá-lo a partir do registro de «Plugins e MCP» — com um botão só |
| não há modelo de tarefa | criar um nó de modelo com esse executor — com um botão só |
| está tudo no lugar | a janela de escolha do modelo e três respostas |

3. **A janela de escolha**: o modelo de tarefa e uma de três respostas — **«criar e disparar»**,
   **«somente criar»** ou **«cancelar»**. «Somente criar» é preciso quando o treinamento deve
   correr à noite ou depois de outro trabalho: a tarefa é criada e espera o disparo comum.
4. **O título, a descrição, o executor e as etiquetas você não escreve em lugar nenhum** —
   quem os traz é o nó de modelo, junto com a experiência dele. Corrigem-se no modelo, uma vez
   para todos os treinamentos.
5. O plugin treinador o sistema procura **pelos pacotes de treinamento** nomeados pelo modelo —
   pela mesma regra com que o registro do plugin é criado na instalação do modelo. A operação
   ele escolhe sozinho: a marcada como «uma instância» (no treinador é justamente o
   treinamento — a placa de vídeo é uma só).

Essa tarefa não precisa de campos próprios de parâmetros: a linha de treinamento lembra a sua
tarefa, e pela tarefa se encontra a linha — e nela já estão o objeto, o dataset, o modelo e
toda a configuração do treinamento.

**Onde ver como a coisa vai.** No cartão da tarefa: o console mostra a saída do treinador ao
vivo e o log do trabalho a guarda inteira. O erro depois disso fica em três lugares ao mesmo
tempo — no console, no artefato do trabalho e na dica sobre a palavra «erro» na linha de
treinamento.

**Os guardas contra «duas vezes a mesma coisa» são dois, e são de coisas diferentes**: a uma
linha que já está treinando o botão recusa na hora, ao passo que uma segunda tarefa sobre a
mesma operação do treinador **espera** na fila enquanto a placa de vídeo está ocupada.

### O que o sistema faz enquanto a linha está «treinando»

1. **Monta o dataset** no diretório indicado no perfil do modelo (no Kandinsky é
   `lora/<código do objeto>/dataset` a partir da pasta do projeto). O diretório é **limpo** antes
   da montagem: senão um quadro retirado continuaria participando do treinamento. As imagens são
   copiadas como `001.<ext>`, `002.<ext>`…, e as legendas ficam ao lado, em `.txt` de mesmo nome.
   São usados os quadros **do dataset pelo qual o disparo foi feito**; no comando também se pode
   inserir o código dele — `{datasetId}`.
2. **Verifica os pacotes de treinamento** nomeados pelo modelo (`lora.train.packages`) — no
   Kandinsky são `musubi-tuner` e `python`. O treinamento não se encarrega de instalá-los: eles
   chegam com a instalação do modelo, que tem para isso uma janela com o andamento do trabalho.
   Se algo faltar, a recusa vem de imediato, no momento em que o botão é apertado, e nela está
   escrito o que abrir e o que apertar.
3. **Escreve os arquivos de configuração** do treinador (`lora.train.files`) ao lado do dataset —
   no Kandinsky são o `dataset.toml` e o `train.cmd`. Eles são reescritos a cada disparo.
4. **Executa** o comando de treinamento do perfil do modelo (em um processo oculto à parte) ou
   aciona o serviço de treinamento por HTTP. No comando e nos arquivos de configuração são
   inseridos: `{object}` — o código do objeto, `{name}` — o nome dele, `{dataset}` — o diretório
   do dataset, `{output}` — o diretório do resultado, `{steps}` — o número de passos,
   `{width}`/`{height}` — o tamanho do quadro conforme a configuração do dataset, e ainda os
   caminhos da instalação do modelo: `{modelsRepo}`, `{groupDir}`, `{model:<arquivo>}` — o
   arquivo de pesos, `{package:<código>:<arquivo>}` — o arquivo de um pacote instalado. **A
   descrição do objeto não é inserida** — todo o texto que o treinamento vê está nas legendas dos
   quadros.
5. **Espera** o fim do trabalho. Além do prazo definido no perfil (por padrão 720 minutos, ou
   seja, 12 horas) não esperamos: um treinamento travado precisa em algum momento virar erro, e
   não ficar para sempre no estado «treinando». Um código de retorno diferente de zero também é
   erro, e as últimas linhas da saída entram no texto dele.
6. **Recolhe o arquivo** do adaptador e o coloca no repositório de modelos — no lugar de onde o
   próprio motor o pega (no ComfyUI é o subdiretório `loras`, com o nome padrão
   `<código do objeto>.safetensors`).
7. **Preenche no objeto** o caminho desse arquivo e o estado «pronto» — ou seja, a partir desse
   momento o adaptador é acoplado ao modelo sozinho.

Os passos 4–7 correm **dentro do trabalho da tarefa**: a saída do treinador é lida sem parar e
cai no console do cartão em vez de acumular no cano. Os limites de espera são dois — o geral
(720 minutos por padrão) e o **tempo limite de silêncio** (30 minutos por padrão: é quanto pode
levar a leitura dos pesos de um codificador de texto). Um programa que não disse uma linha por
mais tempo que isso é considerado travado e é encerrado — doze horas de silêncio não acontecem
mais com nenhuma configuração.

---

## 6. Que campos vão para onde

| Campo | Onde é preenchido | Para onde vai |
|---|---|---|
| «Descrição» do adaptador (o mesmo «Passaporte») | formulário do editor, formulário do objeto | para o **prompt da geração**, no lugar do link `@obj:` — para o treinamento NÃO vai |
| «Descrição do quadro» | formulário do quadro do dataset | para o **treinamento**: o arquivo `NNN.txt` ao lado da imagem (ou o `captions.json` comum) |
| «Nome» do objeto-adaptador | formulário do objeto | a substituição `{name}` no comando de treinamento; o link `@obj:[Nome]` |
| Código do objeto (OBJ-N) | atribuído sozinho | a substituição `{object}`: o diretório do dataset, o nome do arquivo do adaptador |
| «Nome» do quadro | formulário do quadro | o nome do arquivo na pasta do projeto (no acréscimo) e o nome do objeto-quadro |
| «Arquivo do adaptador» e «Estado do adaptador» | formulário do objeto | o acoplamento do adaptador ao modelo na geração; depois de um treinamento bem-sucedido são preenchidos sozinhos |

---

## 7. Como o adaptador treinado chega à geração

Não existe uma ação «acoplar o adaptador» à parte. Vale a regra do link: coloque na descrição de
uma tarefa de mídia um link para o objeto (`@obj:OBJ-7` ou `@obj:[Nome]`) e, antes de disparar o
trabalho, o sistema decide o que vai para o modelo:

* um link para um objeto **do tipo «adaptador LoRA»** — o adaptador é **obrigatório**: o modelo
  tem de aceitá-lo, e ele próprio tem de estar treinado. Caso contrário o trabalho falha com
  erro;
* um link para **qualquer objeto com adaptador pronto** (o personagem tem o «Arquivo do
  adaptador» preenchido) — o adaptador é acoplado: foi para isso que a pessoa o treinou;
* um adaptador **em andamento** («planejado», «treinando») — apenas uma observação no console do
  trabalho: o quadro é feito pelo passaporte;
* personagem, locação e estilo sem adaptador — como antes: o passaporte vai para o prompt, e os
  quadros de referência dos filhos são candidatos a quadro inicial.

Isso foi feito como erro de propósito: um quadro gerado em silêncio sem adaptador parece normal
e segue como resultado pronto — mas o personagem dele é outro.

---

## 8. Erros frequentes

| Mensagem | O que aconteceu e o que fazer |
|---|---|
| «O projeto não tem pasta neste servidor» | o projeto não tem pasta definida **no servidor em que o treinamento corre**; a pasta é por servidor e se define no cartão do projeto |
| «O catálogo de modelos não diz com o que treinar» | o comando de treinamento está vazio no perfil do modelo — escreva-o (o link para a instrução está no perfil) |
| «O adaptador para o modelo … é treinado fora do sistema» | neste registro o treinamento é externo: treine com meios próprios e informe o caminho do arquivo no campo «Arquivo do adaptador» |
| «O dataset tem N quadros, e o treinamento exige no mínimo M» | acrescente quadros: nos registros distribuídos do Kandinsky o limite inferior é 10 |
| «O arquivo do quadro não está na pasta do projeto» | o arquivo do quadro foi excluído ou renomeado por fora do AI2P — exclua o quadro e acrescente-o de novo |
| «O quadro pesa N KB / o quadro tem N×M pontos» | os limites deste dataset são menores do que o enviado; reduza a moldura ou aumente os limites em «Controle das imagens do dataset» |
| «Dataset não encontrado ou pertence a outro objeto» | o dataset selecionado foi excluído ou é de outro objeto: selecione o dataset de novo |
| «Da última vez o treinamento foi pelo dataset …» | o dataset mudou desde o treinamento anterior — responda por qual disparar |
| «As configurações do dataset são maiores do que as declaradas pelo modelo» | a conferência com o catálogo encontrou um excesso ou um formato estranho: treinar assim mesmo, apertar «Criar/mudar para o dataset adequado» ou copiar as configurações do modelo e remontar os quadros à mão |
| «Um quadro do dataset só pode ser uma imagem» | no caminho indicado não há uma imagem |
| «O treinamento não terminou em N min e foi interrompido» | o prazo do perfil não bastou — aumente o `wait.timeoutMinutes` ou reduza o número de passos |
| «O treinamento terminou com o código N» | o próprio programa de treinamento falhou; as últimas linhas dele estão na dica sobre a palavra «erro» |
| «O treinamento não deixou arquivo de adaptador» | o comando rodou, mas não há arquivo no caminho `result.path`: confira o caminho do perfil com o lugar em que o seu script coloca o resultado |
| «O treinamento não imprimiu uma linha por N min» | disparou o tempo limite de silêncio: o programa travou. Veja as últimas linhas no console da tarefa e no log do trabalho; o prazo é definido em `wait.idleMinutes` no perfil do modelo |

---

## 9. O que o editor não faz

A lista honesta das limitações desta versão — para não procurar o que não existe:

* **a ordem dos quadros não muda** — apenas por exclusão e novo acréscimo;
* **não se pode recortar de novo um quadro acrescentado** — a moldura de recorte só está
  disponível no acréscimo;
* **não se pode desligar um quadro pelo editor** — a marca «Ativo» vive no formulário do
  objeto-quadro, e um quadro desligado não recebe marca nenhuma na lista do editor (e mesmo
  assim ele não vai para o treinamento);
* **o envio do dataset ao provedor** (treinamento na nuvem com um pacote de quadros) não foi
  feito: o treinamento corre como processo próprio ou por HTTP até o seu próprio serviço de
  treinamento;
* **os quadros não são movidos entre datasets por um botão** — o dataset é um objeto comum, e o
  quadro é movido arrastando na visualização «hierarquia» da guia «Objetos»;
* **mudar as configurações de controle NÃO refaz os quadros já acrescentados** — os novos
  limites valem para o próximo quadro;
* **não há progresso do treinamento em percentual** — há o estado da linha, o texto do erro e a
  saída ao vivo do próprio programa de treinamento no console do cartão da tarefa (o log do
  trabalho inteiro está lá também);
* **vários adaptadores de uma vez** o modelo em geral não aceita (`apply.maxCount`, nos registros
  distribuídos é 1): se na descrição da tarefa forem nomeados mais objetos com adaptadores, o
  trabalho responde com erro em vez de pegar o primeiro em silêncio.

---

## Veja também

* [Vídeo a partir do quadro de referência de um personagem](sample_video1.md) — por onde começa o
  trabalho com um modelo de mídia: instalação do modelo local, executor de IA, objeto-personagem
  e o link `@obj:`.
* [Projetos](progects.md) — os objetos do projeto, o passaporte do objeto e a pasta do projeto.
* [Índice da seção](README.md) — os demais capítulos do manual.

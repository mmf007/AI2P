# Plugins e MCP

A aba **«Configurações → Plugins e MCP»** é o lugar onde tudo o que é externo se liga ao AI2P: os
programas instalados no seu computador (editores de vídeo, o conversor ffmpeg) e os servidores
MCP, que trazem ferramentas próprias.

O sentido da seção em uma frase: **o agente de IA ganha ações novas, e sobre essas ações são
concedidas permissões**. Um plugin não é uma «extensão» que pode fazer o que quiser: tudo o que
ele traz passa pelo catálogo de ações e pelas regras de segurança da tarefa.

## De que um plugin é feito

O registro de um plugin são quatro coisas ao mesmo tempo:

| Parte | Onde vive | O que dá |
|---|---|---|
| **Ações** | o catálogo de ações, as regras de segurança | ferramentas novas para o agente de IA |
| **Registros de experiência** | a experiência geral da organização | o agente sabe como usar essas ferramentas |
| **Programa** | o catálogo de pacotes e o `config.json` deste servidor | o programa externo que o plugin chama |
| **Documento** | `doc/<idioma>/plugins/<código>.md` | a descrição, o botão **«i»** na linha da lista |

As duas primeiras partes são **propriedade da organização**: ficam no banco de dados e são
replicadas para todos os servidores. A terceira é **propriedade deste computador**: o programa
encontrado e o seu caminho ficam no `config.json` do servidor e nunca são replicados. No vizinho
do cluster o mesmo programa está em outro caminho, e metade dos servidores não o tem de jeito
nenhum.

A descrição do próprio plugin chega como **arquivo de manifesto**,
`plugins/<código>/plugin.json` no diretório de dados. O manifesto lista as ações, nomeia o
programa necessário e declara os ajustes do registro; ele não é editado à mão na seção — as ações
e os seus parâmetros mudam apenas junto com o manifesto.

A distribuição traz seis registros: gateways para **Shotcut / Kdenlive**, **DaVinci Resolve**,
**Blender VSE** e **OpenShot**, o **conversor de vídeo (ffmpeg)** e o **treinador de
adaptadores LoRA (musubi-tuner)**.

## Dois tipos de registro: gateway e conexão MCP

| | **Gateway** (`gateway`) | **Conexão MCP** (`mcp`) |
|---|---|---|
| O que há do outro lado | um programa deste computador | um servidor MCP (processo próprio ou endereço de rede) |
| De onde vem a lista de ações | do manifesto, ela é fixa | **é obtida do próprio servidor**, a lista é dinâmica |
| O que é preciso para funcionar | encontrar ou instalar o programa | ligar-se e obter a lista de ferramentas |
| Segredo | não é preciso | o token do servidor MCP, se ele exigir |
| Onde o trabalho é calculado | do nosso lado: escrevemos um arquivo e chamamos o programa | do outro lado |

A diferença que vale a pena guardar: num gateway o conjunto de possibilidades é conhecido de
antemão e muda só com uma versão nova do AI2P, ao passo que **num servidor MCP ele pode mudar
qualquer dia**. Por isso a lista de ferramentas do MCP é obtida por uma **ação explícita da
pessoa** — o botão «Atualizar a lista de ferramentas» — e essa ação mostra o que foi acrescentado
e o que desapareceu. Um servidor MCP não consegue conceder a si mesmo, em silêncio, uma
possibilidade nova.

## Estados neste servidor

| Estado | O que significa |
|---|---|
| **declarado** | o manifesto está no disco, ainda não há registro da organização — o plugin não foi inicializado |
| **procurando o software** | o registro existe, mas o programa necessário não foi encontrado neste servidor e nenhum caminho foi informado |
| **inicializado** | está tudo no lugar: as ações registradas, a experiência posta, o programa encontrado — as ferramentas são publicadas ao agente |
| **desativado** | a pessoa desligou o plugin por um tempo; as ferramentas não são publicadas, os registros ficam |
| **retirado** | as ações e os registros de experiência foram excluídos em toda a organização |

O estado é **próprio de cada servidor**: no computador em que o Blender está instalado o plugin
aparece inicializado, e no vizinho, «procurando o software». É nisso que consiste o trabalho
distribuído: um filma, outro monta.

Além disso, pode haver uma linha **sem manifesto**: o registro chegou por replicação enquanto o
arquivo `plugins/<código>/plugin.json` não está neste computador. Essa linha é mostrada de
propósito — senão não haveria como saber aqui de um plugin que funciona no vizinho.

## Como inicializar

1. Abra **«Configurações → Plugins e MCP»**. A lista é montada com os manifestos do disco deste
   servidor e com os registros da organização.
2. Clique em **«Inicializar»** na linha do plugin. Cria-se o registro da organização, cada ação
   recebe um registro no catálogo de ações e os registros de experiência do plugin vão para a
   experiência geral da organização. Num registro do tipo MCP, a primeira coisa que se faz é
   obter a lista de ferramentas: ao agente só se pode publicar aquilo que tem registro no
   catálogo.
3. Dê ao plugin o seu programa, de uma destas três maneiras:
   * o plugin **o encontrou sozinho** no `PATH` e verificou a versão — não há nada a fazer;
   * **«Instalar»**, se o plugin tiver pacote (é assim que se instala o Shotcut portátil e é
     assim que se instala o ffmpeg);
   * **«Indicar onde o programa já está instalado»**: o caminho à mão; serve tanto o arquivo do
     programa quanto a pasta em que ele foi instalado. É assim que se liga o DaVinci Resolve: a
     sua distribuição é entregue atrás de um formulário de registro, e não podemos baixá-la por
     você.
4. O botão **«Verificar»** roda a busca de novo e mostra o que foi encontrado.

Inicializar é uma **ação do administrador do servidor**. O leitor vê a seção, mas não tem botões
nela: dar entrada em ações é conceder permissões, não ajustar a aparência.

É preciso fazer isso **em cada servidor onde o plugin deva funcionar**: o registro da organização
chega sozinho ao vizinho por replicação, o programa e o seu caminho não.

## «Programa não encontrado neste servidor»

Essa linha não é um erro. Ela significa exatamente o que diz: o programa não está neste
computador e nenhum caminho para ele foi informado. Enquanto for assim, **as ações do plugin não
são publicadas ao agente** — uma ferramenta que já se sabe que vai responder com uma recusa
gastaria o turno do agente à toa.

O que fazer:

* instalar o programa com o botão **«Instalar»**, se houver pacote;
* instalá-lo você mesmo e mostrar o caminho com **«Indicar onde o programa já está instalado»**;
* não fazer nada, se não se monta neste computador: deixe a tarefa ir para onde o plugin está
  inicializado.

A verificação da versão faz parte da busca: um programa mais antigo que a versão mínima aceitável
conta como não encontrado. Não é implicância, é proteção contra uma recusa meia hora depois do
começo: o conjunto de chaves do ffmpeg mudou entre as versões 4.x e 7.x, e o do `melt`, entre a
sexta e a sétima.

## O MCP liga-se apenas através do nosso registro

A regra é simples e dura: **um servidor MCP liga-se por um registro de plugin e de nenhuma outra
forma.**

Um servidor MCP escrito diretamente na configuração do agente CLI (por fora do AI2P) passa ao
lado de toda a nossa segurança: as suas ferramentas não estão nem no catálogo de ações, nem nas
regras da tarefa, nem no log. O agente as usa e você nem fica sabendo nem consegue proibi-las.
Por isso essa ligação não é «mais um jeito de configurar»: é um buraco.

Como é feito do nosso lado:

* as ferramentas do servidor MCP são obtidas como lista e cada uma recebe um **registro no
  catálogo de ações** — a partir desse momento ela pode ser permitida ou proibida por uma regra
  de segurança normal da tarefa ou do projeto;
* **nas ferramentas de plugin a política é a inversa**: uma ferramenta sem registro no catálogo é
  **proibida**. Nas ferramentas comuns do sistema a regra é a outra (sem registro não há
  proibição), e é exatamente por isso que para os plugins ela é invertida: um servidor MCP que
  acrescentasse uma ferramenta numa versão nova a passaria em silêncio por cima das regras;
* a ferramenta que sumiu da lista tem o seu registro do catálogo retirado — ela não é publicada
  nem é permitida;
* o **token** do servidor MCP é guardado no subdiretório `secrets/` deste servidor, não é
  replicado e nunca vai para o `config.json` nem para o log;
* a **lista de ferramentas é própria de cada servidor** (fica no `config.json`): um servidor MCP
  é iniciado onde o seu programa está instalado, e o vizinho pode ter outra versão. O que se
  replica não é a lista, e sim a sua consequência: os registros do catálogo de ações.

## Execução longa: um programa como executor da tarefa

As ações de um plugin são ferramentas do agente de IA: o agente chama a ação e recebe a
resposta em segundos. O treinamento de um adaptador LoRA não funciona assim — ele leva horas e
ocupa a placa de vídeo inteira. Por isso o plugin tem um segundo tipo de capacidade: as
**operações de execução longa** (o bloco `run` do manifesto). Quem as chama não é o agente, e
sim uma **tarefa** — com um executor do terceiro tipo, o **«software automático»** (veja
[Executores](performers.md)).

O que essa operação tem além de uma ação comum:

* uma **pasta de trabalho** — se relativa, é contada a partir da pasta do projeto e não é
  deixada sair dela;
* **dois limites em vez de um**: o tempo limite de **silêncio**, separado do limite geral de
  duração. Enquanto o programa imprime, ele está vivo; o que travou de vez deve virar erro em
  meia hora, e não em doze horas;
* uma **regra de leitura do resultado** — o que foi indicado como saída, um arquivo pelo
  caminho do manifesto ou «o arquivo mais recente da pasta pela máscara». O terceiro existe
  exatamente para os treinadores: o nome do arquivo da época
  (`epoch-0007.safetensors`) não dá para nomear de antemão;
* a marca **«Uma instância»** — uma coluna do formulário do plugin. Enquanto essa operação está
  ocupada, a segunda tarefa com ela **espera** na fila. Isso não é «mais devagar»: dois
  treinamentos numa mesma placa de vídeo são uma recusa por memória em um minuto.

**O que se vê enquanto o programa calcula.** A saída dele é lida sem parar e vai de uma vez
para dois lugares: para o **console do cartão da tarefa** — ao vivo, linha por linha — e para o
**log do trabalho** — para sempre. O log pode ser visto sem esperar o fim do trabalho. Parar é
o botão comum de parar a tarefa, e com ele toda a árvore de processos é encerrada. Um código de
retorno diferente de zero, um silêncio maior que o limite e o tempo total esgotado viram
igualmente um erro do trabalho, e as últimas linhas da saída ficam no texto dele — sem elas
«caiu» não tem conserto.

A operação não tem linha de comando própria, assim como a ação: os argumentos estão escritos no
manifesto, e a tarefa substitui neles apenas os ajustes declarados do registro e caminhos
dentro da pasta do projeto.

**O registro do plugin não é criado só à mão.** A instalação de um modelo que declara pacotes
de treinamento LoRA cria por si mesma o registro do **plugin treinador** que leva a todos esses
pacotes e anota o caminho encontrado do programa — mas somente **num campo vazio**: um caminho
indicado pela pessoa não é sobrescrito por uma reinstalação. O registro aparece no estado
**«declarado»** — as ações do catálogo e os registros de experiência continuam sendo criados
pelo botão «Inicializar»: publicar ferramentas ao agente é decisão da pessoa, não consequência
de uma instalação.

## A regra de segurança «Plugins e MCP»

As regras de segurança têm um tipo à parte para esta seção — **acesso aos plugins e ao MCP**.
Uma regra comum responde à pergunta «o que exatamente o agente pode fazer» (o código da ação), e
esta responde a «**quais plugins podem sequer ser usados**».

* O **padrão** é o código do plugin (`trainer.musubi`) ou o código com a operação
  (`trainer.musubi:lora.train`). Máscaras e expressões regulares funcionam como nos demais
  tipos de regra.
* São **duas operações, e ao menos uma precisa ser marcada**: «**usar**» — o agente chama uma
  ferramenta do plugin; «**executar**» — uma tarefa de «software automático» dispara o
  programa. A segunda é verificada **antes** do disparo do programa e cobre todos os caminhos de
  uma vez: o agente, a pessoa, a fila da hierarquia e o disparo adiado.
* **Por padrão tudo é permitido.** Não há regras desse tipo — logo, pode; a proibição é criada
  explicitamente. A regra é posta em qualquer um dos três níveis (organização, projeto, tarefa)
  e, na sobreposição, vence a mais estrita.
* «**Perguntar**» no disparo de um programa significa **recusa**: o disparo vem da fila, e ali
  não há a quem perguntar.

Essa regra é criada nos mesmos formulários que as demais: Configurações → Segurança (a
organização inteira), cartão do projeto → «Segurança», cartão da tarefa → «Segurança».

## Desativar, ativar, remover

* **«Desativar»**, temporariamente: as ferramentas deixam de ser publicadas ao agente, e os
  registros do catálogo e de experiência ficam onde estão. Para voltar, **«Ativar»**.
* **«Remover»**, de vez: as ações e os registros de experiência do plugin são excluídos **em toda
  a organização**. Os ajustes do registro e o caminho para o programa permanecem, para que uma
  segunda inicialização não comece do zero.

Os registros de experiência que o plugin colocou na experiência geral da organização são vistos e
editados na aba **«Configurações → Experiência geral»**; o botão «Abrir a experiência geral» do
formulário do plugin leva ao mesmo lugar.

## Capítulos vizinhos

* [Configuração](config.md) — a tela de configurações inteira e o que há nas suas outras abas.
* [Tarefas](tasks.md) — as regras de segurança da tarefa, que são o que permite e o que proíbe
  ações.
* [Objetos do projeto](objects.md) — a biblioteca de mídia do projeto: é dela que os gateways
  montam a linha do tempo.
* A seção [Plugins e MCP](../plugins/README.md) — um documento por plugin: o que ele sabe fazer,
  o que precisa para funcionar e as limitações por sistema operacional.

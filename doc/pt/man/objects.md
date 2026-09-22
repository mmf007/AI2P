# Objetos do projeto

**O objeto é aquilo a que a tarefa se refere:** o personagem do vídeo, a locação, o adereço, o
estilo, o quadro de referência, o adaptador LoRA. Cada projeto tem a sua própria lista de
objetos; não existe de propósito uma lista de objetos comum à organização — o objeto pertence ao
projeto.

A lista é aberta pela guia **«Objetos»** do cartão do projeto. Um clique na linha abre a **guia
«Objeto»** — um cartão com guias, igual ao da tarefa.

---

## Para que servem

Fica mais fácil de explicar com o personagem fixo de um vídeo. Se a aparência do herói for
descrita com palavras próprias em cada quadro, ele «derrete» de um quadro para outro — **a
paráfrase é justamente a causa**.

Por isso o objeto tem um **perfil descritivo** — o texto literal para o prompt — e arquivos de
referência, e na descrição da tarefa entra um **link `@obj:OBJ-3`**. A **cada** início do
trabalho o link é expandido no perfil descritivo e nos caminhos dos arquivos: corrigiu o perfil,
o próximo quadro já leva a correção em conta, sem precisar reescrever as tarefas.

Três regras sobre o link que economizam tempo:

* são lidas **as duas** formas — `@obj:OBJ-3` e `@obj:[Herói Vasya]`; qual delas o botão da
  interface coloca na área de transferência é definido pela configuração do projeto «formato do
  link do objeto»;
* um link desconhecido permanece no texto como está, e um link **não alcança um objeto de outro
  projeto**;
* um link dentro de um perfil descritivo não é expandido — não há aninhamento.

## Tipos de objeto

| Tipo | Para que serve |
|---|---|
| **personagem** | o herói do vídeo; a aparência dele está no perfil descritivo e nos quadros de referência |
| **locação** | o lugar da ação |
| **adereço** | um item no quadro |
| **estilo** | a maneira de desenhar, as restrições do acabamento |
| **quadro de referência** | uma imagem-modelo; normalmente filho de um personagem ou de uma locação |
| **gravação de referência** | uma amostra de voz ou de som: o conector passa o arquivo ao modelo do mesmo modo que um fotograma de referência |
| **conjunto de dados** | pasta de quadros com legendas para treinar o adaptador; os quadros são filhos dele |
| **adaptador LoRA** | um acréscimo treinado aos pesos do modelo ([Editor de LoRA](LoRAEditor.md)) |
| **arquivo** | um arquivo a que as tarefas se referem |
| **equipamento** | o hardware ligado ao trabalho |
| **recurso de mídia** | uma tomada gravada, uma faixa de áudio, uma legenda, um arquivo de projeto de edição: a mediateca do projeto é uma seleção de objetos deste tipo |

## A lista de objetos

**Quatro visualizações**: tabela (com quadradinhos de prévia), tabela resumida, hierarquia e
etiquetas; a escolhida é memorizada por projeto. Há filtros por tipos e por tags, e busca.

* **a hierarquia usa o mesmo motor da árvore de tarefas**, por isso nela funcionam o arrastar com
  o mouse e com o dedo, a rolagem automática nas bordas e a faixa fixa **«Para a raiz»**;
* um objeto **sem imagem própria** (personagem, locação, estilo) toma a prévia dos filhos, em
  profundidade — a aparência está nos quadros de referência;
* o filtro seleciona **linhas**, e não o que a linha mostra: filtrar «somente personagens» não
  tira o rosto do personagem;
* no fim da linha ficam os botões «editar» (lápis) e «excluir»: editar a partir da lista continua
  sendo um único movimento, embora o clique na linha abra a guia;
* **os objetos têm tags próprias**, um grupo separado das tags das tarefas.

## A guia do objeto

O objeto é **lido em uma guia**, e é criado e editado no formulário de janela.

* **O cabeçalho** é como o do cartão da tarefa: a placa com o nome do projeto, o código `OBJ-N` e
  o nome; abaixo — o tipo do objeto, a marca «inativo» e o estado do adaptador.
* **O título da guia** é «Objeto» mais o código ou o começo do nome; é calculado pela **mesma**
  configuração da tarefa e do projeto (Configurações → Principal, uma configuração para todos).
* **A barra de ferramentas**, em ícones: editar (abre o formulário de janela), trocar de
  servidor, link, configuração do LoRA (somente no adaptador), «o que irá para o modelo»,
  excluir, atualizar. Cada um é a mesma ação do botão de dentro do formulário.
* **São duas guias**: «Principal» (os mesmos campos, somente leitura, o perfil descritivo
  renderizado em Markdown) e **«Subobjetos»** — a lista dos filhos com prévias, os botões
  «editar» e «excluir» no fim da linha, um clique abre uma guia nova, e o botão «adicionar» cria
  um objeto já dentro do aberto.

## O formulário do objeto

Nome, tipo, pai («faz parte do objeto»), tags, arquivo de referência, perfil descritivo, «ativo»
e, no objeto do tipo «adaptador LoRA», a seção do adaptador e o botão **«Configuração do LoRA»**
([Editor de LoRA](LoRAEditor.md)).

O botão do diálogo de arquivos anda **somente dentro da pasta do projeto** e devolve o caminho
relativo a ela. O nome do objeto é único dentro do projeto.

### Dois links — e eles são coisas diferentes

O formulário fornece **dois** links em dois campos:

* **`@obj:OBJ-3`** — para inserir na descrição da tarefa; é expandido no perfil descritivo ao
  iniciar o trabalho;
* **o endereço da guia** `…/object/{id}` — para links externos. O objeto é endereçado pelo uuid
  dele, por isso esse link funciona **entre projetos** e não muda o projeto atual. Os endereços,
  como na tarefa, são dois — o local e o externo.

### «O que irá para o modelo»

O botão mostra a expansão inteira — exatamente o que a geração vai receber. No **adaptador LoRA**
os dados são dois, e diferentes, por isso a janela tem dois campos:

* **«Ao usar»** — o perfil descritivo com os caminhos dos arquivos; é isso que vai para o prompt;
* **«Ao treinar»** — os quadros do conjunto de dados atual com as legendas deles, exatamente o
  que o treinador vai receber. Um quadro sem legenda aparece como uma linha vazia — antes de o
  cálculo levar horas.

Um objeto comum não tem o segundo campo.

## O servidor dono do objeto

Em um cluster o objeto, assim como a tarefa, tem **exatamente um servidor dono**: nele ele é
editado e nele é treinado o adaptador LoRA dele. No cartão há a placa «Objeto do servidor …» e na
barra de ferramentas o botão **«Trocar de servidor»** (a placa e o botão só aparecem quando a
organização tem mais de um servidor).

* **em um servidor alheio o objeto é somente leitura**: ficam apagados a edição, a exclusão, a
  configuração do LoRA, a criação de subobjetos, os botões da linha da lista e a alça de arrastar
  da árvore;
* o objeto migra **com toda a sua subárvore** — subobjetos, conjuntos de dados e quadros: isso é
  o conteúdo do objeto;
* **o número não muda** com a troca de servidor, e um objeto novo o recebe com o código do
  servidor (`OBJ-5-S1`; no maestro não há sufixo) — caso contrário dois servidores criariam um
  `OBJ-5` cada um e o link `@obj:OBJ-5` apontaria para coisas diferentes;
* um objeto novo recebe o servidor sozinho: o filho, o servidor do pai; o de nível superior, este
  servidor;
* os objetos que chegam sem servidor de um parceiro de versão antiga são recolhidos pelo maestro
  ao abrir a organização.

**O adaptador treinado viaja para o vizinho.** Uma cópia do arquivo LoRA pronto é colocada na
pasta de dados da organização, e essa pasta é replicada inteira; no servidor que recebe, o
arquivo é encontrado sozinho e, antes da geração, é colocado no repositório de modelos daquele
servidor. Ou seja, «treinou em um servidor, usa em outro» funciona sem cópia manual. Os
adaptadores treinados antes da atualização não são movidos: a cópia aparece para os treinados
depois.

Mais sobre a propriedade das linhas e sobre a replicação — [Vários servidores](servers.md).

## Depois

* [Projetos](progects.md) — onde os objetos vivem e quais configurações do projeto os afetam.
* [Tarefas](tasks.md) — onde o link `@obj:` é colocado.
* [Editor de LoRA](LoRAEditor.md) — treinar um adaptador a partir de um objeto do projeto.
* [Vídeo a partir do quadro de referência de um personagem](sample_video1.md) — um exemplo
  completo com um objeto.

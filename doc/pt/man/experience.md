# Experiência

**A experiência é a memória do sistema sobre como o trabalho deve ser feito.** Uma lição curta
obtida por uma tarefa é inserida no texto do encargo de outras tarefas — e assim o executor
seguinte não tropeça na mesma pedra.

Registos de experiência são mantidos tanto por pessoas como por agentes de IA. A pessoa, nas listas
de experiência (aba «Experiência» do cartão do projeto, aba «Experiência» do nó de modelo,
**Configurações → Experiência geral**); o agente, com as ferramentas `create_experience` e
`update_experience` durante o trabalho.

---

## Os três âmbitos

Um registo tem exatamente um âmbito, e dele depende quem o recebe.

| Âmbito | Quem o recebe | Do que trata |
|---|---|---|
| **Regras gerais da organização** | todas as tarefas da organização, em qualquer projeto | como trabalhamos: processo, relatórios, disciplina de verificações |
| **Experiência do projeto** | tarefas deste projeto | este produto: estrutura, armadilhas, decisões, nomes |
| **Experiência do nó de modelo** | tarefas criadas a partir desse nó (e dos seus descendentes) | este passo do processo |

A regra de separação numa frase — o mesmo texto aparece no formulário do registo e na janela de
transferência:

> Uma regra geral trata de **como trabalhamos**: é verdadeira em qualquer projeto da organização e
> não nomeia nenhum ficheiro, nenhum código de tarefa, nenhum produto. A experiência **do projeto**
> trata deste produto. A experiência **do nó de modelo** trata deste passo do processo.

A regra não é decorativa: as regras gerais chegam a **todas** as tarefas da organização, e um
registo de projeto que ali entre por engano rouba espaço a todos os outros. Por isso, ao escrever na
experiência geral o texto é verificado por quatro indícios: **código de tarefa** (`T-241`,
`T-12-S0`), **caminho de ficheiro**, **extensão de código-fonte** e **nome de projeto**:

* ao agente de IA é **recusado**, e propõe-se criar o registo na experiência do projeto. Trocar o
  âmbito em silêncio confundiria o agente que depois procura o registo pelo identificador;
* à pessoa é mostrado um **aviso** no formulário e o botão **«Guardar mesmo assim»** — pode saber
  mais do que a verificação;
* registos que vieram com um plugin não passam pela verificação: a decisão já foi tomada pela pessoa
  que instalou o plugin.

A verificação aplica-se ao criar um registo geral, ao editá-lo e ao transferir algo **para** as
regras gerais.

### Revisão da experiência geral

O lixo acumulado não se limpa registo a registo. Acima da lista na aba
**Configurações → Experiência geral** há um bloco **«Revisão da experiência geral»**: indica «parece
experiência de projeto: N registos», aponta linha a linha **que indício o apanhou** e transfere em
bloco os registos marcados para o projeto escolhido. A transferência é feita um a um, por isso uma
recusa (registo de outro servidor, regra do distribuível) não cancela as restantes.

### Transferir um registo entre âmbitos

O botão **«Mover para outro âmbito»** na linha da lista abre uma janela: primeiro o âmbito, depois o
projeto ou o nó de modelo.

A transferência muda **apenas o vínculo**. O identificador, o texto, as etiquetas, a competência, a
autoria, a data de criação e as marcas «carregar sempre» e «ativo» permanecem: o registo não é
recriado e as referências ao seu identificador não se quebram.

O que não pode ser transferido:

* **um registo alheio**, criado por outro servidor do cluster: o lixo de outro servidor limpa-se lá;
* **uma regra fornecida com o distribuível**: cada servidor semeia-as por si, e a transferência daria
  uma segunda cópia da mesma linha. Uma regra fornecida que atrapalha **desativa-se**, não se
  transfere nem se apaga.

---

## O que tem um registo

* **Texto** — a lição. Uma só ideia, no imperativo, sem repetir o óbvio.
* **Competência** — a quem se dirige (`code-test`, `text-docs`, …). **Um registo sem competência e
  sem a marca «carregar sempre» nunca chega a um encargo** — exceto na experiência do nó de modelo,
  onde um registo sem competência continua a ser comum a todas as tarefas desse nó.
* **Etiquetas** — o tema. Desde a versão 1.135 as etiquetas já **não descartam** registos (ver
  abaixo): elas adiantam o registo na partilha do espaço.
* **«Carregar sempre»** — o registo entra no encargo independentemente das competências do executor.
  A marca é cara: taxa o prompt de **todas** as tarefas, por isso reserve-a para regras válidas para
  qualquer executor.
* **«Ativo»** — o interruptor do registo (ver «Atividade e arquivamento»).
* **Autor e servidor proprietário** — só o próprio servidor pode editar, transferir ou desativar o
  registo.

---

## Como um registo chega ao encargo

Ao compor o texto do encargo, o sistema imprime os blocos de experiência: primeiro as regras gerais
de trabalho, depois a experiência do projeto, depois a do nó de modelo. Não entra tudo — a seleção
tem três passos.

### Passo 1. Quem serve

1. o registo está **ativo**: um registo desativado nunca chega a um encargo, nem com a marca
   «carregar sempre»;
2. o registo tem a marca **«carregar sempre»**: é tomado sem mais condições;
3. caso contrário precisa de uma **competência**, e ela tem de coincidir com as do executor.

**As etiquetas já não participam aqui.** Antes um registo com etiquetas não coincidentes era
descartado antes de qualquer contagem; e como o agente marca os registos novos com as etiquetas da
tarefa, esse filtro acabaria por descartar justamente o necessário por uma discrepância formal de uma
palavra. Agora as etiquetas são um **sinal**: a coincidência acrescenta meio passo de relevância, a
falta de coincidência não retira nada.

### Passo 2. Experiência dos nós antecessores

Uma tarefa criada a partir de um nó de modelo recebe a experiência **não só do seu nó**, mas de todos
os nós acima, até à raiz. A experiência de ramos vizinhos do modelo continua a não ser tomada.

### Passo 3. O limite e as quotas

O volume de experiência num encargo é limitado por uma configuração do projeto (por omissão,
**100 000 caracteres**): caso contrário o bloco de experiência expulsaria da janela do modelo a
própria tarefa. O espaço é partilhado assim:

1. os registos **«carregar sempre»** são tomados primeiro — fora de concurso e fora das quotas;
2. o resto divide-se por **quotas de nível**: **50 %** à experiência dos nós de modelo, **30 %** à do
   projeto, **20 %** às regras gerais. Dentro de um nível manda a relevância: nó próprio → pai → avô
   → projeto → regras gerais, mais meio passo por coincidência de etiquetas;
3. **a quota não usada transborda** para os vizinhos: um nível vazio não rouba espaço.

As quotas existem para que um nível prolixo não coma o orçamento inteiro: sem elas a experiência do
projeto expulsaria tanto as regras gerais como a experiência mais precisa do nó.

O registo é inserido **inteiro**: meia lição lê-se como outra lição. A ordem de impressão é
cronológica, como na lista.

**O que isto não resolve.** Com o limite cheio, um registo fora do tema continua a não chegar: já não
é descartado por uma regra, é apenas ultrapassado por outros mais relevantes. Esse registo obtém-se
pela busca (abaixo).

Se algo não coube, o sistema di-lo no próprio encargo e nomeia ao executor a ferramenta de busca —
o que foi descartado não se perde, fica disponível a pedido.

---

## Experiência utilizada pela tarefa

Que registos o sistema **realmente** inseriu vê-se no cartão da tarefa, na aba **«Experiência
utilizada»**. A aba aparece só quando a lista não está vazia (numa tarefa nunca executada não há nada
a mostrar).

No topo está um **resumo**: quantos registos entraram no encargo e quantos caracteres são — em
número e em parte do limite de inserção de experiência (uma definição do projeto). Assim vê-se se o
limite está bem escolhido.

As colunas são as da aba «Experiência» (texto, âmbito, competência, etiquetas, atividade, data do
último uso) e à direita de cada linha há um botão **«Editar a entrada»**: aqui guarda-se uma ligação
para o registo, por isso a alteração vai para o próprio registo e não é preciso procurá-lo de novo
nas listas de experiência. Um registo apagado ou levado para o arquivo é mostrado como «o registo …
não está disponível» e não há nada a editar: o rasto de uso guarda apenas identificadores e
sobrevive ao próprio registo.

O outro lado do mesmo diário são as **estatísticas do registo**: quantas **tarefas** o receberam e
quando foi a última vez. Assim se vê que experiência funciona e qual apenas ocupa espaço no prompt.

---

## Busca na experiência

Acima dos filtros de cada lista de experiência há uma linha **«Buscar no texto»**. Ao contrário dos
filtros por competência, etiquetas e atividade, a busca também define a **ordem**: os resultados saem
por classificação, não por data. A classificação funde três sinais — coincidência lexical, novidade
do registo e número de etiquetas coincidentes com a tarefa —, mas **só a coincidência de palavras
encontra alguma coisa**: novidade e etiquetas apenas reordenam o que foi encontrado.

O agente de IA usa a mesma busca: a ferramenta `search_experience` (âmbito
`project` / `template` / `general` / `all`, por omissão só registos ativos) e o comando

```
ai2p experience-find "palavras da consulta" --scope project --limit 10
```

É precisamente pela busca que se obtém o que não coube no encargo por causa do limite.

### O que a busca não encontra — com franqueza

1. **O sentido.** «como montar o pacote» não encontrará um registo que diz «MakePackage» e «Inno
   Setup» se não contiver essas palavras.
2. **Sinónimos e abreviaturas.** «BD» ≠ «base de dados».
3. **Morfologia complexa.** As terminações são cortadas de forma tosca e os prefixos não são
   retirados («reindexar» ≠ «índice»).
4. **Gralhas.**

### Proteção contra duplicados

Quando o agente cria um registo novo, o sistema compara-o com os existentes **do mesmo âmbito**:

* com uma coincidência **muito forte** o registo não é criado: ao agente devolvem-se o identificador
  e o texto do encontrado, com a proposta de o corrigir;
* com uma coincidência **apreciável** o registo é criado mas recebe a etiqueta `similar:<id>`, uma
  marca para revisão humana.

A mesma ideia contada **por outras palavras** não é detetada pela comparação de palavras: é o limite
de qualquer comparação lexical.

---

## Atividade e arquivamento

Um registo pode ser **desativado** sem ser apagado. O registo desativado continua nas listas, vê-se e
pode ser recuperado, mas não entra nos encargos.

* O filtro **«Atividade»** nas listas: **Ativos / Inativos / Todos**, por omissão «Ativos».
* A coluna **«Ativo»** e o botão de alternância na linha — o mesmo direito de editar e apagar.
* O interruptor **«Ativo»** no formulário do registo.

Também se podem desativar as **regras fornecidas com o distribuível**: de outro modo não haveria como
retirá-las, pois a semeadura não devolve o que foi apagado, ao passo que o desativado volta com um
clique.

**O agente de IA não apaga experiência.** A análise apenas **desativa** registos
(`set_experience_active`), e depois o arquivamento leva o que está desativado. É deliberado: o
desativado sempre se pode restaurar, o apagado não. O agente não pode desativar as regras fornecidas:
são as regras pelas quais ele próprio trabalha.

### A regra de arquivamento «todos os inativos»

Nas regras de arquivamento, para o tipo de dados **«experiência»** funciona a seleção «apenas
inativos»: entram tanto os desativados como os apagados.

Junto dela existe um terceiro tipo de prazo: **«a idade não importa»**. O formulário anterior exigia
um prazo estritamente maior que zero, pelo que a regra «arquivar **todos** os registos inativos» não
podia ser escrita. Agora pode: escolha o tipo de dados «experiência», a seleção «apenas inativos» e o
prazo «a idade não importa»; o formulário esconde sozinho os campos numéricos e a lista de regras
mostra esse prazo como «qualquer».

Esta regra **não** entra nos valores por omissão de um arquivo novo de propósito: o sistema não deve
levar para o arquivo a experiência de ninguém sem autorização. Crie-a você mesmo se quiser uma
limpeza periódica.

Mais sobre arquivos, regras e arquivamento automático por agendamento — o capítulo
**[Arquivamento](Archives.md)**.

---

## Conjuntos de experiência: estilos de trabalho

**Um conjunto é um pacote pronto de registos de experiência, instalado e retirado com um botão.** É
assim que uma disciplina de trabalho («como revemos», «como escrevemos testes», «como fazemos
relatórios») vem de fábrica em vez de ser inventada de novo em cada organização.

Aba **Configurações → Conjuntos de experiência**.

### Instalação

1. Escolha o **âmbito de instalação**: regras gerais da organização, experiência do projeto ou nó de
   modelo (nos dois últimos casos, também o próprio projeto ou nó). O âmbito não é guardado no
   ficheiro do conjunto: é a sua escolha, e a interface não deixa omiti-la.
2. Carregue em **«Instalar»**. O sistema cria os registos e diz quantos são.
3. O botão **«i»** na linha abre o **documento do conjunto**: que estilo é, para quem e o que muda no
   trabalho dos agentes depois da instalação. Os documentos dos conjuntos fornecidos estão em
   `doc/<idioma>/packs/`.

### Retirada e reinstalação

O botão **«Retirar»** tira exatamente os registos desse conjunto: a instalação marca-os com a
etiqueta de serviço `pack:<código>`. Os seus próprios registos e as suas edições dos textos não são
tocados: um registo que editou continua a ser seu, e uma reinstalação **não o sobrescreve**.

### Exportar um conjunto próprio

O bloco **«Exportar registos para um conjunto»** faz um conjunto a partir dos **seus** registos:
selecione-os com o filtro e as caixas, indique código, nome e descrição, e obterá um ficheiro de
conjunto instalável noutra instalação. Os identificadores são preservados, por isso a instalação
inversa dá **as mesmas linhas**, não cópias.

### O que é preciso saber sobre o idioma

Os textos dos conjuntos fornecidos estão escritos nos cinco idiomas da interface, mas **um registo
vive como uma única cadeia**: na instalação é tomado o idioma do servidor. Por isso, num encargo
noutro idioma o título do bloco estará traduzido e os textos dos registos continuarão no idioma da
instalação. É assim que os conjuntos são feitos; não é um erro de tradução.

### O que o distribuível traz

| Código | Nome | De que trata |
|---|---|---|
| `style.strict-review` | Revisão de código rigorosa | primeiro o encargo, depois o diff; cada observação tem peso; o veredicto é obrigatório |
| `style.tdd` | Desenvolvimento guiado por testes | primeiro o teste vermelho, depois a alteração mínima |
| `style.research-report` | Investigação e relatório | disciplina de fontes, de números e da secção «o que não foi feito» |
| `style.experience-analysis` | Análise da experiência | não é um estilo, mas um **modelo de tarefas** para rever a experiência acumulada |

Os ficheiros dos conjuntos estão no diretório `packs/` do diretório de dados, junto a `plugins/` e
`models/`; os conjuntos fornecidos são colocados ali pelo próprio programa ao abrir a organização.

Panorama dos conjuntos e dos seus documentos — **[Conjuntos de experiência: estilos de
trabalho](../packs/README.md)**.

---

## O modelo «Análise da experiência»

A experiência acumula-se mais depressa do que envelhece: os registos duplicam-se, ficam obsoletos,
vivem no âmbito errado. Ninguém vai arrumar isso à mão, por isso a revisão é colocada como **uma
tarefa para um agente de IA**, por agendamento.

O conjunto `style.experience-analysis` traz consigo **nós de modelo de tarefas**:

* **«Análise da experiência»** — nó raiz com a instrução completa: o que ler (estatísticas de uso →
  listagem dos registos por páginas → busca quando necessário), o que fazer (generalizar, dividir,
  etiquetar, transferir, desativar por estatística), o que **não** fazer (não apagar, não tocar em
  registos alheios, não desativar regras fornecidas, não reescrever o sentido ao generalizar) e o que
  escrever no relatório: identificadores e os números «havia / ficaram ativos»;
* **«Análise da experiência geral da organização»** — nó filho para as regras gerais;
* **«Análise da experiência do projeto»** — nó filho para um projeto; copie-o tantas vezes quantos
  projetos tiver.

A regra de desativação por estatística está escrita **por palavras** no encargo do nó raiz, não
fixada no código: um registo é desativado se não chegou a nenhum encargo durante três meses **tendo**
havido tarefas do seu tema nesse período. «Não chegou porque não houve tais tarefas» não é motivo
para desativar. Ajuste essa redação no próprio modelo ao seu processo.

### Como colocar a revisão no agendamento

1. **Configurações → Conjuntos de experiência → «Análise da experiência» → «Instalar»**, âmbito
   **projeto** ou **nó de modelo**. O âmbito «regras gerais» não tem projeto nenhum, e um modelo de
   tarefas sem projeto não vive no AI2P: então os nós não são criados (os registos instalam-se como
   sempre).
2. **Modelos do projeto**: apareceu o nó raiz «Análise da experiência» com dois filhos. Copie o filho
   «Análise da experiência do projeto» tantas vezes quantos projetos tiver e indique a cada cópia o
   seu projeto.
3. **Configurações → Agendamentos**: crie um agendamento periódico (semanal ou mensal) e escolha como
   modelo o nó **«Análise da experiência»**.

Para o agendamento só serve o nó **raiz** do modelo: os filhos chegam copiados com ele e não precisam
de agendamento próprio. A instalação do conjunto **não** cria agendamento de propósito: executar
tarefas gasta dinheiro no modelo, e isso deve decidi-lo uma pessoa.

Mais sobre períodos, o servidor do agendamento e disparos em atraso — o capítulo
**[Agendamento](schedule.md)**.

---

## O que o agente de IA sabe fazer com a experiência

| Ferramenta | O que faz | Comando CLI |
|---|---|---|
| `create_experience` | criar um registo | `ai2p experience` |
| `update_experience` | corrigir texto, competência, etiquetas, «carregar sempre», atividade | `ai2p experience-update` |
| `search_experience` | encontrar um registo por palavras | `ai2p experience-find` |
| `list_experience` | listar os registos de um âmbito por páginas | `ai2p experience-list` |
| `experience_usage` | estatísticas de uso | `ai2p experience-usage` |
| `move_experience` | transferir um registo para outro âmbito | `ai2p experience-move` |
| `set_experience_active` | ativar ou desativar um registo | `ai2p experience-active` |

Em `update_experience` **um campo não transmitido não muda**: editar o texto não retira as marcas
colocadas por uma pessoa.

**O agente não tem apagamento de experiência e não o terá.** Todas estas ações constam do catálogo de
ações, pelo que as regras de segurança da tarefa e da equipa as cobrem; ver o capítulo
**[Configuração](config.md)**, aba «Ações».

---

## Perguntas frequentes

**O registo existe mas não está no encargo.** Verifique por ordem: está **ativo**? tem
**competência**, e o executor tem essa competência? se não tem competência — tem a marca «carregar
sempre»? se tudo isso se cumpre, provavelmente não coube no limite: veja a aba «Experiência
utilizada» da última execução.

**O agente recusou-se a criar uma regra geral.** No texto há um código de tarefa, um caminho de
ficheiro, uma extensão de código-fonte ou um nome de projeto — isso é experiência **do projeto**, não
uma regra geral. Ou se cria na experiência do projeto, ou se reformula sem esses indícios.

**Na experiência geral acumulou-se lixo do projeto.** Configurações → Experiência geral → bloco
«Revisão da experiência geral»: marque e transfira em bloco para o projeto certo.

**Uma regra fornecida atrapalha.** Desative-a. Não é preciso apagar: a semeadura não devolve o que
foi apagado.

**Há experiência a mais.** Ponha a revisão no agendamento (modelo «Análise da experiência») e crie uma
regra de arquivamento «experiência + apenas inativos + a idade não importa».

---

## Capítulos vizinhos

* **[Projetos](progects.md)** — a aba «Experiência» do cartão do projeto e os três níveis de
  experiência.
* **[Modelos](templates.md)** — a experiência do nó de modelo e o lugar onde vive o saber sobre o
  processo.
* **[Tarefas](tasks.md)** — a descrição da tarefa como prompt e a aba «Experiência utilizada».
* **[Arquivamento](Archives.md)** — regras de arquivamento e limpeza automática.
* **[Agendamento](schedule.md)** — execução periódica do modelo «Análise da experiência».
* **[Configuração](config.md)** — abas «Experiência geral», «Conjuntos de experiência» e «Ações».
* **[Conjuntos de experiência: estilos de trabalho](../packs/README.md)** — documentos dos conjuntos
  fornecidos.

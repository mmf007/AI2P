# Conjuntos de experiência: estilos de trabalho

Documentos dos **conjuntos de experiência** — um arquivo por **código de conjunto**
(`style.tdd.md`). O documento abre-se com o botão **«i»** na linha do conjunto:
**Configurações → Experiência → Conjuntos**.

Um conjunto de experiência é um pacote pronto de registros de experiência que a pessoa instala
com um botão e obtém a disciplina de trabalho dos agentes de fábrica: como revisamos, como
escrevemos testes, como redigimos relatórios. O arquivo do conjunto é
`packs/<código>/pack.json` no diretório de dados; os registros vão para o escopo escolhido na
instalação (regras gerais da organização, experiência de um projeto ou nó de modelo) e são
retirados inteiros com um botão.

## Conteúdo da seção

* [style.strict-review](style.strict-review.md) — Revisão de código rigorosa
* [style.tdd](style.tdd.md) — Desenvolvimento guiado por testes
* [style.research-report](style.research-report.md) — Pesquisa e relatório
* [style.experience-analysis](style.experience-analysis.md) — Análise da experiência

O último está à parte: `style.experience-analysis` não é um estilo de trabalho, e
sim um modelo de tarefas para a revisão periódica da experiência acumulada.

## O que todos os conjuntos têm em comum

**Os registros são regras, não raciocínios.** Cada registro é curto, diz uma só coisa e muda o
comportamento do executor. Repetir o sabido não entra num conjunto: consome o limite de
experiência do trabalho e expulsa o que é realmente necessário.

**O registro carrega uma habilidade.** A regra sobre testes chega a quem escreve testes; a
regra sobre relatórios, a quem escreve relatórios. Um registro sem habilidade não chega a
trabalho nenhum, exceto os marcados «carregar sempre» — e na distribuição há exatamente um.

**Nada próprio de um projeto.** Os conjuntos da distribuição não nomeiam produtos, caminhos de
arquivos nem códigos de tarefas: um conjunto instala-se em qualquer organização. O que é seu
fica ao lado, como registros normais da experiência do projeto.

**O conjunto instala-se e retira-se inteiro.** A instalação marca os seus registros com a
etiqueta de serviço `pack:<código>`; a retirada leva exatamente esses e não toca nos seus
registros nem nas suas edições. Um registro que você editou continua seu: uma segunda
instalação não o sobrescreve.

## Como acrescentar o documento de um conjunto novo

Coloque aqui um arquivo `<código do conjunto>.md`, com o mesmo código que ele tem em
`pack.json`. Crie o mesmo arquivo nos demais idiomas: o conjunto de documentos deve coincidir.

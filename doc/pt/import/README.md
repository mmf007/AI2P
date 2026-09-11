# Importação de tarefas: como é organizado o catálogo de fontes

Este é o diretório com os documentos dos tipos de fonte: um arquivo para cada **tipo**, e o nome
do arquivo é o **código do tipo** (`trello.md`, `github.md`, `gitlab.md`). Abre-se pelo botão
**«i»** ao lado do campo «Tipo» no formulário da fonte: **Configurações → Catálogos →
Importações**.

O documento está ligado ao **tipo**, e não ao registro do catálogo: como obter as chaves do
sistema externo é igual para todas as fontes daquele tipo, e isso precisa ser sabido antes mesmo
de o registro ser salvo.

## Índice da seção

* [github](github.md) — Importação de tarefas do GitHub — instruções de conexão
* [gitlab](gitlab.md) — Importação de tarefas do GitLab — instruções de conexão
* [trello](trello.md) — Importação de tarefas do Trello — instruções de conexão

## O que há de comum em todos os tipos

**Os segredos podem ser um ou dois.** No Trello são dois — a **API key** e o **Token** —, por
isso o formulário da fonte tem tanto o botão «Definir a chave de API» quanto o botão do token.
No GitHub e no GitLab o segredo é **um só**: o token pessoal de acesso; eles não têm botão de
chave de API nenhum, e a fonte é ativada com um único token. As referências a segredos do tipo
`trello.*` pertencem apenas ao Trello.

**O segredo pertence à organização**: ele é cifrado com a chave dela e replicado para os
servidores da organização — não é preciso informá-lo em cada computador.

**Há dois tipos de importação:** a em massa (por registro do catálogo — quadro, repositório,
projeto) e a individual, pelo link de um cartão ou de uma tarefa. A individual escolhe o
procedimento **pelo próprio link**, por isso o endereço de uma tarefa do GitHub e o formato
antigo de endereço do GitLab (sem o separador `/-/`) se distinguem pelo nome do servidor: o
GitLab aceita o formato antigo apenas se houver a palavra `gitlab` no nome do host.

**A atualização a partir da fonte** também é feita pelo link da tarefa importada — o botão no
cartão dela. Uma tarefa importada lembra de onde veio.

## Como acrescentar o documento de um novo tipo

Coloque aqui o arquivo `<código do tipo>.md` — exatamente com o código pelo qual o tipo é
nomeado no catálogo (`trello`, `github`, `gitlab`): o nome do arquivo é montado pelo aplicativo,
não se pode inventá-lo. Crie um arquivo igual nos demais idiomas e registre todos nos índices
das seções (com o script `test/t18s1/mktoc.py`).

Escreva os endereços externos destes documentos por **completo**, com o esquema `https://` —
eles são abertos pelo botão «i» dentro de uma página do aplicativo, e ali um link relativo está
morto.

## O que não há nestes documentos

O procedimento para obter as chaves está descrito conforme o dia em que o documento foi escrito.
Os sistemas externos mudam a interface das configurações deles em silêncio: se os nomes dos
botões divergirem do texto, procure a seção de tokens pessoais (Personal access tokens) — a
sequência de ações em si é mais estável do que os rótulos.

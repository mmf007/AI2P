# Como a documentação do AI2P é organizada

Esta página é sobre a própria documentação: onde ela fica, do que é composta, como acrescentar
um documento novo e como manter os idiomas em harmonia. A lista de documentos está no
[índice](../index.md).

## Onde fica e o que é distribuído

O diretório vive em `AI2P_app/doc/` — junto ao código, porque é **distribuído junto com o
programa**: a geração da distribuição copia `AI2P_app/doc` inteiro para a raiz do release, e o
aplicativo mostra esses documentos direto na interface. A organização é a mesma em todos os
idiomas (`doc/<idioma>/<seção>/…`).

**Cada idioma tem duas primeiras páginas, e isso não é duplicação:**

* **`index.md` — o índice.** É para ele que leva o botão «Documentação»: a leitura começa pela
  lista de páginas. O índice é escrito **à mão** e agrupado por sentido (manual do
  administrador, manual do usuário, exemplos), por isso o script que monta os índices não o
  toca. A partir dele todos os documentos presentes na distribuição são alcançáveis por links.
* **`README.md` — a descrição breve do sistema.** É o que a pessoa vê primeiro no GitHub: o que
  é isto, para quê e para onde ir depois. Ele também continua sendo o ponto de entrada de um
  idioma que ainda não tem `index.md` — o botão da documentação cai nele sozinho.

Os idiomas estão listados em `AI2P_app/readme.md` — é o despachante de idiomas, e nele não há
nada além do ícone, do nome e da lista de links.

As imagens ficam em `doc/images/` (comuns a todos os idiomas) e em `doc/<idioma>/images/`
(dependentes do idioma — capturas de tela com textos). Um diretório sem nenhum `.md` não é
considerado um idioma — dele não se exige nem índice nem tradução.

Os documentos de projeto (especificação, relatórios de pesquisa, procedimento de publicação) não
entram aqui — eles ficam em `doc/` na raiz do repositório e não vão para a distribuição.

## Como as seções são organizadas

Uma seção é um subdiretório dentro do idioma. Cada seção tem o **seu próprio `README.md`** — ele
é o índice da seção: lista todos os documentos dela e explica o que há de comum na seção.

Hoje há três seções:

* `man/` — [manual do usuário](README.md): texto corrido por capítulos, o nome do arquivo é
  livre;
* `models/` — [um documento para cada registro do catálogo de modelos de IA](../models/README.md);
* `import/` — [um documento para cada tipo de fonte de tarefas](../import/README.md).

O nome do arquivo do documento o aplicativo calcula **sozinho**: nos modelos é o nome do modelo
do catálogo, nas importações é o código do tipo de fonte. O nome é usado como está, e os
caracteres não permitidos em nome de arquivo são substituídos por `_`. Se o documento não
existir em nenhum idioma, o botão «i» abre uma dica com o caminho completo de onde colocá-lo.

## Como acrescentar um documento

1. Coloque o arquivo `<seção>/<nome>.md` — o nome segue a regra da seção (veja o `README.md`
   dela).
2. Registre-o **à mão** no índice do idioma (`index.md`) — ali os documentos estão agrupados por
   sentido, e o script não consegue adivinhar o grupo. No índice da **seção** ele entra sozinho:
   os índices das seções são montados pelo script `test/t18s1/mktoc.py` conforme a composição
   real dos diretórios; `python test/t18s1/mktoc.py --check` mostra se eles se afastaram dos
   arquivos.
3. Faça referência aos documentos vizinhos com links **relativos** (`models/GLM-5.2.md`,
   `../import/trello.md`) — é a janela da documentação que os interpreta. Nos documentos abertos
   pelo botão **«i»** (modelos, importações), escreva os endereços externos por completo, com o
   esquema `https://`.

O arquivo de índice se chama `README.md` em todos os diretórios — não é preciso mudar as
maiúsculas do nome.

## Onde colocar capítulos novos

Um capítulo novo é um arquivo separado `man/<nome>.md`. Registre-o no
[índice do idioma](../index.md) (ele é escrito à mão e agrupado por sentido) e, aqui no índice
da seção, ele entra sozinho — esta lista é montada por um script. Crie ao mesmo tempo a
**tradução com o mesmo nome** nos demais idiomas: as composições dos idiomas devem coincidir
arquivo por arquivo.

Uma regra que vale seguir: números, nomes de campos e rótulos de botões no manual ficam
desatualizados em silêncio. Confira-os não pela memória, mas pelo dicionário da interface
(`i18n/pt.json`) e pelo código — por exemplo, a aba do catálogo de modelos se chama
**«Modelos»**, e não «Modelos de IA».

## Traduções

Os documentos dos outros idiomas ficam em diretórios vizinhos (`doc/en/…`) e repetem a
organização um a um: quantos arquivos houver em `ru`, tantos haverá em `en`. Isso é conferido
pelo script `test/t18s1/cmp.py` — ele imprime o que está faltando em cada idioma.

Se o documento não existir no idioma da interface, o aplicativo mostra a versão russa e, se ela
também não existir, a inglesa.

# Importar um layout do Figma — guia de conexão

A importação transforma uma **área de layout do Figma** em uma tarefa do AI2P: as telas (e,
se desejado, os elementos de design) chegam como **imagens**, e abaixo de cada imagem a
descrição da tarefa lista **todos os textos** daquele quadro — botões, títulos, campos, de
cima para baixo. Assim uma tarefa «faça esta tela» recebe a aparência e os textos exatos.
É preciso um token pessoal do Figma e um registro no catálogo de importações.

## 1. Obter um token

**A API do Figma não responde sem token — nem mesmo para um arquivo público** que abre no
navegador sem login. O token é sempre necessário.

1. Entre no Figma e abra <https://www.figma.com/settings> → aba **Security**.
2. Em **Personal access tokens** clique em **Generate new token**.
3. Nome livre, por exemplo `AI2P import`; validade a gosto.
4. Permissões (scopes): **File content → Read only**. Nada mais é necessário.
5. **Generate token** e copie a string. Ela começa com `figd_` e é mostrada **uma só vez**.

O token lê tudo o que sua conta vê — guarde-o como uma senha.

## 2. Criar uma fonte no catálogo de importações

**Configurações → aba «Catálogos» → «Catálogo de importações» → «Adicionar»:**

| Campo | Valor |
| --- | --- |
| Nome da fonte | livre, por exemplo `Design Figma` |
| Tipo | Figma |
| Ativa | ainda indisponível — liga sozinha após o passo 3 |

Uma fonte do Figma não tem outros campos: a importação funciona **só por link** para uma
área do layout. Clique em **«Salvar»**.

## 3. Informar o token

Abra a fonte salva com **«editar»**, clique em **«Definir token»** e cole a string do passo 1.
O Figma não tem chave de API: um segredo, um botão. O formulário avisa se o valor não começa
com `figd_`. Assim que o token é informado, a fonte **é ativada sozinha**.

**«Verificar conexão»** pergunta ao Figma «quem sou eu» e responde em palavras: se o token
foi aceito e a qual conta pertence.

O token fica **criptografado** no banco da organização e chega a todos os seus servidores.

## 4. Importar uma área do layout

1. No Figma **selecione a área** — uma tela, uma seção com telas ou uma página — e copie o
   link da seleção: botão direito → **Copy link to selection** (ou `Ctrl+L`). O link ganha o
   parâmetro `node-id=…` — essa é a área.
2. No AI2P escolha o projeto e clique em **«Importar tarefa por URL»** na lista de tarefas.
3. Cole o link, por exemplo
   `https://www.figma.com/design/rqN1QJdU35Q18H5mpUmlFd/Home-Planner?node-id=54965-55076`.
   Aparecem duas opções:
   * **«Elementos de design como imagens separadas»** — desligado: só as telas principais;
     ligado: também cada elemento da tela como imagem separada com seus textos;
   * **«Importar o arquivo inteiro»** — aparece só quando o link **não** tem `node-id`. Sem
     essa marca o link é recusado: um arquivo inteiro pode ser enorme.
4. Clique em **«Importar»**: as imagens são baixadas para o projeto e o conteúdo preenche o
   formulário da nova tarefa. Complete-o e salve.

### O que é uma tela e um elemento

* **Um link para um quadro (frame)** é **uma tela**: seus quadros aninhados são partes dela.
* **Um link para uma página, seção ou grupo** — as telas são os quadros de nível superior
  dentro dele (seções e grupos são atravessados).
* **Elementos** (se ligados) — componentes e suas instâncias (sem entrar nelas) e quadros e
  grupos aninhados. Uma instância repetida do mesmo componente com os mesmos textos é tomada
  uma vez. Camadas muito pequenas (menos de 8 px) são ignoradas.
* Camadas ocultas não são importadas.

### Como fica a descrição

Um cabeçalho de origem (arquivo, área, link) e uma seção por tela: `## 1. Nome da tela`, a
imagem e a lista **«Textos:»**. Com elementos — uma subseção «Elementos da tela …» com o mesmo
arranjo por elemento. Imagens PNG em 2× (um quadro muito grande — 1×).

## 5. Agentes de IA

Os agentes têm a mesma ação pela ferramenta `import_task_from_url` (código
`AI2P.Tasks.ImportFromUrl`); o agente CLI, por
`ai2p import <link> --elements true|false [--wholeFile]`. **As duas opções são perguntas
para a pessoa**: enquanto o agente não as informar, nada é importado e ele é orientado a
perguntar. Um link sem `node-id` só é importado com o consentimento explícito da pessoa.

## 6. Atualizar uma tarefa a partir do Figma

A tarefa importada tem o **«Link de importação»**, que lembra a área e a escolha «elementos»
(`ai2p-elements=1`) ou «arquivo inteiro» (`ai2p-whole=1`). **«Atualizar» → «Da fonte de
importação»** relê o layout do mesmo jeito: o título e a **descrição inteira são
reescritos** com imagens novas. Critérios de aceitação, executores, status e vínculos não
são alterados. As imagens anteriores ficam em uploads/.

## Se algo não funcionar

| Mensagem | Causa e solução |
| --- | --- |
| «O token do Figma não foi encontrado pela referência …» | O token não foi informado: passo 3. |
| «O Figma não aceitou o TOKEN…» (`Invalid token`, 403) | Token errado, revogado ou expirado. Gere um novo (passo 1). |
| «O Figma negou o acesso…» (403) | Falta a permissão **File content: Read** ou o arquivo não é visível para a conta do token. |
| «O Figma não encontrou o arquivo ou o nó…» (404) | Link errado: chave do arquivo ou `node-id`. |
| «O link não indica um nó (node-id)…» | Copie o link da seleção ou ligue «Importar o arquivo inteiro». |
| «Sem imagem…» abaixo de uma tela | O Figma não renderizou o nó (vazio ou grande demais). Os textos continuam lá. |
| «O limite de imagens por importação (300)…» | Elementos demais: importe partes do layout por links separados. |
| «Uma fonte do Figma não tem importação em massa» | Use «Importar tarefa por URL». |

Os detalhes ficam nos logs (`logs/*.jsonl`, registros `FigmaImporter`); o valor do token
nunca é registrado, só o comprimento e o prefixo.

## Limites

* No máximo **300 imagens** por importação.
* Comentários do layout não vão para o chat da tarefa.
* Não há importação em massa — só por link.

# Importação de tarefas do Trello — instruções de conexão

A importação transfere cartões do Trello para tarefas do AI2P (ET, item 2.10, cap. 11). Para a
conexão são necessárias duas cadeias vindas do Trello — a **API key** e o **Token** — e um
registro no catálogo de importações. Passo a passo:

## 1. Obter a API key

O Trello fornece as chaves pela página de integrações (Power-Ups):

1. Entre no Trello pelo navegador e abra <https://trello.com/power-ups/admin>.
2. Clique em **«New»** (criar uma nova integração). Preencha os campos obrigatórios:
   * **Name** — qualquer um, por exemplo `AI2P import`;
   * **Workspace** — o seu espaço de trabalho;
   * **Email / Author** — os seus.
   O campo «Iframe connector URL» pode ficar vazio.
3. Abra a integração criada → aba **«API key»** → botão
   **«Generate a new API key»**.
4. Copie o valor do campo **API key** (uma cadeia de letras e algarismos).

## 2. Obter o Token

Na mesma página, à direita do campo API key, há o link **«Token»**
(no texto «you can manually generate a Token»):

1. Clique nele — abrirá a página de autorização do Trello.
2. Clique em **«Permitir» (Allow)** no rodapé da página.
3. Copie o **token** exibido (uma cadeia longa).

O token dá acesso aos quadros da sua conta — guarde-o como se fosse uma senha.

## 3. Criar a origem no catálogo de importações

**Configurações → aba «Catálogos» → «Catálogo de importações» → «Adicionar»:**

| Campo | O que informar |
| --- | --- |
| Nome da origem | qualquer um, por exemplo `Trello principal` |
| Tipo | Trello |
| Login no Trello | o seu **username** — aparece no perfil do Trello como `@nome` (informe sem o `@`) |
| Filtro | um trecho do nome do quadro, por exemplo `AI2P`; vazio — todos os quadros |
| Ativa | por enquanto indisponível — será ligada sozinha depois do passo 4 |

Clique em **«Salvar»**. Os botões para informar a chave e o token aparecem no registro
**salvo**: o valor vai para o cofre imediatamente, e por isso, antes de salvar, não se sabe de
quem ele é.

## 4. Informar a chave de API e o token (v1.65)

Abra a origem salva pelo botão **«editar»** — no formulário apareceram dois botões:

1. **«Definir a chave de API»** — cole a cadeia do passo 1 e clique em «Salvar».
2. **«Definir o token»** — cole a cadeia do passo 2 e clique em «Salvar».

Cada botão pergunta **exatamente um campo**. O valor antigo nunca é mostrado — para trocar a
chave, informa-se uma nova. Abaixo do formulário vê-se o estado: «Chave de API: definida,
origem — a organização», o mesmo para o token.

**É fácil trocar a chave e o token de lugar**, e o Trello responde a isso com um obscuro
«invalid key». Por isso o formulário confere o formato dos dois e avisa: a **chave de API tem
exatamente 32 caracteres** `0–9 a–f`, o **token tem 64 caracteres iguais a esses** ou é uma
cadeia que começa com `ATTA`. Nem um nem outro é o **«Secret»**: o segredo da integração, obtido
naquela mesma página, não é o token e não é necessário no AI2P.

**O botão «Verificar a conexão»** (ao lado dos botões de entrada, funciona quando os dois
valores estão preenchidos) pergunta ao Trello «quem sou eu» e responde em palavras na hora: se a
chave foi aceita, se o token foi aceito, para qual conta o token foi emitido e se ela coincide
com o login da origem. Verificar a conexão aqui custa menos do que descobrir a recusa na
primeira importação.

**Onde esses valores ficam.** No banco de dados da sua organização, **cifrados** com a chave da
organização (como as chaves de API dos modelos): eles chegam sozinhos a todos os servidores da
organização, e só o servidor que recebeu a chave da organização consegue decifrá-los. Não é mais
preciso escrever nada em `secrets.json`.

**Enquanto os dois valores não estiverem informados, a origem não pode ficar ativa** — o
interruptor «Ativa» fica bloqueado. Assim que o último dos dois for informado, a origem **se liga
sozinha**; daí em diante a atividade é comandada por você.

**As instalações em que as chaves já estão escritas no `secrets.json`** continuam funcionando: os
valores são lidos na ordem «organização → `secrets.json` → variável de ambiente» e, na primeira
partida da nova versão, são transferidos do arquivo para a organização — o formulário mostrará
«definida» imediatamente. As variáveis de ambiente `TRELLO_API_KEY` / `TRELLO_TOKEN` também
continuam funcionando e não são transferidas para lugar nenhum: elas são definidas de fora de
propósito.

**Duas contas do Trello.** Cada origem criada a partir da v1.65 tem o **seu próprio** par
chave/token — crie uma segunda origem e informe outros valores nela. Nas origens que já existiam
antes da v1.65 o par é comum (as referências `trello.apiKey` / `trello.token` nos parâmetros do
registro): para separá-las, crie a origem de novo.

## 5. Executar a importação

1. Escolha o **projeto** (no cabeçalho ou na aba do projeto) — a importação sempre vai para o
   projeto atual.
2. Na lista de tarefas clique no botão-ícone da **nuvem com a seta** («Importação de tarefas») —
   ele só aparece com um projeto escolhido.
3. Na caixa de diálogo escolha a origem e as caixas de seleção:
   * **acrescentar novas** (ligada por padrão) — os cartões que ainda não existem virarão tarefas;
   * **atualizar as existentes** — nas tarefas importadas antes serão atualizados o título, a
     descrição e o prazo vindos do Trello (as edições locais desses campos serão sobrescritas).
4. Clique em **«Importar»**. O resultado aparecerá em uma mensagem: «acrescentadas X, atualizadas
   Y, ignoradas Z»; o registro `import.run` aparecerá no «Histórico de trabalhos».

## O que exatamente é importado

* São tomados os **cartões abertos dos quadros abertos** do login indicado; os quadros são
  filtrados por um trecho do nome (filtro vazio — todos).
* Cartão → tarefa com o estado **«rascunho»**: título, prazo (due), descrição do cartão + uma
  nota de origem no rodapé («Importado do Trello: quadro …, cartão», com o link). Os executores e
  a prioridade você preenche já no AI2P.
* **Não se criam duplicatas**: o id externo do cartão é lembrado na tarefa (`trello:<id>`); uma
  importação repetida ignorará ou atualizará o mesmo cartão — conforme a caixa de seleção.

## Se algo não funcionar

| Mensagem | Causa e o que fazer |
| --- | --- |
| «Chave do Trello não encontrada (…)» | A chave de API ou o token não foi informado: abra a origem e clique em «Definir a chave de API» / «Definir o token» (passo 4). Em uma instalação antiga — verifique o `secrets.json`: o caminho do arquivo está indicado na própria mensagem. |
| «Este servidor ainda não recebeu a chave da organização…» | O servidor está ligado a uma organização alheia, mas a solicitação não foi confirmada pelo maestro dela — sem a chave da organização não há com que cifrar os valores. Confirme a conexão (Configurações → Servidores). |
| «O Trello não aceitou a CHAVE de API…» (resposta do Trello «invalid key») | A chave está errada: o mais comum é o **token** ter ido parar no campo da chave — a mensagem diz isso diretamente e mostra o comprimento do valor informado. Repita o passo 1 e informe a chave de novo. |
| «O Trello não aceitou o TOKEN…» («invalid token») | O token está incorreto, foi revogado ou no campo dele foi parar a chave ou o «Secret» — repita o passo 2. |
| «O Trello negou o acesso…» | A chave e o token foram aceitos, mas a conta para a qual o token foi emitido não tem direitos sobre esse quadro ou cartão. |
| «O Trello não encontrou o que foi pedido (HTTP 404…)» | O login (username) não existe ou o cartão está inacessível à conta do token — confira com o perfil do Trello. |
| A importação foi feita, mas «acrescentadas 0» | O filtro não coincidiu com nenhum quadro, ou o participante não tem cartões abertos; tente com o filtro vazio. |

As mensagens chegam inteiras à própria janela de importação — não é preciso pescá-las de uma dica
flutuante. Os detalhes das requisições ficam nos logs do aplicativo (`logs/*.jsonl`, registros
`TrelloImporter`): por qual referência e de qual cofre a chave e o token foram tomados, qual o
comprimento deles (os valores em si nunca vão para o log) e qual resposta o Trello devolveu.


## Importação de um cartão pelo link (v1.50)

Ao lado do botão de importação em massa, na lista de tarefas, há o botão **«Importação de uma
tarefa por URL»**:

1. Cole o link do cartão no formato `https://trello.com/c/<código>` (botão «Compartilhar» no
   cartão do Trello). A origem só é perguntada se houver várias origens trello ativas — as chaves
   são tomadas dela.
2. O conteúdo do cartão será colocado **no formulário da nova tarefa**: título, descrição, prazo.
   **Os arquivos anexados ao cartão são baixados** para o cofre do projeto e inseridos na
   descrição como links (as imagens, como miniaturas); os anexos que já são links continuam
   links. Um arquivo com mais de 200 MB não é baixado — permanece como link, com uma marca.
3. Complete o formulário (executor, prioridade) e salve. Uma importação repetida do mesmo cartão
   não criará duplicata.

Aos agentes de IA a mesma ação está disponível pela ferramenta `import_task_from_url` (código da
ação `AI2P.Tasks.ImportFromUrl` — as regras de segurança deny/confirm são aplicadas como de
costume).

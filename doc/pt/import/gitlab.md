# Importação de tarefas do GitLab — instruções de conexão

A importação transfere as **issues do GitLab** para tarefas do AI2P (ET, item 2.10, cap. 11).
Para a conexão é necessária uma cadeia vinda do GitLab — o **token pessoal de acesso** — e um
registro no catálogo de importações. Funciona tanto com o `gitlab.com` em nuvem quanto com um
servidor GitLab próprio.

## 1. Obter o token de acesso

O GitLab fornece os tokens nas configurações do perfil:

1. Entre no GitLab pelo navegador e abra
   <https://gitlab.com/-/user_settings/personal_access_tokens>
   (em um servidor próprio — o mesmo caminho a partir do endereço dele: `https://git.example.com/-/user_settings/personal_access_tokens`).
   O caminho da página também é indicado na própria interface: **avatar → Edit profile → Access tokens**.
2. Clique em **«Add new token»**. Preencha:
   * **Token name** — qualquer um, por exemplo `AI2P import`;
   * **Expiration date** — o prazo de validade; ao vencer, o token deixará de funcionar e a
     importação responderá «O GitLab não aceitou o TOKEN»;
   * **Select scopes** — basta **`read_api`**. O `api` completo também serve, mas ele dá direito
     de alterar dados, e a importação apenas lê.
3. Clique em **«Create personal access token»** e **copie imediatamente** o valor exibido: o
   GitLab não o mostrará uma segunda vez. Os tokens atuais começam com `glpat-`.

O token dá acesso aos seus projetos — guarde-o como se fosse uma senha.

**Um token de projeto ou de grupo** (Settings → Access tokens dentro do projeto) também serve, se
tiver o direito `read_api`; ele é mais cômodo por enxergar exatamente um projeto.

## 2. Criar a origem no catálogo de importações

**Configurações → aba «Catálogos» → «Catálogo de importações» → «Adicionar»:**

| Campo | O que informar |
| --- | --- |
| Nome da origem | qualquer um, por exemplo `GitLab principal` |
| Tipo | GitLab |
| Servidor GitLab | vazio — o `https://gitlab.com` em nuvem; um servidor próprio se indica por completo, por exemplo `https://git.example.com` |
| Projeto GitLab | o caminho do projeto no formato `grupo/projeto` — exatamente como ele aparece na barra de endereços; os subgrupos se escrevem com barra (`grupo/subgrupo/projeto`) |
| Rótulos | seleção de issues por rótulos separados por vírgula, por exemplo `bug,ui`; vazio — todas as issues abertas do projeto |
| Ativa | por enquanto indisponível — será ligada sozinha depois do passo 3 |

Clique em **«Salvar»**. O botão para informar o token aparece no registro **salvo**: o valor vai
para o cofre imediatamente, e por isso, antes de salvar, não se sabe de quem ele é.

**O GitLab não tem uma «chave de API» à parte** — ao contrário do Trello, onde a chave e o token
são dois valores diferentes. Por isso o formulário da origem GitLab tem um único botão de
segredo, e a origem se torna ativa com um único token informado.

## 3. Informar o token

Abra a origem salva pelo botão **«editar»** — no formulário apareceu o botão **«Definir o
token»**. Cole a cadeia do passo 1 e clique em «Salvar».

O valor antigo nunca é mostrado — para trocar o token, informa-se um novo. Abaixo do formulário
vê-se o estado: «Token: definido, origem — a organização». Se o valor não se parecer com um token
do GitLab (não começar com `glpat-`), o formulário avisará: isso é uma dica, e não uma proibição
— os tokens de projeto e os de OAuth têm outro formato e funcionam.

**O botão «Verificar a conexão»** (ao lado, funciona quando o token está informado) pergunta ao
GitLab «quem sou eu» e, se houver um projeto nomeado na origem, se ele é visível a esse token. A
resposta vem em palavras: se o token foi aceito, para qual conta ele foi emitido e se o projeto é
visível. Verificar a conexão aqui custa menos do que descobrir a recusa na primeira importação.

**Onde esse valor fica.** No banco de dados da sua organização, **cifrado** com a chave da
organização (como as chaves de API dos modelos): ele chega sozinho a todos os servidores da
organização, e só o servidor que recebeu a chave da organização consegue decifrá-lo. Não é
preciso escrever nada em `secrets.json`.

**Duas contas ou dois servidores GitLab.** Cada origem tem a **sua própria** referência ao token
— crie uma segunda origem (por exemplo, para o seu servidor GitLab) e informe outro valor nela. A
importação de uma issue avulsa pelo link escolhe a origem sozinha, pelo endereço do servidor no
link.

## 4. Executar a importação

1. Escolha o **projeto** (no cabeçalho ou na aba do projeto) — a importação sempre vai para o
   projeto atual do AI2P.
2. Na lista de tarefas clique no botão-ícone da **nuvem com a seta** («Importação de tarefas») —
   ele só aparece com um projeto escolhido.
3. Na caixa de diálogo escolha a origem e as caixas de seleção:
   * **acrescentar novas** (ligada por padrão) — as issues que ainda não existem virarão tarefas
     do AI2P;
   * **atualizar as existentes** — nas tarefas importadas antes serão atualizados o título, a
     descrição e o prazo vindos do GitLab (as edições locais desses campos serão sobrescritas).
4. Clique em **«Importar»**. O resultado aparecerá em uma mensagem: «acrescentadas X, atualizadas
   Y, ignoradas Z»; o registro `import.run` aparecerá no «Histórico de trabalhos».

## O que exatamente é importado

* São tomadas as **issues abertas** (`state=opened`) do projeto indicado; se houver rótulos
  definidos, somente as marcadas com eles.
* Issue do GitLab → tarefa do AI2P com o estado **«rascunho»**: título, prazo (`due_date`), **o
  próprio texto** da issue na descrição + uma nota de origem no rodapé («Importado do GitLab:
  projeto …, issue #N», com o link). Os executores e a prioridade você preenche já no AI2P.
* **Arquivos anexados.** A issue do GitLab não tem uma lista de anexos à parte: um arquivo
  carregado vive como o link `/uploads/<hash>/<nome>` **dentro do próprio texto**. A importação
  encontra esses links, **baixa os arquivos** para o cofre do projeto do AI2P e reescreve os
  links para os locais — as imagens continuam imagens. Um arquivo que não pôde ser baixado (falta
  de direitos, mais de 200 MB) permanece como link para o GitLab: completo, e não relativo — por
  ele ao menos dá para navegar.
* **Discussão.** Os comentários da issue são transferidos para o **chat da tarefa** do AI2P: o
  autor aparece como `gitlab:<login>`, e a hora e a ordem se mantêm. Os registros de serviço do
  GitLab («alterou o rótulo», «designou o responsável», «fechou») são ignorados — isso não é
  conversa de gente. Uma importação repetida acrescenta apenas as mensagens **novas** e não faz
  duplicatas.
* **Não se criam duplicatas**: a chave externa da issue é lembrada no formato
  `gitlab:<servidor>:<projeto>:<número>`; uma importação repetida ignorará ou atualizará a mesma
  issue — conforme a caixa de seleção. O servidor entra na chave de propósito: a issue nº 42 do
  seu GitLab e a issue nº 42 do GitLab em nuvem são issues diferentes.

## Importação de uma issue pelo link

Ao lado do botão de importação em massa, na lista de tarefas, há o botão **«Importação de uma
tarefa por URL»**:

1. Cole o link no formato `https://gitlab.com/<grupo>/<projeto>/-/issues/<número>`. São
   entendidos os subgrupos, o formato antigo de endereço sem `/-/`, as caudas `?…` e `#note_…`, e
   também o endereço de um servidor GitLab próprio. A origem só é perguntada se houver várias
   origens gitlab ativas e não for possível escolher uma pelo endereço do servidor.
2. O conteúdo da issue será colocado **no formulário da nova tarefa**: título, o texto na
   descrição, prazo, arquivos baixados. A discussão irá para o chat logo depois de salvar a
   tarefa.
3. Complete o formulário (executor, prioridade) e salve. Uma importação repetida da mesma issue
   não criará duplicata.

Aos agentes de IA a mesma ação está disponível pela ferramenta `import_task_from_url` (código da
ação `AI2P.Tasks.ImportFromUrl` — as regras de segurança deny/confirm são aplicadas como de
costume). Qual procedimento fará a importação é escolhido pelo **formato do link**: o agente não
precisa saber quais origens estão criadas.

## Atualização da tarefa a partir do GitLab

Uma tarefa importada tem preenchido o **link de importação** (seção «Avançado» do formulário da
tarefa, linha no cabeçalho do cartão). O botão **«atualizar»** no cartão dessa tarefa **faz uma
pergunta**:

* **«Da origem de importação»** — reler a issue e colocar na tarefa o título, o prazo, a
  descrição (com os arquivos baixados atualizados) e as mensagens **novas** da discussão;
* **«Somente aqui»** — simplesmente reler a tarefa, sem perguntar nada ao GitLab;
* **«Cancelar»**.

Na atualização, a descrição é reescrita **por inteiro** — exatamente como na importação em massa
com a marca «atualizar as existentes». Critérios de aceitação, executores, estado, prioridade,
tags e vínculos da tarefa a atualização **não toca**: eles são conduzidos aqui, e não no GitLab.

Limpe o campo «Link de importação» — a tarefa se desvinculará da origem e a pergunta não
aparecerá mais.

## Se algo não funcionar

| Mensagem | Causa e o que fazer |
| --- | --- |
| «Token do GitLab não encontrado (…)» | O token não foi informado: abra a origem e clique em «Definir o token» (passo 3). Em uma instalação antiga — verifique o `secrets.json`: o caminho do arquivo está indicado na própria mensagem. |
| «Este servidor ainda não recebeu a chave da organização…» | O servidor está ligado a uma organização alheia, mas a solicitação não foi confirmada pelo maestro dela — sem a chave da organização não há com que cifrar o valor. Confirme a conexão (Configurações → Servidores). |
| «O GitLab não aceitou o TOKEN (HTTP 401…)» | O token está incorreto, foi revogado ou está **vencido**, ou não tem o direito `read_api`. Emita um novo (passo 1). Uma causa frequente é ter copiado o valor errado: a mensagem mostra o comprimento do que foi informado (o valor em si nunca é mostrado). |
| «O GitLab negou o acesso (HTTP 403…)» | O token foi aceito, mas o dono dele não tem direitos sobre esse projeto ou o token não tem `read_api`. |
| «O GitLab (…) não encontrou o que foi pedido (HTTP 404…)» | O caminho do projeto ou o número da issue estão errados. **Um projeto fechado, invisível ao dono do token, também responde 404** — o GitLab, de propósito, não informa a existência de projetos alheios. |
| «O GitLab responde “com frequência demais” (HTTP 429)» | Limite de frequência de requisições. Espere um minuto e repita. |
| «A origem de importação não tem o projeto GitLab indicado» | O campo «Projeto GitLab» está vazio — a importação em massa não tem de onde tirar issues. Informe o caminho no formato `grupo/projeto`. |
| «O link … não se parece com uma issue do GitLab» | O endereço não leva a uma issue: é preciso o formato `.../-/issues/<número>`. Um link para merge request, quadro ou épico não serve. |
| A importação foi feita, mas «acrescentadas 0» | Os rótulos não coincidiram com nenhuma issue, ou o projeto não tem issues abertas; tente com o campo «Rótulos» vazio. |
| O arquivo da descrição não abre | O arquivo não foi baixado (falta de direitos, mais de 200 MB) — na descrição ficou o link para o GitLab, e ele só abrirá para quem estiver logado no GitLab. A causa está registrada no log. |

As mensagens chegam inteiras à própria janela de importação — não é preciso pescá-las de uma dica
flutuante. Os detalhes das requisições ficam nos logs do aplicativo (`logs/*.jsonl`, registros
`GitLabImporter`): qual endereço foi requisitado, de onde o token foi tomado e qual o comprimento
dele (o valor em si nunca vai para o log) e o que o GitLab respondeu.

## Em que o GitLab difere do Trello nesta importação

| | Trello | GitLab |
| --- | --- | --- |
| Segredos | dois: chave de API + token | um: o token pessoal |
| Como o segredo é enviado | por parâmetros da cadeia de consulta | pelo cabeçalho `PRIVATE-TOKEN` |
| O que a origem define | login e filtro por nome do quadro | endereço do servidor, caminho do projeto, rótulos |
| Unidade de importação | cartão do quadro | issue do projeto |
| Anexos | em uma lista à parte no cartão | como links dentro do texto |
| Chave externa da tarefa | `trello:<id do cartão>` | `gitlab:<servidor>:<projeto>:<número>` |

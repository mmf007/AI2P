# Importação de tarefas do GitHub — instruções de conexão

A importação transfere as **issues do GitHub** para tarefas do AI2P (ET, item 2.10, cap. 11).
Para a conexão é necessária uma cadeia vinda do GitHub — o **token pessoal de acesso** — e um
registro no catálogo de importações. Passo a passo:

## 1. Obter o token

O GitHub fornece os tokens nas configurações da conta. Há dois tipos de token e os dois servem.

**Token de granularidade fina (fine-grained, recomendado — dá para conceder exatamente os
direitos necessários):**

1. Entre no GitHub e abra
   <https://github.com/settings/personal-access-tokens/new>.
2. **Token name** — qualquer um, por exemplo `AI2P import`; **Expiration** — o prazo de validade.
3. **Repository access** — «All repositories» ou «Only select repositories», e enumere aqueles
   dos quais você vai importar.
4. **Permissions → Repository permissions → Issues** — marque **Read-only**.
   (O acesso de leitura ao campo **Metadata** o próprio GitHub liga sozinho — ele é obrigatório.)
5. **Generate token** e copie a cadeia exibida. Ela começa com `github_pat_`
   e é mostrada **uma única vez**.

**Token clássico (classic):**

1. Abra <https://github.com/settings/tokens> → **Generate new token (classic)**.
2. Marque o escopo **`repo`** (ele dá acesso às issues de repositórios privados);
   para repositórios públicos basta **`public_repo`**.
3. **Generate token** e copie a cadeia — ela começa com `ghp_`.

O token dá acesso aos seus repositórios — guarde-o como se fosse uma senha. **Para repositórios
públicos o token também é necessário**: sem ele o GitHub permite apenas 60 requisições por hora
por endereço, e a importação esbarrará nesse limite.

## 2. Criar a origem no catálogo de importações

**Configurações → aba «Catálogos» → «Catálogo de importações» → «Adicionar»:**

| Campo | O que informar |
| --- | --- |
| Nome da origem | qualquer um, por exemplo `GitHub de trabalho` |
| Tipo | GitHub |
| Proprietário dos repositórios | o nome do usuário **ou da organização** — o que aparece no endereço `github.com/<proprietário>` |
| Filtro | um trecho do nome do repositório, por exemplo `planner`; vazio — todos os repositórios do proprietário |
| Ativa | por enquanto indisponível — será ligada sozinha depois do passo 3 |

Clique em **«Salvar»**. O botão para informar o token aparece no registro **salvo**: o valor vai
para o cofre imediatamente, e por isso, antes de salvar, não se sabe de quem ele é.

## 3. Informar o token

Abra a origem salva pelo botão **«editar»** e clique em **«Definir o token»** — cole a cadeia do
passo 1 e salve.

O GitHub não tem chave de API: o segredo é um só, por isso o formulário tem um único botão (no
Trello são dois). O valor antigo nunca é mostrado — para trocar o token, informa-se um novo.
Abaixo do formulário vê-se o estado: «Token: definido, origem — a organização».

O formulário confere o **formato** do valor e avisa se ele não se parecer com um token do GitHub:
os atuais começam com `github_pat_` ou `ghp_`, e os antigos têm 40 caracteres `0–9 a–f`.

**O botão «Verificar a conexão»** pergunta ao GitHub «quem sou eu» e responde em palavras na
hora: se o token foi aceito, para qual conta ele foi emitido e se essa conta coincide com o
proprietário indicado na origem. Verificar a conexão aqui custa menos do que descobrir a recusa
na primeira importação.

**Onde o token fica.** No banco de dados da sua organização, **cifrado** com a chave da
organização (como as chaves de API dos modelos): ele chega sozinho a todos os servidores da
organização, e só o servidor que recebeu a chave da organização consegue decifrá-lo. Não é
preciso escrever nada em `secrets.json`; se, ainda assim, o valor for colocado por arquivo, ele
também será lido — a ordem de leitura é «organização → `secrets.json` → variável de ambiente».

**Enquanto o token não for informado, a origem não pode ficar ativa** — o interruptor «Ativa»
fica bloqueado. Assim que o token for informado, a origem **se liga sozinha**; daí em diante a
atividade é comandada por você.

**Duas contas do GitHub.** Cada origem tem a **sua própria** referência ao token — crie uma
segunda origem e informe outro valor nela.

## 4. Executar a importação

1. Escolha o **projeto** — a importação sempre vai para o projeto atual.
2. Na lista de tarefas clique no botão-ícone da **nuvem com a seta** («Importação de tarefas») —
   ele só aparece com um projeto escolhido.
3. Na caixa de diálogo escolha a origem e as caixas de seleção:
   * **acrescentar novas** (ligada por padrão) — as issues que ainda não existem virarão tarefas
     do AI2P;
   * **atualizar as existentes** — nas tarefas importadas antes serão atualizados o título, a
     descrição e o prazo (as edições locais desses campos serão sobrescritas).
4. Clique em **«Importar»**. O resultado aparecerá em uma mensagem: «acrescentadas X, atualizadas
   Y, ignoradas Z, mensagens de discussão N»; o registro `import.run` aparecerá no «Histórico de
   trabalhos».

## O que exatamente é importado

* São tomadas as **issues abertas** dos repositórios do proprietário; os repositórios são
  selecionados por um trecho do nome (filtro vazio — todos).
* **Os pull requests não são importados.** Na API do GitHub eles se parecem com issues, mas não
  são considerados issues e são ignorados.
* Issue do GitHub → tarefa do AI2P com o estado **«rascunho»**: título, **o texto da issue
  inteiro na descrição** + uma nota de origem no rodapé («Importado do GitHub: repositório …,
  issue #N», com o link). Os executores e a prioridade você preenche já no AI2P.
* **A discussão da issue é transferida para o chat da tarefa do AI2P**: cada comentário vira uma
  mensagem, o autor é nomeado como é nomeado no GitHub — `github:<login>` —, e a hora da mensagem
  é a hora do comentário, de modo que a cronologia se mantém. Uma importação repetida acrescenta
  **apenas as novas** mensagens: as já transferidas são reconhecidas pelo id externo.
* A issue do GitHub não tem **prazo**. Se ela estiver atribuída a um **marco (milestone)** com
  data, essa data vira o prazo da tarefa do AI2P.
* **Não se criam duplicatas**: o id interno da issue é lembrado na tarefa do AI2P (`github:<id>`);
  uma importação repetida ignorará ou atualizará a mesma issue — conforme a caixa de seleção.
* **Os arquivos anexados à issue do GitHub não são baixados.** O GitHub não tem uma lista de
  anexos à parte — as imagens e os arquivos vivem como links dentro do próprio texto, e como
  links eles permanecem. Em um repositório público esse link abre e a imagem aparece; um link
  para um arquivo de repositório **privado** não abrirá sem estar logado no GitHub.
* Rótulos, responsáveis e o estado da issue do GitHub **não são transferidos**: eles são
  conduzidos no AI2P.

## 5. Importação de uma issue pelo link

Ao lado do botão de importação em massa, na lista de tarefas, há o botão **«Importação de uma
tarefa por URL»**:

1. Cole o link no formato `https://github.com/<proprietário>/<repositório>/issues/<número>`
   (serve também um endereço com cauda — `#issuecomment-…` e `?…` são descartados). A origem só é
   perguntada se houver várias origens ativas do GitHub adequadas: **o tipo da origem é
   determinado pelo próprio link**, por isso um endereço do GitHub colado não irá parar no
   importador do Trello.
2. O conteúdo da issue será colocado **no formulário da nova tarefa**: título, descrição, prazo.
3. Complete o formulário (executor, prioridade) e salve — a discussão irá para o chat da tarefa
   logo depois de salvar. Uma importação repetida da mesma issue não criará duplicata.

Aos agentes de IA a mesma ação está disponível pela ferramenta `import_task_from_url` (código da
ação `AI2P.Tasks.ImportFromUrl` — as regras de segurança deny/confirm são aplicadas como de
costume). Qual procedimento cuidará do link é decidido pelo formato dele, e por isso ao agente
basta uma única ferramenta para todas as origens.

## 6. Atualização da tarefa a partir do GitHub

Uma tarefa que chegou por importação tem preenchido o campo **«Link de importação»** (visível no
cabeçalho do cartão e na seção «Avançado» do formulário da tarefa). Por esse link a tarefa pode
ser **relida da origem**:

1. Abra o cartão da tarefa e clique em **«atualizar»**.
2. Aparecerá uma pergunta com três desfechos:
   * **«Da origem de importação»** — a issue do GitHub é lida de novo, e na tarefa do AI2P entram
     o título, o prazo e **a descrição inteira** atualizados, enquanto as novas mensagens da
     discussão chegam ao chat;
   * **«Somente aqui»** — a releitura comum da tarefa a partir do banco, sem consultar o GitHub;
   * **«Cancelar»**.

O que a atualização **não** toca: critérios de aceitação, executores, estado, prioridade, tags,
subtarefas e vínculos — eles são conduzidos no AI2P, e não no GitHub. **A descrição é reescrita
por inteiro**: é justamente para isso que se executa a atualização a partir da origem — para que
na tarefa fique o que está agora no GitHub. As mensagens da discussão já transferidas não são
duplicadas.

O link de importação pode ser **escrito à mão** — assim se atualiza também uma tarefa criada no
AI2P sem importação; e o campo também pode ser **limpo**, para desvincular a tarefa da origem (o
botão «atualizar» passa a ser uma releitura comum).

## Se algo não funcionar

| Mensagem | Causa e o que fazer |
| --- | --- |
| «O token do GitHub não foi encontrado pela referência …» | O token não foi informado: abra a origem e clique em «Definir o token» (passo 3). O caminho do arquivo de segredos está indicado na própria mensagem. |
| «Este servidor ainda não recebeu a chave da organização…» | O servidor está ligado a uma organização alheia, mas a solicitação não foi confirmada pelo maestro dela — sem a chave da organização não há com que cifrar o valor. Confirme a conexão (Configurações → Servidores). |
| «O GitHub não aceitou o TOKEN…» (resposta `Bad credentials`) | O token está incorreto, foi revogado ou venceu. A mensagem mostra o comprimento e o prefixo do valor informado — por eles se vê se não foi parar outra coisa no campo. Repita o passo 1. |
| «O GitHub negou o acesso…» (403) | O token foi aceito, mas ele não tem direitos sobre o repositório: o de granularidade fina precisa do acesso **Issues: Read** e do próprio repositório na lista «Repository access»; o clássico, do escopo **repo**. |
| «O GitHub está recusando temporariamente: esgotado o limite de requisições» | Disparou o rate limit da API. Espere alguns minutos; verifique se a importação está indo **com token** — sem ele o limite é 80 vezes menor. |
| «O GitHub não encontrou o que foi pedido (404…)» | O proprietário, o nome do repositório ou o número da issue estão errados. Um repositório privado ao qual o token não tem direitos é exibido pelo GitHub como inexistente — é o mesmo 404. |
| «O link não se parece com uma issue do GitHub…» | O endereço não tem o formato certo: é preciso `https://github.com/<proprietário>/<repositório>/issues/<número>`. Um link para **pull request** (`/pull/<número>`) não serve — só se importam issues. |
| A importação foi feita, mas «acrescentadas 0» | O filtro não coincidiu com nenhum repositório, os repositórios não têm issues abertas ou todos os registros abertos eram pull requests. Tente com o filtro vazio. |
| Não foram importados todos os repositórios de uma conta privada | O token foi emitido para **outra** conta: os repositórios privados alheios ele não vê. Verifique pelo botão «Verificar a conexão» — ele nomeia a conta do token e avisa da divergência em relação ao proprietário. |

As mensagens chegam inteiras à própria janela de importação. Os detalhes das requisições ficam
nos logs do aplicativo (`logs/*.jsonl`, registros `GitHubImporter`): por qual referência e de qual
cofre o token foi tomado, qual o comprimento e o prefixo dele (o valor em si nunca vai para o
log) e qual resposta o GitHub devolveu.

## Limites

* Em uma importação são tomadas até **10 páginas de 100 registros** de cada gênero —
  repositórios, issues do repositório, comentários da issue. Isso basta para 1000 repositórios,
  1000 issues abertas por repositório e 1000 comentários por issue; se o limite for atingido,
  isso fica visível no log.
* É suportado o **github.com**. O GitHub Enterprise, com o endereço de servidor próprio dele, no
  momento não é suportado.

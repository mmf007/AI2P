# Claude-Fable-5_cli

**Alocação:** em nuvem, mas a conexão **não é por API, e sim pelo CLI**
**Conexão:** `transport: cli`, comando `claude --permission-mode acceptEdits`,
modelo `claude-fable-5`
**Referência da chave:** vazia — **a chave de API não é necessária**

O mesmo modelo que o Claude-Fable-5, mas executado pelo **Claude Code
CLI** em modo headless. Paga-se não por tokens, e sim por **assinatura**, e por isso, no
faturamento do AI2P, o custo dessas tarefas é igual a zero.

## A chave de API não é necessária

A autorização é feita pela sessão do CLI, e não por chave. Por isso, no perfil o `secretRef`
está vazio, e este modelo não tem o botão «Definir a chave de API».

## O que é preciso no lugar da chave

1. **Instalar o Claude Code** — [instruções oficiais](https://docs.claude.com/en/docs/claude-code/overview).
   Verificação: no console, `claude --version` deve imprimir alguma coisa.
2. **Entrar com a sua assinatura**: `claude login` (o navegador abrirá sozinho). A sessão é
   guardada no perfil do usuário do sistema operacional.
3. Certificar-se de que o `claude` está acessível **pelo PATH do usuário sob o qual o AI2P
   funciona**. Se o AI2P estiver rodando como serviço sob outra conta, o login precisa ser feito
   por ela mesma, senão o agente esbarrará em «não autorizado».
4. Se o executável não estiver no PATH — informe o caminho completo no campo `cliCommand` do
   perfil do modelo (botão «Perfil de conexão» no formulário do modelo).

## Em que difere da variante por API

| | por API | pelo CLI |
|---|---|---|
| Pagamento | por tokens | por assinatura |
| Custo no faturamento | é calculado | 0 |
| Ferramentas | as ferramentas do AI2P (leitura/gravação de arquivos etc.) | **as ferramentas próprias do CLI** |
| Diretório de trabalho | não importa | **a pasta do projeto** — o agente trabalha direto nela |
| Perguntas ao humano | de forma padrão | pelo marcador `AI2P_QUESTION` na resposta, continuação por `--resume` |
| Criação de subtarefas | pela ferramenta `create_task` | pelo marcador `AI2P_SUBTASK` na resposta |
| Leitura de outras tarefas | pelas ferramentas `get_task_by_code`, `get_task_by_url`, `get_task_chat` | pelo marcador `AI2P_GET_TASK` na resposta |
| Arquivos externos da tarefa (imagens de anexos) | pela ferramenta `fetch_file` | pelo marcador `AI2P_GET_FILE` na resposta |
| Movimentação da tarefa na hierarquia | pela ferramenta `move_task` | pelo marcador `AI2P_MOVE_TASK` na resposta |

Uma consequência importante: na conexão pelo CLI as ferramentas do AI2P **não são publicadas**
ao agente — ele usa as dele. As regras de segurança do AI2P (cap. 12 do ET) não se estendem às
ações dele dentro do diretório do projeto, e por isso defina o diretório do projeto de forma
consciente.

### Subtarefas pelo marcador `AI2P_SUBTASK`

O agente CLI não tem a ferramenta `create_task`, e a API do AI2P é fechada por login com cookie
— por isso ele antes não conseguia dividir a tarefa em subtarefas de forma alguma. Agora a
subtarefa é criada por uma **linha na resposta**:

```
AI2P_SUBTASK: {"title": "título", "description": "descrição do trabalho", "skills": ["code-write"], "priority": 20, "acceptance": "critérios de aceitação"}
```

Uma linha — uma subtarefa; `title` e `description` são obrigatórios. Assim que o agente termina
o trabalho, o próprio AI2P executa essas linhas: retira-as do texto da resposta, cria as
subtarefas (o executor é escolhido automaticamente — primeiro a IA, depois um humano), marca a
tarefa-mãe como dividida e acrescenta ao resultado uma seção «Subtarefas» com a lista do que foi
criado. Em seguida, as subtarefas são iniciadas pela fila comum da divisão automática — em
ordem decrescente da prioridade numérica.

Esta é a mesma ação **`AI2P.Tasks.Create`** da ferramenta, e por isso as regras de segurança
funcionam sobre ela: proibição (deny) ou exigência de confirmação (confirm) — e a subtarefa não
é criada, enquanto o motivo da recusa vai para o resultado da tarefa e para o registro de
trabalhos. O marcador só é contado **a partir do início da linha**: uma menção no meio de uma
frase ou dentro do relatório não vira subtarefa.

### Leitura de outras tarefas pelo marcador `AI2P_GET_TASK`

As tarefas do AI2P ficam no banco da organização, não estão nos arquivos do projeto, e a `/api`
é fechada por login com cookie — por isso o agente CLI só enxergava o texto da tarefa dele e o
bloco da tarefa-mãe. Se a pessoa desse na formulação um link para outra tarefa («pegue o texto
do resultado da tarefa …»), o agente não conseguia lê-la. Agora ele a solicita por uma **linha
na resposta**:

```
AI2P_GET_TASK: {"code": "T-15"}
```

Em vez de `code` pode-se indicar `"url"` (um link do tipo `…/task/<id>`) ou `"title"` (busca por
parte do título — voltará a lista das tarefas encontradas, sem os textos). O AI2P executa a
consulta e envia a resposta ao agente **na mensagem seguinte da mesma sessão do CLI**
(`--resume`, como na resposta de um humano a uma pergunta), depois do que ele continua o
trabalho do mesmo ponto. A resposta é sempre completa: o cartão da tarefa (formulação, critérios
de aceitação, estado, resultados-artefatos) **e o chat inteiro** — não é preciso pedir a
correspondência à parte. Podem-se emitir vários marcadores de uma vez; no total, o número de
consultas por tarefa não passa de 10 (proteção contra laços), e depois disso o sistema informa
ao agente que o limite se esgotou.

São as mesmas ações **`AI2P.Tasks.GetByCode` / `GetByUrl` / `FindByTitle` / `GetChat`** das
ferramentas de mesmo nome, e por isso as regras de segurança funcionam como de costume:
proibição ou exigência de confirmação — a tarefa não é lida, e a recusa vai para o agente e para
o registro de trabalhos. Só estão disponíveis as tarefas do mesmo projeto.

## Limitações

| | |
|---|---|
| Contexto | 1 000 000 tokens |
| Resposta máxima | 128 000 tokens |
| Custo | 0 (pago pela assinatura) |

**O CLI não informa o saldo do limite da assinatura** (correção da v1.62), e por isso o próprio
AI2P calcula o consumo — pelos tokens das tarefas dentro de uma janela deslizante. Preencha, no
executor de IA, os campos **«limite de tokens por janela»** e **«janela do limite, h»** (na
assinatura do Claude a janela é de 5 horas): assim, antes de iniciar a tarefa o sistema avisará
que quase não sobrou limite e adiará a partida para o momento em que a janela se liberar (em vez
disso, pode-se dividir a tarefa ou iniciá-la à força). Campos vazios — não há verificações, tudo
funciona como antes. E se o limite chegar já no meio da tarefa, o AI2P reconhece a mensagem do
CLI (`5-hour limit reached ∙ resets 4:10pm (…)`), tira dela a hora da reposição e passa a tarefa
para **«aguarda»** até essa hora, em vez de «parou com erro» — depois ela começa sozinha. Mais
detalhes no documento Claude-Opus-5.0_cli.

**Quanto esperar pela resposta** (correção da v1.63) — o campo **«Tempo limite de resposta,
min»** no formulário do executor: vazio — 30 minutos, um número — esses tantos minutos, **0 —
sem limite**. Estourou o tempo limite — a tarefa cai com erro e uma dica; o executor, no
entanto, não é marcado como ocupado (o silêncio do agente não é considerado limite).

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | **o resultado é seu**: a Anthropic transfere a você os direitos dela sobre os Outputs (Consumer Terms, item 4) |
| Uso comercial | permitido; a ressalva «apenas pessoal e não comercial» se refere ao acesso de demonstração, e não à assinatura paga |
| O que é obrigatório | respeitar a Usage Policy; lembrar que, pelas condições de consumo, os materiais vão para o treinamento dos modelos enquanto você não recusar nas configurações da conta |
| Texto das condições | <https://www.anthropic.com/legal/consumer-terms> (redação de 08.10.2025) |
| Pagamento pela geração | por assinatura, e não por tokens — no faturamento do AI2P essas tarefas custam 0 |

Aqui é importante não confundir dois contratos. A assinatura do Claude é regida pelas condições
**de consumo**; a chave de API, pelas **comerciais**
(<https://www.anthropic.com/legal/commercial-terms>), e nelas não há treinamento com os seus
dados de forma alguma.

Consequência prática: se a tarefa envolver código ou dados alheios sob acordo de
confidencialidade, a variante por chave de API é mais segura do que a de assinatura — ou então
recuse o treinamento nas configurações da conta Claude.

As condições foram conferidas pelo texto delas em 27.08.2026.

## Requisitos de hardware

Nenhum em especial: quem calcula é o fornecedor. É preciso ter o Claude Code instalado, saída
para a internet e espaço suficiente no diretório do projeto — o agente trabalha com os arquivos
diretamente.

## Erros frequentes

* **«claude não encontrado»** — o CLI não está instalado ou não está no PATH do usuário do AI2P.
* **Recusa silenciosa ou pedido de login** — a sessão do CLI não foi criada ou foi criada sob
  outra conta do sistema operacional; repita `claude login` sob o usuário correto.
* **O agente não enxerga os arquivos da tarefa** — o projeto não tem diretório definido (campo
  «Pasta do projeto»): é justamente ele que serve de diretório de trabalho do processo do CLI.

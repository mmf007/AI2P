# Claude-Opus-5.0_cli

**Alocação:** em nuvem, conexão **pelo CLI**
**Conexão:** `transport: cli`, comando `claude --permission-mode acceptEdits`,
modelo `claude-opus-5`
**Referência da chave:** vazia — **a chave de API não é necessária**

O Claude-Opus-5.0 executado pelo **Claude Code CLI** em modo
headless. O pagamento é por assinatura e, no faturamento do AI2P, o custo dessas tarefas é
igual a zero.

## A chave de API não é necessária

A autorização é feita pela sessão do CLI. A configuração é exatamente a mesma do
Claude-Fable-5_cli:

1. instalar o [Claude Code](https://docs.claude.com/en/docs/claude-code/overview);
2. executar `claude login` **sob o usuário do sistema operacional em que o AI2P funciona**;
3. certificar-se de que o `claude` está acessível pelo PATH dele (caso contrário, informar o
   caminho completo em `cliCommand` no perfil do modelo).

A configuração é comum aos dois modelos CLI: fazendo-a uma vez, você liga os dois.

## Limitações

| | |
|---|---|
| Contexto | 1 000 000 tokens |
| Resposta máxima | 128 000 tokens |
| Custo | 0 (pago pela assinatura) |

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

Nenhum em especial: quem calcula é o fornecedor. É preciso ter o Claude Code instalado e saída
para a internet.

## Limite da assinatura (correção da v1.62)

O CLI não informa o saldo do limite da assinatura: nem em `claude --help`, nem na saída JSON da
tarefa ele existe, e o `/usage` só funciona em sessão interativa. Por isso quem calcula o
consumo é o próprio AI2P — pelos tokens das tarefas dentro de uma janela deslizante. Para que
isso funcione, preencha no executor de IA (Executores → formulário do executor) dois campos:

* **limite de tokens por janela** — quantos tokens você está disposto a gastar por janela; o
  valor se descobre na prática, conforme a sua assinatura;
* **janela do limite, h** — na assinatura do Claude a janela é de **5 horas**.

Campos vazios — tudo funciona como antes: o saldo não é calculado e não há avisos. Preenchidos —
antes de iniciar a tarefa o AI2P verifica o saldo e, se ele estiver quase no fim, adia a partida
para o momento em que a janela se liberar, com uma mensagem no chat da tarefa (em vez disso, a
tarefa pode ser dividida em subtarefas ou iniciada à força). A partida adiada fica guardada na
tarefa: o computador pode ser desligado; depois de ligado, a tarefa começará sozinha.

**Se o limite chegar mesmo assim no meio do trabalho** (correção da v1.62), o CLI responde com
uma mensagem curta do tipo `5-hour limit reached ∙ resets 4:10pm (Europe/Moscow)`. O AI2P a
reconhece, tira dela a hora da reposição e **não considera isso um erro da tarefa**: a tarefa é
encerrada com um artefato de explicação, o executor é marcado como «ocupado até» e a tarefa
passa para **«aguarda»** — no cabeçalho do cartão aparece a etiqueta «Aguarda até <hora>» e no
chat surge uma mensagem. Quando a hora chegar, a tarefa começará sozinha — **como uma tarefa
nova, do início** (tudo o que o agente conseguiu gravar nos arquivos do projeto permanece no
lugar). Não é preciso esperar junto ao computador: ele pode ser desligado. Se não houver hora de
reposição na mensagem — espera-se uma hora.

## Tempo limite de resposta (correção da v1.63)

O agente CLI conduz o ciclo dele sozinho e, durante o trabalho, **fica calado** — o AI2P só
enxerga o momento em que o processo termina. Quanto esperar por ele é definido pelo campo
**«Tempo limite de resposta, min»** no formulário do executor:

* **vazio** — 30 minutos (o padrão do sistema, como estava fixado no código antes da 1.63);
* **um número** — esses tantos minutos;
* **0** — esperar sem limite; só o botão «parar» interrompe a tarefa.

Estourou o tempo limite — a tarefa é encerrada com **erro** e uma dica de onde aumentar o valor,
e a tarefa passa para «parou com erro». O executor, no entanto, **não é marcado como ocupado**:
até a 1.63 o silêncio do agente era considerado limite de assinatura esgotado, e o executor
ficava uma hora fora de serviço, ainda que o limite pudesse estar gasto em apenas um terço. O
limite de verdade o AI2P continua reconhecendo pela mensagem do próprio CLI (veja a seção
acima).

## O que lembrar sobre a conexão pelo CLI

* o agente trabalha com as **ferramentas próprias do CLI**, e as ferramentas do AI2P não lhe são
  publicadas;
* o diretório de trabalho do processo é a **pasta do projeto**, por isso ela precisa estar
  definida;
* as regras de segurança do AI2P não se estendem às ações do agente dentro desse diretório;
* as perguntas ao humano são transmitidas pelo marcador `AI2P_QUESTION` na resposta, e a
  continuação do diálogo se dá na mesma sessão do CLI (`--resume`);
* as subtarefas o agente cria pelo marcador `AI2P_SUBTASK` na resposta — em vez da ferramenta
  `create_task`, que ele não tem (veja Claude-Fable-5_cli);
* mover uma tarefa na hierarquia (trocar o pai, levar para a raiz) o agente consegue pelo
  marcador `AI2P_MOVE_TASK` — em vez da ferramenta `move_task`;
* os textos de outras tarefas (descrição, resultado e todo o chat) o agente solicita pelo
  marcador `AI2P_GET_TASK` — em vez das ferramentas `get_task_by_code` / `get_task_by_url` /
  `get_task_chat`, que ele também não tem.

Mais detalhes sobre as diferenças entre API e CLI estão no documento Claude-Fable-5_cli.

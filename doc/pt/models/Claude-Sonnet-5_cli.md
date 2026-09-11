# Claude-Sonnet-5_cli

**Alocação:** em nuvem, mas a conexão **não é por API, e sim pelo CLI**
**Conexão:** `provider: anthropic`, `transport: cli`, comando
`claude --permission-mode acceptEdits`, modelo `claude-sonnet-5`
**Referência da chave:** vazia — **a chave de API não é necessária**

O mesmo modelo que o Claude-Sonnet-5, mas executado pelo **Claude Code
CLI** em modo headless. Paga-se não por tokens, e sim por **assinatura**, e por isso, no
faturamento do AI2P, o custo dessas tarefas é igual a zero. O nicho é o trabalho de volume com
os arquivos do projeto, quando não se quer pagar por tokens.

## A chave de API não é necessária

A autorização é feita pela sessão do CLI, e não por chave. Por isso, no perfil o `secretRef`
está vazio, e este modelo não tem o botão «Definir a chave de API».

O que é preciso no lugar da chave:

1. **Instalar o Claude Code** — [instruções oficiais](https://docs.claude.com/en/docs/claude-code/overview).
   Verificação: no console, `claude --version` deve imprimir alguma coisa.
2. **Entrar com a sua assinatura**: `claude login`. A sessão é guardada no perfil do usuário do
   sistema operacional.
3. Certificar-se de que o `claude` está acessível **pelo PATH do usuário sob o qual o AI2P
   funciona**. Se o AI2P estiver rodando como serviço sob outra conta, o login precisa ser feito
   por ela.
4. Se o executável não estiver no PATH — informe o caminho completo no campo `cliCommand` do
   perfil do modelo.

## Em que difere da variante por API

| | por API | pelo CLI |
|---|---|---|
| Pagamento | por tokens | por assinatura |
| Custo no faturamento | é calculado | 0 |
| Ferramentas | as ferramentas do AI2P | **as ferramentas próprias do CLI** |
| Diretório de trabalho | não importa | **a pasta do projeto** |
| Perguntas ao humano | de forma padrão | pelo marcador `AI2P_QUESTION` na resposta |
| Criação de subtarefas | pela ferramenta `create_task` | pelo marcador `AI2P_SUBTASK` |

A análise detalhada das diferenças está no documento Claude-Fable-5_cli;
no Sonnet tudo é organizado exatamente da mesma forma.

## Limitações e custo

| | |
|---|---|
| Contexto | 1 000 000 tokens |
| Resposta máxima | 128 000 tokens |
| Custo | 0 (pago pela assinatura) |

**O AI2P reconhece o limite da assinatura e espera a reposição** (correção da v1.63): ao ver uma
recusa do CLI com código 429, o sistema pega a hora da reposição e passa a tarefa para «aguarda»
em vez de «parou com erro» e, na reposição, **continua a mesma sessão** (`--resume`), em vez de
começar a tarefa do zero. Preencha, no executor de IA, os campos «limite de tokens por janela» e
«janela do limite, h» (na assinatura do Claude a janela é de 5 horas), para que o sistema avise
com antecedência.

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
para a internet e espaço no diretório do projeto — o agente trabalha com os arquivos
diretamente.

## Erros frequentes

* **«claude não encontrado»** — o CLI não está instalado ou não está no PATH do usuário do AI2P.
* **Recusa silenciosa ou pedido de login** — a sessão foi criada sob outra conta do sistema
  operacional.
* **O agente não enxerga os arquivos da tarefa** — o projeto não tem diretório definido («Pasta
  do projeto»).

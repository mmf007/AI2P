# Claude-Opus-5.5_cli

**Alojamento:** nuvem, mas a ligação é **pelo CLI, não pela API**
**Ligação:** `provider: anthropic`, `transport: cli`, comando
`claude --permission-mode acceptEdits`, modelo `claude-opus-5-5`
**Referência da chave:** vazia — **não é preciso chave de API**

O mesmo modelo que Claude-Opus-5.5, mas executado pelo **Claude Code CLI**
em modo headless. Paga-se por **subscrição**, não por tokens, pelo que na faturação do AI2P
estas tarefas custam zero.

O modelo maior da linha Opus, lançado depois do Opus 5: mais forte e mais barato na API. O seu nicho: código difícil, análise de requisitos e tarefas longas sobre os ficheiros do projeto.

## Porquê um registo por versão

No Claude Code CLI a versão do modelo é escolhida pelo campo **«modelo»** do perfil: vai para
o CLI como o flag `--model`. Por isso cada versão que queira poder escolher para um executor
tem **o seu próprio registo do catálogo**, e os registos coexistem: Claude-Opus-5.0_cli mantém
o seu identificador e este funciona com `claude-opus-5-5`.

São aceites duas formas do valor (`claude --help`):

* o **nome completo da versão** — `claude-opus-5-5`;
* um **alias da última versão** — `opus`.

Os registos da distribuição levam o nome completo: com o tempo o alias passa em silêncio
para uma versão nova (verificado com uma chamada real a 24.09.2026: `fable` já significa
`claude-fable-5-1`) e o registo deixa de significar o que o nome diz.

## Não é preciso chave de API

A autorização é pela sessão do CLI: `secretRef` está vazio e o registo não tem botão
«Definir chave de API». O que é preciso em vez disso:

1. **Instalar o Claude Code** — [guia oficial](https://docs.claude.com/en/docs/claude-code/overview).
   Verificação: `claude --version` tem de imprimir algo.
2. **Entrar com a sua subscrição**: `claude login`.
3. Garantir que `claude` está no **PATH do utilizador com que o AI2P corre**.
4. Se o executável não estiver no PATH, escreva o caminho completo no campo `cliCommand`.

A análise detalhada das diferenças «pela API / pelo CLI» está no documento
Claude-Fable-5_cli; aqui tudo funciona da mesma maneira.

## A prova verifica também o modelo

O botão de prova (alteração v1.144) faz três coisas: executa `claude --version`, consulta o
estado da sessão e **verifica o próprio identificador do modelo com uma chamada curta**. Um id
desconhecido ou a que a subscrição não dá acesso aparece logo como falha da prova, e não como
uma tarefa que cai minutos depois. Se o CLI responder com outro modelo, o AI2P indica-o com
um **evento do registo da tarefa** e com uma linha no resumo do resultado.

## Limites e custo

| | |
|---|---|
| Contexto | 1 000 000 tokens |
| Saída máxima | 128 000 tokens |
| Custo | 0 (pago por subscrição) |

**O AI2P reconhece o limite da subscrição e espera a reposição**: perante um 429 lê a hora da
reposição, passa a tarefa a «à espera» e depois continua a mesma sessão.

## Licença

| | |
|---|---|
| Condições sobre o resultado | **o resultado é seu**: a Anthropic cede-lhe os seus direitos sobre os Outputs (Consumer Terms, §4) |
| Uso comercial | permitido |
| O que é obrigatório | cumprir a Usage Policy; lembrar que nas condições de consumo o seu material é usado para treinar modelos até o desativar na conta |
| Texto das condições | <https://www.anthropic.com/legal/consumer-terms> |
| Pagamento da geração | por subscrição, não por tokens — na faturação do AI2P custam 0 |

A subscrição do Claude rege-se pelas condições **de consumo**; a chave de API pelas
**comerciais** (<https://www.anthropic.com/legal/commercial-terms>), que não incluem treino
com os seus dados.

## Requisitos de hardware

Nenhum em especial: calcula o fornecedor. São precisos o Claude Code instalado, saída para a
internet e espaço na pasta do projeto.

## Erros frequentes

* **«claude não encontrado»** — o CLI não está instalado ou não está no PATH do utilizador do AI2P.
* **«O Claude CLI não aceitou o modelo»** — o identificador está desatualizado ou a subscrição
  não dá acesso a essa versão; confronte o campo «modelo» com `claude --help`.
* **O agente não vê os ficheiros da tarefa** — o projeto não tem pasta definida.

## É preciso um Claude Code recente

Este modelo apareceu depois do próprio CLI, por isso um **Claude Code antigo não o conhece**.
Na versão 2.1.278 a chamada era recusada:

```
[claude-code:unrecognized_model]: Claude Code 2.1.278 does not support this model;
version 2.1.280 or newer is required
```

É preciso **Claude Code 2.1.280 ou mais recente** (verificado com uma chamada real a 24.09.2026
em 2.1.281: código de saída 0, respondeu claude-opus-5-5). Atualize com `claude update` e
verifique com `claude --version`. Se a prova do registo reclamar do modelo, comece pela versão
do CLI.

A mesma execução mostra porque os registos da distribuição usam o **nome completo**: o alias
`opus` já passou de `claude-opus-5` para `claude-opus-5-5`, ou seja, significaria coisas
diferentes em máquinas diferentes. O registo Claude-Opus-5.0_cli continua em `claude-opus-5`.

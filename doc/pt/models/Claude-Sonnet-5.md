# Claude-Sonnet-5

**Alocação:** em nuvem (API da Anthropic)
**Conexão:** `provider: anthropic`, modelo `claude-sonnet-5`
**Referência da chave:** `anthropic.apiKey`

O cavalo de batalha da linha Claude 5: a qualidade é próxima da do Claude-Opus-5.0
(notas de habilidade de 89 a 93 contra 95 a 98), e o preço é duas vezes menor e **constante** —
2 $ por 1 milhão de tokens de entrada e 10 $ por 1 milhão de saída, sem descontos por hora do
dia. Uma escolha sensata quando o Opus é excessivo e o Claude-Haiku-4.5 já não dá conta.

## Como obter a chave

1. Crie uma organização em [console.anthropic.com](https://console.anthropic.com/).
2. Coloque saldo: **Billing → Add credits**. Sem saldo, as requisições são recusadas.
3. Abra **API keys → Create Key** e dê um nome.
4. Copie o valor — ele é mostrado **uma única vez**. A chave começa com `sk-ant-`.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Claude-Sonnet-5 → «Definir a chave de API»**.

A chave é comum a todos os modelos da Anthropic — Claude-Fable-5,
Claude-Opus-5.0, Claude-Haiku-4.5: todos eles
referenciam `anthropic.apiKey`. Informou uma vez — todos funcionam.

A chave pertence à organização: é cifrada com a chave dela e replicada para todos os servidores
da organização (cap. 10 do ET).

## Limitações e custo

| | |
|---|---|
| Contexto | 1 000 000 tokens |
| Resposta máxima | 128 000 tokens |
| Custo de entrada | 2,00 $ por 1 milhão de tokens |
| Custo de saída | 10,00 $ por 1 milhão de tokens |

No perfil consta `params.maxTokens: 16000` e `effort: high` — isso é a limitação de **uma
resposta**, e não do contexto; aumente se o resultado for cortado com o motivo `length`.

**O identificador do modelo não foi verificado com uma chamada real.** Antes da primeira tarefa,
confira-o com a requisição `GET /models` à API do fornecedor: na Anthropic os nomes dos
checkpoints mudam, e um id errado dá 404 já no executor. Os preços atuais estão em
[Anthropic Pricing](https://www.anthropic.com/pricing).

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | **o resultado é seu**: a Anthropic transfere a você todos os direitos dela sobre os Outputs (Commercial Terms, seção B) |
| Uso comercial | permitido — estas são as condições para organizações, as de consumo não se aplicam à API |
| O que é obrigatório | respeitar a Usage Policy; não se pode construir sobre o serviço um produto concorrente, treinar nele modelos concorrentes nem revender acesso |
| Texto das condições | <https://www.anthropic.com/legal/commercial-terms> (redação de 17.06.2025) |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

Os pesos do modelo são fechados e não são distribuídos a ninguém — aqui não há o que licenciar,
por isso as condições se referem ao **resultado da geração**, e não aos pesos.

Por essas mesmas condições, a Anthropic **não treina modelos com o que vai para a API**
(«Anthropic may not train models on Customer Content from Services»). Na variante do mesmo
modelo por assinatura (o registro com o sufixo `_cli`) as condições são OUTRAS — ali valem as de
consumo, e o treinamento com os materiais acontece enquanto você não recusar nas configurações
da conta.

As condições foram conferidas pelo texto delas em 27.08.2026; o fornecedor tem o direito de
alterá-las — antes de um lançamento comercial abra o link mais uma vez.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`api.anthropic.com`.

## Erros frequentes

* **401 / «invalid x-api-key»** — a chave foi informada com espaço ou está truncada.
* **400 «credit balance is too low»** — o saldo da organização na Anthropic não foi reposto.
* **Resposta vazia, motivo `length`** — o `params.maxTokens` do perfil do modelo está pequeno.
* **A tarefa sai mais cara do que o esperado** — faça as contas pelos dois números: um histórico
  de chat longo vai para a entrada a cada passo do agente.

# Claude-Opus-5.0

**Alocação:** em nuvem (API do fornecedor Anthropic)
**Conexão:** `provider: anthropic`, modelo `claude-opus-5`
**Referência da chave:** `anthropic.apiKey`

O cavalo de batalha: bem mais barato que o Claude-Fable-5 com qualidade próxima na maioria das
tarefas. É por ele também que o Claude-Fable-5 se resguarda quando os classificadores de
segurança recusam.

## Como obter a chave

**A chave é a mesma** do Claude-Fable-5 — os dois modelos vão ao mesmo fornecedor e
referenciam o mesmo segredo `anthropic.apiKey`. Se a chave já estiver informada para um deles,
o segundo funcionará sozinho.

Se ainda não houver chave:

1. Registre-se no [Anthropic Console](https://console.anthropic.com/).
2. Coloque saldo: **Plan & Billing → Add credits**.
3. **Settings → API keys → Create Key**, copie o valor (mostrado uma única vez,
   começa com `sk-ant-`).
4. No AI2P: **Configurações → Catálogos → Modelos de IA → Claude-Opus-5.0 → «Definir a chave de API»**.

A chave pertence à organização e é replicada para os servidores dela de forma cifrada (cap. 10
do ET).

## Limitações e custo

| | |
|---|---|
| Contexto | 1 000 000 tokens |
| Resposta máxima | 128 000 tokens |
| Custo de entrada | 5,00 $ por 1 milhão de tokens |
| Custo de saída | 25,00 $ por 1 milhão de tokens |

Duas vezes mais barato que o Claude-Fable-5. Os preços atuais estão em
[Anthropic Pricing](https://www.anthropic.com/pricing).

**O identificador do modelo não foi verificado com uma chamada real.** Antes da primeira tarefa,
confira-o com a requisição `GET /models` à API do fornecedor: na Anthropic os nomes dos
checkpoints mudam, e um id errado dá 404 já no executor.

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

## Quando pegar cada um dos dois

* **Claude-Fable-5** — as tarefas de comando do processo, análise de requisitos, planejamento,
  código complexo.
* **Claude-Opus-5.0** — todo o resto: correções, documentação, traduções, revisões.

A escolha não precisa ser feita à mão: o projeto tem o controle deslizante **preço ↔ qualidade**,
e a seleção automática do executor leva em conta tanto as notas de habilidade do modelo quanto o
custo dele (ET, item 2.7).

## Alternativa sem chave

**Claude-Opus-5.0_cli** — o mesmo modelo pelo Claude Code CLI por assinatura, sem chave de API.

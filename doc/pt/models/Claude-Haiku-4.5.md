# Claude-Haiku-4.5

**Alocação:** em nuvem (API da Anthropic)
**Conexão:** `provider: anthropic`, modelo `claude-haiku-4-5`
**Referência da chave:** `anthropic.apiKey`

O Claude mais barato do catálogo: 1 $ e 5 $ por milhão de tokens contra 2 $ e 10 $ do
Claude-Sonnet-5. As notas de habilidade de 70 a 80 significam **rotina curta**:
traduções, resumos, pequenas correções de texto, verificações simples. Não vale pegá-lo para
código complexo e análise de requisitos — para isso existem o Sonnet e o Opus.

A segunda limitação é o contexto de **200 000 tokens** em vez de um milhão: uma tarefa com
descrição muito grande ou histórico de chat longo este modelo não aguenta.

## Como obter a chave

1. Crie uma organização em [console.anthropic.com](https://console.anthropic.com/).
2. Coloque saldo: **Billing → Add credits**.
3. Abra **API keys → Create Key** e dê um nome.
4. Copie o valor — ele é mostrado **uma única vez** (começa com `sk-ant-`).
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Claude-Haiku-4.5 → «Definir a chave de API»**.

A chave é comum a todos os modelos da Anthropic (`anthropic.apiKey`): informou uma vez —
funcionam o Fable, o Opus, o Sonnet e o Haiku.

## Limitações e custo

| | |
|---|---|
| Contexto | 200 000 tokens |
| Resposta máxima | 64 000 tokens |
| Custo de entrada | 1,00 $ por 1 milhão de tokens |
| Custo de saída | 5,00 $ por 1 milhão de tokens |

**O identificador do modelo não foi verificado com uma chamada real.** Antes da primeira tarefa,
confira-o com a requisição `GET /models` à API do fornecedor. Os preços atuais estão em
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

* **Entrada longa demais** — 200 000 tokens acabam mais rápido do que parece: leve os materiais
  volumosos para arquivos e coloque links na descrição.
* **Resultado nitidamente pior do que o esperado** — a tarefa é mais complexa que o nicho do
  modelo; defina o executor à mão ou desloque o controle «preço ↔ qualidade» do projeto para a
  qualidade.
* **401 / «invalid x-api-key»** — a chave foi informada com espaço ou está truncada.

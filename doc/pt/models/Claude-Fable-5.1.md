# Claude-Fable-5.1

**Alocação:** em nuvem (API do fornecedor Anthropic)
**Conexão:** `provider: anthropic`, modelo `claude-fable-5-1`
**Referência da chave:** `anthropic.apiKey`

A Anthropic lançou este modelo em 01.09.2026; ele entrou no catálogo do AI2P pela tarefa
T-216-S0, a revisão de fabricantes de 11.09.2026. O identificador `anthropic/claude-fable-5.1`, o tamanho do
contexto, o preço e as modalidades foram verificados por uma consulta ao catálogo
público OpenRouter.

Dá continuidade à linha **Claude-Fable-5**: contexto de 1 000 000 tokens, resposta de até 128 000
tokens, 10,00 $ e 50,00 $ por milhão de tokens. Pontuações de habilidades 94-99.

Entrada aceita: texto e Markdown, código-fonte, imagens, PDF.

## Como obter a chave

1. Registre-se no [Anthropic Console](https://console.anthropic.com/).
2. Coloque saldo: **Plan & Billing → Add credits**. Sem saldo positivo a chave é criada, mas as
   requisições são recusadas com um erro de fundos insuficientes.
3. Abra **Settings → API keys → Create Key** e dê à chave um nome compreensível
   (por exemplo `AI2P-trabalho`).
4. Copie o valor — ele é mostrado **uma única vez**. A chave começa com `sk-ant-`.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Claude-Fable-5.1 → «Definir a chave de API»**.
   O valor é digitado em um campo de senha e não é mostrado em mais lugar nenhum.

A chave pertence à **organização**: ela é cifrada com a chave da organização e replicada para
todos os servidores dela, e por isso não é preciso informá-la em cada servidor. Os detalhes
estão no cap. 10 do ET.

Assim que a chave estiver definida, o modelo se torna ativo. Sem chave, um modelo em nuvem não
pode ficar ativo.

## Limitações e custo

| | |
|---|---|
| Contexto | 1 000 000 tokens |
| Resposta máxima | 128 000 tokens |
| Custo de entrada | 10,00 $ por 1 milhão de tokens |
| Custo de saída | 50,00 $ por 1 milhão de tokens |

O custo de cada tarefa é calculado por esses números e vai para o faturamento (ET, item
6.2-bis). Veja os preços atuais na página [Anthropic Pricing](https://www.anthropic.com/pricing)
— se eles tiverem mudado, corrija o `cost` na declaração de capacidades do modelo.

**O identificador do modelo não foi verificado com uma chamada real.** Antes da primeira tarefa,
confira-o com a requisição `GET /models` à API do fornecedor: na Anthropic os nomes dos
checkpoints mudam, e um id errado dá 404 já no executor.

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | **o resultado é seu**: a Anthropic transfere a você todos os direitos dela sobre os Outputs (Commercial Terms, seção B) |
| Uso comercial | permitido — estas são as condições para organizações, as de consumo não se aplicam à API |
| O que é obrigatório | respeitar a Usage Policy; não se pode construir sobre o serviço um produto concorrente, treinar nele modelos concorrentes nem revender acesso |
| Texto das condições | <https://www.anthropic.com/legal/commercial-terms> (redação de 11.09.2026) |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

Os pesos do modelo são fechados e não são distribuídos a ninguém — aqui não há o que licenciar,
por isso as condições se referem ao **resultado da geração**, e não aos pesos.

Por essas mesmas condições, a Anthropic **não treina modelos com o que vai para a API**
(«Anthropic may not train models on Customer Content from Services»). Na variante do mesmo
modelo por assinatura (o registro com o sufixo `_cli`) as condições são OUTRAS — ali valem as de
consumo, e o treinamento com os materiais acontece enquanto você não recusar nas configurações
da conta.

As condições foram conferidas pelo texto delas em 11.09.2026; o fornecedor tem o direito de
alterá-las — antes de um lançamento comercial abra o link mais uma vez.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso apenas ter saída para a internet até
`api.anthropic.com`.

## Erros frequentes

* **«Chave de API não encontrada»** — a chave não foi informada ou este servidor ainda não
  recebeu a chave da organização (ela é entregue na confirmação da conexão do servidor à
  organização).
* **401 vindo do fornecedor** — a chave foi revogada ou copiada de forma incompleta.
* **«37529 tokens exceeds context»** — tarefa com uma descrição enorme; reduza a entrada ou
  aumente o `params.maxTokens` no perfil.
* **Recusa dos classificadores de segurança** — no perfil está definido o resguardo
  `params.fallbacks: ["claude-opus-5"]`: a requisição é repetida automaticamente por outro
  modelo.

## Alternativa sem chave

Existe uma variante da mesma família com conexão pelo CLI — o **Claude-Fable-5_cli**: ele
funciona pela assinatura do Claude Code e não exige chave de API. Veja o documento dele.

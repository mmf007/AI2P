# Grok-4.7

**Alocação:** em nuvem (API da xAI, compatível com OpenAI)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://api.x.ai/v1`,
modelo `grok-4.7`
**Referência da chave:** `xai.apiKey`

A xAI lançou este modelo em 21.09.2026; ele entrou no catálogo do AI2P pela tarefa
T-347-S0, a revisão de fabricantes de 23.09.2026. O identificador `x-ai/grok-4.7`, o tamanho do
contexto, o preço e as modalidades foram verificados por uma consulta ao catálogo
público OpenRouter.

Dá continuidade à linha **Grok-4.6**: contexto de 500 000 tokens, resposta de até 64 000
tokens, 1,60 $ e 4,80 $ por milhão de tokens. Pontuações de habilidades 89-93.

Entrada aceita: texto e Markdown, código-fonte, imagens, PDF.

## Como obter a chave

1. Registre-se no [console da xAI](https://console.x.ai/).
2. Crie uma equipe (team) e coloque saldo — sem saldo as requisições são recusadas.
3. Abra **API Keys → Create API Key**, dê um nome e direitos sobre os modelos de chat.
4. Copie o valor — ele é mostrado **uma única vez**. A chave começa com `xai-`.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Grok-4.7 → «Definir a chave de API»**.

A chave pertence à organização: é cifrada com a chave dela e replicada para todos os servidores
da organização (cap. 10 do ET).

## Limitações e custo

| | |
|---|---|
| Contexto | 500 000 tokens |
| Resposta máxima | 64 000 tokens |
| Custo de entrada | 1,60 $ por 1 milhão de tokens |
| Custo de saída | 4,80 $ por 1 milhão de tokens |

**O identificador do modelo não foi verificado com uma chamada real** — ele foi tomado do
catálogo de modelos sem o prefixo do fornecedor. Confira-o com a requisição `GET /models` a
`https://api.x.ai/v1`. Os preços atuais estão em [xAI Pricing](https://x.ai/api).

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | **o resultado é seu**: «Customer … owns all right, title, and interest in the Output», a xAI transfere a você os direitos dela |
| Uso comercial | permitido |
| O que é obrigatório | não treinar outros modelos com o resultado sem permissão à parte e não apresentá-lo como criado por um humano |
| Texto das condições | <https://x.ai/legal/terms-of-service-enterprise> (condições para o acesso por API) |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

Os pesos são fechados — trata-se do **resultado da geração**.

Pelas condições para a API, a xAI **não usa o que foi enviado e recebido para treinar os modelos
dela**. No Grok de consumo (site e aplicativo) as condições são outras: ali os dados vão para o
treinamento e, ao usar o resultado junto com o nome e os sinais da xAI, exige-se um link para o
serviço. O AI2P vai com a chave de API, e por isso valem as condições do link acima.

As condições foram conferidas pelo texto delas em 23.09.2026.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`api.x.ai`.

## Erros frequentes

* **Fatura maior que a calculada** — provavelmente a tarefa cruzou o limiar de tamanho a
  partir do qual a xAI cobra tarifa majorada (não publicada para a 4.7). Mantenha o
  histórico do chat mais curto ou leve os materiais para arquivos.
* **O modelo «não sabe» de um evento recente** — a xAI não anunciou a fronteira do
  conhecimento da 4.7; dê os dados recentes na própria tarefa.
* **403 / «no credits»** — o saldo da equipe na xAI não foi reposto.

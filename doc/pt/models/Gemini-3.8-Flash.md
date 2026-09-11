# Gemini-3.8-Flash

**Alocação:** em nuvem (Google, por uma camada compatível com OpenAI)
**Conexão:** `provider: openai-compatible`,
`baseUrl: https://generativelanguage.googleapis.com/v1beta/openai`, modelo `gemini-3.8-flash`
**Referência da chave:** `google.apiKey`

A Google lançou este modelo em 02.09.2026; ele entrou no catálogo do AI2P pela tarefa
T-216-S0, a revisão de fabricantes de 11.09.2026. O identificador `google/gemini-3.8-flash`, o tamanho do
contexto, o preço e as modalidades foram verificados por uma consulta ao catálogo
público OpenRouter.

Dá continuidade à linha **Gemini-3.7-Flash**: contexto de 1 048 576 tokens, resposta de até 65 536
tokens, 0,75 $ e 3,75 $ por milhão de tokens. Pontuações de habilidades 81-88.

Entrada aceita: texto e Markdown, código-fonte, imagens, PDF, áudio, vídeo.

## Como obter a chave

**A chave é a mesma** dos demais Gemini (`google.apiKey`). Se ela já estiver informada —
este modelo funcionará sozinho.

Se ainda não houver chave:

1. Abra o [Google AI Studio](https://aistudio.google.com/apikey) e entre com a conta Google.
2. Clique em **Create API key** e escolha um projeto do Google Cloud.
3. Copie a chave (começa com `AIza`).
4. Vincule o faturamento do projeto, se as tarefas forem longas e frequentes.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Gemini-3.8-Flash → «Definir a chave de API»**.

## Limitações e custo

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Resposta máxima | 65 536 tokens |
| Custo de entrada | 0,75 $ por 1 milhão de tokens |
| Custo de saída | 3,75 $ por 1 milhão de tokens |

No catálogo foram lançados os valores do catálogo público de modelos em 11.09.2026. Antes de gastos sérios,
confira em [Gemini API Pricing](https://ai.google.dev/pricing).

**O identificador do modelo não foi verificado com uma chamada real** — confira-o com a
requisição `GET /models` a `https://generativelanguage.googleapis.com/v1beta/openai`.

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | o Google **não reivindica direitos** sobre o conteúdo gerado («Google won’t claim ownership over generated content») |
| Uso comercial | permitido |
| O que é obrigatório | respeitar a Prohibited Use Policy; a lei pode exigir informar aos seus usuários que o conteúdo foi gerado por IA |
| Texto das condições | <https://ai.google.dev/gemini-api/terms> |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

Os pesos são fechados — as condições se referem ao **resultado da geração**.

Uma diferença importante entre as tarifas: no acesso **pago** (e o AI2P vai com a chave paga) o
Google não usa as suas requisições e respostas para melhorar os produtos dele e as guarda por um
tempo limitado, apenas para verificação de infrações; na cota gratuita — usa. O mesmo resultado
o Google tem o direito de entregar a outra pessoa — não lhe prometem exclusividade.

As condições foram conferidas em 11.09.2026.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`generativelanguage.googleapis.com`.

## Erros frequentes

* **404 «model not found»** — o id mudou; confira com a lista `GET /models`.
* **429 «resource exhausted»** — esbarrou-se na frequência do nível gratuito.
* **Qualidade abaixo do esperado** — a tarefa é mais complexa que o nicho do modelo; desloque o
  controle «preço ↔ qualidade» do projeto para a qualidade ou defina o executor à mão.

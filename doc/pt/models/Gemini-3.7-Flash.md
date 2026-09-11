# Gemini-3.7-Flash

**Alocação:** em nuvem (Google, por uma camada compatível com OpenAI)
**Conexão:** `provider: openai-compatible`,
`baseUrl: https://generativelanguage.googleapis.com/v1beta/openai`, modelo `gemini-3.7-flash`
**Referência da chave:** `google.apiKey`

O modelo barato e rápido da linha: 0,375 $ e 1,875 $ por milhão de tokens — cinco vezes mais
barato que o Gemini-3.1-Pro, com o mesmo contexto de 1 048 576 tokens.
Notas de habilidade de 77 a 86: trabalho em massa com texto (traduções, resumos, rascunhos) e
código simples. Não vale pegá-lo para análise complexa de requisitos e arquitetura.

Assim como os modelos mais graduados da linha, aceita como entrada **áudio e vídeo**
(`audio/*`, `video/*`).

## Como obter a chave

**A chave é a mesma** dos demais Gemini (`google.apiKey`). Se ela já estiver informada —
este modelo funcionará sozinho.

Se ainda não houver chave:

1. Abra o [Google AI Studio](https://aistudio.google.com/apikey) e entre com a conta Google.
2. Clique em **Create API key** e escolha um projeto do Google Cloud.
3. Copie a chave (começa com `AIza`).
4. Vincule o faturamento do projeto, se as tarefas forem longas e frequentes.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Gemini-3.7-Flash → «Definir a chave de API»**.

## Limitações e custo

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Resposta máxima | 65 536 tokens |
| Custo de entrada | 0,375 $ por 1 milhão de tokens |
| Custo de saída | 1,875 $ por 1 milhão de tokens |

O panorama de mercado (relatório T-213) dava outros números para este modelo; no catálogo foram
lançados os valores do catálogo público de modelos em 17.08.2026. Antes de gastos sérios,
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

As condições foram conferidas em 27.08.2026.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`generativelanguage.googleapis.com`.

## Erros frequentes

* **404 «model not found»** — o id mudou; confira com a lista `GET /models`.
* **429 «resource exhausted»** — esbarrou-se na frequência do nível gratuito.
* **Qualidade abaixo do esperado** — a tarefa é mais complexa que o nicho do modelo; desloque o
  controle «preço ↔ qualidade» do projeto para a qualidade ou defina o executor à mão.

# Gemini-3-Ultra

> **Registro desativado em 11.09.2026 (tarefa T-216-S0, revisão de fabricantes).** Esta
> versão tem mais de três versões mais novas da mesma família (3.1, 3.5, 3.6, 3.7, 3.8)
> e o seu identificador já não está no catálogo público de modelos. O registro é
> mantido: executores, tarefas e relatórios anteriores apontam para ele. Pode ser
> reativado pela caixa «ativa»: **Configurações → Catálogos → Modelos de IA**.

**Alocação:** em nuvem (Google, por uma camada compatível com OpenAI)
**Conexão:** `provider: openai-compatible`,
`baseUrl: https://generativelanguage.googleapis.com/v1beta/openai`, modelo `gemini-3-ultra`
**Referência da chave:** `google.apiKey`

**O maior contexto do catálogo — 2 000 000 de tokens**, o dobro do Claude e do GPT. É por isso
que ele é mantido: uma tarefa com descrição enorme, correspondência longa ou um maço inteiro de
materiais anexados passa por inteiro. Notas de habilidade de 90 a 94, e os pontos fortes são
resumos e análise de dados. O preço é alto: 10 $ e 30 $ por milhão de tokens.

Aceita como entrada não só texto e imagens, mas também **áudio e vídeo** (`audio/*`, `video/*`),
e por isso serve para tarefas em que o material chega como gravação.

## Como obter a chave

1. Abra o [Google AI Studio](https://aistudio.google.com/apikey) e entre com a conta Google.
2. Clique em **Create API key** e escolha ou crie um projeto do Google Cloud.
3. Copie a chave (começa com `AIza`).
4. Para a tarifa paga, vincule o faturamento do projeto: o nível gratuito corta a frequência de
   requisições e, em tarefas longas, o agente esbarra em 429.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Gemini-3-Ultra → «Definir a chave de API»**.

A chave é comum a todos os Gemini do catálogo — o Gemini-3.1-Pro e o
Gemini-3.7-Flash referenciam a mesma `google.apiKey`.

## Limitações e custo

| | |
|---|---|
| Contexto | 2 000 000 tokens |
| Resposta máxima | 65 536 tokens |
| Custo de entrada | 10,00 $ por 1 milhão de tokens |
| Custo de saída | 30,00 $ por 1 milhão de tokens |

Duas ressalvas que são mais importantes que os números:

* **O identificador do modelo não foi verificado com uma chamada real** e, no catálogo público
  de modelos, em 17.08.2026, a variante Ultra não existia de forma alguma — o id e o preço foram
  tomados do panorama de mercado. Confira com a requisição `GET /models` a
  `https://generativelanguage.googleapis.com/v1beta/openai`.
* **A completude da compatibilidade com OpenAI da camada do Google não foi verificada com uma
  chamada real.** A camada `/v1beta/openai` cobre as chamadas principais, mas alguns campos
  (ferramentas, streaming) podem se comportar de forma diferente da OpenAI.

Os preços atuais estão em [Gemini API Pricing](https://ai.google.dev/pricing).

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

* **404 «model not found»** — o id não é o certo; confira com a lista `GET /models`.
* **429 em tarefa longa** — está valendo o nível gratuito; vincule o faturamento.
* **Erro nas ferramentas** — uma peculiaridade da camada compatível; experimente o
  Gemini-3.1-Pro ou outro fornecedor.

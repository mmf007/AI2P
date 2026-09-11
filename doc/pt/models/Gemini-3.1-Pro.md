# Gemini-3.1-Pro

> **Registro desativado em 11.09.2026 (tarefa T-216-S0, revisão de fabricantes).** Esta
> versão tem mais de três versões mais novas da mesma família (3.5, 3.6, 3.7, 3.8)
> e o seu identificador já não está no catálogo público de modelos. O registro é
> mantido: executores, tarefas e relatórios anteriores apontam para ele. Pode ser
> reativado pela caixa «ativa»: **Configurações → Catálogos → Modelos de IA**.

**Alocação:** em nuvem (Google, por uma camada compatível com OpenAI)
**Conexão:** `provider: openai-compatible`,
`baseUrl: https://generativelanguage.googleapis.com/v1beta/openai`, modelo `gemini-3.1-pro`
**Referência da chave:** `google.apiKey`

O modelo de trabalho da linha Gemini: notas de habilidade de 86 a 91 a um preço de 2 $ e 12 $
por milhão de tokens — cinco vezes mais barato que o Gemini-3-Ultra e com contexto de
1 048 576 tokens. É forte em resumos, tradução e análise de dados.

Aceita como entrada texto, código-fonte, imagens, PDF e também **áudio e vídeo**
(`audio/*`, `video/*`) — o material da tarefa pode chegar como gravação.

## Como obter a chave

**A chave é a mesma** dos demais Gemini: os três registros referenciam `google.apiKey`.
Se ela já estiver informada — este modelo funcionará sozinho.

Se ainda não houver chave:

1. Abra o [Google AI Studio](https://aistudio.google.com/apikey) e entre com a conta Google.
2. Clique em **Create API key** e escolha um projeto do Google Cloud.
3. Copie a chave (começa com `AIza`).
4. Vincule o faturamento do projeto — no nível gratuito as tarefas longas esbarram em 429.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Gemini-3.1-Pro → «Definir a chave de API»**.

## Limitações e custo

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Resposta máxima | 65 536 tokens |
| Custo de entrada | 2,00 $ por 1 milhão de tokens |
| Custo de saída | 12,00 $ por 1 milhão de tokens |

**O identificador do modelo não foi verificado com uma chamada real** — ele foi tomado do
catálogo de modelos sem o prefixo do fornecedor. Confira-o com a requisição `GET /models` a
`https://generativelanguage.googleapis.com/v1beta/openai`. A completude da compatibilidade com
OpenAI da camada do Google também não foi verificada com uma chamada real: as chamadas
principais estão cobertas, mas alguns campos podem se comportar de outra forma. Os preços atuais
estão em [Gemini API Pricing](https://ai.google.dev/pricing).

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
* **429 em tarefa longa** — o faturamento do projeto não está vinculado.
* **Resposta vazia, motivo `length`** — aumente o `params.maxTokens` no perfil
  (por padrão 32000).

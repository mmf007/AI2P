# GPT-5.6-Sol

**Alocação:** em nuvem (API da OpenAI, compatível com OpenAI)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://api.openai.com/v1`,
modelo `gpt-5.6-sol`
**Referência da chave:** `openai.apiKey`

O carro-chefe da OpenAI e o registro mais forte do catálogo pelas notas de habilidade (91 a 95):
código, análise de requisitos e planejamento — de 93 a 94 em 100. O preço é correspondente: 5 $
e 30 $ por milhão de tokens, mais caro que todos os demais modelos de texto do catálogo.
Pegue-o para aquilo que realmente exige o melhor resultado; a rotina de volume sai mais barata
entregando ao GPT-5.6-Terra ou ao DeepSeek-V4-Pro.

A OpenAI tem para este modelo um preview limitado da tarifa **Ultrafast** (até 14 vezes mais
rápida); no catálogo ele não está criado — se for preciso, crie um registro próprio com o id
correspondente.

## Como obter a chave

1. Registre-se em [platform.openai.com](https://platform.openai.com/).
2. Coloque saldo: **Settings → Billing → Add to credit balance**. Sem saldo, as requisições são
   recusadas com 429.
3. Abra [platform.openai.com/api-keys](https://platform.openai.com/api-keys) →
   **Create new secret key** e dê um nome.
4. Copie o valor — ele é mostrado **uma única vez**. A chave começa com `sk-`.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → GPT-5.6-Sol → «Definir a chave de API»**.

A chave é comum com o GPT-5.6-Terra — os dois modelos referenciam `openai.apiKey`.
A chave pertence à organização: é cifrada com a chave dela e replicada para todos os servidores
da organização (cap. 10 do ET).

## Limitações e custo

| | |
|---|---|
| Contexto | 1 050 000 tokens |
| Resposta máxima | 128 000 tokens |
| Custo de entrada | 5,00 $ por 1 milhão de tokens |
| Custo de saída | 30,00 $ por 1 milhão de tokens |

**O identificador do modelo não foi verificado com uma chamada real** — ele foi tomado do
catálogo de modelos e está indicado sem o prefixo do fornecedor. Antes da primeira tarefa,
confira-o com a requisição `GET /models` a `https://api.openai.com/v1`. Os preços atuais estão
em [OpenAI Pricing](https://openai.com/api/pricing/).

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | **o resultado é seu**: «Customer owns all Output», a OpenAI transfere a você os direitos dela sobre ele |
| Uso comercial | permitido |
| O que é obrigatório | respeitar as Usage Policies; lembrar que não lhe prometem exclusividade do resultado — a mesma resposta pode caber a outra pessoa |
| Texto das condições | <https://openai.com/policies/services-agreement/> |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

Os pesos são fechados, não há o que licenciar — as condições se referem ao **resultado da
geração**.

O conteúdo que vai para a API a OpenAI **não usa para desenvolver os serviços dela**, enquanto
você não permitir isso explicitamente; no ChatGPT de consumo não é assim.

Conferido em 27.08.2026: a formulação sobre a posse do resultado («you … own the Output. We
hereby assign to you all our right, title, and interest, if any, in and to Output») foi lida
literalmente nas condições da OpenAI; a mesma formulação consta do contrato para a API no link
acima.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`api.openai.com`.

## Erros frequentes

* **404 «model not found»** — o id mudou; confira com a lista `GET /models`.
* **429 «insufficient_quota»** — o saldo não foi reposto ou o limite da organização se esgotou.
* **Resposta vazia, motivo `length`** — o modelo gastou o limite da resposta em raciocínio;
  aumente o `params.maxTokens` no perfil (por padrão 32000).

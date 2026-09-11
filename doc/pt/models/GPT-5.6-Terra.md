# GPT-5.6-Terra

**Alocação:** em nuvem (API da OpenAI, compatível com OpenAI)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://api.openai.com/v1`,
modelo `gpt-5.6-terra`
**Referência da chave:** `openai.apiKey`

A faixa intermediária da linha: notas de habilidade de 85 a 88 contra 91 a 95 do
GPT-5.6-Sol, e um preço cinco vezes menor com o mesmo contexto
de 1 050 000 tokens. Uma boa escolha para trabalho de volume, em que o carro-chefe é
excessivo mas a qualidade deve continuar alta.

## Como obter a chave

**A chave é a mesma** do GPT-5.6-Sol: os dois modelos referenciam `openai.apiKey`.
Se ela já estiver informada — este modelo funcionará sozinho.

Se ainda não houver chave:

1. Registre-se em [platform.openai.com](https://platform.openai.com/).
2. Coloque saldo: **Settings → Billing → Add to credit balance**.
3. Abra [platform.openai.com/api-keys](https://platform.openai.com/api-keys) →
   **Create new secret key**.
4. Copie o valor (mostrado uma única vez, começa com `sk-`).
5. No AI2P: **Configurações → Catálogos → Modelos de IA → GPT-5.6-Terra → «Definir a chave de API»**.

## Limitações e custo

| | |
|---|---|
| Contexto | 1 050 000 tokens |
| Resposta máxima | 128 000 tokens |
| Custo de entrada | 1,00 $ por 1 milhão de tokens |
| Custo de saída | 6,00 $ por 1 milhão de tokens |

**Confira o preço obrigatoriamente antes de gastos sérios.** As fontes divergem: o
panorama de mercado (relatório T-213) dava 2,50 $ e 15,00 $, e o catálogo público de modelos
em 17.08.2026 dava 1,00 $ e 6,00 $. No catálogo foram lançados os segundos números. O preço
atual está em [OpenAI Pricing](https://openai.com/api/pricing/).

**O identificador do modelo não foi verificado com uma chamada real** — ele foi tomado do
catálogo de modelos sem o prefixo do fornecedor. Antes da primeira tarefa, confira-o com a
requisição `GET /models` a `https://api.openai.com/v1`.

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
* **429 «insufficient_quota»** — o saldo da organização na OpenAI não foi reposto.
* **A fatura saiu o dobro do calculado** — verifique o preço na tabela do fornecedor
  (veja acima) e corrija-o na declaração do modelo.

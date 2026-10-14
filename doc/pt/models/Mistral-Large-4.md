# Mistral-Large-4

**Alocação:** em nuvem (Mistral AI, França; compatível com OpenAI)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://api.mistral.ai/v1`,
**Referência da chave:** `mistral.apiKey`

A Mistral AI lançou este modelo em 06.10.2026; ele entrou no catálogo do AI2P pela tarefa
T-466-S0, a revisão de fabricantes de 13.10.2026. O identificador `mistralai/mistral-large-4-0`, o preço e as
modalidades foram verificados por uma consulta ao catálogo público OpenRouter.

Contexto de 1 048 576 tokens, resposta de até 262 144 tokens, 0,68 $ e 2,09 $ por milhão de
tokens, pontuações 83-88. O registro mais próximo do catálogo é **Mistral-Large-3**.

Entrada aceita: texto e Markdown, código-fonte, imagens.

## Como obter a chave

1. Registre-se em [console.mistral.ai](https://console.mistral.ai/).
2. Ative a tarifa paga: **Billing → Add payment method**. No nível gratuito a frequência de
   requisições é bastante limitada.
3. Abra [console.mistral.ai/api-keys](https://console.mistral.ai/api-keys/) →
   **Create new key**, dê um nome e um prazo de validade.
4. Copie o valor — ele é mostrado **uma única vez**.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Mistral-Large-4 → «Definir a chave de API»**.

A chave pertence à organização: é cifrada com a chave dela e replicada para todos os servidores
da organização (cap. 10 do ET).

## Limitações e custo

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Resposta máxima | 262 144 tokens |
| Custo de entrada | 0,68 $ por 1 milhão de tokens |
| Custo de saída | 2,09 $ por 1 milhão de tokens |

**O identificador do modelo não foi verificado com uma chamada real** — ele foi tomado do
catálogo de modelos sem o prefixo do fornecedor. Confira-o com a requisição `GET /models` a
`https://api.mistral.ai/v1`: em checkpoints datados isso é especialmente importante. Os preços
atuais estão em [Mistral Pricing](https://mistral.ai/pricing).

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | **o resultado é seu**: «Customer … owns all Output», a Mistral transfere a você os direitos dela (Commercial ToS, item 3.1) |
| Uso comercial | permitido |
| O que é obrigatório | não apresentar o resultado como trabalho humano (item 3.2); não treinar com as imagens-resultado um gerador de imagens concorrente (item 3.3); respeitar a Usage Policy |
| Texto das condições | <https://legal.mistral.ai/terms/commercial-terms-of-service> |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

Sobre o treinamento com os seus dados está dito com exatidão (item 4.2): a Mistral **não** treina
os modelos dela com eles — exceto nos casos em que você mesmo ligou o treinamento, enviou um
feedback ou pegou um modelo experimental (no AI Studio eles são marcados com o prefixo `labs`).
Este registro do catálogo aponta para o modelo comum, e não para um `labs`.

As condições foram conferidas pelo texto delas em 13.10.2026.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`api.mistral.ai`.

## Erros frequentes

* **404 «model not found»** — saiu um checkpoint novo e a data antiga já não é atendida; pegue o
  id atual em `GET /models`.
* **429 «rate limit exceeded»** — a tarifa paga não está ativada.
* **A chave parou de funcionar** — as chaves da Mistral às vezes têm prazo de validade;
  verifique-o no console.

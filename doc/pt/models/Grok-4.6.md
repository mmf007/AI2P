# Grok-4.6

**Alocação:** em nuvem (API da xAI, compatível com OpenAI)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://api.x.ai/v1`,
modelo `grok-4.6`
**Referência da chave:** `xai.apiKey`

O modelo de fronteira mais barato: notas de habilidade de 86 a 92 (nível do Sonnet 5) a um preço
de 2 $ e 6 $ por milhão de tokens. Contexto de 500 000 tokens. Uma boa escolha quando é preciso
quase a melhor qualidade por um preço médio.

Duas limitações que vale conhecer de antemão:

* **Uma requisição com mais de 200 000 tokens é tarifada por inteiro ao preço majorado** — 4 $ e
  12 $ por milhão, ou seja, o dobro. Isso não é um erro na tabela abaixo, e sim a tarifa da xAI:
  encarece a requisição **inteira**, e não apenas a parte acima do limiar.
* **O conhecimento do mundo vai até 01.02.2026.** Sobre o que aconteceu depois o modelo não
  sabe; forneça as informações recentes na própria tarefa.

## Como obter a chave

1. Registre-se no [console da xAI](https://console.x.ai/).
2. Crie uma equipe (team) e coloque saldo — sem saldo as requisições são recusadas.
3. Abra **API Keys → Create API Key**, dê um nome e direitos sobre os modelos de chat.
4. Copie o valor — ele é mostrado **uma única vez**. A chave começa com `xai-`.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Grok-4.6 → «Definir a chave de API»**.

A chave pertence à organização: é cifrada com a chave dela e replicada para todos os servidores
da organização (cap. 10 do ET).

## Limitações e custo

| | |
|---|---|
| Contexto | 500 000 tokens |
| Resposta máxima | 64 000 tokens |
| Custo de entrada | 2,00 $ por 1 milhão de tokens (acima de 200 000 tokens de requisição — 4,00 $) |
| Custo de saída | 6,00 $ por 1 milhão de tokens (acima de 200 000 tokens de requisição — 12,00 $) |

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

As condições foram conferidas pelo texto delas em 27.08.2026.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`api.x.ai`.

## Erros frequentes

* **Fatura o dobro do calculado** — a tarefa cruzou o limiar de 200 000 tokens de requisição.
  Mantenha o histórico do chat mais curto ou leve os materiais para arquivos.
* **O modelo «não sabe» de um evento recente** — a fronteira do conhecimento é 01.02.2026.
* **403 / «no credits»** — o saldo da equipe na xAI não foi reposto.

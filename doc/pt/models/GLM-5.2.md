# GLM-5.2

**Alocação:** em nuvem (Z.ai / Zhipu, compatível com OpenAI)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://api.z.ai/api/paas/v4`,
modelo `glm-5.2`
**Referência da chave:** `zai.apiKey`

**O melhor código entre os modelos abertos depois do Kimi-K3** — nota `code-write` 91
em 100, e ainda por cima três vezes mais barato: 1,19 $ e 3,74 $ por milhão de tokens. Contexto
de 1 048 576 tokens. O nicho é evidente: trabalho de volume com código, em que o Opus e o
GPT-5.6-Sol saem caro demais.

Os textos lhe saem pior que o código (notas de 83 a 86), e a entrada é só texto e código-fonte —
o modelo não aceita imagens.

## Como obter a chave

1. Registre-se em [z.ai](https://z.ai/) (a plataforma internacional da Zhipu).
2. Coloque saldo na seção de faturamento — sem saldo as requisições são recusadas.
3. Abra a [lista de chaves](https://z.ai/manage-apikey/apikey-list) → **Create API key**
   e dê um nome.
4. Copie o valor — ele é mostrado **uma única vez**.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → GLM-5.2 → «Definir a chave de API»**.

A chave pertence à organização: é cifrada com a chave dela e replicada para todos os servidores
da organização (cap. 10 do ET).

## Limitações e custo

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Resposta máxima | 65 536 tokens |
| Custo de entrada | 1,19 $ por 1 milhão de tokens |
| Custo de saída | 3,74 $ por 1 milhão de tokens |

**O identificador do modelo não foi verificado com uma chamada real** — ele foi tomado do
catálogo de modelos sem o prefixo do fornecedor. Confira-o com a requisição `GET /models` a
`https://api.z.ai/api/paas/v4`.

Repare no `baseUrl` incomum: o caminho é `/api/paas/v4`, e não `/v1`. Se você copiou o endereço
de instruções alheias e está recebendo 404 em todas as requisições — a causa, muito
provavelmente, é essa.

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | **os direitos sobre as requisições e o resultado ficam com você** (Z.AI Terms of Use, seção IV) |
| Uso comercial | permitido |
| O que é obrigatório | não remover as marcas de IA colocadas pela Z.ai, sinalizar o gerado como criado por IA e não apresentá-lo como trabalho humano |
| Texto das condições | <https://docs.z.ai/legal-agreement/terms-of-use> |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

No caso de usuários individuais (não corporativos), a Z.ai tem o direito de usar o que foi
enviado e recebido para desenvolver o serviço — se isso for inaceitável, é preciso um contrato
corporativo.

À parte, sobre os pesos: o **GLM-5.2 foi publicado sob MIT** (cartão `zai-org/GLM-5.2`,
conferido em 27.08.2026), ou seja, o modelo pode ser levantado também na sua casa. Este registro
do catálogo vai à nuvem da Z.ai, e ali valem as condições acima, e não a MIT.

As condições foram conferidas pelo texto delas em 27.08.2026.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`api.z.ai`.

## Erros frequentes

* **404 em qualquer requisição** — o caminho do `baseUrl` foi truncado ou complementado (é
  preciso `/api/paas/v4`).
* **401 «invalid api key»** — a chave foi criada na plataforma chinesa (`bigmodel.cn`) e o
  endereço é o internacional.
* **O modelo se recusa a aceitar uma imagem** — a entrada é apenas de texto; para tarefas
  multimodais pegue o MiniMax-M3 ou o Gemini.

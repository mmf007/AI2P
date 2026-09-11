# GLM-5.3

**Alocação:** em nuvem (Z.ai / Zhipu, compatível com OpenAI)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://api.z.ai/api/paas/v4`,
modelo `glm-5.3`
**Referência da chave:** `zai.apiKey`

A Z.ai (Zhipu) lançou este modelo em 18.08.2026; ele entrou no catálogo do AI2P pela tarefa
T-216-S0, a revisão de fabricantes de 11.09.2026. O identificador `z-ai/glm-5.3`, o tamanho do
contexto, o preço e as modalidades foram verificados por uma consulta ao catálogo
público OpenRouter.

Dá continuidade à linha **GLM-5.2**: contexto de 1 048 576 tokens, resposta de até 65 536
tokens, 1,40 $ e 4,40 $ por milhão de tokens. Pontuações de habilidades 84-93.

Entrada aceita: texto e Markdown, código-fonte.

## Como obter a chave

1. Registre-se em [z.ai](https://z.ai/) (a plataforma internacional da Zhipu).
2. Coloque saldo na seção de faturamento — sem saldo as requisições são recusadas.
3. Abra a [lista de chaves](https://z.ai/manage-apikey/apikey-list) → **Create API key**
   e dê um nome.
4. Copie o valor — ele é mostrado **uma única vez**.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → GLM-5.3 → «Definir a chave de API»**.

A chave pertence à organização: é cifrada com a chave dela e replicada para todos os servidores
da organização (cap. 10 do ET).

## Limitações e custo

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Resposta máxima | 65 536 tokens |
| Custo de entrada | 1,40 $ por 1 milhão de tokens |
| Custo de saída | 4,40 $ por 1 milhão de tokens |

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

À parte, sobre os pesos: o **GLM-5.3 foi publicado sob MIT** (cartão `zai-org/GLM-5.3`,
conferido em 11.09.2026), ou seja, o modelo pode ser levantado também na sua casa. Este registro
do catálogo vai à nuvem da Z.ai, e ali valem as condições acima, e não a MIT.

As condições foram conferidas pelo texto delas em 11.09.2026.

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

# Qwen3.8-Omni-Flash

**Alocação:** em nuvem (Alibaba DashScope, compatível com OpenAI)
**Conexão:** `provider: openai-compatible`,
`baseUrl: https://dashscope-intl.aliyuncs.com/compatible-mode/v1`, modelo `qwen3.8-omni-flash`
**Referência da chave:** `qwen.apiKey`

A Alibaba lançou este modelo em 21.09.2026; ele entrou no catálogo do AI2P pela tarefa
T-347-S0, a revisão de fabricantes de 23.09.2026. O identificador `qwen/qwen3.8-omni-flash`, o tamanho do
contexto, o preço e as modalidades foram verificados por uma consulta ao catálogo
público OpenRouter.

Dá continuidade à linha **Qwen3.8-Max**: contexto de 1 000 000 tokens, resposta de até 32 768
tokens, 0,15 $ e 0,47 $ por milhão de tokens. Pontuações de habilidades 82-89.

Entrada aceita: texto e Markdown, código-fonte, imagens, áudio, vídeo.

## Como obter a chave

1. Registre-se no [Alibaba Cloud Model Studio](https://modelstudio.console.alibabacloud.com/)
   (a plataforma internacional).
2. Ative o serviço de modelos e vincule uma forma de pagamento.
3. Abra **API-KEY → Create API Key** e escolha o espaço de trabalho.
4. Copie o valor — ele é mostrado **uma única vez**. A chave começa com `sk-`.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Qwen3.8-Omni-Flash → «Definir a chave de API»**.

A chave pertence à organização: é cifrada com a chave dela e replicada para todos os servidores
da organização (cap. 10 do ET). Os modelos Qwen **locais** do catálogo não têm chave alguma —
eles funcionam no seu computador.

## Limitações e custo

| | |
|---|---|
| Contexto | 1 000 000 tokens |
| Resposta máxima | 32 768 tokens |
| Custo de entrada | 0,15 $ por 1 milhão de tokens |
| Custo de saída | 0,47 $ por 1 milhão de tokens |

**O identificador do modelo não foi verificado com uma chamada real** — ele foi tomado do
catálogo de modelos sem o prefixo do fornecedor. Confira-o com a requisição `GET /models` a
`https://dashscope-intl.aliyuncs.com/compatible-mode/v1`. Os preços atuais estão no console do
Model Studio.

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | são definidas pelo contrato do Model Studio: o acesso pago por chave dá direito a usar o resultado, e a responsabilidade por ele é sua |
| Uso comercial | permitido no acesso pago; o conteúdo obtido no modo **de teste** do console só pode ser usado para avaliar o modelo |
| O que é obrigatório | respeitar as regras do Model Studio e responder pela legalidade do gerado: o fornecedor não dá garantias sobre o conteúdo |
| Texto das condições | <https://www.alibabacloud.com/help/en/model-studio/related-agreements> (lista dos acordos vigentes) |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

Os pesos do `qwen3.8-omni-flash` são fechados (ao contrário dos Qwen menores, que estão sob Apache
2.0), e por isso se trata do **resultado da geração**.

Conferido em 23.09.2026: a página com a lista de acordos do Model Studio foi lida (última
atualização em 23.09.2026; vigoram os Terms of Service, o Service Level Agreement e os Open
Source Model License Terms); o próprio texto dos Terms of Service não se deixou ler por máquina
— abra-o pelo link acima e leia com os olhos, especialmente a seção sobre o modo de teste.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`dashscope-intl.aliyuncs.com`.

## Erros frequentes

* **404 ou «model not exist»** — ou o id mudou, ou foi pego o host errado (o internacional
  contra o chinês).
* **401 «InvalidApiKey»** — a chave foi criada em outro espaço de trabalho.
* **Resposta vazia, motivo `length`** — aumente o `params.maxTokens` no perfil
  (por padrão 32000).

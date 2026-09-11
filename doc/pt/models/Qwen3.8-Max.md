# Qwen3.8-Max

**Alocação:** em nuvem (Alibaba DashScope, compatível com OpenAI)
**Conexão:** `provider: openai-compatible`,
`baseUrl: https://dashscope-intl.aliyuncs.com/compatible-mode/v1`, modelo `qwen3.8-max`
**Referência da chave:** `qwen.apiKey`

Líder do teste **OSWorld-Verified (86,1)** — ou seja, «trabalho no computador»: ações de muitos
passos com arquivos, ferramentas e interfaces. Este é exatamente o cenário do agente do AI2P, e
por isso o modelo é mais útil do que mostram as notas gerais de habilidade dele (86 a 92). O
preço é de 2 $ e 6 $ por milhão de tokens com contexto de 1 000 000; a entrada aceita imagens e
vídeo.

**O endereço no catálogo é o internacional** (`dashscope-intl`). Dentro da China funciona outro
host — `dashscope.aliyuncs.com`; se for preciso, troque o `baseUrl` no perfil do modelo.

## Como obter a chave

1. Registre-se no [Alibaba Cloud Model Studio](https://modelstudio.console.alibabacloud.com/)
   (a plataforma internacional).
2. Ative o serviço de modelos e vincule uma forma de pagamento.
3. Abra **API-KEY → Create API Key** e escolha o espaço de trabalho.
4. Copie o valor — ele é mostrado **uma única vez**. A chave começa com `sk-`.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Qwen3.8-Max → «Definir a chave de API»**.

A chave pertence à organização: é cifrada com a chave dela e replicada para todos os servidores
da organização (cap. 10 do ET). Os modelos Qwen **locais** do catálogo não têm chave alguma —
eles funcionam no seu computador.

## Limitações e custo

| | |
|---|---|
| Contexto | 1 000 000 tokens |
| Resposta máxima | 65 536 tokens |
| Custo de entrada | 2,00 $ por 1 milhão de tokens |
| Custo de saída | 6,00 $ por 1 milhão de tokens |

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

Os pesos do `qwen3.8-max` são fechados (ao contrário dos Qwen menores, que estão sob Apache
2.0), e por isso se trata do **resultado da geração**.

Conferido em 27.08.2026: a página com a lista de acordos do Model Studio foi lida (última
atualização em 30.06.2026; vigoram os Terms of Service, o Service Level Agreement e os Open
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

# Inkling-2-Small

**Alocação:** em nuvem (Thinking Machines, **por meio do gateway OpenRouter**)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
**Referência da chave:** `openrouter.apiKey`

A Thinking Machines lançou este modelo em 09.11.2026; ele entrou no catálogo do AI2P pela tarefa
T-736-S0, a revisão de fabricantes de 13.11.2026. O identificador `thinkingmachines/inkling-2-small`, o preço e as
modalidades foram verificados por uma consulta ao catálogo público OpenRouter.

Contexto de 1 048 576 tokens, resposta de até 943 718 tokens, 0,30 $ e 1,00 $ por milhão de
tokens, pontuações 81-86. O registro mais próximo do catálogo é **Inkling-975B**.

Entrada aceita: texto e Markdown, código-fonte, imagens.

## Como obter a chave

**A chave é uma só para todos os modelos que passam pelo gateway** — o Muse-Spark-1.2,
o Nemotron-3-Ultra, o Ling-3.0-Flash e este
referenciam a mesma `openrouter.apiKey`.

1. Registre-se em [openrouter.ai](https://openrouter.ai/).
2. Coloque saldo: **Credits → Add credits**.
3. Abra [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**.
4. Copie o valor (mostrado uma única vez, começa com `sk-or-`).
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Inkling-2-Small → «Definir a chave de API»**.

## Limitações e custo

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Resposta máxima | 943 718 tokens |
| Custo de entrada | 0,30 $ por 1 milhão de tokens |
| Custo de saída | 1,00 $ por 1 milhão de tokens |

**O identificador do modelo não foi verificado com uma chamada real.** Nos modelos que passam
pelo gateway ele é indicado como o slug completo, com o prefixo do fornecedor
(`thinkingmachines/inkling-2-small`). Confira-o com a requisição `GET /models` a
`https://openrouter.ai/api/v1` — o gateway entrega essa lista mesmo sem chave.

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | são definidas pelo **dono do modelo**, e não pelo gateway: «Your ownership rights in the Output are set forth in the Model Terms for each Model you use» |
| Uso comercial | veja os Model Terms no cartão do modelo no gateway — o gateway responde pela entrega, e não pelos direitos |
| O que é obrigatório | respeitar as condições tanto do gateway quanto do dono do modelo; o dono pode alterar as condições dele a qualquer momento |
| Texto das condições | <https://openrouter.ai/terms> (redação de 29.07.2026) mais os Model Terms no cartão do modelo |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

Os pesos do modelo são abertos: repositório `thinkingmachines/Inkling-2-Small` no HuggingFace,
licença **Apache 2.0** (campo `cardData.license`, `gated=false`, verificado em 13.11.2026);
o uso comercial é permitido, texto: https://www.apache.org/licenses/LICENSE-2.0.
Pelo gateway valem as condições do proprietário do modelo e do gateway.

O gateway, por sua parte, **recusa na medida do possível o treinamento com dados** nos
fornecedores conectados, mas não responde pela exatidão das condições alheias e diz isso
explicitamente.

As condições do gateway foram conferidas pelo texto delas em 13.11.2026.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`openrouter.ai`. Os pesos são abertos (Apache 2.0), mas 594 bilhões de parâmetros não se
levantam no seu computador — para trabalho local há no catálogo modelos menores.

## Erros frequentes

* **404 «No endpoints found»** — o prefixo do fornecedor se perdeu ou o slug mudou no gateway.
* **402 «Insufficient credits»** — o saldo do OpenRouter não foi reposto.
* **Apareceram fatos inventados no resultado** — é o nicho do modelo; confira o resultado ou
  pegue outro modelo para tarefas factuais.

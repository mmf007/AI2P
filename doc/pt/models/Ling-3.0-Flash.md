# Ling-3.0-Flash

**Alocação:** em nuvem (Ant Group, **por meio do gateway OpenRouter**)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
modelo `inclusionai/ling-3.0-flash`
**Referência da chave:** `openrouter.apiKey`

**O registro mais barato do catálogo: 0,021 $ por 1 milhão de tokens de entrada** — cerca de
cento e cinquenta vezes mais barato que o Kimi-K3. Um modelo de 124 bilhões de parâmetros (5,1
bilhões ativos), voltado a cenários de agente de alta frequência: muitos passos curtos em
sequência.

O nicho no AI2P é o **pano de fundo barato** para tarefas de muitos passos: rascunhos, correções
pequenas em massa, marcação prévia de material. Notas de habilidade de 74 a 82, e o contexto
aqui é menor que o dos vizinhos — 262 144 tokens.

A Ant Group não tem API pública própria, e por isso o registro passa pelo gateway **OpenRouter**.

## Como obter a chave

**A chave é uma só para todos os modelos que passam pelo gateway** — o Muse-Spark-1.2,
o Inkling-975B, o Nemotron-3-Ultra e este
referenciam a mesma `openrouter.apiKey`.

1. Registre-se em [openrouter.ai](https://openrouter.ai/).
2. Coloque saldo: **Credits → Add credits**. A recarga mínima dura muito tempo:
   um milhão de tokens de entrada custa dois centavos.
3. Abra [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**.
4. Copie o valor (mostrado uma única vez, começa com `sk-or-`).
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Ling-3.0-Flash → «Definir a chave de API»**.

## Limitações e custo

| | |
|---|---|
| Contexto | 262 144 tokens |
| Resposta máxima | 32 768 tokens |
| Custo de entrada | 0,021 $ por 1 milhão de tokens |
| Custo de saída | 0,063 $ por 1 milhão de tokens |

**O identificador do modelo não foi verificado com uma chamada real.** Nos modelos que passam
pelo gateway ele é indicado como o slug completo, com o prefixo do fornecedor
(`inclusionai/ling-3.0-flash`). Confira-o com a requisição `GET /models` a
`https://openrouter.ai/api/v1`.

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | são definidas pelo **dono do modelo**, e não pelo gateway: «Your ownership rights in the Output are set forth in the Model Terms for each Model you use» |
| Uso comercial | veja os Model Terms no cartão do modelo no gateway — o gateway responde pela entrega, e não pelos direitos |
| O que é obrigatório | respeitar as condições tanto do gateway quanto do dono do modelo; o dono pode alterar as condições dele a qualquer momento |
| Texto das condições | <https://openrouter.ai/terms> (redação de 29.07.2026) mais os Model Terms no cartão do modelo |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

Os pesos deste modelo também são abertos: **MIT** (cartão `inclusionAI/Ling-3.0-flash`,
conferido em 27.08.2026) — o uso comercial é permitido, e exige-se manter o texto da licença e o
aviso de direitos autorais. Ou seja, o modelo pode ser levantado também na sua casa; o registro
do catálogo vai pelo gateway, e sobre o resultado valem as condições do dono do modelo.

O gateway, por sua parte, **recusa na medida do possível o treinamento com dados** nos
fornecedores conectados, mas não responde pela exatidão das condições alheias e diz isso
explicitamente.

As condições do gateway foram conferidas pelo texto delas em 27.08.2026.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`openrouter.ai`.

## Erros frequentes

* **A seleção automática escolhe sempre justamente este** — o controle «preço ↔ qualidade» do
  projeto está deslocado para o preço (ET, item 2.7). Desloque-o para a qualidade ou defina o
  executor à mão.
* **Entrada longa demais** — o contexto é de 262 144 tokens, quatro vezes menor que o dos
  carros-chefe.
* **404 «No endpoints found»** — o prefixo do fornecedor se perdeu ou o slug mudou no gateway.

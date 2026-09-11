# Muse-Spark-1.2

**Alocação:** em nuvem (Meta, **por meio do gateway OpenRouter**)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
modelo `meta/muse-spark-1.2`
**Referência da chave:** `openrouter.apiKey`

O primeiro modelo da Meta de nível de fronteira: notas de habilidade de 85 a 89, contexto de
1 048 576 tokens, preço de 1,25 $ e 4,25 $ por milhão. Aceita como entrada quase tudo — texto,
código-fonte, imagens, PDF, áudio e vídeo.

**A Meta não tem uma entrada pública própria compatível com OpenAI**, e por isso o registro está
conectado por meio do gateway **OpenRouter**: as requisições vão para `openrouter.ai`, e ele as
repassa ao fornecedor. O pagamento é pela tarifa do gateway (com a comissão por cima), mas em
compensação não é preciso uma chave separada na Meta.

## Como obter a chave

**A chave é uma só para todos os modelos que passam pelo gateway** — o Inkling-975B,
o Nemotron-3-Ultra, o Ling-3.0-Flash e este
referenciam a mesma `openrouter.apiKey`. Informou uma vez — funcionam os quatro.

1. Registre-se em [openrouter.ai](https://openrouter.ai/).
2. Coloque saldo: **Credits → Add credits** (cartão ou cripto). Sem saldo só ficam disponíveis
   as variantes gratuitas dos modelos, com fila.
3. Abra [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**, dê um nome e, se for
   preciso, um limite de gasto por chave.
4. Copie o valor — ele é mostrado **uma única vez**. A chave começa com `sk-or-`.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Muse-Spark-1.2 → «Definir a chave de API»**.

A chave pertence à organização: é cifrada com a chave dela e replicada para todos os servidores
da organização (cap. 10 do ET).

## Limitações e custo

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Resposta máxima | 65 536 tokens |
| Custo de entrada | 1,25 $ por 1 milhão de tokens |
| Custo de saída | 4,25 $ por 1 milhão de tokens |

**O identificador do modelo não foi verificado com uma chamada real.** Nos modelos que passam
pelo gateway ele é indicado como o **slug completo, com o prefixo do fornecedor**
(`meta/muse-spark-1.2`) — sem o prefixo o gateway responde 404. Confira-o com a requisição
`GET /models` a `https://openrouter.ai/api/v1` (o gateway entrega essa lista mesmo sem chave).

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | são definidas pelo **dono do modelo**, e não pelo gateway: «Your ownership rights in the Output are set forth in the Model Terms for each Model you use» |
| Uso comercial | veja os Model Terms no cartão do modelo no gateway — o gateway responde pela entrega, e não pelos direitos |
| O que é obrigatório | respeitar as condições tanto do gateway quanto do dono do modelo; o dono pode alterar as condições dele a qualquer momento |
| Texto das condições | <https://openrouter.ai/terms> (redação de 29.07.2026) mais os Model Terms no cartão do modelo |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

Os pesos deste modelo não estão em acesso aberto (no HuggingFace o repositório não foi
encontrado, verificado em 27.08.2026) — não há o que licenciar, e valem apenas as condições do
dono do modelo e do gateway.

O gateway, por sua parte, **recusa na medida do possível o treinamento com dados** nos
fornecedores conectados, mas não responde pela exatidão das condições alheias e diz isso
explicitamente.

As condições do gateway foram conferidas pelo texto delas em 27.08.2026.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`openrouter.ai`.

## Erros frequentes

* **404 «No endpoints found»** — no campo «modelo» o prefixo do fornecedor se perdeu ou o slug
  mudou no gateway.
* **402 «Insufficient credits»** — o saldo do OpenRouter não foi reposto.
* **A resposta saiu mais cara que o calculado** — o gateway cobra a comissão dele por cima do
  preço do fornecedor.

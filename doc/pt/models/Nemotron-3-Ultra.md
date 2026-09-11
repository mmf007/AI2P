# Nemotron-3-Ultra

**Alocação:** em nuvem (NVIDIA, **por meio do gateway OpenRouter**)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
modelo `nvidia/nemotron-3-ultra-550b-a55b`
**Referência da chave:** `openrouter.apiKey`

O registro mais aberto do catálogo: a NVIDIA publicou não só os pesos, mas também os **dados e
as receitas de treinamento**, sob a licença OpenMDW-1.1. A arquitetura é híbrida — Mamba-2 mais
Transformer, 550 bilhões de parâmetros (55 bilhões ativos). Notas de habilidade de 76 a 82,
contexto de 512 288 tokens, preço de 0,60 $ e 3,60 $ por milhão.

O nicho são as tarefas em que importam a reprodutibilidade e a abertura, e não o recorde de
qualidade. Pelo gateway está disponível também uma **variante gratuita** deste modelo (com fila
e limitação de frequência): ela tem outro slug; se quiser, crie um registro próprio no catálogo.

A NVIDIA não tem uma API pública própria para este modelo, e por isso o registro passa pelo
gateway **OpenRouter**.

## Como obter a chave

**A chave é uma só para todos os modelos que passam pelo gateway** — o Muse-Spark-1.2,
o Inkling-975B, o Ling-3.0-Flash e este referenciam
a mesma `openrouter.apiKey`.

1. Registre-se em [openrouter.ai](https://openrouter.ai/).
2. Coloque saldo: **Credits → Add credits**.
3. Abra [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**.
4. Copie o valor (mostrado uma única vez, começa com `sk-or-`).
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Nemotron-3-Ultra → «Definir a chave de API»**.

## Limitações e custo

| | |
|---|---|
| Contexto | 512 288 tokens |
| Resposta máxima | 65 536 tokens |
| Custo de entrada | 0,60 $ por 1 milhão de tokens |
| Custo de saída | 3,60 $ por 1 milhão de tokens |

**O identificador do modelo não foi verificado com uma chamada real.** Nos modelos que passam
pelo gateway ele é indicado como o slug completo, com o prefixo do fornecedor
(`nvidia/nemotron-3-ultra-550b-a55b`). Confira-o com a requisição `GET /models` a
`https://openrouter.ai/api/v1`.

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | são definidas pelo **dono do modelo**, e não pelo gateway: «Your ownership rights in the Output are set forth in the Model Terms for each Model you use» |
| Uso comercial | veja os Model Terms no cartão do modelo no gateway — o gateway responde pela entrega, e não pelos direitos |
| O que é obrigatório | respeitar as condições tanto do gateway quanto do dono do modelo; o dono pode alterar as condições dele a qualquer momento |
| Texto das condições | <https://openrouter.ai/terms> (redação de 29.07.2026) mais os Model Terms no cartão do modelo |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

Aqui há um caso especial: os **pesos foram publicados abertamente, sob OpenMDW-1.1** (cartão
`nvidia/NVIDIA-Nemotron-3-Ultra-550B-A55B-BF16`, texto da licença —
<https://openmdw.ai/license/1-1/>, conferido em 27.08.2026). A licença é aberta e permite o uso
comercial e, junto com os pesos, a NVIDIA publicou os dados e as receitas de treinamento. Ou
seja, este modelo pode ser levantado na sua casa, sem depender das condições do gateway.

O gateway, por sua parte, **recusa na medida do possível o treinamento com dados** nos
fornecedores conectados, mas não responde pela exatidão das condições alheias e diz isso
explicitamente.

As condições do gateway foram conferidas pelo texto delas em 27.08.2026.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`openrouter.ai`. Os pesos são abertos, mas 550 bilhões de parâmetros exigem um rack de
servidores — no seu computador use os registros locais do catálogo.

## Erros frequentes

* **404 «No endpoints found»** — o prefixo do fornecedor se perdeu ou o slug mudou.
* **A tarefa fica pendurada e termina por tempo limite** — caiu-se na variante gratuita com
  fila; verifique se no campo «modelo» está o slug pago e se há fundos na conta.
* **402 «Insufficient credits»** — o saldo do OpenRouter não foi reposto.

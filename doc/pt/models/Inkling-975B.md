# Inkling-975B

**Alocação:** em nuvem (Thinking Machines, **por meio do gateway OpenRouter**)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://openrouter.ai/api/v1`,
modelo `thinkingmachines/inkling`
**Referência da chave:** `openrouter.apiKey`

Um modelo aberto de 975 bilhões de parâmetros (41 bilhões ativos) sob a licença **Apache 2.0**.
Foi concebido pelos autores como **base para ajuste fino**, e não como recordista: notas de
habilidade de 81 a 87, contexto de 1 048 576 tokens, preço de 0,95 $ e 4,05 $ por milhão. A
entrada aceita texto, código-fonte, imagens e áudio.

**Cuidado com a factualidade.** No teste de conhecimento do mundo (AA Omniscience) o modelo dá
cerca de 40 % de acerto com 63 % de alucinações: ele inventa o que falta com toda a confiança.
Para as habilidades `analyze-data` e `text-docs`, pegue-o apenas onde houver com o que conferir
o resultado e onde os fatos venham na própria tarefa, e não da memória do modelo.

A Thinking Machines não tem API pública própria, e por isso o registro passa pelo gateway
**OpenRouter**.

## Como obter a chave

**A chave é uma só para todos os modelos que passam pelo gateway** — o Muse-Spark-1.2,
o Nemotron-3-Ultra, o Ling-3.0-Flash e este
referenciam a mesma `openrouter.apiKey`.

1. Registre-se em [openrouter.ai](https://openrouter.ai/).
2. Coloque saldo: **Credits → Add credits**.
3. Abra [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**.
4. Copie o valor (mostrado uma única vez, começa com `sk-or-`).
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Inkling-975B → «Definir a chave de API»**.

## Limitações e custo

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Resposta máxima | 65 536 tokens |
| Custo de entrada | 0,95 $ por 1 milhão de tokens |
| Custo de saída | 4,05 $ por 1 milhão de tokens |

**O identificador do modelo não foi verificado com uma chamada real.** Nos modelos que passam
pelo gateway ele é indicado como o slug completo, com o prefixo do fornecedor
(`thinkingmachines/inkling`). Confira-o com a requisição `GET /models` a
`https://openrouter.ai/api/v1` — o gateway entrega essa lista mesmo sem chave.

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
`openrouter.ai`. Os pesos são abertos (Apache 2.0), mas 975 bilhões de parâmetros não se
levantam no seu computador — para trabalho local há no catálogo modelos menores.

## Erros frequentes

* **404 «No endpoints found»** — o prefixo do fornecedor se perdeu ou o slug mudou no gateway.
* **402 «Insufficient credits»** — o saldo do OpenRouter não foi reposto.
* **Apareceram fatos inventados no resultado** — é o nicho do modelo; confira o resultado ou
  pegue outro modelo para tarefas factuais.

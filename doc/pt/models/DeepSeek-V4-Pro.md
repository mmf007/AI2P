# DeepSeek-V4-Pro

**Alocação:** em nuvem (API da DeepSeek, compatível com OpenAI)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://api.deepseek.com`,
modelo `deepseek-v4-pro`
**Referência da chave:** `deepseek.apiKey`

Um modelo barato de nível decente: código e textos de 84 a 89 em 100 pelas notas de habilidade,
a um preço cerca de vinte vezes menor que o do Claude-Fable-5. Uma boa escolha para rotina de
volume.

## Como obter a chave

1. Registre-se em [platform.deepseek.com](https://platform.deepseek.com/).
2. Coloque saldo: **Top up** (pagamento por cartão). Sem saldo, as requisições são recusadas.
3. Abra **API keys → Create new API key** e dê um nome.
4. Copie o valor — ele é mostrado **uma única vez**. A chave começa com `sk-`.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → DeepSeek-V4-Pro → «Definir a chave de API»**.

A chave é comum com o DeepSeek-V4-Flash — os dois modelos referenciam
`deepseek.apiKey`. Informou uma vez — funcionam os dois.

A chave pertence à organização: é cifrada com a chave dela e replicada para todos os servidores
da organização (cap. 10 do ET).

## Limitações e custo

| | |
|---|---|
| Contexto | 1 000 000 tokens |
| Resposta máxima | 65 536 tokens |
| Custo de entrada | 0,66 $ por 1 milhão de tokens |
| Custo de saída | 1,98 $ por 1 milhão de tokens |

Os números foram atualizados para o checkpoint **V4-Pro-0813**: o contexto cresceu de 128 000
para um milhão, a resposta máxima foi de 8 192 para 65 536 tokens, e a saída até barateou (era
0,55 $ e 2,19 $). A limitação anterior, «contexto uma ordem de grandeza menor que o da
Anthropic», foi removida: agora ele é igual ao do Claude-Sonnet-5.

No perfil consta `params.maxTokens: 32768` — esta é a limitação de **uma resposta**. Se o
resultado for cortado com o motivo `length`, aumente-o (o teto do modelo é 65 536) ou divida a
tarefa.

**O identificador do modelo não foi verificado com uma chamada real** — confira-o com a
requisição `GET /models` a `https://api.deepseek.com`. Os preços atuais estão em
[DeepSeek Pricing](https://api-docs.deepseek.com/quick_start/pricing).

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | **o resultado é seu**: «We assign any rights, title, and interests — if any — in the Outputs … to you» (item 4.2) |
| Uso comercial | permitido de forma direta e ampla: uso pessoal, pesquisa, desenvolvimento de produtos derivados e até treinamento de outros modelos (destilação) |
| O que é obrigatório | revelar aos seus usuários que o conteúdo foi gerado por IA (item 8.1); não usar a marca DeepSeek sem permissão |
| Texto das condições | <https://cdn.deepseek.com/policies/en-US/deepseek-open-platform-terms-of-service.html> |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

Os pesos de um registro em nuvem não são entregues a você, e por isso se trata do **resultado da
geração**.

Estas são as condições mais generosas entre os registros em nuvem do catálogo: a permissão de
treinar outros modelos com o resultado está escrita diretamente no texto, ao passo que na
maioria dos fornecedores isso é justamente proibido. A obrigação é uma só e é fácil deixá-la
escapar — marcar para o usuário final que o texto foi feito por IA.

As condições foram conferidas pelo texto delas em 27.08.2026.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`api.deepseek.com`.

## Erros frequentes

* **402 / «Insufficient Balance»** — o saldo não foi reposto.
* **Resposta vazia, motivo `length`** — o modelo gastou o limite da resposta em raciocínio;
  aumente o `params.maxTokens` no perfil do modelo.
* **Entrada longa demais** — um milhão de tokens parece infinito, mas o histórico do chat vai
  para a entrada a cada passo do agente; leve os materiais volumosos para arquivos e coloque
  links na descrição.

# DeepSeek-V4-Flash

**Alocação:** em nuvem (API da DeepSeek, compatível com OpenAI)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://api.deepseek.com`,
modelo `deepseek-v4-flash`
**Referência da chave:** `deepseek.apiKey`

Rápida e muito barata: a qualidade é menor que a do DeepSeek-V4-Pro
(notas de habilidade de 76 a 84 contra 84 a 89). Pegue-a para trabalho simples em massa:
traduções, resumos curtos, rascunhos, pequenas correções. O registro mais barato do catálogo já
não é ela, e sim o Ling-3.0-Flash — 0,021 $ por milhão de tokens de entrada.

## Como obter a chave

**A chave é a mesma** do DeepSeek-V4-Pro: os dois modelos referenciam `deepseek.apiKey`.
Se ela já estiver informada — este modelo funcionará sozinho.

Se ainda não houver chave:

1. Registre-se em [platform.deepseek.com](https://platform.deepseek.com/).
2. Coloque saldo (**Top up**).
3. **API keys → Create new API key**, copie o valor (mostrado uma única vez).
4. No AI2P: **Configurações → Catálogos → Modelos de IA → DeepSeek-V4-Flash → «Definir a chave de API»**.

## Limitações e custo

| | |
|---|---|
| Contexto | 1 000 000 tokens |
| Resposta máxima | 32 768 tokens |
| Custo de entrada | 0,14 $ por 1 milhão de tokens |
| Custo de saída | 0,28 $ por 1 milhão de tokens |

Os números foram atualizados: o contexto cresceu de 128 000 para um milhão, a resposta máxima
foi de 8 192 para 32 768 tokens e o preço caiu de duas a quatro vezes (era 0,27 $ e 1,10 $). A
saída agora é **sete vezes mais barata** do que a do DeepSeek-V4-Pro, com o mesmo contexto.

No perfil consta `params.maxTokens: 32768` — este é o teto de uma resposta neste modelo.

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

## Quando pegar

Com o controle **preço ↔ qualidade** do projeto deslocado para o lado do preço, a seleção
automática do executor escolherá este modelo sozinha (ET, item 2.7). Se a tarefa exigir cuidado,
defina o executor à mão ou desloque o controle para a qualidade.

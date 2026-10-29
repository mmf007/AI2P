# Kimi-K3.1-Pro

**Alocação:** em nuvem (Moonshot AI, compatível com OpenAI)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://api.moonshot.ai/v1`,
**Referência da chave:** `moonshot.apiKey`

A Moonshot AI lançou este modelo em 23.10.2026; ele entrou no catálogo do AI2P pela tarefa
T-653-S0, a revisão de fabricantes de 29.10.2026. O identificador `moonshotai/kimi-k3.1-pro`, o preço e as
modalidades foram verificados por uma consulta ao catálogo público OpenRouter.

Contexto de 1 048 576 tokens, resposta de até 943 718 tokens, 2,00 $ e 10,00 $ por milhão de
tokens, pontuações 87-92. O registro mais próximo do catálogo é **Kimi-K3**.

Entrada aceita: texto e Markdown, código-fonte, imagens, vídeo.

## Como obter a chave

1. Registre-se em [platform.moonshot.ai](https://platform.moonshot.ai/).
2. Coloque saldo na seção **Billing**. Sem saldo, as requisições são recusadas.
3. Abra o [console de chaves](https://platform.moonshot.ai/console/api-keys) →
   **Create API key** e dê um nome.
4. Copie o valor — ele é mostrado **uma única vez**. A chave começa com `sk-`.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Kimi-K3.1-Pro → «Definir a chave de API»**.

A chave pertence à organização: é cifrada com a chave dela e replicada para todos os servidores
da organização (cap. 10 do ET).

## Limitações e custo

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Resposta máxima | 943 718 tokens |
| Custo de entrada | 2,00 $ por 1 milhão de tokens |
| Custo de saída | 10,00 $ por 1 milhão de tokens |

**O identificador do modelo não foi verificado com uma chamada real** — ele foi tomado do
catálogo de modelos sem o prefixo do fornecedor. Confira-o com a requisição `GET /models` a
`https://api.moonshot.ai/v1`. Os preços atuais estão no console da Moonshot.

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | a Moonshot **não reivindica direitos** sobre o conteúdo («we do not claim ownership of it») |
| Uso comercial | permitido |
| O que é obrigatório | lembrar que, por padrão, o que é enviado e recebido pode ir para o desenvolvimento e o treinamento dos modelos da Moonshot — a restrição se acerta por um contrato corporativo à parte |
| Texto das condições | <https://platform.kimi.ai/docs/agreement/modeluse> |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

Sobre os pesos: a Moonshot chama o K3.1 Pro de modelo de pesos abertos, mas em 29.10.2026 o
cartão `moonshotai/Kimi-K3.1-Pro` no HuggingFace não é encontrado pela consulta (resposta 401),
então a licença dos pesos NÃO foi verificada. O antecessor Kimi K3 tem licença própria
(`kimi-k3`), não MIT. Para um registro em nuvem isso não importa — você não recebe os pesos.

As condições foram conferidas pelo texto delas, e a licença dos pesos, pelo cartão do modelo —
ambos em 29.10.2026.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`api.moonshot.ai`. Os pesos são abertos, mas 2,8 trilhões de parâmetros não se levantam no seu
computador — para trabalho local há no catálogo modelos de 27 a 35 bilhões.

## Erros frequentes

* **401 «invalid api key»** — a chave foi criada na plataforma chinesa (`moonshot.cn`) e o
  `baseUrl` aponta para a internacional; elas não são intercambiáveis.
* **404 «model not found»** — o id mudou; confira com a lista `GET /models`.
* **Resposta vazia, motivo `length`** — aumente o `params.maxTokens` no perfil
  (por padrão 32000).

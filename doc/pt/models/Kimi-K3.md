# Kimi-K3

**Alocação:** em nuvem (Moonshot AI, compatível com OpenAI)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://api.moonshot.ai/v1`,
modelo `kimi-k3`
**Referência da chave:** `moonshot.apiKey`

**O melhor modelo aberto pelo índice consolidado da Artificial Analysis** no momento em que o
catálogo foi ampliado: 2,8 trilhões de parâmetros (104 bilhões ativos), notas de habilidade de
85 a 90 — colado nos carros-chefe fechados. Contexto de 1 048 576 tokens, e a entrada aceita
imagens e vídeo. Preço de 3 $ e 15 $ por milhão: mais caro que os vizinhos chineses, mas com
qualidade maior também.

**A licença é própria — Kimi K3 License, não é MIT nem Apache.** Os pesos são abertos, mas leia
as condições de aplicação comercial à parte; «aberto» aqui não significa «faça o que quiser».

## Como obter a chave

1. Registre-se em [platform.moonshot.ai](https://platform.moonshot.ai/).
2. Coloque saldo na seção **Billing**. Sem saldo, as requisições são recusadas.
3. Abra o [console de chaves](https://platform.moonshot.ai/console/api-keys) →
   **Create API key** e dê um nome.
4. Copie o valor — ele é mostrado **uma única vez**. A chave começa com `sk-`.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → Kimi-K3 → «Definir a chave de API»**.

A chave pertence à organização: é cifrada com a chave dela e replicada para todos os servidores
da organização (cap. 10 do ET).

## Limitações e custo

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Resposta máxima | 65 536 tokens |
| Custo de entrada | 3,00 $ por 1 milhão de tokens |
| Custo de saída | 15,00 $ por 1 milhão de tokens |

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

À parte, sobre os pesos, porque aqui é fácil errar: o Kimi K3 tem **licença própria** (no cartão
`moonshotai/Kimi-K3` ela se chama `kimi-k3`), e não MIT nem Apache 2.0. O jargão «modelo aberto
significa MIT» aqui é falso. Para um registro em nuvem isso não importa (você não recebe os
pesos), mas será útil se você decidir levantar o modelo na sua casa.

As condições foram conferidas pelo texto delas, e a licença dos pesos, pelo cartão do modelo —
ambos em 27.08.2026.

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

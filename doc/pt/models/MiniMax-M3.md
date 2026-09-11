# MiniMax-M3

**Alocação:** em nuvem (MiniMax, compatível com OpenAI)
**Conexão:** `provider: openai-compatible`, `baseUrl: https://api.minimax.io/v1`,
modelo `minimax-m3`
**Referência da chave:** `minimax.apiKey`

Barato e com contexto grande: 0,30 $ e 1,20 $ por milhão de tokens com contexto de
1 048 576 — quatro vezes mais barato que o GLM-5.2 e dez vezes mais barato que o
Kimi-K3. Notas de habilidade de 80 a 85: o meio de campo do catálogo.

O que o distingue é a **entrada multimodal**: além de texto e código-fonte, o modelo aceita
imagens e vídeo, custando o mesmo que um modelo de texto barato. Serve para tarefas em que o
material chega em capturas de tela ou gravação de tela.

## Como obter a chave

1. Registre-se em [platform.minimax.io](https://platform.minimax.io/)
   (a plataforma internacional; a chinesa tem outro domínio e outras chaves).
2. Coloque saldo na seção de faturamento.
3. Abra **Account → API Keys → Create new secret key** e dê um nome.
4. Copie o valor — ele é mostrado **uma única vez**.
5. No AI2P: **Configurações → Catálogos → Modelos de IA → MiniMax-M3 → «Definir a chave de API»**.

A chave pertence à organização: é cifrada com a chave dela e replicada para todos os servidores
da organização (cap. 10 do ET).

## Limitações e custo

| | |
|---|---|
| Contexto | 1 048 576 tokens |
| Resposta máxima | 65 536 tokens |
| Custo de entrada | 0,30 $ por 1 milhão de tokens |
| Custo de saída | 1,20 $ por 1 milhão de tokens |

**O identificador do modelo não foi verificado com uma chamada real** — ele foi tomado do
catálogo de modelos sem o prefixo do fornecedor. Confira-o com a requisição `GET /models` a
`https://api.minimax.io/v1`.

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | a MiniMax **não reivindica direitos** sobre o conteúdo gerado («We do not claim ownership of User Contributions or User Generated Content») |
| Uso comercial | permitido |
| O que é obrigatório | lembrar que você concede à MiniMax uma licença perpétua, irrevogável e não exclusiva de uso do que é enviado e recebido |
| Texto das condições | <https://www.minimax.io/platform/protocol/terms-of-service> |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

Os pesos do MiniMax-M3 estão publicados sob uma **licença community própria** (no cartão
`MiniMaxAI/MiniMax-M3` ela se chama `minimax-community`), e não sob MIT ou Apache 2.0 — se você
pretende levantar o modelo na sua casa, leia-a à parte.

Conferido em 27.08.2026: a licença dos pesos, pelo cartão do modelo; as condições sobre o
resultado, pelo texto publicado das condições da MiniMax
(<https://agent.minimax.io/doc/en/terms-of-service.html>).

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`api.minimax.io`.

## Erros frequentes

* **401 «invalid api key»** — uma chave da plataforma chinesa não funciona na internacional
  (e vice-versa); crie a chave no mesmo lugar para onde o `baseUrl` aponta.
* **404 «model not found»** — o id mudou; confira com a lista `GET /models`.
* **Qualidade abaixo do esperado em código complexo** — o nicho do modelo é outro; para código
  pegue o GLM-5.2 ou o Kimi-K3.

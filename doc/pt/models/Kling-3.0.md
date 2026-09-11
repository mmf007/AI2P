# Kling-3.0

**Alocação:** em nuvem — gateway [fal.ai](https://fal.ai/models)
**Conexão:** `provider: fal-ai`, `baseUrl: https://queue.fal.run`, modelo `fal-ai/kling-video/v3/pro/image-to-video`
**Referência da chave:** `fal.apiKey`
**O que faz:** imagem → vídeo com som, até 15 segundos, habilidades `video-animate` 93

O melhor «animador de imagem» do §6.3 do relatório T-213 (`video-animate` 93): som nativo,
controle de voz, até 15 segundos. Está conectado o ramo **Pro**; o fornecedor tem ainda o
Standard (mais barato) e o 4K (mais caro) — eles podem ser criados com um registro próprio no
catálogo, trocando no perfil apenas o identificador do modelo.

## Como obter a chave

1. Crie uma conta em [fal.ai](https://fal.ai/) (entrada pelo GitHub ou Google).
2. Coloque saldo na seção [Billing](https://fal.ai/dashboard/billing): o gateway funciona por
   pré-pagamento e, sem dinheiro na conta, a requisição é recusada.
3. Abra [API Keys](https://fal.ai/dashboard/keys) e clique em **Add key**.
4. Copie o valor — ele é mostrado **uma única vez**.
5. No AI2P: **Configurações → Modelos → Kling-3.0 → «Chave de API»**.

A chave é **uma só** para todos os registros da fal.ai (referência `fal.apiKey`): colocando-a
uma vez, você liga de uma vez os doze modelos de mídia em nuvem. A chave pertence à organização
— é cifrada com a chave dela e replicada para todos os servidores da organização (cap. 10 do
ET). Sem chave, o registro do catálogo não pode ficar ativo e não pode ser colocado como
executor.

## Limitações e custo

| | |
|---|---|
| Quadro inicial | obrigatório, um só, PNG / JPEG / WebP |
| Duração | de 3 a 15 segundos (no modelo de requisição — 5) |
| Som | nativo; com controle de voz sai mais caro |
| Custo | 0,168 $ por segundo com som (sem som — 0,112 $, com voz — 0,196 $) |
| Prompt negativo | é tomado do `params.negative` do perfil |

O identificador do modelo e os preços foram conferidos com o catálogo do fornecedor em
**27.08.2026** (requisição a `https://fal.ai/api/models` e ao esquema de requisição do
endpoint). **O modelo não foi verificado com uma chamada real** — a geração é paga. Confira você
mesmo o id e o preço: `GET /models` do catálogo do gateway (`https://fal.ai/api/models`) e a
[página do modelo](https://fal.ai/models/fal-ai/kling-video/v3/pro/image-to-video). Os preços no
gateway mudam sem aviso.

Quem faz a conta é o fornecedor. **No custo da tarefa do AI2P esse preço não é considerado** e
fica em zero: nos modelos de mídia paga-se não por tokens, e sim por imagem, segundo ou minuto,
e na resposta do gateway não há tokens de forma alguma. A tarifa é escrita em palavras no
console da tarefa e no resumo do resultado.

## Licença

Os pesos do modelo são fechados e não são distribuídos a ninguém — aqui não há o que licenciar,
por isso as condições se referem ao **resultado da geração**, e não aos pesos. O dono do modelo
é a Kuaishou; ao lado valem as condições do próprio gateway.

* No catálogo do fornecedor este registro consta como `licenseType: commercial` — **o uso
  comercial do resultado é permitido** (conferido em 27.08.2026 por requisição ao catálogo).
* Condições exatas: [página do modelo no gateway](https://fal.ai/models/fal-ai/kling-video/v3/pro/image-to-video) e [condições da fal.ai](https://fal.ai/terms).
* Regras de uso do dono do modelo: https://app.klingai.com/global/dev/document-api
* As condições sobre o resultado o dono do modelo pode alterar; elas não se aplicam
  retroativamente ao que você já gerou, mas antes de publicar uma série vale relê-las.
* Pagamento pela geração: **existe** — quem calcula é o fornecedor, e a tarifa está acima, na
  seção sobre o custo.

## Requisitos de hardware

Nenhum: quem calcula é o fornecedor. É preciso ter saída para a internet até `queue.fal.run`
(envio da tarefa e consulta do estado) e até o cofre de arquivos do gateway (`*.fal.media`) —
é de lá que o AI2P baixa o arquivo pronto para os artefatos da tarefa.

## Como a tarefa é formulada

O prompt de um modelo de mídia é a **descrição da tarefa por inteiro**: nem o título, nem os
critérios de aceitação, nem a experiência do projeto chegam até ele.

* a referência `@obj:OBJ-3` na descrição se expande no passaporte literal do objeto do projeto
  (personagem, locação, estilo) a **cada** execução — a correção do passaporte já vale para o
  quadro seguinte;
* a linha «put the result into the file `video/shot-1.mp4`» coloca o arquivo pronto também na
  pasta do projeto; nos artefatos da tarefa ele está sempre. **As linhas de indicação são
  reconhecidas apenas em russo e em inglês** — as palavras-reconhecedoras estão fixadas no
  código, e por isso escreva a indicação em uma dessas duas línguas, ainda que o texto da cena
  esteja em português;
* a imagem de origem é buscada NA PASTA DO PROJETO pelo caminho relativo indicado na descrição
  («use `refs/hero.png` as the source») ou no quadro de referência do objeto nomeado; ela vai
  para a requisição por inteiro, como endereço `data:` — o AI2P não tem um cofre de arquivos
  próprio, e não temos o direito de publicar o seu arquivo para fora como link;
* os campos da requisição (duração, resolução, voz, número de polígonos) são editados no perfil
  do modelo — seção `request`; pegue os valores no esquema do endpoint da página do modelo, pois
  um valor desconhecido é rejeitado pelo gateway com HTTP 422.

## LoRA

O treinamento e a conexão de um adaptador aqui estão **indisponíveis**, e no catálogo isso está
registrado com honestidade: `"lora": { "supported": false, "reason": "provider" }`. Os pesos do
modelo são fechados e o fornecedor não aceita um arquivo de adaptador próprio. Uma tarefa em que
seja nomeada a referência a um objeto com adaptador LoRA não chegará à geração: o AI2P recusará
na hora e proporá no chat um executor que saiba fazer isso (T-14-S1). A imagem constante, aqui,
é mantida pelo quadro inicial vindo do quadro de referência do objeto do projeto (`@obj:`).

## Erros frequentes

* **A tarefa não parte sem imagem** — o Kling 3.0, neste registro, só funciona a partir de um
  quadro inicial (o modo de texto é outro identificador de modelo).
* **Mais caro que no cálculo** — a cobrança é por segundo, e o som e o controle de voz elevam o
  preço do segundo.
* **«Chave de API não encontrada»** — a chave não foi colocada em nenhum registro da fal.ai;
  coloque-a uma vez em qualquer um deles.
* **HTTP 401 / 403 do gateway** — a chave é inválida ou foi revogada.
* **HTTP 429** — o fornecedor limitou a frequência de requisições; repita a tarefa mais tarde.

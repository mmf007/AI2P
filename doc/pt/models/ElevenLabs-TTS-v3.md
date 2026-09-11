# ElevenLabs-TTS-v3

**Alocação:** em nuvem — gateway [fal.ai](https://fal.ai/models)
**Conexão:** `provider: fal-ai`, `baseUrl: https://queue.fal.run`, modelo `fal-ai/elevenlabs/tts/eleven-v3`
**Referência da chave:** `fal.apiKey`
**O que faz:** texto → fala, habilidades `audio-speech` 96

A melhor síntese de fala do §6.4 do relatório T-213 (`audio-speech` 96). O campo de texto deste
endpoint se chama `text`, e não `prompt` — o conector coloca a descrição da tarefa onde o modelo
de requisição do perfil do modelo indicar. A voz é definida pelo campo `voice` (no modelo —
`Rachel`), e o idioma, pelo campo `language_code` ou pelo próprio texto.

## Como obter a chave

1. Crie uma conta em [fal.ai](https://fal.ai/) (entrada pelo GitHub ou Google).
2. Coloque saldo na seção [Billing](https://fal.ai/dashboard/billing): o gateway funciona por
   pré-pagamento e, sem dinheiro na conta, a requisição é recusada.
3. Abra [API Keys](https://fal.ai/dashboard/keys) e clique em **Add key**.
4. Copie o valor — ele é mostrado **uma única vez**.
5. No AI2P: **Configurações → Modelos → ElevenLabs-TTS-v3 → «Chave de API»**.

A chave é **uma só** para todos os registros da fal.ai (referência `fal.apiKey`): colocando-a
uma vez, você liga de uma vez os doze modelos de mídia em nuvem. A chave pertence à organização
— é cifrada com a chave dela e replicada para todos os servidores da organização (cap. 10 do
ET). Sem chave, o registro do catálogo não pode ficar ativo e não pode ser colocado como
executor.

## Limitações e custo

| | |
|---|---|
| Texto | a descrição da tarefa inteira vai para o campo `text` |
| Voz | `voice` no modelo de requisição (por padrão `Rachel`) |
| Estabilidade | `stability` de 0 a 1 (no modelo — 0,5) |
| Custo | **no catálogo do gateway o preço deste registro não está publicado** — veja-o na página do modelo e na fatura |

O identificador do modelo e os preços foram conferidos com o catálogo do fornecedor em
**27.08.2026** (requisição a `https://fal.ai/api/models` e ao esquema de requisição do
endpoint). **O modelo não foi verificado com uma chamada real** — a geração é paga. Confira você
mesmo o id e o preço: `GET /models` do catálogo do gateway (`https://fal.ai/api/models`) e a
[página do modelo](https://fal.ai/models/fal-ai/elevenlabs/tts/eleven-v3). Os preços no gateway
mudam sem aviso.

Quem faz a conta é o fornecedor. **No custo da tarefa do AI2P esse preço não é considerado** e
fica em zero: nos modelos de mídia paga-se não por tokens, e sim por imagem, segundo ou minuto,
e na resposta do gateway não há tokens de forma alguma. A tarifa é escrita em palavras no
console da tarefa e no resumo do resultado.

## Licença

Os pesos do modelo são fechados e não são distribuídos a ninguém — aqui não há o que licenciar,
por isso as condições se referem ao **resultado da geração**, e não aos pesos. O dono do modelo
é a ElevenLabs; ao lado valem as condições do próprio gateway.

* No catálogo do fornecedor este registro consta como `licenseType: commercial` — **o uso
  comercial do resultado é permitido** (conferido em 27.08.2026 por requisição ao catálogo).
* Condições exatas: [página do modelo no gateway](https://fal.ai/models/fal-ai/elevenlabs/tts/eleven-v3) e [condições da fal.ai](https://fal.ai/terms).
* Regras de uso do dono do modelo: https://elevenlabs.io/terms-of-use
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
* o quadro inicial não há como transmitir a este modelo — se a descrição nomear um arquivo de
  imagem, o AI2P dirá isso em uma linha no console da tarefa e não ficará calado;
* os campos da requisição (duração, resolução, voz, número de polígonos) são editados no perfil
  do modelo — seção `request`; pegue os valores no esquema do endpoint da página do modelo, pois
  um valor desconhecido é rejeitado pelo gateway com HTTP 422.

## LoRA

O treinamento e a conexão de um adaptador aqui estão **indisponíveis**, e no catálogo isso está
registrado com honestidade: `"lora": { "supported": false, "reason": "provider" }`. Os pesos do
modelo são fechados e o fornecedor não aceita um arquivo de adaptador próprio. Uma tarefa em que
seja nomeada a referência a um objeto com adaptador LoRA não chegará à geração: o AI2P recusará
na hora e proporá no chat um executor que saiba fazer isso (T-14-S1). A imagem constante, aqui,
é mantida pelo uso da mesma `voice` em todas as tarefas de locução.

## Erros frequentes

* **O custo da tarefa no AI2P é zero** — isso não é gratuidade, e sim a ausência de preço
  publicado: quem faz a conta é o fornecedor.
* **A fala soa «picotada»** — aumente a `stability` no modelo de requisição.
* **«Chave de API não encontrada»** — a chave não foi colocada em nenhum registro da fal.ai;
  coloque-a uma vez em qualquer um deles.
* **HTTP 401 / 403 do gateway** — a chave é inválida ou foi revogada.
* **HTTP 429** — o fornecedor limitou a frequência de requisições; repita a tarefa mais tarde.

# Zonos-2-TTS

**Alocação:** em nuvem — gateway [fal.ai](https://fal.ai/models)
**Conexão:** `provider: fal-ai`, `baseUrl: https://queue.fal.run`, modelo `fal-ai/zonos2`
**Referência da chave:** `fal.apiKey`
**O que faz:** texto + amostra de voz → fala com esse timbre, habilidades `audio-speech` 86

Os primeiros registros do catálogo que aceitam **áudio de referência** (mecanismo do T-249-S0).
Aqui a voz constante de um personagem não é mantida por um adaptador LoRA, e sim por uma
**amostra**: 3-30 segundos de gravação vão para o campo `reference_audio_url` da requisição e o modelo copia
o timbre (clonagem zero-shot). O texto da tarefa vai para o campo `text`.

O idioma de normalização do texto é definido por `language` (`en_us` no modelo; o esquema lista `en_us`, `en_gb`, `fr_fr`, `de` e outros). `accurate_mode: true` fica mais perto da voz da amostra, `false` é mais expressivo. O resultado vem como WAV 44,1 kHz mono.

## Como usar

1. Crie no projeto um objeto do tipo **gravação de referência** (aba «Objetos» do cartão do
   projeto) e coloque no campo de arquivo o caminho de um `.wav` ou `.mp3` relativo à pasta do
   projeto; normalmente como filho do personagem.
2. Refira-o na descrição da tarefa com `@obj:OBJ-N` ou nomeie o arquivo por palavras
   («amostra de voz refs/vera.wav»).
3. Aponte o executor para este registro do catálogo — a amostra de voz é **obrigatória**: no esquema do endpoint `reference_audio_url` é o único campo requerido (`required: ['reference_audio_url']`). Sem gravação o gateway responde HTTP 422, por isso o AI2P para a tarefa antes, do seu lado.

## Como obter a chave

1. Crie uma conta em [fal.ai](https://fal.ai/) (entrada por GitHub ou Google).
2. Recarregue o saldo em [Billing](https://fal.ai/dashboard/billing): o gateway funciona em
   pré-pagamento e sem saldo a requisição é recusada.
3. Abra [API Keys](https://fal.ai/dashboard/keys) e pressione **Add key**.
4. Copie o valor — ele é mostrado **uma única vez**.
5. No AI2P: **Configurações → Modelos → Zonos-2-TTS → «Chave de API»**.

Todos os registros do fal.ai compartilham **uma** chave (referência `fal.apiKey`): colocando-a uma
vez você liga de imediato todos os modelos de mídia em nuvem do gateway. A chave pertence à
organização — é cifrada com a chave dela e replicada para todos os seus servidores (cap. 10 do ET).
Sem chave o registro do catálogo não pode estar ativo nem ser atribuído a um executor.

## Limitações e custo

| | |
|---|---|
| Texto | toda a descrição da tarefa vai para o campo `text` |
| Amostra de voz | campo `reference_audio_url`, **uma** gravação, até **30 segundos**, `audio/wav` ou `audio/mpeg` |
| Duração da amostra | `maxSeconds: 30` é **guardado, mas o AI2P não verifica**: um arquivo longo demais é recusado pelo próprio gateway |
| Custo | o catálogo do gateway **não publica preço** para este registro — consulte a página do modelo e a fatura |

O identificador do modelo, o nome do campo da amostra e o preço foram conferidos com o catálogo do
provedor em **14.09.2026** (requisição a `https://fal.ai/api/models` e ao esquema OpenAPI da fila do
endpoint). **O modelo não foi verificado por uma chamada real** — a geração é paga. Confira o id e o
preço você mesmo: `GET /models` do catálogo (`https://fal.ai/api/models`) e a
[página do modelo](https://fal.ai/models/fal-ai/zonos2). Os preços do gateway mudam sem aviso.

A cobrança é do provedor. **O custo da tarefa no AI2P não conta esse preço** e permanece zero: os
modelos de mídia são pagos por caractere, segundo ou minuto, e não por tokens, e na resposta do
gateway não há tokens. A tarifa é escrita por extenso no console da tarefa e no resumo do resultado.

## Licença

As condições se referem ao **resultado da geração**, não aos pesos: quem calcula é o provedor.
O dono do modelo é Zyphra; ao lado valem as condições do próprio gateway.

* No catálogo do provedor este registro tem `licenseType: commercial` — **o uso comercial do
  resultado é permitido** (conferido em 14.09.2026 por requisição ao catálogo).
* Os pesos do próprio modelo são abertos sob **Apache 2.0** — cartão [https://huggingface.co/Zyphra/ZONOS2](https://huggingface.co/Zyphra/ZONOS2), campo
  `cardData.license` verificado por requisição a `https://huggingface.co/api/models` em 14.09.2026.
  Isso importa também na conexão em nuvem: a licença dos pesos não proíbe o uso comercial de uma voz
  sintetizada a partir da sua própria amostra.
* Condições exatas: [página do modelo no gateway](https://fal.ai/models/fal-ai/zonos2) e
  [condições do fal.ai](https://fal.ai/terms).
* Regras de uso do dono do modelo: https://www.zyphra.com/terms-of-service
* **A licença do modelo não concede direitos sobre a voz em si.** Uma amostra da voz de outra pessoa
  sem o consentimento dela é um risco jurídico à parte, e nem MIT nem Apache 2.0 o eliminam.
* Pagamento pela geração: **sim** — quem conta é o provedor, a tarifa está acima, na seção de custo.

## Requisitos de hardware

Nenhum: quem calcula é o provedor. É preciso saída à internet para `queue.fal.run` (envio da tarefa
e consulta do estado) e para o armazenamento de arquivos do gateway (`*.fal.media`), de onde o AI2P
baixa o arquivo pronto para os artefatos da tarefa. A amostra de voz vai **no corpo da requisição**
como endereço `data:`; o gateway não tem upload de arquivo separado.

## Como a tarefa é formulada

O prompt de um modelo de mídia é **toda a descrição da tarefa**: nem o título, nem os critérios de
aceitação, nem a experiência do projeto chegam até ele.

* a referência `@obj:OBJ-3` na descrição se expande no passaporte literal do objeto do projeto a
  **cada** execução — editar o passaporte já vale para a tarefa seguinte;
* uma referência a um objeto do tipo **gravação de referência** OBRIGA a passar o arquivo;
* a linha «colocar o resultado no arquivo `audio/fala-1.wav`» também deixa o arquivo pronto na pasta
  do projeto; nos artefatos da tarefa ele está sempre;
* não há como passar um quadro inicial a este modelo (`refImage.kind: none`) — se a descrição nomear
  um arquivo de imagem, o AI2P avisa com uma linha no console da tarefa;
* os campos da requisição são editados no perfil do modelo, seção `request`; pegue os valores do
  esquema do endpoint, um valor desconhecido é recusado pelo gateway com HTTP 422.

## LoRA

Aqui o treino e a conexão de um adaptador **não estão disponíveis**, e no catálogo isso está escrito
com honestidade: `"lora": { "supported": false, "reason": "provider" }`. Os pesos estão do lado do
provedor e ele não aceita o seu arquivo de adaptador. Uma tarefa que nomeie uma referência a um
objeto com adaptador LoRA não chegará à geração: o AI2P recusa de imediato e oferece no chat um
executor capaz (T-14-S1). Aqui a voz constante é mantida pela **gravação de referência**, e isso sai
mais barato que treinar: um clone de dez segundos não é pior que um adaptador e custa zero.

## Erros frequentes

* **«A gravação de referência não foi passada»** — a descrição não nomeia nem uma referência `@obj:`
  a um objeto do tipo «gravação de referência» nem um arquivo de som; o marcador `{audio}` está no
  modelo de requisição e a tarefa para antes do envio.
* **«Este modelo não aceita gravação»** — o executor aponta para outro registro; a seção `refAudio`
  é declarada apenas pelos registros citados no relatório T-251-S0.
* **HTTP 422 do gateway** — no modelo de requisição apareceu um campo que o endpoint não tem, ou um
  valor fora da enumeração do esquema.
* **«Chave de API não encontrada»** — a chave não está em nenhum registro do fal.ai; coloque-a uma
  vez em qualquer um deles.
* **HTTP 401 / 403 do gateway** — a chave é inválida ou foi revogada.
* **HTTP 429** — o provedor limitou a frequência das requisições, repita a tarefa mais tarde.

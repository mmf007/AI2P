# Kandinsky-6.0-Pro

**Alocação:** em nuvem — gateway [fal.ai](https://fal.ai/models)
**Conexão:** `provider: fal-ai`, `baseUrl: https://queue.fal.run`, modelo `fal-ai/kandinsky6-pro/text-to-video`
**Referência da chave:** `fal.apiKey`
**O que faz:** texto → vídeo de 5 segundos com som, habilidades `video-generate` 90

A Kandinsky Lab lançou este modelo em 05.10.2026; ele entrou no catálogo do AI2P pela tarefa
T-466-S0, a revisão de fabricantes de 13.10.2026. O identificador `fal-ai/kandinsky6-pro/text-to-video`, o preço e as
modalidades foram verificados por uma consulta ao catálogo público fal.ai.

A geração seguinte ao **Kandinsky-5.0**. Preço do gateway: 1,35 $ por clipe de 5 segundos 480p com 50 passos (≈ 0,27 $ por segundo), upscale para 1080p 1,53 $, menos passos custam menos (20 passos 0,56 $). A variante Lite é a mesma versão, endpoint `fal-ai/kandinsky6-lite/text-to-video` (0,16 $ por clipe).

## Como obter a chave

1. Crie uma conta em [fal.ai](https://fal.ai/) (entrada pelo GitHub ou Google).
2. Coloque saldo na seção [Billing](https://fal.ai/dashboard/billing): o gateway funciona por
   pré-pagamento e, sem dinheiro na conta, a requisição é recusada.
3. Abra [API Keys](https://fal.ai/dashboard/keys) e clique em **Add key**.
4. Copie o valor — ele é mostrado **uma única vez**.
5. No AI2P: **Configurações → Modelos → Kandinsky-6.0-Pro → «Chave de API»**.

A chave é **uma só** para todos os registros da fal.ai (referência `fal.apiKey`): colocando-a
uma vez, você liga de uma vez os doze modelos de mídia em nuvem. A chave pertence à organização
— é cifrada com a chave dela e replicada para todos os servidores da organização (cap. 10 do
ET). Sem chave, o registro do catálogo não pode ficar ativo e não pode ser colocado como
executor.

## Limitações e custo

| | |
|---|---|
| Duração | 5 segundos |
| Resolução | 480p, upscale para 1080p ou 4x |
| Preço | 1,35 $ por clipe com 50 passos |

O identificador do modelo e os preços foram conferidos com o catálogo do fornecedor em
**13.10.2026** (requisição a `https://fal.ai/api/models` e ao esquema de requisição do
endpoint). **O modelo não foi verificado com uma chamada real** — a geração é paga. Confira você
mesmo o id e o preço: `GET /models` do catálogo do gateway (`https://fal.ai/api/models`) e a
[página do modelo](https://fal.ai/models/fal-ai/kandinsky6-pro/text-to-video). Os preços no
gateway mudam sem aviso.

Quem faz a conta é o fornecedor. **No custo da tarefa do AI2P esse preço não é considerado** e
fica em zero: nos modelos de mídia paga-se não por tokens, e sim por imagem, segundo ou minuto,
e na resposta do gateway não há tokens de forma alguma. A tarifa é escrita em palavras no
console da tarefa e no resumo do resultado.

## Licença

Os pesos do Kandinsky 6.0 são **abertos** sob a licença MIT (`kandinskylab/Kandinsky-6.0-Pro-5s-Diffusers`, verificado
em 13.10.2026 por consulta à API do HuggingFace), mas o repositório exige aceitar um acordo (`gated: auto`):
sem token pessoal os arquivos não são entregues. Este registro não usa os pesos: quem calcula é o gateway fal.ai.

* O catálogo do gateway marca o registro `licenseType: commercial` — **o uso comercial do resultado é permitido**.
* Condições: [página do modelo](https://fal.ai/models/fal-ai/kandinsky6-pro/text-to-video) e [termos da fal.ai](https://fal.ai/terms).
* Cobrança por geração: **sim** — feita pelo provedor; tarifa na seção de preço.

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
é mantida pelo quadro inicial (registro **Seedance-2.5-I2V**) e por um passaporte detalhado do
objeto na descrição.

## Erros frequentes

* **«Chave de API não encontrada»** — a chave não foi colocada em nenhum registro da fal.ai;
  coloque-a uma vez em qualquer um deles.
* **HTTP 401 / 403 do gateway** — a chave é inválida ou foi revogada.
* **HTTP 429** — o fornecedor limitou a frequência de requisições; repita a tarefa mais tarde.

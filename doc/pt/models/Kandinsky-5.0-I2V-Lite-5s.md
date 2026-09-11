# Kandinsky-5.0-I2V-Lite-5s

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`,
modelo `kandinsky5lite_i2v_5s`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** imagem + texto → vídeo (5 segundos), habilidades `video-animate` 78,
`video-generate` 74

O mesmo Kandinsky 5.0 Video Lite do `Kandinsky-5.0-T2V-Lite-sft-5s`, mas o clipe é construído
**a partir de uma imagem**: o quadro inicial define a aparência do personagem e da cena, e a
descrição da tarefa diz o que acontece no quadro. É para isso que o registro existe: **um mesmo
personagem em vários clipes** não se mantém por palavras — quem o mantém é o quadro de
referência.

Uma variante de **10 segundos na versão Lite do i2v não existe**: a kandinskylab publicou apenas
o `Kandinsky-5.0-I2V-Lite-5s` (verificado pela lista de repositórios da organização em
2026-08-20). Os 10 segundos existem no `T2V-Lite` textual e nas versões Pro — estas últimas para
outro hardware.

## Como dar a ele o quadro inicial

O arquivo é buscado **na pasta do projeto**, com caminho relativo. Há duas maneiras:

1. **Escrever na descrição da tarefa** — uma linha com a palavra-indicação e o nome do arquivo:
   «Взять за основу `refs/hero.png`», «Стартовый кадр `refs/hero.png`», `based on
   refs/hero.png`. A linha é recortada do prompt — o nome do arquivo não entra no texto da
   geração. **As linhas de indicação são reconhecidas apenas em russo e em inglês** — as
   palavras-reconhecedoras estão fixadas no código, e por isso escreva a indicação em uma dessas
   duas línguas, ainda que o texto da cena esteja em português.
2. **Referenciar um objeto do projeto** — `@obj:OBJ-3` na descrição. Na execução, a referência é
   expandida no passaporte do personagem e nos caminhos dos quadros de referência dele; o
   primeiro caminho é justamente o quadro inicial, e os próprios caminhos são retirados do
   prompt. Assim é mais cômodo: o passaporte se edita em um único lugar e vale para todos os
   quadros seguintes.

Um arquivo nomeado em palavras tem precedência sobre o caminho vindo do objeto. Se não houver
indicação de arquivo alguma, a tarefa cai com um erro claro: sem imagem este modelo não tem o
que fazer.

A imagem vai para o ComfyUI (`POST /upload/image`), no diretório `input` dele, com o nome da
tarefa, e é colocada no nó `LoadImage` do grafo; em seguida o `ImageScale` a ajusta ao
`width`×`height` do quadro (recortando pelo centro), e por isso é melhor já fazer as proporções
do quadro inicial próximas de 768×512.

## Um mesmo personagem em uma série de clipes

| recurso | o que dá |
|---|---|
| **quadro de referência** (este modelo) | rosto, roupa e cor se mantêm entre os clipes |
| **`seed` fixo** (`params.seed` no perfil) | o mesmo «caráter» de geração em todos os quadros; vazio — aleatório, como antes |
| **encadeamento** | salvar o último quadro da cena N como imagem e entregá-lo como entrada da cena N+1 |
| **passaporte literal** na descrição | a descrição da aparência se repete palavra por palavra, em vez de ser recontada |

O primeiro quadro da série é cômodo de obter com um modelo t2i comum ou de desenhar à mão — daí
em diante tudo se constrói a partir dele.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos de IA → Kandinsky-5.0-I2V-Lite-5s → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **quatro arquivos de pesos** no repositório de modelos (`storage.modelsRepo`, por padrão
  `C:\ai` no Windows e `~/ai` no Linux/macOS):

| Arquivo | Tamanho | Onde |
|---|---|---|
| `kandinsky5lite_i2v_5s.safetensors` | ~4,3 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `clip_l.safetensors` | ~235 MiB | `text_encoders` |
| `hunyuan_video_vae_bf16.safetensors` | ~470 MiB | `vae` |

**No total, cerca de 16 GB de download**, mas três dos quatro arquivos são **exatamente os
mesmos** do `Kandinsky-5.0-T2V-Lite-sft-5s`, e ficam no mesmo grupo `Kandinsky-5`. Se o modelo
textual já estiver instalado, será baixado apenas o próprio DiT — **cerca de 4,3 GiB**. É baixado
com retomada: uma instalação interrompida continua do ponto em que parou.

Enquanto os arquivos não estiverem no lugar, o modelo **não pode ficar ativo** — isso é
verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **MIT** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/kandinskylab/Kandinsky-5.0-I2V-Lite-5s> (cartão do modelo) |
| Pagamento pela geração | não há — quem calcula é o seu computador |

A licença foi extraída do cartão do modelo em 27.08.2026 por uma requisição ao HuggingFace
(campo `cardData.license`), e não de memória. A MIT é a mais livre das licenças do catálogo: o
uso comercial é permitido, e a única exigência é manter o texto da licença e o aviso de direitos
autorais.

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **no mínimo 6 GB de VRAM** |
| Driver NVIDIA | **580 ou mais novo** |
| Espaço em disco | ~18 GB (pesos + pacote ComfyUI); ao lado do modelo t2v — cerca de 4,3 GB a mais |
| Memória RAM | a partir de 16 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

**Sobre o driver.** No pacote ComfyUI vem o torch compilado para CUDA 13.0. Em um driver antigo
(por exemplo 527.99 = CUDA 12.0) ele cai não com um erro claro, e sim com **access violation** —
o processo simplesmente desaparece. Isso não é um defeito do AI2P: atualize o driver NVIDIA para
580+. Se o modelo «silenciosamente não inicia», comece pelo `nvidia-smi`.

## Quanto esperar

O mesmo que a variante textual: em 6 GB de VRAM, um clipe de **768×512, 121 quadros, 50 passos**
é calculado em cerca de uma hora (a medição foi feita no modelo t2v: 58 minutos e 49 segundos; os
pesos e o grafo aqui têm o mesmo tamanho). O tempo limite da tarefa é de 180 minutos
(`params.timeoutMinutes`); no executor, o campo «Tempo limite de resposta, min» tem precedência,
e 0 significa esperar sem limite.

O andamento da geração é visível no **console da tarefa**, no cartão da tarefa.

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `width` / `height` | 768 × 512 | resolução do quadro; é a ela que a imagem inicial é ajustada |
| `length` | 121 | quadros (≈5 segundos) |
| `steps` | 50 | passos de difusão; menos — mais rápido e mais grosseiro |
| `negative` | vazio | prompt negativo |
| `seed` | não há | **seed fixo**: vazio (ou 0) — aleatório a cada tarefa |
| `timeoutMinutes` | 180 | quanto esperar pelo resultado |

A descrição da tarefa vai inteira para o prompt — exceto a indicação do quadro inicial e a
indicação do tipo «put the result into the file X.mp4»: as duas são executadas pelo conector.

## Treinamento de LoRA

O modelo sabe trabalhar com adaptador LoRA, e **é possível treinar o adaptador direto do AI2P**:
o editor de LoRA (cartão do projeto → «Objetos» → objeto-personagem → «Conjunto de dados e
treinamento»).

**Com o que se treina.** O [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — ele tem
uma tarefa pronta, `k5-lite-i2v-5s-sd`, exatamente para estes pesos, funciona em **uma** placa de
vídeo e sabe calcular a atenção por `sdpa`. O
[kandinsky-5-lora-train](https://github.com/kandinskylab/kandinsky-5-lora-train) oficial não
serve para isso: ele levanta `torchrun` e `init_device_mesh("cuda")`, ou seja, exige NCCL e
várias placas de vídeo — no Windows ele não inicia de forma alguma.

**O que é instalado adicionalmente.** O pacote `musubi-tuner` (código-fonte, ~30 MB) e o **Python
3.12** (~45 MB) vêm junto com o modelo — pelo botão «Instalar», no mesmo lugar em que os pesos
são baixados; um Python 3.10–3.12 já presente no computador é usado como está e não é baixado de
novo. Na primeira execução do treinamento, o treinador cria ao lado um ambiente (`.venv`) e
instala nele o `torch` para CUDA 12.4 — são mais **cerca de 3 GB** e de dez a vinte minutos na
primeira vez. Depois disso o ambiente é reaproveitado.

O próprio treinador puxa mais dois arquivos no primeiro cache de texto — **não são os mesmos
arquivos usados pela geração**: a geração usa as compilações reduzidas do ComfyUI, e o
treinamento precisa dos modelos originais do Hugging Face:

| O quê | Tamanho | De onde |
|---|---|---|
| `Qwen/Qwen2.5-VL-7B-Instruct` | ~16 GB | cache do Hugging Face |
| `openai/clip-vit-large-patch14` | ~1,7 GB | cache do Hugging Face |

**O que é preciso no computador:** Python **3.10–3.12** no `PATH` (caso contrário o treinamento
para com uma mensagem sobre isso) e uma placa de vídeo NVIDIA. O treinamento de um adaptador do
modelo Lite cabe em **13 a 16 GB de VRAM**; o cache dos latentes exige mais — é o passo mais
voraz. O caminho próprio para o Python é definido pela variável de ambiente
`AI2P_LORA_PYTHON`, e o índice próprio dos wheels do torch, por `AI2P_LORA_TORCH_INDEX` (por
padrão `https://download.pytorch.org/whl/cu124`).

**Como o treinamento acontece.** O AI2P coloca os quadros do conjunto de dados em
`<pasta do projeto>/lora/<código do objeto>/dataset` (para cada quadro, um `.txt` de mesmo nome
com a legenda), escreve ao lado o `dataset.toml` e o `train.cmd` e executa este último. O script
faz três passos em sequência: cache dos latentes → cache das saídas dos codificadores de texto →
o próprio treinamento. O adaptador pronto vai para
`<repositório de modelos>/loras/<código do objeto>.safetensors` e se torna o adaptador atual do
objeto — é de lá que a geração o pega (nó `LoraLoaderModelOnly`).

O `dataset.toml` e o `train.cmd` são **reescritos antes de cada execução**: eles pertencem ao
perfil do modelo (seção `lora.train.files`), e é lá que devem ser editados, e não no disco. É lá
também que se mudam o número de passos (`lora.train.start.steps`, por padrão 2000), o rank e
todas as chaves do treinador.

**O que este caminho não dá.** O conjunto de dados no AI2P é formado por **imagens**, e não por
clipes, e por isso o que se treina assim é um adaptador de **aparência** (personagem, estilo). Um
adaptador de **movimento** (como os oficiais `Arc-right`, `Dolly-in`) se treina em vídeo — e no
editor de LoRA ainda não há conjunto de dados de vídeo.

## Erros frequentes

* **«Este modelo precisa de um quadro inicial»** — na descrição não há nem arquivo de imagem nem
  referência a um objeto com quadro de referência.
* **«O arquivo do quadro inicial não foi encontrado na pasta do projeto»** — o caminho é contado
  a partir da pasta do projeto; sair para fora (`..`, `C:\…`) é proibido de propósito.
* **«O projeto não tem pasta definida neste computador»** — a pasta do projeto é definida no
  cartão dele e é própria de cada servidor.
* **O personagem «flutua» assim mesmo** — verifique se o quadro inicial é sempre o mesmo e se o
  `seed` está fixado; clipes com `width`/`height` diferentes também divergem.
* **O processo some sem mensagem** — quase sempre é o driver NVIDIA antigo.
* **Falta de VRAM** — reduza `width`/`height` ou `length`.
* **«cannot create the virtual environment»** no treinamento de LoRA — não há Python 3.10–3.12
  no computador ou ele não está no `PATH`; instale-o ou informe o caminho em `AI2P_LORA_PYTHON`.

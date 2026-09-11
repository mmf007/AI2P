# Qwen-Image-Edit-2511

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`,
modelo `qwen_image_edit_2511`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** imagem + indicação → imagem alterada, habilidades `image-edit` 90,
`image-text` 90, `image-inpaint` 85, `image-generate` 82

O modelo de **correção de imagens** da Alibaba, edição 2511. Ao contrário do
`Qwen-Image-2512`, que desenha do zero, este pega uma **imagem pronta** e executa a indicação em
palavras comuns: «troque o couro do sofá por pelo», «tire os fios do céu», «deixe o texto da
placa em russo», «pinte a parede de azul». É o único registro do catálogo que cobre as
habilidades `image-edit` e `image-inpaint`.

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia a imagem de origem,
manda o workflow e recolhe o `.png` pronto para os artefatos da tarefa.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → Qwen-Image-Edit-2511 → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **três arquivos de pesos** no repositório de modelos (`storage.modelsRepo`), grupo
  `Qwen-Image`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `qwen_image_edit_2511_fp8mixed.safetensors` | ~19,1 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `qwen_image_vae.safetensors` | ~242 MiB | `vae` |

**No total, cerca de 30 GB de download.** O grupo `Qwen-Image` é comum com o modelo
`Qwen-Image-2512`: se aquele já estiver instalado, será baixado apenas o arquivo de pesos
próprio (~19 GiB).

Enquanto os arquivos não estiverem no lugar, o modelo **não pode ficar ativo** — isso é
verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **Apache 2.0** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/Comfy-Org/Qwen-Image-Edit_ComfyUI> (cartão do modelo) |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

A licença foi conferida pelo cartão do modelo em 27.08.2026. Repare: livre é a licença dos
**pesos**, e não das imagens que você corrige — os direitos sobre a imagem de origem continuam
sendo uma questão de onde você a tirou.

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **a partir de 16 GB de VRAM** (24 GB é confortável) |
| Driver NVIDIA | **580 ou mais novo** — em driver antigo o torch cai sem mensagem |
| Espaço em disco | ~32 GB (pesos + pacote ComfyUI); com o `Qwen-Image-2512` já instalado — ~21 GB |
| Memória RAM | a partir de 32 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

## De onde vem a imagem de origem

Do mesmo jeito que no `Kandinsky-5.0-I2V-Lite-5s`: o caminho do arquivo **relativo à pasta do
projeto** é escrito na descrição da tarefa, o conector envia o arquivo ao ComfyUI e coloca o
nome dele no modelo de requisição. O mais simples é dar uma referência a um objeto do projeto —
`@obj:OBJ-3`: em um objeto do tipo «quadro de referência» o arquivo já está indicado, e ele vai
sozinho para a tarefa.

A imagem de origem aqui é **obrigatória** (`refImage.required`): sem imagem a tarefa não começa,
em vez de devolver um resultado vazio.

O tamanho do resultado é definido **pela própria imagem de origem** — o nó
`FluxKontextImageScale` a ajusta ao tamanho permitido mais próximo. Os campos `width`/`height`
do perfil não influenciam a correção (eles permaneceram para o resumo da tarefa e para o
conjunto de dados do treinamento de LoRA).

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `steps` | 40 | passos de difusão; menos — mais rápido e mais grosseiro |
| `negative` | vazio | prompt negativo (aqui ele funciona, `cfg = 3`) |
| `timeoutMinutes` | 90 | quanto esperar pelo resultado |
| `width` / `height` | 1328 × 1328 | não influenciam a correção, veja acima |

A descrição da tarefa vai inteira para o prompt. Escreva **o que mudar**, e não o que está
retratado: «troque o fundo por uma cidade à noite» funciona melhor que uma descrição completa da
cena.

## Treinamento de LoRA

Suportado por completo: o adaptador é tanto **aplicado** (o nó `LoraLoaderModelOnly` é inserido
no grafo em tempo de execução) quanto **treinado** direto do AI2P — pelo editor de LoRA no cartão
do objeto do projeto.

Quem treina é o [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — os mesmos scripts do
`Qwen-Image-2512`, mas com a chave `--model_version edit-2511`: ela diz ao treinador que o
modelo tem uma imagem de controle, e os prompts são cacheados junto com ela.

**O treinador precisa de outros arquivos de pesos.** As compilações fp8, com que a geração
calcula, não servem para treinamento, e por isso o `train.cmd`, no primeiro treinamento, baixa
uma vez o par **bf16** — `qwen_image_edit_2511_bf16.safetensors` (~38 GiB) e
`qwen_2.5_vl_7b.safetensors` (~15,4 GiB) — para o subdiretório `train` do grupo `Qwen-Image`.
São cerca de **57 GB além da instalação**; a geração em si continua funcionando com os arquivos
fp8 leves.

O conjunto de dados no editor de LoRA são imagens com legendas. Para um modelo de correção isso
é o treinamento **do estilo do resultado**, e não de pares «antes/depois»: no editor de LoRA
ainda não há imagens de controle, e essa é uma limitação honesta, e não uma configuração.

O adaptador pronto vai para `<repositório de modelos>/loras/<código do objeto>.safetensors`.
Todas as chaves de treinamento, o `dataset.toml` e o `train.cmd` ficam no perfil do modelo
(`lora.train`) e são reescritos antes de cada execução — edite-os no perfil, e não nos arquivos.

## Erros frequentes

* **«O modelo exige uma imagem de origem»** — na descrição da tarefa não há nem caminho de
  arquivo nem referência `@obj:` a um objeto com arquivo.
* **O processo some sem mensagem** — quase sempre é o driver NVIDIA antigo (veja acima).
* **«Modelo não instalado»** — os arquivos não terminaram de baixar; abra «Instalar» e a janela
  mostrará o volume restante.
* **A correção «não reparou» na indicação** — reduza o volume da indicação: uma correção por
  tarefa sai mais confiável do que uma lista de cinco.
* **O ComfyUI está ocupado por um processo alheio** — o AI2P descarrega apenas o servidor que
  ele mesmo iniciou; um ComfyUI alheio já em execução na 8188 ele não toca.

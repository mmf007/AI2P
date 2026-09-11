# Kandinsky-5.0-Image-Lite

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `kandinsky5lite_t2i`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** texto → imagem, habilidades `image-text` 88, `image-generate` 84,
`image-concept` 83, `image-photo` 82

O modelo de imagem do Kandinsky Lab (Sber), 6 bilhões de parâmetros, resolução de até 1K. O
principal motivo para mantê-lo: ele **entende prompts em russo e realidades russas** e
**escreve em cirílico dentro do quadro** — placas, textos em embalagens, legendas. Nos demais
modelos abertos do catálogo o cirílico sai pior ou não sai de forma alguma.

A licença é **MIT**, a mais livre de todos os registros do catálogo.

Ele é parente dos registros de vídeo `Kandinsky-5.0-*` que já estão no catálogo: os
codificadores de texto são comuns, mas o grupo de instalação é próprio (o modelo de imagem tem
um arquivo de pesos e um VAE próprios).

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e recolhe o
`.png` pronto para os artefatos da tarefa.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → Kandinsky-5.0-Image-Lite → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **quatro arquivos de pesos** no repositório de modelos (`storage.modelsRepo`), grupo
  `Kandinsky-5-Image`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `kandinsky5lite_t2i.safetensors` | ~11,2 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `clip_l.safetensors` | ~235 MiB | `text_encoders` |
| `ae.safetensors` | ~320 MiB | `vae` |

**No total, cerca de 22 GB de download.** É baixado com retomada: uma instalação interrompida
continua do ponto em que parou.

Sobre o `ae.safetensors`: este é o VAE **do Flux**, e não um próprio. É assim que o modelo em si
é feito — tanto no modelo oficial do ComfyUI quanto nas instruções dos autores
(`weights/flux/vae` vai para `ComfyUI/models/vae`).

Enquanto os arquivos não estiverem no lugar, o modelo **não pode ficar ativo** — isso é
verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **MIT** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/kandinskylab/Kandinsky-5.0-T2I-Lite> (cartão do modelo) |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

A licença foi conferida pelo cartão do modelo em 27.08.2026 (`cardData.license: mit`). A MIT não
impõe restrições de área de aplicação — nisso ela difere da SDXL (que tem uma lista de
aplicações proibidas) e da FLUX.2 [dev] (que proíbe o uso comercial).

Os pesos são baixados **diretamente dos autores**, pois não há repacote da Comfy-Org para este
modelo: `kandinskylab/Kandinsky-5.0-T2I-Lite`, arquivo
`model/kandinsky5lite_t2i.safetensors` — exatamente o nome que o modelo oficial do ComfyUI
espera.

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **a partir de 12 GB de VRAM** (16 GB é confortável) |
| Driver NVIDIA | **580 ou mais novo** — em driver antigo o torch cai sem mensagem |
| Espaço em disco | ~25 GB (pesos + pacote ComfyUI) |
| Memória RAM | a partir de 16 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

## Quanto esperar

Cinquenta passos são **um a dois minutos** por quadro de 1024×1024 em uma placa moderna (em uma
H100 os autores medem 13 segundos). O tempo limite da tarefa no perfil está definido em 90
minutos (`params.timeoutMinutes`).

O andamento da geração é visível no **console da tarefa**, no cartão da tarefa: para lá é
despejada a saída própria do ComfyUI, junto com a barra de progresso dele.

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `width` / `height` | 1024 × 1024 | resolução do quadro (o modelo também é calculado para 1280×768) |
| `steps` | 50 | passos de difusão; abaixo de 30 a qualidade cai visivelmente |
| `negative` | vazio | prompt negativo — funciona (`cfg 3.5`) |
| `timeoutMinutes` | 90 | quanto esperar pelo resultado |

A descrição da tarefa vai inteira para o prompt. O texto que deve aparecer no quadro escreva-o
**entre aspas** — assim o modelo entende que aquilo é uma inscrição, e não uma descrição.

## Treinamento de LoRA

**A aplicação, sim; o treinamento a partir do AI2P, não.**

Um adaptador pronto se conecta como em todos os registros do ComfyUI: o nó
`LoraLoaderModelOnly` é inserido no grafo em tempo de execução, e o arquivo é buscado em
`<repositório de modelos>/loras`.

Já treinar um adaptador direto do AI2P não há como: o
[musubi-tuner](https://github.com/kohya-ss/musubi-tuner) suporta apenas os modelos de VÍDEO do
Kandinsky 5 e escreve diretamente na documentação dele (`docs/kandinsky5.md`) que os modelos
Image Lite não são suportados. Por isso o registro tem `lora.train.kind = external`: o botão de
treinamento responderá com recusa imediata, e não depois de meia hora de cálculo. Se aparecer um
treinador — basta acrescentar a seção `lora.train` ao perfil do modelo, sem precisar de código.

Treinar um adaptador fora do AI2P (por exemplo, com os scripts originais de
<https://github.com/kandinskylab/kandinsky-5>) e colocar o arquivo em `loras` — dá; ele será
aplicado normalmente.

## Erros frequentes

* **O cirílico no quadro sai torto assim mesmo** — aumente os `steps` e escreva a inscrição
  entre aspas; frases muito longas não saem bem em nenhum modelo aberto.
* **«Modelo não instalado»** — os arquivos não terminaram de baixar; abra «Instalar» e a janela
  mostrará o volume restante.
* **O botão de treinamento de LoRA recusa** — é assim mesmo, não há treinador para o Image Lite
  (veja acima).
* **Falta de VRAM** — reduza `width`/`height`.
* **O ComfyUI está ocupado por um processo alheio** — o AI2P descarrega apenas o servidor que
  ele mesmo iniciou; um ComfyUI alheio já em execução na 8188 ele não toca.

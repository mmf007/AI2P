# SDXL-1.0

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `sdxl_base_1_0`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** texto → imagem, habilidades `image-generate` 70, `image-concept` 70,
`image-photo` 68

Stable Diffusion XL 1.0, da Stability AI — **o registro menos exigente do catálogo**: 6,9 GB em
um único arquivo e 8 GB de memória de vídeo. Ele está criado justamente como o degrau mais
baixo: em qualidade perde para todos os vizinhos e **não sabe escrever texto no quadro**, mas em
compensação roda onde o FLUX.2, o Qwen-Image e o Kandinsky não iniciariam de forma alguma.

O segundo argumento a favor dele: para o SDXL foram escritos mais adaptadores LoRA do que para
todos os demais modelos juntos, e todos eles funcionam aqui sem adaptação.

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e recolhe o
`.png` pronto para os artefatos da tarefa.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → SDXL-1.0 → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **um arquivo de pesos** no repositório de modelos (`storage.modelsRepo`), grupo `SDXL-1.0`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `sd_xl_base_1.0.safetensors` | ~6,5 GiB | `checkpoints` |

**No total, cerca de 7 GB de download** — menos que em qualquer outro registro de imagem. É
baixado com retomada.

A categoria `checkpoints`: este é um checkpoint completo, do qual o ComfyUI tira de uma vez o
modelo, os codificadores de texto e o VAE. O segundo estágio do SDXL (Refiner) não faz parte da
entrega — é um arquivo à parte, e o registro foi criado como o mais leve possível.

Enquanto o arquivo não estiver no lugar, o modelo **não pode ficar ativo** — isso é verificado
também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **CreativeML Open RAIL++-M** (de 26 de julho de 2023) |
| Uso comercial | permitido, sem royalties e sem limiar de receita |
| O que é obrigatório | anexar o texto da licença, manter o aviso de direitos autorais e **repassar adiante as restrições de aplicação** — a todos a quem você entregar o modelo ou uma derivação dele |
| Restrições | a licença proíbe uma série de aplicações (a lista está em «Attachment A. Use Restrictions»): violação da lei, dano a menores, desinformação, discriminação etc. |
| Texto da licença | <https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0/blob/main/LICENSE.md> |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

A licença foi conferida pelo texto do `LICENSE.md` do repositório em 27.08.2026. É uma licença
«aberta, mas responsável»: os direitos ela concede como uma permissiva, mas acrescenta uma lista
de aplicações proibidas que você é obrigado a repassar adiante junto com o modelo. Sobre o
resultado da geração em si a Stability não reivindica direitos.

O arquivo é baixado diretamente do repositório da Stability AI
(<https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0>), sem necessidade de
repacote.

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **a partir de 6 GB de VRAM** (8 GB é confortável) |
| Driver NVIDIA | **580 ou mais novo** — em driver antigo o torch cai sem mensagem |
| Espaço em disco | ~10 GB (pesos + pacote ComfyUI) |
| Memória RAM | a partir de 8 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

## Quanto esperar

Vinte e cinco passos em um quadro de 1024×1024 são **segundos a dezenas de segundos**, mesmo em
uma placa modesta. O tempo limite da tarefa no perfil está definido em 60 minutos
(`params.timeoutMinutes`).

O andamento da geração é visível no **console da tarefa**, no cartão da tarefa.

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `width` / `height` | 1024 × 1024 | resolução do quadro; o SDXL foi treinado justamente em 1024 |
| `steps` | 25 | passos de difusão |
| `negative` | vazio | prompt negativo — funciona (`cfg 7`), e no SDXL ele é mais útil que nos modelos novos |
| `timeoutMinutes` | 60 | quanto esperar pelo resultado |

O SDXL entende mal descrições longas: é melhor escrever o prompt como enumeração («o quê, onde,
em que estilo, em que plano»), e não como um parágrafo corrido. Os modelos mais novos (FLUX.2,
Qwen-Image) se comportam exatamente ao contrário.

A descrição da tarefa vai inteira para o prompt. Uma indicação do tipo «put the result into the
file X.png» é executada pelo conector. **As linhas de indicação são reconhecidas apenas em russo
e em inglês** — as palavras-reconhecedoras estão fixadas no código, e por isso escreva a
indicação em uma dessas duas línguas, ainda que o texto da cena esteja em português.

## Treinamento de LoRA

**A aplicação, sim; o treinamento a partir do AI2P, não.**

Um adaptador pronto se conecta como em todos os registros do ComfyUI: o nó
`LoraLoaderModelOnly` é inserido no grafo em tempo de execução, e o arquivo é buscado em
`<repositório de modelos>/loras`. Este é justamente o caso em que há milhares de adaptadores
prontos em acesso aberto.

Treinar um adaptador direto do AI2P não há como: o `musubi-tuner` não conhece a família Stable
Diffusion — para o SDXL existe o [sd-scripts](https://github.com/kohya-ss/sd-scripts), do mesmo
autor. Por isso o registro tem `lora.train.kind = external`, e o botão de treinamento responde
com recusa imediata. Um arquivo treinado por fora basta ser colocado em `loras`.

## Erros frequentes

* **O texto no quadro vira uma papa** — o SDXL não sabe fazer isso; pegue o `Qwen-Image-2512`
  ou o `Kandinsky-5.0-Image-Lite`.
* **A imagem sai «borrada» em um tamanho fora do padrão** — o SDXL foi treinado em 1024×1024, e
  quadros bem menores lhe saem mal.
* **«Modelo não instalado»** — o arquivo não terminou de baixar; abra «Instalar».
* **O botão de treinamento de LoRA recusa** — é assim mesmo (veja acima).
* **O ComfyUI está ocupado por um processo alheio** — o AI2P descarrega apenas o servidor que
  ele mesmo iniciou.

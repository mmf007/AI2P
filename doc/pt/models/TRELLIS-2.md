# TRELLIS-2

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `trellis_2`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** imagem → modelo 3D COM COR, habilidade `3d-image` 85

Um modelo aberto da Microsoft Research (4 bilhões de parâmetros), que transforma UMA imagem em
uma malha tridimensional. É o único dos três registros locais de 3D do catálogo que entrega não
só a forma, mas também a cor: depois do estágio da forma vem um estágio de textura à parte.

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e recolhe o
`.glb` pronto para os artefatos da tarefa.

**Ele lê a IMAGEM, e não o texto.** No grafo do modelo não há codificador de texto de forma
alguma — a condição é dada pelo DINOv3 a partir do quadro inicial. O modelo não enxerga a
descrição da tarefa: o que estiver desenhado na imagem é o que sairá. O quadro inicial é
definido na descrição da tarefa por uma referência a um objeto do projeto (`@obj:OBJ-3`) ou pelo
caminho do arquivo relativo à pasta do projeto; sem ele a tarefa não inicia de forma alguma.

O fundo do quadro inicial é removido automaticamente (BiRefNet), e o quadro é recortado em torno
do objeto: um 3D feito a partir de uma imagem com fundo sai nitidamente pior.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → TRELLIS-2 → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **cinco arquivos de pesos** no repositório de modelos (`storage.modelsRepo`), grupo
  `TRELLIS-2`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `trellis_2_int8_convrot.safetensors` | ~4,89 GiB | `diffusion_models` |
| `dino_v3_vit_l.safetensors` | ~1,13 GiB | `clip_vision` |
| `trellis_2_shape_vae_bf16.safetensors` | ~1,02 GiB | `vae` |
| `trellis_2_texture_vae_bf16.safetensors` | ~904 MiB | `vae` |
| `birefnet.safetensors` | ~424 MiB | `background_removal` |

**No total, cerca de 9,0 GB de download** (8,34 GiB). É baixado com retomada: uma instalação
interrompida continua do ponto em que parou, e os arquivos já baixados não são baixados de novo.

Os pesos são tomados na compilação **int8** — duas vezes mais leve que a bf16 (~9,6 GiB) e
calculada para uma placa de consumo. Quem precisar da precisão máxima troca o `unet_name` no
modelo de workflow por `trellis_2_bf16.safetensors` e acrescenta o arquivo ao manifesto por
conta própria.

Enquanto os arquivos não estiverem no lugar, o modelo **não pode ficar ativo** — isso é
verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **MIT** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/microsoft/TRELLIS.2-4B> (cartão do modelo) |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

Os arquivos que o AI2P baixa são o repacote da Comfy-Org
(<https://huggingface.co/Comfy-Org/TRELLIS.2>), que vem sob a mesma MIT. A remoção do fundo é
feita pelo BiRefNet (<https://huggingface.co/ZhengPeng7/BiRefNet>), também MIT. As licenças
foram conferidas pelos cartões dos modelos em 27.08.2026; nos modelos abertos elas mudam
raramente, mas antes de um lançamento comercial verifique os cartões mais uma vez.

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **a partir de 12 GB de VRAM** (16 GB é confortável) |
| Driver NVIDIA | **580 ou mais novo** — em driver antigo o torch cai sem mensagem |
| Espaço em disco | ~13 GB (pesos + pacote ComfyUI) |
| Memória RAM | a partir de 16 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

O número de memória de vídeo é uma **estimativa**, e não uma medição: ele é calculado pelo
tamanho dos pesos int8 e dos dois VAE. Se faltar memória, reduza o `target_resolution` do
`Trellis2UpsampleStage` no modelo de workflow (por padrão 1536).

## Quanto esperar

O grafo tem quatro estágios — estrutura, forma, aumento de resolução e textura —, e por isso um
modelo é calculado bem mais devagar que no Hunyuan3D: de poucos a dezenas de minutos em uma placa
moderna. O tempo limite da tarefa no perfil está definido em 90 minutos
(`params.timeoutMinutes`).

O andamento da geração é visível no **console da tarefa**, no cartão da tarefa: para lá é
despejada a saída própria do ComfyUI, junto com a barra de progresso dele.

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `steps` | 20 | passos do estágio da FORMA — a alavanca principal da qualidade |
| `timeoutMinutes` | 90 | quanto esperar pelo resultado |
| `width` / `height` | 1024 | NÃO influenciam o resultado: em 3D não há quadro |
| `negative` | vazio | não funciona — não há codificador de texto no grafo |

Os outros três estágios rodam com o número de passos do modelo oficial (12): mudá-los só é
possível editando o próprio workflow — eles alteram pouco o resultado e consomem o mesmo tempo.

**O que o modelo não tem de propósito.** O modelo oficial do ComfyUI, depois da malha, faz ainda
o desdobramento UV e coze o conjunto completo de mapas PBR (`UnwrapMesh` →
`BakeTextureFromVoxel`, `BakeNormalMapFromMesh`, `BakeAmbientOcclusion` → `ApplyTextureToMesh`).
No nosso modelo esse ramo não existe: são mais uma dezena de nós e um cozimento de 2048 a 4096
pontos, e não há como verificá-los sem placa de vídeo. A cor, ainda assim, está lá — ela é
transmitida pelos vértices da malha (`PaintMesh`). Quem precisar dos mapas PBR acrescenta o ramo
ao seu workflow: todos os nós existem no ComfyUI.

## Treinamento de LoRA

**Não é suportado, e esta é uma recusa honesta, e não algo por fazer.** Não existe hoje um
treinador público de LoRA para arquiteturas 3D: o
[musubi-tuner](https://github.com/kohya-ss/musubi-tuner), sobre o qual se apoia todo o
treinamento de adaptadores no AI2P, só sabe lidar com imagens e vídeo. Por isso, no perfil consta
`lora.supported: false`, e o formulário do modelo mostra o motivo em palavras.

A constância do objeto entre quadros se consegue aqui de outra forma: forneça na entrada sempre a
mesma imagem de referência do personagem (objeto do projeto, referência `@obj:`) — de uma imagem
igual e de um `seed` igual sairá um modelo igual.

## Erros frequentes

* **«Modelo não instalado»** — os arquivos não terminaram de baixar; abra «Instalar» e a janela
  mostrará o volume restante.
* **A tarefa se recusa a iniciar sem imagem** — é assim mesmo: o modelo funciona apenas a partir
  de uma imagem. Dê uma referência `@obj:` a um quadro de referência ou o caminho do arquivo.
* **Falta de VRAM** — reduza o `target_resolution` do `Trellis2UpsampleStage`.
* **O objeto saiu cortado** — a remoção de fundo escolheu o objeto errado; dê um quadro em que o
  objeto desejado esteja sozinho e inteiro no enquadramento.
* **O processo some sem mensagem** — quase sempre é o driver NVIDIA antigo (veja acima).

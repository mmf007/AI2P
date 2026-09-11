# Hunyuan3D-2.1

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `hunyuan3d_2_1`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** imagem → modelo 3D, habilidade `3d-image` 86

Um modelo aberto da Tencent, que transforma UMA imagem em uma malha tridimensional. É o primeiro
registro local do catálogo a cobrir o 3D: antes dele, as habilidades `3d-*` só existiam nos
Tripo e Meshy em nuvem.

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e recolhe o
`.glb` pronto para os artefatos da tarefa.

**Ele lê a IMAGEM, e não o texto.** No grafo do modelo não há codificador de texto de forma
alguma — a condição é dada pelo CLIP Vision a partir do quadro inicial. O modelo não enxerga a
descrição da tarefa: o que estiver desenhado na imagem é o que sairá. O quadro inicial é
definido na descrição da tarefa por uma referência a um objeto do projeto (`@obj:OBJ-3`) ou pelo
caminho do arquivo relativo à pasta do projeto; sem ele a tarefa não inicia de forma alguma.

**Não haverá cor.** O ComfyUI calcula apenas o ramo da FORMA do Hunyuan3D
(`VAEDecodeHunyuan3D` → `VoxelToMesh` → `SaveGLB`). O ramo de coloração do repositório da Tencent
— justamente aquele que precisa de Linux, CUDA 12.4 e de uma rasterização CUDA própria — não
está implementado no motor, e não é possível conectá-lo com um pacote padrão. Se você precisa de
uma malha colorida, pegue a [TRELLIS-2](https://huggingface.co/microsoft/TRELLIS.2-4B), que está
criada no registro vizinho.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → Hunyuan3D-2.1 → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **um arquivo de pesos** no repositório de modelos (`storage.modelsRepo`), grupo
  `Hunyuan3D-2.1`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `hunyuan_3d_v2.1.safetensors` | ~6,86 GiB | `checkpoints` |

**No total, cerca de 7,4 GB de download.** É baixado com retomada: uma instalação interrompida
continua do ponto em que parou, e os arquivos já baixados não são baixados de novo.

Enquanto os arquivos não estiverem no lugar, o modelo **não pode ficar ativo** — isso é
verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **Tencent Hunyuan 3D 2.1 Community License** (não é uma licença aberta no sentido usual) |
| Uso comercial | permitido, mas com ressalvas — veja abaixo |
| Onde não se pode | União Europeia, Reino Unido, Coreia do Sul (esses territórios estão excluídos da licença) |
| Limiar | acima de 1 milhão de usuários ativos por mês é preciso uma licença à parte da Tencent |
| O que é obrigatório | marcar o produto com as palavras «Powered by Tencent Hunyuan» e anexar o arquivo Notice ao transferi-lo a terceiros |
| Texto da licença | <https://github.com/Tencent-Hunyuan/Hunyuan3D-2.1/blob/main/LICENSE> |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

Os arquivos que o AI2P baixa são o repacote da Comfy-Org
(<https://huggingface.co/Comfy-Org/hunyuan3D_2.1_repackaged>), que vem sob a mesma licença da
Tencent, e não sob MIT. A licença foi conferida pelo texto do repositório em 27.08.2026. As
condições dela são bem mais rígidas que a Apache 2.0 dos demais modelos locais do catálogo:
**antes de um lançamento comercial leia-a por inteiro.**

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **a partir de 8 GB de VRAM** (12 GB é confortável) |
| Driver NVIDIA | **580 ou mais novo** — em driver antigo o torch cai sem mensagem |
| Espaço em disco | ~10 GB (pesos + pacote ComfyUI) |
| Memória RAM | a partir de 16 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

O número de memória de vídeo é uma **estimativa**, e não uma medição: calcula-se apenas o ramo da
forma, e a exigência de «10 a 29 GB de VRAM» que aparece em análises do Hunyuan3D 2.1 se refere
ao pipeline completo da Tencent, junto com o cozimento da textura. Se faltar memória, reduza o
`octree_resolution` no modelo de workflow.

## Quanto esperar

Poucos minutos por modelo em uma placa moderna. O tempo limite da tarefa no perfil está definido
em 60 minutos (`params.timeoutMinutes`).

O andamento da geração é visível no **console da tarefa**, no cartão da tarefa: para lá é
despejada a saída própria do ComfyUI, junto com a barra de progresso dele.

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `steps` | 30 | passos de difusão; abaixo de 20 a forma se desfaz |
| `timeoutMinutes` | 60 | quanto esperar pelo resultado |
| `width` / `height` | 1024 | NÃO influenciam o resultado: em 3D não há quadro |
| `negative` | vazio | não funciona — não há codificador de texto no grafo |

A densidade da malha é definida não pelo perfil, e sim pelo próprio modelo de workflow: o
`octree_resolution` do `VAEDecodeHunyuan3D` (256) e o `threshold` do `VoxelToMesh` (0,6).

## Treinamento de LoRA

**Não é suportado, e esta é uma recusa honesta, e não algo por fazer.** Não existe hoje um
treinador público de LoRA para arquiteturas 3D: o
[musubi-tuner](https://github.com/kohya-ss/musubi-tuner), sobre o qual se apoia todo o
treinamento de adaptadores no AI2P, só sabe lidar com imagens e vídeo. Por isso, no perfil consta
`lora.supported: false`, e o formulário do modelo mostra o motivo em palavras.

A constância do objeto entre quadros se consegue aqui de outra forma: forneça na entrada sempre a
mesma imagem de referência do personagem (objeto do projeto, referência `@obj:`) — de uma imagem
igual e de um `seed` igual sairá uma malha igual.

## Erros frequentes

* **«Modelo não instalado»** — o arquivo não terminou de baixar; abra «Instalar» e a janela
  mostrará o volume restante.
* **A tarefa se recusa a iniciar sem imagem** — é assim mesmo: o modelo funciona apenas a partir
  de uma imagem. Dê uma referência `@obj:` a um quadro de referência ou o caminho do arquivo.
* **O modelo saiu branco** — cor neste registro não existe de forma alguma (veja o início).
* **Falta de VRAM** — reduza o `octree_resolution` no modelo de workflow.
* **O processo some sem mensagem** — quase sempre é o driver NVIDIA antigo (veja acima).

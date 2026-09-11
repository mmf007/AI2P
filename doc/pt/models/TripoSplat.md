# TripoSplat

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `triposplat`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** imagem → SPLATS GAUSSIANOS, habilidade `3d-image` 80

Um modelo aberto da Tripo AI (VAST-AI), que transforma UMA imagem não em uma malha, mas em uma
nuvem de gaussianas tridimensionais — os **splats gaussianos**. É o único registro do catálogo
que entrega esse resultado: um arquivo `.spz`, aberto por visualizadores de splats, pelo Unreal,
pelo Unity e por players web como o Babylon.js.

Splats NÃO são uma malha: neles não há polígonos nem UV, e por isso eles entram no pipeline de
jogo como estão ou são convertidos à parte. Em compensação, dão uma imagem fotográfica, com
bordas suaves e transparência, ali onde uma malha fica grosseira (vegetação, pelo, fumaça,
interiores).

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e recolhe o
`.spz` pronto para os artefatos da tarefa.

**Ele lê a IMAGEM, e não o texto.** No grafo do modelo não há codificador de texto de forma
alguma — a condição é dada pelo DINOv3 a partir do quadro inicial. O modelo não enxerga a
descrição da tarefa: o que estiver desenhado na imagem é o que sairá. O quadro inicial é
definido na descrição da tarefa por uma referência a um objeto do projeto (`@obj:OBJ-3`) ou pelo
caminho do arquivo relativo à pasta do projeto; sem ele a tarefa não inicia de forma alguma.

O fundo do quadro inicial é removido automaticamente (BiRefNet).

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → TripoSplat → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **cinco arquivos de pesos** no repositório de modelos (`storage.modelsRepo`), grupo
  `TripoSplat`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `dino_v3_vit_h.safetensors` | ~1,57 GiB | `clip_vision` |
| `triposplat_fp16.safetensors` | ~707 MiB | `diffusion_models` |
| `triposplat_vae_decoder_fp16.safetensors` | ~549 MiB | `vae` |
| `flux2-vae.safetensors` | ~321 MiB | `vae` |
| `birefnet.safetensors` | ~424 MiB | `background_removal` |

**No total, cerca de 3,8 GB de download** — o mais leve dos registros locais de 3D do catálogo.
É baixado com retomada: uma instalação interrompida continua do ponto em que parou, e os
arquivos já baixados não são baixados de novo.

Enquanto os arquivos não estiverem no lugar, o modelo **não pode ficar ativo** — isso é
verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **MIT** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/VAST-AI/TripoSplat> (cartão do modelo) |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

A remoção do fundo é feita pelo BiRefNet (<https://huggingface.co/ZhengPeng7/BiRefNet>), também
MIT; o codificador do quadro é o DINOv3 (Meta), que está no mesmo repositório da VAST-AI, sob a
mesma licença. As licenças foram conferidas pelos cartões dos modelos em 27.08.2026; nos modelos
abertos elas mudam raramente, mas antes de um lançamento comercial verifique os cartões mais uma
vez.

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **a partir de 8 GB de VRAM** (12 GB é confortável) |
| Driver NVIDIA | **580 ou mais novo** — em driver antigo o torch cai sem mensagem |
| Espaço em disco | ~6 GB (pesos + pacote ComfyUI) |
| Memória RAM | a partir de 16 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

O número de memória de vídeo é uma **estimativa**, e não uma medição. Se faltar memória, reduza o
`num_gaussians` do `VAEDecodeTripoSplat` no modelo de workflow (por padrão 262144).

## Quanto esperar

Poucos minutos por modelo em uma placa moderna: o modelo em si é pequeno (cerca de 700 MB de
pesos) e o estágio é um só, e não quatro, como no TRELLIS-2. O tempo limite da tarefa no perfil
está definido em 60 minutos (`params.timeoutMinutes`).

O andamento da geração é visível no **console da tarefa**, no cartão da tarefa: para lá é
despejada a saída própria do ComfyUI, junto com a barra de progresso dele.

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `steps` | 20 | passos de difusão |
| `timeoutMinutes` | 60 | quanto esperar pelo resultado |
| `width` / `height` | 1024 | NÃO influenciam o resultado: em 3D não há quadro |
| `negative` | vazio | não funciona — não há codificador de texto no grafo |

O número de gaussianas (`num_gaussians`, 262144) e o formato do arquivo (`spz`) são definidos
pelo modelo de workflow. Os formatos que o nó `SplatToFile3D` conhece são `spz`, `ply`, `splat`,
`ksplat`; o `spz` foi escolhido por ser o mais compacto.

**O que o modelo não tem de propósito.** O modelo oficial do ComfyUI desenha adicionalmente uma
volta de câmera ao redor do objeto e a salva como clipe (`RenderSplat` → `CreateVideo` →
`SaveVideo`). Nós não trouxemos esse ramo: é uma renderização à parte a cada geração, e o
resultado da tarefa já é o arquivo de splats. Ali mesmo está o `SplatToMesh` — o nó que
transforma splats em uma malha comum; quem precisar de malha o acrescenta ao seu workflow ou
pega a [TRELLIS-2](https://huggingface.co/microsoft/TRELLIS.2-4B).

## Treinamento de LoRA

**Não é suportado, e esta é uma recusa honesta, e não algo por fazer.** Não existe hoje um
treinador público de LoRA para arquiteturas 3D: o
[musubi-tuner](https://github.com/kohya-ss/musubi-tuner), sobre o qual se apoia todo o
treinamento de adaptadores no AI2P, só sabe lidar com imagens e vídeo. Por isso, no perfil consta
`lora.supported: false`, e o formulário do modelo mostra o motivo em palavras.

A constância do objeto entre quadros se consegue aqui de outra forma: forneça na entrada sempre a
mesma imagem de referência do personagem (objeto do projeto, referência `@obj:`) — de uma imagem
igual e de um `seed` igual sairá um resultado igual.

## Erros frequentes

* **«Modelo não instalado»** — os arquivos não terminaram de baixar; abra «Instalar» e a janela
  mostrará o volume restante.
* **A tarefa se recusa a iniciar sem imagem** — é assim mesmo: o modelo funciona apenas a partir
  de uma imagem. Dê uma referência `@obj:` a um quadro de referência ou o caminho do arquivo.
* **O arquivo `.spz` não abre no programa de sempre** — são splats, e não uma malha; é preciso um
  visualizador de splats gaussianos ou uma conversão.
* **Falta de VRAM** — reduza o `num_gaussians` no modelo de workflow.
* **O processo some sem mensagem** — quase sempre é o driver NVIDIA antigo (veja acima).

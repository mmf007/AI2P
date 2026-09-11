# LTX-2.5

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `ltx_2_5_distilled`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** texto → vídeo **com som**, habilidade `video-generate` 85

O modelo de vídeo da Lightricks, 22 bilhões de parâmetros, variante destilada. Este é o **único
modelo aberto que desenha vídeo e som em uma única passagem**: a trilha de áudio é calculada em
um latente próprio ao lado do vídeo e vai parar no mesmo `.mp4`. Em todos os demais modelos
locais do catálogo o som precisa ser sobreposto à parte.

Em qualidade de imagem ele é mais forte que o Wan 2.2 e o HunyuanVideo 1.5, mas também é mais
pesado: 22B contra 14B, e os pesos são fechados por um acordo (veja abaixo — os arquivos terão
de ser colocados à mão).

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e recolhe o
`.mp4` pronto para os artefatos da tarefa.

## Instalação: os arquivos terão de ser colocados à mão

O repositório dos pesos é **fechado por um acordo** (gated): o HuggingFace entrega os arquivos
apenas a quem aceitou a licença, e ainda assim por token pessoal. O baixador do AI2P vai sem
token e receberá recusa (verificado por requisição em 27.08.2026); por isso o procedimento é
este:

1. abra <https://huggingface.co/Lightricks/LTX-2.5>, entre na sua conta do HuggingFace e aceite
   as condições da licença;
2. baixe os quatro arquivos da tabela abaixo;
3. coloque-os no subdiretório `LTX-2.5` do repositório de modelos (`storage.modelsRepo`) —
   **de forma plana, sem pastas aninhadas**, sem mudar os nomes dos arquivos;
4. clique em **«Instalar»** no formulário do modelo: **Configurações → Catálogos → Modelos →
   LTX-2.5 → «Instalar»**. A instalação verá a coincidência de tamanhos e instalará apenas o
   pacote ComfyUI, e o modelo passará a constar como instalado.

| Arquivo | Tamanho | Onde |
|---|---|---|
| `ltx-2.5-22b-distilled-transformer-comfy-int8-convrot.safetensors` | ~20 GiB | `diffusion_models` |
| `gemma4-12b-with-proj-ltx-2.5-comfy-int8-convrot.safetensors` | ~14,3 GiB | `text_encoders` |
| `ltx-2.5-video-vae-bf16.safetensors` | ~1,4 GiB | `vae` |
| `ltx-2.5-audio-vae-bf16.safetensors` | ~348 MiB | `vae` |

**No total, cerca de 38,7 GB.** Enquanto os arquivos não estiverem no lugar, o modelo **não pode
ficar ativo** — isso é verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **LTX-2 Community License Agreement** (não é Apache nem MIT) |
| Uso comercial | permitido nas condições do acordo, que precisa ser aceito pessoalmente |
| O que é obrigatório | aceitar a licença no HuggingFace, manter o aviso; as condições limitam implantações de grande porte |
| Texto da licença | <https://github.com/Lightricks/LTX-2/blob/main/LICENSE.md> |
| Cartão do modelo | <https://huggingface.co/Lightricks/LTX-2.5> |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

Esta **não** é uma licença livre: o repositório está fechado por acordo e, até que ele seja
aceito, os arquivos são inacessíveis por completo. Antes de um lançamento comercial leia o texto
inteiro. A licença foi conferida pelo cartão do modelo em 27.08.2026
(`license_name: ltx-2-community-license-agreement`).

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **a partir de 24 GB de VRAM** (32 GB é confortável) |
| Driver NVIDIA | **580 ou mais novo** — em driver antigo o torch cai sem mensagem |
| Espaço em disco | ~41 GB (pesos + pacote ComfyUI) |
| Memória RAM | a partir de 32 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

Os arquivos de pesos foram tomados na compilação `int8-convrot` — justamente a que consta do
modelo oficial do ComfyUI: ela é duas vezes mais leve que a bf16 (em que só o transformador
ocupa 39 GiB).

## Quanto esperar

O modelo é destilado e os passos são apenas oito, e por isso um clipe de 1280×704 com 121
quadros (5 segundos a 24 quadros por segundo) é calculado **mais rápido** que no Wan 2.2 com
vinte passos — de poucos a dezenas de minutos. O tempo limite da tarefa no perfil está definido
em 240 minutos (`params.timeoutMinutes`).

## Parâmetros de geração

| Parâmetro | Padrão | Significado |
|---|---|---|
| `width` / `height` | 1280 × 704 | resolução do quadro; os dois números são múltiplos de 32 — é o que o nó do latente exige |
| `length` | 121 | quadros no clipe; 24 quadros por segundo, ou seja, 5 segundos |
| `steps` | 8 | **informativo**: o número de passos é definido pelas sigmas do modelo, veja abaixo |
| `negative` | vazio | prompt negativo |
| `timeoutMinutes` | 240 | quanto esperar pelo resultado |

**Sobre os passos.** No modelo destilado o cronograma de ruído é definido por uma lista de
números direto no modelo de requisição (nó `ManualSigmas`), e não por um número de passos: nove
valores — oito passos. Editar `steps` no perfil não influencia a geração; o valor está lá para o
resumo da tarefa. Para mudar o cronograma, edite as `sigmas` no arquivo
`models/workflow_….json` do diretório da organização.

**O que falta no nosso modelo em relação ao oficial.** O modelo oficial do ComfyUI calcula em
duas passagens: a primeira em tamanho reduzido pela metade, e a segunda eleva a resolução pelo nó
`LTXVLatentUpsampler`. O conector do AI2P não sabe fazer aritmética sobre `width`/`height`, e por
isso não haveria de onde tirar a «metade», e o resumo da tarefa informaria à pessoa um tamanho
errado — aqui há uma única passagem, já na resolução alvo. A melhoria do prompt por um modelo de
linguagem à parte (`TextGenerateLTX2Prompt`) também está desligada: ela puxa mais um arquivo de
pesos e reescreve o texto da tarefa em silêncio.

## Treinamento de LoRA

O adaptador é **aplicado** normalmente: o nó `LoraLoaderModelOnly` é inserido no grafo em tempo
de execução, e os arquivos são buscados em `<repositório de modelos>/loras`. O LTX tem um
ecossistema de LoRA pronto e rico — adaptadores de movimento de câmera, de estilo e de controle
são publicados pela própria Lightricks.

Já **treinar um adaptador a partir do AI2P não é possível**, e isso está anotado no registro com
honestidade: o [musubi-tuner](https://github.com/kohya-ss/musubi-tuner), com que o AI2P treina
LoRA para os demais modelos locais, não tem treinador de LTX de forma alguma — há Wan,
HunyuanVideo, Kandinsky, Qwen-Image, Z-Image, mas não LTX. Por isso, no perfil, o treinamento
está com o modo «externo»: o botão «treinar» responde com uma recusa clara e um link, em vez de
ficar meia hora pendurado e cair.

Um adaptador pode ser treinado à parte, com o treinador oficial da Lightricks
(<https://github.com/Lightricks/LTX-Video-Trainer>), e o arquivo `.safetensors` pronto pode ser
colocado em `<repositório de modelos>/loras` — de lá o AI2P o pega.

## Erros frequentes

* **A instalação responde com recusa no download** — o repositório está fechado por acordo, e os
  arquivos precisam ser trazidos à mão (veja «Instalação»).
* **«Modelo não instalado» depois da colocação manual** — confira os nomes dos arquivos e se
  eles estão diretamente no diretório do grupo `LTX-2.5`, e não em pastas aninhadas; o tamanho
  deve coincidir com a tabela byte a byte.
* **Clipe sem som** — o som vem da mesma passagem; se ele não existir, verifique se no modelo de
  requisição permaneceram os nós `LTXVEmptyLatentAudio` e `LTXVAudioVAEDecode`.
* **Falta de VRAM** — reduza `width`/`height` ou `length`.

# FLUX.2-dev

> **Atenção: a licença não é livre.** Os pesos do FLUX.2 [dev] foram entregues sob a **FLUX
> Non-Commercial License v2.1** — eles só podem ser usados em trabalho não comercial e não
> produtivo. Para aplicação comercial é preciso uma licença à parte da Black Forest Labs.
> Os detalhes estão na seção «Licença» abaixo; a alternativa livre da mesma família é o
> `FLUX.2-klein-4B` (Apache 2.0).

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `flux2_dev`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** texto → imagem, habilidades `image-generate` 94, `image-photo` 93,
`image-concept` 92, `image-text` 90

O modelo mais graduado da família FLUX.2, da Black Forest Labs, 32 bilhões de parâmetros — **o
topo da qualidade entre os pesos abertos** e o registro mais pesado do catálogo: cerca de 54 GB
de download. Vale mantê-lo se você tiver uma placa potente e o trabalho for não comercial; em
todos os demais casos pegue o `FLUX.2-klein-4B` ou o `Qwen-Image-2512`.

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e recolhe o
`.png` pronto para os artefatos da tarefa.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → FLUX.2-dev → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **três arquivos de pesos** no repositório de modelos (`storage.modelsRepo`), grupo
  `FLUX.2-dev`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `flux2_dev_fp8mixed.safetensors` | ~33,0 GiB | `diffusion_models` |
| `mistral_3_small_flux2_fp8.safetensors` | ~16,8 GiB | `text_encoders` |
| `flux2-vae.safetensors` | ~320 MiB | `vae` |

**No total, cerca de 54 GB de download.** É baixado com retomada: uma instalação interrompida
continua do ponto em que parou. As duas compilações pesadas já estão em fp8 — as bf16 originais
pesam o dobro e não são necessárias em uma placa de consumo.

O codificador de texto do dev é próprio — **Mistral 3 Small**, e não Qwen3, como no klein: os
arquivos da família não são comuns entre os registros.

Enquanto os arquivos não estiverem no lugar, o modelo **não pode ficar ativo** — isso é
verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **FLUX Non-Commercial License v2.1** — NÃO LIVRE |
| Uso comercial | **proibido** sem contrato à parte com a Black Forest Labs |
| O que é permitido | pesquisa pessoal, experimentos, estudo, hobby — tudo aquilo pelo que você não recebe pagamento direto nem indireto |
| O que é obrigatório | manter o texto da licença e os avisos, não remover os filtros de conteúdo |
| Resultado da geração | pelas condições da licença **não é considerado obra derivada** do modelo, mas isso não anula a proibição de uso comercial dos próprios pesos |
| Texto da licença | <https://huggingface.co/black-forest-labs/FLUX.2-dev/blob/main/LICENSE.md> |
| Licença comercial | <https://bfl.ai/> |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

A licença foi conferida pelo texto do `LICENSE.md` do repositório em 27.08.2026. A formulação da
fonte original: os pesos, os parâmetros e o código de inferência são entregues «freely available
for your **non-commercial and non-production** use».

**A mesma licença vale para o FLUX.2 [klein] 9B** (tanto na versão base quanto nas compilações
fp8). Livre em toda a família é apenas o 4B: ele tem Apache 2.0.

Os arquivos são baixados do repacote aberto da Comfy-Org
(<https://huggingface.co/Comfy-Org/flux2-dev>) — os repositórios originais da Black Forest Labs
são distribuídos mediante aceite da licença e, sem um token do HuggingFace, respondem
`401 Unauthorized`. O repacote vem sob **exatamente a mesma** licença não livre: o fato de o
arquivo ser baixado sem token não remove as restrições.

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **a partir de 24 GB de VRAM**; em 16 GB roda com descarregamento para a memória RAM e bem mais devagar |
| Driver NVIDIA | **580 ou mais novo** — em driver antigo o torch cai sem mensagem |
| Espaço em disco | ~57 GB (pesos + pacote ComfyUI) |
| Memória RAM | **a partir de 48 GB** — no descarregamento de camadas o modelo se mantém nela por inteiro |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

Este é o registro mais exigente do catálogo nas três medidas ao mesmo tempo: memória de vídeo,
memória RAM e espaço em disco.

## Quanto esperar

Vinte passos em um quadro de 1024×1024 são **um a dois minutos** em uma placa de 24 GB e **muito
mais** se o modelo for descarregado para a memória RAM. O tempo limite da tarefa no perfil está
definido em 120 minutos (`params.timeoutMinutes`).

O andamento da geração é visível no **console da tarefa**, no cartão da tarefa.

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `width` / `height` | 1024 × 1024 | resolução do quadro |
| `steps` | 20 | passos de difusão |
| `negative` | vazio | **não funciona**: no dev não existe condição negativa de forma alguma (veja abaixo) |
| `timeoutMinutes` | 120 | quanto esperar pelo resultado |

**Sobre o prompt negativo.** O FLUX.2 [dev] é um modelo destilado com guidance controlada: no
grafo estão o `FluxGuidance` (força 4) e o `BasicGuider`, e não há ali condição negativa. O valor
`negative` pode ser escrito no perfil, mas não influenciará a imagem. Se o negativo for
necessário, pegue o `FLUX.2-klein-4B` — lá há pesos básicos e um `cfg` de verdade.

A descrição da tarefa vai inteira para o prompt. O FLUX.2 entende bem descrições longas e
corridas — escreva em frases, e não em uma enumeração de palavras-chave.

## Treinamento de LoRA

**A aplicação, sim; o treinamento a partir do AI2P, não.**

Um adaptador pronto se conecta como em todos os registros do ComfyUI: o nó
`LoraLoaderModelOnly` é inserido no grafo em tempo de execução, e o arquivo é buscado em
`<repositório de modelos>/loras`.

Treinar um adaptador direto do AI2P não é possível, embora o treinador exista: o
[musubi-tuner](https://github.com/kohya-ss/musubi-tuner) sabe lidar com o FLUX.2 [dev]
(`flux_2_train_network.py --model_version dev`), mas exige os pesos **originais** do repositório
fechado da Black Forest Labs — o `flux2-dev.safetensors` avulso e o Mistral 3 fatiado em partes
—, e não o repacote que o AI2P instala. Baixá-los só é possível com um token do HuggingFace e
depois do aceite da licença, e por isso o registro tem `lora.train.kind = external`: o botão de
treinamento responde com recusa imediata.

Além disso, o autor do treinador recomenda diretamente treinar os adaptadores não no dev, e sim
nos pesos básicos do klein — eles foram feitos para isso. Um adaptador treinado no
`FLUX.2-klein-4B` **não serve** para o dev: são modelos de tamanhos diferentes.

## Erros frequentes

* **Falta de VRAM** — reduza `width`/`height`; se não ajudar, este modelo não é para a sua
  placa: pegue o `FLUX.2-klein-4B`.
* **A geração leva dezenas de minutos** — o modelo está sendo descarregado para a memória RAM;
  verifique se ela é suficiente (veja «Requisitos de hardware»).
* **O prompt negativo não funciona** — é assim que o dev é feito (veja «Parâmetros de geração»).
* **`401 Unauthorized` ao baixar você mesmo à mão** — você está baixando do repositório da Black
  Forest Labs; no manifesto do AI2P está o repacote aberto da Comfy-Org.
* **O botão de treinamento de LoRA recusa** — é assim mesmo (veja acima).

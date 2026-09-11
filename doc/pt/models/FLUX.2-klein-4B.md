# FLUX.2-klein-4B

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `flux2_klein_4b`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** texto → imagem, habilidades `image-generate` 88, `image-photo` 87,
`image-concept` 86, `image-text` 80

O modelo mais novo da família **FLUX.2**, da Black Forest Labs: 4 bilhões de parâmetros e a
**licença livre Apache 2.0** — na família só ele a tem (o 9B e o [dev] têm licença não livre).
Desenha nitidamente melhor que o SDXL e o SD 3.5 e, em exigências de hardware, fica entre o
Z-Image-Turbo e o Qwen-Image.

Está criada a variante **básica** dos pesos (`flux-2-klein-base-4b`): vinte passos, `cfg 5` e
prompt negativo de verdade. Há ainda a destilada (quatro passos, `cfg 1`) — esse é outro arquivo
de pesos, e ele não está no manifesto.

O mesmo modelo sabe **corrigir uma imagem conforme indicação**; para isso o catálogo tem um
registro à parte, o `FLUX.2-klein-4B-Edit`, com o mesmo conjunto de pesos.

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e recolhe o
`.png` pronto para os artefatos da tarefa.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → FLUX.2-klein-4B → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **três arquivos de pesos** no repositório de modelos (`storage.modelsRepo`), grupo
  `FLUX.2-klein-4B`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `flux-2-klein-base-4b.safetensors` | ~7,2 GiB | `diffusion_models` |
| `qwen_3_4b.safetensors` | ~7,5 GiB | `text_encoders` |
| `flux2-vae.safetensors` | ~320 MiB | `vae` |

**No total, cerca de 16 GB de download.** É baixado com retomada: uma instalação interrompida
continua do ponto em que parou, e os arquivos já baixados não são baixados de novo.

O grupo é comum com o registro `FLUX.2-klein-4B-Edit` — instalando um, o segundo não precisará
ser baixado. O codificador de texto `qwen_3_4b.safetensors` é o mesmo arquivo do Z-Image, mas os
grupos no repositório de modelos não se cruzam, e cada um baixa a sua cópia.

Enquanto os arquivos não estiverem no lugar, o modelo **não pode ficar ativo** — isso é
verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **Apache 2.0** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/black-forest-labs/FLUX.2-klein-4B> (cartão do modelo) |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

A licença foi conferida pelo cartão do modelo em 27.08.2026 (`cardData.license: apache-2.0`).

**Não confunda com o resto da família.** A Apache 2.0 é só do 4B. O `FLUX.2-klein-9B` (inclusive
as compilações base e fp8) e o `FLUX.2-dev` têm a licença **FLUX Non-Commercial License v2.1**,
ou seja, o uso comercial é proibido sem um contrato à parte com a Black Forest Labs.

Os arquivos que o AI2P baixa são o repacote da Comfy-Org
(<https://huggingface.co/Comfy-Org/vae-text-encorder-for-flux-klein-4b>), sob a mesma Apache
2.0. Ele é necessário também porque os repositórios originais da Black Forest Labs são
distribuídos mediante aceite da licença: sem um token do HuggingFace, o download deles responde
`401 Unauthorized`.

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **a partir de 12 GB de VRAM** (16 GB é confortável) |
| Driver NVIDIA | **580 ou mais novo** — em driver antigo o torch cai sem mensagem |
| Espaço em disco | ~19 GB (pesos + pacote ComfyUI) |
| Memória RAM | a partir de 16 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

Em uma placa de 8 GB o modelo também inicia — o ComfyUI descarrega partes do modelo para a
memória RAM —, mas o quadro é calculado várias vezes mais devagar. Se a placa for fraca, pegue o
`Z-Image-Turbo` ou o `SDXL-1.0`.

## Quanto esperar

Vinte passos em um quadro de 1024×1024 são **dezenas de segundos** em uma placa moderna. O tempo
limite da tarefa no perfil está definido em 60 minutos (`params.timeoutMinutes`).

O andamento da geração é visível no **console da tarefa**, no cartão da tarefa: para lá é
despejada a saída própria do ComfyUI, junto com a barra de progresso dele.

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `width` / `height` | 1024 × 1024 | resolução do quadro |
| `steps` | 20 | passos de difusão |
| `negative` | vazio | prompt negativo — **funciona** (pesos básicos, `cfg 5`) |
| `timeoutMinutes` | 60 | quanto esperar pelo resultado |

No FLUX.2 o cronograma de passos depende do tamanho do quadro, e por isso o número de passos é
informado não ao amostrador, e sim ao nó `Flux2Scheduler` — no modelo isso já está feito.

A descrição da tarefa vai inteira para o prompt. Uma indicação do tipo «put the result into the
file X.png» é executada pelo conector: o arquivo é copiado para a pasta do projeto e a própria
linha é recortada do prompt. **As linhas de indicação são reconhecidas apenas em russo e em
inglês** — as palavras-reconhecedoras estão fixadas no código, e por isso escreva a indicação em
uma dessas duas línguas, ainda que o texto da cena esteja em português.

## Treinamento de LoRA

Suportado por completo: o adaptador é tanto **aplicado** (o nó `LoraLoaderModelOnly` é inserido
no grafo em tempo de execução) quanto **treinado** direto do AI2P — pelo editor de LoRA no cartão
do objeto do projeto.

Quem treina é o [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — os scripts
`flux_2_cache_latents.py`, `flux_2_cache_text_encoder_outputs.py` e
`flux_2_train_network.py` (`--network_module networks.lora_flux_2`,
`--model_version klein-base-4b`), uma placa de vídeo, atenção por `sdpa`.

O pacote `musubi-tuner` (código-fonte, ~30 MB) e o **Python 3.12** (~45 MB) são instalados junto
com o modelo, pelo botão «Instalar»; um Python 3.10–3.12 já presente no computador é usado como
está. O ambiente com `torch` (~3 GB) o próprio treinador cria na primeira execução do
treinamento.

**Diferença em relação ao Z-Image e ao Qwen-Image.** Lá a geração calcula sobre compilações fp8,
que o treinador não aceita, e o `train.cmd` baixa um par de pesos próprio. Aqui não há o que
baixar: no manifesto está o próprio checkpoint básico, e o treinamento acontece sobre os mesmos
arquivos que já estão instalados. O autor do treinador recomenda diretamente treinar justamente
sobre os pesos básicos do klein.

Um adaptador treinado neste registro serve também para o `FLUX.2-klein-4B-Edit`: o modelo por
trás dos dois é o mesmo.

O adaptador pronto vai para `<repositório de modelos>/loras/<código do objeto>.safetensors`.
Todas as chaves de treinamento, o `dataset.toml` e o `train.cmd` ficam no perfil do modelo
(`lora.train`) e são reescritos antes de cada execução — edite-os no perfil, e não nos arquivos.

## Erros frequentes

* **O processo some sem mensagem** — quase sempre é o driver NVIDIA antigo (veja acima).
* **«Modelo não instalado»** — os arquivos não terminaram de baixar; abra «Instalar» e a janela
  mostrará o volume restante.
* **Falta de VRAM** — reduza `width`/`height` ou pegue um modelo mais leve.
* **`401 Unauthorized` ao baixar você mesmo à mão** — você está baixando do repositório da Black
  Forest Labs; no manifesto do AI2P estão os repacotes abertos da Comfy-Org.
* **O ComfyUI está ocupado por um processo alheio** — o AI2P descarrega apenas o servidor que
  ele mesmo iniciou; um ComfyUI alheio já em execução na 8188 ele não toca.

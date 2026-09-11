# FLUX.2-klein-4B-Edit

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `flux2_klein_4b_edit`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** imagem + indicação → imagem, habilidades `image-edit` 86,
`image-inpaint` 83, `image-generate` 80, `image-text` 78

Exatamente o mesmo modelo do `FLUX.2-klein-4B`, mas em modo de **correção de imagem**: na
entrada vão a imagem de origem e a indicação em palavras («pinte a parede de azul», «tire os
fios», «troque o fundo por um entardecer»), e na saída vem o quadro corrigido. Os pesos são os
mesmos; o que difere é apenas o grafo de geração.

A licença é **Apache 2.0**, a única livre na família FLUX.2.

Funciona pelo **ComfyUI**: o AI2P envia a imagem de origem ao motor (`POST /upload/image`) e
coloca o nome dela no modelo de requisição, e recolhe o `.png` pronto para os artefatos da
tarefa.

## De onde vem a imagem de origem

O caminho do arquivo é indicado na descrição da tarefa, relativo à pasta do projeto, ou por uma
referência a um objeto do projeto (`@obj:OBJ-3`) — nesse caso são colocados os quadros de
referência dele. Sem imagem de origem uma tarefa deste modelo não inicia: ela é declarada
**obrigatória** (`refImage.required`).

O tamanho do resultado é tomado da própria imagem (nó `GetImageSize`), e por isso `width` e
`height` do perfil não participam neste modo. Antes da correção a imagem é reduzida a um
megapixel — é assim também no modelo oficial do ComfyUI.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → FLUX.2-klein-4B-Edit → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **três arquivos de pesos** no repositório de modelos (`storage.modelsRepo`), grupo
  `FLUX.2-klein-4B`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `flux-2-klein-base-4b.safetensors` | ~7,2 GiB | `diffusion_models` |
| `qwen_3_4b.safetensors` | ~7,5 GiB | `text_encoders` |
| `flux2-vae.safetensors` | ~320 MiB | `vae` |

**No total, cerca de 16 GB de download** — mas o **grupo é comum** com o registro
`FLUX.2-klein-4B`: se ele já estiver instalado, não será preciso baixar nada, e os dois
registros ficarão ativos de imediato.

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
Livre é só a versão 4B: o `FLUX.2-klein-9B` e o `FLUX.2-dev` têm a **FLUX Non-Commercial License
v2.1** (o uso comercial é proibido sem contrato com a Black Forest Labs).

Os arquivos são baixados do repacote aberto da Comfy-Org
(<https://huggingface.co/Comfy-Org/vae-text-encorder-for-flux-klein-4b>), sob a mesma Apache
2.0: os repositórios originais da Black Forest Labs, sem um token do HuggingFace, respondem
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

## Quanto esperar

Vinte passos em um quadro de cerca de um megapixel são **dezenas de segundos** em uma placa
moderna. A correção é um pouco mais demorada que o desenho do zero: a imagem de origem ainda
precisa ser codificada em latente. O tempo limite da tarefa no perfil está definido em 60
minutos (`params.timeoutMinutes`).

O andamento da geração é visível no **console da tarefa**, no cartão da tarefa.

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `steps` | 20 | passos de difusão |
| `negative` | vazio | prompt negativo — funciona (`cfg 5`) |
| `timeoutMinutes` | 60 | quanto esperar pelo resultado |
| `width` / `height` | 1024 × 1024 | **neste modo não funcionam**: o tamanho é tomado da imagem de origem |

A descrição da tarefa vai inteira para o prompt. Escreva a indicação, e não a descrição da cena
inteira: o modelo corrige aquilo que foi nomeado e procura não tocar no resto.

## Treinamento de LoRA

Suportado por completo: o adaptador é tanto **aplicado** (o nó `LoraLoaderModelOnly` é inserido
no grafo em tempo de execução) quanto **treinado** direto do AI2P — pelo editor de LoRA no cartão
do objeto do projeto.

Quem treina é o [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — os scripts
`flux_2_cache_latents.py`, `flux_2_cache_text_encoder_outputs.py` e
`flux_2_train_network.py` (`--network_module networks.lora_flux_2`,
`--model_version klein-base-4b`). Nada supérfluo é baixado: o treinador treina sobre o mesmo
checkpoint básico que está instalado para a geração.

O pacote `musubi-tuner` (código-fonte, ~30 MB) e o **Python 3.12** (~45 MB) são instalados junto
com o modelo, pelo botão «Instalar». O ambiente com `torch` (~3 GB) o próprio treinador cria na
primeira execução do treinamento.

O adaptador é comum aos dois registros: o treinado aqui funciona também no `FLUX.2-klein-4B`, e
vice-versa — o modelo por trás deles é um só.

O adaptador pronto vai para `<repositório de modelos>/loras/<código do objeto>.safetensors`.
Todas as chaves de treinamento, o `dataset.toml` e o `train.cmd` ficam no perfil do modelo
(`lora.train`) e são reescritos antes de cada execução — edite-os no perfil, e não nos arquivos.

## Erros frequentes

* **«O modelo não aceita imagem»** — a tarefa foi iniciada em um registro sem correção; para
  correção pegue justamente o `FLUX.2-klein-4B-Edit`.
* **O arquivo de origem não foi encontrado** — o caminho é contado **a partir da pasta do
  projeto**, e não da raiz do disco.
* **O modelo redesenhou tudo** — a indicação era uma descrição da cena; escreva o que
  exatamente mudar e acrescente «não mudar o resto».
* **Falta de VRAM** — reduza a imagem de origem: ela é comprimida a um megapixel, mas arquivos
  muito grandes ainda assim pesam mais.
* **O ComfyUI está ocupado por um processo alheio** — o AI2P descarrega apenas o servidor que
  ele mesmo iniciou.

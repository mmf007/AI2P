# Z-Image-Turbo

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `z_image_turbo`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** texto → imagem, habilidades `image-generate` 85, `image-photo` 84,
`image-concept` 82, `image-text` 78

O modelo de desenho de imagens da Tongyi-MAI (Alibaba), variante **Turbo**: 6 bilhões de
parâmetros e **oito passos** por quadro em vez dos vinte a cinquenta habituais. É o mais rápido
e o menos exigente dos modelos locais de imagem do catálogo — uma primeira escolha sensata se a
placa de vídeo for modesta.

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e recolhe o
`.png` pronto para os artefatos da tarefa.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → Z-Image-Turbo → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **três arquivos de pesos** no repositório de modelos (`storage.modelsRepo`), grupo `Z-Image`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `z_image_turbo_bf16.safetensors` | ~11,5 GiB | `diffusion_models` |
| `qwen_3_4b.safetensors` | ~7,5 GiB | `text_encoders` |
| `ae.safetensors` | ~320 MiB | `vae` |

**No total, cerca de 21 GB de download.** É baixado com retomada: uma instalação interrompida
continua do ponto em que parou, e os arquivos já baixados não são baixados de novo.

Enquanto os arquivos não estiverem no lugar, o modelo **não pode ficar ativo** — isso é
verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **Apache 2.0** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/Tongyi-MAI/Z-Image-Turbo> (cartão do modelo) |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

Os arquivos que o AI2P baixa são o repacote da Comfy-Org
(<https://huggingface.co/Comfy-Org/z_image_turbo>), que vem sob a mesma Apache 2.0. A licença foi
conferida pelo cartão do modelo em 27.08.2026; nos modelos abertos ela muda raramente, mas antes
de um lançamento comercial verifique o cartão mais uma vez.

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **a partir de 8 GB de VRAM** (16 GB é confortável) |
| Driver NVIDIA | **580 ou mais novo** — em driver antigo o torch cai sem mensagem |
| Espaço em disco | ~23 GB (pesos + pacote ComfyUI) |
| Memória RAM | a partir de 16 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

## Quanto esperar

Oito passos são **segundos a dezenas de segundos** por quadro de 1024×1024 em uma placa moderna,
e não horas, como nos modelos de vídeo. O tempo limite da tarefa no perfil está definido em 60
minutos (`params.timeoutMinutes`) — isso basta com folga mesmo em hardware lento.

O andamento da geração é visível no **console da tarefa**, no cartão da tarefa: para lá é
despejada a saída própria do ComfyUI, junto com a barra de progresso dele.

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `width` / `height` | 1024 × 1024 | resolução do quadro |
| `steps` | 8 | passos de difusão; no Turbo não faz sentido passar de oito |
| `negative` | vazio | prompt negativo — **no Turbo não funciona** (veja abaixo) |
| `timeoutMinutes` | 60 | quanto esperar pelo resultado |

**Sobre o prompt negativo.** O modo Turbo calcula sem classifier-free guidance (`cfg = 1`), e por
isso a condição negativa no modelo de requisição é feita pelo nó `ConditioningZeroOut` —
exatamente como no modelo oficial do ComfyUI. O valor `negative` pode ser escrito no perfil, mas
não influenciará a imagem; se o negativo for necessário, pegue o modelo comum (não Turbo).

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
`zimage_cache_latents.py`, `zimage_cache_text_encoder_outputs.py` e
`zimage_train_network.py` (`--network_module networks.lora_zimage`), uma placa de vídeo, atenção
por `sdpa`.

O pacote `musubi-tuner` (código-fonte, ~30 MB) e o **Python 3.12** (~45 MB) são instalados junto
com o modelo, pelo botão «Instalar»; um Python 3.10–3.12 já presente no computador é usado como
está. O ambiente com `torch` (~3 GB) o próprio treinador cria na primeira execução do
treinamento.

**Uma peculiaridade específica do Turbo.** O Turbo é feito de pesos destilados, e o próprio autor
do treinador não recomenda treinar LoRA sobre eles; por isso o `train.cmd`, no primeiro
treinamento, baixa uma vez os pesos **básicos** `z_image_bf16.safetensors` (~11,5 GB) para o
subdiretório `train` do grupo `Z-Image` e treina sobre eles. O codificador de texto e o VAE são
tomados dos já instalados — eles são os mesmos na versão básica e na turbo. O adaptador pronto
funciona com os pesos turbo.

O adaptador pronto vai para `<repositório de modelos>/loras/<código do objeto>.safetensors`.
Todas as chaves de treinamento, o `dataset.toml` e o `train.cmd` ficam no perfil do modelo
(`lora.train`) e são reescritos antes de cada execução — edite-os no perfil, e não nos arquivos.

## Erros frequentes

* **O processo some sem mensagem** — quase sempre é o driver NVIDIA antigo (veja acima).
* **«Modelo não instalado»** — os arquivos não terminaram de baixar; abra «Instalar» e a janela
  mostrará o volume restante.
* **O prompt negativo não funciona** — é assim mesmo no Turbo (veja «Parâmetros de geração»).
* **Falta de VRAM** — reduza `width`/`height`.
* **O ComfyUI está ocupado por um processo alheio** — o AI2P descarrega apenas o servidor que
  ele mesmo iniciou; um ComfyUI alheio já em execução na 8188 ele não toca.

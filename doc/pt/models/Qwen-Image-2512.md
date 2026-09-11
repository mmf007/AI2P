# Qwen-Image-2512

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `qwen_image_2512`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** texto → imagem, habilidades `image-text` 94, `image-photo` 91,
`image-generate` 90, `image-concept` 85

O modelo de desenho de imagens da Alibaba, edição **2512** (20 bilhões de parâmetros). O ponto
forte dele é o **texto dentro do quadro**: placas, legendas, capas, cartazes, interfaces — e
também em cirílico. É o melhor dos modelos abertos em inscrições e diagramação — é por ele que
vale optar se a imagem tiver de conter palavras.

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e recolhe o
`.png` pronto para os artefatos da tarefa.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → Qwen-Image-2512 → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **três arquivos de pesos** no repositório de modelos (`storage.modelsRepo`), grupo
  `Qwen-Image`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `qwen_image_2512_fp8_e4m3fn.safetensors` | ~19,0 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `qwen_image_vae.safetensors` | ~242 MiB | `vae` |

**No total, cerca de 30 GB de download.** É baixado com retomada: uma instalação interrompida
continua do ponto em que parou, e os arquivos já baixados não são baixados de novo.

O grupo `Qwen-Image` é comum com o modelo `Qwen-Image-Edit-2511`: o codificador de texto e o VAE
são os mesmos nos dois, e por isso o segundo modelo baixará apenas o arquivo de pesos dele
(~19 GiB), e não os trinta gigabytes de novo.

Enquanto os arquivos não estiverem no lugar, o modelo **não pode ficar ativo** — isso é
verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **Apache 2.0** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/Qwen/Qwen-Image> (cartão do modelo) |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

Os arquivos que o AI2P baixa são o repacote da Comfy-Org
(<https://huggingface.co/Comfy-Org/Qwen-Image_ComfyUI>), que vem sob a mesma Apache 2.0. A
licença foi conferida pelo cartão do modelo em 27.08.2026.

Cuidado com o nome parecido do vizinho: o **Qwen-Image-3.0 (agosto de 2026) é fechado** — não
tem pesos, licença nem relatório técnico de forma alguma, e não roda localmente. A linha aberta
termina nas edições 2512 (desenho) e 2511 (correção).

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **a partir de 16 GB de VRAM** (24 GB é confortável) |
| Driver NVIDIA | **580 ou mais novo** — em driver antigo o torch cai sem mensagem |
| Espaço em disco | ~32 GB (pesos + pacote ComfyUI) |
| Memória RAM | a partir de 32 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

O modelo no manifesto é **fp8**: ele é duas vezes mais leve que o bf16 original e foi calculado
justamente para uma placa de consumo. Para o treinamento de LoRA o fp8 não serve — veja abaixo.

## Quanto esperar

Um quadro de 1328×1328 em 20 passos leva **minutos** em uma placa da classe 4090, e até uma
dezena de minutos em 8–16 GB com descarregamento para a memória RAM. O tempo limite da tarefa no
perfil está definido em 90 minutos (`params.timeoutMinutes`).

O andamento da geração é visível no **console da tarefa**, no cartão da tarefa: para lá é
despejada a saída própria do ComfyUI, junto com a barra de progresso dele.

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `width` / `height` | 1328 × 1328 | resolução do quadro |
| `steps` | 20 | passos de difusão; menos — mais rápido e mais grosseiro |
| `negative` | vazio | prompt negativo (aqui ele **funciona**, `cfg = 4`) |
| `timeoutMinutes` | 90 | quanto esperar pelo resultado |

A descrição da tarefa vai inteira para o prompt. Uma indicação do tipo «put the result into the
file X.png» é executada pelo conector: o arquivo é copiado para a pasta do projeto e a própria
linha é recortada do prompt. **As linhas de indicação são reconhecidas apenas em russo e em
inglês** — as palavras-reconhecedoras estão fixadas no código, e por isso escreva a indicação em
uma dessas duas línguas, ainda que o texto da cena esteja em português.

O texto que deve aparecer no quadro escreva-o no prompt **entre aspas e literalmente** — o
modelo reproduz exatamente o que está entre aspas.

## Treinamento de LoRA

Suportado por completo: o adaptador é tanto **aplicado** (o nó `LoraLoaderModelOnly` é inserido
no grafo em tempo de execução) quanto **treinado** direto do AI2P — pelo editor de LoRA no cartão
do objeto do projeto.

Quem treina é o [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — os scripts
`qwen_image_cache_latents.py`, `qwen_image_cache_text_encoder_outputs.py` e
`qwen_image_train_network.py` (`--model_version original`,
`--network_module networks.lora_qwen_image`), uma placa de vídeo, atenção por `sdpa`.

O pacote `musubi-tuner` (código-fonte, ~30 MB) e o **Python 3.12** (~45 MB) são instalados junto
com o modelo, pelo botão «Instalar»; um Python 3.10–3.12 já presente no computador é usado como
está. O ambiente com `torch` (~3 GB) o próprio treinador cria na primeira execução do
treinamento.

**O treinador precisa de outros arquivos de pesos.** O musubi-tuner diz diretamente que as
compilações fp8 (justamente aquelas com que a geração calcula) não servem para treinamento, e
por isso o `train.cmd`, no primeiro treinamento, baixa uma vez o par **bf16** —
`qwen_image_2512_bf16.safetensors` (~38 GiB) e `qwen_2.5_vl_7b.safetensors` (~15,4 GiB) — para o
subdiretório `train` do grupo `Qwen-Image`. São cerca de **57 GB além da instalação**, mas em
compensação a geração em si continua funcionando com os arquivos fp8 leves. O VAE é tomado do já
instalado.

O adaptador pronto vai para `<repositório de modelos>/loras/<código do objeto>.safetensors`.
Todas as chaves de treinamento, o `dataset.toml` e o `train.cmd` ficam no perfil do modelo
(`lora.train`) e são reescritos antes de cada execução — edite-os no perfil, e não nos arquivos.

## Erros frequentes

* **O processo some sem mensagem** — quase sempre é o driver NVIDIA antigo (veja acima).
* **«Modelo não instalado»** — os arquivos não terminaram de baixar; abra «Instalar» e a janela
  mostrará o volume restante.
* **Falta de VRAM** — reduza `width`/`height`; 20B em 8 GB só roda com descarregamento para a
  memória RAM e bem mais devagar.
* **O treinamento cai no carregamento dos pesos** — verifique o espaço livre: o treinador precisa
  dos ~57 GB dele (veja «Treinamento de LoRA»).
* **O ComfyUI está ocupado por um processo alheio** — o AI2P descarrega apenas o servidor que
  ele mesmo iniciou; um ComfyUI alheio já em execução na 8188 ele não toca.

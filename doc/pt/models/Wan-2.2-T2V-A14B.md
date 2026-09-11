# Wan-2.2-T2V-A14B

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `wan2.2_t2v_a14b`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** texto → vídeo, habilidade `video-generate` 80

O modelo de vídeo da Alibaba, linha aberta **Wan 2.2** (as versões 2.5 e superiores são produtos
de API fechados, e os pesos delas não existem). Pela qualidade geral do clipe, este é o mais
forte dos modelos abertos do catálogo depois do LTX-2.5, e nitidamente mais forte que o
Kandinsky 5.0 Video Lite.

O modelo é **composto** (MoE): há nele dois experts de 14 bilhões de parâmetros. O «ruidoso»
desenha a primeira metade dos passos — a composição geral e o movimento —, e o «limpo» finaliza
a segunda — os detalhes e a nitidez. Por isso, no manifesto de instalação há dois arquivos de
pesos, e no grafo do ComfyUI há dois amostradores que passam o latente um ao outro.

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e recolhe o
`.mp4` pronto para os artefatos da tarefa.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → Wan-2.2-T2V-A14B → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **quatro arquivos de pesos** no repositório de modelos (`storage.modelsRepo`), grupo
  `Wan-2.2-T2V`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `wan2.2_t2v_high_noise_14B_fp8_scaled.safetensors` | ~13,3 GiB | `diffusion_models` |
| `wan2.2_t2v_low_noise_14B_fp8_scaled.safetensors` | ~13,3 GiB | `diffusion_models` |
| `umt5_xxl_fp8_e4m3fn_scaled.safetensors` | ~6,3 GiB | `text_encoders` |
| `wan_2.1_vae.safetensors` | ~242 MiB | `vae` |

**No total, cerca de 35,6 GB de download.** É baixado com retomada: uma instalação interrompida
continua do ponto em que parou, e os arquivos já baixados não são baixados de novo.

Enquanto os arquivos não estiverem no lugar, o modelo **não pode ficar ativo** — isso é
verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **Apache 2.0** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/Wan-AI/Wan2.2-T2V-A14B> (cartão do modelo) |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

Os arquivos que o AI2P baixa são o repacote da Comfy-Org
(<https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged>), que vem sob a mesma Apache 2.0.
A licença foi conferida pelo cartão do modelo em 27.08.2026; nos modelos abertos ela muda
raramente, mas antes de um lançamento comercial verifique o cartão mais uma vez.

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **a partir de 16 GB de VRAM** (24 GB é confortável) |
| Driver NVIDIA | **580 ou mais novo** — em driver antigo o torch cai sem mensagem |
| Espaço em disco | ~38 GB (pesos + pacote ComfyUI), mais ~68 GB se você for treinar LoRA |
| Memória RAM | a partir de 32 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

Os experts são carregados por vez, e por isso na memória entra simultaneamente apenas um dos
dois arquivos — em 16 GB de VRAM um clipe de 832×480 é calculado, ainda que não rápido.

## Quanto esperar

Um clipe de 832×480 com 81 quadros (5 segundos a 16 quadros por segundo) leva **dezenas de
minutos** em uma placa moderna: 20 passos pelos dois experts em cada quadro. O tempo limite da
tarefa no perfil está definido em 240 minutos (`params.timeoutMinutes`).

O andamento da geração é visível no **console da tarefa**, no cartão da tarefa: para lá é
despejada a saída própria do ComfyUI, junto com a barra de progresso dele.

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `width` / `height` | 832 × 480 | resolução do quadro, nativa do 14B |
| `length` | 81 | quadros no clipe; 16 quadros por segundo, ou seja, 5 segundos |
| `steps` | 20 | passos de difusão para os dois experts juntos |
| `negative` | vazio | prompt negativo |
| `timeoutMinutes` | 240 | quanto esperar pelo resultado |

**Sobre o número de passos.** A fronteira entre os experts (depois de qual passo o «ruidoso»
entrega o trabalho ao «limpo») está fixada no modelo de workflow com o número 10 — metade de
vinte. Se você mudar `steps`, corrija-a também: o `end_at_step` do primeiro amostrador e o
`start_at_step` do segundo, no arquivo `models/workflow_….json` do diretório da organização. O
conector não faz aritmética sobre parâmetros, e por isso a fronteira não é recalculada sozinha.

O modo turbo do modelo oficial do ComfyUI (4 passos em vez de 20) não foi trazido para cá: ele
exige arquivos Lightning-LoRA à parte, que não estão no manifesto de instalação.

A descrição da tarefa vai inteira para o prompt. Uma indicação do tipo «put the result into the
file X.mp4» é executada pelo conector: o arquivo é copiado para a pasta do projeto e a própria
linha é recortada do prompt. **As linhas de indicação são reconhecidas apenas em russo e em
inglês** — as palavras-reconhecedoras estão fixadas no código, e por isso escreva a indicação em
uma dessas duas línguas, ainda que o texto da cena esteja em português.

## Treinamento de LoRA

Suportado por completo: o adaptador é tanto **aplicado** (o nó `LoraLoaderModelOnly` é inserido
no grafo em tempo de execução) quanto **treinado** direto do AI2P — pelo editor de LoRA no cartão
do objeto do projeto.

Quem treina é o [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — os scripts
`wan_cache_latents.py`, `wan_cache_text_encoder_outputs.py` e `wan_train_network.py`
(`--task t2v-A14B`, `--network_module networks.lora_wan`), uma placa de vídeo, atenção por
`sdpa`. O adaptador é treinado de uma vez nos dois experts: o «limpo» é indicado pela chave
`--dit`, e o «ruidoso», por `--dit_high_noise`.

O pacote `musubi-tuner` (código-fonte, ~30 MB) e o **Python 3.12** (~45 MB) são instalados junto
com o modelo, pelo botão «Instalar»; um Python 3.10–3.12 já presente no computador é usado como
está. O ambiente com `torch` (~3 GB) o próprio treinador cria na primeira execução do
treinamento.

**O que é preciso saber de antemão.** A geração calcula sobre as compilações `fp8_scaled` — elas
são duas vezes mais leves, e é justamente elas que o botão «Instalar» instala. O treinador não
aceita essas compilações
([dito diretamente na documentação dele](https://github.com/kohya-ss/musubi-tuner/blob/main/docs/wan.md)),
e por isso, no primeiro treinamento, o `train.cmd` baixa uma vez o **par fp16 próprio** (~57 GB)
e o codificador de texto original `models_t5_umt5-xxl-enc-bf16.pth` (~11 GB) para o subdiretório
`train` do grupo `Wan-2.2-T2V`. No total, ~68 GB além da instalação — reserve espaço em disco.
Não se pode colocar esses arquivos no manifesto: é da composição dele que se calcula o indicador
«modelo instalado», e o modelo se apagaria para todos os que não treinam adaptadores.

O adaptador pronto vai para `<repositório de modelos>/loras/<código do objeto>.safetensors`.
Todas as chaves de treinamento, o `dataset.toml` e o `train.cmd` ficam no perfil do modelo
(`lora.train`) e são reescritos antes de cada execução — edite-os no perfil, e não nos arquivos.

## Erros frequentes

* **O processo some sem mensagem** — quase sempre é o driver NVIDIA antigo (veja acima).
* **«Modelo não instalado»** — os arquivos não terminaram de baixar; abra «Instalar» e a janela
  mostrará o volume restante.
* **Falta de VRAM** — reduza `width`/`height` ou `length`; 81 quadros em 720p em um modelo 14B já
  exigem 24 GB.
* **O clipe saiu turvo ou «trepidado»** — verifique se a fronteira dos experts não foi
  desalinhada depois de uma edição de `steps` (veja «Parâmetros de geração»).
* **O ComfyUI está ocupado por um processo alheio** — o AI2P descarrega apenas o servidor que
  ele mesmo iniciou; um ComfyUI alheio já em execução na 8188 ele não toca.

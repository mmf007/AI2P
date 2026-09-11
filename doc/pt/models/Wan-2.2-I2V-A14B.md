# Wan-2.2-I2V-A14B

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `wan2.2_i2v_a14b`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** imagem + texto → vídeo, habilidades `video-animate` 80, `video-generate` 74

A mesma linha aberta da Alibaba, **Wan 2.2**, do Wan-2.2-T2V-A14B, mas o clipe é desenhado **a
partir de uma imagem**: o quadro inicial define o personagem, o enquadramento e o estilo, e o
prompt descreve apenas o movimento. Esta é a forma mais previsível de conseguir um clipe com o
herói desejado — a aparência vem do quadro, e não é recontada em palavras.

O modelo é composto (MoE), com dois experts de 14 bilhões de parâmetros: o «ruidoso» desenha a
primeira metade dos passos, e o «limpo» finaliza a segunda.

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o quadro inicial, manda o
workflow e recolhe o `.mp4` pronto para os artefatos da tarefa.

## De onde vem o quadro inicial

A tarefa é obrigada a nomeá-lo — sem imagem o modelo não inicia de forma alguma (a recusa vem na
hora, e não depois de meia hora de cálculo). Há duas maneiras:

* **referência a um objeto do projeto** — `@obj:OBJ-3` na descrição da tarefa: o caminho do
  quadro de referência é colocado sozinho, e o passaporte do objeto vai para o prompt;
* **caminho do arquivo** relativo à pasta do projeto, em uma linha própria da descrição.

São aceitos `.png`, `.jpg`, `.webp`; o quadro é redimensionado para o `width`×`height` do perfil.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → Wan-2.2-I2V-A14B → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **quatro arquivos de pesos** no repositório de modelos (`storage.modelsRepo`), grupo
  `Wan-2.2-I2V`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `wan2.2_i2v_high_noise_14B_fp8_scaled.safetensors` | ~13,3 GiB | `diffusion_models` |
| `wan2.2_i2v_low_noise_14B_fp8_scaled.safetensors` | ~13,3 GiB | `diffusion_models` |
| `umt5_xxl_fp8_e4m3fn_scaled.safetensors` | ~6,3 GiB | `text_encoders` |
| `wan_2.1_vae.safetensors` | ~242 MiB | `vae` |

**No total, cerca de 35,6 GB de download.** O grupo é próprio: os arquivos de pesos do «texto →
vídeo» e do «imagem → vídeo» são diferentes, enquanto o codificador e o VAE são os mesmos — e por
isso, se os dois registros estiverem instalados, esses dois arquivos ficam duas vezes no disco.

Enquanto os arquivos não estiverem no lugar, o modelo **não pode ficar ativo** — isso é
verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **Apache 2.0** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/Wan-AI/Wan2.2-I2V-A14B> (cartão do modelo) |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

Os arquivos que o AI2P baixa são o repacote da Comfy-Org
(<https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged>), que vem sob a mesma Apache 2.0.
A licença foi conferida pelo cartão do modelo em 27.08.2026.

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

## Quanto esperar

Um clipe de 832×480 com 81 quadros (5 segundos a 16 quadros por segundo) leva **dezenas de
minutos** em uma placa moderna. O tempo limite da tarefa no perfil está definido em 240 minutos
(`params.timeoutMinutes`). O andamento da geração é visível no **console da tarefa**, no cartão
da tarefa.

## Parâmetros de geração

| Parâmetro | Padrão | Significado |
|---|---|---|
| `width` / `height` | 832 × 480 | resolução do quadro; é a ela que o quadro inicial é ajustado |
| `length` | 81 | quadros no clipe; 16 quadros por segundo, ou seja, 5 segundos |
| `steps` | 20 | passos de difusão para os dois experts juntos |
| `negative` | vazio | prompt negativo |
| `timeoutMinutes` | 240 | quanto esperar pelo resultado |

**Sobre o número de passos.** A fronteira entre os experts está fixada no modelo de workflow com
o número 10 — metade de vinte. Ao mudar `steps`, corrija o `end_at_step` do primeiro amostrador e
o `start_at_step` do segundo no arquivo `models/workflow_….json` do diretório da organização: o
conector não faz aritmética sobre parâmetros.

## Treinamento de LoRA

Suportado por completo: o adaptador é tanto **aplicado** (o nó `LoraLoaderModelOnly` é inserido
no grafo em tempo de execução) quanto **treinado** direto do AI2P — pelo editor de LoRA no cartão
do objeto do projeto.

Quem treina é o [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — os scripts
`wan_cache_latents.py` (com a chave `--i2v`), `wan_cache_text_encoder_outputs.py` e
`wan_train_network.py` (`--task i2v-A14B`, `--network_module networks.lora_wan`). O adaptador é
treinado de uma vez nos dois experts: `--dit` e `--dit_high_noise`.

O pacote `musubi-tuner` e o **Python 3.12** são instalados junto com o modelo; o ambiente com
`torch` (~3 GB) o próprio treinador cria na primeira execução do treinamento.

**O que é preciso saber de antemão.** A geração calcula sobre as compilações `fp8_scaled`, e o
treinador não as aceita
([dito diretamente na documentação dele](https://github.com/kohya-ss/musubi-tuner/blob/main/docs/wan.md)),
e por isso, no primeiro treinamento, o `train.cmd` baixa uma vez o par **fp16** próprio (~57 GB)
e o codificador de texto original `models_t5_umt5-xxl-enc-bf16.pth` (~11 GB) para o subdiretório
`train` do grupo `Wan-2.2-I2V`. No total, ~68 GB além da instalação.

O adaptador pronto vai para `<repositório de modelos>/loras/<código do objeto>.safetensors`.
Todas as chaves de treinamento, o `dataset.toml` e o `train.cmd` ficam no perfil do modelo
(`lora.train`) e são reescritos antes de cada execução — edite-os no perfil, e não nos arquivos.

## Erros frequentes

* **«A tarefa exige um quadro inicial»** — na descrição da tarefa não há nem referência `@obj:`
  nem caminho de imagem; para desenhar do zero pegue o registro Wan-2.2-T2V-A14B.
* **O primeiro quadro «flutua»** — a imagem inicial tem proporções muito diferentes de
  `width`×`height`; ajuste-a de antemão ou corrija os tamanhos no perfil.
* **Falta de VRAM** — reduza `width`/`height` ou `length`.
* **O processo some sem mensagem** — quase sempre é o driver NVIDIA antigo.

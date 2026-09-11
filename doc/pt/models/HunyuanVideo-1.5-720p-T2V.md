# HunyuanVideo-1.5-720p-T2V

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `hunyuanvideo15_720p_t2v`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** texto → vídeo, habilidade `video-generate` 79

O modelo de vídeo da Tencent, versão **1.5**. Das três grandes linhas abertas do catálogo (Wan
2.2, HunyuanVideo 1.5, LTX-2.5), esta é a menos exigente em memória de vídeo e a única que
**calcula em 720p de fábrica**, e não em 480p.

O segundo codificador de texto (`byt5_small_glyphxl`) responde pelas **inscrições no quadro** —
se o clipe precisar de texto legível, isso é uma diferença notável; por isso, no grafo está o
`DualCLIPLoader`, e não o carregador comum de um único codificador.

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e recolhe o
`.mp4` pronto para os artefatos da tarefa.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → HunyuanVideo-1.5-720p-T2V → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **quatro arquivos de pesos** no repositório de modelos (`storage.modelsRepo`), grupo
  `HunyuanVideo-1.5`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `hunyuanvideo1.5_720p_t2v_fp16.safetensors` | ~15,5 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `byt5_small_glyphxl_fp16.safetensors` | ~418 MiB | `text_encoders` |
| `hunyuanvideo15_vae_fp16.safetensors` | ~2,3 GiB | `vae` |

**No total, cerca de 28,6 GB de download.** É baixado com retomada: uma instalação interrompida
continua do ponto em que parou.

Enquanto os arquivos não estiverem no lugar, o modelo **não pode ficar ativo** — isso é
verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **Tencent Hunyuan Community License** (não é Apache nem MIT) |
| Uso comercial | permitido, mas com ressalvas do titular dos direitos |
| O que é obrigatório | aceitar as condições da licença e manter o aviso; a licença tem restrições quanto ao número de usuários do produto e quanto a territórios |
| Texto da licença | <https://github.com/Tencent-Hunyuan/HunyuanVideo-1.5/blob/master/LICENSE> |
| Cartão do modelo | <https://huggingface.co/tencent/HunyuanVideo-1.5> |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

Esta **não** é uma licença livre no sentido de Apache/MIT: antes de um lançamento comercial leia
o texto dela por inteiro, pois há ali condições que não existem no Wan 2.2 e no Kandinsky. A
licença foi conferida pelo cartão do modelo em 27.08.2026
(`license_name: tencent-hunyuan-community`).

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **a partir de 12 GB de VRAM** (16 GB é confortável) |
| Driver NVIDIA | **580 ou mais novo** — em driver antigo o torch cai sem mensagem |
| Espaço em disco | ~31 GB (pesos + pacote ComfyUI), mais ~50 GB se você for treinar LoRA |
| Memória RAM | a partir de 32 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

Se a memória de vídeo não bastar, no perfil é possível trocar o `weight_dtype` do nó de
carregamento para `fp8_e4m3fn` — é o que o próprio modelo oficial do ComfyUI recomenda.

## Quanto esperar

Um clipe de 1280×720 com 121 quadros (5 segundos a 24 quadros por segundo) leva **dezenas de
minutos** em uma placa moderna (20 passos). O tempo limite da tarefa no perfil está definido em
240 minutos (`params.timeoutMinutes`). O andamento da geração é visível no **console da tarefa**,
no cartão da tarefa.

## Parâmetros de geração

| Parâmetro | Padrão | Significado |
|---|---|---|
| `width` / `height` | 1280 × 720 | resolução do quadro |
| `length` | 121 | quadros no clipe; 24 quadros por segundo, ou seja, 5 segundos |
| `steps` | 20 | passos de difusão |
| `negative` | vazio | prompt negativo (a força de aderência ao texto no modelo é 6) |
| `timeoutMinutes` | 240 | quanto esperar pelo resultado |

No modelo oficial do ComfyUI, ao lado da geração básica, há ainda a ampliação para 1080p e o
acelerador EasyCache — lá eles estão **desligados** e não foram trazidos para o nosso modelo:
são arquivos de pesos à parte, que não estão no manifesto.

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
`hv_1_5_cache_latents.py`, `hv_1_5_cache_text_encoder_outputs.py` e
`hv_1_5_train_network.py` (`--task t2v`, `--network_module networks.lora_hv_1_5`), uma placa de
vídeo, atenção por `sdpa`.

O pacote `musubi-tuner` (código-fonte, ~30 MB) e o **Python 3.12** (~45 MB) são instalados junto
com o modelo; o ambiente com `torch` (~3 GB) o próprio treinador cria na primeira execução do
treinamento.

**O que é preciso saber de antemão.** A geração calcula sobre o repacote fp16 e sobre o
codificador de texto aliviado fp8 — e nenhum dos dois serve ao treinador
([a documentação dele](https://github.com/kohya-ss/musubi-tuner/blob/main/docs/hunyuan_video_1_5.md)
pede diretamente o DiT original e o codificador completo). Por isso, no primeiro treinamento, o
`train.cmd` baixa uma vez o `diffusion_pytorch_model.safetensors` original (~31 GB) e o
`qwen_2.5_vl_7b.safetensors` completo (~15,5 GB) para o subdiretório `train` do grupo
`HunyuanVideo-1.5`. No total, ~50 GB além da instalação — reserve espaço em disco. O VAE e o
codificador de inscrições (`byt5`) são tomados dos já instalados.

O adaptador pronto vai para `<repositório de modelos>/loras/<código do objeto>.safetensors`.
Todas as chaves de treinamento, o `dataset.toml` e o `train.cmd` ficam no perfil do modelo
(`lora.train`) e são reescritos antes de cada execução — edite-os no perfil, e não nos arquivos.

## Erros frequentes

* **Falta de VRAM** — troque o `weight_dtype` para `fp8_e4m3fn` no modelo de workflow ou reduza
  `width`/`height`.
* **«Modelo não instalado»** — os arquivos não terminaram de baixar; abra «Instalar» e a janela
  mostrará o volume restante.
* **O processo some sem mensagem** — quase sempre é o driver NVIDIA antigo.
* **O ComfyUI está ocupado por um processo alheio** — o AI2P descarrega apenas o servidor que
  ele mesmo iniciou; um ComfyUI alheio já em execução na 8188 ele não toca.

# Kandinsky-5.0-T2V-Lite-distil16-5s

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `kandinsky5lite_t2v_distilled16steps_5s`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** texto → vídeo, habilidade `video-generate` 70

Uma variante do modelo de vídeo russo aberto **Kandinsky 5.0 Video Lite** (2 bilhões de
parâmetros), do Kandinsky Lab. Ele entende prompts em russo e desenha **cirílico dentro do
quadro** — algo que nenhum outro modelo do catálogo sabe fazer.

A variante **distil16**: um destilado do modelo original, ao qual bastam **16 passos** em vez de
cinquenta — seis vezes mais rápido. É a forma mais rápida de ver um clipe de rascunho; pela
velocidade se paga com detalhamento.

A duração do clipe é de **5 segundos** (121 quadros a 24 quadros por segundo): a duração está
fixada nos próprios pesos, e por isso a linha tem arquivos separados para 5 e para 10 segundos.

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e recolhe o
`.mp4` pronto para os artefatos da tarefa.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → Kandinsky-5.0-T2V-Lite-distil16-5s → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **quatro arquivos de pesos** no repositório de modelos (`storage.modelsRepo`), grupo
  `Kandinsky-5`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `kandinsky5lite_t2v_distilled16steps_5s.safetensors` | ~4,3 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `clip_l.safetensors` | ~235 MiB | `text_encoders` |
| `hunyuan_video_vae_bf16.safetensors` | ~470 MiB | `vae` |

**No total, cerca de 14,6 GB de download** — o mais leve dos modelos de vídeo do catálogo. O
grupo `Kandinsky-5` é comum a toda a linha: se ao lado estiver instalada outra variante dela,
será baixado apenas o arquivo de pesos próprio (~4,6 GB), pois os codificadores e o VAE já estão
no lugar.

Enquanto os arquivos não estiverem no lugar, o modelo **não pode ficar ativo** — isso é
verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **MIT** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/kandinskylab/Kandinsky-5.0-T2V-Lite-distilled16steps-5s> (cartão do modelo) |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

A MIT é a mais livre das licenças dos modelos de vídeo do catálogo: no Wan 2.2 é a Apache 2.0, e
no HunyuanVideo 1.5 e no LTX-2.5 são acordos próprios dos titulares dos direitos. A licença foi
conferida pelo cartão do modelo em 27.08.2026.

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **a partir de 12 GB de VRAM** (24 GB é confortável) |
| Driver NVIDIA | **580 ou mais novo** — em driver antigo o torch cai sem mensagem |
| Espaço em disco | ~17 GB (pesos + pacote ComfyUI) |
| Memória RAM | a partir de 16 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

## Quanto esperar

Dezesseis passos em vez de cinquenta: o clipe é calculado **várias vezes mais rápido** que nas
outras variantes da linha — poucos minutos em uma placa moderna, contra dezenas.

O tempo limite da tarefa no perfil está definido em 180 minutos (`params.timeoutMinutes`). O
andamento da geração é visível no **console da tarefa**, no cartão da tarefa: para lá é despejada
a saída própria do ComfyUI, junto com a barra de progresso dele.

## Parâmetros de geração

| Parâmetro | Padrão | Significado |
|---|---|---|
| `width` / `height` | 768 × 512 | resolução do quadro |
| `length` | 121 | quadros no clipe; 24 quadros por segundo, ou seja, 5 s |
| `steps` | 16 | passos de difusão — os mesmos da tarefa de treinamento desta variante |
| `negative` | vazio | prompt negativo — **nesta variante não funciona**: ela foi treinada para calcular sem a segunda passagem |
| `timeoutMinutes` | 180 | quanto esperar pelo resultado |

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
`kandinsky5_cache_latents.py`, `kandinsky5_cache_text_encoder_outputs.py` e
`kandinsky5_train_network.py` (`--task k5-lite-t2v-5s-distil-sd`,
`--network_module networks.lora_kandinsky`), uma placa de vídeo, atenção por `sdpa`.
A tarefa do treinador é própria de cada variante — ela define tanto o número de passos quanto o
cronograma, e por isso não se pode colocar a de outra.

O pacote `musubi-tuner` (código-fonte, ~30 MB) e o **Python 3.12** (~45 MB) são instalados junto
com o modelo; o ambiente com `torch` (~3 GB) o próprio treinador cria na primeira execução do
treinamento. Ao contrário do Wan 2.2 e do HunyuanVideo 1.5, não é preciso baixar pesos
adicionais: o arquivo do DiT já não é fp8, e os codificadores de texto próprios o treinador pega
sozinho no HuggingFace.

O adaptador pronto vai para `<repositório de modelos>/loras/<código do objeto>.safetensors`.
Todas as chaves de treinamento, o `dataset.toml` e o `train.cmd` ficam no perfil do modelo
(`lora.train`) e são reescritos antes de cada execução — edite-os no perfil, e não nos arquivos.

## Erros frequentes

* **O processo some sem mensagem** — quase sempre é o driver NVIDIA antigo (veja acima).
* **«Modelo não instalado»** — os arquivos não terminaram de baixar; abra «Instalar» e a janela
  mostrará o volume restante.
* **Falta de VRAM** — reduza `width`/`height`; um clipe de 121 quadros mantém o latente inteiro
  na memória.
* **O ComfyUI está ocupado por um processo alheio** — o AI2P descarrega apenas o servidor que
  ele mesmo iniciou; um ComfyUI alheio já em execução na 8188 ele não toca.

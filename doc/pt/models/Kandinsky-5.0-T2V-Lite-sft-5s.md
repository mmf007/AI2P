# Kandinsky-5.0-T2V-Lite-sft-5s

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`,
modelo `kandinsky5lite_t2v_sft_5s`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** texto → vídeo (5 segundos), habilidades `video-generate` 76, `video-animate` 72

O modelo de geração de vídeo da família Kandinsky 5.0 (variante Lite, com ajuste fino, 5
segundos). Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e
recolhe o `.mp4` pronto para os artefatos da tarefa.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos de IA → Kandinsky-5.0-T2V-Lite-sft-5s → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **quatro arquivos de pesos** no repositório de modelos (`storage.modelsRepo`, por padrão
  `C:\ai` no Windows e `~/ai` no Linux/macOS):

| Arquivo | Tamanho | Onde |
|---|---|---|
| `Kandinsky-5.0-T2V-Lite-sft-5s.safetensors` | ~4,3 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `clip_l.safetensors` | ~235 MiB | `text_encoders` |
| `hunyuan_video_vae_bf16.safetensors` | ~470 MiB | `vae` |

**No total, cerca de 16 GB de download.** É baixado com retomada: uma instalação interrompida
continua do ponto em que parou, e os arquivos já baixados não são baixados de novo.

Enquanto os arquivos não estiverem no lugar, o modelo **não pode ficar ativo** — isso é
verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **MIT** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/kandinskylab/Kandinsky-5.0-T2V-Lite-sft-5s> (cartão do modelo) |
| Pagamento pela geração | não há — quem calcula é o seu computador |

A licença foi extraída do cartão do modelo em 27.08.2026 por uma requisição ao HuggingFace
(campo `cardData.license`), e não de memória. A MIT é a mais livre das licenças do catálogo: o
uso comercial é permitido, e a única exigência é manter o texto da licença e o aviso de direitos
autorais.

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **no mínimo 6 GB de VRAM** |
| Driver NVIDIA | **580 ou mais novo** — veja o aviso abaixo |
| Espaço em disco | ~18 GB (pesos + pacote ComfyUI) |
| Memória RAM | a partir de 16 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

**Sobre o driver — importante.** No pacote ComfyUI vem o torch compilado para CUDA 13.0. Em um
driver antigo (por exemplo 527.99 = CUDA 12.0) ele cai não com um erro claro, e sim com
**access violation** — o processo simplesmente desaparece. Isso não é um defeito do AI2P:
atualize o driver NVIDIA para 580+ (verificado no 610.x). Se o modelo «silenciosamente não
inicia», comece pelo `nvidia-smi` e pela versão do driver.

## Quanto esperar

Em 6 GB de VRAM, a geração de um clipe de **768×512, 121 quadros, 50 passos leva cerca de uma
hora** (verificado ao vivo: 58 minutos e 49 segundos). Isso é normal — o tempo limite da tarefa
no perfil está definido em 180 minutos (`params.timeoutMinutes`).

Desde a versão 1.63 o mesmo pode ser definido **no executor**: o campo «Tempo limite de
resposta, min» no formulário do executor de IA. Preenchido, ele tem precedência sobre o
`timeoutMinutes` do perfil, e **0 significa esperar sem limite** (a geração corre o tempo que
for preciso; interrompê-la é possível pelo botão «parar»). Vazio — como antes, vale o valor do
perfil.

O andamento da geração é visível no **console da tarefa**, no cartão da tarefa: para lá é
despejada a saída própria do ComfyUI, junto com a barra de progresso dele.

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `width` / `height` | 768 × 512 | resolução; maior — bem mais demorado e mais VRAM |
| `length` | 121 | quadros (≈5 segundos) |
| `steps` | 50 | passos de difusão; menos — mais rápido e mais grosseiro |
| `negative` | vazio | prompt negativo |
| `timeoutMinutes` | 180 | quanto esperar pelo resultado |

A descrição da tarefa vai inteira para o prompt. Uma indicação do tipo «put the result into the
file X.mp4» é executada pelo conector: o arquivo é copiado para a pasta do projeto e a própria
linha é recortada do prompt. **As linhas de indicação são reconhecidas apenas em russo e em
inglês** — as palavras-reconhecedoras estão fixadas no código, e por isso escreva a indicação em
uma dessas duas línguas, ainda que o texto da cena esteja em português.

## Treinamento de LoRA

É organizado do mesmo jeito que no `Kandinsky-5.0-I2V-Lite-5s`, e difere em duas linhas de
configuração: a tarefa do treinador aqui é `k5-lite-t2v-5s-sd`, e os pesos são
`Kandinsky-5.0-T2V-Lite-sft-5s.safetensors`.

Quem treina é o [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) — uma placa de vídeo,
atenção por `sdpa`, de 13 a 16 GB de VRAM. O
[kandinsky-5-lora-train](https://github.com/kandinskylab/kandinsky-5-lora-train) oficial exige
NCCL e várias placas de vídeo, e no Windows não inicia.

O pacote `musubi-tuner` (código-fonte, ~30 MB) e o **Python 3.12** (~45 MB) são instalados junto
com o modelo, pelo botão «Instalar»: um Python 3.10–3.12 já presente no computador é usado como
está e não é baixado de novo. O ambiente com `torch` (~3 GB) o próprio treinador cria na
primeira execução do treinamento.
O próprio treinador baixa os originais `Qwen/Qwen2.5-VL-7B-Instruct` (~16 GB) e
`openai/clip-vit-large-patch14` (~1,7 GB) — as compilações reduzidas que a geração usa não lhe
servem.

O adaptador pronto vai para `<repositório de modelos>/loras/<código do objeto>.safetensors`.
Todas as chaves de treinamento, o `dataset.toml` e o `train.cmd` ficam no perfil do modelo
(`lora.train`) e são reescritos antes de cada execução.

O conjunto de dados no AI2P são **imagens**, e por isso o que se treina assim é um adaptador de
aparência; um adaptador de movimento se treina em vídeo, e conjunto de dados de vídeo ainda não
existe no editor de LoRA.

## Erros frequentes

* **O processo some sem mensagem** — quase sempre é o driver NVIDIA antigo (veja acima).
* **«Modelo não instalado»** — os arquivos não terminaram de baixar; abra «Instalar» e a janela
  mostrará o volume restante.
* **Falta de VRAM** — reduza `width`/`height` ou `length`.
* **O ComfyUI está ocupado por um processo alheio** — o AI2P descarrega apenas o servidor que
  ele mesmo iniciou; um ComfyUI alheio já em execução na 8188 ele não toca.

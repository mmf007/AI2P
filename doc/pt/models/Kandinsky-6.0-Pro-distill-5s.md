# Kandinsky-6.0-Pro-distill-5s

**Hospedagem:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `Kandinsky-6.0-Pro-distill-5s`
**Chave de API:** não é necessária — o servidor é iniciado localmente sem autorização
**O que faz:** texto → vídeo (5 segundos, 864×480, 24 fps), habilidade `video-generate` 86

Kandinsky 6.0 Video é uma família de modelos abertos da Kandinsky Lab (Sber) lançada em setembro de 2026: Lite (3 bilhões de parâmetros) e Pro (29 bilhões). Este registro usa a versão **destilada** Pro: ela gera um clipe em 10 passos em vez de 50, quase sem perda de qualidade. O clipe é gerado apenas a partir da descrição da tarefa.

## Sem som

O próprio modelo gera vídeo junto com som (44 kHz, fala sincronizada com os lábios), mas o som é decodificado pelos pesos MMAudio `v1-44.pth`, publicados sob a licença não comercial CC-BY-NC-4.0. O AI2P não instala esses pesos, então o registro produz um **clipe sem faixa de áudio**. A parte de áudio do prompt (`<AUDCAP>…`) não tem efeito.

## Não precisa de chave, precisa de instalação

O Kandinsky 6 não está no núcleo do ComfyUI: seus nós vêm da extensão https://github.com/kandinskylab/kandinsky-6 (pasta `comfyui`, MIT). O botão «Instalar» no formulário do modelo faz tudo:

1. pacote `comfyui` — a versão portátil do ComfyUI (é necessária a **0.38 ou mais recente**; atualize uma anterior);
2. pacote `kandinsky6` — a extensão: arquivo do commit, dependências `pip` no Python embutido do ComfyUI e cópia dos nós em `ComfyUI/custom_nodes/kandinsky6`;
3. pesos no grupo `Kandinsky-6` do repositório de modelos:

| Arquivo | Origem | Tamanho |
|---|---|---|
| `Kandinsky-6.0-Pro-distill-5s.safetensors` | https://huggingface.co/kandinskylab/Kandinsky-6.0-Pro-distill-5s-Diffusers | 56.2 GiB |
| `qwen_2.5_vl_7b.safetensors` | https://huggingface.co/Comfy-Org/Qwen-Image_ComfyUI | 15.4 GiB |
| `clip_l.safetensors` | https://huggingface.co/comfyanonymous/flux_text_encoders | 0.2 GiB |
| `hunyuan_video_vae_bf16.safetensors` | https://huggingface.co/Kijai/HunyuanVideo_comfy | 0.5 GiB |

No total, cerca de **72.3 GiB**. Todos os repositórios são abertos (`gated=false`), não é preciso token do HuggingFace. O modelo só pode ser ativado depois da instalação.

## Licença

Os pesos do Kandinsky 6.0 são **MIT** (ficha https://huggingface.co/kandinskylab/Kandinsky-6.0-Pro-distill-5s-Diffusers, verificado em 14.10.2026), o uso comercial é permitido. A extensão do ComfyUI também é MIT. O codificador Qwen2.5-VL é Apache 2.0. O registro não usa os pesos de som MMAudio não comerciais (veja «Sem som»).

## Preço

Grátis: o modelo roda na sua placa de vídeo e ninguém precisa ser pago — apenas eletricidade e espaço em disco.

## Requisitos de hardware

GPU NVIDIA com CUDA; como referência, 24 GB de memória de vídeo e 96 GB de RAM ou mais (o ComfyUI descarrega parte dos pesos para a RAM, então com menos memória funciona, só que mais devagar). Um clipe leva cerca de 15–40 minutos numa placa da classe RTX 4090.

## Parâmetros de geração

Perfil: `width` 864, `height` 480, `length` 121 quadros (5 s a 24 fps; número de quadros da forma 4n+1), `steps` 10, CFG 1, amostrador euler/simple — como a Kandinsky Lab recomenda para as versões destiladas. `negative` é o prompt negativo do modelo oficial. Não há treinamento de LoRA para este modelo (`lora.supported: false`).

## Erros comuns

* **«Cannot execute because node Kandinsky6… does not exist»** — a extensão não foi carregada: reinstale o pacote `kandinsky6` e reinicie o ComfyUI; verifique se o ComfyUI é 0.38 ou mais recente.
* **Memória de vídeo insuficiente** — reduza `width`/`height` no perfil (múltiplos de 16) ou use Lite em vez de Pro.
* **Clipe sem som** — é intencional (veja «Sem som»).

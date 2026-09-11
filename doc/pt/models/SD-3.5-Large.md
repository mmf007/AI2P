# SD-3.5-Large

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `sd3_5_large`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** texto → imagem, habilidades `image-generate` 80, `image-photo` 79,
`image-concept` 78, `image-text` 70

Stable Diffusion 3.5 Large, da Stability AI, 8 bilhões de parâmetros. No catálogo ele ocupa o
**degrau intermediário**: nitidamente melhor que o SDXL, nitidamente mais fraco que o FLUX.2 e o
Qwen-Image, mas em compensação se instala com **um único arquivo** e funciona em uma placa de 12
GB.

Instala-se a compilação fp8 da Comfy-Org: **os codificadores de texto ficam dentro do próprio
arquivo**, e não é preciso baixar `clip_g`, `clip_l` e `t5xxl` à parte.

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e recolhe o
`.png` pronto para os artefatos da tarefa.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → SD-3.5-Large → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **um arquivo de pesos** no repositório de modelos (`storage.modelsRepo`), grupo `SD-3.5`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `sd3.5_large_fp8_scaled.safetensors` | ~13,9 GiB | `checkpoints` |

**No total, cerca de 15 GB de download.** É baixado com retomada: uma instalação interrompida
continua do ponto em que parou.

A categoria é `checkpoints`, e não `diffusion_models` — este é um checkpoint completo, do qual o
ComfyUI tira de uma vez o modelo, os codificadores de texto e o VAE.

Enquanto o arquivo não estiver no lugar, o modelo **não pode ficar ativo** — isso é verificado
também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **Stability AI Community License** (não livre) |
| Uso comercial | permitido **enquanto a receita anual for menor que 1 milhão de dólares**; acima disso é preciso a licença Enterprise da Stability AI |
| O que é obrigatório | anexar o texto da licença, manter o aviso «This Stability AI Model is licensed under the Stability AI Community License, Copyright © Stability AI Ltd.» e exibir «Powered by Stability AI» no site ou na descrição do produto |
| Texto da licença | <https://huggingface.co/stabilityai/stable-diffusion-3.5-large/blob/main/LICENSE.md> |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

A licença foi conferida pelo texto do `LICENSE.md` do repositório em 27.08.2026 (redação de 5 de
julho de 2024). Esta **não** é uma licença livre: o uso em pesquisa e não comercial é sempre
gratuito, e o comercial só vale com receita abaixo do limiar indicado. Se o limiar estiver perto
de você, pegue o `FLUX.2-klein-4B` (Apache 2.0) ou o
`Kandinsky-5.0-Image-Lite` (MIT).

O arquivo que o AI2P baixa é o repacote da Comfy-Org
(<https://huggingface.co/Comfy-Org/stable-diffusion-3.5-fp8>), sob a mesma licença.

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **a partir de 10 GB de VRAM** (12 GB é confortável) |
| Driver NVIDIA | **580 ou mais novo** — em driver antigo o torch cai sem mensagem |
| Espaço em disco | ~18 GB (pesos + pacote ComfyUI) |
| Memória RAM | a partir de 16 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

## Quanto esperar

Vinte passos em um quadro de 1024×1024 são **dezenas de segundos** em uma placa moderna. O tempo
limite da tarefa no perfil está definido em 60 minutos (`params.timeoutMinutes`).

O andamento da geração é visível no **console da tarefa**, no cartão da tarefa.

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `width` / `height` | 1024 × 1024 | resolução do quadro |
| `steps` | 20 | passos de difusão |
| `negative` | vazio | prompt negativo — funciona (`cfg 4.01`) |
| `timeoutMinutes` | 60 | quanto esperar pelo resultado |

O valor `cfg 4.01` foi tomado do modelo oficial do ComfyUI como está.

A descrição da tarefa vai inteira para o prompt. Uma indicação do tipo «put the result into the
file X.png» é executada pelo conector: o arquivo é copiado para a pasta do projeto e a própria
linha é recortada do prompt. **As linhas de indicação são reconhecidas apenas em russo e em
inglês** — as palavras-reconhecedoras estão fixadas no código, e por isso escreva a indicação em
uma dessas duas línguas, ainda que o texto da cena esteja em português.

## Treinamento de LoRA

**A aplicação, sim; o treinamento a partir do AI2P, não.**

Um adaptador pronto se conecta como em todos os registros do ComfyUI: o nó
`LoraLoaderModelOnly` é inserido no grafo em tempo de execução, e o arquivo é buscado em
`<repositório de modelos>/loras`. Há muitos adaptadores para o SD 3.5 em acesso aberto, e todos
servem.

Treinar um adaptador direto do AI2P não há como: o `musubi-tuner`, com o qual se treinam os
demais modelos locais, não conhece a família Stable Diffusion de forma alguma — esse é o domínio
da ferramenta vizinha do mesmo autor, o [sd-scripts](https://github.com/kohya-ss/sd-scripts).
Por isso o registro tem `lora.train.kind = external`, e o botão de treinamento responde com
recusa imediata, e não depois de meia hora de cálculo. Um arquivo treinado por fora basta ser
colocado em `loras`.

## Erros frequentes

* **O texto no quadro não sai** — este é o ponto fraco do SD 3.5; para inscrições pegue o
  `Qwen-Image-2512`, e para cirílico, o `Kandinsky-5.0-Image-Lite`.
* **«Modelo não instalado»** — o arquivo não terminou de baixar; abra «Instalar» e a janela
  mostrará o volume restante.
* **O botão de treinamento de LoRA recusa** — é assim mesmo (veja acima).
* **Falta de VRAM** — reduza `width`/`height`.
* **O ComfyUI está ocupado por um processo alheio** — o AI2P descarrega apenas o servidor que
  ele mesmo iniciou.

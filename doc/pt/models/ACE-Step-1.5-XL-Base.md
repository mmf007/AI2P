# ACE-Step-1.5-XL-Base

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `acestep_v1_5_xl_base`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** texto → música e canções, habilidades `audio-song` 86, `audio-music` 87

O modelo de composição musical da equipe ACE-Step, variante **Base** — os pesos originais, sem
ajuste fino para o gosto: 50 passos de difusão e `cfg 6`. Na avaliação dos autores, esta
variante tem qualidade média e **diversidade alta**: ela vai de bom grado a soluções pouco
usuais. Pegue-a quando você precisar de variantes, e não de uma única faixa «certa».

Os três registros do ACE-Step 1.5 XL no catálogo diferem apenas nisto:

| Registro | Passos | `cfg` | Qualidade | Diversidade |
|---|---|---|---|---|
| ACE-Step-1.5-XL-Turbo | 8 | 1 (sem CFG) | comercial | média |
| **ACE-Step-1.5-XL-Base** | 50 | 6 | média | **alta** |
| ACE-Step-1.5-XL-SFT | 50 | 7 | **alta** | média |

Uma peculiaridade do ACE-Step 1.5, comum aos três: dentro do modelo funciona um **modelo de
linguagem planejador**. Ele mesmo decompõe a sua solicitação em estrutura da canção, letra,
andamento e tonalidade. Por isso não é preciso escrever a letra da canção em um campo à parte —
basta descrever a tarefa em palavras, e a letra o modelo compõe sozinho (ou usa a sua, se você a
escreveu). Ele entende mais de cinquenta idiomas, e o russo está entre eles.

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e recolhe o
`.mp3` pronto para os artefatos da tarefa.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → ACE-Step-1.5-XL-Base → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **quatro arquivos de pesos** no repositório de modelos (`storage.modelsRepo`), grupo
  `ACE-Step-1.5`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `acestep_v1.5_xl_base_bf16.safetensors` | ~9,3 GiB | `diffusion_models` |
| `qwen_4b_ace15.safetensors` | ~7,8 GiB | `text_encoders` |
| `qwen_0.6b_ace15.safetensors` | ~1,1 GiB | `text_encoders` |
| `ace_1.5_vae.safetensors` | ~322 MiB | `vae` |

**No total, cerca de 19,9 GB de download.** É baixado com retomada: uma instalação interrompida
continua do ponto em que parou, e os arquivos já baixados não são baixados de novo.

Os três registros do ACE-Step 1.5 XL compartilham **um mesmo grupo** e três dos quatro arquivos.
Se você já instalou o Turbo ou o SFT, este registro baixará apenas os pesos próprios — cerca de
9,3 GiB.

Enquanto os arquivos não estiverem no lugar, o modelo **não pode ficar ativo** — isso é
verificado também na partida do aplicativo.

## Licença

| | |
|---|---|
| Pesos do modelo | **MIT** |
| Uso comercial | permitido — os autores chamam isso de a característica principal do modelo |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/ACE-Step/Ace-Step1.5> (cartão do modelo) |
| Pagamento pela geração | não há — quem calcula é a sua placa de vídeo |

Sobre o uso comercial, no ACE-Step 1.5 está dito diretamente: o modelo foi treinado em gravações
licenciadas, livres de royalties e sintéticas justamente para que o resultado possa ser usado
com fins comerciais.

Os arquivos que o AI2P baixa são o repacote da Comfy-Org
(<https://huggingface.co/Comfy-Org/ace_step_1.5_ComfyUI_files>), publicado sob **Apache 2.0**.
As licenças foram conferidas pelos cartões dos modelos em 27.08.2026.

À parte: o próprio **ComfyUI é distribuído sob GPL-3.0**. O AI2P o executa como programa externa
e conversa com ele por HTTP, e por isso essa licença não passa ao seu produto — mas, se você
levar a alguém uma compilação com o ComfyUI dentro, as condições da GPL se aplicam a ela.

## Requisitos de hardware

| | |
|---|---|
| Placa de vídeo | NVIDIA, **a partir de 8 GB de VRAM** (os autores afirmam que funciona a partir de 4 GB) |
| Driver NVIDIA | **580 ou mais novo** — em driver antigo o torch cai sem mensagem |
| Espaço em disco | ~22 GB (pesos + pacote ComfyUI) |
| Memória RAM | a partir de 16 GB |
| SO | Windows (a compilação portátil do ComfyUI); no Linux o ComfyUI se instala à mão |

## Quanto esperar

Cinquenta passos contra oito do Turbo são cerca de **seis vezes mais tempo**, ou seja, minutos
por faixa em vez de dezenas de segundos, mais uma segunda passagem para a condição negativa (o
`cfg 6` liga a classifier-free guidance). O tempo limite da tarefa no perfil está definido em 60
minutos (`params.timeoutMinutes`).

O andamento da geração é visível no **console da tarefa**, no cartão da tarefa.

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `length` | 120 | **duração da faixa em SEGUNDOS** (não quadros, como no vídeo) |
| `steps` | 50 | passos de difusão |
| `width` / `height` | não definidos | o som não tem tamanho de quadro; no resumo da tarefa aparecem os padrões 768×512 — eles não têm relação alguma com o som |
| `negative` | vazio | prompt negativo (ao contrário do Turbo, aqui ele funciona) |
| `timeoutMinutes` | 60 | quanto esperar pelo resultado |

**Sobre a duração.** O campo `length` neste registro significa segundos e vai de uma vez para
dois lugares do grafo: o tamanho do latente vazio e o campo `duration` do planejador. No resumo
da tarefa ele aparece rotulado com a palavra «quadros» — é assim em todos os modelos de mídia;
leia-o como «segundos». O modelo é calculado para faixas de até cerca de dez minutos.

**Sobre o idioma do vocal.** No grafo, o campo `language` está definido como `unknown` — o
modelo determina o idioma pelo seu texto sozinho. Nos modelos oficiais do ComfyUI ali consta
`en`, o que, para letras em russo, daria pronúncia inglesa. Se você sempre canta em um mesmo
idioma, coloque o código dele (`ru`, `en`, `zh`, …) direto no modelo de workflow.

A descrição da tarefa vai inteira para o prompt (campo `tags` do modelo). Uma indicação do tipo
«put the result into the file X.mp3» é executada pelo conector: o arquivo é copiado para a pasta
do projeto e a própria linha é recortada do prompt. **As linhas de indicação são reconhecidas
apenas em russo e em inglês** — as palavras-reconhecedoras estão fixadas no código, e por isso
escreva a indicação em uma dessas duas línguas, ainda que o texto da cena esteja em português.

## Como escrever a tarefa

O modelo espera uma descrição da música, e não um comando. Funcionam:

* **estilo, andamento, instrumentos, clima** — «ambient tranquilo, 72 batidas por minuto, pads
  quentes, chiado de vinil»;
* **vocal** — «vocal feminino, baixo, com reverberação longa»;
* **letra própria** — escreva-a direto na descrição da tarefa, e o planejador a aproveitará;
* **instrumental** — escreva assim mesmo: «sem vocal, só instrumentos».

Cada execução usa um **seed aleatório**, e por isso duas tarefas com o mesmo texto darão faixas
diferentes. Se você precisa de um resultado repetível, escreva o `seed` como número no perfil do
modelo. No Base a dispersão entre execuções é bem mais ampla que no SFT: isso é uma propriedade
dele, e não uma falha.

## Treinamento de LoRA

**A aplicação, sim; o treinamento, não.**

Um adaptador pronto o modelo aceita: o ComfyUI conhece o formato oficial de LoRA do ACE-Step, e
o AI2P insere o nó `LoraLoaderModelOnly` no grafo em tempo de execução. Coloque o arquivo em
`<repositório de modelos>/loras/` e nomeie o objeto-adaptador na descrição da tarefa pela
referência `@obj:`.

Já **treinar um adaptador a partir do AI2P não é possível**, e isso está anotado no perfil com
honestidade (`lora.train.kind: external`). São duas as razões:

* o [musubi-tuner](https://github.com/kohya-ss/musubi-tuner), com que o AI2P treina LoRA para os
  demais modelos locais, não conhece o ACE-Step de forma alguma — nele não há nenhum script
  acestep (verificado em 27.08.2026);
* o treinador oficial do ACE-Step (<https://github.com/ace-step/ACE-Step-1.5>, `train.py`)
  treina sobre **gravações de áudio**, e o conjunto de dados de LoRA no AI2P são quadros-imagem.

Por isso o adaptador é treinado fora do AI2P, com o treinador oficial, e aqui é colocado como
arquivo pronto. Das três variantes, os autores consideram adequados para treinamento o Base e o
SFT (no Turbo os pesos são destilados), e o próprio Base está marcado na tabela deles como o
mais cômodo para ajuste fino.

## Erros frequentes

* **O processo some sem mensagem** — quase sempre é o driver NVIDIA antigo (veja acima).
* **«Modelo não instalado»** — os arquivos não terminaram de baixar; abra «Instalar» e a janela
  mostrará o volume restante.
* **Demora demais** — são 50 passos com CFG; para rascunhos pegue o Turbo.
* **O vocal canta no idioma errado** — coloque o código do idioma no campo `language` do modelo
  de workflow, em vez de `unknown`.
* **A faixa saiu mais curta ou mais longa que o esperado** — é o `length` do perfil, e ele está
  em segundos.
* **O ComfyUI está ocupado por um processo alheio** — o AI2P descarrega apenas o servidor que
  ele mesmo iniciou; um ComfyUI alheio já em execução na 8188 ele não toca.

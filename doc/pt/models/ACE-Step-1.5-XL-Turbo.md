# ACE-Step-1.5-XL-Turbo

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: comfyui`, `baseUrl: http://127.0.0.1:8188`, modelo `acestep_v1_5_xl_turbo`
**Chave de API:** não é preciso — o servidor sobe localmente, sem autorização
**O que faz:** texto → música e canções, habilidades `audio-song` 84, `audio-music` 86

O modelo de composição musical da equipe ACE-Step, variante **Turbo**: um destilado que calcula
uma faixa em **oito passos** em vez de cinquenta e sem classifier-free guidance. É a forma mais
rápida de obter uma faixa pronta — uma primeira escolha sensata enquanto você ajusta a
formulação.

A peculiaridade do ACE-Step 1.5, justamente aquela pela qual vale pegá-lo: dentro do modelo
funciona um **modelo de linguagem planejador**. Ele mesmo decompõe a sua solicitação em estrutura
da canção, letra, andamento e tonalidade. Por isso não é preciso escrever a letra da canção em um
campo à parte — basta descrever a tarefa em palavras, e a letra o modelo compõe sozinho (ou usa a
sua, se você a escreveu). Ele entende mais de cinquenta idiomas, e o russo está entre eles.

Funciona pelo **ComfyUI**: o AI2P o levanta como servidor local, envia o workflow e recolhe o
`.mp3` pronto para os artefatos da tarefa.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos → ACE-Step-1.5-XL-Turbo → «Instalar»**. São instalados:

* o **pacote ComfyUI** (~2,1 GB) — a compilação portátil com Python próprio;
* **quatro arquivos de pesos** no repositório de modelos (`storage.modelsRepo`), grupo
  `ACE-Step-1.5`:

| Arquivo | Tamanho | Onde |
|---|---|---|
| `acestep_v1.5_xl_turbo_bf16.safetensors` | ~9,3 GiB | `diffusion_models` |
| `qwen_4b_ace15.safetensors` | ~7,8 GiB | `text_encoders` |
| `qwen_0.6b_ace15.safetensors` | ~1,1 GiB | `text_encoders` |
| `ace_1.5_vae.safetensors` | ~322 MiB | `vae` |

**No total, cerca de 19,9 GB de download.** É baixado com retomada: uma instalação interrompida
continua do ponto em que parou, e os arquivos já baixados não são baixados de novo.

Os três registros do ACE-Step 1.5 XL do catálogo (Turbo, Base, SFT) compartilham **um mesmo
grupo** e três dos quatro arquivos. Se você já instalou um deles, o segundo baixará apenas o
arquivo de pesos próprio — cerca de 9,3 GiB, e não os 19,9 GB inteiros.

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
com fins comerciais. Isso o distingue dos modelos treinados em dados de origem obscura.

Os arquivos que o AI2P baixa são o repacote da Comfy-Org
(<https://huggingface.co/Comfy-Org/ace_step_1.5_ComfyUI_files>), publicado sob **Apache 2.0**.
As licenças foram conferidas pelos cartões dos modelos em 27.08.2026; nos modelos abertos elas
mudam raramente, mas antes de um lançamento comercial verifique o cartão mais uma vez.

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

Oito passos são **segundos a dezenas de segundos** por faixa: os autores afirmam uma canção
completa em menos de 10 segundos em uma RTX 3090. O tempo limite da tarefa no perfil está
definido em 60 minutos (`params.timeoutMinutes`) — isso basta com folga mesmo em hardware lento.

O andamento da geração é visível no **console da tarefa**, no cartão da tarefa: para lá é
despejada a saída própria do ComfyUI, junto com a barra de progresso dele.

## Parâmetros de geração

São editados no perfil do modelo (botão «Perfil de conexão»):

| Parâmetro | Padrão | Significado |
|---|---|---|
| `length` | 120 | **duração da faixa em SEGUNDOS** (não quadros, como no vídeo) |
| `steps` | 8 | passos de difusão; no Turbo não faz sentido passar de oito |
| `width` / `height` | não definidos | o som não tem tamanho de quadro; no resumo da tarefa aparecem os padrões 768×512 — eles não têm relação alguma com o som |
| `negative` | vazio | prompt negativo — **no Turbo não funciona** (veja abaixo) |
| `timeoutMinutes` | 60 | quanto esperar pelo resultado |

**Sobre a duração.** A duração de cada faixa é indicada pelo **modelo ponto** a
partir da descrição da tarefa («um minuto e meio» vira `duration: 90`). O campo `length` do
perfil ficou de reserva: ele vale quando não há ponto escolhido, quando ele não respondeu ou
quando a tarefa não diz nada sobre a duração. O valor vai de uma vez para dois lugares do
grafo — o tamanho do latente vazio e o campo `duration` do planejador — e os limites são
rígidos: de 1 a 1000 segundos (conferido com um ComfyUI vivo). No resumo da tarefa o
`length` aparece rotulado «quadros» — é assim em todos os modelos de mídia; leia-o como
«segundos».

**Sobre o prompt negativo.** O modo Turbo calcula sem classifier-free guidance (`cfg = 1`), e por
isso a condição negativa no modelo de requisição é feita pelo nó `ConditioningZeroOut` —
exatamente como no modelo oficial do ComfyUI. O valor `negative` pode ser escrito no perfil, mas
não influenciará o som; se o negativo for necessário, pegue o **ACE-Step-1.5-XL-Base** ou o
**-SFT**.

**Sobre o idioma do vocal.** O idioma é indicado pelo ponto, com um código da lista
do nó (`ru`, `en`, `zh`, `ja`, … — 51 valores ao todo). Se ninguém o indicar, fica
`unknown` e o modelo determina o idioma pela letra sozinho; nos modelos oficiais do ComfyUI
ali consta `en`, o que para letras não inglesas daria pronúncia inglesa. Um idioma fixo
também pode ser posto sem o ponto: pelo código direto no modelo de workflow.

Sem o ponto, a descrição da tarefa vai inteira para o prompt (campo `tags` do modelo); com o ponto, para `tags` vão as etiquetas de estilo que ele montou. Uma indicação do tipo
«put the result into the file X.mp3» é executada pelo conector: o arquivo é copiado para a pasta
do projeto e a própria linha é recortada do prompt. **As linhas de indicação são reconhecidas
apenas em russo e em inglês** — as palavras-reconhecedoras estão fixadas no código, e por isso
escreva a indicação em uma dessas duas línguas, ainda que o texto da cena esteja em português.

## O ponto

Este registro traz no perfil a marca **«precisa de ponto»**. O ponto é OUTRO EXECUTOR: antes da
geração ele lê a descrição da tarefa e prepara o json de controle para o ACE-Step numa
tarefa à parte. Serve qualquer executor de IA: uma assinatura CLI, um modelo local, uma API
na nuvem; ele trabalha com o seu próprio conector, como numa tarefa comum. Atribui-se de
duas maneiras: pelo campo **«Ponto»** no cartão do executor de IA (padrão para todas as suas
tarefas) e pelo campo **«Ponto»** no formulário da tarefa, ao lado da lista **«Podem
substituir o ponto»**, para quando o atribuído estiver ocupado com outro trabalho.

O ponto preenche sete campos do nó `TextEncodeAceStepAudio1.5`:

| Campo | O que é | Se não for indicado |
|---|---|---|
| `tags` | etiquetas de estilo: gênero, andamento, instrumentos, clima, vocal | a descrição inteira da tarefa |
| `lyrics` | a letra da canção | vazio — o modelo a compõe sozinho |
| `duration` | duração em segundos (1…1000) | o `length` do perfil |
| `language` | código do idioma do vocal, da lista do nó | `unknown` |
| `bpm` | andamento, batidas por minuto (10…300) | 120 |
| `keyscale` | tonalidade e modo (`C major` … `B minor`) | `C major` |
| `timesignature` | compasso: 2, 3, 4 ou 6 | 4 |

**O que acontece se você não definir o ponto.** A tarefa NÃO COMEÇA: ela para com um erro
dizendo que o executor precisa de um ponto e não há nenhum, nem na tarefa nem no próprio
executor. O sistema não pode seguir em silêncio «como de costume»: os parâmetros da faixa
viriam do nada, e isso só se veria meia hora depois, quando a faixa pronta não for a pedida.
O mesmo vale para uma resposta sem json e para uma tarefa do ponto que falhou. Os outros
dois desfechos são mais brandos: se o ponto atribuído estiver ocupado, o trabalho vai para o
primeiro livre de «podem substituir o ponto», e se todos estiverem ocupados a tarefa aguarda
em pausa até alguém ficar livre; se o ponto fez uma pergunta à pessoa, a tarefa também fica
em pausa e continua com a resposta.

**Novo arranque.** Uma tarefa em pausa ou com erro que já tem o seu json vai direto para a
geração: o ponto não é perguntado duas vezes. Uma tarefa em rascunho, em espera ou em
revisão recomeça do zero: o ponto prepara um json novo.

**Como influir no resultado pela descrição da tarefa.** Escreva o que precisa chegar aos
campos: a duração («um minuto», «90 segundos»), o idioma do vocal, o andamento, o modo, o
compasso — e dê a letra palavra por palavra, o ponto a transfere como está. O estilo
descreva com palavras: delas saem as etiquetas. O que ele de fato indicou aparece no console
da tarefa e no arquivo `prompter.json`, entre os arquivos da tarefa.

**As regras pelas quais ele trabalha** ficam junto ao perfil do modelo — o arquivo
`models/prompter_<identificador do registro>.md` no diretório de dados. Você pode editá-lo:
o texto vai inteiro para o prompt do ponto, e a edição vale a partir da próxima tarefa. O
arquivo é reescrito pela instalação quando sua versão sobe, e a replicação não o leva para
outros servidores.

## Como escrever a tarefa

O modelo espera uma descrição da música, e não um comando. Funcionam:

* **estilo, andamento, instrumentos, clima** — «ambient tranquilo, 72 batidas por minuto, pads
  quentes, chiado de vinil»;
* **vocal** — «vocal feminino, baixo, com reverberação longa»;
* **letra própria** — escreva-a direto na descrição da tarefa, e o planejador a aproveitará;
* **instrumental** — escreva assim mesmo: «sem vocal, só instrumentos».

Cada execução usa um **seed aleatório**, e por isso duas tarefas com o mesmo texto darão faixas
diferentes. Se você precisa de um resultado repetível, escreva o `seed` como número no perfil do
modelo.

A descrição é lida pelo ponto, então escreva nela os números e o idioma
diretamente: «90 segundos», «vocal em russo», «120 batidas por minuto», «em modo menor». O
que ele entendeu disso aparece no console da tarefa.

## Treinamento de LoRA

**Aplicar — sim; treinar a partir do AI2P — não.**

O modelo aceita um adaptador pronto: o AI2P insere o nó `LoraLoaderModelOnly` no grafo em
tempo de execução. Coloque o arquivo em `<repositório de modelos>/loras/` e mencione o
objeto-adaptador na descrição da tarefa com `@obj:`.

Treinar um adaptador a partir do AI2P não é possível, e o perfil diz isso honestamente:
`lora.train.kind: external` com o comando vazio — o botão «Treinar» recusa de imediato, em
vez de gastar meia hora. Verificado em 14.09.2026 nos arquivos dos repositórios:

* **o modelo tem treinador** — o oficial
  [ACE-Step-1.5](https://github.com/ace-step/ACE-Step-1.5), licença MIT, e ele roda no
  Windows com UMA única placa: `python -m acestep.training_v2.cli.train_fixed`, sem
  `torchrun`, `--num-devices` igual a 1 por padrão e workers do DataLoader
  propositalmente 0 no Windows. Exige 16 GB de VRAM no mínimo, 20 GB ou mais
  recomendados;
* **mas ele precisa de pesos diferentes dos que instalamos.** O treinador lê um diretório
  de checkpoints no formato HuggingFace (`config.json` +
  `model-0000N-of-00004.safetensors`, cerca de 19,9 GB por variante, além do VAE e do
  modelo de linguagem de rotulagem), enquanto o AI2P instala o reempacotamento da
  Comfy-Org: outros arquivos e outro arranjo. A instalação não baixa uma segunda cópia;
* **e o formato do arquivo treinado não foi verificado**: o treinador gera um adaptador
  peft sobre o próprio DiT, e o ramo do «formato oficial do ACE-Step» em `comfy/lora.py`
  está sob a classe `ACEStep`, ao passo que o 1.5 é a classe separada `ACEStep15`;
* o [musubi-tuner](https://github.com/kohya-ss/musubi-tuner), com o qual o AI2P treina
  LoRA dos demais modelos locais, não conhece o ACE-Step (verificado em 27.08.2026).

**Mesmo assim os limites do conjunto estão declarados**: o AI2P compara o conjunto com eles
e você o monta para um treinamento externo. O conjunto é de ÁUDIO (`media: audio`):
gravações de até 240 s, 48 000 Hz, 2 canais, formatos WAV, MP3, FLAC, OGG e Opus, a partir
de 10 gravações, com as legendas em um arquivo `.txt` ao lado da gravação (transcrição ou
tags). O editor mostra exatamente esses campos e guarda a gravação como está: o áudio não
passa pela compressão de imagens.

Ordem de trabalho do treinador oficial: preparar as gravações com `<nome>.lyrics.txt` e as
legendas → pré-processar em tensores → iniciar o treinamento (LoRA ou LoKr, cerca de dez
vezes mais rápido). Detalhes — [LoRA Training Tutorial](https://github.com/ace-step/ACE-Step-1.5/blob/main/docs/en/LoRA_Training_Tutorial.md).

## Erros frequentes

* **O processo some sem mensagem** — quase sempre é o driver NVIDIA antigo (veja acima).
* **«Modelo não instalado»** — os arquivos não terminaram de baixar; abra «Instalar» e a janela
  mostrará o volume restante.
* **O prompt negativo não funciona** — é assim mesmo no Turbo (veja «Parâmetros de geração»).
* **O vocal canta no idioma errado** — indique o idioma na própria descrição da
  tarefa, o ponto o repassa; sem o ponto, coloque o código do idioma no campo `language` do
  modelo de workflow, em vez de `unknown`.
* **A faixa saiu mais curta ou mais longa que o esperado** — indique a duração na
  descrição da tarefa (o ponto a entrega em segundos); sem o ponto vale o `length` do perfil,
  que também está em segundos.
* **O ComfyUI está ocupado por um processo alheio** — o AI2P descarrega apenas o servidor que
  ele mesmo iniciou; um ComfyUI alheio já em execução na 8188 ele não toca.

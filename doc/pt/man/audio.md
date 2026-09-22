# Trabalho com modelos de áudio

**Para que serve este capítulo:** obter uma cena sonora feita de várias partes — música, uma
canção, a fala de pessoas específicas, um ruído — e entender quais modelos existem para isso,
de quais amostras de som eles precisam e o que não sabem fazer.

Vamos pelo exemplo de uma cena:

> Toca uma música country tranquila, com o vocalista **X** cantando. Duas pessoas, **Y** e
> **Z**, comentam a vista de uma montanha sobre uma floresta em chamas. Sobre a cabeça delas
> passa um helicóptero de bombeiros.

## 1. A regra principal: uma tarefa — uma camada de som

Nenhum modelo de áudio do catálogo monta uma cena assim de uma vez:

* o modelo de música canta, mas não fala com a voz de pessoas específicas;
* o modelo de fala fala, mas não toca música nem canta;
* o modelo de fala com amostra de voz aceita **uma** amostra por execução — um diálogo de duas
  vozes numa execução só não sai;
* o catálogo não tem modelo de efeitos sonoros;
* o AI2P não sobrepõe faixas: não há mixagem nem nos conectores nem no plugin `tool.ffmpeg`.

Por isso a cena é dividida em **camadas**; cada camada é uma tarefa separada (ou várias), e os
arquivos prontos são mixados num editor de áudio ou de vídeo.

| Camada | Habilidade da tarefa | Modelos do catálogo | Precisa de amostra de som |
|---|---|---|---|
| música country com o vocalista X | `audio-song` | ACE-Step-1.5-XL-Turbo / -Base / -SFT (local), ElevenLabs-Music (nuvem) | não — e não há onde passá-la (seção 3) |
| falas de Y | `audio-speech` | Chatterbox-TTS, Zonos-2-TTS (por amostra), ElevenLabs-TTS-v3 (vozes prontas) | sim, uma gravação da voz de Y (seção 4) |
| falas de Z | `audio-speech` | os mesmos | sim, uma gravação da voz de Z |
| a passagem do helicóptero | — | não há modelo | sim — uma gravação pronta (seção 5) |

## 2. Modelos de áudio do catálogo

Habilidades: `audio-song` — canções com vocal, `audio-music` — música instrumental,
`audio-speech` — locução. As três são «texto → som»: o prompt é a **descrição da tarefa**; o
título, os critérios de aceitação e a experiência do projeto não chegam ao modelo.

| Modelo | Onde calcula | O que faz | Amostra de som | Preço |
|---|---|---|---|---|
| [ACE-Step-1.5-XL-Turbo](../models/ACE-Step-1.5-XL-Turbo.md), [-Base](../models/ACE-Step-1.5-XL-Base.md), [-SFT](../models/ACE-Step-1.5-XL-SFT.md) | local, ComfyUI, NVIDIA a partir de 8 GB | canções e música; escreve a letra sozinho se ela não estiver na descrição | não aceita | grátis, pesos MIT |
| [ElevenLabs-Music](../models/ElevenLabs-Music.md) | nuvem, gateway fal.ai | canções e música, de 3 s a 10 min | não aceita | US$ 0,6 por minuto |
| [ElevenLabs-TTS-v3](../models/ElevenLabs-TTS-v3.md) | nuvem, gateway fal.ai | fala com uma voz pronta (campo `voice`) | não aceita | ver a página do modelo |
| [Chatterbox-TTS](../models/Chatterbox-TTS.md) | nuvem, gateway fal.ai | fala com a voz copiada de uma amostra | uma gravação de até 30 s | US$ 0,025 por 1000 caracteres |
| [Zonos-2-TTS](../models/Zonos-2-TTS.md) | nuvem, gateway fal.ai | fala com a voz copiada de uma amostra, WAV 44,1 kHz | uma gravação de até 30 s, **obrigatória** | ver a página do modelo |

Alguns **modelos de vídeo** também geram som (Veo-3.1, Seedance-2.5, Wan-3.0-Prime, Kling-3.0,
LTX-2.5, Gemini-Omni-Flash), mas só dentro do clipe; como aproveitar isso — seção 5.

A preparação é igual para todos: o modelo está ativo (o de nuvem tem a chave `fal.apiKey`, o
local tem os arquivos instalados), há um executor de IA sobre ele, o executor está na equipe
do projeto e, no modelo local, o trabalho da equipe foi iniciado. O passo a passo está no
[exemplo do vídeo](sample_video1.md), capítulos 1.3–1.4.

## 3. A música e o vocalista X

**Qual amostra é necessária para X?** Nenhuma: os modelos de música do catálogo não aceitam
amostra de voz. O ElevenLabs-Music não tem campo para gravação, e os workflows entregues para o
ACE-Step não têm nó de carregamento de áudio. Os modelos que copiam a voz por amostra
(Chatterbox, Zonos) **falam, não cantam**.

Por isso a voz de X é descrita **em palavras** — gênero, timbre, jeito de cantar:

```
Uma balada country tranquila, 80 batidas por minuto: violão, slide guitar,
contrabaixo, vassourinhas na caixa. Som quente e antigo.
Vocal masculino: barítono grave, suave, levemente rouco, canta baixinho.
A letra fala de montanhas e de fumaça sobre a floresta, em português.
Save the result to file sound/music.mp3.
```

* A habilidade da tarefa é `audio-song`; um instrumental sem voz é `audio-music` mais as
  palavras «sem vocal».
* Escreva a sua letra direto na descrição — o ACE-Step a aproveita; sem ela, ele compõe a sua.
* Duração: no ACE-Step é `length` no perfil do modelo, **em segundos**; no ElevenLabs-Music é o
  campo de duração da seção `request` do perfil (30 s no modelo de requisição).
* A indicação de onde salvar o resultado vai **numa linha separada**: a linha inteira sai do
  prompt. As palavras que a reconhecem são **só russas e inglesas** (`save`, `result`,
  `file`…), por isso no exemplo ela está em inglês.
* O nome de um artista real na descrição não substitui a amostra e cria risco jurídico —
  descreva o caráter da voz.

**Para a voz de X ficar igual de uma faixa para outra:**

| Técnica | O que dá |
|---|---|
| a mesma descrição da voz | é prático guardá-la como passaporte de um objeto do projeto (tipo «estilo» ou «personagem») e usar o link `@obj:` |
| um `seed` fixo no perfil (ACE-Step) | resultado repetível; vazio ou 0 — aleatório a cada execução |
| um adaptador LoRA (ACE-Step) | dá para aplicar um adaptador pronto, mas não para treiná-lo no AI2P (documento do modelo, seção sobre treinamento de LoRA) |

Se a conversa de Y e Z correr por cima da canção, o vocal terá de ser abaixado na mixagem. É
mais simples pedir uma faixa em que o vocal não entre logo: «introdução de 30 segundos sem
vocal».

## 4. As vozes de Y e Z: síntese de fala por amostra

**Sim, há modelos de síntese de fala** — três registros do catálogo com a habilidade
`audio-speech`:

* **ElevenLabs-TTS-v3** — o mais bem avaliado, mas fala com uma das vozes **prontas** do
  provedor (campo `voice` da seção `request` do perfil, `Rachel` por padrão) e não aceita
  amostra. Serve quando Y e Z não são pessoas específicas: crie dois executores sobre o mesmo
  modelo com `voice` diferentes (cada executor tem o próprio perfil).
* **Chatterbox-TTS** e **Zonos-2-TTS** — copiam a voz de uma **amostra** gravada (zero-shot
  clone): não é preciso treinar nada, 10–20 segundos de gravação bastam.

O catálogo não tem modelo de fala local: os três calculam na nuvem e precisam da chave do
gateway fal.ai.

### 4.1. Quais amostras são necessárias para Y e Z

**Uma** gravação por pessoa:

| Requisito | Por quê |
|---|---|
| 10–20 s (aceita-se 3–30 s) | mais curta — o timbre não é captado; acima de 30 s — o gateway recusa |
| um só falante, sem música, ruído ou eco | o modelo copia tudo o que ouve, a sala junto |
| volume uniforme, sem saturação | as distorções passam para a síntese |
| o mesmo jeito de falar que a cena pede | conversa calma — amostra calma, não gritos |
| o mesmo idioma das falas | a pronúncia fica mais precisa |
| WAV ou MP3 | os formatos que os dois registros do catálogo aceitam |
| **consentimento do dono da voz** | a licença do modelo não dá direitos sobre a voz de outra pessoa |

Coloque os arquivos na pasta do projeto, por exemplo `refs/voice_y.wav` e `refs/voice_z.wav`.
É prático criá-los como objetos do projeto do tipo **«gravação de referência»** — filhos dos
personagens Y e Z: assim as amostras aparecem na lista de objetos e não se perdem.

### 4.2. Uma fala — uma tarefa

A execução tem **uma** amostra, então o diálogo é montado com tarefas separadas: fala de Y,
fala de Z, Y de novo… A descrição da tarefa é **dita em voz alta inteira**, por isso deve ter
só o texto da fala e as indicações ao sistema, cada uma na sua linha:

```
Olha, ali, depois da segunda serra — a fumaça já cobre o vale inteiro.
Voice sample refs/voice_y.wav.
Save the result to file sound/dialog_01_y.wav.
```

* Uma linha com o nome de um arquivo de áudio e uma palavra como `voice`, `sample`, `speaker`,
  `timbre` (em russo, «образец», «голос») é uma indicação: o arquivo vai para o modelo e a linha
  sai do texto. As palavras de reconhecimento são **só russas e inglesas**; «amostra de voz» em
  português não é reconhecida.
* **É melhor não usar o link `@obj:` numa tarefa de locução.** Ele se expande na ficha do
  objeto — nome, tipo, número e passaporte; o caminho é recortado, mas o nome e o passaporte
  ficam no texto e serão **lidos em voz alta**. Nomeie o arquivo numa linha, como no exemplo.
  (Para o modelo de música da seção 3 o link serve: lá o passaporte é a descrição do estilo.)
* A habilidade da tarefa é `audio-speech`, o executor fica sobre Chatterbox-TTS ou Zonos-2-TTS.
  É prático manter as tarefas do diálogo como subtarefas de uma tarefa-mãe — «Diálogo de Y e Z».
* A entonação vem do texto da fala e da amostra. Os campos de ajuste fino estão na seção
  `request` do perfil: o Chatterbox tem `exaggeration` (expressividade), o Zonos tem
  `accurate_mode` (mais fiel à amostra ou mais expressivo) e `language` (idioma de normalização
  do texto).
* Faça todas as falas **com o mesmo modelo**: o formato dos arquivos coincide e juntá-los fica
  mais simples.

As falas prontas são juntadas na ordem pela ação `ffmpeg_concat` do plugin
[tool.ffmpeg](../plugins/tool.ffmpeg.md). Ela não acrescenta pausas entre as falas — isso, assim
como pôr a fala sobre a música, se faz num editor.

## 5. O helicóptero de bombeiros

**É preciso um som de helicóptero separado?** Sim. O catálogo não tem modelo de efeitos
sonoros, e os que existem não servem para isso:

* os modelos de fala (Chatterbox, Zonos) copiam a **voz** da amostra, não um ruído — uma
  gravação de helicóptero na amostra não vira som de helicóptero;
* os modelos de música (ACE-Step, ElevenLabs-Music) fazem **música**: as palavras «ronco de
  helicóptero» darão, no máximo, uma cor musical, não uma passagem realista.

Três caminhos que funcionam, do confiável ao experimental:

1. **Uma gravação pronta** de uma biblioteca de sons com licença que permita o seu uso —
   coloque-a na pasta do projeto (`sound/helicopter.wav`) e na mixagem. A melhor opção para um
   efeito curto e reconhecível.
2. **O som de um modelo de vídeo.** Os modelos «texto → vídeo com som» (Veo-3.1, Seedance-2.5,
   Wan-3.0-Prime e outros) sonorizam a cena sozinhos. Uma tarefa como «Um helicóptero de
   bombeiros passa baixo sobre a floresta em chamas, ouve-se o rotor crescer e se afastar» dá um
   clipe, e a ação `ffmpeg_extract_audio` do plugin `tool.ffmpeg` tira o som dele. É pago, e a
   qualidade do som desses modelos não foi verificada à parte.
3. **Um registro próprio do catálogo.** O gateway fal.ai tem modelos dedicados a efeitos
   sonoros; não há registros entregues para eles, mas um modelo de nuvem do gateway é cadastrado
   como registro do catálogo sem mexer no código. Confira o id e os campos da requisição no
   catálogo `https://fal.ai/api/models` e no esquema do endpoint — o gateway recusa um campo
   desconhecido com HTTP 422 já numa execução paga.

## 6. A mixagem da cena

Você tem os arquivos na pasta do projeto:

```
sound/music.mp3          — country com o vocalista X
sound/dialog_01_y.wav …  — falas de Y e Z
sound/helicopter.wav     — a passagem do helicóptero
```

Eles são mixados num editor de áudio ou de vídeo: a música ao fundo e mais baixa que a fala,
as falas em ordem e com pausas, o helicóptero por cima, com entrada e saída graduais. Se a cena
vai para um vídeo, é prático mixar direto num editor de vídeo conectado por plugin (OpenShot,
Shotcut, DaVinci Resolve, Blender — veja [Plugins e MCP](../plugins/README.md)).

## 7. Erros frequentes

| Mensagem ou sintoma | O que fazer |
|---|---|
| **O modelo leu em voz alta o nome ou o passaporte do objeto** | a tarefa de locução tem o link `@obj:` — troque-o pela linha «Voice sample caminho/do/arquivo.wav» |
| **O modelo leu em voz alta a indicação do resultado** | ela estava na mesma linha da fala — passe-a para uma linha própria |
| **«A gravação de referência não foi passada»** | a descrição não tem nome de arquivo de áudio com uma palavra como `voice` ou `sample`; Chatterbox e Zonos exigem a gravação |
| **O arquivo de resultado foi para o modelo como amostra** | a linha do resultado ou o nome do arquivo contém uma palavra de reconhecimento (`voice`, `sample`, `speaker`…) — dê outro nome ao arquivo, p. ex. `dialog_01_y.wav` |
| **«Este modelo não aceita gravação»** | a tarefa foi para um modelo sem amostra (ElevenLabs-TTS-v3 ou um de música) — indique o executor explicitamente |
| **HTTP 422 do gateway** | a amostra passa de 30 s, o formato não serve ou há um campo de requisição que o endpoint não conhece |
| **A voz não parece a da amostra** | a gravação tem ruído, música ou uma segunda voz — use 10–20 s limpos |
| **A canção sai em outro idioma** | escreva o idioma na descrição; no ACE-Step, o código do idioma no campo `language` do workflow |
| **A faixa ficou mais curta ou mais longa** | no ACE-Step `length` é em segundos |

## 8. Vale lembrar

* O modelo de mídia recebe **só a descrição da tarefa**; num modelo de fala ela vira inteira o
  texto que ele vai dizer.
* A voz constante de um modelo de fala é mantida por uma **amostra**, não por treinamento: a
  mesma gravação em todas as falas de uma pessoa.
* Os modelos de música do catálogo não copiam voz por amostra — a voz do cantor é descrita em
  palavras.
* O catálogo não faz efeitos sonoros, e quem mixa as camadas não é o AI2P, e sim um editor.
* Os direitos sobre a voz e sobre os sons de bibliotecas são responsabilidade sua: a licença do
  modelo não os dá.

## Veja também

* [Objetos do projeto](objects.md) — o tipo «gravação de referência» e o link `@obj:`.
* [Vídeo a partir do quadro de referência de um personagem](sample_video1.md) — instalação do
  modelo, executor e início do trabalho, passo a passo.
* [Modelos de IA](../models/README.md) — o documento de cada modelo de áudio: preços, licenças,
  campos da requisição.

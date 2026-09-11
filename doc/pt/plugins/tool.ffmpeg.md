# Conversor de vídeo (ffmpeg) — plugin `tool.ffmpeg`

**O que faz:** prepara o material para a edição — transcodifica os clipes para o formato
intermediário de edição, extrai o áudio, junta pedaços pela lista e os normaliza a um quadro e
a uma taxa comuns.

**Para que serve.** Nossas gerações são mp4/H.264 com áudio aac/mp3 (fal.ai, ComfyUI,
`SaveAudioMP3`). O DaVinci Resolve gratuito no Linux **não decodifica H.264/H.265 de jeito
nenhum**, o AAC não é suportado nem no Studio pago, e os conjuntos de codificadores das
compilações do Blender são diferentes. O conversor torna a ligação «geração → edição»
independente do que uma compilação concreta do editor sabe fazer em um sistema operacional
concreto: antes da edição o material passa pelo ffmpeg.

---

## Ações

As ações são **estreitas e nomeadas**. Cada uma está descrita por inteiro no manifesto do
plugin: as opções do ffmpeg vêm do arquivo da distribuição, os parâmetros de trabalho vêm dos
ajustes do registro, e o agente de IA fornece **apenas caminhos**, e cada um é verificado.

| Ferramenta do agente | O que faz | O que recebe | O que sai |
|---|---|---|---|
| `ffmpeg_to_edit` | Transcodificar para o formato intermediário de edição | `in` — o arquivo, `out` — para onde (opcional) | `<nome>_edit.mov`: DNxHR ou ProRes com áudio sem compressão |
| `ffmpeg_extract_audio` | Extrair o áudio | `in`, `out` | `<nome>_audio.wav` sem compressão |
| `ffmpeg_concat` | Juntar pela lista | `files` — lista de caminhos, ou um filtro da biblioteca de mídia (`scene`, `kind`, `tag`), `out` | `ai2p_concat.<extensão das origens>` |
| `ffmpeg_uniform` | Normalizar quadro e taxa | `in`, `out` | `<nome>_uniform.mov` do tamanho e da taxa pedidos |

**A ação «executar uma linha de comando do ffmpeg» não existe e nunca vai existir.** Isso é
execução de código arbitrário com direito de escrever arquivos, e nenhuma regra de segurança a
estreita: o ffmpeg tem `-f lavfi`, os protocolos `file:`, `concat:` e `tcp:` e a opção `-y`
sobre qualquer arquivo. Pedir esse comando ao agente não adianta: ele não existe. Se for
preciso outro perfil de trabalho, mude o **ajuste do registro do plugin**.

### A ordem habitual

1. `ffmpeg_uniform` em cada pedaço — tamanho de quadro e taxa comuns.
2. `ffmpeg_concat` — a junção. Ela é **por cópia** (sem recodificar), então os pedaços precisam
   ter o mesmo formato; com pedaços de formatos diferentes a junção não funciona.
3. `ffmpeg_to_edit` — o formato intermediário para editar no editor.
4. `ffmpeg_extract_audio` — se o áudio for necessário como trilha separada.

---

## Ajustes do registro

São definidos em **«Configurações → Plugins e MCP»**, no formulário do registro; pelo texto da tarefa
não é possível alterá-los.

| Chave | Padrão | O que significa |
|---|---|---|
| `videoCodec` | `dnxhd` | codec intermediário: `dnxhd` (DNxHR) ou `prores_ks` (ProRes) |
| `videoProfile` | `dnxhr_hq` | perfil: `dnxhr_lb`/`sq`/`hq`/`hqx` para DNxHR, `0`–`3` para ProRes |
| `pixelFormat` | `yuv422p` | formato de pixel |
| `audioCodec` | `pcm_s16le` | codec de áudio (PCM sem compressão) |
| `audioRate` | `48000` | taxa de amostragem, Hz |
| `width`, `height` | `1920`, `1080` | tamanho de quadro comum para o `ffmpeg_uniform` |
| `fps` | `25` | taxa de quadros comum |

**O DNxHR é o padrão, e não o ProRes**, porque o codificador ProRes do ffmpeg (`prores_ks`) no
Windows e no Linux produz arquivos que o Resolve nem sempre lê, enquanto o DNxHR é o formato
nativo da Avid e todos os editores desta linha o aceitam.

**O par «perfil ↔ formato de pixel» é ligado:** `dnxhr_hq` exige `yuv422p`, e o `prores_ks` de
perfil 3 exige `yuv422p10le`. A divergência o próprio ffmpeg rejeita, e a recusa fica visível
ao agente na resposta da ferramenta.

---

## Onde os arquivos são escritos e o que é proibido

* Por padrão o resultado fica **ao lado do original** (o material vive em pastas por cena, e um
  arquivo que foi parar na raiz do projeto tem de ser procurado à mão); o nome recebe o sufixo
  `_edit`, `_audio` ou `_uniform`.
* O caminho de entrada e o de saída valem **somente dentro da pasta do projeto** ou dentro de
  um diretório externo aberto pelas **regras de segurança da tarefa**. Todo o resto é recusado,
  antes de o programa ser iniciado.
* O resultado **nunca cai por cima do original**: o ffmpeg com `-y` o sobrescreveria com um
  arquivo vazio antes mesmo de o ler, e perder material em silêncio é pior que uma recusa.
* As regras de segurança da tarefa valem aqui também: um subdiretório fechado para leitura está
  fechado também para a transcodificação.

## Uma conversão longa

O conversor roda como **processo separado**, portanto não trava a tarefa. O andamento é anotado
no registro de chamadas a cada 15 segundos («processados N s de material»), e quando o tempo de
espera acaba (2 horas por operação, por padrão) o processo é interrompido e o agente recebe uma
recusa clara dizendo quanto material chegou a ser processado. Isso importa: por um simples «não
terminou» não se distingue um programa travado de um trabalho honestamente longo.

---

## Limitações por sistema operacional

As compilações do ffmpeg **diferem no conjunto de codificadores**, e isso não é um detalhe:
codecs não livres e GPL faltam por completo em algumas delas. Nossas quatro operações se apoiam
em codificadores presentes em **qualquer** compilação (`dnxhd`, `prores_ks`,
`pcm_s16le`/`pcm_s24le`) e nos decodificadores internos de H.264/H.265/AAC — por isso o
conversor funciona mesmo numa compilação reduzida. Se algum dia for preciso exportar de volta
para H.264/H.265, é necessária uma compilação com `libx264`/`libx265` (licença GPL): nas LGPL
elas não existem. O conjunto de codificadores se vê com `ffmpeg -encoders`; um codificador
ausente o próprio ffmpeg nomeia («Unknown encoder») e a recusa chega ao agente.

**Windows.** O programa é instalado pelo nosso pacote `ffmpeg` com o botão «Instalar»: a
compilação `ffmpeg-9.0.1-essentials_build.zip` de https://www.gyan.dev/ffmpeg/builds/
(111 253 802 bytes, com `ffmpeg.exe` e `ffprobe.exe` dentro). Verificado ao vivo: versão 9.0.1,
todos os codificadores necessários no lugar. O link é versionado — um link móvel significaria
que cada um tem a sua versão.

**Linux.** Não temos pacote: o ffmpeg é instalado pelo sistema — `apt install ffmpeg` (Debian,
Ubuntu), `dnf install ffmpeg` (Fedora, precisa do RPM Fusion). Aqui espreita a **versão do
repositório**: o Debian 12 traz a 5.1 e o plugin precisa da **6.0 ou mais nova**, então uma 5.1
encontrada é rejeitada pela verificação de versão — nesse caso instale uma compilação recente
(por exemplo a estática de https://johnvansickle.com/ffmpeg/) e informe o caminho à mão no
formulário do plugin. No Linux não foi verificado ao vivo.

**macOS.** Não temos pacote: `brew install ffmpeg`, depois o caminho é encontrado sozinho ou
informado à mão. Não foi verificado ao vivo.

**A verificação de versão é obrigatória em qualquer sistema.** O conjunto de opções do ffmpeg
mudou bastante entre a 4.x e a 7.x, e um programa antigo encontrado em silêncio quebraria o
trabalho já durante a tarefa — enquanto a causa seria procurada no plugin.

---

## Como o programa é encontrado

1. **O caminho manual**, se estiver definido no formulário do plugin (`ffmpegPath` no
   `config.json` deste servidor) — serve tanto o arquivo do programa quanto o diretório onde
   ele está.
2. **A busca neste servidor**: o comando `ffmpeg` no PATH, a versão perguntada com `-version` e
   obrigada a ser 6.0 ou mais nova.
3. **A instalação pelo pacote** `ffmpeg` — o botão «Instalar» do formulário (Windows).

O caminho encontrado **nunca chega ao banco da organização**: ele fica no `config.json` deste
computador. A descrição do plugin é replicada para todos os servidores da organização, mas a
instalação é sempre local — cada servidor tem a sua própria resposta para «está instalado
aqui?».

Enquanto o programa não for encontrado, o plugin **não publica ação alguma**: não há com o que
transcodificar, e mostrar ao agente uma ferramenta que certamente vai recusar é desperdiçar a
vez dele.

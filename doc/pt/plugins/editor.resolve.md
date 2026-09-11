# DaVinci Resolve (OTIO)

**Código do plugin:** `editor.resolve`
**Tipo:** gateway (`gateway`)
**Formato de intercâmbio:** OTIO — `.otio`, JSON comum, padrão da Academy Software Foundation
**Software:** **não é instalado por nós** — informe o caminho de um Resolve já instalado
**O que faz:** monta uma linha do tempo `.otio` a partir da biblioteca de mídia do projeto; **a linha do tempo é criada pela pessoa**

## O principal: o Resolve gratuito não é automatizável por scripts

Tudo o mais neste documento decorre disso, por isso vem primeiro.

O DaVinci Resolve tem uma API de scripting (`DaVinciResolveScript`), mas **ela é fechada na
versão gratuita** e, desde a versão **19.1 (novembro de 2024)**, está fechada de vez: a ponte
entre processos deixou de aceitar conexões. Na versão gratuita os scripts só rodam no console
interno do próprio Resolve, à mão. Um programa externo — e o AI2P é exatamente um programa
externo para o Resolve — não consegue se conectar nem no Windows, nem no Linux, nem no macOS. A
automação existe apenas no **Resolve Studio** pago.

Por isso a cadeia de trabalho é esta:

1. o agente preenche a biblioteca de mídia do projeto (`media_add`, `media_list`);
2. o agente chama `resolve_timeline_write` e **grava o arquivo `ai2p_scenes.otio`** na pasta do
   projeto;
3. **a pessoa abre o Resolve e importa o arquivo com o mouse:**
   `File → Import → Timeline → OTIO` (em versões antigas
   `File → Import Timeline → AAF, EDL, XML…`, com `.otio` na mesma lista de formatos).

A linha do tempo **não aparece sozinha**. Se o relatório da tarefa disser «a linha do tempo foi
criada no Resolve», isso é falso: o que foi criado é um arquivo que a pessoa ainda precisa
importar.

Nunca escrevemos dentro do projeto do Resolve: ele é um banco de dados fechado (PostgreSQL ou o
formato próprio), e não se mexe nele de fora.

## A ação do agente

| Ferramenta | O que faz |
|---|---|
| `resolve_timeline_write` | montar `ai2p_scenes.otio` a partir da biblioteca de mídia do projeto |

Parâmetros (todos opcionais):

| Campo | Significado |
|---|---|
| `scene` | pegar apenas os recursos desta cena |
| `kind` | pegar apenas este tipo de mídia (`video`, `audio`, `image`, `subtitle`, `project`) |
| `tag` | pegar apenas os recursos com esta etiqueta |
| `file` | onde deixar o arquivo; por padrão `ai2p_scenes.otio` na raiz da pasta do projeto |

A ordem dos clipes é **a mesma que o `media_list` imprime**: cena, `order`, `take`, código do
objeto. Quem leu aquela lista precisa ver exatamente ela no Resolve.

Distribuição pelas trilhas:

| Trilha OTIO | Tipo de trilha | O que vai nela |
|---|---|---|
| `V1` | `Video` | `video`, `image`, `project` |
| `A1` | `Audio` | `audio` |

Um recurso sem duração conhecida (uma imagem, uma cartela) recebe **5 segundos**, e não zero: um
clipe de comprimento zero é um plano perdido em silêncio.

**As legendas (`subtitle`) não viajam no `.otio`:** o OTIO não tem trilha para elas. O `.srt` a
pessoa adiciona no Resolve à parte (`File → Import → Subtitle`).

## Software: não dá para instalar, só dá para apontar

Este plugin não tem botão «Instalar» **de propósito**. O pacote do DaVinci Resolve pesa 3–4 GB e
é baixado do site da Blackmagic Design **atrás de um formulário de registro**; não existe link
direto permanente que pudesse entrar no catálogo de pacotes. Não podemos baixá-lo por você e nem
tentamos.

O que fazer: instale o Resolve você mesmo e informe o caminho no formulário do plugin —
**Configurações → Plugins e MCP → DaVinci Resolve (OTIO)**. Serve tanto o arquivo do programa
quanto a pasta em que você o instalou:

| Sistema | O que informar |
|---|---|
| Windows | `C:\Program Files\Blackmagic Design\DaVinci Resolve\Resolve.exe`, ou essa pasta |
| Linux | `/opt/resolve/bin/resolve`, ou `/opt/resolve` |
| macOS | `/Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/MacOS/DaVinci Resolve` |

O caminho fica **no `config.json` deste servidor** e nunca é replicado: na máquina vizinha o
Resolve está em outro lugar, e metade dos servidores não o tem de forma alguma.

Verifica-se **apenas a existência do executável**: o Resolve não informa a sua versão pela linha
de comando, então o plugin não declara nenhuma faixa de versões aceitáveis.

Enquanto o caminho não for informado e o programa não for encontrado, o estado do plugin neste
servidor é **«procurando o software»** e as suas ações não são publicadas ao agente: uma
ferramenta que certamente responderá com recusa desperdiçaria o turno do agente. Essa é a recusa
«software não encontrado neste servidor», e uma linha no formulário a resolve.

## Limitações por sistema operacional

| Sistema | O que importa |
|---|---|
| **Windows** | O Resolve gratuito lê H.264/H.265 em `.mp4`/`.mov`. O áudio AAC **não é suportado** (veja abaixo) — em nenhuma versão e em nenhum sistema. O programa costuma estar em `C:\Program Files\Blackmagic Design\DaVinci Resolve\Resolve.exe`. |
| **Linux** | A compilação gratuita **não decodifica H.264 nem H.265**: ela não traz os codecs licenciados. Nossas gerações (fal.ai, ComfyUI) são justamente `mp4/H.264`, então sem transcodificar você terá uma linha do tempo em que todos os clipes dizem «Media Offline». Funcionam DNxHR (`.mov`), ProRes (`.mov`), CinemaDNG e WAV sem compressão para o som. O programa fica em `/opt/resolve/bin/resolve`. |
| **macOS** | H.264/H.265 são lidos (o decodificador é do sistema). O AAC continua sem suporte. |
| **Todos os sistemas** | **O AAC não é suportado nem no Resolve Studio pago.** O som dos nossos clipes e gerações (`aac`, `mp3`) precisa ser convertido para WAV. |

À parte: o Resolve lê OTIO de fábrica **a partir da versão 18.5**, inclusive na versão gratuita.
Compilações mais antigas não têm o item de importação de OTIO — nelas é preciso atualizar.

## Ligação com o plugin conversor de vídeo (ffmpeg)

A transcodificação é um **passo explícito**, não mágica silenciosa dentro da exportação: o
agente vê o que faz e você vê o que saiu. A ordem do trabalho antes de montar o `.otio`:

1. `ffmpeg_to_edit` — passar cada clipe para o formato intermediário de edição (por padrão DNxHR
   HQ em contêiner MOV com áudio sem compressão);
2. `ffmpeg_extract_audio` — extrair o som para WAV, se ele for necessário como trilha separada;
3. `media_add` — colocar os arquivos resultantes na biblioteca de mídia (com cena, ordem e take
   próprios);
4. `resolve_timeline_write` — montar `ai2p_scenes.otio`.

Se o plugin conversor (ffmpeg) não estiver configurado, ou o ffmpeg não for encontrado neste
servidor, os passos 1–2 recusam com texto claro («o programa do plugin não foi encontrado neste
servidor»), e isso precisa ser resolvido antes de montar a linha do tempo, não depois: o `.otio`
será montado mesmo com material inservível — ele apenas referencia arquivos —, mas não haverá
com que abri-lo.

No Windows e no macOS o passo de transcodificação é opcional, mas útil: o DNxHR navega quadro a
quadro, enquanto o H.264 de GOP longo vai aos trancos.

## O que o OTIO não transporta

OTIO é um formato de **intercâmbio da decisão de edição**, não um formato de projeto. Ele
transporta:

* o conjunto de clipes e as referências aos arquivos;
* a ordem dos clipes e as suas durações;
* a distribuição pelas trilhas;
* nossas anotações em `metadata.ai2p` — cena, take, ordem, legenda e tipo de mídia.

Ele **não** transporta efeitos, transições, correção de cor, mudanças de velocidade, composição
Fusion nem a mixagem do Fairlight. O ciclo de ida e volta é **com perda**: exporte uma linha do
tempo do Resolve para `.otio` e traga de volta, e todo o acabamento terá sumido.

Conclusão prática: nosso `.otio` é uma **montagem bruta** (ordem e duração). O acabamento a
pessoa faz dentro do Resolve, e reimportar o nosso arquivo por cima do trabalho dela não é
aceitável: importe-o como uma linha do tempo nova.

## Ajustes do registro do plugin

**Configurações → Plugins e MCP → DaVinci Resolve (OTIO) → «Configurar»:**

| Ajuste | Padrão | Significado |
|---|---|---|
| Quadros por segundo | 25 | taxa da linha do tempo; para ela são convertidas as durações |
| Largura do quadro | 1920 | tamanho do projeto, vai para `metadata` |
| Altura do quadro | 1080 | o mesmo |

Os ajustes são propriedade do **registro da organização** e são replicados: combinar editar a
25 quadros é uma combinação para todo o cluster. O caminho do programa, ao contrário, é próprio
de cada servidor.

## Caminhos e segurança

* **Todos os caminhos dentro do `.otio` são relativos** (campo `target_url`). Um caminho absoluto
  quebra a portabilidade: na máquina vizinha esse mesmo clipe está em outro lugar.
* O caminho do clipe é calculado **a partir da pasta do arquivo de saída**.
* Um recurso do armazenamento da organização (`store:`) fica fora da pasta do projeto, por isso a
  exportação o **copia** para o subdiretório `ai2p_media/` ao lado da linha do tempo: de outro
  modo a referência seria absoluta ou conteria `..`.
* O agente escreve **apenas o seu próprio arquivo** `ai2p_scenes.otio`. O projeto do Resolve e
  qualquer arquivo da pessoa nunca são tocados.
* É possível escrever **na pasta do projeto** e em pastas externas, se as regras de segurança da
  tarefa as abriram. O limite funciona literalmente: são verificados tanto o caminho do arquivo
  quanto cada caminho escrito dentro dele.

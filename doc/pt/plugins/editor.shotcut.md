# Shotcut / Kdenlive (MLT XML)

**Código do plugin:** `editor.shotcut`
**Tipo:** gateway (`gateway`)
**Formato de intercâmbio:** MLT XML — um mesmo arquivo abre no Shotcut (`.mlt`) e no Kdenlive
**Software:** instalado por nós (Shotcut portátil) ou aproveitamos um `melt` já instalado
**O que faz:** monta uma linha do tempo a partir da biblioteca de mídia do projeto e **calcula com ela um vídeo pronto sem abrir janela nenhuma**

## Por que este é a primeira escolha entre os quatro gateways

Dos quatro editores desta linha (Shotcut/Kdenlive, DaVinci Resolve, Blender VSE, OpenShot)
só este tem **renderização sem janela e sem pessoa**: o programa `melt`, que vem com o MLT, lê o
arquivo que montamos e escreve um `mp4` pronto. O Resolve e o OpenShot não têm esse caminho de
jeito nenhum; o Blender tem, mas executando um script Python nosso.

Por isso aqui a cadeia se fecha: o agente montou a linha do tempo → o agente calculou o vídeo →
a pessoa recebeu o resultado como arquivo. Nenhum passo com o mouse.

O segundo argumento: **um arquivo para dois editores**. MLT XML é o formato nativo do Shotcut e
é lido também pelo Kdenlive (`Projeto → Abrir`). Não foi preciso um gateway separado para o
Kdenlive.

## Ações do agente

| Ferramenta | O que faz |
|---|---|
| `shotcut_timeline_write` | montar `ai2p_library.mlt` a partir da biblioteca de mídia do projeto |
| `shotcut_render` | calcular o vídeo pela linha do tempo montada, com o programa `melt` |

Parâmetros de `shotcut_timeline_write` (todos opcionais):

| Campo | Significado |
|---|---|
| `scene` | pegar apenas os recursos desta cena |
| `kind` | pegar apenas este tipo de mídia (`video`, `audio`, `image`, `subtitle`, `project`) |
| `tag` | pegar apenas os recursos com esta etiqueta |
| `file` | onde colocar o arquivo; por padrão, `ai2p_library.mlt` na raiz da pasta do projeto |

Parâmetros de `shotcut_render`:

| Campo | Significado |
|---|---|
| `file` | o que renderizar; por padrão, `ai2p_library.mlt` |
| `out` | onde colocar o resultado; extensão `.mp4`, codecs H.264 + AAC |

**A renderização não tem linha de comando própria.** Os argumentos do `melt` estão fixados pelo
manifesto do plugin e o agente passa só dois caminhos. Não existe no sistema nenhuma ação do
tipo «executar um comando qualquer», e isso é de propósito: seria executar código alheio com
direito de escrever arquivos, e uma regra de segurança não consegue estreitar isso.

A ordem dos clipes é **a mesma que o `media_list` imprime**: cena, ordem (`order`), tomada
(`take`), código do objeto. Quem olhou a lista tem de ver no editor exatamente aquela lista.

Distribuição pelas trilhas:

| Trilha | O que vai nela |
|---|---|
| `V1` | `video`, `image`, `project` |
| `A1` | `audio` (a trilha leva a marca `hide="video"`) |

Um recurso sem duração conhecida (uma imagem, uma cartela) recebe **5 segundos**, e não zero: um
clipe de comprimento zero é um plano que some em silêncio.

## Software

O plugin primeiro **procura o programa neste computador**: `melt`, `qmelt`, `melt.exe`,
`qmelt.exe` no `PATH`, verificação com a chave `--version`, versão mínima aceitável **7.0**.
Achou — usa como está; não achou — o botão **«Instalar»** baixa um Shotcut portátil e o
descompacta sozinho. O terceiro caminho é informar o caminho à mão no formulário do plugin
(**Configurações → Plugins e MCP → Shotcut / Kdenlive (MLT XML)**); serve tanto o arquivo do
programa quanto a pasta em que ele foi instalado.

Na distribuição do Shotcut para Windows o programa se chama **`melt.exe`**, e não `qmelt.exe` —
é preciso procurar os dois nomes. Nas compilações de Linux e em algumas de macOS ele se chama
`qmelt`.

O caminho do programa fica **no `config.json` deste servidor** e nunca é replicado: no vizinho
do cluster o Shotcut está em outro caminho, e metade dos servidores não o tem de jeito nenhum.
A descrição do plugin, ao contrário, é replicada para toda a organização.

Enquanto o programa não for encontrado, o estado do plugin neste servidor é **«procurando o
software»**, e as suas ações não são publicadas ao agente: uma ferramenta que já se sabe que vai
responder com uma recusa gastaria o turno do agente à toa. É exatamente isso que significa a
recusa «programa não encontrado neste servidor».

## Limitações por sistema operacional

| Sistema | O que é importante saber |
|---|---|
| **Windows** | Funciona sem ressalvas. O programa de renderização se chama `melt.exe` e fica ao lado do `shotcut.exe` na pasta de instalação. A variável de ambiente `QT_QPA_PLATFORM` não é necessária e não é definida. |
| **Linux** | Num servidor **sem display** o `melt` não inicia de jeito nenhum — cai com «could not connect to display», porque é um programa Qt. Por isso o plugin o executa com `QT_QPA_PLATFORM=offscreen`; a variável é definida **só no Linux** (`envOs` no manifesto). Se você chamar o `melt` à mão, defina-a você mesmo. Nas compilações das distribuições o programa costuma se chamar `melt`; na do Shotcut, `qmelt`. |
| **macOS** | O programa fica **dentro do pacote do aplicativo**: `/Applications/Shotcut.app/Contents/MacOS/qmelt`. Ele não está no `PATH`, então a busca automática quase nunca acha nada — o caminho é informado à mão. As compilações do Homebrew (`brew install mlt`) deixam um `melt` no `PATH` e também servem. |
| **Todos os sistemas** | O conjunto de codificadores depende da compilação do MLT. Os nossos padrões — `libx264` para vídeo e `aac` para som — existem na distribuição do Shotcut nos três sistemas, mas uma compilação enxuta do sistema pode não os ter; então a renderização recusa com o texto do próprio `melt`, e o material precisa ser preparado com o plugin `tool.ffmpeg`. |
| **Kdenlive** | O arquivo abre, mas o Kdenlive **o recalcula para o seu próprio perfil de projeto** ao abrir. Se a frequência da nossa linha do tempo não coincidir com o perfil do Kdenlive, as durações dos clipes se deslocam: mantenha o ajuste «Quadros por segundo» igual dos dois lados. |

## Ajustes do registro do plugin

**Configurações → Plugins e MCP → Shotcut / Kdenlive (MLT XML) → «Configurar»:**

| Ajuste | Padrão | Significado |
|---|---|---|
| Quadros por segundo | 25 | frequência da linha do tempo; para ela também são recalculadas as durações dos clipes |
| Largura do quadro | 1920 | tamanho do perfil MLT |
| Altura do quadro | 1080 | o mesmo |

Os ajustes são propriedade do **registro da organização** e são replicados: combinou montar a 25
quadros — combinou em todo o cluster. O caminho do programa, ao contrário, é próprio de cada
servidor.

## Caminhos e segurança

* **Todos os caminhos dentro do `.mlt` são apenas relativos** (propriedade `resource`). Um
  caminho absoluto mata a portabilidade: no vizinho do cluster o mesmo vídeo está em outro lugar
  e a linha do tempo abre vazia.
* O caminho do clipe é contado **a partir da pasta do arquivo de saída**.
* Um recurso do armazenamento da organização (`store:`) fica fora da pasta do projeto, por isso
  na exportação ele é **copiado** para o subdiretório `ai2p_media/` ao lado da linha do tempo:
  senão a referência a ele seria absoluta ou teria `..`.
* O agente escreve **apenas o seu próprio arquivo** `ai2p_library.mlt`. O projeto da pessoa
  nunca é tocado: ela pode mantê-lo aberto no editor, e salvar pelo editor apagaria o que
  escrevemos (ou o contrário). A linha do tempo pronta a pessoa importa por conta própria.
* Escrever é permitido **na pasta do projeto** e, se as regras de segurança da tarefa abriram
  pastas externas, nelas também. São verificados tanto o caminho do próprio arquivo quanto cada
  caminho que existe dentro dele.

## Ligação com o plugin conversor de vídeo (ffmpeg)

O MLT junta material de tamanhos e frequências diferentes, mas recalculando na hora — ou seja,
devagar e nem sempre com exatidão. Sai mais barato preparar antes:

1. `ffmpeg_uniform` — um tamanho de quadro comum e uma frequência comum;
2. `ffmpeg_extract_audio` — tirar o som para WAV, se ele for preciso como trilha separada;
3. `media_add` — colocar os arquivos obtidos na biblioteca de mídia;
4. `shotcut_timeline_write` e `shotcut_render`.

A recodificação é um **passo explícito**, e não mágica silenciosa dentro da exportação: o agente
vê o que faz e você vê o que saiu.

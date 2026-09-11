# OpenShot — gateway `editor.openshot` (reserva)

Monta um **projeto do OpenShot** `.osp` a partir da biblioteca de mídia do projeto. O formato
`.osp` é JSON puro em **UTF-8** (desde o OpenShot 2.0), por isso o arquivo é gravado direto: não
é preciso executar código nem importar à mão um formato alheio — a pessoa abre o arquivo pronto
com um duplo clique.

## Por que este gateway é o de reserva

Por **critérios formais** o OpenShot é o melhor dos quatro candidatos do ramo:
multiplataforma, gratuito, formato de projeto aberto, gravação automática, e o arquivo abre
inteiro e de uma vez.

Por **confiabilidade** ele é pior, e é mais honesto dizer isso em voz alta:

* **Ele não tem render sem janela, de jeito nenhum.** O `openshot-qt` calcula o filme apenas com
  a sua própria janela; o Shotcut/Kdenlive têm o `melt` para isso, e o filme é renderizado sem
  pessoa. Por isso o manifesto deste plugin não tem bloco `render` nem uma segunda ação:
  prometer um render que não existe é pior do que não tê-lo.
* **A fama de estabilidade do OpenShot é fraca** — as quedas em projetos longos são conhecidas
  há anos.

**A primeira escolha para a edição é `editor.shotcut` (Shotcut / Kdenlive).** Pegue o OpenShot
se a pessoa trabalha justamente nele, ou se o Shotcut não está instalado naquela máquina.

## O que é preciso instalar

| o quê | como |
|---|---|
| OpenShot 2.0 ou mais novo | instale você mesmo e informe o caminho: Configurações → Plugins e MCP → plugin `editor.openshot` → campo do caminho |

**Aqui não há botão «Instalar», e isso é proposital.** No Windows o OpenShot vem apenas como o
instalador `OpenShot-v4.0.0-x86_64.exe` (229 078 328 bytes, publicado em 30/08/2026,
https://github.com/OpenShot/openshot-qt/releases), o projeto não tem nenhum pacote portátil e o
nosso instalador de pacotes descompacta arquivos compactados em vez de rodar instaladores
alheios. Por isso o código de pacote do manifesto está vazio e o plugin mostra o campo «informe
onde ele já está».

Você pode informar tanto o arquivo do programa quanto a pasta onde o instalou. O OpenShot não
informa a sua versão pela linha de comando, então só se verifica a **existência do executável**:
`openshot-qt.exe` (Windows), `openshot-qt` ou o AppImage (Linux), o aplicativo do `.dmg` (macOS).

O caminho até o programa é um valor **deste computador**: ele fica no `config.json` do servidor
(`plugins.editor.openshot.path`) e não é replicado pelo cluster. A descrição do plugin, ao
contrário, é replicada.

**Importante e pouco óbvio:** enquanto o caminho não estiver informado, o estado do plugin neste
servidor é «procurando o software» e a ação **não é publicada** ao agente, ainda que a montagem
do `.osp` funcionasse sem o OpenShot instalado (o arquivo somos nós que gravamos). É uma regra
geral do núcleo de plugins, não uma peculiaridade deste gateway; o `editor.resolve` se comporta
igual.

## Ações do agente

| ferramenta | o que faz |
|---|---|
| `openshot_timeline_write` | montar `ai2p_library.osp` a partir da biblioteca de mídia do projeto: uma trilha de vídeo (camada `L2`, número 2000000) e uma de áudio (camada `L1`, número 1000000), na ordem de `media_list`. Filtros: `scene`, `kind`, `tag`. O nome do arquivo é `file` |

A ordem do trabalho: os recursos entram na biblioteca de mídia (`media_add`), depois
`openshot_timeline_write`, e então uma **pessoa** abre o arquivo no OpenShot e, se o filme for
necessário, inicia a exportação ela mesma.

O arquivo de projeto da pessoa nunca é tocado: grava-se apenas o nosso `ai2p_library.osp`.

## Como o arquivo gerado é estruturado

O `.osp` difere do MLT e do OTIO porque a sua lista de clipes é **plana**:

* `files` — a lista de recursos: caminho, tipo de mídia, tipo de leitor (`FFmpegReader` para
  vídeo e áudio, `QtImageReader` para imagens), duração e os indicadores `has_video` /
  `has_audio` / `has_single_image`;
* `clips` — **um único array para o arquivo inteiro**, e cada clipe indica a sua trilha e o seu
  lugar com `layer` (número da camada de `layers`) e `position` (segundos desde o início). Aqui
  não existe trilha-sequência como no MLT, então o início de cada clipe é calculado por nós;
* `layers` — cinco camadas, duas delas rotuladas (`A1` e `V1`);
* `profile` — o nome do perfil do OpenShot como texto (`FHD PAL 1080p 25 fps` para 1920×1080 a
  25 qps); o quadro e a taxa vêm dos ajustes do registro do plugin.

A cena, a tomada e a legenda do recurso vão para `metadata` da entrada do arquivo
(`ai2p_scene`, `ai2p_take`, `ai2p_caption`) — o OpenShot não os mostra, mas ao ler o arquivo a
olho eles dizem de onde veio o clipe.

## Caminhos e segurança

**Os caminhos são somente relativos** — e essa é a forma nativa do formato: o próprio OpenShot
salva o projeto com caminhos relativos e os expande ao abrir, a partir da pasta do `.osp`. Um
caminho absoluto quebraria a portabilidade no cluster: em outra máquina o mesmo clipe está em
outro lugar e o projeto abriria com todos os clipes como «arquivo não encontrado». A chave no
JSON se chama exatamente `path` e aparece **duas vezes**: na entrada de `files` e no `reader` do
clipe.

O caminho do clipe é calculado **a partir da pasta do arquivo de saída**, e não da pasta do
projeto. Um recurso do armazenamento da organização (`store:`) fica fora da pasta do projeto,
então é copiado para a subpasta `ai2p_media` ao lado do `.osp`; o mesmo se faz com qualquer
recurso que de outra forma teria de ser endereçado com `..`.

A limitação por pastas funciona **literalmente**: gravamos JSON e não executamos código, então
são verificados tanto o caminho do próprio arquivo quanto os caminhos dentro dele. Pode-se
gravar na pasta do projeto e nas pastas abertas pelas regras de segurança da tarefa — em nenhum
outro lugar; um caminho para fora dá uma recusa clara e nenhum arquivo aparece.

## Limitações por sistema operacional

Montar um `.osp` é gravar um arquivo de texto, e isso é **igual em todos os sistemas**. O que
muda é apenas como se obtém o próprio OpenShot em cada um.

* **Windows.** Apenas o instalador `OpenShot-v4.0.0-x86_64.exe` (229 078 328 bytes); há também
  um de 32 bits, `OpenShot-v4.0.0-x86.exe` (224 873 009 bytes). Não há pacote portátil e o nosso
  pacote não consegue instalá-lo. Depois da instalação o executável é `openshot-qt.exe`.
  **Não verificado ao vivo: o OpenShot não está instalado nesta máquina.**
* **Linux.** `OpenShot-v4.0.0-x86_64.AppImage` (254 457 024 bytes) ou o pacote da distribuição
  (`apt install openshot-qt`). O AppImage roda como um arquivo comum — aponte para ele. O
  OpenShot precisa sempre da sua janela: o programa não tem modo sem tela, e é exatamente por
  isso que não há render sem janela. **Não verificado ao vivo.**
* **macOS.** `OpenShot-v4.0.0-x86_64.dmg` (292 188 716 bytes). O caminho é informado à mão.
  **Não verificado ao vivo.**
* **Comum.** Os números foram obtidos por uma consulta à lista de versões do GitHub em
  03/09/2026 (versão v4.0.0 de 30/08/2026, `libopenshot` 1.0.0). O bloco `version` do arquivo
  gerado não é enfeite: ao abrir, o OpenShot roda por ele os seus passos de atualização de
  projetos antigos.

## O que este gateway não faz

* **Não renderiza.** O filme é calculado a partir do `.osp` por uma pessoa que abre o projeto.
  Se for preciso o filme sem pessoa, pegue o `editor.shotcut` (ele tem o `melt`) ou o conversor
  `tool.ffmpeg`.
* **Não edita o projeto da pessoa** — grava apenas o seu próprio `ai2p_library.osp`.
* **Não leva transições, efeitos nem correção de cor** — apenas a distribuição dos recursos em
  duas trilhas com as suas durações.
* **Não transcodifica o material.** Se as peças têm formatos diferentes, passe antes o conversor
  `tool.ffmpeg`.
* **Não usa a `libopenshot`.** Ao lado do editor existe a biblioteca `libopenshot` com bindings
  para Python, C++ e Ruby — com ela daria para montar o projeto e também renderizá-lo sem
  janela. Está fora do escopo deste trabalho: é uma dependência externa à parte, com a sua
  própria instalação, enquanto o formato `.osp` nós gravamos direto e sem ela.

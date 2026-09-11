# Blender VSE — gateway `editor.blender`

Monta o vídeo no **Blender Video Sequence Editor** e entrega o trabalho ao próprio Blender: nós
geramos um script Python, o Blender o executa sem janela e salva ele mesmo o projeto `.blend` —
ou renderiza o filme pronto.

## Por que um script e não editar o `.blend`

Um arquivo `.blend` é um **despejo binário das estruturas internas** do Blender com um bloco
DNA (tipos e deslocamentos dos campos ficam escritos no próprio arquivo e mudam de versão para
versão). Código de terceiros não escreve nele: seria preciso refazer a leitura e a montagem do
arquivo a cada versão do Blender.

Existe exatamente um caminho prático, e ele é o oficial:

```
blender --background --factory-startup --python ai2p_timeline.py -- ai2p_timeline.blend
```

Nós escrevemos o script e o Blender o executa e salva o projeto sozinho. A mesma execução
também renderiza: basta pedir um resultado com outra extensão.

## O que precisa estar instalado

| o quê | como |
|---|---|
| Blender 3.0 ou mais novo | Configurações → Plugins e MCP → plugin `editor.blender` → **Instalar** (arquivo portátil), ou informar o caminho de um `blender` já instalado |

O plugin primeiro **procura `blender` no PATH** (bloco `system` da entrada do pacote, conferido
com `--version`, mínimo 3.0): quem faz 3D já tem o Blender, e baixar ao lado uma segunda cópia
de 386 MiB não faz sentido. O caminho do programa é um valor **desta máquina**: fica no
`config.json` do servidor (`plugins.editor.blender.path`), não na base da organização, e não é
replicado pelo cluster. A descrição do plugin, ao contrário, é replicada.

Gerar o script funciona **sem** o Blender instalado — o programa só é preciso para executá-lo.

## Ações do agente

| ferramenta | o que faz |
|---|---|
| `blender_timeline_write` | cria `ai2p_timeline.py` a partir da biblioteca de mídia do projeto: uma trilha de vídeo e uma de áudio do VSE, na ordem de `media_list`. Filtros: `scene`, `kind`, `tag`. O nome do arquivo é `file` |
| `blender_render` | executa o script gerado no Blender sem janela. Um `out` com extensão `.blend` (padrão) dá o projeto com trilhas; outra extensão dá o vídeo renderizado (mp4, H.264 + AAC) |

Ordem de trabalho: os recursos vão para a biblioteca (`media_add`), depois
`blender_timeline_write`, depois `blender_render`. O `.blend` da pessoa nunca é tocado: o
gateway escreve apenas os seus `ai2p_timeline.py` e `ai2p_timeline.blend`.

## Segurança: por que aqui ela é especial

Com o Blender, limitar por pasta é **ilusório**: o Python dentro do Blender abre qualquer
arquivo com `open()` e nenhuma regra de segurança do AI2P enxerga isso. O que limita de verdade
não é o caminho, e sim o fato de que **o texto do script é escrito por nós**. Daí três regras,
e as três estão em vigor:

1. **Não existe script livre da IA.** A ação «execute este Python» não existe no catálogo e não
   vai existir. Uma ação é o nosso modelo mais substituições, e o modelo preenche apenas
   parâmetros: os filtros de clipes e o nome do arquivo.
2. **As substituições são escapadas** como literal de string do Python. Um nome de arquivo com
   aspas, quebra de linha ou barra invertida continua sendo valor e não vira código.
3. **Só o nosso arquivo é executado.** A ação de render não tem o parâmetro «o que executar»
   (`ownFileOnly` no manifesto); se tivesse, o agente escreveria o próprio `.py` com a
   ferramenta de escrita de arquivos e o lançaria com as nossas mãos.

Mais a regra geral do ramo: pode-se escrever na pasta do projeto e nos diretórios abertos pelas
regras de segurança da tarefa; os caminhos dentro do script são apenas relativos (contados a
partir da pasta do próprio script), e o `.blend` salvo os mantém relativos
(`save_as_mainfile(relative_remap=True)`).

## Limitações por sistema operacional

O conjunto de codecs das compilações do Blender **é diferente**, e isso é o principal a saber
antes de renderizar.

* **Windows.** O arquivo portátil `blender-5.2.1-windows-x64.zip` é instalado pelo nosso pacote
  (404 851 964 bytes, obtidos por requisição HEAD em 03.09.2026). A compilação oficial do
  blender.org traz o FFmpeg com H.264 e AAC — a renderização em mp4 funciona de imediato.
  Verificado ao vivo no Blender 4.0.2.
* **Linux.** Não temos pacote: na publicação há `blender-5.2.1-linux-x64.tar.xz`, e instalá-lo
  é trabalho à parte. A pessoa instala o programa e informa o caminho à mão. Uma compilação
  **do repositório da distribuição** (`apt install blender`, `dnf install blender`) costuma ser
  ligada ao FFmpeg do sistema, cujo conjunto de codificadores varia: H.264 e AAC podem faltar
  por completo. Se a renderização recusar por codec, use a compilação do blender.org, ou
  renderize pelo gateway do Shotcut ou pelo conversor `tool.ffmpeg`. Com `--background` o
  Blender não abre janela, então variáveis como `QT_QPA_PLATFORM` não são necessárias. **Não
  verificado ao vivo.**
* **macOS.** Não temos pacote: na publicação só há `.dmg`, e o da 5.2.1 é **apenas arm64** —
  máquinas Intel precisam de uma versão mais antiga. O caminho é informado à mão. **Não
  verificado ao vivo.**
* **Comum.** Até a 4.4 o Blender chama as peças da edição de `sequences`; da 4.4 em diante, de
  `strips`. Nosso script entende os dois nomes, então funciona tanto na 3.x quanto na 5.x.

## O que este gateway não faz

* Não abre nem altera o `.blend` da pessoa — apenas o seu próprio.
* Não faz transições, letreiros nem correção de cor: o VSE é um editor fraco (sem linhas do
  tempo aninhadas, poucas transições, quase sem processamento de áudio). Use este gateway se o
  projeto **já tem 3D** e você quer a edição no mesmo arquivo; sem 3D, prefira Shotcut/Kdenlive
  (`editor.shotcut`) — edita melhor e renderiza mais fácil.
* Não transcodifica o material. Se as peças têm formatos diferentes, passe antes o conversor
  `tool.ffmpeg`.

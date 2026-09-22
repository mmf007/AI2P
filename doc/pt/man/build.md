# AI2P — compilação, distribuição e estrutura do repositório

> A página principal do repositório é o `Readme.md` da raiz dele; a documentação para o usuário
> é a [descrição breve](../README.md) e o [índice](../index.md). Aqui está tudo para quem compila
> o AI2P a partir dos fontes.

Um sistema de trabalho conjunto de agentes de IA e pessoas sobre tarefas: planejador de
trabalhos, orquestrador de agentes de IA e acumulador de experiência. Funciona localmente
(Windows 10–11, Linux, macOS), com a interface pelo navegador web.

A documentação se divide em duas: a **de projeto** — no diretório `doc/` da raiz do repositório
(`doc/AI2P_ТЗ_v1.NN.md` é a descrição do sistema tal como ele é, e as versões anteriores estão
ali mesmo; a partir da versão 1.60 o número da especificação coincide com a versão do
aplicativo; `doc/AI2P_release.md` trata de versões, distribuição e atualização) — e a
**distribuída junto com o programa**, em `AI2P_app/doc/`, ao lado do código: os documentos dos
modelos ([`models/`](../models/README.md)) e dos tipos de importação
([`import/`](../import/README.md)), que se abrem na interface pelo botão «i». O diretório
`AI2P_app/doc` é copiado por inteiro para a distribuição de release.

> Links para arquivos fora de `AI2P_app/doc` não são colocados em um documento distribuído: ele
> é renderizado dentro do aplicativo, e ali um link relativo para fora não leva a lugar nenhum
> (especificação, cap. 14).

**Situação: etapa 1 — planejador local de tarefas.** Está implementado: SQLite + event log +
armazenamento de arquivos (especificação, cap. 6), as entidades (cap. 2), a API HTTP (API-first,
cap. 3), a interface no estilo do VS Code (cap. 11): quadro de tarefas (kanban, arrastar e
soltar), cartão da tarefa (descrição em .md, histórico, chat, artefatos, trabalhos),
listas/formulários de projetos, equipes e executores, histórico de trabalhos com filtros, Caixa
de entrada da pessoa, configurações; HumanConnector (a pessoa como executor, por uma fila de
trabalhos); multilinguismo (dicionários JSON em `i18n/`).

## Instalação do ambiente de compilação

Os scripts `install_required` verificam e instalam as ferramentas de compilação: **.NET SDK 8+**,
**runtime ASP.NET Core 8.x**, **CMake**, **compilador C/C++**, e em seguida inicializam os
submódulos do git (se houver `.gitmodules`). Os scripts são idempotentes — não reinstalam o que
já está instalado; podem ser executados de novo para conferir.

> **Por que é preciso justamente o runtime 8.x, mesmo com o SDK 9/10 instalado.** O aplicativo é
> compilado para net8.0; um SDK mais novo o compila, mas executá-lo no runtime 9/10
> (roll-forward) não se pode: o script cliente do Blazor (`blazor.web.js`) é o da versão 8, e o
> servidor estaria na 9/10 — a interatividade (botões, eventos) para de funcionar em silêncio.
> Exemplo: o cask do brew `dotnet-sdk` no macOS hoje instala o .NET 10 — o script instala ao lado
> o runtime 8.0, e o aplicativo passa a rodar nele.

### Windows

Executar em um console comum (cmd ou PowerShell); podem surgir pedidos de confirmação do UAC. É
necessário o winget («App Installer» da Microsoft Store, presente por padrão no Windows 10/11).

```bat
cd AI2P_app
install_required.bat
```
Ele pede direitos de administrador. Mas não de imediato. O diálogo pode não aparecer em primeiro
plano — é preciso procurá-lo entre os aplicativos ativos.

O que ele faz:

1. **.NET SDK 8+** — se não houver a versão ≥ 8, instala o `Microsoft.DotNet.SDK.8` pelo winget;
   depois verifica o **runtime ASP.NET Core 8.x** e, na ausência dele, instala o
   `Microsoft.DotNet.AspNetCore.8`;
2. **CMake** — se não houver, instala o `Kitware.CMake`;
3. **Compilador C/C++** — procura o MSVC pelo vswhere; se não houver, instala o VS 2022 Build
   Tools com a carga de trabalho VCTools (um download grande, de vários GB);
4. **submódulos do git** — `git submodule update --init --recursive` (é pulado enquanto não
   houver `.gitmodules`).

Depois da instalação, abra um console **novo** (para que o PATH seja atualizado) e execute o
script mais uma vez — ele deve mostrar todos os `[OK]`.

### Linux (apt / dnf) e macOS (brew)

```sh
cd AI2P_app
chmod +x install_required.sh
./install_required.sh
```

O que ele faz: identifica o SO e o gerenciador de pacotes (apt-get / dnf / brew) e depois segue
os mesmos passos:

1. **.NET SDK 8+** — `dotnet-sdk-8.0` (no macOS: `brew install --cask dotnet-sdk`); depois o
   **runtime ASP.NET Core 8.x** — no Linux, o pacote `aspnetcore-runtime-8.0`; no macOS, o
   `dotnet-install.sh` oficial instala o runtime 8.0 ao lado do SDK existente, no mesmo diretório
   do dotnet (pode pedir sudo);
2. **CMake**;
3. **Compilador C/C++** — no Linux, `build-essential` (apt) ou `gcc gcc-c++ make` (dnf); no
   macOS, as Xcode Command Line Tools (`xcode-select --install` — aparece um diálogo; depois da
   instalação, execute o script de novo);
4. **submódulos do git** — como no Windows.

A instalação dos pacotes pode pedir a senha do sudo. No macOS é preciso ter o
[Homebrew](https://brew.sh/) instalado de antemão.

## Instalação de pacotes

As dependências se dividem em dois tipos (especificação, cap. 13.1):

1. **Binárias (NuGet)** — MudBlazor, Microsoft.Data.Sqlite, Serilog, Microsoft.Extensions.AI,
   OpenAI SDK, Anthropic.SDK, ModelContextProtocol e outras. Elas **não precisam ser instaladas
   à parte**: o `dotnet restore` (parte do `dotnet build`) as baixa automaticamente pelas
   referências dos `.csproj`.
2. **Pacotes-subprojetos (fontes)** em `packages/` — sqlite-vec, gigachat-adapter (candidatos,
   necessários nas etapas 3+). Eles já não têm scripts próprios: o `install_packages.bat` e o
   `install_packages.sh` foram removidos na T-65-S0 por serem desnecessários — nunca surgiu neles
   uma lista de pacotes em toda a história do projeto, e o trabalho que lhes era atribuído é
   feito por outros: as ferramentas de compilação são instaladas pelo `install_required.*`, e os
   pacotes dos modelos (ComfyUI, musubi-tuner, Python) pelo próprio programa, na instalação do
   modelo. Se um subprojeto for necessário, ele é adicionado com um comando:

```sh
git submodule add <endereço> packages/<nome>
```

Hoje o diretório `packages/` não existe mais no repositório (o vazio foi removido em T-207): todas
as dependências atuais são NuGet, e o `git submodule add` cria o diretório sozinho. O diretório
vazio `native/` — a preparação para os plugins C/C++ (CMake) — também foi removido.

## O idioma da saída dos scripts

Os scripts de compilação e instalação falam **em inglês por padrão** (T-65-S0). O script não é
duplicado por idioma: os textos foram levados para fora, para o diretório `i18n/`:

```
i18n/scripts.en.txt          as mensagens, idioma base
i18n/scripts.pt.txt          o mesmo conjunto de chaves em português
i18n/loc.ps1                 o carregador para o PowerShell
i18n/loc.sh                  o carregador para o sh POSIX
i18n/help/<script>.<idioma>.txt  o texto que o --help imprime
```

O idioma é escolhido por uma escada, e vence o primeiro valor não vazio:

1. a opção do script — `-Lang pt` (PowerShell) ou `--lang pt` (sh);
2. a variável de ambiente `AI2P_LANG`;
3. `en`.

A chave `"language"` do `config.json` NÃO é consultada de propósito: o idioma da interface do
aplicativo e o idioma do console de instalação são coisas diferentes.

A ajuda é impressa pelo `--help` (nos `.ps1`, pelo `-Help`) e vem de um arquivo à parte:

```powershell
install.cmd --help
install.cmd D:\AI2P -Lang pt
.\makeAsServise.cmd --help
.\MakePackage.cmd --help
```

```sh
./install.sh --help
./install.sh ~/ai/AI2P --lang pt
./makeAsServise.sh --help
./MakePackage.sh --help
```

Os invólucros `.cmd`/`.bat` não traduzem nada por conta própria e são escritos em **ASCII puro,
em inglês**: o `cmd.exe` decodifica o arquivo na codificação do console mas mantém a posição de
leitura em bytes, e um único caractere multibyte desloca a interpretação de tudo o que vem
depois (visto ao vivo na T-34-S0). Todo o texto, inclusive o `--help`, eles repassam ao `.ps1`
deles.

Um idioma novo são **dois arquivos e nenhuma linha de código**: uma cópia do `scripts.en.txt`
com os valores traduzidos e uma cópia dos arquivos necessários de `help/`. Os conjuntos de
chaves têm de bater um a um, e isso é vigiado por `T65S0Tests`.

A saída própria do `build.*`, do `buildRelease.*` e do `install_required.*` não é traduzida de
propósito (nível B da análise T-64-S0): eles são executados na máquina de compilação e já são
ingleses ou mistos. A ajuda `--help` eles têm.

## Compilação e execução

A compilação da solução inteira (por enquanto só C#; o C/C++ virá depois) com um
script:

```powershell
# Windows
cd AI2P_app
.\build.ps1              # Debug; variante: .\build.ps1 -Configuration Release
build.cmd                # o mesmo; executa com Enter a partir do explorador/FAR
                         # (no Windows, o .ps1 tem a associação «editar», não «executar»);
                         # variante: build.cmd Release
```

```sh
# Linux / macOS
cd AI2P_app
chmod +x build.sh
./build.sh               # Debug; variante: ./build.sh Release
```

Execução:

```sh
dotnet run --project src/AI2P.Server
```

O servidor levanta a interface em `http://localhost:5480` (a porta e o resto ficam no
`config.json`) e abre o navegador (`openBrowserOnStart`). O caminho da configuração pode ser
sobrescrito: `dotnet run --project src/AI2P.Server -- --config <caminho>`.

O `config.json` com as configurações padrão é copiado para o diretório de compilação **apenas se
ainda não estiver lá** — as edições locais não são apagadas por uma recompilação (especificação,
cap. 10).

## Distribuição de release em um diretório separado

A distribuição é um conjunto autossuficiente de arquivos que roda sem os fontes e sem o
`dotnet run`. Ela serve para que, ao lado da versão em desenvolvimento, permaneça uma versão
antiga funcionando.

```powershell
# Windows
cd AI2P_app
buildRelease.cmd                          # Release em builds\windows\release
buildRelease.cmd D:\AI2P_v1.45            # no diretório indicado
.\buildRelease.ps1 -Clean                 # limpar o diretório (exceto os dados) e refazer
.\buildRelease.ps1 -SelfContained         # com o runtime dentro: o ASP.NET Core 8 não é preciso na máquina
```

```sh
# Linux / macOS
cd AI2P_app
chmod +x buildRelease.sh
./buildRelease.sh                         # Release em builds/linux/release
./buildRelease.sh ~/ai/AI2P --clean
```

Até a T-285 esses scripts se chamavam `publish.cmd` / `publish.ps1` / `publish.sh` — o nome
mudou, o comportamento continua o mesmo.

O próprio diretório `builds` fica **dentro do diretório de trabalho** `AI2P_app`, ao lado de `AI2P.sln`
(T-131-S0); antes da 1.114 ele era criado um nível acima, fora do diretório de trabalho.

Entre `builds` e `release`/`releasefull` fica o **diretório do sistema operacional** (T-243):
`windows`, `linux` ou `macos`. O sistema é definido pelo RID da distribuição completa
(`-Runtime`/`--runtime`) e, na comum, por aquele em que a compilação acontece; é ele também que
determina quais scripts de instalação vão para a distribuição. O que é compilado para sistemas
diferentes já não se sobrescreve.

A execução da cópia distribuída é pelo `AI2P.Server.exe` (Windows) ou `./AI2P.Server` a partir do
diretório dela; o diretório atual do processo não tem importância.

**O servidor funciona com um usuário COMUM, e não com o root** (T-135). O diretório de
instalação no Linux e no macOS é `~/ai/AI2P`: os dados (`data/`), os logs (`logs/`), as
configurações e os segredos ficam dentro dele, o servidor não escreve fora do diretório do
usuário, e a porta (5480) não é privilegiada. Direitos de root só são necessários ao instalador
de dependências (`install_required.sh`) — é uma operação única, de nível de sistema. O arquivo
`secrets.json` o aplicativo fecha com as permissões `0600`: nele estão a senha do administrador
do servidor e as chaves das organizações.

O caminho com `~` é entendido tanto pelos scripts quanto pelo próprio aplicativo:
`./install.sh ~/ai/AI2P`, os diretórios do `config.json` e do formulário do servidor local
(`~/ai`), o diretório do projeto e as regras de segurança.

O que a distribuição **não copia** e, em uma nova execução, **não sobrescreve** — é transferido à
mão:

* `data/` — o banco e os arquivos dos projetos;
* `logs/` — se for necessário;
* `secrets.json` — as chaves de API (sem ele os modelos em nuvem ficam inativos; ao lado está o
  `secrets.example.json`);
* `config.json` — é criado a partir dos padrões apenas se ainda não estiver no diretório.

Duas versões ao mesmo tempo na mesma porta não funcionam (verificação de instância única,
especificação cap. 3): a segunda vê a porta ocupada, abre o navegador na primeira e termina. Para
manter as duas em execução, mude o `ui.port` no `config.json` da cópia distribuída.

## Pacote de instalação em um único arquivo (T-285)

A distribuição é um diretório; entregá-la a uma pessoa é incômodo. O `MakePackage` faz da
distribuição pronta **um único arquivo instalador**. O próprio `buildRelease` coloca o script na
distribuição, e ele deve ser executado **a partir do diretório da distribuição**; o resultado vai
para `../../packages` (ou seja, `builds/packages`).

```powershell
# Windows: é preciso o Inno Setup 6 (o install_required.bat o instala)
cd builds\windows\releasefull
.\MakePackage.cmd                    # -> ..\..\packages\AI2P_v_1_99_full_windows_x64.exe
cd ..\release
.\MakePackage.cmd                    # -> ..\..\packages\AI2P_v_1_99_windows_x64.exe
```

```sh
# Linux / macOS: é preciso o makeself (o install_required.sh o instala)
cd builds/linux/releasefull
./MakePackage.sh                     # -> ../../packages/AI2P_v_1_99_full_linux_x64.run
```

O nome do arquivo se forma sozinho, a partir do `version.json` da distribuição — não é preciso
perguntar o número do build `NN`:

| distribuição | Windows | Linux | macOS |
|---|---|---|---|
| `releasefull` | `AI2P_v_1_NN_full_windows_x64.exe` | `AI2P_v_1_NN_full_linux_x64.run` | `AI2P_v_1_NN_full_macos_arm64.run` |
| `release` | `AI2P_v_1_NN_windows_x64.exe` | `AI2P_v_1_NN_linux_x64.run` | `AI2P_v_1_NN_macos_arm64.run` |

As partes do nome vêm nesta ordem: `AI2P_v_` + o número da versão + `_full` na distribuição
completa + o sistema (`windows`, `linux`, `macos`) + a arquitetura (`x64`, `arm64`, `arm`, `x86`).
Na distribuição completa quem indica o sistema e a arquitetura é o seu runtime (`win-x64`,
`linux-arm64`, `osx-arm64`); na comum, a pasta do SO e a arquitetura do compilador atual
(T-234-S0).

A extensão define o tipo de instalador: no Windows é o Inno Setup (`.exe`); no Linux e no macOS é
um arquivo autoextraível do makeself (`.run`), que por dentro executa o mesmo `install.sh`. O
pacote é montado no sistema para o qual a distribuição foi compilada: não há como montar um
`.run` a partir do Windows, e vice-versa — o script diz isso com franqueza.

Os dados do usuário (`data/`, `logs/`, `secrets/`, `secrets.json`, o inventário
`installed.json`) não entram no pacote e, em uma instalação por cima da anterior, não são
tocados: o `config.json` é colocado apenas se não existir, e ao lado sempre é colocado o
`config.new.json` — o aplicativo os mescla no primeiro início (`ConfigMerge`).

**Onde ficam os arquivos de trabalho (T-287).** Junto ao programa — mas apenas se for possível
gravar no diretório dele. A instalação «para todos os usuários» (`C:\Program Files\AI2P`) é
fechada à gravação para um usuário comum, por isso nela o `config.json`, o `data/`, o `logs/` e o
`secrets/` ficam no diretório de dados comum do computador: no Windows `C:\ProgramData\AI2P`, no
Linux e no macOS `/var/lib/ai2p` (e, se ali também não for possível, no diretório de dados do
usuário). A regra vive em um único lugar — o `AppHome` (`src/AI2P.Server/AppHome.cs`) —, e os
scripts não a repetem: eles apenas procuram o `config.json` já criado nesses mesmos lugares. O
diretório também pode ser definido à mão: pela variável `AI2P_HOME` ou pela opção `--config`.
Detalhes em `doc/pt/man/install.md`.

## Estrutura da solução

```
AI2P.sln
src/
├── AI2P.Core/        — o núcleo: entidades (cap. 2), eventos (§ 6.4.3), contratos da API,
│                       a interface IAgentConnector (§ 7.2)
├── AI2P.Storage/     — SQLite (esquema § 6.4.2), event log, armazenamento de arquivos (§ 6.4.4),
│                       serviços: projetos, equipes, executores, tarefas, trabalhos, chat
├── AI2P.Connectors/  — HumanConnector + o orquestrador de início das tarefas; os conectores de IA são a etapa 2
├── AI2P.UI/          — componentes Blazor (MudBlazor): o esqueleto ao estilo VS Code (cap. 11), o quadro,
│                       o cartão da tarefa, a Caixa de entrada, o histórico, os catálogos; ApiClient, i18n
└── AI2P.Server/      — host ASP.NET Core: API HTTP (/api/...) + interface Blazor Server,
                        config.json, Serilog (logs .jsonl)
tests/     — AI2P.Tests: testes do armazenamento, do event log e do ciclo pessoa-executor
i18n/      — dicionários de localização (ru.json, en.json, pt.json, …) e os textos dos scripts
              de compilação e instalação (scripts.<idioma>.txt, loc.ps1, loc.sh,
              help/<script>.<idioma>.txt, T-65-S0) — copiados para o diretório de compilação
doc/       — a documentação junto ao código
```

## Testes

```sh
cd AI2P_app
dotnet test
```

## Armazenamento de dados

O diretório `storage.dataDir` do `config.json` (por padrão `./data`, ao lado do aplicativo):
`ai2p.db` (SQLite, WAL) + `projects/<slug>/tasks/<T-N>/description.md`, `artifacts/`, `.trash/`.
A fonte primária da verdade quanto ao histórico é o diário de eventos (tabela `events`); os logs
técnicos ficam em `logging.dir`, nos arquivos `ai2p-YYYYMMDD.jsonl`. Uma segunda instância do
aplicativo detecta a porta ocupada, abre o navegador na instância em funcionamento e termina
(especificação, cap. 3, princípio 5).

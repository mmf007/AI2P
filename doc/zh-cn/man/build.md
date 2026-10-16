# AI2P — 构建、部署包与仓库结构

> 仓库的主页是它根目录下的 `README.md`；给用户看的文档是[简要说明](../README.md)和
> [目录](../index.md)。这里的一切是给从源码构建 AI2P、或想提交自己修改的人看的。

一套让 AI 代理和人协同处理任务的系统：工作计划器、AI 代理编排器、经验积累器。在本地运行（Windows 10–11、Linux、macOS），UI 通过 web 浏览器访问。

文档分为两部分：**项目文档** — 在仓库根目录的 `doc/` 中（`doc/AI2P_ТЗ_v1.NN.md` 是系统现状的描述，技术规格的编号与应用版本一致；`doc/AI2P_release.md` 是版本、部署包和更新），以及**随程序一起发布的文档** — 在代码旁边的 `AI2P_app/doc/` 中：手册、模型文档（[`models/`](../models/README.md)）和导入方式文档（[`import/`](../import/README.md)），它们在 UI 中用「i」按钮打开。`AI2P_app/doc` 目录会被整个复制到发布的部署包中。

> 发布的文档中不放指向 `AI2P_app/doc` 之外文件的链接：它是在应用内部渲染的，而指向外面的
> 相对链接在那里哪儿也去不了（技术规格第 14 章）。

## 给参与者的简要说明

* **需要什么：** .NET 8 SDK（以及 ASP.NET Core 8.x 运行时）— 由 `install_required` 安装（见下文）。
* **如何构建：** `build.cmd`（Windows）或 `./build.sh`（Linux/macOS）— 用于开发的构建；
  `buildRelease.cmd` / `./buildRelease.sh` — 发布用的部署包，不需要源码就能运行。
* **如何运行测试：** 在 `AI2P_app` 目录中执行 `dotnet test`（详见[测试](#测试)一节）。
* **发现错误** — 请按「Bug report」表单提交 issue，**建议** — 按「Feature request」表单；
  空白 issue 已关闭。**问题** — 不要发 issue，请发到 GitHub Discussions（Q&A 分区）。
* **Pull request** 需事先商定才会接受：先提交描述修改的 issue，在其中达成一致 — 然后再提交
  带有该 issue 链接的 PR。未经讨论的 PR 可能不经审阅就被关闭。
* **许可证** — Apache-2.0（根目录下的 `LICENSE` 文件）。提交代码即表示您同意它按同一许可证
  分发。
* **漏洞** — 不要用公开的 issue：流程见[安全策略](SECURITY.md)。

## 安装构建环境

`install_required` 脚本会检查并安装构建工具：**.NET SDK 8+**、**ASP.NET Core 8.x 运行时**、**CMake**、**C/C++ 编译器**，然后初始化 git submodules（如果有 `.gitmodules`）。脚本是幂等的 — 已装好的不会重装；可以重复运行来做检查。

> **为什么正好需要 8.x 运行时，即使装了 SDK 9/10。** 应用是按 net8.0 构建的；更新的 SDK 能构建它，但不能在 9/10 运行时上运行（roll-forward）：Blazor 的客户端脚本（`blazor.web.js`）取的是 8 版，而服务器却跑在 9/10 上 — 交互性（按钮、事件）会悄无声息地失效。例子：macOS 上的 brew cask `dotnet-sdk` 现在装的是 .NET 10 — 脚本会在旁边补装 8.0 运行时，应用就会在它上面启动。

### Windows

在普通控制台（cmd 或 PowerShell）中运行；可能会出现 UAC 确认请求。需要 winget（Microsoft Store 中的「App Installer」，Windows 10/11 默认自带）。

```bat
cd AI2P_app
install_required.bat
```

脚本会请求管理员权限，但不是立刻请求。请求窗口可能不会出现在其他窗口的前面 — 请在任务栏的
已打开应用中找一找。

它做什么：

1. **.NET SDK 8+** — 如果没有 ≥ 8 的版本，就通过 winget 安装 `Microsoft.DotNet.SDK.8`；然后检查 **ASP.NET Core 8.x 运行时**，若不存在则安装 `Microsoft.DotNet.AspNetCore.8`；
2. **CMake** — 如果没有，就安装 `Kitware.CMake`；
3. **C/C++ 编译器** — 通过 vswhere 查找 MSVC；如果没有 — 安装带 VCTools 工作负载的 VS 2022 Build Tools（下载量很大，好几个 GB）；
4. **git submodules** — `git submodule update --init --recursive`（在没有 `.gitmodules` 之前跳过）。

安装之后请打开一个**新的**控制台（以便刷新 PATH）并再运行一次脚本 — 它应当显示全部 `[OK]`。

### Linux（apt / dnf）和 macOS（brew）

```sh
cd AI2P_app
chmod +x install_required.sh
./install_required.sh
```

它做什么：判断操作系统和包管理器（apt-get / dnf / brew），然后是同样的步骤：

1. **.NET SDK 8+** — `dotnet-sdk-8.0`（macOS：`brew install --cask dotnet-sdk`）；然后是 **ASP.NET Core 8.x 运行时** — Linux：`aspnetcore-runtime-8.0` 包；macOS：官方的 `dotnet-install.sh` 会把 8.0 运行时装到与已有 SDK 相同的 dotnet 目录中（可能会请求 sudo）；
2. **CMake**；
3. **C/C++ 编译器** — Linux：`build-essential`（apt）或 `gcc gcc-c++ make`（dnf）；macOS：Xcode Command Line Tools（`xcode-select --install` — 会弹出对话框，装完后重新运行脚本）；
4. **git submodules** — 与 Windows 相同。

安装软件包可能会请求 sudo 密码。macOS 上需要事先安装好 [Homebrew](https://brew.sh/)。

## 安装软件包

依赖分为两类（技术规格第 13.1 章）：

1. **二进制（NuGet）** — MudBlazor、Microsoft.Data.Sqlite、Serilog、Microsoft.Extensions.AI、OpenAI SDK、Anthropic.SDK、ModelContextProtocol 等等。它们**不需要单独安装**：`dotnet restore`（`dotnet build` 的一部分）会按 `.csproj` 中的引用自动下载它们。
2. **子项目包（源码）** — 目前没有。构建工具由 `install_required.*` 安装，模型所需的软件包（ComfyUI、musubi-tuner、Python）由程序本身在安装模型时安装。需要子项目时，用一条命令就能加上，`packages/` 目录会由它自己创建：

```sh
git submodule add <地址> packages/<名称>
```

## 脚本输出的语言

构建和安装脚本**默认说英语**。脚本不按语言复制多份：文本被移到了外面，放在 `i18n/`
目录中：

```
i18n/scripts.en.txt          消息，基础语言
i18n/scripts.ru.txt          同一套键的俄语版
i18n/loc.ps1                 PowerShell 的加载器
i18n/loc.sh                  POSIX sh 的加载器
i18n/help/<脚本>.<语言>.txt   --help 打印的文本
```

语言按阶梯选择，第一个非空的胜出：

1. 脚本参数 — `-Lang ru`（PowerShell）或 `--lang ru`（sh）；
2. 环境变量 `AI2P_LANG`；
3. `en`。

`config.json` 中的 `"language"` 键是有意**不**去问的：应用界面的语言和安装控制台的语言是两回事。

帮助由 `--help` 打印（`.ps1` 是 `-Help`），并且取自单独的文件：

```powershell
install.cmd --help
install.cmd D:\AI2P -Lang ru
.\makeAsServise.cmd --help
.\MakePackage.cmd --help
```

```sh
./install.sh --help
./install.sh ~/ai/AI2P --lang ru
./makeAsServise.sh --help
./MakePackage.sh --help
```

`.cmd`/`.bat` 包装脚本自己什么都不翻译，并且是用**纯 ASCII 英文**写的：`cmd.exe` 按控制台编码
解码文件，而读取位置是按字节保持的，一个多字节字符就会把之后所有内容的解析错开。包括 `--help`
在内的全部文本，它们都转交给自己的 `.ps1`。

新语言就是**两个文件、一行代码都不用写**：一份带翻译好的值的 `scripts.en.txt` 副本，加上所需
`help/` 文件的副本。各键的组成必须逐个对得上，这由测试看守。

`build.*`、`buildRelease.*` 和 `install_required.*` 自身的输出是有意不翻译的：它们是在构建机器上
运行的，本来就是英文的或混合的。`--help` 帮助它们是有的。

## 构建与运行

用一个脚本构建整个解决方案：

```powershell
# Windows
cd AI2P_app
.\build.ps1              # Debug；也可以：.\build.ps1 -Configuration Release
build.cmd                # 同样的事；在资源管理器/FAR 中按 Enter 即可运行
                         # （Windows 上 .ps1 的关联是「编辑」，不是「运行」）；
                         # 也可以：build.cmd Release
```

```sh
# Linux / macOS
cd AI2P_app
chmod +x build.sh
./build.sh               # Debug；也可以：./build.sh Release
```

运行：

```sh
dotnet run --project src/AI2P.Server
```

服务器会在 `http://localhost:5480` 上启动 UI（端口和其他设置在 `config.json` 中）并打开浏览器（`openBrowserOnStart`）。配置文件的路径可以覆盖：`dotnet run --project src/AI2P.Server -- --config <路径>`。

带默认设置的 `config.json` **只有在构建目录中还没有它的时候**才会被复制过去 — 本地的修改不会被重新构建抹掉（技术规格第 10 章）。

## 发布到单独目录的部署包

部署包是一套自足的文件，不需要源码、也不需要 `dotnet run` 就能运行。
它的用处是：在开发中的版本旁边保留一个能用的旧版本。

```powershell
# Windows
cd AI2P_app
buildRelease.cmd                          # Release 到 builds\windows\release
buildRelease.cmd D:\AI2P_v1.45            # 到指定目录
.\buildRelease.ps1 -Clean                 # 清空目录（数据除外）并重新发布
.\buildRelease.ps1 -SelfContained         # 运行时包含在内：机器上不需要 ASP.NET Core 8
```

```sh
# Linux / macOS
cd AI2P_app
chmod +x buildRelease.sh
./buildRelease.sh                         # Release 到 builds/linux/release
./buildRelease.sh ~/ai/AI2P --clean
```

`builds` 目录位于**工作目录内部** `AI2P_app`，与 `AI2P.sln` 并列。

`builds` 和 `release`/`releasefull` 之间还有一层**操作系统目录**：`windows`、`linux` 或 `macos`。
系统由完整部署包的 RID（`-Runtime`/`--runtime`）决定，而普通部署包则由构建所在的系统决定；
同样由它决定把哪些安装脚本放进部署包。为不同系统构建的产物不会互相覆盖。

运行发布出来的副本 — 在它的目录中执行 `AI2P.Server.exe`（Windows）或 `./AI2P.Server`；进程的
当前目录无关紧要。

**服务器以普通用户身份运行，而不是 root。** Linux 和 macOS 上的安装目录是 `~/ai/AI2P`：
数据（`data/`）、日志（`logs/`）、设置和密钥都在它里面，服务器不会往主目录之外写，而端口
（5480）是非特权的。root 权限只有依赖安装程序（`install_required.sh`）才需要 — 那是一次性的
系统级操作。`secrets.json` 文件被应用设为 `0600` 权限：里面有服务器管理员的密码和各组织的密钥。

带 `~` 的路径脚本和应用本身都认得：`./install.sh ~/ai/AI2P`、`config.json` 和本地服务器表单中的
目录（`~/ai`）、项目目录、安全规则。

部署包**不复制**、并且在再次运行时**不覆盖**的东西 — 需要手动搬迁：

* `data/` — 数据库和项目的文件；
* `logs/` — 按需；
* `secrets.json` — API 密钥（没有它云端模型不会启用；旁边放着 `secrets.example.json`）；
* `config.json` — 只有当目录中还没有它时才从默认值创建。

两个版本不能同时用同一个端口运行（单实例检查，技术规格第 3 章）：第二个会发现端口被占用，在第一个
上打开浏览器然后结束。想让两个都运行着，请修改所发布副本的 `config.json` 中的 `ui.port`。

## 单文件安装包

部署包是一个目录；交给人不方便。`MakePackage` 会把现成的部署包做成**一个安装程序文件**。这个脚本
是 `buildRelease` 自己放进部署包的，运行它必须**在部署包目录中**；结果会落在 `../../packages`
（也就是 `builds/packages`）中。

```powershell
# Windows：需要 Inno Setup 6（install_required.bat 会安装它）
cd builds\windows\releasefull
.\MakePackage.cmd                    # -> ..\..\packages\AI2P_v_1_99_full_windows_x64.exe
cd ..\release
.\MakePackage.cmd                    # -> ..\..\packages\AI2P_v_1_99_windows_x64.exe
```

```sh
# Linux / macOS：需要 makeself（install_required.sh 会安装它）
cd builds/linux/releasefull
./MakePackage.sh                     # -> ../../packages/AI2P_v_1_99_full_linux_x64.run
```

文件名会自己从部署包的 `version.json` 拼出来 — 不用去问构建号 `NN`：

| 部署包 | Windows | Linux | macOS |
|---|---|---|---|
| `releasefull` | `AI2P_v_1_NN_full_windows_x64.exe` | `AI2P_v_1_NN_full_linux_x64.run` | `AI2P_v_1_NN_full_macos_arm64.run` |
| `release` | `AI2P_v_1_NN_windows_x64.exe` | `AI2P_v_1_NN_linux_x64.run` | `AI2P_v_1_NN_macos_arm64.run` |

名称的各部分按此顺序排列：`AI2P_v_` + 版本号 + 完整部署包的 `_full` + 系统（`windows`、`linux`、
`macos`）+ 架构（`x64`、`arm64`、`arm`、`x86`）。完整部署包的系统和架构由其运行时给出
（`win-x64`、`linux-arm64`、`osx-arm64`），普通部署包则取自操作系统目录和当前编译器的架构。

扩展名决定安装程序的种类：Windows 上是 Inno Setup（`.exe`），Linux 和 macOS 上是 makeself 自解压
归档（`.run`），它内部运行的是同一个 `install.sh`。安装包要在与部署包相对应的系统上构建：在
Windows 上没有办法做出 `.run`，反过来也一样 — 脚本会诚实地说明这一点。

用户的数据（`data/`、`logs/`、`secrets/`、`secrets.json`、清单 `installed.json`）不会进入安装包，
覆盖安装到已有安装之上时也不会被动：`config.json` 只有在不存在时才放置，旁边总会放上
`config.new.json` — 应用会在首次启动时把它们合并（`ConfigMerge`）。

**工作文件放在哪里。** 在程序旁边 — 但仅当它的目录可写时。「为所有用户」的安装
（`C:\Program Files\AI2P`）对普通用户是禁止写入的，因此它的 `config.json`、`data/`、`logs/` 和
`secrets/` 放在计算机的公共数据目录中：Windows 上是 `C:\ProgramData\AI2P`，Linux 和 macOS 上是
`/var/lib/ai2p`（如果那里也不行 — 就放到用户的数据目录）。这条规则只存在于一个地方 —
`AppHome`（`src/AI2P.Server/AppHome.cs`），脚本并不重复它，只是在同样的位置查找已经建好的
`config.json`。目录也可以手动指定：用 `AI2P_HOME` 变量或 `--config` 参数。详情见
[安装 AI2P](install.md)。

## 解决方案结构

```
AI2P.sln
src/
├── AI2P.Core/        — 内核：实体（第 2 章）、事件（6.4.3 节）、API 契约、
│                       IAgentConnector 接口（7.2 节）、词典（Loc）
├── AI2P.Storage/     — SQLite（架构 6.4.2 节）、event log、文件存储（6.4.4 节）、
│                       各项服务：项目、团队、执行者、任务、作业、聊天
├── AI2P.Connectors/  — 执行者连接器（人、AI 模型、CLI 代理、媒体）
│                       以及任务启动编排器
├── AI2P.UI/          — Blazor 组件（MudBlazor）：VS Code 骨架（第 11 章）、看板、
│                       任务卡片、收件箱、历史、名录；ApiClient、i18n
└── AI2P.Server/      — ASP.NET Core host：HTTP API (/api/...) + Blazor Server UI，
                        config.json、Serilog（.jsonl 日志）
tests/     — AI2P.Tests：xUnit 测试
i18n/      — 本地化词典（<语言>.json）以及构建
              和安装脚本的文本（scripts.<语言>.txt、loc.ps1、loc.sh、
              help/<脚本>.<语言>.txt） — 会被复制到构建目录
doc/       — 代码旁边的文档
.github/   — issue 表单（ISSUE_TEMPLATE）
```

## 测试

所有测试都在一个 xUnit 项目 `tests/AI2P.Tests` 中：

```sh
cd AI2P_app
dotnet test
```

完整运行耗时较长（大约半小时甚至更久）。在修改过程中，更方便的做法是用筛选器只运行自己的类 —
筛选器中的名称必须是**类**名，而不是文件名：

```sh
dotnet test tests/AI2P.Tests/AI2P.Tests.csproj --filter "FullyQualifiedName~测试类名"
```

请核对通过的测试数量：一个匹配不到任何类的筛选器，会给出零个测试的「绿色」运行结果。
Pull request 需要完整运行全部通过；新的逻辑要带上自己的测试。

## 数据存储

`config.json` 中的 `storage.dataDir` 目录（默认是应用旁边的 `./data`）：
`ai2p.db`（SQLite，WAL） + `projects/<slug>/tasks/<T-N>/description.md`、`artifacts/`、`.trash/`。
历史的第一手事实来源是事件日志（`events` 表）；技术日志在 `logging.dir` 中，文件是
`ai2p-YYYYMMDD.jsonl`。应用的第二个实例会发现端口被占用，在正在运行的实例上打开浏览器然后结束
（技术规格第 3 章，原则 5）。

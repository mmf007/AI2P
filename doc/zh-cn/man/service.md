# 以操作系统服务方式运行 AI2P

默认情况下 AI2P 是一个**普通的控制台程序**：启动了 `AI2P.Server.exe` — 它就在运行，关掉窗口 —
它就停止。这样便于观察发生了什么，首次启动也是这样工作的。

但任务服务器通常需要**一直**运行：它维护作业队列、拉起 AI 代理、与集群中的其他服务器复制数据、
并监视各项计划。它没有理由要等到计算机的主人登录系统并打开窗口。为此，应用能够以**操作系统服务**
的方式工作。

服务名是 **`AI2P`**，在所有系统上都一样。

---

## 1. 如何把安装变成服务

服务是**手动创建、一次性**的 — 用安装目录中的 `makeAsServise` 脚本（就是放着 `AI2P.Server.exe`
的那个目录；脚本是安装程序放进去的）。

### Windows

**以管理员身份**打开控制台（只有管理员才能创建服务）并执行：

```powershell
cd D:\AI2P
.\makeAsServise.cmd
```

脚本会创建 `AI2P` 服务，把它设为随计算机启动自动运行，开启故障后重启，并立刻启动它。最后它会
打印出可以打开界面的地址。

参数：

| 参数 | 作用 |
|---|---|
| `-WhatIf` | 只显示将要做什么；不作任何更改 |
| `-NoStart` | 创建服务，但不启动 |
| `-Manual` | 手动启动服务，而不是随系统启动 |
| `-Remove` | 移除服务（不动文件和数据） |
| `-Target D:\AI2P` | 配置另一个安装，而不是运行脚本的这一个 |
| `-Account .\mike -Password ***` | 服务以某个用户身份运行，而不是 LocalSystem |
| `-UnprotectSecrets` | 解除组织密钥的 DPAPI 保护（见第 4 节） |
| `-Force` | 不顾警告，照做 |

### Linux

```sh
cd ~/ai/AI2P
./makeAsServise.sh
```

创建的是**用户级** systemd 单元 `~/.config/systemd/user/AI2P.service`，这不是偶然：AI2P 服务器
以普通用户身份运行，而不是 root，并且把自己的一切 — 数据、日志、设置和密钥库 — 都放在自己的
目录里。为了让这样的服务在没有人登录系统时也能起来，脚本会自己执行 `loginctl enable-linger`。

系统级单元（`/etc/systemd/system/AI2P.service`，需要 `sudo`）用 `--system` 参数创建。其余参数：
`--no-start`、`--manual`、`--remove`、`--target <目录>`。

### macOS

```sh
cd ~/ai/AI2P
./makeAsServise.sh
```

创建的是 launchd 任务 `~/Library/LaunchAgents/AI2P.plist`。参数相同，除了 `--system`。

---

## 2. 服务方式的启动与控制台启动有什么不同

程序是同一个，没有单独的「服务器版」构建。应用会**自己识别**它是怎样被启动的：在 Windows 的服务
管理器下和在 systemd 下它能认出自己，而 launchd 不提供这样的标志 — 那里的模式是在任务中直接用
`--service` 参数指定的。

不同之处一共只有三个：

* **启动时不打开浏览器。** 服务没有桌面，也就无处打开窗口 — 请自己按地址访问；
* **端口被占用就是错误。** 在被占用的端口上以控制台方式启动，会在已经运行的实例上打开浏览器并
  平静退出；这种情况下服务则会**以错误退出**，否则服务管理器会把安静的退出当成正常工作，什么也
  不说；
* **停止是按系统的命令进行的**，而不是按 Ctrl+C：应用来得及关闭数据库、卸载它启动的本地模型并
  结束复制会话。

其余的一切 — 数据、设置、端口、界面、集群 — 都不变。

想知道服务器是怎么起来的，可以直接在界面上看：**设置 → 基本**，应用版本旁边的**「启动」**一行。

---

## 3. 管理服务

**Windows**

```powershell
Get-Service AI2P            # 状态
Start-Service AI2P
Stop-Service AI2P
.\makeAsServise.ps1 -Remove # 移除服务
```

**Linux**（用户级单元）

```sh
systemctl --user status AI2P
systemctl --user start AI2P
systemctl --user stop AI2P
journalctl --user -u AI2P -f     # 服务器写了什么
./makeAsServise.sh --remove
```

**macOS**

```sh
launchctl list | grep AI2P
launchctl unload ~/Library/LaunchAgents/AI2P.plist
launchctl load   ~/Library/LaunchAgents/AI2P.plist
./makeAsServise.sh --remove
```

无论哪种模式，应用都把自己的日志写到安装目录的 `logs/` 里（`ai2p-<日期>.jsonl`） — 启动失败的
原因也在那里看得到。

---

## 4. Windows：服务以谁的身份运行

这是唯一一处选择真正重要的地方。

服务默认以 **LocalSystem** 身份运行 — 也就是计算机的账户。而**组织密钥**（模型的 API 密钥是用它
加密的）在 Windows 上受 **用户范围**的 DPAPI 机制保护：只有写入它的那个账户才能解密它。也就是说，
以 LocalSystem 运行的服务读不到 API 密钥，云端模型也就不再工作 — 从外面看这就像「模型有，可作业
不动」。

因此 `makeAsServise` 会查看 `secrets.json`，一旦在其中发现受保护的值，就**不会默默创建服务**，而
是让您选择：

* **`-Account <账户> -Password <密码>`** — 服务以同一个用户身份运行，密钥像以前一样对它可用。该
  账户需要**「作为服务登录」**权限（`secpol.msc` → 本地策略 → 用户权限分配）；没有它服务起不来，
  并会以错误 1069 说明这一点；
* **`-UnprotectSecrets`** — 解除 DPAPI：各组织的密钥会以普通 base64 留在 `secrets.json` 中，正如
  它们在 Linux 和 macOS 上的样子，保护它们的将是文件权限。原来的文件会作为
  `secrets.json.dpapi.bak` 保存在旁边；
* **`-Force`** — 就这样创建服务，明知 API 密钥对它不可用。

**登录 Claude CLI** 也是同样的道理：它属于用户的配置文件，以 LocalSystem 运行的服务看不到它。如果
这台服务器上有代理通过 Claude CLI 工作，服务就必须以同一个用户身份创建。

在 Linux 和 macOS 上根本没有这个选择：组织密钥在那里以普通 base64 存放，而服务本来就以同一个用户
身份运行。

### 如果工作文件不在程序旁边

**安装到系统的程序目录**（`C:\Program Files\AI2P`、`/usr`、`/opt`、`/Applications`）时，
`config.json`、`data`、`logs` 和 `secrets` 不是放在程序旁边，而是放在数据目录里：在 Windows 上是
`C:\ProgramData\AI2P`，在 Linux 和 macOS 上是 `/var/lib/ai2p`；如果那里也不能写，应用会转到用户的
数据目录。

`makeAsServise` 会自己考虑这一点：它找到工作用的 `config.json`，把 `serviceMode` 标记放到那里，也
在那里查找受保护的密钥 — 并**用 `--config` 参数把这个路径写给服务**。最后这一点很重要：服务以
另一个账户运行，而用户数据目录对它来说会是**它自己的**，没有明确路径的话它就会给自己弄出一个空
数据库来代替工作数据库。在脚本的输出中，这种情况表现为「工作文件：……」这一行。

目录也可以自己指定 — 用环境变量 `AI2P_HOME` 或者程序自身的 `--config` 参数；那样所有脚本都会用
它。

---

## 5. 版本更新

**不需要做任何特别的事。** 安装记得自己是服务方式的，三种更新途径都会考虑这一点：

* `install.cmd` / `install.ps1`（Windows）和 `./install.sh`（Linux、macOS） — 会在复制文件前自己
  停止服务，之后再把它启动回来；
* **安装包**（`AI2P_v_1_NN_windows_x64.exe`）做的是同样的事，而且在卸载程序时还会移除服务。

服务是**按系统**查找的 — 按服务管理器中的记录、systemd 单元或 launchd 任务 — 并且只找**正好指向
这个安装**的那一个：指向别的文件夹的别人的服务，任何脚本都不会去动。

Windows 下的安装包为此需要管理员权限：否则没法停止服务，它会诚实地事先说明，而不是之后卡在被占用
的文件上。

创建服务时会在安装的 `config.json` 中放上 `"serviceMode": true` 标记。这是一张**便条**，而不是
开关：它能挺过版本更新（安装程序不动工作用的 `config.json`），它的用处在于让应用和脚本能够说出
「这个安装被配置成了服务，而现在系统里没有这个服务」 — 比如当它被手动移除，或者目录被搬到了另一
台机器上的时候。

---

## 6. 常见问题

**服务在运行时，还能手动启动程序吗？**
可以，但要用另一个端口：同一个端口上两个实例是跑不起来的。在被占用的端口上以控制台方式启动，只会
在已经运行的服务器上打开浏览器然后结束。

**如果服务起不来，怎么看它在做什么？**
先看安装目录中的 `logs/ai2p-<日期>.jsonl` — 应用在任何模式下都往那里写。如果日志是空的，说明进程
没走到启动那一步：Windows 上请看事件日志和 `Start-Service` 的错误文本，Linux 上请看
`systemctl --user status AI2P`。

**我在 `config.json` 里改了端口 — 需要对服务做什么吗？**
不需要。服务启动的是同一个目录里的同一个程序，而设置是它在启动时读取的 — 重启服务就够了。

**我把安装搬到了另一个目录。**
请从新目录重新创建服务：`makeAsServise` 会发现服务指向别处，并要求用 `-Force` 参数确认重新指派。

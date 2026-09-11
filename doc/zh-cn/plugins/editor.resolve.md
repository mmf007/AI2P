# DaVinci Resolve（OTIO）

**插件代码：** `editor.resolve`
**类型：** 网关（`gateway`）
**交换格式：** OTIO —— `.otio`，普通 JSON，Academy Software Foundation 标准
**软件：** **我们不安装** —— 请指定已安装的 Resolve 路径
**功能：** 根据项目媒体库生成 `.otio` 时间线；**时间线由人工创建**

## 最重要的一点：免费版 Resolve 无法用脚本自动化

本文其余内容都由此推出，所以放在最前面。

DaVinci Resolve 确实有脚本 API（`DaVinciResolveScript`），但**免费版中它是关闭的**，而且自
**19.1 版（2024 年 11 月）**起彻底关闭：跨进程桥接完全不再接受连接。免费版里脚本只能在
Resolve 自带的内置控制台中手动运行。外部程序——对 Resolve 而言 AI2P 正是外部程序——在
Windows、Linux 和 macOS 上都连不上。自动化只存在于付费的 **Resolve Studio**。

因此工作链条是这样的：

1. 智能体填充项目媒体库（`media_add`、`media_list`）；
2. 智能体调用 `resolve_timeline_write`，**把文件 `ai2p_scenes.otio` 写入项目文件夹**；
3. **人工打开 Resolve，用鼠标导入该文件：** `File → Import → Timeline → OTIO`
   （旧版本为 `File → Import Timeline → AAF, EDL, XML…`，`.otio` 在同一个格式列表里）。

时间线**不会自动出现**。如果任务报告里写着「已在 Resolve 中创建时间线」，那是不实之词：创建的
只是一个文件，还需要人工导入。

我们从不写入 Resolve 工程：Resolve 工程是封闭的数据库（PostgreSQL 或其自有格式），不能从外部
插手。

## 智能体的动作

| 工具 | 作用 |
|---|---|
| `resolve_timeline_write` | 根据项目媒体库生成 `ai2p_scenes.otio` |

参数（全部可选）：

| 字段 | 含义 |
|---|---|
| `scene` | 只取该场景的素材 |
| `kind` | 只取该媒体类型（`video`、`audio`、`image`、`subtitle`、`project`） |
| `tag` | 只取带该标签的素材 |
| `file` | 文件放在哪里；默认是项目文件夹根目录下的 `ai2p_scenes.otio` |

片段顺序与 **`media_list` 打印的顺序完全相同**：场景、`order`、`take`、对象代码。看过那份清单
的人，在 Resolve 里必须看到一模一样的结果。

轨道分配：

| OTIO 轨道 | 轨道类型 | 放什么 |
|---|---|---|
| `V1` | `Video` | `video`、`image`、`project` |
| `A1` | `Audio` | `audio` |

时长未知的素材（静帧、字卡）取 **5 秒**，而不是零：长度为零的片段就是悄无声息丢失的镜头。

**字幕（`subtitle`）不会进入 `.otio`：** OTIO 没有对应的轨道。`.srt` 由人工在 Resolve 中单独
添加（`File → Import → Subtitle`）。

## 软件：装不了，只能指路

这个插件**故意**没有「安装」按钮。DaVinci Resolve 的安装包有 3–4 GB，需要在 Blackmagic Design
网站**填写注册表单**才能下载；不存在可以写进软件包目录的固定直链。我们无法替您下载，也不做这种
尝试。

做法：自行安装 Resolve，然后在插件表单中指定路径 ——
**设置 → 插件与 MCP → DaVinci Resolve (OTIO)**。程序文件和安装目录都可以：

| 系统 | 指定什么 |
|---|---|
| Windows | `C:\Program Files\Blackmagic Design\DaVinci Resolve\Resolve.exe`，或该目录 |
| Linux | `/opt/resolve/bin/resolve`，或 `/opt/resolve` |
| macOS | `/Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/MacOS/DaVinci Resolve` |

路径保存在**本服务器的 `config.json`** 中，从不复制：集群里的邻居把 Resolve 装在别处，而一半的
服务器根本没装。

只检查**可执行文件是否存在**：Resolve 不在命令行给出版本号，所以插件没有声明版本范围。

在指定路径之前、程序未被找到之前，插件在本服务器上的状态是**「正在查找软件」**，它的动作不会
发布给智能体：一个注定会回绝的工具只会白白浪费智能体的一轮。这就是「本服务器上未找到软件」的
回绝，在表单里填一行即可解决。

## 操作系统限制

| 系统 | 要点 |
|---|---|
| **Windows** | 免费版 Resolve 能读 `.mp4`/`.mov` 中的 H.264/H.265。AAC 音频**不受支持**（见下），任何版本、任何系统都不支持。程序通常在 `C:\Program Files\Blackmagic Design\DaVinci Resolve\Resolve.exe`。 |
| **Linux** | 免费构建**根本不解码 H.264 和 H.265**：其中不含授权编解码器。我们的生成结果（fal.ai、ComfyUI）恰恰是 `mp4/H.264`，因此不转码就会得到一条所有片段都显示「Media Offline」的时间线。可用的是 DNxHR（`.mov`）、ProRes（`.mov`）、CinemaDNG，声音只能用无压缩 WAV。程序在 `/opt/resolve/bin/resolve`。 |
| **macOS** | H.264/H.265 可读（解码器来自系统）。AAC 依旧不支持。 |
| **所有系统** | **连付费的 Resolve Studio 也不支持 AAC。** 我们的片子和生成结果中的声音（`aac`、`mp3`）必须转成 WAV。 |

另外：Resolve **自 18.5 版起**原生支持读取 OTIO，免费版也是如此。更老的版本根本没有 OTIO 导入
项——那就只能升级。

## 与「视频转换器（ffmpeg）」插件的配合

转码是**显式步骤**，而不是导出内部的隐形魔法：智能体知道自己在做什么，您也看得见结果。生成
`.otio` 之前的工作顺序：

1. `ffmpeg_to_edit` —— 把每个片段转成剪辑用中间格式（默认 MOV 容器中的 DNxHR HQ 加无压缩音频）；
2. `ffmpeg_extract_audio` —— 如果需要单独的音轨，把声音提取成 WAV；
3. `media_add` —— 把得到的文件放进媒体库（各自带有场景、顺序和条次）；
4. `resolve_timeline_write` —— 生成 `ai2p_scenes.otio`。

如果没有配置「视频转换器（ffmpeg）」插件，或本服务器上找不到 ffmpeg，第 1–2 步会以明确的文字
回绝（「本服务器上未找到插件所需的程序」）。这件事必须在生成时间线之前解决，而不是之后：`.otio`
即使素材不可用也照样生成——它只是引用文件——但届时没有东西能打开它。

在 Windows 和 macOS 上转码步骤不是必须的，但有好处：DNxHR 可以逐帧拖动，而长 GOP 的 H.264 会
一顿一顿。

## OTIO 不传递什么

OTIO 是**剪辑决定的交换格式**，不是工程格式。它传递：

* 片段集合以及对文件的引用；
* 片段顺序与时长；
* 轨道分配；
* 我们写在 `metadata.ai2p` 中的备注 —— 场景、条次、顺序、说明、媒体类型。

它**不传递**特效、转场、调色、变速、Fusion 合成和 Fairlight 混音。往返转换**有损**：把时间线
从 Resolve 导出为 `.otio` 再导回来，所有精修都会消失。

实用结论：我们的 `.otio` 只是**粗剪**（顺序与时长）。精修由人工在 Resolve 中完成，不能用我们的
文件二次覆盖对方的成果——请把它作为新的时间线导入。

## 插件记录的设置

**设置 → 插件与 MCP → DaVinci Resolve (OTIO) → 「配置」：**

| 设置 | 默认值 | 含义 |
|---|---|---|
| 每秒帧数 | 25 | 时间线帧率；片段时长也按它换算 |
| 画面宽度 | 1920 | 工程尺寸，写入 `metadata` |
| 画面高度 | 1080 | 同上 |

设置属于**组织记录**并会复制：约定按 25 帧剪辑，就是整个集群的约定。而程序路径相反，每台服务器
各有各的。

## 路径与安全

* **`.otio` 内的所有路径都是相对路径**（`target_url` 字段）。绝对路径会破坏可移植性：在邻近的
  机器上同一个片段位置不同。
* 片段路径**相对于输出文件所在目录**计算。
* 组织存储（`store:`）中的素材位于项目文件夹之外，因此导出时会**复制**到时间线旁边的
  `ai2p_media/` 子目录：否则引用要么是绝对路径，要么带 `..`。
* 智能体只写**自己的文件** `ai2p_scenes.otio`。Resolve 工程和任何人工文件绝不改动。
* 可以写入**项目文件夹**；若任务的安全规则开放了外部目录，也可写入这些目录。该限制是字面执行
  的：既检查文件自身的路径，也检查写进文件里的每一个路径。

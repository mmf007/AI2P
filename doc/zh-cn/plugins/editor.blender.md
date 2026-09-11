# Blender VSE —— 网关 `editor.blender`

在 **Blender 视频序列编辑器（VSE）** 里搭建剪辑，并把活儿交给 Blender 自己：我们生成一个
Python 脚本，Blender 无窗口执行它，自行保存 `.blend` 工程——或者直接渲染出成片。

## 为什么用脚本，而不是改 `.blend`

`.blend` 是 Blender **内部结构的二进制转储**，带 DNA 块（字段的类型与偏移写在文件自身里，
版本之间会变）。外部代码不往里写：那意味着每出一个 Blender 版本，都要重做一遍文件的解析与
拼装。

可行的路只有一条，而且正是官方的做法：

```
blender --background --factory-startup --python ai2p_timeline.py -- ai2p_timeline.blend
```

脚本由我们编写，Blender 执行它并自行保存工程。同一次执行也能渲染——只要把结果换成别的扩展名。

## 需要安装什么

| 什么 | 怎么装 |
|---|---|
| Blender 3.0 及以上 | 设置 → 插件与 MCP → 插件 `editor.blender` → **安装**（便携压缩包），或直接指定已安装的 `blender` 路径 |

插件先在 **PATH 里查找 `blender`**（软件包条目的 `system` 块，用 `--version` 校验，最低
3.0）：做 3D 的人本来就装了 Blender，旁边再下一份 386 MiB 没有意义。程序路径是**这台机器**
的值：它存放在服务器的 `config.json`（`plugins.editor.blender.path`），不在组织数据库里，
也不在集群内复制。插件描述则相反，是要复制的。

生成脚本**不需要**安装 Blender——只有执行时才用得上程序。

## 智能体的动作

| 工具 | 作用 |
|---|---|
| `blender_timeline_write` | 根据项目媒体库生成 `ai2p_timeline.py`：一条 VSE 视频轨和一条音频轨，顺序与 `media_list` 相同。筛选：`scene`、`kind`、`tag`。文件名由 `file` 指定 |
| `blender_render` | 在无窗口 Blender 中执行生成的脚本。`out` 以 `.blend` 结尾（默认）时得到带轨道的工程；其他扩展名则得到渲染成片（mp4，H.264 + AAC） |

工作顺序：先把素材放进媒体库（`media_add`），再 `blender_timeline_write`，再
`blender_render`。人工的 `.blend` 绝不会被改动：网关只写自己的 `ai2p_timeline.py` 和
`ai2p_timeline.blend`。

## 安全：为什么这个网关格外特殊

对 Blender 来说，按目录限制是**假的**：Blender 内部的 Python 能用 `open()` 打开任何文件，
AI2P 的任何安全规则都看不见这一点。真正起限制作用的不是路径，而是**脚本文本由我们编写**。
由此有三条规则，三条都已落实：

1. **没有来自 AI 的任意脚本。** 目录里没有、将来也不会有「执行这段 Python」的动作。动作就是
   我们的模板加上替换，模型只填参数：片段筛选条件和文件名。
2. **替换值都会转义**，按 Python 字符串字面量处理。带引号、换行或反斜杠的文件名仍然是值，
   不会变成代码。
3. **只执行我们自己的文件。** 渲染动作根本没有「执行什么」这个参数（清单里的
   `ownFileOnly`）；若有，智能体就会用写文件工具写出自己的 `.py`，再借我们的手运行它。

此外还有分支的通用规则：只能写入项目文件夹以及任务安全规则开放的目录；脚本内部的路径一律是
相对路径（从脚本自身所在目录计算），保存后的 `.blend` 也保持相对路径
（`save_as_mainfile(relative_remap=True)`）。

## 操作系统限制

各版 Blender 的**编解码器集合并不相同**，这是渲染前最需要知道的一点。

* **Windows。** 便携包 `blender-5.2.1-windows-x64.zip` 由我们的软件包安装（404 851 964
  字节，2026-09-03 用 HEAD 请求取得）。blender.org 官方构建自带含 H.264 与 AAC 的 FFmpeg，
  渲染成 mp4 开箱即用。已在 Blender 4.0.2 上实机验证。
* **Linux。** 我们没有软件包：发布里是 `blender-5.2.1-linux-x64.tar.xz`，安装它是另一件
  工作。程序由人自行安装并手动指定路径。**发行版仓库里的构建**（`apt install blender`、
  `dnf install blender`）通常链接系统 FFmpeg，而各发行版的编码器集合不同：H.264 和 AAC 可能
  完全没有。如果渲染因编解码器被拒，请改用 blender.org 的构建，或改用 Shotcut 网关、
  `tool.ffmpeg` 转换器来渲染。`--background` 下 Blender 不开窗口，因此不需要
  `QT_QPA_PLATFORM` 之类的变量。**未实机验证。**
* **macOS。** 我们没有软件包：发布里只有 `.dmg`，且 5.2.1 **仅有 arm64** ——Intel 机器需要更早
  的版本。路径需手动指定。**未实机验证。**
* **共通。** 4.4 之前 Blender 把剪辑片段叫 `sequences`，4.4 起叫 `strips`；我们的脚本两个
  名字都认，所以 3.x 和 5.x 都能用。

## 这个网关不做什么

* 不打开、不修改人工的 `.blend`——只动自己的。
* 不做转场、字幕和调色：VSE 作为剪辑软件偏弱（没有嵌套时间线、转场少、几乎没有音频处理）。
  若项目**本来就有 3D**、又希望剪辑和它放在同一个文件里，就用这个网关；若没有 3D，请选
  Shotcut/Kdenlive（`editor.shotcut`）——剪辑更强，渲染也更简单。
* 不转码素材。若各片段格式不一，请先过 `tool.ffmpeg` 转换器。

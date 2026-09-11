# 插件与 MCP：插件文档

存放插件文档的目录：每个**插件代码**一个文件（`tool.ffmpeg.md`）。文档由插件行上的**「i」**
按钮打开：**设置 → 插件与 MCP**。

插件由两部分构成：组织里的一条记录，加上数据目录中的清单文件 `plugins/<代码>/plugin.json`。
清单描述插件会做什么（它的动作，也就是 AI 智能体的工具）、外部程序从哪里获取、记录有哪些设置；
程序本身以及它的路径属于**这台计算机**，不参与复制。

## 本节目录

* [editor.blender](editor.blender.md) — Blender VSE —— 网关 `editor.blender`
* [editor.openshot](editor.openshot.md) — OpenShot — 网关 `editor.openshot`（备选）
* [editor.resolve](editor.resolve.md) — DaVinci Resolve（OTIO）
* [editor.shotcut](editor.shotcut.md) — Shotcut / Kdenlive（MLT XML）
* [tool.ffmpeg](tool.ffmpeg.md) — 视频转换器（ffmpeg）— 插件 `tool.ffmpeg`
* [trainer.musubi](trainer.musubi.md) — Musubi Tuner (LoRA) —— 插件 `trainer.musubi`

## 所有插件的共同点

**动作窄而具名。** 智能体的一个工具只做一件有名字的事，它的参数放在清单和记录设置里。「执行
命令行」这类动作是故意不存在的：那等于允许执行任意代码并写文件，任何安全规则都收不住。

**插件工具受安全规则约束**：每个动作都有自己的动作名录记录，由插件初始化时创建。没有这条记录的
工具根本不会被发布。

**路径受限。** 插件只能在项目目录内，以及任务安全规则开放的外部目录内读写——别处都不行。

**安装是本地的。** 插件的描述会复制到组织的所有服务器，而找到的程序及其路径保存在装有它的那台
计算机的 `config.json` 里。

## 如何为新插件添加文档

把文件 `<插件代码>.md` 放到这里——代码要和清单里给插件起的名字完全一致（`tool.ffmpeg`）。其他
语言也要建同名文件：各语言的文档集合必须一致。上面的目录不用手写——它由脚本
`test/t18s1/mktoc.py` 按目录内容生成，加完文件后运行一次即可。

这些文档里的外部地址请写**完整**，带上 `https://`：文档是用「i」按钮在应用页面内部打开的，相对
链接在那里是死链。

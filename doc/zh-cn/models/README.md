# AI2P 的模型：名录是怎样组织的

这是存放模型文档的目录：AI 模型名录中每条记录一个文件，文件名就是**模型名称**
（`Claude-Sonnet-5.md`、`Qwen3.8-27B-Local.md`）。用模型表单中的 **「i」** 按钮打开：
**设置 → 名录 → AI 模型**。

这里讲的是所有模型的共同内容：三种接入方式有什么区别、如何设置密钥、模型为什么会处于未启用
状态，以及如何建立自己的记录。

## 本节目录

**云端（需要 API 密钥）**

* [Chatterbox-TTS](Chatterbox-TTS.md) — 文本 + 语音样本 → 该音色的语音，技能 `audio-speech` 88
* [Claude-Fable-5](Claude-Fable-5.md)
* [Claude-Fable-5.1](Claude-Fable-5.1.md)
* [Claude-Haiku-4.5](Claude-Haiku-4.5.md)
* [Claude-Opus-5.0](Claude-Opus-5.0.md)
* [Claude-Opus-5.5](Claude-Opus-5.5.md)
* [Claude-Sonnet-5](Claude-Sonnet-5.md)
* [DeepSeek-V4-Flash](DeepSeek-V4-Flash.md)
* [DeepSeek-V4-Pro](DeepSeek-V4-Pro.md)
* [DeepSeek-V4.1-Flash](DeepSeek-V4.1-Flash.md)
* [ElevenLabs-Music](ElevenLabs-Music.md) — 文本 → 音乐和歌曲，版权已清理，技能 `audio-song` 89、`audio-music` 89
* [ElevenLabs-Music-v2.5](ElevenLabs-Music-v2.5.md) — 文本 → 版权清晰的音乐与歌曲，技能 `audio-song` 92、`audio-music` 92
* [ElevenLabs-TTS-v3](ElevenLabs-TTS-v3.md) — 文本 → 语音，技能 `audio-speech` 96
* [Gemini-3-Ultra](Gemini-3-Ultra.md)（**2026-09-11 已停用**）
* [Gemini-3.1-Pro](Gemini-3.1-Pro.md)（**2026-09-11 已停用**）
* [Gemini-3.7-Flash](Gemini-3.7-Flash.md)
* [Gemini-3.8-Flash](Gemini-3.8-Flash.md)
* [Gemini-Omni-Flash](Gemini-Omni-Flash.md) — 文本 → 带声音的视频，8 秒，技能 `video-generate` 91
* [GigaChat-3.5-Ultra](GigaChat-3.5-Ultra.md)
* [GLM-5.2](GLM-5.2.md)
* [GLM-5.3](GLM-5.3.md)
* [GPT-5.6-Sol](GPT-5.6-Sol.md)
* [GPT-5.6-Terra](GPT-5.6-Terra.md)
* [GPT-6-Astra](GPT-6-Astra.md)
* [GPT-6-Luna](GPT-6-Luna.md)
* [GPT-6-Sol](GPT-6-Sol.md)
* [GPT-Image-2](GPT-Image-2.md) — 文本 → 图像，按令牌计费，技能 `image-generate` 96、`image-text` 95、`image-photo` 94、`image-concept` 90
* [GPT-Image-2.5](GPT-Image-2.5.md) — 文本 → 图像，按令牌计费，技能 `image-generate` 97、`image-text` 96、`image-photo` 95、`image-concept` 92
* [Grok-4.6](Grok-4.6.md)
* [Grok-4.7](Grok-4.7.md)
* [Inkling-975B](Inkling-975B.md)
* [Kimi-K3](Kimi-K3.md)
* [Kling-3.0](Kling-3.0.md) — 图片 → 带声音的视频，最长 15 秒，技能 `video-animate` 93
* [Ling-3.0-Flash](Ling-3.0-Flash.md)
* [Meshy-7](Meshy-7.md) — 文本 → 可直接进游戏的 3D 模型，技能 `3d-generate` 87
* [Meshy-7.1](Meshy-7.1.md) — 文本 → 可直接用于游戏的三维模型，技能 `3d-generate` 89
* [MiniMax-H3-Max](MiniMax-H3-Max.md) — 文本 → 最长 15 秒的视频，技能 `video-generate` 92
* [MiniMax-M3](MiniMax-M3.md)
* [Mistral-Large-3](Mistral-Large-3.md)
* [Muse-Spark-1.2](Muse-Spark-1.2.md)
* [Muse-Spark-1.3](Muse-Spark-1.3.md)
* [Nano-Banana-Pro-Edit](Nano-Banana-Pro-Edit.md) — 图片加指令 → 改过的图片，技能 `image-edit` 98、`image-inpaint` 90
* [Nano-Banana-Pro](Nano-Banana-Pro.md) — 文本 → 最高 4K 的图像，画面中的文字最好，技能 `image-text` 98、`image-generate` 97、`image-photo` 96、`image-concept` 92
* [Nemotron-3-Ultra](Nemotron-3-Ultra.md)
* [Qwen3.8-Max](Qwen3.8-Max.md)
* [Qwen3.8-Omni-Flash](Qwen3.8-Omni-Flash.md)
* [Seedance-2.5-I2V](Seedance-2.5-I2V.md) — 图片 → 带声视频，技能 `video-animate` 95
* [Seedance-2.5](Seedance-2.5.md) — 文本 → 最长 30 秒的带声视频，技能 `video-generate` 97
* [Seedream-5-Flash](Seedream-5-Flash.md) — 文本 → 图像，技能 `image-generate` 92、`image-concept` 90、`image-photo` 90
* [Tripo-H3.1](Tripo-H3.1.md) — 图片 → 带 PBR 贴图的 3D 模型，技能 `3d-image` 92
* [Tripo-P2](Tripo-P2.md) — 图像 → 带四档 PBR 贴图的三维模型，技能 `3d-image` 94
* [Veo-3.1](Veo-3.1.md) — 文本 → 带声视频，4–8 秒，最高 4K，技能 `video-generate` 93
* [Wan-3.0-Prime](Wan-3.0-Prime.md) — 文本 → 最长 30 秒的带声视频，技能 `video-generate` 94
* [YandexGPT-5.1-Pro](YandexGPT-5.1-Pro.md)
* [Zonos-2-TTS](Zonos-2-TTS.md) — 文本 + 语音样本 → 该音色的语音（样本必填），技能 `audio-speech` 86

**通过 Claude CLI（按订阅，无需密钥）**

* [Claude-Fable-5_cli](Claude-Fable-5_cli.md)
* [Claude-Opus-5.0_cli](Claude-Opus-5.0_cli.md)
* [Claude-Sonnet-5_cli](Claude-Sonnet-5_cli.md)
* [Claude-Fable-5.1_cli](Claude-Fable-5.1_cli.md)
* [Claude-Haiku-4.5_cli](Claude-Haiku-4.5_cli.md)
* [Claude-Opus-5.5_cli](Claude-Opus-5.5_cli.md)

**本地（由您的计算机计算）**

* [Muse-Glimmer-30B-Local](Muse-Glimmer-30B-Local.md) — 文本和代码，技能评分 73–80
* [Qwen3.5-4B-Local](Qwen3.5-4B-Local.md) — 用于提词者角色的轻量模型：3 GB 显存，技能评分 62–74
* [Qwen3.6-27B-Local](Qwen3.6-27B-Local.md) — 代码和文本，技能评分 75–82
* [Qwen3.6-35B-A3B-Local](Qwen3.6-35B-A3B-Local.md) — 代码和文本，技能评分 72–80
* [Qwen3.8-27B-Local](Qwen3.8-27B-Local.md) — 代码和文本，技能评分 80–87

**本地媒体模型（视频、图像、声音和 3D）**

* [ACE-Step-1.5-XL-Base](ACE-Step-1.5-XL-Base.md) — 文本 → 音乐和歌曲，技能 `audio-song` 86、`audio-music` 87
* [ACE-Step-1.5-XL-SFT](ACE-Step-1.5-XL-SFT.md) — 文本 → 音乐与歌曲，技能 `audio-song` 90、`audio-music` 89
* [ACE-Step-1.5-XL-Turbo](ACE-Step-1.5-XL-Turbo.md) — 文本 → 音乐与歌曲，技能 `audio-song` 84、`audio-music` 86
* [FLUX.2-dev](FLUX.2-dev.md) — 文本 → 图像，技能 `image-generate` 94、`image-photo` 93、 `image-concept` 92、`image-text` 90
* [FLUX.2-klein-4B-Edit](FLUX.2-klein-4B-Edit.md) — 图片 + 指示 → 图像，技能 `image-edit` 86、 `image-inpaint` 83、`image-generate` 80、`image-text` 78
* [FLUX.2-klein-4B](FLUX.2-klein-4B.md) — 文本 → 图像，技能 `image-generate` 88、`image-photo` 87、 `image-concept` 86、`image-text` 80
* [Hunyuan3D-2.1](Hunyuan3D-2.1.md) — 图像 → 3D 模型，技能 `3d-image` 86
* [HunyuanVideo-1.5-720p-T2V](HunyuanVideo-1.5-720p-T2V.md) — 文本 → 视频，技能 `video-generate` 79
* [Kandinsky-5.0-I2V-Lite-5s](Kandinsky-5.0-I2V-Lite-5s.md) — 图像 + 文本 → 视频（5 秒），技能 `video-animate` 78、 `video-generate` 74
* [Kandinsky-5.0-Image-Lite](Kandinsky-5.0-Image-Lite.md) — 文本 → 图像，技能 `image-text` 88、`image-generate` 84、 `image-concept` 83、`image-photo` 82
* [Kandinsky-5.0-T2V-Lite-distil16-10s](Kandinsky-5.0-T2V-Lite-distil16-10s.md) — 文本 → 视频，技能 `video-generate` 70
* [Kandinsky-5.0-T2V-Lite-distil16-5s](Kandinsky-5.0-T2V-Lite-distil16-5s.md) — 文本 → 视频，技能 `video-generate` 70
* [Kandinsky-5.0-T2V-Lite-nocfg-10s](Kandinsky-5.0-T2V-Lite-nocfg-10s.md) — 文本 → 视频，技能 `video-generate` 74
* [Kandinsky-5.0-T2V-Lite-nocfg-5s](Kandinsky-5.0-T2V-Lite-nocfg-5s.md) — 文本 → 视频，技能 `video-generate` 74
* [Kandinsky-5.0-T2V-Lite-sft-10s](Kandinsky-5.0-T2V-Lite-sft-10s.md) — 文本 → 视频，技能 `video-generate` 76
* [Kandinsky-5.0-T2V-Lite-sft-5s](Kandinsky-5.0-T2V-Lite-sft-5s.md) — 文本 → 视频（5 秒），技能 `video-generate` 76、`video-animate` 72
* [LTX-2.5](LTX-2.5.md) — 文本 → 带声音的视频，技能 `video-generate` 85
* [Qwen-Image-2512](Qwen-Image-2512.md) — 文本 → 图像，技能 `image-text` 94、`image-photo` 91、 `image-generate` 90、`image-concept` 85
* [Qwen-Image-Edit-2511](Qwen-Image-Edit-2511.md) — 图片 + 指令 → 改过的图片，技能 `image-edit` 90、 `image-text` 90、`image-inpaint` 85、`image-generate` 82
* [SD-3.5-Large](SD-3.5-Large.md) — 文本 → 图像，技能 `image-generate` 80、`image-photo` 79、 `image-concept` 78、`image-text` 70
* [SDXL-1.0](SDXL-1.0.md) — 文本 → 图像，技能 `image-generate` 70、`image-concept` 70、 `image-photo` 68
* [TRELLIS-2](TRELLIS-2.md) — 图像 → 带颜色的 3D 模型，技能 `3d-image` 85
* [TripoSplat](TripoSplat.md) — 图像 → 高斯泼溅，技能 `3d-image` 80
* [Wan-2.2-I2V-A14B](Wan-2.2-I2V-A14B.md) — 图片 + 文本 → 视频，技能 `video-animate` 80、`video-generate` 74 — **已于 2026-09-23 停用**（2.2 之后出现了 Wan 2.6、2.7 与 3.0）
* [Wan-2.2-T2V-A14B](Wan-2.2-T2V-A14B.md) — 文本 → 视频，技能 `video-generate` 80 — **已于 2026-09-23 停用**（2.2 之后出现了 Wan 2.6、2.7 与 3.0）
* [Z-Image-Turbo](Z-Image-Turbo.md) — 文本 → 图像，技能 `image-generate` 85、`image-photo` 84、 `image-concept` 82、`image-text` 78

## 三种接入方式

| | 云端 | 本地 | 通过 CLI |
|---|---|---|---|
| 在哪里计算 | 提供商的服务器 | **您的计算机** | 提供商的服务器 |
| 费用 | 按令牌 | 无 | **按订阅**，计费中为 0 |
| 需要什么 | **API 密钥** | **下载文件**（数十 GB） | 本机上执行过 `claude login` |
| 配置档中的 `provider` | `anthropic`、`openai-compatible` | `openai-compatible` | `anthropic` + `transport: cli` |
| `baseUrl` | 提供商的地址 | `http://localhost:<端口>/v1` | 不使用 |
| 代理的工具 | AI2P 的工具 | AI2P 的工具 | **CLI 自带的工具** |
| 数据是否外发 | 是 | **否** | 是 |

* **云端** — 名录中的大多数记录。快、质量好、收费；需要提供商的密钥和互联网连接。例如：
  Claude-Sonnet-5、GPT-5.6-Sol、DeepSeek-V4-Pro。
* **本地**（名称以 `-Local` 结尾） — 权重放在您的磁盘上，由您的显卡计算，什么都不会外发。
  不需要密钥，但需要空间、内存以及下载时的耐心。例如：Qwen3.8-27B-Local、
  Muse-Glimmer-30B-Local。本地服务器（`llama-server`）由 AI2P 在团队开始工作时自行启动、
  在停止时关闭 — **只针对自己的进程**，同一端口上别人的进程不会被触动。
* **通过 CLI**（名称以 `_cli` 结尾） — 同样是云端模型，但通过 headless 模式的 Claude Code
  启动，并按**订阅**而不是按令牌付费。**不需要 API 密钥**，需要已经执行过 `claude login`。
  一个重要区别：代理直接在项目文件夹中用**自己的**工具工作，AI2P 的工具不会提供给它。
  例如：Claude-Sonnet-5_cli、Claude-Fable-5_cli。

## 如何设置密钥

1. **设置 → 名录 → AI 模型** — 在列表中找到那条记录。
2. 模型表单中的 **「设置 API 密钥」** 按钮（如果已有密钥，它会叫「更新 API 密钥」；旧值
   永远不会显示）。
3. 粘贴提供商发放的值并保存。

**同一提供商的所有模型共用一个密钥。** 每条记录的配置档中都有「密钥库中的密钥（引用）」
字段 — 例如 `anthropic.apiKey` 或 `openrouter.apiKey`。所有使用同一引用的记录共用一个密钥：
为 Claude-Sonnet-5 输入之后，Opus、Haiku 也一并可用。具体到哪里获取密钥，写在各个模型文档的
**「如何获取密钥」**一节中。

密钥**属于组织**：它用组织的密钥加密，并复制到组织的所有服务器上，因此不需要在每台计算机上
重复输入。组织密钥本身只放在自己的计算机上（`secrets.json`），永远不会被复制。

## 密钥在磁盘上的位置

密钥也可以用**文件**指定 — 这是第二种方式，适用于没有界面的安装以及迁移旧密钥。文件放在
**`config.json` 旁边的 `secrets/`** 子目录中（也就是应用的安装目录），每个引用一个 json：

```
secrets/anthropic.apikey.json
{ "ref": "anthropic.apiKey", "value": "sk-ant-..." }
```

具体某个模型对应的该文件的完整路径，会显示在它的表单中，就是 **「本机上的密钥文件」** 那一行 —
和设置密钥的按钮在一起。该子目录在应用首次启动时创建，里面有一份说明 `readme.txt`。

**在表单中输入的密钥不会在这里生成文件**，这不是错误：它会加密后进入组织的数据库，以便到达
其他服务器。这里出现文件的是文件存储型密钥 — 启动时从旧的 `secrets.json` 迁移过来的，以及
手动写入的。如果这样的文件存在，在表单中输入新值也会同时更新它。

密钥的查找顺序：**组织数据库 → `secrets/` → `secrets.json` → 环境变量**
（`anthropic.apiKey` → `ANTHROPIC_API_KEY`）。`secrets/` 子目录不会进入复制、部署包或安装
清单 — 版本更新时它保持原封不动。

## 模型为什么可能未启用

「已启用」标记决定挑选执行者时是否会提供该模型。并不是任何时候都能打开它，这是规则，而不是
错误：

* **没有 API 密钥的云端模型不能启用** — 表单里就是这样写的：「没有 API 密钥：设置密钥后模型
  会立即启用」，通过 API 强行打开该标记也会被拒绝；
* **没有下载文件的本地模型不能启用** — 「模型未安装：安装完成后会自动启用」。

道理很简单：否则执行者会被算作就绪，作业会派给它，然后在启动阶段就失败 — 得到的是一个含糊的
网络错误，而不是清楚的「没有密钥」。密钥一旦输入或安装一旦完成，该标记就会自行打开。

由此也可以推出：**`_cli` 模型是立即可用的**：它不需要密钥，也没有文件要下载。唯一的前提是
运行 AI2P 的那个用户是否安装了 Claude Code；AI2P 不会预先检查这一点。

## 如何添加自己的记录

发行包自带的名录会随新版本增补，但谁也不妨碍您建立自己的模型 — 例如同一个本地 Qwen 的更小
量化版本，或者名录中没有的某个提供商的模型。

1. **设置 → 名录 → AI 模型 → 「添加模型」**。
2. **名称** — 按 `<通用名称>-<模型名称>` 的格式：`Claude-Opus-5.0`、`DeepSeek-V4-Pro`。
   本地模型请加上 `-Local` 后缀。名称必须唯一。
3. **部署位置** — 「云端（提供商 API）」或「本地（在本机运行）」。
4. **「配置档…」** — 提供商（`anthropic`、`openai-compatible`、`comfyui`）、连接方式
   （API 或 CLI 代理）、**模型（提供商处的 id）**、Base URL、密钥库中的密钥引用、本地服务器
   的启动命令和参数（JSON，例如 `{"maxTokens": 32768}`）。
5. **「能力声明…」** — 输入和输出格式、带 0–100 评分的技能、限额（上下文、最大响应）以及
   每 100 万令牌的价格。执行者自动挑选依据的就是这份声明：**技能名录中没有的技能，谁也看不到** —
   代号要从表单中的列表里选，不要自己编。
6. 设置密钥（云端）或点击 **「安装」**（本地） — 然后打开「已启用」标记。
7. **自己模型的文档**也请放到这里：`doc/zh-cn/models/<模型名称>.md`。如果没有文档，「i」
   按钮会显示提示，告诉您应该把它放到哪个完整路径。

自己建立的记录会被标记为 **「自定义」**，与发行包自带的记录不同，它们可以被整条删除。
发行包自带的记录不能删除 — 如果不需要某个模型，取消它的「已启用」标记即可。

## 模型文档中没有什么

名录中的标识符、价格和限额已与公开的模型目录和市场综述核对过，但**没有任何一个标识符经过
向提供商的真实调用验证**：本项目没有所有这些 API 的密钥。因此每份云端模型文档中都有同一句话 —
在下达第一个作业之前，请用向提供商 API 发送 `GET /models` 的方式核对 id。市场变化很快
（大约每两天一个模型），名录记录会悄无声息地过时：变更过的 id 会在执行者那里直接给出 404，
而错误的价格则会导致计费金额不对。

本地模型的硬件要求是**按权重体积估算**的；没有在真实硬件上做过实测。

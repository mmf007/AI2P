# LTX-2.5

**部署位置:** 本地（在这台计算机上运行）
**接入方式:** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，模型 `ltx_2_5_distilled`
**API 密钥:** 不需要 —— 服务器在本地启动，无需授权
**功能:** 文本 → **带声音的**视频，技能 `video-generate` 85

Lightricks 的视频模型，220 亿参数，蒸馏版本。这是
**唯一一个一遍就把视频和声音都画出来的开放模型**：音轨作为自己的潜变量与视频并排计算，
并进入同一个 `.mp4`。名录中其他所有本地模型的声音都得单独叠上去。

论画面质量它强过 Wan 2.2 和 HunyuanVideo 1.5，但也比它们重：22B 对 14B，
而且权重受协议保护（见下 —— 文件得手动放进去）。

它通过 **ComfyUI** 工作：AI2P 把它作为本地服务器启动，发送 workflow，
再把做好的 `.mp4` 取回任务的产物中。

## 安装：文件得手动放进去

权重仓库**受协议保护**（gated）：HuggingFace 只把文件发给接受了许可证的人，
而且要用个人令牌。AI2P 的下载器是不带令牌去的，会被拒绝（2026.08.27 用请求验证过），
所以步骤是这样：

1. 打开 <https://huggingface.co/Lightricks/LTX-2.5>，登录您的 HuggingFace 账户
   并接受许可证条款；
2. 下载下表中的四个文件；
3. 把它们放进模型仓库（`storage.modelsRepo`）的 `LTX-2.5` 子目录 ——
   **平铺放，不要嵌套文件夹**，文件名不要改；
4. 按模型表单中的**「安装」**：**设置 → 名录 → 模型 →
   LTX-2.5 →「安装」**。安装程序会看到大小吻合，于是只装
   ComfyUI 包，而模型就算装好了。

| 文件 | 大小 | 放到哪里 |
|---|---|---|
| `ltx-2.5-22b-distilled-transformer-comfy-int8-convrot.safetensors` | ~20 GiB | `diffusion_models` |
| `gemma4-12b-with-proj-ltx-2.5-comfy-int8-convrot.safetensors` | ~14,3 GiB | `text_encoders` |
| `ltx-2.5-video-vae-bf16.safetensors` | ~1,4 GiB | `vae` |
| `ltx-2.5-audio-vae-bf16.safetensors` | ~348 MiB | `vae` |

**总共约 38,7 GB。** 文件没有就位之前，模型**不可能处于已启用状态** ——
应用启动时也会检查这一点。

## 许可证

| | |
|---|---|
| 模型权重 | **LTX-2 Community License Agreement**（不是 Apache，也不是 MIT） |
| 商业使用 | 在须本人接受的协议条件下允许 |
| 必须遵守什么 | 在 HuggingFace 上接受许可证、保留声明；条款对大规模部署有限制 |
| 许可证原文 | <https://github.com/Lightricks/LTX-2/blob/main/LICENSE.md> |
| 模型卡片 | <https://huggingface.co/Lightricks/LTX-2.5> |
| 生成费用 | 无 —— 由您的显卡计算 |

这**不是**自由许可证：仓库受协议保护，在接受之前文件根本拿不到。
在商业发布之前请把条款全文读一遍。许可证已于 2026.08.27 按模型卡片核对
（`license_name: ltx-2-community-license-agreement`）。

另外：**ComfyUI 本身以 GPL-3.0 发布**。AI2P 把它作为外部程序启动，通过 HTTP 与它通信，
因此这个许可证不会传染到您的产品上 —— 但如果您把内含 ComfyUI 的整套东西交给别人，
GPL 的条款就适用于它。

## 硬件要求

| | |
|---|---|
| 显卡 | NVIDIA，**24 GB 显存起**（32 GB 会很宽裕） |
| NVIDIA 驱动 | **580 或更新** —— 驱动太旧时 torch 会无声崩溃 |
| 磁盘空间 | ~41 GB（权重 + ComfyUI 包） |
| 内存 | 32 GB 起 |
| 操作系统 | Windows（ComfyUI 便携版）；在 Linux 上 ComfyUI 需要手动安装 |

权重文件取的是 `int8-convrot` 这一版 —— 正是官方 ComfyUI 模板里用的那一版：
它比 bf16 轻一半（bf16 光是变换器就占 39 GiB）。

## 要等多久

模型是蒸馏过的，一共只有八步，所以 1280×704、121 帧的片子
（24 帧每秒时是 5 秒）算起来比二十步的 Wan 2.2 **还快** —— 几分钟到几十分钟。
配置档中作业超时设为 240 分钟（`params.timeoutMinutes`）。

## 生成参数

| 参数 | 默认值 | 含义 |
|---|---|---|
| `width` / `height` | 1280 × 704 | 画面分辨率；两个数都是 32 的倍数 —— 潜变量节点要求这样 |
| `length` | 121 | 片子的帧数；24 帧每秒，也就是 5 秒 |
| `steps` | 8 | **仅供参考**：步数由模板中的 sigma 决定，见下 |
| `negative` | 空 | 负面提示词 |
| `timeoutMinutes` | 240 | 等待结果多久 |

**关于步数。** 蒸馏模型的噪声调度是直接在模板里用一串数字给出的（`ManualSigmas` 节点），
而不是用步数：九个值 = 八步。改配置档里的 `steps` 对生成没有影响，
这个值只是给作业摘要看的；要改调度，请改组织目录下
`models/workflow_….json` 文件中的 `sigmas`。

**我们的模板相对官方少了什么。** 官方 ComfyUI 模板是两遍计算的：第一遍用一半尺寸，
第二遍用 `LTXVLatentUpsampler` 节点把分辨率提上来。AI2P 的连接器不会对
`width`/`height` 做算术，所以「一半」无从取得，而作业摘要给人看的尺寸也会不对 ——
我们这里是一遍直接按目标分辨率算。用另一个语言模型改进提示词
（`TextGenerateLTX2Prompt`）也关掉了：它会再拉一个权重文件，
而且会不声不响地改写任务的文本。

## LoRA 训练

适配器可以正常**应用**：`LoraLoaderModelOnly` 节点会在运行时插入图中，
文件取自 `<模型仓库>/loras`。LTX 有一套丰富的现成 LoRA 生态 ——
运镜、风格和控制类适配器由 Lightricks 自己发布。

而**从 AI2P 训练适配器是不行的**，这一点在记录中如实标明了：
AI2P 用来为其他本地模型训练 LoRA 的
[musubi-tuner](https://github.com/kohya-ss/musubi-tuner) 根本没有 LTX 的训练器 ——
有 Wan、HunyuanVideo、Kandinsky、Qwen-Image、Z-Image，就是没有 LTX。所以配置档里
训练的方式写的是「外部」：「训练」按钮会给出一个说得清楚的拒绝并附上链接，
而不是挂上半小时再失败。

适配器可以单独训练，用 Lightricks 的官方训练器
（<https://github.com/Lightricks/LTX-Video-Trainer>），再把做好的
`.safetensors` 文件放到 `<模型仓库>/loras` —— AI2P 会从那里把它取用。

## 常见错误

* **安装在下载时给出拒绝** —— 仓库受协议保护，文件得手动搬过来（见「安装」）。
* **手动放好之后仍显示「模型未安装」** —— 请检查文件名，以及它们是不是直接躺在
  `LTX-2.5` 组目录里，而不是在嵌套文件夹里；大小必须和表格一个字节不差地吻合。
* **片子没有声音** —— 声音来自同一遍计算；如果没有，请检查模板里
  `LTXVEmptyLatentAudio` 和 `LTXVAudioVAEDecode` 节点还在不在。
* **显存不足** —— 请调小 `width`/`height` 或 `length`。

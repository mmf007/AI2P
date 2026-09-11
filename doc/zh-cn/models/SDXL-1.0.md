# SDXL-1.0

**部署位置:** 本地（在这台计算机上运行）
**接入方式:** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，模型 `sdxl_base_1_0`
**API 密钥:** 不需要 —— 服务器在本地启动，无需授权
**功能:** 文本 → 图像，技能 `image-generate` 70、`image-concept` 70、
`image-photo` 68

Stability AI 的 Stable Diffusion XL 1.0 —— **名录里要求最低的一条记录**：
一个文件 6,9 GB，显存 8 GB。它就是作为最低一档设的：质量不如所有邻居，
而且**不会在画面里写字**，但在 FLUX.2、Qwen-Image 和 Kandinsky 根本起不来的地方
它照样能跑。

支持它的第二个理由：为 SDXL 写的 LoRA 适配器比其他所有模型加起来还多，
而且都能在这里直接用，不用改。

它通过 **ComfyUI** 工作：AI2P 把它作为本地服务器启动，发送 workflow，
再把做好的 `.png` 取回任务的产物中。

## 不需要密钥，需要安装

一切都用模型表单中的**「安装」**按钮完成：**设置 → 名录 →
模型 → SDXL-1.0 →「安装」**。会安装：

* **ComfyUI 包**（约 2,1 GB）—— 自带 Python 的便携版；
* 模型仓库（`storage.modelsRepo`）中的**一个权重文件**，组 `SDXL-1.0`：

| 文件 | 大小 | 放到哪里 |
|---|---|---|
| `sd_xl_base_1.0.safetensors` | ~6,5 GiB | `checkpoints` |

**总共约 7 GB 的下载量** —— 比任何一条画图记录都少。下载支持续传。

类别是 `checkpoints`：这是一个完整的检查点，ComfyUI 从中一次拿到模型、文本编码器
和 VAE。SDXL 的第二级（Refiner）不在这份安装里 —— 那是单独的文件，而这条记录
是按「最轻」来设的。

文件没有就位之前，模型**不可能处于已启用状态** —— 应用启动时也会检查这一点。

## 许可证

| | |
|---|---|
| 模型权重 | **CreativeML Open RAIL++-M**（2023 年 7 月 26 日版） |
| 商业使用 | 允许，无需分成，也没有收入门槛 |
| 必须遵守什么 | 附上许可证文本，保留版权声明，并把**使用限制继续传下去** —— 传给每一个从您这里拿到模型或其衍生物的人 |
| 限制 | 许可证禁止若干用途（清单见「Attachment A. Use Restrictions」）：违法、伤害未成年人、散布虚假信息、歧视等 |
| 许可证原文 | <https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0/blob/main/LICENSE.md> |
| 生成费用 | 无 —— 由您的显卡计算 |

许可证已于 2026.08.27 按仓库的 `LICENSE.md` 正文核对。这是一份「开放但负责任」的
许可证：给的权利像宽松许可证一样，但加了一份禁用清单，您有义务把它随模型一起
传下去。对生成结果本身，Stability 不主张权利。

文件直接从 Stability AI 的仓库下载
（<https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0>），不需要重新打包。

另外：**ComfyUI 本身以 GPL-3.0 发布**。AI2P 把它作为外部程序启动，通过 HTTP 与它通信，
因此这个许可证不会传染到您的产品上 —— 但如果您把内含 ComfyUI 的整套东西交给别人，
GPL 的条款就适用于它。

## 硬件要求

| | |
|---|---|
| 显卡 | NVIDIA，**6 GB 显存起**（8 GB 才宽裕） |
| NVIDIA 驱动 | **580 或更新** —— 驱动太旧时 torch 会无声崩溃 |
| 磁盘空间 | ~10 GB（权重 + ComfyUI 包） |
| 内存 | 8 GB 起 |
| 操作系统 | Windows（ComfyUI 便携版）；在 Linux 上 ComfyUI 需要手动安装 |

## 要等多久

1024×1024 的画面 25 步 —— 即使在普通显卡上也只要**几秒到几十秒**。
配置档中作业超时设为 60 分钟（`params.timeoutMinutes`）。

生成进度可以在任务卡片的**作业控制台**中看到。

## 生成参数

在模型配置档中修改（「连接配置档」按钮）：

| 参数 | 默认值 | 含义 |
|---|---|---|
| `width` / `height` | 1024 × 1024 | 画面分辨率；SDXL 正是在 1024 上训练的 |
| `steps` | 25 | 扩散步数 |
| `negative` | 空 | 负面提示词 —— 它是起作用的（`cfg 7`），而且在 SDXL 上比在新模型上更有用 |
| `timeoutMinutes` | 60 | 等待结果多久 |

SDXL 不太懂长描述：提示词最好写成罗列（「画什么、在哪里、什么风格、什么景别」），
而不是连贯的段落。更新的模型（FLUX.2、Qwen-Image）恰好相反。

任务描述全文都会进入提示词。「результат положить в файл X.png」这类指示由连接器执行。

## LoRA 训练

**应用 —— 可以，从 AI2P 训练 —— 不行。**

现成的适配器像所有 ComfyUI 记录一样接入：`LoraLoaderModelOnly` 节点会在运行时
插入图中，文件取自 `<模型仓库>/loras`。而这恰好是公开的现成适配器成千上万的
那种情况。

直接从 AI2P 训练适配器则没有工具：`musubi-tuner` 不认识 Stable Diffusion 家族 ——
SDXL 有同一作者的 [sd-scripts](https://github.com/kohya-ss/sd-scripts)。
所以这条记录设了 `lora.train.kind = external`，训练按钮会立刻拒绝。
在外面训练好的文件放进 `loras` 就行。

## 常见错误

* **画面里的文字变成一团糊** —— SDXL 就是不会；请用 `Qwen-Image-2512`
  或 `Kandinsky-5.0-Image-Lite`。
* **非标准尺寸下图像发「糊」** —— SDXL 是在 1024×1024 上训练的，画面小得多时表现不好。
* **「模型未安装」** —— 文件没有下全；请打开「安装」。
* **LoRA 训练按钮拒绝** —— 这是设计如此（见上）。
* **ComfyUI 被别的进程占用** —— AI2P 只卸载自己启动的那个服务器。

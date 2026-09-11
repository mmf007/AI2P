# SD-3.5-Large

**部署位置:** 本地（在这台计算机上运行）
**接入方式:** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，模型 `sd3_5_large`
**API 密钥:** 不需要 —— 服务器在本地启动，无需授权
**功能:** 文本 → 图像，技能 `image-generate` 80、`image-photo` 79、
`image-concept` 78、`image-text` 70

Stability AI 的 Stable Diffusion 3.5 Large，80 亿参数。在名录里它处于
**中间档**：明显好过 SDXL，明显弱于 FLUX.2 和 Qwen-Image，但它**一个文件**
就能装上，在 12 GB 的显卡上就能跑。

装的是 Comfy-Org 的 fp8 构建：**文本编码器就在这个文件里面**，
不需要另外下载 `clip_g`、`clip_l` 和 `t5xxl`。

它通过 **ComfyUI** 工作：AI2P 把它作为本地服务器启动，发送 workflow，
再把做好的 `.png` 取回任务的产物中。

## 不需要密钥，需要安装

一切都用模型表单中的**「安装」**按钮完成：**设置 → 名录 →
模型 → SD-3.5-Large →「安装」**。会安装：

* **ComfyUI 包**（约 2,1 GB）—— 自带 Python 的便携版；
* 模型仓库（`storage.modelsRepo`）中的**一个权重文件**，组 `SD-3.5`：

| 文件 | 大小 | 放到哪里 |
|---|---|---|
| `sd3.5_large_fp8_scaled.safetensors` | ~13,9 GiB | `checkpoints` |

**总共约 15 GB 的下载量。** 下载支持续传：中断的安装会从停下的地方继续。

类别是 `checkpoints` 而不是 `diffusion_models` —— 这是一个完整的检查点，
ComfyUI 从中一次拿到模型、文本编码器和 VAE。

文件没有就位之前，模型**不可能处于已启用状态** —— 应用启动时也会检查这一点。

## 许可证

| | |
|---|---|
| 模型权重 | **Stability AI Community License**（非自由许可证） |
| 商业使用 | 允许，**前提是年收入低于 100 万美元**；超过就需要 Stability AI 的企业许可证 |
| 必须遵守什么 | 附上许可证文本，保留声明「This Stability AI Model is licensed under the Stability AI Community License, Copyright © Stability AI Ltd.」，并在网站或产品说明中标出「Powered by Stability AI」 |
| 许可证原文 | <https://huggingface.co/stabilityai/stable-diffusion-3.5-large/blob/main/LICENSE.md> |
| 生成费用 | 无 —— 由您的显卡计算 |

许可证已于 2026.08.27 按仓库的 `LICENSE.md` 正文核对（2024 年 7 月 5 日版本）。
这**不是**自由许可证：研究和非商业使用永远免费，商业使用则只在收入低于上述门槛时
才允许。如果这个门槛对您来说已经很近，请改用 `FLUX.2-klein-4B`（Apache 2.0）或
`Kandinsky-5.0-Image-Lite`（MIT）。

AI2P 下载的文件是 Comfy-Org 的重新打包
（<https://huggingface.co/Comfy-Org/stable-diffusion-3.5-fp8>），用的是同一个许可证。

另外：**ComfyUI 本身以 GPL-3.0 发布**。AI2P 把它作为外部程序启动，通过 HTTP 与它通信，
因此这个许可证不会传染到您的产品上 —— 但如果您把内含 ComfyUI 的整套东西交给别人，
GPL 的条款就适用于它。

## 硬件要求

| | |
|---|---|
| 显卡 | NVIDIA，**10 GB 显存起**（12 GB 才宽裕） |
| NVIDIA 驱动 | **580 或更新** —— 驱动太旧时 torch 会无声崩溃 |
| 磁盘空间 | ~18 GB（权重 + ComfyUI 包） |
| 内存 | 16 GB 起 |
| 操作系统 | Windows（ComfyUI 便携版）；在 Linux 上 ComfyUI 需要手动安装 |

## 要等多久

1024×1024 的画面 20 步 —— 在现代显卡上是**几十秒**。
配置档中作业超时设为 60 分钟（`params.timeoutMinutes`）。

生成进度可以在任务卡片的**作业控制台**中看到。

## 生成参数

在模型配置档中修改（「连接配置档」按钮）：

| 参数 | 默认值 | 含义 |
|---|---|---|
| `width` / `height` | 1024 × 1024 | 画面分辨率 |
| `steps` | 20 | 扩散步数 |
| `negative` | 空 | 负面提示词 —— 它是起作用的（`cfg 4.01`） |
| `timeoutMinutes` | 60 | 等待结果多久 |

`cfg 4.01` 这个值是从 ComfyUI 官方模板里原样拿来的。

任务描述全文都会进入提示词。「результат положить в файл X.png」这类指示由连接器执行：
文件会复制到项目文件夹，而那一行本身会从提示词中剪掉（**这类指示只按俄语和英语识别**）。

## LoRA 训练

**应用 —— 可以，从 AI2P 训练 —— 不行。**

现成的适配器像所有 ComfyUI 记录一样接入：`LoraLoaderModelOnly` 节点会在运行时
插入图中，文件取自 `<模型仓库>/loras`。SD 3.5 的适配器公开的有很多，都能用。

直接从 AI2P 训练适配器则没有工具：其他本地模型用的 `musubi-tuner` 根本不认识
Stable Diffusion 家族 —— 那是同一作者另一个工具
[sd-scripts](https://github.com/kohya-ss/sd-scripts) 的地盘。所以这条记录设了
`lora.train.kind = external`，训练按钮会立刻拒绝，而不是算上半小时再拒绝。
在外面训练好的文件放进 `loras` 就行。

## 常见错误

* **画面里的文字做不出来** —— 这是 SD 3.5 的弱项；要写字请用
  `Qwen-Image-2512`，要西里尔字母请用 `Kandinsky-5.0-Image-Lite`。
* **「模型未安装」** —— 文件没有下全；请打开「安装」，窗口会显示还剩多少。
* **LoRA 训练按钮拒绝** —— 这是设计如此（见上）。
* **显存不足** —— 请调小 `width`/`height`。
* **ComfyUI 被别的进程占用** —— AI2P 只卸载自己启动的那个服务器。

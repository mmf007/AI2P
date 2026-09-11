# FLUX.2-dev

> **注意：许可证不是自由的。** FLUX.2 [dev] 的权重是以 **FLUX Non-Commercial License
> v2.1** 发布的 —— 只能用于非商业、非生产的用途。商业应用需要 Black Forest Labs 的单独
> 授权。详情见下面的「许可证」一节；同一家族中自由的替代品是 `FLUX.2-klein-4B`
> （Apache 2.0）。

**部署位置:** 本地（在这台计算机上运行）
**接入方式:** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，模型 `flux2_dev`
**API 密钥:** 不需要 —— 服务器在本地启动，无需授权
**功能:** 文本 → 图像，技能 `image-generate` 94、`image-photo` 93、
`image-concept` 92、`image-text` 90

Black Forest Labs 的 FLUX.2 家族中的老大，320 亿参数 —— **开放权重中的质量之巅**，也是
名录中最重的一条记录：约 54 GB 的下载量。只有当您的显卡很强、而且工作是非商业性质时，
留着它才有意义；其余情况请用 `FLUX.2-klein-4B` 或 `Qwen-Image-2512`。

它通过 **ComfyUI** 工作：AI2P 把它作为本地服务器启动，发送 workflow，再把做好的 `.png`
取回任务的产物中。

## 不需要密钥，需要安装

一切都用模型表单中的**「安装」**按钮完成：**设置 → 名录 → 模型 → FLUX.2-dev →「安装」**。
会安装：

* **ComfyUI 包**（约 2,1 GB）—— 自带 Python 的便携版；
* 模型仓库（`storage.modelsRepo`）中的**三个权重文件**，组 `FLUX.2-dev`：

| 文件 | 大小 | 放到哪里 |
|---|---|---|
| `flux2_dev_fp8mixed.safetensors` | ~33,0 GiB | `diffusion_models` |
| `mistral_3_small_flux2_fp8.safetensors` | ~16,8 GiB | `text_encoders` |
| `flux2-vae.safetensors` | ~320 MiB | `vae` |

**总共约 54 GB 的下载量。** 下载支持断点续传：中断的安装会从停下的地方继续。两个重量级
文件都已经是 fp8 —— 原始的 bf16 体积是它们的两倍，在消费级显卡上并不需要。

dev 的文本编码器是它自己的 —— **Mistral 3 Small**，而不是 klein 用的 Qwen3：这个家族的
文件在两条记录之间并不共用。

文件没有就位之前，模型**不可能处于已启用状态** —— 应用启动时也会检查这一点。

## 许可证

| | |
|---|---|
| 模型权重 | **FLUX Non-Commercial License v2.1** —— 非自由 |
| 商业使用 | 未与 Black Forest Labs 单独签约则**禁止** |
| 允许做什么 | 个人研究、实验、学习、业余爱好 —— 一切您不会因此获得直接或间接报酬的用途 |
| 必须遵守什么 | 保留许可证文本和声明，不得移除内容过滤 |
| 生成结果 | 按许可证条款**不算作**模型的衍生作品，但这并不解除对权重本身的商业使用禁令 |
| 许可证原文 | <https://huggingface.co/black-forest-labs/FLUX.2-dev/blob/main/LICENSE.md> |
| 商业许可证 | <https://bfl.ai/> |
| 生成费用 | 无 —— 由您的显卡计算 |

许可证已于 2026.08.27 按仓库的 `LICENSE.md` 文本核对。原文的措辞是：权重、参数和推理代码
的发布是「freely available for your **non-commercial and non-production** use」。

**FLUX.2 [klein] 9B 用的是同一份许可证**（base 版本和 fp8 版本都是）。整个家族中自由的
只有 4B：它是 Apache 2.0。

文件是从 Comfy-Org 的开放重新打包（<https://huggingface.co/Comfy-Org/flux2-dev>）下载的 ——
Black Forest Labs 的原始仓库需要同意许可证才能分发，没有 HuggingFace 令牌时会返回
`401 Unauthorized`。重新打包沿用的是**完全相同的**非自由许可证：文件不用令牌就能下载这一点，
并不解除任何限制。

另外：**ComfyUI 本身以 GPL-3.0 发布**。AI2P 把它作为外部程序启动，通过 HTTP 与它通信，
因此这个许可证不会传染到您的产品上 —— 但如果您把内含 ComfyUI 的整套东西交给别人，
GPL 的条款就适用于它。

## 硬件要求

| | |
|---|---|
| 显卡 | NVIDIA，**24 GB 显存起**；16 GB 上要靠卸载到内存运行，而且明显更慢 |
| NVIDIA 驱动 | **580 或更新** —— 驱动太旧时 torch 会无声崩溃 |
| 磁盘空间 | ~57 GB（权重 + ComfyUI 包） |
| 内存 | **48 GB 起** —— 卸载各层时模型会整个驻留在内存里 |
| 操作系统 | Windows（ComfyUI 便携版）；在 Linux 上 ComfyUI 需要手动安装 |

这是名录中同时按三项标准都最苛刻的一条记录：显存、内存和磁盘空间。

## 要等多久

1024×1024 画面上的二十步 —— 在 24 GB 的显卡上是**一到两分钟**，如果模型被卸载到内存里，
就要**久得多**。配置档中作业超时设为 120 分钟（`params.timeoutMinutes`）。

生成进度可以在任务卡片的**作业控制台**中看到。

## 生成参数

在模型配置档中修改（「连接配置档」按钮）：

| 参数 | 默认值 | 含义 |
|---|---|---|
| `width` / `height` | 1024 × 1024 | 画面分辨率 |
| `steps` | 20 | 扩散步数 |
| `negative` | 空 | **不起作用**：dev 根本没有负面条件（见下文） |
| `timeoutMinutes` | 120 | 等待结果多久 |

**关于负面提示词。** FLUX.2 [dev] 是带引导蒸馏的模型：图中放的是 `FluxGuidance`（强度 4）
和 `BasicGuider`，那里没有负面条件。`negative` 的值可以写进配置档，但它不会影响图像。
如果需要负面条件，请用 `FLUX.2-klein-4B` —— 那里是基础权重和真正的 `cfg`。

任务描述全文都会进入提示词。FLUX.2 很擅长理解连贯的长描述 —— 请用句子来写，而不是罗列
关键词。

## LoRA 训练

**应用 —— 可以，从 AI2P 训练 —— 不行。**

现成的适配器像所有 ComfyUI 记录一样接入：`LoraLoaderModelOnly` 节点会在运行时插入图中，
文件取自 `<模型仓库>/loras`。

直接从 AI2P 训练适配器是不行的，尽管训练器是存在的：
[musubi-tuner](https://github.com/kohya-ss/musubi-tuner) 会 FLUX.2 [dev]
（`flux_2_train_network.py --model_version dev`），但它要的是 Black Forest Labs 封闭仓库
里的**原始**权重 —— 单个的 `flux2-dev.safetensors` 和被切成多份的 Mistral 3 —— 而不是
AI2P 安装的那份重新打包。这些文件只有拿着 HuggingFace 令牌并同意许可证之后才能下载，
因此这条记录标的是 `lora.train.kind = external`：训练按钮会立即以拒绝作答。

而且训练器的作者直接建议不要在 dev 上训练适配器，而要在 klein 的基础权重上训练 —— 它们
就是为此而做的。在 `FLUX.2-klein-4B` 上训练出来的适配器**不适用于** dev：这是两个不同
大小的模型。

## 常见错误

* **显存不足** —— 请减小 `width`/`height`；如果没用，这个模型就不适合您的显卡，请改用
  `FLUX.2-klein-4B`。
* **生成要花几十分钟** —— 模型被卸载到了内存里；请检查内存是否够用（见「硬件要求」）。
* **负面提示词不起作用** —— dev 就是这样设计的（见「生成参数」）。
* **自己手动补下载时出现 `401 Unauthorized`** —— 您是在从 Black Forest Labs 的仓库下载；
  AI2P 的清单里写的是 Comfy-Org 的开放重新打包。
* **LoRA 训练按钮拒绝工作** —— 这是有意为之（见上文）。

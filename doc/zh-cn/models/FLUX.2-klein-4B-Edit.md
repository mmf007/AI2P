# FLUX.2-klein-4B-Edit

**部署位置:** 本地（在这台计算机上运行）
**接入方式:** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，模型 `flux2_klein_4b_edit`
**API 密钥:** 不需要 —— 服务器在本地启动，无需授权
**功能:** 图片 + 指示 → 图像，技能 `image-edit` 86、
`image-inpaint` 83、`image-generate` 80、`image-text` 78

与 `FLUX.2-klein-4B` 是同一个模型，但工作在**修改图片**的模式下：输入是原始图像和一句
文字指示（「把墙刷成蓝色」、「去掉电线」、「把背景换成傍晚」），输出是修改后的画面。
权重是同一份，不同的只是生成图。

许可证是 **Apache 2.0**，FLUX.2 家族中唯一自由的一个。

它通过 **ComfyUI** 工作：AI2P 把原始图片上传到引擎（`POST /upload/image`）并把它的文件名
代入模板，再把做好的 `.png` 取回任务的产物中。

## 原始图片从哪里来

文件路径在任务描述中按相对于项目文件夹的方式给出，或者用指向项目对象的引用
（`@obj:OBJ-3`）—— 那样代入的就是它的参考帧。没有原始图片，这个模型的作业不会启动：
它被声明为**必需的**（`refImage.required`）。指出原始文件的那行指示**只按俄语和英语
识别**，场景文本可以用任何语言写。

结果的尺寸取自图片本身（`GetImageSize` 节点），因此配置档中的 `width` 和 `height` 在这个
模式下不参与。图片在修改前会被压到一百万像素 —— ComfyUI 的官方模板也是这样做的。

## 不需要密钥，需要安装

一切都用模型表单中的**「安装」**按钮完成：**设置 → 名录 → 模型 → FLUX.2-klein-4B-Edit →
「安装」**。会安装：

* **ComfyUI 包**（约 2,1 GB）—— 自带 Python 的便携版；
* 模型仓库（`storage.modelsRepo`）中的**三个权重文件**，组 `FLUX.2-klein-4B`：

| 文件 | 大小 | 放到哪里 |
|---|---|---|
| `flux-2-klein-base-4b.safetensors` | ~7,2 GiB | `diffusion_models` |
| `qwen_3_4b.safetensors` | ~7,5 GiB | `text_encoders` |
| `flux2-vae.safetensors` | ~320 MiB | `vae` |

**总共约 16 GB 的下载量** —— 但**组是和 `FLUX.2-klein-4B` 那条记录共用的**：如果它已经
安装过，就什么都不用下载，两条记录会立刻都变为可用。

文件没有就位之前，模型**不可能处于已启用状态** —— 应用启动时也会检查这一点。

## 许可证

| | |
|---|---|
| 模型权重 | **Apache 2.0** |
| 商业使用 | 允许 |
| 必须遵守什么 | 保留许可证文本和版权声明 |
| 许可证原文 | <https://huggingface.co/black-forest-labs/FLUX.2-klein-4B>（模型卡片） |
| 生成费用 | 无 —— 由您的显卡计算 |

许可证已于 2026.08.27 按模型卡片核对（`cardData.license: apache-2.0`）。自由的只有 4B
版本：`FLUX.2-klein-9B` 和 `FLUX.2-dev` 用的是 **FLUX Non-Commercial License v2.1**
（未与 Black Forest Labs 签约则禁止商业使用）。

文件是从 Comfy-Org 的开放重新打包
（<https://huggingface.co/Comfy-Org/vae-text-encorder-for-flux-klein-4b>）下载的，同样是
Apache 2.0：Black Forest Labs 的原始仓库没有 HuggingFace 令牌时会返回
`401 Unauthorized`。

另外：**ComfyUI 本身以 GPL-3.0 发布**。AI2P 把它作为外部程序启动，通过 HTTP 与它通信，
因此这个许可证不会传染到您的产品上 —— 但如果您把内含 ComfyUI 的整套东西交给别人，
GPL 的条款就适用于它。

## 硬件要求

| | |
|---|---|
| 显卡 | NVIDIA，**12 GB 显存起**（16 GB 会很宽裕） |
| NVIDIA 驱动 | **580 或更新** —— 驱动太旧时 torch 会无声崩溃 |
| 磁盘空间 | ~19 GB（权重 + ComfyUI 包） |
| 内存 | 16 GB 起 |
| 操作系统 | Windows（ComfyUI 便携版）；在 Linux 上 ComfyUI 需要手动安装 |

## 要等多久

在大约一百万像素的画面上跑二十步 —— 在现代显卡上是**几十秒**。修改比从零绘制稍慢一点：
原始图片还要先编码成潜变量。配置档中作业超时设为 60 分钟（`params.timeoutMinutes`）。

生成进度可以在任务卡片的**作业控制台**中看到。

## 生成参数

在模型配置档中修改（「连接配置档」按钮）：

| 参数 | 默认值 | 含义 |
|---|---|---|
| `steps` | 20 | 扩散步数 |
| `negative` | 空 | 负面提示词 —— 起作用（`cfg 5`） |
| `timeoutMinutes` | 60 | 等待结果多久 |
| `width` / `height` | 1024 × 1024 | **在这个模式下不起作用**：尺寸取自原始图片 |

任务描述全文都会进入提示词。请写指示，而不是整个场景的描述：模型只改被点名的东西，
并尽量不动其余部分。

## LoRA 训练

完全支持：适配器既可以**应用**（`LoraLoaderModelOnly` 节点会在运行时插入图中），也可以
直接从 AI2P **训练** —— 用项目对象卡片中的 LoRA 编辑器。

训练用的是 [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) —— 脚本
`flux_2_cache_latents.py`、`flux_2_cache_text_encoder_outputs.py` 和
`flux_2_train_network.py`（`--network_module networks.lora_flux_2`，
`--model_version klein-base-4b`）。不会多下载任何东西：训练器就在用于生成的那个基础
检查点上训练。

`musubi-tuner` 包（源码，约 30 MB）和 **Python 3.12**（约 45 MB）会随模型一起用「安装」
按钮装上。带 `torch` 的环境（约 3 GB）由训练器在首次启动训练时自行创建。

适配器是两条记录共用的：在这里训练出来的在 `FLUX.2-klein-4B` 里也能用，反过来也一样 ——
它们背后的模型是同一个。

训练好的适配器会放到 `<模型仓库>/loras/<对象代号>.safetensors`。所有训练参数、
`dataset.toml` 和 `train.cmd` 都在模型配置档中（`lora.train`），并且在每次启动前重写 ——
请在配置档中修改它们，而不是改文件。

## 常见错误

* **「模型不接受图片」** —— 作业跑在了不带修改功能的记录上；要做修改请正是用
  `FLUX.2-klein-4B-Edit`。
* **找不到原始文件** —— 路径是**从项目文件夹**算起的，而不是从磁盘根目录。
* **模型把所有东西都重画了** —— 那句指示是场景描述；请写清楚具体要改什么，并加上
  「其余不要改动」。
* **显存不足** —— 请把原始图片改小：它虽然会被压到一百万像素，但特别大的文件仍然更吃力。
* **ComfyUI 被别的进程占用** —— AI2P 只卸载自己启动的那个服务器。

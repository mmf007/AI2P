# FLUX.2-klein-4B

**部署位置:** 本地（在这台计算机上运行）
**接入方式:** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，模型 `flux2_klein_4b`
**API 密钥:** 不需要 —— 服务器在本地启动，无需授权
**功能:** 文本 → 图像，技能 `image-generate` 88、`image-photo` 87、
`image-concept` 86、`image-text` 80

Black Forest Labs 的 **FLUX.2** 家族中最小的一个：40 亿参数，
**自由的 Apache 2.0 许可证** —— 家族里只有它是这样（9B 和 [dev] 的许可证不自由）。
画得明显好过 SDXL 和 SD 3.5，而对硬件的要求介于 Z-Image-Turbo 和 Qwen-Image 之间。

收录的是**基础**版权重（`flux-2-klein-base-4b`）：二十步、`cfg 5`，
负面提示词是真起作用的。另外还有蒸馏版（四步、`cfg 1`）—— 那是另一个权重文件，
清单里没有它。

同一个模型还会**按指示修改图片**；为此名录里另有一条记录 `FLUX.2-klein-4B-Edit`，
用的是同一套权重。

它通过 **ComfyUI** 工作：AI2P 把它作为本地服务器启动，发送 workflow，
再把做好的 `.png` 取回任务的产物中。

## 不需要密钥，需要安装

一切都用模型表单中的**「安装」**按钮完成：**设置 → 名录 →
模型 → FLUX.2-klein-4B →「安装」**。会安装：

* **ComfyUI 包**（约 2,1 GB）—— 自带 Python 的便携版；
* 模型仓库（`storage.modelsRepo`）中的**三个权重文件**，组 `FLUX.2-klein-4B`：

| 文件 | 大小 | 放到哪里 |
|---|---|---|
| `flux-2-klein-base-4b.safetensors` | ~7,2 GiB | `diffusion_models` |
| `qwen_3_4b.safetensors` | ~7,5 GiB | `text_encoders` |
| `flux2-vae.safetensors` | ~320 MiB | `vae` |

**总共约 16 GB 的下载量。**下载支持续传：中断的安装会从停下的地方继续，
已经下好的文件不会重下。

这个组与记录 `FLUX.2-klein-4B-Edit` 共用 —— 装好一个，另一个就不用再下载了。
文本编码器 `qwen_3_4b.safetensors` 和 Z-Image 用的是同一个文件，但模型仓库里的
组互不相交，各自会下载自己的一份。

文件没有就位之前，模型**不可能处于已启用状态** —— 应用启动时也会检查这一点。

## 许可证

| | |
|---|---|
| 模型权重 | **Apache 2.0** |
| 商业使用 | 允许 |
| 必须遵守什么 | 保留许可证文本和版权声明 |
| 许可证原文 | <https://huggingface.co/black-forest-labs/FLUX.2-klein-4B>（模型卡片） |
| 生成费用 | 无 —— 由您的显卡计算 |

许可证已于 2026.08.27 按模型卡片核对（`cardData.license: apache-2.0`）。

**别和家族里其他成员搞混。**Apache 2.0 只有 4B 版有。`FLUX.2-klein-9B`
（包括 base 和 fp8 版本）以及 `FLUX.2-dev` 用的是 **FLUX Non-Commercial License
v2.1**，也就是说，未与 Black Forest Labs 另行签约就禁止商业使用。

AI2P 下载的文件是 Comfy-Org 的重新打包
（<https://huggingface.co/Comfy-Org/vae-text-encorder-for-flux-klein-4b>），
同样是 Apache 2.0。用它还有一个原因：Black Forest Labs 的原始仓库要先同意许可证
才能下载，没有 HuggingFace 令牌时会返回 `401 Unauthorized`。

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

8 GB 的显卡也能把模型跑起来 —— ComfyUI 会把模型的一部分卸到内存里 —— 但一帧要算
好几倍的时间。显卡不强的话，请用 `Z-Image-Turbo` 或 `SDXL-1.0`。

## 要等多久

1024×1024 的画面跑二十步 —— 在现代显卡上是**几十秒**。
配置档中作业超时设为 60 分钟（`params.timeoutMinutes`）。

生成进度可以在任务卡片的**作业控制台**中看到：ComfyUI 自己的输出连同进度条
都会流到那里。

## 生成参数

在模型配置档中修改（「连接配置档」按钮）：

| 参数 | 默认值 | 含义 |
|---|---|---|
| `width` / `height` | 1024 × 1024 | 画面分辨率 |
| `steps` | 20 | 扩散步数 |
| `negative` | 空 | 负面提示词 —— **起作用**（基础权重，`cfg 5`） |
| `timeoutMinutes` | 60 | 等待结果多久 |

FLUX.2 的步数调度取决于画面尺寸，所以步数不是给采样器的，而是给
`Flux2Scheduler` 节点的 —— 模板里已经这样做好了。

任务描述全文都会进入提示词。「результат положить в файл X.png」这类指示由连接器执行：
文件会复制到项目文件夹，而那一行本身会从提示词中剪掉（**这类指示只按俄语和英语识别**）。

## LoRA 训练

完全支持：适配器既可以**应用**（`LoraLoaderModelOnly` 节点会在运行时插入图中），也可以
直接从 AI2P **训练** —— 用项目对象卡片中的 LoRA 编辑器。

训练用的是 [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) —— 脚本
`flux_2_cache_latents.py`、`flux_2_cache_text_encoder_outputs.py` 和
`flux_2_train_network.py`（`--network_module networks.lora_flux_2`，
`--model_version klein-base-4b`），单张显卡，注意力用 `sdpa`。

`musubi-tuner` 包（源码，约 30 MB）和 **Python 3.12**（约 45 MB）会随模型一起用「安装」
按钮装上；计算机上已经有的 Python 3.10–3.12 会直接拿来用。带 `torch` 的环境
（约 3 GB）由训练器在首次启动训练时自行创建。

**与 Z-Image 和 Qwen-Image 的区别。**那两个的生成是在 fp8 版本上算的，而训练器不接受
fp8，所以 `train.cmd` 要另外下载自己的一份权重。这里没什么可补下的：清单里放的就是
基础检查点，训练用的正是已经装好的那些文件。训练器的作者也明确建议就用 klein 的
基础权重来训练。

在这条记录上训练出来的适配器，`FLUX.2-klein-4B-Edit` 也能用：它们背后的模型是同一个。

训练好的适配器会放到 `<模型仓库>/loras/<对象代号>.safetensors`。所有训练参数、
`dataset.toml` 和 `train.cmd` 都在模型配置档中（`lora.train`），并且在每次启动前重写 ——
请在配置档中修改它们，而不是改文件。

## 常见错误

* **进程无声无息地消失** —— 几乎总是 NVIDIA 驱动太旧（见上）。
* **「模型未安装」** —— 文件没有下全；请打开「安装」，窗口会显示还剩多少。
* **显存不足** —— 请调小 `width`/`height`，或者换一个更轻的模型。
* **自己手工补下时出现 `401 Unauthorized`** —— 您是从 Black Forest Labs 的仓库下载的；
  AI2P 的清单里写的是 Comfy-Org 的开放重新打包。
* **ComfyUI 被别的进程占用** —— AI2P 只卸载自己启动的那个服务器；
  8188 上已经在跑的别人的 ComfyUI 它不会去动。

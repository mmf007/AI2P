# Z-Image-Turbo

**部署位置:** 本地（在这台计算机上运行）
**接入方式:** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，模型 `z_image_turbo`
**API 密钥:** 不需要 —— 服务器在本地启动，无需授权
**功能:** 文本 → 图像，技能 `image-generate` 85、`image-photo` 84、
`image-concept` 82、`image-text` 78

通义 MAI（阿里巴巴）的画图模型，**Turbo** 版：60 亿参数，每张画面
**八步**，而不是通常的二三十步到五十步。它是名录里最快、要求最低的本地画图模型 ——
显卡一般的话，把它作为第一选择很合理。

它通过 **ComfyUI** 工作：AI2P 把它作为本地服务器启动，发送 workflow，
再把做好的 `.png` 取回任务的产物中。

## 不需要密钥，需要安装

一切都用模型表单中的**「安装」**按钮完成：**设置 → 名录 →
模型 → Z-Image-Turbo →「安装」**。会安装：

* **ComfyUI 包**（约 2,1 GB）—— 自带 Python 的便携版；
* 模型仓库（`storage.modelsRepo`）中的**三个权重文件**，组 `Z-Image`：

| 文件 | 大小 | 放到哪里 |
|---|---|---|
| `z_image_turbo_bf16.safetensors` | ~11,5 GiB | `diffusion_models` |
| `qwen_3_4b.safetensors` | ~7,5 GiB | `text_encoders` |
| `ae.safetensors` | ~320 MiB | `vae` |

**总共约 21 GB 的下载量。** 下载支持续传：中断的安装会从停下的地方继续，
已经下好的文件不会重新下载。

文件没有就位之前，模型**不可能处于已启用状态** —— 应用启动时也会检查这一点。

## 许可证

| | |
|---|---|
| 模型权重 | **Apache 2.0** |
| 商业使用 | 允许 |
| 必须遵守什么 | 保留许可证文本和版权声明 |
| 许可证原文 | <https://huggingface.co/Tongyi-MAI/Z-Image-Turbo>（模型卡片） |
| 生成费用 | 无 —— 由您的显卡计算 |

AI2P 下载的文件是 Comfy-Org 的重新打包
（<https://huggingface.co/Comfy-Org/z_image_turbo>），它用的是同一个 Apache 2.0。
许可证已于 2026.08.27 按模型卡片核对；开放模型的许可证很少变，
但在商业发布前请再核对一次卡片。

另外：**ComfyUI 本身以 GPL-3.0 发布**。AI2P 把它作为外部程序启动，通过 HTTP 与它通信，
因此这个许可证不会传染到您的产品上 —— 但如果您把内含 ComfyUI 的整套东西交给别人，
GPL 的条款就适用于它。

## 硬件要求

| | |
|---|---|
| 显卡 | NVIDIA，**8 GB 显存起**（16 GB 才宽裕） |
| NVIDIA 驱动 | **580 或更新** —— 驱动太旧时 torch 会无声崩溃 |
| 磁盘空间 | ~23 GB（权重 + ComfyUI 包） |
| 内存 | 16 GB 起 |
| 操作系统 | Windows（ComfyUI 便携版）；在 Linux 上 ComfyUI 需要手动安装 |

## 要等多久

八步 —— 在现代显卡上，1024×1024 的画面只要**几秒到几十秒**，
而不像视频模型那样要几个小时。配置档中作业超时设为 60 分钟
（`params.timeoutMinutes`）—— 即使硬件很慢，这也绰绰有余。

生成进度可以在任务卡片的**作业控制台**中看到：ComfyUI 自己的输出连同进度条
都会流到那里。

## 生成参数

在模型配置档中修改（「连接配置档」按钮）：

| 参数 | 默认值 | 含义 |
|---|---|---|
| `width` / `height` | 1024 × 1024 | 画面分辨率 |
| `steps` | 8 | 扩散步数；Turbo 超过八步没有意义 |
| `negative` | 空 | 负面提示词 —— **在 Turbo 上不起作用**（见下） |
| `timeoutMinutes` | 60 | 等待结果多久 |

**关于负面提示词。** Turbo 模式是不带 classifier-free guidance 计算的（`cfg = 1`），
所以模板里的负面条件是用 `ConditioningZeroOut` 节点做的 —— 和 ComfyUI 官方模板
一模一样。`negative` 的值可以写进配置档，但它不会影响画面；如果确实需要负面提示词，
请用普通（非 Turbo）的模型。

任务描述全文都会进入提示词。「результат положить в файл X.png」这类指示由连接器执行：
文件会复制到项目文件夹，而那一行本身会从提示词中剪掉。

## LoRA 训练

完全支持：适配器既可以**应用**（`LoraLoaderModelOnly` 节点会在运行时插入图中），也可以
直接从 AI2P **训练** —— 用项目对象卡片中的 LoRA 编辑器。

训练用的是 [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) —— 脚本
`zimage_cache_latents.py`、`zimage_cache_text_encoder_outputs.py` 和
`zimage_train_network.py`（`--network_module networks.lora_zimage`），单张显卡，
注意力用 `sdpa`。

`musubi-tuner` 包（源码，约 30 MB）和 **Python 3.12**（约 45 MB）会随模型一起、
用「安装」按钮装上；计算机上已经有的 Python 3.10–3.12 会被直接采用。
带 `torch` 的环境（约 3 GB）由训练器在首次启动训练时自行创建。

**Turbo 特有的一件要紧事。** Turbo 是蒸馏出来的权重，训练器的作者本人就不建议在
它上面训练 LoRA，所以第一次训练时 `train.cmd` 会一次性补下**基础**权重
`z_image_bf16.safetensors`（约 11,5 GB）到 `Z-Image` 组的 `train` 子目录，
并在它上面训练。文本编码器和 VAE 直接用已经装好的 —— 基础版和 turbo 版的是同一份。
训练好的适配器可以配 turbo 权重使用。

训练好的适配器会放到 `<模型仓库>/loras/<对象代号>.safetensors`。所有训练参数、
`dataset.toml` 和 `train.cmd` 都在模型配置档中（`lora.train`），并且在每次启动前重写 ——
请在配置档中修改它们，而不是改文件。

## 常见错误

* **进程无声无息地消失** —— 几乎总是 NVIDIA 驱动太旧（见上）。
* **「模型未安装」** —— 文件没有下全；请打开「安装」，窗口会显示还剩多少。
* **负面提示词不起作用** —— Turbo 本来就是这样设计的（见「生成参数」）。
* **显存不足** —— 请调小 `width`/`height`。
* **ComfyUI 被别的进程占用** —— AI2P 只卸载自己启动的那个服务器；
  8188 上已经在跑的别人的 ComfyUI 它不会去动。

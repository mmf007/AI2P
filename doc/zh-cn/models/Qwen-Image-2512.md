# Qwen-Image-2512

**部署位置:** 本地（在这台计算机上运行）
**接入方式:** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，模型 `qwen_image_2512`
**API 密钥:** 不需要 —— 服务器在本地启动，无需授权
**功能:** 文本 → 图像，技能 `image-text` 94、`image-photo` 91、
`image-generate` 90、`image-concept` 85

阿里巴巴的画图模型，**2512** 版（200 亿参数）。
它的强项是**画面中的文字**：招牌、字幕、封面、海报、界面，
而且西里尔字母也行。这是开放模型里做文字和排版最好的一个 ——
如果图里必须有字，就该按它来挑。

它通过 **ComfyUI** 工作：AI2P 把它作为本地服务器启动，发送 workflow，
再把做好的 `.png` 取回任务的产物中。

## 不需要密钥，需要安装

一切都用模型表单中的**「安装」**按钮完成：**设置 → 名录 →
模型 → Qwen-Image-2512 →「安装」**。会安装：

* **ComfyUI 包**（约 2,1 GB）—— 自带 Python 的便携版；
* 模型仓库（`storage.modelsRepo`）中的**三个权重文件**，组 `Qwen-Image`：

| 文件 | 大小 | 放到哪里 |
|---|---|---|
| `qwen_image_2512_fp8_e4m3fn.safetensors` | ~19,0 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `qwen_image_vae.safetensors` | ~242 MiB | `vae` |

**总共约 30 GB 的下载量。** 下载支持续传：中断的安装会从停下的地方继续，
已经下好的文件不会重新下载。

组 `Qwen-Image` 是和 `Qwen-Image-Edit-2511` 共用的：它们的文本编码器和 VAE
是同一份，所以第二个模型只会补下自己的那个权重文件（约 19 GiB），
而不是把三十个 GB 重下一遍。

文件没有就位之前，模型**不可能处于已启用状态** —— 应用启动时也会检查这一点。

## 许可证

| | |
|---|---|
| 模型权重 | **Apache 2.0** |
| 商业使用 | 允许 |
| 必须遵守什么 | 保留许可证文本和版权声明 |
| 许可证原文 | <https://huggingface.co/Qwen/Qwen-Image>（模型卡片） |
| 生成费用 | 无 —— 由您的显卡计算 |

AI2P 下载的文件是 Comfy-Org 的重新打包
（<https://huggingface.co/Comfy-Org/Qwen-Image_ComfyUI>），它用的是同一个 Apache 2.0。
许可证已于 2026.08.27 按模型卡片核对。

小心一个相近的名字：**Qwen-Image-3.0（2026 年 8 月）是封闭的**，
权重、许可证和技术报告都没有，本地根本跑不起来。开放的这条线
到 2512（画图）和 2511（修改）两个版本为止。

另外：**ComfyUI 本身以 GPL-3.0 发布**。AI2P 把它作为外部程序启动，通过 HTTP 与它通信，
因此这个许可证不会传染到您的产品上 —— 但如果您把内含 ComfyUI 的整套东西交给别人，
GPL 的条款就适用于它。

## 硬件要求

| | |
|---|---|
| 显卡 | NVIDIA，**16 GB 显存起**（24 GB 会很宽裕） |
| NVIDIA 驱动 | **580 或更新** —— 驱动太旧时 torch 会无声崩溃 |
| 磁盘空间 | ~32 GB（权重 + ComfyUI 包） |
| 内存 | 32 GB 起 |
| 操作系统 | Windows（ComfyUI 便携版）；在 Linux 上 ComfyUI 需要手动安装 |

清单里的模型是 **fp8** 的：它比原始的 bf16 轻一半，正好是照着消费级显卡
设计的。fp8 不适合训练 LoRA —— 见下。

## 要等多久

1328×1328 的画面 20 步 —— 在 4090 级别的显卡上是**几分钟**，
在 8–16 GB 上靠内存卸载则要十来分钟。配置档中作业超时设为
90 分钟（`params.timeoutMinutes`）。

生成进度可以在任务卡片的**作业控制台**中看到：ComfyUI 自己的输出连同进度条
都会流到那里。

## 生成参数

在模型配置档中修改（「连接配置档」按钮）：

| 参数 | 默认值 | 含义 |
|---|---|---|
| `width` / `height` | 1328 × 1328 | 画面分辨率 |
| `steps` | 20 | 扩散步数；越少越快也越粗糙 |
| `negative` | 空 | 负面提示词（这里它是**起作用**的，`cfg = 4`） |
| `timeoutMinutes` | 90 | 等待结果多久 |

任务描述全文都会进入提示词。「результат положить в файл X.png」这类指示由连接器执行：
文件会复制到项目文件夹，而那一行本身会从提示词中剪掉（**这类指示只按俄语和英语识别**）。

要出现在画面中的文字，请在提示词里**用引号逐字**写出来 ——
模型复现的正是引号里的内容。

## LoRA 训练

完全支持：适配器既可以**应用**（`LoraLoaderModelOnly` 节点会在运行时插入图中），也可以
直接从 AI2P **训练** —— 用项目对象卡片中的 LoRA 编辑器。

训练用的是 [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) —— 脚本
`qwen_image_cache_latents.py`、`qwen_image_cache_text_encoder_outputs.py` 和
`qwen_image_train_network.py`（`--model_version original`，
`--network_module networks.lora_qwen_image`），单张显卡，注意力用 `sdpa`。

`musubi-tuner` 包（源码，约 30 MB）和 **Python 3.12**（约 45 MB）会随模型一起、
用「安装」按钮装上；计算机上已经有的 Python 3.10–3.12 会被直接采用。
带 `torch` 的环境（约 3 GB）由训练器在首次启动训练时自行创建。

**训练器需要另外的权重文件。** musubi-tuner 明确说 fp8 的构建
（也就是生成所用的那些）不适合训练，所以 `train.cmd` 会在第一次训练时
一次性补下一对 **bf16** —— `qwen_image_2512_bf16.safetensors`
（约 38 GiB）和 `qwen_2.5_vl_7b.safetensors`（约 15,4 GiB）—— 到 `Qwen-Image` 组的
`train` 子目录。这是**在安装之外再加约 57 GB**，好处是生成本身仍然
跑在轻量的 fp8 文件上。VAE 用已经装好的那个。

训练好的适配器会放到 `<模型仓库>/loras/<对象代号>.safetensors`。所有训练参数、
`dataset.toml` 和 `train.cmd` 都在模型配置档中（`lora.train`），并且在每次启动前重写 ——
请在配置档中修改它们，而不是改文件。

## 常见错误

* **进程无声无息地消失** —— 几乎总是 NVIDIA 驱动太旧（见上）。
* **「模型未安装」** —— 文件没有下全；请打开「安装」，窗口会显示还剩多少。
* **显存不足** —— 请调小 `width`/`height`；20B 在 8 GB 上只能靠卸载到内存来跑，
  而且明显更慢。
* **训练在加载权重时失败** —— 请检查剩余空间：训练器需要自己的
  约 57 GB（见「LoRA 训练」）。
* **ComfyUI 被别的进程占用** —— AI2P 只卸载自己启动的那个服务器；
  8188 上已经在跑的别人的 ComfyUI 它不会去动。

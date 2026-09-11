# Kandinsky-5.0-Image-Lite

**部署位置:** 本地（在这台计算机上运行）
**接入方式:** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，模型 `kandinsky5lite_t2i`
**API 密钥:** 不需要 —— 服务器在本地启动，无需授权
**功能:** 文本 → 图像，技能 `image-text` 88、`image-generate` 84、
`image-concept` 83、`image-photo` 82

Kandinsky Lab（Sber）的图片模型，60 亿参数，分辨率最高 1K。
留着它最主要的理由：它**看得懂俄语提示词和俄罗斯的现实事物**，而且
**能在画面里写西里尔字母** —— 招牌、包装上的字、说明文字。名录中其余的开放模型，
西里尔字母写得更差，或者根本写不出来。

许可证是 **MIT**，名录所有记录中最自由的一个。

它和名录里已有的视频记录 `Kandinsky-5.0-*` 是一家的：文本编码器是共用的，
但安装的组是自己的（图像版有自己的权重文件和自己的 VAE）。

它通过 **ComfyUI** 工作：AI2P 把它作为本地服务器启动，发送 workflow，
再把做好的 `.png` 取回任务的产物中。

## 不需要密钥，需要安装

一切都用模型表单中的**「安装」**按钮完成：**设置 → 名录 →
模型 → Kandinsky-5.0-Image-Lite →「安装」**。会安装：

* **ComfyUI 包**（约 2,1 GB）—— 自带 Python 的便携版；
* 模型仓库（`storage.modelsRepo`）中的**四个权重文件**，组
  `Kandinsky-5-Image`：

| 文件 | 大小 | 放到哪里 |
|---|---|---|
| `kandinsky5lite_t2i.safetensors` | ~11,2 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `clip_l.safetensors` | ~235 MiB | `text_encoders` |
| `ae.safetensors` | ~320 MiB | `vae` |

**总共约 22 GB 的下载量。**下载支持续传：中断的安装会从停下的地方继续。

关于 `ae.safetensors`：这是 **FLUX 的** VAE，不是它自己的。模型本身就是这么设计的 ——
ComfyUI 的官方模板和作者的说明里都是这样（`weights/flux/vae` 放进
`ComfyUI/models/vae`）。

文件没有就位之前，模型**不可能处于已启用状态** —— 应用启动时也会检查这一点。

## 许可证

| | |
|---|---|
| 模型权重 | **MIT** |
| 商业使用 | 允许 |
| 必须遵守什么 | 保留许可证文本和版权声明 |
| 许可证原文 | <https://huggingface.co/kandinskylab/Kandinsky-5.0-T2I-Lite>（模型卡片） |
| 生成费用 | 无 —— 由您的显卡计算 |

许可证已于 2026.08.27 按模型卡片核对（`cardData.license: mit`）。MIT 对使用领域
没有限制 —— 这一点不同于 SDXL（那里有一份禁止用途清单）和 FLUX.2 [dev]
（那里禁止商业使用）。

权重是**直接从作者那里**下载的，这个模型没有 Comfy-Org 的重新打包：
`kandinskylab/Kandinsky-5.0-T2I-Lite`，文件 `model/kandinsky5lite_t2i.safetensors` ——
正是 ComfyUI 官方模板期待的那个名字。

另外：**ComfyUI 本身以 GPL-3.0 发布**。AI2P 把它作为外部程序启动，通过 HTTP 与它通信，
因此这个许可证不会传染到您的产品上 —— 但如果您把内含 ComfyUI 的整套东西交给别人，
GPL 的条款就适用于它。

## 硬件要求

| | |
|---|---|
| 显卡 | NVIDIA，**12 GB 显存起**（16 GB 会很宽裕） |
| NVIDIA 驱动 | **580 或更新** —— 驱动太旧时 torch 会无声崩溃 |
| 磁盘空间 | ~25 GB（权重 + ComfyUI 包） |
| 内存 | 16 GB 起 |
| 操作系统 | Windows（ComfyUI 便携版）；在 Linux 上 ComfyUI 需要手动安装 |

## 要等多久

五十步 —— 在现代显卡上 1024×1024 的一帧要**一两分钟**（作者在 H100 上测得 13 秒）。
配置档中作业超时设为 90 分钟（`params.timeoutMinutes`）。

生成进度可以在任务卡片的**作业控制台**中看到：ComfyUI 自己的输出连同进度条
都会流到那里。

## 生成参数

在模型配置档中修改（「连接配置档」按钮）：

| 参数 | 默认值 | 含义 |
|---|---|---|
| `width` / `height` | 1024 × 1024 | 画面分辨率（模型也适配 1280×768） |
| `steps` | 50 | 扩散步数；少于 30 质量下降明显 |
| `negative` | 空 | 负面提示词 —— 起作用（`cfg 3.5`） |
| `timeoutMinutes` | 90 | 等待结果多久 |

任务描述全文都会进入提示词。要出现在画面里的文字请写**在引号中** ——
这样模型才明白那是一句要写上去的字，而不是描述。

## LoRA 训练

**应用可以，从 AI2P 训练不行。**

现成的适配器接入方式和所有 ComfyUI 记录一样：`LoraLoaderModelOnly` 节点会在运行时
插入图中，文件取自 `<模型仓库>/loras`。

而直接从 AI2P 训练适配器则没有工具：[musubi-tuner](https://github.com/kohya-ss/musubi-tuner)
只支持 Kandinsky 5 的**视频**模型，并且在自己的文档里
（`docs/kandinsky5.md`）明说了 Image Lite 系列不受支持。因此这条记录写的是
`lora.train.kind = external`：训练按钮会立刻拒绝，而不是算了半小时才拒绝。
如果将来出现了训练器 —— 只要在模型配置档里补上 `lora.train` 一节就行，不需要改代码。

在 AI2P 之外训练适配器（例如用原始脚本
<https://github.com/kandinskylab/kandinsky-5>）再把文件放进 `loras` —— 是可以的，
它会照常被应用。

## 常见错误

* **画面里的西里尔字母还是歪的** —— 请增加 `steps`，并把要写的字放进引号；
  特别长的句子没有哪个开放模型能写好。
* **「模型未安装」** —— 文件没有下全；请打开「安装」，窗口会显示还剩多少。
* **LoRA 训练按钮拒绝执行** —— 本来就是这样，Image Lite 没有训练器（见上）。
* **显存不足** —— 请调小 `width`/`height`。
* **ComfyUI 被别的进程占用** —— AI2P 只卸载自己启动的那个服务器；
  8188 上已经在跑的别人的 ComfyUI 它不会去动。

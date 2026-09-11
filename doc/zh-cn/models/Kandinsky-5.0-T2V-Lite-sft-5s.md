# Kandinsky-5.0-T2V-Lite-sft-5s

**部署位置:** 本地（在这台计算机上运行）
**接入方式:** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，
模型 `kandinsky5lite_t2v_sft_5s`
**API 密钥:** 不需要 —— 服务器在本地启动，无需授权
**功能:** 文本 → 视频（5 秒），技能 `video-generate` 76、`video-animate` 72

Kandinsky 5.0 家族的视频生成模型（Lite 版本，经过微调，5 秒）。
它通过 **ComfyUI** 工作：AI2P 把它作为本地服务器启动，发送 workflow，
再把做好的 `.mp4` 取回任务的产物中。

## 不需要密钥，需要安装

一切都用模型表单中的**「安装」**按钮完成：**设置 → 名录 →
AI 模型 → Kandinsky-5.0-T2V-Lite-sft-5s →「安装」**。会安装：

* **ComfyUI 包**（约 2,1 GB）—— 自带 Python 的便携版；
* 模型仓库（`storage.modelsRepo`，Windows 上默认是 `C:\ai`，
  Linux/macOS 上是 `~/ai`）中的**四个权重文件**：

| 文件 | 大小 | 放到哪里 |
|---|---|---|
| `Kandinsky-5.0-T2V-Lite-sft-5s.safetensors` | ~4,3 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `clip_l.safetensors` | ~235 MiB | `text_encoders` |
| `hunyuan_video_vae_bf16.safetensors` | ~470 MiB | `vae` |

**总共约 16 GB 的下载量。** 下载支持续传：中断的安装会从停下的地方继续，
已经下好的文件不会重新下载。

文件没有就位之前，模型**不可能处于已启用状态** —— 应用启动时也会检查这一点。

## 许可证

| | |
|---|---|
| 模型权重 | **MIT** |
| 商业使用 | 允许 |
| 必须遵守什么 | 保留许可证文本和版权声明 |
| 许可证原文 | <https://huggingface.co/kandinskylab/Kandinsky-5.0-T2V-Lite-sft-5s>（模型卡片） |
| 生成费用 | 无 —— 由您的计算机计算 |

许可证是 2026.08.27 通过向 HuggingFace 发请求（字段 `cardData.license`）
从模型卡片上取下来的，而不是凭记忆写的。MIT 是名录中最自由的许可证：
允许商业使用，唯一的要求是保留许可证文本和版权声明。

另外：**ComfyUI 本身以 GPL-3.0 发布**。AI2P 把它作为外部程序启动，通过 HTTP 与它通信，
因此这个许可证不会传染到您的产品上 —— 但如果您把内含 ComfyUI 的整套东西交给别人，
GPL 的条款就适用于它。

## 硬件要求

| | |
|---|---|
| 显卡 | NVIDIA，**至少 6 GB 显存** |
| NVIDIA 驱动 | **580 或更新** —— 见下面的警告 |
| 磁盘空间 | ~18 GB（权重 + ComfyUI 包） |
| 内存 | 16 GB 起 |
| 操作系统 | Windows（ComfyUI 便携版）；在 Linux 上 ComfyUI 需要手动安装 |

**关于驱动 —— 很重要。** ComfyUI 包里带的 torch 是按 CUDA 13.0 编译的。在旧驱动上
（例如 527.99 = CUDA 12.0）它不会给出清楚的错误，而是
**access violation** —— 进程就这么消失了。这不是 AI2P 的缺陷：请把 NVIDIA 驱动
升到 580+（在 610.x 上验证过）。如果模型「无声无息地起不来」，请先从
`nvidia-smi` 和驱动版本查起。

## 要等多久

在 6 GB 显存上生成 **768×512、121 帧、50 步的片子大约要一个小时**
（实测：58 分 49 秒）。这是正常的 —— 配置档中作业超时设为
180 分钟（`params.timeoutMinutes`）。

从 1.63 版起，同样的事情可以在**执行者**处设置：AI 执行者表单中的「响应超时，分钟」
字段。填了它 —— 它比配置档的 `timeoutMinutes` 更优先，**0 表示无限期等待**
（生成需要多久就跑多久；可以用「停止」按钮中断）。留空 —— 和以前一样，用配置档里的值。

生成进度可以在任务卡片的**作业控制台**中看到：ComfyUI 自己的输出连同进度条
都会流到那里。

## 生成参数

在模型配置档中修改（「连接配置档」按钮）：

| 参数 | 默认值 | 含义 |
|---|---|---|
| `width` / `height` | 768 × 512 | 分辨率；越大，明显越慢、越吃显存 |
| `length` | 121 | 帧数（≈5 秒） |
| `steps` | 50 | 扩散步数；越少越快也越粗糙 |
| `negative` | 空 | 负面提示词 |
| `timeoutMinutes` | 180 | 等待结果多久 |

任务描述全文都会进入提示词。「результат положить в файл X.mp4」这类指示由连接器执行：
文件会复制到项目文件夹，而那一行本身会从提示词中剪掉（**这类指示只按俄语和英语识别**）。

## LoRA 训练

安排得和 `Kandinsky-5.0-I2V-Lite-5s` 一样，只差两行设置：
这里训练器的任务是 `k5-lite-t2v-5s-sd`，权重是 `Kandinsky-5.0-T2V-Lite-sft-5s.safetensors`。

训练用的是 [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) —— 单张显卡，
注意力用 `sdpa`，13–16 GB 显存。官方的
[kandinsky-5-lora-train](https://github.com/kandinskylab/kandinsky-5-lora-train) 需要
NCCL 和多张显卡，在 Windows 上根本跑不起来。

`musubi-tuner` 包（源码，约 30 MB）和 **Python 3.12**（约 45 MB）会随模型一起、
用「安装」按钮装上：计算机上已经有的 Python 3.10–3.12 会被直接采用，不会重复下载。
带 `torch` 的环境（约 3 GB）由训练器在首次启动训练时自行创建。
训练器自己还会补下原始的 `Qwen/Qwen2.5-VL-7B-Instruct`（约 16 GB）和
`openai/clip-vit-large-patch14`（约 1,7 GB）—— 生成所用的精简版对它并不合适。

训练好的适配器会放到 `<模型仓库>/loras/<对象代号>.safetensors`。所有训练参数、
`dataset.toml` 和 `train.cmd` 都在模型配置档中（`lora.train`），并且在每次启动前重写。

AI2P 中的数据集是**图片**，所以这样训练出来的是外形适配器；动作适配器要用视频训练，
而 LoRA 编辑器目前还没有视频数据集。

## 常见错误

* **进程无声无息地消失** —— 几乎总是 NVIDIA 驱动太旧（见上）。
* **「模型未安装」** —— 文件没有下全；请打开「安装」，窗口会显示还剩多少。
* **显存不足** —— 请调小 `width`/`height` 或 `length`。
* **ComfyUI 被别的进程占用** —— AI2P 只卸载自己启动的那个服务器；
  8188 上已经在跑的别人的 ComfyUI 它不会去动。

# Wan-2.2-T2V-A14B

**部署位置:** 本地（在这台计算机上运行）
**接入方式:** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，模型 `wan2.2_t2v_a14b`
**API 密钥:** 不需要 —— 服务器在本地启动，无需授权
**功能:** 文本 → 视频，技能 `video-generate` 80

阿里巴巴的视频模型，开放的 **Wan 2.2** 系列（2.5 及以上是封闭的 API 产品，
没有权重）。就片子的整体质量而言，它是名录里仅次于 LTX-2.5 的最强开放模型，
而且明显强于 Kandinsky 5.0 Video Lite。

这个模型是**组合式**的（MoE）：里面有两个各 140 亿参数的专家。「高噪」的那个
画前一半步数 —— 总体构图和运动，「低噪」的那个做后一半 —— 细节和锐度。
所以安装清单里有两个权重文件，ComfyUI 的图里也有两个互相传递潜变量的采样器。

它通过 **ComfyUI** 工作：AI2P 把它作为本地服务器启动，发送 workflow，
再把做好的 `.mp4` 取回任务的产物中。

## 不需要密钥，需要安装

一切都用模型表单中的**「安装」**按钮完成：**设置 → 名录 →
模型 → Wan-2.2-T2V-A14B →「安装」**。会安装：

* **ComfyUI 包**（约 2,1 GB）—— 自带 Python 的便携版；
* 模型仓库（`storage.modelsRepo`）中的**四个权重文件**，组 `Wan-2.2-T2V`：

| 文件 | 大小 | 放到哪里 |
|---|---|---|
| `wan2.2_t2v_high_noise_14B_fp8_scaled.safetensors` | ~13,3 GiB | `diffusion_models` |
| `wan2.2_t2v_low_noise_14B_fp8_scaled.safetensors` | ~13,3 GiB | `diffusion_models` |
| `umt5_xxl_fp8_e4m3fn_scaled.safetensors` | ~6,3 GiB | `text_encoders` |
| `wan_2.1_vae.safetensors` | ~242 MiB | `vae` |

**总共约 35,6 GB 的下载量。** 下载支持续传：中断的安装会从停下的地方继续，
已经下好的文件不会重新下载。

文件没有就位之前，模型**不可能处于已启用状态** —— 应用启动时也会检查这一点。

## 许可证

| | |
|---|---|
| 模型权重 | **Apache 2.0** |
| 商业使用 | 允许 |
| 必须遵守什么 | 保留许可证文本和版权声明 |
| 许可证原文 | <https://huggingface.co/Wan-AI/Wan2.2-T2V-A14B>（模型卡片） |
| 生成费用 | 无 —— 由您的显卡计算 |

AI2P 下载的文件是 Comfy-Org 的重新打包
（<https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged>），它用的是同一个
Apache 2.0。许可证已于 2026.08.27 按模型卡片核对；开放模型的许可证很少变，
但在商业发布前请再核对一次卡片。

另外：**ComfyUI 本身以 GPL-3.0 发布**。AI2P 把它作为外部程序启动，通过 HTTP 与它通信，
因此这个许可证不会传染到您的产品上 —— 但如果您把内含 ComfyUI 的整套东西交给别人，
GPL 的条款就适用于它。

## 硬件要求

| | |
|---|---|
| 显卡 | NVIDIA，**16 GB 显存起**（24 GB 才宽裕） |
| NVIDIA 驱动 | **580 或更新** —— 驱动太旧时 torch 会无声崩溃 |
| 磁盘空间 | ~38 GB（权重 + ComfyUI 包），如果还要训练 LoRA 再加约 68 GB |
| 内存 | 32 GB 起 |
| 操作系统 | Windows（ComfyUI 便携版）；在 Linux 上 ComfyUI 需要手动安装 |

两个专家是轮流加载的，所以同一时刻内存里只放两个文件中的一个 ——
16 GB 显存也能算 832×480 的片子，虽然不快。

## 要等多久

832×480、81 帧的片子（16 帧每秒时是 5 秒）—— 在现代显卡上要**几十分钟**：
每一帧都要在两个专家上走 20 步。配置档中作业超时设为 240 分钟
（`params.timeoutMinutes`）。

生成进度可以在任务卡片的**作业控制台**中看到：ComfyUI 自己的输出连同进度条
都会流到那里。

## 生成参数

在模型配置档中修改（「连接配置档」按钮）：

| 参数 | 默认值 | 含义 |
|---|---|---|
| `width` / `height` | 832 × 480 | 画面分辨率，14B 的原生尺寸 |
| `length` | 81 | 片子的帧数；每秒 16 帧，也就是 5 秒 |
| `steps` | 20 | 两个专家合起来的扩散步数 |
| `negative` | 空 | 负面提示词 |
| `timeoutMinutes` | 240 | 等待结果多久 |

**关于步数。** 专家的分界（「高噪」在第几步之后把活交给「低噪」）写死在 workflow
模板里，是 10 —— 二十的一半。如果您改了 `steps`，也要把它一起改：组织目录下
`models/workflow_….json` 文件中第一个采样器的 `end_at_step` 和第二个采样器的
`start_at_step`。连接器不会对参数做算术，所以这个分界不会自己重算。

ComfyUI 官方模板的 Turbo 模式（4 步而不是 20 步）没有搬过来：它需要单独的
Lightning-LoRA 文件，而安装清单里没有这些文件。

任务描述全文都会进入提示词。「результат положить в файл X.mp4」这类指示由连接器执行：
文件会复制到项目文件夹，而那一行本身会从提示词中剪掉。

## LoRA 训练

完全支持：适配器既可以**应用**（`LoraLoaderModelOnly` 节点会在运行时插入图中），也可以
直接从 AI2P **训练** —— 用项目对象卡片中的 LoRA 编辑器。

训练用的是 [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) —— 脚本
`wan_cache_latents.py`、`wan_cache_text_encoder_outputs.py` 和 `wan_train_network.py`
（`--task t2v-A14B`，`--network_module networks.lora_wan`），单张显卡，注意力用
`sdpa`。适配器是在两个专家上一起训练的：「低噪」的那个由 `--dit` 给出，
「高噪」的那个由 `--dit_high_noise` 给出。

`musubi-tuner` 包（源码，约 30 MB）和 **Python 3.12**（约 45 MB）会随模型一起、
用「安装」按钮装上；计算机上已经有的 Python 3.10–3.12 会被直接采用。
带 `torch` 的环境（约 3 GB）由训练器在首次启动训练时自行创建。

**需要事先知道的事。** 生成算的是 `fp8_scaled` 构建 —— 它们轻一半，「安装」按钮装的
正是这些。训练器不接受这样的构建
（[它的文档里明说了](https://github.com/kohya-ss/musubi-tuner/blob/main/docs/wan.md)），
所以第一次训练时 `train.cmd` 会一次性补下**它自己的一对 fp16**（约 57 GB）
和原始文本编码器 `models_t5_umt5-xxl-enc-bf16.pth`（约 11 GB），放到 `Wan-2.2-T2V` 组的
`train` 子目录。这是在安装之外再加约 68 GB —— 请预留好磁盘空间。
这些文件不能放进清单：「模型已安装」这个判断是按清单的内容算的，
那样一来，不训练适配器的人的模型就都会熄灭。

训练好的适配器会放到 `<模型仓库>/loras/<对象代号>.safetensors`。所有训练参数、
`dataset.toml` 和 `train.cmd` 都在模型配置档中（`lora.train`），并且在每次启动前重写 ——
请在配置档中修改它们，而不是改文件。

## 常见错误

* **进程无声无息地消失** —— 几乎总是 NVIDIA 驱动太旧（见上）。
* **「模型未安装」** —— 文件没有下全；请打开「安装」，窗口会显示还剩多少。
* **显存不足** —— 请调小 `width`/`height` 或 `length`；14B 模型在 720p 下的 81 帧
  已经要 24 GB。
* **片子发糊或者「一抖一抖」** —— 请检查改 `steps` 之后专家分界有没有乱
  （见「生成参数」）。
* **ComfyUI 被别的进程占用** —— AI2P 只卸载自己启动的那个服务器；
  8188 上已经在跑的别人的 ComfyUI 它不会去动。

# HunyuanVideo-1.5-720p-T2V

**部署位置:** 本地（在这台计算机上运行）
**接入方式:** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，模型 `hunyuanvideo15_720p_t2v`
**API 密钥:** 不需要 —— 服务器在本地启动，无需授权
**功能:** 文本 → 视频，技能 `video-generate` 79

腾讯的视频模型，版本 **1.5**。名录里三条大的开放产品线（Wan 2.2、HunyuanVideo 1.5、
LTX-2.5）中，它对显存的要求最低，也是唯一一个**开箱就按 720p 计算**、
而不是 480p 的。

第二个文本编码器（`byt5_small_glyphxl`）负责**画面中的文字** —— 如果片子里需要
能读的文字，这个差别很明显；所以图里用的是 `DualCLIPLoader`，
而不是只加载一个编码器的普通加载器。

它通过 **ComfyUI** 工作：AI2P 把它作为本地服务器启动，发送 workflow，
再把做好的 `.mp4` 取回任务的产物中。

## 不需要密钥，需要安装

一切都用模型表单中的**「安装」**按钮完成：**设置 → 名录 →
模型 → HunyuanVideo-1.5-720p-T2V →「安装」**。会安装：

* **ComfyUI 包**（约 2,1 GB）—— 自带 Python 的便携版；
* 模型仓库（`storage.modelsRepo`）中的**四个权重文件**，组
  `HunyuanVideo-1.5`：

| 文件 | 大小 | 放到哪里 |
|---|---|---|
| `hunyuanvideo1.5_720p_t2v_fp16.safetensors` | ~15,5 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `byt5_small_glyphxl_fp16.safetensors` | ~418 MiB | `text_encoders` |
| `hunyuanvideo15_vae_fp16.safetensors` | ~2,3 GiB | `vae` |

**总共约 28,6 GB 的下载量。**下载支持续传：中断的安装会从停下的地方继续。

文件没有就位之前，模型**不可能处于已启用状态** —— 应用启动时也会检查这一点。

## 许可证

| | |
|---|---|
| 模型权重 | **Tencent Hunyuan Community License**（不是 Apache 也不是 MIT） |
| 商业使用 | 允许，但权利人有附加条件 |
| 必须遵守什么 | 接受许可证条款并保留声明；许可证对产品的用户数量和适用地区有限制 |
| 许可证原文 | <https://github.com/Tencent-Hunyuan/HunyuanVideo-1.5/blob/master/LICENSE> |
| 模型卡片 | <https://huggingface.co/tencent/HunyuanVideo-1.5> |
| 生成费用 | 无 —— 由您的显卡计算 |

这**不是** Apache/MIT 意义上的自由许可证：商业发布前请把它的文本整篇读完，
里面有一些 Wan 2.2 和 Kandinsky 所没有的条件。许可证已于 2026.08.27
按模型卡片核对（`license_name: tencent-hunyuan-community`）。

另外：**ComfyUI 本身以 GPL-3.0 发布**。AI2P 把它作为外部程序启动，通过 HTTP 与它通信，
因此这个许可证不会传染到您的产品上 —— 但如果您把内含 ComfyUI 的整套东西交给别人，
GPL 的条款就适用于它。

## 硬件要求

| | |
|---|---|
| 显卡 | NVIDIA，**12 GB 显存起**（16 GB 会很宽裕） |
| NVIDIA 驱动 | **580 或更新** —— 驱动太旧时 torch 会无声崩溃 |
| 磁盘空间 | ~31 GB（权重 + ComfyUI 包），如果还要训练 LoRA 再加 ~50 GB |
| 内存 | 32 GB 起 |
| 操作系统 | Windows（ComfyUI 便携版）；在 Linux 上 ComfyUI 需要手动安装 |

显存不够时，可以在配置档里把加载节点的 `weight_dtype` 切换成 `fp8_e4m3fn` ——
ComfyUI 的官方模板自己就是这么建议的。

## 要等多久

1280×720、121 帧的片子（24 帧每秒时是 5 秒）—— 在现代显卡上要**几十分钟**
（20 步）。配置档中作业超时设为 240 分钟（`params.timeoutMinutes`）。
生成进度可以在任务卡片的**作业控制台**中看到。

## 生成参数

| 参数 | 默认值 | 含义 |
|---|---|---|
| `width` / `height` | 1280 × 720 | 画面分辨率 |
| `length` | 121 | 片子的帧数；24 帧每秒，也就是 5 秒 |
| `steps` | 20 | 扩散步数 |
| `negative` | 空 | 负面提示词（模板中文本跟随强度为 6） |
| `timeoutMinutes` | 240 | 等待结果多久 |

ComfyUI 的官方模板里，除了基础生成，旁边还有放大到 1080p 和 EasyCache 加速 ——
它们在那边是**关掉**的，也没有搬进我们的模板：那是另外的权重文件，清单里没有。

任务描述全文都会进入提示词。「результат положить в файл X.mp4」这类指示由连接器执行：
文件会复制到项目文件夹，而那一行本身会从提示词中剪掉（**这类指示只按俄语和英语识别**）。

## LoRA 训练

完全支持：适配器既可以**应用**（`LoraLoaderModelOnly` 节点会在运行时插入图中），也可以
直接从 AI2P **训练** —— 用项目对象卡片中的 LoRA 编辑器。

训练用的是 [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) —— 脚本
`hv_1_5_cache_latents.py`、`hv_1_5_cache_text_encoder_outputs.py` 和
`hv_1_5_train_network.py`（`--task t2v`，`--network_module networks.lora_hv_1_5`），
单张显卡，注意力用 `sdpa`。

`musubi-tuner` 包（源码，约 30 MB）和 **Python 3.12**（约 45 MB）会随模型一起装上；
带 `torch` 的环境（约 3 GB）由训练器在首次启动训练时自行创建。

**需要事先知道的事。**生成算的是 fp16 的重新打包和精简过的 fp8 文本编码器 ——
这两样训练器都不接受（[它的文档](https://github.com/kohya-ss/musubi-tuner/blob/main/docs/hunyuan_video_1_5.md)
明确要求原始 DiT 和完整编码器）。因此第一次训练时，`train.cmd` 会一次性把原始的
`diffusion_pytorch_model.safetensors`（约 31 GB）和完整的
`qwen_2.5_vl_7b.safetensors`（约 15,5 GB）下载到组 `HunyuanVideo-1.5` 的 `train`
子目录里。也就是在安装之外还要约 50 GB —— 请预留磁盘空间。VAE 和文字编码器
（`byt5`）用已经装好的那份。

训练好的适配器会放到 `<模型仓库>/loras/<对象代号>.safetensors`。所有训练参数、
`dataset.toml` 和 `train.cmd` 都在模型配置档中（`lora.train`），并且在每次启动前重写 ——
请在配置档中修改它们，而不是改文件。

## 常见错误

* **显存不足** —— 请把 workflow 模板中的 `weight_dtype` 切成 `fp8_e4m3fn`，
  或者调小 `width`/`height`。
* **「模型未安装」** —— 文件没有下全；请打开「安装」，窗口会显示还剩多少。
* **进程无声无息地消失** —— 几乎总是 NVIDIA 驱动太旧。
* **ComfyUI 被别的进程占用** —— AI2P 只卸载自己启动的那个服务器；
  8188 上已经在跑的别人的 ComfyUI 它不会去动。

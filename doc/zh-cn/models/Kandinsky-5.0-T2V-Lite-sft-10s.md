# Kandinsky-5.0-T2V-Lite-sft-10s

**部署位置:** 本地（在这台计算机上运行）
**接入方式:** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，模型 `kandinsky5lite_t2v_sft_10s`
**API 密钥:** 不需要 —— 服务器在本地启动，无需授权
**功能:** 文本 → 视频，技能 `video-generate` 76

Kandinsky Lab 的俄罗斯开放视频模型 **Kandinsky 5.0 Video Lite**（20 亿参数）的一个版本。
它看得懂俄语提示词，还能在**画面里画西里尔字母** —— 名录中别的模型都做不到这一点。

这是这条产品线的原始版本：50 步、完整遵循文本 —— Lite 家族中质量最好的。

片长是 **10 秒**（24 帧每秒时为 241 帧）：长度是写死在权重里的，
所以这条产品线的 5 秒和 10 秒是分开的文件。

它通过 **ComfyUI** 工作：AI2P 把它作为本地服务器启动，发送 workflow，
再把做好的 `.mp4` 取回任务的产物中。

## 不需要密钥，需要安装

一切都用模型表单中的**「安装」**按钮完成：**设置 → 名录 →
模型 → Kandinsky-5.0-T2V-Lite-sft-10s →「安装」**。会安装：

* **ComfyUI 包**（约 2,1 GB）—— 自带 Python 的便携版；
* 模型仓库（`storage.modelsRepo`）中的**四个权重文件**，组
  `Kandinsky-5`：

| 文件 | 大小 | 放到哪里 |
|---|---|---|
| `kandinsky5lite_t2v_sft_10s.safetensors` | ~4,3 GiB | `diffusion_models` |
| `qwen_2.5_vl_7b_fp8_scaled.safetensors` | ~8,7 GiB | `text_encoders` |
| `clip_l.safetensors` | ~235 MiB | `text_encoders` |
| `hunyuan_video_vae_bf16.safetensors` | ~470 MiB | `vae` |

**总共约 14,6 GB 的下载量** —— 是名录中最轻的视频模型。组
`Kandinsky-5` 是整条产品线共用的：如果旁边已经装了它的另一个版本，
就只需补下自己的那个权重文件（约 4,6 GB），编码器和 VAE 已经就位了。

文件没有就位之前，模型**不可能处于已启用状态** —— 应用启动时也会检查这一点。

## 许可证

| | |
|---|---|
| 模型权重 | **MIT** |
| 商业使用 | 允许 |
| 必须遵守什么 | 保留许可证文本和版权声明 |
| 许可证原文 | <https://huggingface.co/kandinskylab/Kandinsky-5.0-T2V-Lite-sft-10s>（模型卡片） |
| 生成费用 | 无 —— 由您的显卡计算 |

MIT 是名录中视频模型许可证里最自由的一个：Wan 2.2 是 Apache 2.0，
HunyuanVideo 1.5 和 LTX-2.5 用的是权利人自己的协议。许可证已于 2026.08.27
按模型卡片核对。

另外：**ComfyUI 本身以 GPL-3.0 发布**。AI2P 把它作为外部程序启动，通过 HTTP 与它通信，
因此这个许可证不会传染到您的产品上 —— 但如果您把内含 ComfyUI 的整套东西交给别人，
GPL 的条款就适用于它。

## 硬件要求

| | |
|---|---|
| 显卡 | NVIDIA，**12 GB 显存起**（24 GB 会很宽裕，对十秒的片子则是必须的） |
| NVIDIA 驱动 | **580 或更新** —— 驱动太旧时 torch 会无声崩溃 |
| 磁盘空间 | ~17 GB（权重 + ComfyUI 包） |
| 内存 | 16 GB 起 |
| 操作系统 | Windows（ComfyUI 便携版）；在 Linux 上 ComfyUI 需要手动安装 |

## 要等多久

五十步且完整遵循文本 —— 是这条产品线里最慢的版本：在现代显卡上是**几十分钟**，
而十秒的片子时间大约要翻一倍。

配置档中作业超时设为 180 分钟（`params.timeoutMinutes`）。生成进度可以在任务卡片的
**作业控制台**中看到：ComfyUI 自己的输出连同进度条都会流到那里。

## 生成参数

| 参数 | 默认值 | 含义 |
|---|---|---|
| `width` / `height` | 768 × 512 | 画面分辨率 |
| `length` | 241 | 片子的帧数；24 帧每秒，也就是 10 秒 |
| `steps` | 50 | 扩散步数 —— 和这个版本的训练任务用的步数一致 |
| `negative` | 空 | 负面提示词 |
| `timeoutMinutes` | 180 | 等待结果多久 |

任务描述全文都会进入提示词。「результат положить в файл X.mp4」这类指示由连接器执行：
文件会复制到项目文件夹，而那一行本身会从提示词中剪掉（**这类指示只按俄语和英语识别**）。

## LoRA 训练

完全支持：适配器既可以**应用**（`LoraLoaderModelOnly` 节点会在运行时插入图中），也可以
直接从 AI2P **训练** —— 用项目对象卡片中的 LoRA 编辑器。

训练用的是 [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) —— 脚本
`kandinsky5_cache_latents.py`、`kandinsky5_cache_text_encoder_outputs.py` 和
`kandinsky5_train_network.py`（`--task k5-lite-t2v-10s-sd`，
`--network_module networks.lora_kandinsky`），单张显卡，注意力用 `sdpa`。
每个版本的训练任务都是自己的 —— 它同时决定步数和调度，所以不能拿别的版本的来顶替。

`musubi-tuner` 包（源码，约 30 MB）和 **Python 3.12**（约 45 MB）会随模型一起装上；
带 `torch` 的环境（约 3 GB）由训练器在首次启动训练时自行创建。
和 Wan 2.2、HunyuanVideo 1.5 不同，这里不需要补下权重：DiT 文件本来就不是
fp8，而自己的文本编码器训练器会从 HuggingFace 自己取。

训练好的适配器会放到 `<模型仓库>/loras/<对象代号>.safetensors`。所有训练参数、
`dataset.toml` 和 `train.cmd` 都在模型配置档中（`lora.train`），并且在每次启动前重写 ——
请在配置档中修改它们，而不是改文件。

## 常见错误

* **进程无声无息地消失** —— 几乎总是 NVIDIA 驱动太旧（见上）。
* **「模型未安装」** —— 文件没有下全；请打开「安装」，窗口会显示还剩多少。
* **显存不足** —— 请调小 `width`/`height`；241 帧的片子会把整个潜变量
  一直放在显存里。
* **ComfyUI 被别的进程占用** —— AI2P 只卸载自己启动的那个服务器；
  8188 上已经在跑的别人的 ComfyUI 它不会去动。

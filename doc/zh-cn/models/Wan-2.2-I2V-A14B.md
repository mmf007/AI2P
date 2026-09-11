# Wan-2.2-I2V-A14B

**部署位置:** 本地（在这台计算机上运行）
**接入方式:** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，模型 `wan2.2_i2v_a14b`
**API 密钥:** 不需要 —— 服务器在本地启动，无需授权
**功能:** 图片 + 文本 → 视频，技能 `video-animate` 80、`video-generate` 74

和 Wan-2.2-T2V-A14B 同属阿里巴巴开放的 **Wan 2.2** 系列，但片子是**从图片**
画起来的：起始帧决定角色、构图和风格，提示词只描述运动。这是拿到带指定主角的
片子最可预期的办法 —— 长相取自画面，而不是用文字复述。

这个模型是组合式的（MoE），两个各 140 亿参数的专家：「高噪」的那个画前一半步数，
「低噪」的那个做后一半。

它通过 **ComfyUI** 工作：AI2P 把它作为本地服务器启动，上传起始帧，发送 workflow，
再把做好的 `.mp4` 取回任务的产物中。

## 起始帧从哪里来

作业必须指名它 —— 没有图片模型根本不会启动（拒绝是立刻来的，而不是算了半小时才来）。
有两种办法：

* **项目对象引用** —— 任务描述中的 `@obj:OBJ-3`：参考帧的路径会自动代入，
  对象的档案会进入提示词；
* 相对于项目文件夹的**文件路径**，在描述中单独写一行。

接受 `.png`、`.jpg`、`.webp`；画面会缩放到配置档的 `width`×`height`。

## 不需要密钥，需要安装

一切都用模型表单中的**「安装」**按钮完成：**设置 → 名录 →
模型 → Wan-2.2-I2V-A14B →「安装」**。会安装：

* **ComfyUI 包**（约 2,1 GB）—— 自带 Python 的便携版；
* 模型仓库（`storage.modelsRepo`）中的**四个权重文件**，组 `Wan-2.2-I2V`：

| 文件 | 大小 | 放到哪里 |
|---|---|---|
| `wan2.2_i2v_high_noise_14B_fp8_scaled.safetensors` | ~13,3 GiB | `diffusion_models` |
| `wan2.2_i2v_low_noise_14B_fp8_scaled.safetensors` | ~13,3 GiB | `diffusion_models` |
| `umt5_xxl_fp8_e4m3fn_scaled.safetensors` | ~6,3 GiB | `text_encoders` |
| `wan_2.1_vae.safetensors` | ~242 MiB | `vae` |

**总共约 35,6 GB 的下载量。** 组是自己的：「文本 → 视频」和「图片 → 视频」的
权重文件不一样，而编码器和 VAE 是同一份，所以两条记录都装了的话，
这两个文件会在磁盘上存两遍。

文件没有就位之前，模型**不可能处于已启用状态** —— 应用启动时也会检查这一点。

## 许可证

| | |
|---|---|
| 模型权重 | **Apache 2.0** |
| 商业使用 | 允许 |
| 必须遵守什么 | 保留许可证文本和版权声明 |
| 许可证原文 | <https://huggingface.co/Wan-AI/Wan2.2-I2V-A14B>（模型卡片） |
| 生成费用 | 无 —— 由您的显卡计算 |

AI2P 下载的文件是 Comfy-Org 的重新打包
（<https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged>），它用的是同一个
Apache 2.0。许可证已于 2026.08.27 按模型卡片核对。

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

## 要等多久

832×480、81 帧的片子（16 帧每秒时是 5 秒）—— 在现代显卡上要**几十分钟**。
配置档中作业超时设为 240 分钟（`params.timeoutMinutes`）。生成进度可以在任务卡片的
**作业控制台**中看到。

## 生成参数

| 参数 | 默认值 | 含义 |
|---|---|---|
| `width` / `height` | 832 × 480 | 画面分辨率；起始帧也会按它来适配 |
| `length` | 81 | 片子的帧数；每秒 16 帧，也就是 5 秒 |
| `steps` | 20 | 两个专家合起来的扩散步数 |
| `negative` | 空 | 负面提示词 |
| `timeoutMinutes` | 240 | 等待结果多久 |

**关于步数。** 专家的分界写死在 workflow 模板里，是 10 —— 二十的一半。
改 `steps` 的时候，请把组织目录下 `models/workflow_….json` 文件中第一个采样器的
`end_at_step` 和第二个采样器的 `start_at_step` 一起改：连接器不会对参数做算术。

## LoRA 训练

完全支持：适配器既可以**应用**（`LoraLoaderModelOnly` 节点会在运行时插入图中），也可以
直接从 AI2P **训练** —— 用项目对象卡片中的 LoRA 编辑器。

训练用的是 [musubi-tuner](https://github.com/kohya-ss/musubi-tuner) —— 脚本
`wan_cache_latents.py`（带 `--i2v` 参数）、`wan_cache_text_encoder_outputs.py` 和
`wan_train_network.py`（`--task i2v-A14B`，`--network_module networks.lora_wan`）。
适配器是在两个专家上一起训练的：`--dit` 和 `--dit_high_noise`。

`musubi-tuner` 包和 **Python 3.12** 会随模型一起装上；带 `torch` 的环境（约 3 GB）
由训练器在首次启动训练时自行创建。

**需要事先知道的事。** 生成算的是 `fp8_scaled` 构建，而训练器不接受它们
（[它的文档里明说了](https://github.com/kohya-ss/musubi-tuner/blob/main/docs/wan.md)），
所以第一次训练时 `train.cmd` 会一次性补下它自己的一对 **fp16**（约 57 GB）
和原始文本编码器 `models_t5_umt5-xxl-enc-bf16.pth`（约 11 GB），放到 `Wan-2.2-I2V` 组的
`train` 子目录。这是在安装之外再加约 68 GB。

训练好的适配器会放到 `<模型仓库>/loras/<对象代号>.safetensors`。所有训练参数、
`dataset.toml` 和 `train.cmd` 都在模型配置档中（`lora.train`），并且在每次启动前重写 ——
请在配置档中修改它们，而不是改文件。

## 常见错误

* **「作业需要起始帧」** —— 任务描述里既没有 `@obj:` 引用，也没有图片路径；
  要从零开始画请用 Wan-2.2-T2V-A14B 那条记录。
* **第一帧「发飘」** —— 起始图片的长宽比和 `width`×`height` 差得太多；
  请事先把它调好，或者改配置档里的尺寸。
* **显存不足** —— 请调小 `width`/`height` 或 `length`。
* **进程无声无息地消失** —— 几乎总是 NVIDIA 驱动太旧。

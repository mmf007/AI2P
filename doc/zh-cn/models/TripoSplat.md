# TripoSplat

**部署位置:** 本地（在这台计算机上运行）
**接入方式:** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，模型 `triposplat`
**API 密钥:** 不需要 —— 服务器在本地启动，无需授权
**功能:** 图像 → 高斯泼溅，技能 `3d-image` 80

Tripo AI（VAST-AI）的开放模型，把一张图片变成的不是网格，而是三维高斯点云 ——
**高斯泼溅**。它是名录里唯一给出这种结果的记录：文件 `.spz`，可以用泼溅查看器、
Unreal、Unity 以及 Babylon.js 之类的网页播放器打开。

泼溅**不是**网格：里面没有多边形也没有 UV，所以进游戏管线时要么原样放进去，
要么单独转换。但在网格显得粗糙的地方（植被、毛发、烟雾、室内），
它能给出边缘柔和、带透明度的照片级画面。

它通过 **ComfyUI** 工作：AI2P 把它作为本地服务器启动，发送 workflow，
再把做好的 `.spz` 取回任务的产物中。

**读的是图片，不是文本。** 模型的图里根本没有文本编码器 —— 条件是由
DINOv3 从起始帧给出的。模型看不见任务描述：图片上画的是什么，出来的就是什么。
起始帧在任务描述中用项目对象引用（`@obj:OBJ-3`）或相对于项目文件夹的文件路径给出；
没有它，作业根本不会启动。

起始帧的背景会自动去掉（BiRefNet）。

## 不需要密钥，需要安装

一切都用模型表单中的**「安装」**按钮完成：**设置 → 名录 →
模型 → TripoSplat →「安装」**。会安装：

* **ComfyUI 包**（约 2,1 GB）—— 自带 Python 的便携版；
* 模型仓库（`storage.modelsRepo`）中的**五个权重文件**，组 `TripoSplat`：

| 文件 | 大小 | 放到哪里 |
|---|---|---|
| `dino_v3_vit_h.safetensors` | ~1,57 GiB | `clip_vision` |
| `triposplat_fp16.safetensors` | ~707 MiB | `diffusion_models` |
| `triposplat_vae_decoder_fp16.safetensors` | ~549 MiB | `vae` |
| `flux2-vae.safetensors` | ~321 MiB | `vae` |
| `birefnet.safetensors` | ~424 MiB | `background_removal` |

**总共约 3,8 GB 的下载量** —— 是名录里最轻的本地 3D 记录。
下载支持续传：中断的安装会从停下的地方继续，已经下好的文件不会重新下载。

文件没有就位之前，模型**不可能处于已启用状态** —— 应用启动时也会检查这一点。

## 许可证

| | |
|---|---|
| 模型权重 | **MIT** |
| 商业使用 | 允许 |
| 必须遵守什么 | 保留许可证文本和版权声明 |
| 许可证原文 | <https://huggingface.co/VAST-AI/TripoSplat>（模型卡片） |
| 生成费用 | 无 —— 由您的显卡计算 |

去背景用的是 BiRefNet（<https://huggingface.co/ZhengPeng7/BiRefNet>），也是 MIT；
画面编码器是 DINOv3（Meta），它就放在 VAST-AI 的同一个仓库里，用同一个许可证。
许可证已于 2026.08.27 按模型卡片核对；开放模型的许可证很少变，但在商业发布前
请再核对一次卡片。

另外：**ComfyUI 本身以 GPL-3.0 发布**。AI2P 把它作为外部程序启动，通过 HTTP 与它通信，
因此这个许可证不会传染到您的产品上 —— 但如果您把内含 ComfyUI 的整套东西交给别人，
GPL 的条款就适用于它。

## 硬件要求

| | |
|---|---|
| 显卡 | NVIDIA，**8 GB 显存起**（12 GB 才宽裕） |
| NVIDIA 驱动 | **580 或更新** —— 驱动太旧时 torch 会无声崩溃 |
| 磁盘空间 | ~6 GB（权重 + ComfyUI 包） |
| 内存 | 16 GB 起 |
| 操作系统 | Windows（ComfyUI 便携版）；在 Linux 上 ComfyUI 需要手动安装 |

显存那个数字是**估算**，不是实测。内存不够时，请调小 workflow 模板中
`VAEDecodeTripoSplat` 的 `num_gaussians`（默认 262144）。

## 要等多久

在现代显卡上每个模型几分钟：模型本身很小（约 700 MB 权重），而且只有一级，
不像 TRELLIS-2 有四级。配置档中作业超时设为 60 分钟（`params.timeoutMinutes`）。

生成进度可以在任务卡片的**作业控制台**中看到：ComfyUI 自己的输出连同进度条
都会流到那里。

## 生成参数

在模型配置档中修改（「连接配置档」按钮）：

| 参数 | 默认值 | 含义 |
|---|---|---|
| `steps` | 20 | 扩散步数 |
| `timeoutMinutes` | 60 | 等待结果多久 |
| `width` / `height` | 1024 | 对结果**没有**影响：3D 没有画面 |
| `negative` | 空 | 不起作用 —— 图里没有文本编码器 |

高斯点的数量（`num_gaussians`，262144）和文件格式（`spz`）由 workflow 模板给定。
`SplatToFile3D` 节点会的格式有 `spz`、`ply`、`splat`、`ksplat`；
选 `spz` 是因为它最紧凑。

**模板里刻意没有的东西。** ComfyUI 的官方模板还会额外绕着物体转一圈相机，
把它存成短片（`RenderSplat` → `CreateVideo` → `SaveVideo`）。这一支我们没有搬过来：
那是每次生成都要单独渲染一遍，而作业的结果本来就是那个泼溅文件。同一个地方还有
`SplatToMesh` —— 把泼溅变成普通网格的节点；需要网格的人可以把它补进自己的
workflow，或者改用 [TRELLIS-2](https://huggingface.co/microsoft/TRELLIS.2-4B)。

## LoRA 训练

**不支持，而且这是诚实的拒绝，不是没做完。** 今天还没有公开的 3D 架构 LoRA 训练器：
AI2P 全部适配器训练所依赖的 [musubi-tuner](https://github.com/kohya-ss/musubi-tuner)
只会图片和视频。所以配置档里设了 `lora.supported: false`，模型表单会用文字说明原因。

这里保持物体在各帧之间一致，靠的是另一种办法：把同一张角色参考图（项目对象，
引用 `@obj:`）送进输入 —— 同样的图片加同样的 `seed`，出来的结果就是一样的。

## 常见错误

* **「模型未安装」** —— 文件没有下全；请打开「安装」，窗口会显示还剩多少。
* **没有图片作业就拒绝启动** —— 这是设计如此：这个模型只能从图像出发。
  请给出指向参考帧的 `@obj:` 引用或文件路径。
* **`.spz` 文件用惯常的程序打不开** —— 这是泼溅，不是网格；需要高斯泼溅查看器
  或者做一次转换。
* **显存不足** —— 请调小 workflow 模板里的 `num_gaussians`。
* **进程无声无息地消失** —— 几乎总是 NVIDIA 驱动太旧（见上）。

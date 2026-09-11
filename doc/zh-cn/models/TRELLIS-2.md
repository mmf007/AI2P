# TRELLIS-2

**部署位置:** 本地（在这台计算机上运行）
**接入方式:** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，模型 `trellis_2`
**API 密钥:** 不需要 —— 服务器在本地启动，无需授权
**功能:** 图像 → 带颜色的 3D 模型，技能 `3d-image` 85

微软研究院的开放模型（40 亿参数），把一张图片变成三维网格。
在名录的三条本地 3D 记录里，只有它不仅给出形状，还给出颜色：
形状那一级之后还有单独的一级贴图。

它通过 **ComfyUI** 工作：AI2P 把它作为本地服务器启动，发送 workflow，
再把做好的 `.glb` 取回任务的产物中。

**读的是图片，不是文本。** 模型的图里根本没有文本编码器 —— 条件是由
DINOv3 从起始帧给出的。模型看不见任务描述：图片上画的是什么，出来的就是什么。
起始帧在任务描述中用项目对象引用（`@obj:OBJ-3`）或相对于项目文件夹的文件路径给出；
没有它，作业根本不会启动。

起始帧的背景会自动去掉（BiRefNet），画面按物体裁剪：从带背景的图片做 3D，
效果会明显差一截。

## 不需要密钥，需要安装

一切都用模型表单中的**「安装」**按钮完成：**设置 → 名录 →
模型 → TRELLIS-2 →「安装」**。会安装：

* **ComfyUI 包**（约 2,1 GB）—— 自带 Python 的便携版；
* 模型仓库（`storage.modelsRepo`）中的**五个权重文件**，组 `TRELLIS-2`：

| 文件 | 大小 | 放到哪里 |
|---|---|---|
| `trellis_2_int8_convrot.safetensors` | ~4,89 GiB | `diffusion_models` |
| `dino_v3_vit_l.safetensors` | ~1,13 GiB | `clip_vision` |
| `trellis_2_shape_vae_bf16.safetensors` | ~1,02 GiB | `vae` |
| `trellis_2_texture_vae_bf16.safetensors` | ~904 MiB | `vae` |
| `birefnet.safetensors` | ~424 MiB | `background_removal` |

**总共约 9,0 GB 的下载量**（8,34 GiB）。下载支持续传：中断的安装会从停下的地方继续，
已经下好的文件不会重新下载。

权重取的是 **int8** 构建 —— 比 bf16（约 9,6 GiB）轻一半，正好是照着消费级显卡设计的。
需要最高精度的人，可以把 workflow 模板里的 `unet_name` 改成
`trellis_2_bf16.safetensors`，并自己把文件补进清单。

文件没有就位之前，模型**不可能处于已启用状态** —— 应用启动时也会检查这一点。

## 许可证

| | |
|---|---|
| 模型权重 | **MIT** |
| 商业使用 | 允许 |
| 必须遵守什么 | 保留许可证文本和版权声明 |
| 许可证原文 | <https://huggingface.co/microsoft/TRELLIS.2-4B>（模型卡片） |
| 生成费用 | 无 —— 由您的显卡计算 |

AI2P 下载的文件是 Comfy-Org 的重新打包
（<https://huggingface.co/Comfy-Org/TRELLIS.2>），它同样是 MIT。去背景用的是 BiRefNet
（<https://huggingface.co/ZhengPeng7/BiRefNet>），也是 MIT。许可证已于 2026.08.27
按模型卡片核对；开放模型的许可证很少变，但在商业发布前请再核对一次卡片。

另外：**ComfyUI 本身以 GPL-3.0 发布**。AI2P 把它作为外部程序启动，通过 HTTP 与它通信，
因此这个许可证不会传染到您的产品上 —— 但如果您把内含 ComfyUI 的整套东西交给别人，
GPL 的条款就适用于它。

## 硬件要求

| | |
|---|---|
| 显卡 | NVIDIA，**12 GB 显存起**（16 GB 才宽裕） |
| NVIDIA 驱动 | **580 或更新** —— 驱动太旧时 torch 会无声崩溃 |
| 磁盘空间 | ~13 GB（权重 + ComfyUI 包） |
| 内存 | 16 GB 起 |
| 操作系统 | Windows（ComfyUI 便携版）；在 Linux 上 ComfyUI 需要手动安装 |

显存那个数字是**估算**，不是实测：它是按 int8 权重和两个 VAE 的大小算出来的。
内存不够时，请调低 workflow 模板中 `Trellis2UpsampleStage` 的 `target_resolution`
（默认 1536）。

## 要等多久

图里有四级 —— 结构、形状、提升分辨率和贴图 —— 所以一个模型算下来明显比
Hunyuan3D 慢：在现代显卡上是几分钟到几十分钟。配置档中作业超时设为 90 分钟
（`params.timeoutMinutes`）。

生成进度可以在任务卡片的**作业控制台**中看到：ComfyUI 自己的输出连同进度条
都会流到那里。

## 生成参数

在模型配置档中修改（「连接配置档」按钮）：

| 参数 | 默认值 | 含义 |
|---|---|---|
| `steps` | 20 | **形状**那一级的步数 —— 质量的主要调节杆 |
| `timeoutMinutes` | 90 | 等待结果多久 |
| `width` / `height` | 1024 | 对结果**没有**影响：3D 没有画面 |
| `negative` | 空 | 不起作用 —— 图里没有文本编码器 |

另外三级用的是官方模板里的步数（12）：要改只能改 workflow 本身 ——
它们对结果影响不大，花的时间却一样多。

**模板里刻意没有的东西。** ComfyUI 的官方模板在做出网格之后还会做 UV 展开，
并烘焙整套 PBR 贴图（`UnwrapMesh` → `BakeTextureFromVoxel`、
`BakeNormalMapFromMesh`、`BakeAmbientOcclusion` → `ApplyTextureToMesh`）。
我们的模板里没有这一支：那是另外十来个节点和 2048–4096 分辨率的烘焙，
而没有显卡就没法验证它们。颜色仍然是有的 —— 它由网格顶点携带（`PaintMesh`）。
需要 PBR 贴图的人，可以把这一支补进自己的 workflow：所有节点 ComfyUI 都有。

## LoRA 训练

**不支持，而且这是诚实的拒绝，不是没做完。** 今天还没有公开的 3D 架构 LoRA 训练器：
AI2P 全部适配器训练所依赖的 [musubi-tuner](https://github.com/kohya-ss/musubi-tuner)
只会图片和视频。所以配置档里设了 `lora.supported: false`，模型表单会用文字说明原因。

这里保持物体在各帧之间一致，靠的是另一种办法：把同一张角色参考图（项目对象，
引用 `@obj:`）送进输入 —— 同样的图片加同样的 `seed`，出来的模型就是一样的。

## 常见错误

* **「模型未安装」** —— 文件没有下全；请打开「安装」，窗口会显示还剩多少。
* **没有图片作业就拒绝启动** —— 这是设计如此：这个模型只能从图像出发。
  请给出指向参考帧的 `@obj:` 引用或文件路径。
* **显存不足** —— 请调低 `Trellis2UpsampleStage` 的 `target_resolution`。
* **物体出来是残缺的** —— 去背景挑错了主体；请给一张目标物体单独、完整入画的图片。
* **进程无声无息地消失** —— 几乎总是 NVIDIA 驱动太旧（见上）。

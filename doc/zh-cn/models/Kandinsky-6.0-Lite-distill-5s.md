# Kandinsky-6.0-Lite-distill-5s

**部署方式：** 本地（在这台计算机上运行）
**连接：** `provider: comfyui`，`baseUrl: http://127.0.0.1:8188`，模型 `Kandinsky-6.0-Lite-distill-5s`
**API 密钥：** 不需要——服务器在本地启动，无需授权
**功能：** 文本 → 视频（5 秒，864×480，24 帧/秒），技能 `video-generate` 80

Kandinsky 6.0 Video 是 Kandinsky Lab（Sber）于 2026 年 9 月发布的开放模型系列：Lite（30 亿参数）和 Pro（290 亿参数）。本记录使用 Lite 的**蒸馏**版本：只需 10 步而不是 50 步即可生成片子，质量几乎不降。片子只根据任务描述生成。

## 没有声音

模型本身会同时生成视频和声音（44 kHz，口型同步的语音），但声音要用 MMAudio `v1-44.pth` 权重解码，而该权重采用非商业许可证 CC-BY-NC-4.0。AI2P 不安装此类权重，因此本记录输出的是**没有音轨的片子**。提示词中的音频部分（`<AUDCAP>…`）不起作用。

## 不需要密钥，需要安装

ComfyUI 核心中没有 Kandinsky 6：它的节点来自扩展 https://github.com/kandinskylab/kandinsky-6（`comfyui` 文件夹，MIT）。模型表单中的「安装」按钮会完成全部工作：

1. `comfyui` 包——ComfyUI 便携版（需要 **0.38 或更高版本**；旧版请更新）；
2. `kandinsky6` 包——扩展本身：提交的压缩包、安装到 ComfyUI 内置 Python 的 `pip` 依赖，以及复制到 `ComfyUI/custom_nodes/kandinsky6` 的节点；
3. 权重放入模型仓库的 `Kandinsky-6` 组：

| 文件 | 来源 | 大小 |
|---|---|---|
| `Kandinsky-6.0-Lite-distill-5s.safetensors` | https://huggingface.co/kandinskylab/Kandinsky-6.0-Lite-distill-5s-Diffusers | 5.9 GiB |
| `qwen_2.5_vl_7b.safetensors` | https://huggingface.co/Comfy-Org/Qwen-Image_ComfyUI | 15.4 GiB |
| `clip_l.safetensors` | https://huggingface.co/comfyanonymous/flux_text_encoders | 0.2 GiB |
| `hunyuan_video_vae_bf16.safetensors` | https://huggingface.co/Kijai/HunyuanVideo_comfy | 0.5 GiB |

合计约 **22.1 GiB**。所有仓库都是公开的（`gated=false`），不需要 HuggingFace 令牌。模型只有安装后才能启用。

## 许可证

Kandinsky 6.0 权重采用 **MIT** 许可证（模型卡 https://huggingface.co/kandinskylab/Kandinsky-6.0-Lite-distill-5s-Diffusers，2026 年 10 月 14 日核对），允许商业使用。ComfyUI 扩展同样是 MIT。Qwen2.5-VL 编码器为 Apache 2.0。本记录不使用非商业的 MMAudio 声音权重（见「没有声音」）。

## 价格

免费：模型在您的显卡上运行，无需向任何人付费——只消耗电力和磁盘空间。

## 硬件要求

支持 CUDA 的 NVIDIA 显卡，参考配置为 16 GB 以上显存和 32 GB 以上内存（ComfyUI 会把部分权重卸载到内存，内存更少也能运行，只是更慢）。在 RTX 4090 级别的显卡上，一个片子大约需要 3–8 分钟。

## 生成参数

配置文件：`width` 864，`height` 480，`length` 121 帧（24 帧/秒下 5 秒；帧数须为 4n+1），`steps` 10，CFG 1，采样器 euler/simple——这是 Kandinsky Lab 为蒸馏版本推荐的设置。`negative` 是官方模板中的反向提示词。该模型不支持 LoRA 训练（`lora.supported: false`）。

## 常见错误

* **「Cannot execute because node Kandinsky6… does not exist」**——扩展未加载：重新安装 `kandinsky6` 包并重启 ComfyUI；确认 ComfyUI 版本不低于 0.38。
* **显存不足**——在配置文件中减小 `width`/`height`（16 的倍数），或用 Lite 代替 Pro。
* **片子没有声音**——这是有意为之（见「没有声音」）。

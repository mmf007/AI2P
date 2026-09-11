# Gemini-3.8-Flash

**部署位置:** 云端（Google，通过 OpenAI 兼容层）
**接入方式:** `provider: openai-compatible`，
`baseUrl: https://generativelanguage.googleapis.com/v1beta/openai`，模型 `gemini-3.8-flash`
**密钥引用:** `google.apiKey`

Google 于 2026-09-02 发布该模型；它通过任务 T-216-S0（2026-09-11 的厂商巡检）进入
AI2P 模型目录。标识符 `google/gemini-3.8-flash`、上下文长度、价格与模态均已通过对公开目录
OpenRouter 的请求核对。

延续 **Gemini-3.7-Flash** 系列：上下文 1 048 576 令牌，回复最多 65 536 令牌，每百万令牌 0,75 $ 和
3,75 $。技能评分 81–88。

接受的输入：文本与 Markdown、源代码、图像、PDF、音频、视频。

## 如何获取密钥

**密钥和其余 Gemini 是同一个**（`google.apiKey`）。如果它已经输入过 ——
这个模型自己就能用了。

如果还没有密钥：

1. 打开 [Google AI Studio](https://aistudio.google.com/apikey) 并用 Google 账号登录。
2. 点 **Create API key**，选择一个 Google Cloud 项目。
3. 复制密钥（以 `AIza` 开头）。
4. 如果任务又长又频繁，请给项目绑定计费。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → Gemini-3.8-Flash →「设置 API 密钥」**。

## 限制与价格

| | |
|---|---|
| 上下文 | 1 048 576 令牌 |
| 最大响应 | 65 536 令牌 |
| 输入价格 | 每 100 万令牌 0,75 $ |
| 输出价格 | 每 100 万令牌 3,75 $ |

名录里记的是 2026.09.11
公开模型目录的值。在花大钱之前，请对照
[Gemini API Pricing](https://ai.google.dev/pricing) 核对。

**模型标识符未经真实调用验证** —— 请向
`https://generativelanguage.googleapis.com/v1beta/openai` 发送 `GET /models` 请求核对。

## 许可证

| | |
|---|---|
| 生成结果的条款 | Google **不主张权利**（「Google won't claim ownership over generated content」） |
| 商业使用 | 允许 |
| 必须遵守什么 | 遵守 Prohibited Use Policy；法律可能要求告知您的用户内容由 AI 生成 |
| 条款原文 | <https://ai.google.dev/gemini-api/terms> |
| 生成费用 | 按令牌计费 —— 费率见「限制与价格」一节 |

权重不公开 —— 条款说的是**生成结果**。

资费之间有个重要差别：在**付费**接入上（AI2P 走的正是付费密钥）Google 不会把您的
请求和响应用于改进自家产品，只为违规检查有限期地保存；在免费额度上则会使用。
同样的结果 Google 也有权给别人 —— 独占性是不保证的。

条款于 2026.09.11 核对。

## 硬件要求

没有任何要求：计算在提供商一侧。需要能访问
`generativelanguage.googleapis.com` 的互联网连接。

## 常见错误

* **404「model not found」** —— id 变了；请用 `GET /models` 列表核对。
* **429「resource exhausted」** —— 撞上了免费档的频率上限。
* **质量低于预期** —— 任务超出了这个模型的定位；请把项目的
  「价格 ↔ 质量」滑块推向质量，或者手工指定执行者。

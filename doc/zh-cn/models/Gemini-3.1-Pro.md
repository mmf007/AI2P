# Gemini-3.1-Pro

> **该记录已于 2026-09-11 停用（任务 T-216-S0，厂商巡检）。** 同一系列中比它更新的版本
> 已超过三个（3.5、3.6、3.7、3.8），而它的标识符也已不在公开模型目录中。记录仍
> 保留在目录里：执行者、既往任务和报告都引用它。可以通过「启用」复选框重新打开：
> **设置 → 参考目录 → AI 模型**。

**部署位置:** 云端（Google，通过 OpenAI 兼容层）
**接入方式:** `provider: openai-compatible`，
`baseUrl: https://generativelanguage.googleapis.com/v1beta/openai`，模型 `gemini-3.1-pro`
**密钥引用:** `google.apiKey`

Gemini 产品线里的主力：技能评分 86–91，价格是每百万令牌 2 $ 和 12 $ ——
比 Gemini-3-Ultra 便宜五分之四，上下文
1 048 576 令牌。强项是摘要、翻译和数据分析。

输入接受文本、源码、图片、PDF，还有**音频和视频**
（`audio/*`、`video/*`）—— 任务材料可以是录音录像。

## 如何获取密钥

**密钥和其余 Gemini 是同一个**：三条记录都引用 `google.apiKey`。
如果它已经输入过 —— 这个模型自己就能用了。

如果还没有密钥：

1. 打开 [Google AI Studio](https://aistudio.google.com/apikey) 并用 Google 账号登录。
2. 点 **Create API key**，选择一个 Google Cloud 项目。
3. 复制密钥（以 `AIza` 开头）。
4. 给项目绑定计费 —— 免费档上长任务会撞上 429。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → Gemini-3.1-Pro →「设置 API 密钥」**。

## 限制与价格

| | |
|---|---|
| 上下文 | 1 048 576 令牌 |
| 最大响应 | 65 536 令牌 |
| 输入价格 | 每 100 万令牌 2,00 $ |
| 输出价格 | 每 100 万令牌 12,00 $ |

**模型标识符未经真实调用验证** —— 它取自模型目录，去掉了厂商前缀。
请向 `https://generativelanguage.googleapis.com/v1beta/openai` 发送
`GET /models` 请求核对。Google 这一层 OpenAI 兼容的完整程度同样没有用真实调用
验证过：主要调用是覆盖的，个别字段的行为可能不同。最新价格见
[Gemini API Pricing](https://ai.google.dev/pricing)。

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

条款于 2026.08.27 核对。

## 硬件要求

没有任何要求：计算在提供商一侧。需要能访问
`generativelanguage.googleapis.com` 的互联网连接。

## 常见错误

* **404「model not found」** —— id 变了；请用 `GET /models` 列表核对。
* **长任务时出现 429** —— 项目没有绑定计费。
* **响应为空，原因是 `length`** —— 请调大配置档中的 `params.maxTokens`
  （默认 32000）。

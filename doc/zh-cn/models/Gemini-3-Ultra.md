# Gemini-3-Ultra

> **该记录已于 2026-09-11 停用（任务 T-216-S0，厂商巡检）。** 同一系列中比它更新的版本
> 已超过三个（3.1、3.5、3.6、3.7、3.8），而它的标识符也已不在公开模型目录中。记录仍
> 保留在目录里：执行者、既往任务和报告都引用它。可以通过「启用」复选框重新打开：
> **设置 → 参考目录 → AI 模型**。

**部署位置:** 云端（Google，通过 OpenAI 兼容层）
**接入方式:** `provider: openai-compatible`，
`baseUrl: https://generativelanguage.googleapis.com/v1beta/openai`，模型 `gemini-3-ultra`
**密钥引用:** `google.apiKey`

**名录中上下文最大的一个 —— 2 000 000 令牌**，是 Claude 和 GPT 的两倍。
留着它就是为了这个：描述极长、聊天记录极长或者附了一大堆材料的任务能整段送进去。
技能评分 90–94，强项是摘要和数据分析。价格不低：每百万令牌 10 $ 和 30 $。

输入不仅接受文本和图片，还接受**音频和视频**（`audio/*`、`video/*`），
所以材料以录音录像形式送来的任务也适合它。

## 如何获取密钥

1. 打开 [Google AI Studio](https://aistudio.google.com/apikey) 并用 Google 账号登录。
2. 点 **Create API key**，选择或新建一个 Google Cloud 项目。
3. 复制密钥（以 `AIza` 开头）。
4. 要用付费资费，请给项目绑定计费：免费档会限制请求频率，长任务会让代理撞上 429。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → Gemini-3-Ultra →「设置 API 密钥」**。

密钥与名录中所有 Gemini 共用 —— Gemini-3.1-Pro 和
Gemini-3.7-Flash 引用的是同一个 `google.apiKey`。

## 限制与价格

| | |
|---|---|
| 上下文 | 2 000 000 令牌 |
| 最大响应 | 65 536 令牌 |
| 输入价格 | 每 100 万令牌 10,00 $ |
| 输出价格 | 每 100 万令牌 30,00 $ |

有两条说明比数字更重要：

* **模型标识符未经真实调用验证**，而且在 2026.08.17 的公开模型目录里根本没有
  Ultra 这个版本 —— id 和价格取自市场综述。请向
  `https://generativelanguage.googleapis.com/v1beta/openai` 发送 `GET /models` 核对。
* **Google 这一层 OpenAI 兼容的完整程度未经真实调用验证。**`/v1beta/openai`
  这一层覆盖了主要调用，但个别字段（工具、流式输出）的行为可能与 OpenAI 不同。

最新价格见 [Gemini API Pricing](https://ai.google.dev/pricing)。

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

* **404「model not found」** —— id 不对；请用 `GET /models` 列表核对。
* **长任务时出现 429** —— 走的是免费档；请绑定计费。
* **用工具时报错** —— 兼容层的特点；可以试试
  Gemini-3.1-Pro 或者别的提供商。

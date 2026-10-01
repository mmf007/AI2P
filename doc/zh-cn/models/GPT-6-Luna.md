# GPT-6-Luna

**部署位置:** 云端（OpenAI API，OpenAI 兼容）
**接入方式:** `provider: openai-compatible`，`baseUrl: https://api.openai.com/v1`，
模型 `gpt-6-luna`
**密钥引用:** `openai.apiKey`

OpenAI 于 2026-09-22 发布该模型；它通过任务 T-347-S0（2026-09-23 的厂商巡检）进入
AI2P 模型目录。标识符 `openai/gpt-6-luna`、上下文长度、价格与模态均已通过对公开目录
OpenRouter 的请求核对。

延续 **GPT-6-Astra** 系列：上下文 1 050 000 令牌，回复最多 128 000 令牌，每百万令牌 0,10 $ 和
0,50 $。技能评分 83–90。

接受的输入：文本与 Markdown、源代码、图像、PDF。

## 如何获取密钥

1. 在 [platform.openai.com](https://platform.openai.com/) 上注册。
2. 充值余额：**Settings → Billing → Add to credit balance**。没有余额，
   请求会以 429 被拒绝。
3. 打开 [platform.openai.com/api-keys](https://platform.openai.com/api-keys) →
   **Create new secret key**，起个名字。
4. 复制那个值 —— 它**只显示一次**。密钥以 `sk-` 开头。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → GPT-6-Luna →「设置 API 密钥」**。

密钥与 GPT-5.6-Terra 共用 —— 两个模型都引用 `openai.apiKey`。
密钥属于组织：用组织的密钥加密，并复制到组织的所有服务器上（技术规格第 10 章）。

## 限制与价格

| | |
|---|---|
| 上下文 | 1 050 000 令牌 |
| 最大响应 | 128 000 令牌 |
| 输入价格 | 每 100 万令牌 0,10 $ |
| 输出价格 | 每 100 万令牌 0,50 $ |

**模型标识符未经真实调用验证** —— 它取自模型目录，并且去掉了厂商前缀。
第一次派活之前，请向 `https://api.openai.com/v1` 发送 `GET /models` 请求核对。
最新价格见 [OpenAI Pricing](https://openai.com/api/pricing/)。

## 许可证

| | |
|---|---|
| 生成结果的条款 | **结果归您**：「Customer owns all Output」，OpenAI 把自己在结果上的权利转让给您 |
| 商业使用 | 允许 |
| 必须遵守什么 | 遵守 Usage Policies；记住结果的唯一性是不保证的 —— 同样的回答可能也给了别人 |
| 条款原文 | <https://openai.com/policies/services-agreement/> |
| 生成费用 | 按令牌计费 —— 费率见「限制与价格」一节 |

权重不公开，没有什么可授权的 —— 条款说的是**生成结果**。

送进 API 的内容，在您没有明确允许之前，OpenAI **不会用于改进自己的服务**；
面向消费者的 ChatGPT 则不是这样。

2026.09.23 核对：关于结果归属的措辞（「you … own the Output. We hereby
assign to you all our right, title, and interest, if any, in and to Output」）是逐字
从 OpenAI 条款中读到的；上面链接的 API 协议里也是同一段措辞。

## 硬件要求

没有任何要求：计算在提供商一侧。需要能访问 `api.openai.com` 的互联网连接。

## 常见错误

* **404「model not found」** —— id 变了；请用 `GET /models` 列表核对。
* **429「insufficient_quota」** —— 余额没充值，或者组织的限额用完了。
* **响应为空，原因是 `length`** —— 模型把响应限额用在推理上了；
  请调大配置档中的 `params.maxTokens`（默认 32000）。

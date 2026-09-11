# Claude-Sonnet-5

**部署位置:** 云端（Anthropic API）
**接入方式:** `provider: anthropic`，模型 `claude-sonnet-5`
**密钥引用:** `anthropic.apiKey`

Claude 5 系列的主力马：质量接近 Claude-Opus-5.0（技能评分 89–93 对 95–98），而价格低一半
并且**恒定** — 每 100 万输入令牌 2 $、每 100 万输出令牌 10 $，没有按时段的折扣。当 Opus
显得过剩、而 Claude-Haiku-4.5 又力不从心时，它是合理的选择。

## 如何获取密钥

1. 在 [console.anthropic.com](https://console.anthropic.com/) 上建立组织。
2. 充值余额：**Billing → Add credits**。没有余额，请求会被拒绝。
3. 打开 **API keys → Create Key**，起个名字。
4. 复制那个值 — 它**只显示一次**。密钥以 `sk-ant-` 开头。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → Claude-Sonnet-5 → 「设置 API 密钥」**。

该密钥与所有 Anthropic 模型共用 — Claude-Fable-5、Claude-Opus-5.0、Claude-Haiku-4.5：
它们全都引用 `anthropic.apiKey`。输入一次 — 全都能用。

密钥属于组织：用组织的密钥加密，并复制到组织的所有服务器上（技术规格第 10 章）。

## 限制与价格

| | |
|---|---|
| 上下文 | 1 000 000 令牌 |
| 最大响应 | 128 000 令牌 |
| 输入价格 | 2,00 $ / 100 万令牌 |
| 输出价格 | 10,00 $ / 100 万令牌 |

配置档中设有 `params.maxTokens: 16000` 和 `effort: high` — 这是对**单次响应**的限制，
而不是对上下文的限制；如果结果以 `length` 为原因被截断，请把它调高。

**模型标识符未经真实调用验证。** 在下达第一个作业之前，请用向提供商 API 发送
`GET /models` 的方式核对它：Anthropic 的检查点名称会变动，而错误的 id 会在执行者那里
直接给出 404。最新价格见 [Anthropic Pricing](https://www.anthropic.com/pricing)。

## 许可证

| | |
|---|---|
| 对生成结果的条款 | **结果归您**：Anthropic 把它对 Outputs 的全部权利让与您（Commercial Terms，B 节） |
| 商业使用 | 允许 — 这是面向组织的条款，消费者条款不适用于 API |
| 必须做到 | 遵守 Usage Policy；不得基于该服务构建竞品、用它训练竞争模型、转售访问权 |
| 条款文本 | <https://www.anthropic.com/legal/commercial-terms> （2025-06-17 版） |
| 生成费用 | 按令牌计 — 费率见「限制与价格」一节 |

模型权重是封闭的，不会分发给任何人 — 这里没有什么可授权的，因此条款针对的是**生成结果**，
而不是权重。

按同一份条款，Anthropic **不会用送进 API 的内容训练模型**（「Anthropic may not train
models on Customer Content from Services」）。同一模型的订阅版变体（带 `_cli` 后缀的记录）
条款**不同** — 那里适用的是消费者条款，在您到账户设置中拒绝之前，都会用您的材料进行训练。

条款已按其文本于 2026-08-27 核对；提供商有权修改 — 商业发布前请再打开链接看一次。

## 硬件要求

没有任何要求：计算在提供商一侧进行。需要能访问 `api.anthropic.com` 的互联网连接。

## 常见问题

* **401 / 「invalid x-api-key」** — 密钥输入时带了空格或者被截断。
* **400 「credit balance is too low」** — Anthropic 组织的余额没有充值。
* **响应为空，原因是 `length`** — 模型配置档中的 `params.maxTokens` 太小。
* **作业比预期贵** — 请按两个数字一起算：每一步代理的动作都会把很长的聊天历史送进输入。

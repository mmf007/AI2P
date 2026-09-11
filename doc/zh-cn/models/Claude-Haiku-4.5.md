# Claude-Haiku-4.5

**部署位置:** 云端（Anthropic API）
**接入方式:** `provider: anthropic`，模型 `claude-haiku-4-5`
**密钥引用:** `anthropic.apiKey`

名录中最便宜的 Claude：每百万令牌 1 $ 和 5 $，而 Claude-Sonnet-5 是 2 $ 和 10 $。技能评分
70–80 意味着**简短的日常活儿**：翻译、摘要、文本小修改、简单检查。复杂代码和需求分析不该
交给它，那是 Sonnet 和 Opus 的事。

第二个限制是上下文只有 **200 000 令牌**，而不是一百万：描述特别长或者聊天历史很长的任务，
这个模型撑不下来。

## 如何获取密钥

1. 在 [console.anthropic.com](https://console.anthropic.com/) 上建立组织。
2. 充值余额：**Billing → Add credits**。
3. 打开 **API keys → Create Key**，起个名字。
4. 复制那个值 — 它**只显示一次**（以 `sk-ant-` 开头）。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → Claude-Haiku-4.5 → 「设置 API 密钥」**。

该密钥与所有 Anthropic 模型共用（`anthropic.apiKey`）：输入一次 — Fable、Opus、Sonnet
和 Haiku 就都能用了。

## 限制与价格

| | |
|---|---|
| 上下文 | 200 000 令牌 |
| 最大响应 | 64 000 令牌 |
| 输入价格 | 1,00 $ / 100 万令牌 |
| 输出价格 | 5,00 $ / 100 万令牌 |

**模型标识符未经真实调用验证。** 在下达第一个作业之前，请用向提供商 API 发送
`GET /models` 的方式核对它。最新价格见
[Anthropic Pricing](https://www.anthropic.com/pricing)。

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

* **输入太长** — 200 000 令牌用完得比想象的快：把大块材料放进文件，描述里给出链接。
* **结果明显不如预期** — 任务超出了模型的定位；请手动指定执行者，或者把项目的
  「价格 ↔ 质量」滑块推向质量一侧。
* **401 / 「invalid x-api-key」** — 密钥输入时带了空格或者被截断。

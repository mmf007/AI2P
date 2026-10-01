# Claude-Opus-5.5

**部署位置:** 云端（Anthropic 提供商 API）
**接入方式:** `provider: anthropic`，模型 `claude-opus-5-5`
**密钥引用:** `anthropic.apiKey`

Anthropic 于 2026-09-22 发布该模型；它通过任务 T-347-S0（2026-09-23 的厂商巡检）进入
AI2P 模型目录。标识符 `anthropic/claude-opus-5.5`、上下文长度、价格与模态均已通过对公开目录
OpenRouter 的请求核对。

延续 **Claude-Opus-5.0** 系列：上下文 1 000 000 令牌，回复最多 128 000 令牌，每百万令牌 4,00 $ 和
20,00 $。技能评分 93–98。

接受的输入：文本与 Markdown、源代码、图像、PDF。

## 如何获取密钥

**密钥和 Claude-Fable-5 的完全相同** — 两个模型都访问同一个提供商，并引用同一个密钥
`anthropic.apiKey`。如果已经为其中一个输入过密钥，另一个会自行开始工作。

如果还没有密钥：

1. 在 [Anthropic Console](https://console.anthropic.com/) 注册。
2. 充值余额：**Plan & Billing → Add credits**。
3. **Settings → API keys → Create Key**，复制那个值（只显示一次，以 `sk-ant-` 开头）。
4. 在 AI2P 中：**设置 → 名录 → AI 模型 → Claude-Opus-5.5 → 「设置 API 密钥」**。

密钥属于组织，并加密后复制到组织的各服务器上（技术规格第 10 章）。

## 限制与价格

| | |
|---|---|
| 上下文 | 1 000 000 令牌 |
| 最大响应 | 128 000 令牌 |
| 输入价格 | 4,00 $ / 100 万令牌 |
| 输出价格 | 20,00 $ / 100 万令牌 |

比 Claude-Fable-5 便宜一倍半。最新价格见
[Anthropic Pricing](https://www.anthropic.com/pricing)。

**模型标识符未经真实调用验证。** 在下达第一个作业之前，请用向提供商 API 发送
`GET /models` 的方式核对它：Anthropic 的检查点名称会变动，而错误的 id 会在执行者那里
直接给出 404。

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

条款已按其文本于 2026-09-23 核对；提供商有权修改 — 商业发布前请再打开链接看一次。

## 硬件要求

没有任何要求：计算在提供商一侧进行。需要能访问 `api.anthropic.com` 的互联网连接。

## 两者之间何时选哪个

* **Claude-Fable-5** — 流程中的关键任务、需求分析、规划、复杂代码。
* **Claude-Opus-5.5** — 其余的一切：修改、文档、翻译、评审。

这个选择也可以不用手动做：项目里有一个 **价格 ↔ 质量** 滑块，执行者自动挑选会同时考虑
模型的技能评分和它的价格（技术规格 2.7 节）。

## 无需密钥的替代方案

**Claude-Opus-5.0_cli** — 同一个模型，通过 Claude Code CLI 按订阅使用，不需要 API 密钥。

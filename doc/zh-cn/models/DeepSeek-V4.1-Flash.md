# DeepSeek-V4.1-Flash

**部署位置:** 云端（DeepSeek API，OpenAI 兼容）
**接入方式:** `provider: openai-compatible`，`baseUrl: https://api.deepseek.com`，
模型 `deepseek-v4.1-flash`
**密钥引用:** `deepseek.apiKey`

DeepSeek 于 2026-09-10 发布该模型；它通过任务 T-216-S0（2026-09-11 的厂商巡检）进入
AI2P 模型目录。标识符 `deepseek/deepseek-v4.1-flash`、上下文长度、价格与模态均已通过对公开目录
OpenRouter 的请求核对。

延续 **DeepSeek-V4-Flash** 系列：上下文 1 048 576 令牌，回复最多 384 000 令牌，每百万令牌 0,15 $ 和
0,6 $。技能评分 80–86。

接受的输入：文本与 Markdown、源代码、图像。

## 如何获取密钥

**密钥和 DeepSeek-V4-Pro 的完全相同**：两个模型都引用 `deepseek.apiKey`。如果已经输入过
它 — 这个模型会自行开始工作。

如果还没有密钥：

1. 在 [platform.deepseek.com](https://platform.deepseek.com/) 注册。
2. 充值余额（**Top up**）。
3. **API keys → Create new API key**，复制那个值（只显示一次）。
4. 在 AI2P 中：**设置 → 名录 → AI 模型 → DeepSeek-V4.1-Flash → 「设置 API 密钥」**。

## 限制与价格

| | |
|---|---|
| 上下文 | 1 048 576 令牌 |
| 最大响应 | 384 000 令牌 |
| 输入价格 | 0,15 $ / 100 万令牌 |
| 输出价格 | 0,60 $ / 100 万令牌 |

数字已更新：上下文从 128 000 增长到一百万，最大响应从 8 192 增长到 384 000 令牌，价格降到
原来的二分之一至四分之一（原为 0,27 $ 和 1,10 $）。在同样的上下文下，输出现在比
DeepSeek-V4-Pro **便宜七倍**。

配置档中设有 `params.maxTokens: 384000` — 这是这个模型单次响应的上限。

**模型标识符未经真实调用验证** — 请用向 `https://api.deepseek.com` 发送 `GET /models` 的
方式核对它。最新价格见
[DeepSeek Pricing](https://api-docs.deepseek.com/quick_start/pricing)。

## 许可证

| | |
|---|---|
| 对生成结果的条款 | **结果归您**：「We assign any rights, title, and interests — if any — in the Outputs … to you」（4.2 条） |
| 商业使用 | 明确而宽泛地允许：个人使用、研究、开发衍生产品，甚至用于训练其他模型（蒸馏） |
| 必须做到 | 向您的用户披露内容由 AI 生成（8.1 条）；未经许可不得使用 DeepSeek 品牌 |
| 条款文本 | <https://cdn.deepseek.com/policies/en-US/deepseek-open-platform-terms-of-service.html> |
| 生成费用 | 按令牌计 — 费率见「限制与价格」一节 |

云端记录的权重不会交给您，因此这里说的是**生成结果**。

这是名录中云端记录里最慷慨的条款：允许用结果训练其他模型这一点直接写在文本里，而在大多数
提供商那里恰恰是被禁止的。义务只有一条，而且很容易漏掉 — 向最终用户标明文本是 AI 做的。

条款已按其文本于 2026-09-11 核对。

## 硬件要求

没有任何要求：计算在提供商一侧进行。需要能访问 `api.deepseek.com` 的互联网连接。

## 什么时候用它

项目的 **价格 ↔ 质量** 滑块推向价格一侧 — 执行者自动挑选就会自己选中这个模型
（技术规格 2.7 节）。如果任务需要细致，请手动指定执行者，或者把滑块推向质量一侧。

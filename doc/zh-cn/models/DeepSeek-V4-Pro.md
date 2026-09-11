# DeepSeek-V4-Pro

**部署位置:** 云端（DeepSeek API，OpenAI 兼容）
**接入方式:** `provider: openai-compatible`，`baseUrl: https://api.deepseek.com`，
模型 `deepseek-v4-pro`
**密钥引用:** `deepseek.apiKey`

水平不错的廉价模型：代码和文本的技能评分为 84–89（满分 100），
价格大约只有 Claude-Fable-5 的二十分之一。大批量日常工作的好选择。

## 如何获取密钥

1. 在 [platform.deepseek.com](https://platform.deepseek.com/) 上注册。
2. 充值余额：**Top up**（银行卡支付）。没有余额，请求会被拒绝。
3. 打开 **API keys → Create new API key**，起个名字。
4. 复制那个值 —— 它**只显示一次**。密钥以 `sk-` 开头。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → DeepSeek-V4-Pro →「设置 API 密钥」**。

密钥与 DeepSeek-V4-Flash 共用 —— 两个模型都引用
`deepseek.apiKey`。输入一次，两个都能用。

密钥属于组织：用组织的密钥加密，并复制到组织的所有服务器上（技术规格第 10 章）。

## 限制与价格

| | |
|---|---|
| 上下文 | 1 000 000 令牌 |
| 最大响应 | 65 536 令牌 |
| 输入价格 | 每 100 万令牌 0,66 $ |
| 输出价格 | 每 100 万令牌 1,98 $ |

数字已按检查点 **V4-Pro-0813** 更新：上下文从 128 000 涨到了一百万，
最大响应从 8 192 涨到 65 536 令牌，输出甚至还便宜了（原本是 0,55 $ 和
2,19 $）。原先「上下文比 Anthropic 小一个数量级」的限制已经消失：现在
它和 Claude-Sonnet-5 一样大。

配置档里写着 `params.maxTokens: 32768` —— 这是**单次响应**的限制。如果
结果以 `length` 为原因被截断，请把它调高（模型上限是 65 536）或者把任务拆开。

**模型标识符未经真实调用验证** —— 请向 `https://api.deepseek.com` 发送 `GET /models`
请求核对。最新价格见
[DeepSeek Pricing](https://api-docs.deepseek.com/quick_start/pricing)。

## 许可证

| | |
|---|---|
| 生成结果的条款 | **结果归您**：「We assign any rights, title, and interests — if any — in the Outputs … to you」（4.2 条） |
| 商业使用 | 明确而宽泛地允许：个人使用、研究、开发衍生产品，甚至训练其他模型（蒸馏） |
| 必须遵守什么 | 向您的用户披露内容由 AI 生成（8.1 条）；未经许可不得使用 DeepSeek 品牌 |
| 条款原文 | <https://cdn.deepseek.com/policies/en-US/deepseek-open-platform-terms-of-service.html> |
| 生成费用 | 按令牌计费 —— 费率见「限制与价格」一节 |

云端记录的权重不会交给您，所以说的是**生成结果**。

这是名录中云端记录里最慷慨的条款：允许用结果训练其他模型这一点直接写在了文本里，
而大多数提供商恰恰是禁止的。义务只有一条，而且很容易漏掉 —— 向最终用户标明文本是
AI 做的。

条款于 2026.08.27 按原文核对。

## 硬件要求

没有任何要求：计算在提供商一侧。需要能访问 `api.deepseek.com` 的互联网连接。

## 常见错误

* **402 /「Insufficient Balance」** —— 余额没有充值。
* **响应为空，原因是 `length`** —— 模型把响应限额用在推理上了；
  请调大模型配置档中的 `params.maxTokens`。
* **输入太长** —— 一百万令牌看似用不完，但聊天记录在代理每一步都会进入输入；
  请把大块材料放进文件，描述里给出链接。

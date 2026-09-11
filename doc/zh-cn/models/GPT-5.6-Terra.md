# GPT-5.6-Terra

**部署位置:** 云端（OpenAI API，OpenAI 兼容）
**接入方式:** `provider: openai-compatible`，`baseUrl: https://api.openai.com/v1`，
模型 `gpt-5.6-terra`
**密钥引用:** `openai.apiKey`

这条产品线的中档资费：技能评分 85–88，而 GPT-5.6-Sol 是 91–95，
价格却低了五倍，上下文同样是 1 050 000 令牌。旗舰显得多余、但质量又
必须保持在高位的大批量工作，选它很合适。

## 如何获取密钥

**密钥和 GPT-5.6-Sol 是同一个**：两个模型都引用 `openai.apiKey`。
如果它已经输入过 —— 这个模型自己就能用了。

如果还没有密钥：

1. 在 [platform.openai.com](https://platform.openai.com/) 上注册。
2. 充值余额：**Settings → Billing → Add to credit balance**。
3. 打开 [platform.openai.com/api-keys](https://platform.openai.com/api-keys) →
   **Create new secret key**。
4. 复制那个值（只显示一次，以 `sk-` 开头）。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → GPT-5.6-Terra →「设置 API 密钥」**。

## 限制与价格

| | |
|---|---|
| 上下文 | 1 050 000 令牌 |
| 最大响应 | 128 000 令牌 |
| 输入价格 | 每 100 万令牌 1,00 $ |
| 输出价格 | 每 100 万令牌 6,00 $ |

**在花大钱之前一定要核对价格。**各个来源说法不一：市场综述（T-213 报告）给的是
2,50 $ 和 15,00 $，2026.08.17 的公开模型目录给的是 1,00 $ 和 6,00 $。名录里
记的是后一组数字。最新价目见 [OpenAI Pricing](https://openai.com/api/pricing/)。

**模型标识符未经真实调用验证** —— 它取自模型目录，去掉了厂商前缀。
第一次派活之前，请向 `https://api.openai.com/v1` 发送 `GET /models` 请求核对。

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

2026.08.27 核对：关于结果归属的措辞（「you … own the Output. We hereby
assign to you all our right, title, and interest, if any, in and to Output」）是逐字
从 OpenAI 条款中读到的；上面链接的 API 协议里也是同一段措辞。

## 硬件要求

没有任何要求：计算在提供商一侧。需要能访问 `api.openai.com` 的互联网连接。

## 常见错误

* **404「model not found」** —— id 变了；请用 `GET /models` 列表核对。
* **429「insufficient_quota」** —— OpenAI 组织的余额没有充值。
* **账单比估算多出一倍** —— 请按提供商的价目表核对价格（见上），
  并在模型的能力声明中改正它。

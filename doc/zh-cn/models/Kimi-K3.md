# Kimi-K3

**部署位置:** 云端（Moonshot AI，OpenAI 兼容）
**接入方式:** `provider: openai-compatible`，`baseUrl: https://api.moonshot.ai/v1`，
模型 `kimi-k3`
**密钥引用:** `moonshot.apiKey`

在名录补充的时候，它是 **Artificial Analysis 综合指数上最好的开放模型**：
2,8 万亿参数（1040 亿活跃），技能评分 85–90 —— 紧贴闭源旗舰。
上下文 1 048 576 个令牌，输入可以接收图片和视频。
价格是每百万 3 美元和 15 美元：比中国的邻居们贵，但质量也更高。

**许可证是自己的 —— Kimi K3 License，不是 MIT，也不是 Apache。** 权重是开放的，
但商业使用的条款请单独读一遍；这里的「开放」并不等于「随便你怎么用」。

## 如何获取密钥

1. 在 [platform.moonshot.ai](https://platform.moonshot.ai/) 注册。
2. 在 **Billing** 一节里充值。没有余额时请求会被拒绝。
3. 打开[密钥控制台](https://platform.moonshot.ai/console/api-keys) →
   **Create API key**，起个名字。
4. 复制它的值 —— 它**只显示一次**。密钥以 `sk-` 开头。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → Kimi-K3 →「设置 API 密钥」**。

密钥属于组织：用组织的密钥加密，并复制到组织的所有服务器上（技术规格第 10 章）。

## 限制与价格

| | |
|---|---|
| 上下文 | 1 048 576 个令牌 |
| 最大回复 | 65 536 个令牌 |
| 输入价格 | 每 100 万令牌 3,00 $ |
| 输出价格 | 每 100 万令牌 15,00 $ |

**模型标识符没有用实际调用验证过** —— 它取自模型目录，去掉了厂商前缀。
请向 `https://api.moonshot.ai/v1` 发 `GET /models` 请求核对一下。
最新价格请看 Moonshot 的控制台。

## 许可证

| | |
|---|---|
| 对生成结果的条款 | Moonshot **不主张对内容的权利**（「we do not claim ownership of it」） |
| 商业使用 | 允许 |
| 必须遵守什么 | 记住：默认情况下发送和收到的内容可能被用于 Moonshot 模型的改进和训练 —— 要限制这一点需要单独的企业合同 |
| 条款原文 | <https://platform.kimi.ai/docs/agreement/modeluse> |
| 生成费用 | 按令牌计 —— 费率见「限制与价格」一节 |

单说权重，因为这里很容易搞错：Kimi K3 有**自己的许可证**（在
`moonshotai/Kimi-K3` 的卡片里它叫 `kimi-k3`），既不是 MIT 也不是 Apache 2.0。
「开放模型就意味着 MIT」这种流行说法在这里是不对的。对云端记录来说这无关紧要
（您并不会拿到权重），但如果您打算自己把模型跑起来，它就有用了。

条款按其原文核对、权重许可证按模型卡片核对，两项都是 2026.08.27。

## 硬件要求

没有任何要求：计算在提供商一侧。需要能访问 `api.moonshot.ai` 的互联网出口。
权重虽然开放，但 2,8 万亿参数在自己的计算机上是跑不起来的 —— 要在本地干活，
名录里有 270–350 亿参数的模型。

## 常见错误

* **401「invalid api key」** —— 密钥是在中国站点（`moonshot.cn`）创建的，而
  `baseUrl` 指向国际站点；两者不能互换。
* **404「model not found」** —— id 换了；请用 `GET /models` 列表核对。
* **回复为空，原因是 `length`** —— 请调大配置档中的 `params.maxTokens`
  （默认 32000）。

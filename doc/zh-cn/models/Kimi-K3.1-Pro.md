# Kimi-K3.1-Pro

**部署位置:** 云端（Moonshot AI，OpenAI 兼容）
**接入方式:** `provider: openai-compatible`，`baseUrl: https://api.moonshot.ai/v1`，
**密钥引用:** `moonshot.apiKey`

Moonshot AI 于 2026-10-23 发布该模型；它通过任务 T-653-S0（2026-10-29 的厂商巡检）进入
AI2P 模型目录。标识符 `moonshotai/kimi-k3.1-pro`、价格与模态均已通过对公开目录 OpenRouter 的请求核对。

上下文 1 048 576 令牌，回复最多 943 718 令牌，每百万令牌 2,00 $ 和 10,00 $，技能评分 87–92。
目录中最接近的记录是 **Kimi-K3**。

接受的输入：文本与 Markdown、源代码、图像、视频。

## 如何获取密钥

1. 在 [platform.moonshot.ai](https://platform.moonshot.ai/) 注册。
2. 在 **Billing** 一节里充值。没有余额时请求会被拒绝。
3. 打开[密钥控制台](https://platform.moonshot.ai/console/api-keys) →
   **Create API key**，起个名字。
4. 复制它的值 —— 它**只显示一次**。密钥以 `sk-` 开头。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → Kimi-K3.1-Pro →「设置 API 密钥」**。

密钥属于组织：用组织的密钥加密，并复制到组织的所有服务器上（技术规格第 10 章）。

## 限制与价格

| | |
|---|---|
| 上下文 | 1 048 576 个令牌 |
| 最大回复 | 943 718 个令牌 |
| 输入价格 | 每 100 万令牌 2,00 $ |
| 输出价格 | 每 100 万令牌 10,00 $ |

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

关于权重：Moonshot 称 K3.1 Pro 为开放权重模型，但截至 2026-10-29，HuggingFace 上的卡片
`moonshotai/Kimi-K3.1-Pro` 查询不到（返回 401），因此权重许可证尚未核对。前代 Kimi K3
使用自己的许可证（`kimi-k3`），不是 MIT。对云端记录而言这无关紧要——您拿不到权重。

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

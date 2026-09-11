# Mistral-Large-3

**部署位置:** 云端（Mistral AI，法国；OpenAI 兼容）
**接入方式:** `provider: openai-compatible`，`baseUrl: https://api.mistral.ai/v1`，
模型 `mistral-large-2512`
**密钥引用:** `mistral.apiKey`

名录中的欧洲选项：数据在欧盟境内处理，有时候单凭这一点就决定了选谁。
它的长处是**文本和翻译**（评分 81–85），代码明显弱一些（78–82）。
价格适中：每百万令牌 0,50 $ 和 1,50 $，上下文 262 144 个令牌。

请留意**标识符**：Mistral 的标识符是带日期的 ——
`mistral-large-2512`，而不是 `mistral-large-3`。提供商就是这样标记检查点的；
下一个版本出来的时候，配置档里的这一行就得改。

## 如何获取密钥

1. 在 [console.mistral.ai](https://console.mistral.ai/) 注册。
2. 开通付费套餐：**Billing → Add payment method**。免费层的请求频率
   限制很严。
3. 打开 [console.mistral.ai/api-keys](https://console.mistral.ai/api-keys/) →
   **Create new key**，起个名字并设定有效期。
4. 复制它的值 —— 它**只显示一次**。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → Mistral-Large-3 →「设置 API 密钥」**。

密钥属于组织：用组织的密钥加密，并复制到组织的所有服务器上（技术规格第 10 章）。

## 限制与价格

| | |
|---|---|
| 上下文 | 262 144 个令牌 |
| 最大回复 | 65 536 个令牌 |
| 输入价格 | 每 100 万令牌 0,50 $ |
| 输出价格 | 每 100 万令牌 1,50 $ |

**模型标识符没有用实际调用验证过** —— 它取自模型目录，去掉了厂商前缀。
请向 `https://api.mistral.ai/v1` 发 `GET /models` 请求核对一下：
对带日期的检查点来说这尤其重要。最新价格见
[Mistral Pricing](https://mistral.ai/pricing)。

## 许可证

| | |
|---|---|
| 对生成结果的条款 | **结果是您的**：「Customer … owns all Output」，Mistral 把自己的权利转让给您（Commercial ToS 第 3.1 条） |
| 商业使用 | 允许 |
| 必须遵守什么 | 不得把结果冒充为人的作品（第 3.2 条）；不得用生成的图片训练与之竞争的图片生成器（第 3.3 条）；遵守 Usage Policy |
| 条款原文 | <https://legal.mistral.ai/terms/commercial-terms-of-service> |
| 生成费用 | 按令牌计 —— 费率见「限制与价格」一节 |

关于用您的数据训练，条款说得很明确（第 4.2 条）：Mistral **不会**拿它们训练自己的模型 ——
除非您自己打开了训练开关、提交了反馈，或者用了实验性模型
（在 AI Studio 中它们带 `labs` 前缀）。这条名录记录指向的是普通模型，而不是 `labs`。

条款已于 2026.08.27 按其原文核对。

## 硬件要求

没有任何要求：计算在提供商一侧。需要能访问 `api.mistral.ai` 的互联网出口。

## 常见错误

* **404「model not found」** —— 出了新的检查点，旧日期不再提供服务；
  请从 `GET /models` 取当前的 id。
* **429「rate limit exceeded」** —— 没有开通付费套餐。
* **密钥不好使了** —— Mistral 的密钥可能带有效期；请在控制台里检查一下。

# Muse-Spark-1.3

**部署位置:** 云端（Meta，**经由 OpenRouter 网关**）
**接入方式:** `provider: openai-compatible`，`baseUrl: https://openrouter.ai/api/v1`，
模型 `meta/muse-spark-1.3`
**密钥引用:** `openrouter.apiKey`

Meta 于 2026-09-02 发布该模型；它通过任务 T-216-S0（2026-09-11 的厂商巡检）进入
AI2P 模型目录。标识符 `meta/muse-spark-1.3`、上下文长度、价格与模态均已通过对公开目录
OpenRouter 的请求核对。

延续 **Muse-Spark-1.2** 系列：上下文 1 048 576 令牌，回复最多 65 536 令牌，每百万令牌 1,25 $ 和
4,25 $。技能评分 87–91。

接受的输入：文本与 Markdown、源代码、图像、PDF、音频、视频。

## 如何获取密钥

**经由网关的所有模型共用一个密钥** —— Inkling-975B、
Nemotron-3-Ultra、Ling-3.0-Flash 和这一条
都引用同一个 `openrouter.apiKey`。输入一次，四个都能用。

1. 在 [openrouter.ai](https://openrouter.ai/) 注册。
2. 充值：**Credits → Add credits**（银行卡或加密货币）。没有余额时
   只能用带排队的免费版模型。
3. 打开 [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**，起个名字，
   需要的话再给这个密钥设一个消费上限。
4. 复制它的值 —— 它**只显示一次**。密钥以 `sk-or-` 开头。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → Muse-Spark-1.3 →「设置 API 密钥」**。

密钥属于组织：用组织的密钥加密，并复制到组织的所有服务器上（技术规格第 10 章）。

## 限制与价格

| | |
|---|---|
| 上下文 | 1 048 576 个令牌 |
| 最大回复 | 65 536 个令牌 |
| 输入价格 | 每 100 万令牌 1,25 $ |
| 输出价格 | 每 100 万令牌 4,25 $ |

**模型标识符没有用实际调用验证过。** 经由网关的模型，标识符要写成
**带厂商前缀的完整 slug**（`meta/muse-spark-1.3`）—— 少了前缀网关会答 404。
请向 `https://openrouter.ai/api/v1` 发 `GET /models` 请求核对一下
（这个列表网关不带密钥也给）。

## 许可证

| | |
|---|---|
| 对生成结果的条款 | 由**模型所有者**而不是网关决定：「Your ownership rights in the Output are set forth in the Model Terms for each Model you use」 |
| 商业使用 | 请看网关模型卡片上的 Model Terms —— 网关负责的是投递，而不是权利 |
| 必须遵守什么 | 网关和模型所有者的条款都要遵守；所有者有权随时修改自己的条款 |
| 条款原文 | <https://openrouter.ai/terms>（2026.09.11 版本）加上模型卡片上的 Model Terms |
| 生成费用 | 按令牌计 —— 费率见「限制与价格」一节 |

这个模型的权重没有公开发布（在 HuggingFace 上找不到仓库，2026.09.11 验证过）——
没有什么可授权的，只适用模型所有者和网关的条款。

网关这一侧则**在可能的范围内拒绝用数据训练**所接入的提供商，
但它不对别人条款的准确性负责，并且明确说明了这一点。

网关条款已于 2026.09.11 按其原文核对。

## 硬件要求

没有任何要求：计算在提供商一侧。需要能访问 `openrouter.ai` 的互联网出口。

## 常见错误

* **404「No endpoints found」** —— 「模型」字段里丢了厂商前缀，或者网关那边的
  slug 换了。
* **402「Insufficient credits」** —— OpenRouter 的余额没充值。
* **回复比估算的贵** —— 网关会在提供商价格之上收自己的佣金。

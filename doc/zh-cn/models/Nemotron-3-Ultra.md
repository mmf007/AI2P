# Nemotron-3-Ultra

**部署位置:** 云端（NVIDIA，**经由 OpenRouter 网关**）
**接入方式:** `provider: openai-compatible`，`baseUrl: https://openrouter.ai/api/v1`，
模型 `nvidia/nemotron-3-ultra-550b-a55b`
**密钥引用:** `openrouter.apiKey`

名录中最开放的一条记录：NVIDIA 不仅公开了权重，还公开了**训练数据和配方**，
用的是 OpenMDW-1.1 许可证。架构是混合的 —— Mamba-2 加 Transformer，
5500 亿参数（550 亿活跃）。技能评分 76–82，上下文
512 288 个令牌，价格每百万 0,60 $ 和 3,60 $。

它的定位是那些看重可复现性和开放性、而不是质量纪录的作业。
经由网关还能用到这个模型的**免费版**（带排队和频率限制）：
它的 slug 不一样，需要的话请自己建一条名录记录。

NVIDIA 没有为这个模型提供自己的公开 API，所以这条记录走的是
**OpenRouter** 网关。

## 如何获取密钥

**经由网关的所有模型共用一个密钥** —— Muse-Spark-1.2、
Inkling-975B、Ling-3.0-Flash 和这一条都引用
同一个 `openrouter.apiKey`。

1. 在 [openrouter.ai](https://openrouter.ai/) 注册。
2. 充值：**Credits → Add credits**。
3. 打开 [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**。
4. 复制那个值（只显示一次，以 `sk-or-` 开头）。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → Nemotron-3-Ultra →「设置 API 密钥」**。

## 限制与价格

| | |
|---|---|
| 上下文 | 512 288 个令牌 |
| 最大回复 | 65 536 个令牌 |
| 输入价格 | 每 100 万令牌 0,60 $ |
| 输出价格 | 每 100 万令牌 3,60 $ |

**模型标识符没有用实际调用验证过。** 经由网关的模型，标识符要写成带厂商前缀的
完整 slug（`nvidia/nemotron-3-ultra-550b-a55b`）。请向
`https://openrouter.ai/api/v1` 发 `GET /models` 请求核对一下。

## 许可证

| | |
|---|---|
| 对生成结果的条款 | 由**模型所有者**而不是网关决定：「Your ownership rights in the Output are set forth in the Model Terms for each Model you use」 |
| 商业使用 | 请看网关模型卡片上的 Model Terms —— 网关负责的是投递，而不是权利 |
| 必须遵守什么 | 网关和模型所有者的条款都要遵守；所有者有权随时修改自己的条款 |
| 条款原文 | <https://openrouter.ai/terms>（2026.07.29 版本）加上模型卡片上的 Model Terms |
| 生成费用 | 按令牌计 —— 费率见「限制与价格」一节 |

这里是个特例：**权重是公开发布的，用的是 OpenMDW-1.1**（卡片
`nvidia/NVIDIA-Nemotron-3-Ultra-550B-A55B-BF16`，许可证原文见
<https://openmdw.ai/license/1-1/>，2026.08.27 核对）。这是开放许可证，允许
商业使用，而且 NVIDIA 连同权重一起公开了训练数据和配方。
也就是说，这个模型可以自己跑起来，完全不必依赖网关的条款。

网关这一侧则**在可能的范围内拒绝用数据训练**所接入的提供商，
但它不对别人条款的准确性负责，并且明确说明了这一点。

网关条款已于 2026.08.27 按其原文核对。

## 硬件要求

没有任何要求：计算在提供商一侧。需要能访问 `openrouter.ai` 的互联网出口。
权重虽然开放，但 5500 亿参数需要一整个服务器机柜 —— 要在自己的计算机上跑，
请用名录里的本地记录。

## 常见错误

* **404「No endpoints found」** —— 丢了厂商前缀，或者 slug 换了。
* **作业挂着，最后按超时结束** —— 用到了带排队的免费版；
  请检查「模型」字段里写的是付费 slug，而且账户上有余额。
* **402「Insufficient credits」** —— OpenRouter 的余额没充值。

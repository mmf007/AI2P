# Nemotron-3.5-Super-VL

**部署位置:** 云端（NVIDIA，**经由 OpenRouter 网关**）
**接入方式:** `provider: openai-compatible`，`baseUrl: https://openrouter.ai/api/v1`，
模型 `nvidia/nemotron-3.5-super-vl`
**密钥引用:** `openrouter.apiKey`

NVIDIA 于 2026-10-15 发布该模型；它通过任务 T-558-S0（2026-10-20 的厂商巡检）进入
AI2P 模型目录。标识符 `nvidia/nemotron-3.5-super-vl`、价格与模态均已通过对公开目录 OpenRouter 的请求核对。

上下文 262 144 令牌，回复最多 235 929 令牌，每百万令牌 0,15 $ 和 0,50 $，技能评分 77–83。
目录中最接近的记录是 **Nemotron-3-Ultra**。

接受的输入：文本与 Markdown、源代码、图像、视频。

## 如何获取密钥

**经由网关的所有模型共用一个密钥** —— Muse-Spark-1.2、
Inkling-975B、Ling-3.0-Flash 和这一条都引用
同一个 `openrouter.apiKey`。

1. 在 [openrouter.ai](https://openrouter.ai/) 注册。
2. 充值：**Credits → Add credits**。
3. 打开 [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**。
4. 复制那个值（只显示一次，以 `sk-or-` 开头）。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → Nemotron-3.5-Super-VL →「设置 API 密钥」**。

## 限制与价格

| | |
|---|---|
| 上下文 | 262 144 个令牌 |
| 最大回复 | 235 929 个令牌 |
| 输入价格 | 每 100 万令牌 0,15 $ |
| 输出价格 | 每 100 万令牌 0,50 $ |

**模型标识符没有用实际调用验证过。** 经由网关的模型，标识符要写成带厂商前缀的
完整 slug（`nvidia/nemotron-3.5-super-vl`）。请向
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
`nvidia/NVIDIA-Nemotron-3.5-Super-VL-120B-A12B-BF16`，许可证原文见
<https://openmdw.ai/license/1-1/>，2026.08.27 核对）。这是开放许可证，允许
商业使用。
也就是说，这个模型可以自己跑起来，完全不必依赖网关的条款。

网关这一侧则**在可能的范围内拒绝用数据训练**所接入的提供商，
但它不对别人条款的准确性负责，并且明确说明了这一点。

网关条款已于 2026.08.27 按其原文核对。

## 硬件要求

没有任何要求：计算在提供商一侧。需要能访问 `openrouter.ai` 的互联网出口。
权重虽然开放，但 1200 亿参数需要一整个服务器机柜 —— 要在自己的计算机上跑，
请用名录里的本地记录。

## 常见错误

* **404「No endpoints found」** —— 丢了厂商前缀，或者 slug 换了。
* **作业挂着，最后按超时结束** —— 用到了带排队的免费版；
  请检查「模型」字段里写的是付费 slug，而且账户上有余额。
* **402「Insufficient credits」** —— OpenRouter 的余额没充值。

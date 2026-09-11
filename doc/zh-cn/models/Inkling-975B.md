# Inkling-975B

**部署位置:** 云端（Thinking Machines，**通过 OpenRouter 网关**）
**接入方式:** `provider: openai-compatible`，`baseUrl: https://openrouter.ai/api/v1`，
模型 `thinkingmachines/inkling`
**密钥引用:** `openrouter.apiKey`

9750 亿参数（410 亿激活）的开放模型，许可证是 **Apache 2.0**。
作者把它设计成**再训练的底座**，而不是纪录保持者：技能评分 81–87，
上下文 1 048 576 令牌，价格每百万 0,95 $ 和 4,05 $。输入接受文本、
源码、图片和音频。

**事实性方面要当心。**在世界知识测试（AA Omniscience）中，这个模型的准确率约
40 %，幻觉率 63 %：它会很自信地把缺的东西编出来。`analyze-data` 和 `text-docs`
这两个技能，只在结果有东西可核对、并且事实来自任务本身而不是模型记忆的场合才用它。

Thinking Machines 没有自己的公开 API，所以这条记录走的是 **OpenRouter** 网关。

## 如何获取密钥

**通过网关的所有模型共用一个密钥** —— Muse-Spark-1.2、
Nemotron-3-Ultra、Ling-3.0-Flash 和这一条
引用的都是同一个 `openrouter.apiKey`。

1. 在 [openrouter.ai](https://openrouter.ai/) 上注册。
2. 充值余额：**Credits → Add credits**。
3. 打开 [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**。
4. 复制那个值（只显示一次，以 `sk-or-` 开头）。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → Inkling-975B →「设置 API 密钥」**。

## 限制与价格

| | |
|---|---|
| 上下文 | 1 048 576 令牌 |
| 最大响应 | 65 536 令牌 |
| 输入价格 | 每 100 万令牌 0,95 $ |
| 输出价格 | 每 100 万令牌 4,05 $ |

**模型标识符未经真实调用验证。**通过网关的模型，标识符写的是带厂商前缀的完整
标识串（`thinkingmachines/inkling`）。请向 `https://openrouter.ai/api/v1` 发送
`GET /models` 请求核对 —— 这个列表网关不用密钥也会给。

## 许可证

| | |
|---|---|
| 生成结果的条款 | 由**模型的所有者**决定，而不是网关：「Your ownership rights in the Output are set forth in the Model Terms for each Model you use」 |
| 商业使用 | 请看网关上模型卡片里的 Model Terms —— 网关负责的是转发，不是权利 |
| 必须遵守什么 | 同时遵守网关和模型所有者的条款；所有者随时有权修改自己的条款 |
| 条款原文 | <https://openrouter.ai/terms>（2026.07.29 版）以及模型卡片上的 Model Terms |
| 生成费用 | 按令牌计费 —— 费率见「限制与价格」一节 |

这个模型的权重没有公开（HuggingFace 上找不到仓库，2026.08.27 核实）——
没有什么可授权的，起作用的只有模型所有者和网关的条款。

网关自己这边**尽可能拒绝让接入的提供商用数据做训练**，但它不为别人的条款是否准确
负责，而且明确说明了这一点。

网关条款于 2026.08.27 按原文核对。

## 硬件要求

没有任何要求：计算在提供商一侧。需要能访问 `openrouter.ai` 的互联网连接。
权重是开放的（Apache 2.0），但 9750 亿参数在自己的计算机上跑不动 ——
要本地运行，名录里有更小的模型。

## 常见错误

* **404「No endpoints found」** —— 丢了厂商前缀，或者网关那边的标识串变了。
* **402「Insufficient credits」** —— OpenRouter 的余额没有充值。
* **结果里出现了编造的事实** —— 这是这个模型的特点；请核对结果，
  或者把事实性任务交给别的模型。

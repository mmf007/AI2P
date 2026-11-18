# Inkling-2-Small

**部署位置:** 云端（Thinking Machines，**通过 OpenRouter 网关**）
**接入方式:** `provider: openai-compatible`，`baseUrl: https://openrouter.ai/api/v1`，
**密钥引用:** `openrouter.apiKey`

Thinking Machines 于 2026-11-09 发布该模型；它通过任务 T-736-S0（2026-11-13 的厂商巡检）进入
AI2P 模型目录。标识符 `thinkingmachines/inkling-2-small`、价格与模态均已通过对公开目录 OpenRouter 的请求核对。

上下文 1 048 576 令牌，回复最多 943 718 令牌，每百万令牌 0,30 $ 和 1,00 $，技能评分 81–86。
目录中最接近的记录是 **Inkling-975B**。

接受的输入：文本与 Markdown、源代码、图像。

## 如何获取密钥

**通过网关的所有模型共用一个密钥** —— Muse-Spark-1.2、
Nemotron-3-Ultra、Ling-3.0-Flash 和这一条
引用的都是同一个 `openrouter.apiKey`。

1. 在 [openrouter.ai](https://openrouter.ai/) 上注册。
2. 充值余额：**Credits → Add credits**。
3. 打开 [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**。
4. 复制那个值（只显示一次，以 `sk-or-` 开头）。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → Inkling-2-Small →「设置 API 密钥」**。

## 限制与价格

| | |
|---|---|
| 上下文 | 1 048 576 令牌 |
| 最大响应 | 943 718 令牌 |
| 输入价格 | 每 100 万令牌 0,30 $ |
| 输出价格 | 每 100 万令牌 1,00 $ |

**模型标识符未经真实调用验证。**通过网关的模型，标识符写的是带厂商前缀的完整
标识串（`thinkingmachines/inkling-2-small`）。请向 `https://openrouter.ai/api/v1` 发送
`GET /models` 请求核对 —— 这个列表网关不用密钥也会给。

## 许可证

| | |
|---|---|
| 生成结果的条款 | 由**模型的所有者**决定，而不是网关：「Your ownership rights in the Output are set forth in the Model Terms for each Model you use」 |
| 商业使用 | 请看网关上模型卡片里的 Model Terms —— 网关负责的是转发，不是权利 |
| 必须遵守什么 | 同时遵守网关和模型所有者的条款；所有者随时有权修改自己的条款 |
| 条款原文 | <https://openrouter.ai/terms>（2026.07.29 版）以及模型卡片上的 Model Terms |
| 生成费用 | 按令牌计费 —— 费率见「限制与价格」一节 |

模型权重公开：HuggingFace 仓库 `thinkingmachines/Inkling-2-Small`，许可证 **Apache 2.0**
（字段 `cardData.license`，`gated=false`，核对于 2026-11-13），允许商业使用，
文本：https://www.apache.org/licenses/LICENSE-2.0。通过网关使用时，适用模型所有者与网关的条款。

网关自己这边**尽可能拒绝让接入的提供商用数据做训练**，但它不为别人的条款是否准确
负责，而且明确说明了这一点。

网关条款于 2026.08.27 按原文核对。

## 硬件要求

没有任何要求：计算在提供商一侧。需要能访问 `openrouter.ai` 的互联网连接。
权重是开放的（Apache 2.0），但 5940 亿参数在自己的计算机上跑不动 ——
要本地运行，名录里有更小的模型。

## 常见错误

* **404「No endpoints found」** —— 丢了厂商前缀，或者网关那边的标识串变了。
* **402「Insufficient credits」** —— OpenRouter 的余额没有充值。
* **结果里出现了编造的事实** —— 这是这个模型的特点；请核对结果，
  或者把事实性任务交给别的模型。

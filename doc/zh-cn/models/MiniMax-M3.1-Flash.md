# MiniMax-M3.1-Flash

**部署位置:** 云端（MiniMax，OpenAI 兼容）
**接入方式:** `provider: openai-compatible`，`baseUrl: https://api.minimax.io/v1`，
**密钥引用:** `minimax.apiKey`

MiniMax 于 2026-10-19 发布该模型；它通过任务 T-653-S0（2026-10-29 的厂商巡检）进入
AI2P 模型目录。标识符 `minimax/minimax-m3.1-flash`、价格与模态均已通过对公开目录 OpenRouter 的请求核对。

上下文 1 048 576 令牌，回复最多 524 288 令牌，每百万令牌 0,15 $ 和 0,60 $，技能评分 80–84。
目录中最接近的记录是 **MiniMax-M3**。

接受的输入：文本与 Markdown、源代码、图像、视频。

## 如何获取密钥

1. 在 [platform.minimax.io](https://platform.minimax.io/) 注册
   （国际站点；中国站点的域名不同，密钥也不同）。
2. 在计费一节里充值。
3. 打开 **Account → API Keys → Create new secret key**，起个名字。
4. 复制它的值 —— 它**只显示一次**。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → MiniMax-M3.1-Flash →「设置 API 密钥」**。

密钥属于组织：用组织的密钥加密，并复制到组织的所有服务器上（技术规格第 10 章）。

## 限制与价格

| | |
|---|---|
| 上下文 | 1 048 576 个令牌 |
| 最大回复 | 524 288 个令牌 |
| 输入价格 | 每 100 万令牌 0,15 $ |
| 输出价格 | 每 100 万令牌 0,60 $ |

**模型标识符没有用实际调用验证过** —— 它取自模型目录，去掉了厂商前缀。
请向 `https://api.minimax.io/v1` 发 `GET /models` 请求核对一下。

## 许可证

| | |
|---|---|
| 对生成结果的条款 | MiniMax **不主张对生成内容的权利**（「We do not claim ownership of User Contributions or User Generated Content」） |
| 商业使用 | 允许 |
| 必须遵守什么 | 记住：您授予 MiniMax 一项对所发送和收到内容的永久、不可撤销、非独占的使用许可 |
| 条款原文 | <https://www.minimax.io/platform/protocol/terms-of-service> |
| 生成费用 | 按令牌计 —— 费率见「限制与价格」一节 |

MiniMax-M3.1-Flash 的权重是以**自己的 community 许可证**发布的（在卡片
`MiniMaxAI/MiniMax-M3.1-Flash` 中它叫 `minimax-community`），而不是 MIT 或 Apache 2.0 ——
如果您打算自己把模型跑起来，请单独把它读一遍。

2026.08.27 核对：权重许可证按模型卡片，结果条款按 MiniMax 公布的条款文本
（<https://agent.minimax.io/doc/en/terms-of-service.html>）。

## 硬件要求

没有任何要求：计算在提供商一侧。需要能访问 `api.minimax.io` 的互联网出口。

## 常见错误

* **401「invalid api key」** —— 中国站点的密钥在国际站点上不能用
  （反过来也一样）；请在 `baseUrl` 指向的那一边建密钥。
* **404「model not found」** —— id 换了；请用 `GET /models` 列表核对。
* **复杂代码上的质量不如预期** —— 这个模型的定位不在这里；写代码请用
  GLM-5.2 或 Kimi-K3。

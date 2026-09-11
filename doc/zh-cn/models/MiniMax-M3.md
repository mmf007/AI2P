# MiniMax-M3

**部署位置:** 云端（MiniMax，OpenAI 兼容）
**接入方式:** `provider: openai-compatible`，`baseUrl: https://api.minimax.io/v1`，
模型 `minimax-m3`
**密钥引用:** `minimax.apiKey`

便宜而且上下文大：每百万令牌 0,30 $ 和 1,20 $，上下文
1 048 576 —— 比 GLM-5.2 便宜四倍，比 Kimi-K3 便宜十倍。
技能评分 80–85：名录里能干活的中档。

它的特别之处是**多模态输入**：除了文本和源码，模型还接收
图片和视频，而价格却和便宜的纯文本模型一样。适合素材以截图或屏幕录像形式
送来的作业。

## 如何获取密钥

1. 在 [platform.minimax.io](https://platform.minimax.io/) 注册
   （国际站点；中国站点的域名不同，密钥也不同）。
2. 在计费一节里充值。
3. 打开 **Account → API Keys → Create new secret key**，起个名字。
4. 复制它的值 —— 它**只显示一次**。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → MiniMax-M3 →「设置 API 密钥」**。

密钥属于组织：用组织的密钥加密，并复制到组织的所有服务器上（技术规格第 10 章）。

## 限制与价格

| | |
|---|---|
| 上下文 | 1 048 576 个令牌 |
| 最大回复 | 65 536 个令牌 |
| 输入价格 | 每 100 万令牌 0,30 $ |
| 输出价格 | 每 100 万令牌 1,20 $ |

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

MiniMax-M3 的权重是以**自己的 community 许可证**发布的（在卡片
`MiniMaxAI/MiniMax-M3` 中它叫 `minimax-community`），而不是 MIT 或 Apache 2.0 ——
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

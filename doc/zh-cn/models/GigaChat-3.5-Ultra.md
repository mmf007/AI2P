# GigaChat-3.5-Ultra

**部署位置:** 云端（Sber，俄罗斯；OpenAI 兼容）
**接入方式:** `provider: openai-compatible`，
`baseUrl: https://gigachat.devices.sberbank.ru/api/v1`，模型 `GigaChat-3.5-Ultra`
**密钥引用:** `gigachat.accessToken`

俄罗斯的 4320 亿参数模型（MoE），权重以 MIT 开放。**名录中俄语文本写得最好的一个**：
`text-write` 85、`text-edit` 84、`text-translate` 84。
代码明显弱一些（61–66）—— 编程请选别的记录。

## 接入并不完整 —— 在配密钥之前请先读这一节

名录里有这条记录，但**按标准方式它并不能完整工作**，这是已知的限制，不是安装出错：

* Sber 给的**不是长期密钥，而是有效期 30 分钟的 OAuth 访问令牌**。
  AI2P 的连接器只会用长期密钥，所以填进去的令牌半小时后就失效，得重新输入。
  名录里的字段名字就叫 `gigachat.accessToken`。
* 需要在计算机的受信任证书存储中安装**俄罗斯根证书**（数字发展部），
  否则与 `gigachat.devices.sberbank.ru` 的连接会在 TLS 校验时断开。

完整的支持（会刷新令牌的专用连接器，或者外部代理适配器）是项目的另一个任务。
在那之前，这条记录只适合一次性试用，不适合让代理在后台干活。

## 如何获取密钥

1. 在 [Sber Studio](https://developers.sber.ru/studio/) 注册并创建
   **GigaChat API** 项目。
2. 拿到 **Client ID / Client Secret** 这一对（界面上叫「授权密钥」）。
3. 在运行 AI2P 的计算机上安装俄罗斯数字发展部的根证书。
4. 用授权密钥换取 **access token**（向
   `https://ngw.devices.sberbank.ru:9443/api/v2/oauth` 发 `POST` 请求，带 `RqUID`
   请求头，字段 `scope=GIGACHAT_API_PERS` 或 `GIGACHAT_API_CORP`）。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → GigaChat-3.5-Ultra →
   「设置 API 密钥」** —— 把拿到的令牌粘进去。**30 分钟后重复第 4–5 步**：
   令牌的有效期不会延长。

## 限制与价格

| | |
|---|---|
| 上下文 | 262 144 令牌 |
| 最大响应 | 32 768 令牌 |
| 价格 | 按 Sber 的资费（名录中填的是零） |

资费是按 Sber 自己的单位计算的，而不是每百万令牌多少美元，所以能力声明里价格
留成了零 —— **AI2P 的计费不会显示这个模型的花费**。请在 Studio 的个人后台里看。

**模型标识符未经真实调用验证** —— 请向
`https://gigachat.devices.sberbank.ru/api/v1` 发送 `GET /models` 请求核对。

## 许可证

| | |
|---|---|
| 生成结果的条款 | **生成内容的权利归用户所有**（GigaChat 协议 1.9 条） |
| 商业使用 | 允许 |
| 必须遵守什么 | 按 5.2 条，您授予银行一项不可撤销、无偿、非独占的许可，可以使用生成内容 —— 直到改编（改编成果的权利归银行）和用于广告 |
| 条款原文 | <https://developers.sber.ru/docs/ru/policies/gigachat-agreement/beta> |
| 生成费用 | 按令牌计费 —— 费率见「限制与价格」一节 |

结果的权利形式上归您，但**您授予 Sber 的许可是名录记录中最宽的一个**：
复制、向公众提供、改编以及用于广告。如果结果是商业秘密或者不能给第三方看，
请换一个模型。

条款于 2026.08.27 按协议原文核对。协议标着测试版 —— 接入前请自己读一遍，
它比其他条款改得更频繁。

## 硬件要求

没有任何要求：计算在提供商一侧。需要能访问
`gigachat.devices.sberbank.ru` 的互联网连接，以及装好的俄罗斯根证书。

## 常见错误

* **成功跑起来半小时后出现 401** —— 访问令牌过期了；请换一个新的。
* **TLS 错误 /「无法建立信任关系」** —— 没有安装俄罗斯根证书。
* **代码任务做得很差** —— 这个模型的定位是俄语文本，不是编程。

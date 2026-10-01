# Qwen3.8-Omni-Flash

**部署位置:** 云端（阿里巴巴 DashScope，OpenAI 兼容）
**接入方式:** `provider: openai-compatible`，
`baseUrl: https://dashscope-intl.aliyuncs.com/compatible-mode/v1`，模型 `qwen3.8-omni-flash`
**密钥引用:** `qwen.apiKey`

阿里巴巴 于 2026-09-21 发布该模型；它通过任务 T-347-S0（2026-09-23 的厂商巡检）进入
AI2P 模型目录。标识符 `qwen/qwen3.8-omni-flash`、上下文长度、价格与模态均已通过对公开目录
OpenRouter 的请求核对。

延续 **Qwen3.8-Max** 系列：上下文 1 000 000 令牌，回复最多 32 768 令牌，每百万令牌 0,15 $ 和
0,47 $。技能评分 82–89。

接受的输入：文本与 Markdown、源代码、图像、音频、视频。

## 如何获取密钥

1. 在 [Alibaba Cloud Model Studio](https://modelstudio.console.alibabacloud.com/)
   注册（国际站）。
2. 开通模型服务并绑定付款方式。
3. 打开 **API-KEY → Create API Key**，选择工作空间。
4. 复制这个值 —— 它**只显示一次**。密钥以 `sk-` 开头。
5. 在 AI2P 里：**设置 → 名录 → AI 模型 → Qwen3.8-Omni-Flash →「设置 API 密钥」**。

密钥属于组织：用组织的密钥加密，并复制到组织的所有服务器上（技术规格第 10 章）。
名录里的**本地** Qwen 模型根本没有密钥 —— 它们在自己的计算机上运行。

## 限制与价格

| | |
|---|---|
| 上下文 | 1 000 000 个令牌 |
| 回复上限 | 32 768 个令牌 |
| 输入价格 | 每 100 万令牌 0,15 $ |
| 输出价格 | 每 100 万令牌 0,47 $ |

**模型标识符没有用实际调用验证过** —— 它取自模型目录，去掉了厂商前缀。
请向 `https://dashscope-intl.aliyuncs.com/compatible-mode/v1` 发 `GET /models`
请求核对。最新价格见 Model Studio 控制台。

## 许可证

| | |
|---|---|
| 对生成结果的条款 | 由 Model Studio 协议规定：按密钥付费访问即有权使用结果，结果的责任在您 |
| 商业使用 | 付费访问时允许；在控制台**试用**模式下取得的内容只允许用于评估模型 |
| 必须遵守什么 | 遵守 Model Studio 规则，并对生成内容的合法性负责：供应商不对内容作任何保证 |
| 条款原文 | <https://www.alibabacloud.com/help/en/model-studio/related-agreements>（现行协议清单） |
| 生成费用 | 按令牌计 —— 费率见「限制与价格」一节 |

`qwen3.8-omni-flash` 的权重是封闭的（与按 Apache 2.0 发布的小号 Qwen 不同），
所以这里说的是**生成结果**。

已于 2026.09.23 核对：读过 Model Studio 的协议清单页面（最后更新 2026.09.23，
现行的有 Terms of Service、Service Level Agreement 和 Open Source Model
License Terms）；Terms of Service 正文机器读不了 —— 请按上面的链接打开，用眼睛读，
尤其是关于试用模式的那一节。

## 硬件要求

没有：计算在供应商那边。需要能访问 `dashscope-intl.aliyuncs.com` 的网络。

## 常见错误

* **404 或「model not exist」** —— 要么 id 变了，要么用错了主机
  （国际站还是中国站）。
* **401「InvalidApiKey」** —— 密钥是在另一个工作空间里创建的。
* **回复为空，原因是 `length`** —— 请调大配置档中的 `params.maxTokens`
  （默认 32000）。

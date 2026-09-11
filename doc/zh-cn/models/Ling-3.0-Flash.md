# Ling-3.0-Flash

**部署位置:** 云端（蚂蚁集团，**经由 OpenRouter 网关**）
**接入方式:** `provider: openai-compatible`，`baseUrl: https://openrouter.ai/api/v1`，
模型 `inclusionai/ling-3.0-flash`
**密钥引用:** `openrouter.apiKey`

**名录中最便宜的一条记录：每 100 万输入令牌 0,021 $** —— 大约比 Kimi-K3
便宜一百五十倍。模型有 1240 亿参数（51 亿活跃），瞄准的是高频的智能体场景：
连续很多个短回合。

它在 AI2P 中的定位是多回合作业的**廉价底层**：草稿、大量细碎修改、素材的初步标注。
技能评分 74–82，而且这里的上下文比邻居们小 —— 262 144 个令牌。

蚂蚁集团没有自己的公开 API，所以这条记录走的是 **OpenRouter** 网关。

## 如何获取密钥

**经由网关的所有模型共用一个密钥** —— Muse-Spark-1.2、
Inkling-975B、Nemotron-3-Ultra 和这一条
都引用同一个 `openrouter.apiKey`。

1. 在 [openrouter.ai](https://openrouter.ai/) 注册。
2. 充值：**Credits → Add credits**。最低额度也能用很久：
   一百万输入令牌只要两分钱。
3. 打开 [openrouter.ai/keys](https://openrouter.ai/keys) → **Create key**。
4. 复制那个值（只显示一次，以 `sk-or-` 开头）。
5. 在 AI2P 中：**设置 → 名录 → AI 模型 → Ling-3.0-Flash →「设置 API 密钥」**。

## 限制与价格

| | |
|---|---|
| 上下文 | 262 144 个令牌 |
| 最大回复 | 32 768 个令牌 |
| 输入价格 | 每 100 万令牌 0,021 $ |
| 输出价格 | 每 100 万令牌 0,063 $ |

**模型标识符没有用实际调用验证过。** 经由网关的模型，标识符要写成带厂商前缀的
完整 slug（`inclusionai/ling-3.0-flash`）。请向 `https://openrouter.ai/api/v1`
发 `GET /models` 请求核对一下。

## 许可证

| | |
|---|---|
| 对生成结果的条款 | 由**模型所有者**而不是网关决定：「Your ownership rights in the Output are set forth in the Model Terms for each Model you use」 |
| 商业使用 | 请看网关模型卡片上的 Model Terms —— 网关负责的是投递，而不是权利 |
| 必须遵守什么 | 网关和模型所有者的条款都要遵守；所有者有权随时修改自己的条款 |
| 条款原文 | <https://openrouter.ai/terms>（2026.07.29 版本）加上模型卡片上的 Model Terms |
| 生成费用 | 按令牌计 —— 费率见「限制与价格」一节 |

这个模型的权重也是开放的：**MIT**（卡片 `inclusionAI/Ling-3.0-flash`，
2026.08.27 核对）—— 允许商业使用，需要保留许可证文本和版权声明。
也就是说模型可以自己跑起来；名录记录走的是网关，而结果适用模型所有者的条款。

网关这一侧则**在可能的范围内拒绝用数据训练**所接入的提供商，
但它不对别人条款的准确性负责，并且明确说明了这一点。

网关条款已于 2026.08.27 按其原文核对。

## 硬件要求

没有任何要求：计算在提供商一侧。需要能访问 `openrouter.ai` 的互联网出口。

## 常见错误

* **自动选择老是挑中它** —— 项目的「价格 ↔ 质量」滑块偏向了价格
  （技术规格 2.7 节）。请把它推向质量，或者手动指定执行者。
* **输入太长** —— 上下文是 262 144 个令牌，比旗舰们少四倍。
* **404「No endpoints found」** —— 丢了厂商前缀，或者网关那边的 slug 换了。

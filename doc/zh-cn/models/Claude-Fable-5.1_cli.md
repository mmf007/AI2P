# Claude-Fable-5.1_cli

**部署位置：** 云端，但**通过 CLI 接入，而不是 API**
**接入方式：** `provider: anthropic`、`transport: cli`、命令
`claude --permission-mode acceptEdits`、模型 `claude-fable-5-1`
**密钥引用：** 留空 — **不需要 API 密钥**

与 Claude-Fable-5.1 是同一个模型，但通过 headless 模式的 **Claude Code CLI**
启动。按**订阅**付费而不是按 token，因此在 AI2P 的计费中这类任务的成本为零。

截至 2026-09-24 该系列中最强的模型。适合复杂代码、需求分析以及针对项目文件的长任务。

## 为什么每个版本要单独一条记录

在 Claude Code CLI 中，模型版本由配置档的**「模型」**字段决定：它会以 `--model` 标志传给
CLI。因此，凡是希望能为执行者选择的版本，都有**自己的目录条目**，而且条目共存：Claude-Fable-5_cli
保留它原来的标识符，本条目运行在 `claude-fable-5-1` 上。

取值有两种合法形式（`claude --help`）：

* **完整版本名** — `claude-fable-5-1`；
* **最新版本别名** — `fable`。

发行版的条目使用完整版本名：别名会随时间悄悄指向新版本（2026-09-24 实际调用验证：`fable`
已经表示 `claude-fable-5-1`），届时条目的含义就与其名称不符了。

## 不需要 API 密钥

授权靠 CLI 会话而不是密钥：配置档中的 `secretRef` 为空，条目上也没有「设置 API 密钥」按钮。
需要做的是：

1. **安装 Claude Code** — [官方说明](https://docs.claude.com/en/docs/claude-code/overview)。
   检查：`claude --version` 应当有输出。
2. **用你的订阅登录**：`claude login`。
3. 确认 `claude` 在 **运行 AI2P 的那个用户的 PATH** 中可用。
4. 如果可执行文件不在 PATH 中，请在 `cliCommand` 字段写完整路径。

「走 API / 走 CLI」的详细差异见 Claude-Fable-5_cli 文档；这里完全一样。

## 探测也会检查模型

探测按钮（v1.144 修改）会做三件事：运行 `claude --version`、查询登录状态，并**用一次简短调用
检查模型标识符本身**。未知的、或订阅无权访问的 id 会立刻以探测失败的形式暴露，而不是在几分钟
后让任务崩溃。如果 CLI 用与请求不同的模型作答，AI2P 会以**任务日志事件**和结果摘要中的一行
把它写出来。

## 限制与成本

| | |
|---|---|
| 上下文 | 1 000 000 tokens |
| 最大输出 | 128 000 tokens |
| 成本 | 0（由订阅支付） |

**AI2P 能识别订阅限额并等待重置**：遇到 429 拒绝时读取重置时间，把任务转为「等待」，重置后
继续同一个会话。

## 许可

| | |
|---|---|
| 生成结果的条款 | **结果归你**：Anthropic 将其对 Outputs 的权利转让给你（Consumer Terms 第 4 条） |
| 商业使用 | 允许 |
| 必须遵守的 | 遵守 Usage Policy；注意在消费者条款下，你的材料会用于模型训练，除非在账户设置中关闭 |
| 条款原文 | <https://www.anthropic.com/legal/consumer-terms> |
| 生成的费用 | 按订阅付费而非按 token — 在 AI2P 计费中此类任务为 0 |

Claude 订阅适用**消费者**条款；API 密钥适用**商业**条款
（<https://www.anthropic.com/legal/commercial-terms>），后者不包含用你的数据训练。

## 硬件要求

没有特别要求：由服务商计算。需要安装 Claude Code、能访问互联网，以及项目目录中的存储空间。

## 常见错误

* **「找不到 claude」** — CLI 未安装，或不在 AI2P 用户的 PATH 中。
* **「Claude CLI 不接受模型」** — 标识符过时，或订阅无权访问该版本；请对照 `claude --help`
  核对「模型」字段。
* **代理看不到任务文件** — 项目没有设置目录（「项目文件夹」）。

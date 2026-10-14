# GPT-6-Sol_cli

**部署：** 云端，**通过 CLI 而非 API** 连接
**连接：** `provider: openai`，`transport: cli`，命令 `codex`，模型 `gpt-6-sol`
**密钥引用：** 空 —— **不需要 API 密钥**，通过 CLI 会话授权

与 GPT-6-Sol 相同的模型，但通过厂商的终端代理 **OpenAI Codex CLI**（https://github.com/openai/codex）运行。按厂商订阅付费，在 AI2P 计费中此类任务费用为零。

## 重要：暂无连接器

AI2P **暂时无法**通过 OpenAI Codex CLI 执行任务：只有 Claude Code 有 CLI 连接器。因此该条目创建时为**未激活**。

## 安装与登录（不需要 API 密钥）

1. 安装 CLI：`npm i -g @openai/codex`。
2. 登录：`codex login` —— 在服务器计算机的终端中，使用运行 AI2P 的用户。
3. 确认 `codex` 在该用户的 PATH 中，否则在 `cliCommand` 字段填写完整路径。

使用 ChatGPT 订阅（Plus、Pro、Team、Enterprise）或 OpenAI API 密钥登录。

非交互（headless）运行：`codex exec "..."`。

模型目录行中的钥匙按钮会显示此 CLI 的登录命令。

## 限制与费用

| | |
|---|---|
| 上下文 | 400 000 |
| 费用 | 0（由订阅支付） |

## 许可

生成结果与使用条款遵循厂商订阅条款（https://github.com/openai/codex）。2026-10-06 通过网络搜索核实。

## 硬件要求

无特殊要求：由厂商计算。

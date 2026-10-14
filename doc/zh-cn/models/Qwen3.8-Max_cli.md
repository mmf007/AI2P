# Qwen3.8-Max_cli

**部署：** 云端，**通过 CLI 而非 API** 连接
**连接：** `provider: alibaba`，`transport: cli`，命令 `qwen`，模型 `qwen3.8-max`
**密钥引用：** 空 —— **不需要 API 密钥**，通过 CLI 会话授权

与 Qwen3.8-Max 相同的模型，但通过厂商的终端代理 **Qwen Code**（https://github.com/QwenLM/qwen-code）运行。按厂商订阅付费，在 AI2P 计费中此类任务费用为零。

## 重要：暂无连接器

AI2P **暂时无法**通过 Qwen Code 执行任务：只有 Claude Code 有 CLI 连接器。因此该条目创建时为**未激活**。

## 安装与登录（不需要 API 密钥）

1. 安装 CLI：`npm i -g @qwen-code/qwen-code@latest`。
2. 登录：`qwen  →  /auth` —— 在服务器计算机的终端中，使用运行 AI2P 的用户。
3. 确认 `qwen` 在该用户的 PATH 中，否则在 `cliCommand` 字段填写完整路径。

Qwen Code 是阿里巴巴基于 Gemini CLI 的分支。登录：运行 `qwen` 并执行 `/auth`（Qwen OAuth 或 API 密钥）。

非交互（headless）运行：`qwen -p "..."`。

模型目录行中的钥匙按钮会显示此 CLI 的登录命令。

## 限制与费用

| | |
|---|---|
| 上下文 | 262 144 |
| 费用 | 0（由订阅支付） |

## 许可

生成结果与使用条款遵循厂商订阅条款（https://github.com/QwenLM/qwen-code）。2026-10-06 通过网络搜索核实。

## 硬件要求

无特殊要求：由厂商计算。

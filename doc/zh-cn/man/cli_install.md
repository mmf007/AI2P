# 安装 CLI 代理软件

## 什么是 CLI 模型

模型目录有三种部署方式：**本地**（显示器图标：模型在本机运行）、**云端**（云图标：AI2P 用密钥调用提供商 API）和 **CLI 代理**（显示器中的云：本机安装了厂商程序 —— Claude Code、Codex CLI、Qwen Code 等 —— 由它按订阅访问云端）。

* 目录记录和执行者在整个组织内**共用**。
* CLI 程序需在**每台服务器上分别安装**，由**该服务器的管理员**安装。

## 安装内容

安装窗口（设置 → AI 模型 → 铅笔 → “安装”）根据模型配置中的 CLI 命令生成列表：Claude Code（npm `@anthropic-ai/claude-code`）、Codex CLI（npm `@openai/codex`）、Qwen Code（npm `@qwen-code/qwen-code`）；Antigravity（`agy`）、Grok Build（`grok`）、Kimi（`kimi`）和 Mistral Vibe（`vibe`）需手动安装，AI2P 只在 PATH 中查找命令。

npm 程序需要 **Node.js 20 或更新版本**：先在系统中查找；找不到则把便携版 Node.js 24 LTS 安装到 AI2P 软件包目录（下载前先检查分发包目录）。全部已安装时按钮显示 **“更新”**（`npm install <包>@latest`）。

## 仅当前用户或全局安装

复选框 **“仅限当前用户”**（默认勾选；保存在本服务器的 config.json 中）：

* **当前用户**：`npm install --prefix "<软件包目录>/cli/<程序>" <包>@latest`，无需管理员权限；程序只对 AI2P 可见（启动 CLI 时 AI2P 把其目录加入进程 PATH）；
* **全局**：`npm install -g <包>@latest`（Windows 上带系统 Node.js 目录的 `--prefix`）；需要系统安装的 Node.js，几乎总是需要管理员权限。

### 权限不足时

1. 在管理员终端（Windows）或用 `sudo`（Linux、macOS）执行提示中的命令，然后点击 **“更新”**。
2. 或以管理员（root）权限重启 AI2P 并重新安装：窗口会显示 AI2P 是否以该权限运行。这是备选方案，并非推荐。
3. 或勾选“仅限当前用户”后重新安装。

安装后，用模型行中的钥匙按钮登录。

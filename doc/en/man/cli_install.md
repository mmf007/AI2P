# Installing CLI agent software

## What a CLI model is

The model catalog has three kinds of placement:

| Icon | Kind | Meaning |
|---|---|---|
| monitor | **local** | the model runs on this computer (ComfyUI, llama.cpp) |
| cloud | **cloud** | AI2P calls the provider API with a key |
| cloud inside a monitor | **CLI agent** | a vendor program is installed on the computer (Claude Code, Codex CLI, Qwen Code …) and it talks to the cloud itself, by subscription |

A CLI model is local software working with the cloud. Therefore:

* **the catalog record and the executor are shared** by the whole organization (like a cloud model);
* **the CLI program is installed on EACH server separately** — jobs of a CLI executor run on the server where the program is installed and logged in (the key button in the model row);
* the program is installed by **the administrator of that server** (local admin): the model form opens on any server, the "Install" / "Update" button and the installation mode are available to the admin only.

## What gets installed

The installation window of a CLI model (Settings → AI models → pencil → "Install") builds the list from the CLI command of the model profile:

| Model | Program | How |
|---|---|---|
| Claude-*_cli | Claude Code (`claude`) | npm `@anthropic-ai/claude-code` |
| GPT-*_cli | Codex CLI (`codex`) | npm `@openai/codex` |
| Qwen*_cli | Qwen Code (`qwen`) | npm `@qwen-code/qwen-code` |
| Gemini-*_cli | Antigravity CLI (`agy`) | manually, AI2P only looks for the command in PATH |
| Grok-*_cli | Grok Build (`grok`) | manually |
| Kimi-*_cli | Kimi CLI (`kimi`) | manually (`uv tool install kimi-cli`) |
| Mistral-*_cli | Mistral Vibe (`vibe`) | manually (`uv tool install mistral-vibe`) |

npm programs need **Node.js 20 or newer**. It is looked up in the system first (`node` in PATH); if missing, a portable Node.js 24 LTS is installed into the AI2P packages folder (`<packages>/nodejs`). Before downloading, the distributions folder (Settings → Server) is checked: an existing `node-v24.21.0-win-x64.zip` (Linux `…-linux-x64.tar.gz`, macOS `…-darwin-arm64.tar.gz`) or an unpacked archive is used as is, without network.

When everything is installed, the button reads **"Update"**: the npm program is reinstalled to the latest version (`npm install <package>@latest`).

## Current user or global installation

The window has a **"Current user only"** checkbox (on by default). The choice is kept in config.json of THIS server.

**Current user** (checked):

* command: `npm install --prefix "<packages>/cli/<program>" <package>@latest`;
* **no** administrator rights needed;
* the program is visible to AI2P only: when starting a CLI agent AI2P adds its folder and its own Node.js to the PATH of the process. To log in use the key button in the model row or call the program by full path.

**Global** (unchecked):

* command: `npm install -g <package>@latest` (on Windows with `--prefix "<system Node.js folder>"`, which is already in the system PATH);
* the program is available to all users and in any terminal;
* requires **Node.js installed system-wide** (not the portable one from the AI2P packages folder);
* almost always requires **administrator rights**.

### If rights are missing

The window shows an error with the exact command. Do one of three:

1. **Run the command yourself as administrator.** Windows: open Terminal/PowerShell with "Run as administrator" and paste the command; Linux/macOS: `sudo npm install -g <package>@latest`. Then press **"Update"** in the window — AI2P finds the program in PATH.
2. **Start AI2P with administrator (root) rights** and repeat the installation from the window — the global installation then goes through by itself. The window tells whether AI2P runs with such rights. This is an alternative, not a recommendation: a server with administrator rights can do anything, and so can its CLI agents.
3. **Tick "Current user only"** and install again — no administrator rights needed.

## Programs installed manually

Antigravity, Grok Build, Kimi and Mistral Vibe have no automatic installation: install the program following the vendor instructions so that the command is in the PATH of the user running AI2P, and press "Update".

## Logging in

Installation does not log in to the vendor account. Press the key button in the model row afterwards.

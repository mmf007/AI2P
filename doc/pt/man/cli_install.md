# Instalação do software de agentes CLI

## O que é um modelo CLI

O catálogo de modelos tem três tipos de localização: **local** (ícone de monitor: o modelo corre neste computador), **na nuvem** (ícone de nuvem: o AI2P chama a API do fornecedor com uma chave) e **agente CLI** (nuvem dentro de um monitor: no computador há um programa do fornecedor — Claude Code, Codex CLI, Qwen Code… — que trabalha com a nuvem por subscrição).

* O registo do catálogo e o executor são **comuns** a toda a organização.
* O programa CLI é instalado **em CADA servidor separadamente**, pelo **administrador desse servidor**.

## O que é instalado

A janela de instalação (Definições → Modelos de IA → lápis → «Instalar») monta a lista pelo comando CLI do perfil: Claude Code (npm `@anthropic-ai/claude-code`), Codex CLI (npm `@openai/codex`), Qwen Code (npm `@qwen-code/qwen-code`); Antigravity (`agy`), Grok Build (`grok`), Kimi (`kimi`) e Mistral Vibe (`vibe`) instalam-se à mão, o AI2P só procura o comando no PATH.

Os programas npm precisam de **Node.js 20 ou mais recente**: primeiro procura-se no sistema; se faltar, instala-se o Node.js 24 LTS portátil na pasta de pacotes do AI2P (antes verifica-se a pasta de distribuições). Se tudo estiver instalado, o botão diz **«Atualizar»** (`npm install <pacote>@latest`).

## Para o utilizador ou instalação global

Caixa **«Apenas para o utilizador atual»** (ligada por omissão; guardada no config.json DESTE servidor):

* **utilizador atual**: `npm install --prefix "<pacotes>/cli/<programa>" <pacote>@latest`, sem direitos de administrador; o programa só é visto pelo AI2P (acrescenta a pasta ao PATH do processo CLI);
* **global**: `npm install -g <pacote>@latest` (no Windows com `--prefix` da pasta do Node.js do sistema); exige Node.js instalado no sistema e quase sempre direitos de administrador.

### Se faltarem direitos

1. Execute o comando da mensagem num terminal de administrador (Windows) ou com `sudo` (Linux, macOS) e prima **«Atualizar»**.
2. Ou reinicie o AI2P com direitos de administrador (root) e repita a instalação: a janela indica se o AI2P corre com esses direitos. É uma alternativa, não uma recomendação.
3. Ou marque «Apenas para o utilizador atual» e instale novamente.

Depois de instalar, inicie sessão com o botão de chave na linha do modelo.

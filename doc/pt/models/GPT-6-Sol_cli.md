# GPT-6-Sol_cli

**Hospedagem:** nuvem, conexão **por CLI, não por API**
**Conexão:** `provider: openai`, `transport: cli`, comando `codex`, modelo `gpt-6-sol`
**Referência de chave:** vazia — **não é preciso chave de API**, autorização pela sessão do CLI

O mesmo modelo que GPT-6-Sol, mas pelo agente de terminal do fornecedor **OpenAI Codex CLI** (https://github.com/openai/codex). Pago pela assinatura do fornecedor; no faturamento do AI2P custa zero.

## Importante: ainda não há conector

O AI2P **ainda não executa tarefas** pelo OpenAI Codex CLI: só o Claude Code tem conector CLI. Por isso a entrada é criada **inativa**.

## Instalação e login (não é preciso chave de API)

1. Instalar o CLI: `npm i -g @openai/codex`.
2. Fazer login: `codex login` — num terminal do computador servidor, com o usuário do AI2P.
3. Verifique que `codex` está no PATH desse usuário; senão, escreva o caminho completo em `cliCommand`.

O login usa uma assinatura do ChatGPT (Plus, Pro, Team, Enterprise) ou uma chave de API da OpenAI.

Execução não interativa (headless): `codex exec "..."`.

O botão de chave na linha do catálogo mostra o comando de login deste CLI.

## Limites e custo

| | |
|---|---|
| Contexto | 400 000 |
| Custo | 0 (pago pela assinatura) |

## Licença

Resultado e termos conforme a assinatura do fornecedor (https://github.com/openai/codex). Verificado em 06.10.2026 por busca na web.

## Requisitos de hardware

Nenhum especial: quem calcula é o fornecedor.

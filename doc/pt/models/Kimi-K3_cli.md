# Kimi-K3_cli

**Hospedagem:** nuvem, conexão **por CLI, não por API**
**Conexão:** `provider: moonshot`, `transport: cli`, comando `kimi`, modelo `kimi-k3`
**Referência de chave:** vazia — **não é preciso chave de API**, autorização pela sessão do CLI

O mesmo modelo que Kimi-K3, mas pelo agente de terminal do fornecedor **Moonshot Kimi Code** (https://github.com/MoonshotAI/kimi-code). Pago pela assinatura do fornecedor; no faturamento do AI2P custa zero.

## Importante: ainda não há conector

O AI2P **ainda não executa tarefas** pelo Moonshot Kimi Code: só o Claude Code tem conector CLI. Por isso a entrada é criada **inativa**.

## Instalação e login (não é preciso chave de API)

1. Instalar o CLI: `irm https://code.kimi.com/kimi-code/install.ps1 | iex`.
2. Fazer login: `kimi  →  /login` — num terminal do computador servidor, com o usuário do AI2P.
3. Verifique que `kimi` está no PATH desse usuário; senão, escreva o caminho completo em `cliCommand`.

Login: rode `kimi` e use `/login` — Kimi Code OAuth ou chave da Moonshot AI Open Platform.

O modo não interativo (headless) não foi confirmado por fontes públicas.

O botão de chave na linha do catálogo mostra o comando de login deste CLI.

## Limites e custo

| | |
|---|---|
| Contexto | 262 144 |
| Custo | 0 (pago pela assinatura) |

## Licença

Resultado e termos conforme a assinatura do fornecedor (https://github.com/MoonshotAI/kimi-code). Verificado em 06.10.2026 por busca na web.

## Requisitos de hardware

Nenhum especial: quem calcula é o fornecedor.

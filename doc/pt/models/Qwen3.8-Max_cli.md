# Qwen3.8-Max_cli

**Hospedagem:** nuvem, conexão **por CLI, não por API**
**Conexão:** `provider: alibaba`, `transport: cli`, comando `qwen`, modelo `qwen3.8-max`
**Referência de chave:** vazia — **não é preciso chave de API**, autorização pela sessão do CLI

O mesmo modelo que Qwen3.8-Max, mas pelo agente de terminal do fornecedor **Qwen Code** (https://github.com/QwenLM/qwen-code). Pago pela assinatura do fornecedor; no faturamento do AI2P custa zero.

## Importante: ainda não há conector

O AI2P **ainda não executa tarefas** pelo Qwen Code: só o Claude Code tem conector CLI. Por isso a entrada é criada **inativa**.

## Instalação e login (não é preciso chave de API)

1. Instalar o CLI: `npm i -g @qwen-code/qwen-code@latest`.
2. Fazer login: `qwen  →  /auth` — num terminal do computador servidor, com o usuário do AI2P.
3. Verifique que `qwen` está no PATH desse usuário; senão, escreva o caminho completo em `cliCommand`.

Qwen Code é o fork do Gemini CLI da Alibaba. Login: rode `qwen` e use `/auth` (Qwen OAuth ou chave de API).

Execução não interativa (headless): `qwen -p "..."`.

O botão de chave na linha do catálogo mostra o comando de login deste CLI.

## Limites e custo

| | |
|---|---|
| Contexto | 262 144 |
| Custo | 0 (pago pela assinatura) |

## Licença

Resultado e termos conforme a assinatura do fornecedor (https://github.com/QwenLM/qwen-code). Verificado em 06.10.2026 por busca na web.

## Requisitos de hardware

Nenhum especial: quem calcula é o fornecedor.

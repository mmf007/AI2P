# Gemini-3.1-Pro_cli

**Hospedagem:** nuvem, conexão **por CLI, não por API**
**Conexão:** `provider: google`, `transport: cli`, comando `agy`, modelo `gemini-3.1-pro`
**Referência de chave:** vazia — **não é preciso chave de API**, autorização pela sessão do CLI

O mesmo modelo que Gemini-3.1-Pro, mas pelo agente de terminal do fornecedor **Google Antigravity CLI** (https://antigravity.google). Pago pela assinatura do fornecedor; no faturamento do AI2P custa zero.

## Importante: ainda não há conector

O AI2P **ainda não executa tarefas** pelo Google Antigravity CLI: só o Claude Code tem conector CLI. Por isso a entrada é criada **inativa**.

## Instalação e login (não é preciso chave de API)

1. Instalar o CLI: `irm https://antigravity.google/cli/install.ps1 | iex`.
2. Fazer login: `agy` — num terminal do computador servidor, com o usuário do AI2P.
3. Verifique que `agy` está no PATH desse usuário; senão, escreva o caminho completo em `cliCommand`.

O Antigravity CLI substituiu o Gemini CLI em 18.06.2026. Na primeira execução o `agy` imprime um link e um código: abra o link, entre no Google e cole o código.

Execução não interativa (headless): `agy -p "..."`.

O botão de chave na linha do catálogo mostra o comando de login deste CLI.

## Limites e custo

| | |
|---|---|
| Contexto | 1 000 000 |
| Custo | 0 (pago pela assinatura) |

## Licença

Resultado e termos conforme a assinatura do fornecedor (https://antigravity.google). Verificado em 06.10.2026 por busca na web.

## Requisitos de hardware

Nenhum especial: quem calcula é o fornecedor.

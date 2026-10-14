# Grok-4.7_cli

**Hospedagem:** nuvem, conexão **por CLI, não por API**
**Conexão:** `provider: xai`, `transport: cli`, comando `grok`, modelo `grok-4.7`
**Referência de chave:** vazia — **não é preciso chave de API**, autorização pela sessão do CLI

O mesmo modelo que Grok-4.7, mas pelo agente de terminal do fornecedor **xAI Grok Build** (https://x.ai). Pago pela assinatura do fornecedor; no faturamento do AI2P custa zero.

## Importante: ainda não há conector

O AI2P **ainda não executa tarefas** pelo xAI Grok Build: só o Claude Code tem conector CLI. Por isso a entrada é criada **inativa**.

## Instalação e login (não é preciso chave de API)

1. Instalar o CLI: `npm install -g @xai-official/grok`.
2. Fazer login: `grok` — num terminal do computador servidor, com o usuário do AI2P.
3. Verifique que `grok` está no PATH desse usuário; senão, escreva o caminho completo em `cliCommand`.

Na primeira execução o `grok` abre o navegador para o login. Sem navegador, use a variável `XAI_API_KEY`.

Execução não interativa (headless): `grok -p "..."`.

O botão de chave na linha do catálogo mostra o comando de login deste CLI.

## Limites e custo

| | |
|---|---|
| Contexto | 256 000 |
| Custo | 0 (pago pela assinatura) |

## Licença

Resultado e termos conforme a assinatura do fornecedor (https://x.ai). Verificado em 06.10.2026 por busca na web.

## Requisitos de hardware

Nenhum especial: quem calcula é o fornecedor.

# Mistral-Large-3_cli

**Hospedagem:** nuvem, conexão **por CLI, não por API**
**Conexão:** `provider: mistral`, `transport: cli`, comando `vibe`, modelo `mistral-large-3`
**Referência de chave:** vazia — **não é preciso chave de API**, autorização pela sessão do CLI

O mesmo modelo que Mistral-Large-3, mas pelo agente de terminal do fornecedor **Mistral Vibe CLI** (https://github.com/mistralai/mistral-vibe). Pago pela assinatura do fornecedor; no faturamento do AI2P custa zero.

## Importante: ainda não há conector

O AI2P **ainda não executa tarefas** pelo Mistral Vibe CLI: só o Claude Code tem conector CLI. Por isso a entrada é criada **inativa**.

## Instalação e login (não é preciso chave de API)

1. Instalar o CLI: `curl -LsSf https://mistral.ai/vibe/install.sh | bash   (pip install mistral-vibe)`.
2. Fazer login: `vibe` — num terminal do computador servidor, com o usuário do AI2P.
3. Verifique que `vibe` está no PATH desse usuário; senão, escreva o caminho completo em `cliCommand`.

Na primeira execução o `vibe` abre um assistente: login com conta Mistral ou chave de API. Por padrão o Vibe usa Devstral; o modelo da entrada vem do campo «modelo».

O modo não interativo (headless) não foi confirmado por fontes públicas.

O botão de chave na linha do catálogo mostra o comando de login deste CLI.

## Limites e custo

| | |
|---|---|
| Contexto | 256 000 |
| Custo | 0 (pago pela assinatura) |

## Licença

Resultado e termos conforme a assinatura do fornecedor (https://github.com/mistralai/mistral-vibe). Verificado em 06.10.2026 por busca na web.

## Requisitos de hardware

Nenhum especial: quem calcula é o fornecedor.

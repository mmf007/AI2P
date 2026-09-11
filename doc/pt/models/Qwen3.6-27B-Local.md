# Qwen3.6-27B-Local

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: openai-compatible`, `baseUrl: http://localhost:8082/v1`,
modelo `qwen3.6-27b`
**Chave de API:** não é preciso — o servidor local sobe sem autorização
**O que faz:** código e textos, notas de habilidade de 75 a 82

A geração anterior da mesma linha: 27 bilhões de parâmetros, quantização **UD-Q4_K_XL**,
funciona pelo **llama.cpp / llama-server**. Em qualidade perde para o
Qwen3.8-27B-Local (código 82 contra 87) com as mesmas exigências de
hardware, e por isso ele é mantido principalmente por compatibilidade: se uma tarefa já foi
verificada nesta versão, não é obrigatório trocar o modelo dela. Não é preciso pagar, e **os
dados não saem do computador**.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos de IA → Qwen3.6-27B-Local → «Instalar»**. São instalados:

* o **pacote llama.cpp** (~0,5 GB) — a compilação com o `llama-server`, comum a outros modelos
  locais: se ele já estiver instalado, não é baixado uma segunda vez;
* o **arquivo de pesos** `Qwen3.6-27B-UD-Q4_K_XL.gguf` — **16,4 GiB** (17 612 564 704 bytes)
  no repositório de modelos (`storage.modelsRepo`, por padrão `C:\ai` no Windows e `~/ai`
  no Linux/macOS, grupo `Qwen3.6`).

É baixado com retomada, e o que foi baixado é conferido pelo tamanho exato. **Enquanto os
arquivos não estiverem no lugar, o modelo não pode ficar ativo** — isso é uma regra do AI2P, e
não um erro.

O comando de inicialização que é gravado no perfil depois da instalação:

```
llama-server.exe -m <caminho do .gguf> -ngl 99 -c 65536 --jinja --port 8082 --alias qwen3.6-27b
```

A porta **8082** é própria deste registro (nos modelos locais vizinhos são 8081 e 8083): dois
`llama-server` não sobem lado a lado na mesma porta, e o segundo modelo responderia em silêncio
com as respostas do primeiro.

## Licença

| | |
|---|---|
| Pesos do modelo | **Apache 2.0** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/Qwen/Qwen3.6-27B/blob/main/LICENSE> |
| Pagamento pela geração | não há — quem calcula é o seu computador |

O AI2P baixa não os pesos originais, e sim a quantização GGUF (`unsloth/Qwen3.6-27B-GGUF`) —
ela está publicada sob a mesma licença que o modelo original (`Qwen/Qwen3.6-27B`). A licença foi
extraída do cartão do modelo em 27.08.2026 por uma requisição ao HuggingFace (campo
`cardData.license`), e não de memória: nos modelos abertos ela muda raramente, mas antes de um
lançamento comercial verifique o cartão mais uma vez.

O próprio motor **llama.cpp é MIT** (arquivo `LICENSE` do repositório ggml-org/llama.cpp,
conferido em 27.08.2026), e por isso ele também não impõe condição alguma ao seu produto.

## Requisitos de hardware

| | |
|---|---|
| Espaço em disco | **a partir de 20 GB** por modelo (pesos + pacote llama.cpp) |
| Memória de vídeo | **24 GB** — o modelo inteiro na placa de vídeo |
| | **16 GB** — parte das camadas é descarregada na RAM, bem mais lento |
| | **menos de 12 GB** — pegue uma quantização menor (`UD-Q3_K_XL`) com um registro próprio no catálogo |
| Memória RAM | **a partir de 32 GB** |
| Processador | quanto mais núcleos, mais rápido no modo CPU |

Os números são uma **estimativa pelo tamanho dos pesos, não foram feitas medições**. O contexto
no comando de inicialização é de 65 536 tokens (`-c 65536`); quanto maior ele for, mais memória
de vídeo vai para o cache KV.

**A primeira inicialização é demorada:** carregar os pesos leva minutos — isso não é
travamento.

## Limitações e custo

| | |
|---|---|
| Contexto | 65 536 tokens (`-c 65536` no comando de inicialização) |
| Resposta máxima | 32 768 tokens (`params.maxTokens`) |
| Custo | 0 — quem calcula é o seu computador |

## Erros frequentes

* **«N tokens exceeds context 32768»** — o `-c` do comando de inicialização está pequeno;
  coloque `-c 65536` e reinicie o trabalho da equipe.
* **Resultado vazio, motivo `length`** — o `params.maxTokens` está pequeno; coloque 32768.
* **Os dois modelos Qwen respondem igual** — as portas dos comandos de inicialização
  coincidiram; este registro deve ter a 8082.
* **Falta memória** — feche o que for supérfluo ou pegue uma quantização menor.

# Qwen3.8-27B-Local

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: openai-compatible`, `baseUrl: http://localhost:8081/v1`,
modelo `qwen3.8-27b`
**Chave de API:** não é preciso — o servidor local sobe sem autorização
**O que faz:** código e textos, notas de habilidade de 80 a 87

A melhor opção «para uma placa de vídeo» do catálogo: 27 bilhões de parâmetros, quantização
**UD-Q4_K_XL**, funciona pelo **llama.cpp / llama-server**. Em código (nota 87) ultrapassa o
Qwen3.6-27B-Local e o Muse-Glimmer-30B-Local, e não é
preciso pagar nada: **os dados não saem do computador**. Uma escolha sensata para tarefas que
não podem ser entregues à nuvem.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos de IA → Qwen3.8-27B-Local → «Instalar»**. São instalados:

* o **pacote llama.cpp** (~0,5 GB) — a compilação com o `llama-server`;
* o **arquivo de pesos** `Qwen3.8-27B-UD-Q4_K_XL.gguf` — **16,7 GiB** (17 923 394 624 bytes)
  no repositório de modelos (`storage.modelsRepo`, por padrão `C:\ai` no Windows e `~/ai`
  no Linux/macOS, grupo `Qwen3.8`).

É baixado com retomada: uma instalação interrompida continua, o que já foi baixado não é baixado
de novo, e o que foi baixado é conferido pelo tamanho exato. **Enquanto os arquivos não
estiverem no lugar, o modelo não pode ficar ativo** — isso é uma regra do AI2P, e não um erro.

Depois da instalação, o comando de inicialização é gravado no perfil:

```
llama-server.exe -m <caminho do .gguf> -ngl 99 -c 65536 --jinja --port 8081 --alias qwen3.8-27b
```

A porta **8081** é própria deste registro: nos modelos locais vizinhos são 8082 e 8083, senão o
segundo `llama-server` não subiria ao lado do primeiro. O próprio AI2P inicia esse processo ao
começar o trabalho da equipe e o descarrega ao parar — **justamente o processo dele**; um
llama-server alheio na mesma porta não é tocado.

## Licença

| | |
|---|---|
| Pesos do modelo | **Apache 2.0** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/Qwen/Qwen3.8-27B> |
| Pagamento pela geração | não há — quem calcula é o seu computador |

O AI2P baixa não os pesos originais, e sim a quantização GGUF (`unsloth/Qwen3.8-27B-GGUF`) —
ela está publicada sob a mesma licença que o modelo original (`Qwen/Qwen3.8-27B`). A licença foi
extraída do cartão do modelo em 27.08.2026 por uma requisição ao HuggingFace (campo
`cardData.license`), e não de memória: nos modelos abertos ela muda raramente, mas antes de um
lançamento comercial verifique o cartão mais uma vez.

O próprio motor **llama.cpp é MIT** (arquivo `LICENSE` do repositório ggml-org/llama.cpp,
conferido em 27.08.2026), e por isso ele também não impõe condição alguma ao seu produto.

## Requisitos de hardware

| | |
|---|---|
| Espaço em disco | **a partir de 20 GB** por modelo (pesos + pacote llama.cpp) |
| Memória de vídeo | **24 GB** — o modelo inteiro na placa de vídeo (RTX 3090/4090/5090) |
| | **16 GB** — parte das camadas é descarregada na RAM, bem mais lento |
| | **menos de 12 GB** — pegue uma quantização menor (`UD-Q3_K_XL`) com um registro próprio no catálogo |
| Memória RAM | **a partir de 32 GB** |
| Processador | quanto mais núcleos, mais rápido no modo CPU |

Os números são uma **estimativa pelo tamanho dos pesos, não foram feitas medições**: o modelo
não foi executado neste computador. O contexto no comando de inicialização é de 65 536 tokens
(`-c 65536`); quanto maior ele for, mais memória de vídeo vai para o cache KV.

**A primeira inicialização é demorada:** carregar os pesos leva minutos. O AI2P espera a API
ficar pronta e mostra o membro da equipe no estado «iniciando» — isso não é travamento.

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
* **Falta memória / o sistema entra em swap** — feche o que for supérfluo ou pegue uma
  quantização menor.
* **A porta 8081 está ocupada** — troque o `--port` no comando de inicialização e o `baseUrl` no
  perfil.

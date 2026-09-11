# Muse-Glimmer-30B-Local

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: openai-compatible`, `baseUrl: http://localhost:8083/v1`,
modelo `muse-glimmer-30b`
**Chave de API:** não é preciso — o servidor local sobe sem autorização
**O que faz:** textos e código, notas de habilidade de 73 a 80

Um modelo aberto da Meta com 30 bilhões de parâmetros na quantização **UD-Q4_K_XL**, funciona
pelo **llama.cpp / llama-server**. É o parente mais novo do Muse-Spark-1.2, que fica na nuvem.
Em código é mais fraco que os modelos Qwen do mesmo tamanho (75 a 78 contra 82 a 87), mas em
compensação é mais parelho em textos — e, como todos os registros locais, é **gratuito e não
envia dados para lugar nenhum**.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos de IA → Muse-Glimmer-30B-Local → «Instalar»**. São instalados:

* o **pacote llama.cpp** (~0,5 GB) — a compilação com o `llama-server`, comum a outros modelos
  locais;
* o **arquivo de pesos** `Muse-Glimmer-30B-UD-Q4_K_XL.gguf` — **14,8 GiB** (15 878 222 368 bytes)
  no repositório de modelos (`storage.modelsRepo`, por padrão `C:\ai` no Windows e `~/ai`
  no Linux/macOS, grupo `Muse-Glimmer`).

Este é o menor arquivo entre os registros locais do catálogo, e por isso, quando falta memória
de vídeo, é mais fácil começar por ele. É baixado com retomada, e o que foi baixado é conferido
pelo tamanho exato. **Enquanto os arquivos não estiverem no lugar, o modelo não pode ficar
ativo** — isso é uma regra do AI2P, e não um erro.

O comando de inicialização que é gravado no perfil depois da instalação:

```
llama-server.exe -m <caminho do .gguf> -ngl 99 -c 65536 --jinja --port 8083 --alias muse-glimmer-30b
```

A porta **8083** é própria deste registro (nos modelos Qwen são 8081 e 8082): dois
`llama-server` não sobem lado a lado na mesma porta.

## Licença

| | |
|---|---|
| Pesos do modelo | **Apache 2.0** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/meta-models/Muse-Glimmer-30B> |
| Pagamento pela geração | não há — quem calcula é o seu computador |

O AI2P baixa não os pesos originais, e sim a quantização GGUF (`unsloth/Muse-Glimmer-30B-GGUF`)
— ela está publicada sob a mesma licença que o modelo original (`meta-models/Muse-Glimmer-30B`).
A licença foi extraída do cartão do modelo em 27.08.2026 por uma requisição ao HuggingFace
(campo `cardData.license`), e não de memória: nos modelos abertos ela muda raramente, mas antes
de um lançamento comercial verifique o cartão mais uma vez. Os dois cartões — o da quantização e
o do modelo original — indicam Apache 2.0.

O próprio motor **llama.cpp é MIT** (arquivo `LICENSE` do repositório ggml-org/llama.cpp,
conferido em 27.08.2026), e por isso ele também não impõe condição alguma ao seu produto.

## Requisitos de hardware

| | |
|---|---|
| Espaço em disco | **a partir de 20 GB** (pesos de 14,8 GiB + pacote llama.cpp) |
| Memória de vídeo | **24 GB** — o modelo inteiro na placa de vídeo, com folga para o contexto |
| | **16 GB** — cabe justinho ou com um pequeno descarregamento de camadas na RAM |
| | **menos de 12 GB** — pegue uma quantização menor (`UD-Q3_K_XL`) com um registro próprio no catálogo |
| Memória RAM | **a partir de 32 GB** |
| Processador | quanto mais núcleos, mais rápido no modo CPU |

Os números são uma **estimativa pelo tamanho dos pesos, não foram feitas medições**: o modelo
não foi executado neste computador. O contexto no comando de inicialização é de 65 536 tokens
(`-c 65536`); quanto maior ele for, mais memória de vídeo vai para o cache KV.

**A primeira inicialização é demorada:** carregar os pesos leva minutos — isso não é travamento.

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
* **A porta 8083 está ocupada** — troque o `--port` no comando de inicialização e o `baseUrl` no
  perfil.
* **Falta memória** — pegue uma quantização menor ou libere memória de vídeo.

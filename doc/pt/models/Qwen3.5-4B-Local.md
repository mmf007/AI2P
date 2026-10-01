# Qwen3.5-4B-Local

**Hospedagem:** LOCAL (funciona neste computador)
**Conexão:** `provider: openai-compatible`, `baseUrl: http://localhost:8084/v1`,
modelo `qwen3.5-4b`
**Chave de API:** não é necessária — o servidor local sobe sem autorização
**O que faz:** trabalho leve com texto e análise de dados, pontuações 62-74

O registro menos exigente em hardware de todo o catálogo. Foi criado pela tarefa T-347-S0
(23.09.2026) para o PAPEL DE PONTO: antes de um trabalho de mídia o ponto lê a descrição da
tarefa e devolve um pequeno json de controle conforme o esquema do perfil do modelo de
trabalho. Esse trabalho é sempre o mesmo e sempre simples — subir um modelo de 27 bilhões
ou pagar a nuvem não faz sentido.

4 bilhões de parâmetros, quantização **Q4_K_S**, motor **llama.cpp / llama-server**. O
modelo também serve para tarefas pequenas comuns — tradução, resumo, edição de texto —,
mas não convém para código nem análise complexa: o catálogo tem registros mais fortes
para isso.

## Não precisa de chave, precisa de instalação

Tudo é instalado pelo botão **«Instalar»** do formulário do modelo: **Configurações →
Catálogos → Modelos de IA → Qwen3.5-4B-Local → «Instalar»**. São instalados:

* o **pacote llama.cpp** (~0,5 GB) — uma compilação com `llama-server`;
* o **arquivo de pesos** `Qwen3.5-4B-Q4_K_S.gguf` — **2,41 GiB** (2 590 430 368 bytes) no
  repositório de modelos (`storage.modelsRepo`, grupo `Qwen3.5-4B`).

O tamanho foi obtido por uma requisição HEAD ao HuggingFace em 23.09.2026: resposta 200 sem
token, o repositório `unsloth/Qwen3.5-4B-GGUF` não é restrito por licença. O download é
retomável e o que foi baixado é conferido pelo tamanho exato. **Enquanto os arquivos não
estiverem no lugar o modelo não pode ficar ativo** — é uma regra do AI2P, não um erro.

Após a instalação o comando de inicialização é escrito no perfil:

```
llama-server.exe -m <caminho do .gguf> -ngl 99 -c 16384 --cache-type-k q8_0 --cache-type-v q8_0 --jinja --port 8084 --alias qwen3.5-4b
```

A porta **8084** é própria deste registro: 8080-8083 estão ocupadas pelos modelos locais
vizinhos, caso contrário um segundo `llama-server` não subiria ao lado do primeiro.

## Licença

| | |
|---|---|
| Pesos do modelo | **Apache 2.0** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/unsloth/Qwen3.5-4B-GGUF> |
| Pagamento por geração | nenhum — quem calcula é o seu computador |

O AI2P baixa não os pesos originais, mas uma quantização GGUF
(`unsloth/Qwen3.5-4B-GGUF`), publicada sob a mesma licença do modelo Qwen original. A
licença foi tirada do cartão do modelo em 23.09.2026 por uma consulta ao HuggingFace
(campo `cardData.license`), e não de memória.

O próprio motor **llama.cpp está sob MIT**, portanto também não impõe condições ao seu
produto.

## Requisitos de hardware

| | |
|---|---|
| Espaço em disco | **a partir de 4 GB** (pesos + pacote llama.cpp) |
| Memória de vídeo | **3 GB** — todas as camadas na GPU com contexto 16 384 e cache KV `q8_0` |
| | **2 GB** — reduza `-c` para 4096 ou pegue a quantização `IQ4_XS` como registro próprio |
| | sem placa de vídeo — roda na CPU, mais devagar, mas suficiente para o ponto |
| Memória RAM | **a partir de 10 GB** para a máquina toda |
| Processador | quanto mais núcleos, mais rápido no modo CPU |

Os números são **uma estimativa pelo tamanho dos pesos e do cache KV, não houve medições**:
o modelo não foi executado neste computador. Os pesos Q4_K_S ocupam 2,41 GiB e o cache de
chaves e valores com `-c 16384` e `q8_0`, menos de 200 MiB.

## Limites e preço

| | |
|---|---|
| Contexto | 16 384 tokens (`-c 16384` no comando de inicialização) |
| Resposta máxima | 8 192 tokens (`params.maxTokens`) |
| Preço | 0 — quem calcula é o seu computador |

## Este registro NÃO é desativado por antiguidade de versões

A regra usual do catálogo é «um modelo com três versões mais novas da mesma família deve
ser desligado». Ela NÃO se aplica a este registro enquanto o arquivo de pesos estiver
disponível para download: o valor dele não é a qualidade, e sim o tamanho, e o catálogo não
tem substituto com esses requisitos de hardware. Há um único motivo para desligá-lo: o
repositório `unsloth/Qwen3.5-4B-GGUF` deixar de servir o arquivo.

## Erros frequentes

* **O trabalho de mídia vai para a geração sem json de controle** — o executor ponto não
  tem modelo escolhido, ou o perfil do modelo de trabalho não traz `prompter.required`.
* **Falta memória de vídeo** — reduza `-c` no comando: o contexto é o principal consumidor
  de memória além dos próprios pesos.
* **A porta 8084 está ocupada** — troque `--port` no comando e `baseUrl` no perfil.
* **As respostas são piores que o esperado** — é um modelo de 4 bilhões de parâmetros; para
  código e análise complexa pegue registros mais fortes.

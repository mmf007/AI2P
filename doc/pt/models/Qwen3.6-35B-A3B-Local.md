# Qwen3.6-35B-A3B-Local

**Alocação:** LOCAL (funciona neste computador)
**Conexão:** `provider: openai-compatible`, `baseUrl: http://localhost:8080/v1`
**Chave de API:** não é preciso — o servidor local sobe sem autorização
**O que faz:** código e textos, notas de habilidade de 72 a 80

O modelo de texto Qwen 3.6 (35 bilhões de parâmetros, MoE com 3 bilhões ativos) na quantização
**Q4_K_M**, funciona pelo **llama.cpp / llama-server**. A qualidade é menor que a dos modelos em
nuvem, mas em compensação é **gratuito e não vai para lugar nenhum**: os dados não saem do
computador. Uma escolha sensata para tarefas que não podem ser entregues à nuvem.

## Não é preciso chave, é preciso instalar

Tudo se instala pelo botão **«Instalar»** no formulário do modelo: **Configurações → Catálogos
→ Modelos de IA → Qwen3.6-35B-A3B-Local → «Instalar»**. São instalados:

* o **pacote llama.cpp** (~640 MB) — a compilação com o `llama-server`;
* o **arquivo de pesos** `Qwen3.6-35B-A3B-UD-Q4_K_M.gguf` — **~20,6 GiB** no repositório de
  modelos (`storage.modelsRepo`, por padrão `C:\ai` no Windows e `~/ai` no Linux/macOS,
  grupo `Qwen3.6`).

É baixado com retomada: uma instalação interrompida continua e o que já foi baixado não é
baixado de novo. Enquanto os arquivos não estiverem no lugar, o modelo não pode ficar ativo.

Depois da instalação, o comando de inicialização é gravado automaticamente no perfil:

```
llama-server.exe -m <caminho do .gguf> --n-cpu-moe 99 -ngl 99 -c 65536 --jinja
                 --port 8080 --alias qwen3.6-35b-a3b
```

O próprio AI2P levanta esse processo ao começar o trabalho da equipe e o descarrega ao parar —
**justamente o processo dele**; um llama-server alheio já em execução na 8080 não é tocado.

## Licença

| | |
|---|---|
| Pesos do modelo | **Apache 2.0** |
| Uso comercial | permitido |
| O que é obrigatório | manter o texto da licença e o aviso de direitos autorais |
| Texto da licença | <https://huggingface.co/Qwen/Qwen3.6-35B-A3B/blob/main/LICENSE> |
| Pagamento pela geração | não há — quem calcula é o seu computador |

O AI2P baixa não os pesos originais, e sim a quantização GGUF (`unsloth/Qwen3.6-35B-A3B-GGUF`) —
ela está publicada sob a mesma licença que o modelo original (`Qwen/Qwen3.6-35B-A3B`). A licença
foi extraída do cartão do modelo em 27.08.2026 por uma requisição ao HuggingFace (campo
`cardData.license`), e não de memória: nos modelos abertos ela muda raramente, mas antes de um
lançamento comercial verifique o cartão mais uma vez.

O próprio motor **llama.cpp é MIT** (arquivo `LICENSE` do repositório ggml-org/llama.cpp,
conferido em 27.08.2026), e por isso ele também não impõe condição alguma ao seu produto.

## Requisitos de hardware

| | |
|---|---|
| Espaço em disco | **~22 GB** (pesos + pacote) |
| Memória RAM | **no mínimo 24 GB**, confortável com 32 GB |
| Placa de vídeo | não é obrigatória, mas acelera bastante |
| VRAM | a partir de 6 GB já dá um ganho perceptível; a chave `--n-cpu-moe 99` mantém os experts na RAM e o resto vai para a GPU |
| Processador | quanto mais núcleos, mais rápido no modo CPU |

O modelo é **MoE**: com 35 bilhões de parâmetros ficam ativos cerca de 3 bilhões, e por isso ele
é bem mais rápido que um modelo denso do mesmo tamanho. Mas ele é carregado inteiro na memória —
os 20,6 GiB de pesos precisam caber em algum lugar.

**A primeira inicialização é demorada:** carregar os pesos leva minutos. O AI2P espera a API
ficar pronta e mostra o membro da equipe no estado «iniciando» — isso não é travamento.

## Contexto e resposta máxima

| | |
|---|---|
| Contexto | 65 536 tokens (`-c 65536` no comando de inicialização) |
| Resposta máxima | 32 768 tokens (`params.maxTokens`) |

Essa combinação foi verificada e é importante: com um contexto **menor**, as respostas eram
cortadas pelo motivo `length` — o modelo queimava o limite em raciocínio e devolvia resultado
vazio. Se você mudar um dos números, mude o outro também.

## Erros frequentes

* **«37529 tokens exceeds context 32768»** — o comando de inicialização está com um `-c`
  pequeno. Coloque `-c 65536` no campo «Comando de inicialização» do perfil e reinicie o
  trabalho da equipe.
* **Resultado vazio, motivo `length`** — o `params.maxTokens` está pequeno; coloque 32768.
* **Falta memória / o sistema entra em swap** — o modelo precisa de cerca de 24 GB de RAM; feche
  o que for supérfluo ou pegue uma quantização menor (aí será preciso criar um registro próprio
  no catálogo com um manifesto próprio).
* **A porta 8080 está ocupada** — troque o `--port` no comando de inicialização e o `baseUrl` no
  perfil.

## Forma antiga de instalação

Os scripts `install_local_model.bat/.ps1/.sh` no diretório do aplicativo instalam o mesmo modelo
sem interface (para cenários offline). Eles criam um **registro personalizado à parte** no
catálogo — não o confunda com o embutido. A partir da versão 1.42, a forma padrão é o botão
«Instalar».

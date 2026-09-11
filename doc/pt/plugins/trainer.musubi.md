# Musubi Tuner (LoRA) — o plugin `trainer.musubi`

**O que ele faz:** treina adaptadores **LoRA** com o treinador musubi-tuner (kohya-ss) — cache
de latentes, cache das saídas dos codificadores de texto e o treinamento em si. O trabalho leva
horas, por isso é realizado por uma **tarefa separada** com um executor «software automático», e
não por uma ferramenta do agente de IA.

**Para que ele serve.** O treinador oficial do Kandinsky exige várias placas de vídeo e Linux;
o musubi-tuner se vira com uma placa só, funciona no Windows e grava o arquivo do adaptador com
os nomes de chave que o ComfyUI entende. O botão «Treinar» do editor de LoRA cria a tarefa
justamente sobre este plugin.

As fontes do treinador: https://github.com/kohya-ss/musubi-tuner

---

## Os programas

O plugin tem **dois** programas, e ambos são obrigatórios:

| Registro | O que é | Como se instala |
|---|---|---|
| **Python 3.10–3.12** | é com ele que os três passos são disparados | pelo botão «Instalar» (o nosso pacote `python`) ou pelo caminho de um Python já instalado. No 3.13 o musubi-tuner não compila, e um 3.13 encontrado é considerado inadequado |
| **Musubi Tuner** | a pasta de scripts do treinador | pelo botão «Instalar» (as fontes v0.3.4) ou pelo caminho da pasta onde você as descompactou: nela está o `kandinsky5_train_network.py`. No `PATH` ela não é procurada: é uma pasta, não um programa |

O ambiente com torch o próprio treinador cria para si, na subpasta `.venv`, no primeiro
treinamento.

**O registro do plugin costuma ser criado sozinho** — na instalação de um modelo que declara
pacotes de treinamento (as duas entradas do Kandinsky 5 os declaram). No mesmo momento o
programa encontrado é anotado no campo de caminho vazio. A inicialização a pessoa continua
fazendo com o botão «Inicializar»: ela cria os registros do catálogo de ações e, com eles, os
pontos aos quais as regras de segurança se prendem.

---

## As operações de execução longa

Três passos, exatamente nesta ordem:

| Operação | O que faz |
|---|---|
| `lora.cacheLatents` | passa os quadros do dataset pelo VAE e guarda os latentes no cache |
| `lora.cacheText` | calcula as saídas dos codificadores de texto (Qwen2.5-VL e CLIP) sobre as legendas dos quadros; o passo é obrigatório, sem ele o treinamento não arranca |
| `lora.train` | o treinamento do adaptador em si; marcado como **«Uma instância»** |

A marca «Uma instância» no treinamento não é enfeite: a placa de vídeo é uma só, e um segundo
treinamento disparado ao mesmo tempo terminaria numa recusa por memória em um minuto. A segunda
tarefa **espera** na fila.

O resultado do treinamento é **o arquivo `.safetensors` mais recente** da pasta de trabalho
pela máscara do nome do adaptador: o nome do arquivo da época não é conhecido de antemão. Os
limites: o tempo limite de silêncio é de 15 minutos e o limite geral, de um dia.

---

## Os ajustes do registro

São definidos em **«Configurações → Plugins e MCP»**, no formulário do registro; no texto de uma
tarefa eles não podem ser mudados.

| Chave | Padrão | O que significa |
|---|---|---|
| `scripts` | vazio | a pasta de scripts do musubi-tuner — é obrigatório preencher |
| `dit`, `vae` | vazio | os arquivos de pesos do modelo |
| `task` | `k5-lite-t2v-5s-sd` | a tarefa do treinador; a outra opção é `k5-lite-i2v-5s-sd` |
| `textEncoderQwen` | `Qwen/Qwen2.5-VL-7B-Instruct` | o codificador de texto |
| `textEncoderClip` | `openai/clip-vit-large-patch14` | o segundo codificador de texto |
| `steps` | `2000` | o número de passos de treinamento |
| `networkDim`, `networkAlpha` | `32`, `32` | o tamanho da rede LoRA |
| `learningRate` | `1e-4` | a taxa de aprendizado |
| `mixedPrecision` | `bf16` | a precisão de cálculo (`bf16`, `fp16`, `no`) |
| `outputName` | `lora` | o nome do arquivo do adaptador |

O treinador precisa dos pesos **originais** dos codificadores de texto (o Qwen2.5-VL tem cerca
de 16 GB), e não das compilações reduzidas do ComfyUI: com elas o treinamento não vai.

---

## Limitações por sistema operacional

**Windows** — é para ele que os nossos pacotes de Python e do musubi-tuner são feitos. **Linux
e macOS** — o treinador os suporta, mas nós não conferimos ao vivo: neles é mais seguro
instalar o Python e os scripts por meios próprios e indicar os caminhos à mão.

A placa de vídeo é necessária de qualquer forma: no processador o treinamento não é calculado em
tempo razoável.

---

## O que este plugin não faz

* **não publica ferramentas ao agente de IA** — o treinamento é montado como tarefa, e não como
  chamada do agente;
* **não monta o dataset**: os quadros, as legendas e os arquivos de configuração do treinador
  quem prepara é o próprio AI2P, pelos ajustes do modelo;
* **o ambiente `.venv`** quem cria é o treinador, não o plugin;
* hoje as três operações estão descritas no manifesto, mas o disparo real do treinamento do
  Kandinsky é feito pelo comando do perfil do modelo; o plugin ainda assim dá a amarração ao
  servidor, a marca «Uma instância» e o ponto ao qual a regra de segurança se prende.

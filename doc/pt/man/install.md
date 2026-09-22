# Instalação do AI2P e onde ficam os dados dele

Este capítulo responde a duas perguntas: **onde o programa é instalado** e **onde ficam depois
os arquivos de trabalho dele** — a configuração, o banco de tarefas, os logs e as chaves. A
segunda pergunta é mais importante do que parece: os arquivos de trabalho sobrevivem à
atualização de versão, são copiados quando se muda de computador, e são justamente eles que não
se pode perder.

---

## 1. Três formas de instalar

| Forma | O que é | Quando é cômodo |
|---|---|---|
| **Pacote de instalação** | um único arquivo `AI2P_v_1_NN_…` (ou `AI2P_v_1_NN_full_…`, com o runtime dentro), instalado com um duplo clique | o caso comum no Windows |
| **Script de instalação** | `install.cmd` / `install.sh` do diretório da distribuição | quando a distribuição já foi baixada e o diretório é escolhido à mão |
| **Só a distribuição** | o diretório descompactado, executa-se `AI2P.Server.exe` | testes, instalação portátil em um pendrive |

O pacote de instalação no Windows pergunta se a instalação é **para todos os usuários** ou
**somente para mim**. Disso depende o diretório do programa:

* **para todos** — `C:\Program Files\AI2P` (exige direitos de administrador);
* **somente para mim** — `C:\Users\<você>\AppData\Local\Programs\AI2P`.

No Linux e no macOS a instalação vai para o diretório do usuário: por padrão `~/ai/AI2P`.

---

## 2. Onde ficam os arquivos de trabalho

A regra é uma só: **os arquivos de trabalho ficam junto ao programa, se for possível gravar no
diretório dele.**

Junto ao programa quer dizer o `config.json`, o diretório de dados `data/` (o banco do servidor,
os bancos das organizações, os arquivos dos projetos), os logs `logs/` e as chaves `secrets/`.

No diretório de programas do Windows (`C:\Program Files`) um usuário comum **não pode** gravar,
e isso não é um defeito, é uma regra do sistema: caso contrário qualquer usuário do computador
poderia trocar o `AI2P.Server.exe` que depois é executado pelo administrador. Por isso, na
instalação «para todos» os arquivos de trabalho ficam à parte:

| Instalação | Programa | Arquivos de trabalho |
|---|---|---|
| Windows, «para todos» | `C:\Program Files\AI2P` | **`C:\ProgramData\AI2P`** |
| Windows, «somente para mim» | `…\AppData\Local\Programs\AI2P` | junto ao programa |
| Distribuição em pasta própria (`D:\AI2P`) | `D:\AI2P` | junto ao programa |
| Linux/macOS, `~/ai/AI2P` | `~/ai/AI2P` | junto ao programa |
| Linux/macOS, `/opt/ai2p` | `/opt/ai2p` | `/var/lib/ai2p`, e sem permissão nele, `~/.local/share/ai2p` |

`C:\ProgramData\AI2P` é o diretório comum **deste computador**: a instalação é uma só, os dados
são comuns, e o serviço (que funciona em nome do sistema) enxerga os mesmos dados que a pessoa.
O instalador cria esse diretório e o abre para gravação a todos os usuários do computador; ao
desinstalar o programa ele **permanece** — os dados são seus.

### Como saber com certeza

Ao iniciar, o próprio programa diz onde estão os arquivos de trabalho dele:

```
AI2P: o diretório do programa C:\Program Files\AI2P\ está fechado para gravação —
os arquivos de trabalho (config.json, data, logs, secrets) ficam em C:\ProgramData\AI2P
```

A mesma linha vai para o log técnico (`logs/ai2p-<data>.jsonl`), e os caminhos completos da
configuração e do diretório de dados aparecem no log logo depois do início.

### Como definir o diretório você mesmo

| Forma | O que faz |
|---|---|
| `AI2P.Server.exe --config D:\meuAI2P\config.json` | usar exatamente esse `config.json`; `data/`, `logs/` e `secrets/` ficarão ao lado dele |
| variável de ambiente `AI2P_HOME=D:\meuAI2P` | o mesmo, mas definido uma vez para o serviço, o contêiner ou o atalho |

Um diretório definido assim é mais forte do que qualquer regra: o programa não interfere nele.

---

## 3. Atualização de versão

Instalar por cima da versão antiga **não toca** nos arquivos de trabalho: `config.json`,
`data/` e `secrets/` ficam como estavam. Junto ao programa é colocado o `config.new.json` — a
configuração da nova versão —, e no primeiro início o programa mescla os dois: a base é a nova
(nela estão todos os parâmetros novos com seus padrões) e por cima ficam os seus valores. Sobre
a mesclagem ele avisa com uma linha no console, e ao lado do `config.json` de trabalho fica uma
cópia do arquivo aplicado (`config.new.json.applied`) — por ela se vê o que chegou e quando.

A mesclagem acontece **exatamente uma vez por versão**: um novo início não reescreve nada.

### Dados que ficaram junto ao programa

Se antes você instalava o AI2P em `C:\Program Files\AI2P` e o executava **como administrador**,
os dados podem ter sido criados ali mesmo. Nesse caso, depois da atualização o programa diz:

```
AI2P: junto ao programa ficou o diretório de dados C:\Program Files\AI2P\data do
funcionamento anterior — agora os dados ficam em C:\ProgramData\AI2P; se precisar, mova-o
para lá à mão
```

Ele mesmo **não move** esses dados: transferir o banco pelas costas da pessoa não se faz. As
configurações (o `config.json` da instalação anterior), essas sim, ele leva consigo — a porta, o
nome do host e os diretórios foram definidos por você, e não há motivo para perdê-los. Para
mover também os dados: pare o AI2P, copie o diretório `data` (e também `secrets`, se estiver
ali) para o novo diretório de arquivos de trabalho e inicie de novo.

### `AI2P_HOME: parameter not set` ao atualizar no Linux/macOS

As versões **1.100 e 1.101** interrompiam a atualização nesta linha:

```
./install.sh: 147: AI2P_HOME: parameter not set
```

Nada era copiado, e a versão instalada permanecia a mesma. A culpa era do próprio instalador:
ele lia a variável `AI2P_HOME`, que uma instalação comum não tem. A partir da versão **1.102**
isso não acontece mais.

Se você só tem em mãos uma pasta de release 1.100 ou 1.101, ainda assim dá para fazer a
atualização com ela — basta definir a variável com um valor vazio:

```sh
AI2P_HOME= ./install.sh ~/ai/AI2P
```

O mesmo remédio serve para o `makeAsServise.sh` dessas mesmas versões.

---

### Atualização a partir do próprio programa

«Configurações → Geral», ao lado do número da versão, tem o botão **«Verificar atualizações»**. Ele
consulta o repositório de versões (por padrão `https://github.com/mmf007/ai2p`, o endereço é editado
ali mesmo) e procura o arquivo **para esta instalação**: o mesmo sistema, a mesma arquitetura e o
mesmo modo de instalação — versão completa (com o runtime dentro) ou comum. O modo de instalação é
lido do `version.json` que fica junto ao programa.

Se saiu uma versão mais nova, acende o botão **«Atualizar»**. Antes de instalar, o programa pergunta
quem está ocupado: a atualização **reinicia o servidor** e os agentes em execução de todas as
organizações abertas serão parados — a pergunta mostra a lista deles. Depois o pacote é baixado, o
programa encerra e um script à parte conclui a instalação: espera o fim do processo, instala o pacote
em silêncio e levanta o servidor de volta (o serviço, com `net start` / `systemctl`; a execução em
console, iniciando o programa de novo). A página no navegador precisará ser recarregada.

Duas caixas ao lado:

* **verificação automática** — apenas ver se saiu uma versão nova (a resposta vai para o registro);
* **atualização automática** — verificar e instalar em seguida.

Quando o programa é executado em **console**, a verificação automática acontece ao iniciar. Quando
ele trabalha como **serviço do sistema**, não há início por semanas — então a caixa cria uma entrada
na **agenda** (uma vez por dia, às 2:00 da hora local por padrão), e as configurações mostram o
código dela: a hora é alterada na própria entrada, como em qualquer agenda. Desmarcar a caixa apaga
a entrada.

## 4. Serviço do sistema operacional

O serviço e o início comum usam **o mesmo** diretório de arquivos de trabalho — ele é escolhido
pelo diretório do programa, e não pelos direitos de quem executou. Ou seja, ao transformar a
instalação em serviço (`makeAsServise`, veja [service](service.md)), você não ganha um segundo
banco «para o sistema».

---

## 5. Se o programa não iniciar

Executado pelo atalho, o AI2P é um programa de console comum: se houver falha no início, ele
**escreve o motivo por extenso e mantém a janela aberta** até que uma tecla seja pressionada (a
própria janela fecha depois de um minuto). O que pode acontecer:

| O que se vê | O que é | O que fazer |
|---|---|---|
| `AI2P: o início falhou — Access to the path … is denied` | os arquivos de trabalho foram definidos em um diretório sem permissão de gravação | indicar outro diretório (`AI2P_HOME`, `--config`) |
| `Não há onde colocar os arquivos de trabalho do AI2P` | estão fechados tanto o diretório do programa quanto os dois diretórios de dados | o mesmo: definir o diretório explicitamente |
| `O AI2P já está em execução (…) — abrindo o navegador e saindo` | não é um erro: só existe uma instância neste computador, e ela já está funcionando | nada |
| a janela fecha na hora e em silêncio | uma versão **anterior à 1.100**: ali uma falha no início levava a janela junto com a mensagem | atualize, ou execute o `AI2P.Server.exe` a partir de um console e leia a saída |

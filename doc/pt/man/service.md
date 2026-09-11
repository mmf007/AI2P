# Executar o AI2P como serviço do sistema operacional

Por padrão o AI2P é um **programa de console comum**: executou o `AI2P.Server.exe`, ele
funciona; fechou a janela, ele parou. Assim é cômodo ver o que acontece, e é assim também que
funciona o primeiro início.

Mas um servidor de tarefas normalmente é necessário **o tempo todo**: ele conduz a fila de
trabalhos, levanta os agentes de IA, replica com os outros servidores do cluster e cuida das
agendas. Ele não tem por que esperar que o dono do computador faça logon e abra uma janela. Para
isso o aplicativo sabe funcionar como **serviço do sistema operacional**.

O nome do serviço é **`AI2P`**, o mesmo em todos os sistemas.

---

## 1. Como transformar a instalação em serviço

O serviço é criado **à mão e uma única vez** — pelo script `makeAsServise` do diretório de
instalação (aquele em que está o `AI2P.Server.exe`; o script é colocado ali pela instalação).

### Windows

Abra o console **como administrador** (só ele cria serviços) e execute:

```powershell
cd D:\AI2P
.\makeAsServise.cmd
```

O script cria o serviço `AI2P`, define para ele o início automático na inicialização do
computador, liga o reinício após falha e o inicia de imediato. No fim ele imprime o endereço em
que a interface se abre.

Opções:

| Opção | O que faz |
|---|---|
| `-WhatIf` | apenas mostrar o que será feito; não alterar nada |
| `-NoStart` | criar o serviço, mas não iniciá-lo |
| `-Manual` | início manual do serviço, e não na inicialização do sistema |
| `-Remove` | remover o serviço (arquivos e dados ficam intactos) |
| `-Target D:\AI2P` | configurar outra instalação, e não aquela de onde o script foi executado |
| `-Account .\mike -Password ***` | o serviço funciona em nome de um usuário, e não como LocalSystem |
| `-UnprotectSecrets` | retirar a proteção DPAPI das chaves das organizações (veja a seção 4) |
| `-Force` | fazer apesar dos avisos |

### Linux

```sh
cd ~/ai/AI2P
./makeAsServise.sh
```

É criado um unit **de usuário** do systemd, `~/.config/systemd/user/AI2P.service`, e isso não é
por acaso: o servidor do AI2P funciona com um usuário comum, e não com o root, e guarda tudo o
que é seu — dados, logs, configurações e segredos — no diretório dele. Para que esse serviço
suba mesmo sem o logon da pessoa, o script executa por conta própria `loginctl enable-linger`.

O unit de sistema (`/etc/systemd/system/AI2P.service`, exige `sudo`) é criado com a opção
`--system`. As demais opções: `--no-start`, `--manual`, `--remove`, `--target <diretório>`.

### macOS

```sh
cd ~/ai/AI2P
./makeAsServise.sh
```

É criado o job do launchd `~/Library/LaunchAgents/AI2P.plist`. As opções são as mesmas, exceto
`--system`.

---

## 2. Em que o início como serviço difere do de console

O programa é o mesmo, não existe uma compilação «de servidor» à parte. O aplicativo **descobre
sozinho** como foi iniciado: sob o gerenciador de serviços do Windows e sob o systemd ele se
reconhece, e o launchd não dá esse indício — ali o modo é definido pela opção `--service` no
próprio job.

As diferenças são apenas três:

* **o navegador não abre no início.** O serviço não tem área de trabalho e não tem onde abrir
  uma janela — acesse o endereço você mesmo;
* **uma porta ocupada é um erro.** O início de console em uma porta ocupada abre o navegador na
  instância já em funcionamento e sai tranquilamente; o serviço, nesse caso, sai **com erro**,
  senão o gerenciador de serviços consideraria a saída silenciosa como funcionamento normal e
  não diria nada;
* **a parada vem por comando do sistema**, e não por Ctrl+C: o aplicativo consegue fechar o
  banco, descarregar os modelos locais que levantou e encerrar as sessões de replicação.

Todo o resto — dados, configurações, porta, interface, cluster — não muda.

Dá para verificar como o servidor foi levantado direto na interface: **Configurações →
Principal**, a linha **«Início»** ao lado da versão do aplicativo.

---

## 3. Gerenciar o serviço

**Windows**

```powershell
Get-Service AI2P            # situação
Start-Service AI2P
Stop-Service AI2P
.\makeAsServise.ps1 -Remove # remover o serviço
```

**Linux** (unit de usuário)

```sh
systemctl --user status AI2P
systemctl --user start AI2P
systemctl --user stop AI2P
journalctl --user -u AI2P -f     # o que o servidor escreve
./makeAsServise.sh --remove
```

**macOS**

```sh
launchctl list | grep AI2P
launchctl unload ~/Library/LaunchAgents/AI2P.plist
launchctl load   ~/Library/LaunchAgents/AI2P.plist
./makeAsServise.sh --remove
```

Os seus próprios logs o aplicativo escreve, em qualquer modo, no diretório `logs/` da instalação
(`ai2p-<data>.jsonl`) — ali também se vê o motivo de um início malsucedido.

---

## 4. Windows: em nome de quem o serviço funciona

Este é o único ponto em que a escolha realmente importa.

O serviço, por padrão, funciona como **LocalSystem** — a conta do computador. E a **chave da
organização** (com a qual são cifradas as chaves de API dos modelos) no Windows é protegida pelo
mecanismo DPAPI **do escopo do usuário**: só a conta que a gravou consegue decifrá-la. Ou seja,
o serviço como LocalSystem não vai ler as chaves de API, e os modelos em nuvem param de
funcionar — visto de fora, isso parece «o modelo existe, mas a tarefa não anda».

Por isso o `makeAsServise` olha dentro do `secrets.json` e, ao encontrar ali valores protegidos,
**não cria o serviço em silêncio**: ele oferece uma escolha:

* **`-Account <conta> -Password <senha>`** — o serviço funciona com o mesmo usuário, e as chaves
  ficam acessíveis como antes. A conta precisa do direito **«Fazer logon como serviço»**
  (`secpol.msc` → Políticas locais → Atribuição de direitos de usuário); sem ele o serviço não
  inicia e informa isso com o erro 1069;
* **`-UnprotectSecrets`** — retirar a DPAPI: as chaves das organizações ficam no `secrets.json`
  em base64 comum, exatamente como ficam no Linux e no macOS, e quem as protege passam a ser as
  permissões do arquivo. O arquivo anterior é guardado ao lado como `secrets.json.dpapi.bak`;
* **`-Force`** — criar o serviço como está, sabendo que as chaves de API ficarão inacessíveis
  para ele.

O mesmo vale para o **logon no Claude CLI**: ele pertence ao perfil do usuário, e o serviço como
LocalSystem não o enxerga. Se neste servidor houver agentes trabalhando pelo Claude CLI, o
serviço tem de ser criado com o mesmo usuário.

No Linux e no macOS essa escolha não existe de forma alguma: ali a chave da organização fica em
base64 comum, e o serviço já funciona com o mesmo usuário.

### Se os arquivos de trabalho não estiverem junto ao programa

Uma instalação **no diretório de programas do sistema** (`C:\Program Files\AI2P`, `/usr`,
`/opt`, `/Applications`) mantém `config.json`, `data`, `logs` e `secrets` não junto ao programa,
e sim no diretório de dados: no Windows é `C:\ProgramData\AI2P`, no Linux e no macOS é
`/var/lib/ai2p`; se ali também não for possível gravar, o aplicativo vai para o diretório de
dados do usuário.

O `makeAsServise` leva isso em conta sozinho: ele encontra o `config.json` de trabalho, coloca
ali a marca `serviceMode`, procura ali as chaves protegidas — e **registra esse caminho no
serviço com a opção `--config`**. Este último ponto é importante: o serviço funciona com outra
conta, e o diretório de dados do usuário dele seria **outro**; sem o caminho explícito ele
criaria um banco vazio no lugar do de trabalho. Na saída do script esse caso aparece na linha
«Arquivos de trabalho: …».

O diretório também pode ser definido por você — pela variável de ambiente `AI2P_HOME` ou pela
opção `--config` do próprio programa; nesse caso todos os scripts usam justamente ele.

---

## 5. Atualização de versão

**Não é preciso fazer nada de especial.** A instalação lembra que é um serviço, e as três formas
de atualização levam isso em conta:

* `install.cmd` / `install.ps1` (Windows) e `./install.sh` (Linux, macOS) param o serviço
  sozinhos antes de copiar os arquivos e o iniciam de volta depois;
* o **pacote de instalação** (`AI2P_v_1_NN_win64.exe`) faz o mesmo e, ao desinstalar o programa,
  também remove o serviço.

O serviço é procurado **no sistema** — pelo registro no gerenciador de serviços, pelo unit do
systemd ou pelo job do launchd — e apenas aquele que aponta para **esta instalação**: um serviço
alheio, que aponta para outra pasta, nenhum script toca.

O pacote de instalação no Windows exige, para isso, direitos de administrador: não há outra
forma de parar o serviço, e ele diz isso com franqueza em vez de falhar depois em arquivos
ocupados.

No `config.json` da instalação, ao criar o serviço, é colocada a marca `"serviceMode": true`.
É uma **anotação**, e não um interruptor: ela sobrevive à atualização de versão (a instalação
não toca no `config.json` de trabalho) e serve para que o aplicativo e os scripts possam dizer
«esta instalação está configurada como serviço, mas agora não há serviço no sistema» — por
exemplo, quando ele foi removido à mão ou o diretório foi levado para outra máquina.

---

## 6. Perguntas frequentes

**Dá para executar o programa à mão enquanto o serviço está funcionando?**
Dá, mas em outra porta: duas instâncias na mesma porta não funcionam. O início de console em uma
porta ocupada simplesmente abre o navegador no servidor já em funcionamento e termina.

**Como ver o que o serviço faz, se ele não inicia?**
Primeiro o `logs/ai2p-<data>.jsonl` do diretório de instalação — o aplicativo escreve ali em
qualquer modo. Se o log estiver vazio, é porque o processo não chegou ao início: no Windows veja
o log de eventos e o texto do erro do `Start-Service`; no Linux, `systemctl --user status AI2P`.

**Mudei a porta no `config.json` — preciso fazer algo com o serviço?**
Não. O serviço executa o mesmo programa do mesmo diretório, e as configurações ele lê no início
— basta reiniciar o serviço.

**Movi a instalação para outro diretório.**
Crie o serviço de novo a partir do novo diretório: o `makeAsServise` vai ver que o serviço
aponta para outro lugar e vai pedir a confirmação da mudança com a opção `-Force`.

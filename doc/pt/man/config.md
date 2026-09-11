# Configuração

As configurações do AI2P vivem em dois lugares, e isso não é desleixo, é uma regra.

* **A tela «Configurações»** — o que pertence à **organização** e a **este computador**: idioma,
  catálogos, modelos de IA, regras de segurança, usuários, servidores, notificações.
* **O arquivo `config.json`** — aquilo de que depende **o próprio início** do programa: porta,
  endereço, diretórios, e-mail. Ele é editado por fora do sistema, quando não se consegue entrar
  nele: por exemplo, a porta está ocupada e o servidor não sobe.

Tudo o que é editado na tela vai parar nesse mesmo `config.json` — mas o contrário não é
verdade: parte dos parâmetros não é mostrada na interface de propósito (veja
[§ 3](#3-o-que-só-é-editado-no-configjson)).

---

## 1. Como abrir

O ícone de engrenagem no fim da barra da esquerda (a barra vertical de ações), abaixo do botão
da documentação. As mesmas «Configurações» estão no menu **AI2P → Visões** e na última linha do
explorador. A tela não tem endereço próprio — não se abre por link.

As configurações abrem como **aba da área de trabalho**, igual a uma tarefa ou a um projeto: dá
para deixá-las abertas ao lado do trabalho e alternar entre elas.

Quem vê o quê: as seções da organização (catálogos, ações, segurança, usuários) são editadas
pelo **dono** ou pelo **administrador**, e o formulário do próprio servidor exige um **logon
local de administrador do servidor** à parte (o botão do usuário no canto superior direito →
«Logon local de administrador do servidor»). Ele é local de verdade: só é aceito a partir deste
computador.

---

## 2. As abas

### Principal

As configurações desta instalação e desta organização:

| O quê | Particularidades |
|---|---|
| **Idioma da interface** | muda a interface e também o **idioma das mensagens do sistema**; não influi no idioma em que o agente recebe a tarefa — esse é definido na equipe (veja [Equipes](teams.md)) |
| **Porta da UI/API** | aplica-se **depois de reiniciar** — o processo já está escutando na porta anterior |
| **Abrir o navegador no início** | não funciona em um serviço do SO: o serviço não tem área de trabalho |
| **Segunda linha das abas** | o que escrever sob a palavra «Tarefa»/«Projeto» — o começo do nome ou o código curto (`T-17`, `PRJ-2`) |
| **Nível de saída no log** | Debug / Info / Warning; vale **de imediato** tanto para o log técnico quanto para o diário de trabalhos |
| **Célula do calendário de agendas** | horário + código do modelo ou horário + começo do título |
| **Repositório de modelos**, **diretório de distribuições**, **diretório de instalação de pacotes** | são diretórios **deste computador**; editados pelo administrador do servidor |
| **Quadro do dataset de LoRA** | até que limites comprimir a imagem e com o que preencher as bordas |
| **Versão do aplicativo**, **Início** | apenas leitura: o número do build e se o servidor foi levantado pelo console ou como serviço do SO |

Vale entender os três diretórios de máquina desde já, senão eles parecem enigmáticos. Os padrões
deles são **relativos** (`./models`, `./distribs`, `./packages`) e são calculados **um nível
acima do diretório do programa**: AI2P em `C:\ai\AI2P` → os pesos dos modelos em `C:\ai\models` e
os pacotes em `C:\ai\packages`. Foi feito assim de propósito: os pesos dos modelos locais são
dezenas de gigabytes, e colocá-los dentro do diretório do programa, que é apagado na
reinstalação, não se pode. O formulário mostra qual caminho de fato resultou («Agora: …») — o
valor relativo, por si só, não diz nada. O valor vazio nas distribuições significa «diretório
temporário»; nos pacotes, «`<repositório de modelos>/packages`».

### Modelos

O catálogo de **modelos de IA** — aquilo de que os executores de IA são feitos. Um registro = um
modelo em um provedor: nome, provedor, endereço da API, referência da chave, preço, habilidades,
perfil de parâmetros e configuração do treinamento de LoRA.

Particularidades que vale conhecer antes que algo deixe de funcionar:

* **Um modelo em nuvem sem chave de API não pode estar ativo; um local, sem os arquivos
  baixados, também não.** Essa regra elimina toda uma classe de recusas obscuras do tipo «o
  modelo existe, mas a tarefa não anda». A chave é informada pelo botão da chave na linha do
  modelo, e os arquivos pelo botão «Instalar».
* O botão **«i»** na linha abre o documento do modelo: o que ele faz, o que é preciso para
  conectá-lo e quanto custa. A lista completa está em [Modelos de IA](../models/README.md).
* **Entrar no Claude CLI** é um botão à parte acima da lista. Os modelos «por assinatura»
  (`*_cli`) não exigem chave, mas exigem que o logon tenha sido feito; quando ele expira, os
  trabalhos não caem com erro: eles entram em pausa «aguardando o logon» — e isso se resolve
  aqui.
* Um modelo local **é ligado em um servidor específico**: ele está fisicamente em um único
  computador. O executor desse modelo fica preso ao mesmo servidor.

### Catálogos

As listas menores das quais tudo o mais é montado: **habilidades** (skills), **estados das
tarefas**, **papéis nas equipes**, **formatos de entrada e saída**, **tipos de fonte de
importação**, **regras de arquivamento padrão**. Os registros se dividem em **embutidos** (vêm
com a distribuição, não podem ser excluídos) e **do usuário**.

As habilidades são o mais importante aqui: é por elas que funciona a escolha automática de
executor. Ao criar uma habilidade sua, verifique se ela está declarada na declaração de
capacidades de alguém, senão a tarefa com essa habilidade nunca encontrará executor.

### Ações

O catálogo daquilo que o agente de IA tem permissão de fazer pelas mãos do sistema: ler e
escrever arquivos do projeto, ler tarefas vizinhas, criar subtarefas, perguntar a uma pessoa,
registrar experiência, editar modelos de processo, mover uma tarefa e assim por diante.

O principal sobre esta aba: **é aqui que ficam os prompts** — justamente os textos com que cada
ferramenta é descrita ao agente. Eles não estão no código. Em uma ação embutida só o prompt é
editável, e a edição volta ao texto de fábrica por um botão; uma ação do usuário é editável por
inteiro. Os textos são mantidos em vários idiomas e inseridos conforme o idioma da equipe.

Os códigos das ações (`AI2P.Files.Write`, `AI2P.Tasks.Create`, …) são hierárquicos, separados
por ponto. É com eles que as regras de segurança operam, por isso vale dar uma olhada aqui antes
de escrever uma regra.

### Segurança

As regras de «o que o agente pode»: **permitir / perguntar / proibir** para uma ação (ou para um
ramo inteiro de ações) em um escopo — a organização inteira, o projeto, a tarefa. A regra
«perguntar» significa que, antes da chamada, o sistema vai pedir confirmação ao responsável pela
tarefa.

Uma sutileza que economiza horas de investigação: a regra fecha uma ação apenas se a ferramenta
**tiver registro no catálogo de ações**. Uma ferramenta que não esteja no catálogo a regra não
alcança de forma alguma.

Além das ações, a regra tem o tipo **«plugins e MCP»**: ele responde não a «o que fazer», mas a
«quais plugins podem ser usados» — o padrão é `trainer.musubi` ou `trainer.musubi:lora.train`, e
as operações são «usar» (o agente chama uma ferramenta do plugin) e «executar» (uma tarefa de
«software automático» dispara o programa). **Por padrão tudo é permitido**, a proibição é criada
explicitamente. Os detalhes estão em [Plugins e MCP](plugins.md).

### Usuários

As contas de acesso e os papéis delas: `owner` (dono de toda a organização), `admin` (dono de um
computador do cluster), `project_admin`, `editor`, `reader`. A conta é o acesso; quem
**trabalha** não é a conta, é o executor que faz referência a essa conta (veja
[Executores](performers.md)).

### Organizações

A lista de organizações deste servidor e a troca entre elas. Uma organização é um banco à parte,
um diretório à parte, catálogos à parte e uma chave de cifra própria. É aqui também que vivem os
**arquivos**: eles aparecem como linhas filhas do registro da organização (veja
[Arquivamento](Archives.md)).

### Servidores

O próprio servidor, os vizinhos de cluster e as solicitações de conexão. O **formulário do
servidor local** é justamente onde ficam as configurações do próprio servidor: endereço, porta,
segundo endereço (externo), interface de escuta, diretórios deste computador e senha do
administrador do servidor. Só o administrador do servidor os edita, e os demais os veem para
leitura; **o endereço e a porta se aplicam depois de reiniciar**. Se o protocolo escolhido for
`https`, abaixo aparece a seção **«Certificado HTTPS»** (veja [HTTPS](https.md)). Em detalhes:
[Vários servidores](servers.md).

### Notificações

As regras de «sobre o que escrever para a pessoa»: a tarefa passou para um estado, a tarefa
precisa de revisão, o prazo se aproxima. Acima da lista ficam a configuração do **servidor de
e-mail** (é uma configuração do computador, e não da organização) e o botão de **mensagem de
teste**: sem ele o «configurei e estou esperando» vira «estou esperando e não sei se funciona».

Uma lista vazia de executores ou de projetos em uma regra significa **«todos»**, e não
«ninguém».

### Experiência geral

Os registros de experiência que valem para **todos** os projetos da organização — as regras
gerais de trabalho, que entram em cada tarefa do agente. A experiência de um projeto específico
vive no cartão do projeto, e a de um nó de modelo, no modelo (veja [Projetos](progects.md),
[Modelos de processo](templates.md)).

---

## 3. O que só é editado no `config.json`

O arquivo fica no mesmo lugar dos arquivos de trabalho da instalação (`data/`, `logs/`,
`secrets/`) — onde exatamente, está dito em [Instalação](install.md). Antes de editar, o
programa precisa ser **parado**: ele escreve nesse mesmo arquivo.

```json
{
  "ui": {
    "protocol": "http",
    "port": 5480,
    "basePath": "/ai2p",
    "hostname": "localhost",
    "hostname2": "",
    "port2": null,
    "bindAddress": "0.0.0.0",
    "https": {
      "source": "file",
      "certFile": "",
      "keyFile": "",
      "passwordRef": "https.certPassword",
      "storeLocation": "CurrentUser",
      "storeName": "My",
      "subject": "",
      "thumbprint": "",
      "trustAnyPeer": true
    },
    "openBrowserOnStart": true
  },
  "storage": {
    "dataDir": "./data",
    "dbFile": "ai2p.db",
    "distDir": "./distribs",
    "packagesDir": "./packages",
    "docDir": "",
    "modelsRepo": "./models",
    "serverDbFile": "server.db"
  },
  "logging": { "level": "Warning", "dir": "./logs", "rotation": "day" },
  "language": "pt",
  "serviceMode": false
}
```

Este é o arquivo **tal como vem com a distribuição**. Com o tempo aparecem mais seções nele: o
e-mail (`mail`) surge quando for configurado na aba «Notificações» — não é preciso criá-lo à
mão.

O que é importante aqui e não existe na tela:

* **`basePath`** — o prefixo do endereço depois da porta (`/ai2p`). Dele dependem todos os links
  para as tarefas; ele é alterado quando o AI2P é colocado atrás de um proxy reverso comum.
* **`bindAddress`** — qual interface de rede escutar. `0.0.0.0` são todas (é assim que o cluster
  funciona), `127.0.0.1` é apenas este computador. Para uma instalação isolada, a segunda opção
  é mais segura.
* **`hostname2` / `port2`** — o **segundo endereço, externo**, do servidor: o nome público ou o
  endereço do roteador com a porta encaminhada. Vazio significa que não há segundo endereço.
* **`https`** — o certificado do servidor: junto com `protocol: "https"` é isso que constitui a
  passagem para HTTPS. Também é editável na tela — no formulário do servidor local a seção
  aparece assim que o protocolo `https` é escolhido. Em detalhes (de onde tirar o certificado,
  como autorizá-lo no navegador, como fica o cluster): [HTTPS](https.md).
* **`dataDir`, `logging.dir`** — um caminho relativo é calculado **a partir do `config.json`**, e
  o `~/…`, a partir do diretório do usuário. O caminho é gravado do jeito que foi digitado: o
  arquivo continua portátil.
* **`docDir`** — o diretório da documentação; vazio (o caso comum) significa que o programa o
  encontra sozinho. Se for indicado um inexistente, os documentos não são exibidos, e a interface
  diz isso em texto claro.
* **`serviceMode`** — a marca «esta instalação funciona como serviço do SO»; quem a coloca é o
  `makeAsServise`, e quem a lê é o instalador, para parar o serviço durante a atualização.

Três regras que é útil conhecer de antemão:

1. **Nunca há segredos no `config.json`.** As chaves de API ficam cifradas no banco da
   organização; a chave da organização e a conta do administrador do servidor ficam no
   `secrets.json`; e a senha do e-mail fica em `secrets/mail.password.json`. Na configuração
   permanece apenas a **referência** (`mail.passwordRef`). O subdiretório `secrets/` não vai nem
   para a replicação, nem para a distribuição, nem para a atualização; dentro dele fica um
   `readme.txt` com a explicação no idioma da instalação.
2. **Os diretórios são ajustados à plataforma no início.** Uma configuração trazida do Windows
   para o Linux não deixa no sistema o caminho `C:\ai`: um caminho alheio é substituído pelo
   padrão, e a correção é explicada por uma linha no console. Um caminho do tipo `~/ai` nunca é
   considerado alheio.
3. **A atualização de versão não perde os seus valores.** Ao lado é colocado o `config.new.json`
   com os novos padrões; no primeiro início os arquivos são mesclados (base nova + os seus
   valores), e o arquivo aplicado permanece como a cópia `config.new.json.applied`.

---

## Depois

* [Instalação do AI2P e onde ficam os dados dele](install.md) — onde tudo isso foi colocado.
* [Início rápido](quickstart.md) — se a configuração for necessária apenas para o primeiro
  início.
* [Vários servidores](servers.md) — o formulário do servidor local e o cluster.
* [HTTPS](https.md) — o certificado do servidor e a configuração do navegador.

# HTTPS

Por padrão o AI2P conversa por **http**: senhas, chaves de API e o conteúdo das tarefas trafegam
pela rede em texto aberto. Em um único computador isso não afeta ninguém — o tráfego não sai do
loopback. Assim que o servidor passa a ser acessado de outras máquinas (cluster, celular,
colegas), é hora de cifrar a conversa.

Este capítulo trata de como ligar o **https**: de onde tirar o certificado, o que preencher nas
configurações, o que será preciso fazer uma vez **no navegador** e o que fazer nos **servidores
vizinhos** do cluster.

---

## 1. Em resumo

**O caminho mais simples é um botão.** No formulário do servidor local, com o protocolo
`https` e a origem «arquivo», há o botão **«Criar um certificado»** (§ 3): o programa emite
a sua própria autoridade certificadora e um certificado de servidor assinado por ela para
todos os nomes e endereços deste servidor, e preenche os campos de arquivo sozinho. Resta
instalar o arquivo da autoridade no navegador (§ 5). O restante deste capítulo é para quem
já tem um certificado ou precisa de outro.

1. Obtenha um certificado: de uma autoridade certificadora (Let's Encrypt e semelhantes), do
   administrador da sua rede — ou faça um você mesmo (§ 3).
2. Coloque-o como arquivo ao lado do `config.json` **ou** instale-o no repositório de
   certificados do computador.
3. Configurações → **Servidores** → formulário do servidor local: protocolo **https** e, abaixo,
   a seção **«Certificado HTTPS»**. Salve e **reinicie** o AI2P.
4. Se o certificado não for de uma AC global, autorize-o **no navegador** (§ 5). Os vizinhos de
   cluster não ficam presos nisso: a marca **«Confiar no certificado dos vizinhos de cluster»**
   está ligada por padrão desde a versão 1.129 (§ 6).

A seção do certificado só é visível **quando o protocolo https está escolhido**: um servidor
http não tem certificado, e não há por que falar dele. A opção «Confiar no certificado dos vizinhos do cluster» é uma exceção: ela fica ABAIXO da seção e aparece com qualquer protocolo próprio (§ 6).

---

## 2. De onde vem o certificado

Há duas origens, e elas são escolhidas no mesmo formulário, no campo **«De onde tirar»**.

### Arquivo

O caso comum. Servem:

| O quê | Campos do formulário |
|---|---|
| `.pfx` / `.p12` — certificado e chave privada em um só arquivo, normalmente com senha | **Arquivo do certificado**, **Senha do arquivo .pfx** |
| Par PEM — `cert.crt` (ou `fullchain.pem`) e um `privkey.pem` separado | **Arquivo do certificado** e **Arquivo da chave privada** |
| PEM em que a chave está no mesmo arquivo | apenas **Arquivo do certificado** |

O caminho pode ser escrito de forma **relativa** — ele é calculado a partir do `config.json` (ou
seja, do diretório de trabalho da instalação, veja [Instalação](install.md)); um caminho
absoluto e o `~/…` são tomados como estão.

**A senha do `.pfx` nunca vai parar no `config.json`** — ela vai para os segredos
(`secrets/https.certPassword.json`), e na configuração fica apenas a referência. Um campo de
senha vazio na gravação significa «não alterar», como na senha do e-mail.

### Repositório do sistema

Um certificado **já instalado neste computador**:

* **Windows** — o console «Certificados» (`certmgr.msc` para o usuário, `certlm.msc` para o
  computador);
* **macOS** — o chaveiro (Keychain);
* **Linux** — o repositório do .NET do usuário atual
  (`~/.dotnet/corefx/cryptography/x509stores`).

Faz-se assim onde a chave privada **não deve ficar como arquivo em disco** — por exemplo, quando
o certificado é emitido pelo domínio e renovado por política de grupo. Os campos:

* **Repositório** — `CurrentUser` (padrão) ou `LocalMachine`. O `LocalMachine` no Linux e no
  macOS costuma ser inacessível a um usuário comum: coloque o certificado no repositório **do
  usuário sob o qual o AI2P funciona** (em um serviço, é a conta dele; veja
  [Executar como serviço](service.md)).
* **Seção do repositório** — `My` (certificados pessoais), o caso comum.
* **Nome no certificado (CN)** — parte do nome; vazio significa procurar pelo **nome do host**
  deste servidor.
* **Impressão digital** — é mais precisa que o nome e não se confunde quando há vários
  certificados para o mesmo nome (o antigo e o renovado). Se ela estiver definida, o nome não é
  consultado.

É tomado apenas um certificado **com chave privada**; se houver vários adequados, o de validade
mais longa.

---

## 3. Como fazer um certificado você mesmo

### Com um botão no próprio programa (o mais simples)

Configurações → **Servidores** → formulário do servidor local → protocolo **https** → «De
onde obter» = **arquivo** → botão **«Criar um certificado»**. O programa faz DOIS
certificados de uma vez, exatamente o que o navegador exige:

* `certs/ai2pCA.crt` e `certs/ai2pCA.key` — a **sua própria autoridade certificadora**. Ela
  é instalada no navegador (§ 5) e nas raízes confiáveis dos servidores vizinhos (§ 6); o
  link **«Baixar o certificado da sua autoridade»**, ao lado do botão, entrega esse arquivo
  direto ao navegador.
* `certs/ai2p.crt` e `certs/ai2p.key` — o certificado do **servidor**, assinado por essa
  autoridade: `CA:FALSE`, `extendedKeyUsage=serverAuth` e `subjectAltName` com todos os
  nomes do servidor: o nome das configurações, o segundo nome (externo), o nome da máquina,
  os seus endereços na rede, `localhost` e `127.0.0.1`. Válido por 825 dias.

Os campos «Arquivo do certificado» e «Arquivo da chave privada» são preenchidos sozinhos;
resta **salvar o formulário e reiniciar** o AI2P. Pressionar o botão de novo reemite apenas
o certificado do servidor, e a **autoridade é reaproveitada**: não é preciso percorrer os
navegadores outra vez. É assim também que se acrescenta um nome ou endereço novo ao
certificado: corrija os nomes do servidor no formulário e pressione o botão de novo.

> **Para isso não é preciso nenhuma autoridade certificadora em funcionamento.** O navegador
> não «vai até a autoridade» nem lhe pede permissão: ele verifica a **assinatura** do
> certificado do site com a chave pública da autoridade — localmente, sem rede. Por isso «a
> sua própria autoridade» aqui é apenas um par de arquivos, e não um serviço que precise ser
> levantado e mantido ligado. Da pessoa exige-se exatamente uma coisa: colocar uma vez o
> arquivo da autoridade no repositório do navegador.

Um certificado próprio é necessário quando o servidor está na rede local e uma AC global não tem
como emitir nada para ele (`ai2p.local`, `192.168.1.10`).

### Windows — com o PowerShell embutido

```powershell
$cert = New-SelfSignedCertificate -DnsName "ai2p.local","192.168.1.10" `
        -CertStoreLocation "Cert:\CurrentUser\My" -NotAfter (Get-Date).AddYears(3)
$pwd = ConvertTo-SecureString -String "senha" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath "C:\ai\AI2P\ai2p.pfx" -Password $pwd
```

O certificado fica de imediato **tanto no repositório** (`CurrentUser\My`) **quanto em arquivo**
— serve qualquer uma das duas origens. A impressão digital é impressa por `$cert.Thumbprint`.

### Linux / macOS — openssl

```sh
# SUBSTITUA estas duas linhas pelo nome e pelo endereço do SEU servidor — exatamente o que
# você digita na barra de endereços do navegador. Copiadas como estão, elas emitem um
# certificado para um nome alheio, e o navegador o recusa por divergência de nome
HOST=ai2p.local
IP=192.168.1.10

# 1) a sua própria autoridade certificadora — é ELA que se instala no navegador
openssl req -x509 -newkey rsa:2048 -nodes -days 3650 \
    -keyout ai2pCA.key -out ai2pCA.crt \
    -subj "/CN=AI2P local CA" \
    -addext "basicConstraints=critical,CA:TRUE" \
    -addext "keyUsage=critical,keyCertSign,cRLSign"

# 2) o certificado do PRÓPRIO servidor, assinado por essa autoridade
openssl req -newkey rsa:2048 -nodes -keyout ai2p.key -out ai2p.csr -subj "/CN=$HOST"
cat > ai2p.ext <<EOF
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:$HOST,IP:$IP,DNS:localhost,IP:127.0.0.1
EOF
openssl x509 -req -in ai2p.csr -CA ai2pCA.crt -CAkey ai2pCA.key -CAcreateserial \
    -days 825 -out ai2p.crt -extfile ai2p.ext

# 3) confira que o certificado traz os SEUS nomes, e não o exemplo da documentação
openssl x509 -in ai2p.crt -noout -text | grep -A1 "Alternative Name"
```

Saem `ai2p.crt` e `ai2p.key` — é esse o par PEM para o formulário («Arquivo do certificado» e
«Arquivo da chave privada»), e `ai2pCA.crt` é o que se instala no navegador (seção 5). O arquivo
`ai2p.csr` é uma «solicitação de assinatura» intermediária: não entra em nenhuma configuração e,
depois de emitido o certificado, não é mais necessário. Em um único arquivo `.pfx` o par do
servidor se junta assim:

```sh
openssl pkcs12 -export -out ai2p.pfx -inkey ai2p.key -in ai2p.crt -passout pass:senha
```

O `openssl` é instalado pelo script `install_required.sh` (no Windows, pelo
`install_required.bat`, onde ele não é obrigatório: o PowerShell faz o mesmo).

> **O `subjectAltName` é obrigatório.** Os navegadores há muitos anos não olham para o `CN` de
> forma alguma: sem a lista de nomes (`DNS:` e `IP:`) o certificado será rejeitado mesmo depois
> de ter sido autorizado.

> **Um único certificado autoassinado não basta para o navegador.** O comando `openssl req -x509`
> cria um certificado de AUTORIDADE CERTIFICADORA (`basicConstraints=CA:TRUE`), e os navegadores se
> recusam a aceitar esse certificado como certificado do SITE — mesmo quando ele próprio foi
> adicionado às autoridades confiáveis. O Firefox chama isso de
> `MOZILLA_PKIX_ERROR_CA_CERT_USED_AS_END_ENTITY` e escreve na tela «você não está conectado com
> segurança a este site», sem explicar nada. Por isso são feitos dois certificados: a autoridade e
> o certificado de servidor assinado por ela. O AI2P avisa disso por conta própria — no formulário
> do servidor local, abaixo da linha «O certificado é lido».

> **É preciso entrar por um nome do certificado.** Em `subjectAltName` estão listados os nomes pelos
> quais o servidor pode ser chamado, e `localhost` geralmente não está entre eles — por isso o
> navegador recusa `https://localhost:5480` por divergência de nome e abre `https://ai2p.local:5480`.
> A partir da versão 1.125 o AI2P abre o navegador no nome do servidor indicado nas configurações e
> não no laço local; mas, se você quiser digitar o nome à mão, acrescente-o ao SAN e ao `hosts`
> (ou ao DNS).

### Certificado do Let's Encrypt

Se o servidor tiver um nome real na internet, emita um certificado comum (`certbot`) e indique
os arquivos dele: `fullchain.pem` no «Arquivo do certificado» e `privkey.pem` no «Arquivo da
chave privada». Depois de cada renovação, o AI2P precisa ser **reiniciado**: o certificado é
lido uma única vez, no início.

---

## 4. O que o próprio programa verifica

A verificação é a mesma em três lugares, e isso é de propósito.

* **O formulário do servidor local** não deixa salvar «https» com um certificado inválido. O
  motivo é escrito por extenso: «o arquivo do certificado não existe: …», «o certificado não tem
  chave privada», «não há no repositório certificado com o critério …», «em um .pfx a causa
  frequente é a senha errada». Do contrário o início seguinte não subiria, e já não seria
  possível entrar para corrigir.
* **O assistente de primeiro início** recusa do mesmo jeito — na primeiríssima tela que a pessoa
  vê na vida.
* **O início do servidor** imprime o certificado encontrado («certificado CN=…, válido até …,
  impressão digital …») ou o motivo da recusa, e para: cair silenciosamente para http quando se
  pediu https é pior do que não subir.

No formulário, enquanto o https estiver escolhido, também é exibida uma **linha de estado**:
verde para «o certificado é lido: …» e amarela para «não há certificado: …». Ela responde à
pergunta «será que vai funcionar?» antes do reinício, e não depois.

O endereço, a porta e o **protocolo só se aplicam depois do reinício** — o processo já está
escutando na porta anterior.

---

## 5. Configuração do navegador

Um certificado de AC global não precisa ser explicado ao navegador. Um **próprio** (ou de um
domínio, ou de outra AC não global) precisa: senão, a cada acesso o navegador recebe a pessoa
com a página «A conexão não é particular».

A forma correta é adicionar **uma única vez** o certificado (ou o certificado da sua AC) aos
confiáveis. Isso é feito **no computador de onde se acessa**, e não no servidor.

> **No navegador instala-se SOMENTE o arquivo da autoridade — `ai2pCA.crt`.** O certificado do
> servidor `ai2p.crt` não precisa ser importado e nem pode: o navegador aceita em «Autoridades»
> apenas um certificado com `CA:TRUE`, e o do servidor é de propósito `CA:FALSE`; o Firefox
> responde «Este não é um certificado de autoridade certificadora, portanto não pode ser importado
> para a lista de autoridades». O próprio servidor entrega o arquivo certo: ao lado do botão «Criar
> certificado» há o link **«Baixar o certificado da sua autoridade»**. Se o certificado foi feito
> com `openssl` e não pelo botão, o arquivo da autoridade é o `ai2pCA.crt` da primeira chamada
> (§ 3), nunca o `ai2p.crt`.

**Windows (Chrome, Edge, qualquer navegador do sistema).** Copie o `ai2pCA.crt` para o computador
→ duplo clique → «Instalar certificado» → «Computador local» → «Colocar todos os certificados no
repositório a seguir» → **«Autoridades de Certificação Raiz Confiáveis»**. O mesmo pelo
PowerShell como administrador:

```powershell
Import-Certificate -FilePath ai2pCA.crt -CertStoreLocation Cert:\LocalMachine\Root
```

**macOS (Safari, Chrome).** Duplo clique no `ai2pCA.crt` → o certificado vai para o chaveiro →
abra-o → «Confiança» → «Ao usar este certificado» → **«Sempre confiar»**.

**Linux (Chrome/Chromium).** Configurações → «Privacidade e segurança» → «Segurança» →
«Gerenciar certificados» → aba «Autoridades» → «Importar» → marcar «Confiar neste certificado
para identificar sites». Para todo o sistema:

```sh
sudo cp ai2pCA.crt /usr/local/share/ca-certificates/ai2pCA.crt && sudo update-ca-certificates
```

O **Firefox** mantém o **próprio** repositório e não lê o do sistema: Configurações →
«Privacidade e segurança» → «Certificados» → «Ver certificados» → «Autoridades» → «Importar».

> **A caixa de seleção da janela de importação é obrigatória.** Ao escolher o arquivo, o
> Firefox pergunta «Confiar nesta CA para identificar sites?» (*Trust this CA to identify
> websites*) — e por padrão a caixa está **desmarcada**. Sem ela o certificado até fica na
> lista «Autoridades» e é bem visível, mas não é confiado para sites, e a página continua
> mostrando o aviso. Esta é a causa mais comum de «instalei a CA e o Firefox continua
> reclamando». Para conferir e corrigir sem importar de novo: escolha sua CA na lista
> «Autoridades» → «Editar confiança» → marcar «Este certificado pode identificar sites».

**Qual é o erro de verdade.** Na página de aviso pressione «Avançado»: embaixo aparece o
código, e por ele se vê o que consertar:

| Código | O que é | O que fazer |
|---|---|---|
| `SEC_ERROR_UNKNOWN_ISSUER` | a CA não está no repositório **ou** não tem a confiança para sites marcada | instalar `ai2pCA.crt` e marcar a caixa (acima) |
| `SSL_ERROR_BAD_CERT_DOMAIN` | o nome que você digitou não está em `subjectAltName` | entrar por um nome do certificado ou acrescentar o nome nas configurações do servidor e apertar «Criar certificado» de novo |
| `MOZILLA_PKIX_ERROR_CA_CERT_USED_AS_END_ENTITY` | o servidor entrega o certificado da **CA** em vez do do servidor | § 3: são necessários dois certificados, o mais simples é o botão |
| `SEC_ERROR_EXPIRED_CERTIFICATE` | o prazo acabou | emitir de novo (o botão) |

Mais dois detalhes do mesmo lugar: o certificado é lido **na inicialização**, portanto depois
de «Criar certificado» é preciso salvar o formulário e **reiniciar** o AI2P — senão o
navegador vê o arquivo anterior; e uma «exceção» adicionada antes para este endereço é
removida em Configurações → «Privacidade e segurança» → «Ver certificados» → **«Servidores»**.

**Android / iOS.** O arquivo `.crt` é aberto nas configurações do sistema («Instalar
certificado» / perfil de configuração); no Android, para o Chrome, o certificado precisa ser
instalado como «Certificado de CA», senão ele é instalado «para VPN» e o navegador não o
enxerga.

Três coisas em que se tropeça com mais frequência:

1. **O nome tem de coincidir.** Um certificado autorizado não salva se você acessa por
   `https://192.168.1.10` mas no certificado só há `DNS:ai2p.local`. Coloque os dois nomes no
   `subjectAltName` — ou acesse pelo nome para o qual o certificado foi emitido.
2. **«Continuar mesmo assim» não é solução.** A exceção vive até o navegador ser reiniciado e,
   em parte dos navegadores, ela quebra a conexão WebSocket de que a interface vive: a tela abre
   e congela.
3. **O nome do servidor nas configurações tem de ser o mesmo** do certificado: os links das
   tarefas são montados a partir do campo «Nome do host» (veja
   [Vários servidores](servers.md)).

---

## 6. O cluster por HTTPS

> **A opção fica sempre visível, não apenas com `https` próprio.** A configuração trata das
> chamadas de **saída**: um servidor que trabalha por `http` liga para o vizinho por `https`
> do mesmo jeito e esbarra do mesmo jeito no certificado caseiro dele («The remote
> certificate is invalid because of errors in the certificate chain: UntrustedRoot»). Antes
> da versão 1.126 a opção ficava dentro da seção do certificado: em um servidor http ela não
> aparecia de forma alguma, e a única maneira de ligá-la era editar o `config.json` à mão.
> Ela passa a valer **imediatamente**, sem reiniciar.

Os servidores acessam uns aos outros **sozinhos**, e não há ali quem confirme uma exceção: se o
certificado não for de uma AC global, a replicação para com erro de verificação para sempre, até
que as configurações sejam corrigidas.

Por isso a seção tem a marca **«Confiar no certificado dos vizinhos de cluster»**
(`ui.https.trustAnyPeer`, **ligada por padrão a partir da versão 1.129**). Ligada, ela desativa a
verificação do certificado **somente nas chamadas servidor-servidor** — solicitações, replicação e
transferência de arquivos; o navegador e o acesso aos provedores de modelos ela não alcança.

O padrão mudou porque o certificado dos nossos servidores quase sempre é próprio (§ 3): com a
marca desligada, o primeiro servidor a se conectar a um cluster com https parava com «o
certificado remoto foi rejeitado», e não havia como adivinhar onde procurar a causa. No cluster o
servidor entra apenas pelo segredo comum de qualquer forma. Se quiser a verificação estrita da
cadeia, distribua o certificado da sua AC para todas as máquinas do cluster (§ 5) e **desligue** a
marca: o desligamento sobrevive à atualização.

Mais duas regras sobre o cluster:

* O protocolo é uma **propriedade do registro do servidor**. Um vizinho que estiver com `https`
  na lista de servidores é chamado por https; ao passar um servidor para https, corrija o
  registro dele também nos vizinhos (normalmente ele chega pela replicação sozinho).
* **O acesso a si mesmo pelo loopback não verifica o certificado.** A interface acessa a própria
  API por `localhost`, e nenhum certificado corresponde a esse nome — não há o que verificar,
  do outro lado está o mesmo computador. O mesmo vale para o cliente de linha de comando `ai2p`,
  com que o agente de IA trabalha.

---

## 7. O que se edita no `config.json`

Pelo formulário se edita a mesma coisa; o arquivo é necessário quando não se consegue entrar no
sistema. **Pare** o programa antes de editar.

```json
{
  "ui": {
    "protocol": "https",
    "port": 5480,
    "hostname": "ai2p.local",
    "https": {
      "source": "file",
      "certFile": "ai2p.pfx",
      "keyFile": "",
      "passwordRef": "https.certPassword",
      "storeLocation": "CurrentUser",
      "storeName": "My",
      "subject": "",
      "thumbprint": "",
      "trustAnyPeer": true
    }
  }
}
```

* `source` — `file` (padrão) ou `store`. Um valor vazio ou desconhecido é lido como `file`: a
  configuração de versões anteriores, em que havia apenas os dois caminhos, funciona como antes.
* `passwordRef` — a **referência** à senha nos segredos, e não a senha. O valor é informado no
  formulário ou colocado à mão em `secrets/https.certPassword.json`.
* A interpretação é sensível à extensão: `.pfx`/`.p12` são lidos como PKCS#12, e todo o resto
  como PEM.

---

## 8. Se não subiu

| O que se vê | O que é | O que fazer |
|---|---|---|
| «HTTPS: certificado não definido» | o protocolo é `https` e a seção está vazia | indicar um arquivo ou escolher o repositório |
| «HTTPS: o arquivo do certificado não existe: …» | o caminho está errado | o caminho é calculado **a partir do `config.json`**; confira o caminho completo da mensagem |
| «HTTPS: não foi possível ler o certificado …» | a senha do `.pfx` está errada ou o arquivo não é PKCS#12 | conferir a senha; um arquivo PEM precisa de `keyFile` |
| «HTTPS: o certificado não tem chave privada» | foi indicado só um `.crt` sem a chave | acrescentar o `keyFile` ou usar um `.pfx` |
| «HTTPS: não há no repositório …/… o certificado …» | seção errada, usuário errado ou sem chave | conferir o repositório e a impressão digital; o serviço olha o repositório da conta **dele** |
| No navegador, «A conexão não é particular» | o certificado é desconhecido para o navegador | § 5 |
| A tela abriu e congelou | o navegador cortou o WebSocket por causa da exceção temporária | não «continuar mesmo assim», e sim autorizar o certificado como no § 5 |
| A replicação parou com erro de certificado | o vizinho não confia na nossa AC | § 6 |

---

## Depois

* [Configuração](config.md) — os demais campos do `config.json` e a tela de configurações.
* [Vários servidores](servers.md) — cluster, replicação e o formulário do servidor local.
* [Executar o AI2P como serviço do sistema operacional](service.md) — com que conta o serviço
  funciona (disso depende o repositório de certificados).
* [Instalação do AI2P e onde ficam os dados dele](install.md) — onde ficam o `config.json` e o
  `secrets/`.

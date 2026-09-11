# Início rápido

O caminho mínimo do download até o primeiro resultado de um agente de IA. Nada de supérfluo:
configurações, modelos de processo, equipes e cluster ficam para depois — cada um tem o seu
capítulo.

Serão necessários de 10 a 15 minutos, e a maior parte disso é o download.

---

## Passo 1. Baixar

As distribuições ficam aqui: **${refpackages}**

Há dois arquivos para cada sistema, e a escolha entre eles é a escolha de «preciso do runtime
à parte?»:

| Arquivo | O que tem dentro | Quando pegar |
|---|---|---|
| `AI2P_full_v_1_NN_win64.exe` | o programa **junto com o runtime** | o caso comum: não é preciso instalar mais nada |
| `AI2P_v_1_NN_win64.exe` | apenas o programa | se o computador já tiver o runtime **ASP.NET Core 8.x** |

No Linux e no macOS é a mesma coisa, mas com a extensão `.run` (`AI2P_full_v_1_NN_Linux.run`).
`NN` é o número da versão; pegue o maior.

> Na dúvida, pegue o **`full`**. Ele é maior, mas não exige nada além de si mesmo.

## Passo 2. Instalar

### Windows
Execute o arquivo baixado. O instalador vai perguntar se a instalação é **para todos os
usuários** ou **somente para mim** — disso dependem tanto o diretório do programa quanto o lugar
onde ficarão os arquivos de trabalho dele; os detalhes estão no capítulo
[Instalação](install.md). Para experimentar, «somente para mim» serve: não serão necessários
direitos de administrador.

### Linux

```sh
chmod +x AI2P_full_v_1_NN_Linux.run
./AI2P_full_v_1_NN_Linux.run
```

Instala em `~/ai/AI2P`. Direitos de `root` não são necessários: o servidor funciona com um
usuário comum.

### macOS

```sh
chmod +x AI2P_full_v_1_NN_macos.run
./AI2P_full_v_1_NN_macos.run
```

Instala em `~/ai/AI2P`. Direitos de `root` não são necessários: o servidor funciona com um
usuário comum.

## Passo 3. Executar

O atalho **AI2P** na área de trabalho (ou `AI2P.Server.exe` / `./AI2P.Server` a partir do
diretório de instalação). O programa abre o navegador sozinho em
`http://localhost:5480/ai2p`.

Se não abrir, digite esse endereço à mão. Se a janela com o texto do erro fechou rápido demais,
veja [«Se o programa não iniciar»](install.md#5-se-o-programa-não-iniciar).

## Passo 4. Passar pelo assistente de primeiro início

No primeiríssimo início o AI2P não deixa entrar na interface: ele conduz por etapas. São seis:

| Etapa | O que pergunta | O que vale saber |
|---|---|---|
| **1. Idioma** | o idioma da interface | é sugerido o idioma do navegador; o escolhido vale **imediatamente** em todas as telas seguintes |
| **2. Servidor** | nome do servidor, protocolo, porta e **três diretórios de máquina** | o nome do servidor é o nome dele **na rede**; para uma instalação isolada serve `localhost`, basta digitá-lo explicitamente. Os diretórios (pesos dos modelos, distribuições, pacotes) podem ficar como sugeridos |
| **3. Usuário** | nome, e-mail, telefone, senha | a marca **«A senha coincide com a senha do administrador do servidor»** vem marcada — deixe assim: então as configurações do servidor ficam acessíveis de imediato. A senha pode ficar vazia, mas aí a entrada só é possível a partir deste computador |
| **4. Organização** | nome e código no endereço | o código entra no endereço (`/ai2p/<código>/…`). Deixe a marca **«servidor primário do cluster»** ligada: desmarcá-la significa «conectar-se a um servidor alheio» |
| **5. Uso típico** | código e análise / vídeo / imagens / nenhum uso típico | pela resposta o sistema monta o ambiente de trabalho |
| **6. Primeiro projeto** | nome e **pasta** | a pasta é aquela em que o agente vai ler e escrever arquivos. Sem ela, as ferramentas de arquivo não são liberadas ao agente |

Depois da última etapa você já tem: a organização, o servidor `S0` (que também é o maestro),
você como dono, **três executores de IA `Jon`, `Bob` e `Stiv`**, a equipe `<organização>_team` e
um projeto com a pasta indicada.

> A opção «código e análise» em uma instalação limpa dá três executores em modelos **por
> assinatura do Claude**. Para vídeo e imagens não há modelos ativos em uma instalação limpa — o
> assistente diz isso com franqueza, mas cria a equipe e o projeto assim mesmo.

## Passo 5. Dar um modelo ao sistema

Os executores existem, mas só vão conseguir trabalhar quando o modelo deles tiver com o que
pagar. Abra **Configurações** (a engrenagem no fim da barra da esquerda) → aba **«Modelos»**.

Escolha **um** dos caminhos:

**a) Assinatura do Claude (o mais rápido, não precisa de chave).** O botão **«Entrar no Claude
CLI»** acima da lista. É assim que funcionam os registros com o sufixo `_cli` — foram
exatamente esses que o assistente colocou.

**b) Chave de API.** Encontre o modelo desejado, aperte o botão da chave na linha dele e cole a
chave do provedor. O registro fica ativo sozinho: **um modelo em nuvem sem chave não pode
ficar ativo**.

**c) Modelo local.** O botão «Instalar» na linha do modelo baixa os pesos e instala os pacotes
necessários. Isso demora (gigabytes) e exige uma placa de vídeo — para o primeiro início não é
o caminho mais rápido.

O que cada registro faz, quanto custa e do que precisa está no botão **«i»** da linha dele
(e na [lista de modelos](../models/README.md)).

## Passo 6. Criar uma tarefa

Abra **Tarefas** (barra da esquerda) → botão **«adicionar»** → **«tarefa vazia»**.

Preencha:

* **Título** — em poucas palavras, do que se trata o trabalho.
* **Descrição** — como para uma pessoa: o que fazer, onde, como conferir. Isso é o prompt; tudo
  o que o agente precisa saber tem de estar aqui.
* **Critérios de aceitação** — como saber que está pronto.
* **Habilidades** — o que de fato precisa ser feito (por exemplo `code-write-cs`,
  `text-write`). É por elas que funciona a escolha automática.
* **Executor** — escolha na lista (`Jon`, `Bob`, `Stiv`) ou aperte o botão de escolha
  automática ao lado do campo: **«primeiro a IA, depois a pessoa»**.

O projeto e a equipe são preenchidos sozinhos — os que estão abertos no momento.

## Passo 7. Iniciar e receber o resultado

No cartão da tarefa há o botão **«iniciar»**. Depois disso:

* a situação muda para **«em andamento»**, e ao lado se vê quem exatamente está trabalhando;
* o andamento aparece em **«Histórico de trabalhos»**: chamadas de ferramentas, leitura e
  gravação de arquivos, gasto de tokens;
* se faltar algo ao agente, ele **pergunta no chat da tarefa** — a tarefa entra em «pausa» e a
  pergunta aparece na Caixa de entrada. Responda no chat e o trabalho continua do mesmo ponto;
* ao terminar, o agente coloca o resultado como **artefato da tarefa** (o texto do relatório,
  arquivos, imagens), e a tarefa passa para **«revisão»** — aceitar o trabalho é com você.

Tudo o que o agente fez com os arquivos está na pasta do projeto — exatamente onde você espera.

---

## Se algo deu errado

| O que se vê | Por quê | O que fazer |
|---|---|---|
| O botão «iniciar» não faz nada | a tarefa não tem executor | atribua um ou aperte a escolha automática |
| A tarefa entrou logo «com erro» | o modelo não tem chave ou não foi feito o logon | Configurações → Modelos: a chave ou «Entrar no Claude CLI» |
| A tarefa está em «pausa» | o agente fez uma pergunta ou o executor esgotou o limite | veja o chat da tarefa e a Caixa de entrada |
| O agente não vê os arquivos do projeto | o projeto não tem pasta definida | cartão do projeto → campo da pasta |
| O agente recusou uma ação | atuou uma regra de segurança | Configurações → Segurança |

## Depois

* [Projetos](progects.md) — a pasta do projeto, os objetos, a experiência, «preço ↔ qualidade».
* [Tarefas](tasks.md) — quadro, hierarquia, subtarefas, bloqueadoras, chat e artefatos.
* [Executores](performers.md) — IA e pessoas, habilidades, limites, preço.
* [Equipes](teams.md) — quem trabalha com quem e em que idioma o agente fala.
* [Modelos de processo](templates.md) — como não digitar o mesmo processo duas vezes.
* [Configuração](config.md) — todo o resto.

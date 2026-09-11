
# Vídeo a partir do quadro de referência de um personagem. Exemplo

**O objetivo do capítulo:** no projeto há uma imagem de um personagem, e é preciso que o modelo
de mídia faça dela um vídeo — e que no vídeo seguinte o personagem continue com a mesma
aparência.

Vamos usar o modelo local **`Kandinsky-5.0-I2V-Lite-5s`** (imagem + texto → vídeo, 5 segundos).
Tudo o que está abaixo vale para qualquer modelo do modo «imagem → vídeo»; em um modelo «texto →
vídeo» (`Kandinsky-5.0-T2V-Lite-sft-5s`) não há como passar o quadro inicial — ele recebe apenas
o texto.

## 1.1. Por que a imagem não pode ser simplesmente descrita em palavras

O modelo de mídia recebe como prompt **apenas a descrição da tarefa**, e nada mais: nem o
título, nem os critérios de aceitação, nem a experiência do projeto, nem o chat — nada disso
chega até ele. Logo, a aparência do personagem tem de estar na descrição do quadro, ao pé da
letra.

Reescrevê-la à mão em cada quadro não se pode: a paráfrase com palavras próprias, de quadro para
quadro, é justamente a causa pela qual o personagem «derrete». Por isso a aparência é guardada
**uma única vez** — em um objeto do projeto —, e na descrição do quadro fica um **link** para o
objeto. A cada início do trabalho o link é expandido no passaporte e nos caminhos dos arquivos
de referência, e o primeiro caminho vai para o modelo como quadro inicial.

Uma correção no passaporte vale já para o quadro seguinte — não é preciso reescrever as
descrições das tarefas.

## 1.2. O que será preciso

| | |
|---|---|
| Direitos | o papel **admin** na organização (o catálogo de modelos é uma configuração do sistema). Para **editar o registro de um modelo local** (ligá-lo ou desligá-lo neste computador) é preciso ainda o **logon local de administrador do servidor** — o link está no menu do usuário no cabeçalho e em «Configurações → Servidores»; para a instalação em si ele não é necessário |
| Hardware | NVIDIA, no mínimo 6 GB de VRAM, driver 580+, ~18 GB em disco, 16 GB de RAM (detalhes no botão **«i»** do formulário do modelo) |
| Tempo | um vídeo de 768×512, 121 quadros e 50 passos leva cerca de uma hora em 6 GB de VRAM |
| Imagem | o arquivo de referência **dentro da pasta do projeto**, de preferência já com proporção próxima de 768×512 |

## 1.3. Passo 1. Instalar o modelo

1. **Configurações** (o ícone de engrenagem na barra da esquerda) → aba **«Modelos»**.
2. A linha `Kandinsky-5.0-I2V-Lite-5s` → abre o formulário do modelo.
3. O botão **«i»** traz o documento do modelo: requisitos de hardware, tamanhos dos arquivos,
   erros frequentes.
4. O botão **«Instalar»** abre a janela de instalação. São instalados a compilação portátil do
   ComfyUI (~2,1 GB) e quatro arquivos de pesos (~16 GB; três deles são comuns ao modelo de
   texto Kandinsky — se ele já estiver instalado, serão baixados apenas ~4,3 GiB). O download é
   retomável: uma instalação interrompida continua do ponto em que parou.

**Depois de uma instalação bem-sucedida o modelo se liga sozinho.** Enquanto os arquivos não
estiverem no lugar, ele não pode ficar ativo — isso é verificado também no início do aplicativo.
A atividade de um modelo local é **própria de cada computador do cluster** (no formulário:
«Ativo neste servidor»): no servidor vizinho ele é ligado à parte, no mesmo lugar em que estão
os arquivos dele.

A chave de API desse modelo não é necessária — o ComfyUI sobe localmente, sem autorização.

## 1.4. Passo 2. Criar um executor de IA e colocá-lo em funcionamento

1. **Executores** (o ícone de pessoas na barra da esquerda) → o botão **«+»** («Adicionar
   executor»):
   * *Nome externo (apelido)* — como ele será chamado nas tarefas, por exemplo `kandinsky-i2v`;
   * *Tipo* — **IA**;
   * *Nome interno* — `Kandinsky-5.0-I2V-Lite-5s` (na lista há apenas os modelos **ativos**);
   * *Ativo* — ligar.
   O perfil de conexão e a declaração de capacidades são copiados do registro do catálogo, e não
   é preciso editá-los agora.
2. **Equipes** → a equipe do projeto (ou «Adicionar equipe») → a seção **«Integrantes»** →
   adicionar esse executor; verifique que ele esteja com **«Ativo na equipe»** — um integrante
   inativo não se conecta no início e não é pego pela escolha automática.
3. **Apertar «Iniciar»** — a equipe inteira ou **«Iniciar o integrante»**.

O terceiro passo é obrigatório, e eis por quê: **quem sobe o ComfyUI é justamente o início do
trabalho da equipe** (o comando de início foi gravado no perfil na instalação). Se o trabalho não
for iniciado e o ComfyUI não for levantado à mão, o trabalho falha com a mensagem *«Tempo de
espera esgotado ao conectar ao ComfyUI (o servidor não foi iniciado?)»*.

Enquanto o modelo carrega os pesos, o integrante fica no estado «conectando» — é normal, a
primeira conexão leva minutos.

## 1.5. Passo 3. A pasta do projeto e o arquivo de referência

O quadro inicial é procurado **na pasta do projeto**, e o caminho é sempre relativo. A saída
para fora (`..`, `C:\…`) é proibida de propósito.

1. Cartão do projeto → guia **«Principal»** → campo **«Caminho da pasta do projeto»** (ao lado
   há o botão de navegação). A pasta é **própria de cada servidor do cluster**: em outro
   computador o mesmo projeto vive em outro diretório, mas os caminhos relativos internos são os
   mesmos.
2. Coloque a referência nessa pasta, por exemplo `refs/hero.png`.

O primeiro quadro da série é cômodo de obter com um modelo comum «texto → imagem» ou de desenhar
à mão — daí em diante tudo se constrói a partir dele.

## 1.6. Passo 4. Criar o objeto-personagem

Cartão do projeto → guia **«Objetos»** → botão **«+»** («Novo objeto»).

| Campo | O que escrever |
|---|---|
| *Nome* | como as pessoas chamam o personagem: `Herói Vasya` |
| *Tipo* | **personagem** (tipos: personagem, locação, adereço, estilo, quadro de referência, adaptador LoRA, arquivo, equipamento, recurso de mídia) |
| *Pertence ao objeto* | vazio no próprio personagem; nos quadros de referência dele, o próprio personagem |
| *Etiquetas* | um grupo próprio, separado das etiquetas de tarefas |
| *Arquivo ou endereço* | `refs/hero.png` — o caminho **relativo à pasta do projeto**; ao lado há o botão de escolha de arquivo (que anda pela pasta do projeto e coloca o caminho relativo), e à esquerda a janela de prévia |
| *Passaporte (vai para o prompt)* | a descrição **literal** da aparência: é esse texto que entra no lugar do link |
| *Ativo* | ligado |

O passaporte é o campo principal do formulário. Escreva-o do jeito que quer vê-lo no prompt:

> Homem de 35 anos, barba escura curta, cicatriz acima da sobrancelha esquerda, olhos
> verde-acinzentados. Jaqueta de couro marrom desgastada, cachecol cinza, jeans. Iluminação
> fria, de fim de tarde.

Depois de salvar, aparecem no formulário dois botões:

* **«Link do objeto»** — o que se cola na descrição da tarefa (`@obj:OBJ-1`);
* **«O que vai para o modelo»** — o texto no qual o link será expandido. Confira o passaporte
  aqui, **antes** da geração, e não por um quadro que saiu diferente.

**Várias referências de um mesmo personagem** são criadas como filhas dele: um objeto novo do
tipo «quadro de referência», com o personagem no campo *«Pertence ao objeto»*. Na lista eles
ficam abaixo dele, com recuo, e todos os caminhos deles entram na substituição.

## 1.7. Passo 5. A descrição do quadro

Criamos a tarefa (o botão «nova tarefa» na lista de tarefas) e preenchemos:

| Campo | Valor |
|---|---|
| *Título* | `Cena 1: o herói se vira` (**não** entra no prompt) |
| *Descrição (.md)* | o texto do prompt + o link do objeto — veja abaixo |
| *Habilidades* | **`video-animate`** — é justamente o modo «imagem → vídeo» (i2v) |
| *Executor* | o executor de IA criado; ou deixar em branco e confiar na escolha automática — mas então a tarefa precisa ter uma **equipe**: a escolha é feita entre os integrantes dela |
| *Projeto* e *Equipe* | na seção **«Avançado»**; o projeto é obrigatório — é a partir dele que os caminhos são calculados |

A descrição:

```
@obj:OBJ-1 vira-se lentamente por cima do ombro esquerdo, olha para a câmera.
Chuva fina, reflexos de neon no asfalto molhado, rua noturna.
A câmera se aproxima lentamente.
Colocar o resultado no arquivo scenes/s01.mp4.
```

O que acontece aqui no disparo:

1. `@obj:OBJ-1` é expandido no cartão do objeto — nome, tipo, número, o passaporte literal e os
   caminhos dos arquivos de referência, cada um em uma linha nova;
2. os caminhos são **recortados** do prompt (são caminhos, não texto da cena), e o **primeiro**
   deles se torna o quadro inicial;
3. a linha «Colocar o resultado no arquivo `scenes/s01.mp4`» também é recortada — quem a executa
   é o sistema: o vídeo pronto é copiado para a pasta do projeto com esse nome;
4. todo o resto — inclusive o passaporte — vai para o modelo como prompt.

> **Escreva as instruções em uma linha à parte.** Em uma descrição de várias linhas, uma
> instrução («colocar o resultado em …», «tomar como base …») é removida **com a linha inteira**
> — junto com o que você tiver escrito ao lado. A linha «A câmera se aproxima lentamente.
> Colocar o resultado no arquivo scenes/s01.mp4.» custaria ao prompt a aproximação da câmera.
> (Uma descrição de uma única linha é exceção: ali só a frase com a instrução é removida.)

**O link é cômodo de não digitar à mão:** na lista de objetos cada linha tem um botão «link», e
no editor da descrição há o botão **«Link para um objeto do projeto»**: ele insere o link na
posição do cursor no formato definido pela configuração do projeto *«Formato do link de
objeto»* — por número (`@obj:OBJ-1`) ou por nome (`@obj:[Herói Vasya]`). **As duas formas são
sempre reconhecidas**, de modo que trocar a configuração não quebra as descrições já escritas.

### Se for preciso um quadro específico, e não o primeiro

Nomeie o arquivo por extenso — essa instrução é **mais forte** que o caminho do objeto:

```
Tomar como base refs/hero_side.png.
@obj:OBJ-1 vira-se lentamente por cima do ombro esquerdo…
```

É considerada instrução a linha em que houver o nome de um arquivo de imagem e uma palavra como
«original», «como base», «inicial», «referência», «anime», `source`, `start`, `based on`. Essa
linha é removida do prompt por inteiro.

### Uma limitação importante

O quadro inicial do modelo é **um só**. Os demais caminhos de referência são simplesmente
removidos do prompt — o modelo não os vê. Vários quadros de referência em um objeto são úteis
para a pessoa e para um futuro treinamento de adaptador LoRA, mas na geração i2v só influi o
primeiro caminho: o caminho do próprio objeto e, se ele estiver vazio, o caminho do primeiro
filho ativo.

## 1.8. Passo 6. Disparo e acompanhamento

O botão **«Iniciar»** no cartão da tarefa.

* A guia **«Trabalhos»** traz a lista de trabalhos e o **console**: é para ele que é despejada a
  saída do próprio ComfyUI, inclusive o contador de passos. Por ele se vê se a geração anda ou
  travou.
* A primeira linha do console é o resumo do disparo: o endereço do ComfyUI, o modelo, o tamanho
  do quadro, o número de quadros e de passos, o `seed`, o comprimento do prompt, o quadro inicial
  e o arquivo do resultado.
* A espera é longa: cerca de uma hora em 6 GB de VRAM. O limite de espera é o «Tempo de espera da
  resposta» do executor e, se ele não estiver definido, o `params.timeoutMinutes` do perfil (por
  padrão 180 minutos).
* Dá para parar pelo botão **«Parar»** do cartão da tarefa: a geração no ComfyUI é interrompida e
  o trabalho é retirado da fila dele.

**O que se obtém:**

* a guia **«Resultado»** — os arquivos do trabalho (`J-12-Kandinsky_00001.mp4`) e o resumo
  `J-12-result.md`: modelo, tamanho do quadro, número de quadros e de passos, o **`seed`** e o
  tempo de geração;
* uma cópia do vídeo na pasta do projeto — se na descrição houver a instrução de onde colocá-lo;
* a requisição exata — o arquivo `J-12-request.json` (o prompt depois de todas as substituições,
  o `startImage`, o `seed` e o grafo inteiro). O caminho dele está registrado no diário de
  trabalhos, na guia **«Histórico»** da tarefa. É a melhor forma de conferir que o modelo
  recebeu exatamente o que você pretendia.

## 1.9. O mesmo personagem em uma série de quadros

| técnica | o que dá |
|---|---|
| **o mesmo objeto** em todos os quadros | rosto, roupa e cor se mantêm entre os vídeos |
| **`seed` fixo** | o mesmo «temperamento» da geração; vazio ou 0 significa aleatório a cada trabalho |
| **encadeamento** | salvar o último quadro da cena N como imagem na pasta do projeto e entregá-lo como entrada da cena N+1 |
| **um único tamanho de quadro** | vídeos com `width`/`height` diferentes se afastam uns dos outros |

O `seed` e os demais parâmetros de geração são editados no perfil do modelo — o botão
**«Perfil…»** do formulário do modelo ou do formulário do executor:

| Parâmetro | Padrão | Significado |
|---|---|---|
| `width` / `height` | 768 × 512 | a resolução do quadro; a imagem inicial também é ajustada a ela (recortada pelo centro) |
| `length` | 121 | quadros, ≈5 segundos |
| `steps` | 50 | passos de difusão; menos é mais rápido e mais grosseiro |
| `negative` | vazio | prompt negativo |
| `seed` | não definido | seed fixo |
| `timeoutMinutes` | 180 | quanto esperar pelo resultado |

O perfil do executor é próprio dele: dá para criar dois executores no mesmo modelo com `seed` e
resolução diferentes.

## 1.10. Erros frequentes

| Mensagem ou sintoma | O que fazer |
|---|---|
| **«Este modelo precisa de um quadro inicial…»** | na descrição não há nem um arquivo de imagem nem um link para um objeto com arquivo de referência |
| **«O arquivo do quadro inicial não foi encontrado na pasta do projeto: …»** | o caminho é calculado a partir da pasta do projeto; confira a grafia e se o arquivo está mesmo ali |
| **«O projeto não tem pasta definida neste computador…»** | defina o «Caminho da pasta do projeto» no cartão do projeto — a pasta é própria de cada servidor |
| **«O quadro inicial … leva para fora da pasta do projeto»** | `..` e caminhos absolutos são proibidos de propósito |
| **«Tempo de espera esgotado ao conectar ao ComfyUI (o servidor não foi iniciado?)»** | o trabalho da equipe não foi iniciado (passo 2) — é ele que sobe o ComfyUI |
| **«A descrição da tarefa está vazia — o modelo de mídia precisa do texto do prompt de geração na descrição»** | depois de recortar as instruções não sobrou nada da descrição; escreva o que acontece no quadro |
| **«O workflow deste modelo não aceita quadro inicial — o arquivo … não é usado»** | a tarefa foi para um modelo «texto → vídeo»; dê à tarefa a habilidade `video-animate` ou indique explicitamente o executor desejado |
| **O link `@obj:…` ficou no prompt como texto** | o objeto não foi encontrado: número/nome errado ou objeto de outro projeto. O link não é apagado de propósito — senão o quadro sairia sem o personagem e o prompt pareceria correto |
| **O personagem «derrete»** | verifique se os quadros fazem referência ao mesmo objeto, se o `seed` está fixo, se o tamanho do quadro é o mesmo e se o passaporte não foi parafraseado de novo na descrição |
| **O processo desaparece sem mensagem** | quase sempre é o driver NVIDIA antigo; comece por `nvidia-smi`, é preciso o 580+ |
| **Falta VRAM** | reduza `width`/`height` ou `length` |

## 1.11. O que vale lembrar

* O modelo de mídia **não conversa**: ele não tem ferramentas nem chat. Escrever para ele no
  chat da tarefa é inútil — um quadro novo é um novo disparo de trabalho.
* O título, os critérios de aceitação, a experiência do projeto e a experiência do nó de modelo
  **não entram** no prompt. Tudo o que precisa chegar à geração está na descrição da tarefa ou no
  passaporte do objeto.
* Um link dentro do passaporte **não é expandido**: um passaporte que faça referência a outro
  objeto deixa o link como texto. É a proteção contra objetos que se referenciam mutuamente.
* O objeto pertence ao projeto: um link não alcança um objeto de um projeto vizinho.
* Um objeto desligado permanece na lista e os links para ele não se rompem — isso não é exclusão;
  mas os **filhos** desligados não entram na substituição de caminhos.

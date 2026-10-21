# AI2P no VS Code

O AI2P pode funcionar dentro do VS Code: o cartão do projeto, o ambiente de trabalho, as
configurações, a documentação e os cartões de tarefas abrem como painéis do editor, ao lado do
código do projeto. É a mesma interface do AI2P que no navegador, só que sem o menu superior, as
abas, a barra lateral esquerda e o navegador; o tema claro ou escuro vem do VS Code.

Tudo funciona pela extensão **AI2P** para VS Code. Ela acompanha o AI2P (o arquivo
`ai2p-<versão>.vsix` na pasta do programa), sua versão é sempre igual à do AI2P, e o próprio AI2P
a instala — não é preciso baixar nada do Marketplace.

## Requisitos

* AI2P e VS Code no **mesmo computador**: os painéis acessam o servidor em `http://localhost`.
* O projeto tem uma **pasta do projeto** neste servidor. Cada servidor tem a sua: se neste servidor não houver pasta, aqui não é possível conectar o VS Code
  ao projeto e os botões não aparecem.
* VS Code instalado da forma habitual (veja «Limitações»).

## Conectar um projeto

1. No AI2P abra o cartão do projeto, aba **«Configuração»**, em modo de edição.
2. Clique em **«Conectar o VS Code»**. Se houver várias cópias do VS Code no computador, primeiro
   aparece uma janela de escolha. A escolha fica guardada para este projeto **neste servidor** e
   não passa para outros servidores do cluster — projetos diferentes podem usar cópias diferentes.
3. O AI2P então:
   * instala ou atualiza a extensão na cópia escolhida;
   * cria `.ai2p/link.json` na pasta do projeto — endereço do servidor, código da organização,
     projeto e caminho do servidor. **Não há segredos neste arquivo**;
   * emite um **token de dispositivo** e o entrega ao VS Code por um link
     `vscode://ai2p.ai2p/link`. O VS Code pergunta se a extensão pode abrir o link — permita. O
     token fica no armazenamento seguro do VS Code (SecretStorage), não nos arquivos do projeto;
     O link usa o esquema da cópia escolhida (`vscode-insiders://`, `vscodium://`, `cursor://`,
     `windsurf://`), então o token chega exatamente a essa cópia;
   * conforme as caixas da janela de conexão — adicionar `.ai2p/` ao `.gitignore` e abrir o
     projeto no VS Code — atualiza o `.gitignore` e abre a pasta do projeto.

Também é possível conectar ao criar o projeto: se o VS Code for encontrado e a pasta estiver
definida, o formulário de novo projeto tem a caixa «Conectar o VS Code».

A extensão é ativada **apenas** em pastas com `.ai2p/link.json`; em outros projetos ela não aparece.

## Primeira abertura da pasta: «Trust Folder»

Quando o VS Code abre a pasta do projeto **pela primeira vez**, ele pergunta se você confia nos
autores (a janela **«Do you trust the authors of the files in this folder?»** com os botões
**«Yes, I trust the authors»** / **«Trust Folder»** e **«No, I don't trust the authors»**).
Enquanto a pasta não for confiável, o VS Code a mantém no modo restrito (Restricted Mode) e as
extensões não funcionam por completo nela.

Por isso, na primeiríssima conexão o link com o token que o AI2P passa ao VS Code **se perde**:
enquanto você lê a janela de confiança, a extensão ainda não consegue recebê-lo e os painéis do
AI2P não abrem. O que fazer — **uma única vez**:

1. Na janela de confiança clique em **«Yes, I trust the authors»** (**«Trust Folder»**).
2. **Feche** essa janela do VS Code (ou a pasta: **File → Close Folder**).
3. Clique de novo no AI2P em **«Abrir o projeto no VS Code»** (ou **«Conectar VS Code»**).

Agora a pasta é confiável, o VS Code não pergunta mais, o link chega e o projeto abre na hora.
Não é preciso repetir isso para a mesma pasta — só para cada nova pasta de projeto.

## Menu e painéis

Na Activity Bar à esquerda aparece o ícone **AI2P**. O menu tem quatro itens:

| Item | Abre |
|---|---|
| **Projeto** | o cartão do projeto |
| **Ambiente** | o ambiente de trabalho e os arquivos (só se houver arquivos) |
| **Configurações** | as configurações do AI2P |
| **Documentos** | esta documentação |

As mesmas ações estão na paleta de comandos (`Ctrl+Shift+P`) com o prefixo **«AI2P:»**:
«AI2P: Projeto», «AI2P: Ambiente», «AI2P: Configurações», «AI2P: Documentos», «AI2P: Abrir
tarefa…», «AI2P: Conectar projeto a partir do AI2P», «AI2P: Desconectar o VS Code», «AI2P:
Iniciar servidor», «AI2P: Verificar atualização da extensão». Os textos estão traduzidos para os
cinco idiomas do AI2P e seguem o idioma do VS Code (para o português o VS Code usa `pt-br`).

Cada item abre um só painel: escolhê-lo de novo traz o painel aberto para a frente. Uma
**tarefa** escolhida em qualquer painel (árvore, quadro, tabela, diagrama, Inbox, link no texto)
abre num **painel separado** ao lado — o cartão de uma tarefa. O VS Code restaura os painéis
abertos após reiniciar.

<img src="../../images/Vs_code_AI2P_tasks_diagram_2.png" alt="Diagrama de subtarefas em um painel do VS Code: o menu «…» do cartão da tarefa" width="900">

**Tema.** O tema claro ou escuro vem do VS Code e muda na hora em todos os painéis quando você
troca o tema do editor. Não altera o seu tema do AI2P no navegador.

**Idioma.** Os painéis usam o idioma do VS Code se for um dos idiomas do AI2P (russo, inglês,
espanhol, português, chinês); caso contrário, o idioma da instalação do AI2P.

## O servidor AI2P

Se o servidor não estiver em execução, a extensão o inicia ao abrir o primeiro painel — uma vez
por computador, mesmo com várias janelas do VS Code. O servidor vive separado do VS Code: fechar
o editor não o encerra.

Se o servidor for reiniciado, os painéis esperam por ele e recarregam sozinhos.

## Entrada e revogação do token

Os painéis não têm formulário de entrada: o VS Code entra com o **token de dispositivo** emitido
na conexão, em nome de quem clicou em «Conectar o VS Code» e com os direitos dessa pessoa. O
token vale só neste servidor.

Seus tokens aparecem no AI2P: **Configurações → «Notificações» → «Meus dispositivos (VS Code)»** —
dispositivo, projeto, data de emissão e última entrada. **«Revogar»** fecha o acesso: na próxima
requisição o painel mostra «É preciso conectar o VS Code». Cada um vê e revoga apenas os seus
tokens.

## Desconectar

Na aba «Configuração» do projeto clique em **«Desconectar o VS Code»**: o AI2P apaga `.ai2p/link.json` e
revoga todos os seus tokens deste projeto. A extensão continua instalada — ela é comum a todos
os projetos.

O comando **«AI2P: Desconectar o VS Code»** da paleta apaga o token só neste computador; para
fechá-lo também no servidor, revogue-o em «Meus dispositivos».

## Atualização da extensão

A extensão é atualizada junto com o AI2P:

* ao iniciar, o servidor verifica a versão da extensão em todas as cópias do VS Code guardadas
  e, se estiver atrasada, instala a nova em segundo plano;
* a própria extensão compara sua versão com a do servidor ao iniciar e, se estiver atrasada,
  oferece **«Atualizar»**; depois o VS Code pede para recarregar a janela (Reload Window).

Para verificar manualmente: «AI2P: Verificar atualização da extensão».

## Limitações

* **Somente localhost por HTTP.** Os painéis do VS Code não aceitam certificado autoassinado,
  por isso mesmo num servidor HTTPS o modo integrado usa `http://localhost`. Não é possível
  conectar o VS Code a um servidor em outro computador.
* **Derivados do VS Code** (VS Code Insiders, VSCodium, Cursor, Windsurf) são detectados e podem
  ser escolhidos, mas só o VS Code normal foi testado.
* **macOS e Linux.** A extensão foi testada no Windows, no macOS e no Linux.
* Se o computador entrou em suspensão ou a rede caiu sem o servidor parar, o painel pode mostrar
  a janela de reconexão por mais tempo; feche e abra o painel de novo.
* Dentro dos painéis não funcionam janelas de entrada nem formulários que exigem a entrada
  normal: entra-se apenas por token.

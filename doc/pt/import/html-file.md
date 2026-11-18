# Importar uma página HTML («HTML público») — guia

A entrada **«HTML público»** já está no catálogo de importações, na primeira linha. Não tem
configurações nem segredos: não é preciso criá-la, inserir chaves nem verificar a conexão.

## Como importar

Lista de tarefas do projeto → **«Importar tarefa por URL»** → cole o endereço da página → **«Importar»**.
O endereço decide o que acontece:

| Endereço | O que é importado |
| --- | --- |
| `file:///C:/pasta/pagina.htm` ou um caminho completo `C:\pasta\pagina.htm` | Uma página salva em disco. As imagens da pasta `…_files` ao lado (o navegador a cria em «Salvar como… → completa») são copiadas para a tarefa. Frames com arquivos locais são expandidos. |
| `https://…` ou `http://…` | Uma página pública da web — **só essa página**, a importação não segue links. As imagens são baixadas e copiadas para a tarefa; todos os links continuam clicáveis e levam ao site de origem. |

A tarefa é criada como rascunho: o título vem de `<title>` (senão do primeiro `<h1>`) e a
descrição é o texto da página em Markdown. Se a página de origem for apagada, a tarefa continua inteira.

Endereços do Trello, GitHub, GitLab e Figma continuam indo para os seus importadores — «HTML público»
só pega o que ninguém mais reconheceu.

## Limitações

* Uma página desenhada por script (aplicações de página única) chega quase vazia: a importação
  lê o HTML como está e não executa scripts. Salve essa página no navegador
  («Salvar como… → completa») e importe o arquivo.
* Uma página protegida por login não pode ser lida: a importação não conhece os cookies do navegador.
* Limites: 64 MB por página, 50 MB por imagem, no máximo 300 imagens.
* Estilos, formulários, vídeo e áudio não são transferidos.
* Uma página da web não pode pegar arquivos deste computador: as imagens `file:` nela são ignoradas.
* Para um agente, um arquivo em disco só é legível em pastas abertas pelas regras de segurança da tarefa.

## Atualização

O botão «atualizar» no cartão da tarefa relê a página pelo link de importação.

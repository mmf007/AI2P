# Política de segurança do AI2P

## Versões suportadas

Correções de segurança saem apenas para a **última versão lançada** do AI2P
(página Releases do repositório). As versões anteriores não são corrigidas — atualize para a mais
recente: a instalação por cima da anterior preserva os dados, as configurações e as chaves.

| Versão | Suportada |
|---|---|
| último lançamento | sim |
| todas as anteriores | não |

## Como relatar uma vulnerabilidade

**Não abra uma issue pública** nem escreva sobre a vulnerabilidade no Discussions: enquanto não
houver correção, uma descrição pública ajuda quem quiser explorá-la.

Relate de forma privada por um destes meios:

1. **GitHub → aba Security → Report a vulnerability** (Private vulnerability reporting) —
   o meio principal. A mensagem é vista apenas pelos mantenedores do repositório.
2. **E-mail** — para o endereço indicado no perfil do dono do repositório no GitHub, com o assunto
   «AI2P security».

O que anexar à mensagem:

* a versão do AI2P (arquivo `version.json` no diretório de instalação) e o SO;
* o que exatamente é vulnerável e qual é o risco;
* os passos para reproduzir ou um exemplo (requisição, arquivo, texto da tarefa);
* se souber — a correção proposta.

## Prazos de resposta

* confirmação do recebimento — em até **7 dias**;
* primeira avaliação (se é uma vulnerabilidade ou não, e quão grave é) — em até **30 dias**;
* a correção sai no próximo lançamento; combinaremos o prazo com você e, até a correção sair,
  pedimos que não publique os detalhes. Se quiser, mencionaremos você na descrição do lançamento.

## Modelo de ameaças: o que o AI2P protege e o que não protege

O AI2P é um programa local: o servidor roda no seu computador, e os agentes de IA que ele
inicia rodam nele também. Daí vêm os limites da proteção, que é preciso conhecer de antemão.

**O agente CLI roda com o usuário do servidor.** O agente de linha de comando (Claude Code e
outros executores CLI) é iniciado como um processo separado, com a mesma conta do SO que o
servidor AI2P, sem sandbox e sem isolamento próprio. As regras de segurança da tarefa
(diretórios permitidos e proibidos, ações) valem para as ferramentas do próprio AI2P e são
passadas ao agente no trabalho, mas **o SO não as impõe**: os meios próprios do agente
(leitura de arquivos, busca, shell) não são limitados ao diretório de trabalho do projeto. Ou seja,
o agente **pode ler arquivos fora do projeto** — tudo o que essa conta pode acessar,
inclusive chaves e tokens no diretório pessoal. Os perfis da distribuição iniciam o agente no modo
de edição sem confirmações.

O que isso significa na prática:

* execute o servidor AI2P com uma **conta do SO separada**, sem acesso a dados alheios e pessoais,
  se nem tudo na máquina for confiado ao agente; no Linux é possível executá-lo em um contêiner,
  montando dentro dele apenas os diretórios dos projetos;
* não mantenha nesta máquina segredos que o agente não pode ver;
* as regras «proibir»/«perguntar» para o agente CLI são um acordo, e não uma barreira; a barreira
  são as permissões do SO;
* o texto de uma tarefa importada de um sistema externo (Trello, GitHub, GitLab, Figma) é
  uma entrada de terceiros: ele pode conter instruções que o agente vai executar
  (prompt injection). Verifique as tarefas vindas de fontes alheias antes de entregá-las ao agente.

**Servidor e rede.** Por padrão, a interface e a API escutam em `localhost`. O acesso a `/api` é
fechado por login. Ao abrir o servidor para a rede (vários servidores, acesso pelo celular), ative
o HTTPS (veja «HTTPS» no manual do administrador) e não publique o servidor na internet sem um
proxy reverso com proteção própria.

**Segredos.** As chaves de API e a senha do administrador ficam no disco, no diretório de instalação
(`secrets.json`, `secrets/`), em texto aberto, sob as permissões do usuário do servidor (no Linux e
no macOS — `0600`). A proteção deles são as permissões do sistema de arquivos e a criptografia do disco.

Consideramos vulnerabilidade contornar esses limites: acesso à API sem login, o agente sair das
regras onde o AI2P promete cumpri-las com as suas ferramentas, vazamento de segredos para os logs,
as tarefas ou a réplica em outro servidor, execução de código a partir de dados de importação ou de
replicação etc. O fato de o agente CLI ler os arquivos da sua conta é a propriedade descrita acima,
e não uma vulnerabilidade.

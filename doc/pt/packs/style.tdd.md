# Desenvolvimento guiado por testes

**Código:** `style.tdd` · **Registros:** 10 · **Habilidades:** `code-test`, `code-write`,
`code-debug`, `code-refactor`

## O que é este estilo

Desenvolvimento em que o teste se escreve antes do código: primeiro o teste vermelho, depois a
menor alteração que o torna verde, depois a refatoração no verde. Esta metodologia foi
escolhida porque muda a **ordem das ações** do executor, e não a forma do relatório — é aí que
um pacote de regras de texto rende mais.

## Para quem é

Para projetos com código executável e algum conjunto de testes. Se não há onde executá-los, ou
a execução leva horas e ninguém a lança, o conjunto vira uma lista de desejos: arranje primeiro
uma execução rápida.

## O que muda após a instalação

* O teste escreve-se primeiro e tem de falhar pelo motivo para o qual foi escrito; a mensagem
  de falha da execução vermelha vai para o relatório como prova.
* Um teste, uma afirmação; o nome diz a condição e o resultado esperado.
* A um teste vermelho corresponde a menor alteração; o que «vai servir depois» vai na volta
  seguinte.
* Corrigir um defeito começa por um teste que o reproduz.
* Verifica-se o comportamento observável, não a construção interna: um teste não deve proibir a
  refatoração.
* O teste é reproduzível: sem hora atual, sem acaso, sem rede, sem depender da ordem.
* Uma execução verde só conta junto com o número de testes aprovados.
* A refatoração faz-se no verde e não altera nenhum teste.
* Um teste alheio que fica vermelho investiga-se, não se desliga em silêncio.
* Um caso não coberto é nomeado no relatório numa linha própria.

## O que vale a pena ajustar

* **A exigência do «vermelho no relatório»** sai cara quando a execução leva horas: troque-a
  por «indique o teste e o motivo da falha», ou o executor dará a volta.
* **A regra do comportamento observável** ganha se você acrescentar a sua lista do que conta
  como observável (resposta HTTP, linha na base, evento do log).
* **A regra do número de testes aprovados** é a única que apanha uma «execução verde sem teste
  nenhum». Se os seus filtros forem outros, escreva a sua forma do comando no registro.
* **O escopo de instalação.** Os registros `code-test` servem a toda a organização; a regra da
  alteração mínima choca com projetos de passos grandes: instale-a por projeto.

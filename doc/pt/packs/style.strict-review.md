# Revisão de código rigorosa

**Código:** `style.strict-review` · **Registros:** 9 · **Habilidades:** `code-review`,
`code-write`

## O que é este estilo

A disciplina de revisar o trabalho alheio: por onde a revisão começa, o que cada observação
deve conter e como ela termina. Esta metodologia foi escolhida porque a revisão é o único ponto
do processo onde a qualidade depende inteiramente de um acordo e não de uma ferramenta: nenhum
analisador vai notar que foi feito algo diferente do que se pediu.

## Para quem é

Para equipes em que uma pessoa escreve o código e outra o aceita — e sobretudo para as mistas,
em que a IA escreve e a pessoa aceita (ou o contrário). Se a revisão não existe como passo
próprio, acrescente-a ao processo primeiro e instale o conjunto depois.

## O que muda após a instalação

* A revisão começa pelo pedido e pelos critérios de aceitação, não pelo diff.
* O texto abre com a lista do que vai quebrar: cenário de falha com entradas concretas; as
  observações de estilo vão depois, em seção separada.
* Cada observação indica lugar (arquivo, linha) e peso: bloqueante, importante, a critério.
* Cada defeito indica o teste que o detecta, existente ou a escrever; sem ele a observação
  conta como não comprovada.
* Os limites examinam-se sempre: entrada vazia, zero, o máximo, chamada repetida, chamada
  concorrente, queda de um serviço externo.
* A revisão limita-se ao código alterado; um problema antigo ao lado vira outra tarefa.
* Quem revisa não edita o código alheio e termina com um veredicto inequívoco: aceito, aceito
  com correções, devolvido.
* Antes de entregar, o autor percorre a sua alteração e tira tudo o que não consegue explicar.

O conjunto traz um nó de modelo, **«Revisão das alterações»**, com o critério de aceitação
«cada defeito indica um cenário de falha e um teste».

## O que vale a pena ajustar

* **A escala de pesos.** «Bloqueante / importante / a critério» é a mais simples; se a sua for
  outra, reescreva o registro, senão o executor inventa uma.
* **A regra «não mexa no código alheio».** Em equipes pequenas onde a correção de quem revisa é
  normal, suavize-a: caso contrário o agente recusará um pedido direto do autor.
* **O escopo de instalação.** A regra do veredicto cabe nas regras gerais da organização; o
  resto, na experiência dos projetos que de fato têm revisão.
* **O registro para o autor** (`code-write`) serve sozinho: só ele já impede que prints de
  depuração e reformatações acidentais cheguem à revisão.

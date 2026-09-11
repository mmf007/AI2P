# YandexGPT-5.1-Pro

**Alocação:** em nuvem (Yandex Cloud, Rússia; compatível com OpenAI)
**Conexão:** `provider: openai-compatible`,
`baseUrl: https://llm.api.cloud.yandex.net/v1`, modelo
`gpt://<folder-id>/yandexgpt/latest`
**Referência da chave:** `yandex.apiKey`

Texto forte em russo: `text-write` 86, `text-translate` 86, `text-edit` 85. O código é o ponto
fraco (60 a 64) — não vale pegar este modelo para programação. O contexto é modesto para os
padrões do catálogo: 32 000 tokens.

## No campo «modelo» é preciso colocar o seu próprio endereço

O Yandex Cloud espera não um identificador curto, e sim o **recurso completo**:

```
gpt://<folder-id>/yandexgpt/latest
```

**O identificador do diretório (folder ID) é próprio de cada usuário**, e por isso no catálogo
ele foi deixado como marcador — o registro não funcionará «de fábrica». Abra
**Configurações → Catálogos → Modelos de IA → YandexGPT-5.1-Pro → «Perfil de conexão»** e
substitua `<folder-id>` pelo seu (no formato `b1g...`, visível no console do Yandex Cloud no
endereço do diretório). Em vez de `latest` pode-se indicar uma versão específica do modelo.

## Como obter a chave

1. Crie um diretório no [console do Yandex Cloud](https://console.yandex.cloud/) e vincule-o a
   uma conta de pagamento.
2. Crie uma **conta de serviço** e conceda a ela o papel `ai.languageModels.user` sobre o
   diretório.
3. Crie uma **chave de API** da conta de serviço: **Contas de serviço → sua conta →
   Criar nova chave → Criar chave de API**.
4. Copie a parte secreta — ela é mostrada **uma única vez**.
5. Copie o **identificador do diretório** e coloque-o no campo «modelo» do perfil
   (veja acima).
6. No AI2P: **Configurações → Catálogos → Modelos de IA → YandexGPT-5.1-Pro →
   «Definir a chave de API»**.

## Limitações e custo

| | |
|---|---|
| Contexto | 32 000 tokens |
| Resposta máxima | 16 000 tokens |
| Custo | pela tarifa do Yandex Cloud (no catálogo está marcado zero) |

A tarifa é calculada em rublos por mil tokens, e por isso na declaração o custo foi deixado como
zero — **o consumo deste modelo o faturamento do AI2P não mostrará**; veja-o no console do
Yandex Cloud. O contexto de 32 000 tokens é o menor do catálogo: uma descrição longa de tarefa
ou um histórico grande de chat não caberão nele.

**O identificador do modelo não foi verificado com uma chamada real** — confira a lista dos
modelos disponíveis com a requisição `GET /models` a `https://llm.api.cloud.yandex.net/v1` com a
sua chave.

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | o cliente **tem o direito de usar o Conteúdo Gerado por quaisquer meios** que não contrariem as condições e a lei (item 4.1); não lhe prometem direito exclusivo nem exclusividade |
| Uso comercial | permitido |
| O que é obrigatório | não apresentar o gerado como resultado de trabalho humano (item 3.11.4); a Yandex pode instituir a obrigação de indicar que o serviço foi aplicado (item 3.8) |
| Texto das condições | <https://yandex.ru/legal/cloud_terms_yandex_foundation_models/ru/> |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

Sobre o treinamento está dito diretamente (item 5.4.1): as informações das requisições **podem
ser usadas para depuração e treinamento dos modelos**, enquanto você não definir o parâmetro
especial de proibição. Para trabalhar com dados alheios, vale fazer isso antes da primeira
tarefa.

As condições foram conferidas pelo texto delas em 27.08.2026.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`llm.api.cloud.yandex.net`.

## Erros frequentes

* **400 / «model not found»** — no campo «modelo» ficou o marcador
  `<folder-id>`.
* **403 «permission denied»** — à conta de serviço não foi concedido o papel
  `ai.languageModels.user` sobre o diretório correto.
* **A tarefa é interrompida em uma descrição longa** — os 32 000 tokens de contexto não bastam;
  leve os materiais para arquivos e coloque links na descrição.

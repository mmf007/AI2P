# GigaChat-3.5-Ultra

**Alocação:** em nuvem (Sber, Rússia; compatível com OpenAI)
**Conexão:** `provider: openai-compatible`,
`baseUrl: https://gigachat.devices.sberbank.ru/api/v1`, modelo `GigaChat-3.5-Ultra`
**Referência da chave:** `gigachat.accessToken`

Um modelo russo de 432 bilhões de parâmetros (MoE) com pesos abertos sob MIT. **O melhor texto
em russo do catálogo**: notas `text-write` 85, `text-edit` 84, `text-translate` 84. O código é
bem mais fraco (61 a 66) — para programação pegue outros registros.

## A conexão é incompleta — leia antes de criar a chave

O registro existe no catálogo, mas **de forma padrão ele não funciona até o fim**, e esta é uma
limitação conhecida, e não um erro de instalação:

* O Sber fornece **não uma chave permanente, e sim um token de acesso por OAuth que vive 30
  minutos**. O conector do AI2P só sabe lidar com chave permanente, e por isso o token colado
  deixa de funcionar depois de meia hora e terá de ser informado de novo. O campo no catálogo se
  chama justamente assim — `gigachat.accessToken`.
* É preciso o **certificado-raiz russo** (o do Ministério do Desenvolvimento Digital) no
  repositório de certificados confiáveis
  do computador; caso contrário a conexão com `gigachat.devices.sberbank.ru` será interrompida
  na verificação TLS.

O suporte pleno (um conector próprio com renovação do token ou um adaptador de proxy externo) é
uma tarefa à parte do projeto. Enquanto ela não existe, este registro serve para testes
pontuais, e não para o trabalho de fundo do agente.

## Como obter a chave

1. Registre-se no [Studio do Sber](https://developers.sber.ru/studio/) e crie um projeto
   **GigaChat API**.
2. Obtenha o par **Client ID / Client Secret** («chave de autorização» na interface).
3. Instale o certificado-raiz russo no computador em que o AI2P funciona.
4. Troque a chave de autorização por um **access token** (requisição `POST` a
   `https://ngw.devices.sberbank.ru:9443/api/v2/oauth`, cabeçalho `RqUID`, campo
   `scope=GIGACHAT_API_PERS` ou `GIGACHAT_API_CORP`).
5. No AI2P: **Configurações → Catálogos → Modelos de IA → GigaChat-3.5-Ultra →
   «Definir a chave de API»** — cole o token obtido. **Depois de 30 minutos repita os
   passos 4 e 5**: a validade do token não é prorrogada.

## Limitações e custo

| | |
|---|---|
| Contexto | 262 144 tokens |
| Resposta máxima | 32 768 tokens |
| Custo | pela tarifa do Sber (no catálogo está marcado zero) |

A tarifa é calculada em unidades próprias do Sber, e não em dólares por milhão de tokens, e por
isso na declaração o custo foi deixado como zero — **o consumo deste modelo o faturamento do
AI2P não mostrará**. Veja-o na área pessoal do Studio.

**O identificador do modelo não foi verificado com uma chamada real** — confira-o com a
requisição `GET /models` a `https://gigachat.devices.sberbank.ru/api/v1`.

## Licença

| | |
|---|---|
| Condições sobre o resultado da geração | **os direitos sobre o Conteúdo Gerado pertencem ao Usuário** (Acordo GigaChat, item 1.9) |
| Uso comercial | permitido |
| O que é obrigatório | pelo item 5.2 você concede ao Banco uma licença irrevogável, gratuita e não exclusiva de uso do Conteúdo Gerado — até a transformação (os direitos sobre a transformação ficam com o Banco) e a publicidade |
| Texto das condições | <https://developers.sber.ru/docs/ru/policies/gigachat-agreement/beta> |
| Pagamento pela geração | por tokens — a tarifa está na seção «Limitações e custo» |

Os direitos sobre o resultado são formalmente seus, mas **a licença que você concede ao Sber é a
mais ampla entre os registros do catálogo**: reprodução, comunicação ao público, transformação e
uso em publicidade. Se o resultado for segredo comercial ou não puder ser mostrado a terceiros,
pegue outro modelo.

As condições foram conferidas pelo texto do acordo em 27.08.2026. O acordo está marcado como
beta — leia-o antes de conectar, pois ele muda com mais frequência que os demais.

## Requisitos de hardware

Nenhum: os cálculos ficam do lado do fornecedor. É preciso ter saída para a internet até
`gigachat.devices.sberbank.ru` e o certificado-raiz russo instalado.

## Erros frequentes

* **401 meia hora depois de uma partida bem-sucedida** — o token de acesso venceu; obtenha um
  novo.
* **Erro de TLS / «não foi possível estabelecer relação de confiança»** — o certificado-raiz
  russo não está instalado.
* **A tarefa com código saiu mal feita** — o nicho do modelo é o texto em russo, e não a
  programação.

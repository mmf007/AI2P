# Модели AI2P: как устроен справочник

Каталог с документами моделей: по файлу на каждую запись справочника ИИ-моделей, имя
файла — **название модели** (`Claude-Sonnet-5.md`, `Qwen3.8-27B-Local.md`). Открывается
кнопкой **«i»** в форме модели: **Настройки → Справочники → ИИ-модели**.

Здесь — общее для всех: чем различаются три способа подключения, как поставить ключ,
почему модель бывает неактивной и как завести свою запись.

## Оглавление раздела

**Облачные (нужен ключ API)**

* [Chatterbox-TTS](Chatterbox-TTS.md) — текст + образец голоса → речь этим голосом, навыки `audio-speech` 88
* [Claude-Fable-5](Claude-Fable-5.md) — API провайдера Anthropic
* [Claude-Fable-5.1](Claude-Fable-5.1.md) — API провайдера Anthropic
* [Claude-Haiku-4.5](Claude-Haiku-4.5.md) — API Anthropic
* [Claude-Opus-5.0](Claude-Opus-5.0.md) — API провайдера Anthropic
* [Claude-Opus-5.5](Claude-Opus-5.5.md) — API провайдера Anthropic
* [Claude-Sonnet-5](Claude-Sonnet-5.md) — API Anthropic
* [DeepSeek-V4-Flash](DeepSeek-V4-Flash.md) — API DeepSeek, OpenAI-совместимый
* [DeepSeek-V4-Pro](DeepSeek-V4-Pro.md) — API DeepSeek, OpenAI-совместимый
* [DeepSeek-V4.1-Flash](DeepSeek-V4.1-Flash.md) — API DeepSeek, OpenAI-совместимый
* [ElevenLabs-Music](ElevenLabs-Music.md) — текст → музыка и песни, очищенные по правам, навыки `audio-song` 89, `audio-music` 89
* [ElevenLabs-Music-v2.5](ElevenLabs-Music-v2.5.md) — текст → музыка и песни, очищенные по правам, навыки `audio-song` 92, `audio-music` 92
* [ElevenLabs-TTS-v3](ElevenLabs-TTS-v3.md) — текст → речь, навыки `audio-speech` 96
* [Gemini-3-Ultra](Gemini-3-Ultra.md) — Google, через OpenAI-совместимый слой (**погашена 11.09.2026**)
* [Gemini-3.1-Pro](Gemini-3.1-Pro.md) — Google, через OpenAI-совместимый слой (**погашена 11.09.2026**)
* [Gemini-3.7-Flash](Gemini-3.7-Flash.md) — Google, через OpenAI-совместимый слой
* [Gemini-3.8-Flash](Gemini-3.8-Flash.md) — Google, через OpenAI-совместимый слой
* [Gemini-Omni-Flash](Gemini-Omni-Flash.md) — текст → видео со звуком, 8 секунд, навыки `video-generate` 91
* [GigaChat-3.5-Ultra](GigaChat-3.5-Ultra.md) — Сбер, Россия; OpenAI-совместимый
* [GLM-5.2](GLM-5.2.md) — Z.ai / Zhipu, OpenAI-совместимый
* [GLM-5.3](GLM-5.3.md) — Z.ai / Zhipu, OpenAI-совместимый
* [GPT-5.6-Sol](GPT-5.6-Sol.md) — API OpenAI, OpenAI-совместимый
* [GPT-5.6-Terra](GPT-5.6-Terra.md) — API OpenAI, OpenAI-совместимый
* [GPT-6-Astra](GPT-6-Astra.md) — API OpenAI, OpenAI-совместимый
* [GPT-6-Luna](GPT-6-Luna.md) — API OpenAI, OpenAI-совместимый
* [GPT-6-Sol](GPT-6-Sol.md) — API OpenAI, OpenAI-совместимый
* [GPT-Image-2](GPT-Image-2.md) — текст → изображение, счёт по токенам, навыки `image-generate` 96, `image-text` 95, `image-photo` 94, `image-concept` 90
* [GPT-Image-2.5](GPT-Image-2.5.md) — текст → изображение, счёт по токенам, навыки `image-generate` 97, `image-text` 96, `image-photo` 95, `image-concept` 92
* [Grok-4.6](Grok-4.6.md) — API xAI, OpenAI-совместимый
* [Grok-4.7](Grok-4.7.md) — API xAI, OpenAI-совместимый
* [Inkling-975B](Inkling-975B.md) — Thinking Machines, через шлюз OpenRouter
* [Kimi-K3](Kimi-K3.md) — Moonshot AI, OpenAI-совместимый
* [Kling-3.0](Kling-3.0.md) — картинка → видео со звуком, до 15 секунд, навыки `video-animate` 93
* [Ling-3.0-Flash](Ling-3.0-Flash.md) — Ant Group, через шлюз OpenRouter
* [Meshy-7](Meshy-7.md) — текст → готовая к игре 3D-модель, навыки `3d-generate` 87
* [Meshy-7.1](Meshy-7.1.md) — текст → готовая к игре 3D-модель, навыки `3d-generate` 89
* [MiniMax-H3-Max](MiniMax-H3-Max.md) — текст → видео до 15 секунд, навыки `video-generate` 92
* [MiniMax-M3](MiniMax-M3.md) — MiniMax, OpenAI-совместимый
* [Mistral-Large-3](Mistral-Large-3.md) — Mistral AI, Франция; OpenAI-совместимый
* [Muse-Spark-1.2](Muse-Spark-1.2.md) — Meta, через шлюз OpenRouter
* [Muse-Spark-1.3](Muse-Spark-1.3.md) — Meta, через шлюз OpenRouter
* [Nano-Banana-Pro-Edit](Nano-Banana-Pro-Edit.md) — картинка и указание → изменённая картинка, навыки `image-edit` 98, `image-inpaint` 90
* [Nano-Banana-Pro](Nano-Banana-Pro.md) — текст → изображение до 4K, лучший текст в кадре, навыки `image-text` 98, `image-generate` 97, `image-photo` 96, `image-concept` 92
* [Nemotron-3-Ultra](Nemotron-3-Ultra.md) — NVIDIA, через шлюз OpenRouter
* [Qwen3.8-Max](Qwen3.8-Max.md) — Alibaba DashScope, OpenAI-совместимый
* [Qwen3.8-Omni-Flash](Qwen3.8-Omni-Flash.md) — Alibaba DashScope, OpenAI-совместимый
* [Seedance-2.5-I2V](Seedance-2.5-I2V.md) — картинка → видео со звуком, навыки `video-animate` 95
* [Seedance-2.5](Seedance-2.5.md) — текст → видео до 30 секунд со звуком, навыки `video-generate` 97
* [Seedream-5-Flash](Seedream-5-Flash.md) — текст → изображение, навыки `image-generate` 92, `image-concept` 90, `image-photo` 90
* [Tripo-H3.1](Tripo-H3.1.md) — картинка → 3D-модель с PBR-текстурами, навыки `3d-image` 92
* [Tripo-P2](Tripo-P2.md) — картинка → 3D-модель с PBR-текстурами четырёх уровней, навыки `3d-image` 94
* [Veo-3.1](Veo-3.1.md) — текст → видео со звуком, 4–8 секунд, до 4K, навыки `video-generate` 93
* [Wan-3.0-Prime](Wan-3.0-Prime.md) — текст → видео до 30 секунд со звуком, навыки `video-generate` 94
* [YandexGPT-5.1-Pro](YandexGPT-5.1-Pro.md) — Yandex Cloud, Россия; OpenAI-совместимый
* [Zonos-2-TTS](Zonos-2-TTS.md) — текст + образец голоса → речь этим голосом (образец обязателен), навыки `audio-speech` 86

**Через Claude CLI (по подписке, ключ не нужен)**

* [Claude-Fable-5_cli](Claude-Fable-5_cli.md)
* [Claude-Opus-5.0_cli](Claude-Opus-5.0_cli.md)
* [Claude-Sonnet-5_cli](Claude-Sonnet-5_cli.md)
* [Claude-Fable-5.1_cli](Claude-Fable-5.1_cli.md)
* [Claude-Haiku-4.5_cli](Claude-Haiku-4.5_cli.md)
* [Claude-Opus-5.5_cli](Claude-Opus-5.5_cli.md)

**Локальные (считает ваш компьютер)**

* [Muse-Glimmer-30B-Local](Muse-Glimmer-30B-Local.md) — тексты и код, оценки навыков 73–80
* [Qwen3.5-4B-Local](Qwen3.5-4B-Local.md) — лёгкая модель для роли суфлёра: 3 ГБ видеопамяти, оценки навыков 62–74
* [Qwen3.6-27B-Local](Qwen3.6-27B-Local.md) — код и тексты, оценки навыков 75–82
* [Qwen3.6-35B-A3B-Local](Qwen3.6-35B-A3B-Local.md) — код и тексты, оценки навыков 72–80
* [Qwen3.8-27B-Local](Qwen3.8-27B-Local.md) — код и тексты, оценки навыков 80–87

**Локальные медиа-модели (видео, изображения, звук и 3D)**

* [ACE-Step-1.5-XL-Base](ACE-Step-1.5-XL-Base.md) — текст → музыка и песни, навыки `audio-song` 86, `audio-music` 87
* [ACE-Step-1.5-XL-SFT](ACE-Step-1.5-XL-SFT.md) — текст → музыка и песни, навыки `audio-song` 90, `audio-music` 89
* [ACE-Step-1.5-XL-Turbo](ACE-Step-1.5-XL-Turbo.md) — текст → музыка и песни, навыки `audio-song` 84, `audio-music` 86
* [FLUX.2-dev](FLUX.2-dev.md) — текст → изображение, навыки `image-generate` 94, `image-photo` 93, `image-concept` 92, `image-text` 90
* [FLUX.2-klein-4B-Edit](FLUX.2-klein-4B-Edit.md) — картинка + указание → изображение, навыки `image-edit` 86, `image-inpaint` 83, `image-generate` 80, `image-text` 78
* [FLUX.2-klein-4B](FLUX.2-klein-4B.md) — текст → изображение, навыки `image-generate` 88, `image-photo` 87, `image-concept` 86, `image-text` 80
* [Hunyuan3D-2.1](Hunyuan3D-2.1.md) — изображение → 3D-модель, навык `3d-image` 86
* [HunyuanVideo-1.5-720p-T2V](HunyuanVideo-1.5-720p-T2V.md) — текст → видео, навык `video-generate` 79
* [Kandinsky-5.0-I2V-Lite-5s](Kandinsky-5.0-I2V-Lite-5s.md) — изображение + текст → видео (5 секунд), навыки `video-animate` 78, `video-generate` 74
* [Kandinsky-5.0-Image-Lite](Kandinsky-5.0-Image-Lite.md) — текст → изображение, навыки `image-text` 88, `image-generate` 84, `image-concept` 83, `image-photo` 82
* [Kandinsky-5.0-T2V-Lite-distil16-10s](Kandinsky-5.0-T2V-Lite-distil16-10s.md) — текст → видео, навык `video-generate` 70
* [Kandinsky-5.0-T2V-Lite-distil16-5s](Kandinsky-5.0-T2V-Lite-distil16-5s.md) — текст → видео, навык `video-generate` 70
* [Kandinsky-5.0-T2V-Lite-nocfg-10s](Kandinsky-5.0-T2V-Lite-nocfg-10s.md) — текст → видео, навык `video-generate` 74
* [Kandinsky-5.0-T2V-Lite-nocfg-5s](Kandinsky-5.0-T2V-Lite-nocfg-5s.md) — текст → видео, навык `video-generate` 74
* [Kandinsky-5.0-T2V-Lite-sft-10s](Kandinsky-5.0-T2V-Lite-sft-10s.md) — текст → видео, навык `video-generate` 76
* [Kandinsky-5.0-T2V-Lite-sft-5s](Kandinsky-5.0-T2V-Lite-sft-5s.md) — текст → видео (5 секунд), навыки `video-generate` 76, `video-animate` 72
* [LTX-2.5](LTX-2.5.md) — текст → видео со звуком, навык `video-generate` 85
* [Qwen-Image-2512](Qwen-Image-2512.md) — текст → изображение, навыки `image-text` 94, `image-photo` 91, `image-generate` 90, `image-concept` 85
* [Qwen-Image-Edit-2511](Qwen-Image-Edit-2511.md) — картинка + указание → изменённая картинка, навыки `image-edit` 90, `image-text` 90, `image-inpaint` 85, `image-generate` 82
* [SD-3.5-Large](SD-3.5-Large.md) — текст → изображение, навыки `image-generate` 80, `image-photo` 79, `image-concept` 78, `image-text` 70
* [SDXL-1.0](SDXL-1.0.md) — текст → изображение, навыки `image-generate` 70, `image-concept` 70, `image-photo` 68
* [TRELLIS-2](TRELLIS-2.md) — изображение → 3D-модель С ЦВЕТОМ, навык `3d-image` 85
* [TripoSplat](TripoSplat.md) — изображение → ГАУССОВЫ СПЛАТЫ, навык `3d-image` 80
* [Wan-2.2-I2V-A14B](Wan-2.2-I2V-A14B.md) — картинка + текст → видео, навыки `video-animate` 80, `video-generate` 74 — **погашена 23.09.2026** (после 2.2 вышли Wan 2.6, 2.7 и 3.0)
* [Wan-2.2-T2V-A14B](Wan-2.2-T2V-A14B.md) — текст → видео, навык `video-generate` 80 — **погашена 23.09.2026** (после 2.2 вышли Wan 2.6, 2.7 и 3.0)
* [Z-Image-Turbo](Z-Image-Turbo.md) — текст → изображение, навыки `image-generate` 85, `image-photo` 84, `image-concept` 82, `image-text` 78

## Три способа подключения

| | облачная | локальная | через CLI |
|---|---|---|---|
| Где считает | сервер провайдера | **ваш компьютер** | сервер провайдера |
| Плата | по токенам | нет | **подпиской**, в биллинге 0 |
| Что нужно | **ключ API** | **скачать файлы** (десятки ГБ) | `claude login` на этом компьютере |
| `provider` в профайле | `anthropic`, `openai-compatible` | `openai-compatible` | `anthropic` + `transport: cli` |
| `baseUrl` | адрес провайдера | `http://localhost:<порт>/v1` | не используется |
| Инструменты агента | инструменты AI2P | инструменты AI2P | **штатные инструменты CLI** |
| Данные уходят наружу | да | **нет** | да |

* **Облачная** — большинство записей справочника. Быстро, качественно, платно; нужен
  ключ провайдера и выход в интернет. Примеры: Claude-Sonnet-5,
  GPT-5.6-Sol, DeepSeek-V4-Pro.
* **Локальная** (имя оканчивается на `-Local`) — веса лежат на вашем диске, считает ваша
  видеокарта, наружу не уходит ничего. Ключ не нужен, зато нужны место, память и
  терпение на скачивание. Примеры: Qwen3.8-27B-Local,
  Muse-Glimmer-30B-Local. Локальный сервер (`llama-server`)
  AI2P поднимает сама при старте работы команды и гасит при остановке — **только свой
  процесс**, чужой на том же порту не трогается.
* **Через CLI** (имя оканчивается на `_cli`) — та же облачная модель, но запускается
  через Claude Code в headless-режиме и оплачивается **подпиской**, а не токенами.
  **Ключ API не нужен**, нужен выполненный `claude login`. Важное отличие: агент
  работает **своими** инструментами прямо в папке проекта, инструменты AI2P ему не
  публикуются. Примеры: Claude-Sonnet-5_cli,
  Claude-Fable-5_cli.

## Как поставить ключ

1. **Настройки → Справочники → ИИ-модели** — найдите запись в списке.
2. Кнопка **«Установить ключ API»** в форме модели (если ключ уже есть, она называется
   «Обновить ключ API»; старое значение не показывается никогда).
3. Вставьте значение, выданное провайдером, и сохраните.

**Ключ общий у всех моделей одного провайдера.** В профайле каждой записи есть поле
«Ключ в секретах (ссылка)» — например `anthropic.apiKey` или `openrouter.apiKey`. Все
записи с одной и той же ссылкой пользуются одним ключом: ввели его для Claude-Sonnet-5 —
заработали и Opus, и Haiku. Где именно взять ключ, написано в документе конкретной
модели, в разделе **«Как получить ключ»**.

Ключ **принадлежит организации**: он шифруется ключом организации и реплицируется на все
её серверы, так что вводить его на каждом компьютере не нужно. Сам ключ организации
лежит только на своём компьютере (`secrets.json`) и не реплицируется никогда.

## Где ключ лежит на диске

Ключ можно задать и **файлом** — это второй способ, для установки без интерфейса и для
переноса старых ключей. Файлы лежат в подкаталоге **`secrets/` рядом с `config.json`**
(то есть в каталоге, куда установлено приложение), по одному json на ссылку:

```
secrets/anthropic.apikey.json
{ "ref": "anthropic.apiKey", "value": "sk-ant-..." }
```

Полный путь этого файла для конкретной модели показан в её форме строкой **«Файл ключа
на этом компьютере»** — там же, где кнопка установки ключа. Подкаталог заводится при
первом запуске приложения, внутри лежит пояснение `readme.txt`.

**Ключ, введённый в форме, файла здесь не создаёт**, и это не ошибка: он уходит в базу
организации зашифрованным, чтобы доехать до остальных серверов. Файл появляется у ключей
файлового хранилища — перенесённых из старого `secrets.json` при старте и вписанных
руками. Если такой файл есть, ввод нового значения в форме обновит и его.

Порядок, в котором ищется ключ: **база организации → `secrets/` → `secrets.json` →
переменная окружения** (`anthropic.apiKey` → `ANTHROPIC_API_KEY`). Подкаталог `secrets/`
не попадает ни в репликацию, ни в выкладку, ни в опись установки — при обновлении версии
он остаётся нетронутым.

## Почему модель может быть неактивной

Признак «активна» решает, предлагается ли модель при выборе исполнителя. Включить его
можно не всегда, и это правило, а не ошибка:

* **облачная модель без ключа API активной быть не может** — форма так и пишет: «Нет
  ключа API: модель активируется сразу после установки ключа», а попытка включить
  признак через API отклоняется;
* **локальная модель без скачанных файлов активной быть не может** — «Модель не
  установлена: активируется автоматически после установки».

Смысл простой: иначе исполнитель числился бы готовым, задание уходило бы ему и падало
уже на запуске — с невнятной ошибкой сети вместо понятного «нет ключа». Как только ключ
введён или установка завершилась, признак включается сам.

Отсюда же следует, что **модель `_cli` активна сразу**: ключ ей не нужен, файлы качать
нечего. Проверка одна — установлен ли Claude Code у пользователя, под которым работает
AI2P; её AI2P заранее не делает.

## Как добавить свою запись

Справочник дистрибутива пополняется с новыми версиями, но никто не мешает завести свою
модель — например, тот же локальный Qwen в более мелком кванте или модель провайдера,
которого в справочнике нет.

1. **Настройки → Справочники → ИИ-модели → «Добавить модель»**.
2. **Имя** — по схеме `<общее имя>-<имя модели>`: `Claude-Opus-5.0`, `DeepSeek-V4-Pro`.
   У локальной добавляйте суффикс `-Local`. Имя должно быть уникальным.
3. **Размещение** — «В облаке (API провайдера)» или «Локальная (работает на этом
   компьютере)».
4. **«Профайл…»** — провайдер (`anthropic`, `openai-compatible`, `comfyui`), подключение
   (API или CLI-агент), **модель (id у провайдера)**, Base URL, ссылка на ключ в
   секретах, команда запуска локального сервера и параметры (JSON, например
   `{"maxTokens": 32768}`).
5. **«Декларация возможностей…»** — форматы ввода и вывода, навыки с оценкой 0–100,
   лимиты (контекст, максимум ответа) и стоимость за 1 млн токенов. По этой декларации
   работает автоподбор исполнителя: **навык, которого нет в справочнике навыков, не
   увидит никто** — коды берите из списка в форме, а не придумывайте.
6. Поставьте ключ (облачной) или нажмите **«Установить»** (локальной) — и включите
   признак «активна».
7. **Документ своей модели** положите сюда же: `doc/ru/models/<имя модели>.md`. Если
   документа нет, кнопка «i» покажет подсказку с полным путём, куда его класть.

Свои записи помечаются как **«Кастомная»** и, в отличие от записей дистрибутива,
удаляются целиком. Записи дистрибутива удалить нельзя — если модель не нужна, просто
снимите у неё признак «активна».

## Чего в документах моделей нет

Идентификаторы, цены и лимиты в справочнике сверены с публичным каталогом моделей и
обзором рынка, но **ни один идентификатор не проверен живым вызовом провайдера**: у
проекта нет ключей ко всем этим API. Поэтому в каждом документе облачной модели стоит
одна и та же строка — перед первым заданием сверьте id запросом `GET /models` к API
провайдера. Рынок меняется быстро (примерно модель в двое суток), и запись справочника
устаревает молча: сменившийся id даёт 404 уже на исполнителе, а неверная цена — неверный
счёт в биллинге.

Требования к железу локальных моделей — **оценка по размеру весов**; замеров на реальном
железе не делали.

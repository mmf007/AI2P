using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Core.Events;
using Microsoft.Data.Sqlite;

namespace AI2P.Storage.Services;

/// <summary>
/// Справочник ИИ-моделей (ТЗ п. 2.9) — часть настройки системы.
/// Стартовый набор поставляется с дистрибутивом (is_custom = 0), пользователи пополняют.
/// Профайл и декларация возможностей — json-файлы в dataDir: models/profile_&lt;ID&gt;.json,
/// models/scope_&lt;ID&gt;.json; в БД — относительные пути.
/// </summary>
public sealed class AiModelService
{
    /// <summary>
    /// Версия seed-файлов дистрибутива: при повышении версии профайлы и декларации
    /// НЕкастомных моделей перезаписываются данными дистрибутива (поле "_seed" в файле).
    /// Файлы кастомных моделей никогда не трогаются.
    /// </summary>
    /// <remarks>
    /// 14 (T-4-S0, версия 1.101): пакеты обучения LoRA ставятся при УСТАНОВКЕ МОДЕЛИ, а не при
    /// первом запуске обучения. У обеих записей Kandinsky-5 к musubi-tuner добавлен python
    /// (lora.train.packages), и запускающий скрипт берёт python из пакета, а не из PATH.
    /// Смысл правки — во времени ожидания: установку модели человек и так ждёт, а нажатие
    /// «обучить» должно начинать обучение, а не загрузку десятков мегабайт. Плата известна и
    /// принята заказчиком: у тех, у кого Kandinsky уже стоит, модель после обновления станет
    /// «не установлена», пока они не зайдут в неё и не нажмут «Установить».
    ///
    /// 13 (T-289): у обеих записей Kandinsky-5 ЗАПОЛНЕНА настройка обучения LoRA — до этого
    /// команда обучения была пустой, и первое же нажатие «обучить» отвечало «в справочнике
    /// не сказано, чем обучать». Обучение идёт musubi-tuner (kohya-ss): у него есть задачи
    /// k5-lite-t2v-5s-sd и k5-lite-i2v-5s-sd, одна видеокарта, внимание через sdpa и вывод
    /// адаптера в формате lora_unet_*, который ComfyUI грузит своим LoraLoaderModelOnly.
    /// Официальный kandinsky-5-lora-train сюда не годится: он требует NCCL и нескольких
    /// видеокарт (init_device_mesh("cuda") + torchrun), то есть на Windows не работает вовсе,
    /// а его файл адаптера пришлось бы ещё и переименовывать ключи. Файлы настройки тренера
    /// (dataset.toml и train.cmd) объявлены прямо в профайле — блок "files" (LoraTrainFile).
    ///
    /// 12 (T-274, версия 1.98): у датасета обучения появились КОНТРОЛЬНЫЕ НАСТРОЙКИ картинок —
    /// формат файла кадра и предел его веса. Обеим записям Kandinsky-5 проставлен формат PNG:
    /// обучающий репозиторий kandinsky-5-lora-train берёт пары «*.png (или *.mp4) + *.txt»,
    /// другого формата картинки он не читает. Предел веса кадра остался нулём — «не сказано»:
    /// в репозитории про размер файла нет ни слова, а выдуманное ограничение хуже пустого.
    ///
    /// 15 (T-115-S0): в справочник пакетов добавлен ПОРТАТИВНЫЙ SHOTCUT — им ставится
    /// программа шлюза в Shotcut/Kdenlive, а нужен от неё ровно один файл, melt (рендер без
    /// окна). Ссылка и размер сняты запросом к релизам mltframework/shotcut, а не выдуманы:
    /// v26.8.1, shotcut-win64-26.8.1.zip, 225 002 077 байт (HEAD, 03.09.2026). Блок "system"
    /// стоит намеренно: melt чаще всего уже есть у того, кто монтирует, и качать рядом второй
    /// экземпляр на 215 МиБ незачем.
    ///
    /// 16 (T-116-S0 и T-118-S0, один подъём на двоих — номер вбит руками в проверках, и два
    /// подъёма подряд означали бы правку их дважды): добавлены пакеты FFMPEG (конвертор) и
    /// ПОРТАТИВНЫЙ BLENDER — программа шлюза в Blender VSE. Ссылка и
    /// размер сняты запросом к download.blender.org, а не выдуманы: blender-5.2.1-windows-x64.zip,
    /// 404 851 964 байта (HEAD, 03.09.2026, Last-Modified 25.08.2026). Блок "system"
    /// обязателен: Blender у того, кто делает 3D, стоит и так, а 386 МиБ рядом — впустую.
    ///
    /// 17 (T-137-S0): у ВСЕХ одиннадцати train.cmd починен поиск корня репозитория тренера.
    /// Раньше он считался жёстко — «два каталога вверх от найденного скрипта» (musubi-tuner
    /// разложен как &lt;корень&gt;/src/musubi_tuner/*.py), а в архиве тега v0.3.4 скрипты лежат
    /// в САМОМ корне: два уровня вверх уводили в корень пакетов, окружение создавалось не там,
    /// и «pip install -e» не находил musubi-tuner вовсе — обучение падало на первом же запуске
    /// с жалобой на неустановленный тренер. Теперь корень ищется по pyproject.toml (сам
    /// каталог скриптов, один и два уровня вверх), а не нашёлся — берётся каталог скриптов.
    ///
    /// 18 (T-185-S0): ОБУЧЕНИЕ БОЛЬШЕ НИЧЕГО НЕ СТАВИТ МОЛЧА. Первый запуск обучения
    /// Kandinsky тянул с huggingface.co исходный Qwen/Qwen2.5-VL-7B-Instruct (5 файлов,
    /// ~16 ГБ) и CLIP, печатая при этом одну строку «Fetching 5 files», — со стороны это
    /// зависание, и тайм-аут молчания обрывал живую закачку (жалоба T-313). Оба кодировщика
    /// стали ПАКЕТАМИ справочника (qwen2.5-vl-7b и clip-vit-large-patch14) с точными
    /// размерами файлов, ставятся вместе с моделью в окне установки с ходом работы, а
    /// train.cmd получает их КАТАЛОГИ ({packageDir:…}) вместо идентификаторов HuggingFace —
    /// тренер берёт готовое с диска и в сеть за ними не ходит вовсе. Там же, в установке,
    /// делается окружение тренера: у пакета musubi-tuner появился блок "setup" (venv,
    /// torch cu124, «pip install -e .» по его файлу требований, huggingface_hub и hf_xet).
    /// Блок ":setup" в train.cmd остался запасным путём — на случай, когда окружение
    /// удалили или тренер поставлен руками.
    ///
    /// 21 (T-286-S0): МОДЕЛЬ-СУФЛЁР. У трёх вариантов ACE-Step 1.5 XL в профайле появилась
    /// секция "prompter" (required / rules / schema): параметры трека — длительность, язык
    /// вокала, слова песни, темп, тональность — задаются у этой модели ОТДЕЛЬНЫМИ полями
    /// графа, а не общим текстом промпта, поэтому управляющий json для неё готовит вторая,
    /// текстовая, модель. Версия поднята ради того, чтобы секция доехала до профайлов, уже
    /// лежащих на диске у установленных систем: без подъёма файл не перезаписывается.
    ///
    /// 22 (T-287-S0): ПОЛЯ ГРАФА ИЗ JSON СУФЛЁРА. В workflow трёх вариантов ACE-Step 1.5 XL
    /// поля узлов "4" и "6" стали именованными плейсхолдерами {p:имя|умолчание} (tags,
    /// lyrics, duration, language, bpm, keyscale, timesignature), причём длительность в
    /// обоих узлах — ОДИН плейсхолдер {p:duration}. В профайле у тех же записей уточнена
    /// схема суфлёра: перечни значений language, keyscale и timesignature и границы bpm и
    /// duration сняты с ЖИВОГО ComfyUI (/object_info), а не объявлены по смыслу. Версия
    /// поднята ради того, чтобы новый workflow и новая схема доехали до уже установленных
    /// систем; умолчания в шаблоне прежние, поэтому без суфлёра граф собирается как раньше.
    /// </remarks>
    private const int SeedFileVersion = 22;

    /// <summary>
    /// Модели дистрибутива: фиксированные UUID — при распределённом вводе (дистрибутив +
    /// пользователи на разных узлах) записи дистрибутива на всех установках совпадают.
    /// Профайлы (todo16): полные настройки подключения; ключ — secrets.json (гл. 10).
    /// Декларации (п. 7.3): оценки skills по публичным бенчмаркам (SWE-bench и др.),
    /// лимиты и цены — по официальным данным провайдеров на 2026-07.
    /// </summary>
    /// <summary>
    /// ВАРИАНТЫ Kandinsky 5.0 Video Lite (T-15-S0). У линейки опубликовано шесть вариантов
    /// одной и той же модели 2B, в справочнике до этого задания была только пара sft-5s
    /// (текст → ролик) и i2v-5s (картинка → ролик). Различаются варианты ровно четырьмя
    /// величинами: файл весов, число шагов, сила следования тексту и длина ролика.
    /// <list type="bullet">
    /// <item>sft — исходный вариант: 50 шагов, cfg 5, лучшее качество;</item>
    /// <item>no-CFG — те же 50 шагов, но без второго прохода на отрицательный текст,
    /// то есть ВДВОЕ быстрее (обучающая сила следования 1.0);</item>
    /// <item>distil16 — 16 шагов, ВШЕСТЕРО быстрее исходного, качество ниже.</item>
    /// </list>
    /// Пять записей строятся из одного шаблона намеренно: пять почти одинаковых копий
    /// профайла со своим train.cmd разошлись бы молча при первой же правке.
    /// Таблица объявлена ВЫШЕ <see cref="SeedModels"/> не для красоты: статические поля
    /// считаются в порядке объявления, и снизу она досталась бы сиду пустой.
    /// </summary>
    private sealed record KandinskyLiteKind(
        string Id, string Name, string Model, string Unet, string Repo,
        int Steps, int Cfg, int Length, int Score, string Task);

    private static readonly KandinskyLiteKind[] KandinskyLite =
    [
        new("6f1a45e0-0d31-4c65-9a01-000000000052", "Kandinsky-5.0-T2V-Lite-sft-10s",
            "kandinsky5lite_t2v_sft_10s", "kandinsky5lite_t2v_sft_10s.safetensors",
            "Kandinsky-5.0-T2V-Lite-sft-10s", 50, 5, 241, 76, "k5-lite-t2v-10s-sd"),
        new("6f1a45e0-0d31-4c65-9a01-000000000053", "Kandinsky-5.0-T2V-Lite-nocfg-5s",
            "kandinsky5lite_t2v_nocfg_5s", "kandinsky5lite_t2v_nocfg_5s.safetensors",
            "Kandinsky-5.0-T2V-Lite-nocfg-5s", 50, 1, 121, 74, "k5-lite-t2v-5s-nocfg-sd"),
        new("6f1a45e0-0d31-4c65-9a01-000000000054", "Kandinsky-5.0-T2V-Lite-nocfg-10s",
            "kandinsky5lite_t2v_nocfg_10s", "kandinsky5lite_t2v_nocfg_10s.safetensors",
            "Kandinsky-5.0-T2V-Lite-nocfg-10s", 50, 1, 241, 74, "k5-lite-t2v-10s-nocfg-sd"),
        new("6f1a45e0-0d31-4c65-9a01-000000000055", "Kandinsky-5.0-T2V-Lite-distil16-5s",
            "kandinsky5lite_t2v_distilled16steps_5s",
            "kandinsky5lite_t2v_distilled16steps_5s.safetensors",
            "Kandinsky-5.0-T2V-Lite-distilled16steps-5s", 16, 1, 121, 70,
            "k5-lite-t2v-5s-distil-sd"),
        new("6f1a45e0-0d31-4c65-9a01-000000000056", "Kandinsky-5.0-T2V-Lite-distil16-10s",
            "kandinsky5lite_t2v_distilled16steps_10s",
            "kandinsky5lite_t2v_distilled16steps_10s.safetensors",
            "Kandinsky-5.0-T2V-Lite-distilled16steps-10s", 16, 1, 241, 70,
            "k5-lite-t2v-10s-distil-sd"),
    ];

    /// <summary>
    /// Варианты ACE-Step 1.5 XL (T-18-S0) — музыка и песни по тексту, локально через ComfyUI.
    /// Граф у всех трёх ОДИН (официальные шаблоны Comfy-Org audio_ace_step1_5_xl_base/_sft/
    /// _turbo отличаются только этими величинами), различаются файл весов, число шагов,
    /// сила следования тексту и <c>top_p</c> планировщика:
    /// <list type="bullet">
    /// <item>turbo — дистиллят: 8 шагов без CFG, «коммерческое качество» за секунды;</item>
    /// <item>base — 50 шагов, cfg 6: качество среднее, РАЗНООБРАЗИЕ высокое;</item>
    /// <item>sft — 50 шагов, cfg 7: качество высокое, разнообразие среднее.</item>
    /// </list>
    /// Группа установки одна на все три: вес делят текстовые энкодеры (9 ГБ) и VAE, своим
    /// у варианта остаётся только DiT — вторая и третья записи докачивают по 9,3 ГиБ.
    /// Слова песни отдельным полем НЕ передаются намеренно: у ACE-Step 1.5 языковая модель
    /// внутри сама раскладывает запрос на строение песни, слова и метаданные (карточка
    /// модели, «LM functions as an omni-capable planner»), а у коннектора текстовый
    /// плейсхолдер один — {prompt}, и вторым пришлось бы занять {negative} из профайла,
    /// то есть одни и те же слова во всех заданиях.
    /// </summary>
    private sealed record AceStep15Kind(
        string Id, string Name, string Model, string Unet, long UnetSize,
        int Steps, int Cfg, string TopP, int Song, int Music);

    private static readonly AceStep15Kind[] AceStep15 =
    [
        new("6f1a45e0-0d31-4c65-9a01-000000000057", "ACE-Step-1.5-XL-Turbo",
            "acestep_v1_5_xl_turbo", "acestep_v1.5_xl_turbo_bf16.safetensors", 9974719892L,
            8, 1, "0.9", 84, 86),
        new("6f1a45e0-0d31-4c65-9a01-000000000058", "ACE-Step-1.5-XL-Base",
            "acestep_v1_5_xl_base", "acestep_v1.5_xl_base_bf16.safetensors", 9974719930L,
            50, 6, "0.9", 86, 87),
        new("6f1a45e0-0d31-4c65-9a01-000000000059", "ACE-Step-1.5-XL-SFT",
            "acestep_v1_5_xl_sft", "acestep_v1.5_xl_sft_bf16.safetensors", 9974719930L,
            50, 7, "1", 90, 89),
    ];

    private static readonly (string Id, string Name, string ProfileJson, string ScopeJson)[] SeedModels =
    [
        ("6f1a45e0-0d31-4c65-9a01-000000000001", "Claude-Fable-5",
            """
            {
              "_seed": 22,
              "provider": "anthropic",
              "model": "claude-fable-5",
              "baseUrl": "",
              "secretRef": "anthropic.apiKey",
              "params": {
                "maxTokens": 16000,
                "effort": "high",
                "fallbacks": ["claude-opus-5"]
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "claude-fable-5",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 98 },
                { "name": "code-review",          "score": 97 },
                { "name": "code-debug",           "score": 97 },
                { "name": "code-test",            "score": 95 },
                { "name": "code-refactor",        "score": 96 },
                { "name": "text-write",           "score": 95 },
                { "name": "text-docs",            "score": 95 },
                { "name": "text-edit",            "score": 94 },
                { "name": "text-translate",       "score": 93 },
                { "name": "text-summarize",       "score": 96 },
                { "name": "analyze-requirements", "score": 97 },
                { "name": "analyze-plan",         "score": 96 },
                { "name": "analyze-data",         "score": 95 }
              ],
              "limits":  { "context": 1000000, "max_output": 128000 },
              "cost":    { "in_per_1m": 10.0, "out_per_1m": 50.0 }
            }
            """),
        ("6f1a45e0-0d31-4c65-9a01-000000000002", "Claude-Opus-5.0",
            """
            {
              "_seed": 22,
              "provider": "anthropic",
              "model": "claude-opus-5",
              "baseUrl": "",
              "secretRef": "anthropic.apiKey",
              "params": {
                "maxTokens": 16000,
                "effort": "high"
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "claude-opus-5",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 96 },
                { "name": "code-review",          "score": 95 },
                { "name": "code-debug",           "score": 95 },
                { "name": "code-test",            "score": 93 },
                { "name": "code-refactor",        "score": 95 },
                { "name": "text-write",           "score": 93 },
                { "name": "text-docs",            "score": 93 },
                { "name": "text-edit",            "score": 92 },
                { "name": "text-translate",       "score": 92 },
                { "name": "text-summarize",       "score": 95 },
                { "name": "analyze-requirements", "score": 95 },
                { "name": "analyze-plan",         "score": 95 },
                { "name": "analyze-data",         "score": 94 }
              ],
              "limits":  { "context": 1000000, "max_output": 128000 },
              "cost":    { "in_per_1m": 5.0, "out_per_1m": 25.0 }
            }
            """),
        // Подключение по CLI (todo29): те же модели Claude через Claude Code CLI (headless).
        // transport "cli" + cliCommand; secretRef пуст — авторизация сессией CLI (claude login);
        // стоимость 0 — оплата подпиской, потому по CLI дешевле, чем по API.
        ("6f1a45e0-0d31-4c65-9a01-000000000005", "Claude-Fable-5_cli",
            """
            {
              "_seed": 22,
              "provider": "anthropic",
              "transport": "cli",
              "cliCommand": "claude --permission-mode acceptEdits",
              "model": "claude-fable-5",
              "baseUrl": "",
              "secretRef": "",
              "params": {},
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "claude-fable-5",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 98 },
                { "name": "code-review",          "score": 97 },
                { "name": "code-debug",           "score": 97 },
                { "name": "code-test",            "score": 95 },
                { "name": "code-refactor",        "score": 96 },
                { "name": "text-write",           "score": 95 },
                { "name": "text-docs",            "score": 95 },
                { "name": "text-edit",            "score": 94 },
                { "name": "text-translate",       "score": 93 },
                { "name": "text-summarize",       "score": 96 },
                { "name": "analyze-requirements", "score": 97 },
                { "name": "analyze-plan",         "score": 96 },
                { "name": "analyze-data",         "score": 95 }
              ],
              "limits":  { "context": 1000000, "max_output": 128000 },
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        ("6f1a45e0-0d31-4c65-9a01-000000000006", "Claude-Opus-5.0_cli",
            """
            {
              "_seed": 22,
              "provider": "anthropic",
              "transport": "cli",
              "cliCommand": "claude --permission-mode acceptEdits",
              "model": "claude-opus-5",
              "baseUrl": "",
              "secretRef": "",
              "params": {},
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "claude-opus-5",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 96 },
                { "name": "code-review",          "score": 95 },
                { "name": "code-debug",           "score": 95 },
                { "name": "code-test",            "score": 93 },
                { "name": "code-refactor",        "score": 95 },
                { "name": "text-write",           "score": 93 },
                { "name": "text-docs",            "score": 93 },
                { "name": "text-edit",            "score": 92 },
                { "name": "text-translate",       "score": 92 },
                { "name": "text-summarize",       "score": 95 },
                { "name": "analyze-requirements", "score": 95 },
                { "name": "analyze-plan",         "score": 95 },
                { "name": "analyze-data",         "score": 94 }
              ],
              "limits":  { "context": 1000000, "max_output": 128000 },
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // DeepSeek: id моделей подтверждены фактическим ответом GET /models API DeepSeek
        // (2026-07-15, лог пробы подключения): deepseek-v4-pro, deepseek-v4-flash.
        // T-214: лимиты и цены подтянуты к чекпойнту V4-Pro-0813 (отчёт T-213, §5.2) —
        // контекст 1 млн вместо прежних 128 тыс., ответ до 64 тыс.; цены сверены с каталогом
        // OpenRouter 2026-08-17 ($0,66/$1,98 у Pro, $0,14/$0,28 у Flash). У нативного API
        // DeepSeek цена может отличаться — сверять в консоли провайдера.
        ("6f1a45e0-0d31-4c65-9a01-000000000003", "DeepSeek-V4-Pro",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "deepseek-v4-pro",
              "baseUrl": "https://api.deepseek.com",
              "secretRef": "deepseek.apiKey",
              "params": { "maxTokens": 32768 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "deepseek-v4-pro",
              "inputs":  ["text/markdown", "text/plain", "text/source-code"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 89 },
                { "name": "code-review",          "score": 87 },
                { "name": "code-debug",           "score": 87 },
                { "name": "code-test",            "score": 85 },
                { "name": "code-refactor",        "score": 87 },
                { "name": "text-write",           "score": 85 },
                { "name": "text-docs",            "score": 86 },
                { "name": "text-edit",            "score": 84 },
                { "name": "text-translate",       "score": 86 },
                { "name": "text-summarize",       "score": 88 },
                { "name": "analyze-requirements", "score": 87 },
                { "name": "analyze-plan",         "score": 86 },
                { "name": "analyze-data",         "score": 86 }
              ],
              "limits":  { "context": 1000000, "max_output": 65536 },
              "cost":    { "in_per_1m": 0.66, "out_per_1m": 1.98 }
            }
            """),
        ("6f1a45e0-0d31-4c65-9a01-000000000004", "DeepSeek-V4-Flash",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "deepseek-v4-flash",
              "baseUrl": "https://api.deepseek.com",
              "secretRef": "deepseek.apiKey",
              "params": { "maxTokens": 32768 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "deepseek-v4-flash",
              "inputs":  ["text/markdown", "text/plain", "text/source-code"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 82 },
                { "name": "code-review",          "score": 80 },
                { "name": "code-debug",           "score": 79 },
                { "name": "code-test",            "score": 78 },
                { "name": "code-refactor",        "score": 79 },
                { "name": "text-write",           "score": 80 },
                { "name": "text-docs",            "score": 80 },
                { "name": "text-edit",            "score": 79 },
                { "name": "text-translate",       "score": 82 },
                { "name": "text-summarize",       "score": 84 },
                { "name": "analyze-requirements", "score": 78 },
                { "name": "analyze-plan",         "score": 77 },
                { "name": "analyze-data",         "score": 76 }
              ],
              "limits":  { "context": 1000000, "max_output": 32768 },
              "cost":    { "in_per_1m": 0.14, "out_per_1m": 0.28 }
            }
            """),
        // Kandinsky 5.0 Video Lite (todo36–36_3): локальная видео-модель, текст → ролик 5 с.
        // Секция install — манифест установки (ТЗ v1.40): группа = подкаталог репозитория
        // моделей, файлы с точными размерами (проверены HEAD-запросами 2026-07-24);
        // DiT — из репозитория kandinskylab, энкодеры и VAE — общие comfy-перепаковки.
        // Провайдер comfyui — задел под MediaConnector (отдельный todo); до него модель
        // ставится и хранится, но задания ей не назначаются.
        ("6f1a45e0-0d31-4c65-9a01-000000000007", "Kandinsky-5.0-T2V-Lite-sft-5s",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "kandinsky5lite_t2v_sft_5s",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000007.json",
              "params": {
                "width": 768,
                "height": 512,
                "length": 121,
                "steps": 50,
                "negative": "",
                "timeoutMinutes": 180
              },
              "install": {
                "group": "Kandinsky-5",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "Kandinsky-5.0-T2V-Lite-sft-5s.safetensors",
                    "url": "https://huggingface.co/kandinskylab/Kandinsky-5.0-T2V-Lite-sft-5s/resolve/main/model/kandinsky5lite_t2v_sft_5s.safetensors",
                    "size": 4573130528,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "qwen_2.5_vl_7b_fp8_scaled.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/HunyuanVideo_1.5_repackaged/resolve/main/split_files/text_encoders/qwen_2.5_vl_7b_fp8_scaled.safetensors",
                    "size": 9384670680,
                    "category": "text_encoders"
                  },
                  {
                    "name": "clip_l.safetensors",
                    "url": "https://huggingface.co/comfyanonymous/flux_text_encoders/resolve/main/clip_l.safetensors",
                    "size": 246144152,
                    "category": "text_encoders"
                  },
                  {
                    "name": "hunyuan_video_vae_bf16.safetensors",
                    "url": "https://huggingface.co/Kijai/HunyuanVideo_comfy/resolve/main/hunyuan_video_vae_bf16.safetensors",
                    "size": 492986478,
                    "category": "vae"
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": {
                  "kind": "workflow",
                  "node": "LoraLoaderModelOnly",
                  "placeholder": "{lora}",
                  "strengthPlaceholder": "{loraStrength}",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".safetensors"]
                },
                "train": {
                  "kind": "process",
                  "docUrl": "https://github.com/kohya-ss/musubi-tuner/blob/main/docs/kandinsky5.md",
                  "packages": ["musubi-tuner", "python", "qwen2.5-vl-7b", "clip-vit-large-patch14"],
                  "dataset": {
                    "kind": "dir",
                    "path": "lora/{object}/dataset",
                    "captions": "txt",
                    "minItems": 10,
                    "maxItems": 60,
                    "width": 768,
                    "height": 512,
                    "maxKb": 0,
                    "formats": ["png"]
                  },
                  "files": [
                    {
                      "path": "lora/{object}/dataset.toml",
                      "text": [
                        "# AI2P: written from the model profile before every training run.",
                        "# Edit it in the model profile, not here: it is overwritten each time.",
                        "[general]",
                        "resolution = [{width}, {height}]",
                        "caption_extension = \".txt\"",
                        "batch_size = 1",
                        "enable_bucket = true",
                        "bucket_no_upscale = false",
                        "",
                        "[[datasets]]",
                        "image_directory = '{dataset}'",
                        "cache_directory = '{output}\\cache'",
                        "num_repeats = 1"
                      ]
                    },
                    {
                      "path": "lora/{object}/train.cmd",
                      "text": [
                        "@echo off",
                        "rem AI2P: LoRA training for Kandinsky 5 T2V Lite via musubi-tuner.",
                        "rem Written from the model profile before every run - edit it in the model.",
                        "setlocal enableextensions",
                        "set \"TRAIN={package:musubi-tuner:kandinsky5_train_network.py}\"",
                        "for %%I in (\"%TRAIN%\") do set \"SCRIPTS=%%~dpI\"",
                        "set \"ROOT=%SCRIPTS%\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" set \"ROOT=%SCRIPTS%\"",
                        "set \"VENV=%ROOT%.venv\"",
                        "set \"PY=%VENV%\\Scripts\\python.exe\"",
                        "set \"OUT={output}\"",
                        "if not exist \"%OUT%\" mkdir \"%OUT%\"",
                        "if not exist \"%PY%\" call :setup",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching latents",
                        "\"%PY%\" \"%SCRIPTS%kandinsky5_cache_latents.py\" --dataset_config \"%~dp0dataset.toml\" --vae \"{model:hunyuan_video_vae_bf16.safetensors}\"",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching text encoder outputs",
                        "\"%PY%\" \"%SCRIPTS%kandinsky5_cache_text_encoder_outputs.py\" --dataset_config \"%~dp0dataset.toml\" --text_encoder_qwen \"{packageDir:qwen2.5-vl-7b}\" --text_encoder_clip \"{packageDir:clip-vit-large-patch14}\" --batch_size 1",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] training",
                        "\"%VENV%\\Scripts\\accelerate.exe\" launch --num_cpu_threads_per_process 1 --mixed_precision bf16 \"%SCRIPTS%kandinsky5_train_network.py\" --mixed_precision bf16 --dataset_config \"%~dp0dataset.toml\" --task k5-lite-t2v-5s-sd --dit \"{model:Kandinsky-5.0-T2V-Lite-sft-5s.safetensors}\" --vae \"{model:hunyuan_video_vae_bf16.safetensors}\" --text_encoder_qwen \"{packageDir:qwen2.5-vl-7b}\" --text_encoder_clip \"{packageDir:clip-vit-large-patch14}\" --sdpa --fp8_base --gradient_checkpointing --max_data_loader_n_workers 1 --persistent_data_loader_workers --learning_rate 1e-4 --optimizer_type AdamW8Bit --optimizer_args \"weight_decay=0.001\" --max_grad_norm 1.0 --lr_scheduler constant_with_warmup --lr_warmup_steps 100 --network_module networks.lora_kandinsky --network_dim 32 --network_alpha 32 --timestep_sampling shift --discrete_flow_shift 5.0 --scheduler_scale 10.0 --max_train_steps {steps} --output_dir \"%OUT%\" --output_name {object} --seed 42",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] done",
                        "exit /b 0",
                        "",
                        ":setup",
                        "echo [AI2P] preparing the training environment",
                        "set \"PYEXE={package:python:python.exe}\"",
                        "if defined AI2P_LORA_PYTHON set \"PYEXE=%AI2P_LORA_PYTHON%\"",
                        "set \"TORCH_INDEX=https://download.pytorch.org/whl/cu124\"",
                        "if defined AI2P_LORA_TORCH_INDEX set \"TORCH_INDEX=%AI2P_LORA_TORCH_INDEX%\"",
                        "\"%PYEXE%\" -m venv \"%VENV%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot create the virtual environment. Reinstall the model (it brings Python 3.10-3.12), or point AI2P_LORA_PYTHON at your own Python.",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install --upgrade pip",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install torch torchvision --index-url %TORCH_INDEX%",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install -e \"%ROOT%.\"",
                        "if errorlevel 1 echo [AI2P] ERROR: musubi-tuner was not found in %ROOT% - reinstall the model (Settings - Models - Install), it brings the trainer.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0"
                      ]
                    }
                  ],
                  "start": {
                    "kind": "process",
                    "command": "train.cmd",
                    "workDir": "lora/{object}",
                    "steps": 2000
                  },
                  "wait": {
                    "kind": "process",
                    "timeoutMinutes": 720
                  },
                  "result": {
                    "kind": "file",
                    "path": "lora/{object}/out/{object}.safetensors",
                    "target": "loras/{object}.safetensors"
                  }
                }
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "kandinsky5lite_t2v_sft_5s",
              "inputs":  ["text/plain"],
              "outputs": ["video/*"],
              "skills":  [
                { "name": "video-generate", "score": 76 },
                { "name": "video-animate",  "score": 72 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // Kandinsky 5.0 Video Lite Image-to-Video (T-258): та же локальная видео-модель, но
        // ролик рисуется ОТ КАРТИНКИ — стартовый кадр задаёт персонажа и сцену, промпт
        // описывает движение. Отличий от T2V-записи ровно два: свой файл весов
        // (kandinsky5lite_i2v_5s.safetensors, 4,57 ГБ) и свой workflow с узлами
        // LoadImage → ImageScale → Kandinsky5ImageToVideo.start_image.
        // Группа установки та же (Kandinsky-5): энкодеры и VAE у обеих записей общие —
        // поставив T2V, качать придётся только сам DiT. Варианта на 10 секунд у I2V-Lite
        // нет: у kandinskylab опубликован только Kandinsky-5.0-I2V-Lite-5s (проверено
        // списком репозиториев организации на HuggingFace 2026-08-20); 10 секунд есть
        // только у T2V-Lite и у Pro-версий (19 млрд параметров — другое железо).
        ("6f1a45e0-0d31-4c65-9a01-000000000032", "Kandinsky-5.0-I2V-Lite-5s",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "kandinsky5lite_i2v_5s",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000032.json",
              "params": {
                "width": 768,
                "height": 512,
                "length": 121,
                "steps": 50,
                "negative": "",
                "timeoutMinutes": 180
              },
              "install": {
                "group": "Kandinsky-5",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "kandinsky5lite_i2v_5s.safetensors",
                    "url": "https://huggingface.co/kandinskylab/Kandinsky-5.0-I2V-Lite-5s/resolve/main/model/kandinsky5lite_i2v_5s.safetensors",
                    "size": 4573130528,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "qwen_2.5_vl_7b_fp8_scaled.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/HunyuanVideo_1.5_repackaged/resolve/main/split_files/text_encoders/qwen_2.5_vl_7b_fp8_scaled.safetensors",
                    "size": 9384670680,
                    "category": "text_encoders"
                  },
                  {
                    "name": "clip_l.safetensors",
                    "url": "https://huggingface.co/comfyanonymous/flux_text_encoders/resolve/main/clip_l.safetensors",
                    "size": 246144152,
                    "category": "text_encoders"
                  },
                  {
                    "name": "hunyuan_video_vae_bf16.safetensors",
                    "url": "https://huggingface.co/Kijai/HunyuanVideo_comfy/resolve/main/hunyuan_video_vae_bf16.safetensors",
                    "size": 492986478,
                    "category": "vae"
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": {
                  "kind": "workflow",
                  "node": "LoraLoaderModelOnly",
                  "placeholder": "{lora}",
                  "strengthPlaceholder": "{loraStrength}",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".safetensors"]
                },
                "train": {
                  "kind": "process",
                  "docUrl": "https://github.com/kohya-ss/musubi-tuner/blob/main/docs/kandinsky5.md",
                  "packages": ["musubi-tuner", "python", "qwen2.5-vl-7b", "clip-vit-large-patch14"],
                  "dataset": {
                    "kind": "dir",
                    "path": "lora/{object}/dataset",
                    "captions": "txt",
                    "minItems": 10,
                    "maxItems": 60,
                    "width": 768,
                    "height": 512,
                    "maxKb": 0,
                    "formats": ["png"]
                  },
                  "files": [
                    {
                      "path": "lora/{object}/dataset.toml",
                      "text": [
                        "# AI2P: written from the model profile before every training run.",
                        "# Edit it in the model profile, not here: it is overwritten each time.",
                        "[general]",
                        "resolution = [{width}, {height}]",
                        "caption_extension = \".txt\"",
                        "batch_size = 1",
                        "enable_bucket = true",
                        "bucket_no_upscale = false",
                        "",
                        "[[datasets]]",
                        "image_directory = '{dataset}'",
                        "cache_directory = '{output}\\cache'",
                        "num_repeats = 1"
                      ]
                    },
                    {
                      "path": "lora/{object}/train.cmd",
                      "text": [
                        "@echo off",
                        "rem AI2P: LoRA training for Kandinsky 5 I2V Lite via musubi-tuner.",
                        "rem Written from the model profile before every run - edit it in the model.",
                        "setlocal enableextensions",
                        "set \"TRAIN={package:musubi-tuner:kandinsky5_train_network.py}\"",
                        "for %%I in (\"%TRAIN%\") do set \"SCRIPTS=%%~dpI\"",
                        "set \"ROOT=%SCRIPTS%\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" set \"ROOT=%SCRIPTS%\"",
                        "set \"VENV=%ROOT%.venv\"",
                        "set \"PY=%VENV%\\Scripts\\python.exe\"",
                        "set \"OUT={output}\"",
                        "if not exist \"%OUT%\" mkdir \"%OUT%\"",
                        "if not exist \"%PY%\" call :setup",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching latents",
                        "\"%PY%\" \"%SCRIPTS%kandinsky5_cache_latents.py\" --dataset_config \"%~dp0dataset.toml\" --vae \"{model:hunyuan_video_vae_bf16.safetensors}\"",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching text encoder outputs",
                        "\"%PY%\" \"%SCRIPTS%kandinsky5_cache_text_encoder_outputs.py\" --dataset_config \"%~dp0dataset.toml\" --text_encoder_qwen \"{packageDir:qwen2.5-vl-7b}\" --text_encoder_clip \"{packageDir:clip-vit-large-patch14}\" --batch_size 1",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] training",
                        "\"%VENV%\\Scripts\\accelerate.exe\" launch --num_cpu_threads_per_process 1 --mixed_precision bf16 \"%SCRIPTS%kandinsky5_train_network.py\" --mixed_precision bf16 --dataset_config \"%~dp0dataset.toml\" --task k5-lite-i2v-5s-sd --dit \"{model:kandinsky5lite_i2v_5s.safetensors}\" --vae \"{model:hunyuan_video_vae_bf16.safetensors}\" --text_encoder_qwen \"{packageDir:qwen2.5-vl-7b}\" --text_encoder_clip \"{packageDir:clip-vit-large-patch14}\" --sdpa --fp8_base --gradient_checkpointing --max_data_loader_n_workers 1 --persistent_data_loader_workers --learning_rate 1e-4 --optimizer_type AdamW8Bit --optimizer_args \"weight_decay=0.001\" --max_grad_norm 1.0 --lr_scheduler constant_with_warmup --lr_warmup_steps 100 --network_module networks.lora_kandinsky --network_dim 32 --network_alpha 32 --timestep_sampling shift --discrete_flow_shift 5.0 --scheduler_scale 10.0 --max_train_steps {steps} --output_dir \"%OUT%\" --output_name {object} --seed 42",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] done",
                        "exit /b 0",
                        "",
                        ":setup",
                        "echo [AI2P] preparing the training environment",
                        "set \"PYEXE={package:python:python.exe}\"",
                        "if defined AI2P_LORA_PYTHON set \"PYEXE=%AI2P_LORA_PYTHON%\"",
                        "set \"TORCH_INDEX=https://download.pytorch.org/whl/cu124\"",
                        "if defined AI2P_LORA_TORCH_INDEX set \"TORCH_INDEX=%AI2P_LORA_TORCH_INDEX%\"",
                        "\"%PYEXE%\" -m venv \"%VENV%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot create the virtual environment. Reinstall the model (it brings Python 3.10-3.12), or point AI2P_LORA_PYTHON at your own Python.",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install --upgrade pip",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install torch torchvision --index-url %TORCH_INDEX%",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install -e \"%ROOT%.\"",
                        "if errorlevel 1 echo [AI2P] ERROR: musubi-tuner was not found in %ROOT% - reinstall the model (Settings - Models - Install), it brings the trainer.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0"
                      ]
                    }
                  ],
                  "start": {
                    "kind": "process",
                    "command": "train.cmd",
                    "workDir": "lora/{object}",
                    "steps": 2000
                  },
                  "wait": {
                    "kind": "process",
                    "timeoutMinutes": 720
                  },
                  "result": {
                    "kind": "file",
                    "path": "lora/{object}/out/{object}.safetensors",
                    "target": "loras/{object}.safetensors"
                  }
                }
              },
              "refImage": {
                "kind": "upload",
                "placeholder": "{image}",
                "uploadPath": "/upload/image",
                "field": "image",
                "maxCount": 1,
                "formats": ["image/png", "image/jpeg", "image/webp"],
                "required": true
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "kandinsky5lite_i2v_5s",
              "inputs":  ["text/plain", "image/*"],
              "outputs": ["video/*"],
              "skills":  [
                { "name": "video-animate",  "score": 78 },
                { "name": "video-generate", "score": 74 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // ── Локальные модели ИЗОБРАЖЕНИЙ через ComfyUI (T-241, отчёт T-213 §6.2) ─────────
        // До этого выпуска навыки image-* не закрывала ни одна запись справочника: медиа были
        // только видео (Kandinsky). Все три записи ниже подключаются БЕЗ единой строки кода —
        // коннектор comfyui уже умеет и graph, и загрузку стартовой картинки, и забор
        // результата (он ищет в outputs любые объекты с полем filename, поэтому SaveImage
        // работает так же, как SaveVideo). Отличия от видео-записей: нет параметра length,
        // а размеры файлов сняты HEAD-запросами к HuggingFace 2026-08-27 (заголовок
        // X-Linked-Size — Content-Length после 302 на CDN врёт).
        //
        // Z-Image Turbo (Tongyi-MAI, Apache 2.0): самая лёгкая из трёх — 6B, восемь шагов,
        // одна картинка за секунды. Workflow сконвертирован из официального шаблона
        // Comfy-Org image_z_image_turbo (подграф «Text to Image (Z-Image-Turbo)»): негатив
        // у turbo-режима не используется вовсе (cfg = 1), поэтому отрицательное условие
        // делается ConditioningZeroOut, как в шаблоне, а не вторым CLIPTextEncode.
        ("6f1a45e0-0d31-4c65-9a01-000000000033", "Z-Image-Turbo",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "z_image_turbo",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000033.json",
              "params": {
                "width": 1024,
                "height": 1024,
                "length": 1,
                "steps": 8,
                "negative": "",
                "timeoutMinutes": 60
              },
              "install": {
                "group": "Z-Image",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "z_image_turbo_bf16.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/z_image_turbo/resolve/main/split_files/diffusion_models/z_image_turbo_bf16.safetensors",
                    "size": 12309866400,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "qwen_3_4b.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/z_image_turbo/resolve/main/split_files/text_encoders/qwen_3_4b.safetensors",
                    "size": 8044982048,
                    "category": "text_encoders"
                  },
                  {
                    "name": "ae.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/z_image_turbo/resolve/main/split_files/vae/ae.safetensors",
                    "size": 335304388,
                    "category": "vae"
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": {
                  "kind": "workflow",
                  "node": "LoraLoaderModelOnly",
                  "placeholder": "{lora}",
                  "strengthPlaceholder": "{loraStrength}",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".safetensors"]
                },
                "train": {
                  "kind": "process",
                  "docUrl": "https://github.com/kohya-ss/musubi-tuner/blob/main/docs/zimage.md",
                  "packages": ["musubi-tuner", "python"],
                  "dataset": {
                    "kind": "dir",
                    "path": "lora/{object}/dataset",
                    "captions": "txt",
                    "minItems": 10,
                    "maxItems": 60,
                    "width": 1024,
                    "height": 1024,
                    "maxKb": 0,
                    "formats": ["png"]
                  },
                  "files": [
                    {
                      "path": "lora/{object}/dataset.toml",
                      "text": [
                        "# AI2P: written from the model profile before every training run.",
                        "# Edit it in the model profile, not here: it is overwritten each time.",
                        "[general]",
                        "resolution = [{width}, {height}]",
                        "caption_extension = \".txt\"",
                        "batch_size = 1",
                        "enable_bucket = true",
                        "bucket_no_upscale = false",
                        "",
                        "[[datasets]]",
                        "image_directory = '{dataset}'",
                        "cache_directory = '{output}\\cache'",
                        "num_repeats = 1"
                      ]
                    },
                    {
                      "path": "lora/{object}/train.cmd",
                      "text": [
                        "@echo off",
                        "rem AI2P: LoRA training for Z-Image via musubi-tuner (zimage_train_network.py).",
                        "rem Written from the model profile before every run - edit it in the model.",
                        "setlocal enableextensions",
                        "set \"TRAIN={package:musubi-tuner:zimage_train_network.py}\"",
                        "for %%I in (\"%TRAIN%\") do set \"SCRIPTS=%%~dpI\"",
                        "set \"ROOT=%SCRIPTS%\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" set \"ROOT=%SCRIPTS%\"",
                        "set \"VENV=%ROOT%.venv\"",
                        "set \"PY=%VENV%\\Scripts\\python.exe\"",
                        "set \"OUT={output}\"",
                        "set \"BASE={groupDir}\\train\"",
                        "set \"DIT=%BASE%\\split_files\\diffusion_models\\z_image_bf16.safetensors\"",
                        "if not exist \"%OUT%\" mkdir \"%OUT%\"",
                        "if not exist \"%PY%\" call :setup",
                        "if errorlevel 1 exit /b 1",
                        "rem Turbo weights are distilled: musubi-tuner trains on the BASE checkpoint,",
                        "rem so it is fetched once (about 12 GB) next to the installed model.",
                        "if not exist \"%DIT%\" call :base",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching latents",
                        "\"%PY%\" \"%SCRIPTS%zimage_cache_latents.py\" --dataset_config \"%~dp0dataset.toml\" --vae \"{model:ae.safetensors}\"",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching text encoder outputs",
                        "\"%PY%\" \"%SCRIPTS%zimage_cache_text_encoder_outputs.py\" --dataset_config \"%~dp0dataset.toml\" --text_encoder \"{model:qwen_3_4b.safetensors}\" --batch_size 1",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] training",
                        "\"%VENV%\\Scripts\\accelerate.exe\" launch --num_cpu_threads_per_process 1 --mixed_precision bf16 \"%SCRIPTS%zimage_train_network.py\" --dit \"%DIT%\" --vae \"{model:ae.safetensors}\" --text_encoder \"{model:qwen_3_4b.safetensors}\" --dataset_config \"%~dp0dataset.toml\" --sdpa --mixed_precision bf16 --timestep_sampling shift --weighting_scheme none --discrete_flow_shift 2.0 --optimizer_type adamw8bit --learning_rate 1e-4 --gradient_checkpointing --max_data_loader_n_workers 2 --persistent_data_loader_workers --network_module networks.lora_zimage --network_dim 32 --max_train_steps {steps} --save_every_n_epochs 1 --seed 42 --output_dir \"%OUT%\" --output_name {object}",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] done",
                        "exit /b 0",
                        "",
                        ":setup",
                        "echo [AI2P] preparing the training environment",
                        "set \"PYEXE={package:python:python.exe}\"",
                        "if defined AI2P_LORA_PYTHON set \"PYEXE=%AI2P_LORA_PYTHON%\"",
                        "set \"TORCH_INDEX=https://download.pytorch.org/whl/cu124\"",
                        "if defined AI2P_LORA_TORCH_INDEX set \"TORCH_INDEX=%AI2P_LORA_TORCH_INDEX%\"",
                        "\"%PYEXE%\" -m venv \"%VENV%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot create the virtual environment. Reinstall the model (it brings Python 3.10-3.12), or point AI2P_LORA_PYTHON at your own Python.",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install --upgrade pip",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install torch torchvision --index-url %TORCH_INDEX%",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install -e \"%ROOT%.\"",
                        "if errorlevel 1 echo [AI2P] ERROR: musubi-tuner was not found in %ROOT% - reinstall the model (Settings - Models - Install), it brings the trainer.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0",
                        "",
                        ":base",
                        "echo [AI2P] fetching the base checkpoint for training (about 12 GB, once)",
                        "\"%PY%\" -m pip install --upgrade huggingface_hub",
                        "if errorlevel 1 exit /b 1",
                        "\"%VENV%\\Scripts\\hf.exe\" download Comfy-Org/z_image split_files/diffusion_models/z_image_bf16.safetensors --local-dir \"%BASE%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot download the base checkpoint. Check the network, or put z_image_bf16.safetensors under the path above by hand.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0"
                      ]
                    }
                  ],
                  "start": {
                    "kind": "process",
                    "command": "train.cmd",
                    "workDir": "lora/{object}",
                    "steps": 2000
                  },
                  "wait": {
                    "kind": "process",
                    "timeoutMinutes": 720
                  },
                  "result": {
                    "kind": "file",
                    "path": "lora/{object}/out/{object}.safetensors",
                    "target": "loras/{object}.safetensors"
                  }
                }
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "z_image_turbo",
              "inputs":  ["text/plain"],
              "outputs": ["image/*"],
              "skills":  [
                { "name": "image-generate", "score": 85 },
                { "name": "image-photo",    "score": 84 },
                { "name": "image-concept",  "score": 82 },
                { "name": "image-text",     "score": 78 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // Qwen-Image 2512 (Alibaba, Apache 2.0): 20B, лучшая из открытых по ТЕКСТУ В КАДРЕ
        // (вывески, подписи, вёрстка), в том числе кириллицей. Workflow сконвертирован из
        // официального шаблона Comfy-Org image_qwen_image (подграф «Text to Image
        // (Qwen-Image)»), взят БАЗОВЫЙ режим шаблона (steps 20, cfg 4), а не turbo-ветка:
        // она требует отдельного файла Lightning-LoRA, которого в манифесте нет.
        // Текстовый энкодер тот же файл, что у записей Kandinsky (qwen_2.5_vl_7b_fp8_scaled),
        // но группа установки своя — группы в репозитории моделей не пересекаются.
        ("6f1a45e0-0d31-4c65-9a01-000000000034", "Qwen-Image-2512",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "qwen_image_2512",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000034.json",
              "params": {
                "width": 1328,
                "height": 1328,
                "length": 1,
                "steps": 20,
                "negative": "",
                "timeoutMinutes": 90
              },
              "install": {
                "group": "Qwen-Image",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "qwen_image_2512_fp8_e4m3fn.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/Qwen-Image_ComfyUI/resolve/main/split_files/diffusion_models/qwen_image_2512_fp8_e4m3fn.safetensors",
                    "size": 20430679144,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "qwen_2.5_vl_7b_fp8_scaled.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/Qwen-Image_ComfyUI/resolve/main/split_files/text_encoders/qwen_2.5_vl_7b_fp8_scaled.safetensors",
                    "size": 9384670680,
                    "category": "text_encoders"
                  },
                  {
                    "name": "qwen_image_vae.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/Qwen-Image_ComfyUI/resolve/main/split_files/vae/qwen_image_vae.safetensors",
                    "size": 253806246,
                    "category": "vae"
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": {
                  "kind": "workflow",
                  "node": "LoraLoaderModelOnly",
                  "placeholder": "{lora}",
                  "strengthPlaceholder": "{loraStrength}",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".safetensors"]
                },
                "train": {
                  "kind": "process",
                  "docUrl": "https://github.com/kohya-ss/musubi-tuner/blob/main/docs/qwen_image.md",
                  "packages": ["musubi-tuner", "python"],
                  "dataset": {
                    "kind": "dir",
                    "path": "lora/{object}/dataset",
                    "captions": "txt",
                    "minItems": 10,
                    "maxItems": 60,
                    "width": 1328,
                    "height": 1328,
                    "maxKb": 0,
                    "formats": ["png"]
                  },
                  "files": [
                    {
                      "path": "lora/{object}/dataset.toml",
                      "text": [
                        "# AI2P: written from the model profile before every training run.",
                        "# Edit it in the model profile, not here: it is overwritten each time.",
                        "[general]",
                        "resolution = [{width}, {height}]",
                        "caption_extension = \".txt\"",
                        "batch_size = 1",
                        "enable_bucket = true",
                        "bucket_no_upscale = false",
                        "",
                        "[[datasets]]",
                        "image_directory = '{dataset}'",
                        "cache_directory = '{output}\\cache'",
                        "num_repeats = 1"
                      ]
                    },
                    {
                      "path": "lora/{object}/train.cmd",
                      "text": [
                        "@echo off",
                        "rem AI2P: LoRA training for Qwen-Image via musubi-tuner (qwen_image_train_network.py).",
                        "rem Written from the model profile before every run - edit it in the model.",
                        "setlocal enableextensions",
                        "set \"TRAIN={package:musubi-tuner:qwen_image_train_network.py}\"",
                        "for %%I in (\"%TRAIN%\") do set \"SCRIPTS=%%~dpI\"",
                        "set \"ROOT=%SCRIPTS%\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" set \"ROOT=%SCRIPTS%\"",
                        "set \"VENV=%ROOT%.venv\"",
                        "set \"PY=%VENV%\\Scripts\\python.exe\"",
                        "set \"OUT={output}\"",
                        "set \"BASE={groupDir}\\train\"",
                        "set \"DIT=%BASE%\\split_files\\diffusion_models\\qwen_image_2512_bf16.safetensors\"",
                        "set \"TE=%BASE%\\split_files\\text_encoders\\qwen_2.5_vl_7b.safetensors\"",
                        "if not exist \"%OUT%\" mkdir \"%OUT%\"",
                        "if not exist \"%PY%\" call :setup",
                        "if errorlevel 1 exit /b 1",
                        "rem musubi-tuner does NOT accept the fp8 files used for generation:",
                        "rem the bf16 pair is fetched once (about 57 GB) next to the installed model.",
                        "if not exist \"%DIT%\" call :weights",
                        "if errorlevel 1 exit /b 1",
                        "if not exist \"%TE%\" call :weights",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching latents",
                        "\"%PY%\" \"%SCRIPTS%qwen_image_cache_latents.py\" --dataset_config \"%~dp0dataset.toml\" --vae \"{model:qwen_image_vae.safetensors}\" --model_version original",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching text encoder outputs",
                        "\"%PY%\" \"%SCRIPTS%qwen_image_cache_text_encoder_outputs.py\" --dataset_config \"%~dp0dataset.toml\" --text_encoder \"%TE%\" --batch_size 1 --model_version original",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] training",
                        "\"%VENV%\\Scripts\\accelerate.exe\" launch --num_cpu_threads_per_process 1 --mixed_precision bf16 \"%SCRIPTS%qwen_image_train_network.py\" --dit \"%DIT%\" --vae \"{model:qwen_image_vae.safetensors}\" --text_encoder \"%TE%\" --model_version original --dataset_config \"%~dp0dataset.toml\" --sdpa --mixed_precision bf16 --timestep_sampling shift --weighting_scheme none --discrete_flow_shift 2.2 --optimizer_type adamw8bit --learning_rate 5e-5 --gradient_checkpointing --max_data_loader_n_workers 2 --persistent_data_loader_workers --network_module networks.lora_qwen_image --network_dim 16 --max_train_steps {steps} --save_every_n_epochs 1 --seed 42 --output_dir \"%OUT%\" --output_name {object}",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] done",
                        "exit /b 0",
                        "",
                        ":setup",
                        "echo [AI2P] preparing the training environment",
                        "set \"PYEXE={package:python:python.exe}\"",
                        "if defined AI2P_LORA_PYTHON set \"PYEXE=%AI2P_LORA_PYTHON%\"",
                        "set \"TORCH_INDEX=https://download.pytorch.org/whl/cu124\"",
                        "if defined AI2P_LORA_TORCH_INDEX set \"TORCH_INDEX=%AI2P_LORA_TORCH_INDEX%\"",
                        "\"%PYEXE%\" -m venv \"%VENV%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot create the virtual environment. Reinstall the model (it brings Python 3.10-3.12), or point AI2P_LORA_PYTHON at your own Python.",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install --upgrade pip",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install torch torchvision --index-url %TORCH_INDEX%",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install -e \"%ROOT%.\"",
                        "if errorlevel 1 echo [AI2P] ERROR: musubi-tuner was not found in %ROOT% - reinstall the model (Settings - Models - Install), it brings the trainer.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0",
                        "",
                        ":weights",
                        "echo [AI2P] fetching bf16 weights for training (about 57 GB, once)",
                        "\"%PY%\" -m pip install --upgrade huggingface_hub",
                        "if errorlevel 1 exit /b 1",
                        "\"%VENV%\\Scripts\\hf.exe\" download Comfy-Org/Qwen-Image_ComfyUI split_files/diffusion_models/qwen_image_2512_bf16.safetensors split_files/text_encoders/qwen_2.5_vl_7b.safetensors --local-dir \"%BASE%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot download the training weights. Check the network, or put the two files under the path above by hand.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0"
                      ]
                    }
                  ],
                  "start": {
                    "kind": "process",
                    "command": "train.cmd",
                    "workDir": "lora/{object}",
                    "steps": 2000
                  },
                  "wait": {
                    "kind": "process",
                    "timeoutMinutes": 720
                  },
                  "result": {
                    "kind": "file",
                    "path": "lora/{object}/out/{object}.safetensors",
                    "target": "loras/{object}.safetensors"
                  }
                }
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "qwen_image_2512",
              "inputs":  ["text/plain"],
              "outputs": ["image/*"],
              "skills":  [
                { "name": "image-text",     "score": 94 },
                { "name": "image-photo",    "score": 91 },
                { "name": "image-generate", "score": 90 },
                { "name": "image-concept",  "score": 85 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // Qwen-Image-Edit 2511 (Alibaba, Apache 2.0): та же архитектура, но ПРАВКА картинки
        // по указанию — единственная запись справочника, закрывающая image-edit и
        // image-inpaint. Стартовый кадр приходит тем же путём, что у Kandinsky I2V
        // (refImage.kind = upload, плейсхолдер {image} в узле LoadImage): коннектор заливает
        // файл POST /upload/image и подставляет сюда его имя. Workflow сконвертирован из
        // шаблона Comfy-Org image_qwen_image_edit_2511 (базовый режим, steps 40, cfg 3);
        // узлы FluxKontextMultiReferenceLatentMethod из шаблона НЕ перенесены осознанно —
        // его же примечание говорит, что они нужны только для чужих перепаковок весов.
        ("6f1a45e0-0d31-4c65-9a01-000000000035", "Qwen-Image-Edit-2511",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "qwen_image_edit_2511",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000035.json",
              "params": {
                "width": 1328,
                "height": 1328,
                "length": 1,
                "steps": 40,
                "negative": "",
                "timeoutMinutes": 90
              },
              "install": {
                "group": "Qwen-Image",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "qwen_image_edit_2511_fp8mixed.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/Qwen-Image-Edit_ComfyUI/resolve/main/split_files/diffusion_models/qwen_image_edit_2511_fp8mixed.safetensors",
                    "size": 20533762817,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "qwen_2.5_vl_7b_fp8_scaled.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/Qwen-Image_ComfyUI/resolve/main/split_files/text_encoders/qwen_2.5_vl_7b_fp8_scaled.safetensors",
                    "size": 9384670680,
                    "category": "text_encoders"
                  },
                  {
                    "name": "qwen_image_vae.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/Qwen-Image_ComfyUI/resolve/main/split_files/vae/qwen_image_vae.safetensors",
                    "size": 253806246,
                    "category": "vae"
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": {
                  "kind": "workflow",
                  "node": "LoraLoaderModelOnly",
                  "placeholder": "{lora}",
                  "strengthPlaceholder": "{loraStrength}",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".safetensors"]
                },
                "train": {
                  "kind": "process",
                  "docUrl": "https://github.com/kohya-ss/musubi-tuner/blob/main/docs/qwen_image.md",
                  "packages": ["musubi-tuner", "python"],
                  "dataset": {
                    "kind": "dir",
                    "path": "lora/{object}/dataset",
                    "captions": "txt",
                    "minItems": 10,
                    "maxItems": 60,
                    "width": 1328,
                    "height": 1328,
                    "maxKb": 0,
                    "formats": ["png"]
                  },
                  "files": [
                    {
                      "path": "lora/{object}/dataset.toml",
                      "text": [
                        "# AI2P: written from the model profile before every training run.",
                        "# Edit it in the model profile, not here: it is overwritten each time.",
                        "[general]",
                        "resolution = [{width}, {height}]",
                        "caption_extension = \".txt\"",
                        "batch_size = 1",
                        "enable_bucket = true",
                        "bucket_no_upscale = false",
                        "",
                        "[[datasets]]",
                        "image_directory = '{dataset}'",
                        "cache_directory = '{output}\\cache'",
                        "num_repeats = 1"
                      ]
                    },
                    {
                      "path": "lora/{object}/train.cmd",
                      "text": [
                        "@echo off",
                        "rem AI2P: LoRA training for Qwen-Image-Edit-2511 via musubi-tuner.",
                        "rem Written from the model profile before every run - edit it in the model.",
                        "setlocal enableextensions",
                        "set \"TRAIN={package:musubi-tuner:qwen_image_train_network.py}\"",
                        "for %%I in (\"%TRAIN%\") do set \"SCRIPTS=%%~dpI\"",
                        "set \"ROOT=%SCRIPTS%\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" set \"ROOT=%SCRIPTS%\"",
                        "set \"VENV=%ROOT%.venv\"",
                        "set \"PY=%VENV%\\Scripts\\python.exe\"",
                        "set \"OUT={output}\"",
                        "set \"BASE={groupDir}\\train\"",
                        "set \"DIT=%BASE%\\split_files\\diffusion_models\\qwen_image_edit_2511_bf16.safetensors\"",
                        "set \"TE=%BASE%\\split_files\\text_encoders\\qwen_2.5_vl_7b.safetensors\"",
                        "if not exist \"%OUT%\" mkdir \"%OUT%\"",
                        "if not exist \"%PY%\" call :setup",
                        "if errorlevel 1 exit /b 1",
                        "rem musubi-tuner does NOT accept the fp8 files used for generation:",
                        "rem the bf16 pair is fetched once (about 57 GB) next to the installed model.",
                        "if not exist \"%DIT%\" call :weights",
                        "if errorlevel 1 exit /b 1",
                        "if not exist \"%TE%\" call :weights",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching latents",
                        "\"%PY%\" \"%SCRIPTS%qwen_image_cache_latents.py\" --dataset_config \"%~dp0dataset.toml\" --vae \"{model:qwen_image_vae.safetensors}\" --model_version edit-2511",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching text encoder outputs",
                        "\"%PY%\" \"%SCRIPTS%qwen_image_cache_text_encoder_outputs.py\" --dataset_config \"%~dp0dataset.toml\" --text_encoder \"%TE%\" --batch_size 1 --model_version edit-2511",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] training",
                        "\"%VENV%\\Scripts\\accelerate.exe\" launch --num_cpu_threads_per_process 1 --mixed_precision bf16 \"%SCRIPTS%qwen_image_train_network.py\" --dit \"%DIT%\" --vae \"{model:qwen_image_vae.safetensors}\" --text_encoder \"%TE%\" --model_version edit-2511 --dataset_config \"%~dp0dataset.toml\" --sdpa --mixed_precision bf16 --timestep_sampling shift --weighting_scheme none --discrete_flow_shift 2.2 --optimizer_type adamw8bit --learning_rate 5e-5 --gradient_checkpointing --max_data_loader_n_workers 2 --persistent_data_loader_workers --network_module networks.lora_qwen_image --network_dim 16 --max_train_steps {steps} --save_every_n_epochs 1 --seed 42 --output_dir \"%OUT%\" --output_name {object}",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] done",
                        "exit /b 0",
                        "",
                        ":setup",
                        "echo [AI2P] preparing the training environment",
                        "set \"PYEXE={package:python:python.exe}\"",
                        "if defined AI2P_LORA_PYTHON set \"PYEXE=%AI2P_LORA_PYTHON%\"",
                        "set \"TORCH_INDEX=https://download.pytorch.org/whl/cu124\"",
                        "if defined AI2P_LORA_TORCH_INDEX set \"TORCH_INDEX=%AI2P_LORA_TORCH_INDEX%\"",
                        "\"%PYEXE%\" -m venv \"%VENV%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot create the virtual environment. Reinstall the model (it brings Python 3.10-3.12), or point AI2P_LORA_PYTHON at your own Python.",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install --upgrade pip",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install torch torchvision --index-url %TORCH_INDEX%",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install -e \"%ROOT%.\"",
                        "if errorlevel 1 echo [AI2P] ERROR: musubi-tuner was not found in %ROOT% - reinstall the model (Settings - Models - Install), it brings the trainer.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0",
                        "",
                        ":weights",
                        "echo [AI2P] fetching bf16 weights for training (about 57 GB, once)",
                        "\"%PY%\" -m pip install --upgrade huggingface_hub",
                        "if errorlevel 1 exit /b 1",
                        "\"%VENV%\\Scripts\\hf.exe\" download Comfy-Org/Qwen-Image-Edit_ComfyUI split_files/diffusion_models/qwen_image_edit_2511_bf16.safetensors --local-dir \"%BASE%\"",
                        "if errorlevel 1 exit /b 1",
                        "\"%VENV%\\Scripts\\hf.exe\" download Comfy-Org/Qwen-Image_ComfyUI split_files/text_encoders/qwen_2.5_vl_7b.safetensors --local-dir \"%BASE%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot download the training weights. Check the network, or put the two files under the path above by hand.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0"
                      ]
                    }
                  ],
                  "start": {
                    "kind": "process",
                    "command": "train.cmd",
                    "workDir": "lora/{object}",
                    "steps": 2000
                  },
                  "wait": {
                    "kind": "process",
                    "timeoutMinutes": 720
                  },
                  "result": {
                    "kind": "file",
                    "path": "lora/{object}/out/{object}.safetensors",
                    "target": "loras/{object}.safetensors"
                  }
                }
              },
              "refImage": {
                "kind": "upload",
                "placeholder": "{image}",
                "uploadPath": "/upload/image",
                "field": "image",
                "maxCount": 1,
                "formats": ["image/png", "image/jpeg", "image/webp"],
                "required": true
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "qwen_image_edit_2511",
              "inputs":  ["text/plain", "image/*"],
              "outputs": ["image/*"],
              "skills":  [
                { "name": "image-edit",     "score": 90 },
                { "name": "image-text",     "score": 90 },
                { "name": "image-inpaint",  "score": 85 },
                { "name": "image-generate", "score": 82 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // ── ОБЛАЧНОЕ медиа через шлюз fal.ai (T-14-S0, отчёт T-213 §6.2–6.5) ────────────
        // До этого задания всё облачное медиа не подключалось ВОВСЕ: своего
        // OpenAI-совместимого входа у Seedance, Veo, Kling, GPT Image, Nano Banana,
        // ElevenLabs, Tripo и Meshy нет, а у каждой свой протокол. Шлюз закрывает их одним
        // протоколом очереди и ОДНИМ ключом API (secretRef «fal.apiKey» общий у всех
        // записей ниже — как у моделей через OpenRouter, T-214).
        //
        // Общее для всех записей: тело запроса лежит в самом профайле (ключ "request",
        // T-14-S0) — набор полей у каждой модели свой, и это ровно то, что человек правит
        // в форме модели; значения полей сверены со схемой OpenAPI шлюза 27.08.2026
        // (test/t14s0/schemas.py), выдумывать их нельзя — незнакомое значение перечисления
        // шлюз отвергает с HTTP 422. LoRA у всех — честное "supported": false с причиной
        // "provider": обучения адаптера облачные медиа-модели не дают вовсе (у Meshy и Tripo
        // «свой стиль» делается их собственными подписками, а не файлом весов).
        // Цена: где провайдер считает ТОКЕНЫ, стоят настоящие in_per_1m/out_per_1m; где счёт
        // идёт за картинку, секунду или минуту — per_unit + unit (подбор исполнителя
        // per_unit пока не читает, коннектор пишет тариф словами в сводку задания).
        //
        // Seedance 2.5 (ByteDance) — верх рынка видео на 17.08.2026: до 30 секунд со звуком
        // в один проход. Запись t2v; «оживление картинки» — отдельная запись ниже, иначе
        // проверка форматов (T-257) отдала бы i2v-задачу модели, которой картинку не передать.
        ("6f1a45e0-0d31-4c65-9a01-000000000036", "Seedance-2.5",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "bytedance/seedance-2.5/text-to-video",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 60
              },
              "request": {
                "prompt": "{prompt}",
                "duration": "5",
                "resolution": "720p",
                "aspect_ratio": "16:9",
                "generate_audio": true
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "bytedance/seedance-2.5/text-to-video",
              "inputs":  ["text/plain"],
              "outputs": ["video/mp4"],
              "skills":  [
                { "name": "video-generate", "score": 97 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 21.4,
                           "per_unit": 0.473, "unit": "second" }
            }
            """),
        // Та же Seedance 2.5, «изображение → видео»: стартовый кадр уходит В САМОМ ЗАПРОСЕ
        // адресом data: (своего хранилища файлов у нас нет, а публиковать кадр наружу
        // ссылкой мы не вправе) — отсюда refImage вида "request-field".
        ("6f1a45e0-0d31-4c65-9a01-000000000037", "Seedance-2.5-I2V",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "bytedance/seedance-2.5/image-to-video",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 60
              },
              "request": {
                "prompt": "{prompt}",
                "image_url": "{image}",
                "duration": "5",
                "resolution": "720p",
                "generate_audio": true
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "request-field",
                "placeholder": "{image}",
                "field": "image_url",
                "maxCount": 1,
                "formats": ["image/png", "image/jpeg", "image/webp"],
                "required": true
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "bytedance/seedance-2.5/image-to-video",
              "inputs":  ["text/plain", "image/*"],
              "outputs": ["video/mp4"],
              "skills":  [
                { "name": "video-animate", "score": 95 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 21.4,
                           "per_unit": 0.473, "unit": "second" }
            }
            """),
        // Gemini Omni Flash (Google, 30.06.2026): принимает текст+картинку+звук+видео и
        // правит ГОТОВОЕ видео обычными фразами. Заведён режим «текст → видео»: правка
        // (endpoint /edit) требует видео на входе, а передавать его коннектору пока нечем.
        ("6f1a45e0-0d31-4c65-9a01-000000000038", "Gemini-Omni-Flash",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "google/gemini-omni-flash",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 60
              },
              "request": {
                "prompt": "{prompt}",
                "duration": 8,
                "aspect_ratio": "16:9"
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "google/gemini-omni-flash",
              "inputs":  ["text/plain"],
              "outputs": ["video/mp4"],
              "skills":  [
                { "name": "video-generate", "score": 91 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 21.875,
                           "per_unit": 0.125, "unit": "second" }
            }
            """),
        // Veo 3.1 (Google): звук в кадре, 4/6/8 секунд, до 4K. Единственная из видео-записей,
        // принимающая negative_prompt, — поэтому params.negative у неё осмысленно.
        ("6f1a45e0-0d31-4c65-9a01-000000000039", "Veo-3.1",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "fal-ai/veo3.1",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "negative": "",
                "timeoutMinutes": 60
              },
              "request": {
                "prompt": "{prompt}",
                "negative_prompt": "{negative}",
                "duration": "8s",
                "resolution": "720p",
                "aspect_ratio": "16:9",
                "generate_audio": true,
                "seed": "{seed}"
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "fal-ai/veo3.1",
              "inputs":  ["text/plain"],
              "outputs": ["video/mp4"],
              "skills":  [
                { "name": "video-generate", "score": 93 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0,
                           "per_unit": 0.4, "unit": "second" }
            }
            """),
        // Kling 3.0 Pro (Kuaishou) — лучший в отчёте «оживлятель картинки» (video-animate 93),
        // родной звук и голос. Поле стартового кадра у него называется start_image_url.
        ("6f1a45e0-0d31-4c65-9a01-000000000040", "Kling-3.0",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "fal-ai/kling-video/v3/pro/image-to-video",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "negative": "blur, distort, and low quality",
                "timeoutMinutes": 60
              },
              "request": {
                "prompt": "{prompt}",
                "start_image_url": "{image}",
                "negative_prompt": "{negative}",
                "duration": "5",
                "cfg_scale": 0.5,
                "generate_audio": true
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "request-field",
                "placeholder": "{image}",
                "field": "start_image_url",
                "maxCount": 1,
                "formats": ["image/png", "image/jpeg", "image/webp"],
                "required": true
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "fal-ai/kling-video/v3/pro/image-to-video",
              "inputs":  ["text/plain", "image/*"],
              "outputs": ["video/mp4"],
              "skills":  [
                { "name": "video-animate", "score": 93 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0,
                           "per_unit": 0.168, "unit": "second" }
            }
            """),
        // Nano Banana Pro (Gemini 3 Pro Image) — первое место §6.2 по генерации и по тексту
        // в кадре, до 4096×4096. Запись «текст → картинка»; правка — отдельной записью ниже.
        ("6f1a45e0-0d31-4c65-9a01-000000000041", "Nano-Banana-Pro",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "fal-ai/nano-banana-pro",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 30
              },
              "request": {
                "prompt": "{prompt}",
                "num_images": 1,
                "output_format": "png",
                "resolution": "1K",
                "aspect_ratio": "1:1",
                "seed": "{seed}"
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "fal-ai/nano-banana-pro",
              "inputs":  ["text/plain"],
              "outputs": ["image/png"],
              "skills":  [
                { "name": "image-text",     "score": 98 },
                { "name": "image-generate", "score": 97 },
                { "name": "image-photo",    "score": 96 },
                { "name": "image-concept",  "score": 92 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0,
                           "per_unit": 0.15, "unit": "image" }
            }
            """),
        // Nano Banana Pro, правка картинки (image-edit 98 — первое место §6.2). Маски у него
        // нет вовсе: и правка целиком, и локальная правка задаются ФРАЗОЙ, поэтому навык
        // image-inpaint объявлен той же записью и с меньшей оценкой — отдельного замера
        // «по маске» в отчёте T-213 нет, и выдавать его за измеренный нельзя.
        ("6f1a45e0-0d31-4c65-9a01-000000000042", "Nano-Banana-Pro-Edit",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "fal-ai/nano-banana-pro/edit",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 30
              },
              "request": {
                "prompt": "{prompt}",
                "image_urls": ["{image}"],
                "num_images": 1,
                "output_format": "png",
                "resolution": "1K",
                "seed": "{seed}"
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "request-field",
                "placeholder": "{image}",
                "field": "image_urls",
                "maxCount": 1,
                "formats": ["image/png", "image/jpeg", "image/webp"],
                "required": true
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "fal-ai/nano-banana-pro/edit",
              "inputs":  ["text/plain", "image/*"],
              "outputs": ["image/png"],
              "skills":  [
                { "name": "image-edit",    "score": 98 },
                { "name": "image-inpaint", "score": 90 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0,
                           "per_unit": 0.15, "unit": "image" }
            }
            """),
        // GPT Image 2 (OpenAI) — первое место в обеих аренах Artificial Analysis. Единственная
        // из картиночных записей, у которой провайдер считает ТОКЕНЫ, а не картинки: текст
        // $5/$10 за 1M, картинка $8/$30 за 1M. В in_per_1m/out_per_1m стоят те цены, по
        // которым идёт основной счёт (текст на входе и картинка на выходе).
        ("6f1a45e0-0d31-4c65-9a01-000000000043", "GPT-Image-2",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "openai/gpt-image-2",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 30
              },
              "request": {
                "prompt": "{prompt}",
                "num_images": 1,
                "output_format": "png",
                "quality": "high",
                "image_size": "square_hd"
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "openai/gpt-image-2",
              "inputs":  ["text/plain"],
              "outputs": ["image/png"],
              "skills":  [
                { "name": "image-generate", "score": 96 },
                { "name": "image-text",     "score": 95 },
                { "name": "image-photo",    "score": 94 },
                { "name": "image-concept",  "score": 90 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 5.0, "out_per_1m": 30.0 }
            }
            """),
        // ElevenLabs Music (§6.4 отчёта — «ElevenLabs Music V2»): у шлюза версия в
        // идентификатор не вынесена, эндпойнт один — fal-ai/elevenlabs/music. Ценность
        // не в оценке, а в правах: это «очищенная по правам» альтернатива Suno.
        // САМОГО Suno v5.5 у шлюза нет ни в каком виде (проверено 27.08.2026).
        ("6f1a45e0-0d31-4c65-9a01-000000000044", "ElevenLabs-Music",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "fal-ai/elevenlabs/music",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 30
              },
              "request": {
                "prompt": "{prompt}",
                "music_length_ms": 30000,
                "output_format": "mp3_44100_128"
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "fal-ai/elevenlabs/music",
              "inputs":  ["text/plain"],
              "outputs": ["audio/mpeg"],
              "skills":  [
                { "name": "audio-song",  "score": 89 },
                { "name": "audio-music", "score": 89 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0,
                           "per_unit": 0.6, "unit": "minute" }
            }
            """),
        // ElevenLabs v3 — лучший в отчёте синтез речи (audio-speech 96). Поле текста у него
        // называется text, а не prompt: коннектор кладёт описание задачи туда, куда указывает
        // шаблон запроса, — потому шаблон и лежит в справочнике, а не в коде.
        ("6f1a45e0-0d31-4c65-9a01-000000000045", "ElevenLabs-TTS-v3",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "fal-ai/elevenlabs/tts/eleven-v3",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 30
              },
              "request": {
                "text": "{prompt}",
                "voice": "Rachel",
                "stability": 0.5
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "fal-ai/elevenlabs/tts/eleven-v3",
              "inputs":  ["text/plain"],
              "outputs": ["audio/mpeg"],
              "skills":  [
                { "name": "audio-speech", "score": 96 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // Tripo H3.1 (§6.5, лучший 3d-image = 92): картинка → сетка с PBR-текстурами.
        // Промпта эта модель не принимает вовсе — описание задачи ей не достаётся, и
        // поэтому в inputs объявлена только картинка: иначе проверка форматов (T-257)
        // отдала бы ей задачу «сделай модель по описанию», которую она прочитать не может.
        ("6f1a45e0-0d31-4c65-9a01-000000000046", "Tripo-H3.1",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "tripo3d/h3.1/image-to-3d",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 30
              },
              "request": {
                "image_url": "{image}",
                "texture": true,
                "pbr": true,
                "texture_quality": "standard",
                "geometry_quality": "standard",
                "quad": false
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "request-field",
                "placeholder": "{image}",
                "field": "image_url",
                "maxCount": 1,
                "formats": ["image/png", "image/jpeg", "image/webp"],
                "required": true
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "tripo3d/h3.1/image-to-3d",
              "inputs":  ["image/*"],
              "outputs": ["model/glb"],
              "skills":  [
                { "name": "3d-image", "score": 92 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0,
                           "per_unit": 0.3, "unit": "model" }
            }
            """),
        // Meshy V7 (§6.5 мерил ещё Meshy 6): текст → готовая к игре сетка с квад-топологией
        // и PBR. Оценка 3d-generate перенесена из отчёта по шестой версии и служит нижней
        // границей: седьмая вышла 24.08.2026, после отчёта, и отдельно не мерилась.
        ("6f1a45e0-0d31-4c65-9a01-000000000047", "Meshy-7",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "meshy/v7/text-to-3d",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 60
              },
              "request": {
                "prompt": "{prompt}",
                "mode": "full",
                "topology": "quad",
                "target_polycount": 30000,
                "enable_pbr": true,
                "seed": "{seed}"
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "meshy/v7/text-to-3d",
              "inputs":  ["text/plain"],
              "outputs": ["model/glb"],
              "skills":  [
                { "name": "3d-generate", "score": 87 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0,
                           "per_unit": 1.2, "unit": "model" }
            }
            """),
        // Локальная текстовая модель (ТЗ v1.42, todo36_5): раньше ставилась скриптом
        // install_local_model.ps1/.sh, теперь — встроенная запись справочника с манифестом:
        // пакет llama.cpp (llama-server) + один GGUF-файл ~20,6 ГиБ (размер снят с сервера
        // HF 2026-07-27). launchCommand собирается при установке из шаблона манифеста:
        // ключ -c 65536 (контекст) вместе с params.maxTokens 32768 — рабочая связка,
        // проверенная в todo_bugfix_2 (меньший контекст обрывал ответ по length).
        ("6f1a45e0-0d31-4c65-9a01-000000000008", "Qwen3.6-35B-A3B-Local",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "qwen3.6-35b-a3b",
              "baseUrl": "http://localhost:8080/v1",
              "secretRef": "",
              "launchCommand": "",
              "params": { "maxTokens": 32768 },
              "install": {
                "group": "Qwen3.6",
                "packages": ["llama.cpp"],
                "launchCommand": "\"{package:llama.cpp:llama-server.exe}\" -m \"{model:Qwen3.6-35B-A3B-UD-Q4_K_M.gguf}\" --n-cpu-moe 99 -ngl 99 -c 65536 --jinja --port 8080 --alias qwen3.6-35b-a3b",
                "files": [
                  {
                    "name": "Qwen3.6-35B-A3B-UD-Q4_K_M.gguf",
                    "url": "https://huggingface.co/unsloth/Qwen3.6-35B-A3B-GGUF/resolve/main/Qwen3.6-35B-A3B-UD-Q4_K_M.gguf",
                    "size": 22134528992
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "llama-cpp",
                "apply": {
                  "kind": "launch-arg",
                  "field": "--lora-scaled",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".gguf"]
                },
                "train": {
                  "kind": "external",
                  "docUrl": "https://github.com/ggml-org/llama.cpp/discussions/10123",
                  "dataset": { "kind": "none" },
                  "start": { "kind": "none" },
                  "wait": { "kind": "none" },
                  "result": {
                    "kind": "file",
                    "target": "loras/{object}.gguf"
                  }
                }
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "qwen3.6-35b-a3b",
              "inputs":  ["text/markdown", "text/plain", "text/source-code"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 80 },
                { "name": "code-review",          "score": 77 },
                { "name": "code-debug",           "score": 76 },
                { "name": "code-test",            "score": 75 },
                { "name": "code-refactor",        "score": 76 },
                { "name": "text-write",           "score": 78 },
                { "name": "text-docs",            "score": 77 },
                { "name": "text-edit",            "score": 76 },
                { "name": "text-translate",       "score": 82 },
                { "name": "text-summarize",       "score": 81 },
                { "name": "analyze-requirements", "score": 76 },
                { "name": "analyze-plan",         "score": 75 },
                { "name": "analyze-data",         "score": 74 }
              ],
              "limits":  { "context": 65536, "max_output": 32768 },
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // --- T-214: пополнение справочника по таблицам §5 отчёта T-213 (2026-08-17).
        // Оценки навыков — из таблиц §5.1 и §5.2 отчёта; id, контекст, цены и модальности
        // сверены с каталогом OpenRouter GET /api/v1/models 2026-08-17 (test/t214/or.json).
        // Живым вызовом провайдера ни один id не проверен — у моделей со СВОИМ API он взят
        // из слага каталога без префикса вендора; порядок сверки описан в документе модели.
        // Claude 5 (T-214): к Fable и Opus добавлены Sonnet 5 — рабочая лошадка по постоянной
        // цене $2/$10 — и Haiku 4.5, самый дешёвый Claude. У Sonnet, как у Fable и Opus, есть
        // двойник через подписку Claude Code (transport cli).
        ("6f1a45e0-0d31-4c65-9a01-000000000009", "Claude-Sonnet-5",
            """
            {
              "_seed": 22,
              "provider": "anthropic",
              "model": "claude-sonnet-5",
              "baseUrl": "",
              "secretRef": "anthropic.apiKey",
              "params": { "maxTokens": 16000, "effort": "high" },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "claude-sonnet-5",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 93 },
                { "name": "code-review",          "score": 92 },
                { "name": "code-debug",           "score": 91 },
                { "name": "code-test",            "score": 90 },
                { "name": "code-refactor",        "score": 92 },
                { "name": "text-write",           "score": 90 },
                { "name": "text-docs",            "score": 91 },
                { "name": "text-edit",            "score": 89 },
                { "name": "text-translate",       "score": 90 },
                { "name": "text-summarize",       "score": 92 },
                { "name": "analyze-requirements", "score": 91 },
                { "name": "analyze-plan",         "score": 90 },
                { "name": "analyze-data",         "score": 90 }
              ],
              "limits":  { "context": 1000000, "max_output": 128000 },
              "cost":    { "in_per_1m": 2.0, "out_per_1m": 10.0 }
            }
            """),
        ("6f1a45e0-0d31-4c65-9a01-000000000010", "Claude-Sonnet-5_cli",
            """
            {
              "_seed": 22,
              "provider": "anthropic",
              "transport": "cli",
              "cliCommand": "claude --permission-mode acceptEdits",
              "model": "claude-sonnet-5",
              "baseUrl": "",
              "secretRef": "",
              "params": {},
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "claude-sonnet-5",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 93 },
                { "name": "code-review",          "score": 92 },
                { "name": "code-debug",           "score": 91 },
                { "name": "code-test",            "score": 90 },
                { "name": "code-refactor",        "score": 92 },
                { "name": "text-write",           "score": 90 },
                { "name": "text-docs",            "score": 91 },
                { "name": "text-edit",            "score": 89 },
                { "name": "text-translate",       "score": 90 },
                { "name": "text-summarize",       "score": 92 },
                { "name": "analyze-requirements", "score": 91 },
                { "name": "analyze-plan",         "score": 90 },
                { "name": "analyze-data",         "score": 90 }
              ],
              "limits":  { "context": 1000000, "max_output": 128000 },
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // ВЕРСИИ CLI ОТДЕЛЬНЫМИ ЗАПИСЯМИ (T-359-S0). Переключение модели у Claude Code CLI
        // делается полем "model" профайла — оно уходит флагом --model. Команды «claude models»
        // у CLI нет, поэтому идентификаторы сверены живым запуском 24.09.2026
        // (claude -p --output-format json --model <id> «ok», смотрели modelUsage ответа):
        //   fable  → claude-fable-5-1     claude-fable-5   → claude-fable-5
        //   opus   → claude-opus-5        claude-sonnet-5  → claude-sonnet-5
        //   sonnet → claude-sonnet-5      claude-haiku-4-5 → claude-haiku-4-5
        //   haiku  → claude-haiku-4-5-20251001
        // Подмены модели не было ни разу: неизвестный id даёт код возврата 1 и
        // «[claude-code:unrecognized_model]», а не молчаливую замену. Полное имя надёжнее
        // алиаса: алиас «fable» уже означает 5.1, и с выходом 5.2 он молча переедет.
        // Записи версий СОСУЩЕСТВУЮТ: старые не гасим — человек выбирает исполнителю нужную.
        ("6f1a45e0-0d31-4c65-9a01-000000000060", "Claude-Fable-5.1_cli",
            """
            {
              "_seed": 22,
              "provider": "anthropic",
              "transport": "cli",
              "cliCommand": "claude --permission-mode acceptEdits",
              "model": "claude-fable-5-1",
              "baseUrl": "",
              "secretRef": "",
              "params": {},
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "claude-fable-5-1",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 99 },
                { "name": "code-review",          "score": 98 },
                { "name": "code-debug",           "score": 98 },
                { "name": "code-test",            "score": 96 },
                { "name": "code-refactor",        "score": 97 },
                { "name": "text-write",           "score": 96 },
                { "name": "text-docs",            "score": 96 },
                { "name": "text-edit",            "score": 95 },
                { "name": "text-translate",       "score": 94 },
                { "name": "text-summarize",       "score": 97 },
                { "name": "analyze-requirements", "score": 98 },
                { "name": "analyze-plan",         "score": 97 },
                { "name": "analyze-data",         "score": 96 }
              ],
              "limits":  { "context": 1000000, "max_output": 128000 },
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        ("6f1a45e0-0d31-4c65-9a01-000000000061", "Claude-Haiku-4.5_cli",
            """
            {
              "_seed": 22,
              "provider": "anthropic",
              "transport": "cli",
              "cliCommand": "claude --permission-mode acceptEdits",
              "model": "claude-haiku-4-5",
              "baseUrl": "",
              "secretRef": "",
              "params": {},
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "claude-haiku-4-5",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 76 },
                { "name": "code-review",          "score": 74 },
                { "name": "code-debug",           "score": 73 },
                { "name": "code-test",            "score": 74 },
                { "name": "code-refactor",        "score": 73 },
                { "name": "text-write",           "score": 76 },
                { "name": "text-docs",            "score": 75 },
                { "name": "text-edit",            "score": 75 },
                { "name": "text-translate",       "score": 78 },
                { "name": "text-summarize",       "score": 80 },
                { "name": "analyze-requirements", "score": 72 },
                { "name": "analyze-plan",         "score": 70 },
                { "name": "analyze-data",         "score": 70 }
              ],
              "limits":  { "context": 200000, "max_output": 64000 },
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // T-370-S0: Opus 5.5 по CLI. В 1.144 записи не было намеренно — установленный тогда
        // Claude Code 2.1.278 отвечал отказом «[claude-code:unrecognized_model]: does not
        // support this model; version 2.1.280 or newer is required». CLI обновлён, и живой
        // запуск 24.09.2026 на 2.1.281 подтвердил: `claude -p --model claude-opus-5-5` даёт
        // код возврата 0 и modelUsage ["claude-opus-5-5"]. Тем же запуском проверено, что
        // алиас `opus` УЖЕ переехал на 5.5 (в 1.144 он означал claude-opus-5) — лишнее
        // подтверждение правила «в записи дистрибутива только полное имя версии».
        // Запись 006 (Claude-Opus-5.0_cli) остаётся на claude-opus-5: версии сосуществуют.
        ("6f1a45e0-0d31-4c65-9a01-000000000062", "Claude-Opus-5.5_cli",
            """
            {
              "_seed": 22,
              "provider": "anthropic",
              "transport": "cli",
              "cliCommand": "claude --permission-mode acceptEdits",
              "model": "claude-opus-5-5",
              "baseUrl": "",
              "secretRef": "",
              "params": {},
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "claude-opus-5-5",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 98 },
                { "name": "code-review",          "score": 98 },
                { "name": "code-debug",           "score": 97 },
                { "name": "code-test",            "score": 95 },
                { "name": "code-refactor",        "score": 96 },
                { "name": "text-write",           "score": 95 },
                { "name": "text-docs",            "score": 95 },
                { "name": "text-edit",            "score": 94 },
                { "name": "text-translate",       "score": 93 },
                { "name": "text-summarize",       "score": 96 },
                { "name": "analyze-requirements", "score": 97 },
                { "name": "analyze-plan",         "score": 97 },
                { "name": "analyze-data",         "score": 95 }
              ],
              "limits":  { "context": 1000000, "max_output": 128000 },
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        ("6f1a45e0-0d31-4c65-9a01-000000000011", "Claude-Haiku-4.5",
            """
            {
              "_seed": 22,
              "provider": "anthropic",
              "model": "claude-haiku-4-5",
              "baseUrl": "",
              "secretRef": "anthropic.apiKey",
              "params": { "maxTokens": 16000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "claude-haiku-4-5",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 76 },
                { "name": "code-review",          "score": 74 },
                { "name": "code-debug",           "score": 73 },
                { "name": "code-test",            "score": 74 },
                { "name": "code-refactor",        "score": 73 },
                { "name": "text-write",           "score": 76 },
                { "name": "text-docs",            "score": 75 },
                { "name": "text-edit",            "score": 75 },
                { "name": "text-translate",       "score": 78 },
                { "name": "text-summarize",       "score": 80 },
                { "name": "analyze-requirements", "score": 72 },
                { "name": "analyze-plan",         "score": 70 },
                { "name": "analyze-data",         "score": 70 }
              ],
              "limits":  { "context": 200000, "max_output": 64000 },
              "cost":    { "in_per_1m": 1.0, "out_per_1m": 5.0 }
            }
            """),
        // OpenAI (T-214): флагман Sol и средний Terra. Вход по OpenAI-совместимому API —
        // нашему коннектору хватает baseUrl и ключа. Цены сверены с каталогом OpenRouter
        // 2026-08-17: у Terra там $1/$6 (в отчёте T-213 значилось $2,50/$15).
        ("6f1a45e0-0d31-4c65-9a01-000000000012", "GPT-5.6-Sol",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "gpt-5.6-sol",
              "baseUrl": "https://api.openai.com/v1",
              "secretRef": "openai.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "gpt-5.6-sol",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 95 },
                { "name": "code-review",          "score": 94 },
                { "name": "code-debug",           "score": 94 },
                { "name": "code-test",            "score": 92 },
                { "name": "code-refactor",        "score": 93 },
                { "name": "text-write",           "score": 93 },
                { "name": "text-docs",            "score": 92 },
                { "name": "text-edit",            "score": 92 },
                { "name": "text-translate",       "score": 91 },
                { "name": "text-summarize",       "score": 94 },
                { "name": "analyze-requirements", "score": 94 },
                { "name": "analyze-plan",         "score": 94 },
                { "name": "analyze-data",         "score": 93 }
              ],
              "limits":  { "context": 1050000, "max_output": 128000 },
              "cost":    { "in_per_1m": 5.0, "out_per_1m": 30.0 }
            }
            """),
        ("6f1a45e0-0d31-4c65-9a01-000000000013", "GPT-5.6-Terra",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "gpt-5.6-terra",
              "baseUrl": "https://api.openai.com/v1",
              "secretRef": "openai.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "gpt-5.6-terra",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 88 },
                { "name": "code-review",          "score": 87 },
                { "name": "code-debug",           "score": 86 },
                { "name": "code-test",            "score": 85 },
                { "name": "code-refactor",        "score": 86 },
                { "name": "text-write",           "score": 87 },
                { "name": "text-docs",            "score": 86 },
                { "name": "text-edit",            "score": 86 },
                { "name": "text-translate",       "score": 86 },
                { "name": "text-summarize",       "score": 88 },
                { "name": "analyze-requirements", "score": 86 },
                { "name": "analyze-plan",         "score": 85 },
                { "name": "analyze-data",         "score": 85 }
              ],
              "limits":  { "context": 1050000, "max_output": 128000 },
              "cost":    { "in_per_1m": 1.0, "out_per_1m": 6.0 }
            }
            """),
        // Google Gemini 3 (T-214): подключается через OpenAI-совместимый слой Google
        // (/v1beta/openai) — отдельный коннектор не нужен, но полнота совместимости на живом
        // вызове не проверена (отчёт T-213, §8). Ultra в каталоге OpenRouter отсутствует, его
        // id и цена — из отчёта; Pro и Flash сверены с каталогом.
        ("6f1a45e0-0d31-4c65-9a01-000000000014", "Gemini-3-Ultra",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "gemini-3-ultra",
              "baseUrl": "https://generativelanguage.googleapis.com/v1beta/openai",
              "secretRef": "google.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "gemini-3-ultra",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf", "audio/*", "video/*"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 93 },
                { "name": "code-review",          "score": 92 },
                { "name": "code-debug",           "score": 91 },
                { "name": "code-test",            "score": 90 },
                { "name": "code-refactor",        "score": 91 },
                { "name": "text-write",           "score": 92 },
                { "name": "text-docs",            "score": 91 },
                { "name": "text-edit",            "score": 90 },
                { "name": "text-translate",       "score": 92 },
                { "name": "text-summarize",       "score": 94 },
                { "name": "analyze-requirements", "score": 93 },
                { "name": "analyze-plan",         "score": 92 },
                { "name": "analyze-data",         "score": 94 }
              ],
              "limits":  { "context": 2000000, "max_output": 65536 },
              "cost":    { "in_per_1m": 10.0, "out_per_1m": 30.0 }
            }
            """),
        ("6f1a45e0-0d31-4c65-9a01-000000000015", "Gemini-3.1-Pro",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "gemini-3.1-pro",
              "baseUrl": "https://generativelanguage.googleapis.com/v1beta/openai",
              "secretRef": "google.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "gemini-3.1-pro",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf", "audio/*", "video/*"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 89 },
                { "name": "code-review",          "score": 88 },
                { "name": "code-debug",           "score": 87 },
                { "name": "code-test",            "score": 86 },
                { "name": "code-refactor",        "score": 87 },
                { "name": "text-write",           "score": 89 },
                { "name": "text-docs",            "score": 88 },
                { "name": "text-edit",            "score": 87 },
                { "name": "text-translate",       "score": 90 },
                { "name": "text-summarize",       "score": 91 },
                { "name": "analyze-requirements", "score": 88 },
                { "name": "analyze-plan",         "score": 87 },
                { "name": "analyze-data",         "score": 90 }
              ],
              "limits":  { "context": 1048576, "max_output": 65536 },
              "cost":    { "in_per_1m": 2.0, "out_per_1m": 12.0 }
            }
            """),
        ("6f1a45e0-0d31-4c65-9a01-000000000016", "Gemini-3.7-Flash",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "gemini-3.7-flash",
              "baseUrl": "https://generativelanguage.googleapis.com/v1beta/openai",
              "secretRef": "google.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "gemini-3.7-flash",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf", "audio/*", "video/*"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 82 },
                { "name": "code-review",          "score": 80 },
                { "name": "code-debug",           "score": 79 },
                { "name": "code-test",            "score": 79 },
                { "name": "code-refactor",        "score": 79 },
                { "name": "text-write",           "score": 82 },
                { "name": "text-docs",            "score": 81 },
                { "name": "text-edit",            "score": 80 },
                { "name": "text-translate",       "score": 85 },
                { "name": "text-summarize",       "score": 86 },
                { "name": "analyze-requirements", "score": 78 },
                { "name": "analyze-plan",         "score": 77 },
                { "name": "analyze-data",         "score": 80 }
              ],
              "limits":  { "context": 1048576, "max_output": 65536 },
              "cost":    { "in_per_1m": 0.375, "out_per_1m": 1.875 }
            }
            """),
        // xAI Grok 4.6 (T-214): самый дешёвый на фронтире, но запрос длиннее 200 000 токенов
        // тарифицируется целиком вдвое дороже ($4/$12) — это не ошибка в цифрах ниже, а тариф.
        ("6f1a45e0-0d31-4c65-9a01-000000000017", "Grok-4.6",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "grok-4.6",
              "baseUrl": "https://api.x.ai/v1",
              "secretRef": "xai.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "grok-4.6",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 92 },
                { "name": "code-review",          "score": 90 },
                { "name": "code-debug",           "score": 90 },
                { "name": "code-test",            "score": 88 },
                { "name": "code-refactor",        "score": 89 },
                { "name": "text-write",           "score": 89 },
                { "name": "text-docs",            "score": 87 },
                { "name": "text-edit",            "score": 88 },
                { "name": "text-translate",       "score": 86 },
                { "name": "text-summarize",       "score": 89 },
                { "name": "analyze-requirements", "score": 88 },
                { "name": "analyze-plan",         "score": 87 },
                { "name": "analyze-data",         "score": 88 }
              ],
              "limits":  { "context": 500000, "max_output": 64000 },
              "cost":    { "in_per_1m": 2.0, "out_per_1m": 6.0 }
            }
            """),
        // Alibaba Qwen3.8-Max (T-214): лидер OSWorld-Verified (86,1) — «работа за компьютером»,
        // то есть ровно наш сценарий агента, при цене вчетверо ниже Opus 5. Международный вход
        // DashScope; в Китае адрес другой (dashscope.aliyuncs.com).
        ("6f1a45e0-0d31-4c65-9a01-000000000018", "Qwen3.8-Max",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "qwen3.8-max",
              "baseUrl": "https://dashscope-intl.aliyuncs.com/compatible-mode/v1",
              "secretRef": "qwen.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "qwen3.8-max",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "video/*"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 92 },
                { "name": "code-review",          "score": 90 },
                { "name": "code-debug",           "score": 90 },
                { "name": "code-test",            "score": 88 },
                { "name": "code-refactor",        "score": 90 },
                { "name": "text-write",           "score": 88 },
                { "name": "text-docs",            "score": 87 },
                { "name": "text-edit",            "score": 86 },
                { "name": "text-translate",       "score": 90 },
                { "name": "text-summarize",       "score": 90 },
                { "name": "analyze-requirements", "score": 88 },
                { "name": "analyze-plan",         "score": 87 },
                { "name": "analyze-data",         "score": 89 }
              ],
              "limits":  { "context": 1000000, "max_output": 65536 },
              "cost":    { "in_per_1m": 2.0, "out_per_1m": 6.0 }
            }
            """),
        // Meta Muse Spark 1.2 (T-214): своего публичного OpenAI-совместимого входа у Meta нет
        // (отчёт T-213, §8), поэтому запись идёт через шлюз OpenRouter — один ключ на все модели
        // шлюза. Так же подключены Inkling, Nemotron и Ling: у них тоже нет своего API.
        ("6f1a45e0-0d31-4c65-9a01-000000000019", "Muse-Spark-1.2",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "meta/muse-spark-1.2",
              "baseUrl": "https://openrouter.ai/api/v1",
              "secretRef": "openrouter.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "meta/muse-spark-1.2",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf", "audio/*", "video/*"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 89 },
                { "name": "code-review",          "score": 87 },
                { "name": "code-debug",           "score": 86 },
                { "name": "code-test",            "score": 85 },
                { "name": "code-refactor",        "score": 86 },
                { "name": "text-write",           "score": 88 },
                { "name": "text-docs",            "score": 86 },
                { "name": "text-edit",            "score": 86 },
                { "name": "text-translate",       "score": 87 },
                { "name": "text-summarize",       "score": 89 },
                { "name": "analyze-requirements", "score": 87 },
                { "name": "analyze-plan",         "score": 86 },
                { "name": "analyze-data",         "score": 87 }
              ],
              "limits":  { "context": 1048576, "max_output": 65536 },
              "cost":    { "in_per_1m": 1.25, "out_per_1m": 4.25 }
            }
            """),
        // Открытые веса и дешёвые API (§5.2 отчёта T-213). Kimi K3 — лучший открытый по
        // сводному индексу Artificial Analysis; лицензия СВОЯ (Kimi K3 License), не MIT.
        ("6f1a45e0-0d31-4c65-9a01-000000000020", "Kimi-K3",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "kimi-k3",
              "baseUrl": "https://api.moonshot.ai/v1",
              "secretRef": "moonshot.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "kimi-k3",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "video/*"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 90 },
                { "name": "code-review",          "score": 88 },
                { "name": "code-debug",           "score": 88 },
                { "name": "code-test",            "score": 86 },
                { "name": "code-refactor",        "score": 88 },
                { "name": "text-write",           "score": 86 },
                { "name": "text-docs",            "score": 86 },
                { "name": "text-edit",            "score": 85 },
                { "name": "text-translate",       "score": 86 },
                { "name": "text-summarize",       "score": 89 },
                { "name": "analyze-requirements", "score": 88 },
                { "name": "analyze-plan",         "score": 88 },
                { "name": "analyze-data",         "score": 86 }
              ],
              "limits":  { "context": 1048576, "max_output": 65536 },
              "cost":    { "in_per_1m": 3.0, "out_per_1m": 15.0 }
            }
            """),
        ("6f1a45e0-0d31-4c65-9a01-000000000021", "GLM-5.2",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "glm-5.2",
              "baseUrl": "https://api.z.ai/api/paas/v4",
              "secretRef": "zai.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "glm-5.2",
              "inputs":  ["text/markdown", "text/plain", "text/source-code"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 91 },
                { "name": "code-review",          "score": 89 },
                { "name": "code-debug",           "score": 88 },
                { "name": "code-test",            "score": 87 },
                { "name": "code-refactor",        "score": 89 },
                { "name": "text-write",           "score": 84 },
                { "name": "text-docs",            "score": 85 },
                { "name": "text-edit",            "score": 83 },
                { "name": "text-translate",       "score": 84 },
                { "name": "text-summarize",       "score": 86 },
                { "name": "analyze-requirements", "score": 87 },
                { "name": "analyze-plan",         "score": 87 },
                { "name": "analyze-data",         "score": 85 }
              ],
              "limits":  { "context": 1048576, "max_output": 65536 },
              "cost":    { "in_per_1m": 1.19, "out_per_1m": 3.74 }
            }
            """),
        ("6f1a45e0-0d31-4c65-9a01-000000000022", "MiniMax-M3",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "minimax-m3",
              "baseUrl": "https://api.minimax.io/v1",
              "secretRef": "minimax.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "minimax-m3",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "video/*"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 84 },
                { "name": "code-review",          "score": 82 },
                { "name": "code-debug",           "score": 81 },
                { "name": "code-test",            "score": 80 },
                { "name": "code-refactor",        "score": 82 },
                { "name": "text-write",           "score": 82 },
                { "name": "text-docs",            "score": 82 },
                { "name": "text-edit",            "score": 81 },
                { "name": "text-translate",       "score": 83 },
                { "name": "text-summarize",       "score": 85 },
                { "name": "analyze-requirements", "score": 81 },
                { "name": "analyze-plan",         "score": 80 },
                { "name": "analyze-data",         "score": 82 }
              ],
              "limits":  { "context": 1048576, "max_output": 65536 },
              "cost":    { "in_per_1m": 0.3, "out_per_1m": 1.2 }
            }
            """),
        // Thinking Machines Inkling (T-214): Apache 2.0 на 975B/41B, вход текст+картинки+звук.
        // Осторожно с фактологией: AA Omniscience даёт 40 % точности при 63 % галлюцинаций
        // (отчёт T-213, §5.2) — на analyze-data и text-docs ставить с оглядкой.
        ("6f1a45e0-0d31-4c65-9a01-000000000023", "Inkling-975B",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "thinkingmachines/inkling",
              "baseUrl": "https://openrouter.ai/api/v1",
              "secretRef": "openrouter.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "thinkingmachines/inkling",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "audio/*"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 85 },
                { "name": "code-review",          "score": 83 },
                { "name": "code-debug",           "score": 83 },
                { "name": "code-test",            "score": 81 },
                { "name": "code-refactor",        "score": 83 },
                { "name": "text-write",           "score": 84 },
                { "name": "text-docs",            "score": 83 },
                { "name": "text-edit",            "score": 82 },
                { "name": "text-translate",       "score": 85 },
                { "name": "text-summarize",       "score": 87 },
                { "name": "analyze-requirements", "score": 84 },
                { "name": "analyze-plan",         "score": 84 },
                { "name": "analyze-data",         "score": 84 }
              ],
              "limits":  { "context": 1048576, "max_output": 65536 },
              "cost":    { "in_per_1m": 0.95, "out_per_1m": 4.05 }
            }
            """),
        ("6f1a45e0-0d31-4c65-9a01-000000000024", "Nemotron-3-Ultra",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "nvidia/nemotron-3-ultra-550b-a55b",
              "baseUrl": "https://openrouter.ai/api/v1",
              "secretRef": "openrouter.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "nvidia/nemotron-3-ultra-550b-a55b",
              "inputs":  ["text/markdown", "text/plain", "text/source-code"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 80 },
                { "name": "code-review",          "score": 78 },
                { "name": "code-debug",           "score": 78 },
                { "name": "code-test",            "score": 76 },
                { "name": "code-refactor",        "score": 78 },
                { "name": "text-write",           "score": 78 },
                { "name": "text-docs",            "score": 78 },
                { "name": "text-edit",            "score": 77 },
                { "name": "text-translate",       "score": 79 },
                { "name": "text-summarize",       "score": 82 },
                { "name": "analyze-requirements", "score": 79 },
                { "name": "analyze-plan",         "score": 79 },
                { "name": "analyze-data",         "score": 78 }
              ],
              "limits":  { "context": 512288, "max_output": 65536 },
              "cost":    { "in_per_1m": 0.6, "out_per_1m": 3.6 }
            }
            """),
        // Ling 3.0 Flash (Ant Group, T-214): 124B/5,1B активных, целится в высокочастотные
        // агентные сценарии. Самая дешёвая запись справочника — $0,021 за 1 млн входных
        // токенов, то есть в полтораста раз дешевле Kimi K3: кандидат в дешёвый фон.
        ("6f1a45e0-0d31-4c65-9a01-000000000025", "Ling-3.0-Flash",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "inclusionai/ling-3.0-flash",
              "baseUrl": "https://openrouter.ai/api/v1",
              "secretRef": "openrouter.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "inclusionai/ling-3.0-flash",
              "inputs":  ["text/markdown", "text/plain", "text/source-code"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 80 },
                { "name": "code-review",          "score": 78 },
                { "name": "code-debug",           "score": 77 },
                { "name": "code-test",            "score": 76 },
                { "name": "code-refactor",        "score": 77 },
                { "name": "text-write",           "score": 78 },
                { "name": "text-docs",            "score": 77 },
                { "name": "text-edit",            "score": 76 },
                { "name": "text-translate",       "score": 79 },
                { "name": "text-summarize",       "score": 82 },
                { "name": "analyze-requirements", "score": 76 },
                { "name": "analyze-plan",         "score": 75 },
                { "name": "analyze-data",         "score": 74 }
              ],
              "limits":  { "context": 262144, "max_output": 32768 },
              "cost":    { "in_per_1m": 0.021, "out_per_1m": 0.063 }
            }
            """),
        ("6f1a45e0-0d31-4c65-9a01-000000000026", "Mistral-Large-3",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "mistral-large-2512",
              "baseUrl": "https://api.mistral.ai/v1",
              "secretRef": "mistral.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "mistral-large-2512",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 82 },
                { "name": "code-review",          "score": 80 },
                { "name": "code-debug",           "score": 79 },
                { "name": "code-test",            "score": 78 },
                { "name": "code-refactor",        "score": 80 },
                { "name": "text-write",           "score": 83 },
                { "name": "text-docs",            "score": 82 },
                { "name": "text-edit",            "score": 81 },
                { "name": "text-translate",       "score": 85 },
                { "name": "text-summarize",       "score": 84 },
                { "name": "analyze-requirements", "score": 80 },
                { "name": "analyze-plan",         "score": 79 },
                { "name": "analyze-data",         "score": 78 }
              ],
              "limits":  { "context": 262144, "max_output": 65536 },
              "cost":    { "in_per_1m": 0.5, "out_per_1m": 1.5 }
            }
            """),
        // Российский контур (T-214). ВНИМАНИЕ: у обеих записей подключение неполное и это
        // известно (отчёт T-213, §8). GigaChat выдаёт токен доступа на 30 минут по OAuth и
        // требует российские корневые сертификаты — наш коннектор умеет только постоянный ключ,
        // поэтому запись работает лишь до истечения токена; полноценно — отдельный коннектор
        // или прокси. YandexGPT ждёт в поле «модель» полный ресурс gpt://<каталог>/…, свой
        // для каждого пользователя, — подставить его руками в профайле.
        ("6f1a45e0-0d31-4c65-9a01-000000000027", "GigaChat-3.5-Ultra",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "GigaChat-3.5-Ultra",
              "baseUrl": "https://gigachat.devices.sberbank.ru/api/v1",
              "secretRef": "gigachat.accessToken",
              "params": { "maxTokens": 16000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "GigaChat-3.5-Ultra",
              "inputs":  ["text/markdown", "text/plain", "text/source-code"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 66 },
                { "name": "code-review",          "score": 63 },
                { "name": "code-debug",           "score": 62 },
                { "name": "code-test",            "score": 61 },
                { "name": "code-refactor",        "score": 62 },
                { "name": "text-write",           "score": 85 },
                { "name": "text-docs",            "score": 79 },
                { "name": "text-edit",            "score": 84 },
                { "name": "text-translate",       "score": 84 },
                { "name": "text-summarize",       "score": 82 },
                { "name": "analyze-requirements", "score": 73 },
                { "name": "analyze-plan",         "score": 71 },
                { "name": "analyze-data",         "score": 69 }
              ],
              "limits":  { "context": 262144, "max_output": 32768 },
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        ("6f1a45e0-0d31-4c65-9a01-000000000028", "YandexGPT-5.1-Pro",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "gpt://<folder-id>/yandexgpt/latest",
              "baseUrl": "https://llm.api.cloud.yandex.net/v1",
              "secretRef": "yandex.apiKey",
              "params": { "maxTokens": 16000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "gpt://<folder-id>/yandexgpt/latest",
              "inputs":  ["text/markdown", "text/plain", "text/source-code"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 64 },
                { "name": "code-review",          "score": 62 },
                { "name": "code-debug",           "score": 61 },
                { "name": "code-test",            "score": 60 },
                { "name": "code-refactor",        "score": 60 },
                { "name": "text-write",           "score": 86 },
                { "name": "text-docs",            "score": 80 },
                { "name": "text-edit",            "score": 85 },
                { "name": "text-translate",       "score": 86 },
                { "name": "text-summarize",       "score": 84 },
                { "name": "analyze-requirements", "score": 74 },
                { "name": "analyze-plan",         "score": 72 },
                { "name": "analyze-data",         "score": 70 }
              ],
              "limits":  { "context": 32000, "max_output": 16000 },
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // Локальные модели на своём компьютере (T-214), манифесты по образцу
        // Qwen3.6-35B-A3B-Local: пакет llama.cpp + один файл GGUF. Размеры сняты HEAD-запросами
        // к HuggingFace 2026-08-17 (test/t214/head.py) — установка сверяет скачанное по точному
        // размеру. Порты у каждой модели свои (8081/8082/8083): иначе второй llama-server не
        // поднимется рядом с первым. Qwen3.8-27B — лучший вариант «на одну видеокарту»
        // по отчёту T-213 (§2).
        ("6f1a45e0-0d31-4c65-9a01-000000000029", "Qwen3.8-27B-Local",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "qwen3.8-27b",
              "baseUrl": "http://localhost:8081/v1",
              "secretRef": "",
              "launchCommand": "",
              "params": { "maxTokens": 32768 },
              "install": {
                "group": "Qwen3.8",
                "packages": ["llama.cpp"],
                "launchCommand": "\"{package:llama.cpp:llama-server.exe}\" -m \"{model:Qwen3.8-27B-UD-Q4_K_XL.gguf}\" -ngl 99 -c 65536 --jinja --port 8081 --alias qwen3.8-27b",
                "files": [
                  {
                    "name": "Qwen3.8-27B-UD-Q4_K_XL.gguf",
                    "url": "https://huggingface.co/unsloth/Qwen3.8-27B-GGUF/resolve/main/Qwen3.8-27B-UD-Q4_K_XL.gguf",
                    "size": 17923394624
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "llama-cpp",
                "apply": {
                  "kind": "launch-arg",
                  "field": "--lora-scaled",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".gguf"]
                },
                "train": {
                  "kind": "external",
                  "docUrl": "https://github.com/ggml-org/llama.cpp/discussions/10123",
                  "dataset": { "kind": "none" },
                  "start": { "kind": "none" },
                  "wait": { "kind": "none" },
                  "result": {
                    "kind": "file",
                    "target": "loras/{object}.gguf"
                  }
                }
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "qwen3.8-27b",
              "inputs":  ["text/markdown", "text/plain", "text/source-code"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 87 },
                { "name": "code-review",          "score": 84 },
                { "name": "code-debug",           "score": 84 },
                { "name": "code-test",            "score": 82 },
                { "name": "code-refactor",        "score": 84 },
                { "name": "text-write",           "score": 82 },
                { "name": "text-docs",            "score": 81 },
                { "name": "text-edit",            "score": 80 },
                { "name": "text-translate",       "score": 84 },
                { "name": "text-summarize",       "score": 84 },
                { "name": "analyze-requirements", "score": 81 },
                { "name": "analyze-plan",         "score": 80 },
                { "name": "analyze-data",         "score": 80 }
              ],
              "limits":  { "context": 65536, "max_output": 32768 },
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        ("6f1a45e0-0d31-4c65-9a01-000000000030", "Qwen3.6-27B-Local",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "qwen3.6-27b",
              "baseUrl": "http://localhost:8082/v1",
              "secretRef": "",
              "launchCommand": "",
              "params": { "maxTokens": 32768 },
              "install": {
                "group": "Qwen3.6",
                "packages": ["llama.cpp"],
                "launchCommand": "\"{package:llama.cpp:llama-server.exe}\" -m \"{model:Qwen3.6-27B-UD-Q4_K_XL.gguf}\" -ngl 99 -c 65536 --jinja --port 8082 --alias qwen3.6-27b",
                "files": [
                  {
                    "name": "Qwen3.6-27B-UD-Q4_K_XL.gguf",
                    "url": "https://huggingface.co/unsloth/Qwen3.6-27B-GGUF/resolve/main/Qwen3.6-27B-UD-Q4_K_XL.gguf",
                    "size": 17612564704
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "llama-cpp",
                "apply": {
                  "kind": "launch-arg",
                  "field": "--lora-scaled",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".gguf"]
                },
                "train": {
                  "kind": "external",
                  "docUrl": "https://github.com/ggml-org/llama.cpp/discussions/10123",
                  "dataset": { "kind": "none" },
                  "start": { "kind": "none" },
                  "wait": { "kind": "none" },
                  "result": {
                    "kind": "file",
                    "target": "loras/{object}.gguf"
                  }
                }
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "qwen3.6-27b",
              "inputs":  ["text/markdown", "text/plain", "text/source-code"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 82 },
                { "name": "code-review",          "score": 79 },
                { "name": "code-debug",           "score": 78 },
                { "name": "code-test",            "score": 77 },
                { "name": "code-refactor",        "score": 78 },
                { "name": "text-write",           "score": 79 },
                { "name": "text-docs",            "score": 78 },
                { "name": "text-edit",            "score": 77 },
                { "name": "text-translate",       "score": 82 },
                { "name": "text-summarize",       "score": 81 },
                { "name": "analyze-requirements", "score": 77 },
                { "name": "analyze-plan",         "score": 76 },
                { "name": "analyze-data",         "score": 75 }
              ],
              "limits":  { "context": 65536, "max_output": 32768 },
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        ("6f1a45e0-0d31-4c65-9a01-000000000031", "Muse-Glimmer-30B-Local",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "muse-glimmer-30b",
              "baseUrl": "http://localhost:8083/v1",
              "secretRef": "",
              "launchCommand": "",
              "params": { "maxTokens": 32768 },
              "install": {
                "group": "Muse-Glimmer",
                "packages": ["llama.cpp"],
                "launchCommand": "\"{package:llama.cpp:llama-server.exe}\" -m \"{model:Muse-Glimmer-30B-UD-Q4_K_XL.gguf}\" -ngl 99 -c 65536 --jinja --port 8083 --alias muse-glimmer-30b",
                "files": [
                  {
                    "name": "Muse-Glimmer-30B-UD-Q4_K_XL.gguf",
                    "url": "https://huggingface.co/unsloth/Muse-Glimmer-30B-GGUF/resolve/main/Muse-Glimmer-30B-UD-Q4_K_XL.gguf",
                    "size": 15878222368
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "llama-cpp",
                "apply": {
                  "kind": "launch-arg",
                  "field": "--lora-scaled",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".gguf"]
                },
                "train": {
                  "kind": "external",
                  "docUrl": "https://github.com/ggml-org/llama.cpp/discussions/10123",
                  "dataset": { "kind": "none" },
                  "start": { "kind": "none" },
                  "wait": { "kind": "none" },
                  "result": {
                    "kind": "file",
                    "target": "loras/{object}.gguf"
                  }
                }
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "muse-glimmer-30b",
              "inputs":  ["text/markdown", "text/plain", "text/source-code"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 78 },
                { "name": "code-review",          "score": 76 },
                { "name": "code-debug",           "score": 75 },
                { "name": "code-test",            "score": 74 },
                { "name": "code-refactor",        "score": 75 },
                { "name": "text-write",           "score": 78 },
                { "name": "text-docs",            "score": 77 },
                { "name": "text-edit",            "score": 76 },
                { "name": "text-translate",       "score": 78 },
                { "name": "text-summarize",       "score": 80 },
                { "name": "analyze-requirements", "score": 75 },
                { "name": "analyze-plan",         "score": 74 },
                { "name": "analyze-data",         "score": 73 }
              ],
              "limits":  { "context": 65536, "max_output": 32768 },
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // ── Локальные ВИДЕО-модели через ComfyUI (T-15-S0, отчёт T-213 §6.1 и §6.3) ─────
        // До этого задания видео закрывали только две записи Kandinsky 5.0 Video Lite
        // (t2v 5 с и i2v 5 с). Здесь добавлены остальные открытые линейки — Wan 2.2,
        // HunyuanVideo 1.5, LTX-2.5 — и недостающие варианты самого Kandinsky.
        // Кода не потребовалось: коннектор comfyui умеет и граф, и загрузку стартового
        // кадра, и забор результата. Размеры файлов сняты у HuggingFace 27.08.2026
        // (X-Linked-Size; Content-Length после 302 на CDN врёт — наука T-214).
        //
        // ОБЩЕЕ ПРО ОБУЧЕНИЕ LoRA: musubi-tuner прямо пишет, что fp8_scaled-сборки ему не
        // годятся, а в манифестах установки стоят именно они (вдвое легче). Класть тяжёлые
        // fp16/bf16 в манифест нельзя — от его состава считается признак «модель
        // установлена» (T-4-S0), и у всех, у кого модель работает, она бы погасла.
        // Поэтому обучение догружает свою пару само, первым запуском (train.cmd, hf
        // download в подкаталог train каталога группы) — приём из T-241.
        //
        // Wan 2.2 T2V A14B (Alibaba, Apache 2.0): самая сильная из открытых по общему
        // качеству ролика. Модель СОСТАВНАЯ (MoE): «шумный» эксперт рисует первую половину
        // шагов, «чистый» доводит вторую, поэтому в манифесте два файла весов по 14,3 ГБ.
        ("6f1a45e0-0d31-4c65-9a01-000000000048", "Wan-2.2-T2V-A14B",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "wan2.2_t2v_a14b",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000048.json",
              "params": {
                "width": 832,
                "height": 480,
                "length": 81,
                "steps": 20,
                "negative": "",
                "timeoutMinutes": 240
              },
              "install": {
                "group": "Wan-2.2-T2V",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "wan2.2_t2v_high_noise_14B_fp8_scaled.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged/resolve/main/split_files/diffusion_models/wan2.2_t2v_high_noise_14B_fp8_scaled.safetensors",
                    "size": 14293923632,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "wan2.2_t2v_low_noise_14B_fp8_scaled.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged/resolve/main/split_files/diffusion_models/wan2.2_t2v_low_noise_14B_fp8_scaled.safetensors",
                    "size": 14293923632,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "umt5_xxl_fp8_e4m3fn_scaled.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged/resolve/main/split_files/text_encoders/umt5_xxl_fp8_e4m3fn_scaled.safetensors",
                    "size": 6735906897,
                    "category": "text_encoders"
                  },
                  {
                    "name": "wan_2.1_vae.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged/resolve/main/split_files/vae/wan_2.1_vae.safetensors",
                    "size": 253815318,
                    "category": "vae"
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": {
                  "kind": "workflow",
                  "node": "LoraLoaderModelOnly",
                  "placeholder": "{lora}",
                  "strengthPlaceholder": "{loraStrength}",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".safetensors"]
                },
                "train": {
                  "kind": "process",
                  "docUrl": "https://github.com/kohya-ss/musubi-tuner/blob/main/docs/wan.md",
                  "packages": ["musubi-tuner", "python"],
                  "dataset": {
                    "kind": "dir",
                    "path": "lora/{object}/dataset",
                    "captions": "txt",
                    "minItems": 10,
                    "maxItems": 60,
                    "width": 832,
                    "height": 480,
                    "maxKb": 0,
                    "formats": ["png"]
                  },
                  "files": [
                    {
                      "path": "lora/{object}/dataset.toml",
                      "text": [
                        "# AI2P: written from the model profile before every training run.",
                        "# Edit it in the model profile, not here: it is overwritten each time.",
                        "[general]",
                        "resolution = [{width}, {height}]",
                        "caption_extension = \".txt\"",
                        "batch_size = 1",
                        "enable_bucket = true",
                        "bucket_no_upscale = false",
                        "",
                        "[[datasets]]",
                        "image_directory = '{dataset}'",
                        "cache_directory = '{output}\\cache'",
                        "num_repeats = 1"
                      ]
                    },
                    {
                      "path": "lora/{object}/train.cmd",
                      "text": [
                        "@echo off",
                        "rem AI2P: LoRA training for Wan 2.2 T2V A14B via musubi-tuner (wan_train_network.py).",
                        "rem Written from the model profile before every run - edit it in the model.",
                        "setlocal enableextensions",
                        "set \"TRAIN={package:musubi-tuner:wan_train_network.py}\"",
                        "for %%I in (\"%TRAIN%\") do set \"SCRIPTS=%%~dpI\"",
                        "set \"ROOT=%SCRIPTS%\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" set \"ROOT=%SCRIPTS%\"",
                        "set \"VENV=%ROOT%.venv\"",
                        "set \"PY=%VENV%\\Scripts\\python.exe\"",
                        "set \"OUT={output}\"",
                        "set \"BASE={groupDir}\\train\"",
                        "set \"HIGH=%BASE%\\split_files\\diffusion_models\\wan2.2_t2v_high_noise_14B_fp16.safetensors\"",
                        "set \"LOW=%BASE%\\split_files\\diffusion_models\\wan2.2_t2v_low_noise_14B_fp16.safetensors\"",
                        "set \"T5=%BASE%\\models_t5_umt5-xxl-enc-bf16.pth\"",
                        "if not exist \"%OUT%\" mkdir \"%OUT%\"",
                        "if not exist \"%PY%\" call :setup",
                        "if errorlevel 1 exit /b 1",
                        "rem The generation weights are fp8_scaled and musubi-tuner does not accept them,",
                        "rem so the fp16 pair (about 57 GB) and the original T5 are fetched once.",
                        "if not exist \"%LOW%\" call :base",
                        "if errorlevel 1 exit /b 1",
                        "if not exist \"%HIGH%\" call :base",
                        "if errorlevel 1 exit /b 1",
                        "if not exist \"%T5%\" call :t5",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching latents",
                        "\"%PY%\" \"%SCRIPTS%wan_cache_latents.py\" --dataset_config \"%~dp0dataset.toml\" --vae \"{model:wan_2.1_vae.safetensors}\"",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching text encoder outputs",
                        "\"%PY%\" \"%SCRIPTS%wan_cache_text_encoder_outputs.py\" --dataset_config \"%~dp0dataset.toml\" --t5 \"%T5%\" --batch_size 1",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] training",
                        "\"%VENV%\\Scripts\\accelerate.exe\" launch --num_cpu_threads_per_process 1 --mixed_precision bf16 \"%SCRIPTS%wan_train_network.py\" --task t2v-A14B --dit \"%LOW%\" --dit_high_noise \"%HIGH%\" --dataset_config \"%~dp0dataset.toml\" --sdpa --mixed_precision bf16 --fp8_base --optimizer_type adamw8bit --learning_rate 2e-4 --gradient_checkpointing --max_data_loader_n_workers 2 --persistent_data_loader_workers --network_module networks.lora_wan --network_dim 32 --timestep_sampling shift --discrete_flow_shift 3.0 --max_train_steps {steps} --save_every_n_epochs 1 --seed 42 --output_dir \"%OUT%\" --output_name {object}",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] done",
                        "exit /b 0",
                        "",
                        ":setup",
                        "echo [AI2P] preparing the training environment",
                        "set \"PYEXE={package:python:python.exe}\"",
                        "if defined AI2P_LORA_PYTHON set \"PYEXE=%AI2P_LORA_PYTHON%\"",
                        "set \"TORCH_INDEX=https://download.pytorch.org/whl/cu124\"",
                        "if defined AI2P_LORA_TORCH_INDEX set \"TORCH_INDEX=%AI2P_LORA_TORCH_INDEX%\"",
                        "\"%PYEXE%\" -m venv \"%VENV%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot create the virtual environment. Reinstall the model (it brings Python 3.10-3.12), or point AI2P_LORA_PYTHON at your own Python.",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install --upgrade pip",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install torch torchvision --index-url %TORCH_INDEX%",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install -e \"%ROOT%.\"",
                        "if errorlevel 1 echo [AI2P] ERROR: musubi-tuner was not found in %ROOT% - reinstall the model (Settings - Models - Install), it brings the trainer.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0",
                        "",
                        ":base",
                        "echo [AI2P] fetching the fp16 experts for training (about 57 GB, once)",
                        "\"%PY%\" -m pip install --upgrade huggingface_hub",
                        "if errorlevel 1 exit /b 1",
                        "\"%VENV%\\Scripts\\hf.exe\" download Comfy-Org/Wan_2.2_ComfyUI_Repackaged split_files/diffusion_models/wan2.2_t2v_high_noise_14B_fp16.safetensors split_files/diffusion_models/wan2.2_t2v_low_noise_14B_fp16.safetensors --local-dir \"%BASE%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot download the fp16 experts. Check the network, or put both files under the path above by hand.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0",
                        "",
                        ":t5",
                        "echo [AI2P] fetching the original T5 text encoder for training (about 11 GB, once)",
                        "\"%PY%\" -m pip install --upgrade huggingface_hub",
                        "if errorlevel 1 exit /b 1",
                        "\"%VENV%\\Scripts\\hf.exe\" download Wan-AI/Wan2.1-I2V-14B-720P models_t5_umt5-xxl-enc-bf16.pth --local-dir \"%BASE%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot download the T5 encoder. Check the network, or put the file under the path above by hand.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0"
                      ]
                    }
                  ],
                  "start": {
                    "kind": "process",
                    "command": "train.cmd",
                    "workDir": "lora/{object}",
                    "steps": 2000
                  },
                  "wait": {
                    "kind": "process",
                    "timeoutMinutes": 720
                  },
                  "result": {
                    "kind": "file",
                    "path": "lora/{object}/out/{object}.safetensors",
                    "target": "loras/{object}.safetensors"
                  }
                }
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "wan2.2_t2v_a14b",
              "inputs":  ["text/plain"],
              "outputs": ["video/*"],
              "skills":  [
                { "name": "video-generate", "score": 80 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // Wan 2.2 I2V A14B — та же модель, но ролик рисуется ОТ КАРТИНКИ: стартовый кадр
        // задаёт персонажа и сцену, промпт описывает движение. Отличия от t2v-записи:
        // свои файлы весов, узел WanImageToVideo в графе и refImage «upload» (обязательный) —
        // без картинки задание должно отказываться сразу, а не рисовать что-то своё.
        // Группа установки СВОЯ: файлы весов у t2v и i2v разные, а энкодер и VAE
        // повторяются — группы в репозитории моделей не пересекаются (как у Z-Image
        // и Qwen-Image в T-241).
        ("6f1a45e0-0d31-4c65-9a01-000000000049", "Wan-2.2-I2V-A14B",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "wan2.2_i2v_a14b",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000049.json",
              "params": {
                "width": 832,
                "height": 480,
                "length": 81,
                "steps": 20,
                "negative": "",
                "timeoutMinutes": 240
              },
              "install": {
                "group": "Wan-2.2-I2V",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "wan2.2_i2v_high_noise_14B_fp8_scaled.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged/resolve/main/split_files/diffusion_models/wan2.2_i2v_high_noise_14B_fp8_scaled.safetensors",
                    "size": 14294742832,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "wan2.2_i2v_low_noise_14B_fp8_scaled.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged/resolve/main/split_files/diffusion_models/wan2.2_i2v_low_noise_14B_fp8_scaled.safetensors",
                    "size": 14294742832,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "umt5_xxl_fp8_e4m3fn_scaled.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged/resolve/main/split_files/text_encoders/umt5_xxl_fp8_e4m3fn_scaled.safetensors",
                    "size": 6735906897,
                    "category": "text_encoders"
                  },
                  {
                    "name": "wan_2.1_vae.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged/resolve/main/split_files/vae/wan_2.1_vae.safetensors",
                    "size": 253815318,
                    "category": "vae"
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": {
                  "kind": "workflow",
                  "node": "LoraLoaderModelOnly",
                  "placeholder": "{lora}",
                  "strengthPlaceholder": "{loraStrength}",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".safetensors"]
                },
                "train": {
                  "kind": "process",
                  "docUrl": "https://github.com/kohya-ss/musubi-tuner/blob/main/docs/wan.md",
                  "packages": ["musubi-tuner", "python"],
                  "dataset": {
                    "kind": "dir",
                    "path": "lora/{object}/dataset",
                    "captions": "txt",
                    "minItems": 10,
                    "maxItems": 60,
                    "width": 832,
                    "height": 480,
                    "maxKb": 0,
                    "formats": ["png"]
                  },
                  "files": [
                    {
                      "path": "lora/{object}/dataset.toml",
                      "text": [
                        "# AI2P: written from the model profile before every training run.",
                        "# Edit it in the model profile, not here: it is overwritten each time.",
                        "[general]",
                        "resolution = [{width}, {height}]",
                        "caption_extension = \".txt\"",
                        "batch_size = 1",
                        "enable_bucket = true",
                        "bucket_no_upscale = false",
                        "",
                        "[[datasets]]",
                        "image_directory = '{dataset}'",
                        "cache_directory = '{output}\\cache'",
                        "num_repeats = 1"
                      ]
                    },
                    {
                      "path": "lora/{object}/train.cmd",
                      "text": [
                        "@echo off",
                        "rem AI2P: LoRA training for Wan 2.2 I2V A14B via musubi-tuner (wan_train_network.py).",
                        "rem Written from the model profile before every run - edit it in the model.",
                        "setlocal enableextensions",
                        "set \"TRAIN={package:musubi-tuner:wan_train_network.py}\"",
                        "for %%I in (\"%TRAIN%\") do set \"SCRIPTS=%%~dpI\"",
                        "set \"ROOT=%SCRIPTS%\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" set \"ROOT=%SCRIPTS%\"",
                        "set \"VENV=%ROOT%.venv\"",
                        "set \"PY=%VENV%\\Scripts\\python.exe\"",
                        "set \"OUT={output}\"",
                        "set \"BASE={groupDir}\\train\"",
                        "set \"HIGH=%BASE%\\split_files\\diffusion_models\\wan2.2_i2v_high_noise_14B_fp16.safetensors\"",
                        "set \"LOW=%BASE%\\split_files\\diffusion_models\\wan2.2_i2v_low_noise_14B_fp16.safetensors\"",
                        "set \"T5=%BASE%\\models_t5_umt5-xxl-enc-bf16.pth\"",
                        "if not exist \"%OUT%\" mkdir \"%OUT%\"",
                        "if not exist \"%PY%\" call :setup",
                        "if errorlevel 1 exit /b 1",
                        "rem The generation weights are fp8_scaled and musubi-tuner does not accept them,",
                        "rem so the fp16 pair (about 57 GB) and the original T5 are fetched once.",
                        "if not exist \"%LOW%\" call :base",
                        "if errorlevel 1 exit /b 1",
                        "if not exist \"%HIGH%\" call :base",
                        "if errorlevel 1 exit /b 1",
                        "if not exist \"%T5%\" call :t5",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching latents",
                        "\"%PY%\" \"%SCRIPTS%wan_cache_latents.py\" --dataset_config \"%~dp0dataset.toml\" --vae \"{model:wan_2.1_vae.safetensors}\" --i2v",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching text encoder outputs",
                        "\"%PY%\" \"%SCRIPTS%wan_cache_text_encoder_outputs.py\" --dataset_config \"%~dp0dataset.toml\" --t5 \"%T5%\" --batch_size 1",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] training",
                        "\"%VENV%\\Scripts\\accelerate.exe\" launch --num_cpu_threads_per_process 1 --mixed_precision bf16 \"%SCRIPTS%wan_train_network.py\" --task i2v-A14B --dit \"%LOW%\" --dit_high_noise \"%HIGH%\" --dataset_config \"%~dp0dataset.toml\" --sdpa --mixed_precision bf16 --fp8_base --optimizer_type adamw8bit --learning_rate 2e-4 --gradient_checkpointing --max_data_loader_n_workers 2 --persistent_data_loader_workers --network_module networks.lora_wan --network_dim 32 --timestep_sampling shift --discrete_flow_shift 5.0 --max_train_steps {steps} --save_every_n_epochs 1 --seed 42 --output_dir \"%OUT%\" --output_name {object}",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] done",
                        "exit /b 0",
                        "",
                        ":setup",
                        "echo [AI2P] preparing the training environment",
                        "set \"PYEXE={package:python:python.exe}\"",
                        "if defined AI2P_LORA_PYTHON set \"PYEXE=%AI2P_LORA_PYTHON%\"",
                        "set \"TORCH_INDEX=https://download.pytorch.org/whl/cu124\"",
                        "if defined AI2P_LORA_TORCH_INDEX set \"TORCH_INDEX=%AI2P_LORA_TORCH_INDEX%\"",
                        "\"%PYEXE%\" -m venv \"%VENV%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot create the virtual environment. Reinstall the model (it brings Python 3.10-3.12), or point AI2P_LORA_PYTHON at your own Python.",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install --upgrade pip",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install torch torchvision --index-url %TORCH_INDEX%",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install -e \"%ROOT%.\"",
                        "if errorlevel 1 echo [AI2P] ERROR: musubi-tuner was not found in %ROOT% - reinstall the model (Settings - Models - Install), it brings the trainer.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0",
                        "",
                        ":base",
                        "echo [AI2P] fetching the fp16 experts for training (about 57 GB, once)",
                        "\"%PY%\" -m pip install --upgrade huggingface_hub",
                        "if errorlevel 1 exit /b 1",
                        "\"%VENV%\\Scripts\\hf.exe\" download Comfy-Org/Wan_2.2_ComfyUI_Repackaged split_files/diffusion_models/wan2.2_i2v_high_noise_14B_fp16.safetensors split_files/diffusion_models/wan2.2_i2v_low_noise_14B_fp16.safetensors --local-dir \"%BASE%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot download the fp16 experts. Check the network, or put both files under the path above by hand.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0",
                        "",
                        ":t5",
                        "echo [AI2P] fetching the original T5 text encoder for training (about 11 GB, once)",
                        "\"%PY%\" -m pip install --upgrade huggingface_hub",
                        "if errorlevel 1 exit /b 1",
                        "\"%VENV%\\Scripts\\hf.exe\" download Wan-AI/Wan2.1-I2V-14B-720P models_t5_umt5-xxl-enc-bf16.pth --local-dir \"%BASE%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot download the T5 encoder. Check the network, or put the file under the path above by hand.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0"
                      ]
                    }
                  ],
                  "start": {
                    "kind": "process",
                    "command": "train.cmd",
                    "workDir": "lora/{object}",
                    "steps": 2000
                  },
                  "wait": {
                    "kind": "process",
                    "timeoutMinutes": 720
                  },
                  "result": {
                    "kind": "file",
                    "path": "lora/{object}/out/{object}.safetensors",
                    "target": "loras/{object}.safetensors"
                  }
                }
              },
              "refImage": {
                "kind": "upload",
                "placeholder": "{image}",
                "uploadPath": "/upload/image",
                "field": "image",
                "maxCount": 1,
                "formats": ["image/png", "image/jpeg", "image/webp"],
                "required": true
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "wan2.2_i2v_a14b",
              "inputs":  ["text/plain", "image/*"],
              "outputs": ["video/*"],
              "skills":  [
                { "name": "video-animate",  "score": 80 },
                { "name": "video-generate", "score": 74 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // HunyuanVideo 1.5 (Tencent, лицензия Tencent Hunyuan Community): 720p из коробки,
        // самая нетребовательная к видеопамяти из трёх больших открытых линеек. Второй
        // текстовый энкодер (byt5 glyphxl) отвечает за НАДПИСИ В КАДРЕ — поэтому в графе
        // DualCLIPLoader, а не CLIPLoader.
        ("6f1a45e0-0d31-4c65-9a01-000000000050", "HunyuanVideo-1.5-720p-T2V",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "hunyuanvideo15_720p_t2v",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000050.json",
              "params": {
                "width": 1280,
                "height": 720,
                "length": 121,
                "steps": 20,
                "negative": "",
                "timeoutMinutes": 240
              },
              "install": {
                "group": "HunyuanVideo-1.5",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "hunyuanvideo1.5_720p_t2v_fp16.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/HunyuanVideo_1.5_repackaged/resolve/main/split_files/diffusion_models/hunyuanvideo1.5_720p_t2v_fp16.safetensors",
                    "size": 16653368128,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "qwen_2.5_vl_7b_fp8_scaled.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/HunyuanVideo_1.5_repackaged/resolve/main/split_files/text_encoders/qwen_2.5_vl_7b_fp8_scaled.safetensors",
                    "size": 9384670680,
                    "category": "text_encoders"
                  },
                  {
                    "name": "byt5_small_glyphxl_fp16.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/HunyuanVideo_1.5_repackaged/resolve/main/split_files/text_encoders/byt5_small_glyphxl_fp16.safetensors",
                    "size": 438643184,
                    "category": "text_encoders"
                  },
                  {
                    "name": "hunyuanvideo15_vae_fp16.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/HunyuanVideo_1.5_repackaged/resolve/main/split_files/vae/hunyuanvideo15_vae_fp16.safetensors",
                    "size": 2521292758,
                    "category": "vae"
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": {
                  "kind": "workflow",
                  "node": "LoraLoaderModelOnly",
                  "placeholder": "{lora}",
                  "strengthPlaceholder": "{loraStrength}",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".safetensors"]
                },
                "train": {
                  "kind": "process",
                  "docUrl": "https://github.com/kohya-ss/musubi-tuner/blob/main/docs/hunyuan_video_1_5.md",
                  "packages": ["musubi-tuner", "python"],
                  "dataset": {
                    "kind": "dir",
                    "path": "lora/{object}/dataset",
                    "captions": "txt",
                    "minItems": 10,
                    "maxItems": 60,
                    "width": 1280,
                    "height": 720,
                    "maxKb": 0,
                    "formats": ["png"]
                  },
                  "files": [
                    {
                      "path": "lora/{object}/dataset.toml",
                      "text": [
                        "# AI2P: written from the model profile before every training run.",
                        "# Edit it in the model profile, not here: it is overwritten each time.",
                        "[general]",
                        "resolution = [{width}, {height}]",
                        "caption_extension = \".txt\"",
                        "batch_size = 1",
                        "enable_bucket = true",
                        "bucket_no_upscale = false",
                        "",
                        "[[datasets]]",
                        "image_directory = '{dataset}'",
                        "cache_directory = '{output}\\cache'",
                        "num_repeats = 1"
                      ]
                    },
                    {
                      "path": "lora/{object}/train.cmd",
                      "text": [
                        "@echo off",
                        "rem AI2P: LoRA training for HunyuanVideo 1.5 via musubi-tuner (hv_1_5_train_network.py).",
                        "rem Written from the model profile before every run - edit it in the model.",
                        "setlocal enableextensions",
                        "set \"TRAIN={package:musubi-tuner:hv_1_5_train_network.py}\"",
                        "for %%I in (\"%TRAIN%\") do set \"SCRIPTS=%%~dpI\"",
                        "set \"ROOT=%SCRIPTS%\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" set \"ROOT=%SCRIPTS%\"",
                        "set \"VENV=%ROOT%.venv\"",
                        "set \"PY=%VENV%\\Scripts\\python.exe\"",
                        "set \"OUT={output}\"",
                        "set \"BASE={groupDir}\\train\"",
                        "set \"DIT=%BASE%\\transformer\\720p_t2v\\diffusion_pytorch_model.safetensors\"",
                        "set \"TE=%BASE%\\split_files\\text_encoders\\qwen_2.5_vl_7b.safetensors\"",
                        "if not exist \"%OUT%\" mkdir \"%OUT%\"",
                        "if not exist \"%PY%\" call :setup",
                        "if errorlevel 1 exit /b 1",
                        "rem Generation runs on the fp16 repack and the fp8 text encoder; musubi-tuner",
                        "rem wants the original bf16 DiT and the full encoder, so they are fetched once.",
                        "if not exist \"%DIT%\" call :base",
                        "if errorlevel 1 exit /b 1",
                        "if not exist \"%TE%\" call :te",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching latents",
                        "\"%PY%\" \"%SCRIPTS%hv_1_5_cache_latents.py\" --dataset_config \"%~dp0dataset.toml\" --vae \"{model:hunyuanvideo15_vae_fp16.safetensors}\"",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching text encoder outputs",
                        "\"%PY%\" \"%SCRIPTS%hv_1_5_cache_text_encoder_outputs.py\" --dataset_config \"%~dp0dataset.toml\" --text_encoder \"%TE%\" --byt5 \"{model:byt5_small_glyphxl_fp16.safetensors}\" --batch_size 1",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] training",
                        "\"%VENV%\\Scripts\\accelerate.exe\" launch --num_cpu_threads_per_process 1 --mixed_precision bf16 \"%SCRIPTS%hv_1_5_train_network.py\" --dit \"%DIT%\" --vae \"{model:hunyuanvideo15_vae_fp16.safetensors}\" --text_encoder \"%TE%\" --byt5 \"{model:byt5_small_glyphxl_fp16.safetensors}\" --dataset_config \"%~dp0dataset.toml\" --task t2v --sdpa --mixed_precision bf16 --timestep_sampling shift --weighting_scheme none --discrete_flow_shift 2.0 --optimizer_type adamw8bit --learning_rate 1e-4 --gradient_checkpointing --max_data_loader_n_workers 2 --persistent_data_loader_workers --network_module networks.lora_hv_1_5 --network_dim 32 --max_train_steps {steps} --save_every_n_epochs 1 --seed 42 --output_dir \"%OUT%\" --output_name {object}",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] done",
                        "exit /b 0",
                        "",
                        ":setup",
                        "echo [AI2P] preparing the training environment",
                        "set \"PYEXE={package:python:python.exe}\"",
                        "if defined AI2P_LORA_PYTHON set \"PYEXE=%AI2P_LORA_PYTHON%\"",
                        "set \"TORCH_INDEX=https://download.pytorch.org/whl/cu124\"",
                        "if defined AI2P_LORA_TORCH_INDEX set \"TORCH_INDEX=%AI2P_LORA_TORCH_INDEX%\"",
                        "\"%PYEXE%\" -m venv \"%VENV%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot create the virtual environment. Reinstall the model (it brings Python 3.10-3.12), or point AI2P_LORA_PYTHON at your own Python.",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install --upgrade pip",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install torch torchvision --index-url %TORCH_INDEX%",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install -e \"%ROOT%.\"",
                        "if errorlevel 1 echo [AI2P] ERROR: musubi-tuner was not found in %ROOT% - reinstall the model (Settings - Models - Install), it brings the trainer.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0",
                        "",
                        ":base",
                        "echo [AI2P] fetching the original DiT for training (about 33 GB, once)",
                        "\"%PY%\" -m pip install --upgrade huggingface_hub",
                        "if errorlevel 1 exit /b 1",
                        "\"%VENV%\\Scripts\\hf.exe\" download tencent/HunyuanVideo-1.5 transformer/720p_t2v/diffusion_pytorch_model.safetensors --local-dir \"%BASE%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot download the DiT. Check the network, or put the file under the path above by hand.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0",
                        "",
                        ":te",
                        "echo [AI2P] fetching the full text encoder for training (about 17 GB, once)",
                        "\"%PY%\" -m pip install --upgrade huggingface_hub",
                        "if errorlevel 1 exit /b 1",
                        "\"%VENV%\\Scripts\\hf.exe\" download Comfy-Org/HunyuanVideo_1.5_repackaged split_files/text_encoders/qwen_2.5_vl_7b.safetensors --local-dir \"%BASE%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot download the text encoder. Check the network, or put the file under the path above by hand.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0"
                      ]
                    }
                  ],
                  "start": {
                    "kind": "process",
                    "command": "train.cmd",
                    "workDir": "lora/{object}",
                    "steps": 2000
                  },
                  "wait": {
                    "kind": "process",
                    "timeoutMinutes": 720
                  },
                  "result": {
                    "kind": "file",
                    "path": "lora/{object}/out/{object}.safetensors",
                    "target": "loras/{object}.safetensors"
                  }
                }
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "hunyuanvideo15_720p_t2v",
              "inputs":  ["text/plain"],
              "outputs": ["video/*"],
              "skills":  [
                { "name": "video-generate", "score": 79 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // LTX-2.5 (Lightricks, LTX-2 Community License) — ЕДИНСТВЕННАЯ открытая модель с
        // нативной парой «видео + звук» в один проход: звук считается своим латентом рядом
        // с видео и попадает в тот же mp4. Число шагов у неё задают сигмы (модель
        // дистиллированная), поэтому steps профайла в графе не участвует и стоит для сводки.
        //
        // ЧЕГО ЖДАТЬ ЧЕЛОВЕКУ: репозиторий весов ЗАКРЫТ соглашением (gated) — HuggingFace
        // отдаёт файлы только после принятия лицензии, и наш загрузчик, который ходит без
        // токена, получит 401 (проверено HEAD-запросом 27.08.2026). Поэтому четыре файла
        // манифеста кладутся в каталог группы руками; размеры в манифесте настоящие (взяты
        // из метаданных репозитория), и по ним установка считает модель установленной.
        ("6f1a45e0-0d31-4c65-9a01-000000000051", "LTX-2.5",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "ltx_2_5_distilled",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000051.json",
              "params": {
                "width": 1280,
                "height": 704,
                "length": 121,
                "steps": 8,
                "negative": "",
                "timeoutMinutes": 240
              },
              "install": {
                "group": "LTX-2.5",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "ltx-2.5-22b-distilled-transformer-comfy-int8-convrot.safetensors",
                    "url": "https://huggingface.co/Lightricks/LTX-2.5/resolve/main/diffusion_models/ltx-2.5-22b-distilled-transformer-comfy-int8-convrot.safetensors",
                    "size": 21504034224,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "gemma4-12b-with-proj-ltx-2.5-comfy-int8-convrot.safetensors",
                    "url": "https://huggingface.co/Lightricks/LTX-2.5/resolve/main/text_encoders/gemma4-12b-with-proj-ltx-2.5-comfy-int8-convrot.safetensors",
                    "size": 15372969374,
                    "category": "text_encoders"
                  },
                  {
                    "name": "ltx-2.5-video-vae-bf16.safetensors",
                    "url": "https://huggingface.co/Lightricks/LTX-2.5/resolve/main/vae/ltx-2.5-video-vae-bf16.safetensors",
                    "size": 1472223346,
                    "category": "vae"
                  },
                  {
                    "name": "ltx-2.5-audio-vae-bf16.safetensors",
                    "url": "https://huggingface.co/Lightricks/LTX-2.5/resolve/main/vae/ltx-2.5-audio-vae-bf16.safetensors",
                    "size": 364866540,
                    "category": "vae"
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": {
                  "kind": "workflow",
                  "node": "LoraLoaderModelOnly",
                  "placeholder": "{lora}",
                  "strengthPlaceholder": "{loraStrength}",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".safetensors"]
                },
                "train": {
                  "kind": "external",
                  "docUrl": "https://github.com/Lightricks/LTX-Video-Trainer",
                  "packages": [],
                  "dataset": {
                    "kind": "dir",
                    "path": "lora/{object}/dataset",
                    "captions": "txt",
                    "minItems": 10,
                    "maxItems": 60,
                    "width": 1280,
                    "height": 704,
                    "maxKb": 0,
                    "formats": ["png"]
                  },
                  "files": [],
                  "start": {
                    "kind": "process",
                    "command": "",
                    "workDir": "lora/{object}",
                    "steps": 2000
                  },
                  "wait": {
                    "kind": "process",
                    "timeoutMinutes": 720
                  },
                  "result": {
                    "kind": "file",
                    "path": "lora/{object}/out/{object}.safetensors",
                    "target": "loras/{object}.safetensors"
                  }
                }
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "ltx_2_5_distilled",
              "inputs":  ["text/plain"],
              "outputs": ["video/*"],
              "skills":  [
                { "name": "video-generate", "score": 85 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // ── ЛИНЕЙКА ЛОКАЛЬНЫХ МОДЕЛЕЙ ИЗОБРАЖЕНИЙ, ВТОРАЯ ОЧЕРЕДЬ (T-19-S0) ───────────
        // Первые три записи завёл T-241 (Z-Image-Turbo, Qwen-Image 2512 и её правка);
        // здесь дописаны FLUX.2, Kandinsky 5.0 Image Lite и нижняя ступень для слабого
        // железа — SDXL 1.0 и SD 3.5 Large.
        //
        // Двух моделей §6.2 отчёта T-213 здесь НЕТ намеренно, и это не забывчивость:
        //  * FLUX.2 [klein] 9B — лицензия у него НЕ Apache 2.0 (та только у 4B), а FLUX
        //    Non-Commercial License v2.1, и репозитории весов ЗАКРЫТЫ: resolve отвечает
        //    401 без токена (проверено 27.08.2026 на base-9B и base-9b-fp8, при том что
        //    4B из той же организации отдаётся свободно). Манифест, который заведомо не
        //    качается, хуже отсутствия записи: модель навсегда осталась бы «не
        //    установлена» и неактивной (T-4-S0);
        //  * Kandinsky 5.0 Image Editing (I2I-Lite) — веса есть и открыты
        //    (kandinskylab/Kandinsky-5.0-I2I-Lite, MIT, 12 ГБ), но ComfyUI знает у этого
        //    семейства только ТЕКСТ→КАРТИНКА (тип токенизатора kandinsky5_image в
        //    DualCLIPLoader), официального шаблона image_kandinsky5_i2i у Comfy-Org нет
        //    вовсе, а вендорский граф идёт своим custom-node.
        //
        // FLUX.2 [klein] 4B (Black Forest Labs, Apache 2.0) — 4 млрд параметров, самая
        // лёгкая из FLUX.2 и ЕДИНСТВЕННАЯ в семействе со свободной лицензией. Workflow
        // собран из официального шаблона Comfy-Org image_flux2_klein_text_to_image, взята
        // ветка БАЗОВЫХ весов (20 шагов, cfg 5, настоящий негативный промпт), а не
        // дистиллята (4 шага, cfg 1): у дистиллята свой файл весов, и негатив там не
        // действует. VAE берётся flux2-vae.safetensors — так его называет сам репак
        // Comfy-Org; шаблон подставляет альтернативный full_encoder_small_decoder,
        // которого Comfy-Org не раздаёт вовсе.
        ("6f1a45e0-0d31-4c65-9a01-000000000070", "FLUX.2-klein-4B",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "flux2_klein_4b",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000070.json",
              "params": {
                "width": 1024,
                "height": 1024,
                "length": 1,
                "steps": 20,
                "negative": "",
                "timeoutMinutes": 60
              },
              "install": {
                "group": "FLUX.2-klein-4B",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "flux-2-klein-base-4b.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/vae-text-encorder-for-flux-klein-4b/resolve/main/split_files/diffusion_models/flux-2-klein-base-4b.safetensors",
                    "size": 7751105712,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "qwen_3_4b.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/vae-text-encorder-for-flux-klein-4b/resolve/main/split_files/text_encoders/qwen_3_4b.safetensors",
                    "size": 8044982048,
                    "category": "text_encoders"
                  },
                  {
                    "name": "flux2-vae.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/vae-text-encorder-for-flux-klein-4b/resolve/main/split_files/vae/flux2-vae.safetensors",
                    "size": 336211292,
                    "category": "vae"
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": {
                  "kind": "workflow",
                  "node": "LoraLoaderModelOnly",
                  "placeholder": "{lora}",
                  "strengthPlaceholder": "{loraStrength}",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".safetensors"]
                },
                "train": {
                  "kind": "process",
                  "docUrl": "https://github.com/kohya-ss/musubi-tuner/blob/main/docs/flux_2.md",
                  "packages": ["musubi-tuner", "python"],
                  "dataset": {
                    "kind": "dir",
                    "path": "lora/{object}/dataset",
                    "captions": "txt",
                    "minItems": 10,
                    "maxItems": 60,
                    "width": 1024,
                    "height": 1024,
                    "maxKb": 0,
                    "formats": ["png"]
                  },
                  "files": [
                    {
                      "path": "lora/{object}/dataset.toml",
                      "text": [
                        "# AI2P: written from the model profile before every training run.",
                        "# Edit it in the model profile, not here: it is overwritten each time.",
                        "[general]",
                        "resolution = [{width}, {height}]",
                        "caption_extension = \".txt\"",
                        "batch_size = 1",
                        "enable_bucket = true",
                        "bucket_no_upscale = false",
                        "",
                        "[[datasets]]",
                        "image_directory = '{dataset}'",
                        "cache_directory = '{output}\\cache'",
                        "num_repeats = 1"
                      ]
                    },
                    {
                      "path": "lora/{object}/train.cmd",
                      "text": [
                        "@echo off",
                        "rem AI2P: LoRA training for FLUX.2 [klein] 4B via musubi-tuner (flux_2_train_network.py).",
                        "rem Written from the model profile before every run - edit it in the model.",
                        "rem Unlike Z-Image and Qwen-Image, nothing extra is downloaded here: musubi-tuner",
                        "rem trains on the very base checkpoint that AI2P installs for generation.",
                        "setlocal enableextensions",
                        "set \"TRAIN={package:musubi-tuner:flux_2_train_network.py}\"",
                        "for %%I in (\"%TRAIN%\") do set \"SCRIPTS=%%~dpI\"",
                        "set \"ROOT=%SCRIPTS%\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" set \"ROOT=%SCRIPTS%\"",
                        "set \"VENV=%ROOT%.venv\"",
                        "set \"PY=%VENV%\\Scripts\\python.exe\"",
                        "set \"OUT={output}\"",
                        "if not exist \"%OUT%\" mkdir \"%OUT%\"",
                        "if not exist \"%PY%\" call :setup",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching latents",
                        "\"%PY%\" \"%SCRIPTS%flux_2_cache_latents.py\" --dataset_config \"%~dp0dataset.toml\" --vae \"{model:flux2-vae.safetensors}\" --model_version klein-base-4b",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching text encoder outputs",
                        "\"%PY%\" \"%SCRIPTS%flux_2_cache_text_encoder_outputs.py\" --dataset_config \"%~dp0dataset.toml\" --text_encoder \"{model:qwen_3_4b.safetensors}\" --batch_size 1 --fp8_text_encoder --model_version klein-base-4b",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] training",
                        "\"%VENV%\\Scripts\\accelerate.exe\" launch --num_cpu_threads_per_process 1 --mixed_precision bf16 \"%SCRIPTS%flux_2_train_network.py\" --model_version klein-base-4b --dit \"{model:flux-2-klein-base-4b.safetensors}\" --vae \"{model:flux2-vae.safetensors}\" --text_encoder \"{model:qwen_3_4b.safetensors}\" --dataset_config \"%~dp0dataset.toml\" --sdpa --mixed_precision bf16 --timestep_sampling flux2_shift --weighting_scheme none --optimizer_type adamw8bit --learning_rate 1e-4 --fp8_base --fp8_scaled --gradient_checkpointing --max_data_loader_n_workers 2 --persistent_data_loader_workers --network_module networks.lora_flux_2 --network_dim 32 --max_train_steps {steps} --save_every_n_epochs 1 --seed 42 --output_dir \"%OUT%\" --output_name {object}",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] done",
                        "exit /b 0",
                        "",
                        ":setup",
                        "echo [AI2P] preparing the training environment",
                        "set \"PYEXE={package:python:python.exe}\"",
                        "if defined AI2P_LORA_PYTHON set \"PYEXE=%AI2P_LORA_PYTHON%\"",
                        "set \"TORCH_INDEX=https://download.pytorch.org/whl/cu124\"",
                        "if defined AI2P_LORA_TORCH_INDEX set \"TORCH_INDEX=%AI2P_LORA_TORCH_INDEX%\"",
                        "\"%PYEXE%\" -m venv \"%VENV%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot create the virtual environment. Reinstall the model (it brings Python 3.10-3.12), or point AI2P_LORA_PYTHON at your own Python.",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install --upgrade pip",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install torch torchvision --index-url %TORCH_INDEX%",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install -e \"%ROOT%.\"",
                        "if errorlevel 1 echo [AI2P] ERROR: musubi-tuner was not found in %ROOT% - reinstall the model (Settings - Models - Install), it brings the trainer.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0"
                      ]
                    }
                  ],
                  "start": {
                    "kind": "process",
                    "command": "train.cmd",
                    "workDir": "lora/{object}",
                    "steps": 2000
                  },
                  "wait": {
                    "kind": "process",
                    "timeoutMinutes": 720
                  },
                  "result": {
                    "kind": "file",
                    "path": "lora/{object}/out/{object}.safetensors",
                    "target": "loras/{object}.safetensors"
                  }
                }
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "flux2_klein_4b",
              "inputs":  ["text/plain"],
              "outputs": ["image/*"],
              "skills":  [
                { "name": "image-generate", "score": 88 },
                { "name": "image-photo",    "score": 87 },
                { "name": "image-concept",  "score": 86 },
                { "name": "image-text",     "score": 80 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // FLUX.2 [klein] 4B, ПРАВКА КАРТИНКИ. Веса те же самые — модель одна и умеет оба
        // режима, — поэтому группа установки общая с записью выше: поставив одну, вторую
        // качать не придётся. Разница только в графе: официальный шаблон Comfy-Org
        // image_flux2_klein_image_edit_4b_base заводит исходное изображение узлами
        // ImageScaleToTotalPixels → VAEEncode → ReferenceLatent (ссылочный латент кладётся
        // и в положительное условие, и в отрицательное), а размер кадра берёт у самой
        // картинки узлом GetImageSize — поэтому {width}/{height} в этом шаблоне не нужны.
        ("6f1a45e0-0d31-4c65-9a01-000000000071", "FLUX.2-klein-4B-Edit",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "flux2_klein_4b_edit",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000071.json",
              "params": {
                "width": 1024,
                "height": 1024,
                "length": 1,
                "steps": 20,
                "negative": "",
                "timeoutMinutes": 60
              },
              "install": {
                "group": "FLUX.2-klein-4B",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "flux-2-klein-base-4b.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/vae-text-encorder-for-flux-klein-4b/resolve/main/split_files/diffusion_models/flux-2-klein-base-4b.safetensors",
                    "size": 7751105712,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "qwen_3_4b.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/vae-text-encorder-for-flux-klein-4b/resolve/main/split_files/text_encoders/qwen_3_4b.safetensors",
                    "size": 8044982048,
                    "category": "text_encoders"
                  },
                  {
                    "name": "flux2-vae.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/vae-text-encorder-for-flux-klein-4b/resolve/main/split_files/vae/flux2-vae.safetensors",
                    "size": 336211292,
                    "category": "vae"
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": {
                  "kind": "workflow",
                  "node": "LoraLoaderModelOnly",
                  "placeholder": "{lora}",
                  "strengthPlaceholder": "{loraStrength}",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".safetensors"]
                },
                "train": {
                  "kind": "process",
                  "docUrl": "https://github.com/kohya-ss/musubi-tuner/blob/main/docs/flux_2.md",
                  "packages": ["musubi-tuner", "python"],
                  "dataset": {
                    "kind": "dir",
                    "path": "lora/{object}/dataset",
                    "captions": "txt",
                    "minItems": 10,
                    "maxItems": 60,
                    "width": 1024,
                    "height": 1024,
                    "maxKb": 0,
                    "formats": ["png"]
                  },
                  "files": [
                    {
                      "path": "lora/{object}/dataset.toml",
                      "text": [
                        "# AI2P: written from the model profile before every training run.",
                        "# Edit it in the model profile, not here: it is overwritten each time.",
                        "[general]",
                        "resolution = [{width}, {height}]",
                        "caption_extension = \".txt\"",
                        "batch_size = 1",
                        "enable_bucket = true",
                        "bucket_no_upscale = false",
                        "",
                        "[[datasets]]",
                        "image_directory = '{dataset}'",
                        "cache_directory = '{output}\\cache'",
                        "num_repeats = 1"
                      ]
                    },
                    {
                      "path": "lora/{object}/train.cmd",
                      "text": [
                        "@echo off",
                        "rem AI2P: LoRA training for FLUX.2 [klein] 4B via musubi-tuner (flux_2_train_network.py).",
                        "rem Written from the model profile before every run - edit it in the model.",
                        "rem The adapter is trained on the base checkpoint and works in both modes,",
                        "rem text-to-image and image editing: it is the same model behind them.",
                        "setlocal enableextensions",
                        "set \"TRAIN={package:musubi-tuner:flux_2_train_network.py}\"",
                        "for %%I in (\"%TRAIN%\") do set \"SCRIPTS=%%~dpI\"",
                        "set \"ROOT=%SCRIPTS%\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\..\\\") do set \"ROOT=%%~fI\"",
                        "if not exist \"%ROOT%pyproject.toml\" set \"ROOT=%SCRIPTS%\"",
                        "set \"VENV=%ROOT%.venv\"",
                        "set \"PY=%VENV%\\Scripts\\python.exe\"",
                        "set \"OUT={output}\"",
                        "if not exist \"%OUT%\" mkdir \"%OUT%\"",
                        "if not exist \"%PY%\" call :setup",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching latents",
                        "\"%PY%\" \"%SCRIPTS%flux_2_cache_latents.py\" --dataset_config \"%~dp0dataset.toml\" --vae \"{model:flux2-vae.safetensors}\" --model_version klein-base-4b",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] caching text encoder outputs",
                        "\"%PY%\" \"%SCRIPTS%flux_2_cache_text_encoder_outputs.py\" --dataset_config \"%~dp0dataset.toml\" --text_encoder \"{model:qwen_3_4b.safetensors}\" --batch_size 1 --fp8_text_encoder --model_version klein-base-4b",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] training",
                        "\"%VENV%\\Scripts\\accelerate.exe\" launch --num_cpu_threads_per_process 1 --mixed_precision bf16 \"%SCRIPTS%flux_2_train_network.py\" --model_version klein-base-4b --dit \"{model:flux-2-klein-base-4b.safetensors}\" --vae \"{model:flux2-vae.safetensors}\" --text_encoder \"{model:qwen_3_4b.safetensors}\" --dataset_config \"%~dp0dataset.toml\" --sdpa --mixed_precision bf16 --timestep_sampling flux2_shift --weighting_scheme none --optimizer_type adamw8bit --learning_rate 1e-4 --fp8_base --fp8_scaled --gradient_checkpointing --max_data_loader_n_workers 2 --persistent_data_loader_workers --network_module networks.lora_flux_2 --network_dim 32 --max_train_steps {steps} --save_every_n_epochs 1 --seed 42 --output_dir \"%OUT%\" --output_name {object}",
                        "if errorlevel 1 exit /b 1",
                        "echo [AI2P] done",
                        "exit /b 0",
                        "",
                        ":setup",
                        "echo [AI2P] preparing the training environment",
                        "set \"PYEXE={package:python:python.exe}\"",
                        "if defined AI2P_LORA_PYTHON set \"PYEXE=%AI2P_LORA_PYTHON%\"",
                        "set \"TORCH_INDEX=https://download.pytorch.org/whl/cu124\"",
                        "if defined AI2P_LORA_TORCH_INDEX set \"TORCH_INDEX=%AI2P_LORA_TORCH_INDEX%\"",
                        "\"%PYEXE%\" -m venv \"%VENV%\"",
                        "if errorlevel 1 echo [AI2P] ERROR: cannot create the virtual environment. Reinstall the model (it brings Python 3.10-3.12), or point AI2P_LORA_PYTHON at your own Python.",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install --upgrade pip",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install torch torchvision --index-url %TORCH_INDEX%",
                        "if errorlevel 1 exit /b 1",
                        "\"%PY%\" -m pip install -e \"%ROOT%.\"",
                        "if errorlevel 1 echo [AI2P] ERROR: musubi-tuner was not found in %ROOT% - reinstall the model (Settings - Models - Install), it brings the trainer.",
                        "if errorlevel 1 exit /b 1",
                        "exit /b 0"
                      ]
                    }
                  ],
                  "start": {
                    "kind": "process",
                    "command": "train.cmd",
                    "workDir": "lora/{object}",
                    "steps": 2000
                  },
                  "wait": {
                    "kind": "process",
                    "timeoutMinutes": 720
                  },
                  "result": {
                    "kind": "file",
                    "path": "lora/{object}/out/{object}.safetensors",
                    "target": "loras/{object}.safetensors"
                  }
                }
              },
              "refImage": {
                "kind": "upload",
                "placeholder": "{image}",
                "uploadPath": "/upload/image",
                "field": "image",
                "maxCount": 1,
                "formats": ["image/png", "image/jpeg", "image/webp"],
                "required": true
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "flux2_klein_4b_edit",
              "inputs":  ["text/plain", "image/*"],
              "outputs": ["image/*"],
              "skills":  [
                { "name": "image-edit",     "score": 86 },
                { "name": "image-inpaint",  "score": 83 },
                { "name": "image-generate", "score": 80 },
                { "name": "image-text",     "score": 78 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // Kandinsky 5.0 Image Lite (Kandinsky Lab, MIT) — 6 млрд параметров, КИРИЛЛИЦА
        // в кадре и понимание русских реалий; та же семья, что уже стоящие в справочнике
        // видео-записи Kandinsky, поэтому текстовые энкодеры у них общие (qwen2.5-vl + clip_l).
        // ВАЖНО про поиск весов: отчёт T-241 записал, что «весов Image Lite на HuggingFace
        // найти не удалось». Они есть — kandinskylab/Kandinsky-5.0-T2I-Lite, файл
        // model/kandinsky5lite_t2i.safetensors (12,04 ГБ, ровно то имя, которое ждёт
        // официальный шаблон image_kandinsky5_t2i); поиск по слову «kandinsky» их не
        // показывает, они видны только полным списком моделей организации (проверено
        // 27.08.2026). VAE у образа не свой, а ФЛАКСОВЫЙ ae.safetensors — так и в шаблоне
        // Comfy-Org, и в инструкции вендора (weights/flux/vae → ComfyUI/models/vae).
        // Обучения адаптера НЕТ: musubi-tuner прямо пишет в docs/kandinsky5.md, что
        // Image Lite не поддержан, — поэтому "train": "external", а применение готового
        // адаптера работает как у всех записей ComfyUI.
        ("6f1a45e0-0d31-4c65-9a01-000000000072", "Kandinsky-5.0-Image-Lite",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "kandinsky5lite_t2i",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000072.json",
              "params": {
                "width": 1024,
                "height": 1024,
                "length": 1,
                "steps": 50,
                "negative": "",
                "timeoutMinutes": 90
              },
              "install": {
                "group": "Kandinsky-5-Image",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "kandinsky5lite_t2i.safetensors",
                    "url": "https://huggingface.co/kandinskylab/Kandinsky-5.0-T2I-Lite/resolve/main/model/kandinsky5lite_t2i.safetensors",
                    "size": 12044328816,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "qwen_2.5_vl_7b_fp8_scaled.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/HunyuanVideo_1.5_repackaged/resolve/main/split_files/text_encoders/qwen_2.5_vl_7b_fp8_scaled.safetensors",
                    "size": 9384670680,
                    "category": "text_encoders"
                  },
                  {
                    "name": "clip_l.safetensors",
                    "url": "https://huggingface.co/comfyanonymous/flux_text_encoders/resolve/main/clip_l.safetensors",
                    "size": 246144152,
                    "category": "text_encoders"
                  },
                  {
                    "name": "ae.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/z_image_turbo/resolve/main/split_files/vae/ae.safetensors",
                    "size": 335304388,
                    "category": "vae"
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": {
                  "kind": "workflow",
                  "node": "LoraLoaderModelOnly",
                  "placeholder": "{lora}",
                  "strengthPlaceholder": "{loraStrength}",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".safetensors"]
                },
                "train": {
                  "kind": "external",
                  "docUrl": "https://github.com/kohya-ss/musubi-tuner/blob/main/docs/kandinsky5.md",
                  "packages": [],
                  "dataset": {
                    "kind": "dir",
                    "path": "lora/{object}/dataset",
                    "captions": "txt",
                    "minItems": 10,
                    "maxItems": 60,
                    "width": 1024,
                    "height": 1024,
                    "maxKb": 0,
                    "formats": ["png"]
                  },
                  "files": [],
                  "start": {
                    "kind": "process",
                    "command": "",
                    "workDir": "lora/{object}",
                    "steps": 2000
                  },
                  "wait": {
                    "kind": "process",
                    "timeoutMinutes": 720
                  },
                  "result": {
                    "kind": "file",
                    "path": "lora/{object}/out/{object}.safetensors",
                    "target": "loras/{object}.safetensors"
                  }
                }
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "kandinsky5lite_t2i",
              "inputs":  ["text/plain"],
              "outputs": ["image/*"],
              "skills":  [
                { "name": "image-text",     "score": 88 },
                { "name": "image-generate", "score": 84 },
                { "name": "image-concept",  "score": 83 },
                { "name": "image-photo",    "score": 82 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // ── НИЖНЯЯ СТУПЕНЬ: старые модели Stability для слабого железа ────────────────
        // Обе ставятся ОДНИМ файлом: у них полный чекпойнт (CheckpointLoaderSimple отдаёт
        // сразу MODEL, CLIP и VAE), поэтому категория весов не diffusion_models, а
        // checkpoints. Обучение адаптера у обеих «external»: musubi-tuner их не знает
        // вовсе — это вотчина соседнего инструмента того же автора, kohya-ss/sd-scripts,
        // а вот ПРИМЕНЕНИЕ готового адаптера работает как у всех записей ComfyUI (узел
        // LoraLoaderModelOnly вставляется и после загрузчика чекпойнта, T-14-S1).
        //
        // SD 3.5 Large (Stability AI, 8 млрд): fp8-сборка Comfy-Org, ТЕКСТОВЫЕ ЭНКОДЕРЫ
        // ВНУТРИ файла (так и написано в её карточке), поэтому в манифесте одна строка.
        // Лицензия НЕ свободная: Stability AI Community — бесплатна, пока годовая выручка
        // организации меньше 1 млн долларов, дальше нужна отдельная Enterprise-лицензия.
        ("6f1a45e0-0d31-4c65-9a01-000000000073", "SD-3.5-Large",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "sd3_5_large",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000073.json",
              "params": {
                "width": 1024,
                "height": 1024,
                "length": 1,
                "steps": 20,
                "negative": "",
                "timeoutMinutes": 60
              },
              "install": {
                "group": "SD-3.5",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "sd3.5_large_fp8_scaled.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/stable-diffusion-3.5-fp8/resolve/main/sd3.5_large_fp8_scaled.safetensors",
                    "size": 14934922866,
                    "category": "checkpoints"
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": {
                  "kind": "workflow",
                  "node": "LoraLoaderModelOnly",
                  "placeholder": "{lora}",
                  "strengthPlaceholder": "{loraStrength}",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".safetensors"]
                },
                "train": {
                  "kind": "external",
                  "docUrl": "https://github.com/kohya-ss/sd-scripts",
                  "packages": [],
                  "dataset": {
                    "kind": "dir",
                    "path": "lora/{object}/dataset",
                    "captions": "txt",
                    "minItems": 10,
                    "maxItems": 60,
                    "width": 1024,
                    "height": 1024,
                    "maxKb": 0,
                    "formats": ["png"]
                  },
                  "files": [],
                  "start": {
                    "kind": "process",
                    "command": "",
                    "workDir": "lora/{object}",
                    "steps": 2000
                  },
                  "wait": {
                    "kind": "process",
                    "timeoutMinutes": 720
                  },
                  "result": {
                    "kind": "file",
                    "path": "lora/{object}/out/{object}.safetensors",
                    "target": "loras/{object}.safetensors"
                  }
                }
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "sd3_5_large",
              "inputs":  ["text/plain"],
              "outputs": ["image/*"],
              "skills":  [
                { "name": "image-generate", "score": 80 },
                { "name": "image-photo",    "score": 79 },
                { "name": "image-concept",  "score": 78 },
                { "name": "image-text",     "score": 70 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // SDXL 1.0 (Stability AI, CreativeML Open RAIL++-M) — самая нетребовательная запись
        // справочника: 6,9 ГБ одним файлом и 8 ГБ видеопамяти. Она здесь именно как нижняя
        // ступень: качество ниже всех соседей, текст в кадре не умеет вовсе, зато идёт на
        // железе, на котором FLUX.2 и Qwen-Image не запустятся. Граф — официальный шаблон
        // Comfy-Org image_sdxl_simple (25 шагов, cfg 7, dpmpp_2m/karras).
        // Адаптеров LoRA под SDXL в мире больше, чем под все остальные модели вместе, и все
        // они применяются здесь без изменений.
        ("6f1a45e0-0d31-4c65-9a01-000000000074", "SDXL-1.0",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "sdxl_base_1_0",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000074.json",
              "params": {
                "width": 1024,
                "height": 1024,
                "length": 1,
                "steps": 25,
                "negative": "",
                "timeoutMinutes": 60
              },
              "install": {
                "group": "SDXL-1.0",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "sd_xl_base_1.0.safetensors",
                    "url": "https://huggingface.co/stabilityai/stable-diffusion-xl-base-1.0/resolve/main/sd_xl_base_1.0.safetensors",
                    "size": 6938078334,
                    "category": "checkpoints"
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": {
                  "kind": "workflow",
                  "node": "LoraLoaderModelOnly",
                  "placeholder": "{lora}",
                  "strengthPlaceholder": "{loraStrength}",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".safetensors"]
                },
                "train": {
                  "kind": "external",
                  "docUrl": "https://github.com/kohya-ss/sd-scripts",
                  "packages": [],
                  "dataset": {
                    "kind": "dir",
                    "path": "lora/{object}/dataset",
                    "captions": "txt",
                    "minItems": 10,
                    "maxItems": 60,
                    "width": 1024,
                    "height": 1024,
                    "maxKb": 0,
                    "formats": ["png"]
                  },
                  "files": [],
                  "start": {
                    "kind": "process",
                    "command": "",
                    "workDir": "lora/{object}",
                    "steps": 2000
                  },
                  "wait": {
                    "kind": "process",
                    "timeoutMinutes": 720
                  },
                  "result": {
                    "kind": "file",
                    "path": "lora/{object}/out/{object}.safetensors",
                    "target": "loras/{object}.safetensors"
                  }
                }
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "sdxl_base_1_0",
              "inputs":  ["text/plain"],
              "outputs": ["image/*"],
              "skills":  [
                { "name": "image-generate", "score": 70 },
                { "name": "image-concept",  "score": 70 },
                { "name": "image-photo",    "score": 68 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // FLUX.2 [dev] (Black Forest Labs, 32 млрд) — верх открытых весов по качеству и
        // ЕДИНСТВЕННАЯ запись справочника с НЕСВОБОДНОЙ лицензией на веса: FLUX Non-Commercial
        // License v2.1 разрешает только некоммерческое и непроизводственное использование,
        // на коммерческое нужна отдельная лицензия BFL. Заведена намеренно и с оговоркой в
        // документе — именно ради таких случаев в документах моделей есть раздел «Лицензия».
        // Технически она доступна: оригинальные репозитории BFL закрыты (401), но
        // перепаковка Comfy-Org отдаётся свободно, поэтому манифест рабочий.
        // Граф — официальный шаблон image_flux2_text_to_image, ветка БЕЗ turbo-LoRA
        // (в шаблоне выключатель стоит в «выкл»), поэтому лишнего файла адаптера не нужно.
        // Негативного промпта у dev нет вовсе: он дистиллирован под управляемую подсказку
        // (FluxGuidance + BasicGuider), поле "negative" профайла на него не влияет.
        // Обучение адаптера — "external": musubi-tuner умеет FLUX.2 [dev], но требует
        // ОРИГИНАЛЬНЫХ весов из закрытого репозитория (одиночный flux2-dev.safetensors и
        // разрезанный Mistral 3), а не перепаковки, которую ставит AI2P.
        ("6f1a45e0-0d31-4c65-9a01-000000000075", "FLUX.2-dev",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "flux2_dev",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000075.json",
              "params": {
                "width": 1024,
                "height": 1024,
                "length": 1,
                "steps": 20,
                "negative": "",
                "timeoutMinutes": 120
              },
              "install": {
                "group": "FLUX.2-dev",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "flux2_dev_fp8mixed.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/flux2-dev/resolve/main/split_files/diffusion_models/flux2_dev_fp8mixed.safetensors",
                    "size": 35455599592,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "mistral_3_small_flux2_fp8.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/flux2-dev/resolve/main/split_files/text_encoders/mistral_3_small_flux2_fp8.safetensors",
                    "size": 18034640095,
                    "category": "text_encoders"
                  },
                  {
                    "name": "flux2-vae.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/flux2-dev/resolve/main/split_files/vae/flux2-vae.safetensors",
                    "size": 336213556,
                    "category": "vae"
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "apply": {
                  "kind": "workflow",
                  "node": "LoraLoaderModelOnly",
                  "placeholder": "{lora}",
                  "strengthPlaceholder": "{loraStrength}",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".safetensors"]
                },
                "train": {
                  "kind": "external",
                  "docUrl": "https://github.com/kohya-ss/musubi-tuner/blob/main/docs/flux_2.md",
                  "packages": [],
                  "dataset": {
                    "kind": "dir",
                    "path": "lora/{object}/dataset",
                    "captions": "txt",
                    "minItems": 10,
                    "maxItems": 60,
                    "width": 1024,
                    "height": 1024,
                    "maxKb": 0,
                    "formats": ["png"]
                  },
                  "files": [],
                  "start": {
                    "kind": "process",
                    "command": "",
                    "workDir": "lora/{object}",
                    "steps": 2000
                  },
                  "wait": {
                    "kind": "process",
                    "timeoutMinutes": 720
                  },
                  "result": {
                    "kind": "file",
                    "path": "lora/{object}/out/{object}.safetensors",
                    "target": "loras/{object}.safetensors"
                  }
                }
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "flux2_dev",
              "inputs":  ["text/plain"],
              "outputs": ["image/*"],
              "skills":  [
                { "name": "image-generate", "score": 94 },
                { "name": "image-photo",    "score": 93 },
                { "name": "image-concept",  "score": 92 },
                { "name": "image-text",     "score": 90 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // --- локальные 3D-модели (T-20-S0, §6.5 отчёта T-213) ---
        // Общее для всех трёх: ComfyUI умеет их СВОИМИ узлами (comfy_extras/nodes_hunyuan3d.py,
        // nodes_trellis2.py, nodes_triposplat.py, nodes_mesh_postprocess.py, nodes_save_3d.py) —
        // чужих узлов ставить не нужно, а значит работает штатный пакет comfyui на Windows.
        // Оговорка §6.5 «Hunyuan3D 2.1 требует Linux + CUDA 12.4» относится к ОРИГИНАЛЬНОМУ
        // репозиторию Tencent (там своя CUDA-растеризация для запекания текстуры), а не к
        // реализации ComfyUI: она считает только ФОРМУ и обходится обычным torch.
        // Навык у всех один — 3d-image: SkillIo.For говорит, что 3d-generate и 3d-environment
        // берут на вход ТЕКСТ, а 3d-texture — готовую сетку; все три записи работают от
        // картинки, поэтому объявить их значило бы соврать подбору исполнителя.
        // LoRA: у публичных тренеров (musubi-tuner и прочие) 3D-архитектур нет вовсе —
        // отмечено честным отказом, а не пустой настройкой.
        ("6f1a45e0-0d31-4c65-9a01-000000000080", "Hunyuan3D-2.1",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "hunyuan3d_2_1",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000080.json",
              "params": {
                "width": 1024,
                "height": 1024,
                "length": 1,
                "steps": 30,
                "negative": "",
                "timeoutMinutes": 60
              },
              "install": {
                "group": "Hunyuan3D-2.1",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "hunyuan_3d_v2.1.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/hunyuan3D_2.1_repackaged/resolve/main/hunyuan_3d_v2.1.safetensors",
                    "size": 7365943290,
                    "category": "checkpoints"
                  }
                ]
              },
              "lora": {
                "supported": false,
                "reason": "no-lora"
              },
              "refImage": {
                "kind": "upload",
                "placeholder": "{image}",
                "uploadPath": "/upload/image",
                "field": "image",
                "maxCount": 1,
                "formats": ["image/png", "image/jpeg", "image/webp"],
                "required": true
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "hunyuan3d_2_1",
              "inputs":  ["image/*"],
              "outputs": ["model/glb"],
              "skills":  [
                { "name": "3d-image", "score": 86 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // TRELLIS 2 (Microsoft Research, MIT): единственная из трёх, кто выдаёт сетку С ЦВЕТОМ —
        // после формы идёт отдельная ступень текстуры (Trellis2TextureStage → цвета вокселей →
        // PaintMesh). Полная ветка официального шаблона с UV-развёрткой и запеканием карт PBR
        // (UnwrapMesh → BakeTextureFromVoxel/BakeNormalMapFromMesh/BakeAmbientOcclusion →
        // ApplyTextureToMesh) в шаблон НЕ перенесена намеренно: это ещё десяток узлов и
        // запекание в 2048–4096 точек, а проверить их живьём (видеокарта + 9 ГБ весов) нечем.
        // Кто захочет — дописывает их в свой workflow, узлы у ComfyUI все на месте.
        ("6f1a45e0-0d31-4c65-9a01-000000000081", "TRELLIS-2",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "trellis_2",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000081.json",
              "params": {
                "width": 1024,
                "height": 1024,
                "length": 1,
                "steps": 20,
                "negative": "",
                "timeoutMinutes": 90
              },
              "install": {
                "group": "TRELLIS-2",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "trellis_2_int8_convrot.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/TRELLIS.2/resolve/main/diffusion_models/trellis_2_int8_convrot.safetensors",
                    "size": 5253048192,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "trellis_2_shape_vae_bf16.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/TRELLIS.2/resolve/main/vae/trellis_2_shape_vae_bf16.safetensors",
                    "size": 1095844024,
                    "category": "vae"
                  },
                  {
                    "name": "trellis_2_texture_vae_bf16.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/TRELLIS.2/resolve/main/vae/trellis_2_texture_vae_bf16.safetensors",
                    "size": 948461364,
                    "category": "vae"
                  },
                  {
                    "name": "dino_v3_vit_l.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/TRELLIS.2/resolve/main/clip_vision/dino_v3_vit_l.safetensors",
                    "size": 1212559776,
                    "category": "clip_vision"
                  },
                  {
                    "name": "birefnet.safetensors",
                    "url": "https://huggingface.co/Comfy-Org/BiRefNet/resolve/main/background_removal/birefnet.safetensors",
                    "size": 444473596,
                    "category": "background_removal"
                  }
                ]
              },
              "lora": {
                "supported": false,
                "reason": "no-lora"
              },
              "refImage": {
                "kind": "upload",
                "placeholder": "{image}",
                "uploadPath": "/upload/image",
                "field": "image",
                "maxCount": 1,
                "formats": ["image/png", "image/jpeg", "image/webp"],
                "required": true
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "trellis_2",
              "inputs":  ["image/*"],
              "outputs": ["model/glb"],
              "skills":  [
                { "name": "3d-image", "score": 85 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // TripoSplat (VAST-AI, MIT): единственная местная запись, отдающая ГАУССОВЫ СПЛАТЫ,
        // а не сетку (.spz). Заведена потому, что §6.5 приписывал сплаты TRELLIS 2, а в
        // ComfyUI их считает именно эта модель: у trellis2 в ядре нет ни одного узла splat,
        // они лежат в nodes_triposplat.py и nodes_gaussian_splat.py. Навык всё тот же
        // 3d-image: 3d-environment по SkillIo идёт ОТ ТЕКСТА, а тут вход — картинка.
        ("6f1a45e0-0d31-4c65-9a01-000000000082", "TripoSplat",
            """
            {
              "_seed": 22,
              "provider": "comfyui",
              "model": "triposplat",
              "baseUrl": "http://127.0.0.1:8188",
              "secretRef": "",
              "launchCommand": "",
              "workflow": "models/workflow_6f1a45e0-0d31-4c65-9a01-000000000082.json",
              "params": {
                "width": 1024,
                "height": 1024,
                "length": 1,
                "steps": 20,
                "negative": "",
                "timeoutMinutes": 60
              },
              "install": {
                "group": "TripoSplat",
                "packages": ["comfyui"],
                "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
                "files": [
                  {
                    "name": "triposplat_fp16.safetensors",
                    "url": "https://huggingface.co/VAST-AI/TripoSplat/resolve/main/diffusion_models/triposplat_fp16.safetensors",
                    "size": 741106994,
                    "category": "diffusion_models"
                  },
                  {
                    "name": "dino_v3_vit_h.safetensors",
                    "url": "https://huggingface.co/VAST-AI/TripoSplat/resolve/main/clip_vision/dino_v3_vit_h.safetensors",
                    "size": 1681247696,
                    "category": "clip_vision"
                  },
                  {
                    "name": "flux2-vae.safetensors",
                    "url": "https://huggingface.co/VAST-AI/TripoSplat/resolve/main/vae/flux2-vae.safetensors",
                    "size": 336213556,
                    "category": "vae"
                  },
                  {
                    "name": "triposplat_vae_decoder_fp16.safetensors",
                    "url": "https://huggingface.co/VAST-AI/TripoSplat/resolve/main/vae/triposplat_vae_decoder_fp16.safetensors",
                    "size": 576148442,
                    "category": "vae"
                  },
                  {
                    "name": "birefnet.safetensors",
                    "url": "https://huggingface.co/VAST-AI/TripoSplat/resolve/main/background_removal/birefnet.safetensors",
                    "size": 444473596,
                    "category": "background_removal"
                  }
                ]
              },
              "lora": {
                "supported": false,
                "reason": "no-lora"
              },
              "refImage": {
                "kind": "upload",
                "placeholder": "{image}",
                "uploadPath": "/upload/image",
                "field": "image",
                "maxCount": 1,
                "formats": ["image/png", "image/jpeg", "image/webp"],
                "required": true
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "triposplat",
              "inputs":  ["image/*"],
              "outputs": ["model/3d"],
              "skills":  [
                { "name": "3d-image", "score": 80 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        // ---------------------------------------------------------------------------------
        // АКТУАЛИЗАЦИЯ СПРАВОЧНИКА ПО РЫНКУ (T-216-S0, сверка 11.09.2026). Девять записей,
        // вышедших после прошлого обхода производителей (T-241 и её подзадачи, 27.08.2026).
        // Идентификатор, длина контекста, цена и модальности КАЖДОЙ взяты запросом к
        // публичному каталогу: текстовые — GET https://openrouter.ai/api/v1/models
        // (443 записи на 11.09.2026), медийные — GET https://fal.ai/api/models (1500 записей)
        // плюс схема очереди эндпойнта (поля запроса и допустимые значения перечислений).
        // ---------------------------------------------------------------------------------

        // OpenAI GPT-6 Astra (каталог: openai/gpt-6-astra, 04.09.2026) — новое ПОКОЛЕНИЕ
        // вместо линейки 5.6. Отдельной записи для gpt-6-astra-pro нет намеренно: по описанию
        // каталога это ТА ЖЕ модель с reasoning.mode=pro, то есть вариант, а не версия.
        ("6f1a45e0-0d31-4c65-9a01-000000000083", "GPT-6-Astra",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "gpt-6-astra",
              "baseUrl": "https://api.openai.com/v1",
              "secretRef": "openai.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "gpt-6-astra",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 97 },
                { "name": "code-review",          "score": 96 },
                { "name": "code-debug",           "score": 96 },
                { "name": "code-test",            "score": 94 },
                { "name": "code-refactor",        "score": 95 },
                { "name": "text-write",           "score": 95 },
                { "name": "text-docs",            "score": 94 },
                { "name": "text-edit",            "score": 94 },
                { "name": "text-translate",       "score": 93 },
                { "name": "text-summarize",       "score": 96 },
                { "name": "analyze-requirements", "score": 96 },
                { "name": "analyze-plan",         "score": 96 },
                { "name": "analyze-data",         "score": 95 }
              ],
              "limits":  { "context": 1050000, "max_output": 128000 },
              "cost":    { "in_per_1m": 10.0, "out_per_1m": 50.0 }
            }
            """),

        // Anthropic Claude Fable 5.1 (каталог: anthropic/claude-fable-5.1, 01.09.2026).
        // Каталог подтверждает СУЩЕСТВОВАНИЕ, цену ($10/$50) и контекст; НАТИВНЫЙ id
        // получен по правилу самого производителя «точка → дефис» (claude-haiku-4.5 →
        // claude-haiku-4-5, так уже стоит у записи 011) и живым вызовом не проверен —
        // поэтому у записи есть запасная модель claude-fable-5: при 404 задание не сорвётся.
        ("6f1a45e0-0d31-4c65-9a01-000000000084", "Claude-Fable-5.1",
            """
            {
              "_seed": 22,
              "provider": "anthropic",
              "model": "claude-fable-5-1",
              "baseUrl": "",
              "secretRef": "anthropic.apiKey",
              "params": {
                "maxTokens": 16000,
                "effort": "high",
                "fallbacks": ["claude-fable-5", "claude-opus-5"]
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "claude-fable-5-1",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 99 },
                { "name": "code-review",          "score": 98 },
                { "name": "code-debug",           "score": 98 },
                { "name": "code-test",            "score": 96 },
                { "name": "code-refactor",        "score": 97 },
                { "name": "text-write",           "score": 96 },
                { "name": "text-docs",            "score": 96 },
                { "name": "text-edit",            "score": 95 },
                { "name": "text-translate",       "score": 94 },
                { "name": "text-summarize",       "score": 97 },
                { "name": "analyze-requirements", "score": 98 },
                { "name": "analyze-plan",         "score": 97 },
                { "name": "analyze-data",         "score": 96 }
              ],
              "limits":  { "context": 1000000, "max_output": 128000 },
              "cost":    { "in_per_1m": 10.0, "out_per_1m": 50.0 }
            }
            """),

        // Google Gemini 3.8 Flash (каталог: google/gemini-3.8-flash, 02.09.2026). Цена здесь
        // ОБЫЧНАЯ ($0,75/$3,75), а не половинная пакетная, которую по недосмотру записали
        // версии 3.7 — сверено с полем pricing каталога.
        ("6f1a45e0-0d31-4c65-9a01-000000000085", "Gemini-3.8-Flash",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "gemini-3.8-flash",
              "baseUrl": "https://generativelanguage.googleapis.com/v1beta/openai",
              "secretRef": "google.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "gemini-3.8-flash",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf", "audio/*", "video/*"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 86 },
                { "name": "code-review",          "score": 84 },
                { "name": "code-debug",           "score": 83 },
                { "name": "code-test",            "score": 83 },
                { "name": "code-refactor",        "score": 83 },
                { "name": "text-write",           "score": 84 },
                { "name": "text-docs",            "score": 83 },
                { "name": "text-edit",            "score": 82 },
                { "name": "text-translate",       "score": 86 },
                { "name": "text-summarize",       "score": 88 },
                { "name": "analyze-requirements", "score": 82 },
                { "name": "analyze-plan",         "score": 81 },
                { "name": "analyze-data",         "score": 83 }
              ],
              "limits":  { "context": 1048576, "max_output": 65536 },
              "cost":    { "in_per_1m": 0.75, "out_per_1m": 3.75 }
            }
            """),

        // Meta Muse Spark 1.3 (каталог: meta/muse-spark-1.3, 02.09.2026). Своего API у Meta
        // по-прежнему нет — подключение через шлюз OpenRouter, как у версии 1.2.
        ("6f1a45e0-0d31-4c65-9a01-000000000086", "Muse-Spark-1.3",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "meta/muse-spark-1.3",
              "baseUrl": "https://openrouter.ai/api/v1",
              "secretRef": "openrouter.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "meta/muse-spark-1.3",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf", "audio/*", "video/*"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 91 },
                { "name": "code-review",          "score": 89 },
                { "name": "code-debug",           "score": 88 },
                { "name": "code-test",            "score": 87 },
                { "name": "code-refactor",        "score": 88 },
                { "name": "text-write",           "score": 90 },
                { "name": "text-docs",            "score": 88 },
                { "name": "text-edit",            "score": 88 },
                { "name": "text-translate",       "score": 88 },
                { "name": "text-summarize",       "score": 91 },
                { "name": "analyze-requirements", "score": 89 },
                { "name": "analyze-plan",         "score": 88 },
                { "name": "analyze-data",         "score": 89 }
              ],
              "limits":  { "context": 1048576, "max_output": 65536 },
              "cost":    { "in_per_1m": 1.25, "out_per_1m": 4.25 }
            }
            """),

        // DeepSeek V4.1 Flash (каталог: deepseek/deepseek-v4.1-flash, 10.09.2026) — первая
        // модель производителя на архитектуре CED, принимает КАРТИНКИ (у v4-flash был только
        // текст). Цена у DeepSeek плавает по часам суток; в справочник занесён льготный
        // тариф, он же основной по каталогу.
        ("6f1a45e0-0d31-4c65-9a01-000000000087", "DeepSeek-V4.1-Flash",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "deepseek-v4.1-flash",
              "baseUrl": "https://api.deepseek.com",
              "secretRef": "deepseek.apiKey",
              "params": { "maxTokens": 32768 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "deepseek-v4.1-flash",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 86 },
                { "name": "code-review",          "score": 84 },
                { "name": "code-debug",           "score": 83 },
                { "name": "code-test",            "score": 82 },
                { "name": "code-refactor",        "score": 83 },
                { "name": "text-write",           "score": 83 },
                { "name": "text-docs",            "score": 83 },
                { "name": "text-edit",            "score": 82 },
                { "name": "text-translate",       "score": 84 },
                { "name": "text-summarize",       "score": 86 },
                { "name": "analyze-requirements", "score": 82 },
                { "name": "analyze-plan",         "score": 81 },
                { "name": "analyze-data",         "score": 80 }
              ],
              "limits":  { "context": 1048576, "max_output": 384000 },
              "cost":    { "in_per_1m": 0.15, "out_per_1m": 0.60 }
            }
            """),

        // Z.ai GLM-5.3 (каталог: z-ai/glm-5.3, 18.08.2026) — следующая версия после GLM-5.2.
        // Вариант glm-5.3-flash отдельной записью НЕ заводится: это та же версия, дешёвый
        // вариант (правило гашения и учёта версий считает варианты одной версией).
        ("6f1a45e0-0d31-4c65-9a01-000000000088", "GLM-5.3",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "glm-5.3",
              "baseUrl": "https://api.z.ai/api/paas/v4",
              "secretRef": "zai.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "glm-5.3",
              "inputs":  ["text/markdown", "text/plain", "text/source-code"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 93 },
                { "name": "code-review",          "score": 91 },
                { "name": "code-debug",           "score": 90 },
                { "name": "code-test",            "score": 89 },
                { "name": "code-refactor",        "score": 91 },
                { "name": "text-write",           "score": 85 },
                { "name": "text-docs",            "score": 86 },
                { "name": "text-edit",            "score": 84 },
                { "name": "text-translate",       "score": 85 },
                { "name": "text-summarize",       "score": 87 },
                { "name": "analyze-requirements", "score": 89 },
                { "name": "analyze-plan",         "score": 89 },
                { "name": "analyze-data",         "score": 86 }
              ],
              "limits":  { "context": 1048576, "max_output": 65536 },
              "cost":    { "in_per_1m": 1.40, "out_per_1m": 4.40 }
            }
            """),

        // OpenAI GPT Image 2.5 через шлюз fal.ai (каталог: openai/gpt-image-2.5/flare/
        // text-to-image, 08.09.2026). У версии 2.5 ДВА варианта: flare — обычный, быстрый,
        // «для большинства применений»; sunburst — точнее и медленнее. Заведён flare;
        // поля запроса и значения перечислений сверены со схемой очереди эндпойнта.
        ("6f1a45e0-0d31-4c65-9a01-000000000089", "GPT-Image-2.5",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "openai/gpt-image-2.5/flare/text-to-image",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 30
              },
              "request": {
                "prompt": "{prompt}",
                "num_images": 1,
                "output_format": "png",
                "quality": "high",
                "image_size": "square_hd"
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "openai/gpt-image-2.5/flare/text-to-image",
              "inputs":  ["text/plain"],
              "outputs": ["image/png"],
              "skills":  [
                { "name": "image-generate", "score": 97 },
                { "name": "image-text",     "score": 96 },
                { "name": "image-photo",    "score": 95 },
                { "name": "image-concept",  "score": 92 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 5.0, "out_per_1m": 30.0 }
            }
            """),

        // Alibaba Wan 3.0 Prime через шлюз fal.ai (каталог: alibaba/wan-3.0-prime/
        // text-to-video, 24.08.2026) — следующее поколение после локальной Wan 2.2, причём
        // со ЗВУКОМ в ролике. Локально она не заводится: шаблона ComfyUI под неё нет.
        // Цена по каталогу: $0,068 за секунду 480p, $0,14 за 720p, $0,28 за 1080p.
        ("6f1a45e0-0d31-4c65-9a01-000000000090", "Wan-3.0-Prime",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "alibaba/wan-3.0-prime/text-to-video",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 60
              },
              "request": {
                "prompt": "{prompt}",
                "duration": 5,
                "resolution": "720p",
                "aspect_ratio": "16:9",
                "audio": true
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "alibaba/wan-3.0-prime/text-to-video",
              "inputs":  ["text/plain"],
              "outputs": ["video/mp4"],
              "skills":  [
                { "name": "video-generate", "score": 94 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0,
                           "per_unit": 0.14, "unit": "second" }
            }
            """),

        // MiniMax H3 Max через шлюз fal.ai (каталог: minimax/h3-max/text-to-video,
        // 23.08.2026) — видео-линейка производителя, которого мы знали только по текстовой
        // модели M3. Цена в каталоге ПУСКОВАЯ (скидка 75 % до 14.09.2026): в справочник
        // занесён тариф ПОСЛЕ скидки — $0,08 за секунду 768p, иначе счёт удивил бы человека.
        ("6f1a45e0-0d31-4c65-9a01-000000000091", "MiniMax-H3-Max",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "minimax/h3-max/text-to-video",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 60
              },
              "request": {
                "prompt": "{prompt}",
                "prompt_expansion_mode": "balanced",
                "duration": 5,
                "resolution": "768P",
                "aspect_ratio": "16:9"
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "minimax/h3-max/text-to-video",
              "inputs":  ["text/plain"],
              "outputs": ["video/mp4"],
              "skills":  [
                { "name": "video-generate", "score": 92 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0,
                           "per_unit": 0.08, "unit": "second" }
            }
            """),

        // ПЕРВЫЕ ДВЕ ЗАПИСИ С РЕФЕРЕНСНЫМ АУДИО (T-251-S0). Механизм секции profile.refAudio
        // сделан в T-249-S0, но ни одна поставляемая запись его не объявляла — «принимает
        // образец голоса» не умела ни одна модель справочника. Каталог шлюза опрошен
        // 14.09.2026 (GET https://fal.ai/api/models, 1500 записей) плюс схемы OpenAPI очереди
        // кандидатов; ИМЯ ПОЛЯ У КАЖДОГО ЭНДПОЙНТА СВОЁ и выдумывать его нельзя — незнакомое
        // поле шлюз отвергает HTTP 422 уже во время платного задания:
        //   fal-ai/chatterbox/text-to-speech  → audio_url            (текст в text)
        //   fal-ai/zonos2                     → reference_audio_url  (текст в text, поле ОБЯЗАТЕЛЬНОЕ)
        //   fal-ai/index-tts-2/text-to-speech → audio_url            (текст в prompt) — не взята: лицензия весов не объявлена
        //   fal-ai/qwen-3-tts/clone-voice/*   → audio_url, НО отдаёт speaker_embedding, а не звук
        //   fal-ai/minimax/voice-clone        → audio_url, НО отдаёт custom_voice_id, а не звук
        // Поля вида reference_audio_url, названного в задании «общим», у Chatterbox НЕТ вовсе.
        //
        // Chatterbox (Resemble AI, веса MIT) — самый дешёвый zero-shot клон голоса у шлюза:
        // $0,025 за 1000 знаков. Оценка audio-speech НИЖЕ, чем у ElevenLabs-TTS-v3 (96)
        // намеренно: обычную озвучку без образца должен брать ElevenLabs, а эта запись —
        // задачи, где голос задан эталонной записью.
        ("6f1a45e0-0d31-4c65-9a01-000000000092", "Chatterbox-TTS",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "fal-ai/chatterbox/text-to-speech",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 30
              },
              "request": {
                "text": "{prompt}",
                "audio_url": "{audio}",
                "exaggeration": 0.25,
                "temperature": 0.7,
                "cfg": 0.5,
                "seed": "{seed}"
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              },
              "refAudio": {
                "kind": "request-field",
                "placeholder": "{audio}",
                "field": "audio_url",
                "maxCount": 1,
                "maxSeconds": 30,
                "formats": ["audio/wav", "audio/mpeg"],
                "required": true
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "fal-ai/chatterbox/text-to-speech",
              "inputs":  ["text/plain", "audio/wav", "audio/mpeg"],
              "outputs": ["audio/wav"],
              "skills":  [
                { "name": "audio-speech", "score": 88 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0,
                           "per_unit": 0.025, "unit": "1000 chars" }
            }
            """),

        // Zonos 2 (Zyphra, веса Apache 2.0, репозиторий Zyphra/ZONOS2 — сверено 14.09.2026
        // по https://huggingface.co/api/models?author=Zyphra). У этого эндпойнта образец
        // голоса — ЕДИНСТВЕННОЕ обязательное поле схемы (required=['reference_audio_url']),
        // поэтому "required": true здесь не наша осторожность, а требование провайдера:
        // без записи задание слать бессмысленно, шлюз ответит 422.
        ("6f1a45e0-0d31-4c65-9a01-000000000093", "Zonos-2-TTS",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "fal-ai/zonos2",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 30
              },
              "request": {
                "text": "{prompt}",
                "reference_audio_url": "{audio}",
                "language": "en_us",
                "accurate_mode": true,
                "temperature": 1.15,
                "seed": "{seed}"
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              },
              "refAudio": {
                "kind": "request-field",
                "placeholder": "{audio}",
                "field": "reference_audio_url",
                "maxCount": 1,
                "maxSeconds": 30,
                "formats": ["audio/wav", "audio/mpeg"],
                "required": true
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "fal-ai/zonos2",
              "inputs":  ["text/plain", "audio/wav", "audio/mpeg"],
              "outputs": ["audio/wav"],
              "skills":  [
                { "name": "audio-speech", "score": 86 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),

        // ---------------------------------------------------------------------------------
        // ОБХОД РЫНКА 23.09.2026 (T-347-S0). Прошлый обход — T-216-S0, 11.09.2026; здесь
        // только то, что вышло ПОСЛЕ него. Идентификаторы, длина контекста, цены и модальности
        // текстовых записей сверены запросом к публичному каталогу OpenRouter
        // (GET https://openrouter.ai/api/v1/models, 454 записи), медиа — к каталогу fal.ai
        // (GET https://fal.ai/api/models, 1514 записей) плюс схема очереди каждого эндпойнта.
        // СУФЛЁР ни одной из этих записей не нужен: управляющий json читает только
        // ComfyUiConnector (JobOrchestrator → ComfyUiConnector.UsePrompter), а у записей
        // openai-compatible / anthropic / fal-ai он ушёл бы в никуда.
        // ---------------------------------------------------------------------------------

        // OpenAI GPT-6 Sol (каталог: openai/gpt-6-sol, 22.09.2026) — рабочая лошадка нового
        // поколения: вчетверо дешевле Astra ($2/$10 против $10/$50) при том же контексте.
        // Записи gpt-6-sol-pro нет намеренно: это тот же id с reasoning.mode=pro, вариант,
        // а не версия (так же решено у Astra).
        ("6f1a45e0-0d31-4c65-9a01-000000000094", "GPT-6-Sol",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "gpt-6-sol",
              "baseUrl": "https://api.openai.com/v1",
              "secretRef": "openai.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "gpt-6-sol",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 95 },
                { "name": "code-review",          "score": 94 },
                { "name": "code-debug",           "score": 94 },
                { "name": "code-test",            "score": 92 },
                { "name": "code-refactor",        "score": 93 },
                { "name": "text-write",           "score": 93 },
                { "name": "text-docs",            "score": 92 },
                { "name": "text-edit",            "score": 92 },
                { "name": "text-translate",       "score": 91 },
                { "name": "text-summarize",       "score": 94 },
                { "name": "analyze-requirements", "score": 94 },
                { "name": "analyze-plan",         "score": 93 },
                { "name": "analyze-data",         "score": 93 }
              ],
              "limits":  { "context": 1050000, "max_output": 128000 },
              "cost":    { "in_per_1m": 2.0, "out_per_1m": 10.0 }
            }
            """),

        // OpenAI GPT-6 Luna (каталог: openai/gpt-6-luna, 22.09.2026) — САМАЯ ДЕШЁВАЯ запись
        // поколения: $0,10/$0,50 за миллион при контексте 1 050 000. Место в справочнике —
        // массовая черновая работа (перевод, сводка, разбор данных), где Astra и Sol
        // переплата в 20–100 раз.
        ("6f1a45e0-0d31-4c65-9a01-000000000095", "GPT-6-Luna",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "gpt-6-luna",
              "baseUrl": "https://api.openai.com/v1",
              "secretRef": "openai.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "gpt-6-luna",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 86 },
                { "name": "code-review",          "score": 84 },
                { "name": "code-debug",           "score": 84 },
                { "name": "code-test",            "score": 83 },
                { "name": "code-refactor",        "score": 84 },
                { "name": "text-write",           "score": 88 },
                { "name": "text-docs",            "score": 87 },
                { "name": "text-edit",            "score": 88 },
                { "name": "text-translate",       "score": 88 },
                { "name": "text-summarize",       "score": 90 },
                { "name": "analyze-requirements", "score": 85 },
                { "name": "analyze-plan",         "score": 84 },
                { "name": "analyze-data",         "score": 86 }
              ],
              "limits":  { "context": 1050000, "max_output": 128000 },
              "cost":    { "in_per_1m": 0.10, "out_per_1m": 0.50 }
            }
            """),

        // Anthropic Claude Opus 5.5 (каталог: anthropic/claude-opus-5.5, 22.09.2026) — новая
        // версия старшей линейки, и притом ДЕШЕВЛЕ Opus 5 ($4/$20 против $5/$25). Нативный id
        // получен правилом производителя «точка → дефис» (claude-haiku-4.5 → claude-haiku-4-5,
        // запись 011; claude-fable-5.1 → claude-fable-5-1, запись 084) и живым вызовом НЕ
        // проверен — поэтому, как и у Fable 5.1, оставлены запасные модели.
        ("6f1a45e0-0d31-4c65-9a01-000000000096", "Claude-Opus-5.5",
            """
            {
              "_seed": 22,
              "provider": "anthropic",
              "model": "claude-opus-5-5",
              "baseUrl": "",
              "secretRef": "anthropic.apiKey",
              "params": {
                "maxTokens": 16000,
                "effort": "high",
                "fallbacks": ["claude-opus-5", "claude-sonnet-5"]
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "claude-opus-5-5",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 98 },
                { "name": "code-review",          "score": 98 },
                { "name": "code-debug",           "score": 97 },
                { "name": "code-test",            "score": 95 },
                { "name": "code-refactor",        "score": 96 },
                { "name": "text-write",           "score": 95 },
                { "name": "text-docs",            "score": 95 },
                { "name": "text-edit",            "score": 94 },
                { "name": "text-translate",       "score": 93 },
                { "name": "text-summarize",       "score": 96 },
                { "name": "analyze-requirements", "score": 97 },
                { "name": "analyze-plan",         "score": 97 },
                { "name": "analyze-data",         "score": 95 }
              ],
              "limits":  { "context": 1000000, "max_output": 128000 },
              "cost":    { "in_per_1m": 4.0, "out_per_1m": 20.0 }
            }
            """),

        // xAI Grok 4.7 (каталог: x-ai/grok-4.7, 21.09.2026) — следующая версия после 4.6 и
        // на 20 % дешевле её ($1,60/$4,80 против $2/$6). Запись 017 (Grok-4.6) НЕ гасится:
        // более старших версий семейства у неё пока одна, а порог гашения — три.
        ("6f1a45e0-0d31-4c65-9a01-000000000097", "Grok-4.7",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "grok-4.7",
              "baseUrl": "https://api.x.ai/v1",
              "secretRef": "xai.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "grok-4.7",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "application/pdf"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 93 },
                { "name": "code-review",          "score": 92 },
                { "name": "code-debug",           "score": 92 },
                { "name": "code-test",            "score": 90 },
                { "name": "code-refactor",        "score": 91 },
                { "name": "text-write",           "score": 91 },
                { "name": "text-docs",            "score": 90 },
                { "name": "text-edit",            "score": 90 },
                { "name": "text-translate",       "score": 89 },
                { "name": "text-summarize",       "score": 92 },
                { "name": "analyze-requirements", "score": 92 },
                { "name": "analyze-plan",         "score": 92 },
                { "name": "analyze-data",         "score": 91 }
              ],
              "limits":  { "context": 500000, "max_output": 64000 },
              "cost":    { "in_per_1m": 1.60, "out_per_1m": 4.80 }
            }
            """),

        // Alibaba Qwen3.8 Omni Flash (каталог: qwen/qwen3.8-omni-flash, 21.09.2026). Из всей
        // линейки 3.8 это ЕДИНСТВЕННАЯ запись, принимающая ЗВУК (модальности каталога:
        // text,image,audio,video → text), и стоит она как Flash — $0,15/$0,47. Выход у неё
        // только текстовый, поэтому навыков audio-* в декларации нет: подбор исполнителя
        // (SkillIo) обнулил бы оценку записи, у которой пара «вход → выход» не сошлась.
        ("6f1a45e0-0d31-4c65-9a01-000000000098", "Qwen3.8-Omni-Flash",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "qwen3.8-omni-flash",
              "baseUrl": "https://dashscope-intl.aliyuncs.com/compatible-mode/v1",
              "secretRef": "qwen.apiKey",
              "params": { "maxTokens": 32000 },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "qwen3.8-omni-flash",
              "inputs":  ["text/markdown", "text/plain", "text/source-code", "image/*", "audio/wav", "audio/mpeg"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "code-write",           "score": 85 },
                { "name": "code-review",          "score": 83 },
                { "name": "code-debug",           "score": 83 },
                { "name": "code-test",            "score": 82 },
                { "name": "code-refactor",        "score": 83 },
                { "name": "text-write",           "score": 87 },
                { "name": "text-docs",            "score": 86 },
                { "name": "text-edit",            "score": 86 },
                { "name": "text-translate",       "score": 89 },
                { "name": "text-summarize",       "score": 89 },
                { "name": "analyze-requirements", "score": 85 },
                { "name": "analyze-plan",         "score": 84 },
                { "name": "analyze-data",         "score": 87 }
              ],
              "limits":  { "context": 1000000, "max_output": 32768 },
              "cost":    { "in_per_1m": 0.15, "out_per_1m": 0.47 }
            }
            """),

        // ByteDance Seedream V5 Flash (каталог fal.ai: bytedance/seedream/v5/flash/
        // text-to-image, 23.09.2026) — первая запись семейства Seedream в справочнике.
        // Поля запроса сверены со схемой очереди эндпойнта (обязателен только prompt;
        // image_size по умолчанию auto_2K, output_format — jpeg|png).
        // ЦЕНА ШЛЮЗОМ НЕ ОПУБЛИКОВАНА (pricingInfoOverride = null на 23.09.2026), поэтому
        // per_unit не выдуман, а оставлен нулём — как у записи 045 (ElevenLabs-TTS-v3).
        ("6f1a45e0-0d31-4c65-9a01-000000000099", "Seedream-5-Flash",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "bytedance/seedream/v5/flash/text-to-image",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 30
              },
              "request": {
                "prompt": "{prompt}",
                "num_images": 1,
                "output_format": "png",
                "image_size": "auto_2K",
                "enable_safety_checker": true
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "bytedance/seedream/v5/flash/text-to-image",
              "inputs":  ["text/plain"],
              "outputs": ["image/png"],
              "skills":  [
                { "name": "image-generate", "score": 92 },
                { "name": "image-concept",  "score": 90 },
                { "name": "image-photo",    "score": 90 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),

        // Meshy 7.1 (каталог fal.ai: meshy/v7.1/text-to-3d, 19.09.2026) — следующая версия
        // после Meshy V7 (запись 047, она остаётся активной: более старшая версия у неё одна).
        // Поля запроса сверены со схемой очереди: mode=full|preview, topology=quad|triangle,
        // target_polycount, enable_pbr, seed. Цена каталога: $0,80 без текстур, $1,20 с
        // текстурами — в декларации стоит тариф «с текстурами», как и у записи 047.
        ("6f1a45e0-0d31-4c65-9a01-000000000100", "Meshy-7.1",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "meshy/v7.1/text-to-3d",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 60
              },
              "request": {
                "prompt": "{prompt}",
                "mode": "full",
                "topology": "quad",
                "target_polycount": 30000,
                "enable_pbr": true,
                "seed": "{seed}"
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "meshy/v7.1/text-to-3d",
              "inputs":  ["text/plain"],
              "outputs": ["model/glb"],
              "skills":  [
                { "name": "3d-generate", "score": 89 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0,
                           "per_unit": 1.2, "unit": "model" }
            }
            """),

        // Tripo P2 (каталог fal.ai: tripo3d/p2/image-to-3d, 20.09.2026) — старшая линейка
        // Tripo: дороже H3.1 (запись 046) втрое, но с четырьмя уровнями качества текстур.
        // Промпта эндпойнт не принимает вовсе (required=['image_url']), поэтому в inputs
        // объявлена только картинка — иначе проверка форматов отдала бы ему задачу «сделай
        // модель по описанию». Цена каталога: $1,10 за модель со стандартными текстурами.
        ("6f1a45e0-0d31-4c65-9a01-000000000101", "Tripo-P2",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "tripo3d/p2/image-to-3d",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 30
              },
              "request": {
                "image_url": "{image}",
                "texture": true,
                "pbr": true,
                "texture_quality": "standard",
                "quad": false
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "request-field",
                "placeholder": "{image}",
                "field": "image_url",
                "maxCount": 1,
                "formats": ["image/png", "image/jpeg", "image/webp"],
                "required": true
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "tripo3d/p2/image-to-3d",
              "inputs":  ["image/*"],
              "outputs": ["model/glb"],
              "skills":  [
                { "name": "3d-image", "score": 94 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0,
                           "per_unit": 1.1, "unit": "model" }
            }
            """),

        // ElevenLabs Music v2.5 (каталог fal.ai: elevenlabs/music/v2.5, 14.09.2026). У записи
        // 044 версия в идентификатор не вынесена вовсе (эндпойнт fal-ai/elevenlabs/music) —
        // теперь у шлюза есть ЯВНЫЕ версии v2 и v2.5, и берётся старшая. Прежняя запись
        // остаётся активной: её эндпойнт из каталога не исчез. Поля сверены со схемой
        // очереди, цена подтверждена строкой тарифа: $0,6 за минуту звука с округлением вверх.
        ("6f1a45e0-0d31-4c65-9a01-000000000102", "ElevenLabs-Music-v2.5",
            """
            {
              "_seed": 22,
              "provider": "fal-ai",
              "model": "elevenlabs/music/v2.5",
              "baseUrl": "https://queue.fal.run",
              "secretRef": "fal.apiKey",
              "params": {
                "timeoutMinutes": 30
              },
              "request": {
                "prompt": "{prompt}",
                "music_length_ms": 30000,
                "output_format": "mp3_44100_128",
                "force_instrumental": false
              },
              "lora": {
                "supported": false,
                "reason": "provider"
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "elevenlabs/music/v2.5",
              "inputs":  ["text/plain"],
              "outputs": ["audio/mpeg"],
              "skills":  [
                { "name": "audio-song",  "score": 92 },
                { "name": "audio-music", "score": 92 }
              ],
              "limits":  {},
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0,
                           "per_unit": 0.6, "unit": "minute" }
            }
            """),

        // ЛОКАЛЬНАЯ МОДЕЛЬ ДЛЯ РОЛИ СУФЛЁРА (T-347-S0, дополнительная задача). Работа суфлёра
        // всегда одна и та же и всегда простая: прочитать описание задачи и вернуть маленький
        // json по схеме профайла — тяжёлую модель ради этого поднимать незачем. Qwen3.5-4B в
        // кванте Q4_K_S весит 2 590 430 368 байт (2,41 ГиБ; размер снят HEAD-запросом к
        // HuggingFace 23.09.2026, ответ 200 без токена, репозиторий unsloth/Qwen3.5-4B-GGUF
        // не закрыт согласием, лицензия apache-2.0), то есть целиком ложится в 3 ГиБ
        // видеопамяти; контекст 16 384 с квантованным KV-кэшем (--cache-type-k/v q8_0)
        // добавляет к этому меньше 200 МиБ, а всей машине хватает 10 ГиБ ОЗУ. Порт 8084:
        // 8080–8083 уже заняты другими локальными записями, иначе второй llama-server
        // не поднимется рядом с первым.
        // ЗАПИСЬ НЕ ГАСИТСЯ ПО ВЫСЛУГЕ ВЕРСИЙ, пока файл доступен для скачивания у
        // производителя: правило «три более старших версии» здесь неприменимо — её ценность
        // не в качестве, а в размере, и замены с такими требованиями к железу у нас нет.
        ("6f1a45e0-0d31-4c65-9a01-000000000103", "Qwen3.5-4B-Local",
            """
            {
              "_seed": 22,
              "provider": "openai-compatible",
              "model": "qwen3.5-4b",
              "baseUrl": "http://localhost:8084/v1",
              "secretRef": "",
              "launchCommand": "",
              "params": { "maxTokens": 8192 },
              "install": {
                "group": "Qwen3.5-4B",
                "packages": ["llama.cpp"],
                "launchCommand": "\"{package:llama.cpp:llama-server.exe}\" -m \"{model:Qwen3.5-4B-Q4_K_S.gguf}\" -ngl 99 -c 16384 --cache-type-k q8_0 --cache-type-v q8_0 --jinja --port 8084 --alias qwen3.5-4b",
                "files": [
                  {
                    "name": "Qwen3.5-4B-Q4_K_S.gguf",
                    "url": "https://huggingface.co/unsloth/Qwen3.5-4B-GGUF/resolve/main/Qwen3.5-4B-Q4_K_S.gguf",
                    "size": 2590430368
                  }
                ]
              },
              "lora": {
                "supported": true,
                "engine": "llama-cpp",
                "apply": {
                  "kind": "launch-arg",
                  "field": "--lora-scaled",
                  "strength": 1.0,
                  "dir": "loras",
                  "maxCount": 1,
                  "formats": [".gguf"]
                },
                "train": {
                  "kind": "external",
                  "docUrl": "https://github.com/ggml-org/llama.cpp/discussions/10123",
                  "dataset": { "kind": "none" },
                  "start": { "kind": "none" },
                  "wait": { "kind": "none" },
                  "result": {
                    "kind": "file",
                    "target": "loras/{object}.gguf"
                  }
                }
              },
              "refImage": {
                "kind": "none"
              }
            }
            """,
            """
            {
              "_seed": 22,
              "id": "qwen3.5-4b",
              "inputs":  ["text/markdown", "text/plain", "text/source-code"],
              "outputs": ["text/markdown", "text/source-code"],
              "skills":  [
                { "name": "analyze-data",         "score": 74 },
                { "name": "text-summarize",       "score": 72 },
                { "name": "text-translate",       "score": 70 },
                { "name": "text-edit",            "score": 68 },
                { "name": "analyze-requirements", "score": 66 },
                { "name": "text-write",           "score": 65 },
                { "name": "code-write",           "score": 62 }
              ],
              "limits":  { "context": 16384, "max_output": 8192 },
              "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
            }
            """),
        .. KandinskyLiteVariants(),
        .. AceStep15Variants(),
    ];

    /// <summary>Записи справочника вариантов ACE-Step 1.5 XL (T-18-S0) — из общего шаблона.</summary>
    private static (string Id, string Name, string ProfileJson, string ScopeJson)[]
        AceStep15Variants() =>
        [.. AceStep15.Select(v => (
            v.Id,
            v.Name,
            FillAce(AceStep15ProfileJson, v),
            FillAce(AceStep15ScopeJson, v)))];

    /// <summary>Подстановка величин варианта ACE-Step — теми же угловыми скобками, что у Kandinsky Lite.</summary>
    private static string FillAce(string template, AceStep15Kind v) => template
        .Replace("<<ID>>", v.Id, StringComparison.Ordinal)
        .Replace("<<MODEL>>", v.Model, StringComparison.Ordinal)
        .Replace("<<UNET>>", v.Unet, StringComparison.Ordinal)
        .Replace("<<UNETSIZE>>", v.UnetSize.ToString(), StringComparison.Ordinal)
        .Replace("<<STEPS>>", v.Steps.ToString(), StringComparison.Ordinal)
        .Replace("<<SONG>>", v.Song.ToString(), StringComparison.Ordinal)
        .Replace("<<MUSIC>>", v.Music.ToString(), StringComparison.Ordinal);

    /// <summary>Путь файла правил для модели-суфлёра (T-289-S0) — рядом с профайлом, в том же
    /// каталоге данных; ровно его называет <c>prompter.rules</c> профайла.</summary>
    public static string PrompterRulesPathOf(string modelId) => $"models/prompter_{modelId}.md";

    /// <summary>Правила составления управляющего json для варианта ACE-Step 1.5 XL: текст общий,
    /// подставляется только имя варианта — суфлёр управляет теми же полями у всех трёх.</summary>
    private static string AceStep15Rules(AceStep15Kind v) =>
        AceStep15RulesMd.Replace("<<NAME>>", v.Name, StringComparison.Ordinal);

    /// <summary>Workflow-шаблон варианта ACE-Step 1.5 XL: свои веса, своя сила текста и top_p.</summary>
    private static string AceStep15Workflow(AceStep15Kind v) =>
        AceStep15WorkflowJson
            .Replace("<<UNET>>", v.Unet, StringComparison.Ordinal)
            .Replace("\"<<CFG>>\"", v.Cfg.ToString(), StringComparison.Ordinal)
            .Replace("\"<<TOPP>>\"", v.TopP, StringComparison.Ordinal);

    /// <summary>Записи справочника для вариантов Kandinsky Lite — из общего шаблона.</summary>
    private static (string Id, string Name, string ProfileJson, string ScopeJson)[]
        KandinskyLiteVariants() =>
        [.. KandinskyLite.Select(v => (
            v.Id,
            v.Name,
            Fill(KandinskyLiteProfileJson, v),
            Fill(KandinskyLiteScopeJson, v)))];

    /// <summary>
    /// Подстановка величин варианта. Плейсхолдеры в УГЛОВЫХ скобках взяты намеренно:
    /// фигурные в этих же строках заняты плейсхолдерами AI2P ({steps} обучения,
    /// {object}, {model:файл}), и общий вид плейсхолдера привёл бы к тому, что подстановка
    /// съела бы чужой.
    /// </summary>
    private static string Fill(string template, KandinskyLiteKind v) => template
        .Replace("<<ID>>", v.Id, StringComparison.Ordinal)
        .Replace("<<MODEL>>", v.Model, StringComparison.Ordinal)
        .Replace("<<UNET>>", v.Unet, StringComparison.Ordinal)
        .Replace("<<REPO>>", v.Repo, StringComparison.Ordinal)
        .Replace("<<TASK>>", v.Task, StringComparison.Ordinal)
        .Replace("<<STEPS>>", v.Steps.ToString(), StringComparison.Ordinal)
        .Replace("<<CFG>>", v.Cfg.ToString(), StringComparison.Ordinal)
        .Replace("<<LENGTH>>", v.Length.ToString(), StringComparison.Ordinal)
        .Replace("<<SCORE>>", v.Score.ToString(), StringComparison.Ordinal);

    /// <summary>
    /// Профайл варианта Kandinsky Lite. Группа установки — общая с уже работающими
    /// записями (Kandinsky-5): текстовые энкодеры и VAE у всей линейки одни и те же файлы,
    /// и второй раз их качать незачем; свой у варианта только DiT (4,57 ГБ).
    /// Обучение LoRA — musubi-tuner, своя задача тренера на каждый вариант
    /// (k5-lite-t2v-…-sd); догрузки весов, в отличие от Wan и Hunyuan, не нужно:
    /// веса Kandinsky и так лежат не в fp8, а текстовые энкодеры тренер берёт свои,
    /// прямо с HuggingFace по имени репозитория.
    /// </summary>
    private const string KandinskyLiteProfileJson = """
        {
          "_seed": 22,
          "provider": "comfyui",
          "model": "<<MODEL>>",
          "baseUrl": "http://127.0.0.1:8188",
          "secretRef": "",
          "launchCommand": "",
          "workflow": "models/workflow_<<ID>>.json",
          "params": {
            "width": 768,
            "height": 512,
            "length": <<LENGTH>>,
            "steps": <<STEPS>>,
            "negative": "",
            "timeoutMinutes": 180
          },
          "install": {
            "group": "Kandinsky-5",
            "packages": ["comfyui"],
            "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
            "files": [
              {
                "name": "<<UNET>>",
                "url": "https://huggingface.co/kandinskylab/<<REPO>>/resolve/main/model/<<UNET>>",
                "size": 4573130528,
                "category": "diffusion_models"
              },
              {
                "name": "qwen_2.5_vl_7b_fp8_scaled.safetensors",
                "url": "https://huggingface.co/Comfy-Org/HunyuanVideo_1.5_repackaged/resolve/main/split_files/text_encoders/qwen_2.5_vl_7b_fp8_scaled.safetensors",
                "size": 9384670680,
                "category": "text_encoders"
              },
              {
                "name": "clip_l.safetensors",
                "url": "https://huggingface.co/comfyanonymous/flux_text_encoders/resolve/main/clip_l.safetensors",
                "size": 246144152,
                "category": "text_encoders"
              },
              {
                "name": "hunyuan_video_vae_bf16.safetensors",
                "url": "https://huggingface.co/Kijai/HunyuanVideo_comfy/resolve/main/hunyuan_video_vae_bf16.safetensors",
                "size": 492986478,
                "category": "vae"
              }
            ]
          },
          "lora": {
            "supported": true,
            "engine": "comfyui",
            "apply": {
              "kind": "workflow",
              "node": "LoraLoaderModelOnly",
              "placeholder": "{lora}",
              "strengthPlaceholder": "{loraStrength}",
              "strength": 1.0,
              "dir": "loras",
              "maxCount": 1,
              "formats": [".safetensors"]
            },
            "train": {
              "kind": "process",
              "docUrl": "https://github.com/kohya-ss/musubi-tuner/blob/main/docs/kandinsky5.md",
              "packages": ["musubi-tuner", "python", "qwen2.5-vl-7b", "clip-vit-large-patch14"],
              "dataset": {
                "kind": "dir",
                "path": "lora/{object}/dataset",
                "captions": "txt",
                "minItems": 10,
                "maxItems": 60,
                "width": 768,
                "height": 512,
                "maxKb": 0,
                "formats": ["png"]
              },
              "files": [
                {
                  "path": "lora/{object}/dataset.toml",
                  "text": [
                    "# AI2P: written from the model profile before every training run.",
                    "# Edit it in the model profile, not here: it is overwritten each time.",
                    "[general]",
                    "resolution = [{width}, {height}]",
                    "caption_extension = \".txt\"",
                    "batch_size = 1",
                    "enable_bucket = true",
                    "bucket_no_upscale = false",
                    "",
                    "[[datasets]]",
                    "image_directory = '{dataset}'",
                    "cache_directory = '{output}\\cache'",
                    "num_repeats = 1"
                  ]
                },
                {
                  "path": "lora/{object}/train.cmd",
                  "text": [
                    "@echo off",
                    "rem AI2P: LoRA training for Kandinsky 5 Video Lite via musubi-tuner.",
                    "rem Written from the model profile before every run - edit it in the model.",
                    "setlocal enableextensions",
                    "set \"TRAIN={package:musubi-tuner:kandinsky5_train_network.py}\"",
                    "for %%I in (\"%TRAIN%\") do set \"SCRIPTS=%%~dpI\"",
                    "set \"ROOT=%SCRIPTS%\"",
                    "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\\") do set \"ROOT=%%~fI\"",
                    "if not exist \"%ROOT%pyproject.toml\" for %%I in (\"%SCRIPTS%..\\..\\\") do set \"ROOT=%%~fI\"",
                    "if not exist \"%ROOT%pyproject.toml\" set \"ROOT=%SCRIPTS%\"",
                    "set \"VENV=%ROOT%.venv\"",
                    "set \"PY=%VENV%\\Scripts\\python.exe\"",
                    "set \"OUT={output}\"",
                    "if not exist \"%OUT%\" mkdir \"%OUT%\"",
                    "if not exist \"%PY%\" call :setup",
                    "if errorlevel 1 exit /b 1",
                    "echo [AI2P] caching latents",
                    "\"%PY%\" \"%SCRIPTS%kandinsky5_cache_latents.py\" --dataset_config \"%~dp0dataset.toml\" --vae \"{model:hunyuan_video_vae_bf16.safetensors}\"",
                    "if errorlevel 1 exit /b 1",
                    "echo [AI2P] caching text encoder outputs",
                    "\"%PY%\" \"%SCRIPTS%kandinsky5_cache_text_encoder_outputs.py\" --dataset_config \"%~dp0dataset.toml\" --text_encoder_qwen \"{packageDir:qwen2.5-vl-7b}\" --text_encoder_clip \"{packageDir:clip-vit-large-patch14}\" --batch_size 1",
                    "if errorlevel 1 exit /b 1",
                    "echo [AI2P] training",
                    "\"%VENV%\\Scripts\\accelerate.exe\" launch --num_cpu_threads_per_process 1 --mixed_precision bf16 \"%SCRIPTS%kandinsky5_train_network.py\" --mixed_precision bf16 --dataset_config \"%~dp0dataset.toml\" --task <<TASK>> --dit \"{model:<<UNET>>}\" --vae \"{model:hunyuan_video_vae_bf16.safetensors}\" --text_encoder_qwen \"{packageDir:qwen2.5-vl-7b}\" --text_encoder_clip \"{packageDir:clip-vit-large-patch14}\" --sdpa --fp8_base --gradient_checkpointing --max_data_loader_n_workers 1 --persistent_data_loader_workers --learning_rate 1e-4 --optimizer_type AdamW8Bit --optimizer_args \"weight_decay=0.001\" --max_grad_norm 1.0 --lr_scheduler constant_with_warmup --lr_warmup_steps 100 --network_module networks.lora_kandinsky --network_dim 32 --network_alpha 32 --timestep_sampling shift --discrete_flow_shift 5.0 --scheduler_scale 10.0 --max_train_steps {steps} --output_dir \"%OUT%\" --output_name {object} --seed 42",
                    "if errorlevel 1 exit /b 1",
                    "echo [AI2P] done",
                    "exit /b 0",
                    "",
                    ":setup",
                    "echo [AI2P] preparing the training environment",
                    "set \"PYEXE={package:python:python.exe}\"",
                    "if defined AI2P_LORA_PYTHON set \"PYEXE=%AI2P_LORA_PYTHON%\"",
                    "set \"TORCH_INDEX=https://download.pytorch.org/whl/cu124\"",
                    "if defined AI2P_LORA_TORCH_INDEX set \"TORCH_INDEX=%AI2P_LORA_TORCH_INDEX%\"",
                    "\"%PYEXE%\" -m venv \"%VENV%\"",
                    "if errorlevel 1 echo [AI2P] ERROR: cannot create the virtual environment. Reinstall the model (it brings Python 3.10-3.12), or point AI2P_LORA_PYTHON at your own Python.",
                    "if errorlevel 1 exit /b 1",
                    "\"%PY%\" -m pip install --upgrade pip",
                    "if errorlevel 1 exit /b 1",
                    "\"%PY%\" -m pip install torch torchvision --index-url %TORCH_INDEX%",
                    "if errorlevel 1 exit /b 1",
                    "\"%PY%\" -m pip install -e \"%ROOT%.\"",
                    "if errorlevel 1 echo [AI2P] ERROR: musubi-tuner was not found in %ROOT% - reinstall the model (Settings - Models - Install), it brings the trainer.",
                    "if errorlevel 1 exit /b 1",
                    "exit /b 0"
                  ]
                }
              ],
              "start": {
                "kind": "process",
                "command": "train.cmd",
                "workDir": "lora/{object}",
                "steps": 2000
              },
              "wait": {
                "kind": "process",
                "timeoutMinutes": 720
              },
              "result": {
                "kind": "file",
                "path": "lora/{object}/out/{object}.safetensors",
                "target": "loras/{object}.safetensors"
              }
            }
          },
          "refImage": {
            "kind": "none"
          }
        }
        """;

    /// <summary>Декларация возможностей варианта Kandinsky Lite.</summary>
    private const string KandinskyLiteScopeJson = """
        {
          "_seed": 22,
          "id": "<<MODEL>>",
          "inputs":  ["text/plain"],
          "outputs": ["video/*"],
          "skills":  [
            { "name": "video-generate", "score": <<SCORE>> }
          ],
          "limits":  {},
          "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
        }
        """;

    /// <summary>
    /// Профайл подключения варианта ACE-Step 1.5 XL (T-18-S0). Три особенности звука против
    /// картинок и видео:
    /// <list type="bullet">
    /// <item>«длина» (params.length) — ДЛИТЕЛЬНОСТЬ ТРЕКА В СЕКУНДАХ: она уходит и в размер
    /// пустого латента, и в поле duration планировщика;</item>
    /// <item>ширины и высоты в params НЕТ вовсе: у звука размера кадра не бывает, а ноль
    /// поставить нельзя — ModelProfile.Int отбрасывает неположительные («не задано»), и
    /// в сводку задания всё равно попали бы умолчания профайла 768×512. Пусть уж это
    /// будут честные умолчания, а не выдуманное нами число;</item>
    /// <item>обучение адаптера — <c>external</c> С ПУСТОЙ КОМАНДОЙ, и это ПРОВЕРЕННЫЙ ОТКАЗ,
    /// а не пропущенная работа (T-250-S0, сверено по файлам репозитория 14.09.2026).
    /// Тренер у модели ЕСТЬ и идёт на Windows с одной видеокартой: ace-step/ACE-Step-1.5,
    /// MIT, <c>python -m acestep.training_v2.cli.train_fixed</c> — без torchrun, номер
    /// видеокарты по умолчанию один, число рабочих DataLoader на Windows намеренно 0.
    /// Не хватает ВЕСОВ: тренеру нужен каталог чекпойнтов в формате HuggingFace
    /// (config.json + model-0000N-of-00004.safetensors, ~19,9 ГБ на вариант, плюс vae и
    /// языковая модель разметки), а мы ставим ПЕРЕПАКОВКУ Comfy-Org — другие файлы, другая
    /// раскладка. Это ровно тот случай, когда честнее отказать сразу (msg.lora.6), чем
    /// считать полчаса и упасть. Второе, что мешает: адаптер тренера — peft поверх их DiT,
    /// а в comfy/lora.py разбор «официального формата ACE-Step» стоит под
    /// <c>isinstance(model, ACEStep)</c>, тогда как 1.5 — ОТДЕЛЬНЫЙ класс ACEStep15
    /// (comfy/model_base.py), то есть подхватится ли обученный файл узлом
    /// LoraLoaderModelOnly, не проверено. musubi-tuner ACE-Step не знает вовсе (в нём нет
    /// ни одного acestep-скрипта, проверено 27.08.2026).
    /// Пределы датасета при этом ОБЪЯВЛЕНЫ (media = audio): по ним собирают датасет те,
    /// кто обучает адаптер руками у себя, и по ним же сверяется наш редактор.
    /// </item>
    /// </list>
    /// </summary>
    private const string AceStep15ProfileJson = """
        {
          "_seed": 22,
          "provider": "comfyui",
          "model": "<<MODEL>>",
          "baseUrl": "http://127.0.0.1:8188",
          "secretRef": "",
          "launchCommand": "",
          "workflow": "models/workflow_<<ID>>.json",
          "params": {
            "length": 120,
            "steps": <<STEPS>>,
            "negative": "",
            "timeoutMinutes": 60
          },
          "install": {
            "group": "ACE-Step-1.5",
            "packages": ["comfyui"],
            "launchCommand": "\"{package:comfyui:python.exe}\" -s \"{package:comfyui:main.py}\" --windows-standalone-build --disable-auto-launch --extra-model-paths-config \"{extraModelPaths}\"",
            "files": [
              {
                "name": "<<UNET>>",
                "url": "https://huggingface.co/Comfy-Org/ace_step_1.5_ComfyUI_files/resolve/main/split_files/diffusion_models/<<UNET>>",
                "size": <<UNETSIZE>>,
                "category": "diffusion_models"
              },
              {
                "name": "qwen_0.6b_ace15.safetensors",
                "url": "https://huggingface.co/Comfy-Org/ace_step_1.5_ComfyUI_files/resolve/main/split_files/text_encoders/qwen_0.6b_ace15.safetensors",
                "size": 1191588248,
                "category": "text_encoders"
              },
              {
                "name": "qwen_4b_ace15.safetensors",
                "url": "https://huggingface.co/Comfy-Org/ace_step_1.5_ComfyUI_files/resolve/main/split_files/text_encoders/qwen_4b_ace15.safetensors",
                "size": 8379154232,
                "category": "text_encoders"
              },
              {
                "name": "ace_1.5_vae.safetensors",
                "url": "https://huggingface.co/Comfy-Org/ace_step_1.5_ComfyUI_files/resolve/main/split_files/vae/ace_1.5_vae.safetensors",
                "size": 337431732,
                "category": "vae"
              }
            ]
          },
          "lora": {
            "supported": true,
            "engine": "comfyui",
            "apply": {
              "kind": "workflow",
              "node": "LoraLoaderModelOnly",
              "placeholder": "{lora}",
              "strengthPlaceholder": "{loraStrength}",
              "strength": 1.0,
              "dir": "loras",
              "maxCount": 1,
              "formats": [".safetensors"]
            },
            "train": {
              "kind": "external",
              "docUrl": "https://github.com/ace-step/ACE-Step-1.5/blob/main/docs/en/LoRA_Training_Tutorial.md",
              "packages": [],
              "dataset": {
                "kind": "none",
                "media": "audio",
                "path": "",
                "captions": "txt",
                "minItems": 10,
                "maxItems": 0,
                "width": 0,
                "height": 0,
                "maxKb": 0,
                "minSeconds": 0,
                "maxSeconds": 240,
                "sampleRate": 48000,
                "channels": 2,
                "formats": ["wav", "mp3", "flac", "ogg", "opus"]
              },
              "files": [],
              "start": { "kind": "none", "command": "", "workDir": "", "steps": 0 },
              "wait": { "kind": "none", "timeoutMinutes": 0 },
              "result": { "kind": "file", "path": "", "target": "loras/{object}.safetensors" }
            }
          },
          "refImage": {
            "kind": "none"
          },
          "prompter": {
            "required": true,
            "rules": "models/prompter_<<ID>>.md",
            "schema": [
              { "name": "tags", "type": "string", "required": true, "maxLength": 600,
                "description": "стилевые тэги через запятую: жанр, инструменты, настроение, тембр вокала; не назван — в поле уйдёт описание задачи целиком" },
              { "name": "lyrics", "type": "string", "maxLength": 3000,
                "description": "слова песни; пусто — модель сочинит их сама" },
              { "name": "duration", "type": "number", "min": 1, "max": 1000,
                "description": "длительность трека в СЕКУНДАХ (уходит и в duration планировщика, и в seconds пустого латента); не названа — length профайла" },
              { "name": "language", "type": "enum",
                "values": ["ar", "az", "bg", "bn", "ca", "cs", "da", "de", "el", "en", "es", "fa", "fi", "fr", "he", "hi", "hr", "ht", "hu", "id", "is", "it", "ja", "ko", "la", "lt", "ms", "ne", "nl", "no", "pa", "pl", "pt", "ro", "ru", "sa", "sk", "sr", "sv", "sw", "ta", "te", "th", "tl", "tr", "uk", "ur", "vi", "yue", "zh", "unknown"],
                "description": "язык вокала; unknown (умолчание AI2P) — модель решает сама по словам песни" },
              { "name": "bpm", "type": "int", "min": 10, "max": 300,
                "description": "темп, ударов в минуту; не назван — 120" },
              { "name": "keyscale", "type": "enum",
                "values": ["C major", "C# major", "Db major", "D major", "D# major", "Eb major", "E major", "F major", "F# major", "Gb major", "G major", "G# major", "Ab major", "A major", "A# major", "Bb major", "B major", "C minor", "C# minor", "Db minor", "D minor", "D# minor", "Eb minor", "E minor", "F minor", "F# minor", "Gb minor", "G minor", "G# minor", "Ab minor", "A minor", "A# minor", "Bb minor", "B minor"],
                "description": "тональность и лад; не названа — C major" },
              { "name": "timesignature", "type": "enum", "values": ["2", "3", "4", "6"],
                "description": "размер такта: 2, 3, 4 или 6 четвертей; не назван — 4" }
            ]
          }
        }
        """;

    /// <summary>
    /// Декларация возможностей варианта ACE-Step 1.5 XL. Выход — audio/mpeg (граф
    /// сохраняет MP3), вход — только текст: пара inputs/outputs участвует в подборе
    /// исполнителя (SkillIo, T-257), и у навыков audio-song/audio-music режим ровно такой.
    /// </summary>
    private const string AceStep15ScopeJson = """
        {
          "_seed": 22,
          "id": "<<MODEL>>",
          "inputs":  ["text/plain"],
          "outputs": ["audio/mpeg"],
          "skills":  [
            { "name": "audio-song",  "score": <<SONG>> },
            { "name": "audio-music", "score": <<MUSIC>> }
          ],
          "limits":  {},
          "cost":    { "in_per_1m": 0.0, "out_per_1m": 0.0 }
        }
        """;

    /// <summary>
    /// Workflow ACE-Step 1.5 XL в API-формате — собран по официальным шаблонам Comfy-Org
    /// audio_ace_step1_5_xl_base/_sft/_turbo (узлы верхнего уровня, подграфа у них нет).
    /// Узлы фронтенда PrimitiveNode/PrimitiveInt, раздававшие seed и длительность, в
    /// API-формат не переносятся — их значения стоят прямо в полях узлов.
    ///
    /// Плейсхолдеры AI2P: "{seed}" — общий для планировщика и сэмплера (в шаблоне он тоже
    /// раздавался из одного узла), "{steps}" — шаги сэмплера, {job} — имя файла.
    /// Угловыми скобками — величины варианта (см. <see cref="AceStep15Workflow"/>).
    ///
    /// ПОЛЯ ОТ МОДЕЛИ-СУФЛЁРА (T-287-S0) — именованные плейсхолдеры {p:имя|умолчание}:
    /// tags, lyrics, duration, language, bpm, keyscale, timesignature. Имена входов и
    /// перечни значений сверены с ЖИВЫМ ComfyUI (GET /object_info/TextEncodeAceStepAudio1.5
    /// и /EmptyAceStep1.5LatentAudio, 18.09.2026), а не взяты из памяти: опечатка во входе
    /// даёт «required input is missing» уже во время задания (наука T-20-S0). Умолчания
    /// оставлены прежние, поэтому БЕЗ суфлёра граф собирается ровно как до правки: tags —
    /// описание задачи ({prompt}), lyrics пусто, duration и seconds — "{length}" профайла,
    /// bpm 120, размер такта 4, тональность C major, язык "unknown".
    ///
    /// Длительность стоит в ДВУХ местах — duration узла "4" и seconds пустого латента узла
    /// "6", — и это ОДИН И ТОТ ЖЕ плейсхолдер {p:duration}: расхождение даёт обрезанный
    /// либо растянутый трек.
    ///
    /// Про язык: в официальных шаблонах стоит "en", здесь — "unknown" (опция объявлена в
    /// схеме узла): система многоязычная, и жёсткий английский заставил бы модель петь
    /// русский текст с английским произношением. Конкретный язык называет суфлёр.
    ///
    /// SaveAudioMP3 в ComfyUI помечен deprecated (замена — SaveAudioAdvanced с составным
    /// полем format), но именно он стоит в официальных шаблонах и работает; когда его
    /// удалят, менять надо здесь.
    /// </summary>
    private const string AceStep15WorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "UNETLoader", "inputs": { "unet_name": "<<UNET>>", "weight_dtype": "default" } },
            "2": { "class_type": "ModelSamplingAuraFlow", "inputs": { "shift": 3, "model": ["1", 0] } },
            "3": { "class_type": "DualCLIPLoader", "inputs": { "clip_name1": "qwen_0.6b_ace15.safetensors", "clip_name2": "qwen_4b_ace15.safetensors", "type": "ace", "device": "default" } },
            "4": { "class_type": "TextEncodeAceStepAudio1.5", "inputs": { "clip": ["3", 0], "tags": "{p:tags|{prompt}}", "lyrics": "{p:lyrics|}", "seed": "{seed}", "bpm": "{p:bpm|120}", "duration": "{p:duration|{length}}", "timesignature": "{p:timesignature|4}", "language": "{p:language|unknown}", "keyscale": "{p:keyscale|C major}", "generate_audio_codes": true, "cfg_scale": 2.0, "temperature": 0.85, "top_p": "<<TOPP>>", "top_k": 0, "min_p": 0.0 } },
            "5": { "class_type": "ConditioningZeroOut", "inputs": { "conditioning": ["4", 0] } },
            "6": { "class_type": "EmptyAceStep1.5LatentAudio", "inputs": { "seconds": "{p:duration|{length}}", "batch_size": 1 } },
            "7": { "class_type": "VAELoader", "inputs": { "vae_name": "ace_1.5_vae.safetensors" } },
            "8": { "class_type": "KSampler", "inputs": { "seed": "{seed}", "steps": "{steps}", "cfg": "<<CFG>>", "sampler_name": "euler", "scheduler": "simple", "denoise": 1, "model": ["2", 0], "positive": ["4", 0], "negative": ["5", 0], "latent_image": ["6", 0] } },
            "9": { "class_type": "VAEDecodeAudio", "inputs": { "samples": ["8", 0], "vae": ["7", 0] } },
            "10": { "class_type": "SaveAudioMP3", "inputs": { "audio": ["9", 0], "filename_prefix": "AI2P/{job}", "quality": "V0" } }
          }
        }
        """;

    /// <summary>
    /// ПРАВИЛА СОСТАВЛЕНИЯ УПРАВЛЯЮЩЕГО JSON для ACE-Step 1.5 XL (T-289-S0) — файл
    /// <c>models/prompter_&lt;ID&gt;.md</c> рядом с профайлом, на него показывает
    /// <c>prompter.rules</c> профайла. Текст читает МОДЕЛЬ-СУФЛЁР: он уходит в её промпт
    /// целиком (<c>PrompterService.BuildPrompt</c>), перед перечнем полей со схемой.
    ///
    /// ПОЧЕМУ ФАЙЛОМ, А НЕ ЗАПИСЬЮ ОПЫТА: медиа-модель опыта не видит вовсе (ComfyUiConnector
    /// берёт только описание задачи), а суфлёр — обычный текстовый вызов, и правила ему
    /// передаются ЯВНО, из файла рядом с профайлом. Правка файла действует со следующего же
    /// задания — как правка workflow.
    ///
    /// Поля, границы и перечни сверены с ЖИВЫМ ComfyUI (GET /object_info узлов
    /// TextEncodeAceStepAudio1.5, EmptyAceStep1.5LatentAudio и KSampler, 18.09.2026 —
    /// снимок test/t289s0/objinfo.json), а не взяты из памяти. Числа В ТЕКСТЕ намеренно
    /// не повторяют схему профайла подробно: границы проверяет код (PrompterField), и
    /// число, написанное в двух местах, рано или поздно разойдётся.
    ///
    /// Язык текста — русский, как и описания полей схемы в профайле: файл один на все
    /// языки интерфейса, а суфлёр — языковая модель, для которой язык правил безразличен
    /// (промпт вокруг правил собирается на языке команды, Loc.In).
    ///
    /// Версия файла — строка-отметка <c>&lt;!-- _seed: N --&gt;</c> в первой строке: json-поля
    /// "_seed" в markdown не бывает, а перезаписывать файл при каждом старте нельзя —
    /// правки человека пропадали бы молча (см. <see cref="WriteSeedText"/>).
    /// </summary>
    private const string AceStep15RulesMd = """
        <!-- _seed: 22 -->
        # Управляющий json для ACE-Step 1.5 XL (<<NAME>>)

        Ты готовишь задание локальной модели сочинения музыки **ACE-Step 1.5 XL**. Она
        работает в ComfyUI: твой json заполняет поля узла `TextEncodeAceStepAudio1.5`
        (стилевые тэги, слова песни, темп, тональность, размер такта, язык вокала) и
        длительность пустого латента `EmptyAceStep1.5LatentAudio`.

        Ответ — **один json-объект и ничего больше**: без пояснений, без ```-заборчика,
        без комментариев внутри json. Поля — только из перечня ниже; лишние игнорируются.
        Поле, о котором в задаче ничего не сказано, **лучше не называть вовсе**, чем
        выдумать: у каждого есть разумное умолчание, оно перечислено здесь.

        ## Что класть в каждое поле

        **`tags` — СТИЛЕВЫЕ ТЭГИ, а не пересказ задания.** Через запятую: жанр, темп
        словами, инструменты, настроение, тембр и пол вокала, приёмы записи. Пример:
        `synthwave, driving, analog bass, gated drums, male vocal, wide reverb`. Сюда НЕ
        пишут ни слова песни, ни длительность, ни язык, ни служебные указания («положи
        результат в файл»), ни сам текст задачи предложениями — у них свои поля. Тэги
        принято писать по-английски: на них модель обучена, и английский тэг срабатывает
        точнее перевода. Нет вокала — так и скажи тэгом (`instrumental, no vocals`).

        **`lyrics` — слова песни.** Заполняй, только если человек написал текст сам либо
        просит спеть названные слова. Переноси их ДОСЛОВНО, строка в строку, ничего не
        переводя и не дописывая. Строение помечают строками-метками `[verse]`, `[chorus]`,
        `[bridge]`, `[outro]` — если человек их уже расставил, сохрани. Пусто — модель
        сочинит слова сама (внутри неё языковая модель-планировщик), и это обычный случай:
        для инструментала поле оставляют пустым всегда.

        **`duration` — длительность в СЕКУНДАХ** (не в минутах и не в кадрах). «Полторы
        минуты» — это 90. Не названа — не называй её и ты: возьмётся `length` профайла
        модели. Значение уходит сразу в два места графа, поэтому число должно быть одно.

        **`language` — код языка ВОКАЛА** из перечня узла (`ru`, `en`, `zh`, `ja`, `es`, …).
        Ставь его, когда язык назван прямо или очевиден из слов песни. Инструментал, разные
        языки вперемешку и любые сомнения — `unknown`: тогда модель решает сама по словам, а
        жёстко поставленный `en` заставил бы её петь русский текст с английским
        произношением. Язык ОПИСАНИЯ задачи языком вокала не является.

        **`bpm` — темп, ударов в минуту.** Назван словами («медленно», «танцевальный») —
        переведи в число сам: баллада 60–75, поп 100–120, танцевальное 124–132, драм-н-бэйс
        170–175. Ничего про темп не сказано — не называй поле, возьмётся 120.

        **`keyscale` — тональность и лад** строкой из перечня (`C major`, `A minor`, …).
        Умолчание `C major`. Просьба «грустно, минорно» — повод взять минор (`A minor`),
        «светло, мажорно» — мажор; точная тональность нужна, только когда её назвали.

        **`timesignature` — размер такта**: `2`, `3`, `4` или `6` четвертей. Умолчание `4`;
        вальс — `3`, колыбельная или баллада с «раскачкой» — `6`.

        ## Чем ты НЕ управляешь

        Шаги диффузии, `cfg` сэмплера, seed, отрицательный промпт и таймаут берутся из
        профайла модели — полей для них в json нет, называть их бесполезно. Имя выходного
        файла и папку результата задаёт AI2P.

        ## Пример

        Описание задачи:

        > Нужна заставка для ролика про горы — минута спокойной музыки без вокала,
        > неторопливая, акустическая гитара и струнные, немного грустная.

        Ответ:

        {"tags": "cinematic acoustic, calm, slow, fingerpicked guitar, warm strings, airy pads, instrumental, no vocals", "duration": 60, "bpm": 70, "keyscale": "A minor", "language": "unknown"}

        Слов песни нет — поля `lyrics` нет вовсе; размер такта не назван — поля
        `timesignature` тоже нет.
        """;

    /// <summary>
    /// Справочник пакетов, необходимых локальным моделям для запуска (ТЗ v1.42, todo36_5) —
    /// seed-файл models/packages.json в dataDir (тот же механизм версий "_seed"). Пакет
    /// общий для всех моделей: ставится один раз, дальше только проверяется по признаку
    /// установленности (check). Дистрибутивы качаются в каталог дистрибутивов (гл. 10),
    /// распаковываются в &lt;корень пакетов&gt;/&lt;dir&gt;.
    /// Поля файла пакета: name/url — прямая ссылка, либо github + asset (маска ассета
    /// последнего релиза); unpack — zip/7z/tar (пусто — просто положить файл);
    /// when — "cuda"/"cpu": файл только для соответствующей сборки (определяется по nvidia-smi).
    /// В маске {flavor} заменяется на сборку: win-cuda-12.4-x64 либо win-cpu-x64.
    ///
    /// Пакеты ОБУЧЕНИЯ (musubi-tuner, python) названы у модели отдельно — lora.train.packages,
    /// — но ставятся вместе с ней, при установке модели (T-4-S0): установка это единственное
    /// место, где человек согласился ждать, и там для этого готово окно с ходом работы.
    /// Ссылки на дистрибутивы — с ТЕГОМ, а не releases/latest: иначе версия тренера у каждого
    /// своя, и разбор жалоб становится гаданием.
    ///
    /// У python есть блок "system": он ставится не всегда — сначала его ищут на этом
    /// компьютере (команда в PATH и годная версия, SystemPackageProbe), и найденный годный
    /// экземпляр берётся как есть. Диапазон 3.10-3.12 не придирчивость: на 3.13 musubi-tuner
    /// не собирается, а узналось бы это через несколько часов обучения.
    ///
    /// Блок "setup" (T-185-S0) — что сделать ПОСЛЕ распаковки: у musubi-tuner это окружение
    /// с torch и зависимости из его pyproject.toml. Пока шаги не прошли, пакет считается
    /// неустановленным (файл-признак .venv/Scripts/accelerate.exe).
    /// Пакеты весов qwen2.5-vl-7b и clip-vit-large-patch14 — снимок репозитория HuggingFace
    /// файл в файл: имя дистрибутива с подкаталогом, чтобы одинаковые config.json двух
    /// репозиториев не столкнулись, размеры названы (иначе окно установки молчало бы про
    /// 18 ГБ, пока не отработают два десятка запросов HEAD).
    /// </summary>
    private const string PackagesSeedJson = """
        {
          "_seed": 22,
          "packages": [
            {
              "id": "comfyui",
              "name": "ComfyUI (portable)",
              "dir": "ComfyUI",
              "os": "windows",
              "check": "python.exe",
              "hint": "Скачайте ComfyUI_windows_portable_nvidia.7z со страницы релизов ComfyUI и распакуйте в каталог пакета.",
              "files": [
                {
                  "name": "ComfyUI_windows_portable_nvidia.7z",
                  "url": "https://github.com/comfyanonymous/ComfyUI/releases/latest/download/ComfyUI_windows_portable_nvidia.7z",
                  "unpack": "7z"
                }
              ]
            },
            {
              "id": "musubi-tuner",
              "name": "Musubi Tuner (обучение LoRA)",
              "dir": "musubi-tuner",
              "os": "windows",
              "check": "kandinsky5_train_network.py",
              "setup": {
                "workDir": "{package:musubi-tuner:pyproject.toml}",
                "check": ".venv/Scripts/accelerate.exe",
                "timeoutMinutes": 180,
                "idleMinutes": 30,
                "steps": [
                  { "name": "Python + venv", "run": "{package:python:python.exe}", "args": "-m venv .venv" },
                  { "name": "pip", "run": ".venv/Scripts/python.exe", "args": "-m pip install --upgrade pip setuptools wheel" },
                  { "name": "torch (CUDA 12.4)", "run": ".venv/Scripts/python.exe", "args": "-m pip install torch torchvision --index-url https://download.pytorch.org/whl/cu124" },
                  { "name": "musubi-tuner requirements", "run": ".venv/Scripts/python.exe", "args": "-m pip install -e ." },
                  { "name": "huggingface_hub, hf_xet", "run": ".venv/Scripts/python.exe", "args": "-m pip install --upgrade huggingface_hub hf_xet", "optional": true }
                ]
              },
              "hint": "Скачайте исходники musubi-tuner v0.3.4 (https://github.com/kohya-ss/musubi-tuner/archive/refs/tags/v0.3.4.zip) и распакуйте в каталог пакета. Окружение с torch тренер создаёт себе сам при первом запуске обучения — в подкаталоге .venv каталога пакета.",
              "files": [
                {
                  "name": "musubi-tuner-v0.3.4.zip",
                  "url": "https://github.com/kohya-ss/musubi-tuner/archive/refs/tags/v0.3.4.zip",
                  "unpack": "zip"
                }
              ]
            },
            {
              "id": "python",
              "name": "Python 3.12 (для обучения LoRA)",
              "dir": "python",
              "os": "windows",
              "check": "python.exe",
              "system": {
                "commands": ["python", "python3"],
                "versionArgs": "--version",
                "minVersion": "3.10",
                "maxVersion": "3.12"
              },
              "hint": "Установите Python 3.10-3.12 с python.org — он будет найден в PATH; либо скачайте сборку cpython-3.12.14+20260825-x86_64-pc-windows-msvc-install_only.tar.gz (https://github.com/astral-sh/python-build-standalone/releases) и распакуйте в каталог пакета.",
              "files": [
                {
                  "name": "cpython-3.12.14-x86_64-pc-windows-msvc.tar.gz",
                  "url": "https://github.com/astral-sh/python-build-standalone/releases/download/20260825/cpython-3.12.14%2B20260825-x86_64-pc-windows-msvc-install_only.tar.gz",
                  "unpack": "tar.gz"
                }
              ]
            },
            {
              "id": "qwen2.5-vl-7b",
              "name": "Qwen2.5-VL-7B-Instruct (текстовый кодировщик для обучения LoRA)",
              "dir": "hf/Qwen2.5-VL-7B-Instruct",
              "check": "model.safetensors.index.json",
              "hint": "Скачайте репозиторий Qwen/Qwen2.5-VL-7B-Instruct с huggingface.co (около 16 ГБ, лицензия Apache 2.0) и положите его файлы прямо в каталог пакета — подкаталогов там быть не должно.",
              "files": [
                { "name": "Qwen2.5-VL-7B-Instruct/config.json", "url": "https://huggingface.co/Qwen/Qwen2.5-VL-7B-Instruct/resolve/main/config.json", "size": 1374 },
                { "name": "Qwen2.5-VL-7B-Instruct/generation_config.json", "url": "https://huggingface.co/Qwen/Qwen2.5-VL-7B-Instruct/resolve/main/generation_config.json", "size": 216 },
                { "name": "Qwen2.5-VL-7B-Instruct/preprocessor_config.json", "url": "https://huggingface.co/Qwen/Qwen2.5-VL-7B-Instruct/resolve/main/preprocessor_config.json", "size": 350 },
                { "name": "Qwen2.5-VL-7B-Instruct/chat_template.json", "url": "https://huggingface.co/Qwen/Qwen2.5-VL-7B-Instruct/resolve/main/chat_template.json", "size": 1050 },
                { "name": "Qwen2.5-VL-7B-Instruct/tokenizer_config.json", "url": "https://huggingface.co/Qwen/Qwen2.5-VL-7B-Instruct/resolve/main/tokenizer_config.json", "size": 5702 },
                { "name": "Qwen2.5-VL-7B-Instruct/tokenizer.json", "url": "https://huggingface.co/Qwen/Qwen2.5-VL-7B-Instruct/resolve/main/tokenizer.json", "size": 7031645 },
                { "name": "Qwen2.5-VL-7B-Instruct/vocab.json", "url": "https://huggingface.co/Qwen/Qwen2.5-VL-7B-Instruct/resolve/main/vocab.json", "size": 2776833 },
                { "name": "Qwen2.5-VL-7B-Instruct/merges.txt", "url": "https://huggingface.co/Qwen/Qwen2.5-VL-7B-Instruct/resolve/main/merges.txt", "size": 1671839 },
                { "name": "Qwen2.5-VL-7B-Instruct/model-00001-of-00005.safetensors", "url": "https://huggingface.co/Qwen/Qwen2.5-VL-7B-Instruct/resolve/main/model-00001-of-00005.safetensors", "size": 3900233256 },
                { "name": "Qwen2.5-VL-7B-Instruct/model-00002-of-00005.safetensors", "url": "https://huggingface.co/Qwen/Qwen2.5-VL-7B-Instruct/resolve/main/model-00002-of-00005.safetensors", "size": 3864726320 },
                { "name": "Qwen2.5-VL-7B-Instruct/model-00003-of-00005.safetensors", "url": "https://huggingface.co/Qwen/Qwen2.5-VL-7B-Instruct/resolve/main/model-00003-of-00005.safetensors", "size": 3864726424 },
                { "name": "Qwen2.5-VL-7B-Instruct/model-00004-of-00005.safetensors", "url": "https://huggingface.co/Qwen/Qwen2.5-VL-7B-Instruct/resolve/main/model-00004-of-00005.safetensors", "size": 3864733680 },
                { "name": "Qwen2.5-VL-7B-Instruct/model-00005-of-00005.safetensors", "url": "https://huggingface.co/Qwen/Qwen2.5-VL-7B-Instruct/resolve/main/model-00005-of-00005.safetensors", "size": 1089994880 },
                { "name": "Qwen2.5-VL-7B-Instruct/model.safetensors.index.json", "url": "https://huggingface.co/Qwen/Qwen2.5-VL-7B-Instruct/resolve/main/model.safetensors.index.json", "size": 57619 }
              ]
            },
            {
              "id": "clip-vit-large-patch14",
              "name": "CLIP ViT-L/14 (текстовый кодировщик для обучения LoRA)",
              "dir": "hf/clip-vit-large-patch14",
              "check": "model.safetensors",
              "hint": "Скачайте репозиторий openai/clip-vit-large-patch14 с huggingface.co (около 1,7 ГБ) и положите его файлы прямо в каталог пакета; файлы flax/tf/pytorch_model.bin не нужны.",
              "files": [
                { "name": "clip-vit-large-patch14/config.json", "url": "https://huggingface.co/openai/clip-vit-large-patch14/resolve/main/config.json", "size": 4519 },
                { "name": "clip-vit-large-patch14/preprocessor_config.json", "url": "https://huggingface.co/openai/clip-vit-large-patch14/resolve/main/preprocessor_config.json", "size": 316 },
                { "name": "clip-vit-large-patch14/special_tokens_map.json", "url": "https://huggingface.co/openai/clip-vit-large-patch14/resolve/main/special_tokens_map.json", "size": 389 },
                { "name": "clip-vit-large-patch14/tokenizer_config.json", "url": "https://huggingface.co/openai/clip-vit-large-patch14/resolve/main/tokenizer_config.json", "size": 905 },
                { "name": "clip-vit-large-patch14/tokenizer.json", "url": "https://huggingface.co/openai/clip-vit-large-patch14/resolve/main/tokenizer.json", "size": 2224003 },
                { "name": "clip-vit-large-patch14/vocab.json", "url": "https://huggingface.co/openai/clip-vit-large-patch14/resolve/main/vocab.json", "size": 961143 },
                { "name": "clip-vit-large-patch14/merges.txt", "url": "https://huggingface.co/openai/clip-vit-large-patch14/resolve/main/merges.txt", "size": 524619 },
                { "name": "clip-vit-large-patch14/model.safetensors", "url": "https://huggingface.co/openai/clip-vit-large-patch14/resolve/main/model.safetensors", "size": 1710540580 }
              ]
            },
            {
              "id": "shotcut",
              "name": "Shotcut (портативный, с melt)",
              "dir": "Shotcut",
              "os": "windows",
              "check": "melt.exe",
              "system": {
                "commands": ["melt", "qmelt"],
                "versionArgs": "--version",
                "minVersion": "7.0"
              },
              "hint": "Скачайте портативный архив shotcut-win64-26.8.1.zip со страницы релизов mltframework/shotcut и распакуйте в каталог пакета; годится и уже установленный Shotcut — тогда укажите путь к melt в форме плагина.",
              "files": [
                {
                  "name": "shotcut-win64-26.8.1.zip",
                  "url": "https://github.com/mltframework/shotcut/releases/download/v26.8.1/shotcut-win64-26.8.1.zip",
                  "unpack": "zip"
                }
              ]
            },
            {
              "id": "blender",
              "name": "Blender (портативный архив)",
              "dir": "Blender",
              "os": "windows",
              "check": "blender.exe",
              "system": {
                "commands": ["blender", "blender.exe"],
                "versionArgs": "--version",
                "minVersion": "3.0"
              },
              "hint": "Скачайте blender-5.2.1-windows-x64.zip (https://download.blender.org/release/Blender5.2/) и распакуйте в каталог пакета; годится и уже установленный Blender 3.0 и новее — тогда укажите путь к blender в форме плагина (на Windows это blender.exe в каталоге программы, Linux: apt install blender, macOS: brew install --cask blender).",
              "files": [
                {
                  "name": "blender-5.2.1-windows-x64.zip",
                  "url": "https://download.blender.org/release/Blender5.2/blender-5.2.1-windows-x64.zip",
                  "unpack": "zip"
                }
              ]
            },
            {
              "id": "ffmpeg",
              "name": "ffmpeg (сборка essentials)",
              "dir": "ffmpeg",
              "os": "windows",
              "check": "ffmpeg.exe",
              "system": {
                "commands": ["ffmpeg", "ffmpeg.exe"],
                "versionArgs": "-version",
                "minVersion": "6.0"
              },
              "hint": "Скачайте ffmpeg-9.0.1-essentials_build.zip (https://www.gyan.dev/ffmpeg/builds/packages/) и распакуйте в каталог пакета; годится и уже установленный ffmpeg версии 6.0 и новее — он находится в PATH сам (Linux: apt install ffmpeg, macOS: brew install ffmpeg).",
              "files": [
                {
                  "name": "ffmpeg-9.0.1-essentials_build.zip",
                  "url": "https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.1-essentials_build.zip",
                  "unpack": "zip"
                }
              ]
            },
            {
              "id": "llama.cpp",
              "name": "llama.cpp (llama-server)",
              "dir": "llama",
              "os": "windows",
              "check": "llama-server.exe",
              "hint": "Скачайте архив llama-<версия>-bin-win-cuda-12.4-x64.zip (или win-cpu-x64) со страницы релизов ggml-org/llama.cpp и распакуйте в каталог пакета.",
              "files": [
                { "github": "ggml-org/llama.cpp", "asset": "llama-*-bin-{flavor}.zip", "unpack": "zip" },
                { "github": "ggml-org/llama.cpp", "asset": "cudart-llama-bin-{flavor}.zip", "unpack": "zip", "when": "cuda" }
              ]
            }
          ]
        }
        """;

    /// <summary>Путь seed-файла справочника пакетов в dataDir (ТЗ v1.42).</summary>
    public const string PackagesPath = "models/packages.json";

    /// <summary>
    /// Workflow-шаблон Kandinsky 5.0 T2V для ComfyUI (ТЗ v1.41, todo36_4) — API-формат
    /// POST /prompt, сконвертирован из официального шаблона Comfy-Org video_kandinsky5_t2v.
    /// Плейсхолдеры подставляет ComfyUiConnector: {prompt}/{negative} — текст внутри кавычек,
    /// "{seed}"/"{width}"/"{height}"/"{length}"/"{steps}" — числа вместе с кавычками,
    /// {job} — код задания в имени выходного файла. Имя DiT-файла — как в репозитории
    /// моделей (todo36_3), остальные — стандартные имена comfy-перепаковок.
    /// </summary>
    private const string KandinskyWorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "DualCLIPLoader", "inputs": { "clip_name1": "qwen_2.5_vl_7b_fp8_scaled.safetensors", "clip_name2": "clip_l.safetensors", "type": "kandinsky5", "device": "default" } },
            "2": { "class_type": "CLIPTextEncode", "inputs": { "text": "{negative}", "clip": ["1", 0] } },
            "3": { "class_type": "ModelSamplingSD3", "inputs": { "shift": 5, "model": ["4", 0] } },
            "4": { "class_type": "UNETLoader", "inputs": { "unet_name": "Kandinsky-5.0-T2V-Lite-sft-5s.safetensors", "weight_dtype": "default" } },
            "5": { "class_type": "Kandinsky5ImageToVideo", "inputs": { "width": "{width}", "height": "{height}", "length": "{length}", "batch_size": 1, "positive": ["7", 0], "negative": ["2", 0], "vae": ["6", 0] } },
            "6": { "class_type": "VAELoader", "inputs": { "vae_name": "hunyuan_video_vae_bf16.safetensors" } },
            "7": { "class_type": "CLIPTextEncode", "inputs": { "text": "{prompt}", "clip": ["1", 0] } },
            "8": { "class_type": "KSampler", "inputs": { "seed": "{seed}", "steps": "{steps}", "cfg": 5, "sampler_name": "euler_ancestral", "scheduler": "beta", "denoise": 1, "model": ["3", 0], "positive": ["5", 0], "negative": ["5", 1], "latent_image": ["5", 2] } },
            "9": { "class_type": "VAEDecode", "inputs": { "samples": ["8", 0], "vae": ["6", 0] } },
            "10": { "class_type": "CreateVideo", "inputs": { "fps": 24, "images": ["9", 0] } },
            "11": { "class_type": "SaveVideo", "inputs": { "filename_prefix": "AI2P/{job}", "format": "mp4", "codec": "h264", "video": ["10", 0] } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон Kandinsky 5.0 I2V для ComfyUI (T-258) — API-формат POST /prompt,
    /// сконвертирован из официального шаблона Comfy-Org video_kandinsky5_i2v
    /// (подграф «Image to Video (Kandinsky5)»). Отличия от T2V-шаблона:
    /// узлы 12 (LoadImage) и 13 (ImageScale) дают стартовый кадр в
    /// Kandinsky5ImageToVideo.start_image, а чистый латент этого кадра (выход cond_latent)
    /// вставляется в начало результата сэмплера узлами ReplaceVideoLatentFrames +
    /// NormalizeVideoLatentStart — без них первый кадр «плывёт» относительно картинки.
    /// Плейсхолдер {image} — ИМЯ ФАЙЛА в каталоге input ComfyUI: коннектор заливает туда
    /// стартовый кадр (POST /upload/image) и подставляет сюда его имя.
    /// </summary>
    private const string KandinskyI2vWorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "DualCLIPLoader", "inputs": { "clip_name1": "qwen_2.5_vl_7b_fp8_scaled.safetensors", "clip_name2": "clip_l.safetensors", "type": "kandinsky5", "device": "default" } },
            "2": { "class_type": "CLIPTextEncode", "inputs": { "text": "{negative}", "clip": ["1", 0] } },
            "3": { "class_type": "ModelSamplingSD3", "inputs": { "shift": 5, "model": ["4", 0] } },
            "4": { "class_type": "UNETLoader", "inputs": { "unet_name": "kandinsky5lite_i2v_5s.safetensors", "weight_dtype": "default" } },
            "5": { "class_type": "Kandinsky5ImageToVideo", "inputs": { "width": "{width}", "height": "{height}", "length": "{length}", "batch_size": 1, "positive": ["7", 0], "negative": ["2", 0], "vae": ["6", 0], "start_image": ["13", 0] } },
            "6": { "class_type": "VAELoader", "inputs": { "vae_name": "hunyuan_video_vae_bf16.safetensors" } },
            "7": { "class_type": "CLIPTextEncode", "inputs": { "text": "{prompt}", "clip": ["1", 0] } },
            "8": { "class_type": "KSampler", "inputs": { "seed": "{seed}", "steps": "{steps}", "cfg": 5, "sampler_name": "euler_ancestral", "scheduler": "beta", "denoise": 1, "model": ["3", 0], "positive": ["5", 0], "negative": ["5", 1], "latent_image": ["5", 2] } },
            "9": { "class_type": "VAEDecode", "inputs": { "samples": ["15", 0], "vae": ["6", 0] } },
            "10": { "class_type": "CreateVideo", "inputs": { "fps": 24, "images": ["9", 0] } },
            "11": { "class_type": "SaveVideo", "inputs": { "filename_prefix": "AI2P/{job}", "format": "mp4", "codec": "h264", "video": ["10", 0] } },
            "12": { "class_type": "LoadImage", "inputs": { "image": "{image}" } },
            "13": { "class_type": "ImageScale", "inputs": { "image": ["12", 0], "upscale_method": "lanczos", "width": "{width}", "height": "{height}", "crop": "center" } },
            "14": { "class_type": "ReplaceVideoLatentFrames", "inputs": { "destination": ["8", 0], "source": ["5", 3], "index": 0 } },
            "15": { "class_type": "NormalizeVideoLatentStart", "inputs": { "latent": ["14", 0], "start_frame_count": 4, "reference_frame_count": 5 } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон Z-Image Turbo для ComfyUI (T-241) — API-формат POST /prompt,
    /// сконвертирован из официального шаблона Comfy-Org image_z_image_turbo.
    /// Отрицательное условие берётся ConditioningZeroOut, а не вторым CLIPTextEncode:
    /// у turbo-режима cfg = 1, и негативный текст на результат не влияет вовсе.
    /// Результат сохраняет SaveImage — коннектор забирает его так же, как ролики
    /// (в outputs он ищет любые объекты с полем filename).
    /// </summary>
    private const string ZImageWorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "CLIPLoader", "inputs": { "clip_name": "qwen_3_4b.safetensors", "type": "lumina2", "device": "default" } },
            "2": { "class_type": "UNETLoader", "inputs": { "unet_name": "z_image_turbo_bf16.safetensors", "weight_dtype": "default" } },
            "3": { "class_type": "VAELoader", "inputs": { "vae_name": "ae.safetensors" } },
            "4": { "class_type": "CLIPTextEncode", "inputs": { "text": "{prompt}", "clip": ["1", 0] } },
            "5": { "class_type": "ConditioningZeroOut", "inputs": { "conditioning": ["4", 0] } },
            "6": { "class_type": "EmptySD3LatentImage", "inputs": { "width": "{width}", "height": "{height}", "batch_size": 1 } },
            "7": { "class_type": "ModelSamplingAuraFlow", "inputs": { "shift": 3, "model": ["2", 0] } },
            "8": { "class_type": "KSampler", "inputs": { "seed": "{seed}", "steps": "{steps}", "cfg": 1, "sampler_name": "res_multistep", "scheduler": "simple", "denoise": 1, "model": ["7", 0], "positive": ["4", 0], "negative": ["5", 0], "latent_image": ["6", 0] } },
            "9": { "class_type": "VAEDecode", "inputs": { "samples": ["8", 0], "vae": ["3", 0] } },
            "10": { "class_type": "SaveImage", "inputs": { "filename_prefix": "AI2P/{job}", "images": ["9", 0] } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон Qwen-Image 2512 для ComfyUI (T-241) — API-формат POST /prompt,
    /// сконвертирован из официального шаблона Comfy-Org image_qwen_image (базовый режим:
    /// steps 20, cfg 4, sampler euler/simple, сдвиг ModelSamplingAuraFlow 3.1).
    /// Turbo-ветка шаблона (8 шагов, cfg 1) сюда не перенесена: ей нужен отдельный файл
    /// Lightning-LoRA, которого в манифесте установки нет.
    /// </summary>
    private const string QwenImageWorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "CLIPLoader", "inputs": { "clip_name": "qwen_2.5_vl_7b_fp8_scaled.safetensors", "type": "qwen_image", "device": "default" } },
            "2": { "class_type": "UNETLoader", "inputs": { "unet_name": "qwen_image_2512_fp8_e4m3fn.safetensors", "weight_dtype": "default" } },
            "3": { "class_type": "VAELoader", "inputs": { "vae_name": "qwen_image_vae.safetensors" } },
            "4": { "class_type": "CLIPTextEncode", "inputs": { "text": "{prompt}", "clip": ["1", 0] } },
            "5": { "class_type": "CLIPTextEncode", "inputs": { "text": "{negative}", "clip": ["1", 0] } },
            "6": { "class_type": "EmptySD3LatentImage", "inputs": { "width": "{width}", "height": "{height}", "batch_size": 1 } },
            "7": { "class_type": "ModelSamplingAuraFlow", "inputs": { "shift": 3.1, "model": ["2", 0] } },
            "8": { "class_type": "KSampler", "inputs": { "seed": "{seed}", "steps": "{steps}", "cfg": 4, "sampler_name": "euler", "scheduler": "simple", "denoise": 1, "model": ["7", 0], "positive": ["4", 0], "negative": ["5", 0], "latent_image": ["6", 0] } },
            "9": { "class_type": "VAEDecode", "inputs": { "samples": ["8", 0], "vae": ["3", 0] } },
            "10": { "class_type": "SaveImage", "inputs": { "filename_prefix": "AI2P/{job}", "images": ["9", 0] } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон Qwen-Image-Edit 2511 для ComfyUI (T-241) — API-формат POST /prompt,
    /// сконвертирован из официального шаблона Comfy-Org image_qwen_image_edit_2511
    /// (базовый режим: steps 40, cfg 3). Плейсхолдер {image} — ИМЯ ФАЙЛА в каталоге input
    /// ComfyUI: коннектор заливает туда исходную картинку (POST /upload/image) и
    /// подставляет сюда её имя, как это уже сделано для Kandinsky I2V (T-258).
    /// Текст указания и исходное изображение сводит вместе TextEncodeQwenImageEditPlus —
    /// поэтому у него на входе и clip, и vae, и сама картинка.
    /// </summary>
    private const string QwenImageEditWorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "CLIPLoader", "inputs": { "clip_name": "qwen_2.5_vl_7b_fp8_scaled.safetensors", "type": "qwen_image", "device": "default" } },
            "2": { "class_type": "UNETLoader", "inputs": { "unet_name": "qwen_image_edit_2511_fp8mixed.safetensors", "weight_dtype": "default" } },
            "3": { "class_type": "VAELoader", "inputs": { "vae_name": "qwen_image_vae.safetensors" } },
            "4": { "class_type": "LoadImage", "inputs": { "image": "{image}" } },
            "5": { "class_type": "FluxKontextImageScale", "inputs": { "image": ["4", 0] } },
            "6": { "class_type": "TextEncodeQwenImageEditPlus", "inputs": { "prompt": "{prompt}", "clip": ["1", 0], "vae": ["3", 0], "image1": ["5", 0] } },
            "7": { "class_type": "TextEncodeQwenImageEditPlus", "inputs": { "prompt": "{negative}", "clip": ["1", 0], "vae": ["3", 0], "image1": ["5", 0] } },
            "8": { "class_type": "ModelSamplingAuraFlow", "inputs": { "shift": 3.1, "model": ["2", 0] } },
            "9": { "class_type": "CFGNorm", "inputs": { "strength": 1, "pre_cfg": false, "model": ["8", 0] } },
            "10": { "class_type": "VAEEncode", "inputs": { "pixels": ["5", 0], "vae": ["3", 0] } },
            "11": { "class_type": "KSampler", "inputs": { "seed": "{seed}", "steps": "{steps}", "cfg": 3, "sampler_name": "euler", "scheduler": "simple", "denoise": 1, "model": ["9", 0], "positive": ["6", 0], "negative": ["7", 0], "latent_image": ["10", 0] } },
            "12": { "class_type": "VAEDecode", "inputs": { "samples": ["11", 0], "vae": ["3", 0] } },
            "13": { "class_type": "SaveImage", "inputs": { "filename_prefix": "AI2P/{job}", "images": ["12", 0] } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон Wan 2.2 14B «текст → ролик» (T-15-S0) — API-формат POST /prompt,
    /// сконвертирован из официального шаблона Comfy-Org video_wan2_2_14B_t2v (подграф
    /// «Text to Video (Wan2.2)»). Модель СОСТАВНАЯ: два эксперта, «шумный» и «чистый»,
    /// поэтому в графе два UNETLoader и два KSamplerAdvanced — первый считает шаги 0…10,
    /// второй доводит остальные.
    /// Turbo-ветка шаблона (4 шага, cfg 1) сюда не перенесена: ей нужны отдельные файлы
    /// lightx2v-LoRA, которых в манифесте установки нет (так же решено в T-241).
    /// ВНИМАНИЕ на будущее: граница экспертов — ЧИСЛО, а число шагов — плейсхолдер
    /// {steps}; меняя steps в профайле, поправьте и границу здесь (арифметики над
    /// плейсхолдерами у коннектора нет).
    /// </summary>
    private const string WanT2vWorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "UNETLoader", "inputs": { "unet_name": "wan2.2_t2v_high_noise_14B_fp8_scaled.safetensors", "weight_dtype": "default" } },
            "2": { "class_type": "UNETLoader", "inputs": { "unet_name": "wan2.2_t2v_low_noise_14B_fp8_scaled.safetensors", "weight_dtype": "default" } },
            "3": { "class_type": "CLIPLoader", "inputs": { "clip_name": "umt5_xxl_fp8_e4m3fn_scaled.safetensors", "type": "wan", "device": "default" } },
            "4": { "class_type": "VAELoader", "inputs": { "vae_name": "wan_2.1_vae.safetensors" } },
            "5": { "class_type": "CLIPTextEncode", "inputs": { "text": "{prompt}", "clip": ["3", 0] } },
            "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "{negative}", "clip": ["3", 0] } },
            "7": { "class_type": "EmptyHunyuanLatentVideo", "inputs": { "width": "{width}", "height": "{height}", "length": "{length}", "batch_size": 1 } },
            "8": { "class_type": "ModelSamplingSD3", "inputs": { "shift": 5, "model": ["1", 0] } },
            "9": { "class_type": "ModelSamplingSD3", "inputs": { "shift": 5, "model": ["2", 0] } },
            "10": { "class_type": "KSamplerAdvanced", "inputs": { "add_noise": "enable", "noise_seed": "{seed}", "steps": "{steps}", "cfg": 3.5, "sampler_name": "euler", "scheduler": "simple", "start_at_step": 0, "end_at_step": 10, "return_with_leftover_noise": "enable", "model": ["8", 0], "positive": ["5", 0], "negative": ["6", 0], "latent_image": ["7", 0] } },
            "11": { "class_type": "KSamplerAdvanced", "inputs": { "add_noise": "disable", "noise_seed": 0, "steps": "{steps}", "cfg": 3.5, "sampler_name": "euler", "scheduler": "simple", "start_at_step": 10, "end_at_step": "{steps}", "return_with_leftover_noise": "disable", "model": ["9", 0], "positive": ["5", 0], "negative": ["6", 0], "latent_image": ["10", 0] } },
            "12": { "class_type": "VAEDecode", "inputs": { "samples": ["11", 0], "vae": ["4", 0] } },
            "13": { "class_type": "CreateVideo", "inputs": { "fps": 16, "images": ["12", 0] } },
            "14": { "class_type": "SaveVideo", "inputs": { "filename_prefix": "AI2P/{job}", "format": "mp4", "codec": "h264", "video": ["13", 0] } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон Wan 2.2 14B «картинка → ролик» (T-15-S0) — из официального шаблона
    /// Comfy-Org video_wan2_2_14B_i2v. Отличия от t2v-шаблона ровно два: свои файлы весов
    /// и узлы 15 (LoadImage) → 7 (WanImageToVideo), который сам готовит и условия, и латент
    /// нужной длины от стартового кадра. Плейсхолдер {image} — ИМЯ ФАЙЛА в каталоге input
    /// ComfyUI: коннектор заливает туда кадр (POST /upload/image) и подставляет сюда имя.
    /// </summary>
    private const string WanI2vWorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "UNETLoader", "inputs": { "unet_name": "wan2.2_i2v_high_noise_14B_fp8_scaled.safetensors", "weight_dtype": "default" } },
            "2": { "class_type": "UNETLoader", "inputs": { "unet_name": "wan2.2_i2v_low_noise_14B_fp8_scaled.safetensors", "weight_dtype": "default" } },
            "3": { "class_type": "CLIPLoader", "inputs": { "clip_name": "umt5_xxl_fp8_e4m3fn_scaled.safetensors", "type": "wan", "device": "default" } },
            "4": { "class_type": "VAELoader", "inputs": { "vae_name": "wan_2.1_vae.safetensors" } },
            "5": { "class_type": "CLIPTextEncode", "inputs": { "text": "{prompt}", "clip": ["3", 0] } },
            "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "{negative}", "clip": ["3", 0] } },
            "7": { "class_type": "WanImageToVideo", "inputs": { "positive": ["5", 0], "negative": ["6", 0], "vae": ["4", 0], "width": "{width}", "height": "{height}", "length": "{length}", "batch_size": 1, "start_image": ["15", 0] } },
            "8": { "class_type": "ModelSamplingSD3", "inputs": { "shift": 5, "model": ["1", 0] } },
            "9": { "class_type": "ModelSamplingSD3", "inputs": { "shift": 5, "model": ["2", 0] } },
            "10": { "class_type": "KSamplerAdvanced", "inputs": { "add_noise": "enable", "noise_seed": "{seed}", "steps": "{steps}", "cfg": 3.5, "sampler_name": "euler", "scheduler": "simple", "start_at_step": 0, "end_at_step": 10, "return_with_leftover_noise": "enable", "model": ["8", 0], "positive": ["7", 0], "negative": ["7", 1], "latent_image": ["7", 2] } },
            "11": { "class_type": "KSamplerAdvanced", "inputs": { "add_noise": "disable", "noise_seed": 0, "steps": "{steps}", "cfg": 3.5, "sampler_name": "euler", "scheduler": "simple", "start_at_step": 10, "end_at_step": "{steps}", "return_with_leftover_noise": "disable", "model": ["9", 0], "positive": ["7", 0], "negative": ["7", 1], "latent_image": ["10", 0] } },
            "12": { "class_type": "VAEDecode", "inputs": { "samples": ["11", 0], "vae": ["4", 0] } },
            "13": { "class_type": "CreateVideo", "inputs": { "fps": 16, "images": ["12", 0] } },
            "14": { "class_type": "SaveVideo", "inputs": { "filename_prefix": "AI2P/{job}", "format": "mp4", "codec": "h264", "video": ["13", 0] } },
            "15": { "class_type": "LoadImage", "inputs": { "image": "{image}" } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон HunyuanVideo 1.5 720p «текст → ролик» (T-15-S0) — из официального
    /// шаблона Comfy-Org video_hunyuan_video_1.5_720p_t2v. В шаблоне включены только узлы
    /// базовой генерации: увеличение до 1080p, EasyCache и вторая ступень сэмплинга там
    /// выключены (проброс), поэтому модель берётся прямо у UNETLoader.
    /// Сэмплинг разложен на части (шум, сэмплер, сигмы, guider) — так в самом шаблоне,
    /// и это то место, куда позже встанет своё расписание сигм.
    /// </summary>
    private const string HunyuanVideo15WorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "UNETLoader", "inputs": { "unet_name": "hunyuanvideo1.5_720p_t2v_fp16.safetensors", "weight_dtype": "default" } },
            "2": { "class_type": "DualCLIPLoader", "inputs": { "clip_name1": "qwen_2.5_vl_7b_fp8_scaled.safetensors", "clip_name2": "byt5_small_glyphxl_fp16.safetensors", "type": "hunyuan_video_15", "device": "default" } },
            "3": { "class_type": "VAELoader", "inputs": { "vae_name": "hunyuanvideo15_vae_fp16.safetensors" } },
            "4": { "class_type": "CLIPTextEncode", "inputs": { "text": "{prompt}", "clip": ["2", 0] } },
            "5": { "class_type": "CLIPTextEncode", "inputs": { "text": "{negative}", "clip": ["2", 0] } },
            "6": { "class_type": "EmptyHunyuanVideo15Latent", "inputs": { "width": "{width}", "height": "{height}", "length": "{length}", "batch_size": 1 } },
            "7": { "class_type": "BasicScheduler", "inputs": { "model": ["1", 0], "scheduler": "simple", "steps": "{steps}", "denoise": 1 } },
            "8": { "class_type": "RandomNoise", "inputs": { "noise_seed": "{seed}" } },
            "9": { "class_type": "KSamplerSelect", "inputs": { "sampler_name": "euler" } },
            "10": { "class_type": "ModelSamplingSD3", "inputs": { "shift": 7, "model": ["1", 0] } },
            "11": { "class_type": "CFGGuider", "inputs": { "model": ["10", 0], "positive": ["4", 0], "negative": ["5", 0], "cfg": 6 } },
            "12": { "class_type": "SamplerCustomAdvanced", "inputs": { "noise": ["8", 0], "guider": ["11", 0], "sampler": ["9", 0], "sigmas": ["7", 0], "latent_image": ["6", 0] } },
            "13": { "class_type": "VAEDecode", "inputs": { "samples": ["12", 0], "vae": ["3", 0] } },
            "14": { "class_type": "CreateVideo", "inputs": { "fps": 24, "images": ["13", 0] } },
            "15": { "class_type": "SaveVideo", "inputs": { "filename_prefix": "AI2P/{job}", "format": "mp4", "codec": "h264", "video": ["14", 0] } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон LTX-2.5 (T-15-S0) — из официального шаблона Comfy-Org
    /// video_ltx2_5_t2v. Это ЕДИНСТВЕННАЯ открытая модель, которая рисует видео И ЗВУК
    /// одним проходом: звуковой латент идёт рядом с видео (LTXVEmptyLatentAudio →
    /// LTXVConcatAVLatent), после сэмплинга они разделяются, и звук попадает в CreateVideo.
    /// Отличий от шаблона два, оба вынужденные:
    /// 1) генерация ОДНОСТАДИЙНАЯ. В шаблоне первый проход идёт в половинном размере, а
    ///    второй поднимает его узлом LTXVLatentUpsampler; арифметики над {width}/{height}
    ///    у коннектора нет, поэтому «половину» было бы неоткуда взять, а сводка задания
    ///    называла бы человеку не тот размер. Берём полный набор сигм сразу в целевом.
    /// 2) улучшение промпта (TextGenerateLTX2Prompt) выключено: оно тянет ещё одну
    ///    языковую модель, которой нет в манифесте, и переписывает текст задачи молча.
    /// Число шагов задают СИГМЫ (модель дистиллированная), поэтому {steps} в этом шаблоне
    /// не участвует — в профайле steps стоит для сводки задания.
    /// </summary>
    private const string Ltx25WorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "UNETLoader", "inputs": { "unet_name": "ltx-2.5-22b-distilled-transformer-comfy-int8-convrot.safetensors", "weight_dtype": "default" } },
            "2": { "class_type": "CLIPLoader", "inputs": { "clip_name": "gemma4-12b-with-proj-ltx-2.5-comfy-int8-convrot.safetensors", "type": "ltxv", "device": "default" } },
            "3": { "class_type": "VAELoader", "inputs": { "vae_name": "ltx-2.5-video-vae-bf16.safetensors" } },
            "4": { "class_type": "VAELoader", "inputs": { "vae_name": "ltx-2.5-audio-vae-bf16.safetensors" } },
            "5": { "class_type": "CLIPTextEncode", "inputs": { "text": "{prompt}", "clip": ["2", 0] } },
            "6": { "class_type": "CLIPTextEncode", "inputs": { "text": "{negative}", "clip": ["2", 0] } },
            "7": { "class_type": "LTXVConditioning", "inputs": { "positive": ["5", 0], "negative": ["6", 0], "frame_rate": 24 } },
            "8": { "class_type": "EmptyLTXVLatentVideo", "inputs": { "width": "{width}", "height": "{height}", "length": "{length}", "batch_size": 1 } },
            "9": { "class_type": "LTXVEmptyLatentAudio", "inputs": { "frames_number": "{length}", "frame_rate": 24, "batch_size": 1, "audio_vae": ["4", 0] } },
            "10": { "class_type": "LTXVConcatAVLatent", "inputs": { "video_latent": ["8", 0], "audio_latent": ["9", 0] } },
            "11": { "class_type": "RandomNoise", "inputs": { "noise_seed": "{seed}" } },
            "12": { "class_type": "KSamplerSelect", "inputs": { "sampler_name": "euler_ancestral" } },
            "13": { "class_type": "ManualSigmas", "inputs": { "sigmas": "1.0, 0.99375, 0.9875, 0.98125, 0.975, 0.909375, 0.725, 0.421875, 0.0" } },
            "14": { "class_type": "LTXVDualCFGGuider", "inputs": { "model": ["1", 0], "positive": ["7", 0], "negative": ["7", 1], "video_cfg": 1, "audio_cfg": 1 } },
            "15": { "class_type": "SamplerCustomAdvanced", "inputs": { "noise": ["11", 0], "guider": ["14", 0], "sampler": ["12", 0], "sigmas": ["13", 0], "latent_image": ["10", 0] } },
            "16": { "class_type": "LTXVSeparateAVLatent", "inputs": { "av_latent": ["15", 0] } },
            "17": { "class_type": "VAEDecodeTiled", "inputs": { "samples": ["16", 0], "vae": ["3", 0], "tile_size": 512, "overlap": 64, "temporal_size": 64, "temporal_overlap": 16 } },
            "18": { "class_type": "LTXVAudioVAEDecode", "inputs": { "samples": ["16", 1], "audio_vae": ["4", 0] } },
            "19": { "class_type": "CreateVideo", "inputs": { "fps": 24, "images": ["17", 0], "audio": ["18", 0] } },
            "20": { "class_type": "SaveVideo", "inputs": { "filename_prefix": "AI2P/{job}", "format": "mp4", "codec": "h264", "video": ["19", 0] } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон FLUX.2 [klein] 4B «текст → картинка» (T-19-S0) — API-формат
    /// POST /prompt, собран из официального шаблона Comfy-Org
    /// image_flux2_klein_text_to_image, подграф «Text to Image (Flux.2 Klein 4B)».
    /// Семейство FLUX.2 сэмплится НЕ узлом KSampler, а связкой
    /// RandomNoise + KSamplerSelect + Flux2Scheduler + CFGGuider → SamplerCustomAdvanced:
    /// расписание сигм у FLUX.2 зависит от РАЗМЕРА кадра (Flux2Scheduler принимает width и
    /// height), поэтому число шагов живёт в планировщике, а не в сэмплере.
    /// Взята ветка базовых весов: cfg 5 и настоящее отрицательное условие. У дистиллята
    /// (flux-2-klein-4b.safetensors) cfg равен 1 и негатив зануляется — это другой файл
    /// весов, и в манифесте его нет.
    /// </summary>
    private const string Flux2Klein4bWorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "CLIPLoader", "inputs": { "clip_name": "qwen_3_4b.safetensors", "type": "flux2", "device": "default" } },
            "2": { "class_type": "UNETLoader", "inputs": { "unet_name": "flux-2-klein-base-4b.safetensors", "weight_dtype": "default" } },
            "3": { "class_type": "VAELoader", "inputs": { "vae_name": "flux2-vae.safetensors" } },
            "4": { "class_type": "CLIPTextEncode", "inputs": { "text": "{prompt}", "clip": ["1", 0] } },
            "5": { "class_type": "CLIPTextEncode", "inputs": { "text": "{negative}", "clip": ["1", 0] } },
            "6": { "class_type": "EmptyFlux2LatentImage", "inputs": { "width": "{width}", "height": "{height}", "batch_size": 1 } },
            "7": { "class_type": "Flux2Scheduler", "inputs": { "steps": "{steps}", "width": "{width}", "height": "{height}" } },
            "8": { "class_type": "KSamplerSelect", "inputs": { "sampler_name": "euler" } },
            "9": { "class_type": "RandomNoise", "inputs": { "noise_seed": "{seed}" } },
            "10": { "class_type": "CFGGuider", "inputs": { "model": ["2", 0], "positive": ["4", 0], "negative": ["5", 0], "cfg": 5 } },
            "11": { "class_type": "SamplerCustomAdvanced", "inputs": { "noise": ["9", 0], "guider": ["10", 0], "sampler": ["8", 0], "sigmas": ["7", 0], "latent_image": ["6", 0] } },
            "12": { "class_type": "VAEDecode", "inputs": { "samples": ["11", 0], "vae": ["3", 0] } },
            "13": { "class_type": "SaveImage", "inputs": { "filename_prefix": "AI2P/{job}", "images": ["12", 0] } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон FLUX.2 [klein] 4B «картинка + указание → картинка» (T-19-S0),
    /// собран из официального шаблона Comfy-Org image_flux2_klein_image_edit_4b_base
    /// (подграф «Image Edit (Flux.2 Klein 4B)», ветка с ОДНОЙ исходной картинкой: второй
    /// ссылочный кадр коннектору передать нечем).
    /// Исходная картинка приезжает узлом LoadImage по имени {image} — коннектор заливает
    /// файл в каталог input ComfyUI (POST /upload/image) и подставляет сюда имя (T-258).
    /// Дальше она ужимается до мегапикселя (ImageScaleToTotalPixels), кодируется в латент
    /// и кладётся ССЫЛОЧНЫМ латентом в оба условия (ReferenceLatent) — так работают все
    /// редактирующие модели ComfyUI. Размер результата берётся у самой картинки
    /// (GetImageSize), поэтому {width}/{height} в этом шаблоне не участвуют.
    /// </summary>
    private const string Flux2Klein4bEditWorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "CLIPLoader", "inputs": { "clip_name": "qwen_3_4b.safetensors", "type": "flux2", "device": "default" } },
            "2": { "class_type": "UNETLoader", "inputs": { "unet_name": "flux-2-klein-base-4b.safetensors", "weight_dtype": "default" } },
            "3": { "class_type": "VAELoader", "inputs": { "vae_name": "flux2-vae.safetensors" } },
            "4": { "class_type": "LoadImage", "inputs": { "image": "{image}" } },
            "5": { "class_type": "ImageScaleToTotalPixels", "inputs": { "image": ["4", 0], "upscale_method": "nearest-exact", "megapixels": 1, "resolution_steps": 1 } },
            "6": { "class_type": "GetImageSize", "inputs": { "image": ["5", 0] } },
            "7": { "class_type": "VAEEncode", "inputs": { "pixels": ["5", 0], "vae": ["3", 0] } },
            "8": { "class_type": "CLIPTextEncode", "inputs": { "text": "{prompt}", "clip": ["1", 0] } },
            "9": { "class_type": "CLIPTextEncode", "inputs": { "text": "{negative}", "clip": ["1", 0] } },
            "10": { "class_type": "ReferenceLatent", "inputs": { "conditioning": ["8", 0], "latent": ["7", 0] } },
            "11": { "class_type": "ReferenceLatent", "inputs": { "conditioning": ["9", 0], "latent": ["7", 0] } },
            "12": { "class_type": "EmptyFlux2LatentImage", "inputs": { "width": ["6", 0], "height": ["6", 1], "batch_size": 1 } },
            "13": { "class_type": "Flux2Scheduler", "inputs": { "steps": "{steps}", "width": ["6", 0], "height": ["6", 1] } },
            "14": { "class_type": "KSamplerSelect", "inputs": { "sampler_name": "euler" } },
            "15": { "class_type": "RandomNoise", "inputs": { "noise_seed": "{seed}" } },
            "16": { "class_type": "CFGGuider", "inputs": { "model": ["2", 0], "positive": ["10", 0], "negative": ["11", 0], "cfg": 5 } },
            "17": { "class_type": "SamplerCustomAdvanced", "inputs": { "noise": ["15", 0], "guider": ["16", 0], "sampler": ["14", 0], "sigmas": ["13", 0], "latent_image": ["12", 0] } },
            "18": { "class_type": "VAEDecode", "inputs": { "samples": ["17", 0], "vae": ["3", 0] } },
            "19": { "class_type": "SaveImage", "inputs": { "filename_prefix": "AI2P/{job}", "images": ["18", 0] } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон Kandinsky 5.0 Image Lite (T-19-S0), собран из официального шаблона
    /// Comfy-Org image_kandinsky5_t2i (подграф «Text to Image (Kandinsky5)»).
    /// Два текстовых энкодера сразу (DualCLIPLoader с типом kandinsky5_image): qwen2.5-vl
    /// понимает описание, clip_l держит стиль — те же файлы, что у видео-записей
    /// Kandinsky. VAE ЧУЖОЙ, флаксовый ae.safetensors: так в официальном шаблоне и в
    /// инструкции вендора (weights/flux/vae → ComfyUI/models/vae).
    /// </summary>
    private const string Kandinsky5ImageWorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "DualCLIPLoader", "inputs": { "clip_name1": "qwen_2.5_vl_7b_fp8_scaled.safetensors", "clip_name2": "clip_l.safetensors", "type": "kandinsky5_image", "device": "default" } },
            "2": { "class_type": "UNETLoader", "inputs": { "unet_name": "kandinsky5lite_t2i.safetensors", "weight_dtype": "default" } },
            "3": { "class_type": "VAELoader", "inputs": { "vae_name": "ae.safetensors" } },
            "4": { "class_type": "CLIPTextEncode", "inputs": { "text": "{prompt}", "clip": ["1", 0] } },
            "5": { "class_type": "CLIPTextEncode", "inputs": { "text": "{negative}", "clip": ["1", 0] } },
            "6": { "class_type": "EmptyLatentImage", "inputs": { "width": "{width}", "height": "{height}", "batch_size": 1 } },
            "7": { "class_type": "ModelSamplingSD3", "inputs": { "shift": 3, "model": ["2", 0] } },
            "8": { "class_type": "KSampler", "inputs": { "seed": "{seed}", "steps": "{steps}", "cfg": 3.5, "sampler_name": "euler", "scheduler": "simple", "denoise": 1, "model": ["7", 0], "positive": ["4", 0], "negative": ["5", 0], "latent_image": ["6", 0] } },
            "9": { "class_type": "VAEDecode", "inputs": { "samples": ["8", 0], "vae": ["3", 0] } },
            "10": { "class_type": "SaveImage", "inputs": { "filename_prefix": "AI2P/{job}", "images": ["9", 0] } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон SD 3.5 Large (T-19-S0), собран из официального шаблона Comfy-Org
    /// sd3.5_simple_example. Модель ставится ОДНИМ файлом, поэтому загрузчик один
    /// (CheckpointLoaderSimple): выход 0 — модель, 1 — текстовые энкодеры внутри файла,
    /// 2 — VAE. Значение cfg 4.01 — не описка, а число из официального шаблона.
    /// </summary>
    private const string Sd35LargeWorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "sd3.5_large_fp8_scaled.safetensors" } },
            "2": { "class_type": "CLIPTextEncode", "inputs": { "text": "{prompt}", "clip": ["1", 1] } },
            "3": { "class_type": "CLIPTextEncode", "inputs": { "text": "{negative}", "clip": ["1", 1] } },
            "4": { "class_type": "EmptySD3LatentImage", "inputs": { "width": "{width}", "height": "{height}", "batch_size": 1 } },
            "5": { "class_type": "KSampler", "inputs": { "seed": "{seed}", "steps": "{steps}", "cfg": 4.01, "sampler_name": "euler", "scheduler": "sgm_uniform", "denoise": 1, "model": ["1", 0], "positive": ["2", 0], "negative": ["3", 0], "latent_image": ["4", 0] } },
            "6": { "class_type": "VAEDecode", "inputs": { "samples": ["5", 0], "vae": ["1", 2] } },
            "7": { "class_type": "SaveImage", "inputs": { "filename_prefix": "AI2P/{job}", "images": ["6", 0] } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон SDXL 1.0 (T-19-S0), собран из официального шаблона Comfy-Org
    /// image_sdxl_simple: один чекпойнт, обычный KSampler, 25 шагов, cfg 7,
    /// dpmpp_2m/karras. Второй ступени (SDXL Refiner) в шаблоне нет намеренно — это
    /// отдельный файл весов, а запись заведена как САМАЯ ЛЁГКАЯ в справочнике.
    /// </summary>
    private const string SdxlWorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "CheckpointLoaderSimple", "inputs": { "ckpt_name": "sd_xl_base_1.0.safetensors" } },
            "2": { "class_type": "CLIPTextEncode", "inputs": { "text": "{prompt}", "clip": ["1", 1] } },
            "3": { "class_type": "CLIPTextEncode", "inputs": { "text": "{negative}", "clip": ["1", 1] } },
            "4": { "class_type": "EmptyLatentImage", "inputs": { "width": "{width}", "height": "{height}", "batch_size": 1 } },
            "5": { "class_type": "KSampler", "inputs": { "seed": "{seed}", "steps": "{steps}", "cfg": 7, "sampler_name": "dpmpp_2m", "scheduler": "karras", "denoise": 1, "model": ["1", 0], "positive": ["2", 0], "negative": ["3", 0], "latent_image": ["4", 0] } },
            "6": { "class_type": "VAEDecode", "inputs": { "samples": ["5", 0], "vae": ["1", 2] } },
            "7": { "class_type": "SaveImage", "inputs": { "filename_prefix": "AI2P/{job}", "images": ["6", 0] } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон FLUX.2 [dev] (T-19-S0), собран из официального шаблона Comfy-Org
    /// image_flux2_text_to_image — ветка БЕЗ turbo-LoRA (выключатель шаблона стоит в
    /// «выкл», и обе ветки дают 20 шагов против 8 у turbo).
    /// Отличие от klein: dev управляется не парой «положительное/отрицательное», а
    /// ВСТРОЕННОЙ подсказкой силы (FluxGuidance 4 → BasicGuider), поэтому отрицательного
    /// условия в графе нет вовсе и поле negative профайла на результат не влияет.
    /// Текстовый энкодер тоже свой — Mistral 3 Small, а не Qwen3.
    /// </summary>
    private const string Flux2DevWorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "CLIPLoader", "inputs": { "clip_name": "mistral_3_small_flux2_fp8.safetensors", "type": "flux2", "device": "default" } },
            "2": { "class_type": "UNETLoader", "inputs": { "unet_name": "flux2_dev_fp8mixed.safetensors", "weight_dtype": "default" } },
            "3": { "class_type": "VAELoader", "inputs": { "vae_name": "flux2-vae.safetensors" } },
            "4": { "class_type": "CLIPTextEncode", "inputs": { "text": "{prompt}", "clip": ["1", 0] } },
            "5": { "class_type": "FluxGuidance", "inputs": { "conditioning": ["4", 0], "guidance": 4 } },
            "6": { "class_type": "BasicGuider", "inputs": { "model": ["2", 0], "conditioning": ["5", 0] } },
            "7": { "class_type": "EmptyFlux2LatentImage", "inputs": { "width": "{width}", "height": "{height}", "batch_size": 1 } },
            "8": { "class_type": "Flux2Scheduler", "inputs": { "steps": "{steps}", "width": "{width}", "height": "{height}" } },
            "9": { "class_type": "KSamplerSelect", "inputs": { "sampler_name": "euler" } },
            "10": { "class_type": "RandomNoise", "inputs": { "noise_seed": "{seed}" } },
            "11": { "class_type": "SamplerCustomAdvanced", "inputs": { "noise": ["10", 0], "guider": ["6", 0], "sampler": ["9", 0], "sigmas": ["8", 0], "latent_image": ["7", 0] } },
            "12": { "class_type": "VAEDecode", "inputs": { "samples": ["11", 0], "vae": ["3", 0] } },
            "13": { "class_type": "SaveImage", "inputs": { "filename_prefix": "AI2P/{job}", "images": ["12", 0] } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон Hunyuan3D 2.1 для ComfyUI (T-20-S0) — API-формат POST /prompt,
    /// сконвертирован из официального шаблона Comfy-Org <c>3d_hunyuan3d-v2.1</c>.
    /// Текстового энкодера в графе нет вовсе: условие даёт CLIP Vision по картинке
    /// (Hunyuan3Dv2Conditioning), поэтому плейсхолдера {prompt} тут нет и быть не может —
    /// описание задачи модель не читает, она читает СТАРТОВЫЙ КАДР ({image}).
    /// Результат — форма без цвета: ComfyUI считает только шейп-ветку Hunyuan3D
    /// (VAEDecodeHunyuan3D → VoxelToMesh → SaveGLB); ветка окраски из репозитория Tencent
    /// (та, которой нужны Linux и CUDA 12.4) в ядре движка не реализована.
    /// </summary>
    private const string Hunyuan3D21WorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "ImageOnlyCheckpointLoader", "inputs": { "ckpt_name": "hunyuan_3d_v2.1.safetensors" } },
            "2": { "class_type": "LoadImage", "inputs": { "image": "{image}" } },
            "3": { "class_type": "ModelSamplingAuraFlow", "inputs": { "shift": 1, "model": ["1", 0] } },
            "4": { "class_type": "CLIPVisionEncode", "inputs": { "clip_vision": ["1", 1], "image": ["2", 0], "crop": "center" } },
            "5": { "class_type": "Hunyuan3Dv2Conditioning", "inputs": { "clip_vision_output": ["4", 0] } },
            "6": { "class_type": "EmptyLatentHunyuan3Dv2", "inputs": { "resolution": 4096, "batch_size": 1 } },
            "7": { "class_type": "KSampler", "inputs": { "seed": "{seed}", "steps": "{steps}", "cfg": 5, "sampler_name": "euler", "scheduler": "normal", "denoise": 1, "model": ["3", 0], "positive": ["5", 0], "negative": ["5", 1], "latent_image": ["6", 0] } },
            "8": { "class_type": "VAEDecodeHunyuan3D", "inputs": { "samples": ["7", 0], "vae": ["1", 2], "num_chunks": 8000, "octree_resolution": 256 } },
            "9": { "class_type": "VoxelToMesh", "inputs": { "voxel": ["8", 0], "algorithm": "surface net", "threshold": 0.6 } },
            "10": { "class_type": "SaveGLB", "inputs": { "mesh": ["9", 0], "filename_prefix": "AI2P/{job}" } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон TRELLIS 2 для ComfyUI (T-20-S0) — API-формат POST /prompt,
    /// сконвертирован из официального шаблона Comfy-Org
    /// <c>3d_pixal3d_trellis2_image_to_model</c>, ветка TRELLIS.2 (в шаблоне её включает
    /// выключатель PrimitiveBoolean; вторая ветка — Pixal3D, у неё свой файл весов и своя
    /// подготовка кадра через MoGe, поэтому веса MoGe в манифест не попали).
    /// Ступеней четыре, и это не украшение: структура (воксели) → форма → повышение
    /// разрешения → текстура. У каждой свой сэмплер, а сила следования картинке задаётся
    /// парой CFGOverride + RescaleCFG — так шаблон повторяет поведение авторского конвейера.
    /// {steps} стоит у ступени ФОРМЫ: остальные три меняют результат мало, а времени
    /// съедают столько же. Фон снимается BiRefNet и кадр обрезается по маске: 3D из
    /// картинки с фоном получается заметно хуже.
    /// </summary>
    private const string Trellis2WorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "LoadImage", "inputs": { "image": "{image}" } },
            "2": { "class_type": "LoadBackgroundRemovalModel", "inputs": { "bg_removal_name": "birefnet.safetensors" } },
            "3": { "class_type": "RemoveBackground", "inputs": { "bg_removal_model": ["2", 0], "image": ["1", 0] } },
            "4": { "class_type": "ImageCropToMask", "inputs": { "images": ["1", 0], "masks": ["3", 0], "width": 1024, "height": 1024, "pad_factor": 1.1, "grow_mask": 0, "background": "#000000" } },
            "5": { "class_type": "CLIPVisionLoader", "inputs": { "clip_name": "dino_v3_vit_l.safetensors" } },
            "6": { "class_type": "Trellis2Conditioning", "inputs": { "clip_vision_model": ["5", 0], "image": ["4", 0] } },
            "7": { "class_type": "UNETLoader", "inputs": { "unet_name": "trellis_2_int8_convrot.safetensors", "weight_dtype": "default" } },
            "8": { "class_type": "VAELoader", "inputs": { "vae_name": "trellis_2_shape_vae_bf16.safetensors" } },
            "9": { "class_type": "VAELoader", "inputs": { "vae_name": "trellis_2_texture_vae_bf16.safetensors" } },
            "10": { "class_type": "CFGOverride", "inputs": { "model": ["7", 0], "cfg": 1, "start_percent": 0.667, "end_percent": 1 } },
            "11": { "class_type": "RescaleCFG", "inputs": { "model": ["10", 0], "multiplier": 0.7 } },
            "12": { "class_type": "ModelSamplingSD3", "inputs": { "model": ["11", 0], "shift": 5 } },
            "13": { "class_type": "EmptyTrellis2LatentStructure", "inputs": { "batch_size": 1 } },
            "14": { "class_type": "KSampler", "inputs": { "seed": "{seed}", "steps": 12, "cfg": 7.5, "sampler_name": "euler", "scheduler": "normal", "denoise": 1, "model": ["12", 0], "positive": ["6", 0], "negative": ["6", 1], "latent_image": ["13", 0] } },
            "15": { "class_type": "VaeDecodeStructureTrellis2", "inputs": { "samples": ["14", 0], "vae": ["8", 0], "resolution": "32" } },
            "16": { "class_type": "Trellis2ShapeStage", "inputs": { "positive": ["6", 0], "negative": ["6", 1], "voxel": ["15", 0] } },
            "17": { "class_type": "CFGOverride", "inputs": { "model": ["7", 0], "cfg": 1, "start_percent": 0.769, "end_percent": 1 } },
            "18": { "class_type": "RescaleCFG", "inputs": { "model": ["17", 0], "multiplier": 0.5 } },
            "19": { "class_type": "KSampler", "inputs": { "seed": "{seed}", "steps": "{steps}", "cfg": 7.5, "sampler_name": "euler", "scheduler": "normal", "denoise": 1, "model": ["18", 0], "positive": ["16", 0], "negative": ["16", 1], "latent_image": ["16", 2] } },
            "20": { "class_type": "Trellis2UpsampleStage", "inputs": { "positive": ["16", 0], "negative": ["16", 1], "shape_latent": ["19", 0], "vae": ["8", 0], "target_resolution": "1536" } },
            "21": { "class_type": "KSampler", "inputs": { "seed": "{seed}", "steps": 12, "cfg": 7.5, "sampler_name": "euler", "scheduler": "simple", "denoise": 1, "model": ["18", 0], "positive": ["20", 0], "negative": ["20", 1], "latent_image": ["20", 2] } },
            "22": { "class_type": "VaeDecodeShapeTrellis", "inputs": { "samples": ["21", 0], "vae": ["8", 0] } },
            "23": { "class_type": "Trellis2TextureStage", "inputs": { "positive": ["20", 0], "negative": ["20", 1], "shape_latent": ["21", 0] } },
            "24": { "class_type": "KSampler", "inputs": { "seed": "{seed}", "steps": 12, "cfg": 1, "sampler_name": "euler", "scheduler": "normal", "denoise": 1, "model": ["7", 0], "positive": ["23", 0], "negative": ["23", 1], "latent_image": ["23", 2] } },
            "25": { "class_type": "VaeDecodeTextureTrellis", "inputs": { "samples": ["24", 0], "vae": ["9", 0], "shape_subdivides": ["22", 1] } },
            "26": { "class_type": "DecimateMesh", "inputs": { "mesh": ["22", 0], "target_face_count": 700000, "placement_mode": "midpoint" } },
            "27": { "class_type": "MeshSmoothNormals", "inputs": { "mesh": ["26", 0], "crease_angle": 180 } },
            "28": { "class_type": "PaintMesh", "inputs": { "mesh": ["27", 0], "voxel_colors": ["25", 0] } },
            "29": { "class_type": "SaveGLB", "inputs": { "mesh": ["28", 0], "filename_prefix": "AI2P/{job}" } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон TripoSplat для ComfyUI (T-20-S0) — API-формат POST /prompt,
    /// сконвертирован из официального шаблона Comfy-Org
    /// <c>3d_triposplat_image_to_gaussian_splat</c> (оба его подграфа раскрыты).
    /// На выходе не сетка, а ГАУССОВЫ СПЛАТЫ: SplatToFile3D пакует их в .spz, и SaveGLB
    /// кладёт файл как есть (узел принимает и splat-файлы, а не только сетку).
    /// Оборот вокруг объекта (RenderSplat → CreateVideo → SaveVideo) из шаблона НЕ
    /// перенесён: это отдельный рендер на каждую генерацию, а результат задания и так
    /// файл сплатов.
    /// </summary>
    private const string TripoSplatWorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "LoadImage", "inputs": { "image": "{image}" } },
            "2": { "class_type": "LoadBackgroundRemovalModel", "inputs": { "bg_removal_name": "birefnet.safetensors" } },
            "3": { "class_type": "RemoveBackground", "inputs": { "bg_removal_model": ["2", 0], "image": ["1", 0] } },
            "4": { "class_type": "TripoSplatPreprocessImage", "inputs": { "image": ["1", 0], "mask": ["3", 0], "erode_radius": 1, "size": 1024 } },
            "5": { "class_type": "CLIPVisionLoader", "inputs": { "clip_name": "dino_v3_vit_h.safetensors" } },
            "6": { "class_type": "VAELoader", "inputs": { "vae_name": "flux2-vae.safetensors" } },
            "7": { "class_type": "VAELoader", "inputs": { "vae_name": "triposplat_vae_decoder_fp16.safetensors" } },
            "8": { "class_type": "TripoSplatConditioning", "inputs": { "clip_vision": ["5", 0], "vae": ["6", 0], "image": ["4", 0] } },
            "9": { "class_type": "UNETLoader", "inputs": { "unet_name": "triposplat_fp16.safetensors", "weight_dtype": "default" } },
            "10": { "class_type": "KSampler", "inputs": { "seed": "{seed}", "steps": "{steps}", "cfg": 3, "sampler_name": "dpmpp_2m", "scheduler": "simple", "denoise": 1, "model": ["9", 0], "positive": ["8", 0], "negative": ["8", 1], "latent_image": ["8", 2] } },
            "11": { "class_type": "VAEDecodeTripoSplat", "inputs": { "samples": ["10", 0], "vae": ["7", 0], "num_gaussians": 262144, "seed": "{seed}" } },
            "12": { "class_type": "SplatToFile3D", "inputs": { "splat": ["11", 0], "format": "spz" } },
            "13": { "class_type": "SaveGLB", "inputs": { "mesh": ["12", 0], "filename_prefix": "AI2P/{job}" } }
          }
        }
        """;

    /// <summary>
    /// Workflow-шаблон вариантов Kandinsky 5.0 Video Lite (T-15-S0). Граф тот же, что у
    /// уже работающей записи sft-5s: варианты отличаются ровно двумя величинами — файлом
    /// весов и силой следования тексту (у no-CFG и distil16 обучающий guidance_weight
    /// равен 1.0, то есть отрицательный текст на результат не влияет вовсе).
    /// {unet} и {cfg} здесь — ТЕХНИЧЕСКИЕ плейсхолдеры: их подставляет
    /// <see cref="KandinskyLiteWorkflow"/> при записи seed-файла, до того как шаблон
    /// увидит коннектор. Пять почти одинаковых копий этой константы разошлись бы молча.
    /// </summary>
    private const string KandinskyLiteVariantWorkflowJson = """
        {
          "_seed": 22,
          "prompt": {
            "1": { "class_type": "DualCLIPLoader", "inputs": { "clip_name1": "qwen_2.5_vl_7b_fp8_scaled.safetensors", "clip_name2": "clip_l.safetensors", "type": "kandinsky5", "device": "default" } },
            "2": { "class_type": "CLIPTextEncode", "inputs": { "text": "{negative}", "clip": ["1", 0] } },
            "3": { "class_type": "ModelSamplingSD3", "inputs": { "shift": 5, "model": ["4", 0] } },
            "4": { "class_type": "UNETLoader", "inputs": { "unet_name": "{unet}", "weight_dtype": "default" } },
            "5": { "class_type": "Kandinsky5ImageToVideo", "inputs": { "width": "{width}", "height": "{height}", "length": "{length}", "batch_size": 1, "positive": ["7", 0], "negative": ["2", 0], "vae": ["6", 0] } },
            "6": { "class_type": "VAELoader", "inputs": { "vae_name": "hunyuan_video_vae_bf16.safetensors" } },
            "7": { "class_type": "CLIPTextEncode", "inputs": { "text": "{prompt}", "clip": ["1", 0] } },
            "8": { "class_type": "KSampler", "inputs": { "seed": "{seed}", "steps": "{steps}", "cfg": "{cfg}", "sampler_name": "euler_ancestral", "scheduler": "beta", "denoise": 1, "model": ["3", 0], "positive": ["5", 0], "negative": ["5", 1], "latent_image": ["5", 2] } },
            "9": { "class_type": "VAEDecode", "inputs": { "samples": ["8", 0], "vae": ["6", 0] } },
            "10": { "class_type": "CreateVideo", "inputs": { "fps": 24, "images": ["9", 0] } },
            "11": { "class_type": "SaveVideo", "inputs": { "filename_prefix": "AI2P/{job}", "format": "mp4", "codec": "h264", "video": ["10", 0] } }
          }
        }
        """;

    /// <summary>Шаблон варианта Kandinsky Lite: подставить файл весов и силу следования.</summary>
    private static string KandinskyLiteWorkflow(string unetName, int cfg) =>
        KandinskyLiteVariantWorkflowJson
            .Replace("{unet}", unetName, StringComparison.Ordinal)
            .Replace("\"{cfg}\"", cfg.ToString(), StringComparison.Ordinal);

    private readonly Database _db;
    private readonly EventStore _events;
    private readonly FileStore _files;

    /// <summary>
    /// Проверка права активации (ТЗ v1.41, todo36_4): вызывается при сохранении модели
    /// с isActive = true; вернуть текст ошибки — активация запрещена (модель с манифестом
    /// установки не установлена), null — можно. Подставляется при старте приложения
    /// (проверяет ModelInstallService — слой Connectors, сюда напрямую не виден).
    /// </summary>
    public Func<AiModel, string?>? ActivationGuard { get; set; }

    private readonly ServerScope _scope;

    public AiModelService(Database db, EventStore events, FileStore files, ServerScope? scope = null)
    {
        _db = db;
        _events = events;
        _files = files;
        _scope = scope ?? ServerScope.Standalone();
    }

    /// <summary>
    /// ОБЩАЯ часть записи справочника (название, признак «кастом», удаление) принадлежит
    /// серверу-владельцу: локальная модель заводится там, где работает, облачная — в общей
    /// части справочника (дирижёр). Право проверяется при правке этих полей.
    ///
    /// ПЕР-СЕРВЕРНОЙ части (активность, профайл, установка) это НЕ касается (T-8-S1):
    /// локальная модель ставится на каждом компьютере отдельно, поэтому её настройка
    /// открывается на любом сервере — см. <see cref="CanConfigure"/>.
    /// </summary>
    public bool CanWrite(AiModel model) => _scope.CanWrite(model.ServerId);

    /// <summary>
    /// Настройку модели можно открыть ЗДЕСЬ (T-8-S1): у локальной — на любом сервере
    /// (свой профайл, свои пути, своя активность, своя установка), у облачной — только
    /// там, где правится сама запись.
    /// </summary>
    public bool CanConfigure(AiModel model) => model.IsLocal || CanWrite(model);

    /// <summary>
    /// Владелец записи справочника (ТЗ гл. 6, этап 42): локальная модель — этот сервер,
    /// облачная — общая часть справочника (дирижёр).
    ///
    /// Отдельный случай — НЕ-дирижёр: у новой записи профайл ещё пуст, и «локальной» она
    /// становится только после его правки. Считать её общей нельзя — тогда сервер не смог бы
    /// дописать собственный профайл и поставить модель у себя. Поэтому всё, что заведено не
    /// на дирижёре, принадлежит своему серверу: писатель у строки остаётся один.
    ///
    /// ВАЖНО (T-227): спрашивать это можно ТОЛЬКО там, где запись заводят или правят ЗДЕСЬ —
    /// в <see cref="Create"/>, <see cref="Update"/> и при импорте инсталлятора. Пересчёт
    /// производного признака размещения (<see cref="SyncLocalFlags"/>) зовётся при каждом
    /// открытии организации, в том числе на реплике, — и, спрашивая здесь владельца, рядовой
    /// сервер присваивал себе ВСЕ облачные записи дирижёра (у них <c>server_id</c> пуст).
    /// На дирижёре они после этого становились чужими и переставали правиться.
    /// </summary>
    private string? OwnerFor(bool isLocal) => isLocal || !_scope.IsConductor ? _scope.ServerId : null;

    /// <summary>
    /// Подставить в запись справочника то, что зависит от СЕРВЕРА (T-8-S1): у локальной
    /// модели активность своя на каждом сервере, поэтому наружу отдаётся активность ЭТОГО
    /// сервера, а списком <see cref="AiModel.Servers"/> — где она включена ещё.
    /// </summary>
    /// <param name="parts">Пер-серверные строки ЭТОЙ модели (все серверы); null — прочитать.</param>
    private AiModel Decorate(AiModel model, List<AiModelServer>? parts = null)
    {
        model.ServerCode = _scope.CodeOf(model.ServerId);
        model.ServerName = _scope.NameOf(model.ServerId);
        model.IsReadOnly = !CanWrite(model);
        model.CanConfigure = CanConfigure(model);
        if (!model.IsLocal)
        {
            model.Servers = [];
            return model;   // облачная модель одна на всю организацию (ключ API — у организации)
        }
        if (parts is null)
        {
            using var conn = _db.Open();
            parts = PartsOf(conn, null, model.Id);
        }
        foreach (var part in parts)
        {
            part.ServerCode = _scope.CodeOf(part.ServerId);
            part.ServerName = _scope.NameOf(part.ServerId);
            part.IsSelf = part.ServerId == _scope.ServerId;
        }
        model.Servers = parts;
        model.IsActive = parts.Any(p => p.IsSelf && p.IsActive);
        return model;
    }

    // --- ПЕР-СЕРВЕРНАЯ ЧАСТЬ ЗАПИСИ СПРАВОЧНИКА (T-8-S1) ---
    // Устроена так же, как пер-серверная часть проекта (project_servers, ТЗ гл. 6): строка
    // на каждый сервер, пишет её только он, остальным она видна.

    /// <summary>Пер-серверные строки ВСЕХ моделей одним запросом — для списка справочника.</summary>
    private static Dictionary<string, List<AiModelServer>> AllParts(SqliteConnection conn) =>
        Sql.Query(conn, null, "SELECT * FROM ai_model_servers WHERE deleted_at IS NULL", MapPart)
            .GroupBy(p => p.ModelId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

    /// <summary>Пер-серверные строки одной модели (все серверы).</summary>
    private static List<AiModelServer> PartsOf(SqliteConnection conn, SqliteTransaction? tx, string modelId) =>
        Sql.Query(conn, tx,
            "SELECT * FROM ai_model_servers WHERE model_id=@m AND deleted_at IS NULL", MapPart,
            ("@m", modelId));

    /// <summary>Строка ЭТОГО сервера; null — модель здесь ещё не настраивали.</summary>
    private AiModelServer? LocalPart(SqliteConnection conn, SqliteTransaction? tx, string modelId) =>
        Sql.Query(conn, tx, """
            SELECT * FROM ai_model_servers
            WHERE model_id=@m AND server_id=@s AND deleted_at IS NULL
            """, MapPart, ("@m", modelId), ("@s", _scope.ServerId)).FirstOrDefault();

    /// <summary>
    /// Записать активность локальной модели НА ЭТОМ СЕРВЕРЕ (T-8-S1). Строки ещё нет и
    /// включать не просят — писать нечего: «нет строки» и значит «здесь не включена».
    /// </summary>
    private void WriteLocalPart(SqliteConnection conn, SqliteTransaction? tx, string modelId,
        bool isActive, DateTime now)
    {
        var part = LocalPart(conn, tx, modelId);
        if (part is null)
        {
            if (!isActive)
            {
                return;
            }
            Sql.Exec(conn, tx, """
                INSERT INTO ai_model_servers (id, model_id, server_id, is_active, created_at, updated_at)
                VALUES (@id, @m, @s, 1, @now, @now)
                """,
                ("@id", Guid.NewGuid().ToString()), ("@m", modelId), ("@s", _scope.ServerId),
                ("@now", Sql.ToDb(now)));
            return;
        }
        if (part.IsActive == isActive)
        {
            return;   // ничего не изменилось — в журнал изменений писать нечего
        }
        Sql.Exec(conn, tx, "UPDATE ai_model_servers SET is_active=@a, updated_at=@now WHERE id=@id",
            ("@a", isActive ? 1 : 0), ("@now", Sql.ToDb(now)), ("@id", part.Id));
    }

    /// <summary>
    /// Серверы, где ЛОКАЛЬНАЯ модель включена (T-8-S1) — внутренние ключи. Ими проверяется
    /// исполнитель: агент с локальной моделью работает только там, где она включена.
    /// </summary>
    public List<string> ActiveServersOf(string modelId)
    {
        using var conn = _db.Open();
        return PartsOf(conn, null, modelId).Where(p => p.IsActive).Select(p => p.ServerId).ToList();
    }

    /// <summary>
    /// РАЗОВЫЙ ПЕРЕНОС общей активности локальных моделей в пер-серверные строки (T-8-S1).
    /// До этой версии «активна» у локальной модели было одно на всю организацию, и на сервере,
    /// где модель не установлена, она всё равно горела активной, а выключить её там было
    /// нельзя — запись принадлежит другому серверу.
    ///
    /// Каждый сервер переносит СВОЁ: строка заводится только у записи, которую он и вёл
    /// (владелец — он сам либо владельца нет вовсе). Чужая локальная модель остаётся здесь
    /// выключенной, пока её тут не установят, — это и есть исправление жалобы.
    ///
    /// Идемпотентно: у модели, чья строка уже есть, ничего не делается. Вызывается при
    /// каждом открытии организации — как перенос каталогов проектов (ТЗ гл. 6, этап 42).
    /// </summary>
    public int MigrateActivationToServerParts()
    {
        using var conn = _db.Open();
        var now = DateTime.UtcNow;
        var moved = 0;
        var rows = Sql.Query(conn, null, """
            SELECT id, is_active, server_id FROM ai_models WHERE is_local=1 AND deleted_at IS NULL
            """,
            r => (Id: r.S("id"), Active: r.B("is_active"), Owner: r.SN("server_id")));
        foreach (var row in rows)
        {
            if (LocalPart(conn, null, row.Id) is not null)
            {
                continue;
            }
            var mine = row.Owner is not { Length: > 0 } || row.Owner == _scope.ServerId;
            if (!row.Active || !mine)
            {
                continue;   // «строки нет» — это и есть «здесь не включена»
            }
            WriteLocalPart(conn, null, row.Id, isActive: true, now);
            moved++;
        }
        return moved;
    }

    /// <summary>Пути файлов модели по схеме из ТЗ п. 2.9.</summary>
    public static string ProfilePathOf(string modelId) => $"models/profile_{modelId}.json";

    public static string ScopePathOf(string modelId) => $"models/scope_{modelId}.json";

    /// <summary>Декларация возможностей по умолчанию (шаблон ТЗ п. 7.3).</summary>
    public static string DefaultScopeJson(string name) => $$"""
        {
          "id": "{{name.ToLowerInvariant()}}",
          "inputs":  ["text/markdown"],
          "outputs": ["text/markdown"],
          "skills":  [],
          "limits":  {},
          "cost":    {}
        }
        """;

    public List<AiModel> List(bool includeDeleted = false)
    {
        using var conn = _db.Open();
        var sql = "SELECT * FROM ai_models" + (includeDeleted ? "" : " WHERE deleted_at IS NULL") + " ORDER BY name";
        var models = Sql.Query(conn, null, sql, Map);
        // пер-серверные строки берутся одним запросом на весь список (T-8-S1)
        var parts = AllParts(conn);
        foreach (var model in models)
        {
            Decorate(model, parts.GetValueOrDefault(model.Id) ?? []);
        }
        return models;
    }

    /// <summary>
    /// Список для справочника в UI (ТЗ v1.42, todo36_5): у каждой модели дополнительно
    /// заполнены навыки из декларации возможностей — колонка «навыки» списка.
    /// </summary>
    public List<AiModel> ListWithSkills()
    {
        var models = List();
        foreach (var model in models)
        {
            model.Skills = SkillsOf(model);
            // форматы результата — оттуда же, из декларации (T-286-S0): по ним форма
            // исполнителя отбирает текстовые модели в суфлёры
            model.Outputs = OutputsOf(model);
            // настройка LoRA и референсной картинки — из профайла (T-13-S1): колонка «LoRA»
            // справочника показывает именно её отметку
            ReadProfileSettings(model);
        }
        return models;
    }

    /// <summary>
    /// Настройка работы с LoRA и с референсной картинкой — из ПРОФАЙЛА модели (T-13-S1).
    /// Профайл читается один раз на обе настройки: файл один, а разбирать его дважды на
    /// каждую строку списка справочника незачем. Профайла нет (чужая локальная запись,
    /// файл ещё не приехал) — «не поддерживается» с причиной «настройка не заполнена»:
    /// это ровно то, что система про такую модель знает.
    /// </summary>
    public void ReadProfileSettings(AiModel model)
    {
        var profileJson = ReadProfile(model);
        model.Lora = LoraSettings.Parse(profileJson);
        model.RefImage = RefImageSettings.Parse(profileJson);
        model.RefAudio = RefAudioSettings.Parse(profileJson);
        // модель-суфлёр (T-286-S0): нужна ли этой модели вторая, текстовая, которая готовит
        // управляющий json. Секции нет — суфлёр не нужен, и это обычный случай
        model.Prompter = PrompterSettings.Parse(profileJson);
    }

    /// <summary>
    /// Форматы РЕЗУЛЬТАТА модели из её декларации возможностей (<c>outputs</c>, ТЗ п. 7.3);
    /// нечитаемая декларация — пустой список. Читается вместе с навыками: по ним форма
    /// исполнителя отличает ТЕКСТОВУЮ модель от медийной (T-286-S0), а подбор исполнителя —
    /// режим навыка (SkillIo, T-257).
    /// </summary>
    public List<string> OutputsOf(AiModel model)
    {
        var abs = _files.Abs(model.CapabilitiesPath);
        if (model.CapabilitiesPath.Length == 0 || !File.Exists(abs))
        {
            return [];
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(abs));
            return JsonRead.Strings(doc.RootElement, "outputs");
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [];
        }
    }

    /// <summary>Навыки модели из её декларации возможностей; нечитаемая декларация — пустой список.</summary>
    public List<ModelSkill> SkillsOf(AiModel model)
    {
        var result = new List<ModelSkill>();
        var abs = _files.Abs(model.CapabilitiesPath);
        if (model.CapabilitiesPath.Length == 0 || !File.Exists(abs))
        {
            return result;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(abs));
            if (!doc.RootElement.TryGetProperty("skills", out var skills) ||
                skills.ValueKind != JsonValueKind.Array)
            {
                return result;
            }
            foreach (var item in skills.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
                {
                    continue;
                }
                result.Add(new ModelSkill
                {
                    Name = name.GetString()!,
                    Score = item.TryGetProperty("score", out var s) && s.TryGetInt32(out var score) ? score : 0,
                });
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return result; // повреждённая декларация — навыки не показываем
        }
        return result;
    }

    public AiModel? Get(string id)
    {
        using var conn = _db.Open();
        var model = Sql.Query(conn, null, "SELECT * FROM ai_models WHERE id=@id", Map, ("@id", id))
            .FirstOrDefault();
        if (model is null)
        {
            return null;
        }
        Decorate(model);
        // настройка LoRA и картинки нужна и одной записи: форму модели открывают по ней
        ReadProfileSettings(model);
        model.Outputs = OutputsOf(model);
        return model;
    }

    public AiModel Create(AiModel model, string? actorId)
    {
        model.Name = model.Name.Trim();
        if (model.Name.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.aiModel.1"));
        }
        // в релизе добавляемые модели — всегда кастомные; менять признак можно только в debug (п. 2.9)
        if (!AppInfo.IsDebug)
        {
            model.IsCustom = true;
        }
        EnsureActivationAllowed(model);
        // новая запись справочника принадлежит серверу, если она локальная (ТЗ гл. 6, этап 42);
        // у новой модели профайл ещё пуст, поэтому окончательный владелец проставится
        // вместе с признаком размещения — в SyncLocalFlag сразу после создания
        model.ServerId = OwnerFor(model.IsLocal);
        _scope.EnsureCanWrite(model.ServerId, Loc.T("msg.aiModel.2"));
        var now = DateTime.UtcNow;
        model.CreatedAt = now;
        model.UpdatedAt = now;
        model.ProfilePath = ProfilePathOf(model.Id);
        model.CapabilitiesPath = ScopePathOf(model.Id);

        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        EnsureUniqueName(conn, tx, model.Name, model.Id);
        // локальная часть справочника нумеруется кодом своего сервера (ТЗ гл. 6): M-7-S1
        model.DisplayId = Database.NextDisplayId(conn, tx, "M", _scope.CodeOf(model.ServerId));

        Sql.Exec(conn, tx, """
            INSERT INTO ai_models (id, display_id, name, is_active, is_custom, is_local,
                                   profile_path, capabilities_path, server_id, created_at, updated_at)
            VALUES (@id, @did, @name, @active, @custom, @local, @profile, @caps, @server,
                    @created, @updated)
            """,
            ("@server", model.ServerId),
            ("@id", model.Id), ("@did", model.DisplayId), ("@name", model.Name),
            ("@active", model.IsActive ? 1 : 0), ("@custom", model.IsCustom ? 1 : 0),
            ("@local", model.IsLocal ? 1 : 0),
            ("@profile", model.ProfilePath), ("@caps", model.CapabilitiesPath),
            ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));

        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ModelCreated,
            EntityType = "ai_model",
            EntityId = model.Id,
            PayloadJson = JsonSerializer.Serialize(new { model.DisplayId, model.Name, model.IsCustom }),
        });

        tx.Commit();
        EnsureFiles(model, "{}");
        // размещение (локальная / в облаке) выводится из профайла (ТЗ v1.42): у новой модели
        // профайл ещё пустой — флаг проставится после его правки, при следующем SyncLocalFlags.
        // Владелец здесь проставляется законно: запись ЗАВОДЯТ на этом сервере (T-227)
        SyncLocalFlag(model, assignOwner: true);
        // локальная модель включается НА ЭТОМ СЕРВЕРЕ (T-8-S1): запись заводят здесь, значит
        // и работать ей здесь. Общая колонка активности у локальной модели больше не читается
        if (model.IsLocal && model.IsActive)
        {
            WriteLocalPart(conn, null, model.Id, isActive: true, now);
        }
        return Decorate(model);
    }

    public AiModel Update(AiModel model, string? actorId)
    {
        model.Name = model.Name.Trim();
        if (model.Name.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.aiModel.1"));
        }
        EnsureActivationAllowed(model);
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        // У ЗАПИСИ ДВЕ ЧАСТИ (T-8-S1). ОБЩАЯ (название, «кастом», размещение, активность
        // облачной модели) правится только у владельца записи — как и раньше (ТЗ гл. 6,
        // этап 42). ПЕР-СЕРВЕРНАЯ (активность ЛОКАЛЬНОЙ модели) правится на любом сервере:
        // локальная модель ставится на каждом компьютере отдельно, и выключение её на одном
        // сервере не должно гасить её на остальных
        var stored = Sql.Query(conn, tx, "SELECT * FROM ai_models WHERE id=@id", Map, ("@id", model.Id))
            .FirstOrDefault();
        var owner = stored?.ServerId;
        var canShared = _scope.CanWrite(owner);

        // признак isCustom менять можно только в debug-режиме (п. 2.9)
        if (!AppInfo.IsDebug)
        {
            model.IsCustom = stored?.IsCustom ?? model.IsCustom;
        }

        // размещение (локальная / в облаке) — производное от профайла, руками не меняется (v1.42).
        // Считает его ВЛАДЕЛЕЦ записи по СВОЕМУ профайлу: у чужого сервера профайла этой модели
        // может не быть вовсе (файлы моделей не реплицируются), и пересчёт превратил бы там
        // локальную модель в облачную — то есть сорвал бы любое сохранение (T-8-S1)
        model.IsLocal = canShared ? IsLocalProfile(ReadProfile(model)) : stored?.IsLocal ?? model.IsLocal;
        // вместе с размещением меняется и владелец записи (ТЗ гл. 6, этап 42): модель стала
        // локальной — она принадлежит этому серверу, стала облачной — общей части справочника.
        // ЧУЖУЮ запись мы себе не присваиваем: у неё правится только пер-серверная часть
        model.ServerId = canShared ? OwnerFor(model.IsLocal) : owner;
        // общая колонка активности у ЛОКАЛЬНОЙ модели больше не читается никем и остаётся
        // прежней: её значение — исходник разового переноса (MigrateActivationToServerParts)
        var sharedActive = model.IsLocal ? stored?.IsActive ?? model.IsActive : model.IsActive;
        var sharedChanged = stored is null
            || !string.Equals(stored.Name, model.Name, StringComparison.Ordinal)
            || stored.IsCustom != model.IsCustom || stored.IsLocal != model.IsLocal
            || stored.IsActive != sharedActive || stored.ServerId != model.ServerId;

        if (sharedChanged)
        {
            // общую часть правит только владелец: чужому серверу здесь понятный отказ
            _scope.EnsureCanWrite(owner, Loc.T("msg.aiModel.2"));
            EnsureUniqueName(conn, tx, model.Name, model.Id);
            Sql.Exec(conn, tx, """
                UPDATE ai_models SET name=@name, is_active=@active, is_custom=@custom, is_local=@local,
                                     server_id=@server, updated_at=@updated
                WHERE id=@id
                """,
                ("@name", model.Name), ("@active", sharedActive ? 1 : 0),
                ("@custom", model.IsCustom ? 1 : 0), ("@local", model.IsLocal ? 1 : 0),
                ("@server", model.ServerId),
                ("@updated", Sql.ToDb(now)), ("@id", model.Id));
        }
        if (model.IsLocal)
        {
            WriteLocalPart(conn, tx, model.Id, model.IsActive, now);
            if (!model.IsActive)
            {
                // модель выключена здесь — работать её агентам тут больше нечем (T-8-S1)
                DeactivateExecutorsHere(conn, tx, model, now, actorId);
            }
        }

        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ModelUpdated,
            EntityType = "ai_model",
            EntityId = model.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                model.Name,
                model.IsActive,
                server = model.IsLocal ? _scope.Code : "",
            }),
        });

        tx.Commit();
        model.UpdatedAt = now;
        return Decorate(model);
    }

    /// <summary>
    /// ВЫКЛЮЧИТЬ ИСПОЛНИТЕЛЕЙ ВЫКЛЮЧЕННОЙ ЗДЕСЬ ЛОКАЛЬНОЙ МОДЕЛИ (T-8-S1). Агент с локальной
    /// моделью физически привязан к компьютеру: модель тут выключили — работать ему нечем,
    /// и активным он остаться не может (иначе задача ушла бы исполнителю, который не стартует).
    ///
    /// Трогаются только СВОИ исполнители — привязанные к этому серверу: строку чужого сервера
    /// правит он сам, и там модель может быть включена.
    /// </summary>
    private void DeactivateExecutorsHere(SqliteConnection conn, SqliteTransaction tx,
        AiModel model, DateTime now, string? actorId)
    {
        var affected = Sql.Query(conn, tx, """
            SELECT id, nick FROM executors
            WHERE model_id=@m AND server_id=@s AND is_active=1 AND deleted_at IS NULL
            """,
            r => (Id: r.S("id"), Nick: r.S("nick")), ("@m", model.Id), ("@s", _scope.ServerId));
        foreach (var executor in affected)
        {
            Sql.Exec(conn, tx, "UPDATE executors SET is_active=0, updated_at=@now WHERE id=@id",
                ("@now", Sql.ToDb(now)), ("@id", executor.Id));
            _events.Append(conn, tx, new EventRecord
            {
                ActorId = actorId,
                EventType = EventTypes.ExecutorUpdated,
                EntityType = "executor",
                EntityId = executor.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    isActive = false,
                    reason = "model_off_here",
                    model = model.Name,
                    server = _scope.Code,
                }),
            });
        }
    }

    /// <summary>
    /// Удалить кастомную запись справочника (ТЗ v1.43, todo36_6). Удаляется **только запись**:
    /// скачанные файлы модели и установленные пакеты остаются на диске. Модель дистрибутива
    /// удалить нельзя (её вернёт сид при следующем старте) — можно только сделать неактивной.
    /// Модель, выбранную исполнителями, тоже удалять нельзя: сначала переназначьте их.
    /// </summary>
    public void Delete(string id, string? actorId)
    {
        var model = Get(id);
        if (model is null || model.DeletedAt is not null)
        {
            return;
        }
        _scope.EnsureCanWrite(model.ServerId, Loc.T("msg.aiModel.2"));
        if (!model.IsCustom)
        {
            throw new ArgumentException(
                Loc.T("msg.aiModel.3", model.Name));
        }

        using var conn = _db.Open();
        var users = Sql.Query(conn, null, """
            SELECT nick FROM executors WHERE model_id=@id AND deleted_at IS NULL ORDER BY nick
            """, r => r.S("nick"), ("@id", id));
        if (users.Count > 0)
        {
            throw new ArgumentException(
                Loc.T("msg.aiModel.4", model.Name, string.Join(", ", users)));
        }

        using var tx = conn.BeginTransaction();
        Sql.Exec(conn, tx,
            "UPDATE ai_models SET deleted_at=@deleted, updated_at=@deleted WHERE id=@id",
            ("@deleted", Sql.ToDb(DateTime.UtcNow)), ("@id", id));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ModelDeleted,
            EntityType = "ai_model",
            EntityId = id,
            PayloadJson = JsonSerializer.Serialize(new { model.DisplayId, model.Name }),
        });
        tx.Commit();
    }

    /// <summary>
    /// ВЕРНУТЬ В ОБЩУЮ ЧАСТЬ СПРАВОЧНИКА ОБЛАЧНЫЕ ЗАПИСИ, ПРИСВОЕННЫЕ РЯДОВЫМ СЕРВЕРОМ
    /// (шаг обновления билда 85, T-227).
    ///
    /// До этой версии пересчёт признака размещения зовётся на каждом старте и на реплике
    /// тоже — и рядовой сервер проставлял себя владельцем ВСЕХ облачных записей дирижёра.
    /// На дирижёре они после этого числились чужими и не правились вовсе; интерфейсом это
    /// не чинится. Здесь чинится СОСТОЯНИЕ, а не разница между версиями: у кого проставлено
    /// неверно — снимаем. Повторный запуск ничего не находит.
    ///
    /// Отличить присвоенную запись от законной можно по её номеру: запись, заведённую на
    /// рядовом сервере, он же и нумерует своим кодом — <c>M-7-S1</c>. Присвоенные приехали
    /// с дирижёра и такого суффикса не имеют, поэтому облачные записи, действительно
    /// заведённые на своём сервере, шаг не трогает.
    /// </summary>
    /// <param name="conductorServerId">Дирижёр организации: его записи законны.</param>
    /// <param name="codeOf">Код сервера в организации по его unid (S0, S1, …).</param>
    /// <returns>Сколько записей возвращено в общую часть.</returns>
    public int ReleaseStolenCloudRecords(string? conductorServerId, Func<string, string> codeOf)
    {
        using var conn = _db.Open();
        var rows = Sql.Query(conn, null, """
            SELECT id, display_id, server_id FROM ai_models
            WHERE is_local=0 AND server_id IS NOT NULL AND server_id<>''
            """,
            r => (Id: r.S("id"), DisplayId: r.S("display_id"), ServerId: r.S("server_id")));
        var freed = 0;
        foreach (var row in rows)
        {
            if (conductorServerId is { Length: > 0 } && row.ServerId == conductorServerId)
            {
                continue;   // дирижёр — законный владелец общей части
            }
            var code = codeOf(row.ServerId);
            if (code.Length > 0 && row.DisplayId.EndsWith("-" + code, StringComparison.Ordinal))
            {
                continue;   // запись заведена на этом же сервере — она его по праву
            }
            // правка обычная, в журнал изменений она попадает и уезжает партнёру:
            // чинить надо на обеих сторонах, а значения после этого совпадают
            Sql.Exec(conn, null,
                "UPDATE ai_models SET server_id=NULL, updated_at=@now WHERE id=@id",
                ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", row.Id));
            freed++;
        }
        return freed;
    }

    /// <summary>
    /// Применить решение человека по конфликту репликации записи справочника (T-227):
    /// побеждает левая (версия дирижёра) или правая сторона. Значение приходит тем же JSON,
    /// каким конфликт был записан: имя, активность, размещение, признак удаления.
    ///
    /// Проверки владения здесь НЕТ намеренно: решение принял человек, и обе стороны обязаны
    /// сойтись на одном значении. Правка обычная — она попадёт в журнал изменений и уедет
    /// к партнёру ближайшим сеансом.
    /// </summary>
    public void ApplyConflictResolution(string id, string valueJson, string? actorId)
    {
        string name;
        bool isActive;
        bool isLocal;
        bool deleted;
        try
        {
            using var doc = JsonDocument.Parse(valueJson);
            var root = doc.RootElement;
            name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString()!
                : "";
            isActive = root.TryGetProperty("isActive", out var a) && a.ValueKind == JsonValueKind.True;
            isLocal = root.TryGetProperty("isLocal", out var l) && l.ValueKind == JsonValueKind.True;
            deleted = root.TryGetProperty("deleted", out var d) && d.ValueKind == JsonValueKind.True;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException(Loc.T("msg.aiModel.10"), ex);
        }
        if (name.Length == 0)
        {
            throw new ArgumentException(Loc.T("msg.aiModel.10"));
        }
        var now = DateTime.UtcNow;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        var existed = Sql.Scalar<long>(conn, tx, "SELECT COUNT(*) FROM ai_models WHERE id=@id",
            ("@id", id)) > 0;
        if (!existed)
        {
            return;
        }
        Sql.Exec(conn, tx, """
            UPDATE ai_models SET name=@name, is_active=@active, is_local=@local,
                                 deleted_at=@deleted, updated_at=@updated
            WHERE id=@id
            """,
            ("@name", name), ("@active", isActive ? 1 : 0), ("@local", isLocal ? 1 : 0),
            ("@deleted", deleted ? Sql.ToDb(now) : null),
            ("@updated", Sql.ToDb(now)), ("@id", id));
        _events.Append(conn, tx, new EventRecord
        {
            ActorId = actorId,
            EventType = EventTypes.ModelUpdated,
            EntityType = "ai_model",
            EntityId = id,
            PayloadJson = JsonSerializer.Serialize(new { name, isActive, conflict = true }),
        });
        tx.Commit();
    }

    /// <summary>
    /// Стартовое наполнение справочника из дистрибутива (первый запуск) и обновление
    /// seed-файлов при апгрейде: профайлы/декларации НЕкастомных моделей перезаписываются,
    /// если их "_seed" в файле старше SeedFileVersion (todo16). Кастомные модели не трогаются.
    /// </summary>
    /// <summary>
    /// Время правки записи, заведённой СИДОМ (T-227). Оно нарочно не «сейчас»: запись
    /// дистрибутива появляется на каждой установке в свой момент, а правкой человека не
    /// является. Партнёр сравнивает время правки с прошлым удачным сеансом, и «свежая»
    /// отметка выдавала бы только что засеянную запись за местную правку — реплика объявляла
    /// бы конфликт на каждой записи, которой никто не касался.
    /// </summary>
    public static readonly DateTime SeedStamp =
        new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// ПОГАШЕННЫЕ записи дистрибутива (T-216-S0): модель, которую рынок обогнал, помечается
    /// НЕактивной, но НЕ удаляется — на неё ссылаются исполнители, прошлые задания и отчёты.
    ///
    /// Правило узла шаблона «Актуализация моделей»: гасим, если производитель объявил конец
    /// поддержки (или убрал идентификатор из своего API) ЛИБО у модели есть три более старших
    /// версии того же семейства. Считаются версии семейства, а не записи справочника: mini и
    /// pro, t2v и i2v, кванты — это варианты ОДНОЙ версии.
    ///
    /// <list type="bullet">
    /// <item><c>…014</c> Gemini-3-Ultra — после версии 3 у Google вышли 3.1, 3.5, 3.6, 3.7 и
    /// 3.8; идентификатора <c>gemini-3-ultra</c> в публичном каталоге моделей уже нет;</item>
    /// <item><c>…015</c> Gemini-3.1-Pro — после 3.1 вышли 3.5, 3.6, 3.7 и 3.8; в каталоге
    /// остались только <c>gemini-3.1-pro-preview</c>, обычного идентификатора нет;</item>
    /// <item><c>…048</c> Wan-2.2-T2V-A14B и <c>…049</c> Wan-2.2-I2V-A14B (T-347-S0,
    /// 23.09.2026) — после 2.2 у Alibaba вышли Wan 2.6, Wan 2.7 и Wan 3.0 (плюс вариант
    /// 3.0 Prime, он у нас заведён записью …090), то есть порог «три более старших версии
    /// того же семейства» набран с запасом. Записи остаются в справочнике и включаются
    /// галочкой обратно: веса по манифесту по-прежнему качаются, и у кого Wan 2.2 уже
    /// установлен, тот ничего не теряет.</item>
    /// </list>
    ///
    /// Список действует в двух местах: на ЧИСТОЙ установке — при вставке записи (сид), на уже
    /// работающей — разовым шагом обновления (<c>Upgrade.Steps</c>, билды 132 и 143), который зовёт
    /// <see cref="RetireOutdatedSeedModels"/>. Включить погашенную запись обратно человек
    /// по-прежнему может галочкой «активна»: шаг разовый и второй раз не сработает.
    /// </summary>
    public static readonly IReadOnlySet<string> RetiredSeedModels = new HashSet<string>
    {
        "6f1a45e0-0d31-4c65-9a01-000000000014",
        "6f1a45e0-0d31-4c65-9a01-000000000015",
        "6f1a45e0-0d31-4c65-9a01-000000000048",
        "6f1a45e0-0d31-4c65-9a01-000000000049",
    };

    /// <summary>
    /// Погасить записи из <see cref="RetiredSeedModels"/> у УЖЕ заведённых установок.
    /// Возвращает число погашенных; идемпотентен — второй запуск ничего не делает.
    /// Кастомные записи не трогаются, удаления нет.
    /// </summary>
    public int RetireOutdatedSeedModels()
    {
        var done = 0;
        using var conn = _db.Open();
        using var tx = conn.BeginTransaction();
        foreach (var id in RetiredSeedModels)
        {
            var changed = Sql.Exec(conn, tx, """
                UPDATE ai_models SET is_active=0, updated_at=@now
                WHERE id=@id AND is_custom=0 AND is_active<>0 AND deleted_at IS NULL
                """,
                ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", id));
            // У ЛОКАЛЬНОЙ записи общая колонка активности не читается вовсе (T-8-S1):
            // включена она или нет, решает строка ЭТОГО сервера в ai_model_servers. Без
            // этой правки гашение локальной записи (в 1.143 — две записи Wan 2.2) не гасило
            // бы ничего: в справочнике галочка снята, а исполнитель продолжал бы работать
            changed += Sql.Exec(conn, tx, """
                UPDATE ai_model_servers SET is_active=0, updated_at=@now
                WHERE model_id=@id AND server_id=@s AND is_active<>0 AND deleted_at IS NULL
                """,
                ("@now", Sql.ToDb(DateTime.UtcNow)), ("@id", id), ("@s", _scope.ServerId));
            if (changed > 0)
            {
                done += changed;
                _events.Append(conn, tx, new EventRecord
                {
                    EventType = EventTypes.ModelUpdated,
                    EntityType = "ai_model",
                    EntityId = id,
                    PayloadJson = JsonSerializer.Serialize(new { isActive = false, retired = true }),
                });
            }
        }
        tx.Commit();
        return done;
    }

    public void Seed()
    {
        // ЗАПИСИ ДИСТРИБУТИВА НЕ РЕПЛИЦИРУЮТСЯ (T-227): идентификаторы у них фиксированные,
        // и точно такие же строки заводит у себя каждая установка — включая реплику, где сид
        // справочников с СЛУЧАЙНЫМИ идентификаторами по-прежнему пропускается. Гонять их
        // журналом изменений незачем, а вредно: местный порядковый номер записи и время
        // создания уезжали бы к партнёру и объявлялись бы там правкой
        SeedRecords();
        // размещение (локальная / в облаке) проставляется всем записям справочника (v1.42),
        // а вместе с ним — пер-серверная активность локальных моделей (T-8-S1). Зовётся уже
        // ПОСЛЕ пометки «молчания»: сами флаги внутри молчат по-прежнему, а вот строка
        // пер-серверной активности партнёру нужна — из таких строк и складывается список
        // «на каких серверах модель включена»
        SyncLocalFlags();
    }

    /// <summary>Собственно записи и файлы дистрибутива — под пометкой «не писать в журнал».</summary>
    private void SeedRecords()
    {
        using var mute = ChangeLog.Mute(_db);
        foreach (var (id, name, profileJson, scopeJson) in SeedModels)
        {
            // модель дистрибутива могла раньше ставиться инсталлятором как кастомная
            // (Qwen3.6-35B-A3B-Local до v1.42) — имя занято, переименовываем старую запись:
            // ссылки исполнителей на неё сохраняются, пользователь сам решает, что оставить
            RenameConflictingCustom(id, name);
            AiModel model;
            using (var conn = _db.Open())
            {
                var exists = Sql.Scalar<long>(conn, null,
                    "SELECT COUNT(*) FROM ai_models WHERE id=@id", ("@id", id)) > 0;
                var now = SeedStamp;
                model = new AiModel
                {
                    Id = id,
                    Name = name,
                    IsCustom = false,
                    ProfilePath = ProfilePathOf(id),
                    CapabilitiesPath = ScopePathOf(id),
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                if (!exists)
                {
                    using var tx = conn.BeginTransaction();
                    model.DisplayId = Database.NextDisplayId(conn, tx, "M");
                    Sql.Exec(conn, tx, """
                        INSERT INTO ai_models (id, display_id, name, is_active, is_custom,
                                               profile_path, capabilities_path, created_at, updated_at)
                        VALUES (@id, @did, @name, @active, 0, @profile, @caps, @created, @updated)
                        """,
                        ("@id", model.Id), ("@did", model.DisplayId), ("@name", model.Name),
                        ("@active", RetiredSeedModels.Contains(id) ? 0 : 1),
                        ("@profile", model.ProfilePath), ("@caps", model.CapabilitiesPath),
                        ("@created", Sql.ToDb(now)), ("@updated", Sql.ToDb(now)));
                    tx.Commit();
                }
                else
                {
                    // имя модели дистрибутива переименовали в новой версии (ТЗ v1.43, todo36_6:
                    // Claude-Opus-4.8 → Claude-Opus-5.0) — запись существует, имя обновляем;
                    // ссылки исполнителей не рвутся, id остаётся прежним
                    using var tx = conn.BeginTransaction();
                    var updated = Sql.Exec(conn, tx, """
                        UPDATE ai_models SET name=@name, updated_at=@updated
                        WHERE id=@id AND name<>@name
                        """,
                        ("@name", model.Name), ("@updated", Sql.ToDb(now)), ("@id", model.Id));
                    if (updated > 0)
                    {
                        _events.Append(conn, tx, new EventRecord
                        {
                            EventType = EventTypes.ModelUpdated,
                            EntityType = "ai_model",
                            EntityId = model.Id,
                            PayloadJson = JsonSerializer.Serialize(new { model.Name, seed = SeedFileVersion }),
                        });
                    }
                    tx.Commit();
                }
            }
            WriteSeedFile(model.ProfilePath, profileJson);
            WriteSeedFile(model.CapabilitiesPath, scopeJson);
        }
        // workflow-шаблон ComfyUI модели Kandinsky (ТЗ v1.41, todo36_4): тот же механизм
        // _seed-версий — пользовательские правки текущей версии сохраняются
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000007.json", KandinskyWorkflowJson);
        // тот же шаблон для варианта «изображение → видео» (T-258): у него свой файл весов
        // и свои узлы стартового кадра, поэтому шаблон отдельный
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000032.json", KandinskyI2vWorkflowJson);
        // локальные модели изображений (T-241): у каждой свой шаблон — разные загрузчики
        // весов, разный сэмплер и, у правки картинки, узлы исходного изображения
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000033.json", ZImageWorkflowJson);
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000034.json", QwenImageWorkflowJson);
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000035.json", QwenImageEditWorkflowJson);
        // локальные видео-модели (T-15-S0): Wan 2.2 (текст → ролик и картинка → ролик),
        // HunyuanVideo 1.5 и LTX-2.5 — у каждой свои загрузчики весов и своя схема сэмплинга
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000048.json", WanT2vWorkflowJson);
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000049.json", WanI2vWorkflowJson);
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000050.json", HunyuanVideo15WorkflowJson);
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000051.json", Ltx25WorkflowJson);
        // вторая очередь локальных моделей изображений (T-19-S0): FLUX.2 (klein 4B в двух
        // режимах и dev), Kandinsky 5.0 Image Lite и нижняя ступень SD 3.5 / SDXL —
        // у каждой свой загрузчик весов и своя схема сэмплинга, поэтому шаблоны отдельные
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000070.json", Flux2Klein4bWorkflowJson);
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000071.json", Flux2Klein4bEditWorkflowJson);
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000072.json", Kandinsky5ImageWorkflowJson);
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000073.json", Sd35LargeWorkflowJson);
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000074.json", SdxlWorkflowJson);
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000075.json", Flux2DevWorkflowJson);
        // локальные 3D-модели (T-20-S0): картинка → сетка либо гауссовы сплаты
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000080.json", Hunyuan3D21WorkflowJson);
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000081.json", Trellis2WorkflowJson);
        WriteSeedFile("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000082.json", TripoSplatWorkflowJson);
        // варианты Kandinsky Lite (T-15-S0): граф общий, у каждого варианта подставляются
        // свой файл весов и своя сила следования тексту
        foreach (var v in KandinskyLite)
        {
            WriteSeedFile("models/workflow_" + v.Id + ".json", KandinskyLiteWorkflow(v.Unet, v.Cfg));
        }
        // локальные модели звука (T-18-S0): у трёх вариантов ACE-Step 1.5 XL граф общий,
        // подставляются свой файл весов, сила следования тексту и top_p планировщика
        foreach (var v in AceStep15)
        {
            WriteSeedFile("models/workflow_" + v.Id + ".json", AceStep15Workflow(v));
            // правила составления управляющего json для модели-суфлёра (T-289-S0): файл
            // ставится тем же путём, что профайл и workflow, — программой, а не скриптами
            // выкладки (наука T-270-S0), и по той же причине не реплицируется
            WriteSeedText(PrompterRulesPathOf(v.Id), AceStep15Rules(v));
        }
        // справочник пакетов локальных моделей (ТЗ v1.42, todo36_5)
        WriteSeedFile(PackagesPath, PackagesSeedJson);
    }

    /// <summary>
    /// Переименовать кастомную запись, занявшую имя модели дистрибутива (ТЗ v1.42): та же
    /// модель раньше добавлялась инсталлятором (import_*.json). Ссылки исполнителей на неё
    /// не рвутся — старая запись остаётся с суффиксом «(инсталлятор)».
    /// </summary>
    private void RenameConflictingCustom(string seedId, string seedName)
    {
        // переименование делает у себя каждая установка сама и одинаково — в журнал оно
        // не пишется (T-227); вызывается метод из-под пометки Seed(), эта — вторая, на случай
        // вызова со стороны (пометки вложенные)
        using var mute = ChangeLog.Mute(_db);
        using var conn = _db.Open();
        var conflictId = Sql.Scalar<string>(conn, null,
            "SELECT id FROM ai_models WHERE name=@name AND id<>@id AND deleted_at IS NULL",
            ("@name", seedName), ("@id", seedId));
        if (conflictId is null)
        {
            return;
        }
        var renamed = Loc.T("msg.aiModel.5", seedName);
        for (var n = 2; Sql.Scalar<long>(conn, null,
                 "SELECT COUNT(*) FROM ai_models WHERE name=@name AND deleted_at IS NULL",
                 ("@name", renamed)) > 0; n++)
        {
            renamed = Loc.T("msg.aiModel.6", seedName, n);
        }
        Sql.Exec(conn, null, "UPDATE ai_models SET name=@name, updated_at=@updated WHERE id=@id",
            ("@name", renamed), ("@updated", Sql.ToDb(DateTime.UtcNow)), ("@id", conflictId));
    }

    /// <summary>
    /// Проставить всем моделям признак размещения по их профайлу (ТЗ v1.42, todo36_5):
    /// вызывается при заполнении справочника и при старте приложения — флаг всегда
    /// соответствует профайлу, руками его не меняют.
    ///
    /// ЭТО ПЕРЕСЧЁТ ПРОИЗВОДНОГО ЗНАЧЕНИЯ, и владельца записи он не трогает (T-227): считает
    /// его каждая установка сама по своему же профайлу, поэтому и в журнал изменений результат
    /// не пишется — пересчёт идёт под пометкой «молчания» (<see cref="ChangeLog.Mute"/>).
    /// </summary>
    public void SyncLocalFlags()
    {
        using (ChangeLog.Mute(_db))
        {
            foreach (var model in List(includeDeleted: true))
            {
                SyncLocalFlag(model);
            }
        }
        // модель, СТАВШАЯ здесь локальной, получает свою пер-серверную строку активности
        // (T-8-S1) — и уже БЕЗ пометки «молчания»: строка принадлежит этому серверу, и
        // партнёру она нужна, иначе он не узнает, где модель включена
        MigrateActivationToServerParts();
    }

    /// <summary>
    /// Пересчитать признак размещения одной модели; в БД пишется только при изменении.
    /// </summary>
    /// <param name="assignOwner">Проставить заодно и владельца записи (ТЗ гл. 6, этап 42):
    /// локальная модель принадлежит ЭТОМУ серверу, облачная — общей части справочника.
    /// Разрешено ТОЛЬКО там, где запись заводят здесь (создание, импорт инсталлятора):
    /// массовый пересчёт при старте владельца не меняет — см. <see cref="OwnerFor"/>.</param>
    private void SyncLocalFlag(AiModel model, bool assignOwner = false)
    {
        var profile = ReadProfile(model);
        // о ЧУЖОЙ записи без профайла судить не по чему: файл ещё не приехал репликацией,
        // и «облачная» здесь было бы не пересчётом, а догадкой
        if (profile.Length == 0 && !_scope.CanWrite(model.ServerId))
        {
            return;
        }
        var isLocal = IsLocalProfile(profile);
        var owner = model.ServerId;
        if (assignOwner)
        {
            owner = OwnerFor(isLocal);
        }
        else if (isLocal && model.ServerId is not { Length: > 0 } && _scope.CanWrite(model.ServerId))
        {
            // модель СТАЛА локальной, а хозяина у записи нет: она работает на этом компьютере,
            // значит принадлежит ему (ТЗ гл. 6) — от этого зависит и привязка исполнителя.
            // Присвоение возможно только там, где строку и так вправе писать, поэтому забрать
            // чужую (в том числе облачную запись дирижёра) этой веткой нельзя
            owner = _scope.ServerId;
        }
        if (isLocal == model.IsLocal && owner == model.ServerId)
        {
            return;
        }
        model.IsLocal = isLocal;
        model.ServerId = owner;
        using var conn = _db.Open();
        Sql.Exec(conn, null, "UPDATE ai_models SET is_local=@local, server_id=@server WHERE id=@id",
            ("@local", isLocal ? 1 : 0), ("@server", owner), ("@id", model.Id));
    }

    /// <summary>Текст профайла модели; файла нет — пустая строка.</summary>
    private string ReadProfile(AiModel model)
    {
        var abs = model.ProfilePath.Length > 0 ? _files.Abs(model.ProfilePath) : "";
        return abs.Length > 0 && File.Exists(abs) ? File.ReadAllText(abs) : "";
    }

    /// <summary>
    /// Модель работает на этом компьютере (ТЗ v1.42): провайдер comfyui, либо есть манифест
    /// установки или команда запуска локального сервера, либо baseUrl смотрит на localhost.
    /// Всё остальное — облако.
    /// </summary>
    public static bool IsLocalProfile(string profileJson)
    {
        if (profileJson.Trim().Length == 0)
        {
            return false;
        }
        try
        {
            using var doc = JsonDocument.Parse(profileJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
            if (Text(root, "provider").Equals("comfyui", StringComparison.OrdinalIgnoreCase) ||
                Text(root, "launchCommand").Length > 0 ||
                root.TryGetProperty("install", out var install) && install.ValueKind == JsonValueKind.Object)
            {
                return true;
            }
            var baseUrl = Text(root, "baseUrl");
            return baseUrl.Length > 0 && Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) &&
                   uri.Host is "localhost" or "127.0.0.1" or "0.0.0.0" or "::1" or "[::1]";
        }
        catch (JsonException)
        {
            return false; // повреждённый профайл — считаем облачной, install-логика его тоже игнорирует
        }

        static string Text(JsonElement obj, string name) =>
            obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()!.Trim()
                : "";
    }

    /// <summary>
    /// Импорт моделей, подготовленных инсталляторами (например install_local_model):
    /// файлы models/import_*.json формата {"name": "...", "profile": {...}, "scope": {...}}.
    /// Обрабатываются при старте приложения: создаётся кастомная запись справочника,
    /// профайл и декларация пишутся из файла импорта, файл переименовывается в *.done
    /// (модель с таким именем уже есть — импорт пропускается, файл тоже помечается done).
    /// Повреждённый файл импорта — понятная ошибка на старте (как у сидов).
    ///
    /// <paramref name="sharedDir"/> — ОБЩИЙ каталог импорта уровня сервера
    /// (<c>data/models/</c>, куда пишут инсталляторы): его файлы видны всем организациям,
    /// поэтому в *.done они НЕ переименовываются — повторный импорт отсекается проверкой
    /// «модель с таким именем уже есть» (ТЗ v1.47, todo40).
    /// </summary>
    public void ImportPending(string? sharedDir = null)
    {
        ImportFrom(_files.Abs("models"), markDone: true);
        if (sharedDir is { Length: > 0 })
        {
            ImportFrom(sharedDir, markDone: false);
        }
        // импортированная модель оказывается локальной уже ПОСЛЕ записи профайла, поэтому
        // её пер-серверная строка заводится здесь (T-8-S1) — и без пометки «молчания»:
        // она принадлежит этому серверу и партнёру нужна
        MigrateActivationToServerParts();
    }

    private void ImportFrom(string modelsDir, bool markDone)
    {
        if (!Directory.Exists(modelsDir))
        {
            return;
        }
        // модель, подготовленную инсталлятором, каждая установка заводит себе сама из своего
        // же файла import_*.json — в журнал изменений это не пишется (T-227)
        using var mute = ChangeLog.Mute(_db);
        foreach (var path in Directory.GetFiles(modelsDir, "import_*.json").OrderBy(p => p))
        {
            string name;
            string profileJson;
            string scopeJson;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString()!.Trim()
                    : "";
                profileJson = root.TryGetProperty("profile", out var p) ? p.GetRawText() : "{}";
                scopeJson = root.TryGetProperty("scope", out var s) ? s.GetRawText() : "";
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    Loc.T("msg.aiModel.7", path), ex);
            }
            if (name.Length == 0)
            {
                throw new InvalidOperationException(Loc.T("msg.aiModel.8", path));
            }

            var exists = List().Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                var model = Create(new AiModel { Name = name, IsCustom = true }, actorId: null);
                _files.WriteText(model.ProfilePath, profileJson);
                if (scopeJson.Length > 0)
                {
                    _files.WriteText(model.CapabilitiesPath, scopeJson);
                }
                // размещение — по только что записанному профайлу (v1.42); владелец
                // проставляется законно: запись заводят ЗДЕСЬ, своим инсталлятором (T-227)
                SyncLocalFlag(model, assignOwner: true);
            }
            if (markDone)
            {
                File.Move(path, path + ".done", overwrite: true);
            }
        }
    }

    /// <summary>
    /// Записать seed-файл дистрибутивной модели: отсутствует — создать; существует —
    /// перезаписать только если его "_seed" старше текущей версии дистрибутива.
    /// </summary>
    private void WriteSeedFile(string relativePath, string seedJson)
    {
        var abs = _files.Abs(relativePath);
        if (File.Exists(abs) && ReadSeedVersion(abs) >= SeedFileVersion)
        {
            return;
        }
        _files.WriteText(relativePath, seedJson);
    }

    /// <summary>
    /// То же для ТЕКСТОВОГО seed-файла (T-289-S0, правила для модели-суфлёра): версия
    /// написана отметкой <c>&lt;!-- _seed: N --&gt;</c> в первых строках — json-поля в markdown
    /// не бывает, а <see cref="ReadSeedVersion"/> на не-json честно отвечает нулём, то есть
    /// файл переписывался бы при КАЖДОМ старте и правки человека пропадали бы молча.
    /// </summary>
    private void WriteSeedText(string relativePath, string text)
    {
        var abs = _files.Abs(relativePath);
        if (File.Exists(abs) && ReadSeedMark(abs) >= SeedFileVersion)
        {
            return;
        }
        _files.WriteText(relativePath, text);
    }

    /// <summary>Версия из отметки <c>&lt;!-- _seed: N --&gt;</c>; нет отметки или файл нечитаем — 0.</summary>
    private static int ReadSeedMark(string absPath)
    {
        try
        {
            foreach (var line in File.ReadLines(absPath).Take(5))
            {
                var m = SeedMark.Match(line);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var version))
                {
                    return version;
                }
            }
            return 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    /// <summary>Отметка версии текстового seed-файла.</summary>
    private static readonly System.Text.RegularExpressions.Regex SeedMark =
        new(@"<!--\s*_seed:\s*(\d+)\s*-->", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Версия "_seed" из файла; 0 — поле отсутствует или файл нечитаем.</summary>
    private static int ReadSeedVersion(string absPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(absPath));
            return doc.RootElement.TryGetProperty("_seed", out var v) && v.TryGetInt32(out var version)
                ? version
                : 0;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return 0;
        }
    }

    /// <summary>Создать файлы профайла и декларации, если их ещё нет (существующие не трогаются).</summary>
    private void EnsureFiles(AiModel model, string profileJson)
    {
        if (!File.Exists(_files.Abs(model.ProfilePath)))
        {
            _files.WriteText(model.ProfilePath, profileJson);
        }
        if (!File.Exists(_files.Abs(model.CapabilitiesPath)))
        {
            _files.WriteText(model.CapabilitiesPath, DefaultScopeJson(model.Name));
        }
    }

    /// <summary>Неустановленную модель активировать нельзя (ТЗ v1.41, todo36_4).</summary>
    private void EnsureActivationAllowed(AiModel model)
    {
        if (model.IsActive && ActivationGuard?.Invoke(model) is { } error)
        {
            throw new ArgumentException(error);
        }
    }

    private static void EnsureUniqueName(SqliteConnection conn, SqliteTransaction tx, string name, string selfId)
    {
        var taken = Sql.Scalar<long>(conn, tx,
            "SELECT COUNT(*) FROM ai_models WHERE name=@name AND id<>@id AND deleted_at IS NULL",
            ("@name", name), ("@id", selfId)) > 0;
        if (taken)
        {
            throw new ArgumentException(Loc.T("msg.aiModel.9", name));
        }
    }

    /// <summary>Пер-серверная строка записи справочника (T-8-S1).</summary>
    private static AiModelServer MapPart(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        ModelId = r.S("model_id"),
        ServerId = r.S("server_id"),
        IsActive = r.B("is_active"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };

    private static AiModel Map(SqliteDataReader r) => new()
    {
        Id = r.S("id"),
        DisplayId = r.S("display_id"),
        Name = r.S("name"),
        IsActive = r.B("is_active"),
        IsCustom = r.B("is_custom"),
        IsLocal = r.B("is_local"),
        ServerId = r.Has("server_id") ? r.SN("server_id") : null,
        ProfilePath = r.S("profile_path"),
        CapabilitiesPath = r.S("capabilities_path"),
        CreatedAt = r.Dt("created_at"),
        UpdatedAt = r.Dt("updated_at"),
        DeletedAt = r.DtN("deleted_at"),
    };
}

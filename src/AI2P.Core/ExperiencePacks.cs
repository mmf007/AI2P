using System.Text;
using System.Text.Json;

namespace AI2P.Core;

/// <summary>
/// НАБОР ОПЫТА — «СТИЛЬ РАБОТЫ» (T-270-S0, ветка T-318 «Проект опыта»). Файл дистрибутива
/// <c>packs/&lt;код&gt;/pack.json</c> в каталоге данных — рядом с <c>plugins/</c> и
/// <c>models/</c> и ровно по их образцу (<see cref="PluginManifest"/>).
///
/// <para>ЗАЧЕМ. Стиль работы (как ревьюим, как пишем отчёты, как ведём медиа-продакшн) — это
/// ПАЧКА записей опыта, которую хочется ставить и снимать ЦЕЛИКОМ и возить между установками.
/// Половина механизма была написана для плагинов (<c>PluginSetupService.Initialize</c>:
/// записи манифеста ложатся в общий опыт с пометкой владельца <c>plugin:&lt;код&gt;</c> и
/// снимаются по ней же); здесь тот же механизм вынут из плагинов и отдан человеку отдельно.
/// Обмена опытом между установками у нас до этого не было вовсе.</para>
///
/// <para>ТРИ ПРАВИЛА, КОТОРЫЕ ЗАДАЮТ ФОРМАТ (все выучены на этом проекте):</para>
/// <list type="number">
/// <item>ИДЕНТИФИКАТОРЫ ЗАПИСЕЙ И УЗЛОВ ШАБЛОНА — ФИКСИРОВАННЫЕ, как у поставляемых общих
/// правил (<c>ExperienceService.GeneralSeed</c>) и записей справочника моделей (T-227):
/// набор ставит КАЖДЫЙ сервер сам, а таблица <c>experience</c> реплицируется — со случайными
/// идентификаторами в организации копился бы второй комплект, по одному на сервер;</item>
/// <item>ТЕКСТЫ ЛОКАЛИЗОВАНЫ (<see cref="LocalizedText"/>, словарь языков — как в манифесте
/// плагина): поставляемые наборы пишутся на всех пяти языках, а выгруженный человеком
/// получает язык своего сервера;</item>
/// <item>ВЛАДЕЛЕЦ — ТЭГ <c>pack:&lt;код&gt;</c> (<see cref="PackCodes"/>): колонки под него нет,
/// а <c>created_by</c> ссылается на исполнителей внешним ключом и строкой его не занять.
/// Отбору опыта такой тэг не мешает — <c>ExperienceService.TagsMatch</c> служебные пометки
/// владельца пропускает мимо отбора.</item>
/// </list>
///
/// <para>ОБЛАСТЬ УСТАНОВКИ В ФАЙЛЕ НЕ ХРАНИТСЯ НИКОГДА: общий опыт / конкретный проект /
/// узел шаблона — это выбор человека в момент установки, а не свойство набора. Один и тот же
/// «Строгое код-ревью» одному нужен на всю организацию, другому — в одном проекте.</para>
///
/// <para>СОСТАВ:</para>
/// <code>
/// {
///   "code": "style.strict-review",
///   "version": 1,
///   "name": { "ru": "Строгое код-ревью", "en": "Strict code review" },
///   "description": { "ru": "…", "en": "…" },
///   "doc": { "ru": "packs/style.strict-review.md" },
///   "records": [
///     { "id": "b1e5…0001", "skill": "code-review", "tags": ["ревью"],
///       "alwaysLoad": false, "text": { "ru": "…", "en": "…" } }
///   ],
///   "templates": [
///     { "id": "b1e5…1001", "parent": null, "title": { "ru": "…" },
///       "description": { "ru": "…" }, "acceptance": { "ru": "…" }, "skills": ["analyze-plan"] }
///   ]
/// }
/// </code>
/// </summary>
public sealed class ExperiencePack
{
    /// <summary>Имя файла набора внутри его каталога.</summary>
    public const string FileName = "pack.json";

    /// <summary>Каталог наборов в каталоге данных (относительный путь).</summary>
    public const string Dir = "packs";

    /// <summary>Код набора: <c>style.strict-review</c>. Он же имя каталога и то, из чего
    /// складывается пометка владельца записей (<see cref="PackCodes.ExperienceOwner"/>).</summary>
    public string Code { get; set; } = "";

    /// <summary>Версия САМОГО файла набора: по ней сид решает, класть ли поверх новый.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Название набора по языкам.</summary>
    public LocalizedText Name { get; set; } = new();

    /// <summary>Одна фраза о том, что это за стиль работы.</summary>
    public LocalizedText Description { get; set; } = new();

    /// <summary>Записи опыта набора.</summary>
    public List<ExperiencePackRecord> Records { get; set; } = [];

    /// <summary>
    /// УЗЛЫ ШАБЛОНА ЗАДАЧ, которые набор приносит с собой. Блок нужен подзадаче ветки
    /// «Опыт: действия агента для анализа и шаблон „Анализ опыта“» (T-271-S0): шаблон
    /// поставляется ВМЕСТЕ со стилями — прямое требование заказчика. Разбор и формат
    /// здесь, ЗАВЕДЕНИЕ узлов при установке — за той подзадачей.
    /// </summary>
    public List<ExperiencePackTemplate> Templates { get; set; } = [];

    /// <summary>Документ набора по языкам: страница поставляемой документации
    /// (<c>packs/&lt;код&gt;.md</c> относительно <c>doc/&lt;язык&gt;/</c>), как у плагинов.</summary>
    public Dictionary<string, string> Doc { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Набор годен: есть код и хотя бы одна запись либо узел шаблона. Пустой набор
    /// ставить не во что — это дефект файла, а не «набор без содержимого».</summary>
    public bool IsValid => Code.Length > 0 && (Records.Count > 0 || Templates.Count > 0);

    /// <summary>Путь файла относительно каталога данных: <c>packs/&lt;код&gt;/pack.json</c>.</summary>
    public static string PathOf(string code) => $"{Dir}/{code}/{FileName}";

    /// <summary>Страница документа набора в поставляемой документации (как у плагинов).</summary>
    public static string DocPageOf(string code) => $"{Dir}/{code}.md";

    /// <summary>Разобрать набор из текста; null — это не JSON-объект либо набор негоден.</summary>
    public static ExperiencePack? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            var pack = new ExperiencePack
            {
                Code = JsonRead.Str(root, "code"),
                Version = Math.Max(1, JsonRead.Int(root, "version", 1)),
                Name = LocalizedText.From(root, "name"),
                Description = LocalizedText.From(root, "description"),
            };
            pack.Records = Each(root, "records", ExperiencePackRecord.From);
            pack.Templates = Each(root, "templates", ExperiencePackTemplate.From);
            if (root.TryGetProperty("doc", out var docs) && docs.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in docs.EnumerateObject())
                {
                    if (p.Value.ValueKind == JsonValueKind.String &&
                        p.Value.GetString() is { Length: > 0 } path)
                    {
                        pack.Doc[p.Name] = path.Trim();
                    }
                }
            }
            return pack.IsValid ? pack : null;
        }
    }

    /// <summary>Прочитать набор с диска: <c>&lt;каталог данных&gt;/packs/&lt;код&gt;/pack.json</c>.</summary>
    public static ExperiencePack? Read(string dataDir, string code)
    {
        if (string.IsNullOrWhiteSpace(dataDir) || string.IsNullOrWhiteSpace(code))
        {
            return null;
        }
        var file = Path.Combine(dataDir, Dir, code, FileName);
        try
        {
            return File.Exists(file) ? Parse(File.ReadAllText(file)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Все наборы каталога данных, по одному на подкаталог <c>packs/*</c>. Негодные
    /// пропускаются молча: один сломанный файл не должен прятать остальные наборы. Порядок —
    /// по коду, чтобы список был одинаков на всех серверах.</summary>
    public static List<ExperiencePack> ReadAll(string dataDir)
    {
        var root = string.IsNullOrWhiteSpace(dataDir) ? "" : Path.Combine(dataDir, Dir);
        if (root.Length == 0 || !Directory.Exists(root))
        {
            return [];
        }
        var found = new List<ExperiencePack>();
        foreach (var dir in Directory.GetDirectories(root))
        {
            // код ВНУТРИ файла главнее имени каталога: по нему живёт пометка владельца
            // записей, а каталог человек мог переименовать
            var pack = Read(dataDir, Path.GetFileName(dir));
            if (pack is not null)
            {
                found.Add(pack);
            }
        }
        return [.. found.OrderBy(p => p.Code, StringComparer.Ordinal)];
    }

    /// <summary>
    /// ЗАПИСАТЬ НАБОР ФАЙЛОМ (выгрузка, п. 3 задания). Пишется своими руками, а не
    /// сериализатором: у <see cref="LocalizedText"/> нет формы, годной для
    /// <c>JsonSerializer</c>, а файл читают и правят руками — отступы и порядок полей должны
    /// быть теми же, что у поставляемых наборов.
    /// </summary>
    public string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"code\": ").Append(Str(Code)).Append(",\n");
        sb.Append("  \"version\": ").Append(Version).Append(",\n");
        sb.Append("  \"name\": ").Append(Loc(Name)).Append(",\n");
        sb.Append("  \"description\": ").Append(Loc(Description)).Append(",\n");
        if (Doc.Count > 0)
        {
            sb.Append("  \"doc\": {")
              .Append(string.Join(", ", Doc.OrderBy(p => p.Key, StringComparer.Ordinal)
                  .Select(p => Str(p.Key) + ": " + Str(p.Value))))
              .Append("},\n");
        }
        sb.Append("  \"records\": [\n");
        for (var i = 0; i < Records.Count; i++)
        {
            var r = Records[i];
            sb.Append("    { \"id\": ").Append(Str(r.Id))
              .Append(", \"skill\": ").Append(Str(r.Skill))
              .Append(", \"tags\": [")
              .Append(string.Join(", ", r.Tags.Select(Str))).Append(']')
              .Append(", \"alwaysLoad\": ").Append(r.AlwaysLoad ? "true" : "false")
              .Append(",\n      \"text\": ").Append(Loc(r.Text)).Append(" }")
              .Append(i + 1 < Records.Count ? ",\n" : "\n");
        }
        sb.Append("  ],\n");
        sb.Append("  \"templates\": [\n");
        for (var i = 0; i < Templates.Count; i++)
        {
            var t = Templates[i];
            sb.Append("    { \"id\": ").Append(Str(t.Id))
              .Append(", \"parent\": ").Append(t.Parent.Length > 0 ? Str(t.Parent) : "null")
              .Append(", \"skills\": [")
              .Append(string.Join(", ", t.Skills.Select(Str))).Append(']')
              .Append(",\n      \"title\": ").Append(Loc(t.Title))
              .Append(",\n      \"description\": ").Append(Loc(t.Description))
              .Append(",\n      \"acceptance\": ").Append(Loc(t.Acceptance)).Append(" }")
              .Append(i + 1 < Templates.Count ? ",\n" : "\n");
        }
        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    private static string Str(string value) => JsonSerializer.Serialize(value);

    private static string Loc(LocalizedText text) =>
        "{" + string.Join(", ", text.Languages.OrderBy(l => l, StringComparer.Ordinal)
            .Select(l => Str(l) + ": " + Str(text.Text(l)))) + "}";

    private static List<T> Each<T>(JsonElement root, string name, Func<JsonElement, T?> from)
        where T : class =>
        root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
            ? [.. array.EnumerateArray().Select(from).Where(v => v is not null).Select(v => v!)]
            : [];
}

/// <summary>
/// ЗАПИСЬ ОПЫТА НАБОРА. Идентификатор ОБЯЗАТЕЛЕН и постоянен (см. <see cref="ExperiencePack"/>):
/// запись без него разбор выбрасывает — случайный идентификатор при установке означал бы
/// второй комплект записей у каждого сервера организации.
/// </summary>
public sealed class ExperiencePackRecord
{
    /// <summary>Постоянный идентификатор записи (он же id строки <c>experience</c>).</summary>
    public string Id { get; set; } = "";

    /// <summary>Код навыка справочника (<c>code-review</c>); пусто — запись общая.</summary>
    public string Skill { get; set; } = "";

    /// <summary>Тэги записи — тема работы, как у задач. Пометка владельца сюда НЕ пишется:
    /// её проставляет установка сама (<see cref="PackCodes.ExperienceOwner"/>).</summary>
    public List<string> Tags { get; set; } = [];

    /// <summary>«Загружать всегда»: запись идёт в задание помимо отбора. Ставится скупо —
    /// поставляемая пачка с этой пометкой пришла бы КАЖДОЙ задаче организации.</summary>
    public bool AlwaysLoad { get; set; }

    /// <summary>Текст записи по языкам.</summary>
    public LocalizedText Text { get; set; } = new();

    internal static ExperiencePackRecord? From(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var record = new ExperiencePackRecord
        {
            Id = JsonRead.Str(e, "id"),
            Skill = JsonRead.Str(e, "skill"),
            Tags = JsonRead.Strings(e, "tags"),
            AlwaysLoad = JsonRead.Bool(e, "alwaysLoad", false),
            Text = LocalizedText.From(e, "text"),
        };
        return record.Id.Length > 0 && !record.Text.IsEmpty ? record : null;
    }
}

/// <summary>
/// УЗЕЛ ШАБЛОНА ЗАДАЧ, поставляемый вместе со стилем (блок <c>templates</c>). Состав полей
/// согласован с подзадачей T-271-S0 «Опыт: действия агента для анализа и шаблон „Анализ
/// опыта“»: идентификатор постоянный (как у записей), <see cref="Parent"/> — идентификатор
/// узла-родителя ИЗ ЭТОГО ЖЕ набора (пусто — корневой узел шаблона).
/// </summary>
public sealed class ExperiencePackTemplate
{
    /// <summary>Постоянный идентификатор узла (он же id строки <c>tasks</c>).</summary>
    public string Id { get; set; } = "";

    /// <summary>Идентификатор узла-родителя из того же набора; пусто — корневой узел.</summary>
    public string Parent { get; set; } = "";

    public LocalizedText Title { get; set; } = new();
    public LocalizedText Description { get; set; } = new();
    public LocalizedText Acceptance { get; set; } = new();

    /// <summary>Коды навыков, которые узел требует от исполнителя.</summary>
    public List<string> Skills { get; set; } = [];

    internal static ExperiencePackTemplate? From(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var node = new ExperiencePackTemplate
        {
            Id = JsonRead.Str(e, "id"),
            Parent = JsonRead.Str(e, "parent"),
            Title = LocalizedText.From(e, "title"),
            Description = LocalizedText.From(e, "description"),
            Acceptance = LocalizedText.From(e, "acceptance"),
            Skills = JsonRead.Strings(e, "skills"),
        };
        return node.Id.Length > 0 && !node.Title.IsEmpty ? node : null;
    }
}

/// <summary>
/// СЛУЖЕБНЫЕ КОДЫ НАБОРА ОПЫТА (T-270-S0) — родня <c>PluginCodes</c>. Пометка владельца
/// живёт ТЭГОМ записи: снятие набора находит по ней свои записи, а отбор опыта в задание
/// такой тэг темой работы не считает (<c>ExperienceService.TagsMatch</c>).
/// </summary>
public static class PackCodes
{
    /// <summary>Приставка служебного тэга-владельца: <c>pack:style.strict-review</c>.</summary>
    public const string ExperienceOwnerPrefix = "pack:";

    /// <summary>Пометка владельца записей набора.</summary>
    public static string ExperienceOwner(string packCode) =>
        ExperienceOwnerPrefix + packCode.Trim();

    /// <summary>Тэг — это пометка владельца-набора, а не тема работы.</summary>
    public static bool IsPackOwner(string? tag) =>
        tag is not null && tag.StartsWith(ExperienceOwnerPrefix, StringComparison.Ordinal);

    /// <summary>Код набора из пометки владельца; null — это не пометка.</summary>
    public static string? PackOf(string? tag) =>
        IsPackOwner(tag) ? tag![ExperienceOwnerPrefix.Length..] : null;
}

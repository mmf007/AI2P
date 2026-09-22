using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-268-S0 (ветка T-318 «Проект опыта»): ПОИСК ПО ОПЫТУ И ИНСТРУМЕНТ АГЕНТА (вариант Б).
///
/// До этой версии достать запись опыта, не поместившуюся в задание, было НЕЧЕМ: у агента
/// были только create_experience и update_experience, а подсказка <c>prompt.job.24</c>
/// предлагала «спроси человека». Проверяется:
/// <list type="number">
/// <item>поиск находит запись по слову ИЗ СЕРЕДИНЫ текста, а не только по началу;</item>
/// <item>неактивные записи не выдаются без флага и выдаются с <c>includeInactive</c>;</item>
/// <item>отбор по области (project / template / general) работает;</item>
/// <item>индекс ДОГОНЯЕТ запись, заведённую мимо сервиса (так они приезжают репликацией);</item>
/// <item>у инструмента <c>search_experience</c> ЕСТЬ запись справочника действий во всех
/// пяти языках — без неё правила безопасности его не закрывают вовсе (наука 2d3af8da);</item>
/// <item>подсказка про отброшенные записи во всех пяти словарях больше не предлагает
/// «спроси человека», а называет инструмент и команду;</item>
/// <item>порог отсечения: запись без лексического попадания в выдачу не попадает;</item>
/// <item>дедупликация при записи (§4.9): сильное совпадение ловится, слабое — помечается.</item>
/// </list>
/// </summary>
public sealed class T268S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private Project NewProject() =>
        _f.Projects.Create("Проект T-268-S0", Path.Combine(_f.Dir, "prj268"), null, null);

    private static string I18nDir => Path.Combine(AppContext.BaseDirectory, "i18n");

    private static readonly string[] Langs = ["ru", "en", "es", "pt", "zh-cn"];

    // ---------- находит ----------

    [Fact]
    public void The_Search_Finds_A_Record_By_A_Word_From_The_Middle_Of_Its_Text()
    {
        var project = NewProject();
        var wanted = _f.Experience.CreateForProject(project.Id,
            "Выкладка релиза: buildRelease запускается штатно, если звать его из python — "
            + "проверено на выпуске 1.77.", null);
        _f.Experience.CreateForProject(project.Id,
            "Иконка приложения: пять вариантов лежат в doc/icons, пересобираются скриптом.", null);

        var hits = _f.Experience.Search("релиз", projectId: project.Id);
        Assert.Equal(wanted.Id, hits.Single().Record.Id);

        // и по слову в другой падежной форме: «выкладке» → основа «выкладк» (морфологии
        // у стандартного токенизатора нет, её грубо заменяет поиск по префиксу)
        Assert.Equal(wanted.Id, _f.Experience.Search("выкладке", projectId: project.Id)
            .Single().Record.Id);
    }

    [Fact]
    public void A_Record_Without_A_Lexical_Hit_Is_Not_Taken_At_All()
    {
        var project = NewProject();
        _f.Experience.CreateForProject(project.Id, "про сборку пакета установки", null);
        _f.Experience.CreateForProject(project.Id, "про репликацию между серверами", null);

        // ПОРОГ ОТСЕЧЕНИЯ: свежесть и тэги переставляют найденное, но сами никого не находят —
        // «добить выдачу чем попало» это ровно то поведение отбора, ради которого заведён поиск
        Assert.Empty(_f.Experience.Search("кандинский", projectId: project.Id));
        Assert.Single(_f.Experience.Search("репликация", projectId: project.Id));
    }

    [Fact]
    public void The_Tags_Of_The_Task_Move_A_Record_Up_But_Do_Not_Find_It()
    {
        var project = NewProject();
        var plain = _f.Experience.CreateForProject(project.Id, "сборка пакета: первый вывод", null);
        var tagged = _f.Experience.CreateForProject(project.Id, "сборка пакета: второй вывод", null,
            tags: ["выкладка"]);

        var hits = _f.Experience.Search("сборка", projectId: project.Id, taskTags: ["выкладка"]);
        Assert.Equal(2, hits.Count);
        Assert.Equal(tagged.Id, hits[0].Record.Id);   // совпадение темы подняло запись
        Assert.Equal(1, hits[0].TagHits);
        Assert.Equal(plain.Id, hits[1].Record.Id);
        Assert.Equal(0, hits[1].TagHits);
    }

    // ---------- активность ----------

    [Fact]
    public void Inactive_Records_Are_Given_Only_With_The_Flag()
    {
        var project = NewProject();
        var live = _f.Experience.CreateForProject(project.Id, "живая запись про репликацию", null);
        var off = _f.Experience.CreateForProject(project.Id, "погашенная запись про репликацию", null);
        _f.Experience.SetActive(off.Id, false, null);

        // по умолчанию — ТОЛЬКО АКТИВНЫЕ: неактивную запись агент не получает ни при каком
        // раскладе, и находить её по умолчанию значило бы вернуть её в работу через чёрный ход
        Assert.Equal(live.Id, _f.Experience.Search("репликация", projectId: project.Id)
            .Single().Record.Id);

        var both = _f.Experience.Search("репликация", projectId: project.Id, includeInactive: true);
        Assert.Equal(2, both.Count);
        Assert.Contains(off.Id, both.Select(h => h.Record.Id));
    }

    [Fact]
    public void A_Deleted_Record_Is_Not_Found_Even_With_The_Flag()
    {
        var project = NewProject();
        var gone = _f.Experience.CreateForProject(project.Id, "удалённая запись про репликацию", null);
        _f.Experience.Delete(gone.Id, null);

        Assert.Empty(_f.Experience.Search("репликация", projectId: project.Id, includeInactive: true));
    }

    // ---------- области ----------

    [Fact]
    public void The_Scope_Filter_Separates_Project_Template_And_General()
    {
        var project = NewProject();
        var template = _f.Tasks.Create(
            new TaskItem { Title = "Шаблон T-268-S0", IsTemplate = true, ProjectId = project.Id },
            "", "", null);
        var byProject = _f.Experience.CreateForProject(project.Id, "вывод про сборку: проект", null);
        var byNode = _f.Experience.Create(template.Id, "вывод про сборку: узел шаблона", null);
        var general = _f.Experience.CreateGeneral("вывод про сборку: общее правило", null);

        Assert.Equal(byProject.Id, Ids(ExperienceService.ScopeProject).Single());
        Assert.Equal(byNode.Id, Ids(ExperienceService.ScopeTemplate).Single());
        Assert.Equal(general.Id, Ids(ExperienceService.ScopeGeneral).Single());
        // без области — все три; общее правило видно и при отборе по проекту: его получает
        // ЛЮБАЯ задача организации
        Assert.Equal(3, Ids(null).Count);

        List<string> Ids(string? scope) => _f.Experience
            .Search("сборка", scope, projectId: project.Id)
            .Select(h => h.Record.Id).ToList();
    }

    [Fact]
    public void A_Record_Of_Another_Project_Is_Not_Found()
    {
        var mine = NewProject();
        var other = _f.Projects.Create("Чужой проект", Path.Combine(_f.Dir, "prj268b"), null, null);
        _f.Experience.CreateForProject(other.Id, "чужой вывод про репликацию", null);

        Assert.Empty(_f.Experience.Search("репликация", projectId: mine.Id));
        Assert.Single(_f.Experience.Search("репликация"));   // без проекта — вся организация
    }

    // ---------- индекс ----------

    [Fact]
    public void The_Index_Catches_Up_A_Record_Inserted_Past_The_Service()
    {
        var project = NewProject();
        // так записи приезжают РЕПЛИКАЦИЕЙ: журнал изменений пишет строку прямо в таблицу,
        // мимо сервиса, а сам индекс не реплицируется (таблица производная)
        var id = Guid.NewGuid().ToString();
        var at = Sql.ToDb(DateTime.UtcNow);
        using (var conn = _f.Db.Open())
        {
            Sql.Exec(conn, null, """
                INSERT INTO experience (id, project_id, text, always_load, is_active,
                                        created_at, updated_at)
                VALUES (@id, @p, @t, 0, 1, @at, @at)
                """,
                ("@id", id), ("@p", project.Id),
                ("@t", "приехало репликацией: вывод про репликацию файлов"), ("@at", at));
        }

        if (_f.Experience.SearchIndexReady)
        {
            // в индексе её нет — и поиск её не находит
            Assert.Empty(_f.Experience.Search("репликация", projectId: project.Id));
        }
        Assert.Equal(1, _f.Experience.CatchUpSearchIndex());
        Assert.Equal(id, _f.Experience.Search("репликация", projectId: project.Id)
            .Single().Record.Id);
        // проход идемпотентен: второй раз добавлять нечего
        Assert.Equal(0, _f.Experience.CatchUpSearchIndex());
    }

    [Fact]
    public void The_Index_Table_Is_Not_Replicated()
    {
        // таблица ПРОИЗВОДНАЯ: в белом списке журнала изменений её быть не должно —
        // иначе виртуальная таблица FTS5 уехала бы партнёру строками (ЧЕТВЁРТАЯ наука 4.4)
        Assert.DoesNotContain(ExperienceIndex.Table, ChangeLog.OrgTables);
    }

    [Fact]
    public void Editing_The_Text_Moves_The_Record_In_The_Index()
    {
        var project = NewProject();
        var record = _f.Experience.CreateForProject(project.Id, "первый вывод про репликацию", null);
        Assert.Single(_f.Experience.Search("репликация", projectId: project.Id));

        _f.Experience.Update(record.Id, "теперь вывод про выкладку релиза", null);
        Assert.Empty(_f.Experience.Search("репликация", projectId: project.Id));
        Assert.Single(_f.Experience.Search("выкладка", projectId: project.Id));
    }

    // ---------- дедупликация при записи (§4.9) ----------

    [Fact]
    public void A_Strong_Duplicate_Is_Caught_And_A_Weak_One_Is_Only_Marked()
    {
        var project = NewProject();
        const string text = "Выкладка релиза: buildRelease запускается штатно, если звать его "
                            + "из python — проверено на выпуске 1.77, код возврата 0.";
        var first = _f.Experience.CreateForProject(project.Id, text, null);

        // тот же текст с мелкой правкой — это ТА ЖЕ запись
        var same = _f.Experience.FindSimilar(text + " И ещё раз проверено.",
            ExperienceService.ScopeProject, project.Id);
        Assert.NotNull(same);
        Assert.Equal(first.Id, same!.Record.Id);
        Assert.True(same.Ratio >= ExperienceService.DuplicateRatio);

        // пересказ той же мысли ДРУГИМИ словами лексическое сравнение не ловит — и не может:
        // эмбеддингов у нас нет. Это ограничение названо в отчёте честно
        Assert.Null(_f.Experience.FindSimilar("Сборку дистрибутива надо запускать через обёртку.",
            ExperienceService.ScopeProject, project.Id));
        // и наоборот: общая тема при разном содержании похожестью не считается
        Assert.Null(_f.Experience.FindSimilar(
            "Выкладка релиза: на Linux пакет собирает makeself, а не Inno Setup.",
            ExperienceService.ScopeProject, project.Id));

        // а вот запись про ТО ЖЕ САМОЕ с добавленной подробностью — «похоже»: заводим,
        // но помечаем тэгом для ревизии человеком
        var weak = _f.Experience.FindSimilar(
            "Выкладка релиза: buildRelease запускается из python, проверено на выпуске 1.90; "
            + "код возврата 0, но version.json пришлось править руками.",
            ExperienceService.ScopeProject, project.Id);
        Assert.NotNull(weak);
        Assert.True(weak!.Ratio >= ExperienceService.SimilarRatio);
        Assert.True(weak.Ratio < ExperienceService.DuplicateRatio);
    }

    [Fact]
    public void Similarity_Is_Symmetric_And_Bounded()
    {
        Assert.Equal(1.0, ExperienceService.Similarity("один и тот же текст", "один и тот же текст"), 3);
        Assert.Equal(0.0, ExperienceService.Similarity("репликация серверов", "иконка приложения"), 3);
        Assert.Equal(ExperienceService.Similarity("а бэ вэ", "бэ вэ гэ"),
            ExperienceService.Similarity("бэ вэ гэ", "а бэ вэ"), 6);
        // пустой текст ни на что не похож — иначе первая же запись отказала бы всем
        Assert.Equal(0.0, ExperienceService.Similarity("", "что угодно"), 3);
    }

    // ---------- справочник действий и словари ----------

    [Fact]
    public void The_Search_Tool_Has_Its_Action_Catalog_Row_In_All_Five_Languages()
    {
        // ИНСТРУМЕНТ БЕЗ ЗАПИСИ СПРАВОЧНИКА правилами безопасности не закрывается ВОВСЕ:
        // CodeByTool вернёт null, и Authorize разрешит вызов (наука 2d3af8da)
        foreach (var lang in Langs)
        {
            var file = Path.Combine(I18nDir, $"ActionCatalogService_{lang}.json");
            using var seed = JsonDocument.Parse(File.ReadAllText(file));
            Assert.True(seed.RootElement.GetProperty("seedVersion").GetInt32() >= 23,
                $"seedVersion в ActionCatalogService_{lang}.json не поднят");
            var row = seed.RootElement.GetProperty("actions").EnumerateArray()
                .FirstOrDefault(a => a.TryGetProperty("tool", out var t)
                                     && t.ValueKind == JsonValueKind.String
                                     && t.GetString() == "search_experience");
            Assert.True(row.ValueKind == JsonValueKind.Object,
                $"в ActionCatalogService_{lang}.json нет записи на инструмент search_experience");
            Assert.Equal("AI2P.Experience.Search", row.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("prompt").GetString()));
        }

        // и засеянный справочник действительно отдаёт код по имени инструмента —
        // именно этим значением правило безопасности и находит действие
        _f.Actions.Seed();
        Assert.Equal("AI2P.Experience.Search", _f.Actions.CodeByTool("search_experience"));
    }

    [Fact]
    public void The_Note_About_Skipped_Records_Names_The_Tool_Instead_Of_Asking_The_Human()
    {
        foreach (var lang in Langs)
        {
            var text = Loc.In(lang, "prompt.job.24", 358);
            Assert.Contains("search_experience", text);
            Assert.Contains("experience-find", text);
            Assert.Contains("358", text);
        }
        // русский текст прежней редакции звал человека — этого там больше нет
        Assert.DoesNotContain("спроси человека", Loc.In("ru", "prompt.job.24", 1));
        Assert.DoesNotContain("ask the human", Loc.In("en", "prompt.job.24", 1));
    }

    [Fact]
    public void The_Cli_Has_The_Experience_Find_Subcommand()
    {
        // CLI-агенту маркер не годится: он заканчивает ход и стоит дорого, а команда
        // отвечает в том же ходе
        var source = File.ReadAllText(Path.Combine(SourceRoot(), "src", "AI2P.Server", "Cli",
            "AgentCliRunner.cs"));
        Assert.Contains("\"experience-find\"", source);
        Assert.Contains("search_experience", source);
        foreach (var lang in Langs)
        {
            Assert.NotEqual("msg.agentCli.45", Loc.In(lang, "msg.agentCli.45"));
        }
    }

    /// <summary>Корень дерева исходников — от каталога сборки тестов вверх до AI2P_app.</summary>
    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "AI2P.Server")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new DirectoryNotFoundException("не найден корень исходников");
    }
}

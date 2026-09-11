using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-191: ПЕРЕВОД КОННЕКТОРОВ И СИДА СПРАВОЧНИКОВ (продолжение T-180).
///
/// Проверяется разделение, ради которого задача и делалась:
/// <list type="number">
/// <item>сообщение ЧЕЛОВЕКУ (ошибка коннектора, строка консоли задания) идёт по языку
///   УСТАНОВКИ — <see cref="Loc.T(string)"/>;</item>
/// <item>текст, уходящий МОДЕЛИ (результат инструмента, блок правил безопасности в промпте),
///   идёт по языку КОМАНДЫ — <see cref="Loc.In(string?,string)"/>;</item>
/// <item>распознавание ответа агента (обещание «доделаю потом») работает на ВСЕХ языках
///   словарей сразу — язык ответа задаёт команда, а не установка;</item>
/// <item>названия справочников (роли, навыки, форматы) сеются из файлов дистрибутива
///   i18n/RefDataService_&lt;lang&gt;.json на каждый язык, а не вписаны в код: значения
///   уезжают в БД организации и реплицируются.</item>
/// </list>
/// </summary>
public sealed class T191Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai2p-t191-" + Guid.NewGuid().ToString("N"));
    private readonly Database _db;

    private static string SeedDir => Path.Combine(AppContext.BaseDirectory, "i18n");

    public T191Tests()
    {
        _db = new Database(Path.Combine(_dir, "org"), "ai2p.db");
        _db.Init("unid-T191");
    }

    public void Dispose()
    {
        Loc.Lang = LocCatalog.BaseLanguage;
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // временный каталог уберёт система
        }
    }

    private RefDataService Refs() => new(_db, SeedDir);

    // --- часть 1: сообщения коннекторов ---

    /// <summary>Сообщение коннектора человеку берётся из словаря языка установки.</summary>
    [Fact]
    public void Connector_Messages_Come_From_Dictionaries()
    {
        Loc.Lang = "ru";
        Assert.Equal("Trello недоступен: сеть", Loc.T("msg.trelloImporter.38", "сеть"));
        Assert.Equal("Таймаут подключения к провайдеру", Loc.T("msg.openAiCompatibleConnector.4"));

        Loc.Lang = "en";
        Assert.Equal("Trello is unreachable: network", Loc.T("msg.trelloImporter.38", "network"));
        Assert.Equal("Timed out connecting to the provider", Loc.T("msg.openAiCompatibleConnector.4"));
    }

    /// <summary>В коде русских текстов для человека у переведённых коннекторов не осталось:
    /// на английской установке сообщение обязано быть английским целиком.</summary>
    [Fact]
    public void No_Russian_Left_In_Translated_Connector_Messages()
    {
        Loc.Lang = "en";
        foreach (var key in new[]
                 {
                     "msg.comfyUiConnector.5", "msg.comfyUiConnector.28", "msg.modelInstall.14",
                     "msg.localModelProcess.6", "msg.teamWork.4", "msg.importKey.6",
                     "msg.connectorRegistry.3", "msg.orgSecretKey.1", "msg.secret.1",
                     "msg.agentTimeout.1", "msg.anthropicConnector.5",
                 })
        {
            var text = Loc.T(key);
            Assert.DoesNotContain(text, c => c is >= 'А' and <= 'я');
        }
    }

    /// <summary>Формат времени в тексте словаря переехал БЕЗ лишнего экранирования: «{0:hh\:mm\:ss}»
    /// обязан отформатироваться, а не вернуться шаблоном (кривой формат Loc.T глотает молча).</summary>
    [Fact]
    public void Timespan_Format_Survived_The_Move_To_The_Dictionary()
    {
        Loc.Lang = "ru";
        var text = Loc.T("msg.comfyUiConnector.19", new TimeSpan(0, 1, 2, 3));
        Assert.Equal("Задание остановлено после 01:02:03", text);
        Assert.DoesNotContain("{0", text);
    }

    /// <summary>Текст, уходящий МОДЕЛИ, идёт на языке команды, а не установки.</summary>
    [Fact]
    public void Texts_For_The_Model_Follow_The_Team_Language()
    {
        Loc.Lang = "ru";
        Assert.Equal("Ответ человека: да", Loc.In(null, "prompt.agent.12", "да"));
        Assert.Equal("The human's answer: yes", Loc.In("en", "prompt.agent.12", "yes"));
    }

    /// <summary>Блок правил безопасности собирается на языке команды (он часть промпта).</summary>
    [Fact]
    public void Security_Rules_Block_Is_Built_In_The_Team_Language()
    {
        var rules = new List<SecurityRule>
        {
            new()
            {
                Target = SecurityTarget.Directory, Pattern = @"c:\tmp\*",
                Permission = SecurityPermission.Allow, OpRead = true, OpWrite = true,
            },
        };
        var evaluator = new SecurityEvaluator(rules, null);

        var russian = evaluator.DescribeForAgent("ru");
        Assert.Contains("Правила безопасности этой задачи", russian);
        Assert.Contains("разрешено", russian);
        Assert.Contains("читать, писать", russian);

        var english = evaluator.DescribeForAgent("en");
        Assert.Contains("Security rules of this task", english);
        Assert.Contains("allowed", english);
        Assert.Contains("read, write", english);
        Assert.DoesNotContain(english, c => c is >= 'А' and <= 'я');
    }

    /// <summary>Обещание «доделаю, когда прогон закончится» ловится и по-русски, и по-английски:
    /// язык ответа задаёт команда, поэтому образцы берутся по всем языкам словарей сразу.</summary>
    [Theory]
    [InlineData("Прогон тестов идёт в фоне, отчёт допишу позже.")]
    [InlineData("Как только прогон закончится, соберу выкладку.")]
    [InlineData("The full test run is running in the background, I will report back later.")]
    [InlineData("As soon as the build finishes I will collect the release.")]
    public void Unfinished_Work_Is_Detected_In_Both_Languages(string answer)
    {
        Assert.True(BackgroundPromise.Detect(answer, out var evidence), answer);
        Assert.NotEqual("", evidence);
    }

    /// <summary>Законченная сдача не считается обещанием ни на одном языке.</summary>
    [Theory]
    [InlineData("Прогон тестов пройден: 1200/1200, отчёт в doc/.")]
    [InlineData("The full test run passed: 1200/1200, the report is in doc/.")]
    public void Finished_Work_Is_Not_Detected(string answer) =>
        Assert.False(BackgroundPromise.Detect(answer, out _), answer);

    // --- часть 2: сид справочников по языкам ---

    /// <summary>Названия навыков и форматов приезжают из файлов сида на каждом языке.</summary>
    [Fact]
    public void Reference_Data_Is_Seeded_Per_Language()
    {
        Refs().Seed();

        var ru = Refs().Skills("ru");
        var en = Refs().Skills("en");
        Assert.Equal("написание кода", ru.Single(s => s.Name == "code-write").Description);
        Assert.Equal("writing code", en.Single(s => s.Name == "code-write").Description);
        // языковые варианты code-навыков собираются кодом из описания языка сида
        Assert.Equal("writing code (Python)", en.Single(s => s.Name == "code-write-py").Description);
        Assert.Equal("написание кода (Python)", ru.Single(s => s.Name == "code-write-py").Description);

        Assert.Equal("изображение PNG", Refs().IoFormats("ru").Single(f => f.Name == "image/png").Description);
        Assert.Equal("PNG image", Refs().IoFormats("en").Single(f => f.Name == "image/png").Description);

        Assert.Contains(Refs().Roles("ru"), r => r.Name == "программист");
        Assert.Contains(Refs().Roles("en"), r => r.Name == "developer");
    }

    /// <summary>Язык не задан — язык установки; неизвестный язык откатывается на en.</summary>
    [Fact]
    public void Language_Of_The_Request_Defaults_To_The_Installation()
    {
        Refs().Seed();

        Loc.Lang = "en";
        Assert.Equal("plain text", Refs().IoFormats().Single(f => f.Name == "text/plain").Description);
        Loc.Lang = "ru";
        Assert.Equal("простой текст", Refs().IoFormats().Single(f => f.Name == "text/plain").Description);
        Assert.Equal("plain text", Refs().IoFormats("de").Single(f => f.Name == "text/plain").Description);
    }

    /// <summary>Сеяние идемпотентно: второй прогон не двоит записи и не плодит тексты.</summary>
    [Fact]
    public void Seeding_Is_Idempotent()
    {
        Refs().Seed();
        var skills = Refs().Skills("ru").Count;
        var roles = Refs().Roles("ru").Count;
        var formats = Refs().IoFormats("ru").Count;

        Refs().Seed();
        Refs().Seed();

        Assert.Equal(skills, Refs().Skills("ru").Count);
        Assert.Equal(roles, Refs().Roles("ru").Count);
        Assert.Equal(formats, Refs().IoFormats("ru").Count);
        Assert.Equal(94, skills);   // 89 + пять действий-режимов T-257
    }

    /// <summary>Обновление НАПОЛНЕННОЙ базы (записи заведены прошлой версией по-русски):
    /// новых строк не появляется, id прежние (ссылки задач целы), а тексты языков доезжают.</summary>
    [Fact]
    public void Existing_Rows_Get_Texts_Without_Being_Duplicated()
    {
        // так справочники выглядели до T-191: строка с русским описанием и без текстов
        using (var conn = _db.Open())
        {
            var now = Sql.ToDb(DateTime.UtcNow);
            Sql.Exec(conn, null, """
                INSERT INTO skills (id, name, description, is_custom, created_at, updated_at)
                VALUES ('skill-old', 'code-write', 'написание кода', 1, @now, @now)
                """, ("@now", now));
            Sql.Exec(conn, null, """
                INSERT INTO roles (id, name, created_at, updated_at)
                VALUES ('role-old', 'программист', @now, @now)
                """, ("@now", now));
        }

        Refs().Seed();

        var skill = Refs().Skills("en").Single(s => s.Name == "code-write");
        Assert.Equal("skill-old", skill.Id);
        Assert.Equal("writing code", skill.Description);
        Assert.False(skill.IsCustom);

        var role = Refs().Roles("en").Single(r => r.Id == "role-old");
        Assert.Equal("developer", role.Name);
        Assert.Single(Refs().Roles("ru"), r => r.Name == "программист");
    }

    /// <summary>Названия справочников реплицируются: тексты по языкам — такие же строки
    /// БД организации, значит они обязаны быть в журнале изменений.</summary>
    [Fact]
    public void Reference_Texts_Are_Replicated()
    {
        Assert.Contains("role_texts", ChangeLog.OrgTables);
        Assert.Contains("skill_texts", ChangeLog.OrgTables);
        Assert.Contains("io_format_texts", ChangeLog.OrgTables);
    }

    /// <summary>Свой навык пользователя описанием языка запроса и признаком «кастом».</summary>
    [Fact]
    public void Custom_Skill_Keeps_Its_Own_Text()
    {
        Refs().Seed();
        Refs().AddSkill("plan-sprint", "планирование спринта", lang: "ru");

        var added = Refs().Skills("ru").Single(s => s.Name == "plan-sprint");
        Assert.True(added.IsCustom);
        Assert.Equal("планирование спринта", added.Description);
        // языка перевода у кастомной записи нет — показывается её собственное описание
        Assert.Equal("планирование спринта",
            Refs().Skills("en").Single(s => s.Name == "plan-sprint").Description);
    }
}

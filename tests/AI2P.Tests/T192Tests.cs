using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// ЗАВЕРШЕНИЕ ПЕРЕВОДА T-180: последняя порция текстов из кода (JobOrchestrator,
/// AiConnectorBase, ClaudeCliConnector, FileToolset, ModelKeyService, AgentToolset).
///
/// Проверяется то же деление, что и в T-190/T-191, плюс сквозная целостность словарей:
/// <list type="number">
/// <item>ответ файлового инструмента читает АГЕНТ — он идёт по языку КОМАНДЫ
///   (<see cref="FileToolset.Language"/>, <see cref="Loc.In(string?,string)"/>), а не установки;</item>
/// <item>язык раздаётся из <see cref="AgentToolset.Language"/> — как он уже раздавался
///   инструментам заданий;</item>
/// <item>исключения задания и строки консоли читает ЧЕЛОВЕК — они по языку УСТАНОВКИ
///   (<see cref="Loc.T(string)"/>);</item>
/// <item>словари ru и en сходятся посчётно И по составу подстановок: расхождение дырок
///   ломает <c>string.Format</c> на одном языке и молчит на другом.</item>
/// </list>
/// </summary>
public sealed class T192Tests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "ai2p-t192-" + Guid.NewGuid().ToString("N"));

    public T192Tests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "readme.md"), "текст");
    }

    public void Dispose()
    {
        Loc.Lang = LocCatalog.BaseLanguage;
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // временный каталог уберёт система
        }
    }

    private static async Task<string> Call(FileToolset tools, string name, string argsJson)
    {
        using var doc = JsonDocument.Parse(argsJson);
        return await tools.ExecuteAsync(name, doc.RootElement, default);
    }

    // --- ответы файловых инструментов: язык КОМАНДЫ ---

    /// <summary>Установка русская, команда английская — ответ инструмента английский.</summary>
    [Fact]
    public async Task File_Tool_Answers_Follow_The_Team_Language()
    {
        Loc.Lang = "ru";
        var tools = new FileToolset(_root);

        Assert.Equal("Файл не найден: нет.txt", await Call(tools, "read_file", """{ "path": "нет.txt" }"""));

        tools.Language = "en";
        Assert.Equal("File not found: нет.txt", await Call(tools, "read_file", """{ "path": "нет.txt" }"""));
    }

    /// <summary>Отказ по границе каталога проекта агент тоже читает — он на языке команды.</summary>
    [Fact]
    public async Task File_Tool_Refusal_Follows_The_Team_Language()
    {
        Loc.Lang = "ru";
        var tools = new FileToolset(_root) { Language = "en" };
        var answer = await Call(tools, "read_file", """{ "path": "../../secret.txt" }""");

        Assert.Contains("is outside the project folder", answer);
        Assert.DoesNotContain(answer, c => c is >= 'А' and <= 'я');
    }

    /// <summary>Пустой каталог, обрезка файла, запись — тексты берутся из словаря по языку команды.</summary>
    [Fact]
    public async Task File_Tool_Reports_Are_Translated()
    {
        Loc.Lang = "ru";
        var empty = Path.Combine(_root, "пусто");
        Directory.CreateDirectory(empty);
        var tools = new FileToolset(_root, "prj-1", "http://localhost:5480/ai2p");

        Assert.Equal("(каталог пуст)", await Call(tools, "list_files", """{ "path": "пусто" }"""));
        Assert.StartsWith("Файл создан:", await Call(tools, "write_file", """{ "path": "a.txt", "content": "x" }"""));
        Assert.StartsWith("Файл перезаписан:", await Call(tools, "write_file", """{ "path": "a.txt", "content": "y" }"""));

        tools.Language = "en";
        Assert.Equal("(the folder is empty)", await Call(tools, "list_files", """{ "path": "пусто" }"""));
        Assert.StartsWith("File overwritten:", await Call(tools, "write_file", """{ "path": "a.txt", "content": "z" }"""));
        Assert.Equal("No matches for “чего-то нет” were found",
            await Call(tools, "search_files", """{ "query": "чего-то нет" }"""));
    }

    /// <summary>Обязательный параметр не задан — отказ читает агент, значит язык команды.</summary>
    [Fact]
    public async Task Missing_Argument_Refusal_Follows_The_Team_Language()
    {
        Loc.Lang = "ru";
        var tools = new FileToolset(_root) { Language = "en" };
        var answer = await Call(tools, "read_file", """{ }""");

        Assert.Contains("The required parameter", answer);
        Assert.DoesNotContain(answer, c => c is >= 'А' and <= 'я');
    }

    /// <summary>Язык набора инструментов раздаётся ФАЙЛОВЫМ инструментам, а не только заданиям.</summary>
    [Fact]
    public void AgentToolset_Passes_Language_To_File_Tools()
    {
        var files = new FileToolset(_root);
        var toolset = new AgentToolset(files, null!) { Language = "en" };

        Assert.Equal("en", files.Language);
        Assert.Same(files, toolset.Files);
    }

    // --- сообщения человеку: язык УСТАНОВКИ ---

    /// <summary>Отказы запуска задания (они уходят в UI и в артефакт ошибки) — из словаря.</summary>
    [Fact]
    public void Job_Start_Refusals_Come_From_Dictionaries()
    {
        Loc.Lang = "ru";
        Assert.Equal("По задаче уже есть активное задание", Loc.T("msg.jobOrchestrator.4"));
        Assert.Equal("Исполнитель «Вася» уже работает над другим заданием (ТЗ п. 2.2)",
            Loc.T("msg.jobOrchestrator.7", "Вася"));

        Loc.Lang = "en";
        Assert.Equal("The task already has an active job", Loc.T("msg.jobOrchestrator.4"));
        Assert.Equal("The executor «Basil» is already working on another job (spec 2.2)",
            Loc.T("msg.jobOrchestrator.7", "Basil"));
    }

    /// <summary>Сообщения CLI-коннектора и ключей моделей на английской установке — без русского.</summary>
    [Fact]
    public void No_Russian_Left_In_The_Last_Batch()
    {
        Loc.Lang = "en";
        foreach (var key in new[]
                 {
                     "msg.jobOrchestrator.1", "msg.jobOrchestrator.16", "msg.jobOrchestrator.22",
                     "msg.aiConnectorBase.2", "msg.aiConnectorBase.20", "msg.aiConnectorBase.24",
                     "msg.claudeCliConnector.1", "msg.claudeCliConnector.18", "msg.claudeCliConnector.25",
                     "msg.modelKey.3", "msg.modelKey.7", "msg.agentToolset.6", "msg.taskToolset.1",
                     "msg.fileToolset.1", "prompt.fileToolset.3", "prompt.fileToolset.13",
                 })
        {
            Assert.DoesNotContain(Loc.T(key), c => c is >= 'А' and <= 'я');
        }
    }

    /// <summary>Формат внутри подстановки пережил переезд: и время, и число с точностью.
    /// Кривой формат <see cref="Loc.T(string,object[])"/> глотает молча и возвращает шаблон.</summary>
    [Fact]
    public void Formats_Survived_The_Move_To_The_Dictionary()
    {
        Loc.Lang = "ru";
        var answer = Loc.T("msg.aiConnectorBase.7", new TimeSpan(0, 1, 2, 3), 100, 7, 9, "stop");
        Assert.StartsWith("Ответ получен за 01:02:03:", answer);
        Assert.DoesNotContain("{0", answer);

        var limit = Loc.T("msg.jobOrchestrator.9", "Вася", 900, 1000, 5.0, 100, 10.0,
            new DateTime(2026, 8, 13, 18, 30, 0, DateTimeKind.Local));
        Assert.Contains("за окно 5 ч", limit);
        Assert.Contains("13.08 18:30", limit);
        Assert.DoesNotContain("{0", limit);
    }

    // --- целостность словарей ---

    /// <summary>Составы ru и en сходятся посчётно: текст без перевода Loc отдаёт как есть
    /// по базовому языку, и английская установка молча показывает русскую строку.</summary>
    [Fact]
    public void Dictionaries_Have_The_Same_Keys()
    {
        var (ru, en) = LoadDictionaries();

        Assert.Equal([], ru.Keys.Where(k => !en.ContainsKey(k)).Order().ToArray());
        Assert.Equal([], en.Keys.Where(k => !ru.ContainsKey(k)).Order().ToArray());
    }

    /// <summary>Набор подстановок в паре ru/en одинаков — и номера дырок, и формат внутри них.
    /// Расхождение роняет string.Format на одном языке и остаётся незаметным на другом.</summary>
    [Fact]
    public void Dictionaries_Have_The_Same_Placeholders()
    {
        var (ru, en) = LoadDictionaries();
        var mismatched = ru
            .Where(p => en.ContainsKey(p.Key) && Placeholders(p.Value) != Placeholders(en[p.Key]))
            .Select(p => p.Key)
            .Order()
            .ToArray();

        Assert.Equal([], mismatched);
    }

    private static string Placeholders(string text) =>
        string.Join('|', System.Text.RegularExpressions.Regex
            .Matches(text, @"\{\d+(?::[^}]*)?\}")
            .Select(m => m.Value)
            .Order());

    private static (Dictionary<string, string> Ru, Dictionary<string, string> En) LoadDictionaries()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "i18n");
        return (Read(Path.Combine(dir, "ru.json")), Read(Path.Combine(dir, "en.json")));

        static Dictionary<string, string> Read(string path) =>
            JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    }
}

using System.Text.Json;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-274-S0 «Устранить причину несанкционированного автозапуска»: у запуска ОДНОЙ задачи
/// появился переспрос с галочкой «автоматически выполнять новых потомков» (по умолчанию
/// включена), а ответ уходит вместе с запуском — POST /tasks/{id}/start?autoChildren=.
///
/// Зачем: подзадачи агент заводит ПРЯМО В ХОДЕ работы, и система запускает их сама
/// (todo22, очередь авторазбиения, задачи-блокирующие). Пометка «автозапуск выключен»
/// (T-54-S0) существовала, но поставить её было негде: кнопка выключения показывается
/// только у задачи, У КОТОРОЙ УЖЕ ЕСТЬ подзадачи, а указание «не запускай потомков» в
/// описании задачи не решает ничего — запускает их система, а не агент.
///
/// У запуска ИЕРАРХИИ вопроса нет намеренно: там человек уже сказал «беги всё дерево»,
/// и StartHierarchyAsync сам снимает пометку с корня.
///
/// Проверяется по тексту файлов (приём T-209/T-244): живая проверка в браузере — отдельно.
/// </summary>
public sealed class T274S0Tests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 10; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return dir.Parent?.FullName
                       ?? throw new InvalidOperationException("у AI2P_app нет родительского каталога");
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }

    private static string Src(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot(), "AI2P_app", "src", .. parts]));

    /// <summary>Переспрос запуска есть, и в нём галочка автозапуска потомков.</summary>
    [Fact]
    public void The_Start_Button_Asks_About_New_Subtasks()
    {
        var card = Src("AI2P.UI", "Components", "TaskCardView.razor");

        Assert.Contains("@ref=\"_startBox\"", card);
        Assert.Contains("data-task-start-autochildren=\"1\"", card);
        Assert.Contains("L[\"task.start.autoChildren\"]", card);
        // ответ уходит вместе с запуском, а не отдельной кнопкой
        Assert.Contains("await Api.StartTaskAsync(TaskId, force, autoChildren);", card);
        // галочка ставится по нынешнему состоянию задачи: у обычной это «включено»
        Assert.Contains("_startAutoChildren = !AutoStartOff;", card);
    }

    /// <summary>Ответ доезжает до сервера и до оркестратора: у запуска свой параметр, и он
    /// ставит (или снимает) ту же пометку ветки, что кнопка T-54-S0.</summary>
    [Fact]
    public void The_Answer_Sets_The_Branch_Flag()
    {
        var api = Src("AI2P.Server", "Api", "ApiEndpoints.cs");
        var orchestrator = Src("AI2P.Connectors", "JobOrchestrator.cs");

        Assert.Contains("api.MapPost(\"/tasks/{id}/start\", async (string id, bool? force, bool? autoChildren)",
            api);
        Assert.Contains("bool? autoChildren = null)", orchestrator);
        Assert.Contains("_tasks.SetLaunchFlag(taskId, TaskService.NoAutoStartFlag, !auto);", orchestrator);
    }

    /// <summary>Тексты переспроса есть во ВСЕХ словарях: составы обязаны сходиться (T-180).</summary>
    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("pt")]
    [InlineData("zh-cn")]
    public void The_Confirm_Is_Translated(string lang)
    {
        var path = Path.Combine(RepoRoot(), "AI2P_app", "i18n", lang + ".json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        foreach (var key in new[]
                 {
                     "task.start.confirm", "task.start.autoChildren", "task.start.autoChildren.hint",
                 })
        {
            Assert.True(doc.RootElement.TryGetProperty(key, out var value) &&
                        value.GetString() is { Length: > 0 },
                $"в словаре {lang} нет ключа {key}");
        }
    }
}

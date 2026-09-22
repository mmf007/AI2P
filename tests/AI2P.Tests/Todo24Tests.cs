using AI2P.Connectors;
using AI2P.Core.Api;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// Справочник действий агентов (ТЗ v1.21, todo24): сид встроенных на en/ru, правка промпта
/// хранится отдельно и откатывается, кастомные действия, подстановка промптов в инструменты.
/// </summary>
public sealed class ActionCatalogTests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public void Seed_Is_Idempotent_And_Bilingual()
    {
        _f.Actions.Seed(); // фикстура уже сидировала — повторный вызов ничего не дублирует

        var ru = _f.Actions.List("ru");
        var en = _f.Actions.List("en");
        // 13 сида v4 + опыт (v5, todo32) + импорт по URL (v7, todo50) + задания-соседи (v10, T-132)
        // + шаблоны проекта (v12, T-144) + получение файла по ссылке (v14, T-255)
        // + перенос задачи по иерархии (v15, T-2-S0)
        // + подзадачи и состояние чужой задачи (v16, T-34-S0)
        // + перезапуск для повторной проверки и ожидание (v17, T-31-S0)
        // + автоматическая архивация (v18, T-46-S0) — действие расписания, инструмента у него
        //   нет: его запускают расписание на дирижёре и форма добавления архива
        // + медиатека проекта (v19, T-113-S0) — три инструмента media_add/list/remove
        // + правка полей чужой задачи (v20, T-160-S0) — инструмент update_task
        // + запуск задачи «авто ПО» (v21, T-155-S0) — действие-политика, инструмента нет:
        //   решение спрашивается перед запуском задания исполнителя «авто ПО»
        // + проверка и установка обновления приложения (v22, T-208) — два действия расписания,
        //   инструментов у них нет: их ставят флажки автообновления в настройках
        // + ПОИСК ПО ОПЫТУ (v23, T-268-S0) — инструмент search_experience: без записи
        //   справочника правила безопасности его не закрывали бы вовсе (наука 2d3af8da)
        // + РАЗГОВОР АГЕНТОВ (v24, T-280-S0) — send_task_message и wait_task_reply (T-185):
        //   записи у них не было с самого появления, поэтому переписка родственных задач
        //   ничем не закрывалась, а описание инструмента приходило агенту пустым
        // + ВЕТВЛЕНИЕ И ЦИКЛ (v25, T-300-S0) — set_condition_result, set_loop_result,
        //   create_tasks_from_template, stop_hierarchy: решение условия, исход проверки
        //   цикла, задачи ветви из узла шаблона, остановка иерархии агентом
        Assert.Equal(46, ru.Count);
        Assert.Equal(46, en.Count);
        Assert.Null(ru.Single(a => a.Code == "AI2P.Archives.AutoRun").ToolName);
        // действие заданий-соседей есть на обоих языках и привязано к инструменту
        Assert.Equal("get_sibling_tasks", ru.Single(a => a.Code == "AI2P.Tasks.GetSiblings").ToolName);
        Assert.Equal("get_sibling_tasks", en.Single(a => a.Code == "AI2P.Tasks.GetSiblings").ToolName);
        Assert.All(ru, a => Assert.False(a.IsCustom));
        Assert.All(ru, a => Assert.False(a.PromptModified));

        var readRu = ru.Single(a => a.Code == "AI2P.Files.Read");
        var readEn = en.Single(a => a.Code == "AI2P.Files.Read");
        Assert.Equal("read_file", readRu.ToolName);
        Assert.Contains("Прочитать", readRu.Prompt);
        Assert.Contains("Read", readEn.Prompt);
        // сортировка по коду — иерархия видна
        Assert.True(ru.FindIndex(a => a.Code == "AI2P.Chat.AskQuestion")
                    < ru.FindIndex(a => a.Code == "AI2P.Files.List"));
    }

    [Fact]
    public void Builtin_Prompt_Edit_Is_Stored_Separately_And_Reset_Restores_Default()
    {
        var action = _f.Actions.List("ru").Single(a => a.Code == "AI2P.Tasks.GetParent");
        var defaultPrompt = action.Prompt;

        // правка промпта: PromptModified, значение по умолчанию не теряется
        var edited = _f.Actions.Update(action.Id,
            new ActionSaveDto { Code = action.Code, Prompt = "Мой промпт про родителя" }, "ru");
        Assert.True(edited.PromptModified);
        Assert.Equal("Мой промпт про родителя", edited.Prompt);
        Assert.Equal(defaultPrompt, edited.DefaultPrompt);
        // другой язык не затронут
        Assert.False(_f.Actions.List("en").Single(a => a.Id == action.Id).PromptModified);

        // сохранение промпта, равного значению по умолчанию, снимает правку
        var same = _f.Actions.Update(action.Id,
            new ActionSaveDto { Code = action.Code, Prompt = defaultPrompt }, "ru");
        Assert.False(same.PromptModified);

        // откат кнопкой
        _f.Actions.Update(action.Id, new ActionSaveDto { Code = action.Code, Prompt = "Ещё правка" }, "ru");
        var reset = _f.Actions.ResetPrompt(action.Id, "ru");
        Assert.False(reset.PromptModified);
        Assert.Equal(defaultPrompt, reset.Prompt);
    }

    [Fact]
    public void Custom_Action_Is_Created_With_Unique_Code_And_Editable()
    {
        var created = _f.Actions.Create(new ActionSaveDto
        {
            Code = "OS.Shell.Run",
            Title = "Запуск программы",
            Hint = "Задел: запуск внешней программы",
            Prompt = "Запусти программу…",
        }, "ru");
        Assert.True(created.IsCustom);
        Assert.Equal("OS.Shell.Run", created.Code);
        // код уникален
        Assert.Throws<ArgumentException>(() =>
            _f.Actions.Create(new ActionSaveDto { Code = "os.shell.run", Prompt = "x" }, "ru"));
        // недостающий язык падает на введённый текст
        Assert.Equal("Запуск программы", _f.Actions.List("en").Single(a => a.Id == created.Id).Title);

        // у кастомного редактируется всё, включая код
        var updated = _f.Actions.Update(created.Id, new ActionSaveDto
        {
            Code = "OS.Shell.Start",
            Title = "Запуск",
            Hint = "…",
            Prompt = "Новый промпт",
        }, "ru");
        Assert.Equal("OS.Shell.Start", updated.Code);
        Assert.Equal("Новый промпт", updated.Prompt);
        Assert.False(updated.PromptModified); // у кастомного нет «значения по умолчанию»
    }

    [Fact]
    public void New_Language_Is_Just_A_Translation_File()
    {
        // отдельный каталог сида: копия en/ru из дистрибутива + перевод de на одно действие
        var seedDir = Path.Combine(_f.Dir, "seed-i18n");
        Directory.CreateDirectory(seedDir);
        var source = Path.Combine(AppContext.BaseDirectory, "i18n");
        foreach (var file in Directory.EnumerateFiles(source, "ActionCatalogService_*.json"))
        {
            File.Copy(file, Path.Combine(seedDir, Path.GetFileName(file)));
        }
        File.WriteAllText(Path.Combine(seedDir, "ActionCatalogService_de.json"), """
            {
              "seedVersion": 2,
              "actions": [
                {
                  "id": "ac710000-0000-4000-9000-000000000003",
                  "code": "AI2P.Files.Read",
                  "tool": "read_file",
                  "title": "Projektdatei lesen",
                  "hint": "Liest eine Textdatei des Projekts.",
                  "prompt": "Lies eine Textdatei des Projekts über ihren relativen Pfad."
                }
              ]
            }
            """);

        var actions = new AI2P.Storage.Services.ActionCatalogService(_f.Db, seedDir);
        actions.Seed();

        var de = actions.List("de");
        Assert.Equal(46, de.Count); // все действия видны и на новом языке
        Assert.Equal("Projektdatei lesen", de.Single(a => a.Code == "AI2P.Files.Read").Title);
        // непереведённые — фолбэк на en
        Assert.Contains("List files", de.Single(a => a.Code == "AI2P.Files.List").Prompt);
        Assert.Contains("Lies eine Textdatei", actions.PromptByTool("read_file", "de"));
    }

    [Fact]
    public void Effective_Prompt_Is_Substituted_Into_Tool_Specs()
    {
        var action = _f.Actions.List("ru").Single(a => a.Code == "AI2P.Tasks.GetParent");
        _f.Actions.Update(action.Id,
            new ActionSaveDto { Code = action.Code, Prompt = "ПРОМПТ ИЗ СПРАВОЧНИКА" }, "ru");

        var task = _f.Tasks.Create(new TaskItem { Title = "Задача" }, "т", "", null);
        // тот же резолвер, что в AiConnectorBase (todo24): промпт из справочника по языку
        var toolset = new AgentToolset(files: null,
            new TaskToolset(_f.Tasks, _f.Chat, _f.Executors, _f.Files, task),
            spec => _f.Actions.PromptByTool(spec.Name, "ru") is { } prompt
                ? spec with { Description = prompt }
                : spec);

        var parentSpec = toolset.Specs.Single(s => s.Name == "get_parent_task");
        Assert.Equal("ПРОМПТ ИЗ СПРАВОЧНИКА", parentSpec.Description);
        // остальные — сидовые тексты справочника (ru)
        var chatSpec = toolset.Specs.Single(s => s.Name == "get_task_chat");
        Assert.Contains("Прочитать чат задания", chatSpec.Description);
        // язык en — свой текст
        Assert.Contains("Read the task chat", _f.Actions.PromptByTool("get_task_chat", "en"));
        // неизвестного инструмента в справочнике нет — null (описание останется из кода)
        Assert.Null(_f.Actions.PromptByTool("no_such_tool", "ru"));
    }
}

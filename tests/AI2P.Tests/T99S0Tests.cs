using AI2P.Core;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-99-S0: ФОРМА ОБЪЕКТА — ЗАКЛАДКА, ДВЕ ССЫЛКИ, ДВА «ЧТО УЙДЁТ В МОДЕЛЬ».
///
/// Что проверяется:
/// <list type="number">
/// <item>ЧТО УЙДЁТ В МОДЕЛЬ ПРИ ОБУЧЕНИИ (<c>ObjectService.TrainPrompt</c>) — у адаптера
/// это кадры текущего датасета с подписями, у остальных объектов пусто;</item>
/// <item>РАЗМЕТКА закладки объекта: шапка, тулбар, две внутренние вкладки, «внутреннего
/// списка» в оконной форме больше нет;</item>
/// <item>ССЫЛКИ: маркер <c>@obj:OBJ-N</c> и адрес <c>…/object/{id}</c> — в двух полях;</item>
/// <item>СЛОВАРИ: новые ключи есть во ВСЕХ языках (правило гл. 9).</item>
/// </list>
///
/// Чего здесь НЕТ: самого открытия закладки в браузере — это живая проверка по CDP.
/// </summary>
public sealed class T99S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private ObjectItem NewObject(string projectId, string name, string kind,
        string? parentId = null, string path = "", string passport = "") =>
        _f.Objects.Create(new ObjectItem
        {
            ProjectId = projectId,
            ParentId = parentId,
            Name = name,
            Type = kind,
            PathOrUrl = path,
            Description = passport,
        }, null);

    // ---------- 1. что уйдёт в модель при обучении ----------

    /// <summary>У обычного объекта обучения нет вовсе — второе поле формы пустое, и она
    /// показывает одно, как раньше.</summary>
    [Fact]
    public void A_Plain_Object_Has_Nothing_To_Train()
    {
        var project = _f.Projects.Create("Ролик", null, null, null).Id;
        var hero = NewObject(project, "Вася", ObjectKinds.Character, passport: "рыжий");

        Assert.Equal("", _f.Objects.TrainPrompt(hero.Id));
    }

    /// <summary>
    /// У АДАПТЕРА это кадры ТЕКУЩЕГО датасета вместе с подписями — то, что получит тренер.
    /// Паспорт сюда не попадает: он уходит в промпт генерации, и это разные данные.
    /// </summary>
    [Fact]
    public void The_Adapter_Shows_The_Frames_Of_Its_Dataset()
    {
        var project = _f.Projects.Create("Ролик", null, null, null).Id;
        var lora = NewObject(project, "Вася LoRA", ObjectKinds.Lora, passport: "паспорт адаптера");
        var dataset = _f.Objects.CreateDataset(lora.Id, "датасет 512", new LoraDatasetLimits(), null);
        NewObject(project, "кадр 1", ObjectKinds.Image, dataset.Id, "refs/1.png", "рыжий в профиль");
        NewObject(project, "кадр 2", ObjectKinds.Image, dataset.Id, "refs/2.png", "рыжий анфас");

        var text = _f.Objects.TrainPrompt(lora.Id);

        Assert.Contains("датасет 512", text);
        Assert.Contains("refs/1.png", text);
        Assert.Contains("рыжий в профиль", text);
        Assert.Contains("refs/2.png", text);
        Assert.Contains("рыжий анфас", text);
        // паспорт объекта — это «при использовании», а не «при обучении»
        Assert.DoesNotContain("паспорт адаптера", text);
    }

    /// <summary>Кадр без подписи виден пустой строкой: увидеть это надо ДО того, как
    /// обучение начнёт считать часами.</summary>
    [Fact]
    public void A_Frame_Without_A_Caption_Is_Still_Listed()
    {
        var project = _f.Projects.Create("Ролик", null, null, null).Id;
        var lora = NewObject(project, "Вася LoRA", ObjectKinds.Lora);
        var dataset = _f.Objects.CreateDataset(lora.Id, "датасет", new LoraDatasetLimits(), null);
        NewObject(project, "кадр", ObjectKinds.Image, dataset.Id, "refs/nocaption.png");

        Assert.Contains("refs/nocaption.png", _f.Objects.TrainPrompt(lora.Id));
    }

    // ---------- 2. разметка ----------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? "";
    }

    private static string Ui(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "AI2P.UI", "Components", file));

    /// <summary>Закладка объекта устроена как карточка задачи: шапка с номером и названием,
    /// тулбар иконками и две внутренние вкладки.</summary>
    [Fact]
    public void The_Object_Card_Has_A_Toolbar_And_Two_Tabs()
    {
        var markup = Ui("ObjectCardView.razor");

        foreach (var probe in new[]
                 {
                     "data-object-card-edit", "data-object-card-link", "data-object-card-prompt",
                     "data-object-card-delete", "data-object-card-refresh",
                     "data-object-card-lora-setup",
                     "projects.tab.main", "objects.card.children",
                     "data-object-card-passport", "data-object-child-edit", "data-object-child-delete",
                     "State.OpenObject",
                 })
        {
            Assert.Contains(probe, markup);
        }
    }

    /// <summary>Закладка живёт в общем переключателе представлений и открывается по адресу
    /// <c>/object/{id}</c> — как задача.</summary>
    [Fact]
    public void The_Card_Is_Wired_To_The_Tab_Bar_And_To_The_Url()
    {
        var home = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AI2P.UI", "Pages", "Home.razor"));

        Assert.Contains("@page \"/object/{DeepLinkObjectId}\"", home);
        Assert.Contains("<ObjectCardView ObjectId=", home);
        Assert.Contains("tab.object", home);
        // заголовок закладки считается ТЕМ ЖЕ параметром, что у задачи и проекта
        Assert.Contains("UiState.ViewTask or UiState.ViewProject or UiState.ViewObject", home);
    }

    /// <summary>Щелчок по строке списка открывает ЗАКЛАДКУ, а правка осталась кнопкой строки:
    /// иначе из списка нельзя было бы поправить объект в одно движение.</summary>
    [Fact]
    public void The_List_Opens_The_Card_And_Keeps_The_Pencil()
    {
        var markup = Ui("ObjectsView.razor");

        Assert.Contains("State.OpenObject", markup);
        Assert.Contains("data-object-edit=", markup);
        Assert.Contains("data-object-delete=", markup);
    }

    /// <summary>Из ОКОННОЙ формы внутренний список убран: читают его в закладке, где у
    /// строки есть превью и кнопки.</summary>
    [Fact]
    public void The_Dialog_Has_No_Inner_List_Anymore()
    {
        var markup = Ui("ObjectDialog.razor");

        Assert.DoesNotContain("data-object-children-list", markup);
        // а поля формы на месте
        Assert.Contains("data-object-passport", markup);
        Assert.Contains("data-object-parent", markup);
    }

    /// <summary>ССЫЛОК ДВЕ и они в двух разных полях: маркер для описания задачи и адрес
    /// закладки для людей. Одно поле на обе не годится — их копируют в разные места.</summary>
    [Fact]
    public void There_Are_Two_Link_Fields()
    {
        var dialog = Ui("ObjectLinkDialog.razor");
        var state = File.ReadAllText(Path.Combine(RepoRoot(), "src", "AI2P.UI", "Services", "UiState.cs"));

        Assert.Contains("data-object-link-marker", dialog);
        Assert.Contains("data-object-link-url", dialog);
        Assert.Contains("link.external", dialog);
        Assert.Contains("/object/{objectId}", state);
        Assert.Contains("public string ObjectLocalUrl", state);
    }

    /// <summary>«Что уйдёт в модель» у адаптера — ДВА поля: при использовании и при обучении.</summary>
    [Fact]
    public void The_Prompt_Dialog_Has_Both_Fields()
    {
        var dialog = Ui("ObjectPromptDialog.razor");

        Assert.Contains("data-object-prompt-use", dialog);
        Assert.Contains("data-object-prompt-train", dialog);
        Assert.Contains("objects.prompt.use", dialog);
        Assert.Contains("objects.prompt.train", dialog);
    }

    // ---------- 3. словари ----------

    /// <summary>Новые тексты есть во ВСЕХ языках: состав ключей обязан сходиться, иначе
    /// на чужом языке человек увидит сам ключ (гл. 9).</summary>
    [Fact]
    public void The_Texts_Are_In_Every_Dictionary()
    {
        foreach (var lang in Loc.Languages)
        {
            foreach (var key in new[]
                     {
                         "tab.object", "objects.card.children", "objects.notFound",
                         "objects.prompt.use", "objects.prompt.train", "objects.prompt.train.hint",
                     })
            {
                var text = Loc.In(lang, key);
                Assert.False(string.IsNullOrWhiteSpace(text), $"{lang}: {key}");
                Assert.NotEqual(key, text);
            }
        }
    }
}

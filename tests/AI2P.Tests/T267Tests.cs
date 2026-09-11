using AI2P.Core;
using AI2P.Core.Entities;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-267 (версия 1.94): КНОПКА «ССЫЛКА НА ОБЪЕКТ» В РЕДАКТОРЕ MARKDOWN.
///
/// До этого ссылку <c>@obj:OBJ-3</c> человек либо писал руками, либо шёл за ней в список
/// объектов проекта (кнопка строки, T-259) и копировал через буфер обмена. Теперь она
/// ставится прямо из редактора: кнопка «@» открывает список АКТИВНЫХ объектов проекта
/// с поиском по заголовку, выбранный встаёт в позицию курсора.
///
/// Что проверяется:
/// <list type="number">
/// <item>ФОРМА ссылки — настройка ПРОЕКТА (<c>objRefFormat</c>): номер либо название;
/// незнакомое и испорченное значение это умолчание, а не ошибка;</item>
/// <item>ЧТЕНИЕ ссылок от настройки НЕ ЗАВИСИТ — обе формы понимаются всегда, иначе смена
/// настройки молча ломала бы уже написанные описания;</item>
/// <item>ХРАНЕНИЕ — настройка едет в <c>projects.settings_json</c> и переживает правку
/// проекта (сохранение переписывает настройки целиком, поэтому обе формы проекта обязаны
/// её знать);</item>
/// <item>РАЗМЕТКА — кнопка в панели редактора, окно выбора и поле настройки в обеих формах
/// проекта (проверка по файлу, приём T-209/T-259; поведение в браузере — живой проверкой).</item>
/// </list>
/// </summary>
public sealed class T267Tests
{
    // ---------- 1. форма ссылки как настройка проекта ----------

    /// <summary>Умолчание — номер: так ссылку ставила кнопка списка объектов до T-267,
    /// и у всех уже заведённых проектов настройки нет вовсе.</summary>
    [Fact]
    public void Without_A_Setting_The_Format_Is_The_Number()
    {
        Assert.Equal(ObjectRefFormats.Code, ObjectRefFormats.Default);
        Assert.Equal(ObjectRefFormats.Code, ProjectSettings.ObjRefFormat(null));
        Assert.Equal(ObjectRefFormats.Code, ProjectSettings.ObjRefFormat(""));
        Assert.Equal(ObjectRefFormats.Code, ProjectSettings.ObjRefFormat("{}"));
        Assert.Equal(ObjectRefFormats.Code,
            ProjectSettings.ObjRefFormat("""{"defaultTeamId":null,"qualityBias":0.5}"""));
    }

    /// <summary>Заданная настройка читается — обе формы.</summary>
    [Fact]
    public void A_Set_Format_Is_Read()
    {
        Assert.Equal(ObjectRefFormats.Name,
            ProjectSettings.ObjRefFormat("""{"qualityBias":0.5,"objRefFormat":"name"}"""));
        Assert.Equal(ObjectRefFormats.Code,
            ProjectSettings.ObjRefFormat("""{"objRefFormat":"code"}"""));
    }

    /// <summary>Настройки едут между серверами кластера и пишутся разными версиями
    /// приложения, поэтому испорченный JSON, чужое значение и не та форма записи — это
    /// повод взять умолчание, а не упасть посреди отрисовки формы.</summary>
    [Fact]
    public void A_Broken_Or_Unknown_Setting_Is_The_Default()
    {
        Assert.Equal(ObjectRefFormats.Code, ProjectSettings.ObjRefFormat("не json вовсе"));
        Assert.Equal(ObjectRefFormats.Code, ProjectSettings.ObjRefFormat("[1,2,3]"));
        Assert.Equal(ObjectRefFormats.Code, ProjectSettings.ObjRefFormat("""{"objRefFormat":"титул"}"""));
        Assert.Equal(ObjectRefFormats.Code, ProjectSettings.ObjRefFormat("""{"objRefFormat":7}"""));
        Assert.Equal(ObjectRefFormats.Code, ProjectSettings.ObjRefFormat("""{"objRefFormat":null}"""));
        // регистр значения не важен: настройку мог записать кто угодно
        Assert.Equal(ObjectRefFormats.Name, ProjectSettings.ObjRefFormat("""{"objRefFormat":"Name"}"""));
    }

    // ---------- 2. что именно вставляет кнопка ----------

    /// <summary>Кнопка ставит ту форму, которая задана проектом.</summary>
    [Fact]
    public void The_Button_Inserts_The_Form_The_Project_Asked_For()
    {
        Assert.Equal("@obj:OBJ-3", ObjectRefs.MarkerFor(ObjectRefFormats.Code, "OBJ-3", "Герой Вася"));
        Assert.Equal("@obj:[Герой Вася]", ObjectRefs.MarkerFor(ObjectRefFormats.Name, "OBJ-3", "Герой Вася"));
        // незаданная и незнакомая настройка — номер
        Assert.Equal("@obj:OBJ-3", ObjectRefs.MarkerFor(null, "OBJ-3", "Герой Вася"));
        Assert.Equal("@obj:OBJ-3", ObjectRefs.MarkerFor("вымысел", "OBJ-3", "Герой Вася"));
    }

    /// <summary>Безымянный объект ссылкой по названию не адресуется: <c>@obj:[]</c> не нашло
    /// бы ничего, поэтому у такого берётся номер. Пробелы по краям названия срезаются —
    /// иначе ссылка отличалась бы от самого названия невидимым знаком.</summary>
    [Fact]
    public void An_Object_Without_A_Name_Is_Referenced_By_Its_Number()
    {
        Assert.Equal("@obj:OBJ-7", ObjectRefs.MarkerFor(ObjectRefFormats.Name, "OBJ-7", ""));
        Assert.Equal("@obj:OBJ-7", ObjectRefs.MarkerFor(ObjectRefFormats.Name, "OBJ-7", "   "));
        Assert.Equal("@obj:OBJ-7", ObjectRefs.MarkerFor(ObjectRefFormats.Name, "OBJ-7", null));
        Assert.Equal("@obj:[Зонт]", ObjectRefs.MarkerFor(ObjectRefFormats.Name, "OBJ-7", "  Зонт  "));
    }

    /// <summary>ГЛАВНОЕ ПРАВИЛО: настройка решает только, какую форму ПИШУТ. Обе формы
    /// читаются всегда — иначе смена настройки молча сломала бы описания, написанные раньше.</summary>
    [Fact]
    public void Both_Forms_Are_Still_Read_Whatever_The_Setting_Is()
    {
        var byCode = ObjectRefs.MarkerFor(ObjectRefFormats.Code, "OBJ-3", "Герой Вася");
        var byName = ObjectRefs.MarkerFor(ObjectRefFormats.Name, "OBJ-3", "Герой Вася");
        var text = $"Крупный план {byCode}, за спиной {byName}";

        Assert.Equal(["OBJ-3", "Герой Вася"], ObjectRefs.Find(text));

        var card = new ObjectRefs.ObjectCard("OBJ-3", "Герой Вася", "персонаж", "рыжий, в плаще", []);
        var expanded = ObjectRefs.Expand(text, _ => card);

        Assert.DoesNotContain("@obj:", expanded);
        Assert.Equal(2, expanded.Split("рыжий, в плаще").Length - 1);
    }

    /// <summary>Ссылка стоит внутри предложения не реже, чем отдельной строкой (T-258):
    /// вставленная кнопкой, она обязана кончаться там, где кончается номер.</summary>
    [Fact]
    public void An_Inserted_Reference_Ends_Where_The_Number_Ends()
    {
        var marker = ObjectRefs.MarkerFor(ObjectRefFormats.Code, "OBJ-12", "Ночная улица");

        Assert.Equal(["OBJ-12"], ObjectRefs.Find($"Крупный план: {marker}, дождь, неон"));
    }

    // ---------- 3. хранение настройки ----------

    /// <summary>Настройка привязана к ПРОЕКТУ: у разных проектов она разная, и новый
    /// проект заводится сразу с ней (форма нового проекта → <c>ProjectCreateDto</c>).</summary>
    [Fact]
    public void The_Setting_Belongs_To_The_Project()
    {
        using var f = new StorageFixture();

        var byCode = f.Projects.Create("Ролик про кота", null, null, null);
        var byName = f.Projects.Create("Ролик про пса", null, null, null,
            objRefFormat: ObjectRefFormats.Name);

        Assert.Equal(ObjectRefFormats.Code, ProjectSettings.ObjRefFormat(byCode.SettingsJson));
        Assert.Equal(ObjectRefFormats.Name, ProjectSettings.ObjRefFormat(byName.SettingsJson));
        // и то же самое после перечитывания из базы
        Assert.Equal(ObjectRefFormats.Name,
            ProjectSettings.ObjRefFormat(f.Projects.Get(byName.Id)!.SettingsJson));
    }

    /// <summary>Незнакомое значение до базы не доезжает: настройку правит и API, и другой
    /// сервер кластера, а форма выбора знает ровно два значения.</summary>
    [Fact]
    public void An_Unknown_Format_Is_Not_Stored()
    {
        using var f = new StorageFixture();

        var project = f.Projects.Create("Ролик", null, null, null, objRefFormat: "загогулина");

        Assert.Equal(ObjectRefFormats.Code, ProjectSettings.ObjRefFormat(project.SettingsJson));
    }

    /// <summary>Правка проекта переписывает настройки ЦЕЛИКОМ (форма собирает их заново),
    /// поэтому проверяем, что вместе с командой и ценой↔качеством доезжает и формат.</summary>
    [Fact]
    public void Saving_The_Project_Keeps_The_Format()
    {
        using var f = new StorageFixture();
        var project = f.Projects.Create("Ролик", null, null, null, objRefFormat: ObjectRefFormats.Name);

        project.Name = "Ролик про кота";
        project.SettingsJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            defaultTeamId = (string?)null,
            qualityBias = 0.75,
            objRefFormat = ObjectRefFormats.Name,
        });
        f.Projects.Update(project, null);

        var saved = f.Projects.Get(project.Id)!;
        Assert.Equal(ObjectRefFormats.Name, ProjectSettings.ObjRefFormat(saved.SettingsJson));
        Assert.Equal(0.75, AI2P.Storage.Services.ProjectService.QualityBias(saved), 3);
    }

    // ---------- 4. разметка и словари ----------

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

    /// <summary>Кнопка стоит в панели редактора и работает в ОБОИХ его режимах — в поле
    /// текста ссылка вставляется в позицию курсора, в редактируемом предпросмотре — тем же
    /// действием, но текстом (ссылка разметкой не является).</summary>
    [Fact]
    public void The_Editor_Has_The_Object_Reference_Button()
    {
        var markup = Ui("MarkdownEditor.razor");

        Assert.Contains("data-md-objref=\"1\"", markup);
        Assert.Contains("md.objectRef", markup);
        Assert.Contains("ObjectPickerDialog", markup);
        Assert.Contains("ObjectRefs.MarkerFor", markup);
        Assert.Contains("ProjectSettings.ObjRefFormat", markup);
        // предпросмотр: текст ссылки экранируется — в названии объекта бывает «<» и «&»
        Assert.Contains("HtmlEncode(marker)", markup);
    }

    /// <summary>Кнопки нет там, где проекта нет вовсе: список объектов брать неоткуда.</summary>
    [Fact]
    public void Without_A_Project_There_Is_No_Button()
    {
        var markup = Ui("MarkdownEditor.razor");

        Assert.Contains("@if (HasProject)", markup);
        Assert.Contains("private bool HasProject => ProjectId is { Length: > 0 };", markup);
    }

    /// <summary>Окно выбора: поиск по заголовку и список ТОЛЬКО АКТИВНЫХ объектов
    /// (выключенным объектом больше не пользуются — предлагать его незачем).</summary>
    [Fact]
    public void The_Picker_Searches_And_Shows_Only_Active_Objects()
    {
        var markup = Ui("ObjectPickerDialog.razor");

        Assert.Contains("data-objpick-search=\"1\"", markup);
        Assert.Contains("data-objpick-row=", markup);
        Assert.Contains("item.IsActive", markup);
        Assert.Contains("item.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)", markup);
        // пустой список объектов и «ничего не нашлось» — разные сообщения
        Assert.Contains("md.objectRef.empty", markup);
        Assert.Contains("md.objectRef.notFound", markup);
    }

    /// <summary>Настройка стоит в ОБЕИХ формах проекта: форма новой и вкладка «основное»
    /// карточки. Каждая из них переписывает настройки целиком — забыть формат в одной
    /// значит терять его при сохранении из неё.</summary>
    [Fact]
    public void Both_Project_Forms_Know_The_Setting()
    {
        foreach (var file in new[] { "ProjectDialog.razor", "ProjectCardView.razor" })
        {
            var markup = Ui(file);

            Assert.Contains("data-project-objref=\"1\"", markup);
            Assert.Contains("projects.objRefFormat", markup);
            Assert.Contains("ProjectSettings.ObjRefFormat", markup);
            Assert.Contains("objRefFormat = ObjectRefFormats.Normalize(_objRefFormat)", markup);
        }
    }

    /// <summary>Тексты — в ОБОИХ словарях (правило гл. 9): иначе английский интерфейс
    /// покажет сам ключ.</summary>
    [Fact]
    public void The_Texts_Are_In_Both_Dictionaries()
    {
        var ru = File.ReadAllText(Path.Combine(RepoRoot(), "i18n", "ru.json"));
        var en = File.ReadAllText(Path.Combine(RepoRoot(), "i18n", "en.json"));

        foreach (var key in new[]
                 {
                     "md.objectRef", "md.objectRef.empty", "md.objectRef.notFound",
                     "projects.objRefFormat", "projects.objRefFormat.hint",
                     "projects.objRefFormat.code", "projects.objRefFormat.name",
                 })
        {
            Assert.Contains($"\"{key}\"", ru);
            Assert.Contains($"\"{key}\"", en);
        }
    }

    /// <summary>Подписи вариантов показывают САМУ ФОРМУ: выбирая настройку, человек видит,
    /// что именно окажется в тексте, и не идёт за этим в документацию.</summary>
    [Fact]
    public void The_Choices_Show_What_Will_Be_Written()
    {
        Assert.Contains("@obj:OBJ-3", Loc.In("ru", "projects.objRefFormat.code"));
        Assert.Contains("@obj:[", Loc.In("ru", "projects.objRefFormat.name"));
        Assert.Contains("@obj:OBJ-3", Loc.In("en", "projects.objRefFormat.code"));
        Assert.Contains("@obj:[", Loc.In("en", "projects.objRefFormat.name"));
    }
}

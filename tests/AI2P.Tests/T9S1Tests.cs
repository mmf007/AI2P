using AI2P.UI.Services;
using MudBlazor;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-9-S1. Форма изменения задачи: ПРОМАХ мимо окна не должен закрывать форму.
///
/// Умолчание MudBlazor — щелчок по затемнению вокруг окна закрывает его ОТМЕНОЙ
/// (<c>DialogOptions.BackdropClick</c>), поэтому промах мимо формы (а ещё чаще — выделение
/// текста в описании мышью с отпусканием кнопки за краем окна) выбрасывал всё введённое
/// без переспроса. Настройки окон-форм собраны в <see cref="FormDialogs"/>, и форма задачи
/// (создание, правка, новый шаблон) открывается только через них.
///
/// Здесь проверяется механизм: сами настройки и то, что ни одно место открытия формы не
/// осталось со старыми умолчаниями. Поведение в браузере проверяется живой проверкой
/// (test/t9s1/ui9s1.py, настоящий Chrome по CDP).
/// </summary>
public sealed class T9S1Tests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; dir is not null && depth < 10; depth++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AI2P.sln")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("не найден каталог AI2P_app (по AI2P.sln)");
    }

    private static string UiRoot() => Path.Combine(RepoRoot(), "src", "AI2P.UI");

    /// <summary>Настройки формы запрещают закрытие щелчком мимо окна.</summary>
    [Fact]
    public void Form_Options_Do_Not_Close_On_Backdrop_Click()
    {
        var options = FormDialogs.Options(MaxWidth.Medium);

        Assert.Equal(false, options.BackdropClick);
        // ширина окна и растяжение остались прежними — правка не про внешний вид
        Assert.Equal(MaxWidth.Medium, options.MaxWidth);
        Assert.Equal(true, options.FullWidth);
        Assert.Equal(MaxWidth.Small, FormDialogs.Options(MaxWidth.Small).MaxWidth);
    }

    /// <summary>
    /// Проверка того, что правка вообще нужна: у настроек ПО УМОЛЧАНИЮ запрета нет
    /// (null — «как задано у провайдера окон», то есть закрывать).
    /// </summary>
    [Fact]
    public void By_Default_MudBlazor_Closes_Dialog_On_Backdrop_Click()
    {
        Assert.NotEqual(false, new DialogOptions().BackdropClick);
    }

    /// <summary>
    /// Форма задачи открывается в семи местах (карточка задачи — правка, правка подзадачи,
    /// новая подзадача; список задач — новая задача и задача из импортированной карточки;
    /// список шаблонов — новый шаблон; «Диаграммы в работе» — правка, T-328-S0). Каждое обязано брать настройки
    /// <see cref="FormDialogs"/>, иначе промах снова начнёт закрывать форму — и заметить
    /// это можно будет только руками.
    /// </summary>
    [Fact]
    public void Every_Task_Form_Is_Opened_With_Form_Options()
    {
        var places = 0;
        foreach (var file in Directory.GetFiles(UiRoot(), "*.razor", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("ShowAsync<TaskDialog>", StringComparison.Ordinal))
                {
                    continue;
                }
                places++;
                // настройки стоят последним доводом вызова — на той же или следующей строке
                var call = lines[i] + "\n" + (i + 1 < lines.Length ? lines[i + 1] : "");
                Assert.True(call.Contains("FormDialogs.Options(", StringComparison.Ordinal),
                    $"{Path.GetFileName(file)}:{i + 1} — форма задачи открывается мимо "
                    + $"FormDialogs.Options: «{call.Trim()}»");
            }
        }

        Assert.Equal(7, places);
    }
}

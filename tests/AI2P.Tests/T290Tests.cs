using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-290. Заявка на смену дирижёра, поданная на РЯДОВОМ сервере, не появлялась у дирижёра.
///
/// Жалоба: «на S1 подана заявка на смену дирижёра, там она видна в списке серверов, а после
/// сеанса репликации на S0 (сегодняшнем дирижёре) она не появилась».
///
/// Строка <c>conductor_requests</c> едет нормально — это доказано и тестом
/// <c>T21S1Tests.The_Request_Travels_By_Replication_Like_Any_Other_Row</c>, и живой проверкой
/// на двух настоящих серверах (<c>test/t290/live290.py</c>, 29/29: заявка приезжает и когда
/// сеанс ведёт рядовой сервер, и когда его ведёт дирижёр). Беда была на ЭКРАНЕ: вкладка
/// «Серверы» перечитывает опросом состояние репликации, список серверов и заявки на
/// ПОДКЛЮЧЕНИЕ (T-141), а список заявок на смену дирижёра читался только при открытии
/// вкладки. Приехавшая заявка показывалась лишь после закрытия и повторного открытия
/// настроек — со стороны это и есть «репликация её не привезла».
///
/// Здесь сторож на сам опрос: поведение в браузере проверяется живой проверкой
/// (<c>test/t290/ui290.py</c>, настоящий Chrome по CDP).
/// </summary>
public sealed class T290Tests
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

    private static string SettingsView() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "src", "AI2P.UI", "Components",
            "SettingsView.razor"));

    /// <summary>Тело метода опроса: от его объявления до следующего члена компонента.</summary>
    private static string PollingBody()
    {
        var text = SettingsView();
        var start = text.IndexOf("private void StartReplPolling()", StringComparison.Ordinal);
        Assert.True(start > 0, "в SettingsView.razor нет метода StartReplPolling");
        var end = text.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
        var stop = text.IndexOf("\n    /// <summary>", start + 1, StringComparison.Ordinal);
        if (stop > 0 && (end < 0 || stop < end))
        {
            end = stop;
        }
        return end > 0 ? text[start..end] : text[start..];
    }

    /// <summary>
    /// Опрос вкладки «Серверы» обязан перечитывать И заявки на смену дирижёра: они приходят
    /// с другого сервера обычной репликацией в любой момент, ровно как заявки на подключение.
    /// </summary>
    [Fact]
    public void The_Servers_Tab_Polls_The_Conductor_Requests_Too()
    {
        var body = PollingBody();

        Assert.Contains("GetConductorRequestsAsync", body, StringComparison.Ordinal);
        // и не в ущерб тому, что перечитывалось раньше (T-141)
        Assert.Contains("GetServersAsync", body, StringComparison.Ordinal);
        Assert.Contains("GetServerRequestsAsync", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Спрашивать заявки НЕ У КОГО, пока сервер не в организации: они живут в её базе,
    /// а на сервере, ждущем решения по своей заявке на подключение, организации нет вовсе.
    /// Поэтому и при открытии вкладки, и в опросе список берётся под этой проверкой.
    /// </summary>
    [Fact]
    public void Conductor_Requests_Are_Not_Asked_Without_An_Organization()
    {
        var text = SettingsView();
        var places = 0;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("GetConductorRequestsAsync", StringComparison.Ordinal))
            {
                continue;
            }
            places++;
            // защита стоит либо в самой строке (State.NoOrg ? [] : …), либо в условии
            // неподалёку над ней (между ними бывает пояснение)
            var from = Math.Max(0, i - 8);
            var around = string.Join("\n", lines[from..(i + 1)]);
            Assert.True(around.Contains("NoOrg", StringComparison.Ordinal),
                $"SettingsView.razor:{i + 1} — заявки на смену дирижёра спрашиваются без "
                + $"проверки организации: «{lines[i].Trim()}»");
        }

        // два места: чтение при открытии вкладки и перечитывание опросом
        Assert.Equal(2, places);
    }
}

using AI2P.Core;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-50-S0, ПОВТОРНЫЙ ЗАХОД (выпуск 1.107): «репликация не идёт».
///
/// Жалоба заказчика после 1.106: ошибка у сервера осталась, обмена нет, а задачи, приехавшие
/// на второй сервер, имеют ПУСТОЕ описание. Причина нашлась в журнале рабочего сервера:
/// сеанс падал на файле <c>T-295/description.md</c> ошибкой «файл не найден» в
/// <c>FileReplicationService.PullFileAsync</c>. Файл был НУЛЕВОЙ ДЛИНЫ — цикл докачки для
/// него не выполняется ни разу, временного <c>.ai2p-part</c> никто не заводит, и завершающее
/// переименование падает. Дальше беда множилась: вид исключения (IOException) не значился
/// в закрытом перечне обработчика сеанса, поэтому сеанс не завершался вовсе — пара навсегда
/// оставалась в состоянии «идёт сеанс» с пустым текстом ошибки, а старая жалоба на связь
/// в записи сервера не снималась и не заменялась.
///
/// Сам перенос файла проверяется живьём на двух серверах (<c>test/t50s0/live107.py</c>):
/// сеанс репликации ходит по сети, и модульным тестом его не поднять — тестов на него в
/// наборе нет вовсе. Здесь стоят сторожа, которые дёшево ловят ОТКАТ трёх правок, и
/// проверка новых текстов на обоих языках.
/// </summary>
public sealed class T50S0bTests
{
    /// <summary>Корень репозитория: над каталогом AI2P_app (в нём лежит AI2P.sln).</summary>
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

    private static string Source(string name) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "AI2P_app", "src", "AI2P.Server", "Org", name));

    /// <summary>Пустой файл: временный <c>.ai2p-part</c> заводится ДО переименования, иначе
    /// переносить нечего и <c>File.Move</c> падает «файл не найден».</summary>
    [Fact]
    public void An_Empty_File_Gets_Its_Part_File()
    {
        var code = Source("FileReplicationService.cs");
        var pull = code[code.IndexOf("private async Task PullFileAsync", StringComparison.Ordinal)..];
        pull = pull[..pull.IndexOf("private async Task PushFileAsync", StringComparison.Ordinal)];

        Assert.Contains("if (!File.Exists(part))", pull);
        Assert.Contains("File.WriteAllBytes(part, [])", pull);
        // и сам перенос на месте: сторож не должен зеленеть на выпотрошенном методе
        Assert.Contains("File.Move(part, full, overwrite: true)", pull);
    }

    /// <summary>Беда с ОДНИМ файлом не рвёт сеанс: шаг плана выполняется под защитой, отмена
    /// пробрасывается, а имя непереехавшего файла попадает в список неудач.</summary>
    [Fact]
    public void One_Broken_File_Does_Not_Break_The_Session()
    {
        var code = Source("FileReplicationService.cs");

        Assert.Contains("await ApplyStepAsync(", code);
        Assert.Contains("catch (OperationCanceledException)", code);
        Assert.Contains("failed.Add(step.Path)", code);
        // список неудач доходит до сеанса, а не тонет внутри
        Assert.Contains("public async Task<List<string>> RunAsync(", code);
    }

    /// <summary>Обработчик сеанса больше не перечисляет виды ошибок: ЛЮБАЯ беда обязана
    /// завершить сеанс, иначе пара застревает в состоянии «идёт сеанс» навсегда. Отмена по
    /// своему признаку — не ошибка, а таймаут запроса (тот же тип исключения) — ошибка.</summary>
    [Fact]
    public void Any_Failure_Finishes_The_Session()
    {
        var code = Source("ReplicationService.cs");

        Assert.Contains(
            "catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)",
            code);
        Assert.DoesNotContain("catch (Exception ex) when (ex is InvalidOperationException", code);
        // сеанс, дошедший до конца с непереехавшими файлами, считается состоявшимся по времени,
        // но красит пару: иначе один такой файл выглядел бы как «репликации не было никогда»
        Assert.Contains("reached: true", code);
        Assert.Contains("FilesFailedText(filesFailed)", code);
    }

    /// <summary>Новые тексты есть на обоих языках: словари обязаны сходиться посчётно.</summary>
    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void The_Text_About_Files_That_Did_Not_Transfer_Is_In_Both_Languages(string lang)
    {
        foreach (var key in new[] { "msg.replication.4", "msg.replication.5" })
        {
            var text = Loc.In(lang, key, 2, "a.md, b.md", 1);

            Assert.NotEqual(key, text);
            Assert.Contains("a.md", text);
        }
    }
}

using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Server;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-756-S0: ОБНОВЛЕНИЕ AI2P ИЗ ЛОКАЛЬНОГО ПАКЕТА по вызову агента (app_update_from_file).
///
/// Проверяется выбор пакета по имени (вид, система, архитектура, билд не ниже текущего),
/// отказ чужому пакету и главное — порядок: задание закрывается ДО запуска скрипта
/// и остановки сервера, а запись о начатом обновлении ложится на диск и при подъёме
/// превращается в «было X, стало Y» либо понятную ошибку.
///
/// Флаг <see cref="AppUpdate.Restarting"/> статический и останавливает запуск заданий
/// во всём процессе, поэтому класс идёт в своей коллекции без параллели.
/// </summary>
[Collection(T756S0Collection.Name)]
public sealed class T756S0Tests
{
    [Fact]
    public void Own_Package_Is_Picked_By_Name()
    {
        var (build, error) = AppUpdate.CheckLocal(
            @"C:\x\builds\packages\AI2P_v_1_209_full_windows_x64.exe", true, "windows", "x64", 208);
        Assert.Equal(209, build);
        Assert.Equal("", error);
        // тот же билд ставить можно (переустановка той же версии при публикации)
        Assert.Equal("", AppUpdate.CheckLocal("AI2P_v_1_208_linux_arm64.run", false, "linux", "arm64", 208).ErrorKey);
    }

    [Fact]
    public void Foreign_Or_Older_Package_Is_Refused()
    {
        // вид установки: полный пакет обычной установке не годится, и наоборот
        Assert.Equal("msg.update.10",
            AppUpdate.CheckLocal("AI2P_v_1_209_full_windows_x64.exe", false, "windows", "x64", 208).ErrorKey);
        Assert.Equal("msg.update.10",
            AppUpdate.CheckLocal("AI2P_v_1_209_windows_x64.exe", true, "windows", "x64", 208).ErrorKey);
        // чужая система и архитектура
        Assert.Equal("msg.update.10",
            AppUpdate.CheckLocal("AI2P_v_1_209_linux_x64.run", false, "windows", "x64", 208).ErrorKey);
        Assert.Equal("msg.update.10",
            AppUpdate.CheckLocal("AI2P_v_1_209_windows_arm64.exe", false, "windows", "x64", 208).ErrorKey);
        // не наше имя вовсе
        Assert.Equal("msg.update.10", AppUpdate.CheckLocal("setup.exe", false, "windows", "x64", 208).ErrorKey);
        // старее установленной — понижать нельзя
        Assert.Equal("msg.update.11",
            AppUpdate.CheckLocal("AI2P_v_1_207_windows_x64.exe", false, "windows", "x64", 208).ErrorKey);
    }

    [Fact]
    public void Package_Path_Stays_In_Project_Or_Allowed_Dir()
    {
        var root = Path.Combine(Path.GetTempPath(), "t756s0_" + Guid.NewGuid().ToString("N"), "proj");
        var inside = TaskToolset.ResolvePackagePath("builds/packages/a.exe", root, null);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "builds", "packages", "a.exe")), inside);
        // выход за папку проекта «..» не пускается, полный путь без правила — тоже
        Assert.Null(TaskToolset.ResolvePackagePath("../other/a.exe", root, null));
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "a.exe");
        Assert.Null(TaskToolset.ResolvePackagePath(outside, root, null));
        Assert.Equal(Path.GetFullPath(outside), TaskToolset.ResolvePackagePath(outside, root, _ => true));
        Assert.Null(TaskToolset.ResolvePackagePath("", root, _ => true));
    }

    [Fact]
    public void Job_Is_Closed_Before_Restart_And_Outcome_Is_Reported()
    {
        var dir = Path.Combine(Path.GetTempPath(), "t756s0_" + Guid.NewGuid().ToString("N"));
        var appDir = Path.Combine(dir, "app");
        var stateDir = Path.Combine(dir, "data");
        Directory.CreateDirectory(appDir);
        try
        {
            var steps = new List<string>();
            var stopped = new ManualResetEventSlim();
            var service = new AppUpdateService(() => new Ai2pConfig(), () =>
            {
                steps.Add("stop");
                stopped.Set();
            }, appDir)
            {
                StateDir = stateDir,
                LaunchScript = script => steps.Add("launch:" + Path.GetFileName(script)),
                StopDelay = TimeSpan.FromMilliseconds(50),
            };
            // обычная установка (version.json нет), архитектура этой машины
            var package = Path.Combine(dir, AppUpdate.PackageName(
                AppInfo.Version, false, AppUpdate.OsName(), AppUpdate.ArchOf("")));
            File.WriteAllText(package, "fake");

            var pending = new AppUpdate.Pending { OrgId = "org", TaskId = "t1", JobId = "j1", ExecutorId = "e1" };
            UpdateStartDto started;
            try
            {
                started = service.StartFromFile(package, pending, text => steps.Add("close:" + text.Length));
                Assert.True(AppUpdate.Restarting);
            }
            finally
            {
                Assert.True(stopped.Wait(TimeSpan.FromSeconds(10)));
                AppUpdate.Restarting = false;
            }

            Assert.Equal(AppInfo.Version, started.Version);
            Assert.True(started.Restarts);
            // ПОРЯДОК: задание закрыто раньше, чем запущен скрипт, и раньше остановки
            Assert.StartsWith("close:", steps[0]);
            Assert.StartsWith("launch:ai2p_update", steps[1]);
            Assert.Equal("stop", steps[2]);

            // запись о начатом обновлении читается один раз
            var taken = service.TakePending();
            Assert.NotNull(taken);
            Assert.Equal("t1", taken!.TaskId);
            Assert.Equal("e1", taken.ExecutorId);
            Assert.Equal(AppInfo.Build, taken.ToBuild);
            Assert.Equal(AppInfo.Version, taken.FromVersion);
            Assert.Null(service.TakePending());

            Assert.Contains(AppInfo.Version, AppUpdateService.PendingOutcome(taken, AppInfo.Build));
            // поднялась не та версия — ошибка с путём к журналу установки
            taken.ToBuild = AppInfo.Build + 1;
            Assert.Contains(taken.LogFile, AppUpdateService.PendingOutcome(taken, AppInfo.Build));
        }
        finally
        {
            AppUpdate.Restarting = false;
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Foreign_Package_Closes_Nothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "t756s0_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var closed = false;
            var service = new AppUpdateService(() => new Ai2pConfig(), () => { }, dir)
            {
                StateDir = dir,
                LaunchScript = _ => throw new InvalidOperationException("не должно запускаться"),
            };
            var foreignOs = AppUpdate.OsName() == "windows" ? "linux" : "windows";
            var package = Path.Combine(dir, AppUpdate.PackageName(AppInfo.Version, false, foreignOs, "x64"));
            File.WriteAllText(package, "fake");
            Assert.Throws<InvalidOperationException>(() =>
                service.StartFromFile(package, new AppUpdate.Pending(), _ => closed = true));
            Assert.False(closed);
            Assert.False(AppUpdate.Restarting);
            Assert.Null(service.TakePending());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

/// <summary>Своя коллекция без параллели: флаг перезапуска общий на процесс (T-756-S0).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class T756S0Collection
{
    public const string Name = "T756S0 app update restart flag";
}

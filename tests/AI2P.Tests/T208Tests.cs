using AI2P.Core;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-208 (выпуск 1.133): АВТООБНОВЛЕНИЕ ПРИЛОЖЕНИЯ.
///
/// Проверяется то, что решает ответ «есть ли обновление» и НЕ ходит в сеть: правило имени
/// файла выпуска (оно общее со скриптами <c>MakePackage</c>, T-234-S0), разбор номера
/// версии из имени и из тэга, адрес API выпусков по адресу репозитория и период суточной
/// проверки для записи расписания.
///
/// Именно здесь легче всего ошибиться молча: неверное правило имени даёт не ошибку,
/// а вечное «обновлений нет» — самый дорогой вид неправды, потому что он выглядит
/// как исправная работа.
/// </summary>
public sealed class T208Tests
{
    [Fact]
    public void Package_Name_Puts_Full_After_The_Version()
    {
        // правило T-234-S0: «full» ПОСЛЕ номера версии, дальше система и архитектура
        Assert.Equal("AI2P_v_1_133_full_windows_x64.exe",
            AppUpdate.PackageName("1.133", full: true, "windows", "x64"));
        Assert.Equal("AI2P_v_1_133_windows_x64.exe",
            AppUpdate.PackageName("1.133", full: false, "windows", "x64"));
        // Linux и macOS упакованы makeself — у них .run
        Assert.Equal("AI2P_v_1_133_linux_arm64.run",
            AppUpdate.PackageName("1.133", full: false, "linux", "arm64"));
        Assert.Equal("AI2P_v_1_140_full_macos_arm64.run",
            AppUpdate.PackageName("1.140", full: true, "macos", "arm64"));
    }

    [Fact]
    public void Only_Our_Own_File_Matches()
    {
        Assert.True(AppUpdate.Matches("AI2P_v_1_133_windows_x64.exe", false, "windows", "x64"));
        // способ установки обязан совпадать: полный пакет и обычный — разные файлы
        Assert.False(AppUpdate.Matches("AI2P_v_1_133_full_windows_x64.exe", false, "windows", "x64"));
        Assert.True(AppUpdate.Matches("AI2P_v_1_133_full_windows_x64.exe", true, "windows", "x64"));
        // чужая система, чужая архитектура и посторонний файл выпуска
        Assert.False(AppUpdate.Matches("AI2P_v_1_133_linux_x64.run", false, "windows", "x64"));
        Assert.False(AppUpdate.Matches("AI2P_v_1_133_windows_arm64.exe", false, "windows", "x64"));
        Assert.False(AppUpdate.Matches("SHA256SUMS.txt", false, "windows", "x64"));
        Assert.False(AppUpdate.Matches("", false, "windows", "x64"));
    }

    [Fact]
    public void Build_Is_Read_From_The_File_Name_And_From_The_Tag()
    {
        Assert.Equal(133, AppUpdate.BuildOfAsset("AI2P_v_1_133_full_windows_x64.exe"));
        Assert.Equal(140, AppUpdate.BuildOfAsset("AI2P_v_1_140_linux_arm64.run"));
        Assert.Equal(0, AppUpdate.BuildOfAsset("readme.md"));
        // тэг человек ставит рукой, поэтому разбор широкий
        Assert.Equal(133, AppUpdate.BuildOfTag("v1.133"));
        Assert.Equal(133, AppUpdate.BuildOfTag("1.133"));
        Assert.Equal(133, AppUpdate.BuildOfTag("v_1_133"));
        Assert.Equal(133, AppUpdate.BuildOfTag("release-1.133"));
        Assert.Equal(0, AppUpdate.BuildOfTag("latest"));
        Assert.Equal("1.133", AppUpdate.VersionOf(133));
    }

    [Fact]
    public void Releases_Address_Survives_The_Address_A_Human_Copied()
    {
        const string api = "https://api.github.com/repos/mmf007/ai2p/releases?per_page=30";
        Assert.Equal(api, AppUpdate.ReleasesApiUrl("https://github.com/mmf007/ai2p"));
        Assert.Equal(api, AppUpdate.ReleasesApiUrl("https://github.com/mmf007/ai2p/releases"));
        Assert.Equal(api, AppUpdate.ReleasesApiUrl("https://github.com/mmf007/ai2p.git"));
        Assert.Equal("", AppUpdate.ReleasesApiUrl("github.com"));
        Assert.Equal("", AppUpdate.ReleasesApiUrl(""));
        Assert.Equal("https://github.com/mmf007/ai2p/releases",
            AppUpdate.ReleasesPageUrl("https://github.com/mmf007/ai2p"));
        Assert.Equal("https://github.com/mmf007/ai2p/releases",
            AppUpdate.ReleasesPageUrl("https://github.com/mmf007/ai2p/release"));
    }

    [Fact]
    public void Daily_Check_Is_A_Weekly_Period_With_All_Seven_Days()
    {
        // ежедневного вида у расписания нет вовсе — ежедневное это недельное со всеми днями
        Assert.Equal("{\"type\":\"weekly\",\"days\":[1,2,3,4,5,6,7],\"time\":\"02:00\"}",
            AppUpdate.DailyPeriodJson("2:00"));
        Assert.Equal(AppUpdate.DefaultTime, AppUpdate.NormalizeTime(""));
        Assert.Equal(AppUpdate.DefaultTime, AppUpdate.NormalizeTime("не время"));
        Assert.Equal("23:45", AppUpdate.NormalizeTime("23:45"));
    }

    [Fact]
    public void Install_Kind_Is_Read_From_The_Deployment_Version_File()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ai2p_t208_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // version.json нет вовсе (запуск из исходников) — обычная выкладка
            Assert.Equal((false, ""), AppUpdate.InstallKind(dir));
            File.WriteAllText(Path.Combine(dir, "version.json"),
                "{\"version\":\"1.133\",\"build\":133,\"selfContained\":true,\"runtime\":\"win-x64\"}");
            var (full, runtime) = AppUpdate.InstallKind(dir);
            Assert.True(full);
            Assert.Equal("win-x64", runtime);
            // архитектура полной выкладки записана рантаймом
            Assert.Equal("x64", AppUpdate.ArchOf(runtime));
            Assert.Equal("arm64", AppUpdate.ArchOf("osx-arm64"));
            // рантайма нет (обычная выкладка) — архитектура этой машины
            Assert.Equal(AppUpdate.ArchName(), AppUpdate.ArchOf(""));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Schedule_Knows_Both_Update_Actions()
    {
        // действие без записи справочника правилами безопасности не закрывается вовсе
        // (наука выпуска 1.80), поэтому связь «действие → код записи» проверяется здесь
        Assert.Contains(ScheduleActions.AppCheck, ScheduleActions.All);
        Assert.Contains(ScheduleActions.AppUpdate, ScheduleActions.All);
        Assert.True(ScheduleActions.IsKnown(ScheduleActions.AppUpdate));
        Assert.Equal("AI2P.Update.Check", ScheduleActions.CatalogCodeOf(ScheduleActions.AppCheck));
        Assert.Equal("AI2P.Update.Run", ScheduleActions.CatalogCodeOf(ScheduleActions.AppUpdate));
        Assert.Equal("schedule.action.app_update",
            ScheduleActions.TitleKeyOf(ScheduleActions.AppUpdate));
    }
}

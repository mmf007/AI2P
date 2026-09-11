using AI2P.Connectors;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-190-S0: ДОПОЛНИТЕЛЬНЫЕ (НЕОБЯЗАТЕЛЬНЫЕ) ПАКЕТЫ УСТАНОВКИ МОДЕЛИ.
///
/// Заказчик: «сделать чекбокс для необязательных пакетов — если не нужно, не ставить LoRA
/// у модели; может быть ситуация, что LoRA обучают только на одном сервере в кластере, а на
/// остальных просто их используют». У Kandinsky обучение приносит ЧЕТЫРЕ пакета
/// (musubi-tuner, python и два снимка базовых весов на 18 ГБ) и заводит запись
/// плагина-тренера в «Плагинах и MCP» (T-156-S0) — всё это теперь снимается одним флажком.
///
/// Три вещи, которые проверяются здесь. (1) Опция собирается из <c>lora.train.packages</c>
/// профайла — второго места для того же списка нет. (2) Выключенная опция убирает свои
/// пакеты и из установки, и из подсчёта «модель установлена» (иначе модель без тренера
/// висела бы неустановленной и неактивной вечно), и снимает признак «есть чем обучать».
/// (3) Выбор — пер-серверный: он уходит делегатом в config.json компьютера, а не в профайл
/// (профайлы переписывает сид) и не в базу организации (её реплицирует весь кластер).
/// </summary>
public sealed class T190S0Tests : IDisposable
{
    private const string T2VId = "6f1a45e0-0d31-4c65-9a01-000000000007";

    private readonly StorageFixture _f = new();
    private readonly string _repoDir;
    private readonly Dictionary<string, List<string>> _config = new(StringComparer.OrdinalIgnoreCase);

    public T190S0Tests()
    {
        _repoDir = Path.Combine(_f.Dir, "models-repo");
        _f.Models.Seed();
        WriteProfile();
        WriteCatalog();
    }

    public void Dispose() => _f.Dispose();

    /// <summary>Установщик с пер-серверным хранилищем выбора — как config.json в продукте.</summary>
    private ModelInstallService NewInstaller()
    {
        var service = new ModelInstallService(_f.Models, _f.Files, _f.Events, () => _repoDir,
            () => Path.Combine(_f.Dir, "dist"), () => Path.Combine(_f.Dir, "packages"))
        {
            SystemSearchPath = () => "",    // ничего «уже установленного» в системе нет
        };
        service.OptionsOff = id => _config.TryGetValue(id, out var off) ? off : [];
        service.SaveOptionsOff = (id, off) => _config[id] = [.. off];
        return service;
    }

    // --- 1. Опция собирается из настройки обучения ---

    [Fact]
    public void The_Training_Packages_Are_An_Optional_Install_Group()
    {
        var manifest = ModelInstallManifest.Parse(
            File.ReadAllText(_f.Files.Abs(_f.Models.Get(T2VId)!.ProfilePath)))!;

        var option = Assert.Single(manifest.Options);
        Assert.Equal(ModelInstallOption.Lora, option.Id);
        Assert.Equal(["musubi-tuner", "python"], option.Packages);
        // по умолчанию ставится ВСЁ — это обещано человеку
        Assert.True(manifest.OptionOn(ModelInstallOption.Lora));
        Assert.Equal(["comfyui", "musubi-tuner", "python"], manifest.AllPackages);
        // обязательный пакет опции не принадлежит, даже если она его тоже называет
        Assert.Equal("", manifest.OptionOf("comfyui"));
        Assert.Equal(ModelInstallOption.Lora, manifest.OptionOf("musubi-tuner"));
    }

    [Fact]
    public void A_Model_Without_Training_Has_No_Options_At_All()
    {
        _f.Files.WriteText(_f.Models.Get(T2VId)!.ProfilePath, """
            { "provider": "comfyui", "model": "test",
              "install": { "group": "Kandinsky-5", "packages": ["comfyui"] } }
            """);
        var status = NewInstaller().Status(T2VId);

        Assert.Empty(status.Options);
        Assert.Equal(["comfyui"], status.Packages.Select(p => p.Id));
    }

    // --- 2. Выключенная опция не ставится и не мешает ---

    [Fact]
    public void Switching_The_Option_Off_Drops_Its_Packages_From_The_Install()
    {
        var service = NewInstaller();
        var before = service.Status(T2VId);
        Assert.Equal(["comfyui", "musubi-tuner", "python"], before.Packages.Select(p => p.Id));
        var shown = Assert.Single(before.Options);
        Assert.True(shown.Enabled);
        Assert.Equal(2, shown.Packages);

        var after = service.SetOption(T2VId, ModelInstallOption.Lora, on: false);

        Assert.Equal(["comfyui"], after.Packages.Select(p => p.Id));
        // выключенная опция всё равно ОТДАЁТСЯ окну — иначе её нечем включить обратно
        Assert.False(Assert.Single(after.Options).Enabled);
        Assert.Equal([ModelInstallOption.Lora], _config[T2VId]);
    }

    [Fact]
    public void Without_The_Option_The_Model_Counts_As_Installed_And_Has_No_Trainer()
    {
        var service = NewInstaller();
        InstallFake(service, "comfyui", "ComfyUI_windows_portable/python_embeded/python.exe");
        Assert.False(service.Status(T2VId).Installed);       // тренера нет — не установлена

        service.SetOption(T2VId, ModelInstallOption.Lora, on: false);

        // …а без обучения ставить больше нечего: модель установлена и может активироваться
        Assert.True(service.Status(T2VId).Installed);
        // и записи плагина-тренера заводить не с чего (T-156-S0)
        Assert.False(service.Manifest(T2VId)!.HasTrainer);
    }

    [Fact]
    public void The_Option_Can_Be_Switched_Back_On()
    {
        var service = NewInstaller();
        service.SetOption(T2VId, ModelInstallOption.Lora, on: false);

        var back = service.SetOption(T2VId, ModelInstallOption.Lora, on: true);

        Assert.Equal(["comfyui", "musubi-tuner", "python"], back.Packages.Select(p => p.Id));
        Assert.True(Assert.Single(back.Options).Enabled);
        Assert.True(service.Manifest(T2VId)!.HasTrainer);
        Assert.Empty(_config[T2VId]);   // «ставить всё» — это ПУСТОЙ список выключенных
    }

    [Fact]
    public void An_Unknown_Option_Is_Refused()
    {
        var service = NewInstaller();
        Assert.Throws<ArgumentException>(() => service.SetOption(T2VId, "video", on: false));
    }

    // --- вспомогательное ---

    /// <summary>Профайл с манифестом БЕЗ гигабайтных весов: проверяются пакеты.</summary>
    private void WriteProfile() =>
        _f.Files.WriteText(_f.Models.Get(T2VId)!.ProfilePath, """
            {
              "provider": "comfyui",
              "model": "test",
              "install": { "group": "Kandinsky-5", "packages": ["comfyui"] },
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "train": { "kind": "process", "packages": ["musubi-tuner", "python"] }
              }
            }
            """);

    /// <summary>Справочник пакетов со ссылками в никуда: качать здесь нечего.</summary>
    private void WriteCatalog() =>
        _f.Files.WriteText(AiModelService.PackagesPath, """
            {
              "packages": [
                { "id": "comfyui", "name": "ComfyUI (portable)", "dir": "ComfyUI",
                  "check": "python.exe",
                  "files": [ { "name": "comfy.zip", "url": "http://127.0.0.1:9/c.zip", "unpack": "zip" } ] },
                { "id": "musubi-tuner", "name": "Musubi Tuner", "dir": "musubi-tuner",
                  "check": "kandinsky5_train_network.py",
                  "files": [ { "name": "musubi.zip", "url": "http://127.0.0.1:9/m.zip", "unpack": "zip" } ] },
                { "id": "python", "name": "Python 3.12", "dir": "python",
                  "check": "python.exe",
                  "files": [ { "name": "python.zip", "url": "http://127.0.0.1:9/p.zip", "unpack": "zip" } ] }
              ]
            }
            """);

    /// <summary>Разложить «установленный» пакет: файл-признак внутри его каталога.</summary>
    private static void InstallFake(ModelInstallService service, string packageId, string relative)
    {
        var package = service.Packages().Find(packageId)!;
        var path = Path.Combine(service.PackageDir(package),
            relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "файл пакета");
    }
}

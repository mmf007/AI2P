using System.Text;
using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-4-S0: НЕДОСТАЮЩИЕ ПАКЕТЫ СТАВЯТСЯ ТОЛЬКО ПРИ УСТАНОВКЕ МОДЕЛИ.
///
/// До этого обучающий репозиторий LoRA (musubi-tuner) приезжал по требованию — первым шагом
/// первого обучения (T-289). Со стороны это выглядело так: человек нажал «обучить», и вместо
/// обучения пошла загрузка. Правило теперь одно: ждать человек согласился В МОМЕНТ УСТАНОВКИ
/// МОДЕЛИ, там для этого есть готовое окно с ходом работы — значит всё, что модели нужно (и
/// для генерации, и для обучения), приходит там. Дальше ожидание сокращается: обучение только
/// ПРОВЕРЯЕТ пакеты и отвечает отказом сразу, если чего-то нет.
///
/// Вторая половина задачи — Python: он тоже стал пакетом установки модели, но с проверкой
/// «а не стоит ли он уже на этом сервере» (как и у всех пакетов). Проверка эта не просто
/// «нашлась команда»: musubi-tuner живёт на 3.10–3.12, и найденный 3.13 сорвал бы обучение
/// через несколько часов после нажатия кнопки.
///
/// Что осталось как было и проверяется здесь же: <c>train.cmd</c>, <c>dataset.toml</c> и
/// окружение .venv по-прежнему готовятся при ПЕРВОМ ЗАПУСКЕ ОБУЧЕНИЯ — это настройка
/// обучения, а не дистрибутив.
/// </summary>
public sealed class T4S0Tests : IDisposable
{
    private const string T2VId = "6f1a45e0-0d31-4c65-9a01-000000000007";
    private const string I2VId = "6f1a45e0-0d31-4c65-9a01-000000000032";

    private readonly StorageFixture _f = new();
    private readonly string _repoDir;
    private readonly string _distDir;
    private readonly string _packagesDir;

    public T4S0Tests()
    {
        _repoDir = Path.Combine(_f.Dir, "models-repo");
        _distDir = Path.Combine(_f.Dir, "dist");
        _packagesDir = Path.Combine(_f.Dir, "packages");
        _f.Models.Seed();
    }

    public void Dispose() => _f.Dispose();

    private ModelInstallService NewInstaller() =>
        new(_f.Models, _f.Files, _f.Events, () => _repoDir, () => _distDir, () => _packagesDir);

    // --- 1. Справочник пакетов: Python ---

    [Fact]
    public void The_Catalog_Has_Python_And_Knows_How_To_Find_It_In_The_System()
    {
        var catalog = ModelPackageCatalog.Parse(
            File.ReadAllText(_f.Files.Abs(AiModelService.PackagesPath)));

        var python = catalog.Find("python");
        Assert.NotNull(python);
        Assert.Equal("python.exe", python!.Check);
        Assert.NotEqual("", python.Hint);

        // «не стоит ли он уже здесь» — это команда в PATH И годная версия
        Assert.NotNull(python.System);
        Assert.Contains("python", python.System!.Commands);
        Assert.Equal("--version", python.System.VersionArgs);
        Assert.Equal("3.10", python.System.MinVersion);
        Assert.Equal("3.12", python.System.MaxVersion);

        // дистрибутив — с ТОЧНОЙ версией, а не «последний»: иначе Python у каждого свой,
        // и разбор жалобы «у меня обучение падает» становится гаданием (наука T-289)
        var file = Assert.Single(python.Files);
        Assert.Contains("/releases/download/", file.Url);
        Assert.DoesNotContain("latest", file.Url);
        Assert.Contains("3.12.", file.Url);
        Assert.NotEqual("", file.Unpack);
    }

    // --- 2. Пакеты обучения ставятся вместе с моделью ---

    [Theory]
    [InlineData(T2VId)]
    [InlineData(I2VId)]
    public void The_Model_Install_Brings_Both_The_Trainer_And_Python(string modelId)
    {
        var manifest = ModelInstallManifest.Parse(
            File.ReadAllText(_f.Files.Abs(_f.Models.Get(modelId)!.ProfilePath)))!;

        // объявлены они в настройке LoRA — там всё про адаптеры (T-13-S1)…
        Assert.Equal(["comfyui"], manifest.Packages);
        Assert.Equal(["musubi-tuner", "python"], manifest.TrainPackages);
        // …а ставятся установкой модели, одним списком и без повторов
        Assert.Equal(["comfyui", "musubi-tuner", "python"], manifest.AllPackages);
    }

    [Fact]
    public void The_Training_Script_Runs_The_Python_Of_The_Package()
    {
        var lora = LoraSettings.Parse(File.ReadAllText(_f.Files.Abs(_f.Models.Get(I2VId)!.ProfilePath)));
        var script = lora.Train.Files
            .Single(f => f.Path.EndsWith("train.cmd", StringComparison.Ordinal)).Text;

        // окружение обучения (.venv, torch) по-прежнему готовится ПЕРВЫМ ЗАПУСКОМ обучения —
        // и делает это сам скрипт, а вот интерпретатор ему теперь приносит установка модели
        Assert.Contains("{package:python:python.exe}", script);
        Assert.DoesNotContain("set \"PYEXE=python\"", script);
        Assert.Contains("-m venv", script);
        // свой Python человек по-прежнему может назначить переменной окружения
        Assert.Contains("AI2P_LORA_PYTHON", script);
        // кириллицы в командном файле быть не должно: консоль читает .cmd в кодировке OEM
        Assert.DoesNotContain(script, c => c > 127);
    }

    [Fact]
    public void A_Model_Is_Not_Installed_Until_The_Training_Packages_Are_There()
    {
        var service = NewInstaller();
        WriteProfile(T2VId);
        WriteCatalog();
        service.SystemSearchPath = () => "";        // на этом сервере Python не стоит
        var status = service.Status(T2VId);

        // в окне установки видны все три пакета — это и есть «готовый интерфейс инсталляции»
        Assert.Equal(["comfyui", "musubi-tuner", "python"], status.Packages.Select(p => p.Id));
        Assert.False(status.Installed);

        InstallFake(service, "comfyui", "ComfyUI_windows_portable/python_embeded/python.exe");
        Assert.False(service.Status(T2VId).Installed);       // тренера ещё нет

        InstallFake(service, "musubi-tuner", "musubi-tuner-0.3.4/src/kandinsky5_train_network.py");
        Assert.False(service.Status(T2VId).Installed);       // и Python ещё нет

        // Python нашёлся в системе — качать его не надо, модель установлена
        service.SystemSearchPath = SystemBin;
        var done = service.Status(T2VId);
        Assert.True(done.Installed);
        var python = done.Packages.Single(p => p.Id == "python");
        Assert.True(python.Installed);
        Assert.True(python.System);
        // и показывается не «каталог пакета», а то, чем мы будем пользоваться
        Assert.Equal(SystemPython(), python.Dir);
    }

    [Fact]
    public async Task An_Already_Installed_Python_Is_Not_Downloaded()
    {
        var service = NewInstaller();
        WriteProfile(T2VId);
        // ссылки дистрибутивов ведут в никуда: если установщик за ними пойдёт — упадёт
        WriteCatalog();
        InstallFake(service, "comfyui", "portable/python.exe");
        InstallFake(service, "musubi-tuner", "src/kandinsky5_train_network.py");
        service.SystemSearchPath = SystemBin;

        service.Start(T2VId);
        await WaitFinishedAsync(service, T2VId);

        var status = service.Status(T2VId);
        Assert.Equal("", status.Error);
        Assert.True(status.Installed);
        Assert.False(Directory.Exists(Path.Combine(_packagesDir, "python")));
    }

    [Fact]
    public void The_Path_Of_The_System_Program_Reaches_The_Setup_Files()
    {
        var service = NewInstaller();
        WriteProfile(T2VId);
        WriteCatalog();
        service.SystemSearchPath = SystemBin;

        // {package:python:python.exe} обязан развернуться в найденную программу: каталог
        // пакета пуст, а запускать обучение чем-то надо
        var expanded = service.ExpandPaths(T2VId, "PY={package:python:python.exe}");
        Assert.Equal("PY=" + SystemPython(), expanded);

        // а появился свой экземпляр — берётся он: он и есть та версия, которую мы обещали
        var own = Path.Combine(_packagesDir, "python", "python", PythonExe);
        Directory.CreateDirectory(Path.GetDirectoryName(own)!);
        File.WriteAllText(own, "exe");
        Assert.Equal("PY=" + own, service.ExpandPaths(T2VId, "PY={package:python:python.exe}"));
    }

    // --- 3. Обучение пакеты не ставит, а спрашивает ---

    [Fact]
    public void Training_Refuses_At_Once_When_A_Training_Package_Is_Missing()
    {
        var service = NewInstaller();
        var projectDir = ProjectDir();
        WriteTrainProfile();
        WriteCatalog();
        service.SystemSearchPath = () => "";        // Python на сервере не стоит
        _f.LoraTrain.MissingPackages = service.MissingPackages;
        var project = _f.Projects.Create("Ролик", projectDir, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        AddFrame(project.Id, lora.Id, "кадр1.png", "герой");
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);

        var error = Assert.Throws<ArgumentException>(() => _f.LoraTrain.Start(row.Id, force: false));

        // в отказе названы и пакеты, и модель: про lora.train.packages человек не знает
        // ничего, ему нужно «что нажать»
        Assert.Contains("Musubi", error.Message);
        Assert.Contains("Python", error.Message);
        Assert.Contains(_f.Models.Get(T2VId)!.Name, error.Message);
        // и строка обучения осталась нетронутой — полчаса в «обучается» ради отказа не тратится
        Assert.Equal(LoraModelStates.None, _f.Objects.LoraModel(row.Id)!.Status);
    }

    [Fact]
    public async Task Training_Starts_At_Once_When_The_Packages_Are_In_Place()
    {
        var service = NewInstaller();
        var projectDir = ProjectDir();
        WriteTrainProfile();
        WriteCatalog();
        InstallFake(service, "musubi-tuner", "src/kandinsky5_train_network.py");
        service.SystemSearchPath = SystemBin;
        _f.LoraTrain.MissingPackages = service.MissingPackages;
        var project = _f.Projects.Create("Ролик", projectDir, null, null);
        var lora = NewLora(project.Id, "Вася-LoRA");
        AddFrame(project.Id, lora.Id, "кадр1.png", "герой");
        var row = _f.Objects.AddLoraModel(lora.Id, T2VId, null);

        _f.LoraTrain.Start(row.Id, force: false);
        var done = await WaitTrainAsync(row.Id);

        Assert.Equal("", done.Error);
        Assert.Equal(LoraModelStates.Ready, done.Status);
        // ни одного дистрибутива обучение не качало: каталог дистрибутивов даже не заведён
        Assert.False(Directory.Exists(_distDir));
    }

    [Fact]
    public void Missing_Packages_Are_Named_By_Their_Human_Names()
    {
        var service = NewInstaller();
        WriteCatalog();
        service.SystemSearchPath = () => "";
        Assert.Equal(["Musubi Tuner (обучение LoRA)", "Python 3.12 (для обучения LoRA)"],
            service.MissingPackages(["musubi-tuner", "python"]));

        InstallFake(service, "musubi-tuner", "src/kandinsky5_train_network.py");
        service.SystemSearchPath = SystemBin;
        Assert.Empty(service.MissingPackages(["musubi-tuner", "python"]));

        // пакета нет в справочнике — назвать его всё равно надо, иначе жалоба будет пустой
        Assert.Equal(["каких-то-нет"], service.MissingPackages(["каких-то-нет"]));
    }

    // --- 4. Как ищется установленная в системе программа ---

    [Fact]
    public void A_System_Program_Is_Found_By_Path_And_Empty_Stubs_Are_Skipped()
    {
        var real = SystemBin();
        var stub = Path.Combine(_f.Dir, "stubs");
        Directory.CreateDirectory(stub);
        File.WriteAllText(Path.Combine(stub, PythonExe), "");    // псевдоним магазина: 0 байт

        var probe = new ModelPackage.SystemProbe { Commands = ["python"] };
        // заглушка нулевой длины пропускается: её запуск открыл бы магазин приложений
        Assert.Equal(SystemPython(), SystemPackageProbe.Find(probe, stub + Path.PathSeparator + real));
        Assert.Null(SystemPackageProbe.Find(probe, stub));
        Assert.Null(SystemPackageProbe.Find(probe, ""));
    }

    [Theory]
    [InlineData("Python 3.12.14", true)]
    [InlineData("Python 3.10.0", true)]
    [InlineData("3.12", true)]
    [InlineData("Python 3.13.1", false)]     // на 3.13 musubi-tuner не собирается
    [InlineData("Python 3.9.13", false)]
    [InlineData("Python 4.0", false)]
    [InlineData("", false)]                  // версию не разобрали — значит не годится
    public void The_Version_Range_Is_Compared_By_Major_And_Minor(string version, bool ok) =>
        Assert.Equal(ok, SystemPackageProbe.Accepts(version, "3.10", "3.12"));

    [Fact]
    public void The_Version_Is_Asked_Of_The_Program_Itself()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;     // проверка про запуск настоящей программы — на своей системе
        }
        // cmd.exe /c ver печатает «Microsoft Windows [Version 10.0.22631.…]» — этого хватает,
        // чтобы доказать, что версию мы именно СПРАШИВАЕМ у программы, а не выдумываем
        var version = SystemPackageProbe.VersionOf(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c ver");
        Assert.Matches(@"^\d+\.\d+", version);
        // а несуществующая программа не роняет проверку и просто не годится
        Assert.Equal("", SystemPackageProbe.VersionOf(Path.Combine(_f.Dir, "нет.exe"), "--version"));
    }

    // --- 5. Распаковка настоящего архива Python ---

    /// <summary>
    /// Архив Python собран так, что в поле имени записи после нуля-терминатора лежит хвост
    /// предыдущего имени, а <c>TarReader</c> .NET отдаёт имя ЦЕЛИКОМ. Пока мы полагались на
    /// штатный <c>TarFile.ExtractToDirectory</c>, из «python/python.exe» получался файл
    /// «python.exe␀hon.exe» — и установка пакета честно кончалась «в каталоге нет
    /// python.exe». Найдено живым прогоном на настоящем дистрибутиве.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]     // .tar.gz — ровно так Python и приходит
    public void A_Tar_Entry_Name_Ends_At_The_Zero_Byte(bool gzip)
    {
        var tar = Tar(("python/python.exe", "hon.exe", "программа"),
                      ("python/LICENSE.txt", "NSE.txt", "лицензия"));
        var archive = Path.Combine(_f.Dir, gzip ? "python.tar.gz" : "python.tar");
        File.WriteAllBytes(archive, gzip ? Gzip(tar) : tar);
        var dir = Path.Combine(_f.Dir, "unpacked" + gzip);

        Assert.True(ArchiveExtractor.TryExtract(archive, dir, out var error), error);

        var inside = Path.Combine(dir, "python");
        Assert.True(File.Exists(Path.Combine(inside, "python.exe")));
        Assert.Equal("программа", File.ReadAllText(Path.Combine(inside, "python.exe")));
        Assert.Equal(2, Directory.GetFiles(inside).Length);   // ни одного лишнего имени
    }

    [Fact]
    public void A_Tar_Entry_Aiming_Outside_The_Target_Is_Refused()
    {
        var archive = Path.Combine(_f.Dir, "evil.tar");
        File.WriteAllBytes(archive, Tar(("../evil.exe", "", "вредное")));
        var dir = Path.Combine(_f.Dir, "unpacked-evil");

        Assert.False(ArchiveExtractor.TryExtract(archive, dir, out var error));
        Assert.NotEqual("", error);
        Assert.False(File.Exists(Path.Combine(_f.Dir, "evil.exe")));
    }

    /// <summary>
    /// Архив ustar из записей «имя, мусор после нуля, содержимое» — собирается руками:
    /// штатный <c>TarWriter</c> пишет заголовки чисто и этот случай не воспроизводит.
    /// </summary>
    private static byte[] Tar(params (string Name, string Garbage, string Text)[] entries)
    {
        var result = new List<byte>();
        foreach (var (name, garbage, text) in entries)
        {
            var content = Encoding.UTF8.GetBytes(text);
            var head = new byte[512];
            var raw = Encoding.ASCII.GetBytes(name);
            raw.CopyTo(head, 0);
            head[raw.Length] = 0;
            Encoding.ASCII.GetBytes(garbage).CopyTo(head, raw.Length + 1);
            Put(head, 100, "0000644\0");
            Put(head, 108, "0000000\0");
            Put(head, 116, "0000000\0");
            Put(head, 124, Convert.ToString(content.Length, 8).PadLeft(11, '0') + "\0");
            Put(head, 136, "00000000000\0");
            Put(head, 148, "        ");        // на время счёта — пробелы
            head[156] = (byte)'0';
            Put(head, 257, "ustar\000");
            var sum = head.Aggregate(0, (acc, b) => acc + b);
            Put(head, 148, Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ");
            result.AddRange(head);
            result.AddRange(content);
            result.AddRange(new byte[(512 - content.Length % 512) % 512]);
        }
        result.AddRange(new byte[1024]);        // конец архива — два нулевых блока
        return result.ToArray();
    }

    private static void Put(byte[] block, int at, string value) =>
        Encoding.ASCII.GetBytes(value).CopyTo(block, at);

    private static byte[] Gzip(byte[] data)
    {
        using var memory = new MemoryStream();
        using (var zip = new System.IO.Compression.GZipStream(memory,
                   System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            zip.Write(data, 0, data.Length);
        }
        return memory.ToArray();
    }

    // --- 6. Словари ---

    [Fact]
    public void The_New_Messages_Have_Texts_In_Both_Languages()
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var refusal = Loc.In(lang, "msg.lora.33", "Python 3.12", "Kandinsky-5.0-T2V-Lite-sft-5s");
            Assert.NotEqual("msg.lora.33", refusal);
            Assert.Contains("Python 3.12", refusal);
            Assert.NotEqual("install.package.system", Loc.In(lang, "install.package.system"));
        }
    }

    // --- общее ---

    /// <summary>Имя файла программы Python на этой системе.</summary>
    private static string PythonExe => OperatingSystem.IsWindows() ? "python.exe" : "python";

    /// <summary>Каталог с «установленным в системе» Python (файл ненулевой длины).</summary>
    private string SystemBin()
    {
        Directory.CreateDirectory(Path.Combine(_f.Dir, "system-bin"));
        if (!File.Exists(SystemPython()))
        {
            File.WriteAllText(SystemPython(), "программа");
        }
        return Path.Combine(_f.Dir, "system-bin");
    }

    /// <summary>Путь того самого «системного» Python.</summary>
    private string SystemPython() => Path.Combine(_f.Dir, "system-bin", PythonExe);

    /// <summary>Разложить «установленный» пакет: файл-признак внутри его каталога.</summary>
    private void InstallFake(ModelInstallService service, string packageId, string relative)
    {
        var package = service.Packages().Find(packageId)!;
        var path = Path.Combine(service.PackageDir(package),
            relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "файл пакета");
    }

    /// <summary>
    /// Профайл модели с манифестом БЕЗ гигабайтных весов: проверяются пакеты, а качать
    /// ради этого 15 ГБ незачем. Пакеты обучения — как в дистрибутиве.
    /// </summary>
    private void WriteProfile(string modelId) =>
        _f.Files.WriteText(_f.Models.Get(modelId)!.ProfilePath, """
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

    /// <summary>Профайл с настоящей настройкой обучения: команда делает вид, что обучила.</summary>
    private void WriteTrainProfile()
    {
        var command = OperatingSystem.IsWindows()
            ? "mkdir \\\"{output}\\\" 2>nul & echo weights> \\\"{output}\\\\adapter.safetensors\\\""
            : "mkdir -p '{output}' && echo weights > '{output}/adapter.safetensors'";
        _f.Files.WriteText(_f.Models.Get(T2VId)!.ProfilePath, $$"""
            {
              "provider": "comfyui",
              "model": "test",
              "lora": {
                "supported": true,
                "engine": "comfyui",
                "train": {
                  "kind": "process",
                  "packages": ["musubi-tuner", "python"],
                  "dataset": { "kind": "dir", "path": "lora/{object}/dataset",
                               "captions": "txt", "minItems": 1, "width": 768, "height": 512 },
                  "start": { "kind": "process", "command": "{{command}}", "steps": 10 },
                  "wait": { "kind": "process", "timeoutMinutes": 5 },
                  "result": { "kind": "file", "path": "lora/{object}/out/adapter.safetensors",
                              "target": "loras/{object}.safetensors" }
                }
              }
            }
            """);
    }

    /// <summary>
    /// Справочник пакетов с той же тройкой и теми же названиями, но со своими (мёртвыми)
    /// ссылками и БЕЗ проверки версии: «системный» Python проверок — обычный файл, запустить
    /// его нельзя. Сама проверка версии проверяется отдельно, на настоящей программе. Заодно
    /// проверки перестают зависеть от того, что стоит на компьютере, где их гоняют.
    /// </summary>
    private void WriteCatalog() =>
        _f.Files.WriteText(AiModelService.PackagesPath, $$"""
            {
              "packages": [
                { "id": "comfyui", "name": "ComfyUI (portable)", "dir": "ComfyUI",
                  "check": "python.exe",
                  "files": [ { "name": "comfy.zip", "url": "http://127.0.0.1:9/c.zip", "unpack": "zip" } ] },
                { "id": "musubi-tuner", "name": "Musubi Tuner (обучение LoRA)", "dir": "musubi-tuner",
                  "check": "kandinsky5_train_network.py",
                  "files": [ { "name": "musubi.zip", "url": "http://127.0.0.1:9/m.zip", "unpack": "zip" } ] },
                { "id": "python", "name": "Python 3.12 (для обучения LoRA)", "dir": "python",
                  "check": "{{PythonExe}}",
                  "system": { "commands": ["python"] },
                  "files": [ { "name": "p.tar.gz", "url": "http://127.0.0.1:9/p.tar.gz", "unpack": "tar.gz" } ] }
              ]
            }
            """);

    private string ProjectDir()
    {
        var dir = Path.Combine(_f.Dir, "project-folder");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private ObjectItem NewLora(string projectId, string name) =>
        _f.Objects.Create(new ObjectItem
        {
            ProjectId = projectId,
            Name = name,
            Type = ObjectKinds.Lora,
        }, null);

    private void AddFrame(string projectId, string ownerId, string fileName, string caption)
    {
        var rel = "lora-src/" + fileName;
        var abs = Path.Combine(ProjectDir(), "lora-src", fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, "png", new UTF8Encoding(false));
        _f.Objects.Create(new ObjectItem
        {
            ProjectId = projectId,
            ParentId = ownerId,
            Name = Path.GetFileNameWithoutExtension(fileName),
            Type = ObjectKinds.Image,
            PathOrUrl = rel,
            Description = caption,
        }, null);
    }

    private static async Task WaitFinishedAsync(ModelInstallService service, string modelId)
    {
        for (var i = 0; i < 200; i++)
        {
            if (!service.Status(modelId).Running)
            {
                return;
            }
            await Task.Delay(100);
        }
        Assert.Fail("Установка не завершилась за 20 секунд");
    }

    private async Task<ObjectLoraModel> WaitTrainAsync(string loraId)
    {
        for (var i = 0; i < 300; i++)
        {
            var row = _f.Objects.LoraModel(loraId)!;
            if (row.Status != LoraModelStates.Training)
            {
                return row;
            }
            await Task.Delay(100);
        }
        return _f.Objects.LoraModel(loraId)!;
    }
}

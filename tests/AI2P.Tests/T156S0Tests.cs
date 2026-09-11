using AI2P.Connectors;
using AI2P.Core;
using AI2P.Core.Entities;
using AI2P.Storage.Services;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-156-S0 — ЗАПИСЬ ПЛАГИНА-ТРЕНЕРА ПОЯВЛЯЕТСЯ ПРИ УСТАНОВКЕ МОДЕЛИ.
///
/// Заказчик: «при установке софта обучения для модели Кандинский из справочника моделей в
/// списке „плагинов и mcp“ появляется соответствующая запись». Проверяется три вещи:
/// <list type="number">
/// <item>МАНИФЕСТ ТРЕНЕРА (<c>trainer.musubi</c>) читается целиком: две программы (главная —
/// python с диапазоном 3.10–3.12, вторая — каталог скриптов musubi-tuner БЕЗ поиска в PATH) и
/// операция долгого запуска «обучение LoRA» с флагом «один экземпляр» и правилом разбора
/// результата <c>newest</c>;</item>
/// <item>ОТБОР ПЛАГИНА ПО ПАКЕТАМ МОДЕЛИ: тренер накрывает <c>lora.train.packages</c> записи
/// Kandinsky, а конвертор ffmpeg — нет, хотя пакет у него тоже есть;</item>
/// <item>ЗАПИСЬ ЗАВОДИТСЯ ПРИ УСТАНОВКЕ И НЕ ДУБЛИРУЕТСЯ, а у модели без тренера
/// (<c>kind: external</c> с пустой командой) не заводится вовсе.</item>
/// </list>
/// </summary>
public sealed class T156S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static PluginManifest Trainer() =>
        PluginManifest.Parse(PluginSeed.TrainerMusubiJson)
        ?? throw new InvalidOperationException("манифест тренера LoRA негоден");

    private PluginService Plugins => new(_f.Db, _f.Events);

    private TrainerPluginService Service() =>
        new(new ModelInstallService(_f.Models, _f.Files, _f.Events, () => _f.ModelsRepo),
            _f.Files, Plugins);

    /// <summary>Профайл модели с настройкой обучения: пакеты, способ запуска и команда.</summary>
    private static string Profile(string kind, string command, string packages) =>
        $$"""
        {
          "provider": "comfyui",
          "model": "test",
          "install": { "group": "Test", "packages": ["comfyui"] },
          "lora": {
            "supported": true,
            "train": {
              "kind": "{{kind}}",
              "packages": [{{packages}}],
              "start": { "kind": "process", "command": "{{command}}" }
            }
          }
        }
        """;

    // ---------- 1. Манифест тренера ----------

    /// <summary>
    /// МАНИФЕСТ ЧИТАЕТСЯ ЦЕЛИКОМ. Главное здесь — ПОРЯДОК программ: запускает
    /// <c>SoftwareConnector</c> первую (<c>PluginManifest.Software</c>), а все три шага
    /// обучения musubi-tuner — это запуски питона.
    /// </summary>
    [Fact]
    public void The_Trainer_Manifest_Is_Read_Whole()
    {
        var manifest = Trainer();

        Assert.Equal(PluginSeed.TrainerMusubiCode, manifest.Code);
        Assert.Equal(PluginKinds.Gateway, manifest.Kind);
        Assert.Equal(2, manifest.SoftwareList.Count);

        var python = manifest.Software!;
        Assert.Equal("python", python.Id);
        Assert.Equal("python", python.Package);
        Assert.NotNull(python.System);
        // 3.13 не годится: на нём musubi-tuner не собирается (T-4-S0)
        Assert.Equal("3.10", python.System!.MinVersion);
        Assert.Equal("3.12", python.System.MaxVersion);

        var musubi = manifest.SoftwareList[1];
        Assert.Equal("musubi-tuner", musubi.Package);
        // это КАТАЛОГ СКРИПТОВ, искать его в PATH нечем — блока system у него нет вовсе
        Assert.Null(musubi.System);
        Assert.True(musubi.Required);
    }

    /// <summary>
    /// ОПЕРАЦИИ ДОЛГОГО ЗАПУСКА — три шага musubi-tuner подряд, и у обучения стоит «один
    /// экземпляр» (видеокарта одна) и разбор результата «самый свежий файл»: имя файла
    /// адаптера заранее неизвестно.
    /// </summary>
    [Fact]
    public void The_Training_Operation_Is_A_Long_Run()
    {
        var manifest = Trainer();

        Assert.NotNull(manifest.Run);
        Assert.Equal(3, manifest.Run!.Ops.Count);
        Assert.NotNull(manifest.Run.Find("lora.cacheLatents"));
        Assert.NotNull(manifest.Run.Find("lora.cacheText"));

        var train = manifest.Run.Find("lora.train")!;
        Assert.True(train.SingleInstance);
        Assert.Equal(RunResult.KindNewest, train.Result.Kind);
        Assert.Equal(".safetensors", train.OutExt);
        // молчание между эпохами законно: главный предел — тайм-аут МОЛЧАНИЯ, а не длительности
        Assert.Equal(900, train.IdleTimeoutSec);
        Assert.True(train.TimeoutSec >= 86400);
        Assert.Contains("--max_train_steps", train.Args);

        // действие обучения есть, и оно ссылается на эту операцию
        var action = Assert.Single(manifest.Actions, a => a.Op == "lora.train");
        Assert.Equal("musubi_lora_train", action.Tool);
        Assert.True(action.NeedsSoftware);
        // роли у действия нет намеренно: обучение агент инструментом не зовёт — оно задача
        Assert.Equal("", action.Role);
    }

    /// <summary>Манифест тренера входит в сид и ложится в каталог данных.</summary>
    [Fact]
    public void The_Trainer_Manifest_Is_Seeded()
    {
        Assert.Contains(PluginSeed.TrainerMusubiJson, PluginSeed.All);

        PluginSeed.Write(_f.Files);

        Assert.True(File.Exists(_f.Files.Abs(
            PluginManifest.PathOf(PluginSeed.TrainerMusubiCode))));
        Assert.Equal(PluginSeed.TrainerMusubiCode,
            PluginManifest.Read(_f.Files.DataDir, PluginSeed.TrainerMusubiCode)!.Code);
    }

    // ---------- 2. Отбор плагина по пакетам обучения модели ----------

    /// <summary>
    /// ТРЕНЕР НАХОДИТСЯ ПО ПАКЕТАМ МОДЕЛИ, а конвертор — нет: у ffmpeg нет ни этих пакетов,
    /// ни блока долгого запуска. Совпадения ОДНОГО пакета (python нужен многим) не довольно.
    /// </summary>
    [Fact]
    public void The_Trainer_Is_Found_By_The_Model_Packages()
    {
        var trainer = Trainer();
        var ffmpeg = PluginManifest.Parse(PluginSeed.FfmpegJson)!;

        Assert.True(TrainerPluginService.Covers(trainer, ["musubi-tuner", "python"]));
        Assert.False(TrainerPluginService.Covers(ffmpeg, ["musubi-tuner", "python"]));
        // плагин ведёт к python, но не к тренеру — запись по такой модели заводить нельзя
        Assert.False(TrainerPluginService.Covers(trainer, ["musubi-tuner", "python", "comfyui"]));
        Assert.False(TrainerPluginService.Covers(trainer, []));
    }

    /// <summary>
    /// «У МОДЕЛИ ЕСТЬ ЧЕМ ОБУЧАТЬ»: пакеты названы И объявлено, чем запускать. Обучение
    /// <c>external</c> с ПУСТОЙ командой — это «файл человек подкладывает сам», тренера нет.
    /// </summary>
    [Fact]
    public void A_Model_Without_A_Trainer_Has_No_Trainer_Packages()
    {
        Assert.True(ModelInstallManifest.Parse(
            Profile("process", "train.cmd", "\"musubi-tuner\", \"python\""))!.HasTrainer);
        Assert.False(ModelInstallManifest.Parse(
            Profile("external", "", "\"musubi-tuner\", \"python\""))!.HasTrainer);
        Assert.False(ModelInstallManifest.Parse(
            Profile("process", "train.cmd", ""))!.HasTrainer);
    }

    // ---------- 3. Запись заводится и не дублируется ----------

    /// <summary>
    /// ЗАПИСЬ ПОЯВЛЯЕТСЯ ПОСЛЕ УСТАНОВКИ МОДЕЛИ С ПАКЕТАМИ ОБУЧЕНИЯ — в состоянии «объявлен»
    /// (действия справочника и опыт по-прежнему заводит инициализация), и повторная установка
    /// второй записи не заводит.
    /// </summary>
    [Fact]
    public void The_Model_Install_Declares_The_Trainer_Record_Once()
    {
        PluginSeed.Write(_f.Files);
        var manifest = ModelInstallManifest.Parse(
            Profile("process", "train.cmd", "\"musubi-tuner\", \"python\""))!;
        var service = Service();

        var first = service.DeclareForModel(manifest);
        var second = service.DeclareForModel(manifest);

        Assert.Equal(PluginSeed.TrainerMusubiCode, Assert.Single(first));
        Assert.Equal(PluginSeed.TrainerMusubiCode, Assert.Single(second));
        var record = Assert.Single(Plugins.List(),
            p => p.Code == PluginSeed.TrainerMusubiCode);
        Assert.Equal(PluginStates.Declared, record.State);
        Assert.NotEqual("", record.DisplayId);
    }

    /// <summary>У модели без поддержки обучения записи не появляется вовсе.</summary>
    [Fact]
    public void A_Model_Without_Training_Declares_Nothing()
    {
        PluginSeed.Write(_f.Files);
        var manifest = ModelInstallManifest.Parse(
            Profile("external", "", "\"musubi-tuner\", \"python\""))!;

        Assert.Empty(Service().DeclareForModel(manifest));
        Assert.Empty(Plugins.List());
    }
}

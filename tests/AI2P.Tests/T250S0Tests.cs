using System.Text.Json;
using AI2P.Core;
using AI2P.Core.Api;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-250-S0 (аудио-датасет LoRA). Датасет обучения перестал быть «всегда картинками»:
/// у <see cref="LoraDataset"/> и <see cref="LoraDatasetLimits"/> появился вид
/// (<see cref="LoraDatasetMedia"/>), а вместе с ним — свои пределы записи (длительность,
/// частота дискретизации, каналы) вместо ширины с высотой.
///
/// Проверяется то, что расходится молча:
/// <list type="bullet">
/// <item>умолчание вида — «картинки»: все прежние профайлы и все прежние датасеты обязаны
/// вести себя ровно как до правки;</item>
/// <item>у звукового датасета ширина и высота НЕ сверяются вовсе (иначе умолчание
/// 1024×1024 вечно спорило бы с моделью, у которой размера кадра нет);</item>
/// <item>вид датасета и вид модели сверяются между собой — датасет картинок под звуковую
/// модель это не «мелкое расхождение», а другая работа;</item>
/// <item>профайл ACE-Step 1.5 XL: датасет звуковой, а обучение — ПРОВЕРЕННЫЙ отказ
/// (<c>external</c> с пустой командой), см. doc/T-250-S0_аудио_датасет_LoRA.md.</item>
/// </list>
/// </summary>
public sealed class T250S0Tests : IDisposable
{
    private readonly StorageFixture _f = new();

    public T250S0Tests()
    {
        _f.RefData.Seed();
        _f.Models.Seed();
    }

    public void Dispose() => _f.Dispose();

    private string Profile(string name) =>
        _f.Files.ReadText(_f.Models.List().Single(m => m.Name == name).ProfilePath);

    // --- вид датасета в профайле модели ---

    [Fact]
    public void Dataset_Without_Media_Key_Stays_Images()
    {
        var lora = LoraSettings.Parse("""
            { "lora": { "supported": true, "train": { "dataset": { "width": 768 } } } }
            """);
        Assert.Equal(LoraDatasetMedia.Image, lora.Train.Dataset.Media);
        Assert.False(lora.Train.Dataset.IsAudio);
    }

    [Fact]
    public void Audio_Dataset_Limits_Are_Parsed_From_The_Profile()
    {
        var lora = LoraSettings.Parse("""
            {
              "lora": {
                "supported": true,
                "train": {
                  "dataset": {
                    "media": "audio", "minSeconds": 5, "maxSeconds": 240,
                    "sampleRate": 48000, "channels": 2, "minItems": 10,
                    "captions": "txt", "formats": ["wav", "mp3"]
                  }
                }
              }
            }
            """);
        var set = lora.Train.Dataset;
        Assert.True(set.IsAudio);
        Assert.Equal(5, set.MinSeconds);
        Assert.Equal(240, set.MaxSeconds);
        Assert.Equal(48000, set.SampleRate);
        Assert.Equal(2, set.Channels);
        Assert.Equal("txt", set.Captions);
        // у записи нет ширины и высоты — «есть что подставлять» считается по своим полям
        Assert.True(set.HasLimits);
    }

    [Fact]
    public void An_Audio_Model_That_Said_Nothing_Has_No_Limits_To_Take()
    {
        var lora = LoraSettings.Parse("""
            { "lora": { "supported": true, "train": { "dataset": { "media": "audio" } } } }
            """);
        Assert.False(lora.Train.Dataset.HasLimits);
    }

    // --- снимок датасета ---

    [Fact]
    public void Limits_Keep_The_Media_Through_Json()
    {
        var limits = new LoraDatasetLimits
        {
            Media = LoraDatasetMedia.Audio,
            MaxSeconds = 30,
            SampleRate = 24000,
            Channels = 1,
            Format = "flac",
        }.Sane();
        var back = LoraDatasetLimits.Parse(limits.ToJson());
        Assert.True(back.IsAudio);
        Assert.Equal(30, back.MaxSeconds);
        Assert.Equal(24000, back.SampleRate);
        Assert.Equal(1, back.Channels);
        Assert.Equal("flac", back.Format);
    }

    [Fact]
    public void Old_Dataset_Json_Without_Media_Reads_As_Images()
    {
        var back = LoraDatasetLimits.Parse(
            """{"maxWidth":768,"maxHeight":512,"maxKb":2048,"format":"png"}""");
        Assert.False(back.IsAudio);
        Assert.Equal(768, back.MaxWidth);
        // умолчания записи проставлены, но на датасет картинок они не влияют
        Assert.Equal(LoraDatasetLimits.DefaultMaxSeconds, back.MaxSeconds);
    }

    [Fact]
    public void Audio_Format_Is_Normalized_To_Wav_Not_To_Png()
    {
        var limits = new LoraDatasetLimits { Media = LoraDatasetMedia.Audio, Format = "png" }.Sane();
        Assert.Equal("wav", limits.Format);
        // и наоборот: у картинок «wav» осмысленным форматом не становится
        var image = new LoraDatasetLimits { Format = "wav" }.Sane();
        Assert.Equal("png", image.Format);
    }

    [Fact]
    public void FromModel_Takes_The_Media_Of_The_Model()
    {
        var model = new LoraDataset
        {
            Media = LoraDatasetMedia.Audio,
            MaxSeconds = 60,
            SampleRate = 44100,
            Channels = 1,
            Formats = ["mp3", "wav"],
        };
        var limits = LoraDatasetLimits.FromModel(model, new LoraDatasetLimits { MaxWidth = 768 });
        Assert.True(limits.IsAudio);
        Assert.Equal(60, limits.MaxSeconds);
        Assert.Equal(44100, limits.SampleRate);
        Assert.Equal(1, limits.Channels);
        // из перечисленных берётся WAV — он без потерь, как PNG у картинок
        Assert.Equal("wav", limits.Format);
        // пределы картинок остаются рабочими: вид переключают туда и обратно
        Assert.Equal(768, limits.MaxWidth);
    }

    // --- сверка перед обучением ---

    [Fact]
    public void Audio_Dataset_Is_Compared_By_Duration_And_Not_By_Size()
    {
        var model = new LoraDataset
        {
            Media = LoraDatasetMedia.Audio,
            MaxSeconds = 30,
            SampleRate = 24000,
            Channels = 1,
        };
        var limits = new LoraDatasetLimits
        {
            Media = LoraDatasetMedia.Audio,
            MaxWidth = 4096,      // у записи ширины нет — сверять нечего
            MaxHeight = 4096,
            MaxSeconds = 240,
            SampleRate = 48000,
            Channels = 2,
            Format = "wav",
        };
        var problems = limits.Compare(model);
        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.Contains("240") && p.Contains("30"));
        Assert.Contains(problems, p => p.Contains("48") && p.Contains("24"));
    }

    [Fact]
    public void An_Audio_Dataset_Inside_The_Model_Limits_Has_Nothing_To_Ask_About()
    {
        var model = new LoraDataset
        {
            Media = LoraDatasetMedia.Audio,
            MaxSeconds = 240,
            SampleRate = 48000,
            Channels = 2,
            Formats = ["wav", "mp3"],
        };
        var limits = LoraDatasetLimits.FromModel(model);
        Assert.Empty(limits.Compare(model));
    }

    [Fact]
    public void Media_Mismatch_Is_A_Problem_Of_Its_Own()
    {
        var model = new LoraDataset { Media = LoraDatasetMedia.Audio, MaxSeconds = 30 };
        var images = new LoraDatasetLimits().Sane();
        Assert.Contains(images.Compare(model), p => p.Length > 0);
        // а молчащая модель не спорит ни с картинками, ни с записями
        Assert.Empty(images.Compare(new LoraDataset { Media = LoraDatasetMedia.Audio }));
    }

    [Fact]
    public void Image_Comparison_Did_Not_Change()
    {
        var model = new LoraDataset { Width = 768, Height = 512 };
        var limits = new LoraDatasetLimits { MaxWidth = 1024, MaxHeight = 1024 }.Sane();
        Assert.Equal(2, limits.Compare(model).Count);
    }

    // --- наружу: DTO ---

    [Fact]
    public void The_Dto_Carries_The_Media_Both_Ways()
    {
        var limits = new LoraDatasetLimits
        {
            Media = LoraDatasetMedia.Audio, MaxSeconds = 12, Channels = 1, Format = "opus",
        }.Sane();
        var dto = LoraDatasetLimitsDto.Of(limits);
        Assert.True(dto.IsAudio);
        var back = dto.ToLimits();
        Assert.True(back.IsAudio);
        Assert.Equal(12, back.MaxSeconds);
        Assert.Equal("opus", back.Format);
    }

    // --- файлы ---

    [Fact]
    public void Audio_Files_Are_Told_Apart_By_Extension()
    {
        Assert.True(ProjectFiles.IsAudio("refs/vera.wav"));
        Assert.True(ProjectFiles.IsAudio("refs/vera.MP3"));
        Assert.False(ProjectFiles.IsAudio("refs/vera.png"));
        Assert.False(ProjectFiles.IsImage("refs/vera.wav"));
    }

    // --- запись справочника ---

    [Theory]
    [InlineData("ACE-Step-1.5-XL-Turbo")]
    [InlineData("ACE-Step-1.5-XL-Base")]
    [InlineData("ACE-Step-1.5-XL-SFT")]
    public void AceStep_Declares_An_Audio_Dataset_And_An_Honest_Refusal(string name)
    {
        var lora = LoraSettings.Parse(Profile(name));
        Assert.True(lora.Supported);
        var set = lora.Train.Dataset;
        Assert.True(set.IsAudio);
        Assert.Equal(240, set.MaxSeconds);
        Assert.Equal(48000, set.SampleRate);
        Assert.Equal(2, set.Channels);
        Assert.Equal("txt", set.Captions);
        Assert.Contains("wav", set.Formats);
        Assert.True(set.HasLimits);
        // ОТКАЗ, а не пропущенная работа: тренер у ACE-Step есть и идёт на Windows с одной
        // видеокартой, но ему нужны ДРУГИЕ веса (каталог чекпойнтов HuggingFace ~19,9 ГБ),
        // которых установка модели не качает. Пустая команда = кнопка отказывает сразу
        Assert.Equal(LoraTrainKinds.External, lora.Train.Kind);
        Assert.Equal("", lora.Train.Start.Command);
        Assert.Contains("ACE-Step-1.5", lora.Train.DocUrl);
    }

    [Fact]
    public void The_AceStep_Profile_Seed_Was_Raised()
    {
        using var doc = JsonDocument.Parse(Profile("ACE-Step-1.5-XL-Turbo"));
        Assert.True(doc.RootElement.GetProperty("_seed").GetInt32() >= 20);
    }
}

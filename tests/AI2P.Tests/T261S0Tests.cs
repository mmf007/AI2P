using AI2P.Storage;
using Xunit;

namespace AI2P.Tests;

/// <summary>
/// T-261-S0: КОНФЛИКТ РЕПЛИКАЦИИ ФАЙЛОВ ДИСТРИБУТИВА.
///
/// Жалоба заказчика: когда на двух серверах идут обновления программы, репликация иногда
/// объявляет конфликт на записях вида <c>models/profile_6f1a45e0-…-000000000008.json</c>.
///
/// Причина: такой файл — часть ПРОГРАММЫ, а не данные организации. Его пишет сид на каждом
/// сервере сам и перекладывает поверх при подъёме своей версии, а профайлу локальной модели
/// установка вдобавок прописывает команду запуска с АБСОЛЮТНЫМИ путями этого компьютера.
/// Пока версии программы на серверах разные, обе стороны изменены относительно базы сравнения,
/// содержимое разное — сверка честно объявляла конфликт, в котором человеку нечего выбирать.
///
/// Решение: такие файлы не сверяются вовсе. Кастомные записи человека — сверяются, их файла
/// на партнёре взять больше неоткуда.
/// </summary>
public sealed class T261S0Tests
{
    [Theory]
    [InlineData("models/profile_6f1a45e0-0d31-4c65-9a01-000000000008.json")]
    [InlineData("models/scope_6f1a45e0-0d31-4c65-9a01-000000000008.json")]
    [InlineData("models/workflow_6f1a45e0-0d31-4c65-9a01-000000000007.json")]
    [InlineData("models/packages.json")]
    [InlineData("plugins/ffmpeg/plugin.json")]
    [InlineData("packs/style.strict-review/pack.json")]
    public void The_Files_The_Distribution_Writes_Itself_Are_Not_Replicated(string path) =>
        Assert.True(FileManifest.IsDistributionFile(path), path);

    [Theory]
    [InlineData("models/profile_3f2504e0-4f89-11d3-9a0c-0305e82c3301.json")]   // кастомная запись
    [InlineData("models/scope_3f2504e0-4f89-11d3-9a0c-0305e82c3301.json")]
    [InlineData("models/workflow_3f2504e0-4f89-11d3-9a0c-0305e82c3301.json")]
    [InlineData("plugins/ffmpeg/settings.json")]                                // не манифест
    [InlineData("projects/P-1/задача.md")]
    [InlineData("executors/profile_6f1a45e0-0d31-4c65-9a01-000000000008.json")] // не каталог моделей
    public void The_Human_Files_Are_Replicated_As_Before(string path) =>
        Assert.False(FileManifest.IsDistributionFile(path), path);

    /// <summary>Служебные пути (корзина, журналы, архивы, сама база) — прежнее правило,
    /// новое его не подменяет: они исключены СВОЕЙ проверкой.</summary>
    [Fact]
    public void The_Service_Paths_Are_Still_Cut_By_Their_Own_Rule()
    {
        Assert.True(FileManifest.IsServicePath("arc/A-1/ai2p.db"));
        Assert.False(FileManifest.IsDistributionFile("arc/A-1/ai2p.db"));
    }
}

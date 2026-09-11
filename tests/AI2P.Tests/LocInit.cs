using System.Runtime.CompilerServices;
using AI2P.Core;

namespace AI2P.Tests;

/// <summary>
/// Словари приложения для тестов (ТЗ гл. 9, T-180).
/// Тексты сообщений хранилища, коннекторов и API с T-180 лежат в i18n/&lt;язык&gt;.json,
/// а не в коде. В приложении их читает Program при старте; тестам стартовать нечего,
/// поэтому словарь загружается инициализатором сборки — ДО первого теста.
/// Язык — базовый (ru): проверки сравнивают тексты сообщений с русскими.
/// </summary>
internal static class LocInit
{
    [ModuleInitializer]
    internal static void Load() =>
        Loc.Load(Path.Combine(AppContext.BaseDirectory, "i18n"), LocCatalog.BaseLanguage);
}

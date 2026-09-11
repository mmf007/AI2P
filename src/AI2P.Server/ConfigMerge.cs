using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AI2P.Server;

/// <summary>
/// ОБНОВЛЕНИЕ config.json при установке новой версии поверх старой (ТЗ гл. 4.3, гл. 10;
/// этап 46).
///
/// Скрипт установки не трогает рабочий <c>config.json</c> — он кладёт рядом файл новой версии
/// (<see cref="NewConfigName"/>). Слияние делает приложение при следующем старте, и делает его
/// так: <b>берётся новый конфиг, поверх накладывается старый</b>. Значит новые параметры
/// приходят с умолчаниями дистрибутива, а всё, что пользователь настраивал, остаётся как было.
/// Результат записывается в рабочий <c>config.json</c>, файл новой версии переименовывается
/// в <c>*.applied</c> — чтобы слияние не повторялось и чтобы было видно, что оно было.
///
/// Слияние идёт по JSON, а не по объекту C#: в конфиге бывают разделы, которых код ещё не знает
/// (заготовки вроде <c>ui.https</c>), и терять их при обновлении нельзя.
/// </summary>
public static class ConfigMerge
{
    /// <summary>Имя файла конфигурации НОВОЙ версии, который кладёт скрипт установки.</summary>
    public const string NewConfigName = "config.new.json";

    /// <summary>Суффикс, который получает файл новой версии после слияния.</summary>
    public const string AppliedSuffix = ".applied";

    /// <summary>
    /// Слить конфигурацию, если рядом лежит файл новой версии. Возвращает true, если слияние
    /// выполнено (значит config.json переписан и его надо перечитать).
    /// </summary>
    /// <param name="configPath">Путь к рабочему config.json.</param>
    public static bool ApplyPending(string configPath) =>
        ApplyPending(configPath, Path.GetDirectoryName(Path.GetFullPath(configPath))!);

    /// <summary>
    /// То же, когда дистрибутив и рабочие файлы РАЗВЕДЕНЫ по разным каталогам (T-287):
    /// программа стоит в <c>C:\Program Files\AI2P</c>, а <c>config.json</c> живёт в каталоге
    /// данных. Тогда <c>config.new.json</c> лежит у программы и переименовать его нельзя —
    /// каталог только для чтения.
    ///
    /// Поэтому «уже применено» отмечается не переименованием файла дистрибутива, а его
    /// КОПИЕЙ рядом с рабочим конфигом (<c>config.new.json.applied</c>): совпало содержимое —
    /// сливать нечего, различается — приехала новая версия. Признак получается тот же самый
    /// («слияние ровно один раз на версию»), но он не требует прав на каталог программы.
    /// </summary>
    /// <param name="configPath">Путь к рабочему config.json.</param>
    /// <param name="distDir">Каталог дистрибутива, где лежит config.new.json.</param>
    public static bool ApplyPending(string configPath, string distDir)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(configPath))!;
        var incoming = Path.Combine(Path.GetFullPath(distDir), NewConfigName);
        if (!File.Exists(incoming))
        {
            return false;
        }
        var sameDir = AppHome.SamePath(dir, distDir);
        // рабочего конфига нет (первая установка) — файл новой версии просто становится им
        if (!File.Exists(configPath))
        {
            if (sameDir)
            {
                File.Move(incoming, configPath);
            }
            else
            {
                File.Copy(incoming, configPath);
                MarkApplied(dir, File.ReadAllText(incoming));
            }
            return true;
        }
        var incomingText = File.ReadAllText(incoming);
        if (!sameDir && File.Exists(AppliedMarker(dir)) &&
            File.ReadAllText(AppliedMarker(dir)) == incomingText)
        {
            return false;   // эта версия дистрибутива уже слита
        }
        var merged = Merge(incomingText, File.ReadAllText(configPath));
        Write(configPath, merged);
        if (!sameDir)
        {
            MarkApplied(dir, incomingText);
            return true;
        }
        var applied = incoming + AppliedSuffix;
        if (File.Exists(applied))
        {
            File.Delete(applied);
        }
        File.Move(incoming, applied);
        return true;
    }

    /// <summary>Отметка «конфигурация этой версии дистрибутива уже применена» рядом
    /// с рабочим config.json (T-287).</summary>
    public static string AppliedMarker(string configDir) =>
        Path.Combine(configDir, NewConfigName + AppliedSuffix);

    /// <summary>Поставить отметку: рядом с рабочим конфигом ложится КОПИЯ применённого
    /// файла дистрибутива. Она же — то, что человек может сравнить глазами.</summary>
    public static void MarkApplied(string configDir, string incomingText) =>
        File.WriteAllText(AppliedMarker(configDir), incomingText,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    /// <summary>
    /// Слияние двух JSON-конфигураций: основа — <paramref name="newJson"/> (в нём есть все
    /// параметры новой версии с умолчаниями), поверх кладётся <paramref name="oldJson"/>
    /// (рабочие значения пользователя). Объекты сливаются рекурсивно, значения и массивы
    /// заменяются целиком: массив в конфиге — это одна настройка, а не набор независимых.
    /// </summary>
    public static string Merge(string newJson, string oldJson)
    {
        var target = Parse(newJson) ?? new JsonObject();
        var source = Parse(oldJson);
        if (source is not null)
        {
            MergeInto(target, source);
        }
        return target.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    private static void MergeInto(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value is JsonObject nested && target[key] is JsonObject existing)
            {
                MergeInto(existing, nested);
                continue;
            }
            // значение пользователя побеждает; параметр, которого в новой версии нет,
            // сохраняется — вдруг это заготовка, о которой код ещё не знает
            target[key] = value?.DeepClone();
        }
    }

    private static JsonObject? Parse(string json)
    {
        try
        {
            return JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Write(string path, string json) =>
        File.WriteAllText(path, json + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

namespace AI2P.Core;

/// <summary>
/// Режим работы навыка «вход → выход» (T-257, разбор T-251). У медиа-моделей режим принято
/// называть аббревиатурой: t2i, i2i, t2v, i2v, flf2v, a2v. Заводить под них ТРЕТЬЮ категорию
/// кодов навыков (как язык у <c>code-write-cs</c>) смысла нет: режим ввода уже закодирован
/// ВТОРЫМ сегментом — действием (<c>image-generate</c> = t2i, <c>video-animate</c> = i2v),
/// а автоподбор снимает уточняющие сегменты по одному, поэтому <c>video-generate-t2v</c> был бы
/// синонимом <c>video-generate</c> и ни на что не влиял.
///
/// Различать режимы должна пара <c>inputs</c>/<c>outputs</c> декларации возможностей (ТЗ п. 7.3):
/// она описывает вход и выход машиночитаемо. Здесь лежит вторая половина этой пары — какой
/// НОСИТЕЛЬ навык берёт на вход и какой отдаёт на выходе. Из двух величин получается проверка
/// совместимости: t2v-модель (<c>inputs: ["text/plain"]</c>) не будет предложена на задачу с
/// навыком <c>video-animate</c>, потому что i2v требует на входе изображение.
///
/// Носитель — часть кода формата до косой черты (<c>image/png</c> → <c>image</c>), то есть
/// проверка идёт по ТИПУ, а не по конкретному формату: модель, принимающая только
/// <c>image/png</c>, для «нужно изображение» подходит. Навыки, которых здесь нет
/// (code-*, text-*, analyze-*), не проверяются вовсе — у них вход и выход всегда текст.
/// </summary>
public static class SkillIo
{
    /// <summary>Носители (типы форматов справочника io formats).</summary>
    public const string Text = "text";
    public const string Image = "image";
    public const string Video = "video";
    public const string Audio = "audio";

    /// <summary>3D-модель: коды справочника <c>model/3d</c>, <c>model/glb</c>.</summary>
    public const string Model = "model";

    /// <summary>
    /// Режимы медиа-навыков стартового набора. Список НЕ уходит в словарь по языкам (T-191):
    /// это не текст для человека, а техническая карта по кодам навыков — код один на все языки.
    /// </summary>
    private static readonly Dictionary<string, (string In, string Out)> Modes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["image-generate"] = (Text, Image),     // t2i
            ["image-concept"] = (Text, Image),      // t2i
            ["image-photo"] = (Text, Image),        // t2i
            ["image-text"] = (Text, Image),         // t2i
            ["image-edit"] = (Image, Image),        // i2i
            ["image-inpaint"] = (Image, Image),     // inpaint
            ["video-generate"] = (Text, Video),     // t2v
            ["video-animate"] = (Image, Video),     // i2v
            ["video-keyframes"] = (Image, Video),   // flf2v
            ["video-edit"] = (Video, Video),        // v2v
            ["video-extend"] = (Video, Video),      // v2v
            ["video-restyle"] = (Video, Video),     // v2v
            ["video-lipsync"] = (Audio, Video),     // a2v
            ["audio-song"] = (Text, Audio),         // t2a
            ["audio-music"] = (Text, Audio),        // t2a
            ["audio-speech"] = (Text, Audio),       // t2s
            ["3d-generate"] = (Text, Model),        // t23d
            ["3d-environment"] = (Text, Model),     // t23d
            ["3d-image"] = (Image, Model),          // i23d
            ["3d-texture"] = (Model, Model),
            ["3d-animation"] = (Model, Model),
        };

    /// <summary>
    /// Носители навыка; null — режим не описан (навык не медийный либо заведён пользователем),
    /// и проверять совместимость нечем. Уточняющие сегменты кода снимаются по одному, как в
    /// автоподборе (<c>image-generate-anime</c> — тот же t2i, что <c>image-generate</c>).
    /// </summary>
    public static (string In, string Out)? For(string skillCode)
    {
        var name = skillCode.Trim();
        if (name.Length == 0)
        {
            return null;
        }
        if (Modes.TryGetValue(name, out var mode))
        {
            return mode;
        }
        var parts = name.Split('-');
        for (var take = parts.Length - 1; take >= 2; take--)
        {
            if (Modes.TryGetValue(string.Join('-', parts.Take(take)), out mode))
            {
                return mode;
            }
        }
        return null;
    }

    /// <summary>
    /// Принимает ли объявленный в декларации набор форматов носитель <paramref name="carrier"/>.
    /// Пустой набор — «не объявлено»: судить не по чему, считаем, что подходит (иначе проверка
    /// отбраковывала бы всех, у кого декларация не заполнена). Код <c>*</c> и <c>*/*</c> —
    /// «любой формат».
    /// </summary>
    public static bool Supports(IReadOnlyCollection<string> formats, string carrier)
    {
        if (formats.Count == 0)
        {
            return true;
        }
        foreach (var format in formats)
        {
            var code = format.Trim();
            if (code is "*" or "*/*")
            {
                return true;
            }
            var slash = code.IndexOf('/');
            var type = slash < 0 ? code : code[..slash];
            if (string.Equals(type, carrier, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Совместим ли исполнитель с навыком по форматам: вход навыка обязан приниматься его
    /// <c>inputs</c>, выход — выдаваться его <c>outputs</c>. Режим навыка не описан — совместим.
    /// </summary>
    public static bool Fits(string skillCode, IReadOnlyCollection<string> inputs,
        IReadOnlyCollection<string> outputs)
    {
        if (For(skillCode) is not { } mode)
        {
            return true;
        }
        return Supports(inputs, mode.In) && Supports(outputs, mode.Out);
    }
}

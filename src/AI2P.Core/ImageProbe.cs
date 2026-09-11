namespace AI2P.Core;

/// <summary>
/// РАЗМЕР КАРТИНКИ В ПИКСЕЛЯХ ПО ЗАГОЛОВКУ ФАЙЛА (T-12-S1).
///
/// Нужен ровно в одном месте: проверить присланный кадр датасета LoRA — не больше ли он
/// того, что позволено настройками. Резать, поворачивать и пережимать картинки умеет
/// браузер холстом (canvas), и он это и делает; серверу остаётся ПРОВЕРКА, а для неё
/// достаточно нескольких байтов заголовка. Поэтому здесь нет ни декодирования, ни
/// библиотеки работы с изображениями — их отсутствие и есть смысл этого файла: тянуть в
/// проект ImageSharp ради двух чисел незачем, а System.Drawing на Linux не работает вовсе.
///
/// Разбираются те форматы, в которые мы сами и переводим кадр: PNG и JPEG. Всё остальное
/// (и повреждённый заголовок) — <c>null</c>: «размер неизвестен», и вызывающий решает сам.
/// </summary>
public static class ImageProbe
{
    /// <summary>Ширина и высота картинки; null — формат не наш или заголовок оборван.</summary>
    public static (int Width, int Height)? Size(byte[] bytes)
    {
        var png = Png(bytes);
        return png ?? Jpeg(bytes);
    }

    /// <summary>Формат по подписи файла: «png», «jpeg» либо пусто.</summary>
    public static string Format(byte[] bytes)
    {
        if (IsPng(bytes))
        {
            return "png";
        }
        return bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF
            ? "jpeg"
            : "";
    }

    private static bool IsPng(byte[] b) =>
        b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
        && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;

    /// <summary>PNG: сразу за подписью идёт IHDR, в нём первыми — ширина и высота (big-endian).</summary>
    private static (int, int)? Png(byte[] b)
    {
        if (!IsPng(b) || b.Length < 24 || b[12] != 'I' || b[13] != 'H' || b[14] != 'D' || b[15] != 'R')
        {
            return null;
        }
        return (Be32(b, 16), Be32(b, 20));
    }

    /// <summary>
    /// JPEG: заголовок с размерами лежит в маркере SOF (0xC0…0xCF, кроме 0xC4/0xC8/0xCC —
    /// это таблицы, а не кадр). До него идёт цепочка маркеров, каждый со своей длиной.
    /// </summary>
    private static (int, int)? Jpeg(byte[] b)
    {
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8)
        {
            return null;
        }
        var at = 2;
        while (at + 3 < b.Length)
        {
            if (b[at] != 0xFF)
            {
                at++;                       // мусор между маркерами — ищем следующий 0xFF
                continue;
            }
            var marker = b[at + 1];
            if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                at += 2;                    // маркеры без длины
                continue;
            }
            if (marker == 0xD9 || marker == 0xDA)
            {
                return null;                // конец картинки или начало данных — размеров не было
            }
            var length = Be16(b, at + 2);
            if (length < 2)
            {
                return null;
            }
            var isFrame = marker >= 0xC0 && marker <= 0xCF
                          && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (isFrame)
            {
                // в SOF: 1 байт точности, затем высота и ширина по два байта
                return at + 9 < b.Length ? (Be16(b, at + 7), Be16(b, at + 5)) : null;
            }
            at += 2 + length;
        }
        return null;
    }

    private static int Be32(byte[] b, int at) =>
        (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];

    private static int Be16(byte[] b, int at) => (b[at] << 8) | b[at + 1];
}

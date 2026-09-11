namespace AI2P.Core;

/// <summary>
/// Размеры и длительности словами (T-180). До T-180 «ГБ», «МБ», «с», «мин» были вписаны
/// в разметку двух диалогов и настроек — одинаково и по-русски; теперь единицы берутся
/// из словарей (ТЗ гл. 9), а перевод правится без пересборки.
/// </summary>
public static class Units
{
    /// <summary>Размер файла: 1,5 ГБ / 320 МБ / 12,0 КБ / 900 Б.</summary>
    public static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => Loc.T("unit.size.gb", bytes / (double)(1L << 30)),
        >= 1L << 20 => Loc.T("unit.size.mb", bytes / (double)(1L << 20)),
        >= 1L << 10 => Loc.T("unit.size.kb", bytes / (double)(1L << 10)),
        _ => Loc.T("unit.size.b", bytes),
    };

    /// <summary>Длительность: до минуты — секундами, дальше «5 мин 07 с».</summary>
    public static string Seconds(int seconds) => seconds >= 60
        ? Loc.T("unit.minSec", seconds / 60, seconds % 60)
        : Loc.T("unit.sec", seconds);
}

namespace AI2P.Core.Events;

/// <summary>Вид операции над строкой в журнале изменений (ТЗ п. 6.1, этап 43).</summary>
public static class ChangeOps
{
    /// <summary>Строка создана или изменена — в payload вся строка целиком.</summary>
    public const string Upsert = "upsert";

    /// <summary>Строка удалена физически — в payload только колонки первичного ключа.
    /// Мягкое удаление (<c>deleted_at</c>) едет обычным <see cref="Upsert"/>.</summary>
    public const string Delete = "delete";
}

/// <summary>
/// Одно изменение строки — единица репликации (ТЗ п. 6.1, гл. 6; этап 43).
///
/// Лежит в Core, а не в хранилище: этими же записями серверы обмениваются по HTTP
/// (раздел <c>/api/cluster/repl</c>), поэтому тип нужен обеим сторонам.
/// </summary>
public sealed class RowChange
{
    /// <summary>Номер в журнале ТОГО узла, у которого запись прочитана: это и есть курсор.
    /// При переносе на другой сервер номер меняется — он локальный.</summary>
    public long Seq { get; set; }

    /// <summary>Имя таблицы.</summary>
    public string Table { get; set; } = "";

    /// <summary>Первичный ключ строки; составной склеен через <c>|</c> в порядке ключа.</summary>
    public string Pk { get; set; } = "";

    public string Op { get; set; } = ChangeOps.Upsert;

    /// <summary>Часы LWW: время изменения У АВТОРА, UTC ISO 8601. При пересылке не меняется —
    /// иначе строка «молодела» бы на каждом транзите и затирала более свежие правки.</summary>
    public string Ts { get; set; } = "";

    /// <summary>Узел-АВТОР изменения (не тот, кто переслал). Им же гасится эхо: обратно
    /// автору его собственные изменения не отдаются. Совпадает с внутренним ключом (unid)
    /// сервера — узел и сервер это одно и то же (ТЗ гл. 6).</summary>
    public string NodeId { get; set; } = "";

    /// <summary>Строка целиком (<see cref="ChangeOps.Upsert"/>) либо её ключ (delete).</summary>
    public string PayloadJson { get; set; } = "{}";
}

/// <summary>
/// ИЗМЕНЕНИЕ, КОТОРОЕ ПРИНЯТЬ НЕ УДАЛОСЬ (T-160): база отвергла строку по ограничению
/// целостности. Раньше такая строка роняла всю пачку и весь сеанс — организация оставалась
/// скопированной наполовину, а до файлов (описания задач, критерии, результаты) дело не
/// доходило вовсе. Теперь строка откладывается в очередь повтора и называется словами.
/// </summary>
public sealed class RowFailure
{
    public RowChange Change { get; set; } = new();

    /// <summary>Причина от SQLite: «FOREIGN KEY constraint failed», «UNIQUE constraint
    /// failed: skills.name» и т. п.</summary>
    public string Reason { get; set; } = "";

    /// <summary>Строка человеку: таблица, ключ и причина.</summary>
    public string Text => $"{Change.Table} {Change.Pk}: {Reason}";
}

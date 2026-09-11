using System.Text;

namespace AI2P.Storage;

/// <summary>
/// Файловое хранилище проекта (ТЗ п. 6.4.4): тела объектов — файлы, в БД — относительные пути.
/// Все пути в БД — относительно dataDir; текстовые файлы — UTF-8 без BOM (гл. 13).
/// </summary>
public sealed class FileStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string DataDir { get; }

    public FileStore(string dataDir) => DataDir = Path.GetFullPath(dataDir);

    /// <summary>Абсолютный путь из относительного (как хранится в БД).</summary>
    public string Abs(string relativePath) => Path.GetFullPath(Path.Combine(DataDir, relativePath));

    /// <summary>Путь внутри dataDir — защита от выхода наружу через «..» (для HTTP-отдачи файлов).</summary>
    public bool IsInside(string relativePath) =>
        Abs(relativePath).StartsWith(DataDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Сохранить загруженный файл (картинка из буфера обмена MD-редактора, гл. 11)
    /// в uploads/ проекта; возвращает относительный путь для ссылки в Markdown.
    /// </summary>
    public string SaveUpload(string? projectSlug, string fileName, byte[] bytes)
    {
        var safeName = SanitizeFileName(fileName);
        var rel = Path.Combine("projects", projectSlug ?? "_no_project", "uploads",
            $"{Guid.NewGuid().ToString("N")[..8]}-{safeName}");
        var abs = Abs(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllBytes(abs, bytes);
        return Rel(rel);
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName.Trim());
        var sb = new StringBuilder();
        foreach (var ch in name)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_');
        }
        return sb.Length == 0 ? "file" : sb.ToString();
    }

    /// <summary>Каталог задачи: projects/&lt;slug&gt;/tasks/&lt;display_id&gt;; без проекта — projects/_no_project/… (шаблоны).</summary>
    public string TaskDirRel(string? projectSlug, string taskDisplayId) =>
        Path.Combine("projects", projectSlug ?? "_no_project", "tasks", taskDisplayId);

    /// <summary>Записать описание задачи (description.md); возвращает относительный путь для БД.</summary>
    public string WriteTaskDescription(string? projectSlug, string taskDisplayId, string markdown)
    {
        var rel = Path.Combine(TaskDirRel(projectSlug, taskDisplayId), "description.md");
        WriteText(rel, markdown);
        return Rel(rel);
    }

    /// <summary>Записать критерии приёмки (acceptance.md); возвращает относительный путь для БД.</summary>
    public string WriteTaskAcceptance(string? projectSlug, string taskDisplayId, string markdown)
    {
        var rel = Path.Combine(TaskDirRel(projectSlug, taskDisplayId), "acceptance.md");
        WriteText(rel, markdown);
        return Rel(rel);
    }

    /// <summary>Записать результат задания в artifacts/ задачи; возвращает относительный путь.</summary>
    public string WriteTaskArtifact(string? projectSlug, string taskDisplayId, string fileName, string text)
    {
        var rel = Path.Combine(TaskDirRel(projectSlug, taskDisplayId), "artifacts", fileName);
        WriteText(rel, text);
        return Rel(rel);
    }

    /// <summary>
    /// Относительный путь файла в artifacts/ задачи — БЕЗ записи и без проверки, что файл
    /// есть (T-130-S0): карточка заводит ручной результат у задачи, у которой результата
    /// не было вовсе, и путь ей нужен до первой записи.
    /// </summary>
    public string TaskArtifactRel(string? projectSlug, string taskDisplayId, string fileName) =>
        Rel(Path.Combine(TaskDirRel(projectSlug, taskDisplayId), "artifacts", SanitizeFileName(fileName)));

    /// <summary>Записать бинарный результат задания (видео/картинка медиа-модели, ТЗ v1.41)
    /// в artifacts/ задачи; возвращает относительный путь.</summary>
    public string WriteTaskArtifactBytes(string? projectSlug, string taskDisplayId, string fileName, byte[] bytes)
    {
        var rel = Path.Combine(TaskDirRel(projectSlug, taskDisplayId), "artifacts", SanitizeFileName(fileName));
        var abs = Abs(rel);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllBytes(abs, bytes);
        return Rel(rel);
    }

    public string ReadText(string relativePath)
    {
        var abs = Abs(relativePath);
        return File.Exists(abs) ? File.ReadAllText(abs, Encoding.UTF8) : "";
    }

    public void WriteText(string relativePath, string text)
    {
        var abs = Abs(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, text, Utf8NoBom);
    }

    /// <summary>Список артефактов задачи — относительные пути.</summary>
    public List<string> ListTaskArtifacts(string? projectSlug, string taskDisplayId)
    {
        var dir = Abs(Path.Combine(TaskDirRel(projectSlug, taskDisplayId), "artifacts"));
        if (!Directory.Exists(dir))
        {
            return [];
        }
        return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(Rel)
            .OrderBy(p => p)
            .ToList();
    }

    /// <summary>
    /// Мягкое удаление файлов задачи: каталог переносится в .trash/ проекта (ТЗ п. 6.4.4).
    /// </summary>
    public void TrashTaskDir(string? projectSlug, string taskDisplayId)
    {
        var src = Abs(TaskDirRel(projectSlug, taskDisplayId));
        if (!Directory.Exists(src))
        {
            return;
        }
        var trashDir = Abs(Path.Combine("projects", projectSlug ?? "_no_project", ".trash"));
        Directory.CreateDirectory(trashDir);
        var dst = Path.Combine(trashDir, taskDisplayId);
        // при повторном удалении того же display_id — суффикс, чтобы не затирать
        for (var i = 2; Directory.Exists(dst); i++)
        {
            dst = Path.Combine(trashDir, $"{taskDisplayId}~{i}");
        }
        Directory.Move(src, dst);
    }

    /// <summary>
    /// Мягкое удаление файлов uploads/ (вставки MD-редактора), на которые больше нет ссылок
    /// (каскад при удалении задачи, ТЗ v1.28, todo30): переносятся в .trash/uploads/ проекта.
    /// </summary>
    public void TrashUploads(string? projectSlug, IEnumerable<string> relPaths)
    {
        var uploadsDir = Abs(Path.Combine("projects", projectSlug ?? "_no_project", "uploads"));
        var trashDir = Abs(Path.Combine("projects", projectSlug ?? "_no_project", ".trash", "uploads"));
        foreach (var rel in relPaths)
        {
            var src = Abs(rel);
            // страховка: трогаем только файлы каталога uploads/ этого проекта
            if (!File.Exists(src)
                || !src.StartsWith(uploadsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            Directory.CreateDirectory(trashDir);
            var dst = Path.Combine(trashDir, Path.GetFileName(src));
            for (var i = 2; File.Exists(dst); i++)
            {
                dst = Path.Combine(trashDir, $"{Path.GetFileNameWithoutExtension(src)}~{i}{Path.GetExtension(src)}");
            }
            File.Move(src, dst);
        }
    }

    /// <summary>
    /// Переименовать каталог хранилища проекта (смена названия проекта, ТЗ v1.17):
    /// projects/&lt;oldSlug&gt; → projects/&lt;newSlug&gt;. Старого каталога нет — просто создаётся новый.
    /// </summary>
    public void RenameProjectDir(string oldSlug, string newSlug)
    {
        var src = Abs(Path.Combine("projects", oldSlug));
        var dst = Abs(Path.Combine("projects", newSlug));
        if (Directory.Exists(src))
        {
            Directory.Move(src, dst);
        }
        else
        {
            EnsureProjectDirs(newSlug);
        }
    }

    /// <summary>Создать каркас каталога проекта (п. 6.4.4).</summary>
    public void EnsureProjectDirs(string projectSlug)
    {
        var root = Abs(Path.Combine("projects", projectSlug));
        Directory.CreateDirectory(Path.Combine(root, "tasks"));
        Directory.CreateDirectory(Path.Combine(root, "objects"));
        Directory.CreateDirectory(Path.Combine(root, "experience", "lessons"));
    }

    /// <summary>Относительный путь для хранения в БД (прямые слэши — переносимость между ОС).</summary>
    private string Rel(string path)
    {
        var abs = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(DataDir, path));
        return Path.GetRelativePath(DataDir, abs).Replace('\\', '/');
    }
}

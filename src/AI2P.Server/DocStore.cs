using AI2P.Core;
using AI2P.Core.Api;

namespace AI2P.Server;

/// <summary>
/// ДОКУМЕНТАЦИЯ, ПОСТАВЛЯЕМАЯ С ПРИЛОЖЕНИЕМ (ТЗ гл. 14, задания todo47, todo47_2).
///
/// Каталог <c>AI2P_app/doc/</c> целиком копируется в релизную выкладку (там он ложится
/// рядом с приложением как <c>doc/</c>), а приложение показывает документы прямо в UI.
/// Раскладка — по языкам, внутри языка — по РАЗДЕЛАМ справочников:
/// <code>
/// doc/
/// ├── ru/models/&lt;название модели&gt;.md
/// ├── ru/import/&lt;код вида источника&gt;.md
/// └── en/…
/// </code>
///
/// Имя файла — НАЗВАНИЕ модели из справочника (у импортов — код вида, <c>trello</c>):
/// оно уникально, стабильно у моделей дистрибутива и читается человеком, который этот
/// документ пишет. Номер (<c>M-3</c>) для этого не годится — он выдаётся счётчиком при
/// заполнении справочника и на разных установках у одной модели разный.
///
/// Языковой отбор: язык интерфейса → русский → английский. Так кнопка «i» в форме
/// не приводит на пустой экран, пока перевод не написан.
/// </summary>
public sealed class DocStore
{
    /// <summary>Раздел документации — подкаталог внутри языка.</summary>
    public const string ModelsSection = "models";

    /// <summary>Раздел документации справочника импортов (todo47_2).</summary>
    public const string ImportSection = "import";

    /// <summary>Языки в порядке отката, если документа на языке интерфейса нет.</summary>
    private static readonly string[] Fallback = ["ru", "en"];

    private readonly Func<string> _configuredDir;
    private readonly Func<string> _language;

    /// <param name="configuredDir">Значение <c>storage.docDir</c>; пусто — искать самим.</param>
    /// <param name="language">Язык интерфейса (ТЗ гл. 9).</param>
    public DocStore(Func<string> configuredDir, Func<string> language)
    {
        _configuredDir = configuredDir;
        _language = language;
    }

    /// <summary>
    /// Корень документации; пусто — не найден. У релизной и отладочной сборки он в РАЗНЫХ
    /// местах (todo47_2), поэтому порядок поиска такой:
    /// <list type="number">
    /// <item>настройка <c>storage.docDir</c>, если задана;</item>
    /// <item>каталог <c>doc</c> рядом с приложением — так лежит в релизной выкладке
    /// (<c>publish</c> копирует туда <c>AI2P_app/doc</c> целиком);</item>
    /// <item>вверх по дереву каталогов — так лежит при запуске из исходников:
    /// приложение собирается в <c>AI2P_app/src/AI2P.Server/bin/…</c>, а документация
    /// живёт в <c>AI2P_app/doc</c>.</item>
    /// </list>
    /// </summary>
    public string Root()
    {
        var configured = _configuredDir().Trim();
        if (configured.Length > 0)
        {
            return Directory.Exists(configured) ? Path.GetFullPath(configured) : "";
        }
        var near = Path.Combine(AppContext.BaseDirectory, "doc");
        if (Directory.Exists(near))
        {
            return Path.GetFullPath(near);
        }
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        // вверх ограниченно: бесконечно ползти к корню диска и подхватить чужой doc/ нельзя
        for (var depth = 0; dir is not null && depth < 8; depth++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "doc");
            // признак «наш»: внутри есть языковые подкаталоги
            if (Directory.Exists(Path.Combine(candidate, "ru"))
                || Directory.Exists(Path.Combine(candidate, "en")))
            {
                return Path.GetFullPath(candidate);
            }
        }
        return "";
    }

    /// <summary>
    /// Первая страница документации — с неё начинается чтение (T-17-S1). Это же имя носит
    /// оглавление любого раздела, поэтому у каждого языка оно обязано существовать.
    /// </summary>
    public const string StartPage = "README.md";

    /// <summary>
    /// Страница, на которую ведёт кнопка «Документация» (T-51-S0). У языка их две:
    /// <c>README.md</c> — краткое описание системы (то, что человек видит на GitHub), и
    /// <c>index.md</c> — ОГЛАВЛЕНИЕ. Кнопка ведёт на оглавление: читать документацию
    /// начинают со списка страниц, а не с рекламной шапки. Языка без <c>index.md</c>
    /// это не касается — он открывается на своём <c>README.md</c>, как раньше.
    /// </summary>
    public const string IndexPage = "index.md";

    /// <summary>
    /// Документ модели по её названию (раздел <c>models</c>).
    /// </summary>
    public ModelDocDto ModelDoc(string modelName) => Doc(ModelsSection, modelName);

    /// <summary>
    /// СТРАНИЦА ДОКУМЕНТАЦИИ ПО ПУТИ (T-17-S1) — то, чем живёт закладка «Документация».
    /// Разделы и документы модели адресуются справочником, а чтение подряд — обычным путём
    /// внутри языкового каталога: <c>README.md</c>, <c>models/README.md</c>. По таким же
    /// путям идут внутренние ссылки самих документов, поэтому переход по ссылке — это
    /// просто следующий вызов с новым путём.
    ///
    /// Пустой путь — первая страница (<see cref="IndexPage"/>, а без неё
    /// <see cref="StartPage"/>). Языковой отбор тот же, что
    /// у документов справочников: язык интерфейса → русский → английский, поэтому ссылка
    /// на ещё не переведённую страницу приводит не в пустоту, а к оригиналу.
    /// </summary>
    /// <param name="relPath">Путь внутри языкового каталога; разделитель — «/» или «\».</param>
    public DocPageDto Page(string? relPath)
    {
        var dto = new DocPageDto();
        var root = Root();
        if (root.Length == 0)
        {
            dto.Hint = Loc.T("msg.doc.1");
            return dto;
        }
        var candidates = PageCandidates(relPath);
        foreach (var lang in Languages())
        {
            foreach (var candidate in candidates)
            {
                // имя ищется БЕЗ УЧЁТА РЕГИСТРА и приводится к настоящему имени на диске
                // (T-18-S1): файл оглавления называется README.md, а в ссылке его пишут как
                // придётся — на Windows это безразлично, на Linux файловая система
                // регистрозависима, и «readme.md» там просто не нашёлся бы
                var rel = FindOnDisk(root, lang, candidate);
                if (rel is null)
                {
                    continue;
                }
                var path = Path.Combine(root, lang, rel.Replace('/', Path.DirectorySeparatorChar));
                // путь собран из проверенных сегментов, но сверка с корнем — последняя
                // застава: наружу документации не выходит ничто (ТЗ гл. 12)
                if (!Inside(root, path) || !File.Exists(path))
                {
                    continue;
                }
                try
                {
                    dto.Text = File.ReadAllText(path);
                }
                catch (IOException ex)
                {
                    dto.Hint = ex.Message;
                    return dto;
                }
                dto.Found = true;
                dto.Language = lang;
                dto.RelPath = rel;
                dto.Path = path;
                return dto;
            }
        }
        dto.RelPath = candidates[0];
        dto.Path = Path.Combine(root, _language(), candidates[0].Replace('/', Path.DirectorySeparatorChar));
        dto.Hint = Loc.T("msg.doc.3", dto.Path);
        return dto;
    }

    /// <summary>
    /// КАРТИНКА ДОКУМЕНТАЦИИ (T-97-S0) — абсолютный путь файла на диске либо null.
    ///
    /// Картинки лежат рядом с текстами (<c>doc/images/ai2p-logo.png</c> — она общая для всех
    /// языков, поэтому и стоит ВЫШЕ языкового каталога), а документ ссылается на них
    /// относительным путём. Браузеру такой путь ничего не говорит: страница показывается
    /// внутри приложения, и он разворачивает его по адресу приложения. Поэтому в готовом
    /// HTML адрес картинки переписывается на этот эндпойнт, а путь здесь считается
    /// ОТ КОРНЯ документации — иначе общую картинку было бы не достать из языкового каталога.
    ///
    /// Наружу корня документации не выпускаем ничто (гл. 12): «..» из пути выброшены
    /// (<see cref="NormalizePath"/>), собранный путь сверяется с корнем, а отдаётся только
    /// файл-КАРТИНКА — читать этим эндпойнтом что-нибудь ещё нельзя.
    /// </summary>
    /// <param name="relPath">Путь от корня документации: <c>images/ai2p-logo.png</c>.</param>
    public string? ImageFile(string? relPath)
    {
        var root = Root();
        if (root.Length == 0)
        {
            return null;
        }
        var rel = NormalizePath(relPath);
        if (rel.Length == 0
            || !ImageExtensions.Contains(Path.GetExtension(rel), StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }
        // имя ищется БЕЗ УЧЁТА РЕГИСТРА (как у страниц, T-18-S1): на Linux файловая система
        // регистрозависима, а ссылки в документах пишут люди
        if (FindOnDisk(root, "", rel) is not { } real)
        {
            return null;
        }
        var path = Path.Combine(root, real.Replace('/', Path.DirectorySeparatorChar));
        return Inside(root, path) && File.Exists(path) ? path : null;
    }

    /// <summary>Расширения, которые считаются картинкой документации.</summary>
    private static readonly string[] ImageExtensions =
        [".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp", ".ico", ".avif"];

    /// <summary>
    /// Пути-кандидаты для запрошенной страницы, в порядке предпочтения:
    /// <list type="bullet">
    /// <item>пусто или каталог — <c>README.md</c> в нём (и <c>readme.md</c>: на Linux
    /// регистр в имени файла значит всё, а ссылку пишут как придётся);</item>
    /// <item>имя без <c>.md</c> — сначала каталог с <c>README.md</c>, затем сам файл
    /// с дописанным расширением;</item>
    /// <item>обычный путь до <c>.md</c> — он сам.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<string> PageCandidates(string? relPath)
    {
        var rel = NormalizePath(relPath);
        if (rel.Length == 0)
        {
            // оглавление языка, если оно у него заведено, иначе прежняя первая страница
            return [IndexPage, .. ReadmeNames];
        }
        if (rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            var slash = rel.LastIndexOf('/');
            var dir = slash < 0 ? "" : rel[..(slash + 1)];
            var file = rel[(slash + 1)..];
            // «readme.md» и «README.md» — одна и та же страница; на Windows это и так так,
            // на Linux — только если спросить оба имени
            return ReadmeNames.Contains(file, StringComparer.OrdinalIgnoreCase)
                ? ReadmeNames.Select(n => dir + n).ToList()
                : [rel];
        }
        var list = ReadmeNames.Select(n => rel + "/" + n).ToList();
        list.Add(rel + ".md");
        return list;
    }

    /// <summary>Имена первой страницы каталога — в порядке предпочтения.</summary>
    private static readonly string[] ReadmeNames = ["README.md", "readme.md", "Readme.md"];

    /// <summary>
    /// Настоящий путь файла на диске по пути из ссылки — сегмент за сегментом, БЕЗ УЧЁТА
    /// РЕГИСТРА; null — такого файла нет. Возвращается имя, как оно записано на диске
    /// (<c>models/README.md</c> для запроса <c>models/readme.md</c>): дальше от него
    /// считаются относительные ссылки самой страницы, и подставлять туда написание из
    /// ссылки нельзя.
    ///
    /// Нужно это ради Linux: там файловая система регистрозависима, а ссылки в документах
    /// пишут люди. На Windows совпадение нашлось бы и так — поэтому поиск идёт одинаково
    /// на обеих системах, чтобы поведение не расходилось незаметно.
    /// </summary>
    private static string? FindOnDisk(string root, string lang, string rel)
    {
        var parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }
        var dir = Path.Combine(root, lang);
        var real = new List<string>(parts.Length);
        try
        {
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var found = MatchName(Directory.Exists(dir) ? Directory.GetDirectories(dir) : [],
                    parts[i]);
                if (found is null)
                {
                    return null;
                }
                real.Add(found);
                dir = Path.Combine(dir, found);
            }
            var file = MatchName(Directory.Exists(dir) ? Directory.GetFiles(dir) : [], parts[^1]);
            if (file is null)
            {
                return null;
            }
            real.Add(file);
        }
        catch (IOException)
        {
            return null;   // каталог исчез или недоступен — считаем, что страницы нет
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        return string.Join('/', real);
    }

    /// <summary>Имя из списка путей, совпавшее с искомым: сначала точно, потом без регистра.</summary>
    private static string? MatchName(string[] paths, string name)
    {
        string? insensitive = null;
        foreach (var path in paths)
        {
            var candidate = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            if (string.Equals(candidate, name, StringComparison.Ordinal))
            {
                return candidate;
            }
            insensitive ??= string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)
                ? candidate
                : null;
        }
        return insensitive;
    }

    /// <summary>
    /// Путь внутри языкового каталога, приведённый к безопасному виду: разделитель — «/»,
    /// пустые сегменты и «.» выброшены, «..» тоже (наверх из документации не выходят),
    /// в каждом сегменте недопустимые в имени файла символы заменены на «_». Хвост запроса
    /// и якорь (<c>#раздел</c>) отрезаются: это адресация ВНУТРИ страницы, а не путь.
    /// </summary>
    public static string NormalizePath(string? relPath)
    {
        var value = (relPath ?? "").Trim().Replace('\\', '/');
        foreach (var cut in new[] { '#', '?' })
        {
            var at = value.IndexOf(cut);
            if (at >= 0)
            {
                value = value[..at];
            }
        }
        var parts = value.Split('/')
            .Select(p => p.Trim())
            .Where(p => p.Length > 0 && p != "." && p != "..")
            .Select(Sanitize)
            .Where(p => p.Length > 0)
            .ToList();
        return string.Join('/', parts);
    }

    /// <summary>Путь лежит внутри корня документации (сверка уже собранного пути).</summary>
    private static bool Inside(string root, string path)
    {
        var full = Path.GetFullPath(path);
        var baseDir = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                      + Path.DirectorySeparatorChar;
        return full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Документ вида источника импорта по его коду (раздел <c>import</c>, todo47_2):
    /// <c>trello</c> → <c>&lt;язык&gt;/import/trello.md</c>. Документ привязан к ВИДУ,
    /// а не к записи справочника: как получить ключи Trello, одинаково для всех источников,
    /// и знать это нужно ДО того, как запись сохранена.
    /// </summary>
    public ModelDocDto ImportDoc(string kind) => Doc(ImportSection, kind);

    /// <summary>
    /// Документ раздела по имени. Не найден ни на одном языке — <c>Found = false</c>
    /// и подсказка, куда файл положить: пустой экран без объяснения бесполезен.
    /// </summary>
    public ModelDocDto Doc(string section, string name)
    {
        var dto = new ModelDocDto { ModelName = name, FileName = FileNameOf(name) };
        var root = Root();
        if (root.Length == 0)
        {
            dto.Hint = Loc.T("msg.doc.1");
            return dto;
        }
        // раздел приходит из кода, но собирать из него путь без проверки нельзя (гл. 12)
        var dir = Segment(section);
        foreach (var lang in Languages())
        {
            var path = Path.Combine(root, lang, dir, dto.FileName);
            if (!File.Exists(path))
            {
                continue;
            }
            try
            {
                dto.Text = File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                dto.Hint = ex.Message;
                return dto;
            }
            dto.Found = true;
            dto.Language = lang;
            dto.Path = path;
            return dto;
        }
        dto.Path = Path.Combine(root, _language(), dir, dto.FileName);
        dto.Hint = Loc.T("msg.doc.2", dto.Path);
        return dto;
    }

    /// <summary>Язык интерфейса первым, дальше — откат; повторов нет.</summary>
    private IEnumerable<string> Languages()
    {
        var seen = new List<string>();
        foreach (var lang in new[] { _language().Trim().ToLowerInvariant() }.Concat(Fallback))
        {
            if (lang.Length > 0 && !seen.Contains(lang))
            {
                seen.Add(lang);
                yield return lang;
            }
        }
    }

    /// <summary>
    /// Имя файла документа по названию модели (у импортов — по коду вида). Название
    /// приходит из справочника и может содержать что угодно — недопустимые в имени файла
    /// символы и разделители каталогов заменяются на <c>_</c>: путь собирает сервер,
    /// и уводить его наружу нельзя (гл. 12).
    /// </summary>
    public static string FileNameOf(string modelName)
    {
        var name = Sanitize(modelName);
        return (name.Length == 0 ? "model" : name) + ".md";
    }

    /// <summary>
    /// Безопасное имя ПОДКАТАЛОГА (раздел документации): в отличие от имени файла, здесь
    /// нельзя пропустить «.» и «..» — они увели бы путь наверх.
    /// </summary>
    private static string Segment(string? value)
    {
        var name = Sanitize(value);
        return name.Trim('.').Length == 0 ? "_" : name;
    }

    /// <summary>Замена недопустимых в имени файла символов и разделителей каталогов.</summary>
    private static string Sanitize(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string((value ?? "").Trim()
            .Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray());
    }
}

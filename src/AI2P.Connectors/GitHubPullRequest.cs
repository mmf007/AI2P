using AI2P.Core;
using System.Text;
using System.Text.Json;
using AI2P.Core.Events;
using AI2P.Storage;

namespace AI2P.Connectors;

/// <summary>
/// ЗАПРОС НА СЛИЯНИЕ В ЧУЖОЙ AWESOME-СПИСОК (T-828-S0, инструмент агента github_pull_request).
/// Шаги REST API GitHub от аккаунта, которому выдан личный токен источника импорта
/// (тот же секрет, что у импорта T-247): fork списка → дождаться готовности → ветка от
/// default branch → правка файла (строка вставляется в раздел по алфавиту) → PR.
/// Предел — не больше ОДНОГО PR в сутки (правило кампании «не пачкой»): считается по
/// событиям журнала <see cref="EventTypes.GitHubPullRequest"/>, а журнал реплицируется,
/// поэтому предел общий на организацию, а не на сервер.
/// </summary>
public sealed partial class GitHubImporter
{
    /// <summary>Сколько ждать, пока fork станет доступен (GitHub создаёт его асинхронно).</summary>
    public TimeSpan ForkWait { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Шаг опроса готовности fork.</summary>
    public TimeSpan ForkPoll { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Часы — подменяются в проверках.</summary>
    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>Окно предела «один PR».</summary>
    public static readonly TimeSpan PullRequestWindow = TimeSpan.FromHours(24);

    /// <summary>Что открыть: список owner/repo, файл, раздел, строка, заголовок и текст PR.</summary>
    public sealed record PullRequestArgs(string Repo, string Path, string Section, string Line,
        string Title, string Body, string? SourceId = null);

    /// <summary>
    /// Открыть PR со вставленной строкой; ответ — адрес PR. Отказ (предел, неверные
    /// аргументы, раздел не найден, строка уже есть, ответ GitHub) — исключение
    /// InvalidOperationException с понятным текстом.
    /// </summary>
    public async Task<string> OpenListPullRequestAsync(PullRequestArgs a, string? actorId,
        string? projectId, string? taskId, CancellationToken ct = default)
    {
        var parts = a.Repo.Trim().Trim('/').Split('/');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw new InvalidOperationException(Loc.T("msg.githubPr.1", a.Repo));
        }
        if (string.IsNullOrWhiteSpace(a.Line) || string.IsNullOrWhiteSpace(a.Section)
            || string.IsNullOrWhiteSpace(a.Title))
        {
            throw new InvalidOperationException(Loc.T("msg.githubPr.2"));
        }
        var (owner, repo) = (parts[0], parts[1]);
        var path = string.IsNullOrWhiteSpace(a.Path) ? "README.md" : a.Path.Trim().TrimStart('/');

        // предел «не пачкой» — до любого обращения к GitHub
        var since = UtcNow() - PullRequestWindow;
        var last = _events.Query(eventType: EventTypes.GitHubPullRequest, from: since, limit: 1);
        if (last.Count > 0)
        {
            var next = last[0].Ts.ToUniversalTime() + PullRequestWindow;
            throw new InvalidOperationException(Loc.T("msg.githubPr.3",
                next.ToLocalTime().ToString("dd.MM.yyyy HH:mm"), PrUrlOf(last[0])));
        }

        var auth = ResolveAuth(ResolveSourceParams(a.SourceId));
        try
        {
            var me = await WhoAmIAsync(auth, ct);
            var up = $"{BaseUrl}/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}";
            string defaultBranch;
            using (var doc = JsonDocument.Parse(await GetAsync(up, auth, ct)))
            {
                defaultBranch = Str(doc.RootElement, "default_branch");
            }
            if (defaultBranch.Length == 0)
            {
                defaultBranch = "main";
            }
            string baseSha;
            using (var doc = JsonDocument.Parse(await GetAsync(
                       $"{up}/git/ref/heads/{Uri.EscapeDataString(defaultBranch)}", auth, ct)))
            {
                baseSha = doc.RootElement.GetProperty("object").GetProperty("sha").GetString() ?? "";
            }

            // fork: на повторный запрос GitHub отдаёт уже существующий fork
            string forkFull;
            using (var doc = JsonDocument.Parse(await SendJsonAsync(HttpMethod.Post, $"{up}/forks",
                       new { default_branch_only = true }, auth, ct)))
            {
                forkFull = Str(doc.RootElement, "full_name");
            }
            if (forkFull.Length == 0)
            {
                forkFull = $"{me}/{repo}";
            }
            var fork = $"{BaseUrl}/repos/{forkFull}";
            await WaitForkAsync(fork, defaultBranch, auth, ct);

            // ветка от default branch СПИСКА (коммит есть в сети fork'ов)
            var key = SortKey(a.Line);
            var slug = new string(key.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray())
                .Trim('-');
            if (slug.Length > 30) slug = slug[..30].Trim('-');
            var branch = $"add-{(slug.Length > 0 ? slug : "entry")}-{UtcNow():yyyyMMddHHmm}";
            await SendJsonAsync(HttpMethod.Post, $"{fork}/git/refs",
                new { @ref = $"refs/heads/{branch}", sha = baseSha }, auth, ct);

            // файл: читаем из новой ветки, вставляем строку, пишем обратно
            var contentsUrl = $"{fork}/contents/{EscapePath(path)}";
            string text, fileSha;
            using (var doc = JsonDocument.Parse(await GetAsync(
                       $"{contentsUrl}?ref={Uri.EscapeDataString(branch)}", auth, ct)))
            {
                fileSha = Str(doc.RootElement, "sha");
                var b64 = Str(doc.RootElement, "content").Replace("\n", "").Replace("\r", "");
                text = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            }
            var updated = InsertIntoSection(text, a.Section, a.Line);
            await SendJsonAsync(HttpMethod.Put, contentsUrl, new
            {
                message = a.Title,
                content = Convert.ToBase64String(Encoding.UTF8.GetBytes(updated)),
                sha = fileSha,
                branch,
            }, auth, ct);

            string prUrl;
            using (var doc = JsonDocument.Parse(await SendJsonAsync(HttpMethod.Post, $"{up}/pulls", new
                   {
                       title = a.Title,
                       body = a.Body ?? "",
                       head = $"{forkFull.Split('/')[0]}:{branch}",
                       @base = defaultBranch,
                   }, auth, ct)))
            {
                prUrl = Str(doc.RootElement, "html_url");
            }

            _events.Append(new EventRecord
            {
                ActorId = actorId,
                ProjectId = projectId,
                TaskId = taskId,
                EventType = EventTypes.GitHubPullRequest,
                EntityType = "github_repo",
                EntityId = $"{owner}/{repo}",
                PayloadJson = JsonSerializer.Serialize(new { repo = $"{owner}/{repo}", path, branch, url = prUrl }),
            });
            Logger.Information("GitHub: открыт PR {Url} ({Repo}, {Path})", prUrl, $"{owner}/{repo}", path);
            return prUrl;
        }
        catch (GitHubApiException ex)
        {
            throw Explained(ex, auth);
        }
    }

    private static string PrUrlOf(EventRecord e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.PayloadJson);
            return Str(doc.RootElement, "url");
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private async Task WaitForkAsync(string fork, string branch, Auth auth, CancellationToken ct)
    {
        var until = DateTime.UtcNow + ForkWait;
        while (true)
        {
            try
            {
                await GetAsync($"{fork}/git/ref/heads/{Uri.EscapeDataString(branch)}", auth, ct);
                return;
            }
            catch (GitHubApiException ex) when ((ex.Status == 404 || ex.Status == 409)
                                                && DateTime.UtcNow < until)
            {
                await Task.Delay(ForkPoll, ct);
            }
        }
    }

    private async Task<string> SendJsonAsync(HttpMethod method, string url, object payload, Auth auth,
        CancellationToken ct)
    {
        Logger.Information("GitHub API: {Method} {Url}", method, url);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {auth.Token}");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8,
            "application/json");
        using var response = await Client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            var preview = body.Length <= 300 ? body : body[..300] + "…";
            Logger.Warning("GitHub API: {Method} {Url} → HTTP {Status}: {Body}",
                method, url, (int)response.StatusCode, preview);
            throw new GitHubApiException((int)response.StatusCode, preview);
        }
        return body;
    }

    /// <summary>Ключ сортировки пункта списка: без маркера списка, скобок ссылки и регистра —
    /// «- [Foo](url) - описание» → «foo».</summary>
    public static string SortKey(string line)
    {
        var s = line.Trim();
        if (s.StartsWith("- ") || s.StartsWith("* ") || s.StartsWith("+ "))
        {
            s = s[2..].TrimStart();
        }
        if (s.StartsWith('['))
        {
            var end = s.IndexOf(']');
            s = end > 0 ? s[1..end] : s[1..];
        }
        return s.Trim().ToLowerInvariant();
    }

    private static bool IsItem(string line)
    {
        var t = line.TrimStart();
        return line.Length - t.Length < 2 && (t.StartsWith("- ") || t.StartsWith("* ") || t.StartsWith("+ "));
    }

    private static int HeadingLevel(string line)
    {
        var t = line.TrimEnd();
        var n = 0;
        while (n < t.Length && t[n] == '#') n++;
        return n > 0 && n <= 6 && (t.Length == n || t[n] == ' ') ? n : 0;
    }

    private static string HeadingText(string line) => line.TrimStart('#').Trim().ToLowerInvariant();

    /// <summary>
    /// Вставить строку в раздел Markdown по алфавиту (T-828-S0). Раздел — заголовок с этим
    /// текстом (без учёта регистра и «#»; сначала точное совпадение, потом вхождение); его
    /// граница — следующий заголовок того же или более высокого уровня. Строка без маркера
    /// списка получает маркер, принятый в разделе. Концы строк файла сохраняются.
    /// Раздела нет или такая запись уже есть — отказ.
    /// </summary>
    public static string InsertIntoSection(string text, string section, string line)
    {
        var eol = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var want = section.Trim().TrimStart('#').Trim().ToLowerInvariant();
        var head = lines.FindIndex(l => HeadingLevel(l) > 0 && HeadingText(l) == want);
        if (head < 0)
        {
            head = lines.FindIndex(l => HeadingLevel(l) > 0 && HeadingText(l).Contains(want));
        }
        if (head < 0)
        {
            throw new InvalidOperationException(Loc.T("msg.githubPr.4", section));
        }
        var level = HeadingLevel(lines[head]);
        var end = lines.Count;
        for (var i = head + 1; i < lines.Count; i++)
        {
            var l = HeadingLevel(lines[i]);
            if (l > 0 && l <= level)
            {
                end = i;
                break;
            }
        }
        var items = Enumerable.Range(head + 1, end - head - 1).Where(i => IsItem(lines[i])).ToList();
        var newLine = line.Trim();
        if (!(newLine.StartsWith("- ") || newLine.StartsWith("* ") || newLine.StartsWith("+ ")))
        {
            var bullet = items.Count > 0 ? lines[items[0]].TrimStart()[..2] : "- ";
            newLine = bullet + newLine;
        }
        var key = SortKey(newLine);
        if (items.Any(i => SortKey(lines[i]) == key))
        {
            throw new InvalidOperationException(Loc.T("msg.githubPr.5", key));
        }
        int at;
        if (items.Count == 0)
        {
            at = head + 1;
            lines.Insert(at, "");
            at++;
        }
        else
        {
            var before = items.FirstOrDefault(i => string.CompareOrdinal(SortKey(lines[i]), key) > 0, -1);
            at = before >= 0 ? before : items[^1] + 1;
        }
        lines.Insert(at, newLine);
        return string.Join(eol, lines);
    }
}

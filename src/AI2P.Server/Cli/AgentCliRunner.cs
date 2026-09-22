using System.Text;
using System.Text.Json;
using AI2P.Connectors;
using AI2P.Core;

namespace AI2P.Server.Cli;

/// <summary>
/// КЛИЕНТ КОМАНДНОЙ СТРОКИ <c>ai2p</c> (T-34-S0) — то, что запускает обёртка
/// <c>ai2p.cmd</c> / <c>ai2p</c>: <c>AI2P.Server.exe cli &lt;команда&gt; …</c>.
///
/// <para>Клиент НИЧЕГО не делает сам: он разбирает командную строку, зовёт раздел
/// <c>/api/agent</c> своего же сервера по петле (адрес и токен задания приходят
/// переменными окружения) и печатает ответ. Вся логика действий — один раз, в
/// <see cref="AgentToolset"/>; дублировать её здесь нельзя.</para>
///
/// <para>Ветка разбирается в <c>Program.cs</c> ДО поднятия хоста — рядом с уже
/// существующей веткой <c>--version</c>: отдельного exe нет намеренно (см.
/// <see cref="AgentCli"/>).</para>
/// </summary>
public static class AgentCliRunner
{
    /// <summary>Команда клиента: как её пишет агент → какой инструмент зовётся и как
    /// называется его ГЛАВНЫЙ параметр (значение можно передать без «--имя»).</summary>
    /// <param name="Tool">Имя инструмента набора (AgentToolset).</param>
    /// <param name="Primary">Главный параметр; пусто — позиционного значения у команды нет.</param>
    /// <param name="Hint">Короткая подсказка для «ai2p help» (ключ словаря).</param>
    private sealed record Command(string Tool, string Primary, string Hint);

    /// <summary>Команды клиента. Кроме них принимается и само имя инструмента
    /// (<c>ai2p call get_task_by_code --code T-15</c>) — на случай нового действия,
    /// у которого короткого имени ещё нет.</summary>
    private static readonly Dictionary<string, Command> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["parent"] = new("get_parent_task", "", "msg.agentCli.20"),
        ["siblings"] = new("get_sibling_tasks", "", "msg.agentCli.21"),
        ["children"] = new("get_child_tasks", "code", "msg.agentCli.22"),
        ["task"] = new("get_task_by_code", "code", "msg.agentCli.23"),
        ["task-url"] = new("get_task_by_url", "url", "msg.agentCli.24"),
        ["find"] = new("find_tasks_by_title", "title", "msg.agentCli.25"),
        ["chat"] = new("get_task_chat", "code", "msg.agentCli.26"),
        ["subtask"] = new("create_task", "title", "msg.agentCli.27"),
        ["move"] = new("move_task", "code", "msg.agentCli.28"),
        ["status"] = new("set_task_status", "code", "msg.agentCli.29"),
        // правка полей чужой задачи (T-160-S0): исполнитель, замены, ответственный, навыки, тэги
        ["update"] = new("update_task", "code", "msg.agentCli.44"),
        // перезапуск чужой задачи для повторной проверки и уход в ожидание (T-31-S0)
        ["restart"] = new("restart_task_for_recheck", "code", "msg.agentCli.39"),
        ["wait"] = new("wait_for_recheck", "", "msg.agentCli.40"),
        ["experience"] = new("create_experience", "text", "msg.agentCli.30"),
        ["experience-update"] = new("update_experience", "id", "msg.agentCli.31"),
        // ПОИСК ПО ОПЫТУ (T-268-S0): CLI-агенту маркер не годится — он заканчивает ход
        // и стоит дорого, а команда отвечает в том же ходе
        ["experience-find"] = new("search_experience", "query", "msg.agentCli.45"),
        // ПЕРЕНОС записи между областями (T-269-S0): общие правила ↔ опыт проекта ↔ опыт
        // узла шаблона. Маркера у переноса нет намеренно — команда отвечает в том же ходе
        ["experience-move"] = new("move_experience", "id", "msg.agentCli.46"),
        // РАЗБОР ОПЫТА (T-271-S0): погасить/оживить запись, перечислить область страницами
        // и спросить статистику использования — этим работает шаблон «Анализ опыта»
        ["experience-active"] = new("set_experience_active", "id", "msg.agentCli.47"),
        ["experience-list"] = new("list_experience", "scope", "msg.agentCli.48"),
        ["experience-usage"] = new("experience_usage", "id", "msg.agentCli.49"),
        ["templates"] = new("list_templates", "", "msg.agentCli.32"),
        ["template"] = new("create_template", "title", "msg.agentCli.33"),
        ["template-update"] = new("update_template", "code", "msg.agentCli.34"),
        ["message"] = new("send_chat_message", "text", "msg.agentCli.35"),
        ["to-task"] = new("send_task_message", "code", "msg.agentCli.36"),
        ["file"] = new("fetch_file", "url", "msg.agentCli.37"),
        ["import"] = new("import_task_from_url", "url", "msg.agentCli.38"),
        // медиатека проекта (T-113-S0)
        ["media-add"] = new("media_add", "path", "msg.agentCli.41"),
        ["media-list"] = new("media_list", "", "msg.agentCli.42"),
        ["media-remove"] = new("media_remove", "code", "msg.agentCli.43"),
        // ветвление и циклы (T-300-S0). value/continue намеренно НЕ в BoolParams: там
        // любое слово, кроме «0/false/no», стало бы true, а третьего исхода быть не должно —
        // строку проверяет само действие и на «да»/«1» отвечает ошибкой
        ["condition"] = new("set_condition_result", "value", "msg.agentCli.50"),
        ["loop"] = new("set_loop_result", "continue", "msg.agentCli.51"),
        ["from-template"] = new("create_tasks_from_template", "template", "msg.agentCli.52"),
        ["stop-hierarchy"] = new("stop_hierarchy", "reason", "msg.agentCli.53"),
    };

    /// <summary>Параметры-числа: в JSON вызова уходят числом, а не строкой.</summary>
    private static readonly HashSet<string> IntParams = new(StringComparer.OrdinalIgnoreCase)
        { "priority", "startAfterMinutes", "depth", "minutes", "take", "order", "limit", "offset" };

    /// <summary>Параметры-флаги: «--restart» без значения означает true.</summary>
    private static readonly HashSet<string> BoolParams = new(StringComparer.OrdinalIgnoreCase)
        { "restart", "wait", "includeInactive", "active", "alwaysLoad" };

    /// <summary>Параметры-списки: «--skills a --skills b» либо «--skills a,b».</summary>
    private static readonly HashSet<string> ListParams = new(StringComparer.OrdinalIgnoreCase)
        { "skills", "tags", "altExecutors" };

    public static async Task<int> RunAsync(string[] args)
    {
        // ВЫВОД — В UTF-8, А НЕ В КОДИРОВКЕ КОНСОЛИ. Ответ клиента читает АГЕНТ (Claude Code
        // забирает stdout запущенной команды), и на русской Windows консоль по умолчанию
        // cp866: без этой строки задание, карточка и текст отказа приезжали бы агенту
        // крякозябрами — та же беда, что у поддельного CLI в живых проверках (T-11-S0)
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (Exception)
        {
            // консоли нет (вывод перенаправлен в закрытый канал) — печатать всё равно есть куда
        }
        var rest = args.ToList();
        var asJson = Take(rest, "--json");
        var readStdin = Take(rest, "--stdin");
        var command = rest.Count > 0 ? rest[0] : "help";
        if (rest.Count > 0)
        {
            rest.RemoveAt(0);
        }

        var url = (Environment.GetEnvironmentVariable(AgentCli.UrlVar) ?? "").Trim().TrimEnd('/');
        var token = (Environment.GetEnvironmentVariable(AgentCli.TokenVar) ?? "").Trim();

        if (command is "help" or "--help" or "-h" or "-?")
        {
            Console.WriteLine(Usage());
            await PrintActionsAsync();
            return 0;
        }

        if (url.Length == 0)
        {
            return Fail(Loc.T("msg.agentCli.2", AgentCli.UrlVar));
        }
        if (token.Length == 0)
        {
            return Fail(Loc.T("msg.agentCli.3", AgentCli.TokenVar));
        }

        if (command is "actions")
        {
            return await PrintActionsAsync() ? 0 : 1;
        }

        // «call <инструмент>» — общий путь: инструмент назван явно
        string tool;
        string primary;
        if (command.Equals("call", StringComparison.OrdinalIgnoreCase))
        {
            if (rest.Count == 0)
            {
                return Fail(Loc.T("msg.agentCli.9"));
            }
            tool = rest[0];
            rest.RemoveAt(0);
            primary = "";
        }
        else if (Commands.TryGetValue(command, out var known))
        {
            (tool, primary) = (known.Tool, known.Primary);
        }
        else if (command.Contains('_'))
        {
            // имя инструмента как есть — «ai2p get_task_by_code --code T-15»
            (tool, primary) = (command, "");
        }
        else
        {
            return Fail(Loc.T("msg.agentCli.4", command,
                string.Join(", ", Commands.Keys.OrderBy(k => k, StringComparer.Ordinal))));
        }

        JsonObjectBuilder builder;
        try
        {
            builder = ParseArgs(rest, primary, readStdin);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }

        var payload = new StringBuilder();
        payload.Append("{\"tool\":").Append(JsonSerializer.Serialize(tool))
            .Append(",\"args\":").Append(builder.ToJson()).Append('}');

        try
        {
            // сервер может слушать HTTPS (T-206): по петле проверка сертификата не действует —
            // адрес localhost имени в сертификате не совпадает никогда
            using var http = new HttpClient(HttpsPeers.LoopbackHandler(), disposeHandler: false)
                { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.Add(Api.AgentEndpoints.TokenHeader, token);
            using var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            using var response = await http.PostAsync(url + "/api/agent/call", content);
            var body = await response.Content.ReadAsStringAsync();
            return Print(body, response.IsSuccessStatusCode, (int)response.StatusCode, asJson);
        }
        catch (Exception ex)
        {
            return Fail(Loc.T("msg.agentCli.5", url, ex.Message));
        }

        async Task<bool> PrintActionsAsync()
        {
            if (url.Length == 0 || token.Length == 0)
            {
                return false;
            }
            try
            {
                using var http = new HttpClient(HttpsPeers.LoopbackHandler(), disposeHandler: false)
                    { Timeout = TimeSpan.FromSeconds(30) };
                http.DefaultRequestHeaders.Add(Api.AgentEndpoints.TokenHeader, token);
                var body = await http.GetStringAsync(url + "/api/agent/actions");
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                Console.WriteLine();
                Console.WriteLine(Loc.T("msg.agentCli.8",
                    Str(root, "job"), Str(root, "task")));
                if (root.TryGetProperty("tools", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    var byTool = Commands.ToDictionary(p => p.Value.Tool, p => p.Key, StringComparer.Ordinal);
                    foreach (var item in list.EnumerateArray())
                    {
                        var name = Str(item, "tool");
                        var allowed = item.TryGetProperty("allowed", out var a)
                                      && a.ValueKind == JsonValueKind.True;
                        var alias = byTool.TryGetValue(name, out var known) ? known : "call " + name;
                        Console.WriteLine($"  {alias,-20} {name}" +
                                          (allowed ? "" : "  — " + Loc.T("msg.agentCli.10")));
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(Loc.T("msg.agentCli.5", url, ex.Message));
                return false;
            }
        }
    }

    /// <summary>Печать ответа: обычно — только текст результата (его читает агент),
    /// с ключом --json — весь ответ как есть.</summary>
    private static int Print(string body, bool success, int status, bool asJson)
    {
        if (asJson)
        {
            Console.WriteLine(body);
            return success ? 0 : 1;
        }
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (success)
            {
                Console.WriteLine(Str(root, "result"));
                return 0;
            }
            var error = Str(root, "error");
            Console.Error.WriteLine(error.Length > 0 ? error : body.Trim());
            return 1;
        }
        catch (JsonException)
        {
            Console.Error.WriteLine(Loc.T("msg.agentCli.11", status, body.Trim()));
            return 1;
        }
    }

    private static string Str(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static bool Take(List<string> args, string flag)
    {
        var index = args.FindIndex(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }
        args.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Разбор аргументов: «--имя значение», флаги без значения, «--имя-file путь»
    /// (значение читается из файла — так передают длинное описание с переводами строк),
    /// позиционное значение уходит в главный параметр команды. С ключом --stdin весь
    /// объект аргументов читается со стандартного ввода готовым JSON.
    /// </summary>
    private static JsonObjectBuilder ParseArgs(List<string> args, string primary, bool readStdin)
    {
        var builder = new JsonObjectBuilder();
        if (readStdin)
        {
            // ВВОД — В UTF-8, А НЕ В КОДИРОВКЕ КОНСОЛИ (T-96-S0). Console.In декодирует
            // стандартный ввод кодировкой консоли (на русской Windows cp866), и заголовки
            // с описаниями подзадач приезжали в базу мусором («╨Я╨╡╤А╨╡╨▓╨╛╨┤» вместо
            // «Перевод») при коде возврата 0 — восемь заведённых так задач пришлось отменять
            // (T-231). Пишет сюда агент, а он печатает UTF-8, поэтому читаем поток сами
            var text = ReadStandardInput().Trim();
            if (text.Length > 0)
            {
                if (LooksLikeBrokenEncoding(text))
                {
                    throw new ArgumentException(Loc.T("msg.agentCli.15", "--stdin"));
                }
                builder.Merge(text);
            }
        }
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (primary.Length == 0)
                {
                    throw new ArgumentException(Loc.T("msg.agentCli.12", arg));
                }
                builder.Set(primary, arg, IntParams, BoolParams, ListParams);
                continue;
            }
            var name = arg[2..];
            var fromFile = name.EndsWith("-file", StringComparison.OrdinalIgnoreCase);
            if (fromFile)
            {
                name = name[..^"-file".Length];
            }
            var hasValue = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            if (!hasValue)
            {
                if (fromFile || !BoolParams.Contains(name))
                {
                    throw new ArgumentException(Loc.T("msg.agentCli.13", name));
                }
                builder.SetBool(name, true);
                continue;
            }
            var value = args[++i];
            if (fromFile)
            {
                if (!File.Exists(value))
                {
                    throw new ArgumentException(Loc.T("msg.agentCli.7", value));
                }
                value = File.ReadAllText(value);
            }
            else if (LooksLikeBrokenEncoding(value))
            {
                // значение испортила оболочка, до нас: отказываем вместо молчаливой записи
                // мусора в базу (T-96-S0) и называем годный способ передать текст
                throw new ArgumentException(Loc.T("msg.agentCli.15", "--" + name));
            }
            builder.Set(name, value, IntParams, BoolParams, ListParams);
        }
        return builder;
    }

    /// <summary>
    /// Стандартный ввод целиком, всегда как UTF-8 (T-96-S0): BOM, если он есть, съедается,
    /// кодировка консоли не спрашивается вовсе — на той стороне канала агент, а не человек.
    /// </summary>
    private static string ReadStandardInput()
    {
        using var reader = new StreamReader(Console.OpenStandardInput(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Признак текста, приехавшего в испорченной кодировке (T-96-S0): UTF-8, прочитанный
    /// как cp866 или cp1251, даёт псевдографику и одиночные заглавные из верхней половины
    /// таблицы — «╨Я╨╡╤А╨╡╨▓╨╛╨┤», «ÐŸÐµÑ€ÐµÐ²Ð¾Ð´». Такой текст молча уезжал в базу заголовком
    /// подзадачи, и вызов при этом возвращал 0. Проверка нужна и после починки чтения:
    /// аргументы командной строки портит уже оболочка, до нас, — и тогда честный отказ
    /// с подсказкой про «--имя-file» дешевле восьми отменённых задач.
    /// </summary>
    public static bool LooksLikeBrokenEncoding(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }
        var suspicious = 0;
        foreach (var c in text)
        {
            // рамки псевдографики (cp866-прочтение кириллицы) и «Ã/Ð/Ñ»-хвосты (cp1251)
            if ((c >= '═' && c <= '╬') || c is '░' or '▒' or '▓'
                or 'Ð' or 'Ñ' or 'Â' or 'Ã' or '╨' or '╤')
            {
                suspicious++;
            }
        }
        // одиночная рамка в тексте законна (таблицы, рисунки) — мусор идёт плотно
        return suspicious * 100 >= text.Length * 8;
    }

    private static string Usage() => Loc.T("msg.agentCli.1",
        string.Join("\n", Commands
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"  ai2p {p.Key,-18} {Loc.T(p.Value.Hint)}")));

    /// <summary>Сборка JSON-объекта аргументов вызова: типы параметров известны заранее
    /// (число, флаг, список), остальное — строки.</summary>
    private sealed class JsonObjectBuilder
    {
        private readonly Dictionary<string, string> _raw = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _lists = new(StringComparer.Ordinal);

        public void Set(string name, string value, HashSet<string> ints, HashSet<string> bools,
            HashSet<string> lists)
        {
            if (lists.Contains(name))
            {
                if (!_lists.TryGetValue(name, out var items))
                {
                    _lists[name] = items = [];
                }
                items.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries
                                                | StringSplitOptions.TrimEntries));
                return;
            }
            if (ints.Contains(name) && long.TryParse(value.Trim(), out var number))
            {
                _raw[name] = number.ToString();
                return;
            }
            if (bools.Contains(name))
            {
                _raw[name] = value.Trim() is "0" or "false" or "no" ? "false" : "true";
                return;
            }
            _raw[name] = JsonSerializer.Serialize(value);
        }

        public void SetBool(string name, bool value) => _raw[name] = value ? "true" : "false";

        /// <summary>Готовый JSON-объект со стандартного ввода: его поля ложатся в аргументы
        /// как есть — так передают длинные тексты, не воюя с экранированием оболочки.</summary>
        public void Merge(string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(Loc.T("msg.agentCli.14"));
            }
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                _raw[property.Name] = property.Value.GetRawText();
            }
        }

        public string ToJson()
        {
            var parts = _raw.Select(p => JsonSerializer.Serialize(p.Key) + ":" + p.Value)
                .Concat(_lists.Select(p => JsonSerializer.Serialize(p.Key) + ":["
                    + string.Join(",", p.Value.Select(v => JsonSerializer.Serialize(v))) + "]"));
            return "{" + string.Join(",", parts) + "}";
        }
    }
}

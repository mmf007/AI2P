using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AI2P.Core;
using AI2P.Core.Api;
using Serilog;

namespace AI2P.Connectors;

/// <summary>
/// ВХОД В CLAUDE CLI ИЗ AI2P (todo96, версия 1.96).
///
/// ЗАЧЕМ. Сеанс CLI протухает (<see cref="ClaudeCliAuth"/>), и до этой версии войти заново
/// можно было только руками в терминале того компьютера, где работает сервер: AI2P об этом
/// не знал и показывал очередную ошибку задания. Теперь вход предлагается в самом AI2P.
///
/// КАК. <c>claude auth login</c> работает и с перенаправленным вводом-выводом: он печатает
/// ссылку авторизации и ждёт кода на stdin —
/// <code>
/// Opening browser to sign in…
/// If the browser did not open, visit: https://claude.com/cai/oauth/authorize?…
/// Paste code here if prompted &gt;
/// </code>
/// Поэтому сервис держит запущенный процесс между двумя запросами UI: сначала «начать вход»
/// (вернуть ссылку), потом «вот код» (дописать его в stdin и дождаться конца). Браузер
/// открывается на КОМПЬЮТЕРЕ СЕРВЕРА — но ссылка отдаётся и в UI, поэтому войти можно
/// и с другого устройства.
///
/// Процесс входа один на компьютер, как и сам вход: сервис — синглтон приложения, рядом
/// с <see cref="LocalModelProcessService"/>. Начатый и брошенный вход убивается следующим
/// «начать вход», а зависший — снимается по <see cref="Timeout"/>.
/// </summary>
public sealed class ClaudeLoginService : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<ClaudeLoginService>();

    /// <summary>Команда CLI по умолчанию — та же, что у коннектора (профайл без cliCommand).</summary>
    public const string DefaultCommand = "claude";

    /// <summary>Сколько ждём конца входа после отправки кода: человек к этому моменту уже
    /// всё сделал, но обмен с сервером авторизации бывает небыстрым.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    /// <summary>Сколько ждём ПЕЧАТИ ССЫЛКИ после запуска: её CLI печатает сразу, и если за это
    /// время ссылки нет — что-то не так с самим CLI, а не с человеком.</summary>
    public static readonly TimeSpan UrlTimeout = TimeSpan.FromSeconds(90);

    /// <summary>Сколько ждём ответа <c>auth status</c>: команда бесплатная и быстрая.</summary>
    public static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Ссылка авторизации в выводе CLI.</summary>
    private static readonly Regex UrlRx = new(@"https://\S+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly object _lock = new();
    private Session? _session;

    /// <summary>Идёт ли вход прямо сейчас (ссылка выдана, кода ещё нет).</summary>
    public bool Running
    {
        get
        {
            lock (_lock)
            {
                return _session is { Finished: false };
            }
        }
    }

    /// <summary>Спросить у CLI, есть ли вход (<see cref="ClaudeCliAuth.StatusAsync"/>):
    /// вынесено туда, чтобы проба подключения исполнителя обходилась без этого сервиса.</summary>
    public Task<ClaudeAuthStatus> StatusAsync(string? command, CancellationToken ct = default) =>
        ClaudeCliAuth.StatusAsync(command, ct);

    /// <summary>
    /// Начать вход: запустить <c>claude auth login</c> и дождаться ссылки авторизации.
    /// Незаконченный прошлый вход снимается — их не может быть два.
    /// </summary>
    public async Task<ClaudeLoginStateDto> StartAsync(string? command, CancellationToken ct = default)
    {
        CancelInternal();
        var exe = Executable(command);
        Session session;
        try
        {
            var process = CliProcess.Start(exe, "auth login", Environment.CurrentDirectory,
                redirectStdin: true);
            session = new Session(process);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Claude CLI: вход не запустился ({Exe})", exe);
            return Failed(Loc.T("msg.claudeLogin.1", exe, ex.Message));
        }
        lock (_lock)
        {
            _session = session;
        }
        Logger.Information("Claude CLI: начат вход ({Exe} auth login)", exe);

        var url = await session.WaitForAsync(UrlOf, UrlTimeout, ct);
        if (url is null)
        {
            // ссылки нет: либо CLI закончил сразу (вход уже был), либо не запустился
            var output = session.Output;
            var finishedOk = session.Finished && session.ExitCode == 0;
            CancelInternal();
            if (finishedOk)
            {
                Logger.Information("Claude CLI: вход завершился без ссылки — вход уже был");
                return Done();
            }
            return Failed(Loc.T("msg.claudeLogin.2", Short(output)));
        }
        return new ClaudeLoginStateDto
        {
            Running = true,
            Url = url,
            Message = Loc.T("msg.claudeLogin.3"),
        };
    }

    /// <summary>
    /// Дослать код авторизации, который человек скопировал со страницы входа, и дождаться
    /// конца входа. Вход не начинали (или процесс уже снят) — так и отвечаем.
    /// </summary>
    public async Task<ClaudeLoginStateDto> SubmitCodeAsync(string? code, CancellationToken ct = default)
    {
        Session session;
        lock (_lock)
        {
            if (_session is not { Finished: false } live)
            {
                return Failed(Loc.T("msg.claudeLogin.4"));
            }
            session = live;
        }
        var value = (code ?? "").Trim();
        if (value.Length == 0)
        {
            return Failed(Loc.T("msg.claudeLogin.5"));
        }
        try
        {
            await session.WriteLineAsync(value, ct);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            CancelInternal();
            return Failed(Loc.T("msg.claudeLogin.6", ex.Message));
        }
        var exited = await session.WaitExitAsync(Timeout, ct);
        var output = session.Output;
        var exitCode = session.ExitCode;
        CancelInternal();
        if (!exited)
        {
            return Failed(Loc.T("msg.claudeLogin.7"));
        }
        if (exitCode != 0)
        {
            Logger.Warning("Claude CLI: вход не удался (код {Code}): {Out}", exitCode, Short(output));
            return Failed(Loc.T("msg.claudeLogin.8", exitCode, Short(output)));
        }
        Logger.Information("Claude CLI: вход выполнен");
        return Done();
    }

    /// <summary>Бросить начатый вход (кнопка «отмена» либо новый заход).</summary>
    public void Cancel() => CancelInternal();

    public void Dispose() => CancelInternal();

    private void CancelInternal()
    {
        Session? session;
        lock (_lock)
        {
            session = _session;
            _session = null;
        }
        session?.Dispose();
    }

    /// <summary>Только ИСПОЛНЯЕМЫЙ ФАЙЛ команды профайла: флаги запуска агента
    /// (<c>--permission-mode</c> и прочие) команде <c>auth</c> не подходят.</summary>
    public static string Executable(string? command)
    {
        var text = (command ?? "").Trim();
        return text.Length == 0
            ? DefaultCommand
            : LocalModelProcessService.SplitCommand(text).FileName;
    }

    /// <summary>Ссылка авторизации в накопленном выводе CLI; null — ещё не напечатана.</summary>
    private static string? UrlOf(string output)
    {
        var match = UrlRx.Match(output);
        // хвостовые знаки препинания в ссылку не входят
        return match.Success ? match.Value.TrimEnd('.', ',', ')', '"', '\'', '…') : null;
    }

    private static ClaudeLoginStateDto Done() => new()
    {
        Running = false,
        LoggedIn = true,
        Message = Loc.T("msg.claudeLogin.9"),
    };

    private static ClaudeLoginStateDto Failed(string message) => new()
    {
        Running = false,
        Error = message,
    };

    private static string Short(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400] + "…";
    }

    /// <summary>
    /// Живой процесс входа: вывод читается посимвольно — приглашение «Paste code here …»
    /// приходит БЕЗ перевода строки, и построчное чтение его бы не дождалось.
    /// </summary>
    private sealed class Session : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _output = new();
        private readonly object _sync = new();
        private readonly Task _reader;
        private readonly Task _errReader;

        public Session(Process process)
        {
            _process = process;
            _reader = PumpAsync(process.StandardOutput);
            _errReader = PumpAsync(process.StandardError);
        }

        public string Output
        {
            get
            {
                lock (_sync)
                {
                    return _output.ToString();
                }
            }
        }

        public bool Finished => _process.HasExited;

        public int ExitCode => _process.HasExited ? _process.ExitCode : -1;

        public async Task WriteLineAsync(string text, CancellationToken ct)
        {
            await _process.StandardInput.WriteLineAsync(text.AsMemory(), ct);
            await _process.StandardInput.FlushAsync(ct);
        }

        /// <summary>Ждать, пока в выводе появится нужное (ссылка); null — не дождались
        /// либо процесс закончился, так её и не напечатав.</summary>
        public async Task<string?> WaitForAsync(Func<string, string?> pick, TimeSpan limit,
            CancellationToken ct)
        {
            var deadline = DateTime.UtcNow + limit;
            while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                if (pick(Output) is { } found)
                {
                    return found;
                }
                if (_process.HasExited)
                {
                    return pick(Output);
                }
                await Task.Delay(200, ct);
            }
            return null;
        }

        /// <summary>Ждать конца процесса; false — не дождались за отведённое время.</summary>
        public async Task<bool> WaitExitAsync(TimeSpan limit, CancellationToken ct)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(limit);
            try
            {
                await _process.WaitForExitAsync(timeout.Token);
                // дочитать хвост вывода: он нужен для объяснения неудачи
                await Task.WhenAny(Task.WhenAll(_reader, _errReader),
                    Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None));
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private async Task PumpAsync(StreamReader reader)
        {
            var buffer = new char[256];
            try
            {
                while (true)
                {
                    var read = await reader.ReadAsync(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        break;
                    }
                    lock (_sync)
                    {
                        _output.Append(buffer, 0, read);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // процесс сняли — читать больше нечего
            }
        }

        public void Dispose()
        {
            if (!_process.HasExited)
            {
                CliProcess.Kill(_process);
            }
            _process.Dispose();
        }
    }
}

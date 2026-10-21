using AI2P.Core;
using AI2P.Core.Api;
using AI2P.Server.Org;

namespace AI2P.Server.Api;

/// <summary>
/// ПОДКЛЮЧЕНИЕ ПРОЕКТА К VS CODE (T-510-S0, этап 8.5 плагина).
///
/// Работает только на СВОЁМ сервере: каталог проекта (<c>ProjectServer.FolderPath</c>) и
/// копии VS Code — свойство этой машины. Нет каталога или не найден VS Code — кнопок нет.
/// «Подключить»: поставить/обновить расширение (<c>code --install-extension &lt;vsix&gt; --force</c>),
/// записать <c>.ai2p/link.json</c> (без секретов), выпустить токен устройства и передать его
/// ссылкой URI handler'а выбранной копии (<c>code --open-url</c>, схема копии — у форков своя),
/// по желанию — дописать <c>.ai2p/</c> в .gitignore и открыть проект. «Отключить»: удалить
/// link.json и отозвать токены проекта; расширение остаётся — оно общее для всех проектов.
/// Токен из ответа наружу не уходит: ссылку строит и передаёт сам сервер.
/// </summary>
public static class VsCodeEndpoints
{
    public static void MapAi2pVsCode(this WebApplication app, int port, string basePath)
    {
        var registry = app.Services.GetRequiredService<OrgRegistry>();
        var current = app.Services.GetRequiredService<CurrentUserAccessor>();
        var configHolder = app.Services.GetRequiredService<ConfigHolder>();

        // КОПИЯ ПЛАГИНА «VS CODE» (T-530-S0): путь из его настройки (поставлена пакетом плагина
        // либо указана человеком), иначе найденная поиском плагина; null — плагина/копии нет
        string? PluginCodePath(OrgContext org)
        {
            var manifest = PluginManifest.Read(org.Files.DataDir, AI2P.Storage.Services.PluginSeed.VsCodeCode);
            if (manifest?.Software is not { } soft)
            {
                return null;
            }
            var manual = configHolder.Config.Plugins.TryGetValue(manifest.Code, out var s) ? s.Path : "";
            var found = AI2P.Connectors.PluginSoftwareProbe.Find(soft, manual);
            return found.Ready && found.Path.Length > 0 ? found.Path : null;
        }
        // плагин подключён: запись инициализирована и ВСЕ его ОБЯЗАТЕЛЬНЫЕ программы на месте
        // (Node.js необязателен, T-533-S0: ни VS Code с плагином, ни Claude Code его не зовут)
        bool PluginReady(OrgContext org)
        {
            var manifest = PluginManifest.Read(org.Files.DataDir, AI2P.Storage.Services.PluginSeed.VsCodeCode);
            if (manifest is null || org.Plugins.ByCode(manifest.Code) is not { State: AI2P.Core.Entities.PluginStates.Initialized })
            {
                return false;
            }
            var settings = configHolder.Config.Plugins.GetValueOrDefault(manifest.Code);
            return manifest.SoftwareList.Where(soft => soft.Required).All(soft =>
            {
                var path = settings is null ? ""
                    : settings.Paths.TryGetValue(soft.Id, out var own) && own.Trim().Length > 0 ? own
                    : ReferenceEquals(manifest.Software, soft) ? settings.Path : "";
                return AI2P.Connectors.PluginSoftwareProbe.Find(soft, path).Ready;
            });
        }
        ProjectVsCodeDto StateOf(OrgContext org, AI2P.Core.Entities.Project project, bool refresh) =>
            State(registry.VsCode, registry.ProjectVsCode, org.Org.Id, project.Id, project.FolderPath, refresh,
                PluginCodePath(org), PluginReady(org));
        // копия по пути — среди найденных И копии плагина
        VsCodeInstall? ByPath(OrgContext org, string? path) =>
            path is { Length: > 0 }
                ? registry.VsCode.FindWith(PluginCodePath(org)).FirstOrDefault(x => VsCodeLocator.SamePath(x.CodePath, path))
                : null;

        var api = app.MapGroup("/api/projects/{projectId}/vscode");
        api.AddEndpointFilter(async (ctx, next) =>
            ApiPermissions.Check(ctx.HttpContext, current) ?? await next(ctx));

        OrgContext Org() => current.Org ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.3"));
        string Account() => current.AccountId ?? throw new InvalidOperationException(Loc.T("msg.apiEndpoints.2"));

        // найденные копии без проекта — форме создания проекта (флажок «Подключить VS Code»)
        var installs = app.MapGroup("/api/vscode");
        installs.AddEndpointFilter(async (ctx, next) =>
            ApiPermissions.Check(ctx.HttpContext, current) ?? await next(ctx));
        installs.MapGet("/installs", (bool? refresh) => ApiEndpoints.Handle(() =>
            Results.Ok(current.Org is { } org
                ? registry.VsCode.FindWith(PluginCodePath(org), refresh == true)
                : registry.VsCode.Find(refresh == true))));

        IResult NotFound() => Results.Problem(Loc.T("msg.deviceToken.4"), statusCode: StatusCodes.Status404NotFound);

        api.MapGet("", (string projectId, bool? refresh) => ApiEndpoints.Handle(() =>
        {
            var org = Org();
            if (org.Projects.Get(projectId) is not { DeletedAt: null } project)
            {
                return NotFound();
            }
            return Results.Ok(StateOf(org, project, refresh == true));
        }));

        api.MapPost("/select", (string projectId, ProjectVsCodeSelectDto dto) => ApiEndpoints.Handle(() =>
        {
            var org = Org();
            if (org.Projects.Get(projectId) is not { DeletedAt: null } project)
            {
                return NotFound();
            }
            if (ByPath(org, dto.CodePath) is not { } install)
            {
                return Results.Problem(Loc.T("msg.vscode.2"), statusCode: StatusCodes.Status400BadRequest);
            }
            registry.ProjectVsCode.Set(org.Org.Id, project.Id, install.CodePath);
            return Results.Ok(StateOf(org, project, false));
        }));

        api.MapPost("/connect", (string projectId, ProjectVsCodeConnectDto dto) => ApiEndpoints.Handle(() =>
        {
            // встроенный режим сам себе токенов не выпускает (как и в T-508-S0)
            if (Ai2pAuth.EmbedOrgId(current.HttpUser) is not null)
            {
                return Results.Problem(Loc.T("msg.deviceToken.3"), statusCode: StatusCodes.Status403Forbidden);
            }
            var org = Org();
            if (org.Projects.Get(projectId) is not { DeletedAt: null } project)
            {
                return NotFound();
            }
            var folder = project.FolderPath.Trim();
            if (folder.Length == 0 || !Directory.Exists(folder))
            {
                return Results.Problem(Loc.T("msg.vscode.3"), statusCode: StatusCodes.Status400BadRequest);
            }
            var installs = registry.VsCode.FindWith(PluginCodePath(org));
            var install = ByPath(org, dto.CodePath)
                          ?? ByPath(org, registry.ProjectVsCode.Get(org.Org.Id, project.Id))
                          ?? installs.FirstOrDefault(i => i.Internal)
                          ?? (installs.Count == 1 ? installs[0] : null);
            if (install is null)
            {
                return Results.Problem(Loc.T("msg.vscode.2"), statusCode: StatusCodes.Status400BadRequest);
            }
            registry.ProjectVsCode.Set(org.Org.Id, project.Id, install.CodePath);

            var result = new ProjectVsCodeConnectResultDto();
            // 1. расширение: .vsix из выкладки (этап 8.6 кладёт его в <программа>/vscode)
            if (VsCodeExtension.VsixPath(AppContext.BaseDirectory) is { } vsix)
            {
                var run = VsCodeRunner.Run(install.CodePath, VsCodeExtension.InstallArgs(vsix),
                    TimeSpan.FromMinutes(3));
                result.ExtensionInstalled = run.ExitCode == 0;
                if (!result.ExtensionInstalled)
                {
                    result.Warnings.Add(Loc.T("msg.vscode.4", Tail(run.Output)));
                }
            }
            else
            {
                result.Warnings.Add(Loc.T("msg.vscode.5"));
            }

            // 2. link.json — секретов в нём нет
            var server = VsCodeLink.ServerUrl(port, basePath);
            var link = BuildLink(server, basePath, org.Org.Code, project.Id, install.CodePath);
            Directory.CreateDirectory(Path.Combine(folder, VsCodeLink.DirName));
            File.WriteAllText(VsCodeLink.PathIn(folder), link.ToJson());
            result.Connected = true;

            // 3. .gitignore и 4. открыть проект — ДО токена: ссылку примет уже окно проекта (T-522-S0)
            if (dto.AddToGitIgnore)
            {
                VsCodeLink.AddToGitIgnore(folder);
            }
            var openedAt = DateTime.UtcNow;
            if (dto.Open)
            {
                var open = VsCodeRunner.Run(install.CodePath, [folder], TimeSpan.FromSeconds(60));
                if (open.ExitCode != 0)
                {
                    result.Warnings.Add(Loc.T("msg.vscode.7", Tail(open.Output)));
                }
            }

            // 5. токен устройства — ссылкой URI handler'а выбранной копии
            var sent = SendToken(install, server, org.Org.Code, org.Org.Id, project.Id, folder,
                dto.Open ? openedAt : null);
            result.TokenSent = sent is null;
            if (sent is not null)
            {
                result.Warnings.Add(sent);
            }
            return Results.Ok(result);
        }));

        // «Открыть проект в VS Code» (T-516-S0): code <каталог проекта> той копией, к которой подключён
        api.MapPost("/open", (string projectId) => ApiEndpoints.Handle(() =>
        {
            var org = Org();
            if (org.Projects.Get(projectId) is not { DeletedAt: null } project)
            {
                return NotFound();
            }
            var state = StateOf(org, project, false);
            if (!state.HasFolder)
            {
                return Results.Problem(Loc.T("msg.vscode.3"), statusCode: StatusCodes.Status400BadRequest);
            }
            if (state.ConnectedInstall is not { } install)
            {
                return Results.Problem(Loc.T("msg.vscode.2"), statusCode: StatusCodes.Status400BadRequest);
            }
            // T-335: расширения в этой копии может не быть (проект подключали на другом сервере,
            // link.json приехал с файлами проекта) — без него ссылка с токеном даёт в VS Code
            // «extension 'ai2p.ai2p' cannot be installed because it was not found»
            var ext = EnsureExtension(install);
            // T-338: link.json, записанный до петлевого HTTP при HTTPS, указывает на порт HTTPS
            // по http:// — расширение пишет «сервер не отвечает». Адрес поправляется на месте
            if (VsCodeLink.Read(project.FolderPath.Trim()) is { } old
                && old.Server.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase)
                && old.Server != VsCodeLink.ServerUrl(port, basePath))
            {
                old.Server = VsCodeLink.ServerUrl(port, basePath);
                File.WriteAllText(VsCodeLink.PathIn(project.FolderPath.Trim()), old.ToJson());
            }
            var openedAt = DateTime.UtcNow;
            var open = VsCodeRunner.Run(install.CodePath, [project.FolderPath.Trim()], TimeSpan.FromSeconds(60));
            if (open.ExitCode != 0)
            {
                return Results.Problem(Loc.T("msg.vscode.7", Tail(open.Output)), statusCode: StatusCodes.Status500InternalServerError);
            }
            if (ext is not null)
            {
                return Results.Problem(ext, statusCode: StatusCodes.Status500InternalServerError);
            }
            // T-522-S0: токен устройства передаётся и здесь. Прежний мог до расширения не дойти
            // (расширения ещё не было, вопрос VS Code «открыть ссылку?» отклонён) — тогда link.json
            // есть, карточка считает проект подключённым, а панели пишут «VS Code не подключён».
            // Из встроенного режима токенов не выпускаем: раз страница открыта в VS Code, токен там есть.
            if (Ai2pAuth.EmbedOrgId(current.HttpUser) is null)
            {
                SendToken(install, VsCodeLink.ServerUrl(port, basePath), org.Org.Code, org.Org.Id, project.Id,
                    project.FolderPath.Trim(), openedAt);
            }
            return Results.Ok();
        }));

        // Расширение AI2P в копии стоит и не старше программы; иначе ставится из .vsix выкладки.
        // null — на месте, иначе текст предупреждения (T-335)
        string? EnsureExtension(VsCodeInstall install)
        {
            var list = VsCodeRunner.Run(install.CodePath, VsCodeExtension.ListArgs, TimeSpan.FromSeconds(60));
            var installed = VsCodeExtension.InstalledVersion(list.Output);
            if (list.ExitCode == 0 && installed is not null && !VsCodeExtension.IsOlder(installed, AppInfo.Version))
            {
                return null;
            }
            if (VsCodeExtension.VsixPath(AppContext.BaseDirectory) is not { } vsix)
            {
                return installed is null ? Loc.T("msg.vscode.5") : null;
            }
            var run = VsCodeRunner.Run(install.CodePath, VsCodeExtension.InstallArgs(vsix), TimeSpan.FromMinutes(3));
            return run.ExitCode == 0 || installed is not null ? null : Loc.T("msg.vscode.4", Tail(run.Output));
        }

        // Выпустить токен устройства и передать его ссылкой vscode://…/link. Прежние токены той же
        // копии VS Code этого человека по проекту отзываются — новый ляжет на их место в SecretStorage.
        // null — передан, иначе текст предупреждения.
        // openedAt — когда окно папки открывали командой code <каталог>: ссылку адресуем ему (T-344),
        // подождав, пока оно запишет свой номер; null — окна не открывали, берём записанный номер как есть.
        string? SendToken(VsCodeInstall install, string server, string orgCode, string orgId, string projectId,
            string folder, DateTime? openedAt)
        {
            var account = Account();
            var name = install.Name + " (" + Environment.MachineName + ")";
            var issued = registry.DeviceTokens.Issue(account, orgId, projectId, name);
            foreach (var old in registry.DeviceTokens.List(account, orgId, projectId))
            {
                if (old.Id != issued.Record.Id && old.Name == issued.Record.Name)
                {
                    registry.DeviceTokens.Revoke(old.Id, account);
                }
            }
            var token = issued.Token;
            var windowId = VsCodeLink.WindowId(folder, openedAt ?? DateTime.MinValue, TimeSpan.FromSeconds(10));
            var uri = VsCodeLink.LinkUri(install.UriScheme, token, server, orgCode, projectId, windowId);
            var sent = VsCodeRunner.Run(install.CodePath, ["--open-url", uri], TimeSpan.FromSeconds(60));
            return sent.ExitCode == 0 ? null : Loc.T("msg.vscode.6", Tail(sent.Output.Replace(token, "***")));
        }

        api.MapPost("/disconnect", (string projectId) => ApiEndpoints.Handle(() =>
        {
            var org = Org();
            if (org.Projects.Get(projectId) is not { DeletedAt: null } project)
            {
                return NotFound();
            }
            var folder = project.FolderPath.Trim();
            if (folder.Length > 0 && File.Exists(VsCodeLink.PathIn(folder)))
            {
                File.Delete(VsCodeLink.PathIn(folder));
            }
            var revoked = registry.DeviceTokens.RevokeProject(Account(), org.Org.Id, project.Id);
            return Results.Ok(new { revoked });
        }));
    }

    /// <summary>Состояние для карточки проекта; кнопок нет без каталога или без VS Code.</summary>
    public static ProjectVsCodeDto State(OrgRegistry registry, string orgId, string projectId, string folderPath,
        bool refresh) => State(registry.VsCode, registry.ProjectVsCode, orgId, projectId, folderPath, refresh);

    public static ProjectVsCodeDto State(VsCodeLocator locator, AI2P.Storage.Services.ProjectVsCodeService choices,
        string orgId, string projectId, string folderPath, bool refresh, string? internalPath = null,
        bool pluginReady = false)
    {
        var folder = folderPath.Trim();
        var hasFolder = folder.Length > 0 && Directory.Exists(folder);
        var installs = locator.FindWith(internalPath, refresh);
        // T-335: link.json лежит в каталоге проекта и приезжает к другим серверам вместе с
        // файлами проекта — подключением ЭТОГО сервера он считается, только если его копия
        // VS Code здесь есть (путь найден среди копий или существует на диске)
        var link = hasFolder ? VsCodeLink.Read(folder) : null;
        var connected = link is not null && LinkIsLocal(link, installs);
        var selected = choices.Get(orgId, projectId);
        return new ProjectVsCodeDto
        {
            HasFolder = hasFolder,
            Available = hasFolder && installs.Count > 0,
            Connected = connected,
            ConnectedInstall = connected ? ConnectedInstall(installs, link?.Code, selected) : null,
            GitIgnoreHas = hasFolder && VsCodeLink.GitIgnoreHas(folder),
            Installs = installs,
            // выбора не было — по умолчанию копия плагина (T-530-S0)
            Selected = selected ?? installs.FirstOrDefault(i => i.Internal)?.CodePath,
            PluginReady = pluginReady,
        };
    }

    /// <summary>link.json записан этим сервером: код копии пуст, найден среди копий или есть на диске.</summary>
    public static bool LinkIsLocal(VsCodeLink link, List<VsCodeInstall> installs)
    {
        var code = link.Code.Trim();
        return code.Length == 0 || installs.Any(i => VsCodeLocator.SamePath(i.CodePath, code)) || File.Exists(code);
    }

    /// <summary>Копия подключения: code из link.json, иначе запомненная, иначе копия плагина, иначе единственная.</summary>
    public static VsCodeInstall? ConnectedInstall(List<VsCodeInstall> installs, string? linkCode, string? selected)
    {
        foreach (var path in new[] { linkCode, selected, installs.FirstOrDefault(i => i.Internal)?.CodePath })
        {
            if (path is { Length: > 0 } && installs.FirstOrDefault(i => VsCodeLocator.SamePath(i.CodePath, path)) is { } hit)
            {
                return hit;
            }
        }
        return installs.Count == 1 ? installs[0] : null;
    }

    /// <summary>Содержимое link.json: адрес, организация, проект, путь к серверу, версия, code.</summary>
    public static VsCodeLink BuildLink(string server, string basePath, string orgCode, string projectId,
        string codePath) => new()
    {
        Server = server,
        BasePath = basePath,
        Org = orgCode,
        ProjectId = projectId,
        ServerExe = Environment.ProcessPath ?? "",
        ServerArgs = [.. Environment.GetCommandLineArgs().Skip(1)],
        Version = AppInfo.Version,
        Code = codePath,
    };

    private static string Tail(string text) => text.Length > 300 ? text[^300..] : text;
}

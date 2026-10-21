namespace AI2P.Core.Api;

/// <summary>Состояние VS Code у проекта на ЭТОМ сервере (T-510-S0).</summary>
public sealed class ProjectVsCodeDto
{
    /// <summary>Кнопки показывать: у проекта есть каталог на этом сервере и VS Code найден.</summary>
    public bool Available { get; set; }

    /// <summary>Каталог проекта на этом сервере задан.</summary>
    public bool HasFolder { get; set; }

    /// <summary>Подключён: в каталоге проекта есть .ai2p/link.json.</summary>
    public bool Connected { get; set; }

    /// <summary>В .gitignore проекта уже есть .ai2p/.</summary>
    public bool GitIgnoreHas { get; set; }

    /// <summary>Копия, к которой подключён проект (code из link.json, иначе запомненная или
    /// единственная); null — не подключён или копия не найдена (T-516-S0).</summary>
    public VsCodeInstall? ConnectedInstall { get; set; }

    public List<VsCodeInstall> Installs { get; set; } = [];

    /// <summary>Запомненный выбор копии (путь к code); не выбирали — копия плагина «VS Code»
    /// (T-530-S0), а нет и её — null.</summary>
    public string? Selected { get; set; }

    /// <summary>Плагин «VS Code» подключён на этом сервере: запись инициализирована и найдены
    /// все его программы (VS Code и Node.js) — T-530-S0.</summary>
    public bool PluginReady { get; set; }
}

public sealed class ProjectVsCodeSelectDto
{
    public string CodePath { get; set; } = "";
}

/// <summary>«Подключить VS Code».</summary>
public sealed class ProjectVsCodeConnectDto
{
    /// <summary>Какой копией; пусто — запомненной или единственной.</summary>
    public string? CodePath { get; set; }

    public bool AddToGitIgnore { get; set; }

    /// <summary>Открыть «code &lt;каталог проекта&gt;».</summary>
    public bool Open { get; set; }
}

/// <summary>Итог подключения: что получилось по шагам.</summary>
public sealed class ProjectVsCodeConnectResultDto
{
    public bool Connected { get; set; }

    /// <summary>Расширение поставлено/обновлено; false — .vsix не найден или установка не удалась.</summary>
    public bool ExtensionInstalled { get; set; }

    /// <summary>Ссылка с токеном передана копии.</summary>
    public bool TokenSent { get; set; }

    /// <summary>Предупреждения для человека (уже переведены).</summary>
    public List<string> Warnings { get; set; } = [];
}

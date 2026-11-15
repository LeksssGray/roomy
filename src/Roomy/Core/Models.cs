namespace Roomy.Core;

public enum Safety { Safe, Review }

public enum CleanAction
{
    /// <summary>Delete files (for folders: delete the contents, keep the folder).</summary>
    Delete,
    EmptyRecycleBin,
    /// <summary>Hand off to Steam's own uninstaller.</summary>
    SteamUninstall,
    /// <summary>Only Windows can remove it safely, so we open Settings.</summary>
    OpenSettings,
}

public sealed class CleanupItem
{
    public required string Name { get; init; }
    public required IReadOnlyList<string> Paths { get; init; }
    public string Path => Paths.Count > 0 ? Paths[0] : "";
    public long Size { get; set; }
    public int FileCount { get; set; }
    public bool IsFile { get; init; }
    public string? Detail { get; init; }
    /// <summary>Only files older than this are measured and deleted.</summary>
    public TimeSpan? MinAge { get; init; }
    public string? AppId { get; init; }
}

public sealed class CleanupCategory
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Glyph { get; init; }
    public Safety Safety { get; init; } = Safety.Safe;
    public CleanAction Action { get; init; } = CleanAction.Delete;
    public bool SelectedByDefault { get; init; }
    public bool ShowItems { get; init; } = true;
    public bool Blocked { get; set; }
    public string? Note { get; set; }
    public List<CleanupItem> Items { get; } = [];
    public long Size => Items.Sum(i => i.Size);
}

/// <summary>Segoe Fluent Icons / MDL2 code points.</summary>
public static class G
{
    public const string Delete = "";
    public const string Clock = "";
    public const string Globe = "";
    public const string Apps = "";
    public const string Color = "";
    public const string Bug = "";
    public const string Update = "";
    public const string Package = "";
    public const string Download = "";
    public const string Game = "";
    public const string History = "";
    public const string Folder = "";
    public const string Document = "";
    public const string Shield = "";
    public const string Check = "";
    public const string ChevronDown = "";
    public const string ChevronUp = "";
    public const string ChevronRight = "";
}

/// <summary>The fixed copy for every category, shared by the real scanner and demo mode.</summary>
public static class Catalog
{
    public static CleanupCategory Create(string id) => id switch
    {
        "recycle" => new CleanupCategory
        {
            Id = id, Title = "Корзина", Glyph = G.Delete,
            Description = "Удалённые файлы продолжают занимать место, пока вы не очистите корзину.",
            Action = CleanAction.EmptyRecycleBin, SelectedByDefault = true, ShowItems = false,
        },
        "temp" => new CleanupCategory
        {
            Id = id, Title = "Временные файлы", Glyph = G.Clock,
            Description = "Файлы, которые программы оставили после работы. Только файлы старше суток.",
            SelectedByDefault = true,
        },
        "browser" => new CleanupCategory
        {
            Id = id, Title = "Кэш браузеров", Glyph = G.Globe,
            Description = "Сохранённые копии страниц. Вход в аккаунты сохранится. При первом открытии сайты могут загружаться чуть дольше.",
            SelectedByDefault = true,
        },
        "apps" => new CleanupCategory
        {
            Id = id, Title = "Кэш приложений", Glyph = G.Apps,
            Description = "Кэш Discord, VS Code, Slack и других приложений. При необходимости программы создадут его заново.",
            SelectedByDefault = true,
        },
        "shaders" => new CleanupCategory
        {
            Id = id, Title = "Кэш шейдеров", Glyph = G.Color,
            Description = "Данные графики, заранее подготовленные видеокартой. Удалять безопасно. Игры создадут их заново, но при следующем запуске возможны подтормаживания.",
            SelectedByDefault = false,
        },
        "crash" => new CleanupCategory
        {
            Id = id, Title = "Отчёты о сбоях", Glyph = G.Bug,
            Description = "Отчёты Windows о сбоях приложений. Обычно нужны только разработчикам.",
            SelectedByDefault = true,
        },
        "windows" => new CleanupCategory
        {
            Id = id, Title = "Остатки обновлений Windows", Glyph = G.Update,
            Description = "Файлы уже установленных обновлений и временные файлы Windows.",
            SelectedByDefault = true,
        },
        "installers" => new CleanupCategory
        {
            Id = id, Title = "Старые установщики", Glyph = G.Package, Safety = Safety.Review,
            Description = "Установщики в папке «Загрузки», которые вы, скорее всего, уже использовали. Только файлы старше недели.",
        },
        "downloads" => new CleanupCategory
        {
            Id = id, Title = "Большие старые загрузки", Glyph = G.Download, Safety = Safety.Review,
            Description = "Большие файлы в папке «Загрузки», не использовавшиеся больше месяца: видео, образы дисков, архивы.",
        },
        "games" => new CleanupCategory
        {
            Id = id, Title = "Давно не запускавшиеся игры", Glyph = G.Game, Safety = Safety.Review,
            Description = "Игры Steam, которые не запускались больше двух месяцев. Удаляются через Steam. Их можно установить заново.",
            Action = CleanAction.SteamUninstall,
        },
        "winold" => new CleanupCategory
        {
            Id = id, Title = "Предыдущая версия Windows", Glyph = G.History, Safety = Safety.Review,
            Description = "Резервная копия Windows после крупного обновления. Нужна только для возврата к прежней версии.",
            Action = CleanAction.OpenSettings, ShowItems = false,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Неизвестная категория"),
    };
}

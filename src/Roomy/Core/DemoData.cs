namespace Roomy.Core;

/// <summary>
/// Made-up but realistic data for `--demo` and `--shots`, so screenshots and videos
/// never show anyone's real files.
/// </summary>
public static class DemoData
{
    const long MB = 1024L * 1024, GB = MB * 1024;
    static long Gb(double v) => (long)(v * GB);
    static long Mb(double v) => (long)(v * MB);

    public static readonly (string Root, string Label, long Total, long Free)[] Drives =
    [
        (@"C:\", "", Gb(237.9), Gb(9.6)),
        (@"D:\", "Games", Gb(931.5), Gb(212.4)),
    ];

    static CleanupItem I(string name, long size, string detail, bool file = false) => new()
    {
        Name = name, Paths = [$@"C:\Users\you\demo\{name}"], Size = size, Detail = detail, IsFile = file,
    };

    public static CleanupCategory? Category(string id)
    {
        var c = Catalog.Create(id);
        void A(string name, long size, string detail, bool file = false) => c.Items.Add(I(name, size, detail, file));
        switch (id)
        {
            case "recycle":
                A("Корзина", Gb(3.21), "Объектов: 214");
                break;
            case "temp":
                A("Ваша папка временных файлов", Gb(4.62), "Файлов: 18 402");
                break;
            case "browser":
                A("Google Chrome", Gb(2.08), "Файлов: 9 311");
                A("Microsoft Edge", Mb(712), "Файлов: 3 904");
                A("Firefox", Mb(388), "Файлов: 2 117");
                break;
            case "apps":
                A("Кэш npm", Gb(2.26), "Файлов: 41 870");
                A("Discord", Gb(1.38), "Файлов: 6 220");
                A("VS Code", Mb(864), "Файлов: 2 951");
                A("Браузер Steam", Mb(412), "Файлов: 1 480");
                A("Slack", Mb(296), "Файлов: 1 022");
                break;
            case "shaders":
                A("Игры Steam", Gb(3.62), "Файлов: 12 094");
                A("NVIDIA", Gb(1.91), "Файлов: 4 388");
                A("DirectX", Mb(742), "Файлов: 1 203");
                break;
            case "crash":
                A("Дампы сбоев приложений", Gb(1.12), "Файлов: 37");
                A("Отчёты об ошибках Windows", Mb(286), "Файлов: 412");
                break;
            case "windows":
                A("Скачанные обновления", Gb(6.31), "Файлов: 1 904");
                A("Оптимизация доставки", Gb(1.82), "Файлов: 96");
                A("Временные файлы Windows", Mb(524), "Файлов: 3 311");
                break;
            case "installers":
                A("Blender-4.3-windows-x64.msi", Mb(341), "Загружен: 4 месяца назад", true);
                A("OBS-Studio-31.0-Windows-Installer.exe", Mb(152), "Загружен: 3 месяца назад", true);
                A("NVIDIA_App_v11.0.exe", Mb(148), "Загружен: 5 недель назад", true);
                A("UnityHubSetup.exe", Mb(127), "Загружен: 7 месяцев назад", true);
                A("DiscordSetup.exe", Mb(112), "Загружен: год назад", true);
                A("VSCodeUserSetup-x64.exe", Mb(98), "Загружен: 2 месяца назад", true);
                A("python-3.13.1-amd64.exe", Mb(27), "Загружен: 6 месяцев назад", true);
                A("node-v22.11.0-x64.msi", Mb(29), "Загружен: 4 месяца назад", true);
                A("SteamSetup.exe", Mb(2.3), "Загружен: год назад", true);
                break;
            case "downloads":
                A("Windows11_InsiderPreview.iso", Gb(6.12), "Последнее использование: 8 месяцев назад", true);
                A("Recording 2026-03-14.mkv", Gb(3.24), "Последнее использование: 7 месяцев назад", true);
                A("final-edit-v3-export.mp4", Gb(2.41), "Последнее использование: 2 месяца назад", true);
                A("dataset-full.zip", Gb(1.83), "Последнее использование: 5 месяцев назад", true);
                break;
            case "games":
                c.Items.Add(new CleanupItem { Name = "Iron Frontier", Paths = [@"D:\Steam\steamapps\common\Iron Frontier"], Size = Gb(118.6), AppId = "0", Detail = "Последний запуск: год назад" });
                c.Items.Add(new CleanupItem { Name = "Neon Drift", Paths = [@"D:\Steam\steamapps\common\Neon Drift"], Size = Gb(71.2), AppId = "0", Detail = "Последний запуск: 7 месяцев назад" });
                c.Items.Add(new CleanupItem { Name = "Starfall Odyssey", Paths = [@"D:\Steam\steamapps\common\Starfall Odyssey"], Size = Gb(43.9), AppId = "0", Detail = "Последний запуск: 4 месяца назад" });
                c.Items.Add(new CleanupItem { Name = "Pixel Kingdoms", Paths = [@"D:\Steam\steamapps\common\Pixel Kingdoms"], Size = Gb(3.4), AppId = "0", Detail = "Ни разу не запускалась" });
                break;
            case "winold":
                A("Windows.old", Gb(21.4), "");
                break;
        }
        return c;
    }

    // ---------- space map ----------

    sealed record N(string Name, double Gb, params N[] Kids);

    static readonly N Tree = new("C:", 0,
        new N("Users", 88.6,
            new N("you", 88.1,
                new N("AppData", 37.9,
                    new N("Local", 29.0, new N("Temp", 4.6), new N("npm-cache", 2.3), new N("Google", 3.1), new N("Packages", 6.4), new N("Microsoft", 5.2), new N("Docker", 6.8)),
                    new N("Roaming", 8.2, new N("discord", 1.6), new N("Code", 1.9), new N("Spotify", 3.1)),
                    new N("LocalLow", 0.7)),
                new N("Downloads", 22.7),
                new N("Videos", 14.2),
                new N("Documents", 6.3),
                new N("Pictures", 4.1),
                new N("Desktop", 1.9),
                new N("OneDrive", 0.8)),
            new N("Public", 0.5)),
        new N("Program Files (x86)", 52.4,
            new N("Steam", 47.8),
            new N("Microsoft", 3.1),
            new N("Epic Games", 1.2)),
        new N("Windows", 28.9,
            new N("WinSxS", 9.6), new N("SoftwareDistribution", 6.4), new N("System32", 6.0), new N("Installer", 3.1), new N("SysWOW64", 1.6)),
        new N("Windows.old", 21.4),
        new N("Program Files", 15.2,
            new N("Adobe", 5.4), new N("NVIDIA Corporation", 2.6), new N("Microsoft Office", 3.9), new N("Docker", 1.8)),
        new N("ProgramData", 5.1),
        new N("$Recycle.Bin", 3.2));

    const double RootFilesGb = 12.0; // pagefile.sys, hiberfil.sys…

    public static SpaceNode SpaceTree() => Build(Tree, null, @"C:\");

    static SpaceNode Build(N n, SpaceNode? parent, string path)
    {
        var node = new SpaceNode { Name = n.Name, FullPath = path, Parent = parent };
        var kids = n.Kids.Select(k => Build(k, node, Path.Combine(path, k.Name))).OrderByDescending(k => k.Size).ToList();
        node.Children = kids;
        long kidBytes = kids.Sum(k => k.Size);
        long own = parent is null ? Gb(RootFilesGb) : Math.Max(0, Gb(n.Gb) - kidBytes);
        node.FileBytes = own;
        node.FileCount = own > 0 ? (int)(own / (3 * MB)) + 1 : 0;
        node.Size = own + kidBytes;
        return node;
    }

    public static IReadOnlyList<BigFile> BigFiles() =>
    [
        new(@"C:\pagefile.sys", Gb(8.0)),
        new(@"C:\Users\you\Downloads\Windows11_InsiderPreview.iso", Gb(6.12)),
        new(@"C:\Users\you\AppData\Local\Docker\wsl\disk\docker_data.vhdx", Gb(5.9)),
        new(@"C:\hiberfil.sys", Gb(3.9)),
        new(@"C:\Users\you\Downloads\Recording 2026-03-14.mkv", Gb(3.24)),
        new(@"C:\Users\you\Videos\Captures\ranked-clutch-4k.mp4", Gb(2.8)),
        new(@"C:\Users\you\Downloads\final-edit-v3-export.mp4", Gb(2.41)),
        new(@"C:\Program Files (x86)\Steam\steamapps\common\Neon Drift\data0.pak", Gb(2.2)),
        new(@"C:\Users\you\Downloads\dataset-full.zip", Gb(1.83)),
        new(@"C:\Windows.old\Windows\System32\MRT.exe", Gb(1.1)),
        new(@"C:\Users\you\AppData\Roaming\Spotify\Data\cache.bnk", Gb(0.96)),
        new(@"C:\Users\you\Videos\vacation-2025.mov", Gb(0.91)),
    ];
}

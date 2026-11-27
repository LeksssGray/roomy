using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Roomy.Core;
using Roomy.ViewModels;

namespace Roomy.Views;

public partial class ProgramsWindow : Window
{
    readonly bool _demo;
    readonly ProgramProjects _projects = new(ProgramProjects.DefaultPath);
    readonly ProgramHistory _history = new(ProgramHistory.DefaultPath);
    readonly CancellationTokenSource _lifetime = new();
    List<ProgramUsage> _programs = [];
    List<ProgramRow> _visible = [];
    List<ProjectLink> _links = [];
    ProgramUsage? _beforeRemoval;
    bool _busy;
    bool _updatingDrives;
    bool _projectsReadable = true;

    public ProgramsWindow(bool demo)
    {
        _demo = demo;
        InitializeComponent();
        DriveFilter.Items.Add("Все диски");
        DriveFilter.SelectedIndex = 0;
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) =>
        {
            _lifetime.Cancel();
            _lifetime.Dispose();
        };
    }

    public MeasuredFolder? FolderToOpen { get; private set; }

    async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        RefreshButton.IsEnabled = false;
        StatusText.Text = "Проверяем программы и связанные папки…";
        ShowDetails();
        var ct = _lifetime.Token;
        try
        {
            var pending = _beforeRemoval;
            var result = await Task.Run(() =>
            {
                var readError = false;
                List<ProjectLink> links = [];
                if (!_demo)
                {
                    try { links = _projects.Load(); }
                    catch (Exception ex) when (ProgramInventory.IsAccessError(ex) || ex is JsonException) { readError = true; }
                }
                var historyError = false;
                List<ProgramUsage> programs;
                if (_demo) programs = DemoPrograms();
                else
                {
                    var installed = ProgramEvidence.Enrich(ProgramInventory.Read(), ct);
                    var inventory = _history.Merge(installed, ct);
                    historyError = inventory.HistoryError;
                    programs = ProgramInventory.Measure(inventory.Programs, links, ct);
                }
                string? removal = pending is null ? null : RemovalSummary(pending, programs, ct);
                return (programs, links, readError, historyError, removal);
            }, ct);
            ct.ThrowIfCancellationRequested();
            _programs = result.programs;
            _links = result.links;
            _projectsReadable = !result.readError;
            if (pending is not null && !_programs.Any(p => !p.Program.IsResidue && p.Program.Id == pending.Program.Id))
            {
                _beforeRemoval = null;
                ResidueFilter.IsChecked = true;
                DriveFilter.SelectedIndex = 0;
            }
            PopulateDrives();
            ApplyFilter();
            StatusText.Text = result.removal ?? (!_projectsReadable
                ? "Не удалось прочитать сохранённые привязки проектов."
                : result.historyError ? "Не удалось прочитать или сохранить историю папок и записей."
                : "Размер рассчитан по найденным папкам. Оценки Windows помечены отдельно.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            StatusText.Text = "Не удалось проверить программы. " + ex.Message;
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                _busy = false;
                RefreshButton.IsEnabled = true;
                ShowDetails();
            }
        }
    }

    static string RemovalSummary(ProgramUsage before, List<ProgramUsage> after, CancellationToken ct)
    {
        if (after.Any(p => !p.Program.IsResidue && p.Program.Id == before.Program.Id))
            return $"«{before.Program.Name}» ещё в списке. Завершите удаление и нажмите «Обновить».";

        var oldFolders = before.Folders.Where(f => f.State == FolderSizeState.Measured &&
            !f.Folder.Suggested && f.Folder.Kind != ProgramFolderKind.Project).ToArray();
        var cuts = after.SelectMany(p => p.Folders).Select(f => f.Folder.Path)
            .Concat(before.Folders.Select(f => f.Folder.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = oldFolders.Select(f => ProgramInventory.MeasureFolder(f.Folder, cuts, ct)).ToArray();
        var comparable = oldFolders.Zip(current)
            .Where(pair => pair.Second.State is FolderSizeState.Measured or FolderSizeState.Missing).ToArray();
        if (comparable.Length == 0)
            return $"«{before.Program.Name}» больше не найдена. Нет сопоставимых размеров папок.";
        var delta = comparable.Sum(p => p.First.Bytes - p.Second.Bytes);
        var remaining = comparable.Sum(p => p.Second.Bytes);
        var change = delta < 0 ? $"стало больше на {Format.Bytes(-delta)}" : $"стало меньше на {Format.Bytes(delta)}";
        return $"«{before.Program.Name}» больше не найдена. В сопоставимых папках {change}, осталось {Format.Bytes(remaining)}.";
    }

    void PopulateDrives()
    {
        _updatingDrives = true;
        var active = DriveFilter.SelectedItem as string;
        while (DriveFilter.Items.Count > 1) DriveFilter.Items.RemoveAt(1);
        var drives = _programs.SelectMany(p => p.Folders)
            .Select(f => Path.GetPathRoot(f.Folder.Path)!)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d);
        foreach (var drive in drives) DriveFilter.Items.Add(drive);
        DriveFilter.SelectedItem = active is not null && DriveFilter.Items.Contains(active) ? active : DriveFilter.Items[0];
        _updatingDrives = false;
    }

    void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    void ApplyFilter()
    {
        if (_updatingDrives || ProgramList is null || SearchBox is null || DriveFilter is null) return;
        var previous = (ProgramList.SelectedItem as ProgramRow)?.Usage.Program.Id;
        var query = SearchBox.Text.Trim();
        var drive = DriveFilter.SelectedIndex > 0 ? DriveFilter.SelectedItem as string : null;
        var residues = ResidueFilter?.IsChecked == true;
        bool Matches(string text) => text.Contains(query, StringComparison.CurrentCultureIgnoreCase);
        _visible = _programs.Where(p => p.Program.IsResidue == residues)
            .Select(p => new ProgramRow(p, drive))
            .Where(p => drive is null || p.Folders.Count > 0)
            .Where(p => Matches(p.Name) || Matches(p.Usage.Program.Publisher) ||
                p.Folders.Any(f => Matches(f.Folder.Path)) ||
                p.Usage.Program.Registry.Any(r => Matches(r.DisplayPath) ||
                    r.ExpectedExecutable is { } executable && Matches(executable)))
            .OrderByDescending(p => p.SortBytes).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        ProgramList.ItemsSource = _visible;
        ProgramList.SelectedItem = _visible.FirstOrDefault(p => p.Usage.Program.Id == previous) ?? _visible.FirstOrDefault();
        EmptyDetails.Text = _visible.Count == 0
            ? (residues ? "В истории Roomy следы удаления не найдены." : "Программы не найдены.")
            : "Выберите программу, чтобы посмотреть её папки.";
        if (MapMode.IsChecked == true) DrawMap();
    }

    void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (MapHost is null || ProgramList is null) return;
        var map = MapMode.IsChecked == true;
        ProgramList.Visibility = map ? Visibility.Collapsed : Visibility.Visible;
        MapHost.Visibility = map ? Visibility.Visible : Visibility.Collapsed;
        if (map) DrawMap();
    }

    void Map_Resized(object sender, SizeChangedEventArgs e) => DrawMap();

    void DrawMap()
    {
        MapCanvas.Children.Clear();
        var measured = _visible.Where(p => p.MeasuredBytes > 0).OrderByDescending(p => p.MeasuredBytes).ToArray();
        var tiles = measured.Take(18).ToArray();
        MapCaption.Text = tiles.Length == 0 ? "Нет программ с измеренным размером."
            : "Площадь плитки соответствует измеренному размеру. Остальные программы доступны в списке.";
        if (tiles.Length == 0 || MapCanvas.ActualWidth < 1 || MapCanvas.ActualHeight < 1) return;
        PlaceTiles(tiles, new Rect(0, 0, MapCanvas.ActualWidth, MapCanvas.ActualHeight));
    }

    void PlaceTiles(ReadOnlySpan<ProgramRow> rows, Rect area)
    {
        if (rows.Length == 1)
        {
            if (area.Width <= 3 || area.Height <= 3) return;
            var row = rows[0];
            var button = new Button
            {
                Width = area.Width - 2, Height = area.Height - 2, Padding = new Thickness(5, 2, 5, 2),
                Style = (Style)FindResource("SecondaryButton"),
                Tag = row,
                BorderBrush = Res.Brush(row == ProgramList.SelectedItem ? "AccentBrush" : "LineBrush"),
                ToolTip = row.Name + " · " + Format.Bytes(row.MeasuredBytes),
                Content = new TextBlock
                {
                    Text = area.Height >= 53 ? row.Name + "\n" + Format.Bytes(row.MeasuredBytes) : row.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = TextAlignment.Center,
                },
            };
            System.Windows.Automation.AutomationProperties.SetName(button, (string)button.ToolTip);
            button.Click += (_, _) => ProgramList.SelectedItem = row;
            Canvas.SetLeft(button, area.Left);
            Canvas.SetTop(button, area.Top);
            MapCanvas.Children.Add(button);
            return;
        }

        double total = 0;
        foreach (var row in rows) total += row.MeasuredBytes;
        var split = 1;
        double first = rows[0].MeasuredBytes;
        while (split < rows.Length - 1 && first + rows[split].MeasuredBytes / 2.0 < total / 2)
            first += rows[split++].MeasuredBytes;
        var ratio = first / total;
        if (area.Width >= area.Height)
        {
            var width = area.Width * ratio;
            PlaceTiles(rows[..split], new Rect(area.X, area.Y, width, area.Height));
            PlaceTiles(rows[split..], new Rect(area.X + width, area.Y, area.Width - width, area.Height));
        }
        else
        {
            var height = area.Height * ratio;
            PlaceTiles(rows[..split], new Rect(area.X, area.Y, area.Width, height));
            PlaceTiles(rows[split..], new Rect(area.X, area.Y + height, area.Width, area.Height - height));
        }
    }

    void Program_Changed(object sender, SelectionChangedEventArgs e)
    {
        ShowDetails();
        if (MapCanvas is null) return;
        foreach (var tile in MapCanvas.Children.OfType<Button>())
            tile.BorderBrush = Res.Brush(tile.Tag == ProgramList.SelectedItem ? "AccentBrush" : "LineBrush");
    }

    void ShowDetails()
    {
        if (Details is null) return;
        var row = ProgramList.SelectedItem as ProgramRow;
        Details.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        EmptyDetails.Visibility = row is null ? Visibility.Visible : Visibility.Collapsed;
        if (row is null) return;
        var app = row.Usage.Program;
        ProgramName.Text = app.Name;
        var footprint = app.IsResidue ? "Отсутствует в текущем списке" :
            app.Registry.Count > 0 && row.Folders.Any(f => f.Folder.Kind == ProgramFolderKind.Installation) &&
            row.Folders.Where(f => f.Folder.Kind == ProgramFolderKind.Installation).All(f => f.State == FolderSizeState.Missing)
                ? "Путь установки не найден" : "";
        ProgramDetail.Text = string.Join(" · ", new[] { row.Source, app.Publisher, app.Version, row.Size, footprint }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
        ProjectButton.IsEnabled = !_demo && !_busy && _projectsReadable && !app.IsResidue;
        RemoveButton.IsEnabled = !_demo && !_busy && app.RemovalId != null;
        Folders.Children.Clear();
        if (row.Folders.Count == 0) Folders.Children.Add(new TextBlock
        {
            Text = app.IsResidue ? "Сохранились записи реестра." : "Папки не определены. Можно привязать папку проекта.",
            Style = (Style)FindResource("MutedText"),
        });
        foreach (var folder in row.Folders.OrderBy(f => f.Folder.Kind).ThenByDescending(f => f.Bytes))
            AddFolderRow(app, folder);
        if (app.Registry.Count == 0) return;
        Folders.Children.Add(new TextBlock
        {
            Text = "Реестр", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 6),
        });
        foreach (var item in app.Registry)
        {
            Folders.Children.Add(new TextBlock
            {
                Text = item.Source + " · " + item.DisplayPath,
                Style = (Style)FindResource("MutedText"), Margin = new Thickness(0, 0, 0, 6), FontSize = 12,
            });
            AddLink(Folders, "Копировать запись", () =>
            {
                try { Clipboard.SetText(item.DisplayPath); }
                catch (System.Runtime.InteropServices.ExternalException) { StatusText.Text = "Не удалось скопировать запись."; }
            });
        }
    }

    void AddFolderRow(InstalledProgram app, MeasuredFolder folder)
    {
        var label = folder.Folder.Kind switch
        {
            ProgramFolderKind.Installation => "Установка",
            ProgramFolderKind.Cache => "Кэш",
            ProgramFolderKind.Temporary => "Временные файлы",
            ProgramFolderKind.Project => "Проект",
            _ => "Данные",
        };
        var size = folder.State switch
        {
            FolderSizeState.Measured => Format.Bytes(folder.Bytes),
            FolderSizeState.Partial => "от " + Format.Bytes(folder.Bytes),
            FolderSizeState.Shared => folder.Bytes > 0 ? "общая папка · ≈ " + Format.Bytes(folder.Bytes) : "общая папка",
            FolderSizeState.Missing => "не найдена",
            _ => "нет доступа или путь исключён",
        };
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(new TextBlock { Text = label + " · " + size, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = folder.Folder.Path, Style = (Style)FindResource("MutedText"),
            Margin = new Thickness(0, 3, 0, 4), FontSize = 12,
        });
        if (folder.Folder.Evidence is { } evidence)
            panel.Children.Add(new TextBlock
            {
                Text = folder.Folder.Suggested ? evidence + " · вне итога" : evidence,
                Style = (Style)FindResource("MutedText"), FontSize = 12,
            });
        var actions = new WrapPanel();
        if (folder.State is not (FolderSizeState.Missing or FolderSizeState.Unavailable))
            AddLink(actions, "На карте диска", () =>
            {
                FolderToOpen = folder;
                Close();
            });
        if (!_demo && !_busy && _projectsReadable && _links.Any(l => SameLink(l, app.Id, folder.Folder.Path)))
            AddLink(actions, "Отвязать проект", async () =>
                await SaveProjectsAsync(_links.Where(l => !SameLink(l, app.Id, folder.Folder.Path)).ToList()));
        panel.Children.Add(actions);
        Folders.Children.Add(panel);
    }

    void AddLink(Panel parent, string text, Action click)
    {
        var button = new Button { Content = text, Style = (Style)FindResource("LinkButton"), Margin = new Thickness(0, 0, 12, 0) };
        button.Click += (_, _) => click();
        parent.Children.Add(button);
    }

    static bool SameLink(ProjectLink link, string id, string path) =>
        string.Equals(link.ProgramId, id, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(link.Path, path, StringComparison.OrdinalIgnoreCase);

    async void AddProject_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _demo || !_projectsReadable || ProgramList.SelectedItem is not ProgramRow row || row.Usage.Program.IsResidue) return;
        var picker = new OpenFolderDialog { Title = "Выберите папку проекта" };
        if (picker.ShowDialog(this) != true) return;
        var path = ProgramInventory.NormalizeFolder(picker.FolderName);
        if (path is null || !Directory.Exists(path))
        {
            StatusText.Text = "Выберите отдельную папку проекта на локальном диске.";
            return;
        }
        var id = row.Usage.Program.Id;
        if (_links.Any(l => SameLink(l, id, path)) ||
            row.Usage.Folders.Any(f => string.Equals(f.Folder.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText.Text = "Эта папка уже есть в карточке программы.";
            return;
        }
        await SaveProjectsAsync([.._links, new(id, path)]);
    }

    async Task SaveProjectsAsync(List<ProjectLink> links)
    {
        if (_busy) return;
        try { _projects.Save(links); }
        catch (Exception ex) when (ProgramInventory.IsAccessError(ex))
        {
            StatusText.Text = "Не удалось сохранить привязки проектов. " + ex.Message;
            return;
        }
        await RefreshAsync();
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _demo || ProgramList.SelectedItem is not ProgramRow row ||
            row.Usage.Program.RemovalId is not { } id) return;
        var app = row.Usage.Program;
        var confirmation = $"Открыть штатное удаление «{app.Name}»? Оно может удалить данные программы. Roomy не удаляет связанные папки.";
        if (MessageBox.Show(this, confirmation, "Удаление программы", MessageBoxButton.OKCancel,
                MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        var info = app.Source switch
        {
            ProgramSource.Steam => new ProcessStartInfo($"steam://uninstall/{id}") { UseShellExecute = true },
            ProgramSource.Msi => new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"))
            {
                UseShellExecute = false, ArgumentList = { "/x", id },
            },
            ProgramSource.Store => new ProcessStartInfo("ms-settings:appsfeatures-app?" + Uri.EscapeDataString(id)) { UseShellExecute = true },
            _ => new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true },
        };
        try
        {
            using var process = Process.Start(info);
            _beforeRemoval = row.Usage;
            StatusText.Text = app.Source == ProgramSource.Desktop
                ? $"Найдите «{app.Name}» в списке Windows и удалите её. Затем нажмите «Обновить»."
                : "Завершите удаление в открывшемся окне. Затем нажмите «Обновить», чтобы проверить остатки.";
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            StatusText.Text = "Не удалось открыть удаление. " + ex.Message;
        }
    }

    async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    static List<ProgramUsage> DemoPrograms()
    {
        const long mb = 1024L * 1024;
        static MeasuredFolder Folder(string path, ProgramFolderKind kind, long bytes) =>
            new(new(path, kind), bytes, FolderSizeState.Measured);
        static ProgramUsage App(string name, ProgramSource source, params MeasuredFolder[] folders) =>
            new(new(name, name, "", "", source, null, null, []), folders);
        return
        [
            App("Visual Studio Code", ProgramSource.Desktop,
                Folder(@"C:\Users\you\AppData\Local\Programs\Microsoft VS Code", ProgramFolderKind.Installation, 402 * mb),
                Folder(@"C:\Users\you\AppData\Roaming\Code", ProgramFolderKind.Data, 86 * mb),
                Folder(@"C:\Users\you\AppData\Roaming\Code\Cache", ProgramFolderKind.Cache, 912 * mb),
                Folder(@"D:\Projects\Demo", ProgramFolderKind.Project, 6200 * mb)),
            App("Docker Desktop", ProgramSource.Desktop,
                Folder(@"C:\Users\you\AppData\Local\Docker", ProgramFolderKind.Data, 14900 * mb)),
            App("Игра из библиотеки Steam", ProgramSource.Steam,
                Folder(@"D:\SteamLibrary\steamapps\common\Demo", ProgramFolderKind.Installation, 22900 * mb)),
            App("Фотографии", ProgramSource.Store,
                Folder(@"C:\Program Files\WindowsApps\Photos", ProgramFolderKind.Installation, 512 * mb)),
        ];
    }

    sealed class ProgramRow(ProgramUsage usage, string? drive)
    {
        public ProgramUsage Usage => usage;
        public IReadOnlyList<MeasuredFolder> Folders { get; } = drive is null ? usage.Folders : usage.Folders
            .Where(f => string.Equals(Path.GetPathRoot(f.Folder.Path), drive, StringComparison.OrdinalIgnoreCase)).ToArray();
        public long MeasuredBytes => Folders.Where(f => f.HasSize).Sum(f => f.Bytes);
        public long SortBytes => Folders.Any(f => f.HasSize) ? MeasuredBytes : drive is null ? usage.Program.ReportedBytes ?? -1 : -1;
        public string Name => usage.Program.Name;
        public string Source => usage.Program.Source switch
        {
            ProgramSource.Store => "Microsoft Store",
            ProgramSource.Steam => "Steam",
            ProgramSource.Msi => "Windows Installer",
            _ => "Windows",
        };
        public string Size => Folders.Any(f => f.HasSize)
            ? (Folders.Any(f => f.State is FolderSizeState.Partial or FolderSizeState.Unavailable) ? "от " : "") + Format.Bytes(MeasuredBytes)
            : drive is null && usage.Program.ReportedBytes is long estimate ? "≈ " + Format.Bytes(estimate) : "Нет замера";
    }
}

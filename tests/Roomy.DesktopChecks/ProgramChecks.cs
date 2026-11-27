using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Roomy.Core;
using Roomy.ViewModels;
using Roomy.Views;

namespace Roomy.DesktopChecks;

static class ProgramChecks
{
    internal static async Task Run(string? snapshot)
    {
        CheckMeasurement();
        var window = new ProgramsWindow(demo: true)
        {
            Left = -20000, Top = -20000, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        window.Show();
        try
        {
            T Find<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
            var list = Find<ListBox>("ProgramList");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (list.Items.Count == 0) await Task.Delay(25, timeout.Token);
            Require(list.Items.Count == 4, "The programs list should load demo entries.");
            var search = Find<TextBox>("SearchBox");
            search.Text = "Visual";
            Require(list.Items.Count == 1, "Search should narrow the list by program name.");
            Require(!Find<Button>("RemoveButton").IsEnabled && !Find<Button>("ProjectButton").IsEnabled,
                "Demo must not uninstall programs or persist links.");
            search.Clear();
            var drive = Find<ComboBox>("DriveFilter");
            drive.SelectedItem = @"D:\";
            Require(list.Items.Count == 2, "Drive filtering should include the manually linked demo project.");
            drive.SelectedIndex = 0;
            Find<RadioButton>("MapMode").IsChecked = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var map = Find<Canvas>("MapCanvas");
            Require(map.Children.Count == 4, "The map should display the four measured demo programs.");
            var tile = map.Children.OfType<Button>().Single(b => ((string)b.ToolTip).StartsWith("Visual Studio Code"));
            tile.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(Find<TextBlock>("ProgramName").Text == "Visual Studio Code", "The tile should select the same program as the list.");
            Require(Find<StackPanel>("Folders").Children.Count == 4, "A program should distinguish installation, cache, data and projects.");
            if (snapshot is not null)
            {
                window.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth * 2, (int)window.ActualHeight * 2,
                    192, 192, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(window);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(snapshot);
                png.Save(output);
            }
            var folderPanel = (StackPanel)Find<StackPanel>("Folders").Children[0];
            var folderButton = folderPanel.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().Single();
            folderButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Require(window.FolderToOpen is not null && !window.IsVisible, "The folder action should return a path to the main window.");
            var main = new MainVM(demo: true);
            await main.OpenProgramFolderAsync(window.FolderToOpen!);
            Require(main.SpaceMap.CurrentSizeText == Format.Bytes(window.FolderToOpen!.Bytes) && main.SpaceMap.BigFiles.Count == 0,
                "The focused demo map should show the selected folder size without unrelated files.");
            Require(main.State == AppState.Results && main.IsSpaceTab && main.SpaceMap.HasData && !main.HasCleanupScan,
                "Opening a program folder should lead to the disk map directly from Home.");
        }
        finally
        {
            if (window.IsVisible) window.Close();
        }
        Console.WriteLine("PASS: program search, drive filter, treemap selection, folder navigation and demo restrictions.");
    }

    static void CheckMeasurement()
    {
        var root = Directory.CreateTempSubdirectory("Roomy-program-checks-");
        try
        {
            var cache = root.CreateSubdirectory("cache");
            var project = root.CreateSubdirectory("project");
            var shared = root.CreateSubdirectory("shared");
            File.WriteAllBytes(Path.Combine(root.FullName, "install.bin"), new byte[30]);
            File.WriteAllBytes(Path.Combine(cache.FullName, "cache.bin"), new byte[20]);
            File.WriteAllBytes(Path.Combine(project.FullName, "project.bin"), new byte[10]);
            File.WriteAllBytes(Path.Combine(shared.FullName, "shared.bin"), new byte[100]);
            ProgramFolder Shared() => new(shared.FullName, ProgramFolderKind.Data);
            var programs = new InstalledProgram[]
            {
                new("app1", "App 1", "", "", ProgramSource.Desktop, null, 500,
                    [new(root.FullName, ProgramFolderKind.Installation), new(cache.FullName, ProgramFolderKind.Cache), Shared()]),
                new("app2", "App 2", "", "", ProgramSource.Desktop, null, null, [Shared()]),
            };
            var projects = new ProgramProjects(Path.Combine(root.FullName, "links.json"));
            projects.Save([new("app1", project.FullName)]);
            var measured = ProgramInventory.Measure(programs, projects.Load(), CancellationToken.None);
            var folders = measured[0].Folders;
            var installBytes = 30 + new FileInfo(Path.Combine(root.FullName, "links.json")).Length;
            Require(folders.Single(f => f.Folder.Kind == ProgramFolderKind.Installation).Bytes == installBytes,
                "Nested cache, project and shared paths must be excluded from the installation total.");
            Require(measured[0].Bytes == installBytes + 20 + 10 &&
                measured.All(p => p.Folders.Single(f => f.Folder.Path == shared.FullName) is { State: FolderSizeState.Shared, Bytes: 100 }),
                "Shared folders must not inflate any program total.");
            Require(folders.Single(f => f.Folder.Kind == ProgramFolderKind.Project).Bytes == 10,
                "A persisted project link should be measured as a project.");
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try
            {
                ProgramInventory.Measure(programs, projects.Load(), canceled.Token);
                throw new InvalidOperationException("Measurement should honor cancellation.");
            }
            catch (OperationCanceledException) { }
        }
        finally
        {
            root.Delete(recursive: true);
        }
        Console.WriteLine("PASS: program sizes, nested and shared folders, persisted project links and cancellation.");
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

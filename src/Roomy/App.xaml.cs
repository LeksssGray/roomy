using System.Windows;
using Roomy.ViewModels;
using Roomy.Services;
using Roomy.Core;

namespace Roomy;

public partial class App : Application
{
    SingleInstance? _instance;
    DesktopController? _desktop;
    NotificationService? _activationRelay;

    protected override async void OnStartup(StartupEventArgs e)
    {
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("ru-RU");
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("ru-RU")));
        base.OnStartup(e);
        var args = e.Args;
        var uninstall = args.Contains("--uninstall");
        var prepareUpdate = args.Contains("--prepare-update");
        var configureInstall = args.Contains("--configure-install");
        if (AppProfile.IsPortable && (uninstall || prepareUpdate || configureInstall ||
            args.Contains("--background") || args.Contains("-ToastActivated")))
        {
            Shutdown(2);
            return;
        }
        if (uninstall || prepareUpdate || configureInstall)
        {
            Shutdown(await RunInstallerCommandAsync(args, uninstall, configureInstall));
            return;
        }
        string? Arg(string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.Message, "Ошибка Roomy", MessageBoxButton.OK, MessageBoxImage.Warning);
            ex.Handled = true;
        };

        // Build-time helper: render the logo into a multi-size .ico (+ a big PNG).
        if (Arg("--export-icon") is { } iconPath)
        {
            IconExporter.Export(iconPath);
            Shutdown();
            return;
        }

        // Local-only tools can hook in here (not part of the public build).
        var handled = false;
        RunExtras(args, ref handled);
        if (handled) return;

        if (Arg("--export-store-art") is { } artDir)
        {
            IconExporter.ExportStoreArt(artDir);
            Shutdown();
            return;
        }

        // Diagnostic: run every scan step (read-only) and write what was found to a text file.
        if (Arg("--scan-report") is { } reportPath)
        {
            var ctx = new Core.ScanContext { IsAdmin = Core.Admin.IsElevated };
            var lines = new List<string> { $"Roomy scan report · admin={ctx.IsAdmin} · {DateTime.Now:u}" };
            foreach (var step in Core.Scanner.Steps)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var cat = step.Run(ctx);
                    lines.Add($"{step.Title,-16} {(cat is null ? "-" : cat.Blocked ? "нужны права администратора" : Core.Format.Bytes(cat.Size)),12}  ({sw.ElapsedMilliseconds} ms)");
                    if (cat != null)
                        foreach (var i in cat.Items.Take(8)) lines.Add($"    {i.Name,-40} {Core.Format.Bytes(i.Size),10}  {i.Detail}");
                }
                catch (Exception ex) { lines.Add($"{step.Title,-16} ERROR {ex.Message}"); }
            }
            File.WriteAllLines(reportPath, lines);
            Shutdown();
            return;
        }

        var shots = Arg("--shots");
        var record = Arg("--record");
        if (record != null) shots = null;
        var demo = shots != null || record != null || args.Contains("--demo");
        var automation = shots != null || record != null;
        var notificationLaunch = args.Contains("-ToastActivated");
        var background = args.Contains("--background");
        if (!automation)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _instance = new SingleInstance(Admin.IsElevated, demo,
                AppProfile.IsPortable ? AppProfile.DataDirectory : null);
            if (!_instance.IsPrimary)
            {
                if (notificationLaunch)
                {
                    _activationRelay = new NotificationService();
                    _activationRelay.Activated += command => Dispatcher.BeginInvoke(async () =>
                    {
                        await _instance.SendAsync(command);
                        Shutdown();
                    });
                    if (!_activationRelay.Initialize()) { Shutdown(); return; }
                    // COM activation should arrive promptly. Do not leave a hidden relay alive forever.
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    Shutdown();
                }
                else
                {
                    var sent = await _instance.SendAsync(background ? "background" : args.Contains("--scan") ? "scan" : "open");
                    if (!sent && !background) MessageBox.Show("Roomy уже запускается. Повторите попытку чуть позже.", "Roomy");
                    Shutdown();
                }
                return;
            }
        }
        var vm = new MainVM(demo);
        var window = new MainWindow(vm);

        if (shots != null || record != null)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000;
            window.Top = 0;
            window.Width = 1240;
            window.Height = record != null ? 780 : 800;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
        }

        MainWindow = window;
        if (!automation)
        {
            _desktop = new DesktopController(window, vm, Shutdown);
            _instance!.Listen(command => Dispatcher.BeginInvoke(() => _desktop.Handle(command)));
            if (background && !_desktop.Preferences.RunInBackground) { Shutdown(); return; }
            if (notificationLaunch) _desktop.RegisterNotificationActivation();
        }
        if (!background && !notificationLaunch) window.Show();

        if (notificationLaunch && !_desktop!.Preferences.RunInBackground)
        {
            // A snooze/disable action can cold-start Roomy without needing a window.
            _ = ExitUnusedActivationAsync();
        }

        if (shots != null) _ = window.RunScreenshotsAsync(shots);
        else if (record != null) _ = window.RecordDemoAsync(record);
        else if (args.Contains("--scan")) vm.ScanCommand.Execute(null);
    }

    static async Task<int> RunInstallerCommandAsync(string[] args, bool uninstall, bool configure)
    {
        try
        {
            if (configure)
            {
                var startup = args.Contains("--start-with-windows");
                var lowSpace = args.Contains("--low-space-check");
                var weekly = args.Contains("--weekly-check");
                var store = new PreferencesStore(PreferencesStore.DefaultPath);
                var current = store.Load();
                if (store.LoadError is not null) return 1;
                store.Save(current.WithInstallerOptions(startup, lowSpace, weekly));
                await StartupRegistration.SetEnabledAsync(startup);
                return 0;
            }

            foreach (var elevated in new[] { false, true })
            {
                using var instance = new SingleInstance(elevated, demo: false);
                if (instance.IsPrimary) continue;
                await instance.SendAsync("quit");
                if (!await instance.WaitForExitAsync(TimeSpan.FromSeconds(8))) return 2;
            }
            if (uninstall)
            {
                await StartupRegistration.SetEnabledAsync(false);
                Microsoft.Toolkit.Uwp.Notifications.ToastNotificationManagerCompat.Uninstall();
            }
            return 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine("Roomy setup: " + ex.Message);
            return 1;
        }
    }

    async Task ExitUnusedActivationAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        if (MainWindow?.IsVisible != true && _desktop?.Preferences.RunInBackground != true) Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _desktop?.Dispose();
        _activationRelay?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    partial void RunExtras(string[] args, ref bool handled);
}

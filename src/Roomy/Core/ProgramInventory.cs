using Microsoft.Win32;
using Windows.Management.Deployment;

namespace Roomy.Core;

public enum ProgramSource { Desktop, Msi, Store, Steam }
public enum ProgramFolderKind { Installation, Cache, Temporary, Data, Project }
public enum FolderSizeState { Measured, Partial, Unavailable, Shared, Missing }

public sealed record ProgramFolder(string Path, ProgramFolderKind Kind)
{
    public string? Evidence { get; init; }
    public bool Suggested { get; init; }
}
public sealed record InstalledProgram(string Id, string Name, string Publisher, string Version,
    ProgramSource Source, string? RemovalId, long? ReportedBytes, IReadOnlyList<ProgramFolder> Folders)
{
    public IReadOnlyList<ProgramRegistryItem> Registry { get; init; } = [];
    public bool IsResidue { get; init; }
}
public sealed record MeasuredFolder(ProgramFolder Folder, long Bytes, FolderSizeState State)
{
    public bool HasSize => !Folder.Suggested && State is (FolderSizeState.Measured or FolderSizeState.Partial);
}
public sealed record ProgramUsage(InstalledProgram Program, IReadOnlyList<MeasuredFolder> Folders)
{
    public long Bytes => Folders.Where(f => f.HasSize).Sum(f => f.Bytes);
    public bool HasSize => Folders.Any(f => f.HasSize);
}

public static class ProgramInventory
{
    static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    public static List<InstalledProgram> Read()
    {
        var games = Steam.Games(includeWithoutHistory: true)
            .Where(g => uint.TryParse(g.AppId, System.Globalization.NumberStyles.None, null, out var id) && id != 0);
        var apps = games.Select(g => new InstalledProgram("steam:" + g.AppId, g.Name, "Steam", "",
            ProgramSource.Steam, g.AppId, g.Size > 0 ? g.Size : null,
            [new(g.InstallPath, ProgramFolderKind.Installation)])).ToList();

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try { ReadRegistry(apps, hive, view); }
            catch (Exception ex) when (IsAccessError(ex)) { }
        }

        try
        {
            var manager = new PackageManager();
            foreach (var package in manager.FindPackagesForUserWithPackageTypes("", PackageTypes.Main))
            {
                try
                {
                    if (package.IsFramework || package.IsResourcePackage) continue;
                    var id = package.Id;
                    var userData = Env.L("Packages", id.FamilyName);
                    var folders = new List<ProgramFolder>
                    {
                        new(Path.Combine(userData, "LocalState"), ProgramFolderKind.Data),
                        new(Path.Combine(userData, "LocalCache"), ProgramFolderKind.Cache),
                        new(Path.Combine(userData, "TempState"), ProgramFolderKind.Temporary),
                        new(Path.Combine(userData, "RoamingState"), ProgramFolderKind.Data),
                        new(Path.Combine(userData, "Settings"), ProgramFolderKind.Data),
                    };
                    try { folders.Add(new(package.InstalledLocation.Path, ProgramFolderKind.Installation)); }
                    catch (Exception ex) when (IsAccessError(ex)) { }
                    var version = id.Version;
                    var name = package.DisplayName;
                    apps.Add(new("store:" + id.FamilyName, string.IsNullOrWhiteSpace(name) ? id.Name : name,
                        package.PublisherDisplayName, $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}",
                        ProgramSource.Store, id.FamilyName, null, folders));
                }
                catch (Exception ex) when (IsAccessError(ex)) { }
            }
        }
        catch (Exception ex) when (IsAccessError(ex)) { }
        return apps.DistinctBy(a => a.Id, Paths).ToList();
    }

    static void ReadRegistry(List<InstalledProgram> apps, RegistryHive hive, RegistryView view)
    {
        using var registry = RegistryKey.OpenBaseKey(hive, view);
        using var uninstall = registry.OpenSubKey(ProgramEvidence.UninstallKey);
        if (uninstall is null) return;
        foreach (var key in uninstall.GetSubKeyNames())
        {
            if (key.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase) &&
                apps.Any(a => a.Id.Equals("steam:" + key[10..], StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                using var entry = uninstall.OpenSubKey(key);
                if (entry?.GetValue("DisplayName") is not string name || string.IsNullOrWhiteSpace(name) ||
                    entry.GetValue("SystemComponent") is 1 || entry.GetValue("ParentKeyName") is string) continue;
                string Value(string field) => entry.GetValue(field) as string ?? "";
                var location = Value("InstallLocation").Trim().Trim('"');
                var publisher = Value("Publisher");
                var msi = entry.GetValue("WindowsInstaller") is 1 && Guid.TryParseExact(key, "B", out _);
                var folders = KnownFolders(name, publisher);
                if (NormalizeFolder(location) is { } path)
                    folders.Add(new(path, ProgramFolderKind.Installation) { Evidence = "Путь установки из реестра" });
                else if ((ProgramEvidence.InstallFolderFromExecutable(name, Value("DisplayIcon")) ??
                          ProgramEvidence.InstallFolderFromExecutable(name, Value("UninstallString"))) is { } inferred)
                    folders.Add(new(inferred, ProgramFolderKind.Installation) { Evidence = "Путь исполняемого файла из реестра" });
                var registeredAction = entry.GetValue("NoRemove") is not 1 && !string.IsNullOrWhiteSpace(Value("UninstallString"));
                apps.Add(new($"registry:{hive}:{key}", name.Trim(), publisher, Value("DisplayVersion"),
                    msi ? ProgramSource.Msi : ProgramSource.Desktop, registeredAction ? (msi ? key : "settings") : null,
                    entry.GetValue("EstimatedSize") is int kib && kib > 0 ? kib * 1024L : null, folders)
                {
                    Registry = [new(hive, view, ProgramEvidence.UninstallKey + "\\" + key, null, "Запись установки")],
                });
            }
            catch (Exception ex) when (IsAccessError(ex)) { }
        }
    }

    static List<ProgramFolder> KnownFolders(string name, string publisher)
    {
        var folders = new List<ProgramFolder>();
        if (name.StartsWith("Microsoft Visual Studio Code", StringComparison.OrdinalIgnoreCase) &&
            publisher.Equals("Microsoft Corporation", StringComparison.OrdinalIgnoreCase))
        {
            var profile = Env.R(name.Contains("Insiders", StringComparison.OrdinalIgnoreCase) ? "Code - Insiders" : "Code");
            folders.Add(new(profile, ProgramFolderKind.Data));
            foreach (var cache in new[] { "Cache", "CachedData", "Code Cache", "GPUCache" })
                folders.Add(new(Path.Combine(profile, cache), ProgramFolderKind.Cache));
        }
        if (name.Equals("Discord", StringComparison.OrdinalIgnoreCase) &&
            publisher.StartsWith("Discord", StringComparison.OrdinalIgnoreCase))
        {
            folders.Add(new(Env.R("discord"), ProgramFolderKind.Data));
            foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache" })
                folders.Add(new(Env.R("discord", cache), ProgramFolderKind.Cache));
        }
        if (name.Equals("Docker Desktop", StringComparison.OrdinalIgnoreCase) &&
            publisher.StartsWith("Docker", StringComparison.OrdinalIgnoreCase))
        {
            folders.Add(new(Env.L("Docker"), ProgramFolderKind.Data));
            folders.Add(new(Env.R("Docker"), ProgramFolderKind.Data));
        }
        var chromium = (name, publisher) switch
        {
            ("Google Chrome", "Google LLC") => Env.L("Google", "Chrome", "User Data"),
            ("Microsoft Edge", "Microsoft Corporation") => Env.L("Microsoft", "Edge", "User Data"),
            ("Brave", "Brave Software Inc") or ("Brave", "Brave Software Inc.") => Env.L("BraveSoftware", "Brave-Browser", "User Data"),
            _ => null,
        };
        if (chromium is not null)
        {
            folders.Add(new(chromium, ProgramFolderKind.Data));
            folders.AddRange(Scanner.ChromiumCaches(chromium).Select(path => new ProgramFolder(path, ProgramFolderKind.Cache)));
        }
        return folders;
    }

    public static string? NormalizeFolder(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Length < 3 || path[1] != ':' || path.IndexOf(':', 2) >= 0)
            return null;
        try
        {
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (normalized.Length <= 3) return null;
            string[] broad = [Env.Windows, Env.ProgramFiles, Env.ProgramFilesX86, Env.ProgramData, Env.UserProfile,
                Env.Local, Env.Roaming, Env.Temp, Path.GetDirectoryName(Env.Local) ?? ""];
            return broad.Any(p => Paths.Equals(Path.TrimEndingDirectorySeparator(p), normalized)) ? null : normalized;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return null; }
    }

    public static bool ContainsFolder(string parent, string child) =>
        child.Length > parent.Length && child.StartsWith(parent, StringComparison.OrdinalIgnoreCase) &&
        child[parent.Length] == Path.DirectorySeparatorChar;

    public static List<ProgramUsage> Measure(IEnumerable<InstalledProgram> source,
        IReadOnlyList<ProjectLink> links, CancellationToken ct)
    {
        var programs = source.Select(app => (App: app, Folders: app.Folders
            .Concat(links.Where(l => !app.IsResidue && Paths.Equals(l.ProgramId, app.Id))
                .Select(l => new ProgramFolder(l.Path, ProgramFolderKind.Project) { Evidence = "Ручная привязка" }))
            .Select(f => (Path: NormalizeFolder(f.Path), Folder: f)).Where(f => f.Path != null)
            .Select(f => f.Folder with { Path = f.Path! }).OrderBy(f => f.Suggested)
            .DistinctBy(f => f.Path, Paths).ToArray())).ToArray();
        var owners = programs.SelectMany(app => app.Folders.Where(f => !f.Suggested).Select(f => f.Path))
            .GroupBy(p => p, Paths).ToDictionary(g => g.Key, g => g.Count(), Paths);
        var cuts = owners.Keys.ToHashSet(Paths);
        var sharedSizes = new Dictionary<string, long>(Paths);
        MeasuredFolder MeasureOwnedFolder(ProgramFolder folder)
        {
            if (folder.Suggested) return MeasureFolder(folder, cuts, ct);
            if (owners[folder.Path] == 1) return MeasureFolder(folder, cuts, ct);
            if (!sharedSizes.TryGetValue(folder.Path, out var size))
            {
                var shared = MeasureFolder(folder, cuts, ct);
                sharedSizes[folder.Path] = size = shared.HasSize ? shared.Bytes : 0;
            }
            return new(folder, size, FolderSizeState.Shared);
        }
        return programs.Select(app =>
        {
            ct.ThrowIfCancellationRequested();
            return new ProgramUsage(app.App, app.Folders.Select(MeasureOwnedFolder)
                .Where(f => f.State != FolderSizeState.Missing || !app.App.IsResidue &&
                    f.Folder.Kind is (ProgramFolderKind.Installation or ProgramFolderKind.Project))
                .ToArray());
        }).ToList();
    }

    public static MeasuredFolder MeasureFolder(ProgramFolder folder, IReadOnlySet<string> cuts, CancellationToken ct)
    {
        var bytes = 0L;
        var partial = false;
        try
        {
            if (!HasOrdinaryAncestors(folder.Path)) return new(folder, 0, FolderSizeState.Unavailable);
            var pending = new Stack<string>();
            pending.Push(folder.Path);
            while (pending.TryPop(out var path))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                        if (entry is FileInfo file) bytes += file.Length;
                        else if (!cuts.Contains(entry.FullName)) pending.Push(entry.FullName);
                    }
                }
                catch (Exception ex) when (IsAccessError(ex))
                {
                    partial = true;
                    if (bytes == 0 && Paths.Equals(path, folder.Path))
                        return new(folder, 0, FolderSizeState.Unavailable);
                }
            }
            return new(folder, bytes, partial ? FolderSizeState.Partial : FolderSizeState.Measured);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(folder, 0, FolderSizeState.Missing);
        }
        catch (Exception ex) when (IsAccessError(ex)) { return new(folder, 0, FolderSizeState.Unavailable); }
    }

    internal static bool IsAccessError(Exception ex) => ex is IOException or UnauthorizedAccessException
        or System.Security.SecurityException or System.Runtime.InteropServices.COMException;

    static bool HasOrdinaryAncestors(string folder)
    {
        for (var path = folder; path != null; path = Path.GetDirectoryName(path))
        {
            var attributes = File.GetAttributes(path);
            if (!attributes.HasFlag(FileAttributes.Directory) || attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
        }
        return true;
    }

    public static bool CanTraverseFolder(string path)
    {
        try { return HasOrdinaryAncestors(path); }
        catch (Exception ex) when (IsAccessError(ex)) { return false; }
    }
}

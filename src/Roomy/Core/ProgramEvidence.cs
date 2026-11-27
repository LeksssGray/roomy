using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Roomy.Core;

public sealed record ProgramRegistryItem(RegistryHive Hive, RegistryView View, string Key, string? Value, string Source)
{
    public string? ExpectedExecutable { get; init; }
    public string DisplayPath => $"{(Hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM")}\\{Key}" +
        (Value is null ? "" : $" · {Value}") + (View == RegistryView.Registry32 ? " (32 бит)" : "");

    public bool IsPresent()
    {
        try
        {
            using var registry = RegistryKey.OpenBaseKey(Hive, View);
            using var key = registry.OpenSubKey(Key);
            if (key is null) return false;
            if (ExpectedExecutable is { } expected)
                return key.GetValue(Value ?? "") is string command &&
                    string.Equals(expected, ProgramEvidence.ExecutablePath(command), StringComparison.OrdinalIgnoreCase);
            return Value is null || key.GetValueNames().Contains(Value, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ProgramInventory.IsAccessError(ex)) { return false; }
    }
}

public static class ProgramEvidence
{
    internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;
    const string CurrentVersion = @"Software\Microsoft\Windows\CurrentVersion\";

    public static List<InstalledProgram> Enrich(IReadOnlyList<InstalledProgram> programs, CancellationToken ct)
    {
        var roots = programs.SelectMany(p => p.Folders.Where(f => f.Kind == ProgramFolderKind.Installation)
                .Select(f => (Path: ProgramInventory.NormalizeFolder(f.Path), p.Id)))
            .Where(p => p.Path is not null)
            .ToLookup(p => p.Path!, p => p.Id, Paths);
        var registry = new Dictionary<string, List<ProgramRegistryItem>>(Paths);
        foreach (var (item, target) in ReadRegistry(ct))
        {
            var path = ExecutablePath(target);
            if (path is null) continue;
            for (var folder = Path.GetDirectoryName(path); folder is not null; folder = Path.GetDirectoryName(folder))
            {
                var owners = roots[folder].Distinct(Paths).Take(2).ToArray();
                if (owners.Length == 0) continue;
                if (owners.Length == 1)
                {
                    if (!registry.TryGetValue(owners[0], out var items)) registry.Add(owners[0], items = []);
                    items.Add(item with { ExpectedExecutable = path });
                }
                break;
            }
        }
        return programs.Select(p =>
        {
            ct.ThrowIfCancellationRequested();
            return p with
            {
                Folders = [..p.Folders, ..DiscoverProjects(p, ct), ..NamedFolders(p)],
                Registry = p.Registry.Concat(registry.GetValueOrDefault(p.Id) ?? [])
                    .DistinctBy(r => r.DisplayPath, Paths).ToArray(),
            };
        }).ToList();
    }

    static IEnumerable<(ProgramRegistryItem Item, string Target)> ReadRegistry(CancellationToken ct)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            ct.ThrowIfCancellationRequested();
            foreach (var entry in ReadSection(hive, view, CurrentVersion + "App Paths", "Путь приложения", "")) yield return entry;
            foreach (var name in new[] { "Run", "RunOnce" })
                foreach (var entry in ReadSection(hive, view, CurrentVersion + name, "Автозагрузка", null)) yield return entry;
        }
        foreach (var entry in ReadSection(RegistryHive.LocalMachine, RegistryView.Registry64,
                     @"SYSTEM\CurrentControlSet\Services", "Служба", "ImagePath")) yield return entry;
    }

    static List<(ProgramRegistryItem, string)> ReadSection(RegistryHive hive, RegistryView view, string path,
        string source, string? childValue)
    {
        var items = new List<(ProgramRegistryItem, string)>();
        try
        {
            using var hiveKey = RegistryKey.OpenBaseKey(hive, view);
            using var section = hiveKey.OpenSubKey(path);
            if (section is null) return items;
            if (childValue is null)
            {
                foreach (var name in section.GetValueNames())
                    if (section.GetValue(name) is string command) items.Add((new(hive, view, path, name, source), command));
            }
            else
            {
                foreach (var name in section.GetSubKeyNames())
                {
                    try
                    {
                        using var child = section.OpenSubKey(name);
                        if (child?.GetValue(childValue) is string command)
                            items.Add((new(hive, view, path + "\\" + name, childValue.Length == 0 ? null : childValue, source), command));
                    }
                    catch (Exception ex) when (ProgramInventory.IsAccessError(ex)) { }
                }
            }
        }
        catch (Exception ex) when (ProgramInventory.IsAccessError(ex)) { }
        return items;
    }

    public static string? ExecutablePath(string raw)
    {
        var input = Environment.ExpandEnvironmentVariables(raw.Trim());
        if (input.StartsWith(@"\??\", StringComparison.Ordinal)) input = input[4..];
        if (input.Length < 5) return null;
        int end;
        int start;
        if (input[0] == '"')
        {
            start = 1;
            end = input.IndexOf('"', 1);
        }
        else
        {
            start = 0;
            var extension = input.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            end = extension < 0 ? -1 : extension + 4;
            if (end > 0 && end < input.Length && !char.IsWhiteSpace(input[end]) && input[end] != ',') return null;
        }
        if (end <= start) return null;
        var value = input[start..end];
        if (!value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || value.Length < 4 ||
            value[1] != ':' || value.IndexOf(':', 2) >= 0 || !Path.IsPathFullyQualified(value)) return null;
        try { return Path.GetFullPath(value); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return null; }
    }

    public static string? InstallFolderFromExecutable(string displayName, string command)
    {
        var exe = ExecutablePath(command);
        if (exe is null || !File.Exists(exe)) return null;
        var name = Token(displayName);
        var exeName = Token(Path.GetFileNameWithoutExtension(exe));
        var directory = Path.GetDirectoryName(exe);
        var leaf = Token(Path.GetFileName(directory) ?? "");
        if (directory is null || leaf.Length < 4 || leaf is "system32" or "syswow64" or "installer" or "temp" or "application" ||
            !(name.Contains(leaf, StringComparison.Ordinal) || exeName.Length >= 4 && name.Contains(exeName, StringComparison.Ordinal)))
            return null;
        return ProgramInventory.NormalizeFolder(directory);
    }

    static string Token(string name) => string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    static IEnumerable<ProgramFolder> NamedFolders(InstalledProgram app)
    {
        if (app.Source is ProgramSource.Store or ProgramSource.Steam) yield break;
        var name = app.Name.Trim();
        if (name.Length < 5 || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) yield break;
        foreach (var (root, kind) in new[] { (Env.Local, ProgramFolderKind.Data), (Env.Roaming, ProgramFolderKind.Data),
                     (Env.ProgramData, ProgramFolderKind.Data), (Env.Temp, ProgramFolderKind.Temporary) })
        {
            var path = Path.Combine(root, name);
            if (Directory.Exists(path) && ProgramInventory.CanTraverseFolder(path))
                yield return new(path, kind) { Evidence = "Совпадение по имени", Suggested = true };
        }
    }

    static IEnumerable<ProgramFolder> DiscoverProjects(InstalledProgram app, CancellationToken ct)
    {
        if (app.Publisher.Equals("Microsoft Corporation", StringComparison.OrdinalIgnoreCase) &&
            app.Name.StartsWith("Microsoft Visual Studio Code", StringComparison.OrdinalIgnoreCase))
        {
            var profile = app.Name.Contains("Insiders", StringComparison.OrdinalIgnoreCase) ? "Code - Insiders" : "Code";
            return ReadCodeProjects(Env.R(profile, "User", "workspaceStorage"), ct);
        }
        if (!app.Publisher.StartsWith("JetBrains", StringComparison.OrdinalIgnoreCase)) return [];
        var name = app.Name.StartsWith("JetBrains ", StringComparison.OrdinalIgnoreCase) ? app.Name[10..] : app.Name;
        var product = name.StartsWith("IntelliJ IDEA Community", StringComparison.OrdinalIgnoreCase) ? "IdeaIC" :
            name.StartsWith("PyCharm Community", StringComparison.OrdinalIgnoreCase) ? "PyCharmCE" :
            name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return product is null ? [] : ReadJetBrainsProjects(Env.R("JetBrains"), product, ct);
    }

    public static IEnumerable<ProgramFolder> ReadCodeProjects(string storage, CancellationToken ct)
    {
        var paths = new HashSet<string>(Paths);
        foreach (var directory in Subdirectories(storage).Take(512))
        {
            ct.ThrowIfCancellationRequested();
            using var record = ReadJson(Path.Combine(directory, "workspace.json"));
            if (record?.RootElement.ValueKind != JsonValueKind.Object) continue;
            var root = record.RootElement;
            if (root.TryGetProperty("folder", out var folder) && LocalUri(folder) is { } single)
                AddProject(paths, single);
            if (root.TryGetProperty("workspace", out var workspace) && LocalUri(workspace) is { } config &&
                config.EndsWith(".code-workspace", StringComparison.OrdinalIgnoreCase))
            {
                using var multi = ReadJson(config);
                if (multi?.RootElement.ValueKind != JsonValueKind.Object ||
                    !multi.RootElement.TryGetProperty("folders", out var list) || list.ValueKind != JsonValueKind.Array) continue;
                foreach (var item in list.EnumerateArray().Take(128))
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (item.TryGetProperty("uri", out var uri) && LocalUri(uri) is { } absolute) AddProject(paths, absolute);
                    if (item.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String &&
                        path.GetString() is { } value) AddProject(paths, Path.Combine(Path.GetDirectoryName(config)!, value));
                }
            }
            if (paths.Count >= 64) break;
        }
        return paths.Take(64).Select(p => new ProgramFolder(p, ProgramFolderKind.Project) { Evidence = "Рабочая область VS Code" });
    }

    static IEnumerable<ProgramFolder> ReadJetBrainsProjects(string storage, string product, CancellationToken ct)
    {
        var paths = new HashSet<string>(Paths);
        foreach (var directory in Subdirectories(storage)
            .Where(p => Path.GetFileName(p).StartsWith(product, StringComparison.OrdinalIgnoreCase)).Take(16))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var stream = OpenSmall(Path.Combine(directory, "options", "recentProjects.xml"));
                if (stream is null) continue;
                foreach (var node in XDocument.Load(stream).Descendants("component")
                    .Where(e => (string?)e.Attribute("name") == "RecentProjectsManager")
                    .Elements().Where(e => e.Name.LocalName == "additionalInfo" ||
                        e.Name.LocalName == "option" && (string?)e.Attribute("name") == "additionalInfo")
                    .Descendants("entry").Take(128))
                    if ((string?)node.Attribute("key") is { } key)
                        AddProject(paths, key.Replace("$USER_HOME$", Env.UserProfile, StringComparison.Ordinal));
            }
            catch (Exception ex) when (ProgramInventory.IsAccessError(ex) || ex is System.Xml.XmlException) { }
        }
        return paths.Take(64).Select(p => new ProgramFolder(p, ProgramFolderKind.Project) { Evidence = "Недавние проекты JetBrains" });
    }

    static void AddProject(HashSet<string> paths, string path)
    {
        if (ProgramInventory.NormalizeFolder(path) is { } normalized && ProgramInventory.CanTraverseFolder(normalized))
            paths.Add(normalized);
    }

    static string? LocalUri(JsonElement item) => item.ValueKind == JsonValueKind.String &&
        Uri.TryCreate(item.GetString(), UriKind.Absolute, out var uri) && uri.IsFile && !uri.IsUnc ? uri.LocalPath : null;

    static string[] Subdirectories(string path)
    {
        if (!ProgramInventory.CanTraverseFolder(path)) return [];
        try
        {
            return Directory.EnumerateDirectories(path, "*", new EnumerationOptions
            {
                IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
            }).Take(2048).ToArray();
        }
        catch (Exception ex) when (ProgramInventory.IsAccessError(ex)) { return []; }
    }

    static FileStream? OpenSmall(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (folder is null || !ProgramInventory.CanTraverseFolder(folder)) return null;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 1024 * 1024 || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
        return info.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }

    static JsonDocument? ReadJson(string path)
    {
        try
        {
            using var stream = OpenSmall(path);
            return stream is null ? null : JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (Exception ex) when (ProgramInventory.IsAccessError(ex) || ex is JsonException) { return null; }
    }
}

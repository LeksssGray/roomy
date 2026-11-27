using System.Text.Json;

namespace Roomy.Core;

public sealed class ProgramHistory(string path)
{
    public static string DefaultPath => Path.Combine(AppProfile.DataDirectory, "program-footprints.json");

    public (List<InstalledProgram> Programs, bool HistoryError) Merge(
        List<InstalledProgram> installed, CancellationToken ct)
    {
        List<InstalledProgram> previous;
        try
        {
            previous = File.Exists(path) ? JsonSerializer.Deserialize<List<InstalledProgram>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception ex) when (ProgramInventory.IsAccessError(ex) || ex is JsonException)
        {
            return (installed, true);
        }

        var active = installed.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var owned = installed.SelectMany(p => p.Folders).Where(f => !f.Suggested && f.Kind != ProgramFolderKind.Project)
            .Select(f => ProgramInventory.NormalizeFolder(f.Path)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var residues = new List<InstalledProgram>();
        foreach (var program in previous)
        {
            ct.ThrowIfCancellationRequested();
            if (program is null || active.Contains(program.Id) || program.Folders is null || program.Registry is null) continue;
            var folders = program.Folders
                .Where(f => f is not null && f.Kind != ProgramFolderKind.Project &&
                    ProgramInventory.NormalizeFolder(f.Path) is { } folder && Directory.Exists(folder) &&
                    !owned.Any(p => string.Equals(p, folder, StringComparison.OrdinalIgnoreCase) ||
                        ProgramInventory.ContainsFolder(p, folder))).ToArray();
            var registry = program.Registry.Where(r => r is not null && r.IsPresent()).ToArray();
            if (folders.Length > 0 || registry.Length > 0)
                residues.Add(program with { Folders = folders, Registry = registry, RemovalId = null, ReportedBytes = null, IsResidue = true });
        }

        var next = installed.Concat(residues).Select(p => p with
        {
            Folders = p.Folders.Where(f => f.Kind != ProgramFolderKind.Project).ToArray(),
        }).ToArray();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, JsonSerializer.Serialize(next));
                File.Move(temp, path, overwrite: true);
            }
            finally { File.Delete(temp); }
        }
        catch (Exception ex) when (ProgramInventory.IsAccessError(ex)) { return ([..installed, ..residues], true); }
        return ([..installed, ..residues], false);
    }
}

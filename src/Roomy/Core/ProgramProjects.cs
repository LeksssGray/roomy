using System.Text.Json;

namespace Roomy.Core;

public sealed record ProjectLink(string ProgramId, string Path);

public sealed class ProgramProjects(string path)
{
    public static string DefaultPath => Path.Combine(Env.Local, "Roomy", "program-projects.json");

    public List<ProjectLink> Load() => File.Exists(path)
        ? (JsonSerializer.Deserialize<List<ProjectLink>>(File.ReadAllText(path)) ?? [])
            .Where(l => l is not null && !string.IsNullOrWhiteSpace(l.ProgramId) && l.Path is not null)
            .ToList()
        : [];

    public void Save(IReadOnlyList<ProjectLink> links)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(links));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}

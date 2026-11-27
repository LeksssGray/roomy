namespace Roomy.Core;

public static class AppProfile
{
#if ROOMY_PORTABLE
    public static bool IsPortable { get; } = true;
#else
    public static bool IsPortable { get; } = false;
#endif

    public static string DataDirectory => IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "Roomy-data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roomy");
}

using System.Diagnostics;

namespace Roomy.Core;

public static class DiskTools
{
    public static ProcessStartInfo? VolumeCheck(string? root)
    {
        if (root is not { Length: 3 } || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] != '\\')
            return null;
        var executable = Path.Combine(Environment.SystemDirectory, "chkdsk.exe").Replace("'", "''");
        return PowerShell($"& '{executable}' '{char.ToUpperInvariant(root[0])}:'", elevated: true);
    }

    public static ProcessStartInfo StorageHealth() => PowerShell(
        "Get-PhysicalDisk | Select-Object FriendlyName, MediaType, HealthStatus, OperationalStatus | Format-Table -AutoSize");

    public static ProcessStartInfo Optimize() => WindowsTool("dfrgui.exe");
    public static ProcessStartInfo Memory() => WindowsTool("MdSched.exe");

    static ProcessStartInfo WindowsTool(string file) =>
        new(Path.Combine(Environment.SystemDirectory, file)) { UseShellExecute = true };

    static ProcessStartInfo PowerShell(string command, bool elevated = false)
    {
        var info = WindowsTool(Path.Combine("WindowsPowerShell", "v1.0", "powershell.exe"));
        if (elevated) info.Verb = "runas";
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NoExit");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(command);
        return info;
    }
}

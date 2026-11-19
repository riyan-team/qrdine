using QrDine.Application.Pc;

namespace QrDine.Infrastructure.Pc;

/// <summary>
/// This computer's code for its licence. It comes from Windows' own machine id (set when Windows is installed),
/// hashed, so it stays the same across restarts and app updates. A fresh Windows install gives a new code.
/// </summary>
public static class MachineCode
{
    public static string ForThisComputer() => MachineCodeText.FromId(RawId());

    private static string RawId()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
                using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                if (key?.GetValue("MachineGuid") is string g && g.Length > 0) return g;
            }
            foreach (var f in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
                if (File.Exists(f) && File.ReadAllText(f).Trim() is { Length: > 0 } id) return id;
        }
        catch (Exception) { /* fall back below */ }
        return Environment.MachineName;
    }
}

using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.FileProviders;

namespace QrDine.Api.Pc;

/// <summary>Where the PC edition keeps everything: one folder, so a backup is a copy of the folder.</summary>
public sealed record PcPaths(string DataDir, string DatabaseFile, string BackupDir, string LicenceFile, string ClockFile, string SettingsFile, int Port);

/// <summary>
/// The restaurant PC edition: the same app as a single DineIn.exe on the counter PC, with its own SQLite database,
/// no internet needed. Turned on when the exe was built with PcEdition=true, or with --pc / DINEIN_EDITION=pc.
///
/// Its folder (next to the exe unless --data or DINEIN_DATA says otherwise), "DineIn-data":
///   dinein.db             the database (orders, bills, menu, staff, everything)
///   backups\              a copy a few minutes after start and every night (the last 14)
///   licence.key           the licence, once entered
///   dinein-settings.json  port, a second backup folder (USB drive), open the browser at start
///   secret.key            the sign-in signing secret, made on first start (keep it private)
/// </summary>
public static class PcEdition
{
#if PC_EDITION
    public const bool Built = true;
#else
    public const bool Built = false;
#endif

    public static bool IsOn(string[] args) =>
        Built || args.Contains("--pc", StringComparer.OrdinalIgnoreCase)
        || string.Equals(Environment.GetEnvironmentVariable("DINEIN_EDITION"), "pc", StringComparison.OrdinalIgnoreCase);

    public static WebApplicationBuilder CreateBuilder(string[] args, out PcPaths paths)
    {
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var dataDir = Path.GetFullPath(ArgValue(args, "--data") ?? Environment.GetEnvironmentVariable("DINEIN_DATA") ?? Path.Combine(exeDir, "DineIn-data"));
        Directory.CreateDirectory(dataDir);
        var settingsFile = Path.Combine(dataDir, "dinein-settings.json");
        if (!File.Exists(settingsFile))
            File.WriteAllText(settingsFile, JsonSerializer.Serialize(new
            {
                Pc = new { Port = 5100, BackupFolder = "", OpenBrowser = true },
            }, new JsonSerializerOptions { WriteIndented = true }));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args.Where(a => !a.Equals("--pc", StringComparison.OrdinalIgnoreCase)).ToArray(),
            // The exe unpacks the app's own files (the web pages) next to itself on first start.
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production,
        });

        var db = Path.Combine(dataDir, "dinein.db");
        var defaults = new Dictionary<string, string?>
        {
            ["Edition"] = "Pc",
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Default"] = $"Data Source={db};Default Timeout=30",
            ["Jwt:Secret"] = Secret(Path.Combine(dataDir, "secret.key")),
            // Each branch picks Practice, Test or Live itself (Practice until the owner changes it).
            ["Fiscal:Simulate"] = "false",
            // FBR's own IMS program on this PC ("On this computer") works here.
            ["Fiscal:AllowLocal"] = "true",
            // The licence decides whether this PC works, not a monthly plan date.
            ["Subscription:Enforce"] = "false",
            ["Sync:Enabled"] = "false",
        };
        // Our defaults win over appsettings.json; the settings file, environment and command line win over ours.
        // (Right after the last appsettings file: the prefixed DOTNET_/ASPNETCORE_ variables come before those.)
        var sources = builder.Configuration.Sources;
        var at = -1;
        for (var i = 0; i < sources.Count; i++)
            if (sources[i] is JsonConfigurationSource j && (j.Path ?? "").StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)) at = i + 1;
        if (at < 0)
        {
            at = sources.Count;
            for (var i = 0; i < sources.Count; i++)
                if (sources[i] is EnvironmentVariablesConfigurationSource e && string.IsNullOrEmpty(e.Prefix)) { at = i; break; }
        }
        sources.Insert(at, new MemoryConfigurationSource { InitialData = defaults });
        sources.Insert(at + 1, new JsonConfigurationSource
        {
            Path = Path.GetFileName(settingsFile), Optional = true, ReloadOnChange = false,
            FileProvider = new PhysicalFileProvider(dataDir),
        });

        var port = builder.Configuration.GetValue("Pc:Port", 5100);
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        paths = new PcPaths(dataDir, db, Path.Combine(dataDir, "backups"), Path.Combine(dataDir, "licence.key"),
            Path.Combine(dataDir, "clock.txt"), settingsFile, port);
        return builder;
    }

    /// <summary>One copy per data folder. A second start just opens the browser on the one already running.</summary>
    public static Mutex? TakeSingleInstance(PcPaths paths)
    {
        var name = "Local\\DineInPc-" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(paths.DataDir.ToLowerInvariant())))[..16];
        var m = new Mutex(true, name, out var mine);
        if (mine) return m;
        m.Dispose();
        return null;
    }

    /// <summary>What the counter PC's window shows once the app is up.</summary>
    public static void Started(PcPaths paths, IConfiguration config)
    {
        var lan = LanAddresses().Select(a => $"http://{a}:{paths.Port}").ToList();
        Console.WriteLine();
        Console.WriteLine("  Dine In is running. Keep this window open (closing it stops Dine In).");
        Console.WriteLine($"  On this PC:            http://localhost:{paths.Port}");
        foreach (var u in lan) Console.WriteLine($"  On the restaurant WiFi: {u}   (phones, tablets, QR codes)");
        Console.WriteLine($"  Data folder:           {paths.DataDir}");
        Console.WriteLine();
        if (config.GetValue("Pc:OpenBrowser", true) && Environment.UserInteractive) OpenBrowser($"http://localhost:{paths.Port}");
    }

    public static void OpenBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { /* no browser: the address is on screen */ }
    }

    /// <summary>The PC's addresses on the local network (WiFi or cable), for phones and QR codes.</summary>
    public static IEnumerable<string> LanAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254."))
            .Select(a => a.Address.ToString()).Distinct();

    private static string Secret(string file)
    {
        if (File.Exists(file) && File.ReadAllText(file).Trim() is { Length: >= 32 } s) return s;
        var made = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        File.WriteAllText(file, made);
        return made;
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

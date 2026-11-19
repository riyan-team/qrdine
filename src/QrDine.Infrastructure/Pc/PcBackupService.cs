using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace QrDine.Infrastructure.Pc;

/// <summary>Where the PC edition keeps its database and its copies.</summary>
public sealed record PcBackupOptions(string DatabaseFile, string BackupFolder, string? ExtraFolder, int Keep = 14, TimeSpan? FirstAfter = null);

/// <summary>
/// The PC edition's own backups: a full copy of the database a few minutes after start and then every night,
/// in the data folder's "backups" (the last 14 kept), and in a second folder too when one is set (a USB drive,
/// a second disk). The copy is made with SQLite's online backup, so orders keep working while it runs.
/// </summary>
public sealed class PcBackupService(PcBackupOptions opt, ILogger<PcBackupService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(opt.FirstAfter ?? TimeSpan.FromMinutes(3), stop); } catch (OperationCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            try { BackupNow(); }
            catch (Exception ex) { log.LogError(ex, "Backup failed"); }
            try { await Task.Delay(TimeSpan.FromHours(24), stop); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Copies the database now. Returns the copies made.</summary>
    public IReadOnlyList<string> BackupNow()
    {
        var made = new List<string>();
        var name = $"dinein-{DateTime.Now:yyyy-MM-dd-HHmm}.db";
        foreach (var folder in new[] { opt.BackupFolder, opt.ExtraFolder }.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!))
        {
            try
            {
                Directory.CreateDirectory(folder);
                var target = Path.Combine(folder, name);
                using (var src = new SqliteConnection($"Data Source={opt.DatabaseFile};Mode=ReadOnly;Pooling=False"))
                using (var dst = new SqliteConnection($"Data Source={target};Pooling=False"))
                {
                    src.Open(); dst.Open();
                    src.BackupDatabase(dst);
                }
                made.Add(target);
                foreach (var old in Directory.GetFiles(folder, "dinein-*.db").OrderByDescending(f => f, StringComparer.Ordinal).Skip(opt.Keep))
                    File.Delete(old);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Backup to {Folder} failed", folder);
            }
        }
        if (made.Count > 0) log.LogInformation("Backup made: {Files}", string.Join(", ", made));
        return made;
    }
}

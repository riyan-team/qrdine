namespace QrDine.Application.Common;

/// <summary>
/// The <c>Sync</c> settings. Off by default: the sync API answers 404 and no change-list lines are
/// written. Rows still get their SyncId and LastChangedAt either way, so the data is ready when a
/// restaurant's PC is paired.
/// <list type="bullet">
/// <item><c>Sync:Enabled</c> (false): switch the sync API and the change list on.</item>
/// <item><c>Sync:SettleSeconds</c> (2): a pull leaves out changes younger than this, so a save still
/// committing can't be skipped by a device that has already read past its line.</item>
/// </list>
/// </summary>
public sealed record SyncSettings(bool Enabled, int SettleSeconds = 2)
{
    public static readonly SyncSettings Off = new(false);
}

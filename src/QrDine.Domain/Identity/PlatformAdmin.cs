using QrDine.Domain.Common;

namespace QrDine.Domain.Identity;

/// <summary>
/// A platform operator (the SaaS owner). NOT tied to any restaurant — they manage
/// all tenants, plans and suspensions. Kept separate from StaffMember on purpose.
/// </summary>
public class PlatformAdmin : Entity
{
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

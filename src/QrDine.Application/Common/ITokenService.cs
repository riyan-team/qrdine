using QrDine.Domain.Identity;

namespace QrDine.Application.Common;

/// <summary>
/// Issues signed tokens. Staff tokens carry a role; guest tokens are scoped to a
/// single table so a customer's phone can only order for the table it scanned.
/// </summary>
public interface ITokenService
{
    string CreateStaffToken(StaffMember staff, string slug, IEnumerable<string> permissions);
    string CreateGuestToken(int restaurantId, string slug, string tableCode);
    string CreatePlatformToken(PlatformAdmin admin);
}

namespace QrDine.Infrastructure.Identity;

/// <summary>Bound from the "Jwt" config section.</summary>
public class JwtOptions
{
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "qrdine";
    public string Audience { get; set; } = "qrdine";
    public int StaffHours { get; set; } = 12;
    public int GuestHours { get; set; } = 6;
}

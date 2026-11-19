using System.Security.Cryptography;
using System.Text;
using QrDine.Application.Pc;

// The seller's licence tool for the Dine In PC edition. Runs on the seller's own computer only.
//
//   dinein-licence keygen  --out <folder>
//       Makes the seller's key pair once. The private key file stays in that folder: keep it safe and backed up
//       (whoever has it can make licences; without it no new licences can be made for this app's builds).
//       Prints the public key, which goes into PcLicence.SellerPublicKey.
//
//   dinein-licence issue   --key <private-key.pem> --restaurant "Name" --machine ABCD-EFGH-JKLM-NPQR
//                          [--days 365 | --until 2027-11-30 | --lifetime] [--features all | ordering,kitchen,...]
//       Prints a licence key for one PC. The machine code is shown on that PC's activation screen.
//
//   dinein-licence show    <licence key>
//       Shows what a key says (without checking it).
var a = args.ToList();
string? Opt(string name)
{
    var i = a.FindIndex(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < a.Count ? a[i + 1] : null;
}
bool Has(string name) => a.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
int Fail(string msg) { Console.Error.WriteLine(msg); return 1; }

switch (a.FirstOrDefault()?.ToLowerInvariant())
{
    case "keygen":
    {
        var dir = Opt("--out") ?? throw new ArgumentException("--out <folder> is needed");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "dinein-licence-private-key.pem");
        if (File.Exists(file)) return Fail($"{file} already exists. Keep using it; making a new one would stop every licence already sold from matching new builds.");
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(file, ec.ExportPkcs8PrivateKeyPem());
        var pub = Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo());
        File.WriteAllText(Path.Combine(dir, "dinein-licence-public-key.txt"), pub);
        Console.WriteLine($"Private key: {file} (keep it safe, never share it, back it up)");
        Console.WriteLine($"Public key (for PcLicence.SellerPublicKey): {pub}");
        return 0;
    }
    case "issue":
    {
        var keyFile = Opt("--key") ?? throw new ArgumentException("--key <private-key.pem> is needed");
        var restaurant = (Opt("--restaurant") ?? "").Trim();
        var machine = MachineCodeText.Normalize(Opt("--machine"));
        if (restaurant.Length == 0) return Fail("--restaurant \"Name\" is needed");
        if (machine.Length != 16) return Fail("--machine needs the 16-letter code from the PC's activation screen, like ABCD-EFGH-JKLM-NPQR");
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5));   // Pakistan's date
        DateOnly? until = Has("--lifetime") ? null
            : Opt("--until") is string u ? DateOnly.Parse(u)
            : today.AddDays(int.Parse(Opt("--days") ?? "365"));
        var features = (Opt("--features") ?? "all").Equals("all", StringComparison.OrdinalIgnoreCase)
            ? LicenceVerifier.AllFeatures
            : (Opt("--features") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(f => f.ToLowerInvariant()).ToArray();
        var unknown = features.Except(LicenceVerifier.AllFeatures).ToList();
        if (unknown.Count > 0) return Fail($"Unknown feature(s): {string.Join(", ", unknown)}. Known: {string.Join(", ", LicenceVerifier.AllFeatures)}");
        using var ec = ECDsa.Create();
        ec.ImportFromPem(File.ReadAllText(keyFile));
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        var key = LicenceIssuer.Issue(ec, new LicenceInfo(id, restaurant, machine, today, until, features));
        Console.WriteLine($"Licence {id} for {restaurant}, PC {MachineCodeText.Pretty(machine)}, {(until is null ? "no end date" : $"until {until:yyyy-MM-dd}")}, features {string.Join(",", features)}:");
        Console.WriteLine();
        Console.WriteLine(key);
        return 0;
    }
    case "show":
    {
        var key = string.Concat((a.ElementAtOrDefault(1) ?? "").Where(c => !char.IsWhiteSpace(c)));
        if (!key.StartsWith(LicenceVerifier.Prefix)) return Fail("Not a Dine In licence key.");
        var p = key[LicenceVerifier.Prefix.Length..].Split('.')[0].Replace('-', '+').Replace('_', '/');
        Console.WriteLine(Encoding.UTF8.GetString(Convert.FromBase64String(p.PadRight(p.Length + (4 - p.Length % 4) % 4, '='))));
        return 0;
    }
    default:
        Console.WriteLine("dinein-licence keygen --out <folder> | issue --key <pem> --restaurant \"Name\" --machine CODE [--days N|--until DATE|--lifetime] [--features all|a,b] | show <key>");
        return a.Count == 0 ? 0 : 1;
}

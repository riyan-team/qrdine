using QrDine.Domain.Billing;

namespace QrDine.Application.Reports;

/// <summary>
/// How the money for a set of bills actually came in: each part payment by its own method, and whatever the
/// parts didn't cover by the bill's own method. Day closing, the report's payment list and the dashboard's
/// "how people paid" all count it this way, so they always agree.
/// </summary>
public static class MoneyTaken
{
    public readonly record struct Taken(int InvoiceId, string Method, decimal Amount);

    public static List<Taken> ByMethod(IEnumerable<Invoice> invoices, IEnumerable<Payment> parts)
    {
        var bySession = parts.GroupBy(p => p.TableSessionId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.Id).ToList());
        var taken = new List<Taken>();
        foreach (var i in invoices)
        {
            var mine = bySession.TryGetValue(i.TableSessionId, out var found)
                ? found.Select(p => new Taken(i.Id, p.Method, p.Amount)).ToList()
                : new List<Taken>();

            // Older bills could take a part bigger than what was left; the extra went back as change, in cash.
            // Count only what the bill kept: trim the excess off the cash parts first, newest first.
            var excess = mine.Sum(t => t.Amount) - i.Total;
            foreach (var k in Enumerable.Range(0, mine.Count).OrderByDescending(k => IsCash(mine[k].Method)).ThenByDescending(k => k))
            {
                if (excess <= 0.01m) break;
                var cut = Math.Min(excess, mine[k].Amount);
                mine[k] = mine[k] with { Amount = mine[k].Amount - cut };
                excess -= cut;
            }
            taken.AddRange(mine.Where(t => t.Amount > 0));

            var rest = i.Total - mine.Sum(t => t.Amount);
            if (rest > 0.01m) taken.Add(new Taken(i.Id, i.PaymentMethod.StartsWith("Split") ? "Other" : i.PaymentMethod, rest));
        }
        return taken;
    }

    public static bool IsCash(string? method) => string.Equals(method, "Cash", StringComparison.OrdinalIgnoreCase);
}

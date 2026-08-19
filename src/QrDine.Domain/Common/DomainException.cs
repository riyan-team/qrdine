namespace QrDine.Domain.Common;

/// <summary>
/// Thrown when a caller tries to break a business rule (e.g. serving an order
/// that was never cooked). The API translates this into a clean 400/409 rather
/// than a 500 — it is expected, not a bug.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

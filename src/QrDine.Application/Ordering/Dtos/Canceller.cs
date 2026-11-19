namespace QrDine.Application.Ordering.Dtos;

/// <summary>
/// Who is cancelling an order or taking a dish off it, as the owner's cancelling rules see them.
/// The default is a member of staff who is not the owner and not at the counter.
/// </summary>
/// <param name="IsGuest">The guest at the table, from their own phone.</param>
/// <param name="IsOwner">The owner: always allowed once cooking starts.</param>
/// <param name="AtCounter">A cashier or manager: allowed at any stage when the owner turns on the cashier option.</param>
public readonly record struct Canceller(bool IsGuest = false, bool IsOwner = false, bool AtCounter = false)
{
    public static Canceller Guest => new(IsGuest: true);
}

/// <summary>The cancelling rules a staff screen needs: whether cooking locks cancelling, and whether the counter
/// may cancel at any stage.</summary>
public record CancelRulesDto(bool LockOnceCooking, bool CounterAnyStage);

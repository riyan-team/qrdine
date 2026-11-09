namespace QrDine.Domain.Ordering;

/// <summary>
/// Where an order originated. Both sources feed the same central order pipeline
/// (reception → kitchen → serve → pay); this only records who put it in, so staff
/// screens can tell a self-service scan from a waiter taking the order at the table.
/// </summary>
public enum OrderSource
{
    Qr,      // the guest scanned the table QR and ordered on their own phone
    Waiter,  // a waiter took the order on the guest's behalf
    Counter, // rung up at the counter for parcel or delivery
}

/// <summary>How the food reaches the customer.</summary>
public enum OrderChannel
{
    Dine,
    Takeaway,
    Delivery,
}

namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>The stored key without the key: whether one exists, the switch, and the two dates the card shows.</summary>
public sealed record DeliveryReplyKeyResponse(bool Configured, bool Enabled, DateTime? CreatedAt, DateTime? LastCallAt)
{
    public static readonly DeliveryReplyKeyResponse None = new(false, false, null, null);
}

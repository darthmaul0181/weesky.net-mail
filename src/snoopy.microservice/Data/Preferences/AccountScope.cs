namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// The <c>account_id</c> sentinel of the per-account preference tables. A nullable column cannot
/// join a composite primary key, so the primary mailbox is spelled with the empty string here —
/// on the wire the same concept is spelled <c>primary</c>, and the two are never mixed.
/// </summary>
public static class AccountScope
{
    public const string Primary = "";
}

namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>
/// The three column equalities a <c>calendar-query</c> preselects on, each optional: a null asks
/// nothing of that column. Only a preselection — the file itself is what the filter is judged on.
/// </summary>
public sealed record EventColumnFilter(string? Status, string? Transparency, string? Class)
{
    public static readonly EventColumnFilter None = new(null, null, null);
}

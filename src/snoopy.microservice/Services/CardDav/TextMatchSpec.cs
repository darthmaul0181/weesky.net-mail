namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// One text-match, its collation resolved at parse time so an unknown one refuses the report
/// before any card is read.
/// </summary>
internal sealed record TextMatchSpec(
    string Value, TextMatchKind MatchType, bool Negate, DavCollationComparer Comparer)
{
    /// <summary>
    /// The raw match on one value. Negation is the caller's: it applies to « some instance
    /// matches », not to each instance in turn.
    /// </summary>
    internal bool Satisfies(string value) => MatchType switch
    {
        TextMatchKind.Equals => Comparer.Equals(value, Value),
        TextMatchKind.StartsWith => Comparer.StartsWith(value, Value),
        TextMatchKind.EndsWith => Comparer.EndsWith(value, Value),
        _ => Comparer.Contains(value, Value),
    };
}

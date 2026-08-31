namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// One collation as a fold to a canonical form, then ordinal operations on the folded strings.
/// Folding once keeps equality, substring, prefix and suffix consistent with one another — a
/// culture-sensitive comparer answers equality but has no substring operation to match it.
/// </summary>
internal sealed class DavCollationComparer(Func<string, string> fold) : StringComparer
{
    public override int Compare(string? x, string? y) =>
        string.CompareOrdinal(Folded(x), Folded(y));

    public override bool Equals(string? x, string? y) => Compare(x, y) == 0;

    public override int GetHashCode(string obj) => fold(obj).GetHashCode();

    internal bool Contains(string value, string needle) => fold(value).Contains(fold(needle));

    internal bool StartsWith(string value, string needle) =>
        fold(value).StartsWith(fold(needle), StringComparison.Ordinal);

    internal bool EndsWith(string value, string needle) =>
        fold(value).EndsWith(fold(needle), StringComparison.Ordinal);

    private string? Folded(string? value) => value is null ? null : fold(value);
}

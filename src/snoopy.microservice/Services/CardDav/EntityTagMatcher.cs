namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// RFC 7232 §2.3.2 conditional-request comparison, applied to the raw header value rather than to
/// a parsed <c>ETag</c> type: neither comparator ever throws, since a malformed header is client
/// input and the worst it may do is fail to match.
/// </summary>
internal static class EntityTagMatcher
{
    /// <summary>
    /// True when the If-None-Match header matches the resource's tag: <c>*</c> matches anything
    /// that exists, a comma-separated list matches on any member, and the weak prefix is ignored
    /// (RFC 7232 §2.3.2 — If-None-Match uses the weak comparison function).
    /// </summary>
    internal static bool NoneMatch(string? header, string entityTag) =>
        Matches(header, entityTag, weak: true);

    /// <summary>
    /// True when the If-Match header matches. <c>*</c> matches anything that exists, and the
    /// comparison is STRONG — a weak tag never satisfies If-Match. A <c>false</c> return means
    /// only "no tag in the header matched", not "the precondition failed" — an absent header is
    /// not itself a failed precondition (RFC 7232: no If-Match header means the condition is not
    /// evaluated), so a caller must check for a missing header itself before treating this
    /// result as a 412.
    /// </summary>
    internal static bool Match(string? header, string entityTag) =>
        Matches(header, entityTag, weak: false);

    private static bool Matches(string? header, string entityTag, bool weak)
    {
        if (string.IsNullOrEmpty(header)) return false;

        foreach (var rawMember in header.Split(','))
        {
            var member = rawMember.Trim();
            if (member.Length == 0) continue;
            if (member == "*") return true;

            if (member.StartsWith("W/", StringComparison.Ordinal))
            {
                if (!weak) continue;
                member = member[2..];
            }

            if (member == entityTag) return true;
        }

        return false;
    }
}

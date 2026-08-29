namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// The <c>Depth</c> header's value on a <c>PROPFIND</c>. An ABSENT header is
/// <see cref="DavDepthValue.Infinity"/> — RFC 4918 § 9.1's own reading of the silence, and the
/// value the collection refuses. Guessing instead is not symmetric: sabre guesses 1, Radicale 0,
/// and a guess of 0 renders a VALID multistatus carrying only the collection, which a client that
/// asked for 1 reads as an empty book — and applies by erasing its local copies. An error is
/// confused with nothing; a correct answer to the wrong Depth is.
/// </summary>
internal static class DavDepth
{
    /// <summary>
    /// This absence rule belongs to <c>PROPFIND</c> and to no other verb: <c>REPORT</c> has its own
    /// depth semantics per report — <c>addressbook-query</c>'s scope is its header's,
    /// <c>addressbook-multiget</c> goes in <c>Depth: 0</c> with its targets in the body, and
    /// <c>sync-collection</c> carries none at all, <c>DAV:sync-level</c> having replaced it.
    /// Null is a header present but unreadable, which the caller refuses as a 400.
    /// </summary>
    internal static DavDepthValue? Parse(string? header)
    {
        if (header is null) return DavDepthValue.Infinity;

        return header.Trim() switch
        {
            "0" => DavDepthValue.Zero,
            "1" => DavDepthValue.One,
            var value when value.Equals("infinity", StringComparison.OrdinalIgnoreCase) =>
                DavDepthValue.Infinity,
            _ => null,
        };
    }
}

/// <summary>The three depths RFC 4918 § 10.2 admits.</summary>
internal enum DavDepthValue
{
    Zero,
    One,
    Infinity,
}

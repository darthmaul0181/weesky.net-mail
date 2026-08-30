namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// One line per /dav request, always the same message template so a log query can filter on it.
/// </summary>
/// <remarks>
/// The symptom of nearly every failure of this protocol is the same sentence — "the book is empty
/// on the client" — behind at least five causes: an <c>Authorization</c> header a proxy swallowed,
/// a PROPFIND a firewall refused, an incomplete backfill, a token refused in a loop, a report we
/// do not serve. An HTTP access log separates none of them: it sees a method, a path and a status,
/// and every one of those five can present as a 207 carrying nothing. What separates them is the
/// depth asked, the report named, the sync tokens in and out, how many <c>response</c> elements
/// the answer actually carried and which precondition refused it — this line, and only this line.
/// </remarks>
internal static class DavRequestLog
{
    /// <summary>
    /// Two of these fields are named by the client's own document. Unbounded, that is a log a
    /// client floods one request at a time.
    /// </summary>
    internal const int MaxFieldLength = 64;

    internal static void Write(ILogger logger, DavRequestTrace trace) =>
        logger.LogInformation(
            "dav {Method} {Resource} depth={Depth} report={Report} tokenIn={TokenIn} " +
            "tokenOut={TokenOut} responses={Responses} status={Status} condition={Condition}",
            trace.Method, trace.Resource, Bounded(trace.Depth), Bounded(trace.Report),
            Bounded(trace.TokenIn), Bounded(trace.TokenOut), trace.Responses, trace.StatusCode,
            Bounded(trace.Condition));

    private static string? Bounded(string? value) =>
        value is null || value.Length <= MaxFieldLength ? value : value[..MaxFieldLength];
}

/// <summary>
/// What one /dav request is worth saying about itself. Nothing here is a secret, hashed or in
/// cleartext, and nothing here is a card: the user is the principal's GUID, which
/// <see cref="Resource"/> already carries because it is in the URL.
/// </summary>
/// <param name="Method">the HTTP method, PROPFIND and REPORT included</param>
/// <param name="Resource">the request path — never the query, which may carry a token</param>
/// <param name="Depth">the Depth header as sent, unparsed: an unreadable one is the diagnosis</param>
/// <param name="Report">the report the body named, or null when the request carried none</param>
/// <param name="TokenIn">the sync token the client presented to sync-collection, through
/// DavSyncToken.ForLog (prefix stripped, control characters blanked) — carried on the refusal
/// path above all, which is the case the field exists for</param>
/// <param name="TokenOut">the sync token the answer minted — a truncated answer mints the cut,
/// not the counter, and telling the two apart in a log is the diagnosis this field buys</param>
/// <param name="Responses">how many response elements the multistatus carried — an empty book is
/// a claim about this number, and an access log cannot carry it</param>
/// <param name="StatusCode">the status actually written</param>
/// <param name="Condition">the precondition element that refused the request, when one did</param>
internal sealed record DavRequestTrace(
    string Method,
    string Resource,
    string? Depth,
    string? Report,
    string? TokenIn,
    string? TokenOut,
    int Responses,
    int StatusCode,
    string? Condition);

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// What a sync-collection answer is worth logging: how many <c>response</c> elements it carried,
/// and the token it minted — the very string written into the document, so the request log can
/// never claim a token the answer did not carry.
/// </summary>
internal sealed record SyncReportOutcome(int Responses, string TokenOut);

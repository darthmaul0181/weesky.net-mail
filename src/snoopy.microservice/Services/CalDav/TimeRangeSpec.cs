namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>A window <c>[FromUtc, ToUtc[</c> where a null bound is an infinity, as RFC 4791 § 9.9
/// reads an absent one. <see cref="Closed"/> is for the callers that demanded both.</summary>
internal sealed record TimeRangeSpec(DateTime? FromUtc, DateTime? ToUtc)
{
    internal (DateTime From, DateTime To) Closed => (FromUtc!.Value, ToUtc!.Value);
}

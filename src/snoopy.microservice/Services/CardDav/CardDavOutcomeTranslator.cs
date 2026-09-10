using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Services.CalDav;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// The one place a write outcome becomes a CardDAV answer: the condition each refusal names is
/// decided here, the status and the writing in <see cref="DavWriteAnswer"/>, shared with the
/// calendar. A new <see cref="DavWriteStatus"/> without a branch here is a CS8509 at build time
/// and a red <c>EveryEnumValue_IsHandled</c> behind it.
/// </summary>
internal static class CardDavOutcomeTranslator
{
    internal const string RetryAfterSeconds = DavWriteAnswer.RetryAfterSeconds;

    private static readonly XName ValidAddressData = DavXml.CardDav + "valid-address-data";
    private static readonly XName SupportedAddressData = DavXml.CardDav + "supported-address-data";
    private static readonly XName NoUidConflict = DavXml.CardDav + "no-uid-conflict";
    private static readonly XName MaxResourceSize = DavXml.CardDav + "max-resource-size";

    /// <summary>
    /// Writes the answer <paramref name="outcome"/> calls for. Each refusal keeps its OWN
    /// condition: a client abandons a valid-address-data, re-exports a supported-address-data and
    /// re-reads the href a no-uid-conflict carries.
    /// </summary>
    internal static Task WriteAsync(HttpResponse response, DavWriteOutcome outcome,
        CancellationToken cancellationToken, ILogger? logger = null) =>
        DavWriteAnswer.WriteAsync(response, outcome, ConditionOf(outcome.Status), cancellationToken, logger);

    internal static int StatusCodeOf(DavWriteStatus status) => DavWriteAnswer.StatusCodeOf(status);

    // CS8524 alone — the arm no NAMED member reaches, only an int cast to the enum, which nothing
    // here produces. CS8509, the missing named member, stays a warning on purpose: it is what
    // fails the build the day a status is added without a branch, rather than the client.
#pragma warning disable CS8524

    /// <summary>
    /// The precondition element a refusal names, null when it names none. A client loops on these
    /// refusals whatever the code — DAVx5 catches neither a 403 outside need-privileges nor a 507 —
    /// but the named condition makes it a readable log line, where a 500 is an accident
    /// indistinguishable server-side. The two statuses only the calendar writer answers keep their
    /// own element wherever they are translated, rather than borrow a CardDAV one that says
    /// something else.
    /// </summary>
    internal static XName? ConditionOf(DavWriteStatus status) => status switch
    {
        DavWriteStatus.InvalidCard => ValidAddressData,
        DavWriteStatus.UnsupportedVersion => SupportedAddressData,
        DavWriteStatus.UidConflict => NoUidConflict,
        DavWriteStatus.TooLarge => MaxResourceSize,
        DavWriteStatus.UnsupportedComponent => CalDavError.SupportedCalendarComponent,
        DavWriteStatus.TooManyInstances => CalDavError.MaxInstances,
        DavWriteStatus.Created => null,
        DavWriteStatus.Replaced => null,
        DavWriteStatus.Deleted => null,
        DavWriteStatus.CollectionFull => null,
        DavWriteStatus.AlreadyExists => null,
        DavWriteStatus.PreconditionFailed => null,
        DavWriteStatus.NotFound => null,
        DavWriteStatus.Busy => null,
    };

#pragma warning restore CS8524

    internal static bool IsTransient(Exception exception) => DavWriteAnswer.IsTransient(exception);
}

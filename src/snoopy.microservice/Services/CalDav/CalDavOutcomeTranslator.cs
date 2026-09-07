using System.Xml.Linq;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>
/// <see cref="CardDav.CardDavOutcomeTranslator"/>'s twin: the condition a calendar refusal names,
/// with the status and the writing in <see cref="DavWriteAnswer"/>. An <see cref="DavWriteStatus.InvalidCard"/>
/// names the precondition the gate judged — one of three — carried on the outcome by
/// <see cref="DavWriteOutcome.Precondition"/>: a second reading of the file to find it out would be
/// a second truth.
/// </summary>
internal static class CalDavOutcomeTranslator
{
    internal static Task WriteAsync(HttpResponse response, DavWriteOutcome outcome,
        CancellationToken cancellationToken, ILogger? logger = null) =>
        DavWriteAnswer.WriteAsync(response, outcome, ConditionOf(outcome), cancellationToken, logger);

#pragma warning disable CS8524

    /// <summary>The precondition element a refusal names, null when it names none.</summary>
    internal static XName? ConditionOf(DavWriteOutcome outcome) => outcome.Status switch
    {
        DavWriteStatus.InvalidCard => outcome.Precondition is { } judged
            ? CalDavError.Of(judged)
            : CalDavError.ValidCalendarData,
        DavWriteStatus.UnsupportedVersion => CalDavError.SupportedCalendarData,
        DavWriteStatus.UnsupportedComponent => CalDavError.SupportedCalendarComponent,
        DavWriteStatus.TooManyInstances => CalDavError.MaxInstances,
        DavWriteStatus.TooLarge => CalDavError.MaxResourceSize,
        DavWriteStatus.UidConflict => CalDavError.NoUidConflict,
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
}

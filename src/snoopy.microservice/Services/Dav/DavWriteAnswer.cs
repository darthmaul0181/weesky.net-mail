using System.Xml.Linq;
using MySqlConnector;
using weesky.Snoopy.Microservice.Models.Dav;

namespace weesky.Snoopy.Microservice.Services.Dav;

/// <summary>
/// What the two protocols' outcome translators share: the status each outcome earns, the answer
/// written once the protocol has named its condition, and the two MariaDB refusals a loaded
/// collection answers with. The condition itself stays per protocol — one status names a CardDAV
/// element on the address book and a CalDAV one on a calendar. Every branch is named and there is
/// no <c>default</c> that falls through to a 500: the store's refusals are written for the
/// webmail's UI, and left untranslated they surface as the one status a DAV client retries
/// indefinitely, on the same resource, every sync cycle. A new <see cref="DavWriteStatus"/>
/// without a branch here is a CS8509 at build time — the build runs at zero warnings — and a red
/// <c>EveryEnumValue_IsHandled</c> behind it.
/// </summary>
internal static class DavWriteAnswer
{
    /// <summary>A lock race is a moment, not a state: this is what turns the 503 into "later".</summary>
    internal const string RetryAfterSeconds = "1";

    // The two MariaDB refusals a loaded collection answers with. The message is never consulted:
    // it is translated according to the server's locale.
    private const int LockWaitTimeout = 1205;
    private const int Deadlock = 1213;

    /// <summary>
    /// Writes the answer <paramref name="outcome"/> calls for: the status, the <c>DAV:</c> header
    /// and the ETag of an accepted write, the <c>Retry-After</c> of a 503, and — when the protocol
    /// named a <paramref name="condition"/> — the error document carrying that element, with the
    /// holder's href inside a <c>no-uid-conflict</c>.
    /// </summary>
    internal static async Task WriteAsync(HttpResponse response, DavWriteOutcome outcome,
        XName? condition, CancellationToken cancellationToken, ILogger? logger = null)
    {
        var statusCode = StatusCodeOf(outcome.Status);

        if (condition is null)
        {
            if (outcome.Status is DavWriteStatus.Busy) response.Headers.RetryAfter = RetryAfterSeconds;
            if (Accepted(outcome.Status))
            {
                DavHeaders.ApplyDav(response);
                // A null ETag stays absent: the stored bytes differ from the sent ones (a stamped
                // UID), and the RFC then requires NO ETag, so the client re-reads.
                if (outcome.Etag is not null) response.Headers.ETag = outcome.Etag;
            }

            response.StatusCode = statusCode;
            return;
        }

        var detail = outcome is { Status: DavWriteStatus.UidConflict, ConflictHref: not null }
            ? new XElement(DavXml.Href, outcome.ConflictHref)
            : null;
        await DavError.WriteAsync(response, statusCode, condition, detail, cancellationToken, logger);
    }

    // CS8524 alone — the arm no NAMED member reaches, only an int cast to the enum, which nothing
    // here produces. CS8509, the missing named member, stays a warning on purpose: it is what
    // fails the build the day a status is added without a branch, rather than the client.
#pragma warning disable CS8524

    /// <summary>The HTTP status each outcome earns. No arm may be shared by accident: the codes
    /// are what a client branches on, and two refusals collapsed into one erase what to do next.
    /// </summary>
    internal static int StatusCodeOf(DavWriteStatus status) => status switch
    {
        DavWriteStatus.Created => StatusCodes.Status201Created,
        DavWriteStatus.Replaced => StatusCodes.Status204NoContent,
        DavWriteStatus.Deleted => StatusCodes.Status204NoContent,
        DavWriteStatus.InvalidCard => StatusCodes.Status403Forbidden,
        DavWriteStatus.UnsupportedVersion => StatusCodes.Status403Forbidden,
        DavWriteStatus.UnsupportedComponent => StatusCodes.Status403Forbidden,
        DavWriteStatus.TooManyInstances => StatusCodes.Status403Forbidden,
        DavWriteStatus.UidConflict => StatusCodes.Status403Forbidden,
        DavWriteStatus.TooLarge => StatusCodes.Status403Forbidden,
        // RFC 4918 § 11.5; no precondition of either protocol names the cap, so the status carries it alone.
        DavWriteStatus.CollectionFull => StatusCodes.Status507InsufficientStorage,
        // The creation race's loser: its If-None-Match: * is simply false now.
        DavWriteStatus.AlreadyExists => StatusCodes.Status412PreconditionFailed,
        // The replacement race's loser: its If-Match no longer held under the lock.
        DavWriteStatus.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
        DavWriteStatus.NotFound => StatusCodes.Status404NotFound,
        DavWriteStatus.Busy => StatusCodes.Status503ServiceUnavailable,
    };

#pragma warning restore CS8524

    /// <summary>
    /// True when an exception is InnoDB saying "come back later" — a lock wait timeout (1205) or a
    /// deadlock it arbitrated (1213) — rather than a fault. The inner chain is walked: EF wraps
    /// whatever the provider threw during a save inside a <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/>,
    /// so the number never sits on the exception the caller catches.
    /// </summary>
    internal static bool IsTransient(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is MySqlException { Number: LockWaitTimeout or Deadlock }) return true;
        }

        return false;
    }

    private static bool Accepted(DavWriteStatus status) =>
        status is DavWriteStatus.Created or DavWriteStatus.Replaced or DavWriteStatus.Deleted;
}

using System.Xml.Linq;

namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// A precondition the request breaches — an <c>address-data</c> naming a version or a media type
/// outside <c>supported-address-data</c>, and the like. It carries the condition element name so a
/// controller can hand it straight to <see cref="DavError.WriteAsync"/> with the status the
/// condition calls for; a refusal escaping as a 500 is what a DAV client retries forever, on the
/// same resource, every cycle.
/// </summary>
internal sealed class DavPreconditionException(XName condition, XElement? detail = null)
    : Exception($"The CardDAV precondition {condition} refuses this request.")
{
    /// <summary>The precondition element, namespace included — never a bare local name.</summary>
    internal XName Condition { get; } = condition;

    /// <summary>Written inside the condition element verbatim, when the condition carries one.</summary>
    internal XElement? Detail { get; } = detail;
}

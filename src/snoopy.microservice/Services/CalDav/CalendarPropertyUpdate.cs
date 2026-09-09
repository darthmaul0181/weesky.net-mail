using System.Globalization;
using System.Xml.Linq;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>
/// What a PROPPATCH on a calendar asks, judged property by property (§ 11): sabre applies the ones
/// it can and refuses the rest, where the RFC would undo them all — a client that wrote four right
/// values and one wrong prefers the four.
/// </summary>
/// <param name="Accepted">
/// The value each writable property is to take, in the order the body wrote them; a null value is
/// the <c>DAV:remove</c> that empties the description.
/// </param>
/// <param name="Refused">The protected properties, the ones a calendar always carries, and the
/// writable ones whose value is not one.</param>
/// <param name="TimeZoneId">The IANA id <c>calendar-timezone</c> resolved to, when it was set.</param>
/// <param name="RemovedAndAbsent">
/// A <c>DAV:remove</c> of a property this server never stores at all — RFC 4918 § 14.23:
/// "Specifying the removal of a property that does not exist is not an error". Kept apart from
/// <see cref="Accepted"/>, which the caller writes to the store: these never were, and never are.
/// </param>
internal sealed record CalendarPropertyUpdate(
    IReadOnlyDictionary<XName, string?> Accepted, IReadOnlyList<XName> Refused, string? TimeZoneId,
    IReadOnlyList<XName> RemovedAndAbsent)
{
    private static readonly XName PropertyUpdate = DavXml.Dav + "propertyupdate";
    private static readonly XName Set = DavXml.Dav + "set";
    private static readonly XName Remove = DavXml.Dav + "remove";

    /// <summary>The properties a calendar always carries, and the protected ones: a DAV:remove of
    /// either is the 403 of § 9.2.1 — a calendar with no name is a blank row in every client, and
    /// § 15 makes the protected ones unsettable. Everything else a remove names is not there, and
    /// § 14.23 says removing what is not there is not an error.</summary>
    private static readonly XName[] Undeletable =
    [
        CalendarPropertyValue.DisplayName, CalendarPropertyValue.Color, CalendarPropertyValue.Order,
        CalendarPropertyValue.TimeZone, CalendarPropertyValue.ResourceType,
        CalendarPropertyValue.ComponentSet,
        DavXml.Dav + "getetag", DavXml.Dav + "getcontentlength", DavXml.Dav + "getlastmodified",
        DavXml.Dav + "creationdate", DavXml.Dav + "lockdiscovery", DavXml.Dav + "supportedlock",
        DavXml.Dav + "sync-token", DavXml.Dav + "current-user-principal", DavXml.Dav + "owner",
        DavXml.Dav + "supported-report-set", DavXml.Dav + "current-user-privilege-set",
    ];

    /// <param name="body">the parsed body, or null when it was empty</param>
    /// <exception cref="DavBadRequestException">
    /// The body is absent or is not a <c>DAV:propertyupdate</c>, exactly as
    /// <see cref="DavPropertyUpdate.NamesIn"/> requires of the shapes that store nothing.
    /// </exception>
    internal static CalendarPropertyUpdate Parse(XDocument? body)
    {
        if (body?.Root is not { } root || root.Name != PropertyUpdate)
            throw new DavBadRequestException("A PROPPATCH body must be a DAV:propertyupdate document.");

        Dictionary<XName, string?> accepted = [];
        List<XName> refused = [];
        List<XName> removedAndAbsent = [];

        foreach (var instruction in root.Elements())
        {
            if (instruction.Name != Set && instruction.Name != Remove) continue;

            foreach (var property in instruction.Elements(DavXml.Prop).SelectMany(prop => prop.Elements()))
            {
                // First judgement wins: a body naming one property twice gets one answer for it,
                // as § 9.2's propstat has one entry per name.
                if (accepted.ContainsKey(property.Name) || refused.Contains(property.Name)
                    || removedAndAbsent.Contains(property.Name))
                {
                    continue;
                }

                switch (Judge(property, instruction.Name == Remove))
                {
                    case (Outcome.Accepted, var value): accepted[property.Name] = value; break;
                    case (Outcome.Refused, _): refused.Add(property.Name); break;
                    case (Outcome.RemovedAndAbsent, _): removedAndAbsent.Add(property.Name); break;
                }
            }
        }

        return new CalendarPropertyUpdate(accepted, refused,
            accepted.GetValueOrDefault(CalendarPropertyValue.TimeZone), removedAndAbsent);
    }

    private enum Outcome { Accepted, Refused, RemovedAndAbsent }

    /// <summary>The property's fate, and the value it takes when accepted — null being the erasure
    /// a <c>DAV:remove</c> spells.</summary>
    private static (Outcome Outcome, string? Value) Judge(XElement property, bool removing)
    {
        if (removing)
        {
            // Only the description is stored AND emptiable; the rest is either always carried, or
            // was never there — and § 14.23 answers 200 to the removal of what is not there.
            if (property.Name == CalendarPropertyValue.Description) return (Outcome.Accepted, null);
            return Undeletable.Contains(property.Name)
                ? (Outcome.Refused, null)
                : (Outcome.RemovedAndAbsent, null);
        }

        if (property.Name == CalendarPropertyValue.Description) return (Outcome.Accepted, property.Value);
        if (property.Name == CalendarPropertyValue.DisplayName)
            return Of(CalendarPropertyValue.Name(property.Value));
        if (property.Name == CalendarPropertyValue.Color)
            return Of(CalendarPropertyValue.Colour(property.Value));
        if (property.Name == CalendarPropertyValue.Order)
            return Of(CalendarPropertyValue.Rank(property.Value)?.ToString(CultureInfo.InvariantCulture));
        if (property.Name == CalendarPropertyValue.TimeZone)
            return Of(CalendarPropertyValue.Zone(property.Value));

        return (Outcome.Refused, null);

        static (Outcome, string?) Of(string? value) =>
            value is not null ? (Outcome.Accepted, value) : (Outcome.Refused, null);
    }
}

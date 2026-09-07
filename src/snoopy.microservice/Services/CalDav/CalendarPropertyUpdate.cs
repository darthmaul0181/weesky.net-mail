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
/// <param name="Refused">Everything else, and the writable ones whose value is not one.</param>
/// <param name="TimeZoneId">The IANA id <c>calendar-timezone</c> resolved to, when it was set.</param>
internal sealed record CalendarPropertyUpdate(
    IReadOnlyDictionary<XName, string?> Accepted, IReadOnlyList<XName> Refused, string? TimeZoneId)
{
    private static readonly XName PropertyUpdate = DavXml.Dav + "propertyupdate";
    private static readonly XName Set = DavXml.Dav + "set";
    private static readonly XName Remove = DavXml.Dav + "remove";

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

        foreach (var instruction in root.Elements())
        {
            if (instruction.Name != Set && instruction.Name != Remove) continue;

            foreach (var property in instruction.Elements(DavXml.Prop).SelectMany(prop => prop.Elements()))
            {
                // First judgement wins: a body naming one property twice gets one answer for it,
                // as § 9.2's propstat has one entry per name.
                if (accepted.ContainsKey(property.Name) || refused.Contains(property.Name)) continue;

                var (stored, value) = Judge(property, instruction.Name == Remove);
                if (stored) accepted[property.Name] = value;
                else refused.Add(property.Name);
            }
        }

        return new CalendarPropertyUpdate(accepted, refused,
            accepted.GetValueOrDefault(CalendarPropertyValue.TimeZone));
    }

    /// <summary>Whether the property is stored, and the value it takes — null being the erasure a
    /// <c>DAV:remove</c> spells.</summary>
    private static (bool Stored, string? Value) Judge(XElement property, bool removing)
    {
        // A calendar always has a name, a colour, a rank and a zone; only the description may be
        // emptied, so every other removal is the 403 of § 9.2.1.
        if (removing) return (property.Name == CalendarPropertyValue.Description, null);

        if (property.Name == CalendarPropertyValue.Description) return (true, property.Value);
        if (property.Name == CalendarPropertyValue.DisplayName)
            return Of(CalendarPropertyValue.Name(property.Value));
        if (property.Name == CalendarPropertyValue.Color)
            return Of(CalendarPropertyValue.Colour(property.Value));
        if (property.Name == CalendarPropertyValue.Order)
            return Of(CalendarPropertyValue.Rank(property.Value)?.ToString(CultureInfo.InvariantCulture));
        if (property.Name == CalendarPropertyValue.TimeZone)
            return Of(CalendarPropertyValue.Zone(property.Value));

        return (false, null);

        static (bool, string?) Of(string? value) => (value is not null, value);
    }
}

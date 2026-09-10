using System.Xml.Linq;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>
/// The precondition elements a calendar answer may name (spec § 12): the six
/// <see cref="IcsPrecondition"/> of the PUT gate, and the ones no guard judges — the UID held by
/// another resource, the target off a calendar, the resource type, the filters and collations of
/// a report, the sync token. Written by <see cref="DavError"/>, in the namespace each RFC gives it.
/// </summary>
internal static class CalDavError
{
    internal static readonly XName SupportedCalendarData = DavXml.CalDav + "supported-calendar-data";
    internal static readonly XName ValidCalendarData = DavXml.CalDav + "valid-calendar-data";
    internal static readonly XName ValidCalendarObjectResource = DavXml.CalDav + "valid-calendar-object-resource";
    internal static readonly XName SupportedCalendarComponent = DavXml.CalDav + "supported-calendar-component";
    internal static readonly XName MaxResourceSize = DavXml.CalDav + "max-resource-size";
    internal static readonly XName MaxInstances = DavXml.CalDav + "max-instances";

    internal static readonly XName NoUidConflict = DavXml.CalDav + "no-uid-conflict";
    internal static readonly XName CalendarCollectionLocationOk = DavXml.CalDav + "calendar-collection-location-ok";
    internal static readonly XName ValidResourceType = DavXml.Dav + "valid-resourcetype";
    internal static readonly XName SupportedFilter = DavXml.CalDav + "supported-filter";
    internal static readonly XName SupportedCollation = DavXml.CalDav + "supported-collation";
    internal static readonly XName ValidFilter = DavXml.CalDav + "valid-filter";
    internal static readonly XName NumberOfMatchesWithinLimits = DavXml.Dav + "number-of-matches-within-limits";
    internal static readonly XName ValidSyncToken = DavXml.Dav + "valid-sync-token";
    internal static readonly XName ResourceMustBeNull = DavXml.Dav + "resource-must-be-null";
    internal static readonly XName CannotModifyProtectedProperty = DavXml.Dav + "cannot-modify-protected-property";

#pragma warning disable CS8524

    /// <summary>The element RFC 4791 § 5.3.2.1 gives each precondition the gate judges.</summary>
    internal static XName Of(IcsPrecondition precondition) => precondition switch
    {
        IcsPrecondition.SupportedCalendarData => SupportedCalendarData,
        IcsPrecondition.ValidCalendarData => ValidCalendarData,
        IcsPrecondition.ValidCalendarObjectResource => ValidCalendarObjectResource,
        IcsPrecondition.SupportedCalendarComponent => SupportedCalendarComponent,
        IcsPrecondition.MaxResourceSize => MaxResourceSize,
        IcsPrecondition.MaxInstances => MaxInstances,
    };

#pragma warning restore CS8524
}

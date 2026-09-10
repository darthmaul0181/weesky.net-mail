using weesky.Snoopy.Microservice.Services.CalDav;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Dav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.CalDav;

public sealed class CalDavErrorTests
{
    [Theory]
    [InlineData(IcsPrecondition.SupportedCalendarData, "supported-calendar-data")]
    [InlineData(IcsPrecondition.ValidCalendarData, "valid-calendar-data")]
    [InlineData(IcsPrecondition.ValidCalendarObjectResource, "valid-calendar-object-resource")]
    [InlineData(IcsPrecondition.SupportedCalendarComponent, "supported-calendar-component")]
    [InlineData(IcsPrecondition.MaxResourceSize, "max-resource-size")]
    [InlineData(IcsPrecondition.MaxInstances, "max-instances")]
    public void EveryPreconditionTheGateJudges_HasItsElement(IcsPrecondition precondition, string element) =>
        // RFC 4791 § 5.3.2.1, in the CalDAV namespace: a client reads the local name AND the
        // namespace, and a max-resource-size spelled in DAV: is one it does not recognise.
        Assert.Equal(DavXml.CalDav + element, CalDavError.Of(precondition));

    [Fact]
    public void EveryEnumValue_IsMapped()
    {
        foreach (var precondition in Enum.GetValues<IcsPrecondition>())
            Assert.Equal(DavXml.CalDav, CalDavError.Of(precondition).Namespace);
    }

    [Fact]
    public void TheConditionsNoGuardJudges_LiveInTheNamespaceTheirRfcGivesThem()
    {
        Assert.Equal(DavXml.CalDav + "no-uid-conflict", CalDavError.NoUidConflict);
        Assert.Equal(DavXml.CalDav + "calendar-collection-location-ok", CalDavError.CalendarCollectionLocationOk);
        Assert.Equal(DavXml.CalDav + "supported-filter", CalDavError.SupportedFilter);
        Assert.Equal(DavXml.CalDav + "supported-collation", CalDavError.SupportedCollation);
        Assert.Equal(DavXml.CalDav + "valid-filter", CalDavError.ValidFilter);
        // RFC 5689 § 3.3, RFC 3253 § 3.6 and RFC 6578 § 3.2 define these three in DAV: itself.
        Assert.Equal(DavXml.Dav + "valid-resourcetype", CalDavError.ValidResourceType);
        Assert.Equal(DavXml.Dav + "number-of-matches-within-limits", CalDavError.NumberOfMatchesWithinLimits);
        Assert.Equal(DavXml.Dav + "valid-sync-token", CalDavError.ValidSyncToken);
    }
}

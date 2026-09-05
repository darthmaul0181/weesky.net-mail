using weesky.Snoopy.Microservice.Services.Calendar;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The eight real files of <c>Fixtures/ICalendar</c>, read from disk rather than from a list, so a
/// ninth one dropped there is exercised without touching a line of code.
/// </summary>
public sealed class IcsCorpusTests
{
    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ICalendar"), "*.ics"))
            data.Add(Path.GetFileName(file));
        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Corpus_EveryResourceParsesProjectsAndSurvivesRoundTrip(string file)
    {
        var outcome = IcsResources.Split(File.ReadAllText(IcsResourcesTests.Corpus(file)));

        Assert.NotEmpty(outcome.Resources);
        foreach (var resource in outcome.Resources)
        {
            var parsed = IcsDocument.TryLoad(resource);
            Assert.NotNull(parsed);
            Assert.Null(IcsGuards.Check(resource, parsed));
            // The write-time trial: every real file the corpus holds must survive the gate that
            // asks the engine for one instance before the resource is stored.
            Assert.Null(IcsGuards.CheckExpansion(parsed));

            var p = IcsProjector.Project(parsed, "Europe/Brussels");
            Assert.NotEqual(string.Empty, p.Uid);
            Assert.True(p.LastOccurrence >= p.FirstOccurrence);
            Assert.True(p.EndsAt >= p.StartsAt);

            var again = IcsDocument.TryLoad(IcsDocument.Serialize(parsed));
            Assert.NotNull(again);
            var replayed = IcsProjector.Project(again, "Europe/Brussels");
            Assert.Equal(p with { Attendees = [] }, replayed with { Attendees = [] });
            Assert.Equal(p.Attendees.Count, replayed.Attendees.Count);
        }
    }

    /// <summary>
    /// The only third-tier TZID of the corpus, pinned to the instant its own VTIMEZONE gives:
    /// 09:30 on 10 March 2009 in "Canberra, Melbourne, Sydney" is +1100, the southern summer
    /// observance still in force until the following April.
    /// </summary>
    [Fact]
    public void Outlook2003_ReadsItsOwnVtimezone()
    {
        var resource = IcsResources.Split(File.ReadAllText(IcsResourcesTests.Corpus("outlook-2003.ics"))).Resources.Single();

        var p = IcsProjector.Project(IcsDocument.TryLoad(resource)!, "Europe/Brussels");

        Assert.Equal(new DateTime(2009, 3, 9, 22, 30, 0, DateTimeKind.Utc), p.StartsAt);
        Assert.Equal(new DateTime(2009, 3, 9, 22, 45, 0, DateTimeKind.Utc), p.EndsAt);
        Assert.Null(p.TimeZone);
        Assert.True(p.UnknownTimeZone);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Corpus_DensityIsAcceptable(string file)
    {
        foreach (var resource in IcsResources.Split(File.ReadAllText(IcsResourcesTests.Corpus(file))).Resources)
            Assert.Null(IcsGuards.CheckDensity(IcsDocument.TryLoad(resource)!));
    }
}

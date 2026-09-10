using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class IcsResourcesTests
{
    [Fact]
    public void Split_GroupsByUid()
    {
        var outcome = IcsResources.Split(Ics.Events(("a", null), ("b", null), ("a", "20260914")));

        Assert.Equal(2, outcome.Resources.Count);
        Assert.Equal(2, IcsDocument.TryLoad(outcome.Resources[0])!.Events.Count);
        Assert.Single(IcsDocument.TryLoad(outcome.Resources[1])!.Events);
    }

    [Fact]
    public void Split_CarriesTheZonesItsComponentsCite()
    {
        var ics = File.ReadAllText(Corpus("apple-icloud.ics"));

        var outcome = IcsResources.Split(ics);

        Assert.Equal(8, outcome.Resources.Count);
        var zoned = outcome.Resources.Where(r => r.Contains("TZID=America/Los_Angeles", StringComparison.Ordinal)).ToList();
        Assert.Equal(6, zoned.Count);
        Assert.All(zoned, r => Assert.Contains("BEGIN:VTIMEZONE", r, StringComparison.Ordinal));
        Assert.All(outcome.Resources.Except(zoned), r => Assert.DoesNotContain("BEGIN:VTIMEZONE", r, StringComparison.Ordinal));
    }

    [Fact]
    public void Split_HandsBackTodosAndJournalsAsCounts()
    {
        var outcome = IcsResources.Split(Ics.Todo().Replace("END:VCALENDAR",
            "BEGIN:VJOURNAL\r\nUID:j\r\nDTSTAMP:20260901T080000Z\r\nEND:VJOURNAL\r\nEND:VCALENDAR"));

        Assert.Empty(outcome.Resources);
        Assert.Equal(1, outcome.IgnoredTodos);
        Assert.Equal(1, outcome.IgnoredJournals);
    }

    [Fact]
    public void Split_DropsTheEnvelopesMethod() =>
        Assert.DoesNotContain("METHOD:", IcsResources.Split(File.ReadAllText(Corpus("google.ics"))).Resources.Single(), StringComparison.Ordinal);

    [Fact]
    public void Split_AnswersNothingForAnUnparsableBody() =>
        Assert.Empty(IcsResources.Split("not an icalendar at all").Resources);

    internal static string Corpus(string file) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "ICalendar", file);
}

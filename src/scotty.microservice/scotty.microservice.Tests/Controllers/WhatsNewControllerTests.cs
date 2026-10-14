using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Scotty.Microservice.Configuration;
using weesky.Scotty.Microservice.Controllers;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models.WhatsNew;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Controllers;

public sealed class WhatsNewControllerTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();
    private readonly Mock<IWhatsNewStore> _store = new();

    private WhatsNewController CreateController() => new(_store.Object)
    {
        ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be", WebmailUid)
    };

    [Fact]
    public async Task Get_AnswersTheStateInUtc()
    {
        var shown = new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Unspecified);
        _store.Setup(s => s.GetAsync(WebmailUid, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new WhatsNewState("2.4.0", [new WhatsNewItem { ItemId = "daily-image", FirstShownAt = shown }]));

        var result = await CreateController().GetWhatsNew(CancellationToken.None);

        var body = Assert.IsType<WhatsNewResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("2.4.0", body.Baseline);
        var item = Assert.Single(body.Items);
        Assert.Equal("daily-image", item.Id);
        Assert.Equal(DateTimeKind.Utc, item.FirstShownAt!.Value.Kind);
        Assert.Null(item.SeenAt);
    }

    [Fact]
    public async Task MarkItems_StoresTheDistinctIdsUnderTheWebmailUid()
    {
        var result = await CreateController().MarkItems(
            new MarkWhatsNewRequest(["a", "b", "a"], WhatsNewMark.Seen), CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsType<StatusCodeResult>(result).StatusCode);
        _store.Verify(s => s.MarkAsync(WebmailUid,
            It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { "a", "b" })),
            WhatsNewMark.Seen, It.IsAny<CancellationToken>()), Times.Once);
    }

    public static TheoryData<string[]> RefusedIds => new()
    {
        Array.Empty<string>(),
        Enumerable.Range(0, 51).Select(i => $"n{i}").ToArray(),
        new[] { "Daily-Image" },
        new[] { "daily image" },
        new[] { new string('a', 65) },
    };

    [Theory]
    [MemberData(nameof(RefusedIds))]
    public async Task MarkItems_RefusesABadBatch(string[] ids)
    {
        var result = await CreateController().MarkItems(
            new MarkWhatsNewRequest(ids, WhatsNewMark.Shown), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarkItems_RefusesAnIdWithATrailingNewline()
    {
        var result = await CreateController().MarkItems(
            new MarkWhatsNewRequest(["daily-image\n"], WhatsNewMark.Shown), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarkItems_RefusesAnUndefinedState()
    {
        var result = await CreateController().MarkItems(
            new MarkWhatsNewRequest(["a"], (WhatsNewMark)7), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarkItems_RefusesAMissingBody()
    {
        Assert.IsType<BadRequestObjectResult>(await CreateController().MarkItems(null!, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await CreateController().MarkItems(
            new MarkWhatsNewRequest(null, WhatsNewMark.Shown), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await CreateController().MarkItems(
            new MarkWhatsNewRequest(["a"], null), CancellationToken.None));
    }

    // The web app sends lowercase states; the app-wide converter also lets an integer through, which the action refuses.
    [Theory]
    [InlineData("""{"ids":["a"],"state":"seen"}""", WhatsNewMark.Seen)]
    [InlineData("""{"ids":["a"],"state":7}""", (WhatsNewMark)7)]
    public void MarkWhatsNewRequest_ReadsTheWireBodyAsTheAppDoes(string json, WhatsNewMark expected)
    {
        var options = new JsonOptions();
        MvcFormatterConfiguration.ConfigureJson(options);

        Assert.Equal(expected, JsonSerializer.Deserialize<MarkWhatsNewRequest>(json, options.JsonSerializerOptions)!.State);
    }

    [Theory]
    [InlineData("2.4.0")]
    [InlineData("2.4.0-dev")]
    public async Task SetBaseline_Returns204WhenWritten(string version)
    {
        _store.Setup(s => s.SetBaselineAsync(WebmailUid, version, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateController().SetBaseline(new SetWhatsNewBaselineRequest(version), CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsType<StatusCodeResult>(result).StatusCode);
    }

    [Fact]
    public async Task SetBaseline_Returns409WhenAlreadySet()
    {
        _store.Setup(s => s.SetBaselineAsync(WebmailUid, "2.4.0", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await CreateController().SetBaseline(new SetWhatsNewBaselineRequest("2.4.0"), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2.4")]
    [InlineData("2.4.0-beta")]
    [InlineData("2.4.0\n")]
    [InlineData("123456.0.0")]
    [InlineData("٢.٤.٠")]   // Arabic-Indic digits: a Unicode \d, not a number the web app can compare
    public async Task SetBaseline_RefusesAMalformedVersion(string? version)
    {
        var result = await CreateController().SetBaseline(new SetWhatsNewBaselineRequest(version), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.VerifyNoOtherCalls();
    }
}

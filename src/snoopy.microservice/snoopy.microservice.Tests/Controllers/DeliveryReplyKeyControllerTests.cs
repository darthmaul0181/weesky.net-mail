using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar.Delivery;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class DeliveryReplyKeyControllerTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private readonly string _database = Guid.NewGuid().ToString("N");
    private readonly Mock<IDeliveryKeyProvider> _provider = new();
    private readonly MutableTimeProvider _clock = new();

    private DeliveryKeyStore Store() => new(new PreferencesTestDbContext(_database));

    private DeliveryReplyKeyController Controller(IDeliveryKeyStore? store = null)
    {
        var controller = new DeliveryReplyKeyController(store ?? Store(), _provider.Object, _clock)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return controller;
    }

    [Fact]
    public async Task Get_SaysNotConfigured_ThenTheDates()
    {
        var none = (await Controller().GetDeliveryReplyKey(None)).Value!;
        Assert.Equal(DeliveryReplyKeyResponse.None, none);

        var generated = await Controller().GenerateDeliveryReplyKey(None);
        var got = (await Controller().GetDeliveryReplyKey(None)).Value!;
        Assert.Equal((true, false, (DateTime?)null), (got.Configured, got.Enabled, got.LastCallAt));
        Assert.Equal(DateTimeKind.Utc, got.CreatedAt!.Value.Kind);
        Assert.IsType<OkObjectResult>(generated.Result);
    }

    [Fact]
    public async Task Generate_ReturnsTheKeyOnce_NoStore_AndInvalidates_AndRevokesThePrevious()
    {
        var controller = Controller();
        var first = (DeliveryReplyKeyGenerated)((OkObjectResult)(await controller.GenerateDeliveryReplyKey(None)).Result!).Value!;
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal(43, first.Key.Length);
        Assert.True(DeliveryKeys.Matches(first.Key, (await Store().FindAsync(None))!.KeyHash));

        var second = (DeliveryReplyKeyGenerated)((OkObjectResult)(await Controller().GenerateDeliveryReplyKey(None)).Result!).Value!;
        var row = (await Store().FindAsync(None))!;
        Assert.False(DeliveryKeys.Matches(first.Key, row.KeyHash));
        Assert.True(DeliveryKeys.Matches(second.Key, row.KeyHash));
        _provider.Verify(p => p.InvalidateAsync(CancellationToken.None), Times.Exactly(2));
    }

    [Fact]
    public async Task Enable_WithoutAKey_Is409_DisableIs204_EnableWithAKeyIs204()
    {
        var refused = await Controller().SetDeliveryReplies(new DeliveryReplyKeyEnableRequest(true), None);
        Assert.Equal(DeliveryReplyKeyController.KeyMissing,
            ((ResultEnveloppe)((ConflictObjectResult)refused).Value!).Message);
        Assert.IsType<NoContentResult>(await Controller().SetDeliveryReplies(new DeliveryReplyKeyEnableRequest(false), None));

        await Controller().GenerateDeliveryReplyKey(None);
        Assert.IsType<NoContentResult>(await Controller().SetDeliveryReplies(new DeliveryReplyKeyEnableRequest(true), None));
        Assert.True((await Store().FindAsync(None))!.Enabled);
        _provider.Verify(p => p.InvalidateAsync(CancellationToken.None), Times.Exactly(2));
    }

    [Fact]
    public async Task Delete_Is404WithoutAKey_And204WithOne_AndDisables()
    {
        Assert.IsType<NotFoundObjectResult>(await Controller().DeleteDeliveryReplyKey(None));

        await Controller().GenerateDeliveryReplyKey(None);
        Assert.IsType<NoContentResult>(await Controller().DeleteDeliveryReplyKey(None));
        Assert.Null(await Store().FindAsync(None));
        Assert.Equal(DeliveryReplyKeyResponse.None, (await Controller().GetDeliveryReplyKey(None)).Value);
        _provider.Verify(p => p.InvalidateAsync(CancellationToken.None), Times.Exactly(2));
    }

    [Fact]
    public async Task ConcurrentWrites_Answer409_AndNeverInvalidateTheCache()
    {
        var store = new Mock<IDeliveryKeyStore>();
        store.Setup(s => s.ReplaceAsync(It.IsAny<byte[]>(), It.IsAny<DateTime>(), None)).ReturnsAsync(false);
        store.Setup(s => s.SetEnabledAsync(It.IsAny<bool>(), It.IsAny<DateTime>(), None)).ReturnsAsync(DeliveryKeyWrite.Conflict);
        store.Setup(s => s.DeleteAsync(None)).ReturnsAsync(DeliveryKeyWrite.Conflict);
        var controller = Controller(store.Object);

        var generated = await controller.GenerateDeliveryReplyKey(None);
        Assert.Equal(DeliveryReplyKeyController.ChangedConcurrently,
            ((ResultEnveloppe)((ConflictObjectResult)generated.Result!).Value!).Message);

        var enabled = await controller.SetDeliveryReplies(new DeliveryReplyKeyEnableRequest(true), None);
        Assert.Equal(DeliveryReplyKeyController.ChangedConcurrently,
            ((ResultEnveloppe)((ConflictObjectResult)enabled).Value!).Message);

        var deleted = await controller.DeleteDeliveryReplyKey(None);
        Assert.Equal(DeliveryReplyKeyController.ChangedConcurrently,
            ((ResultEnveloppe)((ConflictObjectResult)deleted).Value!).Message);

        _provider.Verify(p => p.InvalidateAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}

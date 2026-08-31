using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class ContactsBackfillControllerTests
{
    private readonly Mock<IContactStore> _store = new();

    private ContactsBackfillController CreateController() =>
        new(_store.Object, NullLogger<ContactsBackfillController>.Instance);

    [Fact]
    public async Task Backfill_AnswersTheCount()
    {
        _store.Setup(s => s.BackfillAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackfillOutcome(3, 7));

        var result = await CreateController().Backfill(default(int?), CancellationToken.None);

        Assert.Equal(new BackfillOutcome(3, 7),
            Assert.IsType<OkObjectResult>(result.Result).Value);
        _store.Verify(s => s.BackfillAsync(
            ContactsBackfillController.DefaultBatchSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Un exploitant à 2 h du matin qui tape ?batchSize=0 doit voir du travail fait, pas un 400 :
    // la valeur est ramenée dans les bornes plutôt que refusée.
    [Fact]
    public async Task Backfill_ClampsTheBatchSize()
    {
        _store.Setup(s => s.BackfillAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackfillOutcome(0, 0));
        var controller = CreateController();

        await controller.Backfill(0, CancellationToken.None);
        await controller.Backfill(int.MaxValue, CancellationToken.None);

        _store.Verify(s => s.BackfillAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _store.Verify(s => s.BackfillAsync(
            ContactsBackfillController.MaxBatchSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Le 403 est la réponse du framework, pas la nôtre : ce qui se teste ici est que la route
    // porte bien la policy — la seule chose qui la lui vaudra.
    [Fact]
    public void Backfill_IsReservedToAdministrators()
    {
        var method = typeof(ContactsBackfillController)
            .GetMethod(nameof(ContactsBackfillController.Backfill))!;

        var authorize = Assert.Single(
            method.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>());
        Assert.Equal(AdminRequirement.PolicyName, authorize.Policy);
    }
}

using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Data;

public sealed class DavCredentialEntityTests
{
    [Fact]
    public async Task DavCredential_RoundTripsThroughTheContext()
    {
        var context = new PreferencesTestDbContext(nameof(DavCredential_RoundTripsThroughTheContext));
        var user = Guid.NewGuid();

        context.DavCredentials.Add(new DavCredential
        {
            UserId = user,
            CardDavEnabled = true,
            SecretHash = new string('a', 64),
            Salt = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16],
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(CancellationToken.None);

        var stored = Assert.Single(context.DavCredentials);
        Assert.Equal(user, stored.UserId);
        Assert.True(stored.CardDavEnabled);
        Assert.Equal(16, stored.Salt.Length);
        // Jamais utilisé veut dire null, et se dit à l'écran — pas une case vide (décision 19).
        Assert.Null(stored.LastUsedAt);
    }

    [Fact]
    public void DavCredential_IsEnabledWhenBorn()
    {
        // Le défaut décrit l'état dans lequel la ligne naît : elle n'existe que si l'utilisateur
        // a allumé l'interrupteur. Un compte sans ligne ne synchronise pas.
        Assert.True(new DavCredential().CardDavEnabled);
    }

    [Fact]
    public void UserIdIsThePrimaryKey_SoThereIsExactlyOneSecretPerUser()
    {
        var context = new PreferencesTestDbContext(nameof(UserIdIsThePrimaryKey_SoThereIsExactlyOneSecretPerUser));

        var key = context.Model.FindEntityType(typeof(DavCredential))!.FindPrimaryKey()!;

        var property = Assert.Single(key.Properties);
        Assert.Equal(nameof(DavCredential.UserId), property.Name);
    }
}

using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Data;

public sealed class DavCredentialEntityTests
{
    [Fact]
    public async Task DavCredential_RoundTripsThroughTheContext()
    {
        var db = nameof(DavCredential_RoundTripsThroughTheContext);
        var user = Guid.NewGuid();
        var digest = new string('a', 64);
        var created = new DateTime(2026, 8, 23, 10, 0, 0, DateTimeKind.Utc);

        using (var writing = new PreferencesTestDbContext(db))
        {
            writing.DavCredentials.Add(new DavCredential
            {
                UserId = user,
                CardDavEnabled = true,
                SecretHash = digest,
                Salt = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16],
                CreatedAt = created
            });
            await writing.SaveChangesAsync(CancellationToken.None);
        }

        // Relu depuis un second contexte : celui qui a écrit rend l'instance suivie, et les
        // assertions ne feraient que redire l'objet littéral sans traverser le modèle.
        using var reading = new PreferencesTestDbContext(db);
        var stored = Assert.Single(reading.DavCredentials);
        Assert.Equal(user, stored.UserId);
        Assert.True(stored.CardDavEnabled);
        Assert.Equal(digest, stored.SecretHash);
        Assert.Equal(created, stored.CreatedAt);
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

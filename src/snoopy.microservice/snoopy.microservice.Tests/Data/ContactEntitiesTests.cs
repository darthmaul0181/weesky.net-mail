using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Data;

public sealed class ContactEntitiesTests
{
    [Fact]
    public async Task Contact_RoundTripsThroughTheContext()
    {
        var context = new PreferencesTestDbContext(nameof(Contact_RoundTripsThroughTheContext));
        var id = Guid.NewGuid();
        var user = Guid.NewGuid();

        context.Contacts.Add(new Contact
        {
            Id = id, UserId = user, Uid = id.ToString(), FirstName = "Bruno",
            LastName = "Mertens", Nickname = "bru", IsFavorite = true, UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(CancellationToken.None);

        var stored = Assert.Single(context.Contacts);
        Assert.Equal("Bruno", stored.FirstName);
        Assert.True(stored.IsFavorite);
        Assert.Null(stored.VCardRaw);
    }

    [Fact]
    public void Contact_CarriesTheProjectionColumns()
    {
        var contact = new Contact { DisplayName = "Dr. John Smith Jr.", Birthday = "--0315", CardHash = "" };

        Assert.Equal("--0315", contact.Birthday);
        Assert.Equal(string.Empty, contact.CardHash); // "" = not computed yet, never null
    }

    // The composite key is (ContactId, Position), not (ContactId, Address): under the new schema
    // (spec, § Schéma) the same address may legally appear twice on one contact under two
    // different TYPE values, as two distinct properties on the card.
    [Fact]
    public async Task ContactEmail_KeyIsContactIdAndPosition()
    {
        var context = new PreferencesTestDbContext(nameof(ContactEmail_KeyIsContactIdAndPosition));
        var id = Guid.NewGuid();

        context.ContactEmails.Add(new ContactEmail
        {
            ContactId = id, Position = 0, Address = "a@b.c", Type = "HOME"
        });
        context.ContactEmails.Add(new ContactEmail
        {
            ContactId = id, Position = 1, Address = "a@b.c", Type = "WORK"
        });
        await context.SaveChangesAsync(CancellationToken.None);

        Assert.Equal(2, await context.ContactEmails.CountAsync());
    }

    [Fact]
    public async Task ChildTables_RoundTrip()
    {
        var context = new PreferencesTestDbContext(nameof(ChildTables_RoundTrip));
        var id = Guid.NewGuid();

        context.ContactPhones.Add(new ContactPhone { ContactId = id, Position = 0, Number = "+3221234567", Pref = 101 });
        context.ContactAddresses.Add(new ContactAddress { ContactId = id, Position = 0, Street = "Rue Haute 1", Locality = "Bruxelles" });
        context.ContactPhotos.Add(new ContactPhoto { ContactId = id, MediaType = "image/jpeg", Bytes = [1, 2, 3] });
        await context.SaveChangesAsync(CancellationToken.None);

        Assert.Single(await context.ContactPhones.ToListAsync());
    }

    // The model, not the behaviour: the InMemory provider enforces no foreign key, so no
    // functional test here can reproduce the INSERT-order failure a real MariaDB gives. What is
    // assertable is the edge itself — it is what makes EF write contacts before contact_emails
    // instead of falling back to alphabetical table order.
    [Fact]
    public void ContactEmail_DeclaresForeignKeyToContact()
    {
        var context = new PreferencesTestDbContext(nameof(ContactEmail_DeclaresForeignKeyToContact));

        var entity = context.Model.FindEntityType(typeof(ContactEmail));
        Assert.NotNull(entity);
        var foreignKey = Assert.Single(entity.GetForeignKeys());

        Assert.Equal(typeof(Contact), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(nameof(ContactEmail.ContactId), Assert.Single(foreignKey.Properties).Name);
        Assert.Equal(nameof(Contact.Id), Assert.Single(foreignKey.PrincipalKey.Properties).Name);
        Assert.True(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    // The five sibling tables (contacts, folder_role_overrides, user_preferences,
    // sending_identities, trusted_senders) all carry the identical fk_..._user ON DELETE CASCADE
    // shape to users(id), so one parameterised test stands in for five near-duplicates of
    // ContactEmail_DeclaresForeignKeyToContact above rather than repeating its body five times.
    [Theory]
    [InlineData(typeof(Contact), nameof(Contact.UserId))]
    [InlineData(typeof(FolderRoleOverride), nameof(FolderRoleOverride.UserId))]
    [InlineData(typeof(UserPreference), nameof(UserPreference.UserId))]
    [InlineData(typeof(SendingIdentity), nameof(SendingIdentity.UserId))]
    [InlineData(typeof(TrustedSender), nameof(TrustedSender.UserId))]
    public void Entity_DeclaresForeignKeyToWebmailUser(Type entityType, string foreignKeyPropertyName)
    {
        var context = new PreferencesTestDbContext(
            $"{nameof(Entity_DeclaresForeignKeyToWebmailUser)}_{entityType.Name}");

        var entity = context.Model.FindEntityType(entityType);
        Assert.NotNull(entity);
        var foreignKey = Assert.Single(entity.GetForeignKeys());

        Assert.Equal(typeof(WebmailUser), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(foreignKeyPropertyName, Assert.Single(foreignKey.Properties).Name);
        Assert.Equal(nameof(WebmailUser.Id), Assert.Single(foreignKey.PrincipalKey.Properties).Name);
        Assert.True(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }
}

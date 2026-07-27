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

    // The composite key is what stops one address being stored twice on the same contact:
    // inserting the same (contact_id, address) pair a second time, from a separate context so
    // the conflict is a real store-level duplicate and not merely a change-tracker identity
    // clash, must be rejected rather than create a second row.
    [Fact]
    public async Task ContactEmail_KeyIsContactPlusAddress()
    {
        var dbName = nameof(ContactEmail_KeyIsContactPlusAddress);
        var contact = Guid.NewGuid();

        var context = new PreferencesTestDbContext(dbName);
        context.ContactEmails.Add(new ContactEmail
        {
            ContactId = contact, Address = "bruno@example.com", Position = 0
        });
        await context.SaveChangesAsync(CancellationToken.None);

        var duplicateContext = new PreferencesTestDbContext(dbName);
        duplicateContext.ContactEmails.Add(new ContactEmail
        {
            ContactId = contact, Address = "bruno@example.com", Position = 1
        });
        await Assert.ThrowsAsync<ArgumentException>(
            () => duplicateContext.SaveChangesAsync(CancellationToken.None));

        Assert.NotNull(await context.ContactEmails.FindAsync([contact, "bruno@example.com"],
            CancellationToken.None));
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
}

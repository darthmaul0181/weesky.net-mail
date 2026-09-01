using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

/// <summary>
/// The kind clause on the product surfaces (décision 4). A group card is a full member of the
/// CardDAV collection and of the per-user ceiling, and of nothing the webmail's address book
/// shows: 4e stores the species, the screen that renders it comes later.
/// </summary>
public sealed class ContactStoreKindTests
{
    private static ContactStore CreateStore(string db) =>
        new(new PreferencesTestDbContext(db), ContactStoreTestFactory.NewSync().Object);

    private static ContactWrite Write(string first = "Bruno", string last = "Mertens") =>
        new(first, last, null, null, null, null, null, null, null, null, null, null, null,
            false, [], [], [], "manual");

    private static string GroupCard(string fn) =>
        $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:g1\r\nFN:{fn}\r\nX-ADDRESSBOOKSERVER-KIND:group\r\n" +
        "X-ADDRESSBOOKSERVER-MEMBER:urn:uuid:11111111-1111-1111-1111-111111111111\r\n" +
        "END:VCARD\r\n";

    /// <summary>
    /// A stored group, posed as a row rather than through a PUT: the columns are what the product
    /// reads decide on, and posing them here is what lets one test name the group's own surname.
    /// </summary>
    private static async Task<Guid> GivenAGroup(string db, Guid user, string? last = null)
    {
        await using var context = new PreferencesTestDbContext(db);
        var id = Guid.NewGuid();
        var card = GroupCard(last ?? "Amis");
        context.Contacts.Add(new Contact
        {
            Id = id, UserId = user, Uid = id.ToString(), Kind = ContactKinds.Group,
            LastName = last, DisplayName = last ?? "Amis", VCardRaw = card,
            CardHash = ContactStore.CardHashOf(card), DavName = $"{id}.vcf",
            UpdatedAt = DateTime.UtcNow, SyncSequence = 1
        });
        await context.SaveChangesAsync(CancellationToken.None);
        return id;
    }

    [Fact]
    public async Task ListAsync_LeavesAGroupOut()
    {
        var db = nameof(ListAsync_LeavesAGroupOut);
        var user = Guid.NewGuid();
        await GivenAGroup(db, user);
        await CreateStore(db).CreateAsync(user, Write(), CancellationToken.None);

        var listed = await CreateStore(db).ListAsync(user, CancellationToken.None);

        Assert.Equal("Bruno", Assert.Single(listed).FirstName);
    }

    [Fact]
    public async Task ExportAsync_LeavesAGroupOut()
    {
        var db = nameof(ExportAsync_LeavesAGroupOut);
        var user = Guid.NewGuid();
        await GivenAGroup(db, user);
        await CreateStore(db).CreateAsync(user, Write(), CancellationToken.None);

        var exported = await CreateStore(db).ExportAsync(user, CancellationToken.None);

        Assert.Equal("Bruno", Assert.Single(exported).FirstName);
    }

    [Fact]
    public async Task GetAsync_OnAGroup_AnswersNull()
    {
        var db = nameof(GetAsync_OnAGroup_AnswersNull);
        var user = Guid.NewGuid();
        var group = await GivenAGroup(db, user);

        Assert.Null(await CreateStore(db).GetAsync(user, group, CancellationToken.None));
    }

    [Fact]
    public async Task GetPhotoAsync_OnAGroup_AnswersNull()
    {
        var db = nameof(GetPhotoAsync_OnAGroup_AnswersNull);
        var user = Guid.NewGuid();
        var group = await GivenAGroup(db, user);

        // The picture leaves by its own route, so the kind clause has to be posed on it too: a
        // photo row hung on a group would otherwise be served by an id the book never listed.
        await using (var context = new PreferencesTestDbContext(db))
        {
            context.ContactPhotos.Add(new ContactPhoto
            {
                ContactId = group, MediaType = "image/png", Bytes = [1, 2, 3]
            });
            await context.SaveChangesAsync(CancellationToken.None);
        }

        Assert.Null(await CreateStore(db).GetPhotoAsync(user, group, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_OnAGroup_AnswersNotFound()
    {
        var db = nameof(UpdateAsync_OnAGroup_AnswersNotFound);
        var user = Guid.NewGuid();
        var group = await GivenAGroup(db, user);

        var saved = await CreateStore(db).UpdateAsync(user, group, Write(), CancellationToken.None);

        Assert.True(saved.IsFailure);
        Assert.Equal(ContactStore.NotFound, saved.Error);
    }

    [Fact]
    public async Task DeleteAsync_OnAGroup_AnswersNotFound()
    {
        var db = nameof(DeleteAsync_OnAGroup_AnswersNotFound);
        var user = Guid.NewGuid();
        var group = await GivenAGroup(db, user);

        var deleted = await CreateStore(db).DeleteAsync(user, group, CancellationToken.None);

        Assert.True(deleted.IsFailure);
        Assert.Equal(ContactStore.NotFound, deleted.Error);
        Assert.Equal(1, new PreferencesTestDbContext(db).Contacts.Count(c => c.UserId == user));
    }

    [Fact]
    public async Task SetFavoriteAsync_OnAGroup_AnswersNotFound()
    {
        var db = nameof(SetFavoriteAsync_OnAGroup_AnswersNotFound);
        var user = Guid.NewGuid();
        var group = await GivenAGroup(db, user);

        var starred = await CreateStore(db).SetFavoriteAsync(user, group, true, CancellationToken.None);

        Assert.True(starred.IsFailure);
        Assert.Equal(ContactStore.NotFound, starred.Error);
    }

    [Fact]
    public async Task DeleteManyAsync_SkipsAGroupInSilence()
    {
        var db = nameof(DeleteManyAsync_SkipsAGroupInSilence);
        var user = Guid.NewGuid();
        var group = await GivenAGroup(db, user);
        var contact = (await CreateStore(db).CreateAsync(user, Write(), CancellationToken.None)).Value;

        var removed = await CreateStore(db)
            .DeleteManyAsync(user, [group, contact], includeGroups: false, CancellationToken.None);

        // The same silence a foreign id gets: a batch may not half-fail, and a group is simply not
        // among what this surface addresses.
        Assert.Equal(1, removed);
        Assert.Equal(group, new PreferencesTestDbContext(db).Contacts.Single(c => c.UserId == user).Id);
    }

    [Fact]
    public async Task DeleteManyAsync_IncludingGroups_BuriesIt()
    {
        var db = nameof(DeleteManyAsync_IncludingGroups_BuriesIt);
        var user = Guid.NewGuid();
        var group = await GivenAGroup(db, user);

        // The one caller that says so: emptying the collection over DAV must take both species,
        // or the phone that asked for it keeps seeing what it deleted.
        var removed = await CreateStore(db)
            .DeleteManyAsync(user, [group], includeGroups: true, CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.Empty(new PreferencesTestDbContext(db).Contacts.Where(c => c.UserId == user));
    }

    [Fact]
    public async Task SetFavoriteManyAsync_SkipsAGroup()
    {
        var db = nameof(SetFavoriteManyAsync_SkipsAGroup);
        var user = Guid.NewGuid();
        var group = await GivenAGroup(db, user);
        var contact = (await CreateStore(db).CreateAsync(user, Write(), CancellationToken.None)).Value;

        var starred = await CreateStore(db)
            .SetFavoriteManyAsync(user, [group, contact], true, CancellationToken.None);

        Assert.Equal(1, starred);
        Assert.False(new PreferencesTestDbContext(db).Contacts.Single(c => c.Id == group).IsFavorite);
    }

    // Décision 18: a group is a row of the table and a resource of the collection, so it spends
    // the ceiling like any other — the cap bounds what the book weighs, not what the screen shows.
    [Fact]
    public async Task AGroup_CountsTowardsTheCap()
    {
        var db = nameof(AGroup_CountsTowardsTheCap);
        var user = Guid.NewGuid();
        await GivenAGroup(db, user);

        await using (var context = new PreferencesTestDbContext(db))
        {
            for (var i = 0; i < ContactStore.MaxPerUser - 1; i++)
            {
                var id = Guid.NewGuid();
                context.Contacts.Add(new Contact
                {
                    Id = id, UserId = user, Uid = id.ToString(), FirstName = $"C{i}",
                    UpdatedAt = DateTime.UtcNow
                });
            }
            await context.SaveChangesAsync(CancellationToken.None);
        }

        var created = await CreateStore(db).CreateAsync(user, Write(), CancellationToken.None);

        Assert.True(created.IsFailure);
        Assert.Equal(ContactStore.CapReached, created.Error);
    }

    // The import's name index is the fallback for a row carrying no address at all, and a group is
    // never what such a row describes: without the clause, a CSV line named after a group folds
    // into it instead of creating the contact the user meant (décision 4).
    [Fact]
    public async Task ImportAsync_ANamelessRowNamedAfterAGroup_CreatesAContact()
    {
        var db = nameof(ImportAsync_ANamelessRowNamedAfterAGroup_CreatesAContact);
        var user = Guid.NewGuid();
        await GivenAGroup(db, user, last: "Amis");

        var outcome = await CreateStore(db).ImportAsync(user, [new ContactImportRow(
            Line: 2, FirstName: null, LastName: "Amis", Nickname: null, IsFavorite: false,
            Addresses: [], VCard: null, Uid: null)], CancellationToken.None);

        Assert.Equal(1, outcome.Created);
        Assert.Equal(0, outcome.Merged);
        Assert.Equal(2, new PreferencesTestDbContext(db).Contacts.Count(c => c.UserId == user));
    }
}

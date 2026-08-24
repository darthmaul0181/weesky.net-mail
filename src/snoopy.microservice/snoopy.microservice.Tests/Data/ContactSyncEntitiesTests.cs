using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Data;

public sealed class ContactSyncEntitiesTests
{
    [Fact]
    public void TheSyncState_IsKeyedOnTheUserAlone()
    {
        var context = new PreferencesTestDbContext(nameof(TheSyncState_IsKeyedOnTheUserAlone));

        var key = context.Model.FindEntityType(typeof(ContactSyncState))!.FindPrimaryKey()!;

        // One row per user and not two: a technical key plus an index would let the table accept a
        // second state row that nothing in the code creates — until a restore puts one there.
        Assert.Equal(["UserId"], key.Properties.Select(p => p.Name));
    }

    [Fact]
    public void ATombstone_IsKeyedOnTheUserAndTheName()
    {
        var context = new PreferencesTestDbContext(nameof(ATombstone_IsKeyedOnTheUserAndTheName));

        var key = context.Model.FindEntityType(typeof(ContactTombstone))!.FindPrimaryKey()!;

        Assert.Equal(["UserId", "DavName"], key.Properties.Select(p => p.Name));
    }

    [Fact]
    public void ARevision_IsKeyedOnItsOwnIdentity()
    {
        var context = new PreferencesTestDbContext(nameof(ARevision_IsKeyedOnItsOwnIdentity));

        var entity = context.Model.FindEntityType(typeof(ContactRevision))!;

        // A journal, not a state: several rows coexist for one dav_name and only an order tells
        // them apart. (user_id, dav_name, replaced_at) would make two writes in the same second a
        // collision, on the table whose whole job is to lose nothing.
        Assert.Equal(["Id"], entity.FindPrimaryKey()!.Properties.Select(p => p.Name));
    }

    [Fact]
    public void TheCause_IsStoredAsItsName()
    {
        var context = new PreferencesTestDbContext(nameof(TheCause_IsStoredAsItsName));

        var cause = context.Model.FindEntityType(typeof(ContactRevision))!.FindProperty("Cause")!;

        // Two hazards, and both need an assertion: an int conversion would write 0..4 where MySQL
        // reads an ENUM ordinal (silently off by one, since ENUM is 1-based) — that is what the
        // ProviderClrType check catches. But HasConversion<string>() also has ProviderClrType ==
        // string, and would write "Rejected" where the column's ENUM only knows "rejected" —
        // working by accident under MariaDB's column-collation case-insensitivity until it didn't.
        // Only reading the actual converted value catches that second one.
        Assert.Equal(typeof(string), cause.GetValueConverter()!.ProviderClrType);
        Assert.Equal("rejected", cause.GetValueConverter()!.ConvertToProvider(RevisionCause.Rejected));
    }

    [Fact]
    public async Task AContact_CarriesItsNameAndRank()
    {
        var db = nameof(AContact_CarriesItsNameAndRank);
        var id = Guid.NewGuid();

        using (var writing = new PreferencesTestDbContext(db))
        {
            writing.Contacts.Add(new Contact
            {
                Id = id, UserId = Guid.NewGuid(), Uid = id.ToString(),
                DavName = $"{id}.vcf", SyncSequence = 7
            });
            await writing.SaveChangesAsync(CancellationToken.None);
        }

        // Relu depuis un second contexte : celui qui a écrit rend l'instance suivie, et les
        // assertions ne feraient que redire l'objet littéral sans traverser le modèle.
        using var reading = new PreferencesTestDbContext(db);
        var stored = await reading.Contacts.SingleAsync(CancellationToken.None);

        Assert.Equal($"{id}.vcf", stored.DavName);
        Assert.Equal(7ul, stored.SyncSequence);
    }

    [Fact]
    public async Task AContact_DefaultsToRankZeroAndNoName()
    {
        var db = nameof(AContact_DefaultsToRankZeroAndNoName);
        var id = Guid.NewGuid();

        using (var writing = new PreferencesTestDbContext(db))
        {
            writing.Contacts.Add(new Contact { Id = id, UserId = Guid.NewGuid(), Uid = id.ToString() });
            await writing.SaveChangesAsync(CancellationToken.None);
        }

        using var reading = new PreferencesTestDbContext(db);
        var stored = await reading.Contacts.SingleAsync(CancellationToken.None);

        // Proves EF actually stores and returns the absence on a fresh read, not merely that the
        // C# defaults happen to already look like this before any round trip.
        Assert.Null(stored.DavName);
        Assert.Equal(0ul, stored.SyncSequence);
    }

    [Fact]
    public void TheNewEntities_MapToTheExactTableAndColumnNamesInTheDdl()
    {
        AssertTableAndColumns(typeof(ContactSyncState), "contact_sync_state", new Dictionary<string, string>
        {
            [nameof(ContactSyncState.UserId)] = "user_id",
            [nameof(ContactSyncState.Epoch)] = "epoch",
            [nameof(ContactSyncState.Seq)] = "seq",
            [nameof(ContactSyncState.PrunedBelow)] = "pruned_below"
        });

        AssertTableAndColumns(typeof(ContactTombstone), "contact_tombstones", new Dictionary<string, string>
        {
            [nameof(ContactTombstone.UserId)] = "user_id",
            [nameof(ContactTombstone.DavName)] = "dav_name",
            [nameof(ContactTombstone.SyncSequence)] = "sync_sequence",
            [nameof(ContactTombstone.DeletedAt)] = "deleted_at"
        });

        AssertTableAndColumns(typeof(ContactRevision), "contact_revisions", new Dictionary<string, string>
        {
            [nameof(ContactRevision.Id)] = "id",
            [nameof(ContactRevision.UserId)] = "user_id",
            [nameof(ContactRevision.ContactId)] = "contact_id",
            [nameof(ContactRevision.Uid)] = "uid",
            [nameof(ContactRevision.DavName)] = "dav_name",
            [nameof(ContactRevision.CardHash)] = "card_hash",
            [nameof(ContactRevision.VCardRaw)] = "vcard_raw",
            [nameof(ContactRevision.Cause)] = "cause",
            [nameof(ContactRevision.ReplacedAt)] = "replaced_at"
        });

        // Only the two columns contacts gained in this task: the rest of Contact predates this
        // slice and is not this test's concern.
        AssertColumn(typeof(Contact), nameof(Contact.DavName), "dav_name");
        AssertColumn(typeof(Contact), nameof(Contact.SyncSequence), "sync_sequence");
    }

    // Reflection over the attributes, not the EF model: it stays true whatever provider the tests
    // run on. Every declared property must carry a [Column] naming the DDL's column — a property
    // with none would fall back to EF's by-convention name and read as fine until it hit MariaDB.
    private static void AssertTableAndColumns(Type type, string tableName, Dictionary<string, string> expectedColumnsByProperty)
    {
        var table = type.GetCustomAttribute<TableAttribute>();
        Assert.NotNull(table);
        Assert.Equal(tableName, table!.Name);

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(expectedColumnsByProperty.Count, properties.Length);
        foreach (var property in properties)
        {
            var column = property.GetCustomAttribute<ColumnAttribute>();
            Assert.NotNull(column);
            Assert.True(expectedColumnsByProperty.TryGetValue(property.Name, out var expectedName),
                $"{type.Name}.{property.Name} is not in the expected column map.");
            Assert.Equal(expectedName, column!.Name);
        }
    }

    private static void AssertColumn(Type type, string propertyName, string expectedColumnName)
    {
        var column = type.GetProperty(propertyName)!.GetCustomAttribute<ColumnAttribute>();
        Assert.NotNull(column);
        Assert.Equal(expectedColumnName, column!.Name);
    }
}

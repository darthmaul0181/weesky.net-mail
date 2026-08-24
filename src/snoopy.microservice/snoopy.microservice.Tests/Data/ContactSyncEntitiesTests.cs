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

        // The column is an ENUM of lowercase words; an int conversion would write 0..4 into it and
        // MySQL would take the number as an ordinal — silently off by one, since ENUM is 1-based.
        Assert.Equal(typeof(string), cause.GetValueConverter()!.ProviderClrType);
    }

    [Fact]
    public async Task AContact_CarriesItsNameAndRank()
    {
        var context = new PreferencesTestDbContext(nameof(AContact_CarriesItsNameAndRank));
        var id = Guid.NewGuid();
        context.Contacts.Add(new Contact
        {
            Id = id, UserId = Guid.NewGuid(), Uid = id.ToString(),
            DavName = $"{id}.vcf", SyncSequence = 7
        });
        await context.SaveChangesAsync(CancellationToken.None);

        var stored = await context.Contacts.SingleAsync(CancellationToken.None);

        Assert.Equal($"{id}.vcf", stored.DavName);
        Assert.Equal(7ul, stored.SyncSequence);
    }

    [Fact]
    public async Task AContact_DefaultsToRankZeroAndNoName()
    {
        var context = new PreferencesTestDbContext(nameof(AContact_DefaultsToRankZeroAndNoName));
        var id = Guid.NewGuid();
        context.Contacts.Add(new Contact { Id = id, UserId = Guid.NewGuid(), Uid = id.ToString() });
        await context.SaveChangesAsync(CancellationToken.None);

        var stored = await context.Contacts.SingleAsync(CancellationToken.None);

        // Rank 0 is the value a sync token never asks for (it asks `> n` with `n >= 0`), so a row
        // the backfill has not reached is invisible to the protocol rather than served nameless.
        Assert.Null(stored.DavName);
        Assert.Equal(0ul, stored.SyncSequence);
    }
}

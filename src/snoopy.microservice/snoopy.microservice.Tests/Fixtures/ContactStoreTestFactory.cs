using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;

namespace weesky.Snoopy.Microservice.Tests.Fixtures;

/// <summary>
/// The three building blocks every <see cref="ContactStore"/> test needs, shared so tasks 6 to 8
/// build on the same shapes rather than redeclaring their own. Grows with those tasks:
/// <c>ImportRows</c>, <c>MergeRowFor</c> and <c>DetailWithHash</c> join it there.
/// </summary>
internal static class ContactStoreTestFactory
{
    /// <summary>A fresh InMemory context, one per call — never shared across tests.</summary>
    internal static PreferencesTestDbContext NewContext() => new(Guid.NewGuid().ToString());

    /// <summary>
    /// A doubled <see cref="IContactSyncStore"/> that hands out <paramref name="rank"/> on every
    /// call and archives successfully. What a test wants to assert about the calls made to it is
    /// its own affair — this only sets up the defaults every test starts from.
    /// </summary>
    internal static Mock<IContactSyncStore> NewSync(ulong rank = 1)
    {
        var sync = new Mock<IContactSyncStore>();
        sync.Setup(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rank);
        sync.Setup(s => s.ArchiveAsync(It.IsAny<ContactRevision>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return sync;
    }

    /// <summary>A minimal, valid write: a name and nothing else.</summary>
    internal static ContactWrite Write(string first, string last) =>
        new(FirstName: first, LastName: last, Nickname: null, DisplayName: null, MiddleName: null,
            NamePrefix: null, NameSuffix: null, Organization: null, Department: null, JobTitle: null,
            Birthday: null, Website: null, Notes: null, IsFavorite: false, Addresses: [], Phones: [],
            PostalAddresses: [], Source: "manual");
}

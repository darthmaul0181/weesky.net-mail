using System.Text;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

/// <summary>
/// The one-shot sweep of slice 4a. Every row it reads is a pre-4a row: <c>card_hash = ''</c>, a
/// card written by <c>ContactVCardWriter</c> or none at all, and columns that may have been
/// edited since — which is why the sweep reconciles instead of projecting.
/// </summary>
public sealed class ContactStoreBackfillTests
{
    // The exact shape ContactVCardWriter produced before this slice: no UID, no REV, an N whose
    // components 3 and 4 are filled — the middle name and the honorific it read from the Outlook
    // CSV — and five families no column of a pre-4a contact could hold. Its FN is that writer's
    // own formula, first + middle + last, and no pre-4a column held the middle.
    private const string LegacyWriterCard = "BEGIN:VCARD\r\nVERSION:3.0\r\n" +
        "N:Ancien;Jean;Pierre;Dr.;\r\nFN:Jean Pierre Ancien\r\nEMAIL;TYPE=INTERNET:old@n.be\r\n" +
        "TEL;TYPE=HOME,VOICE:+3221234567\r\nORG:Acme;Ventes\r\n" +
        "ADR;TYPE=HOME:;;Rue Basse 2;Namur;;5000;Belgique\r\nNOTE:Client fidele\r\n" +
        "BDAY:1985-04-12\r\nEND:VCARD\r\n";

    private static ContactStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName), ContactStoreTestFactory.NewSync().Object);

    private static Contact SeedLegacy(
        string db, Guid user, string? vcard = null, string? firstName = "Jean",
        string? lastName = "Ancien", string? nickname = null, string cardHash = "",
        params string[] addresses)
    {
        var context = new PreferencesTestDbContext(db);
        var id = Guid.NewGuid();
        var row = new Contact
        {
            Id = id, UserId = user, Uid = id.ToString(), FirstName = firstName,
            LastName = lastName, Nickname = nickname, VCardRaw = vcard, CardHash = cardHash,
            UpdatedAt = DateTime.UtcNow
        };
        context.Contacts.Add(row);
        for (var i = 0; i < addresses.Length; i++)
            context.ContactEmails.Add(new ContactEmail
            {
                ContactId = id, Position = i, Address = addresses[i], Type = "INTERNET"
            });
        context.SaveChanges();
        return row;
    }

    private static Contact Reload(string db, Guid id) =>
        new PreferencesTestDbContext(db).Contacts.Single(c => c.Id == id);

    [Fact] // fiche sans carte -> carte neuve depuis les colonnes, hash posé, projection écrite
    public async Task Backfill_ComposesTheMissingCard()
    {
        var db = nameof(Backfill_ComposesTheMissingCard);
        var row = SeedLegacy(db, Guid.NewGuid(), firstName: "Ana", lastName: "Ruiz",
            addresses: "ana@example.com");

        var outcome = await CreateStore(db).BackfillAsync(100, CancellationToken.None);

        Assert.Equal(1, outcome.Processed);
        Assert.Equal(0, outcome.Remaining);
        var after = Reload(db, row.Id);
        Assert.Contains("N:Ruiz;Ana", after.VCardRaw!);
        Assert.Contains($"UID:{row.Uid}", after.VCardRaw!);
        Assert.Contains("ana@example.com", after.VCardRaw!);
        Assert.NotEqual(string.Empty, after.CardHash);
        var context = new PreferencesTestDbContext(db);
        Assert.Equal("ana@example.com", Assert.Single(context.ContactEmails).Address);
    }

    // La réconciliation bornée : les colonnes gagnent sur N/FN/EMAIL, et rien d'autre de la carte
    // ne bouge. Un Compose ordinaire effacerait ici TEL, ADR, ORG, BDAY et NOTE.
    [Fact]
    public async Task Backfill_ReconcilesWithoutDestroyingTheCard()
    {
        var db = nameof(Backfill_ReconcilesWithoutDestroyingTheCard);
        var row = SeedLegacy(db, Guid.NewGuid(), vcard: LegacyWriterCard, firstName: "Jean",
            lastName: "Édité", addresses: "neuve@n.be");

        var outcome = await CreateStore(db).BackfillAsync(100, CancellationToken.None);

        Assert.Equal(1, outcome.Processed);
        var after = Reload(db, row.Id);
        // Le nom d'usage et l'honorifique ne vivaient que sur la carte : aucune colonne d'avant 4a
        // ne les portait, et le FN du writer hérité les comptait. Les perdre ici, c'est
        // l'aplatissement que display_name existe pour interdire (Contact.cs:36-39).
        Assert.Contains("N:Édité;Jean;Pierre;Dr.;", after.VCardRaw!);
        Assert.Contains("FN:Jean Pierre Édité", after.VCardRaw!);
        Assert.Equal("Pierre", after.MiddleName);
        Assert.Equal("Dr.", after.NamePrefix);
        // The card carries the honorific and the usage name (asserted above); the FN is the plain
        // join of the three name columns, so the column has nothing to add to them and stays empty.
        Assert.Null(after.DisplayName);
        Assert.Contains("neuve@n.be", after.VCardRaw!);
        Assert.DoesNotContain("old@n.be", after.VCardRaw!);
        Assert.Contains("TEL", after.VCardRaw!);          // seule la carte les portait
        Assert.Contains("ORG:Acme;Ventes", after.VCardRaw!);
        Assert.Contains("ADR", after.VCardRaw!);
        Assert.Contains("BDAY", after.VCardRaw!);
        Assert.Contains("1985-04-12", after.VCardRaw!);
        Assert.Contains("NOTE:Client fidele", after.VCardRaw!);
        Assert.Contains($"UID:{row.Uid}", after.VCardRaw!);
        Assert.NotEqual(string.Empty, after.CardHash);

        var context = new PreferencesTestDbContext(db);
        Assert.NotEmpty(context.ContactPhones.Where(p => p.ContactId == row.Id)); // projeté à la fin
        Assert.NotEmpty(context.ContactAddresses.Where(a => a.ContactId == row.Id));
        Assert.Equal("Édité", after.LastName);
        Assert.Equal("Acme", after.Organization);
        Assert.Equal("1985-04-12", after.Birthday); // colonne neuve, remplie par la seule carte
    }

    // Les colonnes gagnent, mais elles ne suppriment pas : un pseudo que seule la carte porte
    // survit au rattrapage (Reconcile, ruling revue 2).
    [Fact]
    public async Task Backfill_KeepsWhatOnlyTheCardNames()
    {
        var db = nameof(Backfill_KeepsWhatOnlyTheCardNames);
        var card = "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Ancien;Jean;;;\r\nFN:Jean Ancien\r\n" +
            "NICKNAME:Johnny\r\nEMAIL;TYPE=INTERNET:seule@n.be\r\nEND:VCARD\r\n";
        var row = SeedLegacy(db, Guid.NewGuid(), vcard: card, nickname: null);

        await CreateStore(db).BackfillAsync(100, CancellationToken.None);

        var after = Reload(db, row.Id);
        Assert.Contains("NICKNAME:Johnny", after.VCardRaw!);
        Assert.Contains("seule@n.be", after.VCardRaw!); // aucune colonne d'adresse : le bloc reste
        Assert.Equal("Johnny", after.Nickname);
    }

    [Fact] // par lots : batchSize=1 sur 3 fiches -> {1, 2} puis {1, 1} puis {1, 0} ; rejouer -> {0, 0}
    public async Task Backfill_WorksInBatchesAndIsIdempotent()
    {
        var db = nameof(Backfill_WorksInBatchesAndIsIdempotent);
        var user = Guid.NewGuid();
        for (var i = 0; i < 3; i++) SeedLegacy(db, user, firstName: $"C{i}");

        Assert.Equal((1, 2), await Run(db));
        Assert.Equal((1, 1), await Run(db));
        Assert.Equal((1, 0), await Run(db));
        Assert.Equal((0, 0), await Run(db));

        Assert.All(new PreferencesTestDbContext(db).Contacts,
            c => Assert.NotEqual(string.Empty, c.CardHash));
    }

    [Fact] // la sélection est card_hash = '' : une fiche déjà traitée n'est jamais revisitée
    public async Task Backfill_SkipsProcessedRows()
    {
        var db = nameof(Backfill_SkipsProcessedRows);
        var done = SeedLegacy(db, Guid.NewGuid(), vcard: LegacyWriterCard, cardHash: "deadbeef");

        var outcome = await CreateStore(db).BackfillAsync(100, CancellationToken.None);

        // Rien sur Remaining ici : le lot vide répond avant le décompte, donc l'assertion
        // survivrait à une requête de comptage cassée. Backfill_WorksInBatches… l'exerce, lui.
        Assert.Equal(0, outcome.Processed);
        var after = Reload(db, done.Id);
        Assert.Equal("deadbeef", after.CardHash);
        Assert.Equal(LegacyWriterCard, after.VCardRaw!);
    }

    // Décision 15 : un geste d'exploitant sur toute la table, pas sur un carnet.
    [Fact]
    public async Task Backfill_SweepsEveryUser()
    {
        var db = nameof(Backfill_SweepsEveryUser);
        SeedLegacy(db, Guid.NewGuid(), firstName: "Ana");
        SeedLegacy(db, Guid.NewGuid(), firstName: "Bob");

        var outcome = await CreateStore(db).BackfillAsync(100, CancellationToken.None);

        Assert.Equal(2, outcome.Processed);
        Assert.Equal(0, outcome.Remaining);
    }

    // Le § 4 du document d'exploitation repose entièrement sur ce comportement : une carte que le
    // plafond refuse laisse la fiche intacte dans la file, jamais à moitié convertie.
    [Fact]
    public async Task Backfill_LeavesACardOverTheCeilingUntouched()
    {
        var db = nameof(Backfill_LeavesACardOverTheCeilingUntouched);
        var huge = "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Ancien;Jean;;;\r\nFN:Jean Ancien\r\n" +
            "TEL;TYPE=HOME,VOICE:+3221234567\r\nNOTE:" +
            new string('x', ContactStore.MaxCardBytes - 300) + "\r\nEND:VCARD\r\n";
        Assert.True(Encoding.UTF8.GetByteCount(huge) <= ContactStore.MaxCardBytes); // stockable tel quel
        var row = SeedLegacy(db, Guid.NewGuid(), vcard: huge);

        var outcome = await CreateStore(db).BackfillAsync(100, CancellationToken.None);

        Assert.Equal(0, outcome.Processed);
        Assert.Equal(1, outcome.Remaining);   // toujours dans la file, et le décompte le dit
        var after = Reload(db, row.Id);
        Assert.Equal(huge, after.VCardRaw);            // octet pour octet
        Assert.Equal(string.Empty, after.CardHash);
        // La projection n'a pas tourné : le TEL de la carte n'a atteint aucune ligne fille.
        Assert.Empty(new PreferencesTestDbContext(db).ContactPhones);
    }

    // La régression de production : six fiches sur trente-trois sont ressorties du rattrapage avec
    // « ? » pour nom. Sans prénom ni nom, l'écrivain 3.0 remplit le N obligatoire d'un point
    // d'interrogation, et la projection totale le relit comme une donnée.
    [Fact]
    public async Task Backfill_CardlessNicknameOnlyContact_LeavesTheNameNull()
    {
        var db = nameof(Backfill_CardlessNicknameOnlyContact_LeavesTheNameNull);
        var row = SeedLegacy(db, Guid.NewGuid(), firstName: null, lastName: null,
            nickname: "Marie-Rose Molhan", addresses: "marie-rose.molhan@weesky.be");

        var outcome = await CreateStore(db).BackfillAsync(100, CancellationToken.None);

        Assert.Equal(1, outcome.Processed);
        var after = Reload(db, row.Id);
        Assert.Null(after.LastName);
        Assert.Null(after.FirstName);
        Assert.Contains("FN:Marie-Rose Molhan", after.VCardRaw!);
        Assert.Null(after.DisplayName);      // le FN vaut le surnom : l'écrivain, pas l'utilisateur
        Assert.DoesNotContain('?', after.VCardRaw!);
    }

    private static async Task<(int, int)> Run(string db)
    {
        var outcome = await CreateStore(db).BackfillAsync(1, CancellationToken.None);
        return (outcome.Processed, outcome.Remaining);
    }
}

# Contacts 4c-ii-c — l'écriture et la synchronisation incrémentale : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Les rapports de sous-agents dans le scratchpad sont préfixés `4c-ii-c-task-N-…`.

**Spec :** [`docs/superpowers/specs/2026-08-23-webmail-contacts-4c-carddav-design.md`](../specs/2026-08-23-webmail-contacts-4c-carddav-design.md) — toute décision citée ici (« décision N ») y renvoie. En cas de doute, la spec fait foi.

**Périmètre.** Dernier des trois plans qui composent 4c-ii :

| | Plan | État |
|---|---|---|
| a | [le socle de synchronisation](2026-08-24-webmail-contacts-4c-ii-a-sync-foundation.md) | écrit, **à exécuter en premier** |
| b | [le serveur DAV en lecture](2026-08-24-webmail-contacts-4c-ii-b-dav-read.md) | écrit, **à exécuter en second** |
| **c** | **ce document** — l'écriture et la synchro incrémentale | — |

**Goal :** qu'un client CardDAV écrive, supprime, interroge par filtre et se resynchronise de façon incrémentale — c'est-à-dire que le carnet devienne bidirectionnel et que 4d ait quelque chose à mettre à l'épreuve.

**Architecture :** un jeton opaque `http://weesky.net/ns/sync/{epoch}/{seq}` analysé et refusé au bord, un `sync-collection` qui **lit le compteur avant les lignes**, un évaluateur de filtre qui parse la carte plutôt que de se limiter aux colonnes, et un `PUT` qui se branche sur la troisième porte d'écriture de 4a — `VCardProjector` → `ReplaceProjectionAsync` — sans dupliquer une seule règle métier.

**Tech stack :** .NET 10, ASP.NET Core, EF Core, `System.Xml`, l'analyseur et le composeur vCard de 4a, xUnit 2.9.3, Moq 4.20.72.

## Ce que ce plan suppose fait

**Les plans a et b, dans cet ordre, et le rattrapage du plan a confirmé à zéro ligne restante.** C'est le plan qui ouvre l'écriture : un client qui écrit dans un carnet incomplet y crée des doublons de ses propres fiches, et le rattrapage ne les distinguera plus.

De 4c-i : le schéma `CardDav`, sa politique nommée, `AuthAttemptThrottle`, `Dav:PublicUrl`.
De a : `contact_sync_state`, `contact_tombstones`, `contact_revisions`, `IContactSyncStore`, `ContactStore` transactionnel, `EntityTagMatcher`.
De b : `DavPaths`, `DavXml`, `DavXmlReader`, `DavError`, `MultiStatusWriter`, `DavProperties`, `DavSyncToken` (émission), `IDavContactReader`, `AddressDataFilter`, `VCardVersionConverter`, `CardDavController`.

## Global constraints

- Backend : `cd src && dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés) ; `cd src && dotnet build` doit rester à zéro avertissement.
- `src/snoopy.microservice/ApiDocumentation.xml` : le réverter avant chaque commit.
- `Assert.IsType<T>` vérifie le type **exact**.
- Style C# : file-scoped namespaces, un type par fichier, constructeurs primaires, records pour les DTO, `sealed`, `internal` par défaut, `CancellationToken` sur tout `async`, `ILogger` structuré.
- **Toute route `/dav` porte `[Authorize(Policy = CardDavAuthenticationDefaults.PolicyName)]`, jamais un `[Authorize]` nu.** Aucun test ne peut l'attraper ; seule la relecture le peut.
- **Un élément XML se reconnaît à son espace de noms et à son nom local, jamais à son préfixe.**
- **Aucune réponse de ce plan n'est un `500`.** C'est le plan où la règle coûte le plus : chaque plafond du store, chaque violation d'index, chaque attente de verrou a sa traduction, et une seule oubliée est un client qui boucle sur la même carte à chaque cycle.
- **Les `href` sont des chemins absolus** ; la collection porte sa barre finale, une carte jamais.
- **Le secret n'est jamais journalisé** ; l'utilisateur, dans un journal, est le GUID du principal.
- Commits : concis, sujet + ligne vide + corps de 2 lignes max, jamais commencer ni finir par `@`, terminer par `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`. **Ne jamais écrire un message de commit avec un here-string PowerShell dans l'outil Bash** — utiliser `git commit -F -` avec un heredoc.

## Valeurs fixées une fois, à ne pas réinventer

| Constante | Valeur | Où |
|---|---|---|
| Préfixe du jeton | `http://weesky.net/ns/sync/` | `DavSyncToken.Prefix` (posé au plan b) |
| Forme du jeton | `{préfixe}{epoch}/{seq}` | `DavSyncToken` |
| Forme du ctag | `{epoch}:{seq}` | `DavSyncToken` |
| Ctag d'un carnet sans ligne d'état | `0` nu | `DavSyncToken.Ctag(null)` |
| Collations annoncées | `i;ascii-casemap` et `i;unicode-casemap` | `DavCollation` |
| Collation par défaut | `i;unicode-casemap` — **et `default` la vaut aussi** | `DavCollation` |
| `match-type` par défaut | `contains` | `TextMatch` |
| `test` par défaut, sur `filter` comme sur `prop-filter` | `anyof` | `AddressBookFilter` |
| Bornes de rapport | `DAV:limit` pour `sync-collection`, **`CARDDAV:limit` pour `addressbook-query`** | deux espaces de noms, même nom local |
| Codes de succès | `PUT` : `201` créé / `204` remplacé · `DELETE` : `204` · rapports : `207` | — |

## Les deux ordres de lecture qui ne sont pas des préférences

Ils portent tout ce plan, et une inversion se paie par une **perte de données définitive, sans
erreur et sans trace**.

**1. `sync-collection` lit `seq` AVANT les lignes**, puis les fiches et les tombes de
`sync_sequence > n` **et `≤ seq`**, et rend `seq` comme nouveau jeton. Lire les lignes d'abord et le
compteur ensuite laisserait une écriture validée entre les deux être **couverte par le jeton rendu
sans figurer dans la réponse** : le client la croirait vue, ne la redemanderait jamais, et la fiche
manquerait pour toujours. Dans l'ordre retenu, la même écriture concurrente est simplement rendue au
tour suivant ; au pire un client reçoit deux fois une fiche qu'il a déjà, ce qu'un ETag inchangé lui
fait ignorer.

**2. `PROPFIND Depth: 1` sur le carnet suit la MÊME règle, et c'est le chemin qu'on oublie.** Le
raisonnement semble propre au rapport de synchronisation ; il ne l'est pas. Le chemin de repli sans
`sync-collection` lit l'état (`getctag`) puis la liste des membres, et tient le ctag pour couvrant
la liste jusqu'à l'interrogation suivante — DAVx⁵ le fait en deux `PROPFIND` distincts, état
d'abord. Lire les membres puis le compteur y produit exactement la même perte. **La tâche 3 corrige
donc le `PROPFIND` que le plan b a écrit**, et ce n'est pas un défaut du plan b : le compteur n'y
était pas encore lu.

## Découpage en paquets

| | Paquet | Tâches | Vérifiable par |
|---|---|---|---|
| 1 | Le jeton et la synchro incrémentale | 1–3 | la suite .NET ; un client se resynchronise |
| 2 | Le filtre et son rapport | 4–5 | la suite .NET |
| 3 | L'écriture | 6–8 | la suite .NET ; **le carnet devient bidirectionnel** |
| 4 | La traduction au bord et les deux coutures léguées | 9–10 | la suite .NET |

---

## Paquet 1 — le jeton et la synchronisation incrémentale

### Task 1 : lire un jeton, et refuser tout ce qu'on n'a pas émis

Le plan b **émet** `getctag` et `sync-token` ; ce plan les **lit**. Quatre refus, tous répondant
`403 valid-sync-token`, et chacun ferme un mode de défaillance différent :

| Jeton | Pourquoi le refus |
|---|---|
| `n ≤ pruned_below` | les tombes de `(n, P]` n'existent plus : l'accepter **omettrait une suppression sans que rien ne le signale**, et le client garderait la fiche pour toujours |
| `n > seq` | restauration sur une sauvegarde plus ancienne, carnet recréé, client venu d'un autre serveur. L'accepter rendrait une réponse vide, **que le client lit comme « rien n'a changé » sur un carnet qui a tout changé** |
| epoch étrangère | c'est exactement ce que la rotation d'epoch du plan a existe pour produire : tous les jetons de la base d'avant deviennent étrangers |
| forme autre | mauvais préfixe, partie numérique non entière, débordement. **Il n'y a rien à comprendre dans un jeton qu'on n'a pas émis** |

**Et deux formes valent « tout le carnet » plutôt qu'un refus :** un `<DAV:sync-token/>` **vide** —
la forme canonique de la synchro initiale, la seule que le RFC définisse, et ce que DAVx⁵ écrit
littéralement au premier appairage — et un jeton **absent**. Le second est notre tolérance et non
celle du RFC : une requête sans `sync-token` est invalide au sens strict, mais la refuser rendrait
`403` sur le premier geste d'un client approximatif — **un carnet qui refuse de s'appairer**. Le
laisser tomber dans « jeton syntaxiquement autre » serait la même erreur.

**Files :**
- Modify : `src/snoopy.microservice/Services/CardDav/DavSyncToken.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/CardDav/DavSyncTokenParseTests.cs`

**Interfaces :**
- Consomme : `SyncState` (plan a), `DavSyncToken.Token`/`Ctag` (plan b).
- Produit, consommé par les tâches 2 et 3 :

```csharp
/// What a request's sync-token resolved to. `Initial` means "the whole book, no tombstones" — the
/// canonical shape of a first sync, and also what an absent token is treated as.
internal enum SyncTokenKind { Initial, Sequence, Invalid }

internal sealed record SyncTokenRead(SyncTokenKind Kind, ulong Sequence);

internal static partial class DavSyncToken
{
    internal const string Prefix = "http://weesky.net/ns/sync/";

    /// Reads the token element of a sync-collection body against the book's state. Never throws:
    /// an unreadable token is a refusal to write, not an exception to catch.
    internal static SyncTokenRead Read(XElement? tokenElement, SyncState state);
}
```

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    private static readonly Guid Epoch = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static SyncState State(ulong seq = 100, ulong pruned = 10) => new(Epoch, seq, pruned);

    [Fact]
    public void AnEmptyTokenElement_MeansTheWholeBook()
    {
        var read = DavSyncToken.Read(new XElement(DavXml.Dav + "sync-token"), State());

        // The canonical shape of an initial sync — the only one the RFC defines — and what DAVx5
        // writes literally at first pairing.
        Assert.Equal(SyncTokenKind.Initial, read.Kind);
    }

    [Fact]
    public void AnAbsentTokenElement_IsTreatedTheSameWay()
    {
        var read = DavSyncToken.Read(null, State());

        // Our tolerance, not the RFC's: strictly speaking the request is invalid, but refusing it
        // would answer 403 on an approximate client's first gesture — a book that refuses to pair.
        Assert.Equal(SyncTokenKind.Initial, read.Kind);
    }

    [Fact]
    public void AWellFormedTokenInRange_IsRead()
    {
        var read = DavSyncToken.Read(TokenElement($"{DavSyncToken.Prefix}{Epoch}/42"), State());

        Assert.Equal(new SyncTokenRead(SyncTokenKind.Sequence, 42), read);
    }

    [Fact]
    public void ATokenAtTheWatermark_IsRefused()
    {
        // The spec refuses from n <= P rather than n < P: one extra rank resynchronised from scratch
        // costs nothing, and a conservative comparison stays right if pruning ever changes bound.
        var read = DavSyncToken.Read(TokenElement($"{DavSyncToken.Prefix}{Epoch}/10"), State(pruned: 10));

        Assert.Equal(SyncTokenKind.Invalid, read.Kind);
    }

    [Fact]
    public void ATokenBelowTheWatermark_IsRefused()
    {
        // The tombstones of (n, P] are gone: accepting would omit a deletion with nothing to signal
        // it, and the client would keep the card for ever.
        var read = DavSyncToken.Read(TokenElement($"{DavSyncToken.Prefix}{Epoch}/4"), State(pruned: 10));

        Assert.Equal(SyncTokenKind.Invalid, read.Kind);
    }

    [Fact]
    public void ATokenAheadOfTheCounter_IsRefused()
    {
        // A restore onto an older backup, a recreated book, a client that served against another
        // server. Accepting would answer empty — which the client reads as "nothing changed" on a
        // book that changed everything.
        var read = DavSyncToken.Read(TokenElement($"{DavSyncToken.Prefix}{Epoch}/500"), State(seq: 100));

        Assert.Equal(SyncTokenKind.Invalid, read.Kind);
    }

    [Fact]
    public void ATokenOfAnotherEpoch_IsRefused()
    {
        // Exactly what the plan-a epoch rotation exists to produce.
        var other = Guid.NewGuid();

        var read = DavSyncToken.Read(TokenElement($"{DavSyncToken.Prefix}{other}/42"), State());

        Assert.Equal(SyncTokenKind.Invalid, read.Kind);
    }

    [Theory]
    [InlineData("http://sabre.io/ns/sync/42")]
    [InlineData("urn:snoopy:42")]
    [InlineData("http://weesky.net/ns/sync/22222222-2222-2222-2222-222222222222/abc")]
    [InlineData("http://weesky.net/ns/sync/not-a-guid/42")]
    [InlineData("http://weesky.net/ns/sync/22222222-2222-2222-2222-222222222222/-1")]
    [InlineData("http://weesky.net/ns/sync/22222222-2222-2222-2222-222222222222/99999999999999999999999")]
    [InlineData("http://weesky.net/ns/sync/22222222-2222-2222-2222-222222222222")]
    [InlineData("  ")]
    public void ATokenOfAnotherShape_IsRefused(string token)
    {
        // There is nothing to understand in a token we did not issue. The overflow case is in the
        // list on purpose: ulong.Parse throws, and an exception here would be a 500 on a header a
        // client controls.
        Assert.Equal(SyncTokenKind.Invalid, DavSyncToken.Read(TokenElement(token), State()).Kind);
    }

    [Fact]
    public void ATokenOfZero_IsReadAndNotTreatedAsInitial()
    {
        // Zero is a legitimate sequence for a book that has a state row and no write yet. Folding it
        // into Initial would be indistinguishable in effect today and wrong the day it is not.
        var read = DavSyncToken.Read(
            TokenElement($"{DavSyncToken.Prefix}{Epoch}/0"), State(seq: 5, pruned: 0));

        Assert.Equal(new SyncTokenRead(SyncTokenKind.Sequence, 0), read);
    }

    [Fact]
    public void ReadingNeverThrows()
    {
        // Stated as a test because it is the property that keeps a client-controlled value out of
        // the 500 column: an unreadable token is a refusal to write, not an exception to catch.
        var exception = Record.Exception(() =>
            DavSyncToken.Read(TokenElement("\u0000￿"), State()));

        Assert.Null(exception);
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavSyncTokenParseTests`
Expected : ne compile pas — `Read` n'existe pas.

- [ ] **Step 3 : Écrire `Read`**

L'ordre des contrôles importe peu ici — tous rendent le même refus — mais **aucun ne doit lever** :
`ulong.TryParse`, `Guid.TryParse`, et une comparaison de préfixe ordinale.

Le fichier devient `partial` pour que l'émission (plan b) et la lecture (ce plan) vivent dans le
même type sans qu'un plan écrase le fichier de l'autre — ou, si le plan b l'a écrit non-partial,
ajouter simplement les membres au fichier existant.

- [ ] **Step 4 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 5 : Commit**

- sujet : `feat(dav): lire un jeton de synchro, et refuser ce qu'on n'a pas emis`
- corps : `Un jeton vide ou absent vaut tout le carnet ; les quatre autres refus` /
  `repondent 403 valid-sync-token et ferment chacun un mode de defaillance.`

---

### Task 2 : `sync-collection` — le compteur d'abord, les tombes ensuite, la coupe au rang près

**Files :**
- Create : `src/snoopy.microservice/Services/CardDav/SyncCollectionReport.cs`
- Modify : `src/snoopy.microservice/Repositories/IDavContactReader.cs`
- Modify : `src/snoopy.microservice/Repositories/DavContactReader.cs`
- Modify : `src/snoopy.microservice/Services/CardDav/ReportRequest.cs`
- Modify : `src/snoopy.microservice/Controllers/CardDavController.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CardDavSyncCollectionTests.cs`

**Interfaces :**
- Consomme : `SyncTokenRead` (tâche 1), `MultiStatusWriter`, `DavProperties`.
- Produit :

```csharp
// s'ajoutent à IDavContactReader :
/// Cards whose rank is in (after, upTo]. The upper bound is what makes the answer honest when the
/// rows are not read in the same transaction as the counter.
IAsyncEnumerable<DavCard> ChangedAsync(Guid userId, ulong after, ulong upTo, CancellationToken ct);

/// Tombstones in the same window, ordered by rank so a truncation can cut on a rank boundary.
Task<IReadOnlyList<ContactTombstone>> TombstonesAsync(Guid userId, ulong after, ulong upTo, CancellationToken ct);
```

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Fact]
    public async Task AnInitialSync_AnswersTheWholeBookAndNoTombstone()
    {
        GivenCards("a.vcf", "b.vcf");
        GivenATombstone("gone.vcf", rank: 3);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(token: null));

        // An empty token means the whole book and no tombstones: the book is authoritative on what
        // it holds, and cards absent from the initial answer are what the client must forget.
        Assert.Equal(2, ResponsesOfStatus(response, 200).Count);
        Assert.Empty(ResponsesOfStatus(response, 404));
    }

    [Fact]
    public async Task AnIncrementalSync_AnswersOnlyWhatMovedSince()
    {
        GivenCardAtRank("a.vcf", 5);
        GivenCardAtRank("b.vcf", 12);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(8)));

        Assert.Single(HrefsOf(response).Where(h => h.EndsWith("b.vcf")));
        Assert.DoesNotContain(HrefsOf(response), h => h.EndsWith("a.vcf"));
    }

    [Fact]
    public async Task ATombstoneInTheWindow_ComesBackAs404()
    {
        GivenATombstone("gone.vcf", rank: 12);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(8)));

        var gone = ResponsesOfStatus(response, 404).Single();
        // A direct child of its response, never lodged in a propstat.
        Assert.Single(gone.Elements(DavXml.Dav + "status"));
        Assert.Empty(gone.Elements(DavXml.Dav + "propstat"));
    }

    [Fact]
    public async Task ItServesThePropertiesTheRequestAsked_AndNotAHardCodedEtag()
    {
        GivenCardAtRank("a.vcf", 12);

        var response = await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(8), props: ["getetag", "resourcetype"]));

        // DAVx5 asks for getetag AND resourcetype, and uses the second to rule out sub-collections.
        var body = await response.ReadAsync();
        Assert.Contains("getetag", body);
        Assert.Contains("resourcetype", body);
    }

    [Fact]
    public async Task AddressDataInASyncCollection_ComesBackAs404_AndThatIsAChoice()
    {
        GivenCardAtRank("a.vcf", 12);

        var response = await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(8), carddavProps: ["address-data"]));

        // RFC 6352 § 10.4 defines address-data only in query and multiget. Serving it here would put
        // on the sync report the weight decision 15 spares it — a batch of five hundred 1 MB cards.
        // Thunderbird tries it and chains a multiget when the property is missing from the propstat.
        var propstat404 = XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "propstat")
            .Single(p => p.Element(DavXml.Dav + "status")!.Value.Contains("404"));
        Assert.Single(propstat404.Descendants(DavXml.CardDav + "address-data"));
    }

    [Fact]
    public async Task TheNewTokenIsTheCounterReadBeforeTheRows()
    {
        GivenCardAtRank("a.vcf", 12);
        GivenTheCounterAt(20);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(8)));

        // Reading the rows first and the counter after would let a write committed in between be
        // COVERED by the returned token without appearing in the answer: the client would believe it
        // seen, never ask again, and the card would be missing for ever — no error, no trace.
        Assert.Equal(DavSyncToken.Token(new SyncState(Epoch, 20, 0)), NewTokenOf(response));
    }

    [Fact]
    public async Task ARowWrittenAfterTheCounterWasRead_IsNotServedUnderTheReturnedToken()
    {
        GivenTheCounterAt(20);
        GivenCardAtRank("late.vcf", 25);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(8)));

        // The `<= seq` upper bound is what makes the claim true even when the rows are not read in
        // the same transaction as the counter. At worst the client gets it next round.
        Assert.DoesNotContain(HrefsOf(response), h => h.EndsWith("late.vcf"));
    }

    [Fact]
    public async Task ARefusedToken_Answers403ValidSyncToken()
    {
        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(2), pruned: 10));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "valid-sync-token", ConditionOf(response));
    }

    [Fact]
    public async Task ALimit_TruncatesOnARankBoundaryAndSaysSo()
    {
        GivenCardsAtRank(rank: 10, count: 3);
        GivenCardsAtRank(rank: 11, count: 3);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(0), limit: 4));

        // The cut cannot fall in the middle of a rank: a batch carries several rows at one sequence,
        // and returning token n after serving only part of rank n would abandon the rest for ever.
        // Whole ranks while the count stays under the bound, then the token of the last COMPLETE one.
        Assert.Equal(3, ResponsesOfStatus(response, 200).Count);
        Assert.Equal(DavSyncToken.Token(new SyncState(Epoch, 10, 0)), NewTokenOf(response));
        Assert.Single(ResponsesOfStatus(response, 507));
    }

    [Fact]
    public async Task ASingleRankBiggerThanTheLimit_IsServedWhole()
    {
        GivenCardsAtRank(rank: 10, count: 5);

        var response = await Report(DavPaths.Collection(UserId), SyncBody(TokenAt(0), limit: 2));

        // Exceeding the requested bound is an inconvenience; losing half of a rank is data loss.
        Assert.Equal(5, ResponsesOfStatus(response, 200).Count);
    }

    [Fact]
    public async Task TheLimitIsReadInTheDavNamespaceAndNotTheCarddavOne()
    {
        GivenCardsAtRank(rank: 10, count: 3);

        var response = await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(0), carddavLimit: 1));

        // Both exist and share a local name: RFC 6578 § 3.6 defines this one in DAV:, RFC 6352 § 10.6
        // defines addressbook-query's in the carddav namespace. A CARDDAV:limit here is not ours.
        Assert.Equal(3, ResponsesOfStatus(response, 200).Count);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("infinite")]
    public async Task AValidSyncLevel_IsAccepted(string level) =>
        Assert.Equal(207, (await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(0), syncLevel: level))).StatusCode);

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("infinity")]
    public async Task AnAbsentSyncLevel_FallsBackOnAnyDepthHeader(string depth)
    {
        var response = await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(0), syncLevel: null), depth: depth);

        // Appendix A's fallback, read wider than the letter on purpose: taken literally, § 3's
        // "Depth: 0" plus appendix A refuses with 400 the client that set the CONFORMING header and
        // forgot the one element the RFC introduced to replace it — punishing the closest to the norm
        // on its very first request.
        Assert.Equal(207, response.StatusCode);
    }

    [Fact]
    public async Task AnAbsentSyncLevelAndNoDepthAtAll_Answers400()
    {
        var response = await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(0), syncLevel: null), depth: null);

        // Nothing left to convert.
        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task ASyncLevelOfAnotherValue_Answers400()
    {
        var response = await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(0), syncLevel: "2"));

        // Accepting would be guessing what the client meant, on the report where one can least
        // afford it.
        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task ADepthHeaderOtherThanZero_IsIgnoredRatherThanRefused()
    {
        var response = await Report(DavPaths.Collection(UserId),
            SyncBody(TokenAt(0), syncLevel: "1"), depth: "1");

        // § 3 says literally that any other value gives a 400. We do not, and sabre does not either:
        // refusing a Depth: 1 a client set out of habit buys nothing but a book that will not pair.
        // Named divergence for 4d, not a discovery.
        Assert.Equal(207, response.StatusCode);
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~CardDavSyncCollectionTests`
Expected : `403 supported-report` partout — c'est le refus que le plan b a posé.

- [ ] **Step 3 : Étendre `IDavContactReader`**

`ChangedAsync` et `TombstonesAsync`, toutes deux bornées **des deux côtés** et **ordonnées par
rang** — l'ordre est ce qui rend la coupe au rang près possible, et une requête non ordonnée le
rendrait faux d'une façon qu'aucun test à petit volume ne montrerait.

La clause de visibilité s'applique à `ChangedAsync` comme aux autres lectures.

- [ ] **Step 4 : Écrire `SyncCollectionReport`**

L'ordre du corps de la méthode **est** la décision :

1. lire l'état (`ReadOrCreateStateAsync` — un carnet vide a besoin d'une epoch pour former son
   jeton) ;
2. lire le jeton contre cet état ; `Invalid` → `403 valid-sync-token`, et **rien d'autre n'est
   lu** ;
3. lire `sync-level`, avec le repli sur `Depth` ;
4. **seulement ensuite** parcourir les fiches puis les tombes de la fenêtre ;
5. écrire le jeton du dernier rang complet servi.

- [ ] **Step 5 : Câbler la branche `SyncCollection`**

Dans le `switch` du contrôleur, retirer `SyncCollection` de la branche qui rend
`403 supported-report`. **Ne pas toucher à `Query`** — c'est la tâche 5.

- [ ] **Step 6 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 7 : Commit**

- sujet : `feat(dav): sync-collection, le compteur lu avant les lignes`
- corps : `L'ordre inverse fait couvrir par le jeton rendu une ecriture absente de la` /
  `reponse : le client la croit vue et ne la redemande jamais.`

---

### Task 3 : `PROPFIND Depth: 1` lit `seq` d'abord — le chemin qu'on oublie

**Ce n'est pas un défaut du plan b** : le compteur n'y était pas encore lu, et la borne n'avait rien
à borner. C'est ici que les deux moitiés de la réponse doivent devenir cohérentes entre elles.

Le chemin de repli sans `sync-collection` lit l'état (`getctag`) puis la liste des membres, et
**tient le ctag pour couvrant la liste jusqu'à l'interrogation suivante**. Lire les membres puis le
compteur y produit exactement la perte de la tâche 2 : une écriture validée entre les deux est
couverte par le ctag rendu sans figurer dans la liste, le client la croit vue et ne la redemande
jamais.

**La borne ne coûte rien** — toute fiche satisfait déjà `sync_sequence ≤ seq` par construction — et
elle est ce qui rend l'affirmation vraie.

**Files :**
- Modify : `src/snoopy.microservice/Controllers/CardDavController.cs`
- Modify : `src/snoopy.microservice/Repositories/IDavContactReader.cs`
- Modify : `src/snoopy.microservice/Repositories/DavContactReader.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CardDavPropfindTests.cs`

**Interfaces :**

```csharp
// StreamAsync gagne sa borne haute, et l'ancienne signature disparaît plutôt que de coexister :
// deux surcharges dont l'une est fausse est la façon dont on appelle la fausse.
IAsyncEnumerable<DavCard> StreamAsync(Guid userId, ulong upTo, CancellationToken cancellationToken);
```

- [ ] **Step 1 : Écrire les tests, rouges**

Ajouter à `CardDavPropfindTests` :

```csharp
    [Fact]
    public async Task DepthOne_BoundsItsMembersToTheCounterItRead()
    {
        GivenTheCounterAt(20);
        GivenCardAtRank("late.vcf", 25);
        GivenCardAtRank("a.vcf", 5);

        var response = await Propfind(DavPaths.Collection(UserId), depth: "1", body: PropBody("getetag"));

        // The forgotten path: DAVx5 reads the state then the member list in two separate PROPFINDs
        // and holds the ctag as covering the list. A row committed in between would be covered by the
        // returned ctag without appearing in the list.
        Assert.DoesNotContain(HrefsOf(response), h => h.EndsWith("late.vcf"));
        Assert.Contains(HrefsOf(response), h => h.EndsWith("a.vcf"));
    }

    [Fact]
    public async Task DepthOne_ReturnsTheSameCounterItBoundedWith()
    {
        GivenTheCounterAt(20);
        GivenCardAtRank("a.vcf", 5);

        var response = await Propfind(DavPaths.Collection(UserId), depth: "1",
            body: PropBody("getctag", DavXml.CalendarServer));

        // The two halves of the answer must be coherent with each other: the ctag returned is the
        // one the member list was bounded by, never a second read.
        Assert.Equal(DavSyncToken.Ctag(new SyncState(Epoch, 20, 0)), CtagOf(response));
    }

    [Fact]
    public async Task DepthZero_ReadsTheCounterOnceToo()
    {
        GivenTheCounterAt(20);

        var response = await Propfind(DavPaths.Collection(UserId), depth: "0",
            body: PropBody("getctag", DavXml.CalendarServer));

        Assert.Equal(DavSyncToken.Ctag(new SyncState(Epoch, 20, 0)), CtagOf(response));
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~CardDavPropfindTests`
Expected : les trois nouveaux cas FAIL.

- [ ] **Step 3 : Borner `StreamAsync`**

Remplacer la signature, **sans garder l'ancienne** : deux surcharges dont l'une est fausse est
exactement la façon dont on finit par appeler la fausse. Le compilateur nomme alors tous les sites
d'appel à corriger, ce qui est le but.

- [ ] **Step 4 : Lire l'état d'abord dans le `PROPFIND` de collection**

Un seul `ReadStateAsync` en tête d'action, dont la valeur sert **et** au `getctag`/`sync-token` du
jeu de propriétés **et** à la borne des membres. Écrire dans le code le commentaire qui dit que
c'est un ordre et non une préférence.

- [ ] **Step 5 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 6 : Commit**

- sujet : `fix(dav): PROPFIND Depth 1 borne ses membres au compteur qu'il a lu`
- corps : `Le chemin de repli sans sync-collection tient le ctag pour couvrant la` /
  `liste : lire les membres en premier y perd la meme ecriture.`

---

## Paquet 2 — le filtre et son rapport

### Task 4 : `AddressBookFilter` — évaluer sur la carte, pré-filtrer sur les colonnes

**Répondre « tout le carnet » à un filtre incompris a l'apparence du succès et donne au client un
jeu de résultats faux, qu'il inscrira dans son cache.** Un refus explicite le fait basculer sur un
listing complet, qu'il sait faire. D'où : un filtre non évaluable répond `403 supported-filter`.

**Mais le refus doit rester rare, et c'est la carte qui l'évalue — pas les colonnes.** Restreindre
l'évaluation aux colonnes projetées ferait répondre `403` à des filtres parfaitement ordinaires : un
`prop-filter` sur `NICKNAME`, sur `TITLE`, sur `CATEGORIES`, un `param-filter` sur `TYPE`. Or le
carnet détient `vcard_raw` et 4a en fournit l'analyseur. **Les colonnes projetées gardent leur rôle,
mais comme pré-filtre indexé — jamais comme frontière de ce qu'on sait comprendre.** Un carnet de
5000 fiches parsé en entier est un dernier recours acceptable ; un `403` sur `TITLE` ne l'est pas.

**Ce qui est évalué, et le reste refusé :**

| Élément | Traitement |
|---|---|
| `CARDDAV:filter/@test` | `anyof` (défaut) et `allof` |
| `CARDDAV:prop-filter/@name` | toute propriété de la carte, **insensible à la casse** ; sans préfixe de groupe, matche la propriété nue **et** groupée — `TEL` matche `item1.TEL`, un MUST du § 10.5.1 que les cartes iOS exercent partout |
| `CARDDAV:prop-filter/@test` | `anyof` (défaut) et `allof` |
| `CARDDAV:is-not-defined` | évalué, dans `prop-filter` comme dans `param-filter` |
| `CARDDAV:param-filter/@name` | évalué sur les paramètres de la propriété retenue |
| `CARDDAV:text-match/@match-type` | `contains` (défaut), `equals`, `starts-with`, `ends-with` |
| `CARDDAV:text-match/@negate-condition` | `yes` et `no` (défaut) |
| `CARDDAV:text-match/@collation` | les deux annoncées ; toute autre → **`403 supported-collation`**, pas `supported-filter` |
| tout autre élément dans `filter` | `403 supported-filter` |

**Une propriété absente de la carte fait échouer son `prop-filter` sans erreur** : c'est un filtre
qui ne retient rien, pas un filtre qu'on ne comprend pas. La distinction est ce qui fait que
`403 supported-filter` reste un **signal** et non le code de retour ordinaire du rapport.

**Les deux collations sont deux comparaisons, pas une.** `i;ascii-casemap` ne replie que les lettres
ASCII — « É » et « é » y sont **différents** — quand `i;unicode-casemap` replie et décompose tout
Unicode. Une unique comparaison insensible à la casse mentirait pour l'une des deux **sur chaque
lettre accentuée**. Le pré-filtre SQL reste licite parce qu'il ne fait que **sur**-sélectionner :
la comparaison SQL insensible retient au moins tout ce que l'une ou l'autre retiendrait, et
l'évaluation exacte tranche. **L'attribut absent, ou l'identifiant littéral `default`, valent
`i;unicode-casemap`** — le § 8.3 l'impose, et `default` tombé dans « collation inconnue » serait un
refus à tort **garanti** sur un attribut conforme.

**Files :**
- Create : `src/snoopy.microservice/Services/CardDav/AddressBookFilter.cs`
- Create : `src/snoopy.microservice/Services/CardDav/DavCollation.cs`
- Create : `src/snoopy.microservice/Services/CardDav/FilterPrefilter.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/CardDav/AddressBookFilterTests.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/CardDav/DavCollationTests.cs`

**Interfaces :**
- Produit, consommé par la tâche 5 :

```csharp
internal static class DavCollation
{
    internal const string AsciiCasemap = "i;ascii-casemap";
    internal const string UnicodeCasemap = "i;unicode-casemap";

    /// The comparison an attribute names. An absent attribute and the literal `default` both mean
    /// i;unicode-casemap (RFC 6352 § 8.3, a MUST). Throws DavPreconditionException
    /// (supported-collation) on anything else — the client must know whether its FILTER or its
    /// COLLATION is at fault.
    internal static StringComparer Resolve(string? attribute);
}

internal static class AddressBookFilter
{
    /// Parses the filter, or throws DavPreconditionException(supported-filter) on anything the
    /// table above does not name. A filter WITH NO CHILDREN is a special case: the whole book.
    internal static AddressBookFilterSpec Parse(XElement filter);

    /// Whether one parsed card satisfies the filter.
    internal static bool Matches(string vCardRaw, AddressBookFilterSpec spec);
}

internal static class FilterPrefilter
{
    /// A SQL clause that OVER-selects: it never drops a card the exact evaluation would keep. Null
    /// when the filter gives nothing indexable, in which case the whole book is parsed.
    internal static Expression<Func<Contact, bool>>? For(AddressBookFilterSpec spec);
}
```

- [ ] **Step 1 : Écrire les tests des collations, rouges**

```csharp
    [Fact]
    public void AsciiCasemap_FoldsAsciiOnly()
    {
        var comparer = DavCollation.Resolve(DavCollation.AsciiCasemap);

        Assert.Equal(0, comparer.Compare("ADA", "ada"));
        // "É" and "é" are DIFFERENT under i;ascii-casemap (RFC 4790 § 9.2.1). One
        // case-insensitive comparison for both collations would lie for this one on every accent.
        Assert.NotEqual(0, comparer.Compare("ÉLÉONORE", "éléonore"));
    }

    [Fact]
    public void UnicodeCasemap_FoldsEverything()
    {
        var comparer = DavCollation.Resolve(DavCollation.UnicodeCasemap);

        Assert.Equal(0, comparer.Compare("ÉLÉONORE", "éléonore"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("default")]
    public void AnAbsentAttributeOrTheLiteralDefault_MeanUnicodeCasemap(string? attribute)
    {
        // § 8.3 imposes it, and `default` falling into "unknown collation" would be a guaranteed
        // wrongful refusal on a conforming attribute.
        Assert.Equal(0, DavCollation.Resolve(attribute).Compare("É", "é"));
    }

    [Fact]
    public void AnUnknownCollation_IsRefusedWithItsOwnCondition()
    {
        var thrown = Assert.Throws<DavPreconditionException>(() => DavCollation.Resolve("i;octet"));

        // supported-collation and not supported-filter: the client must know whether its filter or
        // its collation is at fault. sabre answers a 400 with no condition and Radicale ignores the
        // attribute; the RFC's MUST says otherwise.
        Assert.Equal(DavXml.CardDav + "supported-collation", thrown.Condition);
    }
```

- [ ] **Step 2 : Écrire les tests du filtre, rouges**

```csharp
    private const string Card =
        "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada Lovelace\r\nTITLE:Analyst\r\n" +
        "item1.TEL;TYPE=CELL:+3210\r\nEMAIL;TYPE=WORK:ada@weesky.be\r\nEND:VCARD\r\n";

    [Fact]
    public void AFilterWithNoChildren_MatchesTheWholeBook()
    {
        // Tricky because the general rule gives the WRONG answer here: anyof over zero tests is
        // false, so an empty <filter/> would keep nothing and the client would get an empty book
        // where it asked for everything. It is the shape several clients send for "give me what you
        // have", and sabre treats it so on its evaluator's first line.
        var spec = AddressBookFilter.Parse(FilterElement());

        Assert.True(AddressBookFilter.Matches(Card, spec));
    }

    [Fact]
    public void APropFilterOnAProjectedColumn_Matches() =>
        Assert.True(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("FN", TextMatch("lovelace")))));

    [Fact]
    public void APropFilterOnAPropertyWeDoNotProject_MatchesToo()
    {
        // Restricting evaluation to projected columns would answer 403 supported-filter to perfectly
        // ordinary filters. The book holds vcard_raw and 4a supplies the parser: this is what
        // separates a usable server from one that refuses half the requests it is sent.
        Assert.True(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("TITLE", TextMatch("analyst")))));
    }

    [Fact]
    public void APropFilterMatchesAGroupedProperty()
    {
        // A MUST of § 10.5.1 that iOS cards exercise everywhere: TEL matches item1.TEL.
        Assert.True(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("TEL", TextMatch("3210")))));
    }

    [Fact]
    public void APropertyNameIsCaseInsensitive() =>
        Assert.True(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("fn", TextMatch("Ada")))));

    [Fact]
    public void APropertyAbsentFromTheCard_FailsWithoutAnError()
    {
        // A filter that keeps nothing, not a filter we do not understand. The distinction is what
        // keeps 403 supported-filter a SIGNAL rather than the report's ordinary return code.
        Assert.False(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("NICKNAME", TextMatch("x")))));
    }

    [Fact]
    public void IsNotDefined_IsEvaluated()
    {
        Assert.True(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("NICKNAME", IsNotDefined()))));
        Assert.False(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("FN", IsNotDefined()))));
    }

    [Fact]
    public void AParamFilter_IsEvaluatedOnTheRetainedProperty()
    {
        Assert.True(AddressBookFilter.Matches(Card,
            ParseFilter(PropFilter("TEL", ParamFilter("TYPE", TextMatch("CELL"))))));
        Assert.False(AddressBookFilter.Matches(Card,
            ParseFilter(PropFilter("TEL", ParamFilter("TYPE", TextMatch("FAX"))))));
    }

    [Theory]
    [InlineData("contains", "ovela", true)]
    [InlineData("equals", "Ada Lovelace", true)]
    [InlineData("equals", "Ada", false)]
    [InlineData("starts-with", "Ada", true)]
    [InlineData("starts-with", "Lovelace", false)]
    [InlineData("ends-with", "Lovelace", true)]
    public void TheFourMatchTypes_AreEvaluated(string matchType, string value, bool expected) =>
        Assert.Equal(expected,
            AddressBookFilter.Matches(Card, ParseFilter(PropFilter("FN", TextMatch(value, matchType)))));

    [Fact]
    public void AnAbsentMatchType_MeansContains() =>
        Assert.True(AddressBookFilter.Matches(Card, ParseFilter(PropFilter("FN", TextMatch("ovela")))));

    [Fact]
    public void NegateCondition_Inverts()
    {
        Assert.False(AddressBookFilter.Matches(Card,
            ParseFilter(PropFilter("FN", TextMatch("Ada", negate: true)))));
        Assert.True(AddressBookFilter.Matches(Card,
            ParseFilter(PropFilter("FN", TextMatch("Grace", negate: true)))));
    }

    [Theory]
    [InlineData("anyof", true)]
    [InlineData("allof", false)]
    public void TheFilterTest_CombinesItsPropFilters(string test, bool expected)
    {
        var spec = ParseFilter(
            PropFilter("FN", TextMatch("Ada")),
            PropFilter("NICKNAME", TextMatch("nope")),
            test: test);

        Assert.Equal(expected, AddressBookFilter.Matches(Card, spec));
    }

    [Fact]
    public void AnAbsentTest_MeansAnyof() =>
        Assert.True(AddressBookFilter.Matches(Card,
            ParseFilter(PropFilter("FN", TextMatch("Ada")), PropFilter("NICKNAME", TextMatch("nope")))));

    [Theory]
    [InlineData("anyof", true)]
    [InlineData("allof", false)]
    public void ThePropFilterTest_CombinesItsOwnChildren(string test, bool expected)
    {
        var spec = ParseFilter(PropFilter("FN",
            test: test, TextMatch("Ada"), TextMatch("Grace")));

        Assert.Equal(expected, AddressBookFilter.Matches(Card, spec));
    }

    [Theory]
    [InlineData("comp-filter")]
    [InlineData("time-range")]
    [InlineData("some-vendor-extension")]
    public void AnythingTheTableDoesNotName_IsRefused(string localName)
    {
        var thrown = Assert.Throws<DavPreconditionException>(() =>
            AddressBookFilter.Parse(FilterElement(new XElement(DavXml.CardDav + localName))));

        // Answering "the whole book" to a filter we do not understand looks like success and hands
        // the client a FALSE result set, which it writes into its cache.
        Assert.Equal(DavXml.CardDav + "supported-filter", thrown.Condition);
    }

    [Fact]
    public void AnUnknownCollationInATextMatch_IsRefusedWithTheCollationCondition()
    {
        var thrown = Assert.Throws<DavPreconditionException>(() =>
            AddressBookFilter.Parse(FilterElement(PropFilter("FN", TextMatch("x", collation: "i;octet")))));

        Assert.Equal(DavXml.CardDav + "supported-collation", thrown.Condition);
    }

    [Fact]
    public void ThePrefilterOnlyEverOverSelects()
    {
        // The property that makes the prefilter safe: it never drops a card the exact evaluation
        // would keep. A prop-filter on TITLE is not indexable, so the prefilter answers null and the
        // whole book is parsed — a last resort, and an acceptable one.
        Assert.Null(FilterPrefilter.For(ParseFilter(PropFilter("TITLE", TextMatch("x")))));
        Assert.NotNull(FilterPrefilter.For(ParseFilter(PropFilter("FN", TextMatch("Ada")))));
    }

    [Fact]
    public void ThePrefilterIsNullUnderAnyofWithOneUnindexableBranch()
    {
        // anyof means a card may match through the unindexable branch alone: a prefilter built from
        // the indexable branch would DROP it. Under allof the same clause is safe, since every branch
        // must hold.
        Assert.Null(FilterPrefilter.For(ParseFilter(
            PropFilter("FN", TextMatch("Ada")), PropFilter("TITLE", TextMatch("x")), test: "anyof")));
        Assert.NotNull(FilterPrefilter.For(ParseFilter(
            PropFilter("FN", TextMatch("Ada")), PropFilter("TITLE", TextMatch("x")), test: "allof")));
    }
```

- [ ] **Step 3 : Lancer les deux fichiers pour les voir échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~AddressBookFilter|FullyQualifiedName~DavCollation"`
Expected : ne compile pas.

- [ ] **Step 4 : Écrire `DavCollation`**

`i;ascii-casemap` : un comparateur qui replie **uniquement** `A`–`Z`, en ordinal pour le reste.
`StringComparer.Ordinal` ne suffit pas et `OrdinalIgnoreCase` non plus — le premier ne replie rien,
le second replie l'Unicode. Écrire un `StringComparer` dédié, et l'épingler par le test sur « É ».

`i;unicode-casemap` : `StringComparer.InvariantCultureIgnoreCase` après une normalisation
`FormD`/`FormKD` selon ce que le test exige — **le vérifier, pas le supposer**.

- [ ] **Step 5 : Écrire `AddressBookFilter`**

Analyse en un `AddressBookFilterSpec` immuable, puis évaluation sur la carte parsée par
l'analyseur de 4a. Le nom d'une propriété se compare **après retrait du préfixe de groupe** —
réutiliser `VCardComposer.NameOf` plutôt que d'en réécrire une variante.

Le cas `filter` sans enfant se traite **en première ligne**, avant toute logique de `test` : c'est
un cas particulier, pas une conséquence de `anyof`, et l'écrire comme une conséquence est
exactement l'erreur.

- [ ] **Step 6 : Écrire `FilterPrefilter`**

Rendre `null` dès qu'une branche n'est pas indexable **sous `anyof`** ; sous `allof`, une seule
branche indexable suffit. C'est la seule subtilité, et le test la porte.

- [ ] **Step 7 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 8 : Commit**

- sujet : `feat(dav): le filtre s'evalue sur la carte, pas sur les colonnes`
- corps : `Les colonnes projetees ne sont qu'un pre-filtre indexe : un 403 sur TITLE` /
  `separe un serveur utilisable d'un serveur qui refuse la moitie des requetes.`

---

### Task 5 : `addressbook-query` — le rapport, sa borne, et ses deux `400`

**Deux règles se ressemblent et disent le contraire l'une de l'autre**, ce qui est exactement
pourquoi elles sont voisines ici :

- **`filter` présent mais vide vaut tout le carnet** (tâche 4) ;
- **`filter` absent vaut `400`.** La définition du § 10.3 est `((allprop | propname | prop)?,
  filter, limit?)`, sans point d'interrogation sur `filter`. Ce n'est pas un filtre qu'on ne sait
  pas évaluer, c'est une requête incomplète, et `403 supported-filter` **mentirait sur ce qui
  manque**.

**Et la borne de ce rapport est `CARDDAV:limit`, pas `DAV:limit`.** Les deux existent, portent le
même nom local et ne sont pas la même chose. **Un lecteur qui n'écouterait que `DAV:` ignorerait en
silence la borne posée par un client d'`addressbook-query`, et lui servirait les cinq mille fiches
qu'il venait de dire ne pas savoir digérer.**

**Files :**
- Create : `src/snoopy.microservice/Services/CardDav/AddressBookQueryReport.cs`
- Modify : `src/snoopy.microservice/Controllers/CardDavController.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CardDavQueryTests.cs`

**Interfaces :**
- Consomme : `AddressBookFilter`, `FilterPrefilter`, `DavCollation` (tâche 4) ;
  `AddressDataFilter`, `VCardVersionConverter`, `MultiStatusWriter`, `DavProperties` (plan b).

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Fact]
    public async Task AQueryWithAnEmptyFilter_AnswersTheWholeBook()
    {
        GivenCards("a.vcf", "b.vcf");

        var response = await Report(DavPaths.Collection(UserId), QueryBody(EmptyFilter()));

        Assert.Equal(2, ResponsesOf(response).Count);
    }

    [Fact]
    public async Task AQueryWithNoFilterElementAtAll_Answers400()
    {
        var response = await Report(DavPaths.Collection(UserId), QueryBodyWithoutFilter());

        // An incomplete request, not an unevaluable filter: 403 supported-filter would lie about
        // what is missing. The neighbouring rule says the opposite for an EMPTY filter, which is
        // exactly why the two are written side by side.
        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task AQuery_ReturnsOnlyTheMatchingCards()
    {
        GivenCardNamed("a.vcf", fn: "Ada Lovelace");
        GivenCardNamed("b.vcf", fn: "Grace Hopper");

        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(Filter(PropFilter("FN", TextMatch("Lovelace")))));

        Assert.Single(ResponsesOf(response));
        Assert.Contains(HrefsOf(response), h => h.EndsWith("a.vcf"));
    }

    [Fact]
    public async Task AQuery_ServesAddressDataWhenAsked()
    {
        GivenCardNamed("a.vcf", fn: "Ada Lovelace");

        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(EmptyFilter(), withAddressData: true));

        Assert.Contains("FN:Ada Lovelace", AddressDataOf(response).Single());
    }

    [Fact]
    public async Task AQuery_HonoursAPartialAddressDataAndStillCarriesGetetag()
    {
        GivenCardNamed("a.vcf", fn: "Ada Lovelace", email: "a@b.c");

        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(EmptyFilter(), addressDataProps: ["EMAIL"]));

        // Returning the whole card would be the silent version of the same defect, with one more
        // consequence: the client would write a COMPLETE card into a cache it believes partial, and
        // rewrite it as such.
        Assert.DoesNotContain("FN:", AddressDataOf(response).Single());
        Assert.NotEmpty(XDocument.Parse(await response.ReadAsync()).Descendants(DavXml.Dav + "getetag"));
    }

    [Fact]
    public async Task AQuery_ConvertsWhenAVersionIsAsked()
    {
        GivenCardNamed("a.vcf", fn: "Ada", version: "3.0");

        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(EmptyFilter(), version: "4.0"));

        Assert.Contains("VERSION:4.0", AddressDataOf(response).Single());
    }

    [Fact]
    public async Task ACarddavLimit_TruncatesAndSaysSo()
    {
        GivenCards("a.vcf", "b.vcf", "c.vcf");

        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(EmptyFilter(), carddavLimit: 2));

        Assert.Equal(2, ResponsesOfStatus(response, 200).Count);
        Assert.Single(ResponsesOfStatus(response, 507));
        Assert.Single(XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "number-of-matches-within-limits"));
    }

    [Fact]
    public async Task ADavLimitOnAQuery_IsNotItsBound()
    {
        GivenCards("a.vcf", "b.vcf", "c.vcf");

        var response = await Report(DavPaths.Collection(UserId), QueryBody(EmptyFilter(), davLimit: 1));

        // A reader listening only to DAV: would SILENTLY ignore the bound an addressbook-query
        // client set, and serve it the five thousand cards it had just said it could not digest.
        Assert.Equal(3, ResponsesOfStatus(response, 200).Count);
    }

    [Fact]
    public async Task AnUnevaluableFilter_Answers403SupportedFilter()
    {
        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(Filter(new XElement(DavXml.CardDav + "comp-filter"))));

        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CardDav + "supported-filter", ConditionOf(response));
    }

    [Fact]
    public async Task AnUnknownCollation_Answers403SupportedCollation()
    {
        var response = await Report(DavPaths.Collection(UserId),
            QueryBody(Filter(PropFilter("FN", TextMatch("x", collation: "i;octet")))));

        Assert.Equal(DavXml.CardDav + "supported-collation", ConditionOf(response));
    }

    [Fact]
    public async Task AQuery_LeavesOutTheCardsTheProtocolCannotSee()
    {
        GivenCards("a.vcf");
        GivenACardWithNoName();

        var response = await Report(DavPaths.Collection(UserId), QueryBody(EmptyFilter()));

        Assert.Single(ResponsesOf(response));
    }

    [Fact]
    public async Task AQueryOnACard_IsServed()
    {
        GivenCards("a.vcf");

        // supported-report-set says so on each card, and a Depth: 0 query on a card is sabre's
        // nominal case for that Depth. The routes must follow, or the header lies.
        var response = await Report(DavPaths.Card(UserId, "a.vcf"), QueryBody(EmptyFilter()));

        Assert.Equal(207, response.StatusCode);
    }

    [Fact]
    public async Task ADepthOfZero_StillReturnsTheMatches_AndThatIsANamedDivergence()
    {
        GivenCards("a.vcf", "b.vcf");

        var response = await Report(DavPaths.Collection(UserId), QueryBody(EmptyFilter()), depth: "0");

        // § 8.6 makes the report's scope its Depth header, so a Depth: 0 should evaluate the
        // collection alone and return no card. We return the filter's result whatever the value: no
        // known client sends Depth: 0 on a request it expects cards from, and returning zero cards
        // to somebody asking for them is precisely the failure mode this whole spec chases.
        // ccs-caldavtester may raise it in 4d — a named divergence, not a discovery.
        Assert.Equal(2, ResponsesOf(response).Count);
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~CardDavQueryTests`
Expected : `403 supported-report` partout — le refus posé par le plan b.

- [ ] **Step 3 : Écrire `AddressBookQueryReport`**

L'ordre : élément `filter` absent → `400` **avant** toute autre lecture ; analyse du filtre (qui
peut lever `supported-filter` ou `supported-collation`) ; pré-filtre SQL ; parcours du flux ;
évaluation exacte ; écriture d'une `response` par carte retenue, avec son `address-data` filtré et
converti si demandé.

La borne se lit **dans l'espace `CARDDAV:`**, et un `DAV:limit` présent est ignoré.

- [ ] **Step 4 : Câbler la branche `Query`**

Retirer `Query` de la branche `403 supported-report`. **La branche `default` reste** — un rapport
inconnu doit continuer de répondre `403 supported-report`.

- [ ] **Step 5 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 6 : Commit**

- sujet : `feat(dav): addressbook-query, sa borne CARDDAV et ses deux 400`
- corps : `filter present mais vide vaut tout le carnet, filter absent vaut 400 : les` /
  `deux regles se ressemblent et disent le contraire.`

---

## Paquet 3 — l'écriture

### Task 6 : `IDavContactWriter` — la troisième porte, pas une quatrième

**Le `PUT` se branche sur la porte d'écriture que 4a a déjà : carte reçue → `VCardProjector` →
`ReplaceProjectionAsync`. Aucun nouveau chemin d'écriture, aucune règle métier dupliquée.**

Ce qui survit à une mise à jour par `PUT` : `id`, `user_id`, `is_favorite`, `source`. Ce qui est
recalculé : **tout le reste**, puisque tout le reste est une projection.

**Cinq refus, chacun avec sa condition nommée**, et ils ne sont pas interchangeables :

| Cause | Réponse |
|---|---|
| corps illisible, ou portant **plus d'une** carte | `403 valid-address-data` — la carte est en cause, pas la requête, et c'est cette condition-là que le client lit |
| corps qui n'est pas de l'UTF-8 strict | `403 valid-address-data` |
| `VERSION` hors de `3.0`/`4.0` | `403 supported-address-data` — la carte peut être parfaitement lisible **tout en étant refusable** |
| `UID` déjà porté par une **autre** ressource, ou changé sous le même nom | `403 no-uid-conflict` + le `DAV:href` du conflit |
| au-delà de 1 Mo | `403 max-resource-size` |
| carnet plein | `507` |

**Le `Content-Type` de la requête n'est pas un juge, le corps l'est.** Les clients envoient
`text/vcard`, `text/x-vcard`, `text/directory`, parfois rien du tout, et les trois désignent la même
chose. L'appliquer à l'en-tête **refuserait de vieux clients parfaitement corrects pour un mot**.

**Un corps qui n'est pas de l'UTF-8 strict est refusé, parce que le stockage est du texte.** Un
corps en `ISO-8859-1` — que de vieux exports 3.0 produisent encore sous un paramètre `CHARSET` — se
décoderait en `U+FFFD`, et **l'ETag mentirait** : ce qui est stocké ne serait plus ce qui a été
envoyé. Décoder sous `DecoderExceptionFallback`.

**Files :**
- Create : `src/snoopy.microservice/Repositories/IDavContactWriter.cs`
- Create : `src/snoopy.microservice/Repositories/DavContactWriter.cs`
- Create : `src/snoopy.microservice/Models/Contacts/DavWriteOutcome.cs`
- Modify : `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/DavContactWriterTests.cs`

**Interfaces :**
- Consomme : `IContactSyncStore` (plan a), `ContactStore`'s projection path, `VCardProjector`.
- Produit, consommé par les tâches 7 et 8 :

```csharp
internal enum DavWriteStatus
{
    Created, Replaced, Deleted,
    InvalidCard, UnsupportedVersion, UidConflict, TooLarge, BookFull, NotFound, Busy
}

/// `Etag` is null when what was stored differs from what was sent — the RFC then requires NO ETag
/// in the response so the client re-reads. `ConflictHref` is set only on UidConflict.
internal sealed record DavWriteOutcome(
    DavWriteStatus Status, string? Etag, string? ConflictHref, ulong Sequence);

internal interface IDavContactWriter
{
    /// Creates or replaces the resource named `davName`. Archives whatever it replaces, advances
    /// the sequence, and lifts any tombstone on that name — all in one transaction.
    Task<DavWriteOutcome> PutAsync(
        Guid userId, string davName, string card, CancellationToken cancellationToken);

    /// Deletes it, archives its card and places a tombstone.
    Task<DavWriteOutcome> DeleteAsync(Guid userId, string davName, CancellationToken cancellationToken);

    /// Archives a body refused on a precondition, under the `rejected` cause. Answers false when the
    /// deduplication window dropped it, or when the body does not decode.
    Task<bool> ArchiveRejectedAsync(
        Guid userId, string davName, string card, CancellationToken cancellationToken);
}
```

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Fact]
    public async Task PuttingANewName_Creates()
    {
        var outcome = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        Assert.Equal(DavWriteStatus.Created, outcome.Status);
        Assert.NotNull(outcome.Etag);
    }

    [Fact]
    public async Task PuttingOverAnExistingName_ReplacesAndArchives()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Ada"), CancellationToken.None);
        SyncStore.Invocations.Clear();

        var outcome = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Grace"), CancellationToken.None);

        Assert.Equal(DavWriteStatus.Replaced, outcome.Status);
        SyncStore.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Cause == RevisionCause.Put), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WhatSurvivesAReplacement_IsIdAndFavouriteAndSource()
    {
        var created = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);
        await GivenTheContactIsFavourite("a.vcf");
        var idBefore = await ContactIdOf("a.vcf");

        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1", fn: "Grace"), CancellationToken.None);

        // No new write path, no business rule duplicated: everything else is a projection and is
        // recomputed.
        var row = await RowOf("a.vcf");
        Assert.Equal(idBefore, row.Id);
        Assert.True(row.IsFavorite);
    }

    [Fact]
    public async Task PuttingOverATombstonedName_LiftsItInTheSameTransaction()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);
        await Writer.DeleteAsync(UserId, "a.vcf", CancellationToken.None);
        SyncStore.Invocations.Clear();

        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u2"), CancellationToken.None);

        // A tombstone and a living card must never coexist on one name: a sync-collection would
        // return both, and the order the client applies them in would decide whether it keeps the
        // card or erases it.
        SyncStore.Verify(s => s.LiftTombstoneAsync(UserId, "a.vcf", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AUidHeldByAnotherResource_IsRefusedWithItsHref()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("shared-uid"), CancellationToken.None);

        var outcome = await Writer.PutAsync(UserId, "b.vcf", ValidCard("shared-uid"), CancellationToken.None);

        // The unique index (user_id, uid) laid by 4a IS this guard: translating its violation is all
        // that is needed, rather than letting it come back as a 500. And without the href the client
        // knows it failed but not what to re-read — its only remaining move is to retry identically.
        Assert.Equal(DavWriteStatus.UidConflict, outcome.Status);
        Assert.Equal(DavPaths.Card(UserId, "a.vcf"), outcome.ConflictHref);
    }

    [Fact]
    public async Task AUidChangedUnderTheSameName_IsRefusedToo()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        var outcome = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u2"), CancellationToken.None);

        // § 6.3.2.1 covers it explicitly: the UID arbitrates the card's identity, and a UID that
        // changes under the same name is another card. Radicale refuses; sabre accepts, and that
        // laxity is an open bug with its own maintainers — not a precedent.
        Assert.Equal(DavWriteStatus.UidConflict, outcome.Status);
        Assert.Equal(DavPaths.Card(UserId, "a.vcf"), outcome.ConflictHref);
    }

    [Fact]
    public async Task ABodyThatDoesNotParse_IsRefusedAsInvalidCard()
    {
        var outcome = await Writer.PutAsync(UserId, "a.vcf", "not a vcard at all", CancellationToken.None);

        Assert.Equal(DavWriteStatus.InvalidCard, outcome.Status);
    }

    [Fact]
    public async Task ABodyCarryingTwoCards_IsRefused()
    {
        var outcome = await Writer.PutAsync(
            UserId, "a.vcf", ValidCard("u1") + ValidCard("u2"), CancellationToken.None);

        // An address resource is ONE card (§ 5.1). This is the point the 4a residual announced for
        // this slice — VCardProjector.RawCard does not stop at the first END:VCARD — and the explicit
        // refusal must PRECEDE the projection, not follow it.
        Assert.Equal(DavWriteStatus.InvalidCard, outcome.Status);
    }

    [Fact]
    public async Task AVersionWeDoNotAnnounce_HasItsOwnCondition()
    {
        var outcome = await Writer.PutAsync(
            UserId, "a.vcf", CardOfVersion("2.1"), CancellationToken.None);

        // Old Android exports still produce 2.1. A card can be perfectly readable while being
        // refusable, and the two conditions say different things to the client.
        Assert.Equal(DavWriteStatus.UnsupportedVersion, outcome.Status);
    }

    [Fact]
    public async Task ACardOverTheCeiling_IsRefusedAsTooLarge()
    {
        var outcome = await Writer.PutAsync(UserId, "a.vcf", HugeCard(), CancellationToken.None);

        Assert.Equal(DavWriteStatus.TooLarge, outcome.Status);
    }

    [Fact]
    public async Task AFullBook_IsRefusedAsBookFull()
    {
        await GivenTheBookIsFull();

        var outcome = await Writer.PutAsync(UserId, "new.vcf", ValidCard("u1"), CancellationToken.None);

        Assert.Equal(DavWriteStatus.BookFull, outcome.Status);
    }

    [Fact]
    public async Task AStoredCardDifferingFromWhatWasSent_AnswersNoEtag()
    {
        // 4a inserts a UID into a card that declares none — the invariant holds for every stored
        // card. When that happens on a PUT, what is stored differs from what was sent, and the RFC
        // then requires NO ETag so the client re-reads. Returning the stored bytes' ETag would be
        // WORSE than none: the client would believe it holds the card it sent, and never re-read.
        var outcome = await Writer.PutAsync(UserId, "a.vcf", CardWithoutUid(), CancellationToken.None);

        Assert.Equal(DavWriteStatus.Created, outcome.Status);
        Assert.Null(outcome.Etag);
    }

    [Fact]
    public async Task AnUntransformedCard_AnswersItsEtag()
    {
        var outcome = await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        Assert.NotNull(outcome.Etag);
    }

    [Fact]
    public async Task TheBytesAreStoredAsTheyArrive_LineEndingsIncluded()
    {
        const string lfOnly = "BEGIN:VCARD\nVERSION:3.0\nUID:u1\nFN:Ada\nEND:VCARD\n";

        await Writer.PutAsync(UserId, "a.vcf", lfOnly, CancellationToken.None);

        // Normalising would be a TRANSFORMATION — hence a response with no ETag, a re-read, and a
        // card that never coincides with the client's. The server's job is to hand any other client
        // exactly what it received, and it is also what makes card_hash the SHA-256 of what is served.
        Assert.Equal(lfOnly, (await RowOf("a.vcf")).VCardRaw);
    }

    [Fact]
    public async Task Deleting_ArchivesAndBuries()
    {
        await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);
        SyncStore.Invocations.Clear();

        var outcome = await Writer.DeleteAsync(UserId, "a.vcf", CancellationToken.None);

        Assert.Equal(DavWriteStatus.Deleted, outcome.Status);
        SyncStore.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Cause == RevisionCause.Delete), It.IsAny<CancellationToken>()),
            Times.Once);
        SyncStore.Verify(s => s.PlaceTombstoneAsync(UserId, "a.vcf", It.IsAny<ulong>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletingWhatIsNotThere_IsNotFound() =>
        Assert.Equal(DavWriteStatus.NotFound,
            (await Writer.DeleteAsync(UserId, "never.vcf", CancellationToken.None)).Status);

    [Fact]
    public async Task ARejectedBodyThatDecodes_IsArchived()
    {
        var archived = await Writer.ArchiveRejectedAsync(
            UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        Assert.True(archived);
    }

    [Fact]
    public async Task ArchivingARejectedBody_TakesNoRankAndNoLock()
    {
        SyncStore.Invocations.Clear();

        await Writer.ArchiveRejectedAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);

        // Nothing visible to the protocol has changed, and the 412 path must wake no client.
        SyncStore.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ARejectedBodyThatDoesNotParse_IsStillArchived_WithNoUid()
    {
        var archived = await Writer.ArchiveRejectedAsync(
            UserId, "a.vcf", "garbage but valid utf-8", CancellationToken.None);

        // It is an ARCHIVE, not a card. contact_revisions.uid is nullable for exactly this.
        Assert.True(archived);
        SyncStore.Verify(s => s.ArchiveAsync(
            It.Is<ContactRevision>(r => r.Uid == null && r.DavName == "a.vcf"),
            It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavContactWriterTests`
Expected : ne compile pas.

- [ ] **Step 3 : Écrire `DavWriteOutcome` et l'interface**

- [ ] **Step 4 : Écrire `DavContactWriter`**

L'ordre du `PUT`, et il est le contenu de la tâche :

1. **valider la carte** — une seule, elle parse, sa version est annoncée. Le refus **précède** la
   projection.
2. ouvrir la transaction, **le verrou d'état d'abord** ;
3. chercher la ligne par `(user_id, dav_name)` ;
4. contrôler l'`UID` : porté par une autre ressource, ou changé sous le même nom → `UidConflict` ;
5. archiver ce qui est remplacé, sous la cause `Put` ;
6. écrire par le chemin de 4a — `VCardProjector` puis `ReplaceProjectionAsync` — en gardant `id`,
   `is_favorite` et `source` ;
7. poser le rang, lever la tombe ;
8. rendre l'ETag **seulement si les octets stockés sont ceux reçus**.

**La course de deux `PUT` créateurs se rattrape plutôt que de remonter en `500`.** Le second passe
la pré-vérification d'existence puis meurt sur l'index unique `(user_id, dav_name)`. Attraper la
violation et **rejouer comme un remplacement** de la ressource que l'autre écriture vient de créer —
c'est ce que le même `PUT` arrivé une seconde plus tard aurait été. Le cas `If-None-Match: *` est
traité à la tâche 7, où la condition vit.

`ArchiveRejectedAsync` n'ouvre **pas** de transaction d'état et ne prend **pas** de rang.

- [ ] **Step 5 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 6 : Commit**

- sujet : `feat(dav): l'ecrivain DAV, branche sur la porte d'ecriture de 4a`
- corps : `Cinq refus nommes, et l'ETag tu quand les octets stockes different de ceux` /
  `recus : rendre celui du stockage serait pire que n'en rendre aucun.`

---

### Task 7 : `PUT` — les préconditions, et le corps refusé qu'on archive quand même

**`If-Match` a la sémantique complète du RFC 7232 § 3.1, et il faut l'écrire aussi bien que celle
d'`If-None-Match`** — sans quoi seule la lecture serait servie correctement. Il accepte une
**liste** et réussit si l'une des valeurs correspond ; il accepte `*`, qui réussit sur toute
ressource existante ; et il compare en **comparaison forte**. Nos ETags étant tous forts, la
comparaison forte ne rejette en pratique qu'un `W/` qu'un client n'aurait pas dû renvoyer — **mais
un client qui envoie deux ETags est courant, et le refuser à tort effacerait sa modification sur un
`412` qu'il ne mérite pas.**

**Et un `PUT` refusé pour `If-Match` archive son corps**, sous la cause `rejected`, **avant** que le
`412` ne parte. La première rédaction de la spec excusait la perte en disant que la version refusée
« n'a jamais atteint le carnet » : le carnet ne l'a pas, c'est vrai ; **le serveur, lui, l'a** — les
octets sont dans le corps de la requête, déjà lu, déjà borné. Les jeter est une décision, pas une
fatalité. Or c'est exactement le cas qu'on redoute : une adresse saisie dans un train, un webmail
qui a bougé, un `412`, et **DAVx⁵ qui applique « le serveur gagne » sans consulter personne**.

**L'ordre est écrit parce que deux lectures du RFC en donnent deux différents :** la condition
`If-Match` s'évalue **d'abord** — RFC 7232 place les préconditions avant le traitement du corps — et
le corps refusé n'est archivé **que s'il se décode en UTF-8 strict**.

**Files :**
- Modify : `src/snoopy.microservice/Controllers/CardDavController.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CardDavPutTests.cs`

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Fact]
    public async Task ACreatingPut_Answers201WithItsEtag()
    {
        var response = await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"));

        Assert.Equal(201, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
    }

    [Fact]
    public async Task AReplacingPut_Answers204()
    {
        await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"));

        Assert.Equal(204, (await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1", fn: "G"))).StatusCode);
    }

    [Theory]
    [InlineData("\"{etag}\"")]
    [InlineData("*")]
    [InlineData("\"other\", \"{etag}\"")]
    public async Task AMatchingIfMatch_IsAccepted(string template)
    {
        var etag = await GivenACardAndItsEtag("a.vcf");

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1", fn: "G"),
            ifMatch: template.Replace("{etag}", etag.Trim('"')));

        // A client sending two ETags is common, and refusing it wrongly would erase its edit on a 412
        // it does not deserve.
        Assert.Equal(204, response.StatusCode);
    }

    [Fact]
    public async Task AWeakIfMatch_IsRefused()
    {
        var etag = await GivenACardAndItsEtag("a.vcf");

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1", fn: "G"),
            ifMatch: $"W/{etag}");

        // If-Match guards a WRITE and compares strongly: a weak tag says "semantically equivalent",
        // which is not a promise a byte-for-byte replacement can rest on.
        Assert.Equal(412, response.StatusCode);
    }

    [Fact]
    public async Task AStaleIfMatch_Answers412()
    {
        await GivenACardAndItsEtag("a.vcf");

        Assert.Equal(412, (await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1", fn: "G"),
            ifMatch: "\"stale\"")).StatusCode);
    }

    [Fact]
    public async Task AnIfMatchOnAnAbsentResource_Answers412AndNot404()
    {
        var response = await Put(DavPaths.Card(UserId, "never.vcf"), ValidCard("u1"), ifMatch: "\"x\"");

        // The condition is false, and 412 is what the client reads as "re-read before rewriting".
        Assert.Equal(412, response.StatusCode);
    }

    [Fact]
    public async Task AnIfNoneMatchStar_OnAnExistingResource_Answers412()
    {
        await GivenACardAndItsEtag("a.vcf");

        Assert.Equal(412, (await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"),
            ifNoneMatch: "*")).StatusCode);
    }

    [Fact]
    public async Task AnIfNoneMatchStar_OnANewName_Creates() =>
        Assert.Equal(201, (await Put(DavPaths.Card(UserId, "new.vcf"), ValidCard("u1"),
            ifNoneMatch: "*")).StatusCode);

    [Fact]
    public async Task ARefusedPut_ArchivesItsBodyBeforeThe412Leaves()
    {
        await GivenACardAndItsEtag("a.vcf");
        var refused = ValidCard("u1", fn: "Written on a train");

        await Put(DavPaths.Card(UserId, "a.vcf"), refused, ifMatch: "\"stale\"");

        // DAVx5 applies "the server wins" without consulting anyone — its manual says so in those
        // terms. The refusal is right; the erasure that follows is not. This is the one place in the
        // slice where we do strictly better than both reference servers: Radicale's git hook sees
        // only ACCEPTED writes, and sabre sees nothing at all.
        Writer.Verify(w => w.ArchiveRejectedAsync(UserId, "a.vcf", refused, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ARefusedPut_IsArchivedBeforeTheStatusIsWritten()
    {
        await GivenACardAndItsEtag("a.vcf");

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"), ifMatch: "\"stale\"");

        // Order matters: the condition is evaluated FIRST (RFC 7232 puts preconditions before body
        // processing), and the archive happens before the 412 leaves.
        Assert.Equal(412, response.StatusCode);
        Writer.Verify(w => w.ArchiveRejectedAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ARefusedPutWhoseBodyIsNotUtf8_IsNotArchived()
    {
        await GivenACardAndItsEtag("a.vcf");

        var response = await PutBytes(DavPaths.Card(UserId, "a.vcf"), Latin1Bytes(), ifMatch: "\"stale\"");

        // Storage is text: archiving bytes a MEDIUMTEXT would betray violates the promise of
        // restitution. It answers 412 with no revision, and decision 18's log line keeps the trace.
        Assert.Equal(412, response.StatusCode);
        Writer.Verify(w => w.ArchiveRejectedAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ARefusedDelete_ArchivesNothing()
    {
        await GivenACardAndItsEtag("a.vcf");

        await Delete(DavPaths.Card(UserId, "a.vcf"), ifMatch: "\"stale\"");

        // It brings no bytes: it leaves decision 18's log line, and that is all.
        Writer.Verify(w => w.ArchiveRejectedAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ABodyThatIsNotStrictUtf8_Answers403ValidAddressData()
    {
        var response = await PutBytes(DavPaths.Card(UserId, "a.vcf"), Latin1Bytes());

        // An ISO-8859-1 body would decode to U+FFFD and the ETag would LIE: what is stored would no
        // longer be what was sent, and the client would believe it holds its card.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.CardDav + "valid-address-data", ConditionOf(response));
    }

    [Theory]
    [InlineData("text/vcard")]
    [InlineData("text/x-vcard")]
    [InlineData("text/directory")]
    [InlineData(null)]
    public async Task TheContentTypeIsNotAJudge(string? contentType) =>
        Assert.Equal(201, (await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"),
            contentType: contentType)).StatusCode);

    [Fact]
    public async Task AUidConflict_CarriesTheHrefOfTheConflict()
    {
        await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("shared"));

        var response = await Put(DavPaths.Card(UserId, "b.vcf"), ValidCard("shared"));

        // A SHOULD of § 6.3.2.1 that its DTD makes mandatory as soon as the element is emitted:
        // without it the client knows it failed but not what to re-read.
        Assert.Equal(403, response.StatusCode);
        var condition = ErrorRootOf(response).Element(DavXml.CardDav + "no-uid-conflict")!;
        Assert.Equal(DavPaths.Card(UserId, "a.vcf"), condition.Element(DavXml.Dav + "href")!.Value);
    }

    [Fact]
    public async Task AnInvalidNameIsRefusedByAConsideredAnswer_NotByRouting()
    {
        var response = await Put($"/dav/addressbooks/{UserId}/default/{Uri.EscapeDataString("..")}",
            ValidCard("u1"));

        // A route pattern demanding .vcf would refuse a name by a routing 404 — the one code a client
        // reads as "this collection does not contain that" rather than "that name will not do".
        Assert.Equal(403, response.StatusCode);
    }

    [Fact]
    public async Task TwoCreatingPutsOnOneName_AreReplayedAsAReplacement()
    {
        GivenTheUniqueIndexWillTripOnce();

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"));

        // Left alone this is a 500, exactly what the errors table promises never to answer. Replayed
        // as a replacement of the resource the other write just created — which is what the same PUT
        // arriving a second later would have been.
        Assert.Equal(204, response.StatusCode);
    }

    [Fact]
    public async Task TwoCreatingPutsWithIfNoneMatchStar_GiveThe412ToTheLoser()
    {
        GivenTheUniqueIndexWillTripOnce();

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"), ifNoneMatch: "*");

        // Its condition is now false.
        Assert.Equal(412, response.StatusCode);
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~CardDavPutTests`
Expected : `405` partout — le plan b n'a lié aucun `PUT`.

- [ ] **Step 3 : Écrire la route**

`[HttpPut("addressbooks/{userId:guid}/default/{*davName}")]`, `[RequestSizeLimit(1024 * 1024)]`.

L'ordre : propriété → validation du nom (`403 valid-address-data`) → **lecture du corps en UTF-8
strict** → préconditions → archivage du refus si `412` → écriture → traduction de l'issue.

**Lire le corps sous `DecoderExceptionFallback`**, et non `Encoding.UTF8.GetString`, qui remplace
en silence par `U+FFFD`.

- [ ] **Step 4 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 5 : Commit**

- sujet : `feat(dav): PUT, ses preconditions, et le corps refuse archive quand meme`
- corps : `Le serveur a les octets d'un 412 : les jeter est une decision, pas une` /
  `fatalite, et DAVx5 applique le serveur gagne sans consulter personne.`

---

### Task 8 : `DELETE` — et la tombe qu'un refus ne pose pas

Court, et une seule chose à ne pas rater : **une suppression conditionnelle en désaccord répond
`412` et ne pose AUCUNE tombe.** Poser la tombe puis refuser ferait disparaître du carnet une fiche
que le serveur vient de dire qu'il gardait.

**Files :**
- Modify : `src/snoopy.microservice/Controllers/CardDavController.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CardDavDeleteTests.cs`

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Fact]
    public async Task ADelete_Answers204()
    {
        await GivenACardAndItsEtag("a.vcf");

        Assert.Equal(204, (await Delete(DavPaths.Card(UserId, "a.vcf"))).StatusCode);
    }

    [Fact]
    public async Task ADeleteWithAMatchingIfMatch_Succeeds()
    {
        var etag = await GivenACardAndItsEtag("a.vcf");

        // Clients send it precisely so as not to erase a card modified elsewhere in between.
        Assert.Equal(204, (await Delete(DavPaths.Card(UserId, "a.vcf"), ifMatch: etag)).StatusCode);
    }

    [Fact]
    public async Task ADeleteWithAStaleIfMatch_Answers412AndBuriesNothing()
    {
        await GivenACardAndItsEtag("a.vcf");

        var response = await Delete(DavPaths.Card(UserId, "a.vcf"), ifMatch: "\"stale\"");

        // Burying then refusing would make a card disappear from the book that the server has just
        // said it was keeping.
        Assert.Equal(412, response.StatusCode);
        Writer.Verify(w => w.DeleteAsync(It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ADeleteOfWhatIsNotThere_Answers404() =>
        Assert.Equal(404, (await Delete(DavPaths.Card(UserId, "never.vcf"))).StatusCode);

    [Fact]
    public async Task ADeleteOnTheCollection_Is405()
    {
        var response = await Delete(DavPaths.Collection(UserId));

        // It would erase the whole book — a gesture the product offers nowhere and that no route must
        // offer by accident. The reference servers serve it, but their book is not tied to the
        // account the way ours is.
        Assert.Equal(405, response.StatusCode);
    }

    [Fact]
    public async Task AfterADelete_TheCardIsGoneAndTheNameIsBuried()
    {
        await GivenACardAndItsEtag("a.vcf");

        await Delete(DavPaths.Card(UserId, "a.vcf"));

        Assert.Equal(404, (await Get(DavPaths.Card(UserId, "a.vcf"))).StatusCode);
        Writer.Verify(w => w.DeleteAsync(UserId, "a.vcf", It.IsAny<CancellationToken>()), Times.Once);
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~CardDavDeleteTests`
Expected : `405` partout.

- [ ] **Step 3 : Écrire la route**

`[HttpDelete("addressbooks/{userId:guid}/default/{*davName}")]`. L'ordre : propriété → lecture →
`If-Match` → suppression. **La suppression n'est appelée qu'après le contrôle**, et le test le
vérifie par un `Times.Never` plutôt que par l'absence de tombe : c'est l'appel qu'on veut interdire,
pas son effet.

- [ ] **Step 4 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 5 : Commit**

- sujet : `feat(dav): DELETE, conditionnel, et sans tombe quand il refuse`
- corps : `Enterrer puis refuser ferait disparaitre du carnet une fiche que le serveur` /
  `vient de dire qu'il gardait.`

---

## Paquet 4 — la traduction au bord et les deux coutures léguées

### Task 9 : aucune réponse n'est un `500`

**Le point vaut d'être écrit, et c'est la tâche qui le rend vrai.** Les refus du store —
`CapReached`, `CardTooLarge`, la violation de l'index `(user_id, uid)` — sont des `Result.Failure`
ou des exceptions de base **rédigées pour l'UI du webmail** ; laissées telles quelles elles
remontent en `500`, et **un `500` est ce qu'un client DAV retente indéfiniment, sur la même carte,
à chaque cycle de synchronisation.**

Le `503` de l'attente de verrou est **le seul cas où retenter est LA bonne conduite**, et c'est
justement pour cela qu'il porte un code qui le dit et un `Retry-After` qui le date. Un import tenant
le verrou d'état jusqu'à son `COMMIT` fait attendre une écriture concurrente jusqu'à
`innodb_lock_wait_timeout` — cinquante secondes par défaut.

**Files :**
- Create : `src/snoopy.microservice/Services/CardDav/DavOutcomeTranslator.cs`
- Modify : `src/snoopy.microservice/Repositories/DavContactWriter.cs`
- Modify : `src/snoopy.microservice/Controllers/CardDavController.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/CardDav/DavOutcomeTranslatorTests.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CardDavNoFiveHundredTests.cs`

**Interfaces :**

```csharp
internal static class DavOutcomeTranslator
{
    /// The one place a write outcome becomes an HTTP answer. Every branch is named; there is no
    /// default that falls through to 500.
    internal static Task WriteAsync(HttpResponse response, DavWriteOutcome outcome,
        CancellationToken cancellationToken);

    /// True when an exception is InnoDB saying "come back later" — a lock wait timeout (1205) or a
    /// deadlock it arbitrated (1213) — rather than a fault.
    internal static bool IsTransient(Exception exception);
}
```

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Theory]
    [InlineData(DavWriteStatus.Created, 201)]
    [InlineData(DavWriteStatus.Replaced, 204)]
    [InlineData(DavWriteStatus.Deleted, 204)]
    [InlineData(DavWriteStatus.NotFound, 404)]
    [InlineData(DavWriteStatus.InvalidCard, 403)]
    [InlineData(DavWriteStatus.UnsupportedVersion, 403)]
    [InlineData(DavWriteStatus.UidConflict, 403)]
    [InlineData(DavWriteStatus.TooLarge, 403)]
    [InlineData(DavWriteStatus.BookFull, 507)]
    [InlineData(DavWriteStatus.Busy, 503)]
    public async Task EveryStatus_HasItsCode(DavWriteStatus status, int expected)
    {
        var context = NewContext();

        await DavOutcomeTranslator.WriteAsync(context.Response, Outcome(status), CancellationToken.None);

        Assert.Equal(expected, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(DavWriteStatus.InvalidCard, "valid-address-data")]
    [InlineData(DavWriteStatus.UnsupportedVersion, "supported-address-data")]
    [InlineData(DavWriteStatus.UidConflict, "no-uid-conflict")]
    [InlineData(DavWriteStatus.TooLarge, "max-resource-size")]
    public async Task EveryRefusal_NamesItsCondition(DavWriteStatus status, string condition)
    {
        var context = NewContext();

        await DavOutcomeTranslator.WriteAsync(context.Response, Outcome(status), CancellationToken.None);

        // A client loops on these refusals whatever the code — DAVx5 catches neither a 403 outside
        // need-privileges nor a 507 — but the named condition makes it a readable log line, where a
        // 500 is an accident indistinguishable server-side.
        Assert.Equal(DavXml.CardDav + condition, ConditionOf(context.Response));
    }

    [Fact]
    public async Task Busy_CarriesARetryAfter()
    {
        var context = NewContext();

        await DavOutcomeTranslator.WriteAsync(context.Response, Outcome(DavWriteStatus.Busy),
            CancellationToken.None);

        // The ONE case where retrying is the right conduct, which is exactly why it carries a code
        // that says so and a header that dates it.
        Assert.Equal(503, context.Response.StatusCode);
        Assert.NotNull(context.Response.Headers.RetryAfter.ToString());
        Assert.NotEmpty(context.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public void EveryEnumValue_IsHandled()
    {
        // The assertion that makes the class stay honest: a status added later without a branch
        // would otherwise fall through to whatever the default does — and a default here is a 500.
        foreach (var status in Enum.GetValues<DavWriteStatus>())
        {
            var context = NewContext();
            var exception = Record.ExceptionAsync(() =>
                DavOutcomeTranslator.WriteAsync(context.Response, Outcome(status), CancellationToken.None));

            Assert.Null(exception.Result);
            Assert.NotEqual(500, context.Response.StatusCode);
        }
    }

    [Theory]
    [InlineData(1205)] // lock wait timeout
    [InlineData(1213)] // deadlock arbitrated by InnoDB
    public void InnoDbSayingComeBackLater_IsTransient(int mySqlErrorNumber) =>
        Assert.True(DavOutcomeTranslator.IsTransient(MySqlExceptionWith(mySqlErrorNumber)));

    [Fact]
    public void AnythingElse_IsNotTransient() =>
        Assert.False(DavOutcomeTranslator.IsTransient(new InvalidOperationException()));
```

et, au niveau du contrôleur, le test qui vaut pour toute la tranche :

```csharp
    [Theory]
    [MemberData(nameof(EveryStoreRefusal))]
    public async Task NoStoreRefusal_EverBecomesA500(Func<Task> arrange, string url, string body)
    {
        await arrange();

        var response = await Put(url, body);

        // Written as one test over every refusal rather than one per case: the rule is the tranche's,
        // not one route's, and a 500 here is a client that retries the same card for ever.
        Assert.NotEqual(500, response.StatusCode);
    }

    [Fact]
    public async Task ALockWaitTimeout_Answers503AndNot500()
    {
        GivenTheStoreWillTimeOutOnItsLock();

        var response = await Put(DavPaths.Card(UserId, "a.vcf"), ValidCard("u1"));

        Assert.Equal(503, response.StatusCode);
        Assert.NotEmpty(response.Headers.RetryAfter);
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~DavOutcomeTranslator|FullyQualifiedName~CardDavNoFiveHundred"`
Expected : ne compile pas.

- [ ] **Step 3 : Écrire `DavOutcomeTranslator`**

Un `switch` **exhaustif** sur `DavWriteStatus`, sans branche `default` qui laisserait passer — ou
avec une branche `default` qui **lève à la compilation** par un `switch` d'expression sur un
`enum` couvert. Le test `EveryEnumValue_IsHandled` est là pour le cas où le compilateur ne suffit
pas.

`IsTransient` reconnaît les numéros d'erreur MySQL `1205` et `1213`. Ne pas se fier au message :
il est traduit selon la locale du serveur.

- [ ] **Step 4 : Traduire dans `DavContactWriter`**

Envelopper le corps transactionnel : une exception reconnue comme transitoire devient
`DavWriteStatus.Busy` plutôt que de remonter. Une violation d'index `(user_id, uid)` devient
`UidConflict` — c'est ce que 4a a posé exactement pour ça.

- [ ] **Step 5 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 6 : Commit**

- sujet : `feat(dav): chaque refus du store devient une reponse nommee`
- corps : `Un 500 est ce qu'un client DAV retente indefiniment sur la meme carte ; le` /
  `503 est le seul cas ou retenter est la bonne conduite.`

---

### Task 10 : les deux coutures que 4c-i a léguées

Deux résidus nommés depuis 4c-i, qu'aucun des trois plans n'a pu fermer plus tôt et que celui-ci
peut. **Ils sont réunis parce qu'ils partagent leur cause : le limiteur et le cache
d'authentification sont `internal`, et rien du côté protocole ne pouvait les atteindre avant que le
protocole existe.**

**1. Le piège du limiteur après une régénération.** Une régénération met chaque appareil configuré
en boucle d'échec, et `AuthAttemptThrottle` bloque à dix échecs par quart d'heure sur l'identifiant ;
un cycle de synchronisation en vaut plusieurs, donc **deux appareils suffisent à franchir le
seuil**. Or `IsBlocked` s'exécute **avant** la comparaison du condensat, et seul un succès efface la
clé : **une fois bloqué, saisir le BON secret répond `429`.** L'onglet « Sync » de 4c-i dit déjà
l'ordre à suivre — éteindre la synchro d'abord — mais un utilisateur qui ne le lit pas se verrouille
quand même.

**Le contrôleur PEUT effacer la clé de l'identifiant en régénérant** : l'appelant vient de prouver
son identité par un JWT, c'est-à-dire par un facteur que le limiteur ne protège pas. Ce qui manquait
était la couture. **La clé d'adresse, elle, reste** — un attaquant partageant le /64 de la victime
ne doit pas pouvoir se déverrouiller en faisant régénérer quelqu'un d'autre.

**2. Le résidu de soixante secondes sur la révocation.** `Forget` ne peut pas battre un `Store`
concurrent : une requête qui a lu l'ancien secret **avant** la rotation peut le réinscrire **après**.
La fenêtre est celle du cache — soixante secondes — et le fermer demande un **compteur de
génération** dans `IDavAuthenticationCache` : `Store` refuse d'inscrire une entrée dont la
génération est antérieure à la dernière révocation.

**Files :**
- Modify : `src/snoopy.microservice/Authentication/CardDav/AuthAttemptThrottle.cs`
- Create : `src/snoopy.microservice/Authentication/CardDav/IAuthAttemptThrottle.cs`
- Modify : `src/snoopy.microservice/Authentication/CardDav/IDavAuthenticationCache.cs`
- Modify : `src/snoopy.microservice/Authentication/CardDav/DavAuthenticationCache.cs`
- Modify : `src/snoopy.microservice/Controllers/DavCredentialsController.cs`
- Modify : `src/snoopy.microservice/Repositories/WebmailUserStore.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Authentication/AuthAttemptThrottleSeamTests.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Authentication/DavAuthenticationCacheGenerationTests.cs`

- [ ] **Step 1 : Écrire les tests de la couture, rouges**

```csharp
    [Fact]
    public void ForgettingAnIdentifier_ClearsItsFailures()
    {
        var throttle = new AuthAttemptThrottle(TimeProvider.System);
        for (var i = 0; i < 10; i++) throttle.RecordFailure("alice@weesky.be", Address);
        Assert.True(throttle.IsBlocked("alice@weesky.be", Address));

        throttle.ForgetIdentifier("alice@weesky.be");

        // The caller just proved its identity with a JWT — a factor the throttle does not guard.
        // Without this seam, a user who regenerates while two devices are still syncing locks
        // themselves out, and the CORRECT new secret answers 429.
        Assert.False(throttle.IsBlocked("alice@weesky.be", Address));
    }

    [Fact]
    public void ForgettingAnIdentifier_LeavesTheAddressKeyAlone()
    {
        var throttle = new AuthAttemptThrottle(TimeProvider.System);
        for (var i = 0; i < 10; i++) throttle.RecordFailure("someone-else@weesky.be", Address);

        throttle.ForgetIdentifier("alice@weesky.be");

        // An attacker sharing the victim's /64 must not be able to unblock themselves by making
        // somebody else regenerate.
        Assert.True(throttle.IsBlocked("someone-else@weesky.be", Address));
    }

    [Fact]
    public void ForgettingIsCanonicalisedLikeEveryOtherEntryPoint()
    {
        var throttle = new AuthAttemptThrottle(TimeProvider.System);
        for (var i = 0; i < 10; i++) throttle.RecordFailure("Alice@Weesky.BE", Address);

        throttle.ForgetIdentifier("  alice@weesky.be  ");

        // IdentifierKey trims and lowercases; a Forget that did not would silently do nothing.
        Assert.False(throttle.IsBlocked("Alice@Weesky.BE", Address));
    }

    [Fact]
    public async Task Regenerating_ForgetsTheIdentifiersFailures()
    {
        var throttle = new Mock<IAuthAttemptThrottle>();
        var controller = NewCredentialsController(throttle.Object);

        await controller.Regenerate(CancellationToken.None);

        throttle.Verify(t => t.ForgetIdentifier(IdentityResolver.Canonical(AuthenticatedEmail)),
            Times.Once);
    }

    [Fact]
    public async Task TurningSyncOn_ForgetsThemToo()
    {
        var throttle = new Mock<IAuthAttemptThrottle>();
        var controller = NewCredentialsController(throttle.Object);

        await controller.SetCardDav(new DavSyncToggle { Enabled = true }, CancellationToken.None);

        // Enabling for the first time also mints a secret, so it lands the user in the same shape.
        throttle.Verify(t => t.ForgetIdentifier(It.IsAny<string>()), Times.Once);
    }
```

- [ ] **Step 2 : Écrire les tests de la génération, rouges**

```csharp
    [Fact]
    public void AStoreThatReadBeforeARevocation_IsRefusedAfterIt()
    {
        var cache = new DavAuthenticationCache(TimeProvider.System);
        var generation = cache.Generation("alice@weesky.be");   // the reader takes it BEFORE

        cache.Forget("alice@weesky.be");                        // the rotation happens
        cache.Store("alice@weesky.be", Fingerprint, UserId, generation);

        // Forget cannot beat a concurrent Store: a request that read the old secret before the
        // rotation could write it back after. The generation counter is what closes the sixty-second
        // window — and sixty seconds of a revoked secret still working is the whole point.
        Assert.False(cache.TryGet("alice@weesky.be", Fingerprint, out _));
    }

    [Fact]
    public void AStoreTakenAfterTheRevocation_IsAccepted()
    {
        var cache = new DavAuthenticationCache(TimeProvider.System);
        cache.Forget("alice@weesky.be");
        var generation = cache.Generation("alice@weesky.be");   // taken AFTER

        cache.Store("alice@weesky.be", Fingerprint, UserId, generation);

        Assert.True(cache.TryGet("alice@weesky.be", Fingerprint, out _));
    }

    [Fact]
    public void TheGenerationIsPerIdentifier()
    {
        var cache = new DavAuthenticationCache(TimeProvider.System);
        var generation = cache.Generation("alice@weesky.be");

        cache.Forget("bob@weesky.be");
        cache.Store("alice@weesky.be", Fingerprint, UserId, generation);

        // Revoking one user must not evict every other user's cache entry — that would turn one
        // password change into a thundering herd of database reads.
        Assert.True(cache.TryGet("alice@weesky.be", Fingerprint, out _));
    }

    [Fact]
    public void TheGenerationSurvivesAnEntryExpiring()
    {
        // The counter must not live inside the cache ENTRY: an entry that expires would take the
        // generation with it, and the next Store would be accepted under a stale one.
        var cache = new DavAuthenticationCache(TimeProvider.System);
        cache.Forget("alice@weesky.be");
        var afterRevocation = cache.Generation("alice@weesky.be");

        cache.Store("alice@weesky.be", Fingerprint, UserId, afterRevocation);
        cache.Forget("alice@weesky.be");

        Assert.NotEqual(afterRevocation, cache.Generation("alice@weesky.be"));
    }
```

- [ ] **Step 3 : Lancer les deux fichiers pour les voir échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~AuthAttemptThrottleSeam|FullyQualifiedName~DavAuthenticationCacheGeneration"`
Expected : ne compile pas.

- [ ] **Step 4 : Extraire `IAuthAttemptThrottle`**

`AuthAttemptThrottle` est `internal` et un contrôleur public ne peut pas le prendre en paramètre.
Extraire une interface **`internal`** et rendre le contrôleur `internal` lui aussi, ou — si le
contrôleur doit rester public pour le routage — passer par `[assembly: InternalsVisibleTo]` déjà en
place pour les tests. **Vérifier ce que le projet fait déjà** plutôt que d'introduire une troisième
forme, et écrire dans le rapport de tâche ce qui a été choisi et pourquoi.

Ajouter `ForgetIdentifier(string identifier)`, qui **passe par `IdentifierKey`** comme les trois
autres points d'entrée.

- [ ] **Step 5 : Ajouter le compteur de génération**

Un dictionnaire `identifiant → génération`, séparé des entrées de cache — **le compteur ne doit pas
vivre dans l'entrée**, qu'une expiration emporterait. `Generation(identifier)` le lit, `Forget`
l'incrémente, et `Store` prend la génération lue à l'entrée du handler et refuse d'inscrire si elle
a bougé.

Mettre à jour `CardDavAuthenticationHandler` pour lire la génération **avant** la lecture en base
et la passer au `Store`.

- [ ] **Step 6 : Câbler le contrôleur**

`DavCredentialsController` appelle `ForgetIdentifier` sur la régénération **et** sur l'allumage,
avec la même canonicalisation que le reste de la tranche
(`IdentityResolver.Canonical(AuthenticatedUser.Email)`).

- [ ] **Step 7 : Mettre les résidus à jour**

Dans `docs/superpowers/contacts-4a-residuals.md` et dans le § « ce que la tranche ne fait pas » des
plans b et c, marquer les deux comme fermés en 4c-ii-c. **Ne pas supprimer les lignes** : un résidu
fermé se lit, un résidu disparu se redécouvre.

- [ ] **Step 8 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement. **Les tests existants du limiteur et du cache doivent
rester verts sans être modifiés** : rien de ce qui existait ne change de sens.

- [ ] **Step 9 : Commit**

- sujet : `fix(dav): regenerer deverrouille l'identifiant, et le cache compte ses generations`
- corps : `Une fois bloque, saisir le BON secret repondait 429 ; et Forget ne pouvait` /
  `pas battre un Store concurrent pendant soixante secondes.`

---

## Vérification de fin de tranche

- [ ] `cd src && dotnet test` — les deux suites au vert.
- [ ] `cd src && dotnet build` — zéro avertissement.
- [ ] `cd src/frontend && npm test && npx tsc --noEmit && npm run lint` — propre.
- [ ] `git status` — `src/snoopy.microservice/ApiDocumentation.xml` non modifié.
- [ ] **Relire à l'œil** que toute route `/dav` porte `[Authorize(Policy = CardDavAuthenticationDefaults.PolicyName)]`. Aucun test ne peut le faire.
- [ ] Les plans a et b sont exécutés, le DDL joué, **et le rattrapage confirmé à zéro ligne restante.**
- [ ] Contre un vrai client, une fois : appairer, **créer une fiche depuis le téléphone**, la modifier depuis le webmail, la supprimer depuis le téléphone, et vérifier qu'à chaque étape les deux côtés convergent. C'est la seule vérification que la suite ne peut pas faire.
- [ ] Après cette dernière : **`ccs-caldavtester` est le travail de 4d**, et l'ordre est délibéré — un défaut trouvé sur un serveur qui suit le RFC est un défaut du serveur ; trouvé sur un serveur écrit contre un client, il est indiscernable d'une divergence de ce client.

## Les divergences nommées, rassemblées

Écrites ici pour que 4d les lise comme des choix et non comme des découvertes. Chacune est motivée
dans la spec ; le tableau existe pour qu'aucune revue n'ait à les rechercher.

| Divergence | Où | Pourquoi |
|---|---|---|
| `Depth` de `sync-collection` ignoré au lieu de `400` | tâche 2 | refuser un `Depth: 1` posé par habitude n'achète qu'un carnet qui ne s'appaire pas ; sabre ne le refuse pas non plus |
| `sync-level` absent replié sur **tout** `Depth`, `0` compris | tâche 2 | la lettre punit le client le plus proche de la norme sur sa première requête |
| `Depth` d'`addressbook-query` ignoré | tâche 5 | rendre zéro carte à qui en demande est le mode de défaillance que toute la spec chasse |
| `address-data` refusé dans `sync-collection` | tâche 2 | le § 10.4 ne le définit pas là, et le servir mettrait un lot de 500 cartes à 1 Mo sur le rapport que la décision 15 épargne |
| `allprop` verse des propriétés marquées « SHOULD NOT » | plan b, tâche 5 | un jeu stable rend les clients approximatifs prévisibles |
| `PROPPATCH` refuse **toute** propriété morte | plan b, tâche 11 | cohérent avec « pas de propriété mutable » ; `proppatch.xml` de `ccs-caldavtester` attend des succès |
| Les quatre rapports de principal du RFC 3744 non servis | plan b, tâche 5 | un carnet à un seul propriétaire n'a ni principal à chercher ni politique à publier |
| `supported-address-data-conversion` et la négociation par `Accept` hors tranche | plan b, tâche 8 | aucun client connu ne s'en sert |
| `Fold` compte des unités UTF-16 et non des octets | plan a, tâche 11 | le pliage est un `SHOULD` et une ligne longue est tolérée ; une paire de substitution coupée ne l'est pas |

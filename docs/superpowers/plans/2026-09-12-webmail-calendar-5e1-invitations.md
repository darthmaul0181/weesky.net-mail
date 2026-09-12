# Agenda 5e1 — recevoir une invitation : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Les rapports de sous-agents dans le scratchpad sont préfixés `5e1-task-N-…`.

**Spec :** [la conception 5e](../specs/2026-09-12-webmail-calendar-5e-invitations-design.md),
décisions 1 à 7 — toute décision citée ici (« décision N ») y renvoie, et elle fait foi en cas de
doute. Ce plan couvre **la phase 1 seulement** (recevoir) ; la phase 2 (inviter, décisions 8 à 13)
aura son propre plan, une fois 5e1 livrée et recettée.

**Goal :** un mail portant une invitation (`METHOD:REQUEST` ou `CANCEL`) est reconnu par le
lecteur, affiché dans un encart lisible, et un clic enregistre le rendez-vous dans l'agenda tel
que l'organisateur l'a écrit puis envoie la réponse par mail.

**Architecture :** le détail d'un message repère la partie calendrier dans le `BODYSTRUCTURE`,
la télécharge (une seule, sous le plafond de taille) et la transporte hors JSON ; le contrôleur
mail la fait lire par `InvitationReader`, qui consulte la base (adresses de l'utilisateur, ligne
`(user_id, uid)`) et remplit le bloc `invitation`. Répondre est un point d'entrée qui relit la
partie depuis IMAP, réécrit textuellement la ligne `ATTENDEE` de l'utilisateur, dépose par le
chemin `PUT` CalDAV, compose un `REPLY` et l'envoie par la session SMTP du compte. Le frontend
dessine l'encart depuis le bloc et le redessine depuis la réponse du point d'entrée.

**Tech stack :** .NET 10, ASP.NET Core, EF Core (InMemory en test), MailKit/MimeKit, Ical.Net
5.2.3, NodaTime, xUnit 2.9.3, Moq 4.20.72 ; React 18, TypeScript, TanStack Query, react-i18next,
Vitest + Testing Library.

## Global Constraints

- Backend : `cd src/snoopy.microservice && dotnet test` (jamais `--no-build` quand des fichiers
  de test sont ajoutés). Frontend : `cd src/frontend && npm run lint && npm run typecheck && npm test`.
- `src/snoopy.microservice/ApiDocumentation.xml` est régénéré par `dotnet test` : le réverter
  (`git checkout -- src/snoopy.microservice/ApiDocumentation.xml`) avant chaque commit.
- Messages de commit : deux lignes max, jamais de `@` en début ou fin ; passer le message par
  un heredoc `git commit -F -`, jamais par une here-string PowerShell.
- Dépôt en `autocrlf=true` : Edit/Write écrivent du LF, c'est attendu ; ne pas « corriger » les
  fins de ligne.
- Typographie française dans les locales `fr` : espace insécable (U+00A0) avant `:` `;` `!` `?`
  et à l'intérieur des guillemets « ». L'outil Edit remplace parfois l'insécable par une espace
  ordinaire : écrire les chaînes françaises avec ` ` dans le JSON, et vérifier par
  `npm test -- keys` (le test de typographie vit dans `src/frontend/src/locales/keys.test.ts`).
- Parité FR/EN des clés : `src/frontend/src/locales/parity.test.ts` doit rester vert.
- L'API omet les champs `null` (`WhenWritingNull`) : côté TypeScript un champ nullable est
  déclaré optionnel (`?`), jamais `| null`, et une fixture l'omet.
- Les enums du backend sortent en JSON par un `JsonStringEnumConverter` **sans** politique de
  casse : `Request`, `Cancel`, `Absent`, `Current`… tels que nommés en C#. Les unions TypeScript
  reprennent cette casse.
- Le rendez-vous entre en base **tel que l'organisateur l'a écrit**, octet pour octet hors la
  ligne `ATTENDEE` de l'utilisateur et la ligne `METHOD` (décision 4). Toute réécriture passe par
  `PartStatRewriter`, jamais par une resérialisation Ical.Net.
- Un nom DAV de création est `{Guid}.ics`, comme le fait `CalendarEventStore.CreateAsync` : la
  spec dit « dérivé du `UID` », l'intention est « un nom que le serveur choisit », et c'est le
  nom que 5a tire déjà.
- Rien n'entre dans l'agenda sans clic ; le détail d'un message ne fait **que lire**.

## Ce que ce plan suppose fait

5d livrée, branche `caldav-invites` à jour de `master`. Le compte de test de 4d/5d n'est pas
requis : toutes les tâches sont exécutables hors ligne, IMAP et SMTP étant simulés par Moq. La
recette avec un vrai mail de Google (le double `text/calendar` + `invite.ics`) se fait en session
avec l'utilisateur, après la tâche 6.

## Structure des fichiers

**Backend, créés**

| Fichier | Rôle |
|---|---|
| `Models/Calendar/StoredEventRef.cs` | une ligne trouvée par `(user_id, uid)` : id, agenda, nom DAV, `ics_raw` |
| `Services/UserAddresses.cs` | `IUserAddresses` : la liste d'adresses d'un compte (décision 2), partagée avec le principal DAV |
| `Models/Mail/MailInvitation.cs` | le bloc `invitation` du DTO, ses enums |
| `Models/Mail/MailCalendarPart.cs` | la partie calendrier téléchargée, hors JSON |
| `Services/Calendar/Invitations/InvitationParser.cs` | fichier → `ParsedInvitation` ; pur |
| `Services/Calendar/Invitations/PartStatRewriter.cs` | la réécriture textuelle de la ligne `ATTENDEE`, le retrait de `METHOD` ; pur |
| `Services/Calendar/Invitations/InvitationReader.cs` | `IInvitationReader` : adresses, ligne en base, `inCalendar`, le bloc |
| `Services/Calendar/Invitations/InvitationText.cs` | la date en toutes lettres FR/EN côté serveur (sujet et corps du `REPLY`) |
| `Services/Calendar/Invitations/ReplyComposer.cs` | le `MimeMessage` `REPLY` ; pur |
| `Services/Calendar/Invitations/InvitationResponder.cs` | `IInvitationResponder` : relit, réécrit, dépose, envoie, corbeille |
| `Services/RoleFolderLocator.cs` | `IRoleFolderLocator` : le dossier d'un rôle (`sent`, `trash`), extrait de `MailSender` |
| `Models/Calendar/RespondInvitationRequest.cs` | corps et réponse de `POST /api/Calendar/Invitations/Respond` |
| `Controllers/CalendarInvitationsController.cs` | le point d'entrée, hérite de `MailControllerBase` |
| `snoopy.microservice.Tests/Fixtures/Invitations/*.ics` | invitations anonymisées : Google, Outlook, Apple, Thunderbird, un `CANCEL`, un `REPLY`, un `CANCEL` d'occurrence |

**Backend, modifiés**

| Fichier | Changement |
|---|---|
| `Data/Preferences/PreferencesDbContext.cs` | index `(user_id, uid)` sur `calendar_events` |
| `docs/superpowers/webmail-calendar-tables.md` | le `KEY` dans le DDL, l'`ALTER TABLE` pour l'existant |
| `Repositories/ICalendarEventStore.cs`, `CalendarEventStore.cs` | `FindByUidAsync` |
| `Repositories/IDavCalendarWriter.cs`, `DavCalendarWriter.cs` | paramètre `cause` de `PutAsync` |
| `Controllers/DavPrincipalController.cs` | délègue sa liste d'adresses à `IUserAddresses` |
| `Services/Calendar/IcsGuards.cs` | `CheckParsed`, la moitié de `CheckAll` après l'analyse |
| `Services/MailMessageMapper.cs` | `IsCalendarPart`, `CalendarPart` (le choix de la partie) |
| `Services/ImapMessageCommands.cs` | télécharge la partie choisie dans `GetMessageAsync` |
| `Models/Mail/MailMessageDetail.cs` | `Invitation`, `CalendarPart` |
| `Controllers/MailMessagesController.cs` | appelle `IInvitationReader` |
| `Services/IMailSender.cs`, `MailSender.cs` | `SendBuiltAsync` (un message déjà composé), `IRoleFolderLocator` |
| `Configuration/ApplicationServicesConfiguration.cs` | enregistrements |

**Frontend, créés**

| Fichier | Rôle |
|---|---|
| `modules/mail/reader/invitationText.ts` | les phrases et la date en toutes lettres, depuis le bloc |
| `modules/mail/reader/InvitationCard.tsx` | l'encart, ses sept états |
| `probes/invitation-card.html` | la sonde de géométrie |

**Frontend, modifiés**

| Fichier | Changement |
|---|---|
| `modules/mail/api/mailTypes.ts` | `MailInvitation`, `invitation?` sur le détail |
| `api.js` | `respondInvitation` |
| `modules/mail/queries.ts` | `useRespondInvitation` |
| `modules/mail/reader/MessageReader.tsx` | l'encart entre l'en-tête et le corps ; les parties calendrier retirées des pièces jointes ; le départ sur `trashed` |
| `modules/calendar/EventPreview.tsx` | « Organisé par … » et les noms des invités |
| `modules/calendar/EventEditor.tsx` | retrait de l'indicateur `PARTSTAT` |
| `styles/mail.css` | `.invitation-card…` |
| `locales/{fr,en}/mail.json`, `calendar.json` | clés |

---

### Task 1 : le socle — index, recherche par `UID`, adresses de l'utilisateur, cause d'archivage

Quatre briques que tout le reste consomme et qu'un relecteur peut juger seules.

**Files:**
- Modify: `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs:154-157`
- Modify: `docs/superpowers/webmail-calendar-tables.md:56-59`
- Create: `src/snoopy.microservice/Models/Calendar/StoredEventRef.cs`
- Modify: `src/snoopy.microservice/Repositories/ICalendarEventStore.cs`, `CalendarEventStore.cs`
- Create: `src/snoopy.microservice/Services/UserAddresses.cs`
- Modify: `src/snoopy.microservice/Controllers/DavPrincipalController.cs:151-181`
- Modify: `src/snoopy.microservice/Repositories/IDavCalendarWriter.cs`, `DavCalendarWriter.cs:270-271`
- Modify: `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs`
- Test: `snoopy.microservice.Tests/Repositories/CalendarEventStoreTests.cs`, `Services/UserAddressesTests.cs` (créé), `Repositories/DavCalendarWriterTests.cs`, `Controllers/DavPrincipalTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record StoredEventRef(Guid Id, Guid CalendarId, string DavName, string IcsRaw);
  // ICalendarEventStore
  Task<IReadOnlyList<StoredEventRef>> FindByUidAsync(Guid userId, string uid, CancellationToken cancellationToken);
  // IUserAddresses
  Task<IReadOnlyList<string>> ForAccountAsync(User user, MailAccountConnection connection, CancellationToken cancellationToken);
  Task<IReadOnlyList<string>> ForPrincipalAsync(User user, CancellationToken cancellationToken);
  // IDavCalendarWriter
  Task<DavWriteOutcome> PutAsync(Guid userId, Guid calendarId, string davName, string ics,
      CancellationToken cancellationToken, bool createOnly = false, string? ifMatch = null,
      RevisionCause cause = RevisionCause.Put);
  ```

- [ ] **Step 1a : l'index.** Dans `PreferencesDbContext.OnModelCreating`, après la ligne
  `HasIndex(e => new { e.CalendarId, e.SyncSequence })` :

```csharp
modelBuilder.Entity<CalendarEvent>().HasIndex(e => new { e.UserId, e.Uid });
```

Dans `webmail-calendar-tables.md`, ajouter au DDL de `calendar_events`, après
`ix_calendar_events_seq` :

```sql
  KEY `ix_calendar_events_user_uid` (`user_id`, `uid`),
```

et, sous une nouvelle section `## Tranche 5e1 — un index` placée avant « Trois écarts… » :

````markdown
## Tranche 5e1 — un index

Le lecteur d'invitations cherche un `UID` reçu par mail dans tous les agendas de l'utilisateur
(spec 5e, décision 3). L'index unique `(calendar_id, uid)` ne sert pas cette recherche.

```sql
ALTER TABLE `calendar_events` ADD KEY `ix_calendar_events_user_uid` (`user_id`, `uid`);
```
````

- [ ] **Step 1b : `FindByUidAsync`, test d'abord.** Dans `CalendarEventStoreTests.cs` :

```csharp
[Fact]
public async Task FindByUid_ListsTheDefaultCalendarFirst()
{
    var (db, user, defaultCalendar) = await CalendarStoreTestFactory.SeedAsync(Guid.NewGuid().ToString());
    var calendars = CalendarStoreTestFactory.Calendars(db);
    var other = (await calendars.CreateAsync(user,
        new CalendarWrite("Travail", "", "#00f", "Europe/Brussels"), "Europe/Brussels", CancellationToken.None)).Value;
    var store = CalendarStoreTestFactory.Events(db);
    var write = CalendarStoreTestFactory.Write(other, summary: "Réunion");
    await store.CreateAsync(user, write, CancellationToken.None);
    var context = new PreferencesTestDbContext(db);
    var inOther = await context.CalendarEvents.SingleAsync();
    // The same UID in the default calendar, as a phone would have put it.
    context.CalendarEvents.Add(new CalendarEvent
    {
        Id = Guid.NewGuid(), CalendarId = defaultCalendar, UserId = user, Uid = inOther.Uid,
        DavName = "phone-name.ics", IcsRaw = inOther.IcsRaw, StartsAt = inOther.StartsAt, EndsAt = inOther.EndsAt,
        FirstOccurrence = inOther.FirstOccurrence, LastOccurrence = inOther.LastOccurrence,
    });
    await context.SaveChangesAsync();

    var found = await store.FindByUidAsync(user, inOther.Uid, CancellationToken.None);

    Assert.Equal(2, found.Count);
    Assert.Equal(defaultCalendar, found[0].CalendarId);
    Assert.Equal("phone-name.ics", found[0].DavName);
    Assert.Empty(await store.FindByUidAsync(Guid.NewGuid(), inOther.Uid, CancellationToken.None));
}
```

Vérifier la signature réelle de `CalendarWrite` dans `Models/Calendar/CalendarWrite.cs` et adapter
l'appel `CreateAsync` (les quatre champs nom, description, couleur, fuseau, dans l'ordre du record).

- [ ] **Step 1c : rouge.** `dotnet test --filter FindByUid` → échec de compilation (`FindByUidAsync` absent).

- [ ] **Step 1d : implémentation.** `Models/Calendar/StoredEventRef.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>One row of <c>calendar_events</c> as the invitation reader needs it: where it lives,
/// the name a rewrite must reuse, and the bytes the SEQUENCE and the user's PARTSTAT are read from.</summary>
public sealed record StoredEventRef(Guid Id, Guid CalendarId, string DavName, string IcsRaw);
```

Dans `ICalendarEventStore` :

```csharp
/// <summary>Every event of the user carrying this UID, the default calendar's first, then the
/// sidebar's order — a UID is unique per calendar, not per user (RFC 4791 § 4.1).</summary>
Task<IReadOnlyList<StoredEventRef>> FindByUidAsync(Guid userId, string uid, CancellationToken cancellationToken);
```

Dans `CalendarEventStore`, après `SearchAsync` :

```csharp
public async Task<IReadOnlyList<StoredEventRef>> FindByUidAsync(
    Guid userId, string uid, CancellationToken cancellationToken) =>
    await context.CalendarEvents.AsNoTracking()
        .Where(e => e.UserId == userId && e.Uid == uid)
        .Join(context.Calendars, e => e.CalendarId, c => c.Id, (e, c) => new { Event = e, Calendar = c })
        .OrderBy(x => x.Calendar.DavName == CalendarStore.DefaultDavName ? 0 : 1)
        .ThenBy(x => x.Calendar.Order).ThenBy(x => x.Calendar.DisplayName)
        .Select(x => new StoredEventRef(x.Event.Id, x.Event.CalendarId, x.Event.DavName, x.Event.IcsRaw))
        .ToListAsync(cancellationToken);
```

- [ ] **Step 1e : vert, commit.** `dotnet test --filter CalendarEventStore` ; réverter
  `ApiDocumentation.xml` ;

```bash
git add -A src/snoopy.microservice/Data src/snoopy.microservice/Models/Calendar/StoredEventRef.cs src/snoopy.microservice/Repositories docs/superpowers/webmail-calendar-tables.md src/snoopy.microservice/snoopy.microservice.Tests/Repositories/CalendarEventStoreTests.cs
git commit -F - <<'EOF'
feat(agenda): recherche d'un UID dans tous les agendas d'un utilisateur

Index (user_id, uid) et FindByUidAsync, l'agenda par défaut en tête (5e1, décision 3).
EOF
```

- [ ] **Step 1f : `UserAddresses`, test d'abord.** Créer
  `snoopy.microservice.Tests/Services/UserAddressesTests.cs` :

```csharp
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class UserAddressesTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();
    private readonly User _user = new("alice@weesky.be") { WebmailUid = WebmailUid };
    private readonly Mock<IAccountInfoProvider> _accounts = new();
    private readonly Mock<ISendingIdentityStore> _identities = new();

    private UserAddresses Create()
    {
        _accounts.Setup(a => a.GetAccountInfoAsync(_user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AccountInfo
            {
                Domains = [new Domain { Name = "weesky.be" }, new Domain { Name = "Weesky.net" }],
            }));
        _identities.Setup(i => i.GetAsync(WebmailUid, "", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { Address = "Alice.Pro@weesky.be" }]);
        _identities.Setup(i => i.GetAllAsync(WebmailUid, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { Address = "Alice.Pro@weesky.be" }, new SendingIdentity { Address = "me@gmail.com" }]);
        return new UserAddresses(_accounts.Object, _identities.Object, NullLogger<UserAddresses>.Instance);
    }

    [Fact]
    public async Task Primary_IsTheAddressItsOtherDomainsAndItsOwnIdentities_LowerCased()
    {
        var conn = TestConnections.Primary("alice@weesky.be", "pw");

        var addresses = await Create().ForAccountAsync(_user, conn, CancellationToken.None);

        Assert.Equal(["alice@weesky.be", "alice@weesky.net", "alice.pro@weesky.be"], addresses);
    }

    [Fact]
    public async Task Connected_IsItsLoginAndItsOwnIdentities()
    {
        var id = Guid.NewGuid().ToString();
        var conn = TestConnections.Connected(id, "Me@Gmail.com", "pw");
        _identities.Setup(i => i.GetAsync(WebmailUid, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { Address = "alias@gmail.com" }]);

        var addresses = await Create().ForAccountAsync(_user, conn, CancellationToken.None);

        Assert.Equal(["me@gmail.com", "alias@gmail.com"], addresses);
        _accounts.Verify(a => a.GetAccountInfoAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Principal_KeepsWhat5cPublished_EveryIdentityOfEveryAccount()
    {
        var addresses = await Create().ForPrincipalAsync(_user, CancellationToken.None);

        Assert.Equal(["alice@weesky.be", "alice@weesky.net", "alice.pro@weesky.be", "me@gmail.com"], addresses);
    }

    [Fact]
    public async Task Primary_WhenThePlatformCannotAnswer_IsTheAddressAlone_AndWarns()
    {
        var sut = Create();
        _accounts.Setup(a => a.GetAccountInfoAsync(_user, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AccountInfo>("down"));

        var addresses = await sut.ForAccountAsync(_user, TestConnections.Primary("alice@weesky.be", "pw"), CancellationToken.None);

        Assert.Equal(["alice@weesky.be", "alice.pro@weesky.be"], addresses);
    }
}
```

Vérifier le nom et les propriétés du type `Domain` dans `Models/` (`AccountInfo.Domains` est un
`IEnumerable<Domain>` ; le principal lit `domain.Name`) et adapter l'initialiseur.

- [ ] **Step 1g : rouge**, puis implémentation. `Services/UserAddresses.cs` :

```csharp
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The addresses a user answers to, lower-cased and without duplicates. Two readings: the one the
/// DAV principal publishes as <c>calendar-user-address-set</c> (5c, décision 5 — the home
/// address, its other domains, every stored identity), and the one an invitation is matched
/// against for ONE mail account (5e, décision 2 — the home list with the home account's own
/// identities, or a connected account's login and its own identities).
/// </summary>
public interface IUserAddresses
{
    Task<IReadOnlyList<string>> ForAccountAsync(User user, MailAccountConnection connection, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ForPrincipalAsync(User user, CancellationToken cancellationToken);
}

internal sealed class UserAddresses(
    IAccountInfoProvider accounts, ISendingIdentityStore identities, ILogger<UserAddresses> logger) : IUserAddresses
{
    public async Task<IReadOnlyList<string>> ForPrincipalAsync(User user, CancellationToken cancellationToken) =>
        Distinct(await HomeAsync(user, cancellationToken),
            (await identities.GetAllAsync(user.WebmailUid, cancellationToken)).Select(i => i.Address));

    public async Task<IReadOnlyList<string>> ForAccountAsync(
        User user, MailAccountConnection connection, CancellationToken cancellationToken)
    {
        var own = (await identities.GetAsync(user.WebmailUid, connection.StorageAccountId, cancellationToken))
            .Select(i => i.Address);
        return connection.AccountId == MailAccountConnection.Primary
            ? Distinct(await HomeAsync(user, cancellationToken), own)
            : Distinct([connection.Username], own);
    }

    /// <summary>The home address first, then the same name on the account's other domains. A
    /// platform that cannot answer is a warning and the home address alone — never a failure.</summary>
    private async Task<List<string>> HomeAsync(User user, CancellationToken cancellationToken)
    {
        List<string> addresses = [user.Email];
        var account = await accounts.GetAccountInfoAsync(user, cancellationToken);
        if (account.IsSuccess)
            addresses.AddRange(account.Value.Domains
                .Where(domain => !string.Equals(domain.Name, user.Domain, StringComparison.OrdinalIgnoreCase))
                .Select(domain => $"{user.Name}@{domain.Name}"));
        else
            logger.LogWarning(
                "The account's domains were unavailable ({Reason}); the address list holds the primary address alone",
                account.Error);
        return addresses;
    }

    private static IReadOnlyList<string> Distinct(IEnumerable<string> first, IEnumerable<string> then) =>
        [.. first.Concat(then).Select(a => a.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal)];
}
```

Dans `DavPrincipalController` : remplacer le paramètre de constructeur `ISendingIdentityStore identities`
par `IUserAddresses addresses` (garder `IAccountInfoProvider accounts` seulement s'il sert
ailleurs dans le fichier ; sinon le retirer), et réduire `AddressesOrNullAsync` à :

```csharp
private async Task<IReadOnlyList<string>?> AddressesOrNullAsync(DavResourceKind kind,
    DavPropertyRequest? request, User user, CancellationToken cancellationToken)
{
    var asked = request is not null
        && (request.Mode is DavPropertyMode.AllProp
            || request.Names.Contains(DavPrincipalProperties.CalendarUserAddressSet));
    if (!asked || kind is not (DavResourceKind.Principal or DavResourceKind.PrincipalCollection))
        return null;
    return await addresses.ForPrincipalAsync(user, cancellationToken);
}
```

Garder le `<remarks>` existant (il explique le « seulement quand on le demande »). Dans
`DavPrincipalTests` (et tout autre test qui construit ce contrôleur, `grep -rn "new DavPrincipalController"`),
passer `new UserAddresses(accounts.Object, identities.Object, NullLogger<UserAddresses>.Instance)`
à la place du store d'identités. Enregistrer dans `ApplicationServicesConfiguration` :

```csharp
services.AddScoped<IUserAddresses, UserAddresses>();
```

- [ ] **Step 1h : vert, commit.** `dotnet test --filter "UserAddresses|DavPrincipal|AppleDiscovery"` ;

```bash
git commit -F - <<'EOF'
refactor(dav): la liste d'adresses du principal devient un service partagé

UserAddresses : la lecture du principal (5c) et celle d'un compte de messagerie (5e1, décision 2).
EOF
```

- [ ] **Step 1i : la cause d'archivage, test d'abord.** Dans `DavCalendarWriterTests.cs` :

```csharp
[Fact]
public async Task PuttingWithACause_ArchivesTheReplacedVersionUnderIt()
{
    await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Standup"), None);

    await writer.PutAsync(userId, calendarId, "a.ics", Event("u1", "Standup, moved"), None,
        cause: RevisionCause.Webmail);

    var revision = await context.CalendarRevisions.SingleAsync(r => r.DavName == "a.ics");
    Assert.Equal(RevisionCause.Webmail, revision.Cause);
}
```

- [ ] **Step 1j : rouge, puis implémentation.** Dans `IDavCalendarWriter.PutAsync`, ajouter le
  paramètre `RevisionCause cause = RevisionCause.Put` en dernière position (et une phrase au
  `<summary>` : « <paramref name="cause"/> names the door for the archive — the webmail's
  invitation writes pass <c>Webmail</c> »). Dans `DavCalendarWriter` : `PutAsync` transmet `cause`
  à `GateAsync`, qui gagne le paramètre et remplace `RevisionCause.Put` par `cause` dans l'appel
  `sync.ArchiveAsync`. `using weesky.Snoopy.Microservice.Data.Preferences;` dans l'interface.

- [ ] **Step 1k : vert, commit.** `dotnet test --filter "DavCalendarWriter|CalDav"` ;

```bash
git commit -F - <<'EOF'
feat(caldav): le writer archive sous la cause que l'appelant nomme

PutAsync gagne un paramètre cause, Put par défaut (5e1, décision 4).
EOF
```

---

### Task 2 : lire une invitation et réécrire une réponse — le code pur

Le fichier calendrier devient un `ParsedInvitation` ; la ligne `ATTENDEE` de l'utilisateur est
réécrite octet pour octet. Aucune dépendance IMAP ni base : tout se teste sur des fixtures.

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/MailInvitation.cs`
- Create: `src/snoopy.microservice/Services/Calendar/Invitations/InvitationParser.cs`
- Create: `src/snoopy.microservice/Services/Calendar/Invitations/PartStatRewriter.cs`
- Modify: `src/snoopy.microservice/Services/Calendar/IcsGuards.cs:59-66`
- Create: `snoopy.microservice.Tests/Fixtures/Invitations/{google-request,outlook-request,apple-request,thunderbird-request,google-cancel,google-reply,occurrence-cancel}.ics`
- Test: `snoopy.microservice.Tests/Services/Calendar/Invitations/InvitationParserTests.cs`, `PartStatRewriterTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum InvitationMethod { Request, Cancel }
  public enum InvitationPresence { Absent, Current, Outdated, Newer, Cancelled }
  public sealed record InvitationPerson(string Email, string? Name);
  public sealed class MailInvitation { … }                       // le bloc du DTO, voir 2a
  internal sealed record InvitationAttendee(string Email, string? Name, string? PartStat);
  internal sealed record ParsedInvitation(
      InvitationMethod Method, string Uid, int Sequence, bool OccurrenceOnly,
      string? Summary, string? Location, bool Repeats, bool IsAllDay,
      DateTime? Start, DateTime? End, DateOnly? StartDate, DateOnly? EndDateExclusive,
      InvitationPerson? Organizer, IReadOnlyList<InvitationAttendee> Attendees,
      string? DtStartLine);
  internal sealed record InvitationReading(ParsedInvitation? Invitation, bool Ignored, string? Reason);
  internal static class InvitationParser
  {
      internal static InvitationReading Read(string ics);
      internal static int SequenceOf(string ics);                 // 0 quand illisible ou absent
      internal static string? PartStatOf(string ics, string address);
  }
  internal static class PartStatRewriter
  {
      internal static string? Rewrite(string ics, string address, string partStat);
      internal static string StripMethod(string ics);
      internal static string? LineOf(string ics, string property); // la ligne logique dépliée, ex. "DTSTART;TZID=Europe/Brussels:20261010T193000"
  }
  // IcsGuards
  internal static IcsProblem? CheckParsed(string ics, IcsCalendar? parsed);
  ```

- [ ] **Step 2a : le bloc.** `Models/Mail/MailInvitation.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

public enum InvitationMethod { Request, Cancel }

/// <summary>What the calendar holds for the received UID (spec 5e, décision 3).</summary>
public enum InvitationPresence { Absent, Current, Outdated, Newer, Cancelled }

public sealed record InvitationPerson(string Email, string? Name);

/// <summary>
/// The <c>invitation</c> block of a message detail, and the answer of the respond endpoint. Filled
/// when the message carries a calendar part whose METHOD is REQUEST or CANCEL; <see cref="Unreadable"/>
/// when that part fails the guards every stored file passes, in which case only <see cref="Part"/>
/// and <see cref="Reason"/> are set.
/// </summary>
public sealed class MailInvitation
{
    public InvitationMethod Method { get; init; }
    public string Uid { get; init; } = string.Empty;
    public int Sequence { get; init; }
    public string? Summary { get; init; }
    /// <summary>UTC, like the API's occurrences; null on a whole-day invitation.</summary>
    public DateTime? Start { get; init; }
    public DateTime? End { get; init; }
    /// <summary>Set on a whole-day invitation; the end is exclusive, as the occurrences spell it.</summary>
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDateExclusive { get; init; }
    public bool IsAllDay { get; init; }
    public string? Location { get; init; }
    public bool Repeats { get; init; }
    public InvitationPerson? Organizer { get; init; }
    public IReadOnlyList<InvitationPerson> Attendees { get; init; } = [];
    /// <summary>The user's address among the ATTENDEEs, or null when the invitation was forwarded (décision 2).</summary>
    public string? AddressedTo { get; init; }
    /// <summary>The PARTSTAT the received file carries for the user — NEEDS-ACTION on a fresh invitation.</summary>
    public string? FilePartStat { get; init; }
    /// <summary>The PARTSTAT the stored file carries for the user — the answer given; null when not in the calendar.</summary>
    public string? SavedPartStat { get; init; }
    public InvitationPresence InCalendar { get; init; }
    public Guid? CalendarId { get; init; }
    /// <summary>The file targets one date of a series (RECURRENCE-ID without a master): shown, never applied (décision 1 bis).</summary>
    public bool OccurrenceOnly { get; init; }
    /// <summary>The MIME part specifier the respond endpoint re-reads the file by (décision 4).</summary>
    public string Part { get; init; } = string.Empty;
    public bool Unreadable { get; init; }
    public string? Reason { get; init; }
}
```

- [ ] **Step 2b : les fixtures.** Créer `snoopy.microservice.Tests/Fixtures/Invitations/` et vérifier
  dans `snoopy.microservice.Tests.csproj` que le dossier `Fixtures` est copié en sortie (le motif
  existant pour `Fixtures/ICalendar/**`) ; ajouter `Invitations/**` de la même façon si le glob
  n'est pas `Fixtures/**`. Fins de ligne CRLF dans les sept fichiers (RFC 5545 § 3.1), à créer
  par PowerShell : `Set-Content -NoNewline -Path … -Value ($lines -join "`r`n")` ou en écrivant
  le fichier puis `(Get-Content -Raw f) -replace "(?<!\r)\n", "`r`n" | Set-Content -NoNewline f`.
  Vérifier ensuite `git ls-files --eol` ne les normalise pas : ajouter à `.gitattributes`
  `*.ics -text` s'il n'y est pas déjà (il l'est sans doute pour `Fixtures/ICalendar`).

`google-request.ics` — la forme de Google : `VTIMEZONE`, `ORGANIZER` avec `CN`, `ATTENDEE`
repliées, `X-GOOGLE-CONFERENCE`, `SEQUENCE:0`, deux invités dont l'utilisateur :

```
BEGIN:VCALENDAR
PRODID:-//Google Inc//Google Calendar 70.9054//EN
VERSION:2.0
CALSCALE:GREGORIAN
METHOD:REQUEST
BEGIN:VTIMEZONE
TZID:Europe/Brussels
X-LIC-LOCATION:Europe/Brussels
BEGIN:DAYLIGHT
TZOFFSETFROM:+0100
TZOFFSETTO:+0200
TZNAME:CEST
DTSTART:19700329T020000
RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU
END:DAYLIGHT
BEGIN:STANDARD
TZOFFSETFROM:+0200
TZOFFSETTO:+0100
TZNAME:CET
DTSTART:19701025T030000
RRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU
END:STANDARD
END:VTIMEZONE
BEGIN:VEVENT
DTSTART;TZID=Europe/Brussels:20261010T193000
DTEND;TZID=Europe/Brussels:20261010T223000
DTSTAMP:20260912T090000Z
ORGANIZER;CN=Marc Dupont:mailto:marc.dupont@example.org
UID:7c2e1c4a9f0b4d2e8a1c3b5d7e9f1a2b@google.com
ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED;CN=Marc Dupont
 ;X-NUM-GUESTS=0:mailto:marc.dupont@example.org
ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE
 ;CN=Alice;X-NUM-GUESTS=0:mailto:Alice@Weesky.be
ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE
 ;CN=jean@example.net;X-NUM-GUESTS=0:mailto:jean@example.net
X-GOOGLE-CONFERENCE:https://meet.google.com/abc-defg-hij
CREATED:20260912T085900Z
DESCRIPTION:On se retrouve chez moi.
LAST-MODIFIED:20260912T085900Z
LOCATION:Rue des Lilas 12\, 1000 Bruxelles
SEQUENCE:0
STATUS:CONFIRMED
SUMMARY:Dîner chez Marc
TRANSP:OPAQUE
END:VEVENT
END:VCALENDAR
```

`outlook-request.ics` — Outlook : `X-MICROSOFT-*`, `ATTENDEE` avec `CN` entre guillemets,
adresse de l'utilisateur en majuscules, `SEQUENCE:2`, répétition hebdomadaire :

```
BEGIN:VCALENDAR
METHOD:REQUEST
PRODID:Microsoft Exchange Server 2010
VERSION:2.0
BEGIN:VTIMEZONE
TZID:Romance Standard Time
BEGIN:STANDARD
DTSTART:16010101T030000
TZOFFSETFROM:+0200
TZOFFSETTO:+0100
RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=-1SU;BYMONTH=10
END:STANDARD
BEGIN:DAYLIGHT
DTSTART:16010101T020000
TZOFFSETFROM:+0100
TZOFFSETTO:+0200
RRULE:FREQ=YEARLY;INTERVAL=1;BYDAY=-1SU;BYMONTH=3
END:DAYLIGHT
END:VTIMEZONE
BEGIN:VEVENT
ORGANIZER;CN="Service RH":mailto:rh@example.com
ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE;CN="ALICE":mailto:
 ALICE@WEESKY.BE
DESCRIPTION;LANGUAGE=fr-FR:Point hebdomadaire.\n
UID:040000008200E00074C5B7101A82E00800000000A0B1C2D3E4F5DA01000000000000000
 010000000ABCDEF0123456789ABCDEF0123456789
SUMMARY;LANGUAGE=fr-FR:Point équipe
DTSTART;TZID=Romance Standard Time:20261005T100000
DTEND;TZID=Romance Standard Time:20261005T103000
RRULE:FREQ=WEEKLY;INTERVAL=1;BYDAY=MO;UNTIL=20261221T235959Z
CLASS:PUBLIC
PRIORITY:5
DTSTAMP:20260912T100000Z
TRANSP:OPAQUE
STATUS:CONFIRMED
SEQUENCE:2
LOCATION;LANGUAGE=fr-FR:Salle Bleue
X-MICROSOFT-CDO-APPT-SEQUENCE:2
X-MICROSOFT-CDO-BUSYSTATUS:BUSY
X-MICROSOFT-CDO-INTENDEDSTATUS:BUSY
X-MICROSOFT-DISALLOW-COUNTER:FALSE
END:VEVENT
END:VCALENDAR
```

`apple-request.ics` — iCloud : journée entière, pas de `CN`, `SEQUENCE` absente :

```
BEGIN:VCALENDAR
VERSION:2.0
PRODID:-//Apple Inc.//iCloud Web Calendar 2519B31//EN
CALSCALE:GREGORIAN
METHOD:REQUEST
BEGIN:VEVENT
TRANSP:TRANSPARENT
DTEND;VALUE=DATE:20261102
UID:9B1DEB4D-3B7D-4BAD-9BDD-2B0D7B3DCB6D
DTSTAMP:20260912T110000Z
X-APPLE-TRAVEL-ADVISORY-BEHAVIOR:AUTOMATIC
ATTENDEE;CN=Alice;CUTYPE=INDIVIDUAL;PARTSTAT=NEEDS-ACTION;ROLE=REQ-PARTICIPANT;RSVP=TRUE:mailto:alice@weesky.be
DTSTART;VALUE=DATE:20261101
ORGANIZER;EMAIL=paul@example.org:mailto:paul@example.org
SUMMARY:Toussaint
END:VEVENT
END:VCALENDAR
```

`thunderbird-request.ics` — Thunderbird : `X-MOZ-*`, l'utilisateur invité sur un **alias**
(`alice@weesky.net`) :

```
BEGIN:VCALENDAR
PRODID:-//Mozilla.org/NONSGML Mozilla Calendar V1.1//EN
VERSION:2.0
METHOD:REQUEST
BEGIN:VEVENT
CREATED:20260912T120000Z
LAST-MODIFIED:20260912T120500Z
DTSTAMP:20260912T120500Z
UID:c1d2e3f4-5a6b-7c8d-9e0f-1a2b3c4d5e6f
SUMMARY:Répétition
ORGANIZER;CN=Léa:mailto:lea@example.net
ATTENDEE;CN=Léa;PARTSTAT=ACCEPTED;ROLE=CHAIR:mailto:lea@example.net
ATTENDEE;PARTSTAT=NEEDS-ACTION;ROLE=REQ-PARTICIPANT;RSVP=TRUE:mailto:alice@weesky.net
DTSTART:20261015T170000Z
DTEND:20261015T190000Z
LOCATION:Studio B
SEQUENCE:1
TRANSP:OPAQUE
X-MOZ-SEND-INVITATIONS:TRUE
END:VEVENT
END:VCALENDAR
```

`google-cancel.ics` — l'annulation de la série de Google (`SEQUENCE:1`, `STATUS:CANCELLED`) :

```
BEGIN:VCALENDAR
PRODID:-//Google Inc//Google Calendar 70.9054//EN
VERSION:2.0
CALSCALE:GREGORIAN
METHOD:CANCEL
BEGIN:VEVENT
DTSTART:20261010T173000Z
DTEND:20261010T203000Z
DTSTAMP:20260913T090000Z
ORGANIZER;CN=Marc Dupont:mailto:marc.dupont@example.org
UID:7c2e1c4a9f0b4d2e8a1c3b5d7e9f1a2b@google.com
ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE
 ;CN=Alice;X-NUM-GUESTS=0:mailto:alice@weesky.be
CREATED:20260912T085900Z
LAST-MODIFIED:20260913T090000Z
SEQUENCE:1
STATUS:CANCELLED
SUMMARY:Dîner chez Marc
TRANSP:OPAQUE
END:VEVENT
END:VCALENDAR
```

`google-reply.ics` — une réponse (ignorée en 5e1) :

```
BEGIN:VCALENDAR
PRODID:-//Google Inc//Google Calendar 70.9054//EN
VERSION:2.0
CALSCALE:GREGORIAN
METHOD:REPLY
BEGIN:VEVENT
DTSTART:20261010T173000Z
DTEND:20261010T203000Z
DTSTAMP:20260913T100000Z
ORGANIZER:mailto:alice@weesky.be
UID:aaaa1111-bbbb-2222-cccc-3333dddd4444
ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=DECLINED;CN=Marc Dupont:mailto:marc.dupont@example.org
SEQUENCE:0
SUMMARY:Declined: Dîner
END:VEVENT
END:VCALENDAR
```

`occurrence-cancel.ics` — l'annulation d'**une date** d'une série (`RECURRENCE-ID`, aucun maître) :

```
BEGIN:VCALENDAR
PRODID:-//Google Inc//Google Calendar 70.9054//EN
VERSION:2.0
METHOD:CANCEL
BEGIN:VEVENT
DTSTART:20261012T080000Z
DTEND:20261012T083000Z
DTSTAMP:20260913T110000Z
ORGANIZER;CN=Service RH:mailto:rh@example.com
UID:040000008200E00074C5B7101A82E00800000000A0B1C2D3E4F5DA01000000000000000010000000ABCDEF0123456789ABCDEF0123456789
ATTENDEE;PARTSTAT=NEEDS-ACTION;CN=Alice:mailto:alice@weesky.be
RECURRENCE-ID:20261012T080000Z
SEQUENCE:3
STATUS:CANCELLED
SUMMARY:Point équipe
END:VEVENT
END:VCALENDAR
```

- [ ] **Step 2c : les tests du lecteur.** `snoopy.microservice.Tests/Services/Calendar/Invitations/InvitationParserTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

public sealed class InvitationParserTests
{
    internal static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Invitations", name + ".ics"));

    [Theory]
    [InlineData("google-request", "Dîner chez Marc", "marc.dupont@example.org", 3, 0)]
    [InlineData("outlook-request", "Point équipe", "rh@example.com", 1, 2)]
    [InlineData("apple-request", "Toussaint", "paul@example.org", 1, 0)]
    [InlineData("thunderbird-request", "Répétition", "lea@example.net", 2, 1)]
    public void ReadsARequestFromEveryBigClient(string name, string summary, string organizer, int attendees, int sequence)
    {
        var reading = InvitationParser.Read(Fixture(name));

        var invitation = Assert.IsType<ParsedInvitation>(reading.Invitation);
        Assert.False(reading.Ignored);
        Assert.Equal(InvitationMethod.Request, invitation.Method);
        Assert.Equal(summary, invitation.Summary);
        Assert.Equal(organizer, invitation.Organizer!.Email);
        Assert.Equal(attendees, invitation.Attendees.Count);
        Assert.Equal(sequence, invitation.Sequence);
        Assert.False(invitation.OccurrenceOnly);
    }

    [Fact]
    public void Google_PlacesTheZonedStartInUtc_AndKeepsTheDtStartLine()
    {
        var invitation = InvitationParser.Read(Fixture("google-request")).Invitation!;

        Assert.Equal(new DateTime(2026, 10, 10, 17, 30, 0, DateTimeKind.Utc), invitation.Start);
        Assert.Equal(new DateTime(2026, 10, 10, 20, 30, 0, DateTimeKind.Utc), invitation.End);
        Assert.False(invitation.IsAllDay);
        Assert.Equal("Rue des Lilas 12, 1000 Bruxelles", invitation.Location);
        Assert.Equal("DTSTART;TZID=Europe/Brussels:20261010T193000", invitation.DtStartLine);
        // The user's ATTENDEE keeps the file's casing here; matching is the reader's job.
        Assert.Contains(invitation.Attendees, a => a.Email == "Alice@Weesky.be" && a.PartStat == "NEEDS-ACTION");
        Assert.Equal("Marc Dupont", invitation.Organizer!.Name);
    }

    [Fact]
    public void Outlook_Repeats_AndUnfoldsTheAttendeeAddress()
    {
        var invitation = InvitationParser.Read(Fixture("outlook-request")).Invitation!;

        Assert.True(invitation.Repeats);
        Assert.Equal("ALICE@WEESKY.BE", invitation.Attendees[0].Email);
        Assert.Equal("ALICE", invitation.Attendees[0].Name);
        Assert.Equal("Service RH", invitation.Organizer!.Name);
    }

    [Fact]
    public void Apple_IsAWholeDay_WithDatesAndNoInstants()
    {
        var invitation = InvitationParser.Read(Fixture("apple-request")).Invitation!;

        Assert.True(invitation.IsAllDay);
        Assert.Null(invitation.Start);
        Assert.Equal(new DateOnly(2026, 11, 1), invitation.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 2), invitation.EndDateExclusive);
        Assert.Null(invitation.Organizer!.Name);
    }

    [Fact]
    public void Cancel_IsRead_WithItsSequence()
    {
        var invitation = InvitationParser.Read(Fixture("google-cancel")).Invitation!;

        Assert.Equal(InvitationMethod.Cancel, invitation.Method);
        Assert.Equal(1, invitation.Sequence);
    }

    [Fact]
    public void Reply_IsIgnored_NotUnreadable()
    {
        var reading = InvitationParser.Read(Fixture("google-reply"));

        Assert.Null(reading.Invitation);
        Assert.True(reading.Ignored);
        Assert.Null(reading.Reason);
    }

    [Fact]
    public void NoMethod_IsIgnored()
    {
        var reading = InvitationParser.Read(Fixture("google-request").Replace("METHOD:REQUEST\r\n", ""));

        Assert.True(reading.Ignored);
    }

    [Fact]
    public void AnOccurrenceAlone_IsOccurrenceOnly()
    {
        var invitation = InvitationParser.Read(Fixture("occurrence-cancel")).Invitation!;

        Assert.True(invitation.OccurrenceOnly);
        Assert.Equal(3, invitation.Sequence);
        Assert.Equal(new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc), invitation.Start);
    }

    [Fact]
    public void AFileTheGuardsRefuse_IsUnreadable_WithTheReason()
    {
        // No DTSTART: IcsGuards.CheckStart refuses what every stored file must carry.
        var reading = InvitationParser.Read(Fixture("thunderbird-request").Replace("DTSTART:20261015T170000Z\r\n", ""));

        Assert.Null(reading.Invitation);
        Assert.False(reading.Ignored);
        Assert.NotNull(reading.Reason);
    }

    [Fact]
    public void Garbage_IsUnreadable()
    {
        var reading = InvitationParser.Read("not a calendar");

        Assert.False(reading.Ignored);
        Assert.NotNull(reading.Reason);
    }

    [Fact]
    public void SequenceOf_ReadsTheMaster_AndZeroWhenAbsentOrUnreadable()
    {
        Assert.Equal(2, InvitationParser.SequenceOf(Fixture("outlook-request")));
        Assert.Equal(0, InvitationParser.SequenceOf(Fixture("apple-request")));
        Assert.Equal(0, InvitationParser.SequenceOf("garbage"));
    }

    [Fact]
    public void PartStatOf_MatchesTheAddressWithoutCase()
    {
        Assert.Equal("NEEDS-ACTION", InvitationParser.PartStatOf(Fixture("outlook-request"), "alice@weesky.be"));
        Assert.Null(InvitationParser.PartStatOf(Fixture("outlook-request"), "nobody@weesky.be"));
    }
}
```

- [ ] **Step 2d : rouge**, puis `IcsGuards.CheckParsed`. Dans `IcsGuards`, réécrire `CheckAll` :

```csharp
internal static IcsProblem? CheckAll(string ics, out IcsCalendar? parsed)
{
    parsed = null;
    if (CheckSize(ics) is { } tooLarge) return tooLarge;
    parsed = IcsDocument.TryLoad(ics);
    return CheckParsed(ics, parsed);
}

/// <summary>Everything <see cref="CheckAll"/> judges after the parse, for a caller that already
/// holds the model — the invitation reader, which reads METHOD before it judges the rest.</summary>
internal static IcsProblem? CheckParsed(string ics, IcsCalendar? parsed) =>
    Check(ics, parsed) ?? CheckDensity(parsed!) ?? CheckExpansion(parsed!)
    ?? CheckOverrides(parsed!) ?? CheckStart(parsed!);
```

- [ ] **Step 2e : le lecteur.** `Services/Calendar/Invitations/InvitationParser.cs` :

```csharp
using Ical.Net.CalendarComponents;
using weesky.Snoopy.Microservice.Models.Mail;
using IcsCalendar = Ical.Net.Calendar;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

internal sealed record InvitationAttendee(string Email, string? Name, string? PartStat);

/// <summary>A received calendar part, read once. <see cref="DtStartLine"/> is the file's own
/// DTSTART line, unfolded, which the REPLY copies verbatim so the organizer's agenda pairs it.</summary>
internal sealed record ParsedInvitation(
    InvitationMethod Method, string Uid, int Sequence, bool OccurrenceOnly,
    string? Summary, string? Location, bool Repeats, bool IsAllDay,
    DateTime? Start, DateTime? End, DateOnly? StartDate, DateOnly? EndDateExclusive,
    InvitationPerson? Organizer, IReadOnlyList<InvitationAttendee> Attendees,
    string? DtStartLine);

/// <summary><see cref="Ignored"/>: no handled METHOD, the part stays an attachment.
/// <see cref="Reason"/>: the guards or the parser refused it — « Invitation illisible ».</summary>
internal sealed record InvitationReading(ParsedInvitation? Invitation, bool Ignored, string? Reason)
{
    internal static InvitationReading Unreadable(string reason) => new(null, false, reason);
    internal static readonly InvitationReading NotAnInvitation = new(null, true, null);
}

/// <summary>Pure: the calendar part's text to what the card and the responder need. The file is
/// judged by the same guards a PUT faces (décision 1), METHOD first so a PUBLISH that would fail
/// them is ignored rather than declared unreadable.</summary>
internal static class InvitationParser
{
    internal const string Unparsable = "The calendar part could not be parsed";

    internal static InvitationReading Read(string ics)
    {
        if (IcsGuards.CheckSize(ics) is { } tooLarge) return InvitationReading.Unreadable(tooLarge.Message);
        var parsed = IcsDocument.TryLoad(ics);
        if (parsed is null) return InvitationReading.Unreadable(Unparsable);

        var method = parsed.Method?.Trim().ToUpperInvariant() switch
        {
            "REQUEST" => InvitationMethod.Request,
            "CANCEL" => InvitationMethod.Cancel,
            _ => (InvitationMethod?)null,
        };
        if (method is null) return InvitationReading.NotAnInvitation;
        if (IcsGuards.CheckParsed(ics, parsed) is { } refused) return InvitationReading.Unreadable(refused.Message);

        var components = IcsDocument.Components(parsed).ToList();
        var master = IcsDocument.MasterOf(parsed);
        var component = master ?? components[0];
        var projection = IcsProjector.Project(parsed, IcsTimeZones.Utc);
        var allDay = component.DtStart is { HasTime: false };

        return new InvitationReading(new ParsedInvitation(
            method.Value,
            component.Uid ?? string.Empty,
            component.Sequence,
            master is null,
            projection.Summary,
            projection.Location,
            master is not null && IcsDocument.Repeats(master),
            allDay,
            allDay ? null : projection.StartsAt,
            allDay ? null : projection.EndsAt,
            allDay ? DateOnly.FromDateTime(component.DtStart!.Value) : null,
            allDay && IcsDocument.EndOf(component) is { } end ? DateOnly.FromDateTime(end.Value) : null,
            component.Organizer is { } organizer && IcsProjector.Address(organizer.Value) is { } email
                ? new InvitationPerson(email, Text(organizer.CommonName)) : null,
            [.. (component.Attendees ?? []).Where(a => a is not null)
                .Select(a => (Address: IcsProjector.Address(a.Value), Attendee: a))
                .Where(x => x.Address is not null)
                .Select(x => new InvitationAttendee(x.Address!, Text(x.Attendee.CommonName), Upper(x.Attendee.ParticipationStatus)))],
            PartStatRewriter.LineOf(ics, "DTSTART")), false, null);
    }

    /// <summary>The master's SEQUENCE of a stored file; 0 when the file carries none or cannot be read.</summary>
    internal static int SequenceOf(string ics) =>
        IcsDocument.TryLoad(ics) is { } parsed && IcsDocument.MasterOf(parsed) is { } master ? master.Sequence : 0;

    /// <summary>The PARTSTAT the file's master carries for an address, case-insensitively; null when the address is not invited.</summary>
    internal static string? PartStatOf(string ics, string address)
    {
        if (IcsDocument.TryLoad(ics) is not { } parsed) return null;
        var component = IcsDocument.MasterOf(parsed) ?? IcsDocument.Components(parsed).FirstOrDefault();
        return component?.Attendees?
            .FirstOrDefault(a => a is not null && string.Equals(IcsProjector.Address(a.Value), address, StringComparison.OrdinalIgnoreCase))
            is { } attendee ? Upper(attendee.ParticipationStatus) ?? "NEEDS-ACTION" : null;
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? Upper(string? value) => Text(value)?.ToUpperInvariant();
}
```

`IcsTimeZones.Utc` existe (le projecteur l'utilise) ; vérifier que c'est bien une `string` (`"UTC"`),
sinon passer `"UTC"`. `IcsProjector.Project` est `internal static` : accessible.

- [ ] **Step 2f : vert.** `dotnet test --filter InvitationParser`. Si `Google_PlacesTheZonedStart…`
  échoue sur `Location` (Ical.Net déséchappe `\,` → `,` : attendu), ou sur `Start` (le
  `VTIMEZONE` de Google est résolu par `IcsTimeZones.Place` via son `TZID` IANA), lire
  `IcsProjectorTests` pour la forme que le projecteur produit et ajuster l'assertion, jamais le
  fixture.

- [ ] **Step 2g : les tests du réécriveur.** `PartStatRewriterTests.cs` (même dossier de tests) :

```csharp
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

public sealed class PartStatRewriterTests
{
    private static string Google => InvitationParserTests.Fixture("google-request");

    [Fact]
    public void RewritesOnlyTheUsersAttendeeLine_AndDropsMethod()
    {
        var rewritten = PartStatRewriter.Rewrite(Google, "alice@weesky.be", "ACCEPTED")!;

        Assert.DoesNotContain("METHOD:", rewritten);
        Assert.DoesNotContain("RSVP=TRUE\r\n ;CN=Alice", rewritten);
        Assert.Contains("PARTSTAT=ACCEPTED", rewritten);
        // Marc's and Jean's lines, folded as Google folded them, are untouched.
        Assert.Contains("PARTSTAT=ACCEPTED;CN=Marc Dupont\r\n ;X-NUM-GUESTS=0:mailto:marc.dupont@example.org", rewritten);
        Assert.Contains("RSVP=TRUE\r\n ;CN=jean@example.net", rewritten);
        // Everything else byte for byte: the file minus METHOD and minus Alice's line equals the rewrite minus Alice's line.
        Assert.Equal(WithoutLine(Google.Replace("METHOD:REQUEST\r\n", ""), "mailto:Alice@Weesky.be"),
            WithoutLine(rewritten, "mailto:Alice@Weesky.be"));
    }

    [Fact]
    public void TheRewrittenLine_KeepsTheOtherParameters_AndFoldsAt75()
    {
        var rewritten = PartStatRewriter.Rewrite(Google, "ALICE@weesky.be", "TENTATIVE")!;
        var line = Unfolded(rewritten).Single(l => l.EndsWith("mailto:Alice@Weesky.be", StringComparison.Ordinal));

        Assert.Equal("ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;CN=Alice;X-NUM-GUESTS=0;PARTSTAT=TENTATIVE:mailto:Alice@Weesky.be", line);
        Assert.All(rewritten.Split("\r\n"), physical => Assert.True(physical.Length <= 75, physical));
    }

    [Fact]
    public void AddsPartStat_WhenTheLineHadNone()
    {
        var ics = Google.Replace("PARTSTAT=NEEDS-ACTION;RSVP=TRUE\r\n ;CN=Alice", "CN=Alice");

        var line = Unfolded(PartStatRewriter.Rewrite(ics, "alice@weesky.be", "DECLINED")!)
            .Single(l => l.EndsWith("mailto:Alice@Weesky.be", StringComparison.Ordinal));

        Assert.EndsWith(";PARTSTAT=DECLINED:mailto:Alice@Weesky.be", line);
    }

    [Fact]
    public void IsIdempotent()
    {
        var once = PartStatRewriter.Rewrite(Google, "alice@weesky.be", "ACCEPTED")!;
        Assert.Equal(once, PartStatRewriter.Rewrite(once, "alice@weesky.be", "ACCEPTED"));
    }

    [Fact]
    public void NullWhenTheAddressIsNotInvited()
    {
        Assert.Null(PartStatRewriter.Rewrite(Google, "nobody@weesky.be", "ACCEPTED"));
    }

    [Fact]
    public void KeepsLfFiles_Lf()
    {
        var lf = Google.Replace("\r\n", "\n");
        var rewritten = PartStatRewriter.Rewrite(lf, "alice@weesky.be", "ACCEPTED")!;
        Assert.DoesNotContain("\r", rewritten);
    }

    [Fact]
    public void StripMethod_RemovesOnlyThatLine()
    {
        Assert.Equal(Google.Replace("METHOD:REQUEST\r\n", ""), PartStatRewriter.StripMethod(Google));
    }

    [Fact]
    public void LineOf_UnfoldsAndFindsTheProperty()
    {
        Assert.Equal("DTSTART;TZID=Europe/Brussels:20261010T193000", PartStatRewriter.LineOf(Google, "DTSTART"));
        Assert.Equal("ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE;CN=\"ALICE\":mailto:ALICE@WEESKY.BE",
            PartStatRewriter.LineOf(InvitationParserTests.Fixture("outlook-request"), "ATTENDEE"));
        Assert.Null(PartStatRewriter.LineOf(Google, "RRULE"));
    }

    private static IEnumerable<string> Unfolded(string ics) => PartStatRewriter.Unfold(ics).Select(l => l.Text);

    private static string WithoutLine(string ics, string marker) =>
        string.Join("\r\n", PartStatRewriter.Unfold(ics).Where(l => !l.Text.EndsWith(marker, StringComparison.Ordinal)).Select(l => l.Text));
}
```

- [ ] **Step 2h : rouge, puis le réécriveur.** `Services/Calendar/Invitations/PartStatRewriter.cs` :

```csharp
using System.Text;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

/// <summary>
/// Textual edits of an iCalendar file, so what the organizer sent enters the calendar byte for byte
/// but for the one line the user answers on (décision 4). Ical.Net would re-serialize the whole
/// file and change every byte of it; this works on logical lines (RFC 5545 § 3.1) and refolds
/// only the line it rewrote.
/// </summary>
internal static class PartStatRewriter
{
    private const int FoldAt = 75;

    /// <summary>One logical line and the physical lines it was folded over.</summary>
    internal readonly record struct Line(string Text, int First, int Count);

    /// <summary>Rewrites the ATTENDEE whose value is <c>mailto:address</c> (case-insensitive) with
    /// <paramref name="partStat"/> and no RSVP, drops METHOD, leaves every other byte. Null when no
    /// ATTENDEE names the address.</summary>
    internal static string? Rewrite(string ics, string address, string partStat)
    {
        var newline = ics.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var physical = ics.Split(newline);
        var lines = Unfold(physical);
        var target = lines.FirstOrDefault(l => IsAttendeeOf(l.Text, address));
        if (target.Text is null) return null;

        var output = new List<string>();
        foreach (var line in lines)
        {
            if (line.Text.StartsWith("METHOD:", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Equals(target)) output.AddRange(Fold(WithPartStat(line.Text, partStat)));
            else output.AddRange(physical.Skip(line.First).Take(line.Count));
        }
        // Split keeps a trailing "" after a final newline; re-joining restores it.
        if (physical[^1].Length == 0) output.Add(string.Empty);
        return string.Join(newline, output);
    }

    /// <summary>Drops the METHOD line and nothing else.</summary>
    internal static string StripMethod(string ics)
    {
        var newline = ics.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return string.Join(newline, ics.Split(newline)
            .Where(l => !l.StartsWith("METHOD:", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The first logical line of a property, unfolded — "DTSTART;TZID=…:…"; null when absent.</summary>
    internal static string? LineOf(string ics, string property) =>
        Unfold(ics).Select(l => l.Text)
            .FirstOrDefault(l => l.StartsWith(property + ":", StringComparison.OrdinalIgnoreCase)
                || l.StartsWith(property + ";", StringComparison.OrdinalIgnoreCase));

    internal static List<Line> Unfold(string ics) =>
        Unfold(ics.Split(ics.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n"));

    private static List<Line> Unfold(string[] physical)
    {
        var lines = new List<Line>();
        var text = new StringBuilder();
        var first = 0;
        for (var i = 0; i < physical.Length; i++)
        {
            var continues = physical[i].Length > 0 && (physical[i][0] == ' ' || physical[i][0] == '\t');
            if (continues && text.Length > 0) { text.Append(physical[i], 1, physical[i].Length - 1); continue; }
            if (i > first || text.Length > 0) lines.Add(new Line(text.ToString(), first, i - first));
            text.Clear().Append(physical[i]);
            first = i;
        }
        lines.Add(new Line(text.ToString(), first, physical.Length - first));
        return lines;
    }

    private static bool IsAttendeeOf(string line, string address)
    {
        if (!line.StartsWith("ATTENDEE", StringComparison.OrdinalIgnoreCase)) return false;
        var colon = ValueStart(line);
        if (colon < 0) return false;
        var value = line[(colon + 1)..].Trim();
        if (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) value = value["mailto:".Length..];
        return string.Equals(value, address, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The parameters minus PARTSTAT and RSVP, then PARTSTAT last, then the value.</summary>
    private static string WithPartStat(string line, string partStat)
    {
        var colon = ValueStart(line);
        var head = line[..colon];
        var value = line[colon..];
        var parameters = SplitParameters(head).Skip(1)
            .Where(p => !p.StartsWith("PARTSTAT=", StringComparison.OrdinalIgnoreCase)
                && !p.StartsWith("RSVP=", StringComparison.OrdinalIgnoreCase));
        return "ATTENDEE;" + string.Join(';', parameters.Append("PARTSTAT=" + partStat)) + value;
    }

    /// <summary>The ':' that ends the name-and-parameters, ignoring any inside a quoted parameter.</summary>
    private static int ValueStart(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ':' && !quoted) return i;
        }
        return -1;
    }

    private static IEnumerable<string> SplitParameters(string head)
    {
        var quoted = false;
        var start = 0;
        for (var i = 0; i < head.Length; i++)
        {
            if (head[i] == '"') quoted = !quoted;
            else if (head[i] == ';' && !quoted) { yield return head[start..i]; start = i + 1; }
        }
        yield return head[start..];
    }

    /// <summary>RFC 5545 § 3.1: physical lines of at most 75 octets, continuations led by a space.
    /// Counted in UTF-8 octets, never inside a multi-byte sequence.</summary>
    private static IEnumerable<string> Fold(string logical)
    {
        var bytes = Encoding.UTF8.GetBytes(logical);
        if (bytes.Length <= FoldAt) { yield return logical; yield break; }
        var at = 0;
        var limit = FoldAt;
        while (at < bytes.Length)
        {
            var take = Math.Min(limit, bytes.Length - at);
            while (take > 0 && at + take < bytes.Length && (bytes[at + take] & 0xC0) == 0x80) take--;
            yield return (at == 0 ? "" : " ") + Encoding.UTF8.GetString(bytes, at, take);
            at += take;
            limit = FoldAt - 1;
        }
    }
}
```

- [ ] **Step 2i : vert, commit.** `dotnet test --filter "InvitationParser|PartStatRewriter|IcsGuards"` ;
  réverter `ApiDocumentation.xml` ;

```bash
git commit -F - <<'EOF'
feat(agenda): lecture d'une invitation reçue et réécriture textuelle du PARTSTAT

InvitationParser, PartStatRewriter, le bloc MailInvitation, sept fixtures anonymisées (5e1, décisions 1 et 4).
EOF
```

---

### Task 3 : la partie calendrier dans le détail du message, et le bloc `invitation`

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/MailCalendarPart.cs`
- Modify: `src/snoopy.microservice/Models/Mail/MailMessageDetail.cs`
- Modify: `src/snoopy.microservice/Services/MailMessageMapper.cs`
- Modify: `src/snoopy.microservice/Services/ImapMessageCommands.cs:442-517`
- Create: `src/snoopy.microservice/Services/Calendar/Invitations/InvitationReader.cs`
- Modify: `src/snoopy.microservice/Controllers/MailMessagesController.cs:92-121`
- Modify: `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs`
- Test: `snoopy.microservice.Tests/Services/MailMessageMapperTests.cs`, `Services/Calendar/Invitations/InvitationReaderTests.cs` (créé), `Controllers/MailMessagesControllerTests.cs`

**Interfaces:**
- Consumes: `IUserAddresses.ForAccountAsync`, `ICalendarEventStore.FindByUidAsync`, `InvitationParser`, `MailInvitation` (tâches 1 et 2).
- Produces:
  ```csharp
  public sealed record MailCalendarPart(string Part, string Ics, bool TooLarge);
  // MailMessageDetail
  public MailInvitation? Invitation { get; set; }
  [JsonIgnore] public MailCalendarPart? CalendarPart { get; set; }
  // MailMessageMapper
  internal static bool IsCalendarPart(BodyPartBasic part);
  internal static BodyPartBasic? CalendarPart(IEnumerable<BodyPartBasic> parts);
  // InvitationReader
  internal sealed record InvitationContext(
      string? AddressedTo, string? FilePartStat, StoredEventRef? Stored, int SavedSequence,
      string? SavedPartStat, InvitationPresence Presence);
  public interface IInvitationReader
  {
      Task<MailInvitation?> ReadAsync(User user, MailAccountConnection connection, MailCalendarPart part, CancellationToken cancellationToken);
  }
  internal sealed class InvitationReader : IInvitationReader
  {
      internal Task<InvitationContext> ResolveAsync(User user, MailAccountConnection connection, ParsedInvitation parsed, CancellationToken cancellationToken);
      internal static MailInvitation Block(ParsedInvitation parsed, InvitationContext context, string part);
  }
  ```

- [ ] **Step 3a : le choix de la partie, test d'abord.** Dans `MailMessageMapperTests.cs`
  (voir comment le fichier construit des `BodyPartBasic` — sinon via `new BodyPartBasic { ContentType = new ContentType("text", "calendar"), PartSpecifier = "2" }`) :

```csharp
[Fact]
public void CalendarPart_PrefersAPartWhoseMethodIsHandled_ThenOneWithoutMethod_NeverAnotherMethod()
{
    var reply = new BodyPartBasic { PartSpecifier = "1", ContentType = new ContentType("text", "calendar") };
    reply.ContentType.Parameters.Add("method", "REPLY");
    var plain = new BodyPartBasic { PartSpecifier = "2", ContentType = new ContentType("application", "ics"), FileName = "invite.ics" };
    var request = new BodyPartBasic { PartSpecifier = "3", ContentType = new ContentType("text", "calendar") };
    request.ContentType.Parameters.Add("method", "REQUEST");
    var text = new BodyPartBasic { PartSpecifier = "4", ContentType = new ContentType("text", "plain") };

    Assert.Same(request, MailMessageMapper.CalendarPart([reply, plain, request, text]));
    Assert.Same(plain, MailMessageMapper.CalendarPart([reply, plain, text]));
    Assert.Null(MailMessageMapper.CalendarPart([reply, text]));
}

[Fact]
public void IsCalendarPart_ByTypeOrByName()
{
    Assert.True(MailMessageMapper.IsCalendarPart(new BodyPartBasic { ContentType = new ContentType("text", "calendar") }));
    Assert.True(MailMessageMapper.IsCalendarPart(new BodyPartBasic { ContentType = new ContentType("application", "ics") }));
    Assert.True(MailMessageMapper.IsCalendarPart(new BodyPartBasic { ContentType = new ContentType("application", "octet-stream"), FileName = "Invite.ICS" }));
    Assert.False(MailMessageMapper.IsCalendarPart(new BodyPartBasic { ContentType = new ContentType("text", "plain"), FileName = "notes.txt" }));
}
```

`BodyPartBasic.FileName` est dérivé de `ContentDisposition`/`ContentType` (`name`) en MailKit :
si la propriété n'est pas assignable, poser `ContentType.Name = "invite.ics"` à la place.

- [ ] **Step 3b : rouge, puis implémentation.** Dans `MailMessageMapper` :

```csharp
/// <summary>A calendar part by type or by name: Google and Outlook slip the invitation into the
/// multipart/alternative as text/calendar with no name, and attach an invite.ics besides.</summary>
internal static bool IsCalendarPart(BodyPartBasic part) =>
    part.ContentType is { } type && (type.IsMimeType("text", "calendar") || type.IsMimeType("application", "ics"))
    || part.FileName is { } name && name.EndsWith(".ics", StringComparison.OrdinalIgnoreCase);

/// <summary>The one calendar part worth downloading (décision 1): a part whose Content-Type
/// announces a handled method wins, then one announcing none; a part announcing another method
/// (REPLY, PUBLISH…) is never it. Document order otherwise.</summary>
internal static BodyPartBasic? CalendarPart(IEnumerable<BodyPartBasic> parts)
{
    BodyPartBasic? unannounced = null;
    foreach (var part in parts.Where(IsCalendarPart))
    {
        var method = part.ContentType?.Parameters["method"]?.Trim().ToUpperInvariant();
        if (method is "REQUEST" or "CANCEL") return part;
        if (method is null) unannounced ??= part;
    }
    return unannounced;
}
```

`Models/Mail/MailCalendarPart.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>The calendar part the detail downloaded, carried to the invitation reader and never
/// serialized. <see cref="TooLarge"/>: the part's announced size passed IcsGuards.MaxIcsBytes, so
/// nothing was fetched and the card says the invitation is unreadable.</summary>
public sealed record MailCalendarPart(string Part, string Ics, bool TooLarge);
```

Dans `MailMessageDetail`, en fin de classe :

```csharp
/// <summary>Filled when the message carries a REQUEST or CANCEL calendar part (spec 5e, décision 1).</summary>
public MailInvitation? Invitation { get; set; }

/// <summary>Transport only: the part the reader fills <see cref="Invitation"/> from.</summary>
[System.Text.Json.Serialization.JsonIgnore]
public MailCalendarPart? CalendarPart { get; set; }
```

Dans `ImapMessageCommands.GetMessageAsync`, après la boucle des pièces jointes et avant
`return Result.Success(detail)` :

```csharp
if (MailMessageMapper.CalendarPart(summary.BodyParts.OfType<BodyPartBasic>()) is { } calendar)
{
    detail.CalendarPart = calendar.Octets > IcsGuards.MaxIcsBytes
        ? new MailCalendarPart(calendar.PartSpecifier, string.Empty, TooLarge: true)
        : new MailCalendarPart(calendar.PartSpecifier,
            await ReadPartTextAsync(folder, uniqueId, calendar, cancellationToken), TooLarge: false);
}
```

et, sous `ReadTextPartAsync`, la lecture d'une partie quelconque en texte :

```csharp
/// <summary>A non-text part decoded as text — the invite.ics an application/ics part carries.
/// The charset parameter rules, UTF-8 when it names none, as RFC 5545 § 3.1.4 defaults.</summary>
private static async Task<string> ReadPartTextAsync(
    IMailFolder folder, UniqueId uniqueId, BodyPartBasic part, CancellationToken cancellationToken)
{
    using var encoded = await folder.GetStreamAsync(uniqueId, SectionOf(part), cancellationToken);
    MimeUtils.TryParse(part.ContentTransferEncoding ?? string.Empty, out ContentEncoding encoding);
    using var decoded = new MemoryStream();
    await new MimeContent(encoded, encoding).DecodeToAsync(decoded, cancellationToken);
    var charset = part.ContentType?.Charset;
    Encoding text;
    try { text = charset is null ? Encoding.UTF8 : Encoding.GetEncoding(charset); }
    catch (ArgumentException) { text = Encoding.UTF8; }
    return text.GetString(decoded.GetBuffer(), 0, (int)decoded.Length);
}
```

`using System.Text;` et `using weesky.Snoopy.Microservice.Services.Calendar;` en tête du fichier.

- [ ] **Step 3c : le lecteur avec la base, tests d'abord.** `InvitationReaderTests.cs` :

```csharp
using Moq;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

public sealed class InvitationReaderTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();
    private static readonly Guid Personal = Guid.NewGuid();
    private static readonly Guid Work = Guid.NewGuid();
    private static readonly MailAccountConnection Conn = TestConnections.Primary("alice@weesky.be", "pw");
    private readonly User _user = new("alice@weesky.be") { WebmailUid = WebmailUid };
    private readonly Mock<IUserAddresses> _addresses = new();
    private readonly Mock<ICalendarEventStore> _events = new();

    private static string Fixture(string name) => InvitationParserTests.Fixture(name);
    private static MailCalendarPart Part(string name) => new("2", Fixture(name), false);

    private InvitationReader Create(params string[] addresses)
    {
        _addresses.Setup(a => a.ForAccountAsync(_user, Conn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(addresses.Length == 0 ? ["alice@weesky.be", "alice@weesky.net"] : addresses);
        _events.Setup(e => e.FindByUidAsync(WebmailUid, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return new InvitationReader(_addresses.Object, _events.Object);
    }

    private void Stored(string ics, Guid calendar, string davName = "abc.ics", params (string Ics, Guid Calendar)[] more)
    {
        var uid = InvitationParser.Read(ics).Invitation!.Uid;
        List<StoredEventRef> rows = [new(Guid.NewGuid(), calendar, davName, ics)];
        rows.AddRange(more.Select(m => new StoredEventRef(Guid.NewGuid(), m.Calendar, "other.ics", m.Ics)));
        _events.Setup(e => e.FindByUidAsync(WebmailUid, uid, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
    }

    [Fact]
    public async Task AFreshRequest_IsAbsent_AddressedToThePrimary_NeedsAction()
    {
        var block = (await Create().ReadAsync(_user, Conn, Part("google-request"), CancellationToken.None))!;

        Assert.Equal(InvitationMethod.Request, block.Method);
        Assert.Equal(InvitationPresence.Absent, block.InCalendar);
        Assert.Equal("alice@weesky.be", block.AddressedTo);
        Assert.Equal("NEEDS-ACTION", block.FilePartStat);
        Assert.Null(block.SavedPartStat);
        Assert.Null(block.CalendarId);
        Assert.Equal("2", block.Part);
        Assert.Equal(["marc.dupont@example.org", "Alice@Weesky.be", "jean@example.net"], block.Attendees.Select(a => a.Email));
        Assert.False(block.Unreadable);
    }

    [Fact]
    public async Task AddressedTo_MatchesAnAlias_AndTheUsersListOrderWins()
    {
        var block = (await Create().ReadAsync(_user, Conn, Part("thunderbird-request"), CancellationToken.None))!;
        Assert.Equal("alice@weesky.net", block.AddressedTo);
    }

    [Fact]
    public async Task AddressedTo_OnAConnectedAccount_IsItsAddress()
    {
        var gmail = TestConnections.Connected(Guid.NewGuid().ToString(), "alice.dupont@gmail.com", "pw");
        _addresses.Setup(a => a.ForAccountAsync(_user, gmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(["alice.dupont@gmail.com"]);
        var reader = Create();
        var ics = Fixture("google-request").Replace("mailto:jean@example.net", "mailto:Alice.Dupont@gmail.com");

        var block = (await reader.ReadAsync(_user, gmail, new MailCalendarPart("2", ics, false), CancellationToken.None))!;

        Assert.Equal("alice.dupont@gmail.com", block.AddressedTo);
    }

    [Fact]
    public async Task Forwarded_HasNoAddressedTo_AndNoFilePartStat()
    {
        var block = (await Create("someone@weesky.be").ReadAsync(_user, Conn, Part("google-request"), CancellationToken.None))!;

        Assert.Null(block.AddressedTo);
        Assert.Null(block.FilePartStat);
    }

    [Fact]
    public async Task Current_WhenTheStoredSequenceIsTheSame_CarriesTheSavedAnswer()
    {
        var reader = Create();
        var stored = PartStatRewriter.Rewrite(Fixture("google-request"), "alice@weesky.be", "ACCEPTED")!;
        Stored(stored, Personal);

        var block = (await reader.ReadAsync(_user, Conn, Part("google-request"), CancellationToken.None))!;

        Assert.Equal(InvitationPresence.Current, block.InCalendar);
        Assert.Equal("ACCEPTED", block.SavedPartStat);
        Assert.Equal("NEEDS-ACTION", block.FilePartStat);
        Assert.Equal(Personal, block.CalendarId);
    }

    [Fact]
    public async Task Outdated_WhenTheReceivedSequenceIsHigher()
    {
        var reader = Create();
        Stored(Fixture("google-request"), Personal);
        var newer = Fixture("google-request").Replace("SEQUENCE:0", "SEQUENCE:1");

        var block = (await reader.ReadAsync(_user, Conn, new MailCalendarPart("2", newer, false), CancellationToken.None))!;

        Assert.Equal(InvitationPresence.Outdated, block.InCalendar);
    }

    [Fact]
    public async Task Newer_WhenTheReceivedSequenceIsLower_ForARequestAndForACancel()
    {
        var reader = Create();
        Stored(Fixture("google-request").Replace("SEQUENCE:0", "SEQUENCE:5"), Personal);

        var request = (await reader.ReadAsync(_user, Conn, Part("google-request"), CancellationToken.None))!;
        var cancel = (await reader.ReadAsync(_user, Conn, Part("google-cancel"), CancellationToken.None))!;

        Assert.Equal(InvitationPresence.Newer, request.InCalendar);
        Assert.Equal(InvitationPresence.Newer, cancel.InCalendar);
    }

    [Fact]
    public async Task Cancelled_WhenACancelFindsTheEvent_AbsentOtherwise()
    {
        var reader = Create();
        var absent = (await reader.ReadAsync(_user, Conn, Part("google-cancel"), CancellationToken.None))!;
        Stored(Fixture("google-request"), Personal);
        var present = (await reader.ReadAsync(_user, Conn, Part("google-cancel"), CancellationToken.None))!;

        Assert.Equal(InvitationPresence.Absent, absent.InCalendar);
        Assert.Equal(InvitationPresence.Cancelled, present.InCalendar);
    }

    [Fact]
    public async Task TheSameUidInTwoCalendars_TheFirstRowWins()
    {
        var reader = Create();
        Stored(Fixture("google-request"), Personal, "p.ics", (Fixture("google-request"), Work));

        var block = (await reader.ReadAsync(_user, Conn, Part("google-request"), CancellationToken.None))!;

        Assert.Equal(Personal, block.CalendarId);
    }

    [Fact]
    public async Task OccurrenceOnly_NeverSearchesTheCalendar()
    {
        var block = (await Create().ReadAsync(_user, Conn, Part("occurrence-cancel"), CancellationToken.None))!;

        Assert.True(block.OccurrenceOnly);
        Assert.Equal(InvitationPresence.Absent, block.InCalendar);
        _events.Verify(e => e.FindByUidAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reply_IsNull_TheFileStaysAnAttachment()
    {
        Assert.Null(await Create().ReadAsync(_user, Conn, Part("google-reply"), CancellationToken.None));
    }

    [Fact]
    public async Task Unreadable_CarriesThePartAndTheReason_NothingElse()
    {
        var reader = Create();

        var tooLarge = (await reader.ReadAsync(_user, Conn, new MailCalendarPart("3", "", true), CancellationToken.None))!;
        var garbage = (await reader.ReadAsync(_user, Conn, new MailCalendarPart("3", "garbage", false), CancellationToken.None))!;

        Assert.True(tooLarge.Unreadable);
        Assert.Equal("3", tooLarge.Part);
        Assert.NotNull(tooLarge.Reason);
        Assert.True(garbage.Unreadable);
        _addresses.Verify(a => a.ForAccountAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 3d : rouge, puis le lecteur.** `Services/Calendar/Invitations/InvitationReader.cs` :

```csharp
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

/// <summary>What the base says about a received invitation: who the user is in it, and what the
/// calendar holds for its UID (décisions 2 and 3).</summary>
internal sealed record InvitationContext(
    string? AddressedTo, string? FilePartStat, StoredEventRef? Stored, int SavedSequence,
    string? SavedPartStat, InvitationPresence Presence);

public interface IInvitationReader
{
    /// <summary>The <c>invitation</c> block for a downloaded calendar part; null when the part is
    /// not a REQUEST or CANCEL, so the file stays an attachment.</summary>
    Task<MailInvitation?> ReadAsync(User user, MailAccountConnection connection, MailCalendarPart part, CancellationToken cancellationToken);
}

internal sealed class InvitationReader(IUserAddresses addresses, ICalendarEventStore events) : IInvitationReader
{
    internal const string TooLarge = "The calendar part is larger than an event may be";

    public async Task<MailInvitation?> ReadAsync(
        User user, MailAccountConnection connection, MailCalendarPart part, CancellationToken cancellationToken)
    {
        if (part.TooLarge) return new MailInvitation { Part = part.Part, Unreadable = true, Reason = TooLarge };
        var reading = InvitationParser.Read(part.Ics);
        if (reading.Ignored) return null;
        if (reading.Invitation is not { } parsed)
            return new MailInvitation { Part = part.Part, Unreadable = true, Reason = reading.Reason };

        return Block(parsed, await ResolveAsync(user, connection, parsed, cancellationToken), part.Part);
    }

    internal async Task<InvitationContext> ResolveAsync(
        User user, MailAccountConnection connection, ParsedInvitation parsed, CancellationToken cancellationToken)
    {
        // The user's list decides the order: the first of HIS addresses that is invited answers.
        var mine = await addresses.ForAccountAsync(user, connection, cancellationToken);
        var addressedTo = mine.FirstOrDefault(a => parsed.Attendees.Any(
            x => string.Equals(x.Email, a, StringComparison.OrdinalIgnoreCase)));
        var filePartStat = addressedTo is null ? null
            : parsed.Attendees.First(x => string.Equals(x.Email, addressedTo, StringComparison.OrdinalIgnoreCase))
                .PartStat ?? "NEEDS-ACTION";

        // An occurrence alone is shown and never applied: the calendar is not even asked (1 bis).
        if (parsed.OccurrenceOnly)
            return new InvitationContext(addressedTo, filePartStat, null, 0, null, InvitationPresence.Absent);

        var stored = (await events.FindByUidAsync(user.WebmailUid, parsed.Uid, cancellationToken)).FirstOrDefault();
        if (stored is null)
            return new InvitationContext(addressedTo, filePartStat, null, 0, null, InvitationPresence.Absent);

        var savedSequence = InvitationParser.SequenceOf(stored.IcsRaw);
        var savedPartStat = addressedTo is null ? null : InvitationParser.PartStatOf(stored.IcsRaw, addressedTo);
        var presence = parsed.Sequence < savedSequence ? InvitationPresence.Newer
            : parsed.Method is InvitationMethod.Cancel ? InvitationPresence.Cancelled
            : parsed.Sequence > savedSequence ? InvitationPresence.Outdated
            : InvitationPresence.Current;
        return new InvitationContext(addressedTo, filePartStat, stored, savedSequence, savedPartStat, presence);
    }

    internal static MailInvitation Block(ParsedInvitation parsed, InvitationContext context, string part) => new()
    {
        Method = parsed.Method,
        Uid = parsed.Uid,
        Sequence = parsed.Sequence,
        Summary = parsed.Summary,
        Start = parsed.Start,
        End = parsed.End,
        StartDate = parsed.StartDate,
        EndDateExclusive = parsed.EndDateExclusive,
        IsAllDay = parsed.IsAllDay,
        Location = parsed.Location,
        Repeats = parsed.Repeats,
        Organizer = parsed.Organizer,
        Attendees = [.. parsed.Attendees.Select(a => new InvitationPerson(a.Email, a.Name))],
        AddressedTo = context.AddressedTo,
        FilePartStat = context.FilePartStat,
        SavedPartStat = context.SavedPartStat,
        InCalendar = context.Presence,
        CalendarId = context.Stored?.CalendarId,
        OccurrenceOnly = parsed.OccurrenceOnly,
        Part = part,
    };
}
```

Enregistrer : `services.AddScoped<IInvitationReader, InvitationReader>();`.

- [ ] **Step 3e : le contrôleur, test d'abord.** Dans `MailMessagesControllerTests`, ajouter le mock
  `private readonly Mock<IInvitationReader> _invitations = new();`, le passer au constructeur
  (`new MailMessagesController(_messages.Object, _connections.Object, _trustedSenders.Object, _invitations.Object, NullLogger…)`
  — mettre à jour tout autre `new MailMessagesController(` du projet de tests), et :

```csharp
[Fact]
public async Task GetMessage_FillsTheInvitationFromTheCalendarPart()
{
    var detail = new MailMessageDetail { Uid = 7, CalendarPart = new MailCalendarPart("2", "BEGIN:VCALENDAR", false) };
    _messages.Setup(m => m.GetAsync(It.IsAny<User>(), Conn, "INBOX", 7u, It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Success(detail));
    var block = new MailInvitation { Uid = "u", Part = "2" };
    _invitations.Setup(i => i.ReadAsync(It.IsAny<User>(), Conn, detail.CalendarPart!, It.IsAny<CancellationToken>()))
                .ReturnsAsync(block);

    var result = await CreateController().GetMessage("INBOX", 7, CancellationToken.None);

    var ok = Assert.IsType<OkObjectResult>(result.Result);
    Assert.Same(block, Assert.IsType<MailMessageDetail>(ok.Value).Invitation);
}

[Fact]
public async Task GetMessage_WithoutACalendarPart_NeverAsksTheReader()
{
    _messages.Setup(m => m.GetAsync(It.IsAny<User>(), Conn, "INBOX", 7u, It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Success(new MailMessageDetail { Uid = 7 }));

    await CreateController().GetMessage("INBOX", 7, CancellationToken.None);

    _invitations.Verify(i => i.ReadAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<MailCalendarPart>(), It.IsAny<CancellationToken>()), Times.Never);
}
```

- [ ] **Step 3f : rouge, puis implémentation.** `MailMessagesController` : paramètre de constructeur
  `IInvitationReader invitations` (avant le logger), et dans `GetMessage`, dans le `if (result.IsSuccess)` :

```csharp
if (result.Value.CalendarPart is { } part)
    result.Value.Invitation = await invitations.ReadAsync(AuthenticatedUser, connection, part, cancellationToken);
```

- [ ] **Step 3g : vert, commit.** `dotnet test` complet (le constructeur a changé) ; réverter
  `ApiDocumentation.xml` ;

```bash
git commit -F - <<'EOF'
feat(mail): le détail d'un message porte son invitation d'agenda

La partie calendrier est repérée dans le BODYSTRUCTURE et lue une fois ; InvitationReader remplit le bloc (5e1, décisions 1 à 3).
EOF
```

---

### Task 4 : le mail de réponse — composer un `REPLY`, l'envoyer par la session du compte

**Files:**
- Create: `src/snoopy.microservice/Services/Calendar/Invitations/InvitationText.cs`
- Create: `src/snoopy.microservice/Services/Calendar/Invitations/ReplyComposer.cs`
- Create: `src/snoopy.microservice/Services/RoleFolderLocator.cs`
- Modify: `src/snoopy.microservice/Services/IMailSender.cs`, `MailSender.cs`
- Modify: `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs`
- Test: `Services/Calendar/Invitations/ReplyComposerTests.cs`, `InvitationTextTests.cs` (créés), `Services/MailSenderTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  internal static class InvitationText
  {
      /// "samedi 10 octobre 2026, 19:30" / "Saturday 10 October 2026, 19:30" ; « Journée entière » sur une date.
      internal static string When(ParsedInvitation invitation, string timeZone, string language);
      internal static string Subject(string partStat, string? summary, string language);   // "Accepté : Dîner"
      internal static string Body(string who, string partStat, string? summary, string when, string language);
  }
  internal sealed record ReplyInput(
      string FromAddress, string? FromName, string OrganizerEmail, string? OrganizerName,
      ParsedInvitation Invitation, string PartStat, string TimeZone, string Language, DateTime NowUtc);
  internal static class ReplyComposer
  {
      internal static MimeMessage Compose(ReplyInput input);
      internal static string Calendar(ReplyInput input);      // le VCALENDAR REPLY, texte
  }
  public interface IRoleFolderLocator
  {
      Task<string?> FindAsync(User user, MailAccountConnection connection, string role, CancellationToken cancellationToken);
  }
  // IMailSender
  Task<Result<SendMessageResult>> SendBuiltAsync(User user, MailAccountConnection connection, MimeMessage message, CancellationToken cancellationToken);
  ```

- [ ] **Step 4a : les textes, test d'abord.** `InvitationTextTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

public sealed class InvitationTextTests
{
    private static ParsedInvitation Google => InvitationParser.Read(InvitationParserTests.Fixture("google-request")).Invitation!;
    private static ParsedInvitation Apple => InvitationParser.Read(InvitationParserTests.Fixture("apple-request")).Invitation!;

    [Fact]
    public void When_InTheBrowserZone_InFrenchAndInEnglish()
    {
        Assert.Equal("samedi 10 octobre 2026, 19:30", InvitationText.When(Google, "Europe/Brussels", "fr"));
        Assert.Equal("Saturday 10 October 2026, 19:30", InvitationText.When(Google, "Europe/Brussels", "en"));
        Assert.Equal("Saturday 10 October 2026, 13:30", InvitationText.When(Google, "America/New_York", "en"));
    }

    [Fact]
    public void When_OnAWholeDay_IsTheDateAlone()
    {
        Assert.Equal("dimanche 1 novembre 2026, journée entière", InvitationText.When(Apple, "Europe/Brussels", "fr"));
        Assert.Equal("Sunday 1 November 2026, all day", InvitationText.When(Apple, "Europe/Brussels", "en"));
    }

    [Fact]
    public void When_FallsBackToUtc_OnAnUnknownZone()
    {
        Assert.Equal("Saturday 10 October 2026, 17:30", InvitationText.When(Google, "Mars/Olympus", "en"));
    }

    [Fact]
    public void Subject_UsesFrenchTypography()
    {
        Assert.Equal("Accepté\u00A0: Dîner chez Marc", InvitationText.Subject("ACCEPTED", "Dîner chez Marc", "fr"));
        Assert.Equal("Provisoire\u00A0: Dîner chez Marc", InvitationText.Subject("TENTATIVE", "Dîner chez Marc", "fr"));
        Assert.Equal("Refusé\u00A0: (sans titre)", InvitationText.Subject("DECLINED", null, "fr"));
        Assert.Equal("Declined: (no title)", InvitationText.Subject("DECLINED", "", "en"));
        Assert.Equal("Tentative: Dinner", InvitationText.Subject("TENTATIVE", "Dinner", "de"));
    }

    [Fact]
    public void Body_IsOneSentence()
    {
        Assert.Equal("Alice Martin a accepté l'invitation «\u00A0Dîner chez Marc\u00A0» du samedi 10 octobre 2026, 19:30.",
            InvitationText.Body("Alice Martin", "ACCEPTED", "Dîner chez Marc", "samedi 10 octobre 2026, 19:30", "fr"));
        Assert.Equal("Alice Martin has declined the invitation \u201CDinner\u201D on Saturday 10 October 2026, 19:30.",
            InvitationText.Body("Alice Martin", "DECLINED", "Dinner", "Saturday 10 October 2026, 19:30", "en"));
        Assert.Equal("alice@weesky.be a répondu peut-être à l'invitation «\u00A0Dîner\u00A0» du lundi.",
            InvitationText.Body("alice@weesky.be", "TENTATIVE", "Dîner", "lundi", "fr"));
    }
}
```

- [ ] **Step 4b : rouge, puis implémentation.** `InvitationText.cs` :

```csharp
using System.Globalization;
using NodaTime;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

/// <summary>The words of a REPLY, in the two languages the webmail speaks. French carries its
/// non-breaking spaces (U+00A0) before ':' and inside « ». Anything but "fr" is English.</summary>
internal static class InvitationText
{
    private const string Nbsp = "\u00A0";

    internal static string When(ParsedInvitation invitation, string timeZone, string language)
    {
        var culture = Culture(language);
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZone) ?? DateTimeZone.Utc;
        if (invitation.IsAllDay && invitation.StartDate is { } day)
            return day.ToString("dddd d MMMM yyyy", culture) + (IsFrench(language) ? ", journée entière" : ", all day");
        if (invitation.Start is not { } start) return string.Empty;
        var local = Instant.FromDateTimeUtc(DateTime.SpecifyKind(start, DateTimeKind.Utc)).InZone(zone).LocalDateTime;
        return local.ToDateTimeUnspecified().ToString("dddd d MMMM yyyy, HH:mm", culture);
    }

    internal static string Subject(string partStat, string? summary, string language)
    {
        var title = string.IsNullOrWhiteSpace(summary) ? (IsFrench(language) ? "(sans titre)" : "(no title)") : summary;
        var word = (IsFrench(language), partStat) switch
        {
            (true, "ACCEPTED") => "Accepté" + Nbsp + ":",
            (true, "TENTATIVE") => "Provisoire" + Nbsp + ":",
            (true, _) => "Refusé" + Nbsp + ":",
            (false, "ACCEPTED") => "Accepted:",
            (false, "TENTATIVE") => "Tentative:",
            (false, _) => "Declined:",
        };
        return word + " " + title;
    }

    internal static string Body(string who, string partStat, string? summary, string when, string language)
    {
        var title = string.IsNullOrWhiteSpace(summary) ? (IsFrench(language) ? "(sans titre)" : "(no title)") : summary;
        if (IsFrench(language))
        {
            var verb = partStat switch
            {
                "ACCEPTED" => "a accepté", "TENTATIVE" => "a répondu peut-être à", _ => "a refusé",
            };
            return $"{who} {verb} l'invitation «{Nbsp}{title}{Nbsp}» du {when}.";
        }
        var english = partStat switch
        {
            "ACCEPTED" => "has accepted", "TENTATIVE" => "has tentatively accepted", _ => "has declined",
        };
        return $"{who} {english} the invitation \u201C{title}\u201D on {when}.";
    }

    private static bool IsFrench(string language) => language.StartsWith("fr", StringComparison.OrdinalIgnoreCase);
    private static CultureInfo Culture(string language) => CultureInfo.GetCultureInfo(IsFrench(language) ? "fr-FR" : "en-GB");
}
```

Si `en-GB` écrit « Saturday 10 October 2026 » et que le test attend autre chose, c'est le test
qui s'aligne sur la culture (l'ordre jour-mois est le choix, pas la culture précise).

- [ ] **Step 4c : le composeur, test d'abord.** `ReplyComposerTests.cs` :

```csharp
using MimeKit;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

public sealed class ReplyComposerTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 8, 0, 0, DateTimeKind.Utc);

    private static ReplyInput Input(string partStat = "ACCEPTED", string language = "fr") => new(
        "alice@weesky.be", "Alice Martin", "marc.dupont@example.org", "Marc Dupont",
        InvitationParser.Read(InvitationParserTests.Fixture("google-request")).Invitation!,
        partStat, "Europe/Brussels", language, Now);

    [Fact]
    public void Envelope_FromTheUserToTheOrganizer_LocalisedSubject()
    {
        var message = ReplyComposer.Compose(Input());

        Assert.Equal("alice@weesky.be", message.From.Mailboxes.Single().Address);
        Assert.Equal("Alice Martin", message.From.Mailboxes.Single().Name);
        Assert.Equal("marc.dupont@example.org", message.To.Mailboxes.Single().Address);
        Assert.Equal("Accepté\u00A0: Dîner chez Marc", message.Subject);
        Assert.Equal(Now, message.Date.UtcDateTime);
    }

    [Fact]
    public void Body_IsAlternative_TextThenCalendarWithMethodReply()
    {
        var message = ReplyComposer.Compose(Input("DECLINED", "en"));

        var alternative = Assert.IsType<MultipartAlternative>(message.Body);
        var text = Assert.IsType<TextPart>(alternative[0]);
        Assert.Equal("Alice Martin has declined the invitation \u201CDîner chez Marc\u201D on Saturday 10 October 2026, 19:30.", text.Text.TrimEnd());
        var calendar = Assert.IsType<TextPart>(alternative[1]);
        Assert.True(calendar.ContentType.IsMimeType("text", "calendar"));
        Assert.Equal("REPLY", calendar.ContentType.Parameters["method"]);
        Assert.Equal("utf-8", calendar.ContentType.Charset);
    }

    [Fact]
    public void Calendar_IsTheReducedReply_ThunderbirdsShape()
    {
        var ics = ReplyComposer.Calendar(Input("TENTATIVE"));

        Assert.Equal(
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//weesky//webmail//EN\r\nMETHOD:REPLY\r\n"
            + "BEGIN:VEVENT\r\nUID:7c2e1c4a9f0b4d2e8a1c3b5d7e9f1a2b@google.com\r\nSEQUENCE:0\r\nDTSTAMP:20260913T080000Z\r\n"
            + "ORGANIZER;CN=Marc Dupont:mailto:marc.dupont@example.org\r\n"
            + "ATTENDEE;PARTSTAT=TENTATIVE;CN=Alice Martin:mailto:alice@weesky.be\r\n"
            + "DTSTART;TZID=Europe/Brussels:20261010T193000\r\n"
            + "SUMMARY:Dîner chez Marc\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n", ics);
    }

    [Fact]
    public void Calendar_QuotesACommonNameHoldingAColonOrComma_AndEscapesTheSummary()
    {
        var input = Input() with { FromName = "Martin, Alice", Invitation = Input().Invitation with { Summary = "Dîner; chez Marc, 19h" } };

        var ics = ReplyComposer.Calendar(input);

        Assert.Contains("ATTENDEE;PARTSTAT=ACCEPTED;CN=\"Martin, Alice\":mailto:alice@weesky.be\r\n", ics);
        Assert.Contains("SUMMARY:Dîner\\; chez Marc\\, 19h\r\n", ics);
    }

    [Fact]
    public void Calendar_WithoutNames_OmitsCn()
    {
        var ics = ReplyComposer.Calendar(Input() with { FromName = null, OrganizerName = null });

        Assert.Contains("ORGANIZER:mailto:marc.dupont@example.org\r\n", ics);
        Assert.Contains("ATTENDEE;PARTSTAT=ACCEPTED:mailto:alice@weesky.be\r\n", ics);
    }
}
```

- [ ] **Step 4d : rouge, puis le composeur.** `ReplyComposer.cs` :

```csharp
using System.Text;
using MimeKit;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

internal sealed record ReplyInput(
    string FromAddress, string? FromName, string OrganizerEmail, string? OrganizerName,
    ParsedInvitation Invitation, string PartStat, string TimeZone, string Language, DateTime NowUtc);

/// <summary>
/// The REPLY mail (décision 5): a one-line text and a reduced VCALENDAR — METHOD, the UID, the
/// SEQUENCE received, DTSTAMP, ORGANIZER, the user's ATTENDEE alone, and the file's own DTSTART
/// line, which some agendas need to pair the answer. The shape Thunderbird and Google produce.
/// </summary>
internal static class ReplyComposer
{
    private const string ProductId = "-//weesky//webmail//EN";

    internal static MimeMessage Compose(ReplyInput input)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(input.FromName ?? string.Empty, input.FromAddress));
        message.To.Add(new MailboxAddress(input.OrganizerName ?? string.Empty, input.OrganizerEmail));
        message.Date = new DateTimeOffset(DateTime.SpecifyKind(input.NowUtc, DateTimeKind.Utc));
        message.Subject = InvitationText.Subject(input.PartStat, input.Invitation.Summary, input.Language);

        var who = string.IsNullOrWhiteSpace(input.FromName) ? input.FromAddress : input.FromName;
        var when = InvitationText.When(input.Invitation, input.TimeZone, input.Language);
        var text = new TextPart("plain") { Text = InvitationText.Body(who, input.PartStat, input.Invitation.Summary, when, input.Language) + "\r\n" };
        var calendar = new TextPart("calendar") { Text = Calendar(input) };
        calendar.ContentType.Parameters.Add("method", "REPLY");
        calendar.ContentType.Charset = "utf-8";
        message.Body = new MultipartAlternative { text, calendar };
        return message;
    }

    internal static string Calendar(ReplyInput input)
    {
        var e = input.Invitation;
        var ics = new StringBuilder()
            .Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:").Append(ProductId).Append("\r\nMETHOD:REPLY\r\n")
            .Append("BEGIN:VEVENT\r\nUID:").Append(e.Uid).Append("\r\n")
            .Append("SEQUENCE:").Append(e.Sequence).Append("\r\n")
            .Append("DTSTAMP:").Append(input.NowUtc.ToString("yyyyMMdd'T'HHmmss'Z'")).Append("\r\n")
            .Append("ORGANIZER").Append(Cn(input.OrganizerName)).Append(":mailto:").Append(input.OrganizerEmail).Append("\r\n")
            .Append("ATTENDEE;PARTSTAT=").Append(input.PartStat).Append(Cn(input.FromName)).Append(":mailto:").Append(input.FromAddress).Append("\r\n");
        if (e.DtStartLine is { } dtstart) ics.Append(dtstart).Append("\r\n");
        if (!string.IsNullOrWhiteSpace(e.Summary)) ics.Append("SUMMARY:").Append(EscapeText(e.Summary)).Append("\r\n");
        return ics.Append("END:VEVENT\r\nEND:VCALENDAR\r\n").ToString();
    }

    /// <summary>RFC 5545 § 3.2: a parameter value holding ':' ';' or ',' is quoted; a '"' cannot appear in one.</summary>
    private static string Cn(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var clean = name.Replace("\"", string.Empty).Trim();
        return ";CN=" + (clean.IndexOfAny([':', ';', ',']) >= 0 ? $"\"{clean}\"" : clean);
    }

    /// <summary>RFC 5545 § 3.3.11.</summary>
    private static string EscapeText(string value) =>
        value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");
}
```

- [ ] **Step 4e : `SendBuiltAsync` et le dossier d'un rôle, test d'abord.** Dans `MailSenderTests`,
  changer `CreateSender()` pour construire
  `new MailSender(factory, _staged.Object, _smtpFactory.Object, new RoleFolderLocator(_folders.Object, _roles.Object), _messages.Object, NullLogger<MailSender>.Instance)`
  et ajouter :

```csharp
[Fact]
public async Task SendBuilt_SendsAsIs_AndFilesACopyInSent()
{
    var sender = CreateSender();
    var message = new MimeMessage { Subject = "Accepté : Dîner" };
    message.From.Add(new MailboxAddress("", "mick@weesky.be"));
    message.To.Add(new MailboxAddress("", "marc@example.org"));

    var result = await sender.SendBuiltAsync(_user, Conn, message, CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.True(result.Value.SentCopySaved);
    _smtp.Verify(s => s.SendAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    _messages.Verify(m => m.AppendAsync(_user, Conn, "Sent", message, true, It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task SendBuilt_WhenSmtpRefuses_FailsWithItsError_AndFilesNothing()
{
    var sender = CreateSender();
    _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Failure("smtp_down"));

    var result = await sender.SendBuiltAsync(_user, Conn, new MimeMessage(), CancellationToken.None);

    Assert.Equal("smtp_down", result.Error);
    _messages.Verify(m => m.AppendAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<MimeMessage>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
}
```

Vérifier le nom du champ de `SendMessageResult` (`grep -n "record SendMessageResult" Models/Mail/SendMessageResult.cs`)
et l'employer à la place de `SentCopySaved` si différent. Et un test du localisateur, dans un
nouveau `Services/RoleFolderLocatorTests.cs` :

```csharp
[Fact]
public async Task Find_ReturnsTheFolderHoldingTheRole_NullWhenNone()
{
    var folders = new Mock<IMailFolderRepository>();
    var roles = new Mock<IFolderRoleStore>();
    var user = new User("mick@weesky.be") { WebmailUid = Guid.NewGuid() };
    var conn = TestConnections.Primary("mick@weesky.be", "pw");
    var trash = new MailFolderNode { Name = "Trash", Path = "Trash", AttributeRole = "trash", Selectable = true };
    folders.Setup(f => f.GetTreeAsync(user, conn, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>([trash]));
    roles.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Array.Empty<FolderRoleOverride>());
    var locator = new RoleFolderLocator(folders.Object, roles.Object);

    Assert.Equal("Trash", await locator.FindAsync(user, conn, "trash", CancellationToken.None));
    Assert.Null(await locator.FindAsync(user, conn, "sent", CancellationToken.None));
}
```

- [ ] **Step 4f : rouge, puis implémentation.** `Services/RoleFolderLocator.cs` :

```csharp
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>The folder a role names on one account — the tree's flags and the user's overrides,
/// resolved the way the folder list resolves them. Null when no folder holds the role.</summary>
public interface IRoleFolderLocator
{
    Task<string?> FindAsync(User user, MailAccountConnection connection, string role, CancellationToken cancellationToken);
}

internal sealed class RoleFolderLocator(IMailFolderRepository folders, IFolderRoleStore roles) : IRoleFolderLocator
{
    public async Task<string?> FindAsync(
        User user, MailAccountConnection connection, string role, CancellationToken cancellationToken)
    {
        var tree = await folders.GetTreeAsync(user, connection, cancellationToken);
        if (tree.IsFailure) return null;
        var overrides = await roles.GetAsync(user.WebmailUid, connection.StorageAccountId, cancellationToken);
        return FolderRoleResolver.Resolve(tree.Value, overrides).Roles
            .FirstOrDefault(r => r.Role == role && r.FolderPath != null)?.FolderPath;
    }
}
```

Dans `IMailSender` :

```csharp
/// <summary>Sends a message something else composed — an invitation reply — through the account's
/// session and files it in Sent like any send. The From is the caller's responsibility.</summary>
Task<Result<SendMessageResult>> SendBuiltAsync(User user, MailAccountConnection connection, MimeMessage message, CancellationToken cancellationToken);
```

Dans `MailSender` : remplacer les paramètres `IMailFolderRepository folders, IFolderRoleStore roles`
par `IRoleFolderLocator locator` ; `SendAsync` devient « construire, puis `SendBuiltAsync`, puis
purger les pièces mises en attente » ; `SendBuiltAsync` porte l'ouverture SMTP, l'envoi et
`AppendToSentAsync`, dont la recherche du dossier devient
`var sent = await locator.FindAsync(user, connection, "sent", cancellationToken); if (sent is null) { warn; return false; }`.
Enregistrer `services.AddScoped<IRoleFolderLocator, RoleFolderLocator>();`.

- [ ] **Step 4g : vert, commit.** `dotnet test --filter "MailSender|RoleFolderLocator|ReplyComposer|InvitationText"`,
  puis la suite complète ; réverter `ApiDocumentation.xml` ;

```bash
git commit -F - <<'EOF'
feat(mail): composer et envoyer la réponse à une invitation

ReplyComposer et InvitationText (FR/EN) ; MailSender.SendBuiltAsync ; RoleFolderLocator extrait (5e1, décision 5).
EOF
```

---

### Task 5 : répondre — `InvitationResponder` et `POST /api/Calendar/Invitations/Respond`

**Files:**
- Create: `src/snoopy.microservice/Models/Calendar/RespondInvitationRequest.cs`
- Create: `src/snoopy.microservice/Services/Calendar/Invitations/InvitationResponder.cs`
- Create: `src/snoopy.microservice/Controllers/CalendarInvitationsController.cs`
- Modify: `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs`
- Test: `Services/Calendar/Invitations/InvitationResponderTests.cs`, `Controllers/CalendarInvitationsControllerTests.cs` (créés)

**Interfaces:**
- Consumes: `InvitationReader.ResolveAsync`/`Block` (tâche 3), `PartStatRewriter` (2), `ReplyComposer`, `IMailSender.SendBuiltAsync`, `IRoleFolderLocator` (4), `IDavCalendarWriter.PutAsync(..., cause)` (1), `IMailMessageRepository.GetAttachmentAsync` / `MoveOrCopyAsync`, `ICalendarStore.ListAsync`, `IProfileReader.GetDisplayNameAsync`.
- Produces:
  ```csharp
  public enum InvitationAnswer { Accepted, Tentative, Declined, AddOnly, Remove }
  public sealed class RespondInvitationRequest
  {
      [Required] public string Folder { get; set; } = "";
      public uint Uid { get; set; }
      [Required(AllowEmptyStrings = true)] public string Part { get; set; } = "";
      public InvitationAnswer Answer { get; set; }
      public Guid? CalendarId { get; set; }
      /// "fr" | "en" — la langue du sujet et du corps du REPLY.
      public string Language { get; set; } = "en";
      /// IANA, le fuseau du navigateur — la date en toutes lettres du corps.
      public string TimeZone { get; set; } = "UTC";
  }
  public sealed record InvitationResponse(MailInvitation Invitation, bool ReplySent, string? ReplyError, bool Trashed);
  public sealed record ResponderFailure(int Status, string Message);
  public interface IInvitationResponder
  {
      Task<Result<InvitationResponse, ResponderFailure>> RespondAsync(
          User user, MailAccountConnection connection, RespondInvitationRequest request, CancellationToken cancellationToken);
  }
  ```

- [ ] **Step 5a : les tests du répondeur.** `InvitationResponderTests.cs` — le lecteur réel sur des
  mocks d'adresses et de base, tout le reste simulé :

```csharp
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

public sealed class InvitationResponderTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();
    private static readonly Guid Personal = Guid.NewGuid();
    private static readonly Guid Work = Guid.NewGuid();
    private static readonly MailAccountConnection Conn = TestConnections.Primary("alice@weesky.be", "pw");
    private static readonly CancellationToken None = CancellationToken.None;
    private readonly User _user = new("alice@weesky.be") { WebmailUid = WebmailUid };
    private readonly Mock<IMailMessageRepository> _messages = new();
    private readonly Mock<IUserAddresses> _addresses = new();
    private readonly Mock<ICalendarEventStore> _events = new();
    private readonly Mock<ICalendarStore> _calendars = new();
    private readonly Mock<IDavCalendarWriter> _writer = new();
    private readonly Mock<IMailSender> _sender = new();
    private readonly Mock<IRoleFolderLocator> _locator = new();
    private readonly Mock<IProfileReader> _profiles = new();

    private static string Fixture(string name) => InvitationParserTests.Fixture(name);

    private InvitationResponder Create()
    {
        _addresses.Setup(a => a.ForAccountAsync(_user, Conn, None)).ReturnsAsync(["alice@weesky.be"]);
        _events.Setup(e => e.FindByUidAsync(WebmailUid, It.IsAny<string>(), None)).ReturnsAsync([]);
        _calendars.Setup(c => c.ListAsync(WebmailUid, None)).ReturnsAsync([
            new CalendarView(Work, "work", "Travail", "", "#00f", 0, "Europe/Brussels", true, false),
            new CalendarView(Personal, "default", "Personnel", "", "#0f0", 1, "Europe/Brussels", true, true),
        ]);
        _writer.Setup(w => w.PutAsync(WebmailUid, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), None, false, null, RevisionCause.Webmail))
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Created, "\"e\"", null, 1));
        _writer.Setup(w => w.DeleteAsync(WebmailUid, It.IsAny<Guid>(), It.IsAny<string>(), None, null))
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Deleted, null, null, 2));
        _sender.Setup(s => s.SendBuiltAsync(_user, Conn, It.IsAny<MimeMessage>(), None))
            .ReturnsAsync(Result.Success(new SendMessageResult(true)));
        _locator.Setup(l => l.FindAsync(_user, Conn, "trash", None)).ReturnsAsync("Trash");
        _messages.Setup(m => m.MoveOrCopyAsync(_user, Conn, "INBOX", It.IsAny<IReadOnlyList<uint>>(), "Trash", false, None))
            .ReturnsAsync(Result.Success());
        _profiles.Setup(p => p.GetDisplayNameAsync(_user, None)).ReturnsAsync("Alice Martin");
        Part("google-request");
        return new InvitationResponder(_messages.Object, new InvitationReader(_addresses.Object, _events.Object),
            _events.Object, _calendars.Object, _writer.Object, _sender.Object, _locator.Object, _profiles.Object,
            NullLogger<InvitationResponder>.Instance);
    }

    private void Part(string fixture) => PartText(Fixture(fixture));

    private void PartText(string ics) =>
        _messages.Setup(m => m.GetAttachmentAsync(_user, Conn, "INBOX", 7u, "2", None))
            .ReturnsAsync(() => Result.Success(new MailAttachmentContent
            {
                Content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ics)), ContentType = "text/calendar",
            }));

    private void Stored(string ics, Guid calendar, string davName = "phone-name.ics") =>
        _events.Setup(e => e.FindByUidAsync(WebmailUid, InvitationParser.Read(ics).Invitation!.Uid, None))
            .ReturnsAsync([new StoredEventRef(Guid.NewGuid(), calendar, davName, ics)]);

    private static RespondInvitationRequest Request(InvitationAnswer answer, Guid? calendarId = null) => new()
    {
        Folder = "INBOX", Uid = 7, Part = "2", Answer = answer, CalendarId = calendarId, Language = "fr", TimeZone = "Europe/Brussels",
    };

    // ── writing ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Accepted_WritesTheOrganizersFileWithThePartStat_InTheDefaultCalendar_UnderAFreshName()
    {
        var sut = Create();
        string? written = null; Guid? calendar = null; string? name = null;
        _writer.Setup(w => w.PutAsync(WebmailUid, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), None, false, null, RevisionCause.Webmail))
            .Callback<Guid, Guid, string, string, CancellationToken, bool, string?, RevisionCause>((_, c, n, ics, _, _, _, _) => { calendar = c; name = n; written = ics; })
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Created, "\"e\"", null, 1));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Personal, calendar);
        Assert.EndsWith(".ics", name);
        Assert.Equal(PartStatRewriter.Rewrite(Fixture("google-request"), "alice@weesky.be", "ACCEPTED"), written);
        Assert.True(result.Value.ReplySent);
        Assert.False(result.Value.Trashed);
    }

    [Fact]
    public async Task Accepted_WithACalendarId_CreatesThere()
    {
        var sut = Create();
        await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted, Work), None);
        _writer.Verify(w => w.PutAsync(WebmailUid, Work, It.IsAny<string>(), It.IsAny<string>(), None, false, null, RevisionCause.Webmail), Times.Once);
    }

    [Fact]
    public async Task Tentative_OnACurrentEvent_RewritesTheStoredFileUnderItsOwnName_AndIgnoresCalendarId()
    {
        var sut = Create();
        // Stored by a phone: its own name, its own alarm, the user already ACCEPTED.
        var stored = PartStatRewriter.Rewrite(Fixture("google-request"), "alice@weesky.be", "ACCEPTED")!
            .Replace("END:VEVENT", "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT10M\r\nEND:VALARM\r\nEND:VEVENT");
        Stored(stored, Personal, "phone-name.ics");
        string? written = null;
        _writer.Setup(w => w.PutAsync(WebmailUid, Personal, "phone-name.ics", It.IsAny<string>(), None, false, null, RevisionCause.Webmail))
            .Callback<Guid, Guid, string, string, CancellationToken, bool, string?, RevisionCause>((_, _, _, ics, _, _, _, _) => written = ics)
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Replaced, "\"f\"", null, 2));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Tentative, Work), None);

        Assert.True(result.IsSuccess);
        Assert.Contains("TRIGGER:-PT10M", written);
        Assert.Contains("PARTSTAT=TENTATIVE", written);
        _writer.Verify(w => w.PutAsync(WebmailUid, Work, It.IsAny<string>(), It.IsAny<string>(), None, false, null, It.IsAny<RevisionCause>()), Times.Never);
    }

    [Fact]
    public async Task Accepted_OnAnOutdatedEvent_WritesTheReceivedFile_UnderTheStoredName()
    {
        var sut = Create();
        Stored(Fixture("google-request"), Work, "phone-name.ics");
        PartText(Fixture("google-request").Replace("SEQUENCE:0", "SEQUENCE:2").Replace("T193000", "T200000"));
        string? written = null;
        _writer.Setup(w => w.PutAsync(WebmailUid, Work, "phone-name.ics", It.IsAny<string>(), None, false, null, RevisionCause.Webmail))
            .Callback<Guid, Guid, string, string, CancellationToken, bool, string?, RevisionCause>((_, _, _, ics, _, _, _, _) => written = ics)
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Replaced, "\"f\"", null, 2));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None);

        Assert.True(result.IsSuccess);
        Assert.Contains("SEQUENCE:2", written);
        Assert.Contains("T200000", written);
    }

    [Fact]
    public async Task Declined_DeletesWhenPresent_SendsTheReply_AndTrashesTheMail()
    {
        var sut = Create();
        Stored(Fixture("google-request"), Personal);

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Declined), None);

        Assert.True(result.IsSuccess);
        _writer.Verify(w => w.DeleteAsync(WebmailUid, Personal, "phone-name.ics", None, null), Times.Once);
        _writer.Verify(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), None, It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<RevisionCause>()), Times.Never);
        Assert.True(result.Value.ReplySent);
        Assert.True(result.Value.Trashed);
        Assert.Equal(InvitationPresence.Absent, result.Value.Invitation.InCalendar);
    }

    [Fact]
    public async Task Declined_WhenAbsent_WritesNothing_StillReplies()
    {
        var sut = Create();
        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Declined), None);

        Assert.True(result.IsSuccess);
        _writer.VerifyNoOtherCalls();
        Assert.True(result.Value.ReplySent);
        Assert.True(result.Value.Trashed);
    }

    [Fact]
    public async Task AddOnly_StoresTheFileUntouchedButMethod_AndSendsNothing()
    {
        var sut = Create();
        _addresses.Setup(a => a.ForAccountAsync(_user, Conn, None)).ReturnsAsync(["someone@weesky.be"]);
        string? written = null;
        _writer.Setup(w => w.PutAsync(WebmailUid, Personal, It.IsAny<string>(), It.IsAny<string>(), None, false, null, RevisionCause.Webmail))
            .Callback<Guid, Guid, string, string, CancellationToken, bool, string?, RevisionCause>((_, _, _, ics, _, _, _, _) => written = ics)
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Created, "\"e\"", null, 1));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.AddOnly), None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PartStatRewriter.StripMethod(Fixture("google-request")), written);
        Assert.False(result.Value.ReplySent);
        Assert.Null(result.Value.ReplyError);
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Remove_OnACancel_DeletesTheStoredEvent_AndSendsNothing()
    {
        var sut = Create();
        Part("google-cancel");
        Stored(Fixture("google-request"), Personal);

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Remove), None);

        Assert.True(result.IsSuccess);
        _writer.Verify(w => w.DeleteAsync(WebmailUid, Personal, "phone-name.ics", None, null), Times.Once);
        _sender.VerifyNoOtherCalls();
        Assert.False(result.Value.Trashed);
    }

    // ── the reply ───────────────────────────────────────────────────────

    [Fact]
    public async Task TheReply_GoesFromAddressedToToTheOrganizer_WithTheUsersName()
    {
        var sut = Create();
        MimeMessage? sent = null;
        _sender.Setup(s => s.SendBuiltAsync(_user, Conn, It.IsAny<MimeMessage>(), None))
            .Callback<User, MailAccountConnection, MimeMessage, CancellationToken>((_, _, m, _) => sent = m)
            .ReturnsAsync(Result.Success(new SendMessageResult(true)));

        await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None);

        Assert.Equal("alice@weesky.be", sent!.From.Mailboxes.Single().Address);
        Assert.Equal("Alice Martin", sent.From.Mailboxes.Single().Name);
        Assert.Equal("marc.dupont@example.org", sent.To.Mailboxes.Single().Address);
        Assert.Equal("Accepté : Dîner chez Marc", sent.Subject);
    }

    [Fact]
    public async Task ASendFailureAfterAcceptance_Is200WithReplySentFalse_TheEventStays()
    {
        var sut = Create();
        _sender.Setup(s => s.SendBuiltAsync(_user, Conn, It.IsAny<MimeMessage>(), None))
            .ReturnsAsync(Result.Failure<SendMessageResult>("smtp_down"));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.ReplySent);
        Assert.Equal("smtp_down", result.Value.ReplyError);
        _writer.Verify(w => w.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), None, null), Times.Never);
    }

    [Fact]
    public async Task ASendFailureOnADecline_LeavesTheMailWhereItIs()
    {
        var sut = Create();
        _sender.Setup(s => s.SendBuiltAsync(_user, Conn, It.IsAny<MimeMessage>(), None))
            .ReturnsAsync(Result.Failure<SendMessageResult>("smtp_down"));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Declined), None);

        Assert.False(result.Value.Trashed);
        _messages.Verify(m => m.MoveOrCopyAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<uint>>(), It.IsAny<string>(), It.IsAny<bool>(), None), Times.Never);
    }

    [Fact]
    public async Task ATrashMoveThatFails_IsTrashedFalse_NothingElseUndone()
    {
        var sut = Create();
        _messages.Setup(m => m.MoveOrCopyAsync(_user, Conn, "INBOX", It.IsAny<IReadOnlyList<uint>>(), "Trash", false, None))
            .ReturnsAsync(Result.Failure("imap_down"));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Declined), None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.ReplySent);
        Assert.False(result.Value.Trashed);
    }

    [Fact]
    public async Task NoOrganizer_IsReplySentFalse_WithAReason()
    {
        var sut = Create();
        PartText(Fixture("google-request").Replace("ORGANIZER;CN=Marc Dupont:mailto:marc.dupont@example.org\r\n", ""));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.ReplySent);
        Assert.Equal(InvitationResponder.NoOrganizer, result.Value.ReplyError);
    }

    // ── refusals ────────────────────────────────────────────────────────

    [Fact]
    public async Task TheFileIsReadFromImap_NeverFromTheRequest()
    {
        var sut = Create();
        _messages.Setup(m => m.GetAttachmentAsync(_user, Conn, "INBOX", 7u, "2", None))
            .ReturnsAsync(Result.Failure<MailAttachmentContent>(ImapSession.AttachmentNotFound));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None);

        Assert.Equal(404, result.Error.Status);
    }

    [Fact]
    public async Task ImapUnreachable_Is502()
    {
        var sut = Create();
        _messages.Setup(m => m.GetAttachmentAsync(_user, Conn, "INBOX", 7u, "2", None))
            .ReturnsAsync(Result.Failure<MailAttachmentContent>("Unable to read the attachment"));

        Assert.Equal(502, (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None)).Error.Status);
    }

    [Fact]
    public async Task AnAnswerForeignToTheMethod_Is400()
    {
        var sut = Create();
        Assert.Equal(400, (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Remove), None)).Error.Status);
        Part("google-cancel");
        Assert.Equal(400, (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None)).Error.Status);
    }

    [Fact]
    public async Task AnOccurrenceAlone_Is400()
    {
        var sut = Create();
        Part("occurrence-cancel");
        Assert.Equal(400, (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Remove), None)).Error.Status);
    }

    [Fact]
    public async Task AnsweringAForwardedInvitation_Is400_AddOnlyIsTheWay()
    {
        var sut = Create();
        _addresses.Setup(a => a.ForAccountAsync(_user, Conn, None)).ReturnsAsync(["someone@weesky.be"]);
        Assert.Equal(400, (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None)).Error.Status);
    }

    [Fact]
    public async Task AnOlderSequenceThanStored_Is409()
    {
        var sut = Create();
        Stored(Fixture("google-request").Replace("SEQUENCE:0", "SEQUENCE:3"), Personal);
        Assert.Equal(409, (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None)).Error.Status);
    }

    [Fact]
    public async Task AFileTheGuardsRefuse_OrNotAnInvitation_Is422()
    {
        var sut = Create();
        PartText("garbage");
        Assert.Equal(422, (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None)).Error.Status);
        Part("google-reply");
        Assert.Equal(422, (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None)).Error.Status);
    }

    [Fact]
    public async Task AWriterRefusal_IsMapped()
    {
        var sut = Create();
        _writer.Setup(w => w.PutAsync(WebmailUid, Personal, It.IsAny<string>(), It.IsAny<string>(), None, false, null, RevisionCause.Webmail))
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.UidConflict, null, "/dav/x", 0));
        Assert.Equal(409, (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None)).Error.Status);

        _writer.Setup(w => w.PutAsync(WebmailUid, Personal, It.IsAny<string>(), It.IsAny<string>(), None, false, null, RevisionCause.Webmail))
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Busy, null, null, 0));
        Assert.Equal(502, (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None)).Error.Status);
        _sender.VerifyNoOtherCalls();
    }
}
```

Vérifier la signature de `CalendarView` (`Models/Calendar/CalendarView.cs`) et l'ordre des
neuf champs ; vérifier `SendMessageResult` ; si `ImapSession.AttachmentNotFound` n'est pas
accessible au projet de tests (il l'est : `MailMessagesControllerTests` l'emploie), garder.

- [ ] **Step 5b : rouge, puis le modèle et le répondeur.** `Models/Calendar/RespondInvitationRequest.cs` :

```csharp
using System.ComponentModel.DataAnnotations;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Models.Calendar;

public enum InvitationAnswer { Accepted, Tentative, Declined, AddOnly, Remove }

/// <summary>What the card sends (spec 5e, décision 4). The file itself never travels: the server
/// re-reads it from IMAP by <see cref="Folder"/>/<see cref="Uid"/>/<see cref="Part"/>.</summary>
public sealed class RespondInvitationRequest
{
    [Required(ErrorMessage = "A folder is required")]
    public string Folder { get; set; } = string.Empty;

    public uint Uid { get; set; }

    /// <summary>The MIME part specifier the detail carried. Empty is a real specifier.</summary>
    [Required(AllowEmptyStrings = true)]
    public string Part { get; set; } = string.Empty;

    public InvitationAnswer Answer { get; set; }

    /// <summary>The target calendar of a creation; ignored once the event exists (décision 4).</summary>
    public Guid? CalendarId { get; set; }

    /// <summary>"fr" or "en": the language of the reply's subject and line.</summary>
    public string Language { get; set; } = "en";

    /// <summary>IANA zone of the browser, for the reply's date in words.</summary>
    public string TimeZone { get; set; } = "UTC";
}

public sealed record InvitationResponse(MailInvitation Invitation, bool ReplySent, string? ReplyError, bool Trashed);

public sealed record ResponderFailure(int Status, string Message);
```

`Services/Calendar/Invitations/InvitationResponder.cs` :

```csharp
using System.Text;
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

public interface IInvitationResponder
{
    Task<Result<InvitationResponse, ResponderFailure>> RespondAsync(
        User user, MailAccountConnection connection, RespondInvitationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Décision 4, in order: the part is re-read from IMAP, judged, matched against the base; the
/// calendar is written through the CalDAV PUT path under the stored name when the event exists;
/// then the REPLY goes out, and a decline's mail goes to the trash once the reply has left. A
/// failure after the write never undoes it: the answer says what was and was not done.
/// </summary>
internal sealed class InvitationResponder(
    IMailMessageRepository messages,
    InvitationReader reader,
    ICalendarEventStore events,
    ICalendarStore calendars,
    IDavCalendarWriter writer,
    IMailSender sender,
    IRoleFolderLocator locator,
    IProfileReader profiles,
    ILogger<InvitationResponder> logger) : IInvitationResponder
{
    internal const string NoOrganizer = "The invitation names no organizer to answer";
    internal const string NotAddressed = "The invitation is not addressed to you; add it without answering";
    internal const string NotInCalendar = "The event is not in your calendar";
    internal const string OccurrenceOnly = "The invitation targets one date of a series";
    internal const string Stale = "A newer version of this event is in your calendar";
    internal const string Incompatible = "This answer does not fit the invitation's method";

    public async Task<Result<InvitationResponse, ResponderFailure>> RespondAsync(
        User user, MailAccountConnection connection, RespondInvitationRequest request, CancellationToken cancellationToken)
    {
        var ics = await ReadPartAsync(user, connection, request, cancellationToken);
        if (ics.IsFailure) return ics.Error;

        var reading = InvitationParser.Read(ics.Value);
        if (reading.Invitation is not { } parsed)
            return new ResponderFailure(422, reading.Reason ?? "The part is not an invitation");
        if (parsed.OccurrenceOnly) return new ResponderFailure(400, OccurrenceOnly);
        var fits = parsed.Method switch
        {
            InvitationMethod.Request => request.Answer is not InvitationAnswer.Remove,
            _ => request.Answer is InvitationAnswer.Remove,
        };
        if (!fits) return new ResponderFailure(400, Incompatible);

        var context = await reader.ResolveAsync(user, connection, parsed, cancellationToken);
        if (context.Presence is InvitationPresence.Newer) return new ResponderFailure(409, Stale);
        var answering = request.Answer is InvitationAnswer.Accepted or InvitationAnswer.Tentative or InvitationAnswer.Declined;
        if (answering && context.AddressedTo is null) return new ResponderFailure(400, NotAddressed);

        var written = await WriteAsync(user, request, ics.Value, parsed, context, cancellationToken);
        if (written.IsFailure) return written.Error;

        var (replySent, replyError) = answering
            ? await SendReplyAsync(user, connection, request, parsed, context.AddressedTo!, cancellationToken)
            : (false, null);

        var trashed = request.Answer is InvitationAnswer.Declined && replySent
            && await TrashAsync(user, connection, request, cancellationToken);

        var after = await reader.ResolveAsync(user, connection, parsed, cancellationToken);
        return new InvitationResponse(InvitationReader.Block(parsed, after, request.Part), replySent, replyError, trashed);
    }

    private async Task<Result<string, ResponderFailure>> ReadPartAsync(
        User user, MailAccountConnection connection, RespondInvitationRequest request, CancellationToken cancellationToken)
    {
        var part = await messages.GetAttachmentAsync(user, connection, request.Folder, request.Uid, request.Part, cancellationToken);
        if (part.IsFailure)
            return new ResponderFailure(
                part.Error is ImapSession.FolderNotFound or ImapSession.MessageNotFound or ImapSession.AttachmentNotFound ? 404 : 502,
                part.Error);
        using var content = part.Value.Content;
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > IcsGuards.MaxIcsBytes) return new ResponderFailure(422, InvitationReader.TooLarge);
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private async Task<Result<bool, ResponderFailure>> WriteAsync(
        User user, RespondInvitationRequest request, string ics, ParsedInvitation parsed, InvitationContext context,
        CancellationToken cancellationToken)
    {
        var stored = context.Stored;
        switch (request.Answer)
        {
            case InvitationAnswer.Declined:
            case InvitationAnswer.Remove:
                if (stored is null)
                    return request.Answer is InvitationAnswer.Remove ? new ResponderFailure(400, NotInCalendar) : true;
                return Map(await writer.DeleteAsync(user.WebmailUid, stored.CalendarId, stored.DavName, cancellationToken));

            case InvitationAnswer.AddOnly:
                return await PutAsync(user, request, stored, PartStatRewriter.StripMethod(ics), cancellationToken);

            default:
                // Current: the stored file, so a phone's alarm survives a change of mind; else the
                // organizer's (new) version replaces everything (décision 4).
                var source = context.Presence is InvitationPresence.Current ? stored!.IcsRaw : ics;
                var partStat = request.Answer is InvitationAnswer.Accepted ? "ACCEPTED" : "TENTATIVE";
                var rewritten = PartStatRewriter.Rewrite(source, context.AddressedTo!, partStat);
                if (rewritten is null) return new ResponderFailure(400, NotAddressed);
                return await PutAsync(user, request, stored, rewritten, cancellationToken);
        }
    }

    /// <summary>A rewrite keeps the stored name and calendar — the only way to update what a
    /// phone put under a name of its own (décision 4); a creation takes calendarId, else the default.</summary>
    private async Task<Result<bool, ResponderFailure>> PutAsync(
        User user, RespondInvitationRequest request, StoredEventRef? stored, string ics, CancellationToken cancellationToken)
    {
        Guid calendarId; string davName;
        if (stored is not null) (calendarId, davName) = (stored.CalendarId, stored.DavName);
        else
        {
            var list = await calendars.ListAsync(user.WebmailUid, cancellationToken);
            var target = request.CalendarId is { } wanted ? list.FirstOrDefault(c => c.Id == wanted) : null;
            target ??= list.FirstOrDefault(c => c.IsDefault) ?? list.FirstOrDefault();
            if (target is null) return new ResponderFailure(502, "No calendar to write into");
            (calendarId, davName) = (target.Id, $"{Guid.NewGuid()}.ics");
        }
        return Map(await writer.PutAsync(user.WebmailUid, calendarId, davName, ics, cancellationToken, cause: RevisionCause.Webmail));
    }

    private static Result<bool, ResponderFailure> Map(DavWriteOutcome outcome) => outcome.Status switch
    {
        DavWriteStatus.Created or DavWriteStatus.Replaced or DavWriteStatus.Deleted or DavWriteStatus.NotFound => true,
        DavWriteStatus.UidConflict or DavWriteStatus.AlreadyExists or DavWriteStatus.PreconditionFailed =>
            new ResponderFailure(409, "The calendar already holds this event under another name"),
        DavWriteStatus.Busy => new ResponderFailure(502, "The calendar is busy; try again"),
        _ => new ResponderFailure(422, $"The calendar refused the file ({outcome.Status})"),
    };

    private async Task<(bool Sent, string? Error)> SendReplyAsync(
        User user, MailAccountConnection connection, RespondInvitationRequest request, ParsedInvitation parsed,
        string addressedTo, CancellationToken cancellationToken)
    {
        if (parsed.Organizer is null) return (false, NoOrganizer);
        var partStat = request.Answer switch
        {
            InvitationAnswer.Accepted => "ACCEPTED", InvitationAnswer.Tentative => "TENTATIVE", _ => "DECLINED",
        };
        var name = await profiles.GetDisplayNameAsync(user, cancellationToken);
        var message = ReplyComposer.Compose(new ReplyInput(
            addressedTo, string.IsNullOrWhiteSpace(name) ? user.FullName : name,
            parsed.Organizer.Email, parsed.Organizer.Name, parsed, partStat,
            request.TimeZone, request.Language, DateTime.UtcNow));
        var sent = await sender.SendBuiltAsync(user, connection, message, cancellationToken);
        if (sent.IsFailure)
            logger.LogWarning("The reply to invitation {Uid} could not be sent: {Error}", parsed.Uid, sent.Error);
        return sent.IsSuccess ? (true, null) : (false, sent.Error);
    }

    /// <summary>Best effort: the reply is gone; a move that fails leaves the mail in place, and the
    /// answer says so (décision 4, risques).</summary>
    private async Task<bool> TrashAsync(
        User user, MailAccountConnection connection, RespondInvitationRequest request, CancellationToken cancellationToken)
    {
        var trash = await locator.FindAsync(user, connection, "trash", cancellationToken);
        if (trash is null || string.Equals(trash, request.Folder, StringComparison.Ordinal)) return false;
        var moved = await messages.MoveOrCopyAsync(user, connection, request.Folder, [request.Uid], trash, copy: false, cancellationToken);
        if (moved.IsFailure) logger.LogWarning("Declined invitation {Uid} stays in {Folder}: {Error}", request.Uid, request.Folder, moved.Error);
        return moved.IsSuccess;
    }
}
```

`Result<T, E>` de CSharpFunctionalExtensions convertit implicitement depuis `T` et depuis `E` ;
si le compilateur refuse une conversion (`return true;`), écrire `Result.Success<bool, ResponderFailure>(true)`
et `Result.Failure<bool, ResponderFailure>(new ResponderFailure(...))`. Enregistrer :

```csharp
services.AddScoped<InvitationReader>();
services.AddScoped<IInvitationReader>(provider => provider.GetRequiredService<InvitationReader>());
services.AddScoped<IInvitationResponder, InvitationResponder>();
```

(et retirer l'enregistrement direct de `IInvitationReader` posé en tâche 3).

- [ ] **Step 5c : vert.** `dotnet test --filter InvitationResponder`.

- [ ] **Step 5d : le contrôleur, tests d'abord.** `Controllers/CalendarInvitationsControllerTests.cs` :

```csharp
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class CalendarInvitationsControllerTests
{
    private static readonly MailAccountConnection Conn = TestConnections.Primary("alice@weesky.be", "pw");
    private readonly Mock<IInvitationResponder> _responder = new();
    private readonly Mock<IAccountConnectionResolver> _connections = new();

    private CalendarInvitationsController Create()
    {
        _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(Conn));
        return new CalendarInvitationsController(_responder.Object, _connections.Object)
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be", Guid.NewGuid()),
        };
    }

    private static RespondInvitationRequest Request() => new() { Folder = "INBOX", Uid = 7, Part = "2", Answer = InvitationAnswer.Accepted };

    [Fact]
    public async Task Respond_ReturnsTheResponderAnswer()
    {
        var answer = new InvitationResponse(new MailInvitation { Uid = "u" }, true, null, false);
        _responder.Setup(r => r.RespondAsync(It.IsAny<User>(), Conn, It.IsAny<RespondInvitationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(answer);

        var result = await Create().Respond(Request(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(answer, ok.Value);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    [InlineData(502)]
    public async Task Respond_MapsTheFailureStatus(int status)
    {
        _responder.Setup(r => r.RespondAsync(It.IsAny<User>(), Conn, It.IsAny<RespondInvitationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResponderFailure(status, "why"));

        var result = await Create().Respond(Request(), CancellationToken.None);

        var obj = Assert.IsAssignableFrom<ObjectResult>(result.Result);
        Assert.Equal(status, obj.StatusCode);
    }

    [Fact]
    public async Task Respond_WithoutCredentials_Is401()
    {
        var controller = Create();
        _connections.Setup(c => c.ResolveAsync(It.IsAny<User>(), It.IsAny<HttpRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<MailAccountConnection>("credentials_unavailable"));

        var result = await controller.Respond(Request(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsAssignableFrom<ObjectResult>(result.Result).StatusCode);
    }
}
```

- [ ] **Step 5e : rouge, puis le contrôleur.** `Controllers/CalendarInvitationsController.cs` :

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// Answering an invitation received by mail (spec 5e, décision 4). A mail controller rather than a
/// calendar one: the file is re-read from the mailbox the request names, so the account is
/// resolved the way every api/Mail action resolves it.
/// </summary>
[ApiController]
[Route("api/Calendar/Invitations")]
[Authorize]
public sealed class CalendarInvitationsController(
    IInvitationResponder responder, IAccountConnectionResolver connections) : MailControllerBase(connections)
{
    /// <summary>Records the answer in the calendar and mails it to the organizer.</summary>
    /// <param name="request">the message, the part, the answer, and where a creation goes</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The invitation block as it now stands, with what was and was not sent</response>
    /// <response code="400">An answer foreign to the method, an invitation targeting one date of a series, or one not addressed to the user</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such message, or no such part on it</response>
    /// <response code="409">A newer version of the event is in the calendar, or the calendar holds the UID under another name</response>
    /// <response code="422">The part is not an invitation, or fails the guards every stored file passes</response>
    /// <response code="502">The mail server or the calendar could not be reached before the write</response>
    [HttpPost("Respond")]
    [ProducesResponseType(typeof(InvitationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<InvitationResponse>> Respond(RespondInvitationRequest request, CancellationToken cancellationToken)
    {
        var resolution = await TryResolveAsync(cancellationToken);
        if (resolution.Failed(out var error, out var connection)) return error;

        var result = await responder.RespondAsync(AuthenticatedUser, connection, request, cancellationToken);
        if (result.IsSuccess) return Ok(result.Value);
        return StatusCode(result.Error.Status, new { message = result.Error.Message });
    }
}
```

Vérifier la forme d'enveloppe d'erreur que `ApiBaseController.BadRequestEnveloppe` produit et
la reproduire (`new { message }` ou la classe qu'il emploie) pour que le frontend lise le
motif comme partout (`apiErrorMessage`).

- [ ] **Step 5f : vert, commit.** `dotnet test` complet ; réverter `ApiDocumentation.xml` ;

```bash
git commit -F - <<'EOF'
feat(agenda): répondre à une invitation reçue par mail

POST /api/Calendar/Invitations/Respond : relecture IMAP, dépôt par le chemin PUT, REPLY, corbeille sur refus (5e1, décisions 4 et 5).
EOF
```

---

### Task 6 : l'encart dans le lecteur

Types, client, phrases, l'encart et ses états, l'intégration au lecteur, les locales, la sonde.
La maquette (spec, « Les maquettes ») fait foi pour le dessin ; l'état 4 est redessiné ici en
trois boutons (décision 3).

**Files:**
- Modify: `src/frontend/src/modules/mail/api/mailTypes.ts:107-141`
- Modify: `src/frontend/src/api.js` (après `getMailMessage`)
- Modify: `src/frontend/src/modules/mail/queries.ts` (après `useMessage`)
- Create: `src/frontend/src/modules/mail/reader/invitationText.ts`, `invitationText.test.ts`
- Create: `src/frontend/src/modules/mail/reader/InvitationCard.tsx`, `InvitationCard.test.tsx`
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx:165-210, 440-470`, `MessageReader.test.tsx`
- Modify: `src/frontend/src/styles/mail.css` (après `.reader-blocked-images`)
- Modify: `src/frontend/src/locales/{en,fr}/mail.json` (`reader.invitation`)
- Create: `src/frontend/probes/invitation-card.html`

**Interfaces:**
- Produces:
  ```ts
  // mailTypes.ts
  export type InvitationMethod = 'Request' | 'Cancel'
  export type InvitationPresence = 'Absent' | 'Current' | 'Outdated' | 'Newer' | 'Cancelled'
  export type InvitationAnswer = 'Accepted' | 'Tentative' | 'Declined' | 'AddOnly' | 'Remove'
  export interface InvitationPerson { email: string; name?: string }
  export interface MailInvitation {
    method: InvitationMethod; uid: string; sequence: number
    summary?: string; start?: string; end?: string; startDate?: string; endDateExclusive?: string
    isAllDay: boolean; location?: string; repeats: boolean
    organizer?: InvitationPerson; attendees: InvitationPerson[]
    addressedTo?: string; filePartStat?: string; savedPartStat?: string
    inCalendar: InvitationPresence; calendarId?: string; occurrenceOnly: boolean
    part: string; unreadable: boolean; reason?: string
  }
  export interface InvitationResponse { invitation: MailInvitation; replySent: boolean; replyError?: string; trashed: boolean }
  export interface RespondInvitationArgs {
    folder: string; uid: number; part: string; answer: InvitationAnswer; calendarId?: string; language: string; timeZone: string
  }
  // MailMessageDetail
  invitation?: MailInvitation
  // api.js
  respondInvitation: (body, options) => request('POST', '/api/Calendar/Invitations/Respond', body, options)
  // queries.ts
  export function useRespondInvitation(): UseMutationResult<InvitationResponse, ApiError, RespondInvitationArgs>
  // invitationText.ts
  export type CardState = 'invite' | 'answered' | 'forwarded' | 'updated' | 'cancelled' | 'noAction' | 'unreadable'
  export function cardStateOf(i: MailInvitation): CardState
  export function whenOf(i: MailInvitation, tz: string, lang: string, region: string, cycle: 'h12' | 'h23', t: TFunction): string
  export function noActionKey(i: MailInvitation): 'occurrenceOnly' | 'newer' | 'cancelAbsent'
  // InvitationCard.tsx
  interface Props { invitation: MailInvitation; folderPath: string; uid: number; onTrashed: () => void }
  export default function InvitationCard(props: Props): JSX.Element
  ```

- [ ] **Step 6a : types, client, mutation.** Ajouter les types ci-dessus à `mailTypes.ts` (commentaires
  de doc dans le style du fichier : ce que le champ signifie), `invitation?: MailInvitation` sur
  `MailMessageDetail`, `respondInvitation` dans `api.js`, et dans `queries.ts` :

```ts
/** One answer to an invitation (spec 5e, décision 4). No cache surgery: the card redraws from the
    answer, and the message query is refreshed so a reopen agrees with it. */
export function useRespondInvitation() {
  const accountId = useAccountId()
  const queryClient = useQueryClient()
  return useMutation<InvitationResponse, ApiError, RespondInvitationArgs>({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: args => api.respondInvitation(args, { accountId }) as Promise<InvitationResponse>,
    onSuccess: (_answer, args) =>
      queryClient.invalidateQueries({ queryKey: mailKeys.message(accountId, args.folder, args.uid) }),
  })
}
```

`npm run typecheck`.

- [ ] **Step 6b : les phrases, test d'abord.** `invitationText.test.ts` :

```ts
import { describe, it, expect } from 'vitest'
import type { TFunction } from 'i18next'
import { cardStateOf, noActionKey, whenOf } from './invitationText'
import type { MailInvitation } from '../api/mailTypes'

const base: MailInvitation = {
  method: 'Request', uid: 'u', sequence: 0, isAllDay: false, repeats: false, attendees: [],
  addressedTo: 'alice@weesky.be', filePartStat: 'NEEDS-ACTION', inCalendar: 'Absent',
  occurrenceOnly: false, part: '2', unreadable: false,
  summary: 'Dîner chez Marc', start: '2026-10-10T17:30:00Z', end: '2026-10-10T20:30:00Z',
}
const t = ((key: string) => key) as unknown as TFunction

describe('cardStateOf', () => {
  it('names the seven states of the mock-up', () => {
    expect(cardStateOf(base)).toBe('invite')
    expect(cardStateOf({ ...base, inCalendar: 'Current', savedPartStat: 'ACCEPTED' })).toBe('answered')
    expect(cardStateOf({ ...base, addressedTo: undefined, filePartStat: undefined })).toBe('forwarded')
    expect(cardStateOf({ ...base, inCalendar: 'Outdated', savedPartStat: 'ACCEPTED' })).toBe('updated')
    expect(cardStateOf({ ...base, method: 'Cancel', inCalendar: 'Cancelled' })).toBe('cancelled')
    expect(cardStateOf({ ...base, method: 'Cancel', inCalendar: 'Absent' })).toBe('noAction')
    expect(cardStateOf({ ...base, inCalendar: 'Newer' })).toBe('noAction')
    expect(cardStateOf({ ...base, occurrenceOnly: true })).toBe('noAction')
    expect(cardStateOf({ ...base, unreadable: true, reason: 'x' })).toBe('unreadable')
  })

  it('a forwarded invitation already in the calendar is answered-less current', () => {
    expect(cardStateOf({ ...base, addressedTo: undefined, inCalendar: 'Current' })).toBe('answered')
  })
})

describe('noActionKey', () => {
  it('picks the sentence', () => {
    expect(noActionKey({ ...base, occurrenceOnly: true })).toBe('occurrenceOnly')
    expect(noActionKey({ ...base, inCalendar: 'Newer' })).toBe('newer')
    expect(noActionKey({ ...base, method: 'Cancel', inCalendar: 'Absent' })).toBe('cancelAbsent')
  })
})

describe('whenOf', () => {
  it('writes the date in the browser zone, in words', () => {
    expect(whenOf(base, 'Europe/Brussels', 'fr', 'fr-BE', 'h23', t)).toBe('samedi 10 octobre 2026, 19:30 – 22:30')
    expect(whenOf(base, 'America/New_York', 'en', 'en-GB', 'h23', t)).toBe('Saturday 10 October 2026, 13:30 – 16:30')
  })

  it('a whole day is its date and the all-day word', () => {
    const day = { ...base, isAllDay: true, start: undefined, end: undefined, startDate: '2026-11-01', endDateExclusive: '2026-11-02' }
    expect(whenOf(day, 'Europe/Brussels', 'fr', 'fr-BE', 'h23', t)).toBe('dimanche 1 novembre 2026 · reader.invitation.allDay')
  })

  it('several whole days are a range', () => {
    const days = { ...base, isAllDay: true, start: undefined, end: undefined, startDate: '2026-11-01', endDateExclusive: '2026-11-03' }
    expect(whenOf(days, 'Europe/Brussels', 'en', 'en-GB', 'h23', t)).toContain('1')
    expect(whenOf(days, 'Europe/Brussels', 'en', 'en-GB', 'h23', t)).toContain('2 November 2026')
  })

  it('a day crossing midnight names both days', () => {
    const late = { ...base, start: '2026-10-10T21:30:00Z', end: '2026-10-11T00:30:00Z' }
    expect(whenOf(late, 'Europe/Brussels', 'fr', 'fr-BE', 'h23', t)).toBe('samedi 10 octobre 2026, 23:30 – dimanche 11 octobre 2026, 02:30')
  })
})
```

- [ ] **Step 6c : rouge, puis `invitationText.ts`.** Réutiliser `modules/calendar/calendarLocale.ts`
  (`dateLocaleOf`, `formatLongDay`, `formatLongDayRange`, `formatTime`) et `plainDate.ts`
  (`plainDateOf`, `addDays`) :

```ts
import type { TFunction } from 'i18next'
import type { MailInvitation } from '../api/mailTypes'
import { dateLocaleOf, formatLongDay, formatLongDayRange, formatTime } from '../../calendar/calendarLocale'
import { addDays, plainDateOf, type PlainDate } from '../../calendar/plainDate'

/** The seven states of the mock-up (spec 5e, décision 3 and 1 bis). */
export type CardState = 'invite' | 'answered' | 'forwarded' | 'updated' | 'cancelled' | 'noAction' | 'unreadable'

export function cardStateOf(i: MailInvitation): CardState {
  if (i.unreadable) return 'unreadable'
  if (i.occurrenceOnly || i.inCalendar === 'Newer') return 'noAction'
  if (i.method === 'Cancel') return i.inCalendar === 'Cancelled' ? 'cancelled' : 'noAction'
  switch (i.inCalendar) {
    case 'Current': return 'answered'
    case 'Outdated': return 'updated'
    default: return i.addressedTo ? 'invite' : 'forwarded'
  }
}

export function noActionKey(i: MailInvitation): 'occurrenceOnly' | 'newer' | 'cancelAbsent' {
  if (i.occurrenceOnly) return 'occurrenceOnly'
  if (i.inCalendar === 'Newer') return 'newer'
  return 'cancelAbsent'
}

/** The date in words, in the browser's zone — where the grid places every hour (décision 6). */
export function whenOf(
  i: MailInvitation, tz: string, lang: string, region: string, cycle: 'h12' | 'h23', t: TFunction,
): string {
  const locale = dateLocaleOf(lang, region)
  if (i.isAllDay && i.startDate) {
    const first = i.startDate as PlainDate
    const last = i.endDateExclusive ? addDays(i.endDateExclusive as PlainDate, -1) : first
    return first === last
      ? `${formatLongDay(first, locale, true)} · ${t('reader.invitation.allDay')}`
      : formatLongDayRange(first, last, locale)
  }
  if (!i.start) return ''
  const start = new Date(i.start)
  const end = i.end ? new Date(i.end) : null
  const day = (at: Date) => formatLongDay(plainDateOf(at, tz), locale, true)
  const clock = (at: Date) => formatTime(at, lang, cycle, tz, region)
  if (!end) return `${day(start)}, ${clock(start)}`
  return plainDateOf(start, tz) === plainDateOf(end, tz)
    ? `${day(start)}, ${clock(start)} – ${clock(end)}`
    : `${day(start)}, ${clock(start)} – ${day(end)}, ${clock(end)}`
}
```

Vérifier les signatures réelles (`formatLongDay(day, locale, withYear)`, `formatLongDayRange(from, to, locale)`)
et que `PlainDate` est bien le type `string` de `plainDate.ts` ; ajuster les attentes du test
à ce que `Intl` produit pour `fr-BE`/`en-GB` (le tiret « – » entre deux heures est celui de
`EventPreview`).

- [ ] **Step 6d : les locales.** Dans `en/mail.json`, sous `reader` :

```json
"invitation": {
  "badgeRequest": "Invitation",
  "badgeUpdate": "Update",
  "badgeCancel": "Cancelled",
  "when": "When",
  "where": "Where",
  "organizer": "Organiser",
  "attendees": "Attendees",
  "allDay": "All day",
  "repeats": "Repeats",
  "accept": "Accept",
  "tentative": "Tentative",
  "decline": "Decline",
  "addOnly": "Add to calendar",
  "remove": "Remove from my calendar",
  "resend": "Resend",
  "calendar": "Calendar",
  "answered": {
    "ACCEPTED": "You accepted",
    "TENTATIVE": "You answered tentatively",
    "in": "{{answer}} · in {{calendar}}",
    "added": "In your calendar · {{calendar}}"
  },
  "forwarded": "This invitation was not addressed to you. You can add it to your calendar without answering.",
  "alreadyAnswered": "The organiser recorded you as having {{answer}}.",
  "partstat": { "ACCEPTED": "accepted", "TENTATIVE": "answered tentatively", "DECLINED": "declined" },
  "updated": "The organiser changed this event. Your previous answer is highlighted.",
  "cancelled": "The organiser cancelled this event.",
  "noAction": {
    "occurrenceOnly": "This invitation only concerns one date of the series. Edit the event in your calendar.",
    "newer": "Your calendar holds a newer version of this event.",
    "cancelAbsent": "This event is not in your calendar."
  },
  "unreadable": "Unreadable invitation",
  "unreadableHint": "The calendar file could not be read. It stays available as an attachment.",
  "replyFailed": "The reply could not be sent.",
  "addedReplyFailed": "Added to your calendar. The reply could not be sent.",
  "failed": "The answer could not be recorded.",
  "reload": "Reload the message"
}
```

et la version `fr` (insécables avant `:` `;` `!` `?` et dans « ») :

```json
"invitation": {
  "badgeRequest": "Invitation",
  "badgeUpdate": "Mise à jour",
  "badgeCancel": "Annulé",
  "when": "Quand",
  "where": "Où",
  "organizer": "Organisateur",
  "attendees": "Invités",
  "allDay": "Journée entière",
  "repeats": "Se répète",
  "accept": "Accepter",
  "tentative": "Provisoire",
  "decline": "Refuser",
  "addOnly": "Ajouter à l'agenda",
  "remove": "Retirer de mon agenda",
  "resend": "Renvoyer",
  "calendar": "Agenda",
  "answered": {
    "ACCEPTED": "Vous avez accepté",
    "TENTATIVE": "Vous avez répondu provisoire",
    "in": "{{answer}} · dans {{calendar}}",
    "added": "Dans votre agenda · {{calendar}}"
  },
  "forwarded": "Cette invitation ne vous était pas adressée. Vous pouvez l'ajouter à votre agenda sans y répondre.",
  "alreadyAnswered": "L'organisateur vous a noté comme ayant {{answer}}.",
  "partstat": { "ACCEPTED": "accepté", "TENTATIVE": "répondu provisoire", "DECLINED": "refusé" },
  "updated": "L'organisateur a modifié ce rendez-vous. Votre réponse précédente est surlignée.",
  "cancelled": "L'organisateur a annulé ce rendez-vous.",
  "noAction": {
    "occurrenceOnly": "Cette invitation ne porte que sur une date de la série. Corrigez le rendez-vous dans votre agenda.",
    "newer": "Votre agenda porte une version plus récente de ce rendez-vous.",
    "cancelAbsent": "Ce rendez-vous n'est pas dans votre agenda."
  },
  "unreadable": "Invitation illisible",
  "unreadableHint": "Le fichier d'agenda n'a pas pu être lu. Il reste disponible en pièce jointe.",
  "replyFailed": "La réponse n'a pas pu être envoyée.",
  "addedReplyFailed": "Ajouté à l'agenda. La réponse n'a pas pu être envoyée.",
  "failed": "La réponse n'a pas pu être enregistrée.",
  "reload": "Recharger le message"
}
```

`npm test -- parity keys` vert avant de continuer.

- [ ] **Step 6e : l'encart, tests d'abord.** `InvitationCard.test.tsx` — même patron de mocks que
  `MessageReader.test.tsx` (`vi.mock('../../../api.js', …)` avec `respondInvitation` et
  `getCalendars`, un `QueryClientProvider`, `MemoryRouter`) ; un `render(card)` utilitaire qui
  monte `<InvitationCard invitation={…} folderPath="INBOX" uid={7} onTrashed={onTrashed} />` :

```ts
const base: MailInvitation = { /* le même que dans invitationText.test.ts */ }
const calendars = [{ id: 'c1', davName: 'default', displayName: 'Personnel', isDefault: true, /* … */ }]

it('state 1: title, badge, four rows, three buttons', async () => {
  mocks.getCalendars.mockResolvedValue({ calendars })
  render(base)
  expect(screen.getByText('Dîner chez Marc')).toBeInTheDocument()
  expect(screen.getByText('Invitation')).toBeInTheDocument()
  expect(screen.getByText('Marc Dupont')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Accept' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Tentative' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Decline' })).toBeInTheDocument()
  expect(screen.queryByLabelText('Calendar')).toBeNull()   // one calendar: no selector
})

it('shows the calendar selector only with several calendars, and sends the chosen one', async () => {
  mocks.getCalendars.mockResolvedValue({ calendars: [...calendars, { ...calendars[0], id: 'c2', davName: 'work', displayName: 'Travail', isDefault: false }] })
  mocks.respondInvitation.mockResolvedValue({ invitation: { ...base, inCalendar: 'Current', savedPartStat: 'ACCEPTED', calendarId: 'c2' }, replySent: true, trashed: false })
  render(base)
  fireEvent.change(await screen.findByLabelText('Calendar'), { target: { value: 'c2' } })
  fireEvent.click(screen.getByRole('button', { name: 'Accept' }))
  await waitFor(() => expect(mocks.respondInvitation).toHaveBeenCalledWith(
    expect.objectContaining({ folder: 'INBOX', uid: 7, part: '2', answer: 'Accepted', calendarId: 'c2', language: 'en' }),
    expect.anything()))
})

it('a click disables the buttons, then redraws state 2 from the answer', async () => {
  let resolve!: (v: unknown) => void
  mocks.respondInvitation.mockReturnValue(new Promise(r => { resolve = r }))
  render(base)
  fireEvent.click(screen.getByRole('button', { name: 'Accept' }))
  expect(screen.getByRole('button', { name: 'Accept' })).toBeDisabled()
  resolve({ invitation: { ...base, inCalendar: 'Current', savedPartStat: 'ACCEPTED', calendarId: 'c1' }, replySent: true, trashed: false })
  expect(await screen.findByText('You accepted · in Personnel')).toBeInTheDocument()
  // The two other answers stay, discreet.
  expect(screen.getByRole('button', { name: 'Tentative' })).toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Decline' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Accept' })).toBeNull()
})

it('a decline that trashed the mail calls onTrashed; one that did not stays', async () => { … deux cas … })

it('state 3: forwarded offers Add only', …)
it('state 4: updated shows the three answers, the previous one highlighted (aria-pressed)', …)
it('state 5: cancelled offers Remove; state 6: the three sentences without a button', …)
it('state 7: unreadable', …)
it('a send failure shows Resend, which replays the same answer', async () => {
  mocks.respondInvitation.mockResolvedValueOnce({ invitation: { ...base, inCalendar: 'Current', savedPartStat: 'ACCEPTED', calendarId: 'c1' }, replySent: false, replyError: 'smtp', trashed: false })
  render(base)
  fireEvent.click(screen.getByRole('button', { name: 'Accept' }))
  expect(await screen.findByText('Added to your calendar. The reply could not be sent.')).toBeInTheDocument()
  mocks.respondInvitation.mockResolvedValueOnce({ invitation: { ...base, inCalendar: 'Current', savedPartStat: 'ACCEPTED', calendarId: 'c1' }, replySent: true, trashed: false })
  fireEvent.click(screen.getByRole('button', { name: 'Resend' }))
  await waitFor(() => expect(mocks.respondInvitation).toHaveBeenLastCalledWith(expect.objectContaining({ answer: 'Accepted' }), expect.anything()))
})
it('a 404 offers to reload the message', …)
it('the context sentence when the file already carries an answer', …)   // filePartStat: 'ACCEPTED', inCalendar: 'Absent'
```

Écrire chaque cas en entier sur le modèle des trois premiers ; les libellés sont ceux des
locales `en` (les tests tournent en anglais, comme `MessageReader.test.tsx`).

- [ ] **Step 6f : rouge, puis l'encart.** `InvitationCard.tsx` :

```tsx
import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import CalendarIcon from '../../../icons/CalendarIcon'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { ApiError } from '../../../api.js'
import { hourCycleOf } from '../../calendar/calendarLocale'
import { useCalendars } from '../../calendar/queries'
import type { InvitationAnswer, InvitationResponse, MailInvitation } from '../api/mailTypes'
import { useRespondInvitation } from '../queries'
import { cardStateOf, noActionKey, whenOf } from './invitationText'

interface Props {
  invitation: MailInvitation
  folderPath: string
  uid: number
  /** The decline's mail left for the trash: the reader moves on as after a delete. */
  onTrashed: () => void
}

/** The card between the header and the body (spec 5e, décision 6): the event as the organizer
    wrote it, and what to do about it — drawn from the block, redrawn from the answer. */
export default function InvitationCard({ invitation: initial, folderPath, uid, onTrashed }: Props) {
  const { t, i18n } = useTranslation('mail')
  const tz = useMemo(() => Intl.DateTimeFormat().resolvedOptions().timeZone, [])
  const region = navigator.language
  const cycle = useMemo(() => hourCycleOf(region), [region])
  const [invitation, setInvitation] = useState(initial)
  const [outcome, setOutcome] = useState<InvitationResponse | null>(null)
  const [lastAnswer, setLastAnswer] = useState<InvitationAnswer | null>(null)
  const [error, setError] = useState<{ message: string; gone: boolean } | null>(null)
  const [calendarId, setCalendarId] = useState<string | undefined>(undefined)
  const respond = useRespondInvitation()
  const { data: calendars } = useCalendars(tz)

  const state = cardStateOf(invitation)
  const busy = respond.isPending
  const calendarName = (id?: string) => calendars?.find(c => c.id === id)?.displayName ?? ''

  async function answer(value: InvitationAnswer) {
    setError(null)
    setLastAnswer(value)
    try {
      const result = await respond.mutateAsync({
        folder: folderPath, uid, part: invitation.part, answer: value, calendarId,
        language: i18n.language.startsWith('fr') ? 'fr' : 'en', timeZone: tz,
      })
      setInvitation(result.invitation)
      setOutcome(result)
      if (result.trashed) onTrashed()
    } catch (caught) {
      const gone = caught instanceof ApiError && caught.status === 404
      setError({ message: apiErrorMessage(caught, t('reader.invitation.failed')), gone })
    }
  }

  const badge = invitation.method === 'Cancel' ? t('reader.invitation.badgeCancel')
    : invitation.inCalendar === 'Outdated' ? t('reader.invitation.badgeUpdate') : t('reader.invitation.badgeRequest')

  if (state === 'unreadable') {
    return (
      <section className="invitation-card is-unreadable" aria-label={t('reader.invitation.unreadable')}>
        <div className="invitation-card-head">
          <CalendarIcon size={16} /><span className="invitation-card-title">{t('reader.invitation.unreadable')}</span>
        </div>
        <p className="invitation-card-hint">{t('reader.invitation.unreadableHint')}</p>
      </section>
    )
  }

  const answerButton = (value: InvitationAnswer, key: string, partStat: string) => (
    <button type="button" className="btn" disabled={busy} onClick={() => answer(value)}
      aria-pressed={state === 'updated' && invitation.savedPartStat === partStat ? true : undefined}>
      {t(`reader.invitation.${key}`)}
    </button>
  )
  const three = (
    <>
      {answerButton('Accepted', 'accept', 'ACCEPTED')}
      {answerButton('Tentative', 'tentative', 'TENTATIVE')}
      {answerButton('Declined', 'decline', 'DECLINED')}
    </>
  )

  let context: string | null = null
  if (state === 'forwarded') context = t('reader.invitation.forwarded')
  else if (state === 'updated') context = t('reader.invitation.updated')
  else if (state === 'cancelled') context = t('reader.invitation.cancelled')
  else if (state === 'invite' && invitation.filePartStat && invitation.filePartStat !== 'NEEDS-ACTION')
    context = t('reader.invitation.alreadyAnswered', { answer: t(`reader.invitation.partstat.${invitation.filePartStat}`) })

  let foot: JSX.Element
  switch (state) {
    case 'invite':
    case 'updated':
      foot = (
        <div className="invitation-card-actions">
          {invitation.addressedTo ? three : <button type="button" className="btn" disabled={busy} onClick={() => answer('AddOnly')}>{t('reader.invitation.addOnly')}</button>}
          {state === 'invite' && calendars && calendars.length > 1 && (
            <label className="invitation-card-calendar">
              <span>{t('reader.invitation.calendar')}</span>
              <select value={calendarId ?? calendars.find(c => c.isDefault)?.id} onChange={e => setCalendarId(e.target.value)}>
                {calendars.map(c => <option key={c.id} value={c.id}>{c.displayName}</option>)}
              </select>
            </label>
          )}
        </div>
      )
      break
    case 'answered':
      foot = (
        <div className="invitation-card-actions is-answered">
          <span className="invitation-card-answer">
            {invitation.savedPartStat
              ? t('reader.invitation.answered.in', { answer: t(`reader.invitation.answered.${invitation.savedPartStat}`), calendar: calendarName(invitation.calendarId) })
              : t('reader.invitation.answered.added', { calendar: calendarName(invitation.calendarId) })}
          </span>
          {invitation.addressedTo && (
            <span className="invitation-card-others">
              {invitation.savedPartStat !== 'ACCEPTED' && answerButton('Accepted', 'accept', 'ACCEPTED')}
              {invitation.savedPartStat !== 'TENTATIVE' && answerButton('Tentative', 'tentative', 'TENTATIVE')}
              {answerButton('Declined', 'decline', 'DECLINED')}
            </span>
          )}
        </div>
      )
      break
    case 'forwarded':
      foot = <div className="invitation-card-actions"><button type="button" className="btn" disabled={busy} onClick={() => answer('AddOnly')}>{t('reader.invitation.addOnly')}</button></div>
      break
    case 'cancelled':
      foot = <div className="invitation-card-actions"><button type="button" className="btn" disabled={busy} onClick={() => answer('Remove')}>{t('reader.invitation.remove')}</button></div>
      break
    default:
      foot = <p className="invitation-card-hint">{t(`reader.invitation.noAction.${noActionKey(invitation)}`)}</p>
  }

  return (
    <section className="invitation-card" aria-label={badge}>
      <div className="invitation-card-head">
        <CalendarIcon size={16} />
        <span className="invitation-card-title">{invitation.summary || t('views.noTitle', { ns: 'calendar' })}</span>
        <span className={`invitation-card-badge is-${invitation.method === 'Cancel' ? 'cancel' : invitation.inCalendar === 'Outdated' ? 'update' : 'request'}`}>{badge}</span>
      </div>
      {context && <p className="invitation-card-context">{context}</p>}
      <dl className="invitation-card-rows">
        <dt>{t('reader.invitation.when')}</dt>
        <dd>{whenOf(invitation, tz, i18n.language, region, cycle, t)}{invitation.repeats && <span className="invitation-card-repeats"> · {t('reader.invitation.repeats')}</span>}</dd>
        {invitation.location && <><dt>{t('reader.invitation.where')}</dt><dd>{invitation.location}</dd></>}
        {invitation.organizer && <><dt>{t('reader.invitation.organizer')}</dt><dd>{invitation.organizer.name || invitation.organizer.email}</dd></>}
        {invitation.attendees.length > 0 && <><dt>{t('reader.invitation.attendees')}</dt><dd>{invitation.attendees.map(a => a.name || a.email).join(', ')}</dd></>}
      </dl>
      {foot}
      {outcome && !outcome.replySent && outcome.replyError !== undefined && lastAnswer && (
        <p className="invitation-card-error">
          {t(lastAnswer === 'Declined' ? 'reader.invitation.replyFailed' : 'reader.invitation.addedReplyFailed')}
          <button type="button" className="btn" disabled={busy} onClick={() => answer(lastAnswer)}>{t('reader.invitation.resend')}</button>
        </p>
      )}
      {error && (
        <p className="invitation-card-error">
          {error.message}
          {error.gone && <button type="button" className="btn" onClick={() => window.location.reload()}>{t('reader.invitation.reload')}</button>}
        </p>
      )}
    </section>
  )
}
```

Vérifier : `icons/CalendarIcon` existe (sinon prendre l'icône que la barre latérale de l'agenda
emploie) ; `ApiError` expose `status` (sinon le champ qu'il expose) ; `views.noTitle` existe
dans `calendar.json`. Le « Recharger » : préférer `queryClient.invalidateQueries` sur la clé du
message à `window.location.reload()` si le lecteur réagit bien à un refetch — c'est le cas
(`useMessage`), donc faire cela et retirer `reload()`.

- [ ] **Step 6g : le style.** Dans `mail.css`, après `.reader-blocked-images .btn` :

```css
/* The invitation card (spec 5e, décision 6): the event-preview card on the reader's sunken band. */
.invitation-card {
  margin: 12px 22px 0;
  padding: 12px 14px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface-sunken);
  display: flex; flex-direction: column; gap: 8px;
  font-size: 13px;
}
.invitation-card-head { display: flex; align-items: center; gap: 8px; }
.invitation-card-title { font-weight: 600; font-size: 14px; flex: 1; min-width: 0; overflow-wrap: anywhere; }
.invitation-card-badge {
  flex: none; padding: 1px 8px; border-radius: 999px; font-size: 11px; font-weight: 600;
  background: color-mix(in oklab, var(--action-primary) 15%, var(--surface)); color: var(--action-primary);
}
.invitation-card-badge.is-cancel { background: color-mix(in oklab, var(--danger) 15%, var(--surface)); color: var(--danger); }
.invitation-card-badge.is-update { background: color-mix(in oklab, var(--warning, #b7791f) 18%, var(--surface)); color: var(--warning, #b7791f); }
.invitation-card-context, .invitation-card-hint { margin: 0; color: var(--text-muted); }
.invitation-card-rows { display: grid; grid-template-columns: max-content 1fr; gap: 4px 14px; margin: 0; }
.invitation-card-rows dt { color: var(--text-muted); }
.invitation-card-rows dd { margin: 0; min-width: 0; overflow-wrap: anywhere; }
.invitation-card-repeats { color: var(--text-muted); }
.invitation-card-actions { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
.invitation-card-actions.is-answered { justify-content: space-between; }
.invitation-card-answer { font-weight: 600; }
.invitation-card-others { display: flex; gap: 6px; }
.invitation-card-others .btn { font-size: 12px; padding: 2px 8px; background: transparent; }
.invitation-card-calendar { display: inline-flex; align-items: center; gap: 6px; margin-left: auto; color: var(--text-muted); }
.invitation-card .btn[aria-pressed="true"] { outline: 2px solid var(--action-primary); outline-offset: 1px; }
.invitation-card-error { margin: 0; display: flex; flex-wrap: wrap; align-items: center; gap: 8px; color: var(--danger); }
@media (max-width: 639px) { .invitation-card { margin: 10px 12px 0; } }
```

Relever les vrais noms de tokens dans `styles/tokens.css` (`--warning`, `--danger`,
`--action-primary`, `--surface-sunken`, `--radius-sm`) et remplacer ceux qui n'existent pas ;
ne pas inventer de couleur littérale hors d'un repli.

- [ ] **Step 6h : le lecteur.** Dans `MessageReader.tsx` :
  - importer `InvitationCard` et `isCalendarType` (ajouter à `mediaType.ts` :
    `export const isCalendarType = (type: string, name = '') => /^(text\/calendar|application\/ics)$/i.test(type) || /\.ics$/i.test(name)`) ;
  - après `displayedParts`, la liste des pièces jointes exclut les parties calendrier quand
    l'encart est affiché :

```ts
const cardShown = !!data.invitation && !data.invitation.unreadable
const attachments = data.attachments.filter(
  attachment => !attachment.isInline && !displayedParts.has(attachment.part)
    && !(cardShown && isCalendarType(attachment.contentType, attachment.fileName)))
```

  - entre le bloc d'en-tête et le corps (`{data.htmlBody ? (` … juste avant), l'encart :

```tsx
{data.invitation && (
  <InvitationCard
    key={`${folderPath}:${uid}`}
    invitation={data.invitation}
    folderPath={folderPath!}
    uid={uid!}
    onTrashed={() => { leave([uid!], () => {}); onDeparted?.(uid!) }}
  />
)}
```

  `leave` est l'utilitaire du lecteur pour un départ animé (`depart ?? …`) — vérifier son nom
  réel dans le fichier (il est employé par `moveTo`) ; la clé force un encart neuf par message.
  - dans `MessageReader.test.tsx` : un test « la partie calendrier ne figure pas dans les pièces
    jointes quand l'encart est affiché, et y reste quand il est illisible », et un test
    « `trashed: true` fait partir le message (`onDeparted` appelé), `trashed: false` non ».

- [ ] **Step 6i : la sonde.** `probes/invitation-card.html` sur le modèle des autres sondes :
  la feuille `mail.css` liée, un `.reader` factice avec l'encart dans ses sept états, FR et EN,
  et l'attendu écrit en tête : la carte ne déborde jamais du lecteur à 400 px de large (aucun
  défilement horizontal), les quatre lignes libellées alignées sur une même colonne de libellés,
  les trois boutons sur une ligne à partir de 480 px. Mesurer dans Chrome (largeurs 400 et 900),
  noter les chiffres dans l'en-tête de la sonde.

- [ ] **Step 6j : vert, commit.** `npm run lint && npm run typecheck && npm test` ;

```bash
git commit -F - <<'EOF'
feat(webmail): l'encart d'invitation dans le lecteur

Sept états depuis le bloc invitation, réponse par l'API, parties calendrier hors des pièces jointes (5e1, décisions 3, 4 et 6).
EOF
```

---

### Task 7 : les participants dans l'agenda, la documentation, la clôture

**Files:**
- Modify: `src/frontend/src/modules/calendar/EventPreview.tsx:88-130`, `EventPreview.test.tsx`
- Modify: `src/frontend/src/modules/calendar/EventEditor.tsx:309-324`, `EventEditor.test.tsx:218`
- Modify: `src/frontend/src/locales/{en,fr}/calendar.json` (`preview.organizedBy`)
- Modify: `src/frontend/src/styles/calendar.css` (`.editor-partstat` retiré)
- Modify: `docs/superpowers/specs/2026-09-12-webmail-calendar-5e-invitations-design.md` (« Où en est le projet » : 5e1 livrée)

**Interfaces:**
- Consumes: `EventDetail.attendees` (`AttendeeProjection`, existant).

- [ ] **Step 7a : l'aperçu, test d'abord.** Dans `EventPreview.test.tsx`, sur le modèle des tests
  existants (le hook `useEvent` y est nourri par `mocks.getEvent`) :

```ts
it('a received event says who organises it and names the attendees, without their state', async () => {
  mocks.getEvent.mockResolvedValue({
    ...detail, attendees: [
      { email: 'marc@example.org', name: 'Marc Dupont', isOrganizer: true },
      { email: 'alice@weesky.be', name: 'Alice', partStat: 'ACCEPTED', isOrganizer: false },
      { email: 'jean@example.net', partStat: 'NEEDS-ACTION', isOrganizer: false },
    ],
  })
  renderPreview(occurrence)
  expect(await screen.findByText('Organised by Marc Dupont')).toBeInTheDocument()
  expect(screen.getByText('Alice, jean@example.net')).toBeInTheDocument()
  expect(screen.queryByText('ACCEPTED')).toBeNull()
})

it('an event without attendees shows neither line', …)
```

- [ ] **Step 7b : rouge, puis l'aperçu.** Dans `EventPreview.tsx`, le détail est désormais lu
  pour tout aperçu (une requête par ouverture, ce que fait déjà `ContactCard`) :

```ts
const { data: detail } = useEvent(occurrence.eventId)
```

(mettre à jour le commentaire au-dessus : le détail porte la règle **et** les participants), et
sous la ligne du rappel :

```tsx
{organizer && (
  <p className="event-preview-row">
    <UserIcon size={14} />{t('preview.organizedBy', { name: organizer.name || organizer.email })}
  </p>
)}
{guests.length > 0 && (
  <p className="event-preview-row event-preview-attendees">
    <UsersIcon size={14} />{guests.map(a => a.name || a.email).join(', ')}
  </p>
)}
```

avec, au-dessus du `return` :

```ts
// Décision 7: names only. The PARTSTAT a received file carries is the organizer's snapshot at
// send time, almost always empty or stale here — the user's own answer lives in the mail's card.
const master = (detail?.attendees ?? []).filter(a => !a.recurrenceId)
const organizer = master.find(a => a.isOrganizer)
const guests = master.filter(a => !a.isOrganizer)
```

Icônes : réutiliser celles du dépôt (`icons/`) ; s'il n'y a pas d'icône « personne » et
« personnes », prendre `MapPinIcon`-style existant pour l'organisateur (`UserIcon` s'il existe)
et ne pas en créer plus d'une. Locales : `"organizedBy": "Organised by {{name}}"` /
`"organizedBy": "Organisé par {{name}}"` sous `preview`.

- [ ] **Step 7c : l'éditeur.** Dans `EventEditor.tsx`, retirer la ligne
  `{one.partStat && <span className="editor-partstat">{one.partStat}</span>}` ; garder l'indicateur
  d'organisateur et le libellé « Read only until invitations are supported » (il tombe en 5e2).
  Dans `EventEditor.test.tsx`, ajouter à un test existant qui nourrit des `attendees` avec un
  `partStat` : `expect(screen.queryByText('ACCEPTED')).toBeNull()`. Retirer `.editor-partstat`
  de `calendar.css`.

- [ ] **Step 7d : vert, commit.** `npm run lint && npm run typecheck && npm test` ;

```bash
git commit -F - <<'EOF'
feat(agenda): l'aperçu nomme l'organisateur et les invités, sans état

L'indicateur PARTSTAT quitte l'éditeur (5e1, décision 7).
EOF
```

- [ ] **Step 7e : clôture.** Dans la spec 5e, tableau « Où en est le projet », la ligne 5e devient
  « 5e1 livrée ; 5e2 à planifier ». Rejouer le harnais `caldavtester` (`tools/caldavtester/`,
  README de 5d) contre dev après déploiement : aucune réponse CalDAV n'a changé, le chiffre doit
  être celui de la clôture de 5d. Recette en session : un vrai mail d'invitation Google vers
  le compte de test (le double `text/calendar` + `invite.ics`), accepter, vérifier l'événement
  sur un iPhone synchronisé et le `REPLY` reçu côté Google.

```bash
git commit -F - <<'EOF'
docs(agenda): 5e1 livrée
EOF
```

---

## Auto-revue du plan

**Couverture de la spec (décisions 1 à 7).**

| Décision | Tâches |
|---|---|
| 1 — le bloc, la partie dans le `BODYSTRUCTURE`, une seule lecture, les garde-fous, `unreadable` | 2 (parser, guards), 3 (picker, téléchargement, bloc) |
| 1 bis — `occurrenceOnly` affiché, jamais appliqué, `400` | 2 (parser), 3 (pas de recherche), 5 (`400`), 6 (état 6) |
| 2 — `addressedTo`, la liste du compte, `UserAddresses` partagé avec le principal | 1 (service), 3 (résolution), 5 (`From` du `REPLY`) |
| 3 — `(user_id, uid)`, l'agenda par défaut, `SEQUENCE` lue dans `ics_raw`, les cinq valeurs, `CANCEL` périmé = `newer`, état 4 à trois boutons | 1 (index, `FindByUidAsync`), 3 (présence), 6 (états) |
| 4 — relecture IMAP, le fichier de base selon l'état, nom DAV conservé, `calendarId` ignoré sur une réécriture, cause `webmail`, corbeille sur refus seul, `409` | 1 (cause), 2 (rewriter), 5 (responder), 6 (`onTrashed`) |
| 5 — le `REPLY`, le pipeline d'envoi du compte, `replySent: false` en `200`, « Renvoyer » idempotent, phrase par réponse | 4 (composeur, `SendBuiltAsync`), 5, 6 |
| 6 — l'encart, la date dans le fuseau du navigateur, les parties calendrier hors des pièces jointes | 6 |
| 7 — organisateur et noms dans l'aperçu, indicateur retiré de l'éditeur | 7 |
| Surface HTTP — `Detail` + `Respond`, codes | 3, 5 |
| Schéma — l'index, la doc des tables | 1 |
| Tests serveur listés | 2 (fixtures des quatre clients, `CANCEL`, `REPLY`, sans `METHOD`, garde-fous), 3 (deux parties, partie trop grosse, `addressedTo` ×4, `inCalendar` ×5, deux agendas, `occurrenceOnly`, `filePartStat`/`savedPartStat`), 5 (le répondeur, chaque ligne) |
| Tests frontend listés | 6 (sept états, phrases de l'état 6, pièces jointes, clic, `trashed`, état 2, « Renvoyer », fuseau, sélecteur, parité), sonde |
| `caldavtester` rejoué | 7e |

**Écarts assumés par rapport à la lettre de la spec** (l'esprit est tenu) :
- « `MailMessageMapper` appelle le lecteur » → c'est le contrôleur mail qui l'appelle, parce que
  le mapper et `ImapMessageCommands` n'ont pas accès à la base ; le mapper choisit la partie.
- « une partie dans le `multipart/alternative` sans nom de fichier (la forme de Google) » : couvert
  par le test du picker (type sans nom) ; aucun test d'intégration IMAP n'existe dans le dépôt
  pour `GetMessageAsync`, la recette réelle (7e) le couvre.
- Le nom DAV d'une création est `{Guid}.ics` (ce que 5a fait), non « dérivé du `UID` ».
- La langue et le fuseau du `REPLY` viennent de la requête (`language`, `timeZone`) : le serveur
  n'a pas de localisation propre.
- `onTrashed` : le plan disait « le lecteur passe au suivant » sans dire
  « et la ligne quitte la liste comme sur un déplacement » ; la revue finale l'a
  rattrapé (`useRespondInvitation` fait la même chirurgie de cache que le déplacement).
  À ne pas répéter en 5e2 pour l'application d'un `REPLY`.

**Cohérence des types.** `ParsedInvitation`, `InvitationContext`, `MailInvitation`,
`StoredEventRef`, `RespondInvitationRequest`, `InvitationResponse`, `ResponderFailure` sont
définis une fois et employés sous le même nom en 2, 3, 5 ; `PutAsync(..., cause:)` en 1 et 5 ;
`SendBuiltAsync` en 4 et 5 ; `IRoleFolderLocator.FindAsync(user, connection, role)` en 4 et 5 ;
les unions TypeScript de 6 reprennent la casse des enums C#.

**Placeholders.** Les cas de test de 6e écrits en une ligne (`…`) sont nommés et leur attendu
est dit ; le sous-agent les écrit en entier sur le modèle des trois premiers. Aucun « TODO ».

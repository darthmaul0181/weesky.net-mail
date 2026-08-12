# Découplage webmail / provider weesky.net — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rendre le webmail déployable sur toute stack IMAP/SMTP standard, la stack weesky.net devenant un provider activé par `"Platform": "weesky"` dans appsettings.

**Architecture:** Trois standardisations protocolaires d'abord (login IMAP, quota IMAP, Sieve en credentials utilisateur), puis des ports fins (`IAliasDirectory`, `IProfileReader`, `IAccountInfoProvider`) implémentés côté weesky, puis la scission en class library `snoopy.providers.weesky` référençant le cœur (jamais l'inverse), un endpoint `GET /api/Capabilities`, et le gating frontend. Spec : `docs/superpowers/specs/2026-08-12-webmail-decoupling-design.md`.

**Tech Stack:** ASP.NET Core .NET 10, MailKit, EF Core/Pomelo, xUnit + Moq ; React/TypeScript, Vitest.

## Global Constraints

- Les mots de passe de la base `dovecot` se stockent **en clair** (triggers MariaDB) — jamais de hash côté service.
- `dotnet test` (jamais `--no-build` quand des fichiers de test ont été ajoutés), lancé depuis `src/snoopy.microservice`.
- Frontend : `npm test` (vitest), `npm run typecheck`, depuis `src/frontend`.
- Style C# : file-scoped namespaces, primary constructors, records pour les DTO, `sealed`, `Result<T>` de CSharpFunctionalExtensions, ILogger structuré, cancellation tokens partout.
- `ApiDocumentation.xml` : artefact versionné que `dotnet test` régénère avec ~855 lignes parasites — le révérter avant chaque commit (`git checkout -- src/snoopy.microservice/ApiDocumentation.xml` si modifié sans rapport).
- Pas d'assertion dépendante de l'hôte (fins de ligne, valeurs observées non spécifiées) — dev Windows, CI Linux.
- Messages de commit : concis, deux lignes max, jamais de '@' en début ou fin.
- Frontend : les flags API absents se lisent `undefined` (WhenWritingNull) — types optionnels, fixtures qui omettent au lieu de `null`.
- Chaque endpoint modifié garde le modèle d'erreurs 401/404/409/502 existant (`ApiBaseController.ConnectedAccountError`, constantes partagées).

---

### Task 1 : Login IMAP (les deux modes)

**Files:**
- Modify: `src/snoopy.microservice/Authentication/Services/UserAuthenticator.cs`
- Modify: `src/snoopy.microservice/Repositories/IUsersRepository.cs` + `UsersRepository.cs` (suppression de `VerifyCredentialsAsync` et de `CredentialCheck`/`CredentialResult` si plus référencés)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Authentication/UserAuthenticatorTests.cs` (réécriture)

**Interfaces:**
- Consumes: `IImapConnectionFactory.OpenAsync(MailAccountConnection, CancellationToken)` → `Result<IImapSession>` ; `MailConnectionBuilder.Home(MailOptions, string accountId, string username, MailCredential)` (le constructeur de connexion primaire qu'`AccountConnectionResolver` utilise déjà) ; `IWebmailUserStore.RegisterLoginAsync` ; `ITokenManager.Generate(User)`.
- Produces: `IUserAuthenticator.AuthenticateAsync(email, password, ct)` — signature inchangée, comportement : LOGIN IMAP au lieu de la vérification DB. Les tâches suivantes supposent que plus rien n'appelle `VerifyCredentialsAsync`.

- [ ] **Step 1 : Réécrire les tests de `UserAuthenticator`** — mock `IImapConnectionFactory` au lieu d'`IUsersRepository` :

```csharp
[Fact]
public async Task Authenticate_ImapLoginSucceeds_GeneratesToken()
{
    // _factory.Setup(f => f.OpenAsync(It.Is<MailAccountConnection>(c =>
    //     c.AccountId == MailAccountConnection.Primary && c.Username == "alice@weesky.be"),
    //     It.IsAny<CancellationToken>()))
    //   .ReturnsAsync(Result.Success(_session.Object));  — la session est disposée par l'authenticator
    var result = await _sut.AuthenticateAsync("alice@weesky.be", "pw", CancellationToken.None);
    Assert.True(result.IsSuccess);
    _session.Verify(s => s.DisposeAsync(), Times.Once);
}

[Fact]
public async Task Authenticate_ImapRefuses_OpaqueFailure()
{
    // OpenAsync → Result.Failure<IImapSession>("AUTHENTICATIONFAILED")
    var result = await _sut.AuthenticateAsync("alice@weesky.be", "wrong", CancellationToken.None);
    Assert.True(result.IsFailure);
    Assert.Equal("Authentication failed", result.Error); // jamais le détail IMAP
}

[Fact]
public async Task Authenticate_ServerUnreachable_SameOpaqueFailure() { /* même assertion — indistinguable */ }

[Fact]
public async Task Authenticate_Failure_NeverTouchesWebmailStore() { /* RegisterLoginAsync jamais appelé sur échec */ }
```

- [ ] **Step 2 : `dotnet test --filter UserAuthenticator` — vérifier l'échec** (les nouveaux tests ne compilent pas encore contre l'implémentation).

- [ ] **Step 3 : Implémenter.** `UserAuthenticator` prend `IImapConnectionFactory factory, IOptionsMonitor<MailOptions> mail, ITokenManager tokenManager, IWebmailUserStore webmailUsers, ILogger<UserAuthenticator>` :

```csharp
public async Task<Result<AuthToken>> AuthenticateAsync(string email, string password, CancellationToken cancellationToken)
{
    var connection = MailConnectionBuilder.Home(
        mail.CurrentValue, MailAccountConnection.Primary, email, new PasswordCredential(password));
    var opened = await factory.OpenAsync(connection, cancellationToken);
    if (opened.IsFailure)
    {
        // Toute cause — mauvais mot de passe, compte inconnu, serveur injoignable — répond le
        // même message et le détail part au log seul : l'anti-énumération fine vit chez Dovecot.
        logger.LogInformation("Audit: login email={Email} outcome=failure reason=imap_no", email);
        return Result.Failure<AuthToken>("Authentication failed");
    }
    await opened.Value.DisposeAsync();

    logger.LogInformation("Audit: login email={Email} outcome=success", email);
    var account = await webmailUsers.RegisterLoginAsync(email, cancellationToken);
    var user = new User(email) { WebmailUid = account.Id, SecurityStamp = account.SecurityStamp };
    return Result.Success(tokenManager.Generate(user));
}
```

Supprimer `VerifyCredentialsAsync` d'`IUsersRepository`/`UsersRepository`, ainsi que `AuditReason`, et les types `CredentialCheck`/`CredentialResult` s'ils n'ont plus de référence (vérifier par compilation). Adapter/supprimer les tests de `UsersRepository.VerifyCredentialsAsync`. **Attention** : `User(email)` doit produire le même `Email` que la ligne écrite par `RegisterLoginAsync` — reprendre la normalisation que faisait le chemin DB si `RegisterLoginAsync` en attend une (regarder l'appelant actuel : il passe `user.Email` résolu par la DB ; passer l'email tel que saisi trim/lowercase via `IdentityResolver.Canonical`).

- [ ] **Step 4 : `dotnet test` complet — vert.**
- [ ] **Step 5 : Commit** — `feat: authenticate logins against IMAP instead of the dovecot database`

---

### Task 2 : Quota utilisateur via IMAP, suppression du doveadm hors admin

**Files:**
- Modify: `src/snoopy.microservice/Services/IImapSession.cs` (+ `ImapSession.cs`) — ajout `SupportsQuota` + `GetQuotaAsync`
- Modify: `src/snoopy.microservice/Controllers/AccountController.cs` — `GetQuota` via IMAP, **suppression de `GetFolders`** (`GET /api/account/folders`, aucun appelant frontend, doveadm-couplé)
- Modify: `src/snoopy.microservice/Services/IDovecotQuotaClient.cs` + `DovecotQuotaClient.cs` — suppression de `GetMailboxesAsync`
- Test: `snoopy.microservice.Tests/Controllers/AccountControllerTests.cs`, tests d'`ImapSession` existants

**Interfaces:**
- Produces: `IImapSession.SupportsQuota` (`bool`, capability `QUOTA` lue post-auth) ; `IImapSession.GetQuotaAsync(CancellationToken)` → `Task<Result<Quota>>` (échec = serveur qui refuse ; l'absence de capability se teste par `SupportsQuota` avant l'appel). La Task 6 consomme `SupportsQuota`.
- Consumes: `IAccountConnectionResolver.ResolveAsync` + `IImapSessionProvider.GetAsync` (le duo de tout endpoint mail).

- [ ] **Step 1 : Tests contrôleur** — `GetQuota` répond le DTO quand `SupportsQuota`, **204** quand `!SupportsQuota`, 401 sans cookie (résolution échouée), 502 quand la session échoue. Supprimer les tests de `GetFolders`.
- [ ] **Step 2 : Vérifier l'échec** (`dotnet test --filter AccountController`).
- [ ] **Step 3 : Implémenter.** Dans `ImapSession` : `SupportsQuota => client.Capabilities.HasFlag(ImapCapabilities.Quota)` ; `GetQuotaAsync` ouvre `GETQUOTAROOT INBOX` via MailKit (`client.Inbox.GetQuotaAsync`) et mappe vers le modèle `Quota` existant — **les valeurs STORAGE RFC 2087 sont en blocs de 1024 octets : multiplier par 1024** ; limite absente (`null`) → 0 (« pas de limite », convention du modèle). Dans `AccountController.GetQuota` : résoudre la connexion primaire (`TryResolveAsync`-équivalent : ce contrôleur n'hérite pas de `MailControllerBase` — injecter `IAccountConnectionResolver` + `IImapSessionProvider`, suivre le motif d'erreurs de `MailControllerBase.TryResolveAsync` pour 401), ouvrir la session, `SupportsQuota ? Ok(quota) : NoContent()`. Retirer `IDovecotQuotaClient` des dépendances d'`AccountController`, supprimer l'endpoint `GetFolders` et `GetMailboxesAsync`.
- [ ] **Step 4 : `dotnet test` complet — vert.**
- [ ] **Step 5 : Commit** — `feat: read the user quota over IMAP GETQUOTAROOT; drop the dead doveadm folders endpoint`

---

### Task 3 : Règles Sieve en credentials utilisateur

**Files:**
- Modify: `src/snoopy.microservice/Controllers/RulesController.cs:43-53` — la branche primaire
- Modify: `src/snoopy.microservice/Models/SieveOptions.cs` — suppression `MasterUser`/`MasterPassword`
- Modify: `src/snoopy.microservice/Services/ManageSieveClient.cs` — le guard ne porte plus que sur `Host`
- Modify: `src/snoopy.microservice/appsettings.json` + `appsettings.Development.json` — retrait des deux clés
- Test: `RulesControllerTests.cs`, `SieveRepositoryTests.cs` si impactés

**Interfaces:**
- Consumes: `SieveConnection(host, port, authorizeAs, authUser, password)` — la forme « credentials propres » est `(sieve.Host, sieve.Port, string.Empty, account.Username, mailbox.Password)`, déjà utilisée par les deux branches connected.

- [ ] **Step 1 : Adapter les tests** — la branche primaire attend désormais une `SieveConnection` construite avec le mot de passe du cookie (le mock de résolution fournit déjà `PasswordCredential`) ; supprimer les tests du master manquant (`SieveErrors.NotConfigured` sur master vide).
- [ ] **Step 2 : Vérifier l'échec.**
- [ ] **Step 3 : Implémenter.** La branche `account.AccountId == MailAccountConnection.Primary` fusionne avec la branche `IsHomeServer` :

```csharp
// Primaire et mailbox partagée sur notre serveur : mêmes credentials propres, même endpoint maison.
if (account.AccountId == MailAccountConnection.Primary || account.IsHomeServer)
    return AccountResolution<SieveConnection>.Success(new SieveConnection(
        sieve.Host, sieve.Port, string.Empty, account.Username, mailbox.Password));
```

Supprimer `MasterUser`/`MasterPassword` de `SieveOptions`, le guard correspondant dans `ManageSieveClient` (garder celui sur `Host`), les deux clés d'appsettings, et `SieveErrors.NotConfigured` s'il n'a plus d'usage.
- [ ] **Step 4 : `dotnet test` complet — vert.**
- [ ] **Step 5 : Commit** — `feat: edit sieve rules with the user's own credentials; retire the master user`

---

### Task 4 : Ports (annuaire d'aliases, profil, account info) et identités libres

Le paquet qui fait exister la couture — encore dans un seul projet, DI inchangé (implémentations weesky câblées par défaut). La Task 5 ne fera que déplacer des fichiers et brancher l'aiguillage.

**Files:**
- Create: `src/snoopy.microservice/Platform/IAliasDirectory.cs`, `Platform/IProfileReader.cs`, `Platform/IAccountInfoProvider.cs` (le dossier `Platform/` = les ports du cœur)
- Create: `src/snoopy.microservice/Platform/Generic/FreeIdentityDirectory.cs`, `Platform/Generic/NullProfileReader.cs`, `Platform/Generic/ClaimsAccountInfoProvider.cs`
- Create: `src/snoopy.microservice/Platform/Weesky/WeeskyAliasDirectory.cs`, `Platform/Weesky/WeeskyProfileReader.cs`, `Platform/Weesky/WeeskyAccountInfoProvider.cs` (adaptateurs sur `IAliasesRepository`/`IUsersRepository` — déplacés en Task 5)
- Modify: `Controllers/IdentitiesController.cs`, `Services/OutgoingMessageFactory.cs`, `Controllers/AccountController.cs` (GetAccountInfo), `Configuration/ApplicationServicesConfiguration.cs` (enregistrement weesky par défaut)
- Test: `IdentitiesControllerTests.cs`, `OutgoingMessageFactoryTests.cs`, nouveaux tests des trois implémentations génériques

**Interfaces (Produces — les signatures que les Tasks 5 et 6 consomment):**

```csharp
/// <summary>Ce que la plateforme sait des adresses d'un compte au-delà de la primaire.</summary>
public interface IAliasDirectory
{
    /// <summary>Faux quand la plateforme ne peut pas vérifier la propriété : identités libres.</summary>
    bool EnforcesOwnership { get; }
    /// <summary>Les aliases live du compte (vide quand EnforcesOwnership est faux).</summary>
    Task<IReadOnlyList<string>> GetAddressesAsync(User user, CancellationToken cancellationToken);
}

public interface IProfileReader
{
    /// <summary>Le nom d'affichage du compte, null quand la plateforme n'en tient pas.</summary>
    Task<string?> GetDisplayNameAsync(User user, CancellationToken cancellationToken);
}

public interface IAccountInfoProvider
{
    Task<Result<AccountInfo>> GetAccountInfoAsync(User user, CancellationToken cancellationToken);
}
```

Générique : `FreeIdentityDirectory` (`EnforcesOwnership => false`, liste vide) ; `NullProfileReader` (null) ; `ClaimsAccountInfoProvider` (synthèse depuis le JWT : `UserId = 0`, `UserName`/`Mailbox` découpés de l'email, `FullName = null`, `Domains = []`, `IsAdmin = false`).
Weesky : adaptateurs directs (`aliases.GetAliasesAsync(...).ToAddresses()`, `users.FindByEmailAsync(...)?.FullName`, `users.GetAccountInfoAsync(...)`).

- [ ] **Step 1 : Tests des consommateurs re-câblés.**
  - `IdentitiesController` : injecté avec `IAliasDirectory`/`IProfileReader` (au lieu d'`IAliasesRepository`/`IUsersRepository`). Mode strict (mock `EnforcesOwnership=true`) : comportement actuel intact. Mode libre (`EnforcesOwnership=false`) : `List` répond `IdentityResolver.ResolveConnected(stored, AuthenticatedUser.Email)` (la primaire éditable, jamais stale) ; `Replace` valide par `IdentityResolver.ValidateConnected(entries, AuthenticatedUser.Email)` (toute adresse bien formée, la primaire exigée dans le set et forcée default) et stocke sous `AccountScope.Primary`.
  - `OutgoingMessageFactory` : injecté avec les deux ports. Strict : `ResolvePrimaryFromAsync`/`LabelForAsync` inchangés (aliases live + FullName). Libre : le chemin primaire prend **exactement** `ResolveConnectedFrom`/le label connected (ligne stockée sinon adresse nue) — un alias non déclaré dans les identités est refusé, une identité stockée quelconque passe (le SMTP tranchera).
  - `AccountController.GetAccountInfo` : délègue à `IAccountInfoProvider`.
  - Trois petits tests unitaires des implémentations génériques (dont le découpage email → `UserName`/`Mailbox` de `ClaimsAccountInfoProvider`).
- [ ] **Step 2 : Vérifier l'échec.**
- [ ] **Step 3 : Implémenter.** Points d'attention : dans `OutgoingMessageFactory`, le branchement se fait sur `aliasDirectory.EnforcesOwnership`, pas sur un nouveau paramètre — la signature publique `CreateAsync` ne bouge pas ; `LabelForAsync` strict lit le FullName via `IProfileReader` (l'appel `users.FindByEmailAsync` sort du factory). Dans `IdentitiesController`, `LoadSourcesAsync` appelle les deux ports. Enregistrer les trois implémentations weesky dans le DI (`AddRepositories` ou une nouvelle méthode `AddWeeskyPlatform` — préparer la Task 5).
- [ ] **Step 4 : `dotnet test` complet — vert.**
- [ ] **Step 5 : Commit** — `feat: platform ports for aliases, profile and account info; free identities path`

---

### Task 5 : Projet `snoopy.providers.weesky` et aiguillage `Platform`

**Files:**
- Create: `src/snoopy.providers.weesky/snoopy.providers.weesky.csproj` (class library net10.0, référence `..\snoopy.microservice\snoopy.microservice.csproj`, packages Pomelo/EF Core) — **le cœur ne référence jamais ce projet ; c'est le projet de tests du cœur et l'hôte qui le chargent** (voir Step 3, ApplicationPart)
- Create: `src/snoopy.providers.weesky/WeeskyPlatform.cs` (marqueur d'assembly + méthode d'enregistrement DI `AddWeeskyPlatform(IServiceCollection, IConfiguration)`)
- Move (namespace `weesky.Snoopy.Providers.Weesky.*`, `git mv` puis ajustement des `using`) : `Data/ApplicationDbContext.cs` + entités (`MailUser`, `MailDomain`, `MailAlias`, `MailDomainOwnership`), `Repositories/{UsersRepository, IUsersRepository, AliasesRepository, IAliasesRepository, AdminRepository, IAdminRepository}`, `Controllers/{AdminController, AliasesController}`, `Services/{DovecotQuotaClient, IDovecotQuotaClient}`, `Models/DovecotOptions.cs`, `Authentication/Authorization/AdminRequirementHandler.cs`, `Platform/Weesky/*` (Task 4)
- Create: `src/snoopy.providers.weesky/Controllers/AccountManagementController.cs` — **`ChangeSecret` (`PATCH api/Account/ChangeSecret`) et `ChangeFullName` (`POST api/Account/FullName`) déménagent ici**, corps inchangés (`[Route("api/Account")]` explicite, comme les quatre contrôleurs api/Mail)
- Create: `src/snoopy.providers.weesky.Tests/` (xUnit) — les tests des types déplacés migrent avec eux
- Modify: `Program.cs`, `Configuration/DatabaseConfiguration.cs`, `Configuration/SecurityConfiguration.cs`, `Configuration/ApplicationServicesConfiguration.cs`, `appsettings*.json`, `.github/workflows` si le chemin de build est explicite
- Create: `src/snoopy.microservice/Models/PlatformOptions.cs`
- Test: `snoopy.microservice.Tests/Configuration/PlatformBootTests.cs` (les deux modes bootent), extension de la surface de routes

**Interfaces:**
- Consumes: les ports de la Task 4.
- Produces: `PlatformOptions { string Platform }` (`"weesky"` | `"generic"`, **requis** — démarrage refusé sinon en nommant la clé) ; config cible :

```json
{
  "Platform": "weesky",
  "Weesky": {
    "ConnectionStrings": { "MailUserAccountsDatabase": "..." },
    "Dovecot": { "ApiUrl": "...", "ApiKey": "..." }
  }
}
```

(`ConnectionStrings:MailUserAccountsDatabase` et la section racine `Dovecot` disparaissent ; `WebmailPreferencesDatabase` reste à la racine.)

- [ ] **Step 1 : Créer les deux csproj et déplacer les fichiers** (compilation comme seul test à ce stade). L'hôte garde une `ProjectReference` vers le provider — un binaire unique.
- [ ] **Step 2 : Tests de boot des deux modes** :

```csharp
[Theory]
[InlineData("weesky")]
[InlineData("generic")]
public void Host_boots_and_resolves_every_port(string platform)
{
    // WebApplicationFactory avec config in-memory : Platform, WebmailPreferencesDatabase (InMemory/
    // SQLite in-memory pour le boot), bloc Weesky complet pour le mode weesky.
    // Résoudre IAliasDirectory, IProfileReader, IAccountInfoProvider, IUserAuthenticator →
    // weesky : types Weesky* ; generic : FreeIdentityDirectory/NullProfileReader/ClaimsAccountInfoProvider.
}

[Fact]
public void Weesky_platform_without_weesky_block_refuses_to_start_naming_the_key() { }

[Fact]
public void Unknown_platform_refuses_to_start() { }
```

Et la surface de routes : en mode generic, `api/Admin/users`, `api/aliases`, `api/Account/ChangeSecret`, `api/Account/FullName` répondent **404** ; en weesky elles existent (étendre le motif de `MailRouteSurfaceTests` — énumérer les routes par `IActionDescriptorCollectionProvider` plutôt que par appels HTTP là où c'est plus simple).
- [ ] **Step 3 : Implémenter l'aiguillage.** Dans `Program.cs` :

```csharp
var platform = builder.Configuration["Platform"]
    ?? throw new InvalidOperationException("'Platform' is missing: set \"weesky\" or \"generic\".");
var isWeesky = platform switch
{
    "weesky" => true, "generic" => false,
    _ => throw new InvalidOperationException($"Unknown Platform '{platform}': use \"weesky\" or \"generic\".")
};

var mvc = builder.Services.AddControllers(MvcFormatterConfiguration.ConfigureFormatters).AddJsonOptions(...);
if (isWeesky) mvc.AddApplicationPart(typeof(WeeskyPlatform).Assembly);   // sinon les routes weesky n'existent pas

if (isWeesky) builder.Services.AddWeeskyPlatform(builder.Configuration); // DbContext dovecot (Weesky:ConnectionStrings),
                                                                          // repositories, doveadm (Weesky:Dovecot),
                                                                          // AdminRequirementHandler, ports Weesky*
else builder.Services.AddGenericPlatform();                               // FreeIdentityDirectory, NullProfileReader,
                                                                          // ClaimsAccountInfoProvider
```

Binder aussi `PlatformOptions` dans `AddSnoopyOptions` (`services.AddOptions<PlatformOptions>().Bind(configuration)` — la clé est à la racine), pour que la Task 6 l'injecte par `IOptions<PlatformOptions>` au lieu de relire la config. `AddWeeskyPlatform` reprend la validation fail-fast de `DatabaseConfiguration` (connection string manquante → exception nommant `Weesky:ConnectionStrings:MailUserAccountsDatabase`). `DatabaseConfiguration.AddSnoopyDatabases` ne garde que `PreferencesDbContext`. `DatabaseHealthCheck` : le check cœur pointe sur `PreferencesDbContext` ; `AddWeeskyPlatform` ajoute un check `weesky-database` sur `ApplicationDbContext`. `SecurityConfiguration` n'enregistre plus `AdminRequirementHandler` (parti dans `AddWeeskyPlatform`) ; la policy `Admin` reste déclarée au cœur (sans handler enregistré, toute évaluation échoue — et en générique aucune route ne la porte). Mettre à jour `appsettings.json`/`appsettings.Development.json` au format cible (avec `"Platform": "weesky"`).
- [ ] **Step 4 : `dotnet test` sur les deux projets de tests — vert.** Vérifier aussi `dotnet run` local en `Platform=generic` (le service démarre sans base dovecot).
- [ ] **Step 5 : Commit** — `feat: split the weesky stack into snoopy.providers.weesky behind a Platform switch`

---

### Task 6 : Endpoint `GET /api/Capabilities`

**Files:**
- Create: `src/snoopy.microservice/Controllers/CapabilitiesController.cs`, `Models/CapabilitiesResponse.cs`
- Create: `src/snoopy.microservice/Services/SieveAvailabilityProbe.cs` (+ interface)
- Modify: `Configuration/ApplicationServicesConfiguration.cs` (enregistrements), `snoopy.providers.weesky/WeeskyPlatform.cs`
- Test: `CapabilitiesControllerTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed record CapabilitiesResponse(
    string Platform, bool Admin, bool Aliases, bool PasswordChange,
    bool ProfileEditing, bool StrictIdentities, bool Quota, bool Rules);
```

- Consumes: `PlatformOptions` ; `IAliasDirectory.EnforcesOwnership` (→ `StrictIdentities`/`Aliases`) ; `IAccountInfoProvider` (→ `Admin` = weesky **et** `IsAdmin`) ; `IImapSession.SupportsQuota` (Task 2) ; `ISieveAvailabilityProbe.IsAvailableAsync(host, port, ct)` — un connect TCP + greeting ManageSieve, timeout court, **résultat mémoïsé par (host,port) pour la vie du process** (la config ne bouge qu'au redéploiement).

- [ ] **Step 1 : Tests.** Mode weesky + user admin → tous les flags vrais (quota/rules selon mocks) ; weesky + non-admin → `admin:false` ; générique → `platform:"generic"`, admin/aliases/passwordChange/profileEditing/strictIdentities faux ; `quota` suit `SupportsQuota` ; `rules` suit le probe ; 401 sans cookie (le quota exige la session IMAP — même résolution que les endpoints mail).
- [ ] **Step 2 : Vérifier l'échec.**
- [ ] **Step 3 : Implémenter.** `PasswordChange`/`ProfileEditing`/`Aliases` = `platform == "weesky"` (les trois surfaces vivent dans le provider) ; `StrictIdentities = aliasDirectory.EnforcesOwnership` ; `Quota` : résoudre la connexion primaire, ouvrir la session, lire `SupportsQuota` ; `Rules` : probe sur `Sieve:Host`/`Port` (host vide → false). Une résolution de compte échouée suit le mapping standard (401 `credentials_unavailable`).
- [ ] **Step 4 : `dotnet test` — vert.**
- [ ] **Step 5 : Commit** — `feat: capabilities endpoint advertising what the platform wires and the servers support`

---

### Task 7 : Frontend — chargement des capacités et gating des surfaces

**Files:**
- Modify: `src/frontend/src/api.js` — `getCapabilities: () => request('/api/Capabilities')` ; `getQuota` tolère le 204 (→ `null`)
- Modify: `src/frontend/src/contexts/AuthContext.tsx` — état `capabilities` chargé avec `refreshAccount()` au flip `isLoggedIn`, exposé par le contexte, remis à `null` à la déconnexion
- Modify: `src/frontend/src/modules/settings/SettingsLayout.tsx` — gating des onglets
- Modify: `src/frontend/src/modules/settings/account/AccountPage.tsx` — sections mot de passe / nom masquées, jauge quota absente sur `null`
- Modify: `src/frontend/src/modules/settings/identities/IdentitiesPage.tsx` + `IdentityDialog.tsx` — mode identités libres
- Modify: `src/frontend/src/types/` — type `Capabilities` (tous champs optionnels : un backend plus vieux n'envoie rien)
- Test: tests des composants touchés (fixtures de capacités qui **omettent** les flags plutôt que `null`)

**Interfaces:**
- Consumes: `CapabilitiesResponse` (camelCase sur le fil : `platform`, `admin`, `aliases`, `passwordChange`, `profileEditing`, `strictIdentities`, `quota`, `rules`).
- Règle de lecture pendant le chargement : même motif que `activeAccount?.isPrimary !== false` — **un flag se gate en `!== false`**, jamais en `=== true`, pour que la nav ne clignote pas pendant la fenêtre de chargement et qu'un backend antérieur (flags `undefined`) garde le comportement weesky actuel.

- [ ] **Step 1 : Tests.**
  - `SettingsLayout` : `aliases:false` masque Aliases ; `admin:false` masque Administration même pour un admin ; `rules:false` masque Rules sur le compte primaire (un connected account garde son gating `sieveSupported` actuel) ; fixture sans capacités = nav actuelle intacte.
  - `AccountPage` : `passwordChange:false` masque `ChangePasswordSection` ; `profileEditing:false` masque l'édition du nom ; `getQuota → null` (204) ne rend aucune jauge.
  - `IdentitiesPage` : `strictIdentities:false` → la page traite le compte primaire **comme un connected account** (chemin déjà existant : tuile primaire éditable, dialog en saisie libre au lieu du combobox d'aliases, pas d'appel `useAliases`).
- [ ] **Step 2 : `npm test` — vérifier l'échec.**
- [ ] **Step 3 : Implémenter.** Gating dans `SettingsLayout` (motif existant) :

```tsx
const { isAdmin, activeAccount, capabilities } = useAuth()
const aliasesAvailable = capabilities?.aliases !== false
const adminAvailable = capabilities?.admin !== false
const rulesAvailable = isPrimary ? capabilities?.rules !== false : activeAccount?.sieveSupported !== false
// items : ...(isPrimary && aliasesAvailable ? [aliases] : []), ...(isAdmin && isPrimary && adminAvailable ? [admin] : [])
```

`IdentitiesPage` : la branche connected-account existe déjà (la page est rendue pour les comptes connectés) — le mode libre force cette branche pour le primaire quand `capabilities?.strictIdentities === false`. `api.js` : dans `getQuota`, un `response.status === 204` répond `null` avant le parse JSON.
- [ ] **Step 4 : `npm test` + `npm run typecheck` + `npm run lint` — vert.**
- [ ] **Step 5 : Commit** — `feat: capability-driven UI gating for the generic platform`

---

## Notes de déploiement (hors tâches — à faire au moment du merge)

1. Mettre à jour les appsettings serveur dev puis prod : `"Platform": "weesky"`, bloc `Weesky` (connection string dovecot + doveadm), retrait de `Sieve:MasterUser`/`MasterPassword`. Le push déploie ; le service nomme la clé manquante s'il refuse de démarrer.
2. Vérifier l'hypothèse du spec : un compte désactivé en base est refusé par le passdb Dovecot (tester un LOGIN IMAP sur un compte désactivé avant de merger la Task 1).
3. Nettoyage Dovecot optionnel ensuite : retirer le master user ManageSieve.

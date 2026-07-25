# Webmail — table `users` à clé GUID — Plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Donner à chaque compte une clé de substitution GUID dans `snoopy_webmail`, portée par le JWT, sur laquelle les trois tables de préférences deviennent des FK en cascade — pour que suppression et renommage fonctionnent sans jamais toucher `dovecot`.

**Architecture:** Une table `snoopy_webmail.users(id GUID, email, creation_date, last_login_date)` ajoutée au `PreferencesDbContext` existant. Un `WebmailUserStore` crée/estampille la ligne au login (`RegisterLoginAsync`) et la supprime (`DeleteByEmailAsync`). Le GUID est mis dans une revendication `webmail_uid` du JWT et exposé via `User.WebmailUid`. Les trois entités/stores de préférences passent de `account_id` (string) à `user_id` (Guid), et tous leurs appelants lisent `WebmailUid` au lieu de `CanonicalAccountId(email)`.

**Tech Stack:** ASP.NET Core .NET 10, EF Core/Pomelo (MySQL/MariaDB), CSharpFunctionalExtensions `Result`, xUnit + Moq, EF Core InMemory pour les tests.

**Spec:** `docs/superpowers/specs/2026-07-24-webmail-users-table-design.md` — lire les sections qu'une tâche cite.

## Global Constraints

- UI/doc en **anglais** ; conversation avec l'utilisateur en français. Messages de commit concis, **2 lignes de corps max**, jamais commençant/finissant par `@` (heredoc `git commit -F -` sous l'outil Bash).
- Style backend (`src/snoopy.microservice/CLAUDE.md`) : namespaces file-scoped, un type par fichier, constructeurs primaires pour les nouvelles classes DI, records pour les DTO, `Result<T>` pour l'erreur, logging structuré uniquement, `CancellationToken` sur l'async. `Assert.IsType<BadRequestObjectResult>` (type exact) pour `BadRequest(body)`.
- Commentaires seulement là où le code seul n'explique pas, 3 lignes max. Aucune logique dupliquée. Penser performance **et sécurité** (backend).
- Tests backend : `dotnet test` depuis `src/snoopy.microservice` (jamais `--no-build` si des fichiers sont ajoutés). Sortie propre (0 warning).
- **`dovecot` n'est jamais modifié par cette tranche** : la seule écriture `dovecot` du périmètre est la suppression de compte que l'admin fait déjà. Aucune FK ne référence `dovecot`.
- Le GUID est **généré côté application** (`Guid.NewGuid()`). `creation_date`/`last_login_date` posées côté application (`DateTime.UtcNow`), jamais par `DEFAULT`/`ON UPDATE` SQL.
- La table est créée **manuellement** (pas d'EF migrations) : le fichier DDL de la Tâche 1 est un livrable.
- **Table rase assumée** : le webmail n'est pas en production. On `DROP`/recrée les trois tables ; aucune migration de données.
- Ne jamais committer `.claude/settings.local.json`, le `CLAUDE.md` racine, ni `src/frontend/src/assets/weesky_net.png` (modifications de l'utilisateur dans l'arbre de travail).

## Limites d'outillage à connaître (elles cadrent les tests)

- **EF Core InMemory n'applique pas les FK `ON DELETE CASCADE`** : la cascade est une garantie du schéma MariaDB (le DDL), pas testable en InMemory. Les tests vérifient que `DeleteByEmailAsync` supprime la **ligne `users`** ; la cascade sur les trois tables filles est documentée, déléguée à la base.
- **EF Core InMemory n'applique pas les contraintes d'unicité** : la course concurrente au login (deux `INSERT` du même email) n'est pas reproductible en InMemory. Le code porte le rattrapage (catch sur violation → re-`SELECT`) ; le test InMemory couvre l'idempotence séquentielle (deux `RegisterLoginAsync` du même email → une ligne, même GUID, `last_login_date` avancée).

## File Structure

| Fichier | Responsabilité |
|---|---|
| `Data/Preferences/WebmailUser.cs` | Entité EF de `snoopy_webmail.users` (créer) |
| `Data/Preferences/PreferencesDbContext.cs` | + `DbSet<WebmailUser>` et sa clé ; clés des 3 filles `AccountId`→`UserId` (modifier) |
| `Repositories/IWebmailUserStore.cs` / `WebmailUserStore.cs` | `RegisterLoginAsync(email)`/`DeleteByEmailAsync(email)` (créer) |
| `docs/superpowers/webmail-users-table.md` | DDL manuel prod+dev + FK cascade + prérequis (créer) |
| `Authentication/WebmailClaimTypes.cs` | Constante `Uid = "webmail_uid"` (créer) |
| `Models/User.cs` | + propriété `Guid WebmailUid` (modifier) |
| `Authentication/Services/TokenManager.cs` | émet la revendication `webmail_uid` (modifier) |
| `Authentication/Services/UserAuthenticator.cs` | appelle `RegisterLoginAsync` avant l'émission, pose `WebmailUid` (modifier) |
| `Authentication/Middleware/SlidingSessionMiddleware.cs` | relit et ré-enfile la revendication au renouvellement (modifier) |
| `Authentication/Extensions/ControllerBaseExtensions.cs` | `GetUser()` peuple `WebmailUid` depuis la revendication (modifier) |
| `Data/Preferences/{FolderRoleOverride,UserPreference,SendingIdentity}.cs` | `AccountId`(string)→`UserId`(Guid) (modifier) |
| `Repositories/{FolderRoleStore,UserPreferenceStore,SendingIdentityStore}.cs` + interfaces | `string accountId`→`Guid userId` (modifier) |
| `Controllers/{PreferencesController,IdentitiesController,MailController}.cs`, `Repositories/MailFolderRepository.cs`, `Services/MailSender.cs` | lisent `WebmailUid` au lieu de `CanonicalAccountId(email)` (modifier) |
| `Repositories/AdminRepository.cs` | supprime la ligne `users` best-effort après le `dovecot` delete (modifier) |

## Ordre et découpage

Big-bang, une tranche, pas de compat ascendante. Ordre imposé par le spec : **socle auth d'abord** (Tâches 1-2), **puis les stores** (Tâches 3-5, une par store, indépendantes et testables séparément), **puis la suppression admin** (Tâche 6).

---

### Task 1 : Table `users`, entité, `WebmailUserStore`, DDL

**Files:**
- Create: `src/snoopy.microservice/Data/Preferences/WebmailUser.cs`
- Modify: `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs` (+ `DbSet` et clé de `WebmailUser`)
- Create: `src/snoopy.microservice/Repositories/IWebmailUserStore.cs`
- Create: `src/snoopy.microservice/Repositories/WebmailUserStore.cs`
- Modify: `src/snoopy.microservice/Program.cs` (DI, après `ISendingIdentityStore`)
- Create: `docs/superpowers/webmail-users-table.md`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/WebmailUserStoreTests.cs`

**Interfaces:**
- Produces:
  - `WebmailUser { Guid Id; string Email; DateTime CreationDate; DateTime? LastLoginDate; }` (`[Table("users")]`)
  - `IWebmailUserStore { Task<Guid> RegisterLoginAsync(string email, CancellationToken ct); Task DeleteByEmailAsync(string email, CancellationToken ct); }`
  - `WebmailUserStore(PreferencesDbContext context)` (constructeur primaire, `internal sealed`)

- [ ] **Step 1: Écrire l'entité**

`src/snoopy.microservice/Data/Preferences/WebmailUser.cs` :

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One webmail account, keyed by a surrogate GUID. The three preference tables reference this
/// row (FK, ON DELETE CASCADE); email is the natural key looked up at login and refreshed on
/// rename. Never mirrors a dovecot row structurally — this table lives only in snoopy_webmail.
/// </summary>
[Table("users")]
public sealed class WebmailUser
{
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>Canonical (trimmed, lower-case); the table collates in binary.</summary>
    [Column("email")]
    public string Email { get; set; } = string.Empty;

    [Column("creation_date")]
    public DateTime CreationDate { get; set; }

    [Column("last_login_date")]
    public DateTime? LastLoginDate { get; set; }
}
```

- [ ] **Step 2: Déclarer le DbSet et la clé**

Dans `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs`, ajouter à `OnModelCreating` (ne pas encore toucher les 3 clés existantes — c'est l'objet des Tâches 3-5) :

```csharp
modelBuilder.Entity<WebmailUser>().HasKey(u => u.Id);
modelBuilder.Entity<WebmailUser>().HasIndex(u => u.Email).IsUnique();
```

et le DbSet :

```csharp
public DbSet<WebmailUser> Users { get; set; }
```

- [ ] **Step 3: Écrire les tests du store (RED)**

`src/snoopy.microservice/snoopy.microservice.Tests/Repositories/WebmailUserStoreTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class WebmailUserStoreTests
{
    private static WebmailUserStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName));

    [Fact]
    public async Task RegisterLogin_WhenAbsent_CreatesRowWithGuidAndStamps()
    {
        var store = CreateStore(nameof(RegisterLogin_WhenAbsent_CreatesRowWithGuidAndStamps));

        var id = await store.RegisterLoginAsync("mick@weesky.be", CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
        using var ctx = new PreferencesTestDbContext(nameof(RegisterLogin_WhenAbsent_CreatesRowWithGuidAndStamps));
        var row = ctx.Users.Single();
        Assert.Equal(id, row.Id);
        Assert.Equal("mick@weesky.be", row.Email);
        Assert.NotNull(row.LastLoginDate);
        Assert.Equal(row.CreationDate, row.LastLoginDate);
    }

    [Fact]
    public async Task RegisterLogin_WhenPresent_KeepsGuidAndCreationButAdvancesLastLogin()
    {
        var db = nameof(RegisterLogin_WhenPresent_KeepsGuidAndCreationButAdvancesLastLogin);
        var first = await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);
        DateTime creation, firstLogin;
        using (var ctx = new PreferencesTestDbContext(db))
        {
            var row = ctx.Users.Single();
            creation = row.CreationDate;
            firstLogin = row.LastLoginDate!.Value;
        }

        var second = await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);

        Assert.Equal(first, second);
        using var after = new PreferencesTestDbContext(db);
        var updated = after.Users.Single();
        Assert.Equal(creation, updated.CreationDate);
        Assert.True(updated.LastLoginDate >= firstLogin);
    }

    [Fact]
    public async Task RegisterLogin_CanonicalisesEmail()
    {
        var db = nameof(RegisterLogin_CanonicalisesEmail);
        await CreateStore(db).RegisterLoginAsync("  Mick@WEESKY.be ", CancellationToken.None);

        using var ctx = new PreferencesTestDbContext(db);
        Assert.Equal("mick@weesky.be", ctx.Users.Single().Email);
    }

    [Fact]
    public async Task DeleteByEmail_RemovesTheRow()
    {
        var db = nameof(DeleteByEmail_RemovesTheRow);
        await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);

        await CreateStore(db).DeleteByEmailAsync("mick@weesky.be", CancellationToken.None);

        using var ctx = new PreferencesTestDbContext(db);
        Assert.Empty(ctx.Users);
    }

    [Fact]
    public async Task DeleteByEmail_WhenAbsent_IsANoOp()
    {
        var db = nameof(DeleteByEmail_WhenAbsent_IsANoOp);

        await CreateStore(db).DeleteByEmailAsync("nobody@weesky.be", CancellationToken.None);

        using var ctx = new PreferencesTestDbContext(db);
        Assert.Empty(ctx.Users);
    }
}
```

- [ ] **Step 4: Lancer les tests → échec de compilation**

Run: `dotnet test --filter "FullyQualifiedName~WebmailUserStoreTests"` depuis `src/snoopy.microservice`
Expected: FAIL (compilation — `WebmailUserStore`, `IWebmailUserStore` absents).

- [ ] **Step 5: Écrire l'interface**

`src/snoopy.microservice/Repositories/IWebmailUserStore.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Repositories;

public interface IWebmailUserStore
{
    /// <summary>
    /// Ensures the account's row exists and stamps the login. Called once per login, never per
    /// request. Returns the stable GUID (created if absent). Email is canonicalised.
    /// </summary>
    Task<Guid> RegisterLoginAsync(string email, CancellationToken cancellationToken);

    /// <summary>Removes the account's row if present (0 rows = success). The FK cascade removes preferences.</summary>
    Task DeleteByEmailAsync(string email, CancellationToken cancellationToken);
}
```

- [ ] **Step 6: Écrire le store (GREEN)**

`src/snoopy.microservice/Repositories/WebmailUserStore.cs`. La canonicalisation est locale (`Trim().ToLowerInvariant()`), identique à celle qu'on retire de `FolderRoleStore` — voir la note de la Tâche 5 sur l'absence de duplication (à ce stade `CanonicalAccountId` existe encore, mais ce store ne doit pas en dépendre puisqu'il disparaîtra ; on écrit la forme canonique directement) :

```csharp
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class WebmailUserStore(PreferencesDbContext context) : IWebmailUserStore
{
    public async Task<Guid> RegisterLoginAsync(string email, CancellationToken cancellationToken)
    {
        var canonical = Canonical(email);
        var now = DateTime.UtcNow;

        var existing = await context.Users
            .FirstOrDefaultAsync(u => u.Email == canonical, cancellationToken);
        if (existing is not null)
        {
            existing.LastLoginDate = now;
            await context.SaveChangesAsync(cancellationToken);
            return existing.Id;
        }

        var row = new WebmailUser { Id = Guid.NewGuid(), Email = canonical, CreationDate = now, LastLoginDate = now };
        context.Users.Add(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return row.Id;
        }
        catch (DbUpdateException)
        {
            // A concurrent first login inserted the same email; adopt the winner's row.
            context.Entry(row).State = EntityState.Detached;
            var winner = await context.Users
                .FirstAsync(u => u.Email == canonical, cancellationToken);
            winner.LastLoginDate = now;
            await context.SaveChangesAsync(cancellationToken);
            return winner.Id;
        }
    }

    public async Task DeleteByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var canonical = Canonical(email);
        var existing = await context.Users
            .FirstOrDefaultAsync(u => u.Email == canonical, cancellationToken);
        if (existing is null) return;

        context.Users.Remove(existing);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static string Canonical(string email) => email.Trim().ToLowerInvariant();
}
```

- [ ] **Step 7: Lancer les tests → succès**

Run: `dotnet test --filter "FullyQualifiedName~WebmailUserStoreTests"`
Expected: PASS (5/5).

- [ ] **Step 8: Enregistrer la DI**

Dans `src/snoopy.microservice/Program.cs`, juste après `builder.Services.AddScoped<ISendingIdentityStore, SendingIdentityStore>();` :

```csharp
builder.Services.AddScoped<IWebmailUserStore, WebmailUserStore>();
```

- [ ] **Step 9: Écrire le DDL doc**

`docs/superpowers/webmail-users-table.md` — DDL idempotent prod (`snoopy_webmail`) et dev (`snoopy_webmail_dev`), à rejouer d'un bloc. Reprendre verbatim le bloc SQL de la spec (§ « Schéma (DDL neuf) ») : `DROP` des trois filles, `CREATE users`, puis recréation des trois filles avec `user_id CHAR(36)` + FK `ON DELETE CASCADE`. Y ajouter en tête :

```markdown
# Table `users` et refonte des clés — DDL manuel

À rejouer d'un bloc sur `snoopy_webmail` **et** `snoopy_webmail_dev`. Table rase assumée
(webmail hors production). Ordre imposé par InnoDB : `DROP` des tables filles d'abord, puis
`CREATE users`, puis recréation des filles avec la FK.

Aucun `GRANT` à rejouer : les utilisateurs `snoopy_webmail`/`snoopy_webmail_dev` ont déjà
`SELECT, INSERT, UPDATE, DELETE` sur toute la base.
```

et en pied, la section mode opératoire :

```markdown
## Mode opératoire (renommage / suppression)

- **Renommage** (geste d'exploitation, il déplace aussi le maildir) : dans le même geste,
  `UPDATE snoopy_webmail.users SET email='<nouveau canonique>' WHERE email='<ancien canonique>';`
  Le GUID ne bouge pas → identités, rôles, préférences suivent. **Oublier cet UPDATE** laisse
  l'ancienne ligne orpheline et recrée une ligne vide à la reconnexion : effet accepté, documenté.
- **Suppression via l'admin** : automatique (`dovecot` d'abord, puis la ligne `users` en
  best-effort ; la cascade FK emporte les trois tables filles).
- **Suppression directe en base** (hors admin) :
  `DELETE FROM snoopy_webmail.users WHERE email='<canonique>';` — sinon le webmail recrée à la
  volée et laisse un orphelin.
```

- [ ] **Step 10: Suite complète + commit**

Run: `dotnet test` depuis `src/snoopy.microservice` — Expected: PASS, 0 warning.

```bash
git add src/snoopy.microservice/Data/Preferences/WebmailUser.cs \
        src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs \
        src/snoopy.microservice/Repositories/IWebmailUserStore.cs \
        src/snoopy.microservice/Repositories/WebmailUserStore.cs \
        src/snoopy.microservice/Program.cs \
        docs/superpowers/webmail-users-table.md \
        src/snoopy.microservice/snoopy.microservice.Tests/Repositories/WebmailUserStoreTests.cs
git commit -F - <<'EOF'
Webmail users: table, store and DDL

New snoopy_webmail.users (GUID key) with RegisterLoginAsync/DeleteByEmailAsync; the three preference tables will FK-cascade onto it.
EOF
```

---

### Task 2 : Revendication `webmail_uid` dans le JWT

**Files:**
- Create: `src/snoopy.microservice/Authentication/WebmailClaimTypes.cs`
- Modify: `src/snoopy.microservice/Models/User.cs` (+ `Guid WebmailUid`)
- Modify: `src/snoopy.microservice/Authentication/Services/TokenManager.cs`
- Modify: `src/snoopy.microservice/Authentication/Services/UserAuthenticator.cs`
- Modify: `src/snoopy.microservice/Authentication/Middleware/SlidingSessionMiddleware.cs`
- Modify: `src/snoopy.microservice/Authentication/Extensions/ControllerBaseExtensions.cs`
- Modify: `src/snoopy.microservice/snoopy.microservice.Tests/Infrastructure/ControllerTestHelpers.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Authentication/UserAuthenticatorTests.cs` (ou fichier existant équivalent), `TokenManagerTests.cs`

**Interfaces:**
- Consumes: `IWebmailUserStore.RegisterLoginAsync` (Tâche 1).
- Produces:
  - `WebmailClaimTypes.Uid = "webmail_uid"`
  - `Models.User.WebmailUid { get; set; }` (Guid)
  - `GetUser()` peuple `WebmailUid` depuis la revendication ; `Guid.Empty` si absente/invalide
  - `ControllerTestHelpers.CreateAuthenticatedContext(string username, string domain, Guid? webmailUid = null)`

- [ ] **Step 1: La constante de revendication**

`src/snoopy.microservice/Authentication/WebmailClaimTypes.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Authentication;

/// <summary>Custom JWT claim types. Upn/Dns come from System.Security.Claims; this one is ours.</summary>
public static class WebmailClaimTypes
{
    public const string Uid = "webmail_uid";
}
```

- [ ] **Step 2: Propriété sur `User`**

Dans `src/snoopy.microservice/Models/User.cs`, ajouter (le reste inchangé) :

```csharp
/// <summary>The snoopy_webmail surrogate key. Stamped into the JWT at login, read every request.</summary>
public Guid WebmailUid { get; set; }
```

- [ ] **Step 3: Test — le jeton porte la revendication (RED)**

Dans le fichier de test de `TokenManager` (le créer si absent : `src/snoopy.microservice/snoopy.microservice.Tests/Authentication/TokenManagerTests.cs`), un test qui décode le jeton et vérifie la revendication. S'inspirer d'un test d'auth existant pour le montage des `IOptions<TokenConstants>` :

```csharp
[Fact]
public void Generate_StampsTheWebmailUidClaim()
{
    var uid = Guid.NewGuid();
    var user = new User("mick@weesky.be") { WebmailUid = uid };
    var token = CreateManager().Generate(user);

    var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token.Token);
    Assert.Equal(uid.ToString(), jwt.Claims.First(c => c.Type == WebmailClaimTypes.Uid).Value);
}
```

(`CreateManager()` monte `new TokenManager(Options.Create(new TokenConstants { ... }))` avec des constantes de test — reprendre le montage d'un test d'auth existant du projet.)

- [ ] **Step 4: Lancer → échec**

Run: `dotnet test --filter "FullyQualifiedName~TokenManagerTests.Generate_StampsTheWebmailUidClaim"`
Expected: FAIL (la revendication n'est pas émise).

- [ ] **Step 5: Émettre la revendication**

Dans `src/snoopy.microservice/Authentication/Services/TokenManager.cs`, méthode `Generate`, ajouter la revendication à la chaîne fluide :

```csharp
JwtSecurityToken token = tokenBuilder.AddClaim(ClaimTypes.Upn, user.Name)
    .AddClaim(ClaimTypes.Dns, user.Domain)
    .AddClaim(WebmailClaimTypes.Uid, user.WebmailUid.ToString())
    .AddIssuer(TokenConstants.Value.Issuer)
    .AddAudience(TokenConstants.Value.Audience)
    .AddExpiry(TokenConstants.Value.ExpiryInMinutes)
    .AddKey(TokenConstants.Value.Key)
    .Build();
```

Ajouter `using weesky.Snoopy.Microservice.Authentication;` si nécessaire.

- [ ] **Step 6: Lancer → succès**

Run: `dotnet test --filter "FullyQualifiedName~TokenManagerTests.Generate_StampsTheWebmailUidClaim"`
Expected: PASS.

- [ ] **Step 7: Test — le login enregistre et estampille (RED)**

Dans le fichier de test de `UserAuthenticator` (le créer si absent), avec un `Mock<IWebmailUserStore>` :

```csharp
[Fact]
public async Task Authenticate_StampsTheGuidFromRegisterLogin()
{
    var uid = Guid.NewGuid();
    _webmailUsers.Setup(s => s.RegisterLoginAsync("mick@weesky.be", It.IsAny<CancellationToken>()))
        .ReturnsAsync(uid);
    _users.Setup(r => r.FindByEmailAsync("mick@weesky.be")).ReturnsAsync(new User("mick@weesky.be"));
    _users.Setup(r => r.IsValidPasswordAsync(It.IsAny<User>(), "pw")).ReturnsAsync(true);

    var result = await CreateAuthenticator().AuthenticateAsync("mick@weesky.be", "pw");

    Assert.True(result.IsSuccess);
    var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Value.Token);
    Assert.Equal(uid.ToString(), jwt.Claims.First(c => c.Type == WebmailClaimTypes.Uid).Value);
    _webmailUsers.Verify(s => s.RegisterLoginAsync("mick@weesky.be", It.IsAny<CancellationToken>()), Times.Once);
}
```

(`_webmailUsers`, `_users` sont des `Mock<>` ; `CreateAuthenticator()` monte `new UserAuthenticator(_users.Object, _tokenManager, _webmailUsers.Object, logger)` — voir Step 9 pour l'ordre du ctor. Utiliser un vrai `TokenManager` monté sur des constantes de test plutôt qu'un mock, pour que le jeton soit réellement décodable.)

- [ ] **Step 8: Lancer → échec de compilation**

Run: `dotnet test --filter "FullyQualifiedName~UserAuthenticatorTests"`
Expected: FAIL (le ctor de `UserAuthenticator` n'a pas encore le store).

- [ ] **Step 9: Câbler `UserAuthenticator`**

Dans `src/snoopy.microservice/Authentication/Services/UserAuthenticator.cs`, ajouter `IWebmailUserStore` au constructeur (ordre : `usersRepository, tokenManager, webmailUsers, logger`) et, juste avant `return Result.Success(_tokenManager.Generate(user));` (après le log `outcome=success`) :

```csharp
user.WebmailUid = await _webmailUsers.RegisterLoginAsync(user.Email, CancellationToken.None);
return Result.Success(_tokenManager.Generate(user));
```

`AuthenticateAsync` n'a pas de `CancellationToken` dans sa signature actuelle : passer `CancellationToken.None` (cohérent avec le reste du flux de login qui n'en propage pas). Ajouter `using weesky.Snoopy.Microservice.Repositories;` si nécessaire.

- [ ] **Step 10: Lancer → succès**

Run: `dotnet test --filter "FullyQualifiedName~UserAuthenticatorTests"` puis `dotnet test --filter "FullyQualifiedName~TokenManagerTests"`
Expected: PASS.

- [ ] **Step 11: Ré-enfiler la revendication au renouvellement**

Dans `src/snoopy.microservice/Authentication/Middleware/SlidingSessionMiddleware.cs`, `TryRenew` reconstruit un `User` à partir des seuls `Upn`/`Dns`. Lire aussi la revendication `webmail_uid` et la reporter — **sans** relire la base :

```csharp
var name = context.User.FindFirst(ClaimTypes.Upn)?.Value;
var domain = context.User.FindFirst(ClaimTypes.Dns)?.Value;
if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(domain)) return;

// ... (checks exp/remaining/password inchangés) ...

var renewed = new User($"{name}@{domain}");
if (Guid.TryParse(context.User.FindFirst(WebmailClaimTypes.Uid)?.Value, out var uid))
    renewed.WebmailUid = uid;
var token = tokens.Generate(renewed);
```

Ajouter `using weesky.Snoopy.Microservice.Authentication;`.

- [ ] **Step 12: `GetUser()` expose la revendication**

Dans `src/snoopy.microservice/Authentication/Extensions/ControllerBaseExtensions.cs`, après avoir construit `user` depuis `name`/`domain` :

```csharp
if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(domain))
{
    user = new User($"{name}@{domain}");
    if (Guid.TryParse(claims.FirstOrDefault(c => c.Type == WebmailClaimTypes.Uid)?.Value, out var uid))
        user.WebmailUid = uid;
}
```

Ajouter `using weesky.Snoopy.Microservice.Authentication;`. Note : un jeton émis avant cette tranche n'a pas la revendication → `WebmailUid` reste `Guid.Empty` ; c'est toléré ici, les stores ne le consomment pas encore (Tâches 3-5).

- [ ] **Step 13: Le helper de test stampe la revendication**

Dans `src/snoopy.microservice/snoopy.microservice.Tests/Infrastructure/ControllerTestHelpers.cs`, élargir la signature (paramètre optionnel → les ~10 classes de tests existantes compilent inchangées) :

```csharp
public static ControllerContext CreateAuthenticatedContext(string username, string domain, Guid? webmailUid = null)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.Upn, username),
        new(ClaimTypes.Dns, domain)
    };
    if (webmailUid.HasValue)
        claims.Add(new Claim(WebmailClaimTypes.Uid, webmailUid.Value.ToString()));
    var identity = new ClaimsIdentity(claims, "Test");
    var principal = new ClaimsPrincipal(identity);
    return new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } };
}
```

Ajouter `using weesky.Snoopy.Microservice.Authentication;`.

- [ ] **Step 14: Suite complète + commit**

Run: `dotnet test` depuis `src/snoopy.microservice` — Expected: PASS, 0 warning.

```bash
git add src/snoopy.microservice/Authentication/WebmailClaimTypes.cs \
        src/snoopy.microservice/Models/User.cs \
        src/snoopy.microservice/Authentication/Services/TokenManager.cs \
        src/snoopy.microservice/Authentication/Services/UserAuthenticator.cs \
        src/snoopy.microservice/Authentication/Middleware/SlidingSessionMiddleware.cs \
        src/snoopy.microservice/Authentication/Extensions/ControllerBaseExtensions.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Infrastructure/ControllerTestHelpers.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Authentication/
git commit -F - <<'EOF'
Webmail auth: stamp the account GUID into the JWT

RegisterLoginAsync at login fills User.WebmailUid; TokenManager emits webmail_uid; the sliding session re-threads it and GetUser reads it.
EOF
```

---

### Task 3 : `user_preferences` sur `user_id` (GUID)

**Files:**
- Modify: `src/snoopy.microservice/Data/Preferences/UserPreference.cs`
- Modify: `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs` (clé de `UserPreference`)
- Modify: `src/snoopy.microservice/Repositories/UserPreferenceStore.cs` + `IUserPreferenceStore.cs`
- Modify: `src/snoopy.microservice/Controllers/PreferencesController.cs` (2 sites)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/UserPreferenceStoreTests.cs`, `.../Controllers/PreferencesControllerTests.cs`

**Interfaces:**
- Consumes: `User.WebmailUid` (Tâche 2).
- Produces: `IUserPreferenceStore { Task<...> GetAsync(Guid userId, CancellationToken ct); Task SetAsync(Guid userId, string key, string value, CancellationToken ct); }` ; `UserPreference.UserId` (Guid, `[Column("user_id")]`).

- [ ] **Step 1: Retyper l'entité**

Dans `src/snoopy.microservice/Data/Preferences/UserPreference.cs`, remplacer la propriété `AccountId` :

```csharp
[Column("user_id")]
public Guid UserId { get; set; }
```

- [ ] **Step 2: Clé composite**

Dans `PreferencesDbContext.OnModelCreating`, remplacer la ligne de `UserPreference` :

```csharp
modelBuilder.Entity<UserPreference>().HasKey(p => new { p.UserId, p.PreferenceKey });
```

- [ ] **Step 3: Adapter le test du store (RED)**

Dans `UserPreferenceStoreTests.cs`, remplacer les seeds/assertions keyés sur une string email par un `Guid`. Exemple de forme :

```csharp
private static readonly Guid User = Guid.NewGuid();

[Fact]
public async Task Set_ThenGet_RoundTripsByUserId()
{
    var db = nameof(Set_ThenGet_RoundTripsByUserId);
    await CreateStore(db).SetAsync(User, "mail.pageSize", "50", CancellationToken.None);

    var rows = await CreateStore(db).GetAsync(User, CancellationToken.None);
    Assert.Equal("50", Assert.Single(rows).PreferenceValue);
}

[Fact]
public async Task Get_IsIsolatedBetweenUsers()
{
    var db = nameof(Get_IsIsolatedBetweenUsers);
    await CreateStore(db).SetAsync(User, "k", "v", CancellationToken.None);

    Assert.Empty(await CreateStore(db).GetAsync(Guid.NewGuid(), CancellationToken.None));
}
```

- [ ] **Step 4: Lancer → échec de compilation**

Run: `dotnet test --filter "FullyQualifiedName~UserPreferenceStoreTests"`
Expected: FAIL (signatures `string`→`Guid` incohérentes).

- [ ] **Step 5: Retyper le store et l'interface (GREEN)**

Dans `IUserPreferenceStore.cs` : `GetAsync(Guid userId, ...)`, `SetAsync(Guid userId, string key, string value, ...)`. Dans `UserPreferenceStore.cs` : remplacer chaque `string accountId` par `Guid userId`, chaque prédicat `p.AccountId == accountId` par `p.UserId == userId`, et l'affectation `AccountId = accountId` par `UserId = userId` :

```csharp
public async Task<IReadOnlyList<UserPreference>> GetAsync(Guid userId, CancellationToken cancellationToken)
    => await _context.UserPreferences.AsNoTracking()
        .Where(p => p.UserId == userId)
        .OrderBy(p => p.PreferenceKey)
        .ToListAsync(cancellationToken);

public async Task SetAsync(Guid userId, string key, string value, CancellationToken cancellationToken)
{
    var existing = await _context.UserPreferences
        .FirstOrDefaultAsync(p => p.UserId == userId && p.PreferenceKey == key, cancellationToken);

    if (existing is null)
    {
        _context.UserPreferences.Add(new UserPreference
        {
            UserId = userId, PreferenceKey = key, PreferenceValue = value, UpdatedAt = DateTime.UtcNow
        });
    }
    else
    {
        existing.PreferenceValue = value;
        existing.UpdatedAt = DateTime.UtcNow;
    }

    await _context.SaveChangesAsync(cancellationToken);
}
```

- [ ] **Step 6: Adapter les 2 sites du contrôleur**

Dans `src/snoopy.microservice/Controllers/PreferencesController.cs`, remplacer les deux `FolderRoleStore.CanonicalAccountId(AuthenticatedUser.Email)` par `AuthenticatedUser.WebmailUid` :

```csharp
// GetPreferences
var stored = await _store.GetAsync(AuthenticatedUser.WebmailUid, cancellationToken);
// SetPreference
await _store.SetAsync(AuthenticatedUser.WebmailUid, request.Key!, request.Value!, cancellationToken);
```

- [ ] **Step 7: Adapter les tests du contrôleur**

Dans `PreferencesControllerTests.cs`, chaque test qui monte le contrôleur doit passer un GUID à `CreateAuthenticatedContext(user, domain, uid)` et attendre que le store soit appelé avec **ce** GUID (au lieu de `CanonicalAccountId(email)`). Mécanique : introduire un `Guid` de test partagé, le passer au helper, l'asserter dans les `Verify`/setups Moq.

- [ ] **Step 8: Lancer → succès**

Run: `dotnet test --filter "FullyQualifiedName~UserPreferenceStoreTests|FullyQualifiedName~PreferencesControllerTests"`
Expected: PASS.

- [ ] **Step 9: Suite complète + commit**

Run: `dotnet test` — Expected: PASS, 0 warning.

```bash
git add src/snoopy.microservice/Data/Preferences/UserPreference.cs \
        src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs \
        src/snoopy.microservice/Repositories/UserPreferenceStore.cs \
        src/snoopy.microservice/Repositories/IUserPreferenceStore.cs \
        src/snoopy.microservice/Controllers/PreferencesController.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/
git commit -F - <<'EOF'
Webmail preferences: key on the account GUID

user_preferences keys on user_id (Guid) from the webmail_uid claim instead of the canonical email.
EOF
```

---

### Task 4 : `sending_identities` sur `user_id` (GUID)

**Files:**
- Modify: `src/snoopy.microservice/Data/Preferences/SendingIdentity.cs`
- Modify: `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs` (clé de `SendingIdentity`)
- Modify: `src/snoopy.microservice/Repositories/SendingIdentityStore.cs` + `ISendingIdentityStore.cs`
- Modify: `src/snoopy.microservice/Controllers/IdentitiesController.cs` (2 sites)
- Modify: `src/snoopy.microservice/Services/MailSender.cs` (1 site + threading)
- Test: `.../Repositories/SendingIdentityStoreTests.cs`, `.../Controllers/IdentitiesControllerTests.cs`, `.../Services/MailSenderTests.cs`

**Interfaces:**
- Consumes: `User.WebmailUid` (Tâche 2).
- Produces: `ISendingIdentityStore { Task<...> GetAsync(Guid userId, CancellationToken ct); Task ReplaceAsync(Guid userId, IReadOnlyList<SendingIdentity> identities, CancellationToken ct); }` ; `SendingIdentity.UserId` (Guid).

- [ ] **Step 1: Retyper l'entité**

Dans `SendingIdentity.cs`, remplacer `AccountId` :

```csharp
[Column("user_id")]
public Guid UserId { get; set; }
```

- [ ] **Step 2: Clé composite**

Dans `PreferencesDbContext.OnModelCreating` :

```csharp
modelBuilder.Entity<SendingIdentity>().HasKey(i => new { i.UserId, i.Address });
```

- [ ] **Step 3: Adapter le test du store (RED)**

Dans `SendingIdentityStoreTests.cs`, le helper `Row(...)` ne pose pas d'account id (il est mis par `ReplaceAsync`). Introduire un `Guid User = Guid.NewGuid()` et l'utiliser dans les appels `GetAsync(User, ...)` / `ReplaceAsync(User, ...)`. Exemple :

```csharp
[Fact]
public async Task Replace_ThenGet_RoundTripsByUserId()
{
    var db = nameof(Replace_ThenGet_RoundTripsByUserId);
    var user = Guid.NewGuid();
    await CreateStore(db).ReplaceAsync(user, new[] { Row("a@weesky.be") }, CancellationToken.None);

    var rows = await CreateStore(db).GetAsync(user, CancellationToken.None);
    Assert.Equal("a@weesky.be", Assert.Single(rows).Address);
}
```

- [ ] **Step 4: Lancer → échec de compilation**

Run: `dotnet test --filter "FullyQualifiedName~SendingIdentityStoreTests"`
Expected: FAIL.

- [ ] **Step 5: Retyper le store et l'interface (GREEN)**

`ISendingIdentityStore.cs` : `GetAsync(Guid userId, ...)`, `ReplaceAsync(Guid userId, ...)`. `SendingIdentityStore.cs` :

```csharp
public async Task<IReadOnlyList<SendingIdentity>> GetAsync(Guid userId, CancellationToken cancellationToken)
    => await context.SendingIdentities.AsNoTracking()
        .Where(i => i.UserId == userId)
        .OrderBy(i => i.Address)
        .ToListAsync(cancellationToken);

public async Task ReplaceAsync(Guid userId, IReadOnlyList<SendingIdentity> identities, CancellationToken cancellationToken)
{
    var existing = await context.SendingIdentities
        .Where(i => i.UserId == userId)
        .ToListAsync(cancellationToken);
    context.SendingIdentities.RemoveRange(existing);

    var now = DateTime.UtcNow;
    foreach (var identity in identities)
    {
        identity.UserId = userId;
        identity.UpdatedAt = now;
        context.SendingIdentities.Add(identity);
    }

    await context.SaveChangesAsync(cancellationToken);
}
```

- [ ] **Step 6: Adapter `IdentitiesController` (2 sites)**

Dans `Controllers/IdentitiesController.cs`, remplacer `FolderRoleStore.CanonicalAccountId(AuthenticatedUser.Email)` par `AuthenticatedUser.WebmailUid` dans `Replace` et dans `LoadSourcesAsync` :

```csharp
// Replace
await store.ReplaceAsync(AuthenticatedUser.WebmailUid, validated.Value, cancellationToken);
// LoadSourcesAsync
var stored = await store.GetAsync(AuthenticatedUser.WebmailUid, cancellationToken);
```

`IdentityResolver` reste inchangé (il travaille sur des adresses ; voir spec).

- [ ] **Step 7: Adapter `MailSender` (1 site + threading)**

Dans `Services/MailSender.cs`, `SendAsync` calcule aujourd'hui `var accountId = FolderRoleStore.CanonicalAccountId(user.Email);` et le passe à `LoadIdentitiesAsync`/`BuildMessageAsync`. `user` est un `Models.User` : il porte désormais `WebmailUid`. Remplacer :

```csharp
var userId = user.WebmailUid;
```

et propager `userId` (Guid) partout où `accountId` (string) était passé aux appels du `ISendingIdentityStore` (`LoadIdentitiesAsync`, etc.). Les autres usages de `accountId` dans `SendAsync` qui n'étaient **pas** des clés d'identité (le staged store, cf. Tâche 5) restent sur leur propre valeur — vérifier chaque usage : seuls les appels à `_identities`/`ISendingIdentityStore` prennent le `Guid`. Adapter les signatures internes (`LoadIdentitiesAsync(Guid userId, ...)`).

- [ ] **Step 8: Adapter les tests contrôleur + MailSender**

`IdentitiesControllerTests.cs` et `MailSenderTests.cs` : passer un `Guid` via `CreateAuthenticatedContext(user, domain, uid)` (contrôleur) ou en montant un `User { WebmailUid = ... }` (MailSender), et asserter que le store est appelé avec ce GUID.

- [ ] **Step 9: Lancer → succès**

Run: `dotnet test --filter "FullyQualifiedName~SendingIdentityStoreTests|FullyQualifiedName~IdentitiesControllerTests|FullyQualifiedName~MailSenderTests"`
Expected: PASS.

- [ ] **Step 10: Suite complète + commit**

Run: `dotnet test` — Expected: PASS, 0 warning.

```bash
git add src/snoopy.microservice/Data/Preferences/SendingIdentity.cs \
        src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs \
        src/snoopy.microservice/Repositories/SendingIdentityStore.cs \
        src/snoopy.microservice/Repositories/ISendingIdentityStore.cs \
        src/snoopy.microservice/Controllers/IdentitiesController.cs \
        src/snoopy.microservice/Services/MailSender.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/
git commit -F - <<'EOF'
Webmail identities: key on the account GUID

sending_identities keys on user_id (Guid); IdentitiesController and MailSender read WebmailUid.
EOF
```

---

### Task 5 : `folder_role_overrides` sur `user_id`, sites staged, suppression de `CanonicalAccountId`

**Files:**
- Modify: `src/snoopy.microservice/Data/Preferences/FolderRoleOverride.cs`
- Modify: `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs` (clé de `FolderRoleOverride`)
- Modify: `src/snoopy.microservice/Repositories/FolderRoleStore.cs` (5 méthodes + suppression de `CanonicalAccountId`) + `IFolderRoleStore.cs`
- Modify: `src/snoopy.microservice/Controllers/MailController.cs` (5 sites folder-role + 2 sites staged)
- Modify: `src/snoopy.microservice/Repositories/MailFolderRepository.cs` (2 sites)
- Test: `.../Repositories/FolderRoleStoreTests.cs` (+ suppression du test de `CanonicalAccountId`), `.../Controllers/MailControllerTests.cs`, `.../Services/FolderRoleResolverTests.cs` (si elle construit des `FolderRoleOverride`)

**Interfaces:**
- Consumes: `User.WebmailUid` (Tâche 2).
- Produces: `IFolderRoleStore` avec `GetAsync(Guid userId, ...)`, `DeleteAsync(Guid userId, string role, ...)`, `ApplyRenameAsync(Guid userId, ...)`, `RemoveSubtreeAsync(Guid userId, ...)`, `UpsertAsync(FolderRoleOverride, ...)` (inchangé, mais l'`override.UserId` est un Guid) ; `FolderRoleOverride.UserId` (Guid). `FolderRoleStore.CanonicalAccountId` **supprimé**.

- [ ] **Step 1: Retyper l'entité**

Dans `FolderRoleOverride.cs`, remplacer `AccountId` :

```csharp
[Column("user_id")]
public Guid UserId { get; set; }
```

- [ ] **Step 2: Clé composite**

Dans `PreferencesDbContext.OnModelCreating` :

```csharp
modelBuilder.Entity<FolderRoleOverride>().HasKey(o => new { o.UserId, o.Role });
```

- [ ] **Step 3: Adapter le test du store (RED) et supprimer le test de `CanonicalAccountId`**

Dans `FolderRoleStoreTests.cs` : supprimer le test `CanonicalAccountId_TrimsAndLowercases` (la méthode disparaît). Le helper `Override(...)` prend aujourd'hui `string accountId = "alice@weesky.be"` ; le remplacer par `Guid userId` (paramètre) et l'utiliser dans les seeds/appels. Exemple :

```csharp
private static FolderRoleOverride Override(Guid userId, string role, string path,
    ulong uidValidity = 1, string? mailboxId = null) =>
    new() { UserId = userId, Role = role, FolderPath = path, UidValidity = uidValidity, MailboxId = mailboxId };
```

et adapter chaque test à un `Guid User = Guid.NewGuid()`.

- [ ] **Step 4: Lancer → échec de compilation**

Run: `dotnet test --filter "FullyQualifiedName~FolderRoleStoreTests"`
Expected: FAIL.

- [ ] **Step 5: Retyper le store et l'interface, supprimer `CanonicalAccountId` (GREEN)**

Dans `FolderRoleStore.cs` : **supprimer** la méthode `public static string CanonicalAccountId(string email) => ...`. Remplacer, dans les 5 méthodes, chaque `string accountId` par `Guid userId` et chaque prédicat `o.AccountId == accountId` (ou `@override.AccountId`) par `o.UserId == userId` (ou `@override.UserId`). `UpsertAsync` garde sa signature (`FolderRoleOverride @override`), seule la comparaison change :

```csharp
public async Task<IReadOnlyList<FolderRoleOverride>> GetAsync(Guid userId, CancellationToken cancellationToken)
    => await _context.FolderRoleOverrides.AsNoTracking()
        .Where(o => o.UserId == userId).OrderBy(o => o.Role).ToListAsync(cancellationToken);

public async Task UpsertAsync(FolderRoleOverride @override, CancellationToken cancellationToken)
{
    var existing = await _context.FolderRoleOverrides.FirstOrDefaultAsync(
        o => o.UserId == @override.UserId && o.Role == @override.Role, cancellationToken);
    // ... corps inchangé ...
}

public async Task DeleteAsync(Guid userId, string role, CancellationToken cancellationToken)
{
    var existing = await _context.FolderRoleOverrides.FirstOrDefaultAsync(
        o => o.UserId == userId && o.Role == role, cancellationToken);
    // ... inchangé ...
}

public async Task ApplyRenameAsync(Guid userId, string oldPath, string newPath, char separator,
    ulong newUidValidity, string? newMailboxId, CancellationToken cancellationToken)
{
    // ... remplacer o.AccountId == accountId par o.UserId == userId ...
}

public async Task RemoveSubtreeAsync(Guid userId, string path, char separator, CancellationToken cancellationToken)
{
    // ... remplacer o.AccountId == accountId par o.UserId == userId ...
}
```

Mettre à jour `IFolderRoleStore.cs` en conséquence (les 4 méthodes prenant un `accountId` → `Guid userId` ; `UpsertAsync` inchangée).

- [ ] **Step 6: Adapter `MailController` (5 sites folder-role + 2 staged)**

Dans `Controllers/MailController.cs`, remplacer chaque `FolderRoleStore.CanonicalAccountId(AuthenticatedUser.Email)` par `AuthenticatedUser.WebmailUid` aux 5 sites folder-role (`RefuseIfSystemFolderAsync`, `GetFolders`, `GetFolderRoles`, `SetFolderRole` — le local `accountId` devient `var userId = AuthenticatedUser.WebmailUid;` réutilisé, et `AccountId = accountId` devient `UserId = userId` dans le `new FolderRoleOverride` —, `ClearFolderRole`). Aux 2 sites staged (`UploadAttachment`, `DeleteAttachment`), remplacer par `AuthenticatedUser.WebmailUid.ToString()` (l'interface `IStagedAttachmentStore` garde sa clé `string` opaque) :

```csharp
// UploadAttachment
var result = await _staged.SaveAsync(
    AuthenticatedUser.WebmailUid.ToString(), file.FileName, file.ContentType, content, cancellationToken);
// DeleteAttachment
_staged.Delete(AuthenticatedUser.WebmailUid.ToString(), id);
```

- [ ] **Step 7: Adapter `MailFolderRepository` (2 sites)**

Dans `Repositories/MailFolderRepository.cs`, `TryMoveOverridesAsync` et `DeleteFolderAsync` reçoivent un `User user` : remplacer `FolderRoleStore.CanonicalAccountId(user.Email)` par `user.WebmailUid` :

```csharp
await _roleStore.ApplyRenameAsync(user.WebmailUid, oldPath, newPath, session.DirectorySeparator,
    status.Value.UidValidity, status.Value.MailboxId, cancellationToken);
// ...
await _roleStore.RemoveSubtreeAsync(user.WebmailUid, path, session.DirectorySeparator, cancellationToken);
```

- [ ] **Step 8: Adapter les tests**

`MailControllerTests.cs` (les 2 anciens usages de `CanonicalAccountId` disparaissent ; passer un GUID via le helper et asserter dessus) ; `FolderRoleResolverTests.cs` si elle construit des `FolderRoleOverride` avec `AccountId` → `UserId`. Vérifier qu'aucune autre référence à `CanonicalAccountId` ne subsiste : `grep -rn CanonicalAccountId src/snoopy.microservice` doit ne renvoyer que zéro occurrence (hors `ApiDocumentation.xml` régénéré).

- [ ] **Step 9: Lancer → succès**

Run: `dotnet test --filter "FullyQualifiedName~FolderRoleStoreTests|FullyQualifiedName~MailControllerTests|FullyQualifiedName~FolderRoleResolverTests"`
Expected: PASS.

- [ ] **Step 10: Suite complète + commit**

Run: `dotnet test` — Expected: PASS, 0 warning. `grep -rn "CanonicalAccountId" src/snoopy.microservice --include=*.cs` → aucune occurrence.

```bash
git add src/snoopy.microservice/Data/Preferences/FolderRoleOverride.cs \
        src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs \
        src/snoopy.microservice/Repositories/FolderRoleStore.cs \
        src/snoopy.microservice/Repositories/IFolderRoleStore.cs \
        src/snoopy.microservice/Controllers/MailController.cs \
        src/snoopy.microservice/Repositories/MailFolderRepository.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/
git commit -F - <<'EOF'
Webmail folder roles: key on the account GUID, drop CanonicalAccountId

folder_role_overrides and the staged store key on WebmailUid; the email-canonicalising helper is gone.
EOF
```

---

### Task 6 : Suppression best-effort de la ligne `users` par l'admin

**Files:**
- Modify: `src/snoopy.microservice/Repositories/AdminRepository.cs` (ctor + `DeleteUserAsync`)
- Modify: `src/snoopy.microservice/Program.cs` (aucun changement de ligne DI — `AdminRepository` est déjà scoped ; vérifier seulement)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/AdminRepositoryTests.cs`

**Interfaces:**
- Consumes: `IWebmailUserStore.DeleteByEmailAsync` (Tâche 1).
- Produces: `AdminRepository(ApplicationDbContext context, IWebmailUserStore webmailUsers, ILogger<AdminRepository> logger)` — la signature publique `IAdminRepository.DeleteUserAsync(int id)` **ne change pas**. Le logger est nouveau (le ctor actuel n'a que `ApplicationDbContext`).

- [ ] **Step 1: Test — la suppression propage un delete best-effort (RED)**

Dans `AdminRepositoryTests.cs`, introduire un helper de fabrication partagé (voir Step 4) et un test qui vérifie que `DeleteByEmailAsync` est appelé avec l'email canonique après une suppression réussie :

```csharp
[Fact]
public async Task DeleteUser_AlsoDeletesTheWebmailRow()
{
    using var ctx = CreateContext();
    AddDomain(ctx, id: "WSY", name: "weesky.be");
    var user = AddUser(ctx, "Alice", "WSY");
    var webmail = new Mock<IWebmailUserStore>();

    await CreateRepository(ctx, webmail.Object).DeleteUserAsync(user.Id);

    webmail.Verify(s => s.DeleteByEmailAsync("alice@weesky.be", It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task DeleteUser_WhenWebmailDeleteThrows_StillSucceeds()
{
    using var ctx = CreateContext();
    AddDomain(ctx, id: "WSY", name: "weesky.be");
    var user = AddUser(ctx, "alice", "WSY");
    var webmail = new Mock<IWebmailUserStore>();
    webmail.Setup(s => s.DeleteByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new InvalidOperationException("db down"));

    var result = await CreateRepository(ctx, webmail.Object).DeleteUserAsync(user.Id);

    Assert.True(result.IsSuccess);
    Assert.False(ctx.Users.Any(u => u.Id == user.Id));
}
```

- [ ] **Step 2: Lancer → échec de compilation**

Run: `dotnet test --filter "FullyQualifiedName~AdminRepositoryTests"`
Expected: FAIL (le ctor n'a pas encore le store ; `CreateRepository` absent).

- [ ] **Step 3: Câbler `AdminRepository`**

Dans `AdminRepository.cs`, ajouter la dépendance (garder le style `_field = field` du fichier) et modifier `DeleteUserAsync` pour reconstruire l'email canonique et supprimer best-effort **après** le `SaveChangesAsync` de `dovecot` :

```csharp
private readonly ApplicationDbContext _context;
private readonly IWebmailUserStore _webmailUsers;
private readonly ILogger<AdminRepository> _logger;

public AdminRepository(ApplicationDbContext context, IWebmailUserStore webmailUsers, ILogger<AdminRepository> logger)
{
    _context = context;
    _webmailUsers = webmailUsers;
    _logger = logger;
}

public async Task<Result> DeleteUserAsync(int id)
{
    var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
    if (user == null)
        return Result.Failure($"User with id {id} not found");

    var domain = await _context.Domains.FirstOrDefaultAsync(d => d.Id == user.DomainId);

    _context.Users.Remove(user);
    await _context.SaveChangesAsync();

    if (domain is not null)
    {
        var email = $"{user.Name}@{domain.Name}".Trim().ToLowerInvariant();
        try
        {
            await _webmailUsers.DeleteByEmailAsync(email, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Best-effort: the dovecot account is already gone; a webmail-DB failure must not
            // fail the deletion. Orphan preference rows are recoverable; a failed request is not.
            _logger.LogWarning(ex, "Webmail user row for {Email} could not be deleted after account removal", email);
        }
    }

    return Result.Success();
}
```

Le constructeur gagne donc `ILogger<AdminRepository>` (le ctor actuel n'a que `ApplicationDbContext`) ; l'enregistrement DI de `AdminRepository` ne change pas (logger et store fournis par le conteneur). Si le domaine est introuvable (`DomainId` orphelin), on saute le delete webmail (best-effort) plutôt que de deviner un email.

- [ ] **Step 4: Factory de test partagée (réparer les ~55 sites)**

Dans `AdminRepositoryTests.cs`, ajouter une fabrique et remplacer mécaniquement chaque `new AdminRepository(ctx)` par `CreateRepository(ctx)` :

```csharp
private static AdminRepository CreateRepository(TestDbContext ctx, IWebmailUserStore? webmailUsers = null) =>
    new(ctx, webmailUsers ?? new Mock<IWebmailUserStore>().Object,
        NullLogger<AdminRepository>.Instance);
```

(`using Microsoft.Extensions.Logging.Abstractions;`, `using Moq;`.) Find/replace : `new AdminRepository(ctx)` → `CreateRepository(ctx)` sur tout le fichier. Adapter l'ordre des arguments de `CreateRepository` à l'ordre réel du constructeur retenu au Step 3.

- [ ] **Step 5: Lancer → succès**

Run: `dotnet test --filter "FullyQualifiedName~AdminRepositoryTests"`
Expected: PASS.

- [ ] **Step 6: Suite complète + commit**

Run: `dotnet test` — Expected: PASS, 0 warning.

```bash
git add src/snoopy.microservice/Repositories/AdminRepository.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Repositories/AdminRepositoryTests.cs
git commit -F - <<'EOF'
Admin delete: best-effort removal of the webmail user row

After the dovecot delete, remove the snoopy_webmail.users row (FK cascade takes the preferences); a webmail-DB failure degrades, never fails the deletion.
EOF
```

---

## Notes transverses pour l'exécutant

- **`ApiBaseController.AuthenticatedUser`** : le plan suppose qu'il résout le `User` via `GetUser()` (donc `WebmailUid` peuplé une fois la Tâche 2 faite). **À vérifier au début de la Tâche 3** : ouvrir `Controllers/ApiBaseController.cs` (ou la base des contrôleurs mail) et confirmer que `AuthenticatedUser` passe bien par `ControllerBaseExtensions.GetUser()`. Si une autre construction du `User` existe, y ajouter la lecture de `WebmailClaimTypes.Uid` de la même façon.
- **Jeton émis avant la tranche** (déjà en cookie) : sa revendication `webmail_uid` est absente → `WebmailUid = Guid.Empty`. Ce cas se résout au prochain login (le cookie glissant réémet, mais `SlidingSessionMiddleware` ne peut pas inventer un GUID absent — il reporte `Guid.Empty`). Comme le webmail n'est pas en production (un seul utilisateur de test), **un `git`-déploiement suivi d'une reconnexion suffit** ; aucun traitement de compat n'est nécessaire. Ne pas ajouter de logique pour ça (YAGNI).
- **Cas jeton valide / utilisateur supprimé** (spec § Gestion d'erreur) : le `401` est déjà fourni par le contrôle d'existence `dovecot` existant dans `OnTokenValidated` (`AuthorizationExtension.cs`, caché 60 s). On n'ajoute **aucune** lecture `snoopy_webmail` par requête (ce serait contraire au design). L'état résiduel — ligne `users` supprimée à la main sans supprimer le compte `dovecot` — est un état incohérent hors périmètre.
- **Type Guid ↔ CHAR(36)** : Pomelo mappe un `Guid` en `char(36)` par défaut, cohérent avec le DDL. Rien à configurer. En tests InMemory le type SQL est ignoré.

## Global self-review (à faire par l'exécutant final)

- `grep -rn "CanonicalAccountId" src/snoopy.microservice --include=*.cs` → 0.
- `grep -rn "AccountId" src/snoopy.microservice/Data/Preferences src/snoopy.microservice/Repositories --include=*.cs` → 0 (les 3 entités et 3 stores sont bien passés à `UserId`).
- `dotnet test` → tout vert, 0 warning.

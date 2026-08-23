# Contacts 4c-i — l'identifiant de synchronisation et son écran : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Les rapports de sous-agents dans le scratchpad sont préfixés `4c-i-task-N-…`.

**Spec :** [`docs/superpowers/specs/2026-08-23-webmail-contacts-4c-carddav-design.md`](../specs/2026-08-23-webmail-contacts-4c-carddav-design.md) — toute décision citée ici (« décision N ») y renvoie. En cas de doute, la spec fait foi.

**Périmètre :** la spec se découpe en **deux** plans, dans cet ordre strict (§ « Découpage »). Celui-ci est **4c-i** : la table `dav_credentials`, l'engendrement du secret, le schéma d'authentification `CardDav`, son limiteur, l'API et l'onglet « Sync » des paramètres (décisions 1, 2 et 19). **4c-ii** — le serveur DAV lui-même, les quatre autres tables, les pierres tombales, l'historique — fait l'objet d'un plan séparé et ne doit **pas** être entamé ici. Rien de ce plan ne crée de route `/dav`.

**Goal :** donner à un client CardDAV de quoi s'authentifier, et à l'utilisateur un écran d'où copier les trois valeurs que ce client réclame.

**Architecture :** une table à une ligne par utilisateur (`user_id` en clé primaire), un secret base32 haché en SHA-256 salé et jamais restitué, un `AuthenticationHandler` ASP.NET nommé `CardDav` qui défie en Basic seul et se replie sur le JWT quand aucun en-tête Basic n'est présent, deux composants mémoire par instance (cache de rafale de 60 s, compteur d'échecs glissant de 15 min), un contrôleur de trois actions, et un onglet de paramètres.

**Tech stack :** .NET 10, EF Core (InMemory pour les tests), xUnit 2.9.3, Moq 4.20.72 ; React 18 + TypeScript, Vitest + Testing Library, i18next.

## Global constraints

- Backend : `cd src && dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés) ; `cd src && dotnet build` doit rester à zéro avertissement.
- Frontend : `cd src/frontend && npm test` ; `npx tsc --noEmit` et `npm run lint` doivent rester propres.
- `src/snoopy.microservice/ApiDocumentation.xml` : artefact versionné que `dotnet test` régénère avec des centaines de lignes sans rapport — le réverter avant chaque commit (`git checkout -- src/snoopy.microservice/ApiDocumentation.xml`).
- `Assert.IsType<T>` vérifie le type **exact** : `NotFoundObjectResult` pour `NotFound(body)`, `OkObjectResult` pour `Ok(body)`, jamais `ObjectResult`.
- Style C# : file-scoped namespaces, un type par fichier, constructeurs primaires pour l'injection, records pour les DTO, `sealed`, `internal` par défaut, `CancellationToken` sur tout `async`, `ILogger` en journalisation structurée (jamais d'interpolation).
- Style TS : pas de `any` ; l'API omet les champs `null` (`WhenWritingNull`), donc côté client un champ optionnel se déclare `champ?: T`, jamais `T | null`.
- i18n : toute clé neuve existe dans `src/frontend/src/locales/en/settings.json` **et** `fr/settings.json` ; l'UI du site est en anglais ; la parité et la typographie française (U+00A0 avant `; : ? !`, apostrophe `’`) sont vérifiées par `src/locales/parity.test.ts`.
- **Le secret n'est jamais journalisé, ni en clair ni haché.** Les lignes de journal nomment l'adresse ou le GUID, rien d'autre.
- **Le mot de passe du compte reste en clair côté microservice** (déclencheurs MariaDB) : cette règle ne concerne **pas** `dav_credentials`, dont le secret est engendré par nous et haché par nous.
- Commits : concis, sujet + ligne vide + corps de 2 lignes max, jamais commencer ni finir par `@`, terminer par `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`. **Ne jamais écrire un message de commit avec un here-string PowerShell dans l'outil Bash** — utiliser `git commit -F -` avec un heredoc.

## Valeurs fixées une fois, à ne pas réinventer

| Constante | Valeur | Où |
|---|---|---|
| Longueur du secret | 20 caractères base32 (`A–Z2–7`), ≈100 bits | `DavSecret.Length` |
| Sel | 16 octets aléatoires, `VARBINARY(16)` | `DavSecret.SaltLength` |
| Condensat | SHA-256 hexadécimal **minuscule** de `sel ‖ UTF8(secret)` | `DavSecret.Hash` |
| Realm du défi | `weesky CardDAV` — **jamais** varier entre déploiements | `CardDavAuthenticationDefaults.Realm` |
| Nom du schéma | `CardDav` | `CardDavAuthenticationDefaults.AuthenticationScheme` |
| Fenêtre du cache de rafale | `SessionGuard.CacheWindow` (60 s), réutilisée telle quelle | `DavAuthenticationCache` |
| Amortissement de `last_used_at` | 1 heure | `DavAuthenticationCache.TouchInterval` |
| Délai après échec | aléatoire dans [500 ms, 1500 ms], `await Task.Delay` **jamais** `Thread.Sleep` | `CardDavAuthenticationHandler` |
| Fenêtre du compteur d'échecs | 15 minutes glissantes | `AuthAttemptThrottle.Window` |
| Seuil d'échecs | 10 par clé | `AuthAttemptThrottle.MaxFailures` |
| Clés suivies au plus | 10 000, avec éviction | `AuthAttemptThrottle.MaxTrackedKeys` |

## Découpage

Quatre paquets, chacun livrable et vérifiable seul :

| | Paquet | Tâches | Vérifiable par |
|---|---|---|---|
| 1 | La table, le secret et son dépôt | 1–4 | la suite .NET ; rien ne bouge à l'écran |
| 2 | Le schéma d'authentification, son limiteur et la révocation | 5–8 | la suite .NET ; aucune route ne le porte encore |
| 3 | La configuration, la capacité et l'API | 9–10 | la suite .NET + Swagger |
| 4 | L'écran | 11–13 | la suite frontend + l'écran |

---

### Task 1 : le DDL et son fichier de prérequis

Rien de ce plan ne fonctionne sans la table, et le § « Prérequis d'infrastructure » de la spec impose l'ordre : **le DDL d'abord, le backend ensuite.** Cette tâche ne livre que de la documentation, et c'est délibéré — c'est le document qu'un opérateur rejoue avant le déploiement.

**Files :**
- Create : `docs/superpowers/webmail-carddav-tables.md`

**Interfaces :**
- Consomme : la table `users` de `snoopy_webmail` (`docs/superpowers/webmail-users-table.md`).
- Produit : les noms de colonnes que la tâche 2 mappe (`user_id`, `carddav_enabled`, `secret_hash`, `salt`, `created_at`, `last_used_at`).

- [ ] **Step 1 : Écrire le fichier**

Créer `docs/superpowers/webmail-carddav-tables.md` avec exactement ce contenu :

````markdown
# Prérequis base de données — tables CardDAV

À rejouer **avant** le déploiement du backend, sur `snoopy_webmail` **et** `snoopy_webmail_dev`.
Création manuelle : ce projet n'utilise pas les migrations EF.

Les FK exigent que `users` existe déjà (voir `webmail-users-table.md`).

L'ordre n'est pas une commodité d'exploitation : le backend refuse de lire une table absente, et
un déploiement qui précède son DDL rend `500` sur l'onglet « Sync ».

## Tranche 4c-i — `dav_credentials`

Une ligne par utilisateur, et c'est la forme qui dit qu'il n'y a qu'un secret par personne
(décision 1). Une clé technique et un index sur `user_id` laisseraient la table accepter une
deuxième ligne que rien dans le code ne crée — jusqu'au jour où une reprise l'y mettrait.

```sql
CREATE TABLE `dav_credentials` (
  `user_id`         CHAR(36)      NOT NULL,
  `carddav_enabled` TINYINT(1)    NOT NULL DEFAULT 1
    COMMENT 'Interrupteur par protocole ; CalDAV aura sa propre colonne, pas une migration',
  `secret_hash`     CHAR(64)      NOT NULL
    COMMENT 'SHA-256 hexadécimal minuscule de (salt || secret UTF-8)',
  `salt`            VARBINARY(16) NOT NULL,
  `created_at`      TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `last_used_at`    TIMESTAMP     NULL DEFAULT NULL
    COMMENT 'Amorti à l''heure côté service ; l''écran le rend en relatif',
  PRIMARY KEY (`user_id`),
  CONSTRAINT `fk_dav_credentials_user`
    FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

## Pourquoi le hachage n'est pas un KDF

C'est l'inverse de la règle habituelle et la raison est écrite ici pour que personne ne
« corrige » le hachage plus tard. Un KDF lent existe pour rendre coûteuse l'attaque par
dictionnaire d'un secret que l'humain a choisi. Ici l'entropie vient de nous : 20 caractères
base32, ≈100 bits, hors de portée d'une recherche exhaustive quelle que soit la vitesse du
hachage. Et un client DAV se ré-authentifie à **chaque** requête — un PBKDF2 à 100 000 itérations
y serait un déni de service que nous nous infligerions nous-mêmes, déclenchable à volonté par des
requêtes non authentifiées.

Le sel reste par ligne : il empêche qu'une même chaîne engendrée deux fois se reconnaisse dans la
table, et il ne coûte rien — la ligne se retrouve par sa clé, jamais par l'empreinte.

## Deux états distincts, et ils ne se confondent pas

- **Aucune ligne** = jamais activé. L'utilisateur n'a pas de secret, et le `401` est la seule
  réponse du bord.
- **`carddav_enabled = 0`** = éteint mais configuré. Le secret survit, rallumer ne reconfigure
  aucun appareil, et le bord répond `403` — mais seulement après une comparaison **réussie** du
  condensat (décision 2), sans quoi la réponse serait un oracle d'énumération de comptes.

Le défaut à `1` décrit l'état dans lequel la ligne naît — elle n'existe que si l'utilisateur a
allumé l'interrupteur —, pas une politique appliquée à qui n'a rien demandé.

## Ce que la tranche 4c-ii ajoutera

`contact_sync_state`, `contact_tombstones`, `contact_revisions`, deux colonnes sur `contacts`
(`dav_name`, `sync_sequence`) et leur rattrapage. Elles ne sont **pas** dans ce fichier tant que
4c-ii n'est pas écrite : un DDL rejoué en avance créerait des tables que rien ne lit et un
rattrapage que rien ne vérifie.

## Vérifier

```sql
SELECT COUNT(*) FROM information_schema.TABLES
 WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'dav_credentials';
-- attendu : 1
```
````

- [ ] **Step 2 : Vérifier que le SQL est syntaxiquement juste**

Aucun test automatisé ne couvre ce fichier. Relire à voix haute les points qui se trompent :
`VARBINARY(16)` et non `BINARY`, `CHAR(64)` et non `VARCHAR`, `ON DELETE CASCADE` présent, la
double apostrophe `''` dans les `COMMENT` français.

- [ ] **Step 3 : Commit**

```bash
git add docs/superpowers/webmail-carddav-tables.md
git commit -F - <<'EOF'
docs(carddav): la table dav_credentials a son prerequis

Une ligne par utilisateur, cle primaire user_id, et la raison ecrite
du hachage non-KDF.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 2 : l'entité et son arête dans le contexte

**Files :**
- Create : `src/snoopy.microservice/Data/Preferences/DavCredential.cs`
- Modify : `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs`
- Create : `src/snoopy.microservice/snoopy.microservice.Tests/Data/DavCredentialEntityTests.cs`

**Interfaces :**
- Consomme : `WebmailUser` (`Data/Preferences/WebmailUser.cs`).
- Produit : `DavCredential` et `PreferencesDbContext.DavCredentials`, lus par la tâche 3.

**Le piège de cette tâche, et c'est le seul :** sans arête déclarée dans `OnModelCreating`, EF
ordonne les `INSERT` par **nom de table** — `dav_credentials` trie avant `users` — et casse
`fk_dav_credentials_user` sur toute création. Les cinq tables voisines la déclarent déjà, sans
propriété de navigation ; celle-ci fait pareil. Le fournisseur InMemory n'applique aucune clé
étrangère, donc **aucun test ne peut attraper l'oubli** : c'est pour cela que la règle est écrite
ici plutôt que découverte en production.

- [ ] **Step 1 : Écrire le test d'aller-retour, rouge**

Créer `src/snoopy.microservice/snoopy.microservice.Tests/Data/DavCredentialEntityTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Data;

public sealed class DavCredentialEntityTests
{
    [Fact]
    public async Task DavCredential_RoundTripsThroughTheContext()
    {
        var context = new PreferencesTestDbContext(nameof(DavCredential_RoundTripsThroughTheContext));
        var user = Guid.NewGuid();

        context.DavCredentials.Add(new DavCredential
        {
            UserId = user,
            CardDavEnabled = true,
            SecretHash = new string('a', 64),
            Salt = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16],
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(CancellationToken.None);

        var stored = Assert.Single(context.DavCredentials);
        Assert.Equal(user, stored.UserId);
        Assert.True(stored.CardDavEnabled);
        Assert.Equal(16, stored.Salt.Length);
        // Jamais utilisé veut dire null, et se dit à l'écran — pas une case vide (décision 19).
        Assert.Null(stored.LastUsedAt);
    }

    [Fact]
    public void DavCredential_IsEnabledWhenBorn()
    {
        // Le défaut décrit l'état dans lequel la ligne naît : elle n'existe que si l'utilisateur
        // a allumé l'interrupteur. Un compte sans ligne ne synchronise pas.
        Assert.True(new DavCredential().CardDavEnabled);
    }

    [Fact]
    public void UserIdIsThePrimaryKey_SoThereIsExactlyOneSecretPerUser()
    {
        var context = new PreferencesTestDbContext(nameof(UserIdIsThePrimaryKey_SoThereIsExactlyOneSecretPerUser));

        var key = context.Model.FindEntityType(typeof(DavCredential))!.FindPrimaryKey()!;

        var property = Assert.Single(key.Properties);
        Assert.Equal(nameof(DavCredential.UserId), property.Name);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavCredentialEntityTests`
Expected : ÉCHEC de compilation — `DavCredential` n'existe pas.

- [ ] **Step 3 : Écrire l'entité**

Créer `src/snoopy.microservice/Data/Preferences/DavCredential.cs` :

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// The one synchronisation secret an account has. <c>user_id</c> is the primary key rather than a
/// surrogate: that is the shape saying there is one secret per person and not two, and a table
/// keyed otherwise would accept a second row nothing in this code creates — until a restore put
/// one there.
///
/// Absent row means never enabled; <see cref="CardDavEnabled"/> false means switched off but still
/// configured, which is a different answer at the edge (403, never 401) and a different gesture on
/// screen. The secret itself is never stored — only the salted digest of it.
/// </summary>
[Table("dav_credentials")]
public sealed class DavCredential
{
    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>Per protocol, not per secret: CalDAV gets a column of its own, never a migration.</summary>
    [Column("carddav_enabled")]
    public bool CardDavEnabled { get; set; } = true;

    /// <summary>Lower-case hexadecimal SHA-256 of <c>salt ‖ UTF8(secret)</c>. 64 characters.</summary>
    [Column("secret_hash")]
    public string SecretHash { get; set; } = string.Empty;

    [Column("salt")]
    public byte[] Salt { get; set; } = [];

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>Null until a client authenticates. Written at most once an hour, per instance.</summary>
    [Column("last_used_at")]
    public DateTime? LastUsedAt { get; set; }
}
```

- [ ] **Step 4 : Déclarer la clé, l'arête et le DbSet**

Dans `PreferencesDbContext.OnModelCreating`, à la suite du bloc `TrustedSender` :

```csharp
        modelBuilder.Entity<DavCredential>().HasKey(c => c.UserId);
        // Same mechanism as the five tables above: "dav_credentials" sorts before "users", so
        // without a declared edge EF orders the INSERTs by table name and breaks the FK on any
        // create. Declared without navigation, like its neighbours. The InMemory provider enforces
        // no foreign key, so no test can catch this — only the declaration can.
        modelBuilder.Entity<DavCredential>()
            .HasOne<WebmailUser>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
```

et, à la suite des `DbSet` :

```csharp
    public DbSet<DavCredential> DavCredentials { get; set; }
```

- [ ] **Step 5 : Lancer les tests pour les voir passer**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavCredentialEntityTests`
Expected : 3 tests PASS.

- [ ] **Step 6 : Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice/Data/Preferences/DavCredential.cs \
        src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Data/DavCredentialEntityTests.cs
git commit -F - <<'EOF'
feat(carddav): l'identifiant de synchronisation a sa table

user_id en cle primaire, arete declaree vers users pour l'ordre des
INSERT.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 3 : le secret — engendrement, condensat, comparaison

**Files :**
- Create : `src/snoopy.microservice/Services/DavSecret.cs`
- Create : `src/snoopy.microservice/snoopy.microservice.Tests/Services/DavSecretTests.cs`

**Interfaces :**
- Produit, consommé par les tâches 5, 6 et 9 :

```csharp
internal static class DavSecret
{
    internal const int Length = 20;
    internal const int SaltLength = 16;

    internal static string Generate();
    internal static byte[] NewSalt();
    internal static string Hash(byte[] salt, string secret);
    internal static bool Matches(byte[] salt, string storedHash, string presented);
    internal static string Fingerprint(string presented);
}
```

`Fingerprint` est le SHA-256 hexadécimal du secret **présenté seul**, sans sel : c'est la moitié
variable de la clé du cache de rafale (décision 1), qui ne doit jamais porter le secret en clair.
Il n'est comparé à rien de stocké — deux rôles, deux fonctions, et les confondre mettrait un
condensat non salé en base.

- [ ] **Step 1 : Écrire les tests, rouges**

Créer `src/snoopy.microservice/snoopy.microservice.Tests/Services/DavSecretTests.cs` :

```csharp
using System.Security.Cryptography;
using System.Text;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class DavSecretTests
{
    [Fact]
    public void Generate_DrawsADistinctSecretEveryTime()
    {
        var drawn = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 200; i++) drawn.Add(DavSecret.Generate());

        Assert.Equal(200, drawn.Count);
    }

    [Fact]
    public void Generate_IsTwentyBase32Characters()
    {
        var secret = DavSecret.Generate();

        Assert.Equal(DavSecret.Length, secret.Length);
        // The base32 alphabet carries no whitespace, which is what makes the Trim below safe.
        Assert.All(secret, c => Assert.Contains(c, "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"));
    }

    [Fact]
    public void Hash_IsTheLowerCaseHexOfSaltThenSecret()
    {
        byte[] salt = [.. Enumerable.Range(0, DavSecret.SaltLength).Select(i => (byte)i)];

        var hash = DavSecret.Hash(salt, "ABCDEFGHIJKLMNOPQRST");

        var expected = Convert.ToHexStringLower(
            SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes("ABCDEFGHIJKLMNOPQRST")]));
        Assert.Equal(expected, hash);
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void Hash_DiffersForTheSameSecretUnderTwoSalts()
    {
        // What the per-row salt buys: the same string drawn twice does not recognise itself in
        // the table.
        var first = DavSecret.Hash(DavSecret.NewSalt(), "ABCDEFGHIJKLMNOPQRST");
        var second = DavSecret.Hash(DavSecret.NewSalt(), "ABCDEFGHIJKLMNOPQRST");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void NewSalt_IsSixteenBytesAndNeverTheSameTwice()
    {
        var first = DavSecret.NewSalt();
        var second = DavSecret.NewSalt();

        Assert.Equal(DavSecret.SaltLength, first.Length);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Matches_AcceptsTheSecretItHashed()
    {
        var salt = DavSecret.NewSalt();
        var secret = DavSecret.Generate();

        Assert.True(DavSecret.Matches(salt, DavSecret.Hash(salt, secret), secret));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void Matches_IgnoresEdgeWhitespaceOnThePresentedSecret(string blank)
    {
        // Copy-paste — mobile above all — adds these, and the base32 alphabet holds none of them.
        // Without the Trim the symptom is a correct password refused, indistinguishable from a typo.
        var salt = DavSecret.NewSalt();
        var secret = DavSecret.Generate();

        Assert.True(DavSecret.Matches(salt, DavSecret.Hash(salt, secret), $"{blank}{secret}{blank}"));
    }

    [Fact]
    public void Matches_RefusesAnotherSecret()
    {
        var salt = DavSecret.NewSalt();

        Assert.False(DavSecret.Matches(salt, DavSecret.Hash(salt, DavSecret.Generate()), DavSecret.Generate()));
    }

    [Fact]
    public void Matches_RefusesAnEmptyOrMalformedStoredHash()
    {
        var salt = DavSecret.NewSalt();

        Assert.False(DavSecret.Matches(salt, string.Empty, DavSecret.Generate()));
        Assert.False(DavSecret.Matches(salt, "not-hex", DavSecret.Generate()));
    }

    [Fact]
    public void Fingerprint_IsSaltFreeAndStableForTheSameSecret()
    {
        var secret = DavSecret.Generate();

        Assert.Equal(DavSecret.Fingerprint(secret), DavSecret.Fingerprint(secret));
        Assert.NotEqual(DavSecret.Fingerprint(secret), DavSecret.Fingerprint(DavSecret.Generate()));
        // Never the stored digest: that one is salted, this one is only ever a cache key.
        Assert.NotEqual(DavSecret.Hash(DavSecret.NewSalt(), secret), DavSecret.Fingerprint(secret));
    }
}
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavSecretTests`
Expected : ÉCHEC de compilation — `DavSecret` n'existe pas.

- [ ] **Step 3 : Écrire le service**

Créer `src/snoopy.microservice/Services/DavSecret.cs` :

```csharp
using System.Security.Cryptography;
using System.Text;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The synchronisation secret: drawn here, hashed here, compared here, and never stored in clear.
///
/// The digest is a salted SHA-256 and deliberately not a slow KDF. A KDF exists to price the
/// dictionary attack on a secret a human chose; this one carries ~100 bits drawn by us, where an
/// exhaustive search is out of reach at any hashing speed — while a DAV client re-authenticates on
/// every single request, so an iterated KDF here would be a denial of service we inflict on
/// ourselves, triggerable by unauthenticated traffic. See the slice's design note before
/// "correcting" this.
/// </summary>
internal static class DavSecret
{
    /// <summary>20 base32 characters ≈ 100 bits.</summary>
    internal const int Length = 20;

    internal const int SaltLength = 16;

    /// <summary>RFC 4648 base32, minus nothing: no whitespace, which is what makes the Trim safe.</summary>
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    internal static string Generate() => RandomNumberGenerator.GetString(Alphabet, Length);

    internal static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltLength);

    internal static string Hash(byte[] salt, string secret) =>
        Convert.ToHexStringLower(SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes(secret)]));

    /// <summary>
    /// Constant-time comparison of the stored digest against the presented secret, whose edge
    /// whitespace is stripped first — copy-paste adds it, the alphabet contains none, and the
    /// symptom without this is a correct password refused.
    /// </summary>
    internal static bool Matches(byte[] salt, string storedHash, string presented)
    {
        var computed = Hash(salt, presented.Trim());
        if (computed.Length != storedHash.Length) return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(storedHash));
    }

    /// <summary>
    /// The variable half of the burst cache's key. Salt-free on purpose: it is never compared to
    /// anything stored, and it exists so the clear secret does not survive the request.
    /// </summary>
    internal static string Fingerprint(string presented) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(presented.Trim())));
}
```

- [ ] **Step 4 : Lancer les tests pour les voir passer**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavSecretTests`
Expected : tous PASS (le `[Theory]` compte pour trois).

- [ ] **Step 5 : Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice/Services/DavSecret.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/DavSecretTests.cs
git commit -F - <<'EOF'
feat(carddav): le secret s'engendre, se hache et se compare

Base32 sur 20 caracteres, SHA-256 sale, comparaison en temps constant,
blancs de bord ignores.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
EOF
```

---

### Task 4 : le dépôt `IDavCredentialStore`

**Files :**
- Create : `src/snoopy.microservice/Repositories/IDavCredentialStore.cs`
- Create : `src/snoopy.microservice/Repositories/DavCredentialStore.cs`
- Modify : `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs` (`AddRepositories`)
- Create : `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/DavCredentialStoreTests.cs`

**Interfaces :**
- Consomme : `DavCredential`, `PreferencesDbContext` (tâche 2), `DavSecret` (tâche 3).
- Produit, consommé par les tâches 7 et 9 :

```csharp
public readonly record struct DavCredentialState(bool Configured, bool CardDavEnabled, DateTime? LastUsedAt);
public readonly record struct DavCredentialRecord(bool CardDavEnabled, string SecretHash, byte[] Salt);

public interface IDavCredentialStore
{
    Task<DavCredentialState> GetStateAsync(Guid userId, CancellationToken cancellationToken);
    Task<DavCredentialRecord?> FindAsync(Guid userId, CancellationToken cancellationToken);
    Task<string?> EnableAsync(Guid userId, CancellationToken cancellationToken);
    Task DisableAsync(Guid userId, CancellationToken cancellationToken);
    Task<string?> RegenerateAsync(Guid userId, CancellationToken cancellationToken);
    Task TouchAsync(Guid userId, DateTime usedAt, CancellationToken cancellationToken);
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken);
}
```

**Le contrat des trois méthodes qui rendent un `string?`, parce qu'il se lit mal autrement :**

| Méthode | Ligne absente | Ligne présente |
|---|---|---|
| `EnableAsync` | crée la ligne, engendre le secret, le **rend** | pose `carddav_enabled = 1`, rend `null` — rallumer n'a rien de neuf à montrer |
| `RegenerateAsync` | rend `null` sans rien créer — régénérer ce qui n'a jamais été allumé n'est pas une création | remplace `secret_hash` **et** `salt` sur la ligne existante, rend le nouveau secret |
| `DisableAsync` | ne fait rien | pose `carddav_enabled = 0`, **conserve** `secret_hash` |

`EnableAsync` est un upsert : deux premiers allumages simultanés — double clic, deux onglets — ne
doivent pas faire mourir le second sur la clé primaire. Le premier secret écrit gagne, l'autre
requête rattrape la `DbUpdateException`, relit la ligne du gagnant, la rallume si besoin et rend
`null`, exactement comme un rallumage. C'est le motif que `WebmailUserStore.RegisterLoginAsync`
applique déjà à sa course de première connexion.

`RegenerateAsync` remplace **le sel avec le condensat**. Garder le sel ferait d'une régénération
une rotation à moitié faite : la table perdrait la propriété que le sel existe pour tenir.

`TouchAsync` ne crée rien : une ligne absente est une écriture à zéro ligne, pas une erreur — c'est
un chemin appelé depuis l'authentification, où rien ne doit se créer.

- [ ] **Step 1 : Écrire les tests, rouges**

Créer `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/DavCredentialStoreTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class DavCredentialStoreTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static DavCredentialStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName));

    [Fact]
    public async Task Enable_WhenAbsent_CreatesTheRowAndAnswersTheSecret()
    {
        var db = nameof(Enable_WhenAbsent_CreatesTheRowAndAnswersTheSecret);

        var secret = await CreateStore(db).EnableAsync(User, CancellationToken.None);

        Assert.NotNull(secret);
        Assert.Equal(DavSecret.Length, secret!.Length);
        using var ctx = new PreferencesTestDbContext(db);
        var row = Assert.Single(ctx.DavCredentials);
        Assert.True(row.CardDavEnabled);
        Assert.Equal(DavSecret.SaltLength, row.Salt.Length);
        // Stored as a digest and nothing else: the table is never a keyring to steal.
        Assert.True(DavSecret.Matches(row.Salt, row.SecretHash, secret));
        Assert.DoesNotContain(secret, row.SecretHash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Enable_WhenAlreadyConfigured_TurnsItBackOnWithoutANewSecret()
    {
        var db = nameof(Enable_WhenAlreadyConfigured_TurnsItBackOnWithoutANewSecret);
        var first = await CreateStore(db).EnableAsync(User, CancellationToken.None);
        await CreateStore(db).DisableAsync(User, CancellationToken.None);

        var again = await CreateStore(db).EnableAsync(User, CancellationToken.None);

        Assert.Null(again);
        using var ctx = new PreferencesTestDbContext(db);
        var row = Assert.Single(ctx.DavCredentials);
        Assert.True(row.CardDavEnabled);
        // Turning off destroys nothing, turning back on reconfigures no device.
        Assert.True(DavSecret.Matches(row.Salt, row.SecretHash, first!));
    }

    [Fact]
    public async Task TwoFirstEnablesAtOnce_LeaveOneRowAndOneSecret()
    {
        // Double click, two tabs. The InMemory provider does enforce the primary key on
        // SaveChanges, so this exercises the real DbUpdateException path rather than a mock of it.
        var db = nameof(TwoFirstEnablesAtOnce_LeaveOneRowAndOneSecret);
        var first = CreateStore(db);
        var second = CreateStore(db);

        var winner = await first.EnableAsync(User, CancellationToken.None);
        var loser = await second.EnableAsync(User, CancellationToken.None);

        Assert.NotNull(winner);
        // The loser answers as a re-enable does: the state, and no second secret.
        Assert.Null(loser);
        using var ctx = new PreferencesTestDbContext(db);
        var row = Assert.Single(ctx.DavCredentials);
        Assert.True(row.CardDavEnabled);
        Assert.True(DavSecret.Matches(row.Salt, row.SecretHash, winner!));
    }

    [Fact]
    public async Task Disable_KeepsTheSecret()
    {
        var db = nameof(Disable_KeepsTheSecret);
        var secret = await CreateStore(db).EnableAsync(User, CancellationToken.None);

        await CreateStore(db).DisableAsync(User, CancellationToken.None);

        using var ctx = new PreferencesTestDbContext(db);
        var row = Assert.Single(ctx.DavCredentials);
        Assert.False(row.CardDavEnabled);
        Assert.True(DavSecret.Matches(row.Salt, row.SecretHash, secret!));
    }

    [Fact]
    public async Task Disable_OnAnAccountThatNeverEnabled_DoesNothing()
    {
        var db = nameof(Disable_OnAnAccountThatNeverEnabled_DoesNothing);

        await CreateStore(db).DisableAsync(User, CancellationToken.None);

        using var ctx = new PreferencesTestDbContext(db);
        Assert.Empty(ctx.DavCredentials);
    }

    [Fact]
    public async Task Regenerate_ReplacesTheSecretAndTheSaltOnTheSameRow()
    {
        var db = nameof(Regenerate_ReplacesTheSecretAndTheSaltOnTheSameRow);
        var first = await CreateStore(db).EnableAsync(User, CancellationToken.None);
        byte[] firstSalt;
        using (var before = new PreferencesTestDbContext(db)) firstSalt = before.DavCredentials.Single().Salt;

        var second = await CreateStore(db).RegenerateAsync(User, CancellationToken.None);

        Assert.NotNull(second);
        Assert.NotEqual(first, second);
        using var ctx = new PreferencesTestDbContext(db);
        // One row, never a second: user_id is the primary key and the shape is the guarantee.
        var row = Assert.Single(ctx.DavCredentials);
        Assert.NotEqual(firstSalt, row.Salt);
        Assert.True(DavSecret.Matches(row.Salt, row.SecretHash, second!));
        Assert.False(DavSecret.Matches(row.Salt, row.SecretHash, first!));
    }

    [Fact]
    public async Task Regenerate_OnAnAccountThatNeverEnabled_CreatesNothing()
    {
        var db = nameof(Regenerate_OnAnAccountThatNeverEnabled_CreatesNothing);

        var secret = await CreateStore(db).RegenerateAsync(User, CancellationToken.None);

        Assert.Null(secret);
        using var ctx = new PreferencesTestDbContext(db);
        Assert.Empty(ctx.DavCredentials);
    }

    [Fact]
    public async Task GetState_ReportsAConfiguredAccountAndCarriesNoSecret()
    {
        var db = nameof(GetState_ReportsAConfiguredAccountAndCarriesNoSecret);
        await CreateStore(db).EnableAsync(User, CancellationToken.None);

        var state = await CreateStore(db).GetStateAsync(User, CancellationToken.None);

        Assert.True(state.Configured);
        Assert.True(state.CardDavEnabled);
        Assert.Null(state.LastUsedAt);
        // The assertion that keeps the "reveal" door shut: the shape has nowhere to put a secret.
        Assert.Equal(3, typeof(DavCredentialState).GetProperties().Length);
    }

    [Fact]
    public async Task GetState_OnAnAccountThatNeverEnabled_IsNotConfigured()
    {
        var db = nameof(GetState_OnAnAccountThatNeverEnabled_IsNotConfigured);

        var state = await CreateStore(db).GetStateAsync(User, CancellationToken.None);

        Assert.False(state.Configured);
        Assert.False(state.CardDavEnabled);
    }

    [Fact]
    public async Task Find_AnswersTheRowTheHandlerCompares()
    {
        var db = nameof(Find_AnswersTheRowTheHandlerCompares);
        var secret = await CreateStore(db).EnableAsync(User, CancellationToken.None);

        var record = await CreateStore(db).FindAsync(User, CancellationToken.None);

        Assert.NotNull(record);
        Assert.True(record!.Value.CardDavEnabled);
        Assert.True(DavSecret.Matches(record.Value.Salt, record.Value.SecretHash, secret!));
    }

    [Fact]
    public async Task Find_OnAnAccountThatNeverEnabled_IsNull()
    {
        var db = nameof(Find_OnAnAccountThatNeverEnabled_IsNull);

        Assert.Null(await CreateStore(db).FindAsync(User, CancellationToken.None));
    }

    [Fact]
    public async Task Touch_WritesTheDateAndCreatesNothingWhenThereIsNoRow()
    {
        var db = nameof(Touch_WritesTheDateAndCreatesNothingWhenThereIsNoRow);
        var used = new DateTime(2026, 8, 23, 10, 0, 0, DateTimeKind.Utc);

        await CreateStore(db).TouchAsync(User, used, CancellationToken.None);
        using (var empty = new PreferencesTestDbContext(db)) Assert.Empty(empty.DavCredentials);

        await CreateStore(db).EnableAsync(User, CancellationToken.None);
        await CreateStore(db).TouchAsync(User, used, CancellationToken.None);

        using var ctx = new PreferencesTestDbContext(db);
        Assert.Equal(used, ctx.DavCredentials.Single().LastUsedAt);
    }

    [Fact]
    public async Task Delete_RemovesTheRowAndIsSilentOnAnAbsentOne()
    {
        var db = nameof(Delete_RemovesTheRowAndIsSilentOnAnAbsentOne);
        await CreateStore(db).EnableAsync(User, CancellationToken.None);

        await CreateStore(db).DeleteAsync(User, CancellationToken.None);
        await CreateStore(db).DeleteAsync(User, CancellationToken.None);

        using var ctx = new PreferencesTestDbContext(db);
        Assert.Empty(ctx.DavCredentials);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavCredentialStoreTests`
Expected : ÉCHEC de compilation — `DavCredentialStore` n'existe pas.

- [ ] **Step 3 : Écrire l'interface**

Créer `src/snoopy.microservice/Repositories/IDavCredentialStore.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// The synchronisation state as the screen needs it. It carries no secret in any shape, and that
/// is the point: a screen able to show one again would force the table to hold it in clear.
/// </summary>
public readonly record struct DavCredentialState(bool Configured, bool CardDavEnabled, DateTime? LastUsedAt);

/// <summary>What the authentication handler compares, read in one indexed lookup.</summary>
public readonly record struct DavCredentialRecord(bool CardDavEnabled, string SecretHash, byte[] Salt);

public interface IDavCredentialStore
{
    /// <summary>Never null: an absent row is "never enabled", which the screen shows as off.</summary>
    Task<DavCredentialState> GetStateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The row, or null when the account never enabled synchronisation.</summary>
    Task<DavCredentialRecord?> FindAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Turns synchronisation on. Returns the freshly drawn secret when this call created the row —
    /// the one and only moment it exists in clear — and null when it merely switched an existing
    /// row back on, including when a concurrent first enable won the race.
    /// </summary>
    Task<string?> EnableAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Switches off without destroying anything. Silent on an account with no row.</summary>
    Task DisableAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Draws a new secret and a new salt on the existing row, returning the secret. Null when
    /// there is no row: regenerating what was never enabled is not a create.
    /// </summary>
    Task<string?> RegenerateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Stamps the last use. Called from the authentication path, so it creates nothing — an absent
    /// row is a zero-row write, never an error.
    /// </summary>
    Task TouchAsync(Guid userId, DateTime usedAt, CancellationToken cancellationToken);

    /// <summary>Removes the row if present. What a security-stamp rotation does (décision 2).</summary>
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken);
}
```

- [ ] **Step 4 : Écrire l'implémentation**

Créer `src/snoopy.microservice/Repositories/DavCredentialStore.cs` :

```csharp
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class DavCredentialStore(PreferencesDbContext context) : IDavCredentialStore
{
    public async Task<DavCredentialState> GetStateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await context.DavCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        return row is null
            ? new DavCredentialState(false, false, null)
            : new DavCredentialState(true, row.CardDavEnabled, row.LastUsedAt);
    }

    public async Task<DavCredentialRecord?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await context.DavCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

        return row is null ? null : new DavCredentialRecord(row.CardDavEnabled, row.SecretHash, row.Salt);
    }

    public async Task<string?> EnableAsync(Guid userId, CancellationToken cancellationToken)
    {
        var existing = await Track(userId, cancellationToken);
        if (existing is not null)
        {
            existing.CardDavEnabled = true;
            await context.SaveChangesAsync(cancellationToken);
            return null;
        }

        var secret = DavSecret.Generate();
        var salt = DavSecret.NewSalt();
        var row = new DavCredential
        {
            UserId = userId,
            CardDavEnabled = true,
            Salt = salt,
            SecretHash = DavSecret.Hash(salt, secret),
            CreatedAt = DateTime.UtcNow
        };
        context.DavCredentials.Add(row);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return secret;
        }
        catch (DbUpdateException)
        {
            // A concurrent first enable — double click, two tabs — inserted the same key. The
            // first secret written wins; this call answers as a plain re-enable would, so no
            // second secret is ever handed out and neither request dies on the primary key.
            context.Entry(row).State = EntityState.Detached;
            var winner = await Track(userId, cancellationToken);
            if (winner is null) throw;

            winner.CardDavEnabled = true;
            await context.SaveChangesAsync(cancellationToken);
            return null;
        }
    }

    public async Task DisableAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await Track(userId, cancellationToken);
        if (row is null) return;

        row.CardDavEnabled = false;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> RegenerateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await Track(userId, cancellationToken);
        if (row is null) return null;

        var secret = DavSecret.Generate();
        // The salt goes with it: keeping it would make a regeneration a half-done rotation.
        row.Salt = DavSecret.NewSalt();
        row.SecretHash = DavSecret.Hash(row.Salt, secret);
        await context.SaveChangesAsync(cancellationToken);

        return secret;
    }

    public async Task TouchAsync(Guid userId, DateTime usedAt, CancellationToken cancellationToken)
    {
        var row = await Track(userId, cancellationToken);
        if (row is null) return;

        row.LastUsedAt = usedAt;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await Track(userId, cancellationToken);
        if (row is null) return;

        context.DavCredentials.Remove(row);
        await context.SaveChangesAsync(cancellationToken);
    }

    private Task<DavCredential?> Track(Guid userId, CancellationToken cancellationToken) =>
        context.DavCredentials.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
}
```

- [ ] **Step 5 : L'enregistrer dans le conteneur**

Dans `Configuration/ApplicationServicesConfiguration.cs`, méthode `AddRepositories`, à la suite de
`IConnectedAccountStore` :

```csharp
        services.AddScoped<IDavCredentialStore, DavCredentialStore>();
```

- [ ] **Step 6 : Lancer les tests pour les voir passer**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavCredentialStoreTests`
Expected : 13 tests PASS.

- [ ] **Step 7 : Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice/Repositories/IDavCredentialStore.cs src/snoopy.microservice/Repositories/DavCredentialStore.cs src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs src/snoopy.microservice/snoopy.microservice.Tests/Repositories/DavCredentialStoreTests.cs
git commit -F - <<'MSG'
feat(carddav): le depot lit, allume, eteint et regenere le secret

Allumer engendre et rend le secret une fois ; eteindre conserve, regenerer
remplace sel et condensat.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
```

---

### Task 5 : le cache de rafale et l'amortissement de `last_used_at`

Deux mémoires par instance, dans un seul composant parce qu'elles répondent à la même question —
« qu'est-ce que cette instance sait déjà de cette authentification ? » — et sont vidées par le même
geste.

**Le cache est pour la rafale, pas pour le balayage.** Un client DAV envoie ses identifiants sur
**chaque** requête, et une synchronisation réelle enchaîne un `PROPFIND`, un `REPORT` et autant de
`GET` en quelques secondes : rien ne justifie d'y relire la table dix fois. Hors rafale il ne sert
à rien et c'est voulu — DAVx⁵ et iOS interrogent l'état toutes les quinze minutes, où le coût est
d'une lecture indexée et d'un SHA-256.

**La clé est le couple (identifiant, empreinte du secret présenté), jamais le secret en clair**, et
la valeur est l'identité résolue avec l'état de l'interrupteur — sans ce second champ, un compte
éteint continuerait de répondre `200` pendant soixante secondes.

**La fenêtre borne, sur les autres instances, la survie d'un secret remplacé.** C'est le compromis
déjà retenu pour les sessions (`SessionGuard.CacheWindow`), et il vaut ici pour la même raison.

**Files :**
- Create : `src/snoopy.microservice/Authentication/CardDav/IDavAuthenticationCache.cs`
- Create : `src/snoopy.microservice/Authentication/CardDav/DavAuthenticationCache.cs`
- Create : `src/snoopy.microservice/snoopy.microservice.Tests/Authentication/DavAuthenticationCacheTests.cs`

**Interfaces :**
- Consomme : `SessionGuard.CacheWindow` (`Authentication/Services/SessionGuard.cs`), `TimeProvider`.
- Produit, consommé par les tâches 7 et 9 :

```csharp
public readonly record struct DavIdentity(Guid UserId, bool CardDavEnabled);

public interface IDavAuthenticationCache
{
    bool TryGet(string identifier, string fingerprint, out DavIdentity identity);
    void Store(string identifier, string fingerprint, DavIdentity identity);
    void Forget(string identifier);
    bool ShouldTouch(Guid userId);
}
```

`identifier` est l'adresse e-mail complète, canonicalisée (rognée, minuscule) par l'appelant —
la même canonicalisation que `WebmailUserStore` applique, sans quoi `Forget` manquerait l'entrée
qu'une casse différente a écrite.

**Une entrée par identifiant, et non par couple** : il n'y a qu'un secret par utilisateur, donc au
plus une empreinte utile à la fois. C'est ce qui rend `Forget` trivial — un `TryRemove` — là où un
`IMemoryCache` ne sait pas énumérer ses clés pour retrouver celles d'un utilisateur.

- [ ] **Step 1 : Écrire les tests, rouges**

Créer `src/snoopy.microservice/snoopy.microservice.Tests/Authentication/DavAuthenticationCacheTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication;

public sealed class DavAuthenticationCacheTests
{
    private static readonly Guid User = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static (DavAuthenticationCache Cache, MutableTimeProvider Clock) Create()
    {
        var clock = new MutableTimeProvider();
        return (new DavAuthenticationCache(clock), clock);
    }

    [Fact]
    public void Store_ThenTryGet_AnswersTheResolvedIdentity()
    {
        var (cache, _) = Create();

        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true));

        Assert.True(cache.TryGet("alice@weesky.be", "fingerprint-a", out var identity));
        Assert.Equal(User, identity.UserId);
        Assert.True(identity.CardDavEnabled);
    }

    [Fact]
    public void TryGet_MissesOnAnotherFingerprint()
    {
        // A replaced secret must not be served from the cache of the one it replaced.
        var (cache, _) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true));

        Assert.False(cache.TryGet("alice@weesky.be", "fingerprint-b", out _));
    }

    [Fact]
    public void TryGet_MissesOnAnotherIdentifier()
    {
        var (cache, _) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true));

        Assert.False(cache.TryGet("bob@weesky.be", "fingerprint-a", out _));
    }

    [Fact]
    public void TryGet_MissesOnceTheWindowHasPassed()
    {
        var (cache, clock) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true));

        clock.Now = clock.Now.Add(SessionGuardWindow + TimeSpan.FromSeconds(1));

        Assert.False(cache.TryGet("alice@weesky.be", "fingerprint-a", out _));
    }

    [Fact]
    public void Forget_DropsTheEntryImmediately()
    {
        // What a regeneration and a security-stamp rotation both call, so the replaced secret
        // stops working on this instance at once rather than at the end of the window.
        var (cache, _) = Create();
        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, true));

        cache.Forget("alice@weesky.be");

        Assert.False(cache.TryGet("alice@weesky.be", "fingerprint-a", out _));
    }

    [Fact]
    public void CachedIdentityCarriesTheSwitch_SoADisabledAccountIsNotServedForAMinute()
    {
        var (cache, _) = Create();

        cache.Store("alice@weesky.be", "fingerprint-a", new DavIdentity(User, false));

        Assert.True(cache.TryGet("alice@weesky.be", "fingerprint-a", out var identity));
        Assert.False(identity.CardDavEnabled);
    }

    [Fact]
    public void ShouldTouch_IsTrueOnceThenFalseUntilTheHourHasPassed()
    {
        // Without this every PROPFIND is one write to a column the screen renders as "2 hours ago".
        var (cache, clock) = Create();

        Assert.True(cache.ShouldTouch(User));
        Assert.False(cache.ShouldTouch(User));

        clock.Now = clock.Now.Add(TimeSpan.FromMinutes(59));
        Assert.False(cache.ShouldTouch(User));

        clock.Now = clock.Now.Add(TimeSpan.FromMinutes(2));
        Assert.True(cache.ShouldTouch(User));
    }

    [Fact]
    public void ShouldTouch_IsPerUser()
    {
        var (cache, _) = Create();
        var other = Guid.NewGuid();

        Assert.True(cache.ShouldTouch(User));
        Assert.True(cache.ShouldTouch(other));
    }

    private static TimeSpan SessionGuardWindow => TimeSpan.FromSeconds(60);
}
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavAuthenticationCacheTests`
Expected : ÉCHEC de compilation — `DavAuthenticationCache` n'existe pas.

- [ ] **Step 3 : Écrire l'interface**

Créer `src/snoopy.microservice/Authentication/CardDav/IDavAuthenticationCache.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Authentication.CardDav;

/// <summary>An authenticated synchronisation caller, as one lookup resolved them.</summary>
public readonly record struct DavIdentity(Guid UserId, bool CardDavEnabled);

/// <summary>
/// What one instance already knows about a synchronisation authentication: the burst cache, and
/// the amortisation of <c>last_used_at</c>. Both live in memory, per instance, and both are
/// assumed as such — shared, they would cost the read they exist to avoid; lost on redeploy, they
/// cost one extra lookup and one extra write per user.
/// </summary>
public interface IDavAuthenticationCache
{
    /// <summary>
    /// The identity resolved for this exact (identifier, secret fingerprint) pair, when it is
    /// still within the window. The fingerprint is never the clear secret, which does not survive
    /// the request.
    /// </summary>
    bool TryGet(string identifier, string fingerprint, out DavIdentity identity);

    void Store(string identifier, string fingerprint, DavIdentity identity);

    /// <summary>
    /// Drops what is known about an account, so a regenerated or revoked secret stops working on
    /// this instance at once. On the others the window is the ceiling — the same trade sessions make.
    /// </summary>
    void Forget(string identifier);

    /// <summary>
    /// True at most once an hour per account. Called on every authenticated request, so answering
    /// true every time would be one write per PROPFIND for a column the screen renders in the
    /// relative past.
    /// </summary>
    bool ShouldTouch(Guid userId);
}
```

- [ ] **Step 4 : Écrire l'implémentation**

Créer `src/snoopy.microservice/Authentication/CardDav/DavAuthenticationCache.cs` :

```csharp
using System.Collections.Concurrent;
using weesky.Snoopy.Microservice.Authentication.Services;

namespace weesky.Snoopy.Microservice.Authentication.CardDav;

/// <summary>
/// One entry per identifier rather than per (identifier, fingerprint) pair: there is exactly one
/// secret per account, so at most one fingerprint is ever useful, and that is what makes
/// <see cref="Forget"/> a single removal — an IMemoryCache cannot enumerate its keys to find an
/// account's.
/// </summary>
internal sealed class DavAuthenticationCache(TimeProvider clock) : IDavAuthenticationCache
{
    /// <summary>Modelled on the session guard's, and for the same reason. Kept equal on purpose.</summary>
    internal static readonly TimeSpan Window = SessionGuard.CacheWindow;

    internal static readonly TimeSpan TouchInterval = TimeSpan.FromHours(1);

    private readonly record struct Entry(string Fingerprint, DavIdentity Identity, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> touched = new();

    public bool TryGet(string identifier, string fingerprint, out DavIdentity identity)
    {
        identity = default;
        if (!entries.TryGetValue(identifier, out var entry)) return false;

        if (entry.ExpiresAt <= clock.GetUtcNow())
        {
            entries.TryRemove(identifier, out _);
            return false;
        }

        if (!string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal)) return false;

        identity = entry.Identity;
        return true;
    }

    public void Store(string identifier, string fingerprint, DavIdentity identity) =>
        entries[identifier] = new Entry(fingerprint, identity, clock.GetUtcNow().Add(Window));

    public void Forget(string identifier)
    {
        entries.TryRemove(identifier, out _);
    }

    public bool ShouldTouch(Guid userId)
    {
        var now = clock.GetUtcNow();
        var previous = touched.GetOrAdd(userId, DateTimeOffset.MinValue);
        if (now - previous < TouchInterval) return false;

        // A lost race writes one extra row, which is the whole cost of not locking here.
        touched[userId] = now;
        return true;
    }
}
```

- [ ] **Step 5 : L'enregistrer dans le conteneur**

Dans `Configuration/SecurityConfiguration.cs`, méthode `AddSnoopyAuthentication`, à la suite de
`services.AddMemoryCache()` :

```csharp
        // Singleton is load-bearing: both memories live in this instance's dictionaries, and a
        // shorter lifetime would forget every burst at the end of the request that started it.
        services.AddSingleton<IDavAuthenticationCache, DavAuthenticationCache>();
```

- [ ] **Step 6 : Lancer les tests pour les voir passer**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavAuthenticationCacheTests`
Expected : 8 tests PASS.

- [ ] **Step 7 : Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice/Authentication/CardDav/IDavAuthenticationCache.cs src/snoopy.microservice/Authentication/CardDav/DavAuthenticationCache.cs src/snoopy.microservice/Configuration/SecurityConfiguration.cs src/snoopy.microservice/snoopy.microservice.Tests/Authentication/DavAuthenticationCacheTests.cs
git commit -F - <<'MSG'
feat(carddav): la rafale se cache soixante secondes

Cle (identifiant, empreinte du secret), valeur l'identite resolue avec son
interrupteur ; last_used_at amorti a l'heure.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
```

---

### Task 6 : le compteur d'échecs

**Un délai n'est pas une limitation de débit**, et les deux existent pour des raisons différentes.
Le délai aléatoire (tâche 7) brouille l'oracle de temps ; il se moyenne sur assez d'échantillons et
ne borne rien pour qui ouvre mille connexions en parallèle. Ce compteur borne, et c'est aussi lui
qui rend le délai suffisant, en bornant le nombre d'échantillons qu'un attaquant peut moyenner.

**Par adresse IP *et* par identifiant, les deux** : l'un vise un compte depuis partout, l'autre
tous les comptes depuis une machine. La paire est plus fine que celle de Nextcloud, dont la clé est
le sous-réseau seul.

**Le seuil atteint répond `429` avec `Retry-After`, jamais `401`.** Un `401` dit « mot de passe
faux » : les clients l'affichent, certains décrochent le compte — et pendant une attaque sur un
identifiant, tous les appareils de sa victime diraient que leur secret est devenu mauvais. Le
revers est nommé : saturer le seuil d'un identifiant coupe sa synchronisation tant que l'attaque
dure, un déni ciblé borné par la fenêtre, préféré à l'alternative qui laisserait compter sans fin.

**Sa mémoire est bornée, avec éviction** : les clés sont des identifiants et des adresses que
l'attaquant choisit, et sans plafond le compteur serait lui-même l'épuisement de mémoire qu'une
requête non authentifiée ne doit pas pouvoir causer.

**Un succès réinitialise le compte de son identifiant** — sans quoi le vrai téléphone, qui réessaie
derrière l'attaquant, resterait dehors l'attaque finie. Il ne réinitialise **pas** celui de
l'adresse : l'adresse d'où l'attaque part n'est pas absoute par un succès qui vient d'ailleurs.

**Files :**
- Create : `src/snoopy.microservice/Authentication/CardDav/AuthAttemptThrottle.cs`
- Create : `src/snoopy.microservice/snoopy.microservice.Tests/Authentication/AuthAttemptThrottleTests.cs`

**Interfaces :**
- Consomme : `TimeProvider`.
- Produit, consommé par la tâche 7 :

```csharp
internal sealed class AuthAttemptThrottle(TimeProvider clock)
{
    internal const int MaxFailures = 10;
    internal const int MaxTrackedKeys = 10_000;
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    internal bool IsBlocked(string identifier, string? address, out TimeSpan retryAfter);
    internal void RecordFailure(string identifier, string? address);
    internal void RecordSuccess(string identifier);
}
```

`address` est celle que `ForwardedHeaders` restitue — `HttpContext.Connection.RemoteIpAddress`
**après** `UseForwardedHeaders`, jamais l'en-tête lu à la main. `null` est toléré (aucune adresse
connue) et n'entre alors dans aucune clé.

- [ ] **Step 1 : Écrire les tests, rouges**

Créer `src/snoopy.microservice/snoopy.microservice.Tests/Authentication/AuthAttemptThrottleTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication;

public sealed class AuthAttemptThrottleTests
{
    private static (AuthAttemptThrottle Throttle, MutableTimeProvider Clock) Create()
    {
        var clock = new MutableTimeProvider();
        return (new AuthAttemptThrottle(clock), clock);
    }

    [Fact]
    public void UnderTheThreshold_NothingIsBlocked()
    {
        var (throttle, _) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxFailures - 1; i++)
            throttle.RecordFailure("alice@weesky.be", "203.0.113.7");

        Assert.False(throttle.IsBlocked("alice@weesky.be", "203.0.113.7", out _));
    }

    [Fact]
    public void AtTheThreshold_TheIdentifierIsBlockedFromEverywhere()
    {
        // One account attacked from many machines: the identifier is what carries the count.
        var (throttle, _) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure("alice@weesky.be", $"203.0.113.{i}");

        Assert.True(throttle.IsBlocked("alice@weesky.be", "198.51.100.1", out var retryAfter));
        Assert.InRange(retryAfter, TimeSpan.Zero, AuthAttemptThrottle.Window);
    }

    [Fact]
    public void AtTheThreshold_TheAddressIsBlockedForEveryIdentifier()
    {
        // Many accounts attacked from one machine: the address is what carries the count.
        var (throttle, _) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure($"user{i}@weesky.be", "203.0.113.7");

        Assert.True(throttle.IsBlocked("someone-else@weesky.be", "203.0.113.7", out _));
    }

    [Fact]
    public void TheWindowSlides_SoTheBlockLiftsOnItsOwn()
    {
        var (throttle, clock) = Create();
        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure("alice@weesky.be", "203.0.113.7");

        clock.Now = clock.Now.Add(AuthAttemptThrottle.Window + TimeSpan.FromSeconds(1));

        Assert.False(throttle.IsBlocked("alice@weesky.be", "203.0.113.7", out _));
    }

    [Fact]
    public void RetryAfter_IsWhatIsLeftOfTheWindowOnTheOldestFailure()
    {
        var (throttle, clock) = Create();
        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure("alice@weesky.be", "203.0.113.7");

        clock.Now = clock.Now.Add(TimeSpan.FromMinutes(5));

        Assert.True(throttle.IsBlocked("alice@weesky.be", "203.0.113.7", out var retryAfter));
        Assert.Equal(TimeSpan.FromMinutes(10), retryAfter);
    }

    [Fact]
    public void ASuccessClearsTheIdentifier_SoTheRealPhoneGetsBackIn()
    {
        var (throttle, _) = Create();
        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure("alice@weesky.be", "203.0.113.7");

        throttle.RecordSuccess("alice@weesky.be");

        Assert.False(throttle.IsBlocked("alice@weesky.be", "198.51.100.1", out _));
    }

    [Fact]
    public void ASuccessDoesNotAbsolveTheAddressItCameFrom()
    {
        var (throttle, _) = Create();
        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure($"user{i}@weesky.be", "203.0.113.7");

        throttle.RecordSuccess("user0@weesky.be");

        Assert.True(throttle.IsBlocked("user0@weesky.be", "203.0.113.7", out _));
    }

    [Fact]
    public void AnUnknownAddress_NeverEntersAKey()
    {
        var (throttle, _) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure($"user{i}@weesky.be", null);

        Assert.False(throttle.IsBlocked("someone-else@weesky.be", null, out _));
    }

    [Fact]
    public void TheMemoryIsBounded_SoAnAttackerCannotGrowIt()
    {
        // The keys are values the attacker chooses. Without a ceiling the counter is itself the
        // memory exhaustion an unauthenticated request must not be able to cause.
        var (throttle, _) = Create();

        for (var i = 0; i < AuthAttemptThrottle.MaxTrackedKeys * 2; i++)
            throttle.RecordFailure($"user{i}@weesky.be", null);

        Assert.InRange(throttle.TrackedKeys, 0, AuthAttemptThrottle.MaxTrackedKeys);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~AuthAttemptThrottleTests`
Expected : ÉCHEC de compilation — `AuthAttemptThrottle` n'existe pas.

- [ ] **Step 3 : Écrire le compteur**

Créer `src/snoopy.microservice/Authentication/CardDav/AuthAttemptThrottle.cs` :

```csharp
using System.Collections.Concurrent;

namespace weesky.Snoopy.Microservice.Authentication.CardDav;

/// <summary>
/// Bounds password guessing on the synchronisation edge. The random delay after a failure blurs
/// the timing oracle; it does not bound anything for someone opening a thousand connections at
/// once. This does — and by bounding the number of samples an attacker can average, it is also
/// what makes that delay sufficient.
///
/// Two key spaces, both counted: the identifier, for one account attacked from everywhere, and the
/// address, for every account attacked from one machine. The address is the one ForwardedHeaders
/// restored — never the raw header, which forges freely.
///
/// In memory, per instance: the effective threshold multiplies by the number of instances, the
/// same trade the burst cache makes and assumed for the same reason.
/// </summary>
internal sealed class AuthAttemptThrottle(TimeProvider clock)
{
    internal const int MaxFailures = 10;

    /// <summary>The keys are values the attacker chooses, so their number is capped.</summary>
    internal const int MaxTrackedKeys = 10_000;

    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> failures = new(StringComparer.Ordinal);

    internal int TrackedKeys => failures.Count;

    internal bool IsBlocked(string identifier, string? address, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        var now = clock.GetUtcNow();

        foreach (var key in Keys(identifier, address))
        {
            if (!failures.TryGetValue(key, out var stamps)) continue;

            DateTimeOffset oldest;
            int count;
            lock (stamps)
            {
                Prune(stamps, now);
                count = stamps.Count;
                oldest = count == 0 ? now : stamps.Peek();
            }

            if (count < MaxFailures) continue;

            // What is left of the window on the oldest failure still counted: once it falls out,
            // the key is under the threshold again.
            var left = Window - (now - oldest);
            if (left > retryAfter) retryAfter = left;
        }

        return retryAfter > TimeSpan.Zero;
    }

    internal void RecordFailure(string identifier, string? address)
    {
        var now = clock.GetUtcNow();
        EvictIfFull(now);

        foreach (var key in Keys(identifier, address))
        {
            var stamps = failures.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
            lock (stamps)
            {
                Prune(stamps, now);
                stamps.Enqueue(now);
            }
        }
    }

    /// <summary>
    /// Clears the identifier's count, and only it: the real phone retrying behind an attacker must
    /// get back in, while the address the attack came from is not absolved by a success elsewhere.
    /// </summary>
    internal void RecordSuccess(string identifier) => failures.TryRemove(IdentifierKey(identifier), out _);

    private static IEnumerable<string> Keys(string identifier, string? address)
    {
        yield return IdentifierKey(identifier);
        if (!string.IsNullOrWhiteSpace(address)) yield return $"ip:{address}";
    }

    private static string IdentifierKey(string identifier) => $"id:{identifier.Trim().ToLowerInvariant()}";

    private static void Prune(Queue<DateTimeOffset> stamps, DateTimeOffset now)
    {
        while (stamps.Count > 0 && now - stamps.Peek() >= Window) stamps.Dequeue();
    }

    /// <summary>
    /// Drops the keys whose newest failure is oldest. Expired keys go first — they cost nothing to
    /// lose — and only if that is not enough does a live key go, which under-counts one attacker
    /// rather than growing without bound.
    /// </summary>
    private void EvictIfFull(DateTimeOffset now)
    {
        if (failures.Count < MaxTrackedKeys) return;

        foreach (var (key, stamps) in failures)
        {
            bool empty;
            lock (stamps)
            {
                Prune(stamps, now);
                empty = stamps.Count == 0;
            }
            if (empty) failures.TryRemove(key, out _);
        }

        if (failures.Count < MaxTrackedKeys) return;

        var oldest = failures
            .Select(pair => (pair.Key, Newest: Newest(pair.Value)))
            .OrderBy(pair => pair.Newest)
            .Take(failures.Count - MaxTrackedKeys + 1)
            .Select(pair => pair.Key);
        foreach (var key in oldest) failures.TryRemove(key, out _);
    }

    private static DateTimeOffset Newest(Queue<DateTimeOffset> stamps)
    {
        lock (stamps) return stamps.Count == 0 ? DateTimeOffset.MinValue : stamps.Max();
    }
}
```

- [ ] **Step 4 : L'enregistrer dans le conteneur**

Dans `Configuration/SecurityConfiguration.cs`, `AddSnoopyAuthentication`, sous l'enregistrement du
cache :

```csharp
        services.AddSingleton<AuthAttemptThrottle>();
```

- [ ] **Step 5 : Lancer les tests pour les voir passer**

Run : `cd src && dotnet test --filter FullyQualifiedName~AuthAttemptThrottleTests`
Expected : 9 tests PASS.

- [ ] **Step 6 : Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice/Authentication/CardDav/AuthAttemptThrottle.cs src/snoopy.microservice/Configuration/SecurityConfiguration.cs src/snoopy.microservice/snoopy.microservice.Tests/Authentication/AuthAttemptThrottleTests.cs
git commit -F - <<'MSG'
feat(carddav): les echecs se comptent par IP et par identifiant

Fenetre glissante de quinze minutes, memoire bornee avec eviction, un succes
reinitialise son identifiant.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
```

---

### Task 7 : le schéma d'authentification `CardDav`

La tâche la plus dense de la tranche, et celle où l'ordre des contrôles est la spécification.

**L'ordre, et pourquoi il est celui-là :**

| # | Contrôle | Réponse si refus | Lit la table ? |
|---|---|---|---|
| 1 | Aucun en-tête `Authorization: Basic` | délègue au JWT ; s'il ne rend rien → `401` + défi Basic | non |
| 2 | En-tête Basic illisible | `401` + défi Basic | non |
| 3 | Origine non-`https` (hors Development) | `403` **nu** | **non** |
| 4 | Seuil d'échecs atteint | `429` + `Retry-After` | **non** |
| 5 | Utilisateur inconnu du webmail | `401` | oui |
| 6 | `IAccountInfoProvider.IsUsableAsync` faux | `401` | oui |
| 7 | Aucune ligne `dav_credentials` | `401` | oui |
| 8 | Condensat en désaccord | `401` | oui |
| 9 | `carddav_enabled = 0` | `403` **nu** | oui, la même ligne |

**Le `403` de l'interrupteur ne vient qu'après le 8.** L'ordre inverse — voir `carddav_enabled = 0`
et répondre tout de suite — serait plus rapide et ferait de la réponse un oracle : `403` sur un
compte qui existe et dont le DAV dort, `401` sur tout le reste, c'est-à-dire l'énumération de
comptes. La lecture est la même dans les deux cas, `enabled` et `secret_hash` étant sur la ligne
qu'on charge de toute façon.

**Un `secret_hash` corrompu ne se distingue pas d'un mauvais secret** au point d'appel de
`DavSecret.Matches`, qui ne peut rien journaliser par contrainte. Le handler, lui, le peut sans
nommer aucun secret : `record.SecretHash.Length != 64` est une faute de stockage, et elle se
journalise sur le seul GUID. Le contrôle 8 répond `401` dans les deux cas ; la ligne de journal,
elle, dit laquelle des deux c'était.

**Les contrôles 3 et 4 ne lisent rien**, et le test l'asserte sur le dépôt (`Verify(..., Times.Never)`),
pas sur le code de retour : rien n'est comparé à un secret déjà compromis par son transport, et une
requête au-delà du seuil ne doit rien coûter.

**L'origine se lit sur `Request.Scheme`**, que `UseForwardedHeaders` a déjà corrigé depuis
`X-Forwarded-Proto` — jamais sur `Request.IsHttps`, que Kestrel voit toujours à `false` derrière le
proxy, ni sur l'en-tête lu à la main. Le contrôle est levé **sur l'environnement de développement,
et à ce seul endroit**.

**Le défi est Basic, et Basic seul.** `AuthorizationExtension` pose JwtBearer en
`DefaultChallengeScheme` ; une politique nommant les deux schémas ferait émettre
`WWW-Authenticate: Bearer` **avant** `Basic`. La politique ne nomme donc que `CardDav`, et c'est le
handler qui, en l'absence d'en-tête Basic, **délègue** l'authentification au JWT
(`Context.AuthenticateAsync("Bearer")`). Les deux schémas authentifient, un seul défie.

**Un échec coûte un délai aléatoire avant la réponse** — 500 à 1500 ms, le modèle de Radicale —
posé par `await Task.Delay`, **jamais** `Thread.Sleep` : un délai bloquant ferait du ralentisseur un
épuisement de pool, c'est-à-dire l'attaque qu'il devait rendre inutile. Aucun verrou, aucune
connexion base ouverte pendant l'attente. Les refus 3 et 4 n'en paient pas : ils ne révèlent rien
de la table.

**Files :**
- Create : `src/snoopy.microservice/Authentication/CardDav/CardDavAuthenticationDefaults.cs`
- Create : `src/snoopy.microservice/Authentication/CardDav/CardDavAuthenticationOptions.cs`
- Create : `src/snoopy.microservice/Authentication/CardDav/CardDavAuthenticationHandler.cs`
- Modify : `src/snoopy.microservice/Configuration/SecurityConfiguration.cs` (`AddSnoopyAuthentication`)
- Create : `src/snoopy.microservice/snoopy.microservice.Tests/Authentication/CardDavAuthenticationHandlerTests.cs`

**Interfaces :**
- Consomme : `IDavCredentialStore` (tâche 4), `IDavAuthenticationCache` (tâche 5),
  `AuthAttemptThrottle` (tâche 6), `IWebmailUserStore.FindByEmailAsync`,
  `IAccountInfoProvider.IsUsableAsync`, `WebmailClaimTypes.Uid`.
- Produit, consommé par les tâches 8 à 10 et par **4c-ii** :

```csharp
public static class CardDavAuthenticationDefaults
{
    public const string AuthenticationScheme = "CardDav";
    public const string Realm = "weesky CardDAV";
    public const string PolicyName = "Dav";
}
```

Le principal construit porte exactement les mêmes claims que le JWT — `ClaimTypes.Upn` (partie
locale), `ClaimTypes.Dns` (domaine), `WebmailClaimTypes.Uid` (le GUID) — pour que
`ControllerBaseExtensions.GetUser` et `AuthenticatedUser` fonctionnent sans savoir par quelle porte
l'appelant est entré. Il ne porte **pas** de `Stamp` : le secret n'est pas une session et n'en porte
pas l'empreinte (décision 2).

- [ ] **Step 1 : Écrire les constantes et les options**

Créer `src/snoopy.microservice/Authentication/CardDav/CardDavAuthenticationDefaults.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Authentication.CardDav;

public static class CardDavAuthenticationDefaults
{
    public const string AuthenticationScheme = "CardDav";

    /// <summary>
    /// A Basic challenge without a realm makes Thunderbird re-ask for credentials at every launch,
    /// and the realm is a keychain key on the client side: this string must never vary between
    /// deployments.
    /// </summary>
    public const string Realm = "weesky CardDAV";

    /// <summary>
    /// The named policy the /dav routes carry (slice 4c-ii). It names this scheme alone, so the
    /// challenge is Basic and only Basic; the JWT still authenticates, through the handler.
    /// </summary>
    public const string PolicyName = "Dav";
}
```

Créer `src/snoopy.microservice/Authentication/CardDav/CardDavAuthenticationOptions.cs` :

```csharp
using Microsoft.AspNetCore.Authentication;

namespace weesky.Snoopy.Microservice.Authentication.CardDav;

/// <summary>No option of its own: everything this scheme needs is injected or configured elsewhere.</summary>
public sealed class CardDavAuthenticationOptions : AuthenticationSchemeOptions;
```

- [ ] **Step 2 : Écrire les tests du handler, rouges**

Créer `src/snoopy.microservice/snoopy.microservice.Tests/Authentication/CardDavAuthenticationHandlerTests.cs`.
Le harnais monte le handler à la main : c'est ce qui rend chaque branche testable sans serveur.

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using weesky.Snoopy.Microservice.Authentication;
using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication;

public sealed class CardDavAuthenticationHandlerTests
{
    private const string Email = "alice@weesky.be";
    private const string Secret = "ABCDEFGHIJKLMNOPQRST";
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly byte[] Salt = [.. Enumerable.Range(0, 16).Select(i => (byte)i)];

    private readonly Mock<IDavCredentialStore> credentials = new();
    private readonly Mock<IWebmailUserStore> users = new();
    private readonly Mock<IAccountInfoProvider> accounts = new();
    private readonly Mock<IAuthenticationService> jwt = new();
    private readonly MutableTimeProvider clock = new();
    private readonly AuthAttemptThrottle throttle;
    private readonly DavAuthenticationCache cache;

    public CardDavAuthenticationHandlerTests()
    {
        throttle = new AuthAttemptThrottle(clock);
        cache = new DavAuthenticationCache(clock);
        users.Setup(s => s.FindByEmailAsync(Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebmailAccount(UserId, Guid.NewGuid()));
        accounts.Setup(s => s.IsUsableAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        credentials.Setup(s => s.FindAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialRecord(true, DavSecretHash(Secret), Salt));
        jwt.Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string>()))
            .ReturnsAsync(AuthenticateResult.NoResult());
    }

    private static string DavSecretHash(string secret) =>
        weesky.Snoopy.Microservice.Services.DavSecret.Hash(Salt, secret);

    private static string Basic(string user, string secret) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{secret}"));

    private async Task<(AuthenticateResult Result, DefaultHttpContext Context)> AuthenticateAsync(
        string? authorization, string scheme = "https", string environment = Environments.Production,
        string? remoteIp = "203.0.113.7")
    {
        var services = new ServiceCollection();
        services.AddSingleton(jwt.Object);
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Scheme = scheme;
        if (authorization is not null) context.Request.Headers.Authorization = authorization;
        if (remoteIp is not null) context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp);
        context.Response.Body = new MemoryStream();

        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(environment);

        var handler = new CardDavAuthenticationHandler(
            new OptionsMonitorStub(), NullLoggerFactory.Instance, UrlEncoder.Default,
            credentials.Object, users.Object, accounts.Object, cache, throttle, clock, env.Object,
            NullLogger<CardDavAuthenticationHandler>.Instance);

        await handler.InitializeAsync(
            new AuthenticationScheme(CardDavAuthenticationDefaults.AuthenticationScheme, null,
                typeof(CardDavAuthenticationHandler)),
            context);

        var result = await handler.AuthenticateAsync();
        if (!result.Succeeded) await handler.ChallengeAsync(null);

        return (result, context);
    }

    private sealed class OptionsMonitorStub : IOptionsMonitor<CardDavAuthenticationOptions>
    {
        public CardDavAuthenticationOptions CurrentValue { get; } = new();
        public CardDavAuthenticationOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<CardDavAuthenticationOptions, string?> listener) => null;
    }

    [Fact]
    public async Task AValidSecret_AuthenticatesWithTheSameClaimsAsTheJwt()
    {
        var (result, _) = await AuthenticateAsync(Basic(Email, Secret));

        Assert.True(result.Succeeded);
        var claims = result.Principal!.Claims.ToList();
        Assert.Equal("alice", claims.Single(c => c.Type == ClaimTypes.Upn).Value);
        Assert.Equal("weesky.be", claims.Single(c => c.Type == ClaimTypes.Dns).Value);
        Assert.Equal(UserId.ToString(), claims.Single(c => c.Type == WebmailClaimTypes.Uid).Value);
        // Never a session stamp: the secret is not a session and carries none (décision 2).
        Assert.DoesNotContain(claims, c => c.Type == WebmailClaimTypes.Stamp);
    }

    [Fact]
    public async Task ASecretWithEdgeWhitespace_IsAccepted()
    {
        var (result, _) = await AuthenticateAsync(Basic(Email, $" {Secret} "));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AWrongSecret_Is401WithABasicChallengeAndNoBearerOne()
    {
        var (result, context) = await AuthenticateAsync(Basic(Email, "WRONGWRONGWRONGWRONG"));

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        var challenge = Assert.Single(context.Response.Headers.WWWAuthenticate!);
        Assert.Equal($"Basic realm=\"{CardDavAuthenticationDefaults.Realm}\"", challenge);
        // The realm is a keychain key on the client: it must never vary between deployments.
        Assert.DoesNotContain("Bearer", challenge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUnknownAccount_Is401()
    {
        users.Setup(s => s.FindByEmailAsync("ghost@weesky.be", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WebmailAccount?)null);

        var (result, context) = await AuthenticateAsync(Basic("ghost@weesky.be", Secret));

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task AnotherAccountsSecret_Is401()
    {
        // Per-row salt, so the same string presented under another identifier never matches: the
        // digest of user B is not the digest of user A even for one and the same secret.
        var otherSalt = new byte[16];
        Array.Fill(otherSalt, (byte)9);
        credentials.Setup(s => s.FindAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialRecord(
                true, weesky.Snoopy.Microservice.Services.DavSecret.Hash(otherSalt, Secret), Salt));

        var (result, context) = await AuthenticateAsync(Basic(Email, Secret));

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task AReplacedSecret_Is401()
    {
        // What a regeneration must produce at the edge, and the reason Forget exists: the previous
        // secret stops working rather than living out the cache window on this instance.
        var (first, _) = await AuthenticateAsync(Basic(Email, Secret));
        Assert.True(first.Succeeded);
        cache.Forget(Email);
        credentials.Setup(s => s.FindAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialRecord(
                true, weesky.Snoopy.Microservice.Services.DavSecret.Hash(Salt, "TSRQPONMLKJIHGFEDCBA"), Salt));

        var (result, context) = await AuthenticateAsync(Basic(Email, Secret));

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task AnAccountTheMailServerNoLongerHolds_Is401()
    {
        // The address book must not be the last open door of a closed account.
        accounts.Setup(s => s.IsUsableAsync(Email, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var (result, context) = await AuthenticateAsync(Basic(Email, Secret));

        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task AnAccountThatNeverEnabled_Is401()
    {
        credentials.Setup(s => s.FindAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DavCredentialRecord?)null);

        var (result, context) = await AuthenticateAsync(Basic(Email, Secret));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task SwitchedOff_IsForbiddenOnAGoodSecret_AndUnauthorizedOnABadOne()
    {
        // The pair, because it is the pair that attests the order of décision 2 and closes the
        // account-enumeration oracle: 403 is only ever visible to whoever already holds the secret.
        credentials.Setup(s => s.FindAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialRecord(false, DavSecretHash(Secret), Salt));

        var (_, good) = await AuthenticateAsync(Basic(Email, Secret));
        Assert.Equal(StatusCodes.Status403Forbidden, good.Response.StatusCode);
        Assert.Empty(good.Response.Headers.WWWAuthenticate!);

        var (_, bad) = await AuthenticateAsync(Basic(Email, "WRONGWRONGWRONGWRONG"));
        Assert.Equal(StatusCodes.Status401Unauthorized, bad.Response.StatusCode);
    }

    [Fact]
    public async Task PlainHttp_Is403AndNeverReadsTheTable()
    {
        var (_, context) = await AuthenticateAsync(Basic(Email, Secret), scheme: "http");

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        // Asserted on the store, not on the status: nothing is ever compared to a secret its own
        // transport already gave away.
        credentials.Verify(s => s.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PlainHttpInDevelopment_IsAllowed()
    {
        var (result, _) = await AuthenticateAsync(
            Basic(Email, Secret), scheme: "http", environment: Environments.Development);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task PastTheThreshold_Is429WithRetryAfterAndNeverReadsTheTable()
    {
        for (var i = 0; i < AuthAttemptThrottle.MaxFailures; i++)
            throttle.RecordFailure(Email, "203.0.113.7");
        credentials.Invocations.Clear();

        var (_, context) = await AuthenticateAsync(Basic(Email, Secret));

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.NotEmpty(context.Response.Headers.RetryAfter!);
        // Never 401: during an attack on one identifier, every device of the victim would be told
        // its secret went bad.
        Assert.Empty(context.Response.Headers.WWWAuthenticate!);
        credentials.Verify(s => s.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ASuccessClearsTheIdentifiersFailures()
    {
        for (var i = 0; i < AuthAttemptThrottle.MaxFailures - 1; i++)
            throttle.RecordFailure(Email, "203.0.113.7");

        await AuthenticateAsync(Basic(Email, Secret));
        for (var i = 0; i < AuthAttemptThrottle.MaxFailures - 1; i++)
            throttle.RecordFailure(Email, "198.51.100.4");

        Assert.False(throttle.IsBlocked(Email, "198.51.100.4", out _));
    }

    [Fact]
    public async Task AFailureIsDelayed_AndNeverBlocksAThread()
    {
        var started = DateTime.UtcNow;

        await AuthenticateAsync(Basic(Email, "WRONGWRONGWRONGWRONG"));

        // The floor of the random window; the ceiling is not asserted, a loaded runner may exceed it.
        Assert.True(DateTime.UtcNow - started >= TimeSpan.FromMilliseconds(450));
    }

    [Fact]
    public async Task NoAuthorizationHeader_DelegatesToTheJwtAndStillChallengesBasic()
    {
        var (result, context) = await AuthenticateAsync(authorization: null);

        jwt.Verify(s => s.AuthenticateAsync(It.IsAny<HttpContext>(),
            JwtBearerDefaults.AuthenticationScheme), Times.Once);
        Assert.False(result.Succeeded);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Contains("Basic", Assert.Single(context.Response.Headers.WWWAuthenticate!)!);
    }

    [Fact]
    public async Task AValidJwt_IsAcceptedOnThisSchemeToo()
    {
        // What keeps the whole /dav surface testable from an ordinary webmail session, with no
        // secret generated at all.
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Upn, "alice"), new Claim(ClaimTypes.Dns, "weesky.be")], "Bearer"));
        jwt.Setup(s => s.AuthenticateAsync(It.IsAny<HttpContext>(), JwtBearerDefaults.AuthenticationScheme))
            .ReturnsAsync(AuthenticateResult.Success(new AuthenticationTicket(principal, "Bearer")));

        var (result, _) = await AuthenticateAsync(authorization: null);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AMalformedBasicHeader_Is401WithoutReadingTheTable()
    {
        var (_, context) = await AuthenticateAsync("Basic not-base64!!");

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        credentials.Verify(s => s.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ABurstReadsTheTableOnce()
    {
        await AuthenticateAsync(Basic(Email, Secret));
        await AuthenticateAsync(Basic(Email, Secret));
        await AuthenticateAsync(Basic(Email, Secret));

        credentials.Verify(s => s.FindAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LastUsedIsWrittenOncePerHour()
    {
        await AuthenticateAsync(Basic(Email, Secret));
        clock.Now = clock.Now.AddMinutes(2);
        await AuthenticateAsync(Basic(Email, Secret));

        credentials.Verify(s => s.TouchAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);

        clock.Now = clock.Now.AddHours(2);
        await AuthenticateAsync(Basic(Email, Secret));

        credentials.Verify(s => s.TouchAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
```

- [ ] **Step 3 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~CardDavAuthenticationHandlerTests`
Expected : ÉCHEC de compilation — `CardDavAuthenticationHandler` n'existe pas.

- [ ] **Step 4 : Écrire le handler**

Créer `src/snoopy.microservice/Authentication/CardDav/CardDavAuthenticationHandler.cs` :

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Authentication.CardDav;

/// <summary>
/// Basic over TLS, carrying the synchronisation secret. The order of its checks is the
/// specification, not an implementation detail — see the slice's design note:
///
/// transport, then throttle, both without reading anything; then the account, its usability, its
/// row and its digest; and only then the switch. Answering 403 on a switched-off account before
/// comparing the digest would make the response an account-enumeration oracle.
/// </summary>
internal sealed class CardDavAuthenticationHandler(
    IOptionsMonitor<CardDavAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IDavCredentialStore credentials,
    IWebmailUserStore users,
    IAccountInfoProvider accounts,
    IDavAuthenticationCache cache,
    AuthAttemptThrottle throttle,
    TimeProvider clock,
    IHostEnvironment environment,
    ILogger<CardDavAuthenticationHandler> log)
    : AuthenticationHandler<CardDavAuthenticationOptions>(options, loggerFactory, encoder)
{
    private const string OutcomeKey = "carddav-auth-outcome";
    private const string RetryAfterKey = "carddav-auth-retry-after";

    private enum Outcome { Unauthorized, Forbidden, TooManyRequests }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!TryReadBasic(out var identifier, out var secret))
        {
            // No Basic header at all: the JWT is a first-class scheme on this surface, which is
            // what keeps /dav testable from an ordinary webmail session. A malformed one is not
            // delegated — it is an attempt, and it answers as one.
            return HasAuthorizationHeader()
                ? Refuse(Outcome.Unauthorized)
                : await Context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        }

        // Basic carries the secret in clear: outside TLS one PROPFIND hands it to whoever listens,
        // and a secret opening the whole address book does not replay once, it replays until it is
        // revoked. Read off Request.Scheme, which UseForwardedHeaders has already corrected from
        // X-Forwarded-Proto — Request.IsHttps is always false behind the proxy.
        if (!Request.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !environment.IsDevelopment())
        {
            log.LogWarning("CardDAV authentication refused: request origin is not https");
            return Refuse(Outcome.Forbidden);
        }

        var address = Context.Connection.RemoteIpAddress?.ToString();
        if (throttle.IsBlocked(identifier, address, out var retryAfter))
        {
            Context.Items[RetryAfterKey] = retryAfter;
            return Refuse(Outcome.TooManyRequests);
        }

        var canonical = identifier.Trim().ToLowerInvariant();
        var fingerprint = DavSecret.Fingerprint(secret);

        if (cache.TryGet(canonical, fingerprint, out var cached))
            return await FinishAsync(canonical, fingerprint, cached, cachedHit: true);

        var account = await users.FindByEmailAsync(canonical, Context.RequestAborted);
        if (account is null) return await RefuseWithDelayAsync(canonical, address);

        // The same check the JWT path runs through ISessionGuard: a deleted or disabled account
        // must not keep synchronising, and forgetting it would make the address book the last open
        // door of a closed account. The security stamp does not apply — a secret is not a session.
        if (!await accounts.IsUsableAsync(canonical, Context.RequestAborted))
            return await RefuseWithDelayAsync(canonical, address);

        var row = await credentials.FindAsync(account.Value.Id, Context.RequestAborted);
        if (row is null) return await RefuseWithDelayAsync(canonical, address);

        if (!DavSecret.Matches(row.Value.Salt, row.Value.SecretHash, secret))
            return await RefuseWithDelayAsync(canonical, address);

        var identity = new DavIdentity(account.Value.Id, row.Value.CardDavEnabled);
        return await FinishAsync(canonical, fingerprint, identity, cachedHit: false);
    }

    /// <summary>
    /// Three responses out of one override: the framework routes every failed authentication here,
    /// forbidden and throttled included, and the marker says which one this was.
    /// </summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties? properties)
    {
        var outcome = Context.Items.TryGetValue(OutcomeKey, out var stored) && stored is Outcome value
            ? value
            : Outcome.Unauthorized;

        switch (outcome)
        {
            case Outcome.Forbidden:
                // No named precondition and no challenge: these two refusals precede the protocol.
                Response.StatusCode = StatusCodes.Status403Forbidden;
                break;

            case Outcome.TooManyRequests:
                Response.StatusCode = StatusCodes.Status429TooManyRequests;
                var retryAfter = Context.Items.TryGetValue(RetryAfterKey, out var left) && left is TimeSpan span
                    ? span
                    : AuthAttemptThrottle.Window;
                Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                break;

            default:
                Response.StatusCode = StatusCodes.Status401Unauthorized;
                // Without this header a client has no reason to send credentials and loops on the
                // failure. The realm never varies: it is a keychain key on the client side.
                Response.Headers.WWWAuthenticate = $"Basic realm=\"{CardDavAuthenticationDefaults.Realm}\"";
                break;
        }

        return Task.CompletedTask;
    }

    private bool HasAuthorizationHeader() => !string.IsNullOrEmpty(Request.Headers.Authorization);

    private bool TryReadBasic(out string identifier, out string secret)
    {
        identifier = string.Empty;
        secret = string.Empty;

        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)) return false;

        Span<byte> decoded = new byte[header.Length];
        if (!Convert.TryFromBase64String(header["Basic ".Length..].Trim(), decoded, out var written)) return false;

        var pair = Encoding.UTF8.GetString(decoded[..written]);
        var separator = pair.IndexOf(':');
        if (separator <= 0) return false;

        identifier = pair[..separator];
        secret = pair[(separator + 1)..];
        return identifier.Length > 0 && secret.Length > 0;
    }

    private AuthenticateResult Refuse(Outcome outcome)
    {
        Context.Items[OutcomeKey] = outcome;
        return AuthenticateResult.Fail(outcome.ToString());
    }

    /// <summary>
    /// The random delay Radicale applies, for the two signals a bare response time gives away: the
    /// existence of the account, and the cost of guessing. Task.Delay and never Thread.Sleep — a
    /// blocking wait would turn the speed bump into the pool exhaustion it exists to prevent — and
    /// no lock, no open connection is held across it.
    /// </summary>
    private async Task<AuthenticateResult> RefuseWithDelayAsync(string identifier, string? address)
    {
        throttle.RecordFailure(identifier, address);
        await Task.Delay(RandomNumberGenerator.GetInt32(500, 1501), Context.RequestAborted);
        return Refuse(Outcome.Unauthorized);
    }

    private async Task<AuthenticateResult> FinishAsync(
        string identifier, string fingerprint, DavIdentity identity, bool cachedHit)
    {
        if (!cachedHit) cache.Store(identifier, fingerprint, identity);
        throttle.RecordSuccess(identifier);

        // After the digest matched and never before: a 403 answered earlier would say "this
        // account exists and its DAV is asleep" to anyone asking.
        if (!identity.CardDavEnabled) return Refuse(Outcome.Forbidden);

        if (cache.ShouldTouch(identity.UserId))
            await credentials.TouchAsync(identity.UserId, clock.GetUtcNow().UtcDateTime, Context.RequestAborted);

        var separator = identifier.LastIndexOf('@');
        var claims = new List<Claim>
        {
            new(ClaimTypes.Upn, identifier[..separator]),
            new(ClaimTypes.Dns, identifier[(separator + 1)..]),
            new(WebmailClaimTypes.Uid, identity.UserId.ToString())
        };

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CardDavAuthenticationDefaults.AuthenticationScheme));

        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
```

- [ ] **Step 5 : Enregistrer le schéma et la politique**

Dans `Configuration/SecurityConfiguration.cs`, `AddSnoopyAuthentication`, après
`services.AddJwtBearerAuthentication(cookiesSupport: true)` :

```csharp
        // Basic over TLS, carrying the synchronisation secret. Registered as a scheme of its own
        // so it is never the default: a secret opens /dav and nothing else.
        services.AddAuthentication()
            .AddScheme<CardDavAuthenticationOptions, CardDavAuthenticationHandler>(
                CardDavAuthenticationDefaults.AuthenticationScheme, _ => { });
```

et, dans le `services.AddAuthorization(...)` du même bloc, à la suite de la politique `Admin` :

```csharp
            // Names this scheme alone, so the challenge is Basic and only Basic: a policy naming
            // both would emit WWW-Authenticate: Bearer first, and the handler already delegates to
            // the JWT when no Basic header is present. Declared here rather than in slice 4c-ii so
            // the challenge shape is settled once, in the tranche that owns it.
            options.AddPolicy(CardDavAuthenticationDefaults.PolicyName, policy => policy
                .AddAuthenticationSchemes(CardDavAuthenticationDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser());
```

- [ ] **Step 6 : Écrire le test de câblage**

Ajouter à `CardDavAuthenticationHandlerTests.cs` :

```csharp
    [Fact]
    public void TheDavPolicyChallengesBasicAndOnlyBasic()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSnoopyAuthentication()
            .BuildServiceProvider();

        var policy = services.GetRequiredService<IAuthorizationPolicyProvider>()
            .GetPolicyAsync(CardDavAuthenticationDefaults.PolicyName).GetAwaiter().GetResult();

        Assert.NotNull(policy);
        // One scheme in the policy, one challenge emitted. Adding "Bearer" here would put a Bearer
        // challenge ahead of the Basic one on every 401 of /dav.
        Assert.Equal([CardDavAuthenticationDefaults.AuthenticationScheme], policy!.AuthenticationSchemes);
    }

    [Fact]
    public void TheDefaultSchemesAreStillTheJwtOnes()
    {
        // A synchronisation secret must not open /api. The default schemes are what decides that.
        var services = new ServiceCollection().AddLogging().AddSnoopyAuthentication().BuildServiceProvider();

        var options = services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, options.DefaultAuthenticateScheme);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, options.DefaultChallengeScheme);
    }
```

(ajouter `using Microsoft.AspNetCore.Authorization;` et
`using weesky.Snoopy.Microservice.Configuration;` en tête du fichier).

**Le « secret refusé sur `/api` » de la spec est asserté sous cette forme**, et non par une requête
réelle : le dépôt n'a pas de harnais `WebApplicationFactory`, et ce qui décide qu'un en-tête Basic
n'ouvre pas `/api` est exactement le couple de schémas par défaut ci-dessus. La forme réelle
deviendra possible en 4c-ii, quand une route portera la politique ; c'est nommé ici pour qu'aucune
revue ne le lise comme une couverture manquante.

- [ ] **Step 7 : Lancer les tests pour les voir passer**

Run : `cd src && dotnet test --filter FullyQualifiedName~CardDavAuthenticationHandlerTests`
Expected : 21 tests PASS.

- [ ] **Step 8 : Vérifier que rien d'autre n'a bougé**

Run : `cd src && dotnet test`
Expected : la suite entière au vert.

- [ ] **Step 9 : Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice/Authentication/CardDav/ src/snoopy.microservice/Configuration/SecurityConfiguration.cs src/snoopy.microservice/snoopy.microservice.Tests/Authentication/CardDavAuthenticationHandlerTests.cs
git commit -F - <<'MSG'
feat(carddav): le schema CardDav authentifie en Basic sur TLS

Le condensat est compare avant l'interrupteur, le defi ne porte que Basic,
et le JWT reste accepte sur la meme surface.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
```

---

### Task 8 : une rotation du `security_stamp` révoque le secret

Trois appelants, tous des gestes de reprise de contrôle : `LoginController.LogoutEverywhere`
(« se déconnecter partout »), `AccountManagementController.ChangePassword`, et
`AdminRepository.RevokeSessionsAsync`. La révocation est voulue dans les trois cas — laisser
survivre à l'un d'eux un secret qui rend tout le carnet lisible et modifiable le viderait de son
sens, et l'utilisateur qui le fait ne devine pas qu'un second trousseau existe.

**Suppression de la ligne, et non `carddav_enabled = 0`.** La distinction de la décision 2 se lit
exactement à l'envers ici : éteindre est un geste de confort, dont l'auteur sait ce qu'il fait ; une
rotation de `security_stamp` est un geste de défiance, et ce qu'il faut détruire est le secret
lui-même.

**Dans la même transaction que la rotation** : un `SaveChangesAsync` unique, ce qu'EF enveloppe déjà
dans une transaction. Aucune transaction explicite n'est nécessaire — et aucune n'est ouverte ici,
la mécanique transactionnelle de la décision 6 appartenant à 4c-ii.

**Et le cache local est vidé**, pour que le secret révoqué cesse de fonctionner tout de suite sur
cette instance plutôt qu'au bout de la fenêtre. C'est fait dans le dépôt et non chez les trois
appelants : deux d'entre eux vivent dans le provider, et leur demander un geste de plus les
laisserait diverger.

**Files :**
- Modify : `src/snoopy.microservice/Repositories/WebmailUserStore.cs`
- Modify : `src/snoopy.microservice/Repositories/IWebmailUserStore.cs` (documentation seule)
- Modify : `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/WebmailUserStoreTests.cs`

**Interfaces :**
- Consomme : `PreferencesDbContext.DavCredentials` (tâche 2), `IDavAuthenticationCache` (tâche 5).
- Produit : aucune signature publique ne change — `RotateSecurityStampAsync` garde la sienne. Seul
  le constructeur de `WebmailUserStore` gagne un paramètre, ce que le conteneur résout seul.

- [ ] **Step 1 : Écrire les tests, rouges**

Ajouter à `snoopy.microservice.Tests/Repositories/WebmailUserStoreTests.cs` (et remplacer le
`CreateStore` existant par la surcharge ci-dessous, les tests présents continuant de l'appeler) :

```csharp
    private static readonly TimeProvider Clock = TimeProvider.System;

    private static WebmailUserStore CreateStore(string dbName, IDavAuthenticationCache? cache = null) =>
        new(new PreferencesTestDbContext(dbName), cache ?? new DavAuthenticationCache(Clock));

    [Fact]
    public async Task RotateSecurityStamp_DestroysTheSynchronisationSecret()
    {
        // A gesture of distrust destroys; switching off is the gesture of comfort, and it keeps.
        // Leaving the secret alive would make "sign out everywhere" leave the whole address book
        // readable and writable to whoever holds it.
        var db = nameof(RotateSecurityStamp_DestroysTheSynchronisationSecret);
        var account = await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);
        using (var seed = new PreferencesTestDbContext(db))
        {
            seed.DavCredentials.Add(new DavCredential
            {
                UserId = account.Id, SecretHash = new string('a', 64),
                Salt = new byte[16], CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync(CancellationToken.None);
        }

        await CreateStore(db).RotateSecurityStampAsync("mick@weesky.be", CancellationToken.None);

        using var ctx = new PreferencesTestDbContext(db);
        Assert.Empty(ctx.DavCredentials);
    }

    [Fact]
    public async Task RotateSecurityStamp_ForgetsTheCachedSynchronisationIdentity()
    {
        var db = nameof(RotateSecurityStamp_ForgetsTheCachedSynchronisationIdentity);
        var cache = new DavAuthenticationCache(Clock);
        var account = await CreateStore(db, cache).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);
        cache.Store("mick@weesky.be", "fingerprint", new DavIdentity(account.Id, true));

        await CreateStore(db, cache).RotateSecurityStampAsync("mick@weesky.be", CancellationToken.None);

        Assert.False(cache.TryGet("mick@weesky.be", "fingerprint", out _));
    }

    [Fact]
    public async Task RotateSecurityStamp_OnAnAccountWithNoSecret_StillRotates()
    {
        var db = nameof(RotateSecurityStamp_OnAnAccountWithNoSecret_StillRotates);
        var before = await CreateStore(db).RegisterLoginAsync("mick@weesky.be", CancellationToken.None);

        var rotated = await CreateStore(db).RotateSecurityStampAsync("mick@weesky.be", CancellationToken.None);

        Assert.NotEqual(before.SecurityStamp, rotated);
    }
```

Ajouter en tête du fichier :

```csharp
using weesky.Snoopy.Microservice.Authentication.CardDav;
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~WebmailUserStoreTests`
Expected : ÉCHEC de compilation — `WebmailUserStore` ne prend qu'un argument.

- [ ] **Step 3 : Modifier le dépôt**

Dans `Repositories/WebmailUserStore.cs`, remplacer la déclaration :

```csharp
internal sealed class WebmailUserStore(PreferencesDbContext context) : IWebmailUserStore
```

par :

```csharp
internal sealed class WebmailUserStore(
    PreferencesDbContext context, IDavAuthenticationCache davCache) : IWebmailUserStore
```

(et ajouter `using weesky.Snoopy.Microservice.Authentication.CardDav;`)

puis remplacer le corps de `RotateSecurityStampAsync` par :

```csharp
    public async Task<Guid> RotateSecurityStampAsync(string email, CancellationToken cancellationToken)
    {
        var canonical = Canonical(email);
        var row = await context.Users
            .FirstOrDefaultAsync(u => u.Email == canonical, cancellationToken);

        // No row means no token was ever issued for this account, so there is nothing to revoke.
        // Answering with a fresh value anyway keeps the caller's contract simple, and one that
        // matches nothing stored is refused on the next request rather than trusted.
        if (row is null) return Guid.NewGuid();

        row.SecurityStamp = Guid.NewGuid();

        // The three callers are all gestures of taking control back — sign out everywhere, change
        // your password, an administrator's reset — and a synchronisation secret surviving any of
        // them would leave the whole address book open to whoever holds it. Destroyed, not
        // switched off: switching off is the gesture of comfort, this one is distrust. One
        // SaveChanges, so it is one transaction with the rotation.
        var secret = await context.DavCredentials
            .FirstOrDefaultAsync(c => c.UserId == row.Id, cancellationToken);
        if (secret is not null) context.DavCredentials.Remove(secret);

        await context.SaveChangesAsync(cancellationToken);
        davCache.Forget(canonical);

        return row.SecurityStamp;
    }
```

- [ ] **Step 4 : Documenter la conséquence sur l'interface**

Dans `Repositories/IWebmailUserStore.cs`, remplacer le résumé de `RotateSecurityStampAsync` par :

```csharp
    /// <summary>
    /// Draws a new security stamp, which invalidates every token already issued for this account,
    /// and destroys its synchronisation secret in the same transaction — every caller of this is a
    /// gesture of taking control back, and a secret surviving one of them would leave the whole
    /// address book open. The DAV clients are to be reconfigured; the screens that trigger it say so.
    /// Returns the new stamp so the caller can re-issue its own session rather than sign itself out.
    /// </summary>
```

- [ ] **Step 5 : Lancer les tests pour les voir passer**

Run : `cd src && dotnet test`
Expected : la suite entière au vert. `LoginControllerTests`, `AccountManagementControllerTests` et
`AdminRepositoryTests` moquent `IWebmailUserStore` et ne voient donc pas le constructeur ; s'ils
rougissent, c'est un site de construction concret oublié.

- [ ] **Step 6 : Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice/Repositories/WebmailUserStore.cs src/snoopy.microservice/Repositories/IWebmailUserStore.cs src/snoopy.microservice/snoopy.microservice.Tests/Repositories/WebmailUserStoreTests.cs
git commit -F - <<'MSG'
feat(carddav): une rotation du security_stamp detruit le secret

Se deconnecter partout, changer son mot de passe et la reinitialisation admin
revoquent la synchronisation, cache local vide.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
```

---

### Task 9 : l'adresse publique et la capacité `dav`

**L'adresse du serveur vient du serveur.** Elle est rendue par l'API depuis sa configuration,
jamais composée côté navigateur : le front connaît l'URL qu'il appelle, qui n'est pas nécessairement
celle que le proxy publie, et une adresse fausse sur cet écran est une configuration client qui
échoue sans que rien n'indique où.

**C'est l'hôte nu, sans chemin** — le client le complète par `/.well-known/carddav` — **et sans
port** : certaines versions du client CardDAV d'iOS ignorent un port non standard et tentent 443
puis 80 quoi qu'on leur ait donné, ce qui fait une configuration qui échoue sur un appareil et
réussit sur l'autre, pour une raison invisible des deux côtés. La validation refuse donc les deux
au démarrage, là où un opérateur regarde, plutôt que sur un écran que personne ne relit.

**La capacité gate l'onglet et l'API ensemble.** `capabilities.dav` vaut vrai quand `Dav:PublicUrl`
est configurée. Un déploiement qui ne la pose pas n'affiche pas d'onglet — et, décision de ce plan
plutôt que de la spec, ses trois routes répondent `404` : engendrer un secret pour un serveur dont
l'adresse n'existe pas produirait un écran qui promet une synchronisation que rien ne sert.

**Files :**
- Create : `src/snoopy.microservice/Models/DavOptions.cs`
- Modify : `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs` (`AddSnoopyOptions`)
- Modify : `src/snoopy.microservice/Models/CapabilitiesResponse.cs`
- Modify : `src/snoopy.microservice/Controllers/CapabilitiesController.cs`
- Modify : `src/snoopy.microservice.host/appsettings.Development.json`
- Modify : `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CapabilitiesControllerTests.cs`
- Create : `src/snoopy.microservice/snoopy.microservice.Tests/Configuration/DavOptionsTests.cs`

**Interfaces :**
- Produit, consommé par la tâche 10 et par le front :

```csharp
public sealed class DavOptions
{
    public string? PublicUrl { get; set; }
    public bool IsConfigured { get; }
}
```

et `CapabilitiesResponse` gagne un dernier membre positionnel `bool Dav`.

- [ ] **Step 1 : Écrire les tests des options, rouges**

Créer `src/snoopy.microservice/snoopy.microservice.Tests/Configuration/DavOptionsTests.cs` :

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Configuration;

public sealed class DavOptionsTests
{
    private static DavOptions Build(string? publicUrl)
    {
        var values = new Dictionary<string, string?>();
        if (publicUrl is not null) values["Dav:PublicUrl"] = publicUrl;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var provider = new ServiceCollection().AddSnoopyOptions(configuration).BuildServiceProvider();

        return provider.GetRequiredService<IOptions<DavOptions>>().Value;
    }

    [Fact]
    public void ABareHttpsOrigin_IsAccepted()
    {
        var options = Build("https://api.mail.weesky.net");

        Assert.True(options.IsConfigured);
        Assert.Equal("https://api.mail.weesky.net", options.PublicUrl);
    }

    [Fact]
    public void NoValueAtAll_IsLegalAndMeansTheFeatureIsOff()
    {
        // A deployment that serves no /dav must not be forced to invent an address.
        Assert.False(Build(null).IsConfigured);
        Assert.False(Build("").IsConfigured);
    }

    [Theory]
    // A path would break the clients that concatenate /.well-known/carddav onto it.
    [InlineData("https://api.mail.weesky.net/dav")]
    [InlineData("https://api.mail.weesky.net/")]
    // A port is ignored by some iOS versions, which try 443 then 80 whatever they were given.
    [InlineData("https://api.mail.weesky.net:8443")]
    // Basic carries the secret in clear; an http address published here invites exactly that.
    [InlineData("http://api.mail.weesky.net")]
    [InlineData("api.mail.weesky.net")]
    public void AnythingElse_RefusesToStart(string publicUrl)
    {
        var exception = Assert.Throws<OptionsValidationException>(() => Build(publicUrl));

        Assert.Contains("Dav:PublicUrl", exception.Message, StringComparison.Ordinal);
    }
}
```

Le cas `https://api.mail.weesky.net/` est refusé volontairement : la spec dit « sans chemin », et
accepter la barre finale rendrait `https://…//.well-known/carddav` chez le premier client qui
concatène.

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavOptionsTests`
Expected : ÉCHEC de compilation — `DavOptions` n'existe pas.

- [ ] **Step 3 : Écrire les options**

Créer `src/snoopy.microservice/Models/DavOptions.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// The address a synchronisation client is told to enter. It comes from here rather than from the
/// browser: the frontend knows the URL it calls, which is not necessarily the one the proxy
/// publishes, and a wrong address on that screen is a client configuration that fails with nothing
/// saying where.
///
/// Bare origin, no path and no port. A path breaks the clients that concatenate
/// <c>/.well-known/carddav</c> onto it, and some iOS versions ignore a non-standard port and try
/// 443 then 80 anyway — a configuration that works on one device and fails on the other for a
/// reason invisible from both. Empty is legal and means this deployment serves no /dav.
/// </summary>
public sealed class DavOptions
{
    public string? PublicUrl { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(PublicUrl);

    /// <summary>Validated on start rather than on first use, where an operator is watching.</summary>
    internal static bool IsBareHttpsOrigin(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.IsDefaultPort
            && uri.PathAndQuery == "/"
            && !value.EndsWith('/'));
}
```

- [ ] **Step 4 : Lier et valider les options**

Dans `Configuration/ApplicationServicesConfiguration.cs`, `AddSnoopyOptions`, à la suite de
`TrustedSenderOptions` :

```csharp
        services.AddOptions<DavOptions>()
            .Bind(configuration.GetSection("Dav"))
            .Validate(
                options => DavOptions.IsBareHttpsOrigin(options.PublicUrl),
                "Dav:PublicUrl must be a bare https origin — no path, no trailing slash, no port " +
                "(e.g. https://api.mail.weesky.net). Clients concatenate /.well-known/carddav onto " +
                "it, and some iOS versions ignore a non-standard port. Leave it unset to serve no " +
                "synchronisation at all.")
            .ValidateOnStart();
```

- [ ] **Step 5 : Ajouter la capacité**

Dans `Models/CapabilitiesResponse.cs`, ajouter le membre en **dernière** position et compléter le
résumé :

```csharp
public sealed record CapabilitiesResponse(
    string Platform,
    bool Admin,
    bool Aliases,
    bool PasswordChange,
    bool ProfileEditing,
    bool StrictIdentities,
    bool Quota,
    bool Rules,
    bool Dav);
```

Dans `Controllers/CapabilitiesController.cs`, injecter `IOptions<DavOptions> davOptions` et
compléter la réponse :

```csharp
            Rules: rules,
            // Configured means served: a deployment with no published address has no /dav, and the
            // Sync tab must not be a dead row on its settings screen.
            Dav: davOptions.Value.IsConfigured));
```

- [ ] **Step 6 : Poser l'adresse en développement**

Dans `src/snoopy.microservice.host/appsettings.Development.json`, à la racine :

```json
  "Dav": {
    "PublicUrl": "https://api.mail.weesky.net"
  },
```

- [ ] **Step 7 : Mettre les tests de capacités à jour**

Dans `snoopy.microservice.Tests/Controllers/CapabilitiesControllerTests.cs` : ajouter
`Dav: false` (ou `true` selon la fixture) au `CapabilitiesResponse` attendu, injecter le nouveau
paramètre dans les constructions du contrôleur, et ajouter :

```csharp
    [Fact]
    public async Task Dav_FollowsWhetherAPublicAddressIsConfigured()
    {
        var withAddress = await GetCapabilitiesAsync(davPublicUrl: "https://api.mail.weesky.net");
        Assert.True(withAddress.Dav);

        var without = await GetCapabilitiesAsync(davPublicUrl: null);
        Assert.False(without.Dav);
    }
```

en ajoutant à l'aide de construction existante un paramètre optionnel `string? davPublicUrl = null`
qui monte `Options.Create(new DavOptions { PublicUrl = davPublicUrl })`.

- [ ] **Step 8 : Lancer les tests**

Run : `cd src && dotnet test`
Expected : la suite entière au vert.

- [ ] **Step 9 : Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice/Models/DavOptions.cs src/snoopy.microservice/Models/CapabilitiesResponse.cs src/snoopy.microservice/Controllers/CapabilitiesController.cs src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs src/snoopy.microservice.host/appsettings.Development.json src/snoopy.microservice/snoopy.microservice.Tests/Configuration/DavOptionsTests.cs src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CapabilitiesControllerTests.cs
git commit -F - <<'MSG'
feat(carddav): l'adresse publique vient du serveur et gate l'onglet

Hote nu valide au demarrage, sans chemin ni port ; capabilities.dav suit sa
presence.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
```

---

### Task 10 : l'API de l'écran

Trois actions, et pas une de plus. **Il n'y a jamais de « révéler »** : la table n'en porte que le
condensat, et un écran qui promettrait de réafficher le secret imposerait de le stocker en clair ou
déchiffrable, faisant de cette table un trousseau à voler.

**Files :**
- Create : `src/snoopy.microservice/Models/DavCredentialsView.cs`
- Create : `src/snoopy.microservice/Models/DavSyncToggle.cs`
- Create : `src/snoopy.microservice/Controllers/DavCredentialsController.cs`
- Create : `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/DavCredentialsControllerTests.cs`

**Interfaces :**
- Consomme : `IDavCredentialStore` (tâche 4), `IDavAuthenticationCache` (tâche 5),
  `IOptions<DavOptions>` (tâche 9), `AuthenticatedUser.Email` / `.WebmailUid`.
- Produit, consommé par le front (tâches 11 et 12) :

```
GET  /api/DavCredentials            200 DavCredentialsView (jamais de password) · 401 · 404
PUT  /api/DavCredentials/CardDav    200 DavCredentialsView (password seulement à la création) · 401 · 404
POST /api/DavCredentials/Regenerate 200 DavCredentialsView (password toujours) · 401 · 404
```

```csharp
public sealed record DavCredentialsView(
    string ServerUrl, string Username, bool Configured, bool CardDavEnabled,
    DateTime? LastUsedAt, string? Password);

public sealed record DavSyncToggle(bool Enabled);
```

`Password` est omis du JSON par `WhenWritingNull` : côté client il se déclare `password?: string`.

Le `404` couvre deux cas et un seul message : `Dav:PublicUrl` non configurée sur les trois actions,
et `Regenerate` sur un compte qui n'a jamais allumé — régénérer ce qui n'existe pas n'est pas une
création.

- [ ] **Step 1 : Écrire les tests, rouges**

Créer `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/DavCredentialsControllerTests.cs` :

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class DavCredentialsControllerTests
{
    private static readonly Guid Uid = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly Mock<IDavCredentialStore> store = new();
    private readonly Mock<IDavAuthenticationCache> cache = new();

    private DavCredentialsController CreateController(string? publicUrl = "https://api.mail.weesky.net")
    {
        var controller = new DavCredentialsController(
            store.Object, cache.Object,
            Options.Create(new DavOptions { PublicUrl = publicUrl }),
            NullLogger<DavCredentialsController>.Instance)
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be", Uid)
        };
        return controller;
    }

    private static DavCredentialsView Body(ActionResult<DavCredentialsView> result) =>
        Assert.IsType<DavCredentialsView>(Assert.IsType<OkObjectResult>(result.Result).Value);

    [Fact]
    public async Task Get_AnswersTheAddressFromConfigurationAndTheFullEmail()
    {
        // Never the host the request came in on: the frontend calls one URL, the proxy publishes
        // another, and the client is configured with the second.
        store.Setup(s => s.GetStateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialState(true, true, new DateTime(2026, 8, 23, 8, 0, 0, DateTimeKind.Utc)));

        var view = Body(await CreateController().Get(CancellationToken.None));

        Assert.Equal("https://api.mail.weesky.net", view.ServerUrl);
        Assert.Equal("alice@weesky.be", view.Username);
        Assert.True(view.Configured);
        Assert.True(view.CardDavEnabled);
        Assert.Equal(new DateTime(2026, 8, 23, 8, 0, 0, DateTimeKind.Utc), view.LastUsedAt);
    }

    [Fact]
    public async Task Get_NeverCarriesASecret()
    {
        // The assertion that keeps shut the door a "reveal" button would open.
        store.Setup(s => s.GetStateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialState(true, true, null));

        var view = Body(await CreateController().Get(CancellationToken.None));

        Assert.Null(view.Password);
    }

    [Fact]
    public async Task SetCardDav_TurningOnForTheFirstTime_AnswersTheSecretInTheSameResponse()
    {
        store.Setup(s => s.EnableAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync("ABCDEFGHIJKLMNOPQRST");
        store.Setup(s => s.GetStateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialState(true, true, null));

        var view = Body(await CreateController().SetCardDav(new DavSyncToggle(true), CancellationToken.None));

        Assert.Equal("ABCDEFGHIJKLMNOPQRST", view.Password);
        Assert.True(view.CardDavEnabled);
    }

    [Fact]
    public async Task SetCardDav_TurningOnAgain_AnswersNoSecret()
    {
        // Including the concurrent-first-enable race, which the store answers as a re-enable:
        // never a 500 on the primary key, and never a second secret handed out.
        store.Setup(s => s.EnableAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        store.Setup(s => s.GetStateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialState(true, true, null));

        var view = Body(await CreateController().SetCardDav(new DavSyncToggle(true), CancellationToken.None));

        Assert.Null(view.Password);
    }

    [Fact]
    public async Task SetCardDav_TurningOff_KeepsTheAccountConfigured()
    {
        store.Setup(s => s.GetStateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialState(true, false, null));

        var view = Body(await CreateController().SetCardDav(new DavSyncToggle(false), CancellationToken.None));

        store.Verify(s => s.DisableAsync(Uid, It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.EnableAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.True(view.Configured);
        Assert.False(view.CardDavEnabled);
        Assert.Null(view.Password);
    }

    [Fact]
    public async Task Regenerate_AnswersTheNewSecretAndForgetsTheCachedOne()
    {
        store.Setup(s => s.RegenerateAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync("TSRQPONMLKJIHGFEDCBA");
        store.Setup(s => s.GetStateAsync(Uid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DavCredentialState(true, true, null));

        var view = Body(await CreateController().Regenerate(CancellationToken.None));

        Assert.Equal("TSRQPONMLKJIHGFEDCBA", view.Password);
        cache.Verify(c => c.Forget("alice@weesky.be"), Times.Once);
    }

    [Fact]
    public async Task Regenerate_OnAnAccountThatNeverEnabled_Is404()
    {
        store.Setup(s => s.RegenerateAsync(Uid, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var result = await CreateController().Regenerate(CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task EveryAction_Is404WhenNoPublicAddressIsConfigured()
    {
        // No published address means no /dav to point a client at, and a secret generated for it
        // would promise a synchronisation nothing serves.
        var controller = CreateController(publicUrl: null);

        Assert.IsType<NotFoundObjectResult>((await controller.Get(CancellationToken.None)).Result);
        Assert.IsType<NotFoundObjectResult>(
            (await controller.SetCardDav(new DavSyncToggle(true), CancellationToken.None)).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.Regenerate(CancellationToken.None)).Result);
        store.Verify(s => s.EnableAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavCredentialsControllerTests`
Expected : ÉCHEC de compilation — `DavCredentialsController` n'existe pas.

- [ ] **Step 3 : Écrire les DTO**

Créer `src/snoopy.microservice/Models/DavCredentialsView.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// What the Sync screen shows. <see cref="Password"/> is set on the one response that draws a
/// secret — enabling for the first time, or regenerating — and is null everywhere else, so the
/// serialiser omits it: there is nothing to reveal, and never will be.
/// </summary>
public sealed record DavCredentialsView(
    string ServerUrl,
    string Username,
    bool Configured,
    bool CardDavEnabled,
    DateTime? LastUsedAt,
    string? Password);
```

Créer `src/snoopy.microservice/Models/DavSyncToggle.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models;

/// <summary>One switch per protocol; the secret behind them is shared (décision 19).</summary>
public sealed record DavSyncToggle(bool Enabled);
```

- [ ] **Step 4 : Écrire le contrôleur**

Créer `src/snoopy.microservice/Controllers/DavCredentialsController.cs` :

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Authentication.CardDav;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The Sync settings tab, and nothing else: the three values a CardDAV client asks for, one switch
/// per protocol, and a regeneration. No reveal — the table holds a digest, and a screen able to
/// show the secret again would force it to hold the secret itself.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class DavCredentialsController(
    IDavCredentialStore store,
    IDavAuthenticationCache cache,
    IOptions<DavOptions> davOptions,
    ILogger<DavCredentialsController> logger) : ApiBaseController
{
    private const string NotServed = "Synchronisation is not available on this deployment";

    /// <summary>
    /// Returns the synchronisation state
    /// </summary>
    /// <response code="200">The state — never a secret, in any shape</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">This deployment publishes no synchronisation address</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DavCredentialsView>> Get(CancellationToken cancellationToken)
    {
        if (!davOptions.Value.IsConfigured) return NotFoundEnveloppe(NotServed);

        return Ok(await ViewAsync(secret: null, cancellationToken));
    }

    /// <summary>
    /// Turns contact synchronisation on or off
    /// </summary>
    /// <remarks>
    /// Turning it on for the first time creates the credentials and returns the secret **in this
    /// same response** — the one and only moment it exists in clear. Turning it back on returns
    /// none: there is nothing new to show, and every configured device keeps working. Turning it
    /// off destroys nothing.
    /// </remarks>
    /// <response code="200">The new state, carrying the secret only when this call drew one</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">This deployment publishes no synchronisation address</response>
    [HttpPut("CardDav")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DavCredentialsView>> SetCardDav(
        DavSyncToggle toggle, CancellationToken cancellationToken)
    {
        if (!davOptions.Value.IsConfigured) return NotFoundEnveloppe(NotServed);

        string? secret = null;
        if (toggle.Enabled)
        {
            secret = await store.EnableAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        }
        else
        {
            await store.DisableAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        }

        // The cached entry carries the switch state, so it answers with the old one for the rest
        // of the window — in both directions: a 200 after switching off, a 403 after switching on.
        cache.Forget(AuthenticatedUser.Email);

        logger.LogInformation("Audit: carddav_sync user={UserId} enabled={Enabled} created={Created}",
            AuthenticatedUser.WebmailUid, toggle.Enabled, secret is not null);

        return Ok(await ViewAsync(secret, cancellationToken));
    }

    /// <summary>
    /// Draws a new synchronisation secret
    /// </summary>
    /// <remarks>
    /// Every device stops syncing until the new one is entered. The screen says so before asking.
    /// </remarks>
    /// <response code="200">The new state, carrying the new secret</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">Synchronisation was never enabled, or this deployment publishes no address</response>
    [HttpPost("Regenerate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DavCredentialsView>> Regenerate(CancellationToken cancellationToken)
    {
        if (!davOptions.Value.IsConfigured) return NotFoundEnveloppe(NotServed);

        var secret = await store.RegenerateAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        // Regenerating what was never enabled is not a create: the switch is the only door in.
        if (secret is null) return NotFoundEnveloppe("Synchronisation has never been enabled");

        cache.Forget(AuthenticatedUser.Email);
        logger.LogInformation("Audit: carddav_regenerate user={UserId}", AuthenticatedUser.WebmailUid);

        return Ok(await ViewAsync(secret, cancellationToken));
    }

    private async Task<DavCredentialsView> ViewAsync(string? secret, CancellationToken cancellationToken)
    {
        var state = await store.GetStateAsync(AuthenticatedUser.WebmailUid, cancellationToken);

        return new DavCredentialsView(
            davOptions.Value.PublicUrl!,
            AuthenticatedUser.Email,
            state.Configured,
            state.CardDavEnabled,
            state.LastUsedAt,
            secret);
    }
}
```

- [ ] **Step 5 : Lancer les tests pour les voir passer**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavCredentialsControllerTests`
Expected : 8 tests PASS.

- [ ] **Step 6 : Vérifier la suite entière et Swagger**

Run : `cd src && dotnet test`
Expected : tout au vert.

Run : `cd src/snoopy.microservice.host && dotnet run` puis ouvrir `http://localhost:5104/swagger`
Expected : `DavCredentials` y figure avec ses trois opérations.

- [ ] **Step 7 : Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice/Models/DavCredentialsView.cs src/snoopy.microservice/Models/DavSyncToggle.cs src/snoopy.microservice/Controllers/DavCredentialsController.cs src/snoopy.microservice/snoopy.microservice.Tests/Controllers/DavCredentialsControllerTests.cs
git commit -F - <<'MSG'
feat(carddav): l'ecran de synchronisation a son API

Allumer engendre et rend le secret dans la meme reponse ; l'etat lu n'en porte
jamais, et il n'y a pas de reveler.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
```

---

### Task 11 : le fil frontend — client, capacité, et les deux pièces partagées

Trois choses avant l'écran, dont deux ne lui appartiennent pas : un `ToggleRow` qui vit
aujourd'hui à l'intérieur de `GeneralPage` et qu'un deuxième écran va vouloir, et une mise en forme
relative que `src/lib/intl.ts` ne porte pas encore. Les extraire ici plutôt que dans la tâche 12
garde l'écran lisible et évite le doublon que la règle du projet interdit.

**Files :**
- Modify : `src/frontend/src/api.js`
- Modify : `src/frontend/src/types/capabilities.ts`
- Create : `src/frontend/src/types/dav.ts`
- Create : `src/frontend/src/components/ToggleRow.tsx`
- Modify : `src/frontend/src/modules/settings/general/GeneralPage.tsx`
- Modify : `src/frontend/src/lib/intl.ts`
- Modify : `src/frontend/src/lib/intl.test.ts`
- Create : `src/frontend/src/components/ToggleRow.test.tsx`

**Interfaces :**
- Produit, consommé par la tâche 12 :

```ts
// types/dav.ts
export interface DavCredentials {
  serverUrl: string
  username: string
  configured: boolean
  cardDavEnabled: boolean
  lastUsedAt?: string
  password?: string
}

// api.js
api.getDavCredentials(): Promise<DavCredentials>
api.setDavCardDav(enabled: boolean): Promise<DavCredentials>
api.regenerateDavSecret(): Promise<DavCredentials>

// lib/intl.ts
export function relativeFromNow(iso: string, now?: Date): string

// components/ToggleRow.tsx  (default export)
```

`lastUsedAt` et `password` sont **optionnels et jamais `null`** : l'API omet les champs nuls
(`WhenWritingNull`), donc côté client c'est `undefined`.

- [ ] **Step 1 : Écrire les tests de `relativeFromNow`, rouges**

Ajouter à `src/frontend/src/lib/intl.test.ts` :

```ts
import { relativeFromNow } from './intl'

describe('relativeFromNow', () => {
  const now = new Date('2026-08-23T12:00:00Z')

  it('reads in the past, in the largest unit that still says something', () => {
    expect(relativeFromNow('2026-08-23T10:00:00Z', now)).toBe('2 hours ago')
    expect(relativeFromNow('2026-08-21T12:00:00Z', now)).toBe('2 days ago')
    expect(relativeFromNow('2026-08-23T11:59:30Z', now)).toBe('30 seconds ago')
  })

  it('does not drift into the future on a clock a few seconds ahead', () => {
    // The server stamps the date, the browser reads it: a small skew must not print "in 3 seconds".
    expect(relativeFromNow('2026-08-23T12:00:03Z', now)).toBe('now')
  })
})
```

- [ ] **Step 2 : Écrire `relativeFromNow`**

Ajouter à `src/frontend/src/lib/intl.ts` :

```ts
const relativeFormats = new Map<string, Intl.RelativeTimeFormat>()

function relativeFormat(locale: string = activeLocale()): Intl.RelativeTimeFormat {
  let formatter = relativeFormats.get(locale)
  if (!formatter) {
    formatter = new Intl.RelativeTimeFormat(locale, { numeric: 'auto' })
    relativeFormats.set(locale, formatter)
  }
  return formatter
}

const RELATIVE_UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 3600], ['month', 30 * 24 * 3600], ['day', 24 * 3600],
  ['hour', 3600], ['minute', 60], ['second', 1],
]

/** A past instant, in the largest unit that still says something. The server stamps the date and
    the browser reads it, so a clock a few seconds ahead prints "now" rather than a future. */
export function relativeFromNow(iso: string, now: Date = new Date()): string {
  const seconds = Math.round((new Date(iso).getTime() - now.getTime()) / 1000)
  if (seconds > -5) return relativeFormat().format(0, 'second')

  const [unit, size] = RELATIVE_UNITS.find(([, s]) => Math.abs(seconds) >= s) ?? RELATIVE_UNITS[5]
  return relativeFormat().format(Math.round(seconds / size), unit)
}
```

- [ ] **Step 3 : Extraire `ToggleRow`**

Créer `src/frontend/src/components/ToggleRow.tsx` en **déplaçant** le composant et son type de
props hors de `GeneralPage.tsx` — commentaires compris, y compris celui qui explique pourquoi le
`hint` reste dehors du `label` — puis, dans `GeneralPage.tsx`, supprimer la définition locale et
ajouter `import ToggleRow from '../../../components/ToggleRow'`.

Aucun changement de comportement, aucune prop ajoutée : `nested`, `covered` et `locked` restent
optionnelles et ne servent qu'à `GeneralPage` pour l'instant.

Créer `src/frontend/src/components/ToggleRow.test.tsx` :

```tsx
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import ToggleRow from './ToggleRow'

describe('ToggleRow', () => {
  it('names the control with the label and leaves the hint out of that name', () => {
    render(<ToggleRow id="t" label="Contacts (CardDAV)" hint="Sync with your phone"
      checked={false} onChange={() => {}} />)

    expect(screen.getByRole('checkbox', { name: 'Contacts (CardDAV)' })).not.toBeChecked()
  })

  it('reports the new value', async () => {
    const onChange = vi.fn()
    render(<ToggleRow id="t" label="Contacts (CardDAV)" hint="" checked={false} onChange={onChange} />)

    await userEvent.click(screen.getByRole('checkbox'))

    expect(onChange).toHaveBeenCalledWith(true)
  })
})
```

- [ ] **Step 4 : Déclarer les types et le client**

Créer `src/frontend/src/types/dav.ts` :

```ts
/**
 * `GET /api/DavCredentials`. `password` is present on exactly two responses — enabling for the
 * first time, and regenerating — and never again: the backend stores a digest, so there is nothing
 * to reveal. Both optional fields are `undefined` and never `null`: the API omits null fields.
 */
export interface DavCredentials {
  serverUrl: string
  username: string
  configured: boolean
  cardDavEnabled: boolean
  lastUsedAt?: string
  password?: string
}
```

Ajouter `dav?: boolean` à `src/frontend/src/types/capabilities.ts`, avec sa ligne de commentaire :

```ts
  /** Whether this deployment publishes a synchronisation address. Read `!== false` like the rest. */
  dav?: boolean
```

Ajouter à `src/frontend/src/api.js`, à la suite du bloc `contacts` :

```js
  // The address comes from the backend's configuration, never composed here: the URL this app
  // calls is not necessarily the one the proxy publishes.
  getDavCredentials: () =>
    request('GET', '/api/DavCredentials'),

  // Turning it on for the first time answers the secret in this very response — the only moment
  // it exists in clear.
  setDavCardDav: (enabled) =>
    request('PUT', '/api/DavCredentials/CardDav', { enabled }),

  regenerateDavSecret: () =>
    request('POST', '/api/DavCredentials/Regenerate'),
```

- [ ] **Step 5 : Lancer les tests**

Run : `cd src/frontend && npm test -- intl ToggleRow GeneralPage`
Expected : tout PASS, `GeneralPage.test.tsx` compris — l'extraction ne change rien à son rendu.

Run : `cd src/frontend && npx tsc --noEmit && npm run lint`
Expected : propre.

- [ ] **Step 6 : Commit**

```bash
git add src/frontend/src/api.js src/frontend/src/types/ src/frontend/src/components/ToggleRow.tsx src/frontend/src/components/ToggleRow.test.tsx src/frontend/src/modules/settings/general/GeneralPage.tsx src/frontend/src/lib/intl.ts src/frontend/src/lib/intl.test.ts
git commit -F - <<'MSG'
feat(sync): le client, la capacite et les deux pieces partagees

ToggleRow sort de GeneralPage, intl gagne une mise en forme relative.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
```

---

### Task 12 : l'onglet « Sync »

L'écran de la décision 19, et le seul de la tranche.

```
┌──────────────────────────────────────────────────────┐
│ Sync                                                 │
├──────────────────────────────────────────────────────┤
│                                                      │
│  Contacts (CardDAV)                    [ ●━] Enabled │
│  Sync your address book with your phone or           │
│  Thunderbird. Turning this off stops every device;   │
│  your password is kept.                              │
│                                                      │
│  ── Connection ─────────────────────────────────────  │
│                                                      │
│  Server URL   https://api.mail.weesky.net       [⧉]  │
│  Username     alice@weesky.be                   [⧉]  │
│  Password     ••••••••••••••••••    [ Regenerate ]   │
│  Last used    2 hours ago                            │
│                                                      │
└──────────────────────────────────────────────────────┘
```

**Les cinq règles que le code doit rendre, et qu'un rendu approximatif perdrait :**

1. **L'onglet est nommé pour ce qu'il fait, pas pour le protocole** — il accueillera CalDAV, et le
   renommer plus tard casserait des marque-pages. Route `/settings/sync`.
2. **Allumer est un seul geste** : basculer l'interrupteur crée et affiche le secret aussitôt. Pas
   de second bouton « créer un mot de passe », pas d'écran vide entre les deux.
3. **Le secret s'affiche à la génération et à ce moment seulement** : en clair, en chasse fixe,
   avec un bouton de copie et un avertissement disant qu'il ne sera plus montré. Il disparaît au
   premier changement de page, et **il n'y a jamais de bouton « révéler »**.
4. **Les deux autres valeurs sont affichées en permanence** : ce sont celles qu'on revient chercher
   pour configurer un deuxième appareil.
5. **`Regenerate` demande confirmation, et la confirmation nomme la conséquence** — l'effet se
   produit ailleurs que sur l'écran où on le déclenche.

Et **« Jamais utilisé » se dit** : c'est le symptôme le plus courant d'une configuration client qui
n'a jamais abouti, et une case vide ne le dit pas.

**Files :**
- Create : `src/frontend/src/modules/settings/sync/SyncPage.tsx`
- Create : `src/frontend/src/modules/settings/sync/SyncPage.test.tsx`
- Modify : `src/frontend/src/modules/settings/SettingsLayout.tsx`
- Modify : `src/frontend/src/modules/settings/SettingsLayout.test.tsx`
- Modify : `src/frontend/src/routes.tsx`
- Modify : `src/frontend/src/locales/en/settings.json`
- Modify : `src/frontend/src/locales/fr/settings.json`
- Modify : `src/frontend/src/styles/shell.css`

**Interfaces :**
- Consomme : `api.getDavCredentials` / `setDavCardDav` / `regenerateDavSecret`, `DavCredentials`,
  `ToggleRow`, `relativeFromNow` (tâche 11) ; `useAuth().capabilities` / `.activeAccount` ;
  `useToasts` / `Toasts` ; `CopyIcon`, `RefreshIcon`.

- [ ] **Step 1 : Écrire les clés de traduction**

Dans `src/frontend/src/locales/en/settings.json`, ajouter `"sync": "Sync"` au bloc `nav` et le bloc :

```json
  "sync": {
    "carddav": "Contacts (CardDAV)",
    "carddavHint": "Sync your address book with your phone or Thunderbird. Turning this off stops every device; your password is kept.",
    "connection": "Connection",
    "serverUrl": "Server URL",
    "username": "Username",
    "password": "Password",
    "lastUsed": "Last used",
    "neverUsed": "Never used",
    "copy": "Copy",
    "copied": "Copied",
    "shownOnce": "Copy it now — it will not be shown again.",
    "regenerate": "Regenerate",
    "regenerateTitle": "Regenerate the sync password?",
    "regenerateWarning": "Every device will stop syncing until you enter the new password. Turn syncing off on your devices first, then enter the new one — repeated failures lock the account out for fifteen minutes.",
    "hidden": "Hidden — regenerate to get a new one",
    "loadFailed": "Could not load the sync settings",
    "saveFailed": "Could not save the change"
  }
```

Dans `fr/settings.json`, les mêmes clés, avec l'insécable avant `?` et l'apostrophe `’` :

```json
  "sync": {
    "carddav": "Contacts (CardDAV)",
    "carddavHint": "Synchronisez votre carnet d’adresses avec votre téléphone ou Thunderbird. Désactiver arrête tous les appareils ; votre mot de passe est conservé.",
    "connection": "Connexion",
    "serverUrl": "Adresse du serveur",
    "username": "Identifiant",
    "password": "Mot de passe",
    "lastUsed": "Dernière utilisation",
    "neverUsed": "Jamais utilisé",
    "copy": "Copier",
    "copied": "Copié",
    "shownOnce": "Copiez-le maintenant : il ne sera plus affiché.",
    "regenerate": "Régénérer",
    "regenerateTitle": "Régénérer le mot de passe de synchronisation ?",
    "regenerateWarning": "Tous les appareils cesseront de se synchroniser jusqu’à la saisie du nouveau mot de passe. Désactivez d’abord la synchronisation sur vos appareils, puis saisissez le nouveau : des échecs répétés bloquent le compte un quart d’heure.",
    "hidden": "Masqué — régénérez pour en obtenir un nouveau",
    "loadFailed": "Impossible de charger les réglages de synchronisation",
    "saveFailed": "Impossible d’enregistrer la modification"
  }
```

et `"sync": "Synchronisation"` dans son bloc `nav`.

**Attention :** dans les deux valeurs françaises portant `;` ou `?`, l'espace qui précède doit être
une **insécable U+00A0**. L'outil `Edit` écrit une espace ordinaire — poser ces deux chaînes en
PowerShell, ou vérifier après coup que `parity.test.ts` est vert.

- [ ] **Step 2 : Écrire les tests de l'écran, rouges**

Créer `src/frontend/src/modules/settings/sync/SyncPage.test.tsx` :

```tsx
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import SyncPage from './SyncPage'
import { api } from '../../../api.js'

vi.mock('../../../api.js', () => ({
  api: {
    getDavCredentials: vi.fn(),
    setDavCardDav: vi.fn(),
    regenerateDavSecret: vi.fn(),
  },
}))

const OFF = {
  serverUrl: 'https://api.mail.weesky.net', username: 'alice@weesky.be',
  configured: false, cardDavEnabled: false,
}
const ON = { ...OFF, configured: true, cardDavEnabled: true }

beforeEach(() => {
  vi.mocked(api.getDavCredentials).mockResolvedValue(OFF)
  vi.mocked(api.setDavCardDav).mockResolvedValue(ON)
  vi.mocked(api.regenerateDavSecret).mockResolvedValue({ ...ON, password: 'TSRQPONMLKJIHGFEDCBA' })
})

describe('SyncPage', () => {
  it('shows the address the server gave, not one composed here', async () => {
    render(<SyncPage />)

    expect(await screen.findByText('https://api.mail.weesky.net')).toBeInTheDocument()
    expect(screen.getByText('alice@weesky.be')).toBeInTheDocument()
  })

  it('turning the switch on generates and shows the secret in one gesture', async () => {
    vi.mocked(api.setDavCardDav).mockResolvedValue({ ...ON, password: 'ABCDEFGHIJKLMNOPQRST' })
    render(<SyncPage />)

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Contacts (CardDAV)' }))

    expect(api.setDavCardDav).toHaveBeenCalledWith(true)
    expect(await screen.findByText('ABCDEFGHIJKLMNOPQRST')).toBeInTheDocument()
    // Shown once, and the screen says so rather than letting the user find out later.
    expect(screen.getByText('Copy it now — it will not be shown again.')).toBeInTheDocument()
  })

  it('turning it back on shows no secret and never offers to reveal one', async () => {
    vi.mocked(api.getDavCredentials).mockResolvedValue({ ...ON, cardDavEnabled: false })
    vi.mocked(api.setDavCardDav).mockResolvedValue(ON)
    render(<SyncPage />)

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Contacts (CardDAV)' }))

    await waitFor(() => expect(api.setDavCardDav).toHaveBeenCalledWith(true))
    expect(screen.getByText('Hidden — regenerate to get a new one')).toBeInTheDocument()
    // The assertion that keeps the door shut: there is nothing to reveal, and never will be.
    expect(screen.queryByRole('button', { name: /reveal|show/i })).not.toBeInTheDocument()
  })

  it('turning it off keeps the connection values on screen', async () => {
    vi.mocked(api.getDavCredentials).mockResolvedValue(ON)
    vi.mocked(api.setDavCardDav).mockResolvedValue({ ...ON, cardDavEnabled: false })
    render(<SyncPage />)

    await userEvent.click(await screen.findByRole('checkbox', { name: 'Contacts (CardDAV)' }))

    await waitFor(() => expect(api.setDavCardDav).toHaveBeenCalledWith(false))
    // Configured stays configured: the values are what one comes back for on a second device.
    expect(screen.getByText('alice@weesky.be')).toBeInTheDocument()
  })

  it('regenerating asks first, and the question names the consequence', async () => {
    vi.mocked(api.getDavCredentials).mockResolvedValue(ON)
    render(<SyncPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Regenerate' }))

    expect(screen.getByText('Every device will stop syncing until you enter the new password.'))
      .toBeInTheDocument()
    expect(api.regenerateDavSecret).not.toHaveBeenCalled()
  })

  it('confirming regenerates and shows the new secret', async () => {
    vi.mocked(api.getDavCredentials).mockResolvedValue(ON)
    render(<SyncPage />)

    await userEvent.click(await screen.findByRole('button', { name: 'Regenerate' }))
    await userEvent.click(screen.getByRole('button', { name: 'Regenerate the sync password?' }))

    expect(await screen.findByText('TSRQPONMLKJIHGFEDCBA')).toBeInTheDocument()
  })

  it('says never used rather than leaving the line blank', async () => {
    // The most common symptom of a client configuration that never got through.
    vi.mocked(api.getDavCredentials).mockResolvedValue(ON)
    render(<SyncPage />)

    expect(await screen.findByText('Never used')).toBeInTheDocument()
  })

  it('renders a used date in the relative past', async () => {
    vi.mocked(api.getDavCredentials).mockResolvedValue({
      ...ON, lastUsedAt: new Date(Date.now() - 2 * 3600 * 1000).toISOString(),
    })
    render(<SyncPage />)

    expect(await screen.findByText('2 hours ago')).toBeInTheDocument()
  })
})
```

Le bouton de confirmation porte le titre de la boîte comme nom accessible ; si le composant écrit
un libellé différent, c'est le **test** qui suit le composant sur ce point précis et lui seul.

- [ ] **Step 3 : Écrire l'écran**

Créer `src/frontend/src/modules/settings/sync/SyncPage.tsx`. La structure, à respecter :

```tsx
import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '../../../api.js'
import type { DavCredentials } from '../../../types/dav'
import ToggleRow from '../../../components/ToggleRow'
import LoadingBlock from '../../../components/LoadingBlock'
import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import { relativeFromNow } from '../../../lib/intl'
import CopyIcon from '../../../icons/CopyIcon'
import RefreshIcon from '../../../icons/RefreshIcon'

/** A value the user comes back for, with the button that puts it on the clipboard. */
function CopyableRow({ label, value }: { label: string; value: string }) { /* … */ }

/**
 * The one screen of slice 4c-i. Named for what it does rather than for the protocol it speaks:
 * CardDAV is a word the user meets in their client, not in their head, and this tab will host
 * CalDAV — naming it after the first protocol to arrive would force a rename on a route bookmarks
 * have kept.
 */
export default function SyncPage() {
  const { t } = useTranslation('settings')
  const [state, setState] = useState<DavCredentials | null>(null)
  const [failed, setFailed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [confirming, setConfirming] = useState(false)
  // Held here and nowhere else, so it dies with the page: it exists in clear in exactly one
  // response, and there is no second way to obtain it.
  const [secret, setSecret] = useState<string | null>(null)
  const { toasts, addToast, removeToast } = useToasts()

  useEffect(() => {
    api.getDavCredentials().then(setState).catch(() => setFailed(true))
  }, [])

  async function toggle(enabled: boolean) { /* setBusy, api.setDavCardDav, setState, setSecret(next.password ?? null) */ }
  async function regenerate() { /* api.regenerateDavSecret, setState, setSecret */ }

  return (/* … */)
}
```

Points de rendu à ne pas approximer :

- `<div className="settings-page-header"><h1 className="settings-page-title"><RefreshIcon size={17} />{t('nav.sync')}</h1></div>` — l'icône du titre est celle de la ligne de nav (règle de continuité déclencheur/titre du site).
- `ToggleRow` avec `id="sync-carddav"`, `label={t('sync.carddav')}`, `hint={t('sync.carddavHint')}`, `checked={state.cardDavEnabled}`, `disabled={busy}`.
- La section `Connection` en `<div className="account-section">` avec un `<h2>{t('sync.connection')}</h2>`.
- **`Server URL` et `Username` sont toujours rendus**, allumé ou éteint.
- Le mot de passe : si `secret` est non nul, `<code className="sync-secret">{secret}</code>` plus le bouton de copie et `<p className="sync-secret-note">{t('sync.shownOnce')}</p>` ; sinon, `state.configured ? t('sync.hidden') : null`. **Aucun bouton « révéler », dans aucune branche.**
- `Regenerate` n'est rendu que si `state.configured`, et ouvre la boîte plutôt que d'appeler l'API.
**Pourquoi l’avertissement dit l’ordre, et pas seulement la conséquence.** Une régénération met chaque appareil configuré en boucle d’échec, et `AuthAttemptThrottle` bloque à dix échecs par quart d’heure sur l’identifiant ; un cycle de synchronisation en vaut plusieurs, donc deux appareils suffisent à franchir le seuil. Or `IsBlocked` s’exécute **avant** la comparaison du condensat, et seul un succès efface la clé : une fois bloqué, saisir le bon secret répond `429`. L’utilisateur doit donc éteindre la synchronisation avant de régénérer, pas après.

- La boîte de confirmation reprend `.modal-overlay` / `.modal` / `.modal-header` / `.modal-title` / `.modal-close`, son corps est `t('sync.regenerateWarning')` et son bouton d'action porte `aria-label={t('sync.regenerateTitle')}`.
- `Last used` : `state.lastUsedAt ? relativeFromNow(state.lastUsedAt) : t('sync.neverUsed')`.
- Copie : `navigator.clipboard.writeText(value)` puis `addToast(t('sync.copied'))`, l'échec restant silencieux — un presse-papiers refusé n'est pas une erreur à afficher.
- Échec de chargement : `failed` → `<p>{t('sync.loadFailed')}</p>` ; échec d'écriture → `addToast(t('sync.saveFailed'), 'error')` et l'interrupteur revient à sa valeur d'avant.

Ajouter à `src/frontend/src/styles/shell.css`, à la suite de `.account-section` :

```css
/* The secret is read once and typed into a client by hand: monospace, and wide enough not to wrap. */
.sync-secret {
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 15px; letter-spacing: 0.06em; user-select: all;
}
.sync-secret-note { margin: 6px 0 0; font-size: 12px; color: var(--text-muted); }
.sync-value { display: flex; align-items: center; gap: 8px; min-width: 0; }
.sync-value > span { overflow-wrap: anywhere; }
```

- [ ] **Step 4 : Poser la route et l'entrée de nav**

Dans `src/frontend/src/routes.tsx` : `const SyncPage = lazy(() => import('./modules/settings/sync/SyncPage'))`
et, dans les enfants de `settings`, à l'intérieur du bloc `RequirePrimary` existant, à la suite de
`account` :

```tsx
                  { path: 'sync', element: <Suspense fallback={null}><SyncPage /></Suspense> },
```

Dans `SettingsLayout.tsx` : `const davAvailable = capabilities?.dav !== false` à côté de
`aliasesAvailable`, l'import `RefreshIcon`, et l'entrée après `identities` :

```tsx
    ...(isPrimary && davAvailable ? [{ to: '/settings/sync', label: t('nav.sync'), icon: <RefreshIcon size={16} /> }] : []),
```

Gaté `isPrimary` comme Account et Aliases : le secret authentifie l'utilisateur weesky, et un compte
externe connecté n'a ni carnet ni principal.

- [ ] **Step 5 : Compléter le test de la nav**

Ajouter à `src/frontend/src/modules/settings/SettingsLayout.test.tsx`, sur le modèle des cas
`aliases` existants :

```tsx
  it('hides Sync on a non-primary account', () => { /* activeAccount.isPrimary = false → queryByText('Sync') null */ })

  it('hides Sync when the deployment publishes no address', () => { /* capabilities.dav = false */ })

  it('shows Sync when capabilities are still loading', () => { /* capabilities null → visible, the !== false rule */ })
```

- [ ] **Step 6 : Lancer les tests**

Run : `cd src/frontend && npm test`
Expected : tout au vert, `src/locales/parity.test.ts` et `keys.test.ts` compris.

Run : `cd src/frontend && npx tsc --noEmit && npm run lint`
Expected : propre.

- [ ] **Step 7 : Regarder l'écran**

Run : `cd src/frontend && npm run dev`, puis `/settings/sync`.
Vérifier de l'œil : l'adresse et l'identifiant lisibles et copiables, le secret en chasse fixe après
l'allumage, sa disparition au changement d'onglet et au retour, la confirmation avant la
régénération, et « Never used » plutôt qu'une ligne vide.

- [ ] **Step 8 : Commit**

```bash
git add src/frontend/src/modules/settings/sync/ src/frontend/src/modules/settings/SettingsLayout.tsx src/frontend/src/modules/settings/SettingsLayout.test.tsx src/frontend/src/routes.tsx src/frontend/src/locales/ src/frontend/src/styles/shell.css
git commit -F - <<'MSG'
feat(sync): l'onglet Sync donne les trois valeurs a recopier

Allumer engendre et affiche le secret une fois ; regenerer demande d'abord, et
nomme la consequence.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
```

---

### Task 13 : l'avertissement sur le changement de mot de passe

Le geste de la tâche 8 détruit le secret **sans que l'écran de synchronisation soit ouvert**. Le
dire là où il se déclenche est ce qui évite un utilisateur dont les trois appareils se sont tus
sans raison lisible.

**Le bouton de déconnexion globale n'existe pas encore dans l'interface** — `DELETE /api/Login/All`
est servi par le backend, mais aucun écran ne l'appelle et `api.js` n'en porte pas d'entrée. Il n'y
a donc rien à annoter de ce côté ; **c'est un résidu à consigner**, pas un oubli de cette tâche.

**Files :**
- Modify : `src/frontend/src/modules/settings/account/ChangePasswordSection.tsx`
- Modify : `src/frontend/src/modules/settings/account/AccountPage.test.tsx`
- Modify : `src/frontend/src/locales/en/settings.json`, `fr/settings.json`
- Modify : `docs/superpowers/contacts-4a-residuals.md`

- [ ] **Step 1 : Ajouter les clés**

**Ce que cette phrase doit dire, et l'ordre compte.** Un client déjà configuré repart en boucle
d'échec dès le changement de mot de passe, et le compteur d'échecs bloque à dix par quart d'heure
sur l'identifiant comme sur le /64 : un utilisateur à trois appareils peut donc se verrouiller
lui-même hors de `/dav` au moment précis où il vient reconfigurer, et seul un succès efface la clé
de son identifiant — or il ne peut pas en produire un tant qu'il est bloqué. La phrase dit donc
d'éteindre les clients d'abord, puis de les reconfigurer avec le nouveau secret ; jamais
« reconfigurez maintenant », qui invite à saturer le seuil.

`en` → bloc `account` : `"passwordResetsSync": "Changing your password also resets your sync password. Every device syncing your contacts will need the new one."`

`fr` → bloc `account` : `"passwordResetsSync": "Changer votre mot de passe réinitialise aussi votre mot de passe de synchronisation. Tous les appareils qui synchronisent vos contacts devront saisir le nouveau."`

- [ ] **Step 2 : Écrire le test, rouge**

Ajouter à `AccountPage.test.tsx` :

```tsx
  it('warns that changing the password also resets the sync one', async () => {
    // The gesture destroys the sync secret without the Sync tab being open, so the warning has to
    // live where the gesture is.
    render(<AccountPage />)

    expect(await screen.findByText(/also resets your sync password/i)).toBeInTheDocument()
  })
```

- [ ] **Step 3 : Rendre l'avertissement**

Dans `ChangePasswordSection.tsx`, juste sous l'ouverture du `<form>` et **au-dessus** de la ligne
d'erreur :

```tsx
      <p className="setting-hint">{t('account.passwordResetsSync')}</p>
```

- [ ] **Step 4 : Consigner le résidu**

Ajouter à `docs/superpowers/contacts-4a-residuals.md`, dans le tableau des reports :

```markdown
- **Aucun bouton « se déconnecter partout » dans l'interface.** `DELETE /api/Login/All` est servi
  et révoque désormais aussi le secret de synchronisation (4c-i, décision 2), mais aucun écran ne
  l'appelle et `api.js` n'en porte pas d'entrée : l'avertissement que la décision réclame sur ce
  bouton n'a rien à annoter. À poser le jour où le bouton apparaît.
```

- [ ] **Step 5 : Lancer les tests**

Run : `cd src/frontend && npm test`
Expected : tout au vert, parité comprise.

- [ ] **Step 6 : Commit**

```bash
git add src/frontend/src/modules/settings/account/ src/frontend/src/locales/ docs/superpowers/contacts-4a-residuals.md
git commit -F - <<'MSG'
feat(sync): changer son mot de passe previent qu'il reinitialise la synchro

Le geste detruit le secret sans que l'onglet Sync soit ouvert ; le bouton de
deconnexion globale n'existe pas encore cote interface.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
MSG
```

---

## Vérification de fin de tranche

- [ ] `cd src && dotnet test` — les deux suites au vert.
- [ ] `cd src && dotnet build` — zéro avertissement.
- [ ] `cd src/frontend && npm test && npx tsc --noEmit && npm run lint` — propre.
- [ ] `git status` — `src/snoopy.microservice/ApiDocumentation.xml` non modifié.
- [ ] Le DDL de la tâche 1 est joué sur `snoopy_webmail_dev` **avant** que le backend n'y soit
      déployé, et la requête de vérification rend `1`.

## Ce que cette tranche ne fait pas, et qui appartient à 4c-ii

Écrit ici pour qu'aucune revue de 4c-i ne le lise comme un manque :

- Aucune route `/dav`, aucun `PROPFIND`, aucun `REPORT`, aucun `.well-known`. La politique
  d'autorisation `Dav` existe (tâche 7) et n'est portée par rien — c'est voulu : la forme du défi
  est une décision de cette tranche, et la fixer une fois vaut mieux que la redécouvrir.
- Aucune des quatre autres tables, aucune pierre tombale, aucune révision, aucun `sync_sequence`,
  aucun rattrapage.
- Aucun `409` sur `PUT /api/contacts/{id}` — c'est la décision 17, et elle appartient à la tranche
  qui crée le second écrivain.
- Aucune transaction explicite : la mécanique de la décision 6 (`BeginTransactionAsync` à travers
  l'`IExecutionStrategy`, ordre de prise de verrou, lots de cent) est celle de 4c-ii. La seule
  écriture multi-instruction de cette tranche — la rotation qui détruit le secret — tient dans un
  `SaveChangesAsync`, ce qu'EF enveloppe déjà.
- **Un `[Authorize]` nu sur une route `/dav` serait un bogue**, et c'est le seul piège que cette
  tranche lègue sans qu'un test puisse l'attraper : le schéma de défi par défaut reste JwtBearer,
  donc un attribut sans nom de politique répondrait `WWW-Authenticate: Bearer` à un client CardDAV,
  qui n'a pas de jeton et ne sait pas en demander. Toute route `/dav` porte
  `[Authorize(Policy = CardDavAuthenticationDefaults.PolicyName)]`, jamais autre chose.
- **Le piège du limiteur après une régénération**, que 4c-i ne peut pas fermer : le contrôleur pourrait effacer la clé de l’identifiant en régénérant — l’appelant vient de prouver son identité par un JWT — mais `AuthAttemptThrottle` est `internal` et un contrôleur public ne peut pas le prendre en paramètre. Il faut une couture, et la clé d’adresse resterait de toute façon. 4c-ii la pose ou l’assume.
- **Le résidu de soixante secondes sur la révocation** : `Forget` ne peut pas battre un `Store`
  concurrent — une requête qui a lu l'ancien secret avant la rotation peut le réinscrire après.
  Le fermer demande un compteur de génération dans `IDavAuthenticationCache` ; c'est le bon
  correctif et 4c-ii le bon endroit.
- Aucune conformité client prouvée : c'est 4d.

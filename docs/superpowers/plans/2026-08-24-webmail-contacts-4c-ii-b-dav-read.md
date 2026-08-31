# Contacts 4c-ii-b — le serveur DAV en lecture : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Les rapports de sous-agents dans le scratchpad sont préfixés `4c-ii-b-task-N-…`.

**Spec :** [`docs/superpowers/specs/2026-08-23-webmail-contacts-4c-carddav-design.md`](../specs/2026-08-23-webmail-contacts-4c-carddav-design.md) — toute décision citée ici (« décision N ») y renvoie. En cas de doute, la spec fait foi.

**Périmètre.** Deuxième des trois plans qui composent 4c-ii :

| | Plan | État |
|---|---|---|
| a | [le socle de synchronisation](2026-08-24-webmail-contacts-4c-ii-a-sync-foundation.md) | écrit, **à exécuter avant celui-ci** |
| **b** | **ce document** — le serveur DAV en lecture | — |
| c | l'écriture et la synchro incrémentale : `PUT`, `DELETE`, `addressbook-query`, `sync-collection` | à écrire |

**Goal :** qu'un client CardDAV s'appaire sur l'adresse affichée par l'onglet « Sync », découvre son principal et son carnet, et tire l'intégralité des fiches — en lecture seule.

**Architecture :** un contrôleur par surface (`WellKnownController` anonyme, `CardDavController` sous la politique `CardDav` de 4c-i), des documents `multistatus` écrits **directement dans `Response.Body`** par un `XmlWriter` une `response` à la fois, des corps de requête analysés par `XDocument` avec DTD interdite et profondeur bornée, et un jeu de propriétés clos et énuméré plutôt que découvert tranche après tranche par des rapports de bogue.

**Tech stack :** .NET 10, ASP.NET Core, EF Core, `System.Xml` (`XmlWriter` / `XDocument`), xUnit 2.9.3, Moq 4.20.72. Aucune bibliothèque WebDAV : aucune n'est maintenue en .NET libre, et la surface est fixe — six documents de réponse, quatre de requête.

## Ce que ce plan suppose fait

**Le plan a doit être exécuté, et son rattrapage joué, avant que ce plan n'ouvre une route.** Ce n'est pas une commodité d'ordonnancement : entre le déploiement et le rattrapage, les fiches existantes n'ont ni `dav_name` ni rang, et **un client qui se connecte dans cette fenêtre voit un carnet vide et efface ses propres copies** en les croyant supprimées côté serveur. La première tâche de ce plan porte le contrôle qui l'interdit.

De 4c-i, déjà livré : le schéma d'authentification `CardDav`, sa politique nommée, le limiteur d'échecs, `Dav:PublicUrl`, et la capacité `dav`.
De 4c-ii-a : `contacts.dav_name` et `contacts.sync_sequence`, `contact_sync_state`, `IContactSyncStore`, et `EntityTagMatcher`.

## Global constraints

- Backend : `cd src && dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés) ; `cd src && dotnet build` doit rester à zéro avertissement.
- `src/snoopy.microservice/ApiDocumentation.xml` : artefact versionné que `dotnet test` régénère — le réverter avant chaque commit.
- `Assert.IsType<T>` vérifie le type **exact**.
- Style C# : file-scoped namespaces, un type par fichier, constructeurs primaires pour l'injection, records pour les DTO, `sealed`, `internal` par défaut, `CancellationToken` sur tout `async`, `ILogger` en journalisation structurée (jamais d'interpolation).
- **Toute route `/dav` porte `[Authorize(Policy = CardDavAuthenticationDefaults.PolicyName)]`, jamais un `[Authorize]` nu.** C'est le seul piège que 4c-i a légué sans qu'un test puisse l'attraper : le schéma de défi par défaut reste JwtBearer, donc un attribut sans nom de politique répondrait `WWW-Authenticate: Bearer` à un client CardDAV, qui n'a pas de jeton et ne sait pas en demander. La seule exception est `/.well-known/carddav`, **anonyme** — un `401` sur une redirection publique est un obstacle gratuit avant même que le client sache où s'authentifier.
- **Un élément XML se reconnaît à son espace de noms et à son nom local, jamais à son préfixe.** Les clients écrivent `D:`, `d:`, `a:` ou rien, et lient `DAV:` comme ils veulent ; un lecteur qui compare `"D:prop"` fonctionne contre l'exemple du RFC et échoue contre le premier client réel.
- **Aucune réponse de ce plan n'est un `500`.** Les refus du store sont des `Result.Failure` rédigés pour l'UI du webmail ; laissés tels quels ils remontent en `500`, et un `500` est ce qu'un client DAV retente indéfiniment, sur la même carte, à chaque cycle. Toute erreur attendue est traduite au bord.
- **Les `href` sont des chemins absolus** (`/dav/addressbooks/…`), jamais des URL complètes : le service est derrière un proxy inverse, et une URL reconstruite depuis l'hôte vu par Kestrel n'est pas celle que le client a demandée. **La collection porte toujours sa barre oblique finale, une carte n'en porte jamais** — un client compare des `href` littéralement.
- **Le secret n'est jamais journalisé**, ni en clair ni haché ; l'utilisateur, dans un journal, est le GUID du principal — celui qui est déjà dans l'URL.
- Commits : concis, sujet + ligne vide + corps de 2 lignes max, jamais commencer ni finir par `@`, terminer par `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`. **Ne jamais écrire un message de commit avec un here-string PowerShell dans l'outil Bash** — utiliser `git commit -F -` avec un heredoc.

## La surface que ce plan ouvre

```
*        /.well-known/carddav                    301 → /dav/ · anonyme · toute méthode
PROPFIND /                                       current-user-principal (l'hôte nu saisi tel quel)
OPTIONS  /dav/…                                  DAV: 1, 3, access-control, addressbook · Allow
PROPFIND /dav/                                   current-user-principal
PROPFIND /dav/principals/{userId}/               addressbook-home-set, principal-URL
REPORT   /dav/principals/{userId}/               expand-property
PROPFIND /dav/addressbooks/{userId}/             depth 0 et 1 → la collection « default »
PROPFIND /dav/addressbooks/{userId}/default/     depth 0 → les propriétés de collection
                                                 depth 1 → la collection, puis une ressource par fiche
PROPPATCH /dav/addressbooks/{userId}/ · …/default/ · …/default/{nom}
                                                 207, chaque propriété refusée en 403
REPORT   …/default/                              addressbook-multiget · expand-property
REPORT   …/default/{nom}                         addressbook-multiget
GET/HEAD …/default/{nom}                         200, la carte verbatim, ETag, text/vcard; charset=utf-8
*        …/default  (sans barre finale)          308 → …/default/
autres verbes                                    405 + Allow
```

**Ce que ce plan n'ouvre pas, et qui appartient au plan c :** `PUT`, `DELETE`, `addressbook-query` et son filtre, `sync-collection`, le jeton et son refus. Les routes n'existent pas ; `REPORT` répond `403 supported-report` à `addressbook-query` et `sync-collection` **jusqu'au plan c**, et ce refus est un test de ce plan-ci — il documente la frontière plutôt que de la laisser en `500`.

## Valeurs fixées une fois, à ne pas réinventer

| Constante | Valeur | Où |
|---|---|---|
| Chemin racine | `/dav` | `DavPaths.Root` |
| Nom du carnet | `default` — segment fixe, variable le jour où plusieurs carnets seront voulus | `DavPaths.BookName` |
| Corps de requête maximal | **1 Mo** | `[RequestSizeLimit]` sur `REPORT` et `PROPFIND` |
| Profondeur XML maximale | **50 niveaux** | `DavXmlReader.MaxDepth` |
| `href` maximaux dans un `multiget` | **5000** | `MultigetReport.MaxHrefs` |
| En-tête `DAV:` | `1, 3, access-control, addressbook` | `DavHeaders.ComplianceClasses` |
| `Allow` d'une collection | `OPTIONS, PROPFIND, PROPPATCH, REPORT` | `DavHeaders.CollectionAllow` |
| `Allow` d'une carte | `OPTIONS, HEAD, GET, PUT, DELETE, PROPFIND, PROPPATCH, REPORT` | `DavHeaders.CardAllow` |
| `max-resource-size` | `ContactStore.MaxCardBytes` — **la même constante**, jamais un littéral recopié | `DavProperties` |
| Type de contenu d'une carte | `text/vcard; charset=utf-8` | `DavHeaders.VCardContentType` |
| Type de contenu d'une réponse XML | `application/xml; charset=utf-8` | `DavHeaders.XmlContentType` |

## Les trois littéralités de `multistatus`

Un client au moins les compare octet à octet, et aucune ne se rattrape après coup :

1. **Dans une `response` à deux `propstat`, celui à `200` s'écrit AVANT celui à `404`.** Thunderbird lit le **premier** `status` descendant d'une `response` et le compare à la chaîne `HTTP/1.1 200 OK`.
2. **Les lignes de statut sont littéralement `HTTP/1.1 200 OK` et `HTTP/1.1 404 Not Found`** — sabre a déjà dû corriger un `Ok` pour iOS.
3. **Le `status` d'une pierre tombale est un enfant direct de sa `response`**, jamais logé dans un `propstat`. (Le cas n'apparaît qu'au plan c ; l'écrivain le porte dès ici pour que le plan c n'ait pas à le rouvrir.)

## Découpage en paquets

| | Paquet | Tâches | Vérifiable par |
|---|---|---|---|
| 1 | Les chemins, le XML et l'écrivain | 1–3 | la suite .NET ; aucune route n'existe encore |
| 2 | La lecture du carnet et le jeu de propriétés | 4–5 | la suite .NET |
| 3 | `PROPFIND` et `GET` | 6–7 | la suite .NET ; **un client tire déjà le carnet** |
| 4 | Les rapports de lecture | 8–9 | la suite .NET |
| 5 | La surface HTTP et le journal | 10–11 | la suite .NET + un client réel |

---

## Paquet 1 — les chemins, le XML et l'écrivain

### Task 1 : `DavPaths` — construire, analyser, encoder, décoder, valider

Un nom de ressource est un segment de chemin : il traverse un encodage à l'aller et un décodage au
retour, et **les deux doivent être écrits ensemble**. C'est la seule tâche du plan où une erreur
d'un caractère ouvre une traversée de répertoire.

**Deux règles qui se contredisent en apparence et qu'il faut tenir toutes les deux :**

- Le chemin de la requête est décodé **une fois**, par `Uri.UnescapeDataString` sur le segment
  brut, **avant** validation — c'est le décodage qui fait de `%2F` un `/`, et valider avant lui
  laisserait passer une traversée que le stockage refuse ensuite.
- « Une fois » se mesure depuis `Features.Get<IHttpRequestFeature>()?.RawTarget`, **tronqué au
  premier `?`**, et depuis rien d'autre. **Mesuré sur Kestrel réel en tâche 1, contre une prise
  brute** : `RawTarget` porte la cible de la ligne de requête telle quelle, chaîne de requête
  comprise. `Request.Path.Value` **n'est pas** le chemin encodé — ce plan l'avait d'abord offert
  comme équivalent et il ne l'est pas : Kestrel en décode déjà une partie — `%20`, `%23`, `%3F`,
  `%2E`, `%5C`, les séquences UTF-8 — et **ne préserve que `%2F`**, de sorte que `a%2Fb.vcf` et
  `a%252Fb.vcf` y arrivent **tous les deux** comme `a%2Fb.vcf` — indiscernables, et
  `Uri.UnescapeDataString` rend `a/b.vcf` pour les deux. C'est la traversée, atteignable depuis
  `Request.Path.Value` exactement comme depuis une valeur de route. Pire : `Request.Path` est
  normalisé de ses segments points **après** ce décodage partiel, donc un `..` **encodé**
  (`…/default/%2E%2E`) s'y effondre et sort de la collection, là où `RawTarget` le conserve.
- **Jamais depuis une valeur de route** : ASP.NET Core décode déjà les siennes, et les repasser
  dans `Uri.UnescapeDataString` ferait de `%252F` un `/` — la traversée revenue par un double
  décodage.

Réciproquement **tout `href` écrit dans une réponse est ré-encodé segment par segment**
(`Uri.EscapeDataString`), sans quoi un nom portant un espace, un `#` ou un `?` — qu'un client a le
droit de choisir — produirait un `href` que ce même client ne saurait pas relire.

**`DavPaths` porte les deux sens et rien d'autre ne construit ni ne lit ces chemins.**

**Files :**
- Create : `src/snoopy.microservice/Services/CardDav/DavPaths.cs`
- Create : `src/snoopy.microservice/Services/CardDav/DavName.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/CardDav/DavPathsTests.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/CardDav/DavNameTests.cs`

**Interfaces :**
- Produit, consommé par toutes les tâches suivantes et par le plan c :

```csharp
internal static class DavPaths
{
    internal const string Root = "/dav";
    internal const string BookName = "default";

    /// "/dav/principals/{userId}/" — always with its trailing slash.
    internal static string Principal(Guid userId);

    /// "/dav/addressbooks/{userId}/" — the address-book home.
    internal static string Home(Guid userId);

    /// "/dav/addressbooks/{userId}/default/" — the collection.
    internal static string Collection(Guid userId);

    /// "/dav/addressbooks/{userId}/default/{escaped name}" — never a trailing slash.
    internal static string Card(Guid userId, string davName);

    /// The resource an href from a request body designates, or null when it is not one of ours.
    /// Decodes each segment once; never touches a route value, which ASP.NET already decoded.
    internal static DavResource? Parse(string absolutePath);
}

/// What an href resolved to. `DavName` is null for anything but a card.
internal sealed record DavResource(DavResourceKind Kind, Guid UserId, string? DavName);

internal enum DavResourceKind { ServiceRoot, Principal, Home, Collection, Card }

internal static class DavName
{
    /// At most 255 characters, non-empty, no '/', no '\', no control character
    /// (U+0000–U+001F, U+007F), no leading or trailing space, and not "." or "..".
    internal static bool IsValid(string? name);

    /// "{id}.vcf" — the convention for a card born in the webmail. Not a constraint: the route
    /// captures whatever the client chose, and IsValid is the only judge.
    internal static string ForContact(Guid contactId);
}
```

- [ ] **Step 1 : Écrire les tests de `DavName`, rouges**

```csharp
    [Theory]
    [InlineData("card.vcf")]
    [InlineData("card")]                        // the suffix is a client convention, not a rule
    [InlineData("un nom avec des espaces.vcf")] // an inner space is legitimate and carried
    [InlineData("urn:uuid:aaaa.vcf")]           // an import keeps the source UID verbatim
    [InlineData("é#?.vcf")]                     // the client may choose these; the segment escapes them
    public void AName_ThatAClientMayChoose_IsAccepted(string name) =>
        Assert.True(DavName.IsValid(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b.vcf")]
    [InlineData("a\\b.vcf")]
    [InlineData("a\u0000b.vcf")]
    [InlineData("a\u001Fb.vcf")]
    [InlineData("a\u007Fb.vcf")]
    [InlineData(" leading.vcf")]
    [InlineData("trailing.vcf ")]
    public void AName_ThatWouldBreakSomething_IsRefused(string? name) =>
        Assert.False(DavName.IsValid(name));

    [Fact]
    public void ANameOfTwoHundredAndFiftyFiveCharacters_IsAccepted() =>
        Assert.True(DavName.IsValid(new string('a', 255)));

    [Fact]
    public void ANameOfTwoHundredAndFiftySix_IsRefused() =>
        Assert.False(DavName.IsValid(new string('a', 256)));

    [Fact]
    public void EdgeSpaces_AreRefusedBecauseTheCollationPadsThem()
    {
        // utf8mb4_bin settles case but not space: it is PAD SPACE under MariaDB, so "carte.vcf"
        // and "carte.vcf " are equal for the unique index while being two distinct URLs for every
        // HTTP client. A uniqueness comparison that merges two resources is worse than one that
        // separates them: the second PUT would fail on a duplicate the client can neither
        // understand nor correct.
        Assert.False(DavName.IsValid("carte.vcf "));
        Assert.True(DavName.IsValid("carte .vcf"));
    }
```

- [ ] **Step 2 : Écrire les tests de `DavPaths`, rouges**

```csharp
    [Fact]
    public void ACollectionHref_AlwaysCarriesItsTrailingSlash()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // A client compares hrefs literally: the collection always has its slash, a card never has
        // one. Getting this wrong makes a client treat two spellings of one resource as two.
        Assert.EndsWith("/", DavPaths.Collection(userId));
        Assert.EndsWith("/", DavPaths.Home(userId));
        Assert.EndsWith("/", DavPaths.Principal(userId));
        Assert.DoesNotContain("//dav", DavPaths.Collection(userId));
    }

    [Fact]
    public void ACardHref_CarriesNoTrailingSlashAndIsEscapedSegmentWise()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var href = DavPaths.Card(userId, "un nom#?.vcf");

        Assert.DoesNotContain(' ', href);
        Assert.DoesNotContain('#', href);
        Assert.DoesNotContain('?', href);
        // Without the escape, a name carrying a space, a '#' or a '?' — which a client may choose —
        // produces an href that same client cannot read back.
        Assert.Equal($"/dav/addressbooks/{userId}/default/{Uri.EscapeDataString("un nom#?.vcf")}", href);
    }

    [Fact]
    public void AnHref_IsNeverAFullUrl()
    {
        // The service is behind a reverse proxy: an absolute URL rebuilt from the host Kestrel sees
        // is not the one the client asked for.
        Assert.StartsWith("/", DavPaths.Collection(Guid.NewGuid()));
        Assert.DoesNotContain("://", DavPaths.Collection(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("/dav/", DavResourceKind.ServiceRoot)]
    [InlineData("/dav/principals/11111111-1111-1111-1111-111111111111/", DavResourceKind.Principal)]
    [InlineData("/dav/addressbooks/11111111-1111-1111-1111-111111111111/", DavResourceKind.Home)]
    [InlineData("/dav/addressbooks/11111111-1111-1111-1111-111111111111/default/", DavResourceKind.Collection)]
    [InlineData("/dav/addressbooks/11111111-1111-1111-1111-111111111111/default/a.vcf", DavResourceKind.Card)]
    public void EachShapeOfPath_ResolvesToItsKind(string path, DavResourceKind expected) =>
        Assert.Equal(expected, DavPaths.Parse(path)!.Kind);

    [Fact]
    public void AnEncodedSegment_IsDecodedExactlyOnce()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var resource = DavPaths.Parse($"/dav/addressbooks/{userId}/default/un%20nom.vcf");

        Assert.Equal("un nom.vcf", resource!.DavName);
    }

    [Fact]
    public void ADoubleEncodedSlash_DoesNotComeBackAsATraversal()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // %252F decoded twice is '/'. Decoding once gives "%2F", which IsValid then accepts as a
        // literal name — no traversal. This is the assertion that says "once" is enforced.
        var resource = DavPaths.Parse($"/dav/addressbooks/{userId}/default/a%252Fb.vcf");

        Assert.Equal("a%2Fb.vcf", resource!.DavName);
    }

    [Fact]
    public void AnEncodedSlash_DecodesToAnInvalidNameRatherThanTraversing()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        // %2F decodes to '/', which DavName refuses. Validating BEFORE the decode would let this
        // through to a store that refuses it later — or does not.
        var resource = DavPaths.Parse($"/dav/addressbooks/{userId}/default/a%2Fb.vcf");

        Assert.False(DavName.IsValid(resource!.DavName));
    }

    [Theory]
    [InlineData("/dav/addressbooks/not-a-guid/default/")]
    [InlineData("/dav/addressbooks/11111111-1111-1111-1111-111111111111/other/")]
    [InlineData("/api/contacts")]
    [InlineData("/dav/addressbooks/11111111-1111-1111-1111-111111111111/default/a/b")]
    public void APathThatIsNotOurs_ResolvesToNothing(string path) =>
        Assert.Null(DavPaths.Parse(path));

    [Fact]
    public void BuildingThenParsing_RoundTripsAnAwkwardName()
    {
        var userId = Guid.NewGuid();
        const string name = "Ada & Grace #1 ?.vcf";

        var parsed = DavPaths.Parse(DavPaths.Card(userId, name));

        Assert.Equal(name, parsed!.DavName);
        Assert.Equal(userId, parsed.UserId);
    }
```

- [ ] **Step 3 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~DavPathsTests|FullyQualifiedName~DavNameTests"`
Expected : ne compile pas.

- [ ] **Step 4 : Écrire `DavName`**

```csharp
namespace weesky.Snoopy.Microservice.Services.CardDav;

/// <summary>
/// What a client may call one of its cards. The route captures the segment whole — suffix
/// included — and this is the only judge: a route pattern demanding ".vcf" would contradict
/// decision 5 in silence, refusing a name by a routing 404 rather than by a considered answer.
/// </summary>
internal static class DavName
{
    private const int MaxLength = 255;

    internal static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxLength) return false;
        if (name is "." or "..") return false;
        // Edge spaces, because utf8mb4_bin is PAD SPACE: two names differing only by a trailing
        // space collide on the unique index while being two distinct URLs for every HTTP client.
        if (name[0] == ' ' || name[^1] == ' ') return false;

        foreach (var c in name)
        {
            if (c is '/' or '\\') return false;
            if (c <= '\u001F' || c == '\u007F') return false;
        }

        return true;
    }

    internal static string ForContact(Guid contactId) => $"{contactId}.vcf";
}
```

- [ ] **Step 5 : Écrire `DavPaths`**

Les constructeurs sont de la concaténation ; l'analyse découpe sur `/`, refuse ce qui n'a pas la
bonne forme, et **décode chaque segment une fois** par `Uri.UnescapeDataString`. Écrire dans le
code le commentaire qui dit pourquoi l'analyse ne prend jamais une valeur de route.

`Parse` **ne valide pas** le nom : elle le rend décodé, et l'appelant demande à `DavName.IsValid`.
Les deux restent séparées parce que le plan c a besoin de distinguer « ce n'est pas une de nos
ressources » (`404`) de « ce nom n'est pas acceptable » (`403 valid-address-data` sur un `PUT`).

- [ ] **Step 6 : Lancer les tests**

Run : `cd src && dotnet test --filter "FullyQualifiedName~DavPaths|FullyQualifiedName~DavName"`
Expected : tous PASS.

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 7 : Commit**

- sujet : `feat(dav): les chemins, leur encodage et la validation des noms`
- corps : `Un segment se decode une fois depuis le chemin encode, jamais depuis une` /
  `valeur de route qu'ASP.NET a deja decodee.`

---

### Task 2 : `DavXml` et le lecteur qui refuse ce qu'il ne doit pas lire

Un corps de `REPORT` est une **entrée non fiable**, et l'expansion d'entités y est la faille
classique — un fichier local lu et renvoyé dans une réponse `multistatus`. Deux gardes, et elles ne
ferment pas la même chose :

- `DtdProcessing = Prohibit` et `XmlResolver = null` ferment l'expansion d'entités.
- **La profondeur d'imbrication ferme la pile**, que la première ne touche pas : le mégaoctet
  autorisé laisse la place à beaucoup de balises imbriquées, et **un débordement de pile en .NET ne
  se rattrape pas** — il emporte le processus qui sert tous les utilisateurs. Aucune requête
  légitime de ce protocole ne dépasse la dizaine de niveaux.
  **Correction mesurée en tâche 2, et elle change la raison sans changer la règle** : ce plan
  affirmait d'abord que « la construction de l'arbre y descend ». C'est faux — `XDocument.Parse`
  survit à 200 000 niveaux, la construction de LINQ-to-XML étant itérative en .NET 10, et
  `XmlReader.Read()` garde sa pile d'éléments sur le tas. Ce qui récurse, c'est
  **`XNode.WriteTo` / `ToString`, un niveau par appel** — et les tâches 6 à 11 sérialisent
  précisément ces arbres dans un `multistatus`. Le plafond reste donc obligatoire ; il protège
  l'écriture et non la lecture. **Ne pas le retirer au motif que `XDocument` ne récurse pas.**

**Files :**
- Create : `src/snoopy.microservice/Services/CardDav/DavXml.cs`
- Create : `src/snoopy.microservice/Services/CardDav/DavXmlReader.cs`
- Create : `src/snoopy.microservice/Services/CardDav/DavError.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/CardDav/DavXmlReaderTests.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/CardDav/DavErrorTests.cs`

**Interfaces :**
- Produit, consommé par toutes les tâches suivantes et par le plan c :

```csharp
internal static class DavXml
{
    internal static readonly XNamespace Dav = "DAV:";
    internal static readonly XNamespace CardDav = "urn:ietf:params:xml:ns:carddav";
    /// getctag is an extension, not a RFC: its namespace is CalendarServer's.
    internal static readonly XNamespace CalendarServer = "http://calendarserver.org/ns/";

    // Element names, as XName — never as a string with a prefix.
    internal static readonly XName Prop, PropStat, Response, MultiStatus, Href, Status, Error, /* … */;
}

internal static class DavXmlReader
{
    internal const int MaxDepth = 50;

    /// Parses a request body with DTD prohibited, no resolver, and a depth ceiling. Answers null
    /// when the body is empty — which several clients send on PROPFIND, and which means allprop.
    /// Throws DavBadRequestException on a DTD, an entity, malformed XML, or excess depth.
    internal static XDocument? Parse(Stream body);
}

internal static class DavError
{
    /// Writes `<D:error xmlns:D="DAV:"><D:{condition}/></D:error>` with the XML declaration, as
    /// `application/xml; charset=utf-8`, and sets the status.
    internal static Task WriteAsync(HttpResponse response, int statusCode, XName condition,
        XElement? detail = null, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Fact]
    public void ADtd_IsRefused()
    {
        var body = ToStream(
            "<!DOCTYPE t [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><t>&x;</t>");

        // The classic hole of this protocol: a local file read and echoed back inside a multistatus.
        Assert.Throws<DavBadRequestException>(() => DavXmlReader.Parse(body));
    }

    [Fact]
    public void AnExternalEntity_IsRefused()
    {
        var body = ToStream(
            "<?xml version=\"1.0\"?><!DOCTYPE r SYSTEM \"http://example.invalid/x.dtd\"><r/>");

        Assert.Throws<DavBadRequestException>(() => DavXmlReader.Parse(body));
    }

    [Fact]
    public void ADocumentDeeperThanFifty_IsRefused()
    {
        var body = ToStream(Nested(DavXmlReader.MaxDepth + 5));

        // DtdProcessing closes entity expansion, not the stack. A .NET stack overflow cannot be
        // caught: it takes down the process serving every user.
        Assert.Throws<DavBadRequestException>(() => DavXmlReader.Parse(body));
    }

    [Fact]
    public void ADocumentAtFiftyExactly_IsAccepted() =>
        Assert.NotNull(DavXmlReader.Parse(ToStream(Nested(DavXmlReader.MaxDepth))));

    [Fact]
    public void AnEmptyBody_IsNotAnError()
    {
        // Several clients send one on PROPFIND at discovery, and it means allprop (RFC 4918 § 9.1).
        Assert.Null(DavXmlReader.Parse(ToStream("")));
    }

    [Fact]
    public void MalformedXml_IsRefused() =>
        Assert.Throws<DavBadRequestException>(() => DavXmlReader.Parse(ToStream("<a><b></a>")));

    [Theory]
    [InlineData("<D:propfind xmlns:D=\"DAV:\"><D:prop/></D:propfind>")]
    [InlineData("<d:propfind xmlns:d=\"DAV:\"><d:prop/></d:propfind>")]
    [InlineData("<a:propfind xmlns:a=\"DAV:\"><a:prop/></a:propfind>")]
    [InlineData("<propfind xmlns=\"DAV:\"><prop/></propfind>")]
    public void ThePrefixIsIrrelevant_OnlyTheNamespaceAndLocalNameCount(string xml)
    {
        var document = DavXmlReader.Parse(ToStream(xml));

        // Clients write D:, d:, a: or nothing. A reader comparing "D:prop" works against the RFC's
        // example and fails against the first real client.
        Assert.NotNull(document!.Root!.Element(DavXml.Dav + "prop"));
    }
```

et pour `DavError` :

```csharp
    [Fact]
    public async Task AnErrorBody_HasErrorAsItsRootAndIsTypedXml()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await DavError.WriteAsync(context.Response, 403, DavXml.CardDav + "supported-report");

        // DAVx5 extracts a precondition only from an XML-typed body whose root is `error`. A 403
        // served as text/plain makes it fail on every cycle instead of starting over.
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Equal("application/xml; charset=utf-8", context.Response.ContentType);
        var written = ReadBody(context.Response);
        Assert.StartsWith("<?xml", written);
        Assert.Equal(DavXml.Dav + "error", XDocument.Parse(written).Root!.Name);
    }

    [Fact]
    public async Task AnErrorBody_NamesItsConditionInsideTheRoot()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        await DavError.WriteAsync(context.Response, 403, DavXml.Dav + "propfind-finite-depth");

        var root = XDocument.Parse(ReadBody(context.Response)).Root!;
        // A bare 403 leaves the client nothing but giving up; the condition is what it reads to
        // choose its fallback.
        Assert.Single(root.Elements(DavXml.Dav + "propfind-finite-depth"));
    }

    [Fact]
    public async Task AConditionMayCarryDetail()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var detail = new XElement(DavXml.Dav + "href", "/dav/addressbooks/x/default/a.vcf");

        await DavError.WriteAsync(context.Response, 403, DavXml.CardDav + "no-uid-conflict", detail);

        // no-uid-conflict carries the href of the conflicting resource: without it the client knows
        // it lost but not to whom.
        var condition = XDocument.Parse(ReadBody(context.Response)).Root!
            .Element(DavXml.CardDav + "no-uid-conflict")!;
        Assert.Equal("/dav/addressbooks/x/default/a.vcf", condition.Element(DavXml.Dav + "href")!.Value);
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~DavXmlReader|FullyQualifiedName~DavError"`
Expected : ne compile pas.

- [ ] **Step 3 : Écrire `DavXml`**

Les espaces de noms et les `XName` du protocole, en `static readonly`. **Aucune chaîne préfixée**
n'apparaît dans le fichier ; un `XName` se compose `DavXml.Dav + "prop"`.

Y mettre les noms des trois espaces — `DAV:`, `urn:ietf:params:xml:ns:carddav`,
`http://calendarserver.org/ns/` — et un commentaire disant que le troisième est une **extension et
non un RFC** : aucun RFC de la tranche ne définit `getctag`, et il reste servi parce que DAVx⁵ le
demande à chaque interrogation d'état et s'y replie quand `sync-collection` manque.

- [ ] **Step 4 : Écrire `DavXmlReader` et son exception**

Créer `DavBadRequestException` (interne, scellée) dans le même dossier. `Parse` :

1. si le flux est vide → `null` ;
2. `XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
   IgnoreWhitespace = true, IgnoreComments = true, IgnoreProcessingInstructions = true }` ;
3. lire avec `XmlReader.Create`, en **comptant les niveaux** — `reader.Depth` suffit, il ne
   descend pas plus loin que ce qu'on lit — et lever au-delà de `MaxDepth` ;
4. `XDocument.Load(reader)` ; toute `XmlException` devient `DavBadRequestException`.

**Compter la profondeur avec `reader.Depth` pendant la lecture, et non après sur l'arbre :**
mesurer l'arbre suppose de l'avoir construit, c'est-à-dire d'avoir déjà descendu la pile qu'on
voulait protéger.

- [ ] **Step 5 : Écrire `DavError`**

Un `XmlWriter` sur `Response.Body`, `Response.StatusCode` posé **avant** la première écriture — une
fois le corps commencé, l'en-tête est parti. `application/xml; charset=utf-8`, déclaration XML
comprise.

- [ ] **Step 6 : Lancer les tests**

Run : `cd src && dotnet test --filter "FullyQualifiedName~DavXml|FullyQualifiedName~DavError"`
Expected : tous PASS.

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 7 : Commit**

- sujet : `feat(dav): le lecteur XML refuse les DTD et borne sa profondeur`
- corps : `DtdProcessing ferme l'expansion d'entites, pas la pile : un debordement` /
  `en .NET ne se rattrape pas, il emporte le processus.`

---

### Task 3 : `MultiStatusWriter` — écrire au fil de l'eau, et les trois littéralités

Un carnet plein fait 5000 fiches et une carte peut peser 1 Mo : une réponse portant `address-data`
sur tout le carnet se compte en gigaoctets. Les documents `multistatus` sont donc écrits
**directement dans `Response.Body`**, une `response` à la fois — jamais un document construit en
mémoire puis sérialisé, ce qui mettrait le carnet entier dans le tas d'un processus qui sert tous
les utilisateurs.

**Files :**
- Create : `src/snoopy.microservice/Services/CardDav/MultiStatusWriter.cs`
- Create : `src/snoopy.microservice/Services/CardDav/DavHeaders.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/CardDav/MultiStatusWriterTests.cs`

**Interfaces :**
- Produit, consommé par les tâches 6 à 11 et par le plan c :

```csharp
/// Writes a multistatus straight into the response body. Disposable: the closing tag is written
/// on dispose, so a `using` is the only correct way to hold one.
internal sealed class MultiStatusWriter : IAsyncDisposable
{
    /// Sets 207, the XML content type and the DAV header, then opens the document. Nothing may be
    /// written to the response before this: once the body has started, the headers are gone.
    internal static Task<MultiStatusWriter> BeginAsync(HttpResponse response, CancellationToken ct);

    /// One `response` carrying properties. `found` is written BEFORE `missing` — Thunderbird reads
    /// the FIRST descendant status of a response and compares it to "HTTP/1.1 200 OK".
    internal Task WriteResourceAsync(string href, IReadOnlyList<XElement> found,
        IReadOnlyList<XName> missing, CancellationToken ct);

    /// One `response` whose status is a DIRECT child — the shape a sync-collection tombstone takes.
    /// Written here rather than in plan c so the literality lives in one place.
    internal Task WriteStatusAsync(string href, int statusCode, CancellationToken ct);

    /// The truncation shape of RFC 6352 § 8.6.2: a `response` on the Request-URI carrying 507 and
    /// `number-of-matches-within-limits`.
    internal Task WriteTruncatedAsync(string href, XElement? extra, CancellationToken ct);

    /// Pushes what is written so far onto the wire. The point of writing straight into the body:
    /// the first response is sent before the last one is composed.
    internal Task FlushAsync(CancellationToken ct);
}

internal static class DavHeaders
{
    internal const string ComplianceClasses = "1, 3, access-control, addressbook";
    internal const string CollectionAllow = "OPTIONS, PROPFIND, PROPPATCH, REPORT";
    internal const string CardAllow = "OPTIONS, HEAD, GET, PUT, DELETE, PROPFIND, PROPPATCH, REPORT";
    internal const string VCardContentType = "text/vcard; charset=utf-8";
    internal const string XmlContentType = "application/xml; charset=utf-8";

    /// `DAV:` on every response that carries it — including PROPFIND, not only OPTIONS: sabre does
    /// it deliberately and Apple clients depend on it outside OPTIONS.
    internal static void ApplyDav(HttpResponse response);
}
```

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Fact]
    public async Task ItAnswers207WithTypedXmlAndTheDavHeader()
    {
        var context = NewContext();

        await using (await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None)) { }

        Assert.Equal(207, context.Response.StatusCode);
        Assert.Equal("application/xml; charset=utf-8", context.Response.ContentType);
        // On PROPFIND too, not only OPTIONS: sabre does it deliberately and Apple clients depend on it.
        Assert.Equal(DavHeaders.ComplianceClasses, context.Response.Headers["DAV"].ToString());
    }

    [Fact]
    public async Task TheFoundPropstat_IsWrittenBeforeTheMissingOne()
    {
        var context = NewContext();

        await using (var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None))
        {
            await writer.WriteResourceAsync("/dav/x/",
                [new XElement(DavXml.Dav + "displayname", "Book")],
                [DavXml.CardDav + "addressbook-description"],
                CancellationToken.None);
        }

        // Thunderbird reads the FIRST descendant status of a response and compares it to the string
        // "HTTP/1.1 200 OK". Writing the 404 propstat first makes every response read as a failure.
        var body = ReadBody(context.Response);
        Assert.True(body.IndexOf("200 OK", StringComparison.Ordinal)
                    < body.IndexOf("404 Not Found", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(200, "HTTP/1.1 200 OK")]
    [InlineData(404, "HTTP/1.1 404 Not Found")]
    [InlineData(403, "HTTP/1.1 403 Forbidden")]
    [InlineData(507, "HTTP/1.1 507 Insufficient Storage")]
    public async Task TheStatusLine_IsLiteral(int code, string expected)
    {
        var context = NewContext();

        await using (var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None))
            await writer.WriteStatusAsync("/dav/x/a.vcf", code, CancellationToken.None);

        // sabre has already had to correct an "Ok" for iOS. These strings are compared byte for byte.
        Assert.Contains(expected, ReadBody(context.Response));
    }

    [Fact]
    public async Task AStatusResponse_CarriesItsStatusAsADirectChild()
    {
        var context = NewContext();

        await using (var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None))
            await writer.WriteStatusAsync("/dav/x/gone.vcf", 404, CancellationToken.None);

        // The shape a sync-collection tombstone takes: never lodged inside a propstat. Written here
        // rather than in plan c so the literality lives in one place.
        var response = XDocument.Parse(ReadBody(context.Response)).Root!.Elements(DavXml.Dav + "response").Single();
        Assert.Single(response.Elements(DavXml.Dav + "status"));
        Assert.Empty(response.Elements(DavXml.Dav + "propstat"));
    }

    [Fact]
    public async Task AResourceWithNothingMissing_CarriesOneSinglePropstat()
    {
        var context = NewContext();

        await using (var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None))
        {
            await writer.WriteResourceAsync("/dav/x/",
                [new XElement(DavXml.Dav + "displayname", "Book")], [], CancellationToken.None);
        }

        var response = XDocument.Parse(ReadBody(context.Response)).Root!.Elements(DavXml.Dav + "response").Single();
        Assert.Single(response.Elements(DavXml.Dav + "propstat"));
    }

    [Fact]
    public async Task AMissingProperty_IsNamedWithoutAValue()
    {
        var context = NewContext();

        await using (var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None))
        {
            await writer.WriteResourceAsync("/dav/x/", [],
                [DavXml.Dav + "acl"], CancellationToken.None);
        }

        // Pure omission is what makes a client wait for ever for a value it believes is on its way.
        var propstat = XDocument.Parse(ReadBody(context.Response))
            .Descendants(DavXml.Dav + "propstat").Single();
        Assert.Contains("404 Not Found", propstat.Element(DavXml.Dav + "status")!.Value);
        Assert.Single(propstat.Element(DavXml.Dav + "prop")!.Elements(DavXml.Dav + "acl"));
    }

    [Fact]
    public async Task AnHref_IsWrittenEscapedAsItWasGiven()
    {
        var context = NewContext();
        var href = DavPaths.Card(Guid.NewGuid(), "un nom.vcf");

        await using (var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None))
            await writer.WriteStatusAsync(href, 404, CancellationToken.None);

        // The writer never re-escapes and never unescapes: DavPaths owns both directions, and doing
        // it twice would give a client an href it cannot read back.
        Assert.Equal(href, XDocument.Parse(ReadBody(context.Response))
            .Descendants(DavXml.Dav + "href").Single().Value);
    }

    [Fact]
    public async Task TheDocument_StreamsRatherThanBuffering()
    {
        var context = NewContext();

        await using var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None);
        await writer.WriteResourceAsync("/dav/x/a.vcf",
            [new XElement(DavXml.Dav + "getetag", "\"h1\"")], [], CancellationToken.None);
        await writer.FlushAsync(CancellationToken.None);

        // A full book with address-data runs to gigabytes: the first response must be on the wire
        // before the last one is composed, or the whole book sits in the heap of a process serving
        // every user.
        Assert.Contains("a.vcf", ReadBody(context.Response));
    }

    [Fact]
    public async Task TheTruncationShape_IsTheOneRfc6352Names()
    {
        var context = NewContext();

        await using (var writer = await MultiStatusWriter.BeginAsync(context.Response, CancellationToken.None))
            await writer.WriteTruncatedAsync("/dav/x/default/", null, CancellationToken.None);

        // Not a bare 403, which rests on no text: § 8.6.2's shape is what clients already read.
        var response = XDocument.Parse(ReadBody(context.Response)).Root!.Elements(DavXml.Dav + "response").Single();
        Assert.Contains("507 Insufficient Storage", response.Element(DavXml.Dav + "status")!.Value);
        Assert.Single(response.Descendants(DavXml.Dav + "number-of-matches-within-limits"));
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~MultiStatusWriterTests`
Expected : ne compile pas.

- [ ] **Step 3 : Écrire `DavHeaders`**

Les constantes du tableau « Valeurs fixées une fois », plus `ApplyDav`. Écrire dans le commentaire
que `DAV:` est posé **aussi sur les réponses `PROPFIND`** et pourquoi.

- [ ] **Step 4 : Écrire `MultiStatusWriter`**

Un `XmlWriter` créé par `XmlWriter.CreateAsync` sur `Response.Body` avec
`XmlWriterSettings { Async = true, Encoding = new UTF8Encoding(false) }`. `BeginAsync` pose le
statut, le type de contenu et l'en-tête `DAV:` **avant** d'écrire quoi que ce soit, puis ouvre
`multistatus` avec ses trois espaces de noms déclarés une fois sur la racine — les redéclarer par
élément multiplierait le poids du document par deux sur un carnet complet.

Une table de correspondance code → ligne de statut, **littérale**, et non `ReasonPhrases.GetReasonPhrase` :
la table du framework a déjà changé de casse entre versions, et ces chaînes sont comparées octet à
octet par au moins un client.

`WriteResourceAsync` écrit `response`, `href`, puis le `propstat` **trouvé** (s'il y a des
propriétés), puis le `propstat` **manquant** (s'il y en a). L'ordre est le premier des trois
invariants littéraux.

- [ ] **Step 5 : Lancer les tests**

Run : `cd src && dotnet test --filter FullyQualifiedName~MultiStatusWriter`
Expected : les dix cas PASS.

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 6 : Commit**

- sujet : `feat(dav): l'ecrivain multistatus, au fil de l'eau et litteral`
- corps : `Le propstat 200 avant le 404, les lignes de statut ecrites a la main, et` /
  `le status d'une tombe en enfant direct de sa response.`

---

## Paquet 2 — la lecture du carnet et le jeu de propriétés

### Task 4 : `IDavContactReader` — lire le carnet comme le protocole le voit

Le protocole ne voit pas le carnet comme le webmail : il ne lit que les fiches **visibles**, il les
adresse par `dav_name` et non par identifiant, et il a besoin de la carte brute plutôt que de la
projection. Un dépôt à part plutôt qu'un élargissement d'`IContactStore` : les deux vues n'ont ni
la même clé ni les mêmes colonnes, et les mélanger ferait porter à chaque écran du webmail les
colonnes que seul DAV lit.

**La clause de visibilité porte trois conditions, jamais une**, et elle est déjà écrite au plan a :

```csharp
c.DavName != null && c.VCardRaw != null && c.CardHash != ""
```

`dav_name IS NOT NULL` est la plus visible, mais elle laisse passer deux voisines nées du
rattrapage de 4a. Une fiche que **ce** rattrapage-là aurait manquée sortirait avec un corps vide et
un `ETag: ""` — syntaxiquement valide, sémantiquement faux, et rangé par le client comme n'importe
quelle autre valeur, pour toujours. **Un ETag vide est précisément le genre de valeur qu'aucune
assertion ne regarde, parce qu'elle a l'air d'une valeur.**

**Files :**
- Create : `src/snoopy.microservice/Repositories/IDavContactReader.cs`
- Create : `src/snoopy.microservice/Repositories/DavContactReader.cs`
- Create : `src/snoopy.microservice/Models/Contacts/DavCard.cs`
- Modify : `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/DavContactReaderTests.cs`

**Interfaces :**
- Produit, consommé par les tâches 6 à 9 et par le plan c :

```csharp
/// One card as the protocol serves it. `VCardRaw` is the sovereign bytes; `CardHash` is their
/// SHA-256 and therefore the ETag; `UpdatedAt` is what getlastmodified renders.
internal sealed record DavCard(
    Guid ContactId, string DavName, string Uid, string VCardRaw, string CardHash,
    DateTime UpdatedAt, ulong SyncSequence);

internal interface IDavContactReader
{
    /// Every visible card of the book, streamed rather than listed: a full book with address-data
    /// runs to gigabytes, and the writer emits one response at a time.
    IAsyncEnumerable<DavCard> StreamAsync(Guid userId, CancellationToken cancellationToken);

    /// One card by its resource name. Null when this user does not own it, and equally when it is
    /// invisible to the protocol — the two are the same 404 to a client.
    Task<DavCard?> FindAsync(Guid userId, string davName, CancellationToken cancellationToken);

    /// The cards a multiget names, in one query rather than N. Names this user does not own simply
    /// do not come back, and the caller answers 404 inside the multistatus for each.
    Task<IReadOnlyList<DavCard>> FindManyAsync(
        Guid userId, IReadOnlyList<string> davNames, CancellationToken cancellationToken);

    /// How many visible cards the book holds — what a Depth: 1 PROPFIND announces nothing of, but
    /// what the log line of decision 18 counts.
    Task<int> CountAsync(Guid userId, CancellationToken cancellationToken);
}
```

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Fact]
    public async Task ItStreamsTheVisibleCards()
    {
        using var context = NewContextWith(
            Visible("a.vcf"), Visible("b.vcf"));
        var reader = new DavContactReader(context);

        var cards = await reader.StreamAsync(UserId, CancellationToken.None).ToListAsync();

        Assert.Equal(["a.vcf", "b.vcf"], cards.Select(c => c.DavName).Order());
    }

    [Fact]
    public async Task ACardWithNoName_IsInvisible()
    {
        using var context = NewContextWith(Visible("a.vcf"), WithoutName());
        var reader = new DavContactReader(context);

        var cards = await reader.StreamAsync(UserId, CancellationToken.None).ToListAsync();

        // The backfill has not reached it. Serving it would build an href from a name that does not
        // exist, and a book that serves a dead href is one a client flags in error every cycle.
        Assert.Single(cards);
    }

    [Fact]
    public async Task ACardWithNoBody_IsInvisible()
    {
        using var context = NewContextWith(Visible("a.vcf"), WithoutCard("b.vcf"));
        var reader = new DavContactReader(context);

        var cards = await reader.StreamAsync(UserId, CancellationToken.None).ToListAsync();

        // The second of the three conditions: the 4a backfill missed this one, so it would go out
        // with an empty body.
        Assert.Single(cards);
    }

    [Fact]
    public async Task ACardWithAnEmptyHash_IsInvisible()
    {
        using var context = NewContextWith(Visible("a.vcf"), WithEmptyHash("b.vcf"));
        var reader = new DavContactReader(context);

        var cards = await reader.StreamAsync(UserId, CancellationToken.None).ToListAsync();

        // The third, and the one no assertion normally looks at: an ETag of "" is syntactically
        // valid and semantically false, and a client files it like any other value, for ever.
        Assert.Single(cards);
    }

    [Fact]
    public async Task AnotherUsersCard_IsNotFound()
    {
        using var context = NewContextWith(Visible("a.vcf"));
        var reader = new DavContactReader(context);

        Assert.Null(await reader.FindAsync(Guid.NewGuid(), "a.vcf", CancellationToken.None));
    }

    [Fact]
    public async Task FindingByName_IsCaseSensitive()
    {
        using var context = NewContextWith(Visible("Carte.vcf"));
        var reader = new DavContactReader(context);

        // The column collates utf8mb4_bin: two names differing only by case are two different URLs
        // for every HTTP client, and a case-insensitive collation would make them a uniqueness
        // conflict where the protocol sees two resources.
        Assert.NotNull(await reader.FindAsync(UserId, "Carte.vcf", CancellationToken.None));
        Assert.Null(await reader.FindAsync(UserId, "carte.vcf", CancellationToken.None));
    }

    [Fact]
    public async Task FindingMany_SkipsWhatTheUserDoesNotOwn()
    {
        using var context = NewContextWith(Visible("a.vcf"), Visible("b.vcf"));
        var reader = new DavContactReader(context);

        var found = await reader.FindManyAsync(
            UserId, ["a.vcf", "missing.vcf", "b.vcf"], CancellationToken.None);

        // A stale name in a client's list is a common case, not a fault: the caller answers 404
        // inside the multistatus for each one that did not come back.
        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task FindingMany_IsOneQueryAndNotOnePerName()
    {
        // Recorded rather than measured: the shape is `Where(c => names.Contains(c.DavName))`, and
        // a per-name loop over five thousand hrefs is five thousand round trips on a report whose
        // whole point is to be a batch read.
        using var context = NewContextWith(Visible("a.vcf"), Visible("b.vcf"));
        var reader = new DavContactReader(context);

        var found = await reader.FindManyAsync(UserId, ["a.vcf", "b.vcf"], CancellationToken.None);

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task Counting_CountsOnlyWhatIsVisible()
    {
        using var context = NewContextWith(Visible("a.vcf"), WithoutName(), WithEmptyHash("c.vcf"));
        var reader = new DavContactReader(context);

        Assert.Equal(1, await reader.CountAsync(UserId, CancellationToken.None));
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavContactReaderTests`
Expected : ne compile pas.

- [ ] **Step 3 : Écrire `DavCard` et l'interface**

- [ ] **Step 4 : Écrire `DavContactReader`**

Une constante privée porte la clause de visibilité **une fois**, en `Expression<Func<Contact, bool>>`
ou en méthode d'extension `IQueryable<Contact> Visible(this IQueryable<Contact> q, Guid userId)` —
la seconde se lit mieux au site d'appel et empêche une des quatre requêtes de l'oublier.
`StreamAsync` rend un `IAsyncEnumerable` par `AsAsyncEnumerable()`, **sans `ToListAsync`** : c'est
tout l'intérêt.

L'enregistrer : `services.AddScoped<IDavContactReader, DavContactReader>();`

- [ ] **Step 5 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 6 : Commit**

- sujet : `feat(dav): la lecture du carnet, bornee par la clause de visibilite`
- corps : `Trois conditions et non une : un ETag vide a l'air d'une valeur, donc` /
  `aucune assertion ne le regarde et un client le range pour toujours.`

---

### Task 5 : `DavProperties` — le jeu clos, `allprop`, `propname`, et la propriété qui manque

**Un client ne demande pas les propriétés qu'un serveur trouve intéressantes ; il demande celles
dont son écran a besoin, et traite l'absence comme un carnet cassé.** La liste est donc close et
écrite ici plutôt que découverte tranche après tranche par des rapports de bogue.

| Ressource | Propriétés servies |
|---|---|
| `/dav/` | `current-user-principal`, `principal-URL`, `resourcetype` (vide) |
| principal | `resourcetype` (`DAV:principal`), `current-user-principal`, `principal-URL`, `displayname` (l'adresse), `addressbook-home-set`, `principal-collection-set`, `supported-report-set` (`DAV:expand-property`), `alternate-URI-set` (vide), `group-membership` (vide) |
| home | `resourcetype` (collection), `displayname`, `current-user-principal` |
| carnet | `resourcetype` (`collection` + `CARDDAV:addressbook`), `displayname`, `getctag`, `sync-token`, `supported-report-set`, `supported-address-data`, `supported-collation-set`, `max-resource-size`, `current-user-principal`, `current-user-privilege-set`, `owner` |
| carte | `getetag`, `getcontenttype`, `getcontentlength`, `getlastmodified`, `resourcetype` (vide), `current-user-privilege-set`, `supported-report-set` (`multiget` + `query`) |

**Sept points méritent leur ligne, et six sont des pièges :**

1. **`getcontentlength` est un nombre d'OCTETS UTF-8**, `Encoding.UTF8.GetByteCount`, jamais un
   nombre de caractères. C'est déjà l'unité de `ContactStore.MaxCardBytes` et celle que
   `max-resource-size` impose. Une carte accentuée annoncerait sinon une longueur inférieure à son
   corps, et **un client qui coupe à la longueur annoncée recevrait une carte tronquée** — donc
   invalide, donc rejetée, sans que rien n'indique pourquoi.
2. **`getlastmodified` vient de `contacts.updated_at` et s'écrit en HTTP-date GMT**, jamais en ISO,
   que rien ne lit ici. `updated_at` bouge aussi sur un basculement d'étoile — entorse nommée à
   l'invisibilité de la décision 6, laissée telle quelle : aucun client ne se synchronise sur
   `getlastmodified`, tous suivent l'ETag et la séquence, qui ne bougent pas.
3. **`max-resource-size` vaut `ContactStore.MaxCardBytes`, la même constante et non un littéral
   recopié.** Une valeur annoncée que le store violerait, ou l'inverse, se paierait en cartes
   refusées sans que le client comprenne pourquoi.
4. **`current-user-privilege-set`, une fois servi, doit toujours porter `write` et
   `write-content`.** DAVx⁵ ne le demande qu'en CalDAV et Thunderbird écrit par défaut quand la
   propriété manque — mais **un jeu présent et incomplet met Thunderbird en lecture seule**. Le jeu
   est constant : `read`, `write`, `write-content`, `write-properties`, `bind`, `unbind`,
   `read-current-user-privilege-set`.
5. **`alternate-URI-set` et `group-membership` sont des éléments VIDES, et ils sont écrits.** Le
   RFC 3744 § 4 les rend obligatoires sur tout principal ; les omettre laisse un client conclure
   que le principal n'en est pas un.
6. **`getctag` est une extension, pas un RFC** — espace de noms CalendarServer. Il reste servi
   parce que DAVx⁵ le demande à chaque interrogation d'état et s'y replie quand `sync-collection`
   manque.
7. **`supported-report-set` est servi sur le principal ET sur les cartes**, pas seulement sur le
   carnet : le RFC 6352 § 8 l'exige sur les ressources d'adresse autant que sur les collections.

**`getctag` et `sync-token` : ce plan les ÉMET, le plan c les LIT.** Leur forme est fixée par la
décision 7 et se pose ici, une fois, dans `DavSyncToken` — l'émission est triviale et le carnet
doit porter son jeu de propriétés complet dès maintenant, sinon un client conclut que la collection
ne sait pas se synchroniser et se rabat sur le ctag pour toujours. **L'analyse et le refus
(`403 valid-sync-token`) appartiennent au plan c** : ce plan ne reçoit jamais de jeton.

```csharp
internal static class DavSyncToken
{
    /// "{epoch}:{seq}" — opaque, only ever compared to itself from one call to the next.
    internal static string Ctag(SyncState? state);

    /// "http://weesky.net/ns/sync/{epoch}/{seq}". An http URI under a domain we own is what sabre
    /// does; it is never dereferenced, only compared byte for byte. `urn:snoopy:` was ruled out —
    /// `snoopy` is not a registered NID and a token is a URI.
    internal static string Token(SyncState? state);
}
```

**Les deux portent l'epoch, et ce n'est pas décoratif.** Une séquence nue laisserait un trou par le
chemin de repli : après restauration, un client dormant qui revient quand la séquence a recru
jusqu'à sa valeur mémorisée verrait **un ctag égal sur un carnet divergent**, et sauterait la
resynchronisation. C'est le mode de défaillance silencieux de la décision 8, revenu par l'autre
interrogation d'état. **Le `0` d'un carnet sans ligne d'état reste un `0` nu** : un carnet qui n'a
jamais rien émis n'a rien à protéger, et le premier vrai ctag en différera toujours. Un test le
pose, sans quoi personne ne saura si `null` doit rendre `0` ou lever.

**Ce que l'on ne sert PAS, et qui doit ressortir en `propstat 404` plutôt qu'être omis :**
`DAV:supported-privilege-set`, `DAV:acl`, `DAV:resource-id`,
`{calendarserver}email-address-set`, `CARDDAV:directory-gateway`,
`CARDDAV:addressbook-description` et les propriétés WebDAV-Push de DAVx⁵. **iOS et DAVx⁵ les
demandent réellement** ; ce sont des `404` inoffensifs, nommés ici pour que 4d ne les prenne pas
pour un défaut. **L'omission pure est ce qui fait qu'un client attend indéfiniment une valeur qu'il
croit en route.**

**Files :**
- Create : `src/snoopy.microservice/Services/CardDav/DavProperties.cs`
- Create : `src/snoopy.microservice/Services/CardDav/DavPropertyRequest.cs`
- Create : `src/snoopy.microservice/Services/CardDav/DavSyncToken.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/CardDav/DavPropertiesTests.cs`

**Interfaces :**
- Consomme : `DavPaths`, `DavXml` (tâches 1–2) ; `DavCard` (tâche 4) ; `SyncState` (plan a).
- Produit, consommé par les tâches 6, 9 et 11 et par le plan c :

```csharp
/// What a PROPFIND body asked for. An empty body means AllProp (RFC 4918 § 9.1), and several
/// clients send one at discovery.
internal sealed record DavPropertyRequest(DavPropertyMode Mode, IReadOnlyList<XName> Names);

internal enum DavPropertyMode { Named, AllProp, PropName }

internal static class DavProperties
{
    /// The closed set for one resource, as (name, value) — the value being null for a property this
    /// resource does not carry, which the caller turns into the 404 propstat.
    internal static (List<XElement> Found, List<XName> Missing) Resolve(
        DavPropertyRequest request, DavResourceContext resource);
}

/// Everything the property set may need, gathered once rather than fetched per property.
internal sealed record DavResourceContext(
    DavResourceKind Kind, Guid UserId, string PrincipalAddress, DavCard? Card, SyncState? State);
```

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Fact]
    public void TheContentLength_CountsBytesAndNotCharacters()
    {
        var card = CardWith("BEGIN:VCARD\r\nFN:Ada Éléonore\r\nEND:VCARD\r\n");

        var value = Resolve(card, DavXml.Dav + "getcontentlength");

        // A client that cuts at the announced length would get a truncated card — invalid, rejected,
        // and with nothing to say why.
        Assert.Equal(Encoding.UTF8.GetByteCount(card.VCardRaw).ToString(), value);
        Assert.NotEqual(card.VCardRaw.Length.ToString(), value);
    }

    [Fact]
    public void TheLastModified_IsAnHttpDateInGmt()
    {
        var card = CardWith("BEGIN:VCARD\r\nEND:VCARD\r\n") with
        {
            UpdatedAt = new DateTime(2026, 8, 24, 13, 5, 0, DateTimeKind.Utc)
        };

        // Never ISO, which nothing reads here.
        Assert.Equal("Mon, 24 Aug 2026 13:05:00 GMT", Resolve(card, DavXml.Dav + "getlastmodified"));
    }

    [Fact]
    public void TheEtag_IsTheCardHashInQuotes()
    {
        var card = CardWith("BEGIN:VCARD\r\nEND:VCARD\r\n") with { CardHash = "abc123" };

        Assert.Equal("\"abc123\"", Resolve(card, DavXml.Dav + "getetag"));
    }

    [Fact]
    public void TheMaxResourceSize_IsTheStoresOwnConstant()
    {
        // Not a copied literal: an announced value the store would violate, or the reverse, is paid
        // for in cards refused without the client understanding why.
        Assert.Equal(ContactStore.MaxCardBytes.ToString(),
            ResolveCollection(DavXml.CardDav + "max-resource-size"));
    }

    [Fact]
    public void ThePrivilegeSet_AlwaysCarriesWriteAndWriteContent()
    {
        var element = ResolveCollectionElement(DavXml.Dav + "current-user-privilege-set");

        // A set that is PRESENT and INCOMPLETE puts Thunderbird in read-only mode — worse than not
        // serving it at all, which makes it write by default.
        var privileges = element.Descendants().Select(e => e.Name.LocalName).ToList();
        Assert.Contains("write", privileges);
        Assert.Contains("write-content", privileges);
    }

    [Fact]
    public void ThePrincipal_CarriesTheTwoEmptyRfc3744Properties()
    {
        var found = ResolvePrincipal(
            [DavXml.Dav + "alternate-URI-set", DavXml.Dav + "group-membership"]);

        // RFC 3744 § 4 makes them mandatory on any principal. Omitting them lets a client conclude
        // the principal is not one.
        Assert.Equal(2, found.Found.Count);
        Assert.Empty(found.Missing);
        Assert.All(found.Found, e => Assert.Empty(e.Elements()));
    }

    [Fact]
    public void TheCollection_IsBothACollectionAndAnAddressbook()
    {
        var element = ResolveCollectionElement(DavXml.Dav + "resourcetype");

        var kinds = element.Elements().Select(e => e.Name).ToList();
        Assert.Contains(DavXml.Dav + "collection", kinds);
        Assert.Contains(DavXml.CardDav + "addressbook", kinds);
    }

    [Fact]
    public void SupportedAddressData_AnnouncesBothVersions()
    {
        var element = ResolveCollectionElement(DavXml.CardDav + "supported-address-data");

        // The book stores both verbatim and serves what it holds: announcing 3.0 alone would make
        // half the answers a lie.
        var versions = element.Elements().Select(e => e.Attribute("version")!.Value).ToList();
        Assert.Equal(["3.0", "4.0"], versions.Order());
    }

    [Fact]
    public void ACardCarriesSupportedReportSet_WithMultigetAndQuery()
    {
        var element = ResolveCardElement(DavXml.Dav + "supported-report-set");

        // RFC 6352 § 8 requires it on address resources as much as on collections.
        var reports = element.Descendants().Select(e => e.Name.LocalName).ToList();
        Assert.Contains("addressbook-multiget", reports);
        Assert.Contains("addressbook-query", reports);
    }

    [Theory]
    [InlineData("supported-privilege-set")]
    [InlineData("acl")]
    [InlineData("resource-id")]
    public void APropertyWeDoNotCarry_ComesBackAsMissingRatherThanOmitted(string localName)
    {
        var resolved = ResolveCollectionRequest([DavXml.Dav + localName]);

        // Pure omission is what makes a client wait for ever for a value it believes is on its way.
        Assert.Empty(resolved.Found);
        Assert.Single(resolved.Missing);
    }

    [Fact]
    public void AnEmptyBook_HasABareZeroForItsCtag()
    {
        // A book that has never emitted anything has nothing to protect, and the first real ctag
        // will always differ from it. Pinned because nobody would otherwise know whether null
        // should render "0" or throw.
        Assert.Equal("0", DavSyncToken.Ctag(null));
    }

    [Fact]
    public void TheCtagAndTheToken_BothCarryTheEpoch()
    {
        var epoch = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var state = new SyncState(epoch, 42, 0);

        // A bare sequence would leave a hole through the fallback path: after a restore, a sleeping
        // client returning once the sequence has grown back to its remembered value would see an
        // EQUAL ctag on a divergent book, and skip resynchronising.
        Assert.Equal($"{epoch}:42", DavSyncToken.Ctag(state));
        Assert.Equal($"http://weesky.net/ns/sync/{epoch}/42", DavSyncToken.Token(state));
    }

    [Fact]
    public void AllProp_LeavesOutSyncTokenAndThePrivilegeSet()
    {
        var resolved = DavProperties.Resolve(
            new DavPropertyRequest(DavPropertyMode.AllProp, []), CollectionContext());

        // Both cost, and a client that wants them names them. Everything else of the closed set is
        // poured in even though its own RFC marks it "SHOULD NOT in allprop" — a stable set makes
        // approximate clients predictable, and that divergence is deliberate.
        var names = resolved.Found.Select(e => e.Name).ToList();
        Assert.DoesNotContain(DavXml.Dav + "sync-token", names);
        Assert.DoesNotContain(DavXml.Dav + "current-user-privilege-set", names);
        Assert.Contains(DavXml.CardDav + "supported-address-data", names);
        Assert.Contains(DavXml.CardDav + "max-resource-size", names);
    }

    [Fact]
    public void AllProp_NeverReportsAnythingMissing()
    {
        var resolved = DavProperties.Resolve(
            new DavPropertyRequest(DavPropertyMode.AllProp, []), CollectionContext());

        Assert.Empty(resolved.Missing);
    }

    [Fact]
    public void PropName_AnswersTheNamesWithoutTheValues()
    {
        var resolved = DavProperties.Resolve(
            new DavPropertyRequest(DavPropertyMode.PropName, []), CollectionContext());

        Assert.NotEmpty(resolved.Found);
        Assert.All(resolved.Found, e =>
        {
            Assert.Empty(e.Elements());
            Assert.Equal(string.Empty, e.Value);
        });
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavPropertiesTests`
Expected : ne compile pas.

- [ ] **Step 3 : Écrire `DavPropertyRequest` et son analyse**

Une méthode statique lisant un `XDocument?` : `null` → `AllProp` ; racine portant
`DAV:allprop` → `AllProp` ; `DAV:propname` → `PropName` ; `DAV:prop` → `Named` avec les `XName` de
ses enfants. **Reconnaître par espace de noms et nom local**, jamais par préfixe.

- [ ] **Step 4 : Écrire `DavProperties`**

Une table par `DavResourceKind` : `XName` → fabrique `Func<DavResourceContext, XElement?>`. `Resolve`
parcourt les noms demandés (ou toute la table en `AllProp`/`PropName`), appelle la fabrique, et
range le résultat dans `Found` ou le nom dans `Missing`.

Le mode `AllProp` **exclut** `sync-token` et `current-user-privilege-set` et rien d'autre. Le mode
`PropName` rend des éléments vides.

Écrire dans le fichier, en commentaire, les sept points de la liste ci-dessus qui sont des pièges —
ce sont ceux qu'une relecture ultérieure voudra rouvrir.

- [ ] **Step 5 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 6 : Commit**

- sujet : `feat(dav): le jeu de proprietes, clos et enumere`
- corps : `getcontentlength compte des octets, le jeu de privileges porte toujours` /
  `write, et une propriete absente sort en 404 plutot que d'etre omise.`

---

## Paquet 3 — `PROPFIND` et `GET`

### Task 6 : `PROPFIND` sur les cinq ressources, et le refus de `Depth: infinity`

C'est la tâche qui ouvre la première route. **Elle porte donc le piège que 4c-i a légué** : toute
route `/dav` s'annote `[Authorize(Policy = CardDavAuthenticationDefaults.PolicyName)]`, jamais un
`[Authorize]` nu — le schéma de défi par défaut reste JwtBearer, et un attribut sans politique
répondrait `WWW-Authenticate: Bearer` à un client qui n'a pas de jeton et ne sait pas en demander.
**Aucun test ne peut l'attraper** ; seule la relecture de l'attribut le peut.

**Un `PROPFIND` sans en-tête `Depth` vaut `Depth: infinity`** (RFC 4918 § 9.1), donc répond
`403 propfind-finite-depth` comme lui. sabre y devine `1`, Radicale `0` — deux réponses différentes
au même silence, ce qui est exactement pourquoi on ne devine pas. **Et le refus n'est pas
symétrique :** deviner `0` rendrait un `multistatus` **valide** ne portant que la collection, qu'un
client demandant `1` lit comme un carnet vide — **et un carnet vide, il l'applique en effaçant ses
copies locales**. Une erreur ne se confond avec rien ; une réponse correcte au mauvais `Depth`, si.

**Files :**
- Create : `src/snoopy.microservice/Controllers/CardDavController.cs`
- Create : `src/snoopy.microservice/Services/CardDav/DavDepth.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CardDavPropfindTests.cs`

**Interfaces :**
- Consomme : tout le paquet 1 et le paquet 2.
- Produit, consommé par les tâches 7 à 11 : le contrôleur et ses routes.

```csharp
internal enum DavDepthValue { Zero, One, Infinity }

internal static class DavDepth
{
    /// The header's value. An ABSENT header is Infinity — the RFC's own recommendation for the
    /// silence, and the value the collection refuses. This is the rule of PROPFIND and of no other
    /// verb: REPORT has its own depth semantics per report.
    internal static DavDepthValue? Parse(string? header);
}
```

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Fact]
    public async Task AnAbsentDepth_IsRefusedRatherThanGuessed()
    {
        var response = await Propfind(DavPaths.Collection(UserId), depth: null, body: null);

        // sabre guesses 1, Radicale guesses 0 — two different answers to the same silence. And
        // guessing 0 would give a VALID multistatus carrying only the collection, which a client
        // asking for 1 reads as an empty book — and an empty book it applies by erasing its copies.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "propfind-finite-depth", ConditionOf(response));
    }

    [Fact]
    public async Task DepthInfinity_IsRefusedTheSameWay()
    {
        var response = await Propfind(DavPaths.Collection(UserId), depth: "infinity", body: null);

        Assert.Equal(403, response.StatusCode);
    }

    [Fact]
    public async Task DepthZeroOnTheCollection_AnswersTheCollectionAlone()
    {
        var response = await Propfind(DavPaths.Collection(UserId), depth: "0", body: PropBody("displayname"));

        Assert.Equal(207, response.StatusCode);
        Assert.Single(ResponsesOf(response));
    }

    [Fact]
    public async Task DepthOneOnTheCollection_AnswersTheCollectionThenOneResponsePerCard()
    {
        GivenCards("a.vcf", "b.vcf");

        var response = await Propfind(DavPaths.Collection(UserId), depth: "1", body: PropBody("getetag"));

        var hrefs = HrefsOf(response);
        Assert.Equal(3, hrefs.Count);
        // The collection comes first and carries its trailing slash; the cards never do.
        Assert.Equal(DavPaths.Collection(UserId), hrefs[0]);
        Assert.DoesNotContain(hrefs.Skip(1), h => h.EndsWith('/'));
    }

    [Fact]
    public async Task DepthOne_LeavesOutTheCardsTheProtocolCannotSee()
    {
        GivenCards("a.vcf");
        GivenACardWithNoName();

        var response = await Propfind(DavPaths.Collection(UserId), depth: "1", body: PropBody("getetag"));

        // A book that serves a dead href is one a client flags in error on every cycle.
        Assert.Equal(2, HrefsOf(response).Count);
    }

    [Fact]
    public async Task DepthZeroOnTheHome_AnswersTheHomeAlone()
    {
        var response = await Propfind(DavPaths.Home(UserId), depth: "0", body: PropBody("displayname"));

        Assert.Single(ResponsesOf(response));
    }

    [Fact]
    public async Task DepthOneOnTheHome_AnswersTheHomeAndTheDefaultCollection()
    {
        var response = await Propfind(DavPaths.Home(UserId), depth: "1", body: PropBody("resourcetype"));

        var hrefs = HrefsOf(response);
        Assert.Equal([DavPaths.Home(UserId), DavPaths.Collection(UserId)], hrefs);
    }

    [Fact]
    public async Task ThePrincipal_AnswersItsHomeSet()
    {
        var response = await Propfind(DavPaths.Principal(UserId), depth: "0",
            body: PropBody("addressbook-home-set", DavXml.CardDav));

        var homeSet = XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.CardDav + "addressbook-home-set").Single();
        Assert.Equal(DavPaths.Home(UserId), homeSet.Element(DavXml.Dav + "href")!.Value);
    }

    [Fact]
    public async Task TheServiceRoot_AnswersCurrentUserPrincipal()
    {
        var response = await Propfind("/dav/", depth: "0", body: PropBody("current-user-principal"));

        var principal = XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "current-user-principal").Single();
        Assert.Equal(DavPaths.Principal(UserId), principal.Element(DavXml.Dav + "href")!.Value);
    }

    [Fact]
    public async Task TheBareRoot_AnswersCurrentUserPrincipalToo()
    {
        // A client given the bare host tries the root as much as the well-known; two more lines
        // spare it failing on a path we do not use ourselves.
        var response = await Propfind("/", depth: "0", body: PropBody("current-user-principal"));

        Assert.Equal(207, response.StatusCode);
    }

    [Fact]
    public async Task AnotherUsersPrincipal_Answers404AndNot403()
    {
        var response = await Propfind(DavPaths.Principal(Guid.NewGuid()), depth: "0", body: null);

        // A 403 would confirm the existence of the principal aimed at.
        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task AnEmptyBody_MeansAllprop()
    {
        var response = await Propfind(DavPaths.Collection(UserId), depth: "0", body: null);

        // RFC 4918 § 9.1, and several clients send one at discovery.
        Assert.Equal(207, response.StatusCode);
        Assert.NotEmpty(XDocument.Parse(await response.ReadAsync()).Descendants(DavXml.Dav + "displayname"));
    }

    [Fact]
    public async Task ARequestedPropertyWeDoNotCarry_ComesBackIn404_AfterThe200()
    {
        var response = await Propfind(DavPaths.Collection(UserId), depth: "0",
            body: PropBody("displayname", "acl"));

        var body = await response.ReadAsync();
        Assert.True(body.IndexOf("200 OK", StringComparison.Ordinal)
                    < body.IndexOf("404 Not Found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADtdInTheBody_Answers400()
    {
        var response = await Propfind(DavPaths.Collection(UserId), depth: "0",
            body: "<!DOCTYPE t [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><t>&x;</t>");

        Assert.Equal(400, response.StatusCode);
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~CardDavPropfindTests`
Expected : ne compile pas.

- [ ] **Step 3 : Écrire `DavDepth`**

Trois valeurs, l'absence valant `Infinity`. Écrire dans le commentaire que **c'est la règle du
`PROPFIND` et d'aucun autre verbe** : le `REPORT` a sa propre sémantique par rapport — la portée
d'`addressbook-query` est celle de son en-tête, `addressbook-multiget` va en `Depth: 0` avec ses
cibles dans le corps, et `sync-collection` n'en porte pas du tout, `DAV:sync-level` l'ayant
remplacé.

- [ ] **Step 4 : Écrire `CardDavController`**

Un contrôleur, `[ApiController]` **non** — les conventions de liaison de modèle d'`ApiController`
n'ont rien à faire ici, et son `400` automatique sur un `ModelState` invalide court-circuiterait
les réponses de ce protocole. `[Route("dav")]`, et
`[Authorize(Policy = CardDavAuthenticationDefaults.PolicyName)]` **sur la classe**.

`[AcceptVerbs("PROPFIND")]` pour la méthode — ASP.NET Core route les méthodes non standard ainsi, et
Kestrel les accepte sans configuration. `[RequestSizeLimit(1024 * 1024)]`.

Cinq routes, une par forme de ressource, plus la racine nue `/` — celle-ci **hors** de `/dav` mais
portant **la même politique d'autorisation** : un client qui commence par la racine avec le secret
de synchronisation recevrait sinon un défi `Bearer`, et c'est le symptôme que la décision 2 chasse.

**Le contrôle de propriété d'abord** : tout `{userId}` différent de l'utilisateur authentifié
répond `404`, pas `403`.

- [ ] **Step 5 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 6 : Relire l'attribut d'autorisation à l'œil**

Aucun test ne peut l'attraper. Vérifier de visu que la classe porte
`[Authorize(Policy = CardDavAuthenticationDefaults.PolicyName)]` et **pas** `[Authorize]`, et
consigner cette vérification dans le rapport de tâche.

- [ ] **Step 7 : Commit**

- sujet : `feat(dav): PROPFIND sur les cinq ressources`
- corps : `Un Depth absent est refuse plutot que devine : une reponse correcte au` /
  `mauvais Depth se lit comme un carnet vide, qu'un client applique.`

---

### Task 7 : `GET` et `HEAD` — la carte verbatim, son ETag, et le `304`

**Les octets servis sont exactement `vcard_raw`**, dont `card_hash` est le SHA-256 : l'ETag est
donc honnête par construction, et le `GET` sert **toujours le verbatim**, `Accept` ou pas. La
conversion de version est une affaire de `address-data` (tâche 8), jamais du `GET`.

**`HEAD` ne demande aucun travail** : ASP.NET Core le sert d'office sur une route `GET` — mêmes
en-têtes, `ETag` compris, corps vide. Ce qui ne se fait pas tout seul est de le **nommer** dans
`Allow` et dans `OPTIONS` (tâche 10), et un client qui ne l'y voit pas ne l'essaie pas.

**Files :**
- Modify : `src/snoopy.microservice/Controllers/CardDavController.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CardDavGetTests.cs`

**Interfaces :**
- Consomme : `IDavContactReader.FindAsync` (tâche 4), `EntityTagMatcher` (plan a, tâche 12),
  `DavHeaders` (tâche 3).

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Fact]
    public async Task ItServesTheStoredBytesUntouched()
    {
        const string card = "BEGIN:VCARD\nVERSION:3.0\nUID:u1\nFN:Ada\nEND:VCARD\n";
        GivenCard("a.vcf", card);

        var response = await Get(DavPaths.Card(UserId, "a.vcf"));

        // Line endings included: RFC 6350 wants CRLF, a client sending bare LF produces a
        // non-conforming card, and normalising it would be a TRANSFORMATION — hence a response with
        // no ETag, a re-read, and a card that never coincides with the client's. The server's job is
        // to hand any other client exactly what it received.
        Assert.Equal(card, await response.ReadAsync());
    }

    [Fact]
    public async Task ItAnswersTheThreeHeadersAClientReads()
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nEND:VCARD\r\n", hash: "abc123",
            updatedAt: new DateTime(2026, 8, 24, 13, 5, 0, DateTimeKind.Utc));

        var response = await Get(DavPaths.Card(UserId, "a.vcf"));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("\"abc123\"", response.Headers.ETag);
        Assert.Equal("text/vcard; charset=utf-8", response.ContentType);
        // The same source as getlastmodified, so the two never disagree.
        Assert.Equal("Mon, 24 Aug 2026 13:05:00 GMT", response.Headers.LastModified);
    }

    [Theory]
    [InlineData("\"abc123\"")]
    [InlineData("*")]
    [InlineData("W/\"abc123\"")]
    [InlineData("\"other\", \"abc123\"")]
    public async Task AConditionalGetCoveringTheCurrentEtag_Answers304(string ifNoneMatch)
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nEND:VCARD\r\n", hash: "abc123");

        var response = await Get(DavPaths.Card(UserId, "a.vcf"), ifNoneMatch: ifNoneMatch);

        // The full semantics the 4a residual asks for: a list of values, `*`, and weak tags compared
        // weakly on a read.
        Assert.Equal(304, response.StatusCode);
    }

    [Fact]
    public async Task A304_CarriesItsEtagAndNoBody()
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nEND:VCARD\r\n", hash: "abc123");

        var response = await Get(DavPaths.Card(UserId, "a.vcf"), ifNoneMatch: "\"abc123\"");

        Assert.Equal("\"abc123\"", response.Headers.ETag);
        Assert.Empty(await response.ReadAsync());
    }

    [Fact]
    public async Task AConditionalGetThatDoesNotCover_Answers200()
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nEND:VCARD\r\n", hash: "abc123");

        var response = await Get(DavPaths.Card(UserId, "a.vcf"), ifNoneMatch: "\"stale\"");

        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownName_Answers404()
    {
        var response = await Get(DavPaths.Card(UserId, "never-existed.vcf"));

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task ACardTheProtocolCannotSee_Answers404Too()
    {
        GivenACardWithAnEmptyHash("b.vcf");

        // Invisible and absent are the same 404 to a client: serving an empty body with an ETag of
        // "" would be filed like any other value, for ever.
        Assert.Equal(404, (await Get(DavPaths.Card(UserId, "b.vcf"))).StatusCode);
    }

    [Fact]
    public async Task AnotherUsersCard_Answers404()
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nEND:VCARD\r\n");

        Assert.Equal(404, (await Get(DavPaths.Card(Guid.NewGuid(), "a.vcf"))).StatusCode);
    }

    [Fact]
    public async Task AGetOnTheCollection_Answers405AndNot404()
    {
        var response = await Get(DavPaths.Collection(UserId));

        // Generic WebDAV clients try it. A 500 there makes them abandon the whole book; a 405 is an
        // answer every client knows how to file. The routes only bind GET on {name}, a segment the
        // collection URL does not present — so routing would otherwise give an accidental 404.
        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.CollectionAllow, response.Headers.Allow);
    }

    [Fact]
    public async Task AHead_CarriesTheSameHeadersAndNoBody()
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nEND:VCARD\r\n", hash: "abc123");

        var response = await Head(DavPaths.Card(UserId, "a.vcf"));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("\"abc123\"", response.Headers.ETag);
        Assert.Empty(await response.ReadAsync());
    }

    [Fact]
    public async Task AnAwkwardNameIsFoundThroughItsEscapedUrl()
    {
        GivenCard("un nom#?.vcf", "BEGIN:VCARD\r\nEND:VCARD\r\n");

        // The round trip DavPaths owns: the href we write is the URL that comes back.
        Assert.Equal(200, (await Get(DavPaths.Card(UserId, "un nom#?.vcf"))).StatusCode);
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~CardDavGetTests`
Expected : les douze cas FAIL.

- [ ] **Step 3 : Écrire la route**

Une méthode `[HttpGet("addressbooks/{userId:guid}/default/{*davName}")]` — `{*davName}` capte le
segment **entier**, suffixe compris. **Ne pas exiger `.vcf`** : le suffixe est une convention de
client, et un motif de route qui l'exigerait refuserait un nom par un `404` de routage plutôt que
par une réponse pensée.

L'ordre : propriété (`404` si l'utilisateur diffère), lecture (`404` si absente ou invisible),
`If-None-Match` (`304` avec l'ETag), puis le corps.

**Écrire les octets en UTF-8 sans BOM**, directement, sans passer par un formateur : un formateur
JSON ou texte réencoderait, et l'ETag cesserait de décrire ce qui sort.

- [ ] **Step 4 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 5 : Commit**

- sujet : `feat(dav): GET et HEAD servent la carte verbatim et son ETag`
- corps : `Fins de ligne comprises : normaliser serait transformer, donc une reponse` /
  `sans ETag et une carte qui ne coincide jamais avec celle du client.`

---

## Paquet 4 — les rapports de lecture

### Task 8 : `address-data` — le sous-ensemble demandé et la conversion de version

Deux services, réunis parce qu'ils décrivent la même chose : **ce que `address-data` rend n'est pas
forcément la carte stockée, et c'est une représentation, pas un nouvel état.**

**Le sous-ensemble.** `CARDDAV:address-data` peut demander certaines propriétés seulement
(`<CARDDAV:prop name="EMAIL"/>`). Rendre la carte entière serait la version silencieuse du défaut
que ce plan chasse, avec une conséquence de plus : **le client inscrirait une carte complète dans un
cache qu'il croit partiel, et la réécrirait telle quelle.** `BEGIN`, `END`, `VERSION` et `UID` sont
**toujours** conservés, sans quoi ce qui sort n'est pas une carte.

**La conversion.** L'attribut `version` porte `3.0` ou `4.0` — les deux que `supported-address-data`
annonce. Une version annoncée mais différente de celle de la carte stockée est **convertie dans la
réponse, jamais dans le stockage**. Ce n'est pas un confort : DAVx⁵ demande `version="4.0"` dès que
l'annonce porte le 4.0, Thunderbird écrit du 4.0 sans lire l'annonce, et iOS lit mal le 4.0 —
**sabre a retiré son annonce 4.0 en 2013 pour cette raison exacte et ne l'a rétablie qu'en livrant
cette même conversion.** Servir tel quel, c'est rejouer sa régression.

**Et la réponse porte quand même son `getetag`.** L'analogie avec le `PUT` transformé de la
décision 9 est **fausse** : `DAV:getetag` est une **propriété de la ressource**, pas l'empreinte du
corps qu'un `propstat` transporte. C'est la valeur que le client range pour savoir, au tour suivant,
s'il doit relire.

Une valeur hors de `3.0`/`4.0`, ou un `content-type` qui n'est pas `text/vcard`, répond
`403 supported-address-data`.

**Files :**
- Create : `src/snoopy.microservice/Services/CardDav/AddressDataRequest.cs`
- Create : `src/snoopy.microservice/Services/CardDav/AddressDataFilter.cs`
- Create : `src/snoopy.microservice/Services/CardDav/VCardVersionConverter.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/CardDav/AddressDataFilterTests.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/CardDav/VCardVersionConverterTests.cs`

**Interfaces :**
- Produit, consommé par la tâche 9 et par le plan c (`addressbook-query` le réutilise tel quel) :

```csharp
/// What a CARDDAV:address-data element asked for. `Version` null means "as stored".
internal sealed record AddressDataRequest(string? Version, IReadOnlyList<string> PropertyNames);

internal static class AddressDataFilter
{
    /// Parses the element, or throws DavPreconditionException(supported-address-data) on a version
    /// or a content-type outside what we announce.
    internal static AddressDataRequest Parse(XElement addressData);

    /// The card restricted to the requested property names. BEGIN, END, VERSION and UID always
    /// survive: without them what comes out is not a card.
    internal static string Restrict(string card, IReadOnlyList<string> propertyNames);
}

internal static class VCardVersionConverter
{
    /// The card as the requested version would spell it — a REPRESENTATION, never a new state. The
    /// stored card stays verbatim and its ETag stays the SHA-256 of what a GET serves.
    internal static string To(string card, string version);
}
```

- [ ] **Step 1 : Écrire les tests du filtre, rouges**

```csharp
    [Fact]
    public void RestrictingToOneProperty_KeepsTheCardAValidCard()
    {
        const string card =
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\nEMAIL:a@b.c\r\nTEL:+32\r\nEND:VCARD\r\n";

        var restricted = AddressDataFilter.Restrict(card, ["EMAIL"]);

        // Without BEGIN, END, VERSION and UID what comes out is not a card at all.
        Assert.Contains("BEGIN:VCARD", restricted);
        Assert.Contains("END:VCARD", restricted);
        Assert.Contains("VERSION:3.0", restricted);
        Assert.Contains("UID:u1", restricted);
        Assert.Contains("EMAIL:a@b.c", restricted);
        Assert.DoesNotContain("TEL:", restricted);
        Assert.DoesNotContain("FN:", restricted);
    }

    [Fact]
    public void RestrictingToNothing_ReturnsTheWholeCard()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\nEND:VCARD\r\n";

        // An address-data with no prop children means "the whole card", not "nothing".
        Assert.Equal(card, AddressDataFilter.Restrict(card, []));
    }

    [Fact]
    public void RestrictingKeepsAGroupedProperty_WhenItsNameMatches()
    {
        const string card =
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nitem1.EMAIL:a@b.c\r\nitem1.X-ABLabel:Work\r\nEND:VCARD\r\n";

        var restricted = AddressDataFilter.Restrict(card, ["EMAIL"]);

        // The group prefix is not the property name. Comparing the whole "item1.EMAIL" would drop a
        // property the client did ask for.
        Assert.Contains("item1.EMAIL:a@b.c", restricted);
    }

    [Fact]
    public void RestrictingKeepsAFoldedLineWhole()
    {
        var card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nNOTE:" + new string('a', 100) +
                   "\r\nEND:VCARD\r\n";

        var restricted = AddressDataFilter.Restrict(FoldedForm(card), ["NOTE"]);

        // A continuation line begins with a space and carries no name of its own: dropping it would
        // truncate the value it continues.
        Assert.Contains(new string('a', 100), Unfold(restricted));
    }

    [Theory]
    [InlineData("2.1")]
    [InlineData("5.0")]
    [InlineData("")]
    public void AVersionOutsideWhatWeAnnounce_IsRefused(string version)
    {
        var element = AddressDataElement(version: version);

        var thrown = Assert.Throws<DavPreconditionException>(() => AddressDataFilter.Parse(element));
        Assert.Equal(DavXml.CardDav + "supported-address-data", thrown.Condition);
    }

    [Fact]
    public void AContentTypeThatIsNotVcard_IsRefused()
    {
        var element = AddressDataElement(contentType: "text/plain");

        Assert.Throws<DavPreconditionException>(() => AddressDataFilter.Parse(element));
    }

    [Fact]
    public void NoVersionAttribute_MeansAsStored()
    {
        Assert.Null(AddressDataFilter.Parse(AddressDataElement()).Version);
    }
```

- [ ] **Step 2 : Écrire les tests du convertisseur, rouges**

```csharp
    [Fact]
    public void ConvertingToTheVersionItAlreadyIs_ChangesNothing()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\nEND:VCARD\r\n";

        Assert.Equal(card, VCardVersionConverter.To(card, "3.0"));
    }

    [Fact]
    public void ConvertingThreeToFour_RewritesTheVersionLine()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\nEND:VCARD\r\n";

        var converted = VCardVersionConverter.To(card, "4.0");

        Assert.Contains("VERSION:4.0", converted);
        Assert.DoesNotContain("VERSION:3.0", converted);
    }

    [Fact]
    public void ConvertingThreeToFour_TransposesPreference()
    {
        const string card =
            "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:A\r\nTEL;TYPE=CELL,PREF:+32\r\nEND:VCARD\r\n";

        var converted = VCardVersionConverter.To(card, "4.0");

        // What one version cannot carry is transposed by the two formats' public rules.
        Assert.Contains("PREF=1", converted);
        Assert.DoesNotContain(",PREF", converted);
    }

    [Fact]
    public void ConvertingFourToThree_TransposesItBack()
    {
        const string card =
            "BEGIN:VCARD\r\nVERSION:4.0\r\nUID:u1\r\nFN:A\r\nTEL;PREF=1:+32\r\nEND:VCARD\r\n";

        var converted = VCardVersionConverter.To(card, "3.0");

        Assert.Contains("PREF", converted);
        Assert.DoesNotContain("PREF=1", converted);
    }

    [Fact]
    public void ConvertingKeepsTheUidVerbatim()
    {
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:urn:uuid:aaaa\r\nFN:A\r\nEND:VCARD\r\n";

        var converted = VCardVersionConverter.To(card, "4.0");

        // The UID is the identity a client syncs on: a card that goes out with a different one is a
        // different card, which the client duplicates on its next sync.
        Assert.Contains("UID:urn:uuid:aaaa", converted);
    }

    [Fact]
    public void ConvertingDoesNotTouchTheStoredCard()
    {
        // Stated as a test because it is the whole point: converting on read touches no 4a
        // invariant. The stored card stays verbatim and its ETag stays the SHA-256 of what a GET
        // serves — a converted card is a REPRESENTATION, not a new state.
        const string card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:A\r\nEND:VCARD\r\n";

        VCardVersionConverter.To(card, "4.0");

        Assert.Equal("BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:A\r\nEND:VCARD\r\n", card);
    }
```

- [ ] **Step 3 : Lancer les deux fichiers pour les voir échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~AddressDataFilter|FullyQualifiedName~VCardVersionConverter"`
Expected : ne compile pas.

- [ ] **Step 4 : Écrire `DavPreconditionException`**

Une exception interne scellée portant son `XName Condition` et, facultativement, un `XElement`
de détail. Le contrôleur la traduit en `DavError.WriteAsync`. **C'est ce qui empêche un refus de
précondition de remonter en `500`.**

- [ ] **Step 5 : Écrire `AddressDataFilter`**

`Parse` lit les attributs `version` et `content-type`, et les enfants `CARDDAV:prop` avec leur
attribut `name`. `Restrict` déplie la carte, garde les lignes dont le nom — **groupe retiré** —
figure dans la liste ou vaut `BEGIN`, `END`, `VERSION`, `UID`, puis replie. Réutiliser
`VCardComposer.NameOf`, qui sait déjà retirer le préfixe de groupe, plutôt que d'en réécrire une
variante.

- [ ] **Step 6 : Écrire `VCardVersionConverter`**

**S'appuyer sur l'analyseur et le composeur de 4a**, pas sur une réécriture textuelle : ce sont eux
qui connaissent les règles de transposition entre les deux formats. La conversion est
`parse → set version → compose`, et ce qui ne se transpose pas se traite selon les règles publiques
des deux formats (`TEL;TYPE=PREF` ↔ `PREF=1`, etc.).

- [ ] **Step 7 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 8 : Commit**

- sujet : `feat(dav): address-data honore le sous-ensemble et la version demandes`
- corps : `Convertir a la lecture ne touche aucun invariant de 4a : la carte servie` /
  `est une representation, et son getetag reste celui de la ressource.`

---

### Task 9 : `REPORT` — `addressbook-multiget`, `expand-property`, et le refus nommé des deux autres

**Un `REPORT` dont le corps nomme un rapport inconnu répond `403 supported-report`** (RFC 3253),
jamais `400` ni `500`. Ce plan s'en sert pour documenter sa propre frontière : `addressbook-query`
et `sync-collection` **existent** au sens où ils sont nommés et refusés proprement, et le plan c
remplace le refus par une implémentation. Un `500` sur ces deux-là ferait boucler un client
indéfiniment sur un rapport qu'il croit temporairement cassé.

**`expand-property` se sert plutôt que de se refuser** : c'est un MUST double (RFC 6352 § 8.1,
RFC 3744 § 9.1) qu'**iOS exerce réellement à la découverte de principal**, et son contenu est
modeste — résoudre les propriétés-`href` que le corps nomme en réponses imbriquées.

**La borne du multiget est 5000 `href`.** Un multiget est une liste que le client compose ; rien
n'en borne la longueur côté protocole, et une requête de quelques kilo-octets ne doit pas pouvoir
demander cinquante mille lectures. Le RFC 6352 ne prévoit aucune précondition pour ce refus : le
dépassement répond donc par le motif que les clients savent déjà lire, **celui de la troncature
(§ 8.6.2)** — un `207` dont la `DAV:response` de la Request-URI porte `507` et
`number-of-matches-within-limits` — et **pas un `403` sec, qui n'est adossé à aucun texte**.

**Files :**
- Create : `src/snoopy.microservice/Services/CardDav/ReportRequest.cs`
- Create : `src/snoopy.microservice/Services/CardDav/MultigetReport.cs`
- Create : `src/snoopy.microservice/Services/CardDav/ExpandPropertyReport.cs`
- Modify : `src/snoopy.microservice/Controllers/CardDavController.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CardDavReportTests.cs`

**Interfaces :**
- Consomme : tout ce qui précède.
- Produit, consommé par le plan c : `ReportRequest.Kind`, que le plan c étend de deux valeurs.

```csharp
internal enum DavReportKind { Multiget, Query, SyncCollection, ExpandProperty, Unknown }

internal static class ReportRequest
{
    /// The report a body names, by namespace and local name of its root — never by prefix.
    internal static DavReportKind KindOf(XDocument body);
}

internal static class MultigetReport
{
    internal const int MaxHrefs = 5000;
}
```

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Fact]
    public async Task AMultiget_AnswersOneResponsePerNamedHref()
    {
        GivenCards("a.vcf", "b.vcf");

        var response = await Report(DavPaths.Collection(UserId), MultigetBody(
            DavPaths.Card(UserId, "a.vcf"), DavPaths.Card(UserId, "b.vcf")));

        Assert.Equal(207, response.StatusCode);
        Assert.Equal(2, ResponsesOf(response).Count);
    }

    [Fact]
    public async Task AMultiget_ServesTheCardInAddressData()
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:Ada\r\nEND:VCARD\r\n");

        var response = await Report(DavPaths.Collection(UserId),
            MultigetBody(DavPaths.Card(UserId, "a.vcf"), withAddressData: true));

        Assert.Contains("FN:Ada", AddressDataOf(response).Single());
    }

    [Fact]
    public async Task AMultiget_CarriesGetetagAlongsideAPartialAddressData()
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:A\r\nEMAIL:a@b.c\r\nEND:VCARD\r\n");

        var response = await Report(DavPaths.Collection(UserId),
            MultigetBody(DavPaths.Card(UserId, "a.vcf"), addressDataProps: ["EMAIL"]));

        // getetag is a PROPERTY OF THE RESOURCE, not the fingerprint of the body a propstat carries:
        // it is the value the client files to know, next time round, whether it must re-read.
        // The analogy with the transformed PUT of decision 9 is false, and it cost dearly.
        Assert.NotEmpty(XDocument.Parse(await response.ReadAsync()).Descendants(DavXml.Dav + "getetag"));
        Assert.DoesNotContain("FN:A", AddressDataOf(response).Single());
    }

    [Fact]
    public async Task AMultiget_ConvertsWhenAVersionIsAsked()
    {
        GivenCard("a.vcf", "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:u1\r\nFN:A\r\nEND:VCARD\r\n");

        var response = await Report(DavPaths.Collection(UserId),
            MultigetBody(DavPaths.Card(UserId, "a.vcf"), version: "4.0"));

        // DAVx5 asks for version="4.0" as soon as the announcement carries 4.0. Serving as stored
        // would replay sabre's 2013 regression.
        Assert.Contains("VERSION:4.0", AddressDataOf(response).Single());
    }

    [Fact]
    public async Task AnUnknownHref_Answers404InsideTheMultistatus()
    {
        GivenCards("a.vcf");

        var response = await Report(DavPaths.Collection(UserId), MultigetBody(
            DavPaths.Card(UserId, "a.vcf"), DavPaths.Card(UserId, "gone.vcf")));

        // The report is a batch read, and a stale name in a client's list is a common case, not a
        // fault. A global error would throw away the card that WAS found.
        Assert.Equal(207, response.StatusCode);
        Assert.Contains("404 Not Found", await response.ReadAsync());
        Assert.Equal(2, ResponsesOf(response).Count);
    }

    [Fact]
    public async Task AnHrefOutsideThisCollection_IsAlso404AndIsNeverFollowed()
    {
        GivenCards("a.vcf");

        var response = await Report(DavPaths.Collection(UserId),
            MultigetBody("/dav/addressbooks/" + Guid.NewGuid() + "/default/x.vcf"));

        Assert.Contains("404 Not Found", await response.ReadAsync());
    }

    [Fact]
    public async Task MoreThanFiveThousandHrefs_AnswersTheTruncationShape()
    {
        var body = MultigetBody(Enumerable.Range(0, MultigetReport.MaxHrefs + 1)
            .Select(i => DavPaths.Card(UserId, $"c{i}.vcf")).ToArray());

        var response = await Report(DavPaths.Collection(UserId), body);

        // Not a bare 403, which rests on no text: the shape clients already read.
        Assert.Equal(207, response.StatusCode);
        var responses = XDocument.Parse(await response.ReadAsync()).Descendants(DavXml.Dav + "response").ToList();
        var onRequestUri = responses.Single(r =>
            r.Element(DavXml.Dav + "href")!.Value == DavPaths.Collection(UserId));
        Assert.Contains("507", onRequestUri.Element(DavXml.Dav + "status")!.Value);
        Assert.Single(onRequestUri.Descendants(DavXml.Dav + "number-of-matches-within-limits"));
    }

    [Fact]
    public async Task AMultigetOnACard_IsServed()
    {
        GivenCards("a.vcf");

        // RFC 6352 § 8.7 defines multiget on address resources too, and supported-report-set says so
        // on each card — the routes must follow, or the header lies.
        var response = await Report(DavPaths.Card(UserId, "a.vcf"),
            MultigetBody(DavPaths.Card(UserId, "a.vcf")));

        Assert.Equal(207, response.StatusCode);
    }

    [Fact]
    public async Task ExpandProperty_ResolvesAnHrefPropertyIntoANestedResponse()
    {
        var response = await Report(DavPaths.Principal(UserId), ExpandPropertyBody(
            DavXml.CardDav + "addressbook-home-set", DavXml.Dav + "displayname"));

        // iOS exercises this at principal discovery; it is a double MUST, and refusing it would be
        // a divergence on the very first request of the pairing.
        Assert.Equal(207, response.StatusCode);
        var nested = XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.CardDav + "addressbook-home-set")
            .Descendants(DavXml.Dav + "response").Single();
        Assert.Equal(DavPaths.Home(UserId), nested.Element(DavXml.Dav + "href")!.Value);
    }

    [Theory]
    [InlineData("addressbook-query")]
    [InlineData("sync-collection")]
    public async Task AReportThisPlanDoesNotYetServe_Answers403SupportedReport(string localName)
    {
        var response = await Report(DavPaths.Collection(UserId), ReportBody(localName));

        // Named and refused rather than left to fall through: a 500 makes a client loop for ever on
        // a report it believes temporarily broken. Plan c replaces the refusal by an implementation.
        Assert.Equal(403, response.StatusCode);
        Assert.Equal(DavXml.Dav + "supported-report", ConditionOf(response));
    }

    [Fact]
    public async Task AnUnknownReport_Answers403SupportedReportToo()
    {
        var response = await Report(DavPaths.Collection(UserId), ReportBody("acl-principal-prop-set"));

        Assert.Equal(403, response.StatusCode);
    }

    [Fact]
    public async Task ADepthHeaderOnAReport_IsIgnoredRatherThanRefused()
    {
        GivenCards("a.vcf");

        // PROPFIND's rule is PROPFIND's alone: a report already says what it applies to, so there is
        // nothing to guess. Extending the refusal here would break all three reports.
        var response = await Report(DavPaths.Collection(UserId),
            MultigetBody(DavPaths.Card(UserId, "a.vcf")), depth: "infinity");

        Assert.Equal(207, response.StatusCode);
    }

    [Fact]
    public async Task ABodyOverOneMegabyte_Answers413()
    {
        var response = await Report(DavPaths.Collection(UserId), OversizedBody());

        Assert.Equal(413, response.StatusCode);
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter FullyQualifiedName~CardDavReportTests`
Expected : ne compile pas.

- [ ] **Step 3 : Écrire `ReportRequest`**

`KindOf` regarde **l'espace de noms et le nom local de la racine**, et rien d'autre.

- [ ] **Step 4 : Écrire `MultigetReport`**

Lire les `DAV:href`, **compter avant de résoudre** — dépasser la borne se refuse sans une seule
lecture —, passer chaque `href` par `DavPaths.Parse`, écarter ce qui n'est pas une carte de **cette**
collection et de **cet** utilisateur, appeler `FindManyAsync` une fois, puis écrire une `response`
par `href` **dans l'ordre du corps** : un `href` retrouvé porte ses propriétés, un `href` absent
porte `404`.

Écrire dans l'ordre du corps, et non dans celui de la base : un client qui apparie ses `response`
par position — il en existe — recevrait sinon les cartes mélangées.

- [ ] **Step 5 : Écrire `ExpandPropertyReport`**

Le corps porte des `DAV:property` avec un attribut `name` (et un `namespace` facultatif, `DAV:` par
défaut) qui peuvent s'imbriquer. Pour chaque propriété-`href` nommée, résoudre la ressource visée et
écrire à sa place une `DAV:response` imbriquée portant les propriétés que les enfants nomment.
Une profondeur d'imbrication est déjà bornée par `DavXmlReader`.

- [ ] **Step 6 : Câbler `REPORT` dans le contrôleur**

`[AcceptVerbs("REPORT")]` sur la collection, sur une carte et sur le principal.
`[RequestSizeLimit(1024 * 1024)]`. Un `switch` sur `ReportRequest.KindOf`, dont les branches
`Query`, `SyncCollection` et `Unknown` rendent toutes `403 supported-report` — **et une seule
branche `default`, pour que le plan c n'ait qu'à en retirer deux.**

- [ ] **Step 7 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 8 : Commit**

- sujet : `feat(dav): multiget et expand-property, les deux autres rapports nommes`
- corps : `Un href inconnu sort en 404 DANS le multistatus, et le depassement de` /
  `borne prend la forme de troncature que les clients savent deja lire.`

---

## Paquet 5 — la surface HTTP et le journal

### Task 10 : `OPTIONS`, les `405`, le `308` et `/.well-known/carddav`

Ce qui reste de la surface, et qui n'a l'air de rien jusqu'à ce qu'un client échoue dessus.

**Le well-known répond à TOUTE méthode, et sans authentification.** Un `[HttpGet]` ne suffit
pas : **DAVx⁵ et Thunderbird y envoient un `PROPFIND`, pas un `GET`**, et une redirection réservée
au `GET` leur rend un `405` au premier geste de la découverte. Le `301` porte un `Cache-Control`
borné — RFC 6764 § 5 le recommande, et **un `301` nu se met en cache indéfiniment** : changer un
jour le chemin `/dav` deviendrait impossible sur les appareils déjà appairés.

**Une URL de collection sans barre finale est redirigée en `308`, pas en `301`** : un `301`
autorise le client à rejouer en `GET`, ce qu'OkHttp nu fait pour tout verbe sauf `PROPFIND` — **un
`REPORT` redirigé y perdrait méthode et corps.** Le `308` préserve les deux pour tous.

**Files :**
- Create : `src/snoopy.microservice/Controllers/WellKnownController.cs`
- Modify : `src/snoopy.microservice/Controllers/CardDavController.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/WellKnownControllerTests.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CardDavSurfaceTests.cs`

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Theory]
    [InlineData("GET")]
    [InlineData("PROPFIND")]
    [InlineData("OPTIONS")]
    [InlineData("HEAD")]
    public async Task TheWellKnown_Redirects_WhateverTheMethod(string method)
    {
        var response = await Send(method, "/.well-known/carddav");

        // DAVx5 and Thunderbird send PROPFIND here, not GET: a redirect bound to GET hands them a
        // 405 on the very first gesture of discovery.
        Assert.Equal(301, response.StatusCode);
        Assert.Equal("/dav/", response.Headers.Location);
    }

    [Fact]
    public async Task TheWellKnown_IsAnonymous()
    {
        var response = await SendUnauthenticated("PROPFIND", "/.well-known/carddav");

        // A 401 on a public redirect is a gratuitous obstacle before the client even knows where to
        // authenticate.
        Assert.Equal(301, response.StatusCode);
    }

    [Fact]
    public async Task TheWellKnown_BoundsItsCaching()
    {
        var response = await Send("GET", "/.well-known/carddav");

        // A bare 301 caches for ever: changing the /dav path one day would become impossible on
        // devices already paired.
        Assert.NotNull(response.Headers.CacheControl);
        Assert.Contains("max-age", response.Headers.CacheControl);
    }

    [Fact]
    public async Task Options_AnnouncesTheComplianceClasses()
    {
        var response = await Send("OPTIONS", DavPaths.Collection(UserId));

        Assert.Equal(DavHeaders.ComplianceClasses, response.Headers.Dav);
    }

    [Fact]
    public async Task Options_AnswersUnauthenticatedToo()
    {
        var response = await SendUnauthenticated("OPTIONS", DavPaths.Collection(UserId));

        // A client asks for capabilities before it has credentials.
        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public async Task Options_OnACollection_AllowsTheFourCollectionMethods()
    {
        var response = await Send("OPTIONS", DavPaths.Collection(UserId));

        Assert.Equal(DavHeaders.CollectionAllow, response.Headers.Allow);
    }

    [Fact]
    public async Task Options_OnACard_NamesHeadAndReport()
    {
        GivenCards("a.vcf");

        var response = await Send("OPTIONS", DavPaths.Card(UserId, "a.vcf"));

        // HEAD because HTTP requires it as soon as GET exists, and a client that does not see it
        // there does not try it. REPORT because multiget and query answer on a card — omitting it
        // would make the header say the opposite of what the method answers.
        Assert.Equal(DavHeaders.CardAllow, response.Headers.Allow);
    }

    [Theory]
    [InlineData("MKCOL")]
    [InlineData("MKCALENDAR")]
    [InlineData("COPY")]
    [InlineData("MOVE")]
    [InlineData("ACL")]
    [InlineData("LOCK")]
    [InlineData("UNLOCK")]
    public async Task AMethodWeDoNotServe_Answers405WithAllow(string method)
    {
        var response = await Send(method, DavPaths.Collection(UserId));

        // LOCK and UNLOCK are in the list although DAV: 1, 3 already announces no locks: the
        // announcement says what we can do, the 405 says what we answer when a client has not read
        // it — and without it routing would give a 404, i.e. "this card does not exist" on a card
        // that does.
        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.CollectionAllow, response.Headers.Allow);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task AWriteOnTheCollection_Answers405(string method)
    {
        var response = await Send(method, DavPaths.Collection(UserId));

        // The routes only bind PUT and DELETE on {name}, a segment the collection URL — ending in a
        // slash — does not present: routing would give an accidental 404. And a DELETE of the
        // collection would erase the whole book, a gesture the product offers nowhere.
        Assert.Equal(405, response.StatusCode);
    }

    [Fact]
    public async Task ACollectionWithoutItsTrailingSlash_Answers308()
    {
        var response = await Send("PROPFIND", $"/dav/addressbooks/{UserId}/default");

        // 308 and not the 301 sabre and Radicale use: a 301 lets the client replay as GET, which
        // bare OkHttp does for every verb but PROPFIND — a redirected REPORT would lose its method
        // and its body.
        Assert.Equal(308, response.StatusCode);
        Assert.Equal(DavPaths.Collection(UserId), response.Headers.Location);
    }

    [Fact]
    public async Task AHomeWithoutItsTrailingSlash_Answers308Too()
    {
        var response = await Send("PROPFIND", $"/dav/addressbooks/{UserId}");

        Assert.Equal(308, response.StatusCode);
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~WellKnownController|FullyQualifiedName~CardDavSurface"`
Expected : ne compile pas.

- [ ] **Step 3 : Écrire `WellKnownController`**

`[AllowAnonymous]`, `[Route(".well-known/carddav")]`, `[AcceptVerbs("GET", "HEAD", "OPTIONS",
"PROPFIND", "PROPPATCH", "REPORT", "PUT", "DELETE")]` — ou `[AcceptVerbs]` sans restriction si le
routage l'autorise ; **le vérifier plutôt que le supposer**, et écrire dans le rapport de tâche ce
que le routage accepte réellement.

`301` vers `/dav/`, avec `Cache-Control: max-age=86400`.

- [ ] **Step 4 : Écrire `OPTIONS`, les `405` et le `308`**

`OPTIONS` : `[AllowAnonymous]` sur **cette méthode-là seulement**, sur toute URL `/dav`.

Les `405` : une méthode attrape-tout par forme de ressource, portant `Allow` et **rien d'autre**.
La poser après les routes réelles pour que la sélection d'action ne la préfère pas.

Le `308` : une route sur la forme sans barre, pour le home et pour la collection.

- [ ] **Step 5 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 6 : Commit**

- sujet : `feat(dav): OPTIONS, les 405, le 308 et le well-known`
- corps : `Le well-known repond a toute methode : DAVx5 et Thunderbird y envoient un` /
  `PROPFIND, et un GET seul leur rend 405 au premier geste.`

---

### Task 11 : `PROPPATCH` en `207`, et la ligne de journal

**`PROPPATCH` est la seule méthode non mutable qui n'est PAS un `405`, et la ranger avec les autres
serait faux à deux titres.** D'abord l'en-tête `DAV: 1` engage : RFC 4918 § 18.1 fait de la classe 1
la satisfaction de tous les MUST du document, et le § 9.2 exige `PROPPATCH` de **toute** ressource
conforme. Ensuite **Contacts.app d'Apple `PROPPATCH` la propriété `{calendarserver}me-card` sur le
home d'adresses** — pas sur le carnet — et **sabre documente que l'absence de prise en charge peut
le faire planter : pas abandonner le carnet, planter.**

La réponse est partout celle du RFC 4918 § 9.2.1 pour une propriété qu'on ne laisse pas écrire : un
`207` dont chaque `propstat` porte `403 Forbidden`. **Rien n'est stocké au passage**, et ce n'est pas
un oubli.

**Et le journal**, parce que le symptôme d'à peu près toutes les pannes de ce protocole est « le
carnet est vide côté client » — un en-tête `Authorization` avalé, un `PROPFIND` refusé par le
pare-feu, un rattrapage incomplet, un jeton refusé en boucle : **cinq causes, un seul symptôme, et
aucune trace côté serveur pour les séparer.**

**Files :**
- Create : `src/snoopy.microservice/Services/CardDav/DavRequestLog.cs`
- Modify : `src/snoopy.microservice/Controllers/CardDavController.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CardDavProppatchTests.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/CardDav/DavRequestLogTests.cs`

- [ ] **Step 1 : Écrire les tests, rouges**

```csharp
    [Theory]
    [InlineData("home")]
    [InlineData("collection")]
    [InlineData("card")]
    public async Task Proppatch_Answers207_Everywhere(string target)
    {
        var response = await Proppatch(UrlOf(target), SetBody(DavXml.Dav + "displayname", "X"));

        // DAV: 1 engages: § 18.1 makes class 1 the satisfaction of every MUST, and § 9.2 requires
        // PROPPATCH of every conforming resource. Answering 405 is a contradiction a conformance
        // test catches on its first pass.
        Assert.Equal(207, response.StatusCode);
    }

    [Fact]
    public async Task Proppatch_RefusesEachPropertyIn403()
    {
        var response = await Proppatch(DavPaths.Collection(UserId),
            SetBody(DavXml.Dav + "displayname", "X"));

        var propstat = XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "propstat").Single();
        Assert.Contains("403 Forbidden", propstat.Element(DavXml.Dav + "status")!.Value);
        Assert.Single(propstat.Element(DavXml.Dav + "prop")!.Elements(DavXml.Dav + "displayname"));
    }

    [Fact]
    public async Task Proppatch_OfMeCardOnTheHome_IsAnsweredAndNotCrashed()
    {
        var response = await Proppatch(DavPaths.Home(UserId),
            SetBody(DavXml.CalendarServer + "me-card", "/dav/addressbooks/x/default/a.vcf"));

        // Contacts.app writes it HERE, on the address home, and sabre documents that not supporting
        // it can make the client CRASH — not abandon the book, crash.
        Assert.Equal(207, response.StatusCode);
    }

    [Fact]
    public async Task Proppatch_StoresNothing()
    {
        await Proppatch(DavPaths.Collection(UserId), SetBody(DavXml.Dav + "displayname", "Renamed"));

        var after = await Propfind(DavPaths.Collection(UserId), "0", PropBody("displayname"));
        // Served does not mean stored: accepting me-card would want one more dead property in the
        // database, for a use no screen of the product renders.
        Assert.DoesNotContain("Renamed", await after.ReadAsync());
    }

    [Fact]
    public async Task Proppatch_NamesEveryPropertyTheBodyAsked()
    {
        var response = await Proppatch(DavPaths.Collection(UserId),
            SetBody(DavXml.Dav + "displayname", "X", DavXml.CardDav + "addressbook-description", "Y"));

        Assert.Equal(2, XDocument.Parse(await response.ReadAsync())
            .Descendants(DavXml.Dav + "prop").Single().Elements().Count());
    }
```

et pour le journal :

```csharp
    [Fact]
    public void TheLine_CarriesWhatSeparatesTheFiveCauses()
    {
        var logger = new Mock<ILogger<CardDavController>>();

        DavRequestLog.Write(logger.Object, new DavRequestTrace(
            Method: "REPORT", Resource: "/dav/addressbooks/x/default/", Depth: null,
            Report: "addressbook-multiget", TokenIn: null, TokenOut: null,
            Responses: 42, StatusCode: 207, Condition: null));

        // Five causes, one symptom — "the book is empty" — and no server-side trace to tell them
        // apart. This turns 4d's conformance work into log reading rather than packet capture.
        logger.VerifyInformationLoggedWithAll("REPORT", "addressbook-multiget", "42", "207");
    }

    [Fact]
    public void TheLine_NamesThePreconditionWhenThereIsOne()
    {
        var logger = new Mock<ILogger<CardDavController>>();

        DavRequestLog.Write(logger.Object, new DavRequestTrace(
            Method: "PROPFIND", Resource: "/dav/addressbooks/x/default/", Depth: null,
            Report: null, TokenIn: null, TokenOut: null,
            Responses: 0, StatusCode: 403, Condition: "propfind-finite-depth"));

        logger.VerifyInformationLoggedWithAll("propfind-finite-depth");
    }

    [Fact]
    public void TheLine_NeverCarriesAnIdentifierNorACard()
    {
        var logger = new Mock<ILogger<CardDavController>>();

        DavRequestLog.Write(logger.Object, new DavRequestTrace(
            Method: "GET", Resource: DavPaths.Card(UserId, "a.vcf"), Depth: null,
            Report: null, TokenIn: null, TokenOut: null,
            Responses: 1, StatusCode: 200, Condition: null));

        // The user in a log line is the principal's GUID — the one already in the URL. Never the
        // address, never the secret, never a card's content.
        logger.VerifyNoLoggedValueContains("@");
        logger.VerifyNoLoggedValueContains("BEGIN:VCARD");
    }
```

- [ ] **Step 2 : Lancer les tests pour les voir échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~CardDavProppatch|FullyQualifiedName~DavRequestLog"`
Expected : ne compile pas.

- [ ] **Step 3 : Écrire `PROPPATCH`**

Une méthode par forme de ressource — home, carnet, carte —, `[AcceptVerbs("PROPPATCH")]`. Lire le
corps pour en extraire **les noms** des propriétés que `DAV:set` et `DAV:remove` nomment, et écrire
un `multistatus` d'un seul `propstat` à `403 Forbidden` les portant tous. **Ne rien écrire en
base.**

- [ ] **Step 4 : Écrire `DavRequestLog`**

Un record `DavRequestTrace` et une méthode `Write` en journalisation **structurée** — jamais
d'interpolation. Un seul modèle de message, pour que la ligne se filtre.

L'appeler depuis chaque action du contrôleur, y compris les chemins d'erreur : **c'est le chemin
d'erreur qui a le plus besoin de la ligne.**

- [ ] **Step 5 : Lancer les tests**

Run : `cd src && dotnet test` puis `cd src && dotnet build`
Expected : suite verte, zéro avertissement.

- [ ] **Step 6 : Commit**

- sujet : `feat(dav): PROPPATCH repond 207 partout, et chaque requete laisse une ligne`
- corps : `Contacts.app ecrit son me-card sur le home, et sabre documente qu'un refus` /
  `peut le faire planter. Servi ne veut pas dire stocke.`

---

## Vérification de fin de plan

- [ ] `cd src && dotnet test` — les deux suites au vert.
- [ ] `cd src && dotnet build` — zéro avertissement.
- [ ] `git status` — `src/snoopy.microservice/ApiDocumentation.xml` non modifié.
- [ ] **Relire à l'œil** que `CardDavController` porte `[Authorize(Policy = CardDavAuthenticationDefaults.PolicyName)]` et non `[Authorize]`, et que `WellKnownController` porte `[AllowAnonymous]`. Aucun test ne peut le faire.
- [ ] Le plan a est exécuté, son DDL joué, et **son rattrapage confirmé à zéro ligne restante** — sinon un client qui s'appaire maintenant voit un carnet incomplet et efface ses copies.
- [ ] `Dav:PublicUrl` est renseignée, sinon l'onglet « Sync » ne montre aucune adresse et personne ne peut s'appairer.
- [ ] Contre un vrai client, une fois : appairer DAVx⁵ ou Thunderbird sur l'adresse de l'onglet « Sync », et vérifier que le carnet **se remplit**. C'est la seule vérification que la suite ne peut pas faire, et le symptôme qu'elle cherche — un carnet vide — est celui que tout le § « journal » existe pour diagnostiquer.

## Ce que ce plan ne fait pas, et qui appartient au plan c

- **Aucune écriture.** `PUT` et `DELETE` répondent `405` sur la collection et ne sont liés nulle part sur une carte ; le plan c les ajoute avec leurs préconditions, leurs tombes et leurs révisions.
- `addressbook-query`, son filtre, sa collation et sa borne `CARDDAV:limit`.
- `sync-collection`, le jeton, le ctag, leur epoch et le `403 valid-sync-token`.
- La traduction des attentes de verrou en `503 Retry-After` : rien de ce plan ne verrouille.
- **Le piège du limiteur après une régénération**, que 4c-i n'a pas pu fermer et que ce plan ne ferme pas non plus : `AuthAttemptThrottle` est `internal` et un contrôleur public ne peut pas le prendre en paramètre. Le plan c pose la couture ou l'assume. **Fermé en 4c-ii-c** (tâche 10) : `IAuthAttemptThrottle` est public — l'implémentation reste `internal` —, `ForgetIdentifier` efface la clé de l'identifiant à la régénération et à l'allumage, et la clé d'adresse reste debout.
- **Le résidu de soixante secondes sur la révocation** : `Forget` ne peut pas battre un `Store` concurrent. Le fermer demande un compteur de génération dans `IDavAuthenticationCache`. **Fermé en 4c-ii-c** (tâche 10) : `Generation` est lue avant la lecture en base et rendue au `Store`, qui refuse une entrée dont la génération a bougé.
- Aucune conformité client prouvée : c'est 4d, et l'ordre est délibéré — un défaut trouvé par `ccs-caldavtester` sur un serveur qui suit le RFC est un défaut du serveur ; trouvé sur un serveur écrit contre un client, il est indiscernable d'une divergence de ce client.

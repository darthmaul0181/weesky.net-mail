# Contacts 4d — la conformité : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Les rapports de sous-agents dans le scratchpad sont préfixés `4d-task-N-…`.

**Spec :** [`docs/superpowers/specs/2026-08-31-webmail-contacts-4d-conformance-design.md`](../specs/2026-08-31-webmail-contacts-4d-conformance-design.md) — toute décision citée ici (« décision N ») y renvoie. En cas de doute, la spec fait foi.

**Goal :** un harnais `ccs-caldavtester` rejouable contre dev, le `DELETE` du carnet qui le vide (décision 3), et le rapport de conformité prêt à recevoir les passages.

**Architecture :** l'outil (Python 2, archivé) est cloné épinglé dans un dossier ignoré et piloté par un script versionné qui engendre `serverinfo.xml` et épure les secrets de la sortie ; côté serveur, une seule nouveauté de protocole — `DELETE` sur la collection — branchée sur `ContactStore.DeleteManyAsync` qui archive et tombe déjà par lots.

**Tech stack :** .NET 10, ASP.NET Core, EF Core, xUnit 2.9.3, Moq 4.20.72 ; PowerShell 7 et Python 2.7.18 pour le harnais.

## Ce que ce plan suppose fait

4c livrée et vérifiée sur dev (branche `cardav`). Rien d'autre : les quatre tâches sont exécutables sans le compte de test ni Python 2 — seule la partie interactive (après les tâches) en a besoin.

## Ce que ce plan ne couvre pas — l'exécution interactive

Les passages de l'outil, le triage, la vague de correctifs issue du triage et les clients réels (spec, « Ordre d'exécution », étapes 1 à 5) se font **en session avec l'utilisateur** : ils demandent Python 2.7.18 installé, un compte dev dédié, des déploiements et deux appareils. Les tâches ci-dessous livrent tout ce qui se code à l'avance. **Ne pas pousser avant que le passage initial soit consigné** : la spec (décision 5) veut la mesure d'avant-correctif, et un push déploie dev.

## Global constraints

- Backend : `cd src && dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés) ; `cd src && dotnet build` doit rester à zéro avertissement.
- `src/snoopy.microservice/ApiDocumentation.xml` : le réverter avant chaque commit.
- Style C# : file-scoped namespaces, un type par fichier, constructeurs primaires, records pour les DTO, `sealed`, `internal` par défaut, `CancellationToken` sur tout `async`, `ILogger` structuré. Commentaires : jamais pour paraphraser le code.
- **Toute route `/dav` porte `[Authorize(Policy = CardDavAuthenticationDefaults.PolicyName)]`** — la nouvelle action DELETE est sur la classe qui le porte déjà ; ne pas l'en sortir.
- **Aucune réponse de la surface DAV n'est un `500`.** Le `DELETE` de la collection traduit le verrou perdu en `503`, comme chaque écriture.
- **Le secret n'est jamais journalisé ni versionné** : ni dans le dépôt (`serverinfo.local.json`, `serverinfo.xml` ignorés), ni dans `results/` (lignes `Authorization` épurées avant écriture), ni dans le rapport.
- Commits : concis, sujet + corps de 2 lignes max, jamais commencer ni finir par `@`, terminer par `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` et `Claude-Session: https://claude.ai/code/session_018GJLoMjHoo1ryYhnRebU3x`. Écrire le message via `git commit -F -` avec un heredoc (outil Bash), jamais un here-string PowerShell.

## Valeurs fixées une fois, à ne pas réinventer

| Constante | Valeur |
|---|---|
| Commit épinglé `ccs-caldavtester` | `bed21e5924275552c1561febc8203a9f194cf737` |
| Commit épinglé `ccs-pycalendar` | `a12dd4e1ce8822b022d4abf2cfe6cc93902ff03f` |
| Hôte cible | `api-dev.mail.weesky.net`, port `443`, `--ssl`, `authtype` `basic` |
| `$root:` | `/dav/` |
| `$addressbook:` | `default` |
| Features actives | `carddav`, `sync-report`, `current-user-principal`, `well-known`, `limits` |
| `CollectionAllow` (après tâche 3) | `OPTIONS, DELETE, PROPFIND, PROPPATCH, REPORT` |
| `HomeAllow` (nouveau, tâche 3) | `OPTIONS, PROPFIND, PROPPATCH, REPORT` |
| `DELETE` collection | `204`, corps vide, `DAV: 1, 3, addressbook` ; home/principal/racine restent `405` |
| Verrou perdu pendant le vidage | `503` (`DavWriteStatus.Busy`) |

---

### Task 1 : le harnais `tools/caldavtester/`

**Files:**
- Create: `tools/caldavtester/run.ps1`
- Create: `tools/caldavtester/serverinfo.template.xml`
- Create: `tools/caldavtester/serverinfo.local.example.json`
- Create: `tools/caldavtester/suites.txt`
- Create: `tools/caldavtester/README.md`
- Modify: `.gitignore` (racine du dépôt, à la fin)

**Interfaces:**
- Consumes: rien du code C#.
- Produces: `run.ps1 [-SetupOnly] [-Suites <fichiers>] [-PrintResponses]` — la commande que l'exécution interactive lancera.

Pas de TDD ici : c'est un script de lancement, sa preuve est la vérification manuelle de l'étape 6 (spec, « Tests »).

- [ ] **Step 1 : `.gitignore`**

Ajouter à la fin du `.gitignore` racine :

```gitignore
# CardDAV conformance harness (4d): pinned tool clones, generated config, scrubbed outputs
tools/caldavtester/.caldavtester/
tools/caldavtester/results/
tools/caldavtester/serverinfo.xml
tools/caldavtester/serverinfo.local.json
```

- [ ] **Step 2 : `serverinfo.local.example.json`**

```json
{
  "guid": "00000000-0000-0000-0000-000000000000",
  "email": "compte-de-test@weesky.be",
  "secret": "LE-SECRET-DAV-DE-L-ONGLET-SYNC"
}
```

- [ ] **Step 3 : `suites.txt`**

```text
# Suites CardDAV lancées (chemins relatifs à scripts/tests du tester).
# Exclues parce qu'elles testent CalendarServer, pas CardDAV (spec, « Le harnais ») :
#   sharing-*.xml, directory.xml, directory-gateway.xml, bulk.xml,
#   add-member.xml, default-addressbook.xml
# mkcol, copymove et aclreports sont lancées EN SACHANT qu'elles échoueront :
# leur échec mesure une divergence nommée (décision 4).
CardDAV/propfind.xml
CardDAV/proppatch.xml
CardDAV/put.xml
CardDAV/get.xml
CardDAV/reports.xml
CardDAV/sync-report.xml
CardDAV/errors.xml
CardDAV/errorcondition.xml
CardDAV/limits.xml
CardDAV/nonascii.xml
CardDAV/well-known.xml
CardDAV/current-user-principal.xml
CardDAV/mkcol.xml
CardDAV/copymove.xml
CardDAV/aclreports.xml
CardDAV/ab-client.xml
```

- [ ] **Step 4 : `run.ps1`**

```powershell
#requires -Version 7
[CmdletBinding()]
param(
    [switch]$SetupOnly,
    [string[]]$Suites,
    [switch]$PrintResponses
)
$ErrorActionPreference = 'Stop'

$testerCommit = 'bed21e5924275552c1561febc8203a9f194cf737'
$pycalendarCommit = 'a12dd4e1ce8822b022d4abf2cfe6cc93902ff03f'
$work = Join-Path $PSScriptRoot '.caldavtester'
$results = Join-Path $PSScriptRoot 'results'

# L'outil est du Python 2 (spec, décision 1) et ne tournera sur rien d'autre.
& py -2.7 -c 'import sys' 2>$null
if ($LASTEXITCODE -ne 0) { throw "Python 2.7 introuvable ('py -2.7'). Installer 2.7.18 : voir README.md." }

function Get-Pinned([string]$Name, [string]$Url, [string]$Commit) {
    $dir = Join-Path $work $Name
    if (-not (Test-Path $dir)) { git clone --quiet $Url $dir | Out-Null }
    git -C $dir -c advice.detachedHead=false checkout --quiet $Commit
    if ($LASTEXITCODE -ne 0) { throw "checkout $Commit a échoué dans $dir" }
    return $dir
}
$tester = Get-Pinned 'ccs-caldavtester' 'https://github.com/apple/ccs-caldavtester.git' $testerCommit
$pycalendar = Get-Pinned 'ccs-pycalendar' 'https://github.com/apple/ccs-pycalendar.git' $pycalendarCommit

# serverinfo.xml : le gabarit versionné + les trois valeurs du fichier local ignoré.
$localPath = Join-Path $PSScriptRoot 'serverinfo.local.json'
if (-not (Test-Path $localPath)) {
    throw 'serverinfo.local.json manquant : copier serverinfo.local.example.json et le remplir.'
}
$local = Get-Content $localPath -Raw | ConvertFrom-Json
foreach ($key in 'guid', 'email', 'secret') {
    if (-not $local.$key) { throw "serverinfo.local.json : champ '$key' vide." }
}
$serverinfoPath = Join-Path $PSScriptRoot 'serverinfo.xml'
(Get-Content (Join-Path $PSScriptRoot 'serverinfo.template.xml') -Raw).
    Replace('{guid}', $local.guid).Replace('{email}', $local.email).Replace('{secret}', $local.secret) |
    Set-Content -Path $serverinfoPath -NoNewline
if ($SetupOnly) { Write-Host "Prêt : $serverinfoPath"; exit 0 }

if (-not $Suites) {
    $Suites = Get-Content (Join-Path $PSScriptRoot 'suites.txt') |
        ForEach-Object { ($_ -split '#')[0].Trim() } | Where-Object { $_ }
}

# --print-details-onfail imprime la requête entière sur chaque échec, Authorization
# comprise : la sortie est épurée AVANT de toucher le disque (décision 6).
New-Item -ItemType Directory -Force $results | Out-Null
$out = Join-Path $results ("{0:yyyyMMdd-HHmmss}.txt" -f (Get-Date))
$flags = @('--ssl', '--print-details-onfail', '-s', $serverinfoPath)
if ($PrintResponses) { $flags += '--always-print-response' }
$env:PYTHONPATH = Join-Path $pycalendar 'src'
Push-Location $tester
try {
    & py -2.7 testcaldav.py @flags @Suites 2>&1 |
        ForEach-Object { "$_" -replace '(?i)^(.*Authorization:).*$', '$1 [scrubbed]' } |
        Tee-Object -FilePath $out | Select-Object -Last 60
}
finally {
    Pop-Location
    Remove-Item Env:PYTHONPATH -ErrorAction SilentlyContinue
}
Write-Host "`nSortie épurée : $out"
```

- [ ] **Step 5 : `serverinfo.template.xml` — dérivé du gabarit amont, jamais inventé**

Le schéma de ce fichier appartient à l'outil (`src/serverinfo.py`) ; on part donc de son propre
gabarit plutôt que d'écrire l'XML de mémoire :

1. Cloner l'outil (une fois, sans Python) :
   `git clone https://github.com/apple/ccs-caldavtester.git tools/caldavtester/.caldavtester/ccs-caldavtester`
   puis `git -C tools/caldavtester/.caldavtester/ccs-caldavtester checkout bed21e5924275552c1561febc8203a9f194cf737`.
2. Copier `tools/caldavtester/.caldavtester/ccs-caldavtester/scripts/server/serverinfo-template.xml`
   vers `tools/caldavtester/serverinfo.template.xml`.
3. Dans la copie :
   - `<host>` → `api-dev.mail.weesky.net`, `<sslport>` → `443`, `<authtype>` → `basic`
     (remplacer les gabarits `{hostname}`, `{sslport}`, `{authtype}` ; laisser `<nonsslport>` à une
     valeur quelconque, `--ssl` l'ignore).
   - `<features>` : ne garder **que** `carddav`, `sync-report`, `current-user-principal`,
     `well-known`, `limits` — supprimer ou commenter toutes les autres (`caldav` compris : sans lui,
     toutes les suites CalDAV seraient de toute façon ignorées, mais la spec veut la liste minimale).
   - Substitutions — poser exactement (les clés existent dans le gabarit ; ajuster leurs valeurs,
     et supprimer les blocs `<repeat>` d'utilisateurs multiples sauf l'utilisateur 1) :

     | Clé | Valeur |
     |---|---|
     | `$root:` | `/dav/` |
     | `$principalcollection:` | `/dav/principals/` |
     | `$principal1:` | `/dav/principals/{guid}/` |
     | `$userid1:` | `{email}` |
     | `$pswd1:` | `{secret}` |
     | `$addressbookhome1:` | `/dav/addressbooks/{guid}/` |
     | `$addressbook:` | `default` |
     | `$addressbookpath1:` | `/dav/addressbooks/{guid}/default` |
     | `$useradmin:` | `{email}` |
     | `$pswdadmin:` | `{secret}` |

   - Toute autre substitution que le gabarit définit à partir de celles-ci (chemins calendrier,
     `$principaluri1:`, notifications) : la laisser telle quelle si elle ne fait que composer les
     valeurs ci-dessus, la supprimer si elle référence un utilisateur 2+ supprimé. Le fichier doit
     rester un XML que `xmllint`/`[xml]` charge.
4. Vérifier : `pwsh -NoProfile -Command "[xml](Get-Content tools/caldavtester/serverinfo.template.xml -Raw) | Out-Null"` → aucun crash.

- [ ] **Step 6 : `README.md`**

Contenu (rédiger en anglais, comme le reste du dépôt versionné hors `docs/superpowers`) couvrant,
dans cet ordre, avec les commandes exactes :

1. What this is: the 4d conformance harness (link to the spec file).
2. Prerequisites: PowerShell 7; Python 2.7.18 from `https://www.python.org/downloads/release/python-2718/`
   (Windows x86-64 MSI installer), checked with `py -2.7 -V`; git.
3. The dedicated dev account: create a user on dev, enable the Sync tab, copy the three values into
   `serverinfo.local.json` (copied from `serverinfo.local.example.json`). **Never a personal
   account: every run empties its address book.** Regenerate the secret when the campaign ends.
4. Run: `pwsh -File tools/caldavtester/run.ps1` (all suites), `-Suites CardDAV/propfind.xml` (one),
   `-SetupOnly` (clone + config only), `-PrintResponses` (verbose replay of a failure).
5. Output: `results/<timestamp>.txt`, `Authorization` lines scrubbed; what goes into the report
   is copied from this file only.
6. Reading results: `[FAILED]`/`[OK]` per test, a file whose `<start>` failed is skipped entirely;
   `mkcol.xml`, `copymove.xml` and `aclreports.xml` are EXPECTED to fail (named divergences).

- [ ] **Step 7 : vérifier le harnais sans réseau ni compte**

```powershell
Copy-Item tools/caldavtester/serverinfo.local.example.json tools/caldavtester/serverinfo.local.json
pwsh -NoProfile -File tools/caldavtester/run.ps1 -SetupOnly
```

Attendu : soit `Prêt : …serverinfo.xml` (Python 2 présent), soit l'erreur nommée
`Python 2.7 introuvable…` — jamais une stack PowerShell. Si Python 2 est présent, ouvrir
`serverinfo.xml` engendré et vérifier que `{guid}` n'y figure plus. Puis
`git status --short` : seuls les cinq fichiers versionnés et `.gitignore` apparaissent —
ni `serverinfo.xml`, ni `serverinfo.local.json`, ni `.caldavtester/`.

- [ ] **Step 8 : supprimer le `serverinfo.local.json` d'essai et committer**

```bash
rm tools/caldavtester/serverinfo.local.json
cd /d/development/repos/weesky.net-mail && git add .gitignore tools/caldavtester && git commit -F - <<'EOF'
feat(dav): harnais ccs-caldavtester versionné, outil cloné épinglé

serverinfo engendré hors dépôt, sortie épurée des Authorization.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_018GJLoMjHoo1ryYhnRebU3x
EOF
```

---

### Task 2 : `DeleteAllAsync` — le writer qui vide le carnet

**Files:**
- Modify: `src/snoopy.microservice/Repositories/IDavContactWriter.cs` (après `DeleteAsync`, ~ligne 41)
- Modify: `src/snoopy.microservice/Repositories/DavContactWriter.cs` (après `DeleteAsync`, ~ligne 123)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/DavContactWriterTests.cs`

**Interfaces:**
- Consumes: `ContactStore.DeleteManyAsync(Guid userId, IReadOnlyList<Guid> ids, CancellationToken)` →
  `Task<int>` — lots de 100, une transaction et un rang par lot, archive `RevisionCause.Delete` puis
  `PlaceTombstoneAsync` par carte ; `DavOutcomeTranslator.IsTransient(Exception)` ;
  `Refused(DavWriteStatus)` (helper privé existant du writer).
- Produces: `Task<DavWriteOutcome> DeleteAllAsync(Guid userId, CancellationToken cancellationToken)`
  — `Deleted` (séquence 0 : les rangs sont pris par le store, un par lot) ou `Busy`. La tâche 3
  l'appelle.

- [ ] **Step 1 : les tests, dans `DavContactWriterTests` (mêmes helpers : `Writer`, `SyncStore`, `Context`, `ValidCard`, `GivenAnInvisibleRow`)**

```csharp
[Fact]
public async Task DeletingTheWholeBook_BuriesAndArchivesEveryVisibleCard()
{
    await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);
    await Writer.PutAsync(UserId, "b.vcf", ValidCard("u2", fn: "Grace"), CancellationToken.None);
    SyncStore.Invocations.Clear();

    var outcome = await Writer.DeleteAllAsync(UserId, CancellationToken.None);

    Assert.Equal(DavWriteStatus.Deleted, outcome.Status);
    Assert.Empty(Context.Contacts);
    SyncStore.Verify(s => s.ArchiveAsync(
        It.Is<ContactRevision>(r => r.Cause == RevisionCause.Delete), It.IsAny<CancellationToken>()),
        Times.Exactly(2));
    SyncStore.Verify(s => s.PlaceTombstoneAsync(
        UserId, "a.vcf", It.IsAny<ulong>(), It.IsAny<CancellationToken>()), Times.Once);
    SyncStore.Verify(s => s.PlaceTombstoneAsync(
        UserId, "b.vcf", It.IsAny<ulong>(), It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task DeletingTheWholeBook_SpansTheStoreBatches()
{
    // 101 cards: one over ContactStore.BatchSize, so the emptying MUST take two batch
    // transactions — two ranks — and still bury every card.
    for (var i = 0; i < 101; i++)
        await Writer.PutAsync(UserId, $"c{i}.vcf", ValidCard($"u{i}", fn: $"N{i}"), CancellationToken.None);
    SyncStore.Invocations.Clear();

    var outcome = await Writer.DeleteAllAsync(UserId, CancellationToken.None);

    Assert.Equal(DavWriteStatus.Deleted, outcome.Status);
    Assert.Empty(Context.Contacts);
    SyncStore.Verify(s => s.NextSequenceAsync(UserId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    SyncStore.Verify(s => s.PlaceTombstoneAsync(
        UserId, It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<CancellationToken>()), Times.Exactly(101));
}

[Fact]
public async Task DeletingTheWholeBook_LeavesInvisibleRowsAlone()
{
    // A row the 4a backfill has not reached was never served: the protocol cannot be asked to
    // delete it, and the webmail contact behind it must survive the book's emptying.
    await GivenAnInvisibleRow("ghost.vcf", uid: "u9");
    await Writer.PutAsync(UserId, "a.vcf", ValidCard("u1"), CancellationToken.None);
    SyncStore.Invocations.Clear();

    var outcome = await Writer.DeleteAllAsync(UserId, CancellationToken.None);

    Assert.Equal(DavWriteStatus.Deleted, outcome.Status);
    Assert.Equal("ghost.vcf", Assert.Single(Context.Contacts.Where(c => c.UserId == UserId)).DavName);
    SyncStore.Verify(s => s.PlaceTombstoneAsync(
        UserId, "a.vcf", It.IsAny<ulong>(), It.IsAny<CancellationToken>()), Times.Once);
    SyncStore.Verify(s => s.PlaceTombstoneAsync(
        UserId, "ghost.vcf", It.IsAny<ulong>(), It.IsAny<CancellationToken>()), Times.Never);
}

[Fact]
public async Task DeletingAnEmptyBook_IsDeletedAndWakesNobody()
{
    var outcome = await Writer.DeleteAllAsync(UserId, CancellationToken.None);

    // 204 on nothing, and NO rank taken: a rank consumed here would wake every client for a
    // change that never happened — the same rule DeleteAsync's refusals follow.
    Assert.Equal(DavWriteStatus.Deleted, outcome.Status);
    SyncStore.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    SyncStore.Verify(s => s.PlaceTombstoneAsync(
        It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<ulong>(), It.IsAny<CancellationToken>()), Times.Never);
}

[Fact]
public async Task DeletingTheWholeBook_TouchesOnlyItsOwner()
{
    var other = Guid.NewGuid();
    await Writer.PutAsync(other, "theirs.vcf", ValidCard("u5"), CancellationToken.None);
    await Writer.PutAsync(UserId, "mine.vcf", ValidCard("u1"), CancellationToken.None);

    await Writer.DeleteAllAsync(UserId, CancellationToken.None);

    Assert.Equal("theirs.vcf", Assert.Single(Context.Contacts).DavName);
}
```

- [ ] **Step 2 : vérifier qu'ils échouent**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavContactWriterTests`
Attendu : échec de compilation — `DeleteAllAsync` n'existe pas.

- [ ] **Step 3 : l'interface**

Dans `IDavContactWriter.cs`, après la déclaration de `DeleteAsync` :

```csharp
/// <summary>
/// Empties the book: every card the protocol serves is archived and buried, in the store's own
/// batches — one transaction and one rank per hundred, never one giant transaction (the store
/// says why). Answers <see cref="DavWriteStatus.Deleted"/>, on an already-empty book too — where
/// it takes NO rank, so a no-op wakes nobody. A lock race answers <see cref="DavWriteStatus.Busy"/>;
/// the batches already committed stay emptied and buried, which is what the client's retry
/// finishes rather than undoes. Rows the 4a backfill never reached are not the protocol's to
/// delete and survive untouched.
/// </summary>
Task<DavWriteOutcome> DeleteAllAsync(Guid userId, CancellationToken cancellationToken);
```

- [ ] **Step 4 : l'implémentation**

Dans `DavContactWriter.cs`, après `DeleteAsync` :

```csharp
public async Task<DavWriteOutcome> DeleteAllAsync(Guid userId, CancellationToken cancellationToken)
{
    // The reader's visibility clause, as in DeleteAsync: what the protocol never served, it
    // cannot be asked to delete. Ids only — DeleteManyAsync re-reads each batch under its lock.
    var ids = await context.Contacts
        .Where(c => c.UserId == userId && c.DavName != null && c.VCardRaw != null && c.CardHash != "")
        .Select(c => c.Id)
        .ToListAsync(cancellationToken);
    if (ids.Count == 0) return new DavWriteOutcome(DavWriteStatus.Deleted, null, null, 0);

    try
    {
        var buried = await store.DeleteManyAsync(userId, ids, cancellationToken);
        logger.LogInformation("DELETE of the book for {UserId} buried {Count} cards", userId, buried);
        return new DavWriteOutcome(DavWriteStatus.Deleted, null, null, 0);
    }
    catch (Exception e) when (DavOutcomeTranslator.IsTransient(e))
    {
        logger.LogWarning(e,
            "DELETE of the book for {UserId} lost a lock race; answering busy", userId);
        context.ChangeTracker.Clear();
        return Refused(DavWriteStatus.Busy);
    }
}
```

- [ ] **Step 5 : vérifier que tout passe**

Run : `cd src && dotnet test --filter FullyQualifiedName~DavContactWriterTests`
Attendu : PASS, les cinq nouveaux compris.

- [ ] **Step 6 : mutation — la preuve que les tests mordent**

Une à la fois, réverter entre chaque :
1. Retirer `&& c.VCardRaw != null && c.CardHash != ""` de la requête → `DeletingTheWholeBook_LeavesInvisibleRowsAlone` doit rougir.
2. Remplacer `if (ids.Count == 0) return …` par rien (laisser passer au store) → `DeletingAnEmptyBook_IsDeletedAndWakesNobody` doit rester vert (le store ne prend pas de rang sur zéro ligne) — si c'est le cas, garder le early-return quand même (il évite la transaction) mais noter dans le rapport de tâche que c'est le store qui porte la garantie.
3. Remplacer `userId` par `Guid.NewGuid()` dans le `Where` → `DeletingTheWholeBook_TouchesOnlyItsOwner` et les autres doivent rougir.

- [ ] **Step 7 : suite complète, build, commit**

Run : `cd src && dotnet test` puis `cd src && dotnet build` (zéro avertissement). Réverter
`ApiDocumentation.xml` si régénéré. Commit :

```bash
cd /d/development/repos/weesky.net-mail && git checkout -- src/snoopy.microservice/ApiDocumentation.xml 2>/dev/null; git add src/snoopy.microservice/Repositories/IDavContactWriter.cs src/snoopy.microservice/Repositories/DavContactWriter.cs src/snoopy.microservice/snoopy.microservice.Tests/Repositories/DavContactWriterTests.cs && git commit -F - <<'EOF'
feat(dav): DeleteAllAsync vide le carnet par lots, archive et tombe

Vider un carnet vide ne prend aucun rang ; verrou perdu = Busy.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_018GJLoMjHoo1ryYhnRebU3x
EOF
```

---

### Task 3 : `DELETE` sur la collection, et l'`Allow` du home qui ne ment pas

**Files:**
- Modify: `src/snoopy.microservice/Services/CardDav/DavHeaders.cs:14` (`CollectionAllow`, + `HomeAllow`)
- Modify: `src/snoopy.microservice/Controllers/CardDavController.cs` (nouvelle action ~ligne 236 ; split OPTIONS ~ligne 818 ; split 405 ~ligne 839)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CardDavDeleteTests.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CardDavSurfaceTests.cs`

**Interfaces:**
- Consumes: `IDavContactWriter.DeleteAllAsync(Guid, CancellationToken)` → `Task<DavWriteOutcome>`
  (tâche 2) ; `TracedAsync`, `AnswerOutcomeAsync`, `MethodNotAllowed`, `Capabilities` (privés
  existants du contrôleur) ; `DavPaths.Collection(Guid)`, `DavPaths.Home(Guid)`.
- Produces: `DavHeaders.HomeAllow` — la tranche n'a pas d'autre consommateur ; les tests l'assertent.

**Pourquoi `HomeAllow` :** aujourd'hui `OptionsCollection` et `MethodNotAllowedOnCollection` servent
racine, principal, home ET collection avec le même `CollectionAllow`. Y ajouter `DELETE` ferait
annoncer au home un verbe qu'il répond `405` — exactement le mensonge que le commentaire de ces
méthodes reproche à l'`Allow` du routage. D'où la scission (spec, décision 3 : « le `405` du home
porte son `Allow` inchangé »).

- [ ] **Step 1 : les tests du contrôleur**

Dans `CardDavDeleteTests` (helpers existants : `Delete(path)`, `GivenACardAndItsEtag`, `Writer`,
`server`, `DavPaths`) — le `Delete` existant accepte tout chemin, donc il sert tel quel.
**Étendre d'abord `DelegateToTheRealWriter`** pour qu'il délègue aussi `DeleteAllAsync` au vrai
writer (même motif que ses Setup existants sur `PutAsync`/`DeleteAsync`), sinon le premier test
verrait un mock muet répondre `Deleted` sans rien vider :

```csharp
[Fact]
public async Task ADeleteOfTheCollection_Answers204AndEmptiesTheBook()
{
    await GivenACardAndItsEtag("a.vcf");

    var response = await Delete(DavPaths.Collection(UserId));

    Assert.Equal(204, response.StatusCode);
    Assert.Equal("1, 3, addressbook", response.Header("DAV"));
    // Emptied, never gone: the card 404s, the collection still answers.
    Assert.Equal(404, (await server.SendAsync("GET", DavPaths.Card(UserId, "a.vcf"))).StatusCode);
    Writer.Verify(w => w.DeleteAllAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task ADeleteOfAnEmptyCollection_Answers204Too()
{
    Assert.Equal(204, (await Delete(DavPaths.Collection(UserId))).StatusCode);
}

[Fact]
public async Task ADeleteOfTheCollectionUnderALostLock_Answers503()
{
    Writer.Setup(w => w.DeleteAllAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Busy, null, null, 0));

    Assert.Equal(503, (await Delete(DavPaths.Collection(UserId))).StatusCode);
}

[Fact]
public async Task ADeleteOfTheHome_IsStill405()
{
    var response = await Delete(DavPaths.Home(UserId));

    Assert.Equal(405, response.StatusCode);
    Assert.Equal(DavHeaders.HomeAllow, response.Header("Allow"));
    Writer.Verify(w => w.DeleteAllAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
}

[Fact]
public async Task ADeleteOfSomeoneElsesCollection_Answers404()
{
    Assert.Equal(404, (await Delete(DavPaths.Collection(Guid.NewGuid()))).StatusCode);
}
```

Dans `CardDavSurfaceTests` :
- `AWriteOnTheCollection_Answers405` : retirer la ligne `[InlineData("DELETE")]` (le verbe est servi
  désormais) ; adapter le commentaire (« And a DELETE of the collection would erase the whole
  book… » ne tient plus : décision 3, il le vide et c'est servi).
- `Options_OnACollection_AllowsTheFourCollectionMethods` : renommer en
  `Options_OnACollection_AllowsTheFiveCollectionMethods` (l'assertion sur
  `DavHeaders.CollectionAllow` ne bouge pas).
- Partout où un test du home, du principal ou de la racine asserte
  `DavHeaders.CollectionAllow` — `AGetOnAResourceThatOnlyAnswersPropfind_Answers405WithAllow`,
  `Options_OnTheBareRoot_AnswersTheCapabilitiesToo`, et le cas home de
  `AMethodWeDoNotServe…` s'il y en a un — remplacer par `DavHeaders.HomeAllow`.
- Ajouter :

```csharp
[Fact]
public async Task Options_OnTheHome_DoesNotAnnounceDelete()
{
    var response = await server.SendAsync("OPTIONS", DavPaths.Home(UserId));

    // The home's Allow must not gain the collection's DELETE: announcing a verb that answers
    // 405 is the exact lie the routing Allow tells and these actions exist to avoid.
    Assert.Equal(200, response.StatusCode);
    Assert.Equal(DavHeaders.HomeAllow, response.Header("Allow"));
}
```

- [ ] **Step 2 : vérifier qu'ils échouent**

Run : `cd src && dotnet test --filter "FullyQualifiedName~CardDavDeleteTests|FullyQualifiedName~CardDavSurfaceTests"`
Attendu : échec de compilation (`HomeAllow` n'existe pas), puis, une fois compilable, `405` là où
`204` est attendu.

- [ ] **Step 3 : `DavHeaders`**

```csharp
internal const string CollectionAllow = "OPTIONS, DELETE, PROPFIND, PROPPATCH, REPORT";

/// <summary>
/// Root, principal and home: every collection verb but DELETE, which only the address book
/// serves (4d decision 3) — one shared string here and the home would announce a verb it 405s.
/// </summary>
internal const string HomeAllow = "OPTIONS, PROPFIND, PROPPATCH, REPORT";
```

- [ ] **Step 4 : le contrôleur**

Nouvelle action, à côté de `DeleteCardAsync` :

```csharp
/// <summary>
/// DELETE — the only book cannot go away, so deleting it EMPTIES it (4d decision 3): every
/// served card archived and buried in the store's batches, the collection immediately answering
/// again, empty. RFC 4918 § 9.6 minus one nuance the RFC does not forbid: the collection
/// reappears at once. This is the tester's model (DELETE then PUT into it) and DAVx5's
/// "Delete collection" gesture. No If-Match: the collection has no ETag to compare.
/// </summary>
[HttpDelete(CollectionRoute)]
public Task DeleteCollectionAsync(Guid userId, CancellationToken cancellationToken) =>
    TracedAsync(userId, DavResourceKind.Collection, async trace =>
        trace.Condition = await AnswerOutcomeAsync(
            await writer.DeleteAllAsync(AuthenticatedUser.WebmailUid, cancellationToken),
            cancellationToken));
```

Scission des OPTIONS — `OptionsCollection` perd ses quatre premières routes au profit d'une
méthode `OptionsHome` :

```csharp
[AcceptVerbs("OPTIONS", Route = "")]
[AcceptVerbs("OPTIONS", Route = "/")]
[AcceptVerbs("OPTIONS", Route = "principals/{userId:guid}")]
[AcceptVerbs("OPTIONS", Route = "addressbooks/{userId:guid}")]
[AllowAnonymous]
public void OptionsHome() => Capabilities(DavHeaders.HomeAllow);

[AcceptVerbs("OPTIONS", Route = CollectionRoute)]
[AllowAnonymous]
public void OptionsCollection() => Capabilities(DavHeaders.CollectionAllow);
```

Scission des 405 sans verbe, même découpe (`MethodNotAllowedOnCollection` ne garde que
`CollectionRoute`) :

```csharp
[Route("")]
[Route("principals/{userId:guid}")]
[Route("addressbooks/{userId:guid}")]
public void MethodNotAllowedOnHome() => MethodNotAllowed(DavHeaders.HomeAllow);

[Route(CollectionRoute)]
public void MethodNotAllowedOnCollection() => MethodNotAllowed(DavHeaders.CollectionAllow);
```

Le commentaire XML existant au-dessus du bloc (« Last on purpose, and bound to no verb… ») reste,
déplacé sur `MethodNotAllowedOnHome`. `GetCollection`, `PutCardAsync` et `DeleteCardAsync`
continuent de citer `CollectionAllow` : leurs URL sont bien celles de la collection.

- [ ] **Step 5 : vérifier que tout passe**

Run : `cd src && dotnet test --filter "FullyQualifiedName~CardDav"`
Attendu : PASS — y compris `CardDavRequestLogTests` et `CardDavNoFiveHundredTests`, que la scission
ne doit pas déranger.

- [ ] **Step 6 : mutation**

1. Rendre `DeleteCollectionAsync` aveugle au writer (répondre 204 sans appel) →
   `ADeleteOfTheCollection_Answers204AndEmptiesTheBook` rougit (le GET rend encore la carte).
2. Faire servir `CollectionAllow` par `OptionsHome` → `Options_OnTheHome_DoesNotAnnounceDelete`
   rougit.
3. Réverter les deux.

- [ ] **Step 7 : suite complète, build, commit**

Run : `cd src && dotnet test` puis `cd src && dotnet build` (zéro avertissement). Réverter
`ApiDocumentation.xml`. Commit :

```bash
cd /d/development/repos/weesky.net-mail && git checkout -- src/snoopy.microservice/ApiDocumentation.xml 2>/dev/null; git add src/snoopy.microservice/Services/CardDav/DavHeaders.cs src/snoopy.microservice/Controllers/CardDavController.cs src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CardDavDeleteTests.cs src/snoopy.microservice/snoopy.microservice.Tests/Controllers/CardDavSurfaceTests.cs && git commit -F - <<'EOF'
feat(dav): DELETE de la collection la vide en 204, l'Allow du home reste honnête

Le home, le principal et la racine gardent leur Allow sans DELETE.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_018GJLoMjHoo1ryYhnRebU3x
EOF
```

---

### Task 4 : le rapport, et la spec 4c qui a dérivé

**Files:**
- Create: `docs/superpowers/carddav-4d-conformance.md`
- Modify: `docs/superpowers/specs/2026-08-23-webmail-contacts-4c-carddav-design.md` (~ligne 1945, la puce « Pas de modèle d'ACL » de « Ce que la tranche ne fait pas »)

**Interfaces:** aucune — deux documents.

- [ ] **Step 1 : le squelette du rapport**

Créer `docs/superpowers/carddav-4d-conformance.md` (en français, comme les autres documents de
`docs/superpowers`) :

```markdown
# CardDAV 4d — rapport de conformité

Rapport de la tranche [4d](specs/2026-08-31-webmail-contacts-4d-conformance-design.md). Les
chiffres viennent de `tools/caldavtester/results/` (sortie épurée) ; rien ici ne se régénère,
chaque passage est recopié une fois et daté.

## 1. Passage initial — avant tout correctif

Date : — · commit serveur déployé : — · fichier : `results/—.txt`

| Suite | Tests | OK | Échecs | Ignorés | Fichier sauté |
|---|---|---|---|---|---|
| propfind.xml | | | | | |
| proppatch.xml | | | | | |
| put.xml | | | | | |
| get.xml | | | | | |
| reports.xml | | | | | |
| sync-report.xml | | | | | |
| errors.xml | | | | | |
| errorcondition.xml | | | | | |
| limits.xml | | | | | |
| nonascii.xml | | | | | |
| well-known.xml | | | | | |
| current-user-principal.xml | | | | | |
| mkcol.xml | | | | | |
| copymove.xml | | | | | |
| aclreports.xml | | | | | |
| ab-client.xml | | | | | |

Attendu avant mesure : `put.xml` et `sync-report.xml` sautent au `<start>` (`DELETE` de la
collection encore en 405) ; `mkcol.xml`, `copymove.xml` et `aclreports.xml` échouent (divergences
nommées).

## 2. Second passage — après le `DELETE` du carnet (décision 3)

Date : — · commit : — · fichier : `results/—.txt`

(mêmes colonnes ; seules les lignes qui ont bougé sont commentées)

## 3. Triage

Un verdict par échec (décision 4) : **défaut serveur** (corrigé, test cité), **divergence nommée**
(décision 4c citée), **défaut de l'outil** (RFC cité).

| Suite / test | Constat | Verdict | Référence | Suite donnée |
|---|---|---|---|---|
| | | | | |

## 4. Passage final — après la vague de correctifs

Date : — · commit : — · fichier : `results/—.txt`

(mêmes colonnes que le passage initial)

## 5. Clients réels

### Thunderbird (Windows, version —)

| # | Scénario (spec, décision 7) | Observé | Verdict |
|---|---|---|---|
| 1 | Appairage par l'adresse de l'onglet Sync | | |
| 2 | Création côté client → webmail | | |
| 3 | Création côté webmail → client | | |
| 4 | Modification dans chaque sens, photo comprise | | |
| 5 | Suppression dans chaque sens | | |
| 6 | Carte 4.0 croisée avec DAVx⁵ | | |
| 7 | Conflit : 412, le serveur gagne, l'autre version archivée | | |
| 8 | Régénération du secret | | |

### DAVx⁵ (Android, version —)

(scénarios 1 à 8, plus :)

| 9 | « Delete collection » : le carnet se vide et réapparaît vide | | |

## 6. Divergences nommées

Reprises de la spec 4d (décision 4), confirmées ou allongées par les passages :

| Divergence | Décision 4c | Constatée par |
|---|---|---|
| `Depth` ignoré sur `sync-collection` et `addressbook-query` | 7, 14 | |
| `PROPPATCH` refuse chaque propriété en `403` | 16 | |
| RFC 3744 non servi, `access-control` retiré | 13, revue 2 (P1) | |
| `address-data` en `propstat 404` dans `sync-collection` | 14 | |
| Plafond d'un mébioctet | 15 | |
| Pas de `MKCOL`, `COPY`, `MOVE` | 3, 16 | |

## 7. Points de guet Apple (hors tranche)

Le mébioctet face aux photos iOS, le `me-card` en `PROPPATCH`, la lecture du 4.0 : à rejouer avec
la liste de la décision 7 le jour où un appareil Apple se présente.
```

- [ ] **Step 2 : corriger la dérive de la spec 4c**

Dans `2026-08-23-webmail-contacts-4c-carddav-design.md`, section « Ce que la tranche ne fait
pas », remplacer la puce :

```markdown
- **Pas de modèle d'ACL.** `current-user-privilege-set` rend un jeu constant et `access-control` est
  annoncé parce que le RFC 6352 l'exige et que les clients le lisent (décision 13) ; la méthode `ACL`
  répond `405`, et les rapports de principal du RFC 3744 sont une divergence nommée (décision 13).
  Un carnet à un seul propriétaire n'a pas de politique
  à exprimer.
```

par :

```markdown
- **Pas de modèle d'ACL.** `current-user-privilege-set` rend un jeu constant ; `access-control` a
  été **retiré** de l'en-tête `DAV:` par la seconde revue (P1) — la classe engagerait les
  propriétés des § 5 et § 8 du RFC 3744 et la méthode `ACL`, qu'un carnet à un seul propriétaire
  ne sert pas. La méthode `ACL` répond `405`, et les rapports de principal du RFC 3744 restent une
  divergence nommée (décision 13).
```

Attention aux fins de ligne : le dépôt est en `autocrlf=true` et les outils d'édition écrivent du
LF — faire le remplacement via un script python qui lit `newline=''`, détecte la fin de ligne
dominante et normalise la chaîne cherchée dessus (voir la mémoire du projet), puis
`git update-index --refresh`.

- [ ] **Step 3 : commit**

```bash
cd /d/development/repos/weesky.net-mail && git add docs/superpowers/carddav-4d-conformance.md docs/superpowers/specs/2026-08-23-webmail-contacts-4c-carddav-design.md && git commit -F - <<'EOF'
docs(carddav): squelette du rapport 4d ; la spec 4c cesse d'annoncer access-control

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_018GJLoMjHoo1ryYhnRebU3x
EOF
```

---

## Après les tâches — l'exécution interactive (spec, « Ordre d'exécution »)

Rien ici n'est une tâche de sous-agent ; c'est la campagne, en session :

1. L'utilisateur installe Python 2.7.18, crée le compte dev, remplit `serverinfo.local.json`.
   **Premier passage** (`run.ps1`), consigné en section 1 du rapport — les commits des tâches 2-3
   existent en local mais **ne sont pas poussés** : la mesure précède le correctif.
2. Push (l'utilisateur le demande), déploiement dev, **second passage** → section 2.
3. Triage (décision 4) → section 3 ; une seule vague de correctifs serveur, chacun avec son test ;
   push, **passage final** → section 4.
4. Thunderbird puis DAVx⁵, scénarios de la décision 7 → section 5 ; correctifs seulement si un
   client réel en impose.
5. Clôture : secret du compte de test régénéré, rapport relu, divergences (section 6) rapprochées
   des passages.

## Vérification de fin de plan

- `cd src && dotnet test` vert, `dotnet build` à zéro avertissement.
- `git status --short` propre après un `run.ps1 -SetupOnly` (rien d'engendré n'est suivi).
- Le rapport existe avec ses sept sections ; la spec 4c ne prétend plus annoncer `access-control`.

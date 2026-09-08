# Agenda 5d — la vague de correctifs : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Les rapports de sous-agents dans le scratchpad sont préfixés `5dw-task-N-…`.

**Spec :** [la conception 5d](../specs/2026-09-07-webmail-calendar-5d-conformance-design.md) — toute
décision citée ici (« décision N ») y renvoie. La mesure qui fonde ce plan est le § 2 du
[rapport de conformité](../calendar-5d-conformance.md) : **c'est lui qui fait foi sur ce qui est un
défaut et ce qui n'en est pas**, et chaque tâche ci-dessous nomme les tests qui l'ont trouvé.

**Goal :** corriger les quarante et un échecs que le triage impute au serveur, plus la borne ouverte
de la décision 11 et la fuite d'état du harnais — **en une seule vague**, mesurée d'un bloc par le
passage final `-Protocol Both`.

**Architecture :** rien de neuf côté produit. Onze corrections dans du code déjà écrit : une porte
de `PUT` qui refuse ce que RFC 5545 refuse, un parseur de filtre qui nomme la bonne précondition,
une zone que la requête pose et que le rapport doit suivre, deux corps de réponse qui manquaient.
Le seul ajout de fichier est `Services/CalDav/CalendarRequestTimeZone.cs`, et le seul changement de
signature publique est celui de `TimeRangeSpec`, dont les bornes deviennent facultatives.

**Tech stack :** .NET 10, ASP.NET Core, EF Core (InMemory en test), Ical.Net 5.2.3, NodaTime TZDB,
xUnit 2.9.3, Moq 4.20.72, `DavTestServer`.

## Ce que ce plan suppose fait

La tranche 5d, tâches 1 à 9, livrée et squashée dans `c0192a48`, déployée sur dev. Le passage
initial (`results/20260908-221019-caldav.txt`) et le repère CardDAV
(`results/20260908-215646-carddav.txt`) sont consignés dans le rapport : **ils ne se refont pas**,
et c'est à eux que le passage final se compare.

La **tâche 10 de la tranche** — « une borne de `time-range` absente est servie ouverte » — n'a
jamais été exécutée : elle était réservée à cette vague. Elle est reprise ici en **tâche 7**,
intégralement réécrite pour tenir compte de ce que la mesure a appris (voir son préambule).

## Global constraints

- Backend : `cd src && dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés) ;
  `cd src && dotnet build` doit rester à zéro avertissement.
- `src/snoopy.microservice/ApiDocumentation.xml` : le réverter avant chaque commit — `dotnet test`
  le régénère avec des centaines de lignes sans rapport.
- **La suite CardDAV ne change pas de sens.** Le socle `Services/Dav` est partagé : après chaque
  tâche, `dotnet test` complet est vert. **Une assertion existante qui change de valeur attendue est
  une régression**, à deux exceptions nommées et à elles seules : la tâche 7 inverse les **trois**
  tests de `CalendarQueryFilterTests` qui figent la fermeture d'une borne absente, et la tâche 8 peut inverser des
  assertions qui figent un tout-journée à minuit UTC. Dans ces deux cas et nulle part ailleurs,
  **chaque inversion est justifiée par écrit contre RFC 4791 § 9.9 dans le rapport de tâche**, et une
  inversion non justifiée est refusée en revue.
- Style C# : file-scoped namespaces, un type par fichier, constructeurs primaires, records pour les
  DTO, `sealed`, `internal` par défaut, `CancellationToken` sur tout `async`, `ILogger` structuré.
  Commentaires **en anglais**, jamais pour paraphraser le code, trois lignes au plus.
- **Aucune réponse de la surface DAV n'est un `500`.** `CalDavNoFiveHundredTests` et
  `CardDavNoFiveHundredTests` le gardent ; aucune tâche ne les affaiblit. Un corps que le serveur ne
  sait pas juger est un `400` ou un `403` nommé, jamais une exception qui remonte.
- **Constantes, jamais de littéraux** : `DavHeaders.NoCache`, `OccurrenceExpander.MaxSpan`,
  `CalendarStore.DefaultDavName`, `CalDavError.*`, `CalendarPropertyValue.*`. Un test qui recopie une
  valeur au lieu de lire la constante est refusé en revue.
- **Rien ne se pousse sans demander.** L'utilisateur pousse lui-même. Aucun sous-agent ne fait
  `git push`.
- Commits : concis, sujet + corps de deux lignes au plus, jamais commencer ni finir par `@`,
  terminer par `Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>` puis
  `Claude-Session: https://claude.ai/code/session_014pZFNVjMycnMn6Crm84wUR`. Écrire le message avec
  `git commit -F -` et un heredoc (outil Bash), jamais un here-string PowerShell.

## Les modèles, et pourquoi

| # | Tâche | Implémentation | Revue | Pourquoi ce modèle |
|---|---|---|---|---|
| 1 | La marque d'ordre d'octets | Sonnet 5 | Sonnet 5 | trois lignes de produit, le plan porte le code ; le travail est dans les tests, qui sont mécaniques. Diff partagé avec le carnet, donc la revue lit surtout la non-régression |
| 2 | Le média, la méthode, l'UID | Sonnet 5 | Sonnet 5 | trois refus indépendants de deux lignes chacun, tous écrits ici, chacun avec son test |
| 3 | Ce que RFC 5545 refuse dans le corps | Opus 5 | Opus 5 | trois validations à écrire à la main, dont une marche de récurrence. **C'est la tâche où un faux refus casserait un client réel** : elle demande du jugement, pas de la transcription |
| 4 | `valid-filter` contre `supported-filter` | Sonnet 5 | Sonnet 5 | une distinction structurelle à poser dans un parseur déjà écrit et déjà testé |
| 5 | Le filtre et son composant | Sonnet 5 | Sonnet 5 | deux gardes, dont une d'une ligne ; le plan porte le code exact |
| 6 | Les répétitions d'une `VALARM` | Sonnet 5 | Sonnet 5 | une boucle dans une méthode qui tient à l'écran |
| 7 | La borne ouverte | Opus 5 | Opus 5 | change une signature que quatre appelants portent, inverse deux tests, et le plafond de marche doit rester fini |
| 8 | `<C:timezone>` | **Fable 5.1** | **Fable 5.1** | la plus grosse et la seule qui soit une fonctionnalité : une zone à faire descendre par trois chemins, la moitié « tout-journée » du même MUST, et onze échecs qui en dépendent. Le modèle le plus capable, des deux côtés |
| 9 | `MKCALENDAR` | Sonnet 5 | Sonnet 5 | deux corps de réponse dont les formes existent déjà (`WriteCreationRefusalAsync`) |
| 10 | `PROPPATCH remove` | Sonnet 5 | Sonnet 5 | une ligne dans `Judge`, et le carnet à vérifier à côté |
| 11 | `allprop` et le harnais | Sonnet 5 | Sonnet 5 | un `Excluding` et un fichier de suite ; aucun jugement |
| — | Revue finale de branche | — | **Fable 5.1** | onze diffs cumulés sur un socle partagé avec le carnet : c'est la seule relecture qui voit la vague entière |

Haiku n'est employé nulle part : chaque tâche porte au moins un test à écrire contre une réponse
HTTP, et le nombre de tours qu'y prend le modèle le moins cher coûte plus que l'écart de prix.

## Structure des fichiers

Un seul fichier naît. Tout le reste est une modification.

| Fichier | Tâche | Responsabilité |
|---|---|---|
| `Services/Dav/DavBody.cs` | 1 | décoder un corps ; **partagé avec CardDAV** |
| `Controllers/CalDavController.cs` | 2, 9 | le `Content-Type` du `PUT`, les deux corps de refus de `MKCALENDAR` |
| `Repositories/DavCalendarWriter.cs` | 2 | l'UID à l'écrasement |
| `Services/Calendar/IcsGuards.cs` | 2, 3 | ce qu'un corps de `PUT` doit être |
| `Services/Calendar/IcsDocument.cs` | 2 | distinguer « pas de l'iCalendar » de « de l'iCalendar invalide » |
| `Services/CalDav/CalendarQueryFilter.cs` | 4, 5, 7, 8 | analyser et évaluer un filtre |
| `Services/CalDav/TimeRangeSpec.cs`, `AbsentBound.cs` | 7 | une fenêtre dont une borne peut manquer |
| `Services/CalDav/FreeBusyReport.cs` | 7 | le seul appelant qui exige ses deux bornes |
| `Services/Calendar/OccurrenceExpander.cs` | 6, 7, 8 | la marche, le plafond, la pose d'un tout-journée |
| **`Services/CalDav/CalendarRequestTimeZone.cs`** | 8 | **nouveau** — lire le `<C:timezone>` d'une requête |
| `Services/CalDav/CalendarPropertyValue.cs` | 8 | ce qu'est « un objet iCalendar à un seul VTIMEZONE » |
| `Services/CalDav/CalendarQueryReport.cs`, `EventMemberSource.cs` | 8 | faire descendre la zone de la requête |
| `Services/CalDav/MkCalendarRequest.cs` | 9 | ce qu'un corps de création nomme, y compris ce qu'il ne devrait pas |
| `Services/CalDav/CalendarPropertyUpdate.cs` | 10 | ce qu'un `PROPPATCH` retire |
| `Services/CalDav/CalDavProperties.cs` | 11 | ce qu'`allprop` verse |
| `tools/caldavtester/suites/CalDAV/floating.xml` | 11 | la zone que le fichier trouve en arrivant |

## L'ordre, et pourquoi il est celui-là

1. **La marque d'ordre d'octets d'abord**, parce qu'elle est la seule à toucher le socle partagé
   avec le carnet : si elle casse quelque chose, on le voit sur un arbre encore propre.
2. **Puis la porte du `PUT`** (2, 3), qui ne dépend d'aucune autre et dont la tâche 3 est la plus
   risquée de la vague : la faire tôt laisse le temps de la reprendre.
3. **Puis le filtre**, en profondeur croissante : ce qu'il refuse (4), ce qu'il évalue (5), ce qu'il
   déroule (6).
4. **Puis la borne ouverte** (7), qui change la forme de `TimeRangeSpec` : après le filtre, pour
   qu'elle ne soit rebasée qu'une fois.
5. **Puis `<C:timezone>`** (8), qui consomme la forme finale de `TimeRangeSpec` et la pose finale
   d'un tout-journée. La plus grosse, et la dernière des sémantiques.
6. **Enfin la surface d'écriture** (9, 10) et les restes (11), qui ne croisent rien de ce qui
   précède.

Aucune tâche ne se pousse. **Le push est un geste unique, à la fin de la vague, sur demande** —
suivi du déploiement, puis du passage final `-Protocol Both` **après quinze minutes pleines**
(budget d'authentification, décision 2 ; le compteur est en mémoire, un redéploiement le vide).

---

### Task 1 : la marque d'ordre d'octets UTF-8 n'est pas du contenu

**Ce que le client fait, en clair** : un export Windows — Outlook, un script PowerShell, un vieil
Agenda — écrit trois octets invisibles, `EF BB BF`, avant la première ligne du fichier. Le fichier
commence donc par « ␛␛␛BEGIN:VCALENDAR » du point de vue de l'analyseur, qui ne reconnaît plus
`BEGIN:` et rend « ce n'est pas de l'iCalendar ». L'utilisateur voit un `403` sur un fichier
parfaitement valide.

**Ce que la mesure a trouvé** : `nonascii.xml` `Non-ascii calendar data` t1, plus ses quatre
cascades t2 à t5 — cinq échecs. `Resource/CalDAV/nonascii/6.ics` commence par ces trois octets
(vérifié à l'`od`). **Le défaut est dans `Services/Dav/DavBody`, donc il touche déjà le carnet** :
un `.vcf` exporté par Outlook est refusé aujourd'hui, sans qu'aucun test ne l'ait dit.

RFC 3629 § 6 : la marque est une **signature**, pas du contenu.

**Files:**
- Modify: `src/snoopy.microservice/Services/Dav/DavBody.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/Dav/DavBodyTests.cs` (créer),
  `…/Controllers/CalDavPutTests.cs`, `…/Controllers/CardDavPutTests.cs`

**Interfaces:**
- Consomme : `DavBody.TryDecode(ReadOnlySpan<byte>, out string?)` — signature inchangée.
- Produit : rien de nouveau. Le comportement change pour tout appelant : trois octets de tête
  disparaissent du texte rendu, donc de ce qui est stocké et de l'`ETag`, qui reste ainsi le
  condensé de ce qui est réellement conservé.

- [ ] **Step 1 : écrire les tests qui échouent**

Créer `snoopy.microservice.Tests/Services/Dav/DavBodyTests.cs` :

```csharp
using System.Text;
using weesky.Snoopy.Microservice.Services.Dav;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Dav;

/// <summary>The one door every DAV body comes through: strict UTF-8, and a leading signature that
/// is not content.</summary>
public sealed class DavBodyTests
{
    [Fact]
    public void AUtf8Signature_IsNotContent()
    {
        // RFC 3629 § 6: EF BB BF ahead of BEGIN: is a signature. Windows exporters write it, and
        // no iCalendar or vCard parser recognises the first line behind it.
        var body = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("BEGIN:VCALENDAR")).ToArray();

        Assert.True(DavBody.TryDecode(body, out var text));
        Assert.Equal("BEGIN:VCALENDAR", text);
    }

    [Fact]
    public void ASignatureAnywhereButTheHead_IsLeftAlone()
    {
        // U+FEFF is a zero-width no-break space anywhere else, and the ETag must describe what the
        // client sent.
        var body = Encoding.UTF8.GetBytes("SUMMARY:a\uFEFFb");

        Assert.True(DavBody.TryDecode(body, out var text));
        Assert.Equal("SUMMARY:a\uFEFFb", text);
    }

    [Fact]
    public void ASignatureAlone_DecodesToNothing()
    {
        Assert.True(DavBody.TryDecode(Encoding.UTF8.GetPreamble(), out var text));
        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void ABodyThatIsNotUtf8_IsStillRefused()
    {
        // The signature is stripped BEFORE the strict decode, never instead of it.
        Assert.False(DavBody.TryDecode(Encoding.Latin1.GetBytes("SUMMARY:caf\u00e9"), out _));
    }
}
```

Dans `Controllers/CalDavPutTests.cs`, une assertion de bout en bout — reprendre les aides du fichier
(`Put(...)`, `Ics.Single(...)` ou l'équivalent qu'il emploie ; **ne pas en inventer**) :

```csharp
    [Fact]
    public async Task ABodyOpeningOnAUtf8Signature_IsStored()
    {
        // The shape every Windows exporter writes. Refused until now with "the body is not
        // iCalendar text", on a file that is exactly that.
        var body = "\uFEFF" + Ics.Single("SUMMARY:conseil d'administration");

        var response = await Put(Calendar(), "bom.ics", body);

        Assert.Equal(201, response.StatusCode);
    }
```

Et le jumeau dans `Controllers/CardDavPutTests.cs`, sur une vCard : **c'est la seule preuve que la
correction sert aussi le carnet**, et le passage `-Protocol Both` la mesurera.

- [ ] **Step 2 : les faire échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~DavBodyTests|FullyQualifiedName~CalDavPutTests|FullyQualifiedName~CardDavPutTests"`
Expected : FAIL — `AUtf8Signature_IsNotContent` rend `"\uFEFFBEGIN:VCALENDAR"`, et les deux `PUT`
répondent `403`.

- [ ] **Step 3 : retirer la signature avant le décodage strict**

Dans `Services/Dav/DavBody.cs` :

```csharp
    /// <summary>RFC 3629 § 6: these three bytes are a signature, not content. Windows exporters
    /// write them ahead of BEGIN:, and every parser downstream then fails on the first line.</summary>
    private static readonly byte[] Signature = [0xEF, 0xBB, 0xBF];

    internal static bool TryDecode(ReadOnlySpan<byte> body, [NotNullWhen(true)] out string? text)
    {
        // Before the decode, never instead of it: what follows is still judged strictly.
        if (body.StartsWith(Signature)) body = body[Signature.Length..];

        try
        {
            text = Strict.GetString(body);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = null;
            return false;
        }
    }
```

Et compléter le résumé de classe, qui ne parle aujourd'hui que de l'ISO-8859-1 : une phrase sur la
signature, pas plus.

- [ ] **Step 4 : les faire passer, puis toute la suite**

Run : `cd src && dotnet test` Expected : PASS, y compris les deux tests de `DavContactWriterTests`
qui appellent déjà `TryDecode` (`:806` et `:814`) — ils ne portent aucune marque et ne bougent pas.

- [ ] **Step 5 : commit**

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice
git commit -F - <<'EOF'
fix(dav): la marque d'ordre d'octets UTF-8 n'est pas du contenu

RFC 3629 6 : EF BB BF est une signature. Corrige le PUT du carnet
autant que celui de l'agenda.
EOF
```

---

### Task 2 : le média, la méthode et l'UID — trois refus que la porte du `PUT` ne prononçait pas

**Ce que le client fait, en clair.** Trois situations, trois réponses qui manquent :
1. Il envoie un corps XML sous `Content-Type: text/xml` à une adresse d'agenda. Nous répondons « ce
   n'est pas de l'iCalendar valide » ; la vraie réponse est « ce n'est pas un média que j'accepte ».
2. Il envoie un `VCALENDAR` portant `METHOD:PUBLISH` — une invitation, pas une ressource d'agenda.
   Nous la stockons. RFC 4791 § 4.1 : « Calendar object resources contained in calendar collections
   **MUST NOT** specify the iCalendar METHOD property. »
3. Il écrase `1.ics` par un événement dont l'`UID` a changé. Nous acceptons, et l'identité que tous
   ses autres appareils ont mémorisée pour cette adresse change sous eux.

**Ce que la mesure a trouvé** : `errors.xml` `PUT` t1, t4 et t7 — trois échecs.

**Le quatrième point de cette tâche ne corrige aucun échec** et vient de l'arbitrage 2 du rapport :
les neuf `Problem VEVENTs` de `put.xml` sont **refusés à juste titre** (RFC 5545 § 3.8.2.2 fait de
l'identité de type entre `DTSTART` et `DTEND` un MUST), mais le refus est aujourd'hui **accidentel**
— un `catch (Exception)` générique — et son message dit « the body is not iCalendar text », **ce qui
est faux** : le corps *est* de l'iCalendar, seulement invalide. Le rendre délibéré et correctement
libellé fait partie de la vague ; l'accepter, non.

**Files:**
- Modify: `src/snoopy.microservice/Controllers/CalDavController.cs` (action `PutEventAsync`)
- Modify: `src/snoopy.microservice/Services/Calendar/IcsGuards.cs`
- Modify: `src/snoopy.microservice/Services/Calendar/IcsDocument.cs`
- Modify: `src/snoopy.microservice/Repositories/DavCalendarWriter.cs` (`GateAsync`)
- Test: `…/Services/IcsGuardsTests.cs`, `…/Services/IcsDocumentTests.cs`,
  `…/Controllers/CalDavPutTests.cs`, `…/Repositories/DavCalendarWriterTests.cs`

**Interfaces:**
- Consomme : `IcsGuards.Check(string ics, IcsCalendar? parsed)`, `IcsDocument.TryLoad(string)`,
  `CalDavError.SupportedCalendarData`, `DavWriteStatus.UidConflict`, `RefuseAsync(trace, condition,
  detail, ct)`.
- Produit :
  - `IcsDocument.TryLoad` inchangé de signature, mais **`IcsDocument.LooksLikeCalendar(string ics)`**
    nouveau : `internal static bool LooksLikeCalendar(string ics)`, vrai quand le texte ouvre sur
    `BEGIN:VCALENDAR` ;
  - le message de `IcsPrecondition.ValidCalendarData` se dédouble selon ce booléen ;
  - `DavCalendarWriter.GateAsync` peut désormais rendre `DavWriteStatus.UidConflict` pour la
    ressource **elle-même**, l'href nommé étant alors celui du `PUT`.

- [ ] **Step 1 : écrire les tests qui échouent**

Dans `Services/IcsGuardsTests.cs` :

```csharp
    [Fact]
    public void AMethodProperty_IsNotACalendarObjectResource()
    {
        // RFC 4791 § 4.1, MUST NOT: METHOD makes the object an iTIP message, not a resource. A
        // stored PUBLISH would be served back to every client as if it were an event of the user's.
        var ics = Ics.Single("SUMMARY:invitation").Replace("VERSION:2.0", "VERSION:2.0\r\nMETHOD:PUBLISH");

        var problem = IcsGuards.CheckAll(ics, out _);

        Assert.Equal(IcsPrecondition.ValidCalendarObjectResource, problem!.Precondition);
    }

    [Fact]
    public void AnIcalendarBodyThatIsInvalid_SaysSo_NotThatItIsNotIcalendar()
    {
        // Arbitration 2 of the report: DTSTART as a DATE and DTEND as a DATE-TIME breaks RFC 5545
        // § 3.8.2.2's MUST, and refusing is right — but the body IS iCalendar, and the message
        // that says otherwise sends a client looking for the wrong bug.
        var ics = Ics.Single("DTSTART;VALUE=DATE:20260907", "DTEND:20260907T100000Z");

        var problem = IcsGuards.CheckAll(ics, out _);

        Assert.Equal(IcsPrecondition.ValidCalendarData, problem!.Precondition);
        Assert.DoesNotContain("not iCalendar text", problem.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ABodyThatIsNotCalendarTextAtAll_StillSaysSo()
    {
        var problem = IcsGuards.CheckAll("<?xml version=\"1.0\"?><nope/>", out _);

        Assert.Equal(IcsPrecondition.ValidCalendarData, problem!.Precondition);
        Assert.Contains("not iCalendar text", problem.Reason, StringComparison.Ordinal);
    }
```

`problem.Reason` : si le champ de `IcsProblem` porte un autre nom, employer le sien — **le lire, ne
pas le renommer**.

Dans `Repositories/DavCalendarWriterTests.cs` :

```csharp
    [Fact]
    public async Task ReplacingAResourceWithADifferentUid_IsAUidConflict()
    {
        // RFC 4791 § 5.3.2.1, no-uid-conflict, second half: « or overwrite an existing calendar
        // object resource with one that has a different UID property value ». The href a client
        // synced on would change identity under every other device it holds.
        await Put("1.ics", Ics.Single("UID:one@weesky.net"));

        var outcome = await Put("1.ics", Ics.Single("UID:two@weesky.net"));

        Assert.Equal(DavWriteStatus.UidConflict, outcome.Status);
    }

    [Fact]
    public async Task ReplacingAResourceWithTheSameUid_IsStillAReplacement()
    {
        await Put("1.ics", Ics.Single("UID:one@weesky.net", "SUMMARY:avant"));

        var outcome = await Put("1.ics", Ics.Single("UID:one@weesky.net", "SUMMARY:apres"));

        Assert.Equal(DavWriteStatus.Replaced, outcome.Status);
    }
```

Dans `Controllers/CalDavPutTests.cs` :

```csharp
    [Fact]
    public async Task APutUnderAContentTypeThatIsNotCalendar_NamesTheMediaType()
    {
        // RFC 4791 § 5.3.2.1: supported-calendar-data is « MUST be a supported media type », a
        // different refusal from « the data is invalid » — and the client acts differently on each.
        var response = await Put(Calendar(), "x.ics", "<?xml version=\"1.0\"?><nope/>",
            contentType: "text/xml");

        Assert.Equal(403, response.StatusCode);
        Assert.Contains("supported-calendar-data", await BodyOf(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APutWithNoContentTypeAtAll_IsJudgedOnItsBody()
    {
        // Not every client sends one, and refusing on absence would break them for nothing.
        var response = await Put(Calendar(), "y.ics", Ics.Single("SUMMARY:sans en-tete"),
            contentType: null);

        Assert.Equal(201, response.StatusCode);
    }
```

Si l'aide `Put` du fichier ne prend pas de `contentType`, **l'étendre** avec un paramètre optionnel
qui vaut `DavHeaders.CalendarContentType` par défaut, et ne toucher à aucun appel existant.

- [ ] **Step 2 : les faire échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~IcsGuardsTests|FullyQualifiedName~DavCalendarWriterTests|FullyQualifiedName~CalDavPutTests"`
Expected : FAIL sur les cinq nouveaux.

- [ ] **Step 3 : `METHOD` est refusée**

Dans `Services/Calendar/IcsGuards.cs`, dans `Check`, **après** le contrôle de `VERSION` et **avant**
celui des composants — l'ordre nomme la vraie cause :

```csharp
        // RFC 4791 § 4.1, MUST NOT: METHOD makes the object an iTIP message. RFC 6638 would give it
        // a meaning; we do not serve scheduling, so storing one would serve a message as an event.
        if (!string.IsNullOrEmpty(parsed.Method))
            return new IcsProblem(IcsPrecondition.ValidCalendarObjectResource,
                $"A calendar object resource must not carry METHOD ('{parsed.Method}').");
```

- [ ] **Step 4 : le refus de parsing dit lequel des deux il est**

Dans `Services/Calendar/IcsDocument.cs`, à côté de `TryLoad` :

```csharp
    /// <summary>Whether the text opens as an iCalendar object at all. What TryLoad answers null on
    /// is two different refusals — a body that is not calendar text, and one that is calendar text
    /// RFC 5545 refuses — and a client acts differently on each.</summary>
    internal static bool LooksLikeCalendar(string ics) =>
        ics.AsSpan().TrimStart().StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase);
```

Dans `IcsGuards.Check`, remplacer la ligne unique du `parsed is null` :

```csharp
        if (parsed is null)
        {
            // Both are valid-calendar-data (§ 5.3.2.1 has no finer element), and both are refused
            // — but the reason a client is handed must be true. The DTSTART/DTEND type mismatch of
            // RFC 5545 § 3.8.2.2 lands here, and it IS iCalendar.
            return new IcsProblem(IcsPrecondition.ValidCalendarData,
                IcsDocument.LooksLikeCalendar(ics)
                    ? "The body is iCalendar text RFC 5545 refuses."
                    : "The body is not iCalendar text.");
        }
```

- [ ] **Step 5 : l'UID ne change pas sous une adresse**

Dans `Repositories/DavCalendarWriter.cs`, `GateAsync`, **juste après** le contrôle du détenteur
(`holder`) et avant celui du plafond :

```csharp
            // The other half of no-uid-conflict, which § 5.3.2.1 spells in the same sentence:
            // « or overwrite an existing calendar object resource with one that has a different
            // UID ». The href names this very resource — the client must re-read what it holds.
            if (row is not null && !string.Equals(row.Uid, uid, StringComparison.Ordinal))
                return Conflict(userId, calendar.DavName, davName);
```

Et **corriger le commentaire de l'appel précédent**, qui affirme aujourd'hui le contraire (« A UID
that merely changes under its own name is accepted ») : cette phrase devient fausse et doit partir.

- [ ] **Step 6 : le média est jugé avant le corps**

Dans `Controllers/CalDavController.cs`, action `PutEventAsync`, **juste après** le `if (body is
null)` :

```csharp
            // RFC 4791 § 5.3.2.1: supported-calendar-data is about the MEDIA TYPE, and this is the
            // one layer that sees it. An absent header is not a refusal — clients omit it — but a
            // header naming something else is, and it is not the same answer as invalid data.
            if (Request.ContentType is { Length: > 0 } contentType
                && !contentType.StartsWith(CalDavProperties.CalendarDataMediaType, StringComparison.OrdinalIgnoreCase))
            {
                await RefuseAsync(trace, CalDavError.SupportedCalendarData, null, cancellationToken);
                return;
            }
```

- [ ] **Step 7 : les faire passer, puis toute la suite**

Run : `cd src && dotnet test` Expected : PASS.

**Un test existant peut rougir ici** : ceux qui posent un `PUT` sous un `Content-Type` absent ou
sous `text/calendar` restent verts ; un test qui poserait `application/xml` sur un corps iCalendar
valide et attendrait `201` décrit un comportement que RFC 4791 § 5.3.2.1 refuse — le corriger, et
**dire dans le rapport de tâche lequel et pourquoi**.

- [ ] **Step 8 : commit**

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice
git commit -F - <<'EOF'
fix(caldav): le media, METHOD et l'UID a l'ecrasement

RFC 4791 4.1 et 5.3.2.1 : METHOD est refusee, un UID qui change sous
son adresse est un no-uid-conflict, un media non iCalendar est nomme.
EOF
```

---

### Task 3 : ce que RFC 5545 refuse dans le corps

**La tâche la plus risquée de la vague.** Trois validations à écrire à la main, et un faux refus
casserait un client réel là où le passage de l'outil ne verrait rien. La règle qui les gouverne
toutes les trois : **ne refuser que ce qu'un MUST nomme, et sur le doute, accepter.**

**Ce que le client fait, en clair.**
1. Il écrit `DTSTART;TZID=US/Eastern:20260101T100000` sans joindre le bloc `VTIMEZONE` qui définit
   `US/Eastern`. Le fichier ne se suffit pas à lui-même : un lecteur qui n'a pas cette zone dans sa
   base ne sait pas quelle heure il est. Nous l'acceptons.
2. Il écrit `DESCRIPTION:Bad \"escaping\" here.` — `\"` n'est pas un échappement iCalendar. Le
   fichier est syntaxiquement invalide ; nous l'acceptons.
3. Il écrit un `RECURRENCE-ID:20260116T160000` sur une série hebdomadaire qui ne produit que des
   instances à 10:00. L'exception ne surcharge rien : elle désigne une instance qui n'existe pas.
   Nous l'acceptons, et elle est invisible pour toujours.

**Ce que la mesure a trouvé** : `errors.xml` `PUT` t10, t13 et t17 — trois échecs.

**Files:**
- Modify: `src/snoopy.microservice/Services/Calendar/IcsGuards.cs`
- Test: `…/Services/IcsGuardsTests.cs`, `…/Services/IcsCorpusTests.cs` (à faire tourner, pas à
  modifier)

**Interfaces:**
- Consomme : `IcsGuards.Check(string ics, IcsCalendar? parsed)`, `IcsGuards.IsWalkable(IcsCalendar)`,
  `IcsDocument.Components`, `IcsDocument.MasterOf`, `IcsDocument.InstanceIdOf`,
  `IcsDocument.LiteralOf`, `IcsTimeZones.Detach`.
- Produit : trois gardes privées appelées depuis `Check` et `CheckAll` ; aucune signature publique
  ne change.

- [ ] **Step 1 : écrire les tests qui échouent**

Dans `Services/IcsGuardsTests.cs` — **les cas acceptés comptent autant que les refusés** :

```csharp
    [Fact]
    public void ATzidWithNoVTimezoneInTheFile_IsNotACalendarObjectResource()
    {
        // RFC 5545 § 3.2.19: the VTIMEZONE the TZID names MUST be present in the same object. We
        // do not announce timezones-by-reference (RFC 7809), so the file is all a reader gets.
        var ics = Ics.Single("DTSTART;TZID=US/Eastern:20260101T100000", "DTEND;TZID=US/Eastern:20260101T110000");

        var problem = IcsGuards.CheckAll(ics, out _);

        Assert.Equal(IcsPrecondition.ValidCalendarObjectResource, problem!.Precondition);
    }

    [Fact]
    public void ATzidThatTheFileDefines_IsAccepted()
    {
        // What Thunderbird, DAVx5 and iOS all send. A false refusal here breaks every one of them.
        Assert.Null(IcsGuards.CheckAll(Ics.Zoned("US/Eastern"), out _));
    }

    [Fact]
    public void AUtcOrFloatingStart_NeedsNoVTimezone()
    {
        Assert.Null(IcsGuards.CheckAll(Ics.Single("DTSTART:20260101T100000Z"), out _));
        Assert.Null(IcsGuards.CheckAll(Ics.Single("DTSTART:20260101T100000"), out _));
    }

    [Theory]
    [InlineData(@"DESCRIPTION:Bad \""escaping\"" here.")]
    [InlineData(@"SUMMARY:a\qb")]
    public void AnInvalidTextEscape_IsInvalidCalendarData(string line) =>
        Assert.Equal(IcsPrecondition.ValidCalendarData,
            IcsGuards.CheckAll(Ics.Single(line), out _)!.Precondition);

    [Theory]
    [InlineData(@"DESCRIPTION:one\, two\; three\\four\nfive\Nsix")]
    [InlineData("URL:https://weesky.net/a\\b")]          // not a TEXT property: no escaping rules
    [InlineData(@"X-WR-NOTE:c:\temp")]                    // an X- property is not judged either
    public void AValidEscapeOrANonTextProperty_IsAccepted(string line) =>
        Assert.Null(IcsGuards.CheckAll(Ics.Single(line), out _));

    [Fact]
    public void ARecurrenceIdNoInstanceOfTheRuleProduces_IsInvalidCalendarData()
    {
        // RFC 5545 § 3.8.4.4: a RECURRENCE-ID identifies an instance the master's rule generates.
        // One that names 16:00 of a series that only ever fires at 10:00 overrides nothing, and is
        // invisible for ever.
        var ics = Ics.Series("DTSTART:20260112T100000Z", "RRULE:FREQ=WEEKLY",
            @override: "RECURRENCE-ID:20260116T160000Z");

        Assert.Equal(IcsPrecondition.ValidCalendarData, IcsGuards.CheckAll(ics, out _)!.Precondition);
    }

    [Fact]
    public void ARecurrenceIdTheRuleProduces_IsAccepted()
    {
        var ics = Ics.Series("DTSTART:20260112T100000Z", "RRULE:FREQ=WEEKLY",
            @override: "RECURRENCE-ID:20260119T100000Z");

        Assert.Null(IcsGuards.CheckAll(ics, out _));
    }

    [Fact]
    public void ADetachedOverrideWithNoMaster_IsAccepted()
    {
        // No rule to judge against. Every client writes these when a series is split.
        var ics = Ics.Single("DTSTART:20260119T100000Z", "RECURRENCE-ID:20260119T100000Z");

        Assert.Null(IcsGuards.CheckAll(ics, out _));
    }

    [Fact]
    public void AnOverrideOfASeriesTheEngineCannotWalk_IsAccepted()
    {
        // IsWalkable already refuses that file for another reason, or lets it through as the
        // master alone: a guard that has to walk in order to judge must not judge what it cannot.
        var ics = Ics.Series("DTSTART;TZID=US/Eastern:20260112T100000", "RRULE:FREQ=HOURLY",
            @override: "RECURRENCE-ID;TZID=US/Eastern:20260116T160000");

        Assert.NotEqual(IcsPrecondition.ValidCalendarData, IcsGuards.CheckAll(ics, out _)?.Precondition);
    }
```

`Ics.Zoned(...)` et `Ics.Series(..., @override:)` : si le fixture ne porte pas ces aides,
**les ajouter à `Fixtures/Ics.cs`** plutôt que d'écrire des littéraux dans chaque test.

- [ ] **Step 2 : les faire échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~IcsGuardsTests"` Expected : FAIL sur les
trois refus attendus ; les cas acceptés sont déjà verts et le resteront.

- [ ] **Step 3 : un `TZID` que le fichier ne définit pas**

Dans `Services/Calendar/IcsGuards.cs` :

```csharp
    /// <summary>
    /// RFC 5545 § 3.2.19: the VTIMEZONE a TZID names MUST be present in the same iCalendar object.
    /// We announce neither RFC 7809's timezone-service nor timezones-by-reference, so the file is
    /// everything a reader gets — one whose zone lives elsewhere cannot be resolved by anyone.
    /// Read off the TEXT, not the model: Ical.Net resolves a known id against tzdb and the parsed
    /// object then no longer remembers that the block was missing.
    /// </summary>
    private static IcsProblem? CheckZones(string ics, IcsCalendar parsed)
    {
        var defined = parsed.TimeZones.Select(zone => zone.TzId).OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var referenced in ReferencedTzIds(ics))
        {
            if (!defined.Contains(referenced))
                return new IcsProblem(IcsPrecondition.ValidCalendarObjectResource,
                    $"TZID '{referenced}' is used but no VTIMEZONE in the object defines it.");
        }

        return null;
    }

    /// <summary>Every TZID parameter the text carries, VTIMEZONE blocks excepted — their own
    /// TZID is the definition, not a reference.</summary>
    private static IEnumerable<string> ReferencedTzIds(string ics)
    {
        var inZone = false;
        foreach (var line in Unfolded(ics))
        {
            if (line.StartsWith("BEGIN:VTIMEZONE", StringComparison.OrdinalIgnoreCase)) inZone = true;
            else if (line.StartsWith("END:VTIMEZONE", StringComparison.OrdinalIgnoreCase)) inZone = false;
            if (inZone) continue;

            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            foreach (var parameter in line[..colon].Split(';').Skip(1))
            {
                if (parameter.StartsWith("TZID=", StringComparison.OrdinalIgnoreCase))
                    yield return parameter[5..].Trim('"');
            }
        }
    }
```

`WrittenUids` déplie déjà le texte à la main : **extraire ce dépliage** dans
`private static IEnumerable<string> Unfolded(string ics)` et faire lire les deux méthodes à la même
source, plutôt que d'écrire le `Replace` une seconde fois.

- [ ] **Step 4 : un échappement que RFC 5545 § 3.3.11 ne définit pas**

```csharp
    /// <summary>The properties whose value is TEXT and therefore obeys § 3.3.11's escaping. X- and
    /// IANA- properties are not judged: their value type is declared, not known.</summary>
    private static readonly HashSet<string> TextProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "CALSCALE", "CATEGORIES", "CLASS", "COMMENT", "CONTACT", "DESCRIPTION", "LOCATION",
        "METHOD", "PRODID", "RELATED-TO", "RESOURCES", "STATUS", "SUMMARY", "TRANSP", "TZID",
        "TZNAME", "UID", "VERSION",
    };

    /// <summary>RFC 5545 § 3.3.11: inside a TEXT value a backslash introduces one of five escapes
    /// and nothing else. A body carrying any other is not valid iCalendar, and what a reader makes
    /// of it differs from reader to reader.</summary>
    private static IcsProblem? CheckTextEscapes(string ics)
    {
        foreach (var line in Unfolded(ics))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line[..colon].Split(';')[0];
            if (!TextProperties.Contains(name)) continue;

            var value = line.AsSpan(colon + 1);
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] != '\\') continue;
                if (i + 1 >= value.Length || "\\;,nN".IndexOf(value[i + 1]) < 0)
                    return new IcsProblem(IcsPrecondition.ValidCalendarData,
                        $"A {name} value carries an escape RFC 5545 § 3.3.11 does not define.");
                i++;   // the escaped character is consumed, so \\\\ is one escape and not two
            }
        }

        return null;
    }
```

- [ ] **Step 5 : un `RECURRENCE-ID` qu'aucune instance ne porte**

```csharp
    /// <summary>
    /// RFC 5545 § 3.8.4.4: an override's RECURRENCE-ID names an instance the master's rule
    /// generates. One that names no instance overrides nothing and is invisible from every window.
    /// Judged only where it can be: a resource with no master, or a series
    /// <see cref="IsWalkable(IcsCalendar)"/> refuses, is left alone — a guard that must walk in
    /// order to refuse never refuses what it could not walk.
    /// </summary>
    private static IcsProblem? CheckOverrides(IcsCalendar parsed)
    {
        var overrides = IcsDocument.Components(parsed).Where(c => c.RecurrenceIdentifier is not null).ToList();
        if (overrides.Count == 0 || !IsWalkable(parsed)) return null;
        if (IcsDocument.MasterOf(parsed) is not { DtStart: { } start } master) return null;
        if (master.RecurrenceRule is null && master.RecurrenceDates?.GetAllDates().Any() != true) return null;

        var ids = InstanceIds(parsed, start);
        if (ids is null) return null;   // the walk failed: CheckExpansion answers for that

        foreach (var component in overrides)
        {
            if (!ids.Contains(IcsDocument.InstanceIdOf(component)))
                return new IcsProblem(IcsPrecondition.ValidCalendarData,
                    $"RECURRENCE-ID '{IcsDocument.InstanceIdOf(component)}' names no instance of the series.");
        }

        return null;
    }
```

`InstanceIds` marche la série sur une fenêtre qui **couvre les surcharges** et rend l'ensemble des
identifiants d'instance, ou null si la marche lève :

```csharp
    /// <summary>The instance identifiers the master generates over a window wide enough to hold
    /// every override the file carries, capped exactly as the walk elsewhere is. Null when the
    /// engine threw — the file is then judged by CheckExpansion, not here.</summary>
    private static HashSet<string>? InstanceIds(IcsCalendar parsed, CalDateTime start)
    {
        try
        {
            var walked = IcsTimeZones.Detach(parsed)?.Calendar ?? parsed;
            var alone = new IcsCalendar();
            alone.Events.Add((CalendarEvent)IcsDocument.MasterOf(walked)!);
            return alone.GetOccurrences(IcsTimeZones.Detached(start))
                .Take(MaxInstancesPerYear)
                .Select(o => o.Period.StartTime is { } at ? IcsDocument.LiteralOf(at) : string.Empty)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception)
        {
            return null;
        }
    }
```

**Le détail qui décide** : la série doit être marchée **sans ses surcharges**, sinon Ical.Net
substitue l'instance surchargée à celle que la règle produit et l'identifiant fautif se retrouve
dans l'ensemble — le test passerait toujours. Si l'isolement de la maîtresse par clonage ne se fait
pas proprement avec Ical.Net 5.2.3, **sérialiser la maîtresse seule et la recharger** :
`IcsDocument.TryLoad(IcsDocument.Serialize(alone))`. La revue vérifie que cette isolation est
réellement en place, en lisant le test `ARecurrenceIdNoInstanceOfTheRuleProduces_…` rouge avant
correction et vert après.

- [ ] **Step 6 : brancher les trois gardes**

Dans `CheckAll`, en gardant l'ordre qui nomme la vraie cause — syntaxe, forme, **texte**, densité,
expansion, **surcharges**, départ :

```csharp
        parsed = IcsDocument.TryLoad(ics);
        return Check(ics, parsed)
            ?? CheckDensity(parsed!) ?? CheckExpansion(parsed!)
            ?? CheckOverrides(parsed!) ?? CheckStart(parsed!);
```

et, à la fin de `Check` (qui voit le texte et le modèle) :

```csharp
        if (CheckZones(ics, parsed) is { } zone) return zone;
        if (CheckTextEscapes(ics) is { } escape) return escape;
        return null;
```

- [ ] **Step 7 : les faire passer, puis toute la suite**

Run : `cd src && dotnet test` Expected : PASS.

**`IcsCorpusTests` est le garde-fou de cette tâche** : il joue le corpus de fichiers réels. Si un
fichier du corpus se met à être refusé, **c'est un faux refus**, pas un progrès — la garde est trop
large et doit être resserrée avant d'aller plus loin. Le rapport de tâche dit lequel, ou dit que le
corpus est resté vert.

- [ ] **Step 8 : commit**

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice
git commit -F - <<'EOF'
fix(calendar): la porte du PUT applique RFC 5545

TZID sans VTIMEZONE, echappement TEXT hors 3.3.11, RECURRENCE-ID
qu'aucune instance ne porte : trois refus qui manquaient.
EOF
```

---

### Task 4 : `valid-filter` contre `supported-filter`, et le filtre que rien ne refusait

**Ce que le client fait, en clair.** Il envoie un filtre mal construit — un `time-range` posé
directement sous `VCALENDAR`, un `VEVENT` dans un `VEVENT`, un `VALARM` hors de tout composant, un
`time-range` niché dans un `prop-filter name="SUMMARY"`. Deux réponses possibles, et elles ne disent
pas la même chose au client : **« ton filtre est malformé »** (`valid-filter`) ou **« ce filtre est
bien formé mais je ne sais pas l'évaluer »** (`supported-filter`). Le premier lui dit de corriger
son code ; le second, de se rabattre sur une requête plus simple. Nous répondons le second partout.

**Ce que la mesure a trouvé** : `errors.xml` `REPORT/filter` t2 à t8 (sept échecs, sept fichiers de
filtre) et t9 (un échec, et c'est **l'exemple littéral du RFC**) — huit échecs.

RFC 4791 § 7.8 : « The CALDAV:filter XML element specified in the REPORT request MUST be valid. For
instance, a CALDAV:filter cannot nest a `<C:comp name="VEVENT">` element in a
`<C:comp name="VTODO">` element. » — la malformation **structurelle** est `valid-filter`. Et
`supported-filter` : « … only make reference to components, properties, and parameters for which
queries are supported by the server » — la **référence** à ce qu'on ne sert pas.

**Files:**
- Modify: `src/snoopy.microservice/Services/CalDav/CalendarQueryFilter.cs`
- Test: `…/Services/CalDav/CalendarQueryFilterTests.cs`

**Interfaces:**
- Consomme : `CalDavError.ValidFilter`, `CalDavError.SupportedFilter`, les aides de test `Filter`,
  `Comp`, `Prop`, `Param`, `TimeRange`, `Text`, `IsNotDefined`, `AssertRefused`.
- Produit : rien de nouveau. Des refus déjà prononcés changent d'élément de précondition.

**La règle, une fois pour toutes** — à écrire dans le résumé de `CalendarQueryFilter` :

| Ce que le corps porte | Précondition |
|---|---|
| une imbrication que la grammaire du § 9.7 n'autorise pas, où qu'elle soit | `valid-filter` |
| un `time-range` sous autre chose qu'un `comp-filter` | `valid-filter` |
| une valeur hors forme (borne non-UTC, `negate-condition` hors `yes`/`no`, deux `time-range`) | `valid-filter` |
| un nom de composant, de propriété ou de paramètre que la grammaire admet mais que nous ne servons pas — `VTODO`, `VJOURNAL`, `X-COMP` | `supported-filter` |
| un `test="anyof"`, un `match-type` inconnu | `supported-filter` |

- [ ] **Step 1 : écrire les tests qui échouent**

Dans `Services/CalDav/CalendarQueryFilterTests.cs` :

```csharp
    [Fact]
    public void ATimeRangeDirectlyUnderTheVCalendar_IsMalformed() =>
        // § 9.7's grammar puts time-range under a comp-filter, and VCALENDAR is not a component
        // that spans time. errors.xml/15.xml.
        AssertRefused(CalDavError.ValidFilter,
            Filter(Comp("VCALENDAR", TimeRange("20260101T000000Z", "20260201T000000Z"))));

    [Fact]
    public void AVEventNestedInAVEvent_IsMalformed() =>
        AssertRefused(CalDavError.ValidFilter, Filter(Comp("VCALENDAR", Comp("VEVENT", Comp("VEVENT")))));

    [Fact]
    public void AVAlarmDirectlyUnderTheVCalendar_IsMalformed() =>
        AssertRefused(CalDavError.ValidFilter, Filter(Comp("VCALENDAR", Comp("VALARM"))));

    [Fact]
    public void ATimeRangeUnderAVTimezone_IsMalformed() =>
        AssertRefused(CalDavError.ValidFilter,
            Filter(Comp("VCALENDAR", Comp("VTIMEZONE", TimeRange("20260101T000000Z", null)))));

    [Fact]
    public void AnUnknownComponentName_IsUnsupported_NotMalformed() =>
        // X-COMP is a well-formed reference to a component we do not serve: § 7.8's own division.
        AssertRefused(CalDavError.SupportedFilter, Filter(Comp("VCALENDAR", Comp("X-COMP"))));

    [Fact]
    public void AVTodoCompFilter_IsUnsupported_NotMalformed() =>
        AssertRefused(CalDavError.SupportedFilter, Filter(Comp("VCALENDAR", Comp("VTODO"))));

    [Fact]
    public void ATimeRangeNestedInAPropFilter_IsMalformed_TheRfcsOwnExample()
    {
        // RFC 4791 § 7.8.9, word for word: « a CALDAV:filter cannot nest a time-range element in a
        // prop name="SUMMARY" element ». Accepted until now — a 207 over a filter we never applied.
        AssertRefused(CalDavError.ValidFilter,
            VEvent(Prop("SUMMARY", TimeRange("20260101T000000Z", "20260201T000000Z"))));
    }
```

Le dernier **remplace** `ATimeRangeOnAProperty_IsUnsupported`, qui fige aujourd'hui l'autre verdict.
C'est une correction de valeur attendue, pas une inversion assumée : le test figeait un défaut. **Le
rapport de tâche le nomme.**

- [ ] **Step 2 : les faire échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~CalendarQueryFilterTests"` Expected : FAIL
sur les sept ; les six premiers rendent `supported-filter` au lieu de `valid-filter`, et le septième
ne refuse rien du tout.

- [ ] **Step 3 : la grammaire décide, le nom ensuite**

Dans `Services/CalDav/CalendarQueryFilter.cs`, `Parse` — la boucle sur les enfants du `VCALENDAR`
distingue désormais la forme du nom :

```csharp
        XElement? vevent = null;
        foreach (var child in children)
        {
            // Structure first (§ 7.8's valid-filter), name second (supported-filter): a time-range
            // under VCALENDAR is malformed whatever we serve, while a VTODO comp-filter is a
            // well-formed reference to a component we do not.
            if (child.Name != CompFilterName) throw Malformed();
            if (vevent is not null) throw Malformed();
            if (!Named(child, VEvent)) throw Unsupported();
            vevent = child;
        }
```

et `ParseEvent`, où un `comp-filter` enfant ne peut être qu'un `VALARM` :

```csharp
            else if (child.Name == CompFilterName)
            {
                if (!Named(child, VAlarm)) throw Malformed();   // § 9.7.1: VALARM alone nests here
                alarmFilters.Add(ParseAlarmFilter(child));
            }
```

et `ParsePropFilter`, où un `time-range` est nommément l'exemple du RFC :

```csharp
            else if (child.Name == TimeRangeName) throw Malformed();   // § 7.8.9's own example
            else throw Unsupported();
```

- [ ] **Step 4 : les faire passer, puis toute la suite**

Run : `cd src && dotnet test` Expected : PASS. `ARootThatIsNotVCalendar_IsMalformed` et
`TwoRootCompFilters_AreMalformed` restent verts sans changer de valeur.

- [ ] **Step 5 : commit**

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice
git commit -F - <<'EOF'
fix(caldav): valid-filter pour la forme, supported-filter pour le nom

RFC 4791 7.8 : une imbrication illegale est malformee, une reference a
ce que nous ne servons pas ne l'est pas.
EOF
```

---

### Task 5 : le filtre et son composant

**Deux gardes, et l'une des deux tient en une ligne.**

**Ce que le client fait, en clair.**
1. Il demande « les événements dont le `TZID` du `DTSTART` ne contient pas *Paci* ». Un événement
   tout-journée n'a **pas** de paramètre `TZID` du tout. Nous répondons qu'il correspond : la
   condition « ne contient pas » est vraie sur rien. Elle ne l'est pas — le RFC demande que la
   propriété **porte** un paramètre de ce nom avant qu'on juge sa valeur. Deux ressources de trop
   dans la réponse.
2. Il demande « les événements qui ont une instance entre 19h et 20h **et** dont le `SUMMARY`
   contient *changed* ». Un fichier dont la maîtresse a la bonne heure et dont une surcharge a le
   bon résumé lui est rendu — alors qu'**aucun** de ses composants ne satisfait les deux. RFC 4791
   § 9.7.1 parle d'un « targeted calendar component » au singulier.

**Ce que la mesure a trouvé** : `reports.xml` `basic query` t8 et `time-range` t7 — deux échecs. Le
premier est le résidu 5c laissé « en attente du verdict de l'outil ou d'un client » : **le verdict
est rendu.**

**Files:**
- Modify: `src/snoopy.microservice/Services/CalDav/CalendarQueryFilter.cs`
- Test: `…/Services/CalDav/CalendarQueryFilterTests.cs`

**Interfaces:**
- Consomme : `CalendarQueryFilter.Matches(IcsCalendar, CalendarQuerySpec, string)`,
  `OccurrenceExpander.Overlaps`, `OccurrenceExpander.AlarmFires(…, CalendarEvent? component)`.
- Produit : rien de nouveau. `Matches` évalue le `time-range` **par composant** au lieu de
  l'évaluer sur le fichier, et `MatchesParam` gagne le garde que `MatchesPropFilter` porte déjà.

- [ ] **Step 1 : écrire les tests qui échouent**

```csharp
    [Fact]
    public void ANegatedParamFilter_DoesNotMatchAPropertyThatCarriesNoSuchParameter()
    {
        // RFC 4791 § 9.7.3: the param-filter's conditions apply to the parameters the property
        // carries. « TZID does not contain Paci » is not true of a DTSTART that has no TZID at all
        // — MatchesPropFilter has held this guard two lines above since 5c; this one lacked it.
        var allDay = Load(Ics.Single("DTSTART;VALUE=DATE:20260907", "DTEND;VALUE=DATE:20260908"));
        var filter = VEvent(Prop("DTSTART", Param("TZID", Text("Paci", negate: true))));

        Assert.False(Matches(allDay, filter));
    }

    [Fact]
    public void ANegatedParamFilter_StillMatchesAParameterWhoseValueDiffers()
    {
        var zoned = Load(Ics.Zoned("Europe/Paris"));

        Assert.True(Matches(zoned, VEvent(Prop("DTSTART", Param("TZID", Text("Paci", negate: true))))));
    }

    [Fact]
    public void ATimeRangeAndAPropFilter_AreSatisfiedByOneComponent_NeverByTwo()
    {
        // RFC 4791 § 9.7.1: « the targeted calendar component » — one component of the resource
        // answers every clause together. reports.xml/time-range t7: the master had the hour and
        // the override had the summary, and the file came back.
        var parsed = Load(Ics.Series("DTSTART:20270103T190000Z", "RRULE:FREQ=DAILY",
            @override: "RECURRENCE-ID:20270104T190000Z\r\nDTSTART:20270104T210000Z\r\nSUMMARY:changed"));

        var filter = VEvent(TimeRange("20270103T190000Z", "20270103T200000Z"),
            Prop("SUMMARY", Text("changed")));

        Assert.False(Matches(parsed, filter));
    }

    [Fact]
    public void ATimeRangeAndAPropFilter_MatchWhenOneComponentCarriesBoth()
    {
        var parsed = Load(Ics.Single("DTSTART:20270103T190000Z", "DTEND:20270103T200000Z",
            "SUMMARY:changed"));

        Assert.True(Matches(parsed, VEvent(TimeRange("20270103T190000Z", "20270103T200000Z"),
            Prop("SUMMARY", Text("changed")))));
    }

    [Fact]
    public void ATimeRangeAlone_StillReadsTheWholeResource()
    {
        // No prop-filter, no alarm filter: the window is asked of the file, and an override that
        // falls inside it answers for the file. Nothing about this changes.
        var parsed = Load(Ics.Series("DTSTART:20270103T190000Z", "RRULE:FREQ=DAILY",
            @override: "RECURRENCE-ID:20270104T190000Z\r\nDTSTART:20270104T210000Z"));

        Assert.True(Matches(parsed, VEvent(TimeRange("20270104T210000Z", "20270104T220000Z"))));
    }
```

- [ ] **Step 2 : les faire échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~CalendarQueryFilterTests"` Expected : FAIL
sur le premier et le troisième.

- [ ] **Step 3 : le garde d'un `param-filter`**

Dans `MatchesParam`, entre le `IsNotDefined` et le `TextMatch` — **exactement la ligne que
`MatchesPropFilter` porte deux méthodes plus haut** :

```csharp
        if (filter.IsNotDefined) return named.Count == 0;
        // The mirror of MatchesPropFilter: past is-not-defined, a condition is asked OF an
        // instance. With none, a negated text-match would be vacuously true.
        if (named.Count == 0) return false;
        if (filter.TextMatch is not { } match) return true;
```

- [ ] **Step 4 : le `time-range` rejoint le composant**

Dans `Matches`, le `time-range` cesse d'être jugé à part du reste :

```csharp
    internal static bool Matches(IcsCalendar parsed, CalendarQuerySpec spec, string calendarTimeZone)
    {
        if (spec.NoneMatch) return false;

        // The window alone is asked of the FILE — an instance is what overlaps it, and an instance
        // may come from any component (§ 9.9 on a VEVENT).
        if (spec.PropFilters.Count == 0 && spec.AlarmFilters.Count == 0)
            return spec.TimeRange is not { } whole
                || OccurrenceExpander.Overlaps(parsed, whole.FromUtc, whole.ToUtc, calendarTimeZone);

        // Beside a prop-filter or an alarm filter, § 9.7.1's « targeted calendar component » binds
        // them together: ONE component of the resource satisfies every clause, window included.
        return IcsDocument.Components(parsed).Any(component =>
            OverlapsFrom(parsed, component, spec.TimeRange, calendarTimeZone)
            && spec.PropFilters.All(filter => MatchesPropFilter(component, filter))
            && spec.AlarmFilters.All(filter => MatchesAlarmFilter(parsed, component, filter, calendarTimeZone)));
    }

    /// <summary>Whether an instance THIS component sources overlaps the window — the narrowing
    /// AlarmFires already does with its own <c>component</c> argument, applied to the VEVENT's
    /// window. A null window is every instant.</summary>
    private static bool OverlapsFrom(IcsCalendar parsed, CalendarEvent component, TimeRangeSpec? range,
        string calendarTimeZone) =>
        range is not { } window
        || OccurrenceExpander.Overlaps(parsed, window.FromUtc, window.ToUtc, calendarTimeZone, component);
```

Et dans `Services/Calendar/OccurrenceExpander.cs`, `Overlaps` gagne le même paramètre facultatif
qu'`AlarmFires` porte déjà, avec la même mécanique (`SourceOf`, `ReferenceEquals`) :

```csharp
    /// <summary>Whether one instance at least overlaps <c>[fromUtc, toUtc[</c> — the very walk of
    /// <see cref="Expand"/>, stopped at the first one found (RFC 4791 § 9.9 on a VEVENT).
    /// <paramref name="component"/> narrows the instances to those that one component sources — the
    /// one a filter is being judged on — and null takes them all.</summary>
    internal static bool Overlaps(IcsCalendar parsed, DateTime fromUtc, DateTime toUtc,
        string calendarTimeZone, CalendarEvent? component = null)
    {
        var found = Over(Guid.Empty, Guid.Empty, parsed, fromUtc, toUtc, calendarTimeZone, calendarTimeZone)
            .Run(firstOnly: component is null);
        if (component is null) return found.Count > 0;

        var components = IcsDocument.Components(parsed).ToList();
        return found.Any(o => ReferenceEquals(SourceOf(o, components), component));
    }
```

**Le `firstOnly` conditionnel n'est pas une coquette** : narrowed, la première instance trouvée peut
venir d'un autre composant, et s'arrêter là répondrait faux. Le plafond de `Run` borne la marche
dans les deux cas.

- [ ] **Step 5 : les faire passer, puis toute la suite**

Run : `cd src && dotnet test` Expected : PASS.

- [ ] **Step 6 : commit**

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice
git commit -F - <<'EOF'
fix(caldav): le filtre est satisfait par un seul composant

RFC 4791 9.7.1 et 9.7.3 : le time-range rejoint le prop-filter sur le
composant, et un param-filter negatif ne matche plus sur rien.
EOF
```

---

### Task 6 : les répétitions d'une `VALARM`

**Ce que le client fait, en clair.** Il pose un rappel qui sonne une heure avant l'événement **et se
répète cinq fois toutes les dix minutes** — `TRIGGER:-PT1H`, `REPEAT:5`, `DURATION:PT10M`. Six
sonneries, pas une. Il demande ensuite « quels événements ont un rappel qui sonne entre 22h45 et
minuit ». Nous ne regardons que la première sonnerie ; les cinq autres n'existent pas pour nous, et
l'événement manque à la réponse.

**Ce que la mesure a trouvé** : `reports.xml` `alarm time-range query` t2 — un échec.

RFC 4791 § 9.9 sur un `VALARM` : chaque répétition est un instant de déclenchement à part entière.
RFC 5545 § 3.8.6.2 : `REPEAT` compte les répétitions **en plus** du déclenchement initial, et
`DURATION` en est l'espacement ; l'un sans l'autre ne vaut rien.

**Files:**
- Modify: `src/snoopy.microservice/Services/Calendar/OccurrenceExpander.cs`
- Test: `…/Services/OccurrenceExpanderTests.cs`, `…/Services/CalDav/CalendarQueryFilterTests.cs`

**Interfaces:**
- Consomme : `OccurrenceExpander.AlarmFires(IcsCalendar, DateTime, DateTime, string, CalendarEvent?)`,
  `Alarm.Trigger`, `Alarm.Repeat`, `Alarm.Duration` (Ical.Net 5.2.3 — **vérifier les noms réels
  avant d'écrire**, et employer ceux-là).
- Produit : rien de nouveau. `AlarmFires` déroule les répétitions.

- [ ] **Step 1 : écrire les tests qui échouent**

Dans `Services/OccurrenceExpanderTests.cs` :

```csharp
    [Fact]
    public void ARepeatedAlarm_FiresAtEachRepetition()
    {
        // RFC 5545 § 3.8.6.2: REPEAT:5 with DURATION:PT10M is five MORE rings after the trigger.
        // The event starts at 00:00, the trigger is an hour before, so the six rings run
        // 23:00, 23:10 … 23:50 of the day before.
        var parsed = Load(Ics.Alarmed("DTSTART:20270102T000000Z",
            "TRIGGER;RELATED=START:-PT1H", "REPEAT:5", "DURATION:PT10M"));

        Assert.True(Fires(parsed, "20270101T234500Z", "20270102T000000Z"));   // the fifth ring
        Assert.True(Fires(parsed, "20270101T230000Z", "20270101T230100Z"));   // the trigger itself
        Assert.False(Fires(parsed, "20270102T000500Z", "20270102T010000Z"));  // past the last one
    }

    [Fact]
    public void ARepeatWithoutADuration_RingsOnce()
    {
        // § 3.8.6.2 makes the two inseparable: REPEAT alone spaces nothing.
        var parsed = Load(Ics.Alarmed("DTSTART:20270102T000000Z", "TRIGGER;RELATED=START:-PT1H", "REPEAT:5"));

        Assert.False(Fires(parsed, "20270101T234500Z", "20270102T000000Z"));
        Assert.True(Fires(parsed, "20270101T230000Z", "20270101T230100Z"));
    }

    [Fact]
    public void AnAbsurdRepeatCount_IsBounded()
    {
        // A body may name REPEAT:1000000. The walk that answers a query must stay finite whatever
        // it is handed; the window decides, never the file.
        var parsed = Load(Ics.Alarmed("DTSTART:20270102T000000Z",
            "TRIGGER;RELATED=START:-PT1H", "REPEAT:1000000", "DURATION:PT1S"));

        Assert.True(Fires(parsed, "20270101T230000Z", "20270101T230100Z"));
    }
```

`Ics.Alarmed(...)` et l'aide `Fires(parsed, from, to)` : si elles n'existent pas, les ajouter au
fixture et au fichier de test — **une seule fois, pas une par test**.

Et dans `CalendarQueryFilterTests.cs`, la forme que l'outil envoie :

```csharp
    [Fact]
    public void AnAlarmTimeRange_SeesARepetition()
    {
        var parsed = Load(Ics.Alarmed("DTSTART:20270102T000000Z",
            "TRIGGER;RELATED=START:-PT1H", "REPEAT:5", "DURATION:PT10M"));

        Assert.True(Matches(parsed, Alarm("20270101T224500Z", "20270102T000000Z")));
    }
```

- [ ] **Step 2 : les faire échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~OccurrenceExpanderTests|FullyQualifiedName~CalendarQueryFilterTests"`
Expected : FAIL — seul le déclenchement initial est vu.

- [ ] **Step 3 : dérouler les répétitions**

Dans `Services/Calendar/OccurrenceExpander.cs`, la boucle interne d'`AlarmFires` :

```csharp
            foreach (var alarm in source.Alarms)
            {
                if (FiresAt(alarm.Trigger, start, end, parsed, zone) is not { } first) continue;
                foreach (var at in Rings(first, alarm))
                    if (at >= fromUtc && at < toUtc) return true;
            }
```

et la méthode qui les compte :

```csharp
    /// <summary>
    /// The instants one alarm rings at: its trigger, then RFC 5545 § 3.8.6.2's REPEAT more, spaced
    /// by DURATION — the two are inseparable, and either alone rings once. The count is bounded by
    /// the window rather than by the file: a REPEAT of a million is a body a client may send, and a
    /// query must stay finite whatever it is handed.
    /// </summary>
    private static IEnumerable<DateTime> Rings(DateTime first, Alarm alarm)
    {
        yield return first;
        if (alarm.Repeat is not { } count || count <= 0 || alarm.Duration is not { } spacing) yield break;

        var step = spacing.ToTimeSpanUnspecified();
        if (step <= TimeSpan.Zero) yield break;

        var at = first;
        for (var i = 0; i < Math.Min(count, MaxRings); i++)
        {
            at = Shift(at, step);
            yield return at;
        }
    }

    /// <summary>The repetitions one alarm may contribute to a window. Ten a day for the day of
    /// slack the walk carries on each side, which is more than any real reminder writes.</summary>
    private const int MaxRings = 10_000;
```

`alarm.Repeat` et `alarm.Duration` : Ical.Net 5.2.3 peut les nommer autrement, ou les rendre `int`
plutôt que `int?`. **Lire le type, employer ses noms**, et si `Duration` y est une `Duration` et non
un `TimeSpan`, la convertir comme `FiresAt` le fait déjà (`ToTimeSpanUnspecified`). Un `try/catch`
autour de la conversion si elle peut lever : une alarme illisible ne sonne pas, elle ne fait pas
tomber la requête.

- [ ] **Step 4 : les faire passer, puis toute la suite**

Run : `cd src && dotnet test` Expected : PASS.

- [ ] **Step 5 : commit**

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice
git commit -F - <<'EOF'
fix(calendar): une VALARM repetee sonne a chaque repetition

RFC 5545 3.8.6.2 : REPEAT compte les sonneries en plus du declenchement,
DURATION les espace. Le compte est borne par la fenetre.
EOF
```

---

### Task 7 : une borne de `time-range` absente est servie ouverte

> Reprise de la **tâche 10 de la tranche**, jamais exécutée : elle était réservée à cette vague
> (décision 11). Elle est réécrite ici sur ce que la mesure a appris.

**Ce que le client demande, en clair** : « tout ce qui se passe à partir du 9 juin », sans date de
fin. C'est la question que DAVx⁵ pose à chaque synchronisation avec son réglage par défaut, et celle
qu'iOS pose au premier chargement. RFC 4791 § 9.9 : « If a start or end attribute is unspecified,
the server assumes unbounded limits in that direction. » 5c refermait la borne à cinq ans en
silence, et un événement unique posé plus loin n'arrivait jamais sur le téléphone.

**Ce que la mesure a changé.** La décision 5 annonçait que l'outil ne mesurerait **pas** ce point :
`timerange-low-limit` et `timerange-high-limit` sont éteintes, et les sept tests qu'elles gardent ne
partent pas. **C'est faux de deux d'entre eux** : `reports.xml` `time-range query` t13 et t14 ne sont
gardés par rien et **mesurent la fermeture** — deux échecs de plus que la table n'en prévoyait, et
ils tombent dans la colonne « non-conformité connue ». La décision 11 n'est donc plus fondée
seulement sur la lecture du code de DAVx⁵ : elle est mesurée.

**Files:**
- Create: `src/snoopy.microservice/Services/CalDav/AbsentBound.cs`
- Modify: `src/snoopy.microservice/Services/CalDav/TimeRangeSpec.cs`,
  `CalendarQueryFilter.cs`, `CalendarQuerySpec.cs`, `FreeBusyReport.cs`
- Modify: `src/snoopy.microservice/Services/Calendar/OccurrenceExpander.cs`
- Test: `…/Services/CalDav/CalendarQueryFilterTests.cs`, `…/Controllers/CalDavQueryTests.cs`,
  `…/Controllers/AppleDiscoveryReplayTests.cs`

**Interfaces:**
- Consomme : `CalendarQueryFilter.ParseTimeRange(XElement, bool bothRequired, XName? refusal)`,
  `TimeRangeSpec(DateTime, DateTime)`, `OccurrenceExpander.Overlaps(IcsCalendar, DateTime, DateTime,
  string, CalendarEvent?)` **tel que la tâche 5 l'a laissé**.
- Produit :
  - `internal enum AbsentBound { Closed, Open, Refused }` dans `Services/CalDav/AbsentBound.cs` ;
  - `CalendarQueryFilter.ParseTimeRange(XElement timeRange, AbsentBound absent, XName? refusal)` —
    `bothRequired` disparaît, absorbé par `AbsentBound.Refused` ;
  - `internal sealed record TimeRangeSpec(DateTime? FromUtc, DateTime? ToUtc)` avec
    `internal (DateTime From, DateTime To) Closed => (FromUtc!.Value, ToUtc!.Value);` ;
  - `OccurrenceExpander.Overlaps(IcsCalendar parsed, DateTime? fromUtc, DateTime? toUtc,
    string calendarTimeZone, CalendarEvent? component = null)`.

- [ ] **Step 1 : écrire les tests qui échouent**

Dans `CalendarQueryFilterTests.cs`, **remplacer les trois tests** qui figent la fermeture —
`AMissingEnd_IsClosedAtTheEnginesSpanFromTheStart`,
`AMissingStart_IsClosedAtTheEnginesSpanFromTheEnd` et
`ABoundAtTheEdgeOfTime_IsClosedAtTheEdge_NeverAnException`. **C'est la première des deux seules
inversions d'assertion de la vague** ; la justification tient dans le commentaire du premier :

```csharp
    [Fact]
    public void AMissingEnd_StaysMissing()
    {
        // RFC 4791 § 9.9: « the server assumes unbounded limits in that direction ». Closing at
        // five years answered wrong without saying so — DAVx5 asks this very question at every
        // sync, and reports.xml/time-range t13 measures it.
        var spec = CalendarQueryFilter.Parse(VEvent(TimeRange("20260907T090000Z", null)));

        Assert.Equal(new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc), spec.TimeRange!.FromUtc);
        Assert.Null(spec.TimeRange.ToUtc);
    }

    [Fact]
    public void AMissingStart_StaysMissing()
    {
        var spec = CalendarQueryFilter.Parse(VEvent(TimeRange(null, "20260907T090000Z")));

        Assert.Null(spec.TimeRange!.FromUtc);
        Assert.Equal(new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc), spec.TimeRange.ToUtc);
    }

    [Fact]
    public void ABoundAtTheEdgeOfTime_NeedsNoClosing_AndNeverThrows()
    {
        // What used to shift MaxSpan past DateTime.MaxValue and be clamped. Nothing is shifted now.
        var spec = CalendarQueryFilter.Parse(VEvent(TimeRange("99991231T000000Z", null)));

        Assert.Null(spec.TimeRange!.ToUtc);
        Assert.NotNull(CalendarQueryFilter.Parse(VEvent(TimeRange(null, "00010101T000000Z"))).TimeRange);
    }

    [Fact]
    public void AVAlarmTimeRange_KeepsItsClosure()
    {
        // The alarm walk reads a WHOLE window rather than stopping at a first hit; leaving it open
        // would reread the calendar at every query. Named in the spec as a known non-conformity
        // that no targeted client exercises.
        var spec = CalendarQueryFilter.Parse(VEvent(Comp("VALARM", TimeRange("20260907T090000Z", null))));

        var from = new DateTime(2026, 9, 7, 9, 0, 0, DateTimeKind.Utc);
        Assert.Equal(from + OccurrenceExpander.MaxSpan, spec.AlarmFilters.Single().TimeRange!.ToUtc);
    }
```

`ParseTimeRange_DemandsBothBounds_WhenTold_AndAnswersA400WithoutACondition` change de **signature**,
pas de valeur : `true` devient `AbsentBound.Refused`, `false` devient `AbsentBound.Open`. Ce n'est
pas une inversion.

`AWindowWiderThanTheEnginesSpan_IsMalformed` **reste vert sans changer** : une fenêtre à deux bornes
plus large que cinq ans reste refusée. C'est l'incohérence assumée et écrite de la décision 11 — la
requête la plus étroite est la seule refusée — et un commentaire du test doit le dire.

Dans `CalDavQueryTests.cs` :

```csharp
    [Fact]
    public async Task ATimeRangeWithStartAlone_ServesAnEventBeyondTheFiveYearsTheEngineWalks()
    {
        // « Renouvellement du passeport », March 2032: the event that used to vanish from the
        // phone without anything saying so.
        GivenEvent("passeport.ics", Ics.Single("DTSTART:20320315T090000Z", "DTEND:20320315T100000Z"));

        var response = await Report(Calendar(), QueryBody(VEvent(TimeRange("20260907T000000Z", null))));

        Assert.Equal(207, response.StatusCode);
        Assert.Contains(Href("passeport.ics"), HrefsOf(response));
    }

    [Fact]
    public async Task AnEndlessSeries_MatchesAnOpenWindow_WithoutBeingWalkedToTheEnd()
    {
        GivenEvent("standup.ics", Ics.Single("DTSTART:20260907T090000Z", "DTEND:20260907T093000Z",
            "RRULE:FREQ=DAILY"));

        var response = await Report(Calendar(), QueryBody(VEvent(TimeRange("20260907T000000Z", null))));

        Assert.Equal([Href("standup.ics")], HrefsOf(response));
    }
```

- [ ] **Step 2 : les faire échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~CalendarQueryFilterTests|FullyQualifiedName~CalDavQueryTests"`
Expected : FAIL — `spec.TimeRange.ToUtc` est fermé à `MaxSpan`, et `passeport.ics` est absent.

- [ ] **Step 3 : la fenêtre peut avoir une borne absente**

`Services/CalDav/AbsentBound.cs`, nouveau :

```csharp
namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>What a time-range does with a bound the client did not write. RFC 4791 § 9.9 reads it
/// as an infinity; only the callers that cannot walk one close or refuse it.</summary>
internal enum AbsentBound
{
    /// <summary>Closed at <see cref="Calendar.OccurrenceExpander.MaxSpan"/> of the other — the
    /// VALARM filter, whose walk reads a whole window rather than stopping at a first hit.</summary>
    Closed,

    /// <summary>Left absent — the VEVENT filter of a calendar-query, what DAVx5 and iOS send.</summary>
    Open,

    /// <summary>Refused — free-busy-query, which composes a VFREEBUSY between two instants.</summary>
    Refused,
}
```

`Services/CalDav/TimeRangeSpec.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>A window <c>[FromUtc, ToUtc[</c> where a null bound is an infinity, as RFC 4791 § 9.9
/// reads an absent one. <see cref="Closed"/> is for the callers that demanded both.</summary>
internal sealed record TimeRangeSpec(DateTime? FromUtc, DateTime? ToUtc)
{
    internal (DateTime From, DateTime To) Closed => (FromUtc!.Value, ToUtc!.Value);
}
```

- [ ] **Step 4 : `ParseTimeRange` décide par appelant**

```csharp
    internal static TimeRangeSpec ParseTimeRange(XElement timeRange, AbsentBound absent, XName? refusal)
    {
        var start = Bound(timeRange, "start", refusal);
        var end = Bound(timeRange, "end", refusal);
        if (start is null && end is null)
            throw Refusal(refusal, "A time-range names no bound the report can close.");
        if (absent == AbsentBound.Refused && (start is null || end is null))
            throw Refusal(refusal, "A time-range names no bound the report can close.");

        var (from, to) = absent switch
        {
            AbsentBound.Open => (start, end),
            _ => (start ?? OccurrenceExpander.Shift(end!.Value, -OccurrenceExpander.MaxSpan),
                  end ?? OccurrenceExpander.Shift(start!.Value, OccurrenceExpander.MaxSpan)),
        };

        if (from is { } f && to is { } t)
        {
            if (t <= f) throw Refusal(refusal, "A time-range ends before it starts.");
            if (t - f > OccurrenceExpander.MaxSpan)
                throw Refusal(refusal,
                    $"A time-range spans more than the {OccurrenceExpander.MaxYears} years served.");
        }

        return new TimeRangeSpec(from, to);
    }
```

Les trois appelants passent leur intention explicitement — **aucun paramètre par défaut**, qui
laisserait un appelant sur l'ancien comportement sans le dire :
- le `time-range` du `comp-filter VEVENT` → `AbsentBound.Open` ;
- celui du `comp-filter VALARM` → `AbsentBound.Closed` ;
- `FreeBusyReport` → `AbsentBound.Refused`, et son corps lit `range.Closed` au lieu de
  `range.FromUtc` / `range.ToUtc` sur ses trois usages.

- [ ] **Step 5 : le marcheur accepte une fenêtre ouverte**

Dans `Services/Calendar/OccurrenceExpander.cs`, `Overlaps` **tel que la tâche 5 l'a laissé** :

```csharp
    /// <summary>Whether one instance at least overlaps the window — the very walk of
    /// <see cref="Expand"/>, stopped at the first one found (RFC 4791 § 9.9 on a VEVENT). A null
    /// bound is that side's infinity: an endless series always answers an endless question, and the
    /// walk is lazy. <paramref name="component"/> narrows the instances to those one component
    /// sources, and null takes them all.</summary>
    internal static bool Overlaps(IcsCalendar parsed, DateTime? fromUtc, DateTime? toUtc,
        string calendarTimeZone, CalendarEvent? component = null)
    {
        var found = Over(Guid.Empty, Guid.Empty, parsed, fromUtc ?? DateTime.MinValue,
                toUtc ?? DateTime.MaxValue, calendarTimeZone, calendarTimeZone)
            .Run(firstOnly: component is null);
        if (component is null) return found.Count > 0;

        var components = IcsDocument.Components(parsed).ToList();
        return found.Any(o => ReferenceEquals(SourceOf(o, components), component));
    }
```

et le plafond d'occurrences se calcule sur une fenêtre **bornée**, pour qu'une série finie très
dense sans aucun hit ne fasse pas marcher jusqu'à la fin des temps. Dans `Expansion` :

```csharp
        // The cap grows with the window, so an open one would not bound it at all: it is computed
        // on at most MaxSpan. A no-op for every closed window, all of which are refused past that.
        private int Cap => CapFor(fromUtc, Min(toUtc, Shift(fromUtc, MaxSpan)));

        private static DateTime Min(DateTime left, DateTime right) => left < right ? left : right;
```

`Matches` passe désormais `range.FromUtc` et `range.ToUtc` tels quels (via `OverlapsFrom`, que la
tâche 5 a posé) ; `MatchesAlarmFilter` lit `range.Closed` — sa fenêtre a toujours ses deux bornes.

- [ ] **Step 6 : la présélection suit sans effort**

`DavCalendarReader.CandidatesAsync` accepte **déjà** une borne nulle de chaque côté : chaque borne
est appliquée sous son propre `if (… is { } …)`. `CalendarQueryReport` passe déjà
`spec.Preselection?.FromUtc` / `?.ToUtc`, qui restent des `DateTime?`. Une série sans fin porte
`IcsProjector.NoEnd` (2100) en `LastOccurrence`, pas `start + 5 ans` : rien à changer.

`CalendarQuerySpec.Preselection` compose l'enveloppe des fenêtres d'alarme, **toutes fermées** :
`windows.Min(w => w.FromUtc)` et `windows.Max(w => w.ToUtc)` sur des `DateTime?` rendent des
`DateTime?` non nuls. Vérifier à la compilation, **ne rien « corriger » de plus**.

- [ ] **Step 7 : les faire passer, retirer le `Skip` du rejeu Apple, lancer tout**

Retirer le `Skip` du test `calendar-query` à `start` seul d'`AppleDiscoveryReplayTests` (posé par la
tâche 8 de la tranche, en attente exactement de ce correctif).

Run : `cd src && dotnet test` Expected : PASS.

- [ ] **Step 8 : commit**

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice
git commit -F - <<'EOF'
fix(caldav): une borne de time-range absente est servie ouverte

RFC 4791 9.9 : une borne absente vaut l'infini. Le VALARM garde sa
fermeture, free-busy-query ses deux bornes.
EOF
```

---

### Task 8 : `<C:timezone>` — la zone que la requête pose

**La plus grosse de la vague, la seule qui soit une fonctionnalité, et onze échecs en dépendent.**

**Ce que le client fait, en clair.** Il demande « les événements du 1er janvier », et il précise
dans quel fuseau il entend « le 1er janvier » : à New York, le 1er janvier court du 1er à 05:00 UTC
au 2 à 05:00 UTC ; à Los Angeles, du 1er à 08:00 au 2 à 08:00. Un événement tout-journée ou à heure
flottante — un fichier qui dit « le 17 octobre » sans dire où — tombe dans l'une et pas dans
l'autre. Le client écrit ce fuseau dans un élément `<C:timezone>` de sa requête. **Nous ne lisons
jamais cet élément**, et nous résolvons tout dans le fuseau de l'agenda : le client reçoit les
événements d'un autre jour que celui qu'il a demandé, sans que rien ne le lui dise.

RFC 4791 § 9.8 : « the server **MUST** rely on the specified VTIMEZONE component instead of the
CALDAV:calendar-timezone property … to resolve "date" values and "date with local time" values ».
Le jeton `calendar-access` de notre en-tête `DAV:` promet ce MUST (§ 5.1) : ne pas le tenir est une
**non-conformité**, et elle n'était nommée ni dans la décision 5 ni dans `calendar-5c-residuals.md`.

**Ce que la mesure a trouvé** — deux moitiés du même MUST, onze échecs :

| Moitié | Ce qui manque | Échecs |
|---|---|---|
| (a) l'élément n'est lu nulle part | `Services/CalDav/` ne référence pas ce nom ; il n'est ni validé ni employé | `reports.xml` `time-range` t12a, `limit/expand` t9a et t10 ; `floating.xml` `calendar without timezone` t2 et `with timezone` t2 |
| (b) un tout-journée est posé à minuit **UTC** | `OccurrenceExpander.Span`, branche `IsAllDay` : la zone reçue est ignorée | `floating.xml` `calendar without timezone` t1, `with timezone` t1 et t3, `free-busy` t2 — cette dernière est la **seule mesure isolée** de (b) |
| (c) un corps de fuseau portant autre chose qu'un `VTIMEZONE` est accepté | `CalendarPropertyValue.Zone` compte les `VTIMEZONE` mais pas le reste | `errors.xml` `Invalid CalDAV:timezone` t1 (un `REPORT`) et t2 (un `PROPPATCH`) |

**Portée : le `calendar-query` et lui seul.** RFC 4791 § 9.5 met `timezone?` dans sa grammaire ;
`calendar-multiget` (§ 9.6) et `free-busy-query` (§ 9.10) ne le portent pas. Ne pas en ajouter la
lecture ailleurs.

**Files:**
- Create: `src/snoopy.microservice/Services/CalDav/CalendarRequestTimeZone.cs`
- Modify: `src/snoopy.microservice/Services/CalDav/CalendarPropertyValue.cs`,
  `CalendarQueryReport.cs`, `EventMemberSource.cs`
- Modify: `src/snoopy.microservice/Services/Calendar/OccurrenceExpander.cs`
- Test: `…/Services/CalDav/CalDavPropertiesTests.cs` ou `CalendarPropertyUpdateTests.cs`,
  `…/Services/OccurrenceExpanderTests.cs`, `…/Controllers/CalDavQueryTests.cs`,
  `…/Controllers/CalDavProppatchTests.cs`, `…/Controllers/CalDavFreeBusyTests.cs`

**Interfaces:**
- Consomme : `CalendarPropertyValue.Zone(string)`, `IcsTimeZones.ResolveIana`,
  `IcsTimeZones.ToUtc(DateTime, string)`, `CalDavError.ValidCalendarData`,
  `EventMemberSource.Prepare(XDocument)`, `CalendarQueryFilter.Matches(…, string calendarTimeZone)`.
- Produit :
  - `CalendarRequestTimeZone.Of(XDocument body)` → `string?`, l'id IANA ou null, et une
    `DavPreconditionException(CalDavError.ValidCalendarData)` sur un corps que § 9.8 refuse ;
  - `EventMemberSource.Prepare(XDocument body, string timeZone)` — l'ancienne surcharge **disparaît**
    plutôt que d'être conservée avec une valeur par défaut : un appelant laissé sur l'ancienne zone
    sans le dire est exactement le défaut qu'on corrige ;
  - `OccurrenceExpander.Span` pose un tout-journée dans la zone reçue, et `Anchors` **disparaît**,
    devenue identique.

- [ ] **Step 1 : écrire les tests qui échouent**

Dans `Services/OccurrenceExpanderTests.cs` — la moitié (b), isolée :

```csharp
    [Fact]
    public void AnAllDayInstance_IsPosedInTheZoneItIsJudgedIn()
    {
        // RFC 4791 § 9.9: « 'Date' and 'date with local time' values are resolved using the time
        // zone specified in the CALDAV:timezone XML element of the request, or the
        // CALDAV:calendar-timezone property, or the server's chosen time zone. » January 1st in
        // New York runs from 05:00Z to 05:00Z, not from midnight UTC to midnight UTC.
        var parsed = Load(Ics.Single("DTSTART;VALUE=DATE:20270101", "DTEND;VALUE=DATE:20270102"));

        Assert.True(OccurrenceExpander.Overlaps(parsed,
            Utc("20270102T000000Z"), Utc("20270102T040000Z"), "America/New_York"));
        Assert.False(OccurrenceExpander.Overlaps(parsed,
            Utc("20270102T060000Z"), Utc("20270102T070000Z"), "America/New_York"));
    }

    [Fact]
    public void AnAllDayInstance_InUtc_IsUnchanged()
    {
        // Every caller that passes UTC — the API's default view — sees exactly what it saw before.
        var parsed = Load(Ics.Single("DTSTART;VALUE=DATE:20270101", "DTEND;VALUE=DATE:20270102"));

        Assert.True(OccurrenceExpander.Overlaps(parsed,
            Utc("20270101T000000Z"), Utc("20270101T010000Z"), IcsTimeZones.Utc));
    }
```

Dans `Services/CalDav/…` — la moitié (c) :

```csharp
    [Fact]
    public void ATimeZoneBodyCarryingAnything_ButOneVTimezone_NamesNoZone()
    {
        // RFC 4791 § 5.2.2 and § 9.8 both spell it « an iCalendar object with exactly one
        // VTIMEZONE component ». A VEVENT beside it is not that object, and storing its zone would
        // keep half of a body we refused to read.
        Assert.Null(CalendarPropertyValue.Zone(Ics.ZoneDocument("US/Eastern") + Ics.LooseEvent()));
        Assert.NotNull(CalendarPropertyValue.Zone(Ics.ZoneDocument("US/Eastern")));
    }
```

Dans `Controllers/CalDavQueryTests.cs` — la moitié (a), de bout en bout :

```csharp
    [Fact]
    public async Task ACalendarQueryTimeZone_DecidesWhichDayAFloatingEventFallsOn()
    {
        // floating.xml/calendar without timezone t2, in one test: the calendar is in UTC, the
        // request says US/Pacific, and the file says « 17 October » without saying where.
        GivenEvent("journee.ics", Ics.Single("DTSTART;VALUE=DATE:20281017", "DTEND;VALUE=DATE:20281018"));

        var inPacific = await Report(Calendar(), QueryBody(
            VEvent(TimeRange("20281018T060000Z", "20281018T070000Z")), zone: "America/Los_Angeles"));

        Assert.Contains(Href("journee.ics"), HrefsOf(inPacific));
    }

    [Fact]
    public async Task ACalendarQueryTimeZoneThatIsNotOne_IsRefused()
    {
        // reports.xml/time-range t12a: a VCALENDAR with no VERSION and no PRODID. § 9.8 names
        // CALDAV:valid-calendar-data.
        var response = await Report(Calendar(), QueryBody(VEvent(), rawZone: "BEGIN:VCALENDAR\r\nEND:VCALENDAR"));

        Assert.Equal(403, response.StatusCode);
        Assert.Contains("valid-calendar-data", await BodyOf(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutATimeZoneElement_TheCollectionsOwnZoneStillDecides()
    {
        // The fallback § 9.9 names second. Nothing about a client that sends no timezone changes.
        GivenCalendarZone("America/New_York");
        GivenEvent("journee.ics", Ics.Single("DTSTART;VALUE=DATE:20270101", "DTEND;VALUE=DATE:20270102"));

        var response = await Report(Calendar(), QueryBody(
            VEvent(TimeRange("20270102T000000Z", "20270102T040000Z"))));

        Assert.Contains(Href("journee.ics"), HrefsOf(response));
    }
```

`QueryBody(..., zone:)` et `QueryBody(..., rawZone:)` : deux paramètres facultatifs à ajouter à
l'aide du fichier — le premier pose un `<C:timezone>` porteur du bloc de la zone nommée, le second
pose le texte brut tel quel. **Aucun appel existant ne change.**

Et dans `Controllers/CalDavProppatchTests.cs`, la même moitié (c) sur l'écriture :

```csharp
    [Fact]
    public async Task ACalendarTimezoneCarryingAVEvent_IsRefusedProperty()
    {
        // errors.xml/Invalid CalDAV:timezone t2: badprops, not a stored half-body.
        var response = await PropPatch(Calendar(), Set(CalendarPropertyValue.TimeZone,
            Ics.ZoneDocument("US/Eastern") + Ics.LooseEvent()));

        Assert.Equal(207, response.StatusCode);
        Assert.Contains("403", StatusOf(response, CalendarPropertyValue.TimeZone));
    }
```

- [ ] **Step 2 : les faire échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~OccurrenceExpanderTests|FullyQualifiedName~CalDavQueryTests|FullyQualifiedName~CalDavProppatchTests|FullyQualifiedName~CalendarPropertyUpdateTests"`
Expected : FAIL sur les six nouveaux.

- [ ] **Step 3 : ce qu'est « un objet iCalendar à un seul VTIMEZONE »**

Dans `Services/CalDav/CalendarPropertyValue.cs` :

```csharp
    /// <summary>
    /// The IANA id the value carries — an iCalendar object holding exactly one VTIMEZONE
    /// <b>and nothing else</b> (RFC 4791 § 5.2.2 and § 9.8 spell it the same way), whose TZID TZDB
    /// or the Windows mapping answers — or null, which every caller refuses with
    /// <c>CALDAV:valid-calendar-data</c>. More than one block names no zone: the property is
    /// singular, and picking one of two would store the wrong one silently.
    /// </summary>
    internal static string? Zone(string value)
    {
        if (IcsDocument.TryLoad(value) is not { } document || document.TimeZones.Count != 1) return null;
        if (document.Events.Count > 0 || document.Todos.Count > 0
            || document.Journals.Count > 0 || document.FreeBusy.Count > 0) return null;
        return IcsTimeZones.ResolveIana(document.TimeZones[0].TzId);
    }
```

- [ ] **Step 4 : lire l'élément**

`Services/CalDav/CalendarRequestTimeZone.cs`, nouveau :

```csharp
using System.Xml.Linq;
using weesky.Snoopy.Microservice.Services.Dav;

namespace weesky.Snoopy.Microservice.Services.CalDav;

/// <summary>
/// The CALDAV:timezone element of a calendar-query (RFC 4791 § 9.8): the zone every « date » and
/// « date with local time » value of THAT request is resolved in, ahead of the collection's own
/// calendar-timezone. § 9.8 makes the precedence a MUST — a report that reads the collection's
/// instead answers another day's events without saying so.
/// </summary>
internal static class CalendarRequestTimeZone
{
    private static readonly XName Element = DavXml.CalDav + "timezone";

    /// <summary>The IANA id the body names, or null when it names none. Throws
    /// <see cref="DavPreconditionException"/> (<c>CALDAV:valid-calendar-data</c>) on a value § 9.8
    /// refuses — its own grammar is « an iCalendar object with exactly one VTIMEZONE component ».</summary>
    internal static string? Of(XDocument body) =>
        body.Root?.Element(Element) is not { } element
            ? null
            : CalendarPropertyValue.Zone(element.Value)
              ?? throw new DavPreconditionException(CalDavError.ValidCalendarData);
}
```

- [ ] **Step 5 : la faire descendre**

Dans `Services/CalDav/CalendarQueryReport.cs`, `WriteAsync` — **avant** le premier octet, comme
toute autre refus du rapport :

```csharp
        var filter = body.Root!.Element(FilterElement) ?? throw new DavPreconditionException(CalDavError.ValidFilter);
        var spec = CalendarQueryFilter.Parse(filter);
        // § 9.8's precedence, and the MUST of this task: the request's zone, else the collection's.
        var zone = CalendarRequestTimeZone.Of(body) ?? calendar.TimeZone;
        var (request, resolver) = source.Prepare(body, zone);
```

et, plus bas, `CalendarQueryFilter.Matches(parsed, spec, zone)`.

Dans `Services/CalDav/EventMemberSource.cs`, `Prepare` prend la zone et cesse de lire
`calendar.TimeZone` :

```csharp
    /// <param name="timeZone">the zone this report resolves dates in — the request's CALDAV:timezone
    /// where it named one (RFC 4791 § 9.8), the collection's otherwise. Never read off the
    /// collection here: the caller is the layer that knows which of the two § 9.8 wants.</param>
    public (DavPropertyRequest Request, IDavMemberResolver<DavEvent> Resolver) Prepare(
        XDocument body, string timeZone)
    {
        if (ReportRequest.KindOf(body) is not (DavReportKind.CalendarMultiget or DavReportKind.CalendarQuery))
            return (DavPropertyRequest.Parse(body), new Resolver(ContextOf, resolve, timeZone, null));

        var request = CalendarDataRequest.PropertiesAsked(body);
        var calendarData = CalendarDataRequest.Asked(body); // may refuse — before anything is written
        return (request, new Resolver(ContextOf, resolve, timeZone, calendarData));
    }
```

Les autres appelants de `Prepare` — multiget, sync-collection, PROPFIND — passent
`calendar.TimeZone` explicitement. **Les chercher tous** (`grep -rn "\.Prepare("`) : la suppression
de l'ancienne surcharge fait échouer la compilation sur chacun, ce qui est le but.

- [ ] **Step 6 : un tout-journée est posé dans cette zone**

Dans `Services/Calendar/OccurrenceExpander.cs` :

```csharp
    /// <summary>
    /// The instants the window is judged on: a dated instance as it stands, a floating or all-day
    /// one posed in <paramref name="zone"/> — RFC 4791 § 9.9's reading, where a « date » value is
    /// resolved in the request's CALDAV:timezone, else the collection's, else the server's. This is
    /// also what a free-busy-query reads a busy period's own bounds from, and what a trigger is
    /// measured from: a reminder rings on the wall clock of the zone it is read in.
    /// </summary>
    internal static (DateTime StartUtc, DateTime EndUtc) Span(EventOccurrence occurrence, string zone) => occurrence switch
    {
        { IsAllDay: true } => (IcsTimeZones.ToUtc(Midnight(occurrence.StartDate!.Value), zone),
                               IcsTimeZones.ToUtc(Midnight(occurrence.EndDateExclusive!.Value), zone)),
        { IsFloating: true } => (IcsTimeZones.ToUtc(occurrence.LocalStart!.Value, zone),
                                 IcsTimeZones.ToUtc(occurrence.LocalEnd!.Value, zone)),
        _ => (occurrence.StartUtc!.Value, occurrence.EndUtc!.Value),
    };
```

**`Anchors` disparaît** : elle est désormais mot pour mot `Span`, et deux copies d'une même règle
sont deux vérités. Son appelant, `AlarmFires`, lit `Span(occurrence, zone)`.

- [ ] **Step 7 : les faire passer, puis toute la suite**

Run : `cd src && dotnet test` Expected : PASS.

**C'est ici que la seconde inversion assumée peut tomber.** Une assertion existante qui fige un
tout-journée à minuit UTC **avec une zone non-UTC** décrivait la moitié (b) du défaut. Chaque
inversion est justifiée par écrit contre § 9.9 dans le rapport de tâche, avec le nom du test et la
valeur avant/après. Une assertion qui passe UTC ne bouge pas : c'est ce que le second test du step 1
garde.

Regarder en particulier `CalendarEventsControllerTests` et `CalDavFreeBusyTests` : la fenêtre de
l'API porte une zone de vue, et un tout-journée y change de bord d'un jour au plus. **Si le décalage
observé dépasse une journée, c'est un bug, pas une correction** : s'arrêter et le dire.

- [ ] **Step 8 : commit**

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice
git commit -F - <<'EOF'
feat(caldav): le CALDAV:timezone d'une requete est lu et applique

RFC 4791 9.8, MUST : la zone de la requete prime sur celle de l'agenda,
et un tout-journee est pose dans cette zone, plus a minuit UTC.
EOF
```

---

### Task 9 : `MKCALENDAR` — le corps du refus, et le corps refusé

**Deux corrections indépendantes sur le même verbe.**

**(a) Un refus sans corps.** Le client crée un agenda à une adresse qui en porte déjà un. Nous
répondons `405` avec l'en-tête `Allow`, et rien d'autre. RFC 4918 § 9.3.1 fait du `405` **la bonne
réponse** et RFC 7231 § 6.5.5 rend l'`Allow` obligatoire : le MUST est tenu. Ce qui manque est le
**corps** que RFC 4918 § 16 recommande (SHOULD), nommant la précondition `DAV:resource-must-be-null`
— sans lui, le client lit un code et devine.

> **Ces trois tests resteront rouges après la correction**, et c'est écrit d'avance :
> `mkcalendar.xml` attend un statut parmi `403`, `409` ou `507`, et notre `405` est celui que le RFC
> prescrit. Le passage final les montrera rouges ; le rapport les portera alors en **divergence
> nommée** — un statut, pas un corps — au lieu du défaut qu'ils sont aujourd'hui. On corrige le
> SHOULD parce qu'il est juste, pas parce qu'il verdit un test.

**(b) Un corps de création dont une propriété est protégée.** Le client envoie un `MKCALENDAR`
portant `DAV:getetag` — une propriété que RFC 4918 § 15.6 déclare protégée, **MUST NOT** être posée
— à côté de `displayname` et `calendar-description`. Nous ignorons ce que nous ne connaissons pas,
créons l'agenda et répondons `201`. RFC 4791 § 5.3.1 fait traiter ce corps comme un `PROPPATCH`, et
RFC 4918 § 9.2 impose alors l'**atomicité** : aucune propriété n'est posée, rien n'est créé, et la
réponse nomme la fautive en échec et les autres en échec de dépendance. C'est le résidu 5c « une
couleur hors forme est ignorée, pas refusée », vu sur une propriété protégée.

**Ce que la mesure a trouvé** : `mkcalendar.xml` `without body` t2, `with body` t2 et t4 pour (a) ;
`with body` t3 et `read-free-busy` t1 (sa cascade) pour (b) — cinq échecs.

**Files:**
- Modify: `src/snoopy.microservice/Controllers/CalDavController.cs`
- Modify: `src/snoopy.microservice/Services/CalDav/MkCalendarRequest.cs`
- Test: `…/Controllers/CalDavMkcalendarTests.cs`

**Interfaces:**
- Consomme : `MultiStatusWriter.WriteCreationRefusalAsync(Response, root, refused, condition, ct)`,
  `DavError.WriteAsync`, `CalendarPropertyValue.Writable`, `CalDavError.*`,
  `DavHeaders.CalendarAllow`.
- Produit : `MkCalendarRequest` gagne `IReadOnlyList<XName> Refused` — les propriétés du corps qui
  ne sont ni les cinq inscriptibles ni `resourcetype` ni `supported-calendar-component-set`.

- [ ] **Step 1 : écrire les tests qui échouent**

Dans `Controllers/CalDavMkcalendarTests.cs` :

```csharp
    [Fact]
    public async Task AMkcalendarOnACollectionThatExists_NamesItsPrecondition()
    {
        // RFC 4918 § 9.3.1 gives the 405 and § 16 asks for the body that says why. The status is
        // the RFC's; only the reason was missing.
        await MakeCalendar("agenda");

        var response = await MakeCalendar("agenda");

        Assert.Equal(405, response.StatusCode);
        Assert.Equal(DavHeaders.CalendarAllow, response.Headers.Allow.ToString());
        Assert.Contains("resource-must-be-null", await BodyOf(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMkcalendarBodyNamingAProtectedProperty_CreatesNothing()
    {
        // RFC 4918 § 15.6: getetag is protected, MUST NOT be set. § 5.3.1 of RFC 4791 makes this
        // body a PROPPATCH, and § 9.2 makes a PROPPATCH atomic — so displayname does not land
        // « half », and no calendar is born.
        var response = await MakeCalendar("caltest3", body: MkCalendarBody(
            Set(DavXml.Dav + "getetag", "\"x\""),
            Set(CalendarPropertyValue.DisplayName, "Essai"),
            Set(CalendarPropertyValue.Description, "essai")));

        Assert.Equal(207, response.StatusCode);
        Assert.Equal(404, (await Get(Calendar("caltest3"))).StatusCode);
    }

    [Fact]
    public async Task AMkcalendarBodyNamingAPropertyWeSimplyDoNotServe_StillCreates()
    {
        // calendar-free-busy-set and its like: a client that sends one wants a calendar, not an
        // argument. Only PROTECTED and unknown-in-a-live-namespace properties refuse — the rule
        // MkCalendarRequest already documents, kept.
        var response = await MakeCalendar("caltest4", body: MkCalendarBody(
            Set(DavXml.CalDav + "calendar-free-busy-set", string.Empty)));

        Assert.Equal(201, response.StatusCode);
    }
```

Le troisième test **fige la limite de la correction** : la revue vérifie qu'il est présent et vert,
sinon la garde est trop large et casse Apple, qui envoie `calendar-free-busy-set`.

**La liste des propriétés refusées est donc courte et nommée**, jamais « tout ce que je ne connais
pas » : les propriétés **protégées** de RFC 4918 § 15 — `getetag`, `getcontentlength`,
`getlastmodified`, `creationdate`, `lockdiscovery`, `supportedlock` — plus `DAV:resourcetype`
lorsqu'il ne décrit pas un agenda, ce que le code juge déjà.

- [ ] **Step 2 : les faire échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~CalDavMkcalendarTests"` Expected : FAIL —
le premier n'a pas de corps, le deuxième répond `201`.

- [ ] **Step 3 : le corps du refus**

Dans `Controllers/CalDavController.cs`, `MakeCalendarAsync`, branche `NameTaken` :

```csharp
            if (created.Error == CalendarStore.NameTaken)
            {
                // The same answer the home gets, and for the same reason: the collection is there.
                // RFC 4918 § 9.3.1 gives the 405 and RFC 7231 § 6.5.5 the Allow; § 16 asks that a
                // refusal name its precondition, so the client reads a reason and not a code.
                Response.Headers.Allow = DavHeaders.CalendarAllow;
                await DavError.WriteAsync(Response, StatusCodes.Status405MethodNotAllowed,
                    DavXml.Dav + "resource-must-be-null", null, cancellationToken, Logger);
                trace.Condition = "resource-must-be-null";
                return;
            }
```

Vérifier que `DavError.WriteAsync` accepte un statut qu'on lui donne (le `RefuseAsync` de la base
lui passe déjà `403`) ; si sa signature impose le `403`, l'appeler avec le statut posé **avant**
sur `Response.StatusCode` et lire son code réel — **ne pas dupliquer l'écriture du document**.

Et le même corps sur `MakeCalendarUnderACalendarAsync` : il répond déjà `RefuseAsync(…
CalendarCollectionLocationOk …)`, qui écrit un `DAV:error`. Rien à faire là.

- [ ] **Step 4 : le corps refusé**

Dans `Services/CalDav/MkCalendarRequest.cs` :

```csharp
    /// <summary>The properties RFC 4918 § 15 declares protected: a body that sets one is a body
    /// § 9.2 must refuse whole, since § 5.3.1 of RFC 4791 makes this body a PROPPATCH.</summary>
    private static readonly XName[] Protected =
    [
        DavXml.Dav + "getetag", DavXml.Dav + "getcontentlength", DavXml.Dav + "getlastmodified",
        DavXml.Dav + "creationdate", DavXml.Dav + "lockdiscovery", DavXml.Dav + "supportedlock",
    ];
```

et, dans `Parse`, le calcul de la nouvelle composante :

```csharp
        var refused = asked.Select(property => property.Name).Where(Protected.Contains).Distinct().ToList();
```

`Refused` entre dans le record, documentée : « the protected properties the body sets, which stop
the creation whole — everything else it does not know is IGNORED, a client sending
`calendar-free-busy-set` wanting a calendar and not an argument ».

**Le record change d'arité, donc ses deux constructions aussi** : le retour anticipé du corps absent
— `new MkCalendarRequest(null, null, null, null, null, false, extendedMkcol, false)` — prend un
huitième argument `[]`, et le retour final la liste calculée. Le compilateur les trouve tous les
deux ; il n'y en a pas d'autre.

Dans le contrôleur, **avant** `CreateNamedAsync` et après `AsksUnsupportedComponent` :

```csharp
            if (request.Refused.Count > 0)
            {
                // § 9.2's atomicity: the named property fails, and every other property of the body
                // fails in dependency — nothing is set, and nothing is created.
                await MultiStatusWriter.WriteCreationRefusalAsync(Response,
                    extendedWithBody ? MkcolResponse : MkcalendarResponse,
                    CalendarPropertyValue.Writable.Except(request.Refused).ToList(),
                    request.Refused, DavXml.Dav + "cannot-modify-protected-property", cancellationToken);
                trace.Responses = 1;
                trace.Condition = "cannot-modify-protected-property";
                return;
            }
```

Lire la signature réelle de `WriteCreationRefusalAsync` (elle prend une liste « en dépendance » et
une liste « refusées », plus la condition) et **employer la sienne** ; le troisième argument des
appels existants est `[]`, ce qui indique la place de la liste de dépendance.

- [ ] **Step 5 : les faire passer, puis toute la suite**

Run : `cd src && dotnet test` Expected : PASS.

- [ ] **Step 6 : commit**

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice
git commit -F - <<'EOF'
fix(caldav): MKCALENDAR nomme son refus et refuse une propriete protegee

RFC 4918 16 : le 405 porte resource-must-be-null. 15.6 et 9.2 : un corps
posant getetag ne cree rien, atomiquement.
EOF
```

---

### Task 10 : `PROPPATCH` — retirer ce qui n'est pas là

**Ce que le client fait, en clair.** Il envoie « supprime la propriété `details` de cet agenda ».
Cette propriété n'y a jamais été posée. Nous répondons `403`. RFC 4918 § 14.23, mot pour mot :
« Specifying the removal of a property that does not exist is not an error. » Le client, qui
nettoie, lit un refus et croit avoir un problème de droits.

**Ce que la mesure a trouvé** : `proppatch.xml` `prop patches` t2 et t3, `prop patch property
attributes` t2 — trois échecs. C'est le candidat que la spec § 5 laissait à trancher après mesure :
**il est tranché**.

**La nuance qui fait la correction juste** : nous ne stockons que cinq propriétés inscriptibles
(5c § 11), et un `remove` d'une propriété que nous ne stockons pas ne retire rien — **ce qui est
exactement ce que le RFC demande de répondre `200` à**. Le refus reste dû pour un `remove` d'une
propriété **protégée** (`getetag`, `resourcetype`…) et d'une propriété **vivante** qu'un agenda
porte toujours (`displayname`, `calendar-color`, `calendar-order`, `calendar-timezone`) : un agenda
sans nom est une ligne blanche dans chaque client.

**Files:**
- Modify: `src/snoopy.microservice/Services/CalDav/CalendarPropertyUpdate.cs`
- Test: `…/Services/CalDav/CalendarPropertyUpdateTests.cs`, `…/Controllers/CalDavProppatchTests.cs`
- Vérifier (sans forcément modifier) : l'équivalent du carnet, `Services/CardDav/`

**Interfaces:**
- Consomme : `CalendarPropertyUpdate.Parse(XDocument?)`, `CalendarPropertyValue.*`.
- Produit : `Judge` distingue trois sorts d'un `remove` au lieu de deux.

- [ ] **Step 1 : écrire les tests qui échouent**

```csharp
    [Fact]
    public void RemovingAPropertyThatWasNeverSet_IsNotAnError()
    {
        // RFC 4918 § 14.23, word for word: « Specifying the removal of a property that does not
        // exist is not an error ». We store five properties; removing a sixth removes nothing,
        // which is precisely what the RFC asks us to answer 200 to.
        var update = CalendarPropertyUpdate.Parse(Remove(Dead("details")));

        Assert.Empty(update.Refused);
        Assert.Contains(Dead("details"), update.Accepted.Keys);
    }

    [Fact]
    public void RemovingTheDescription_StillEmptiesIt()
    {
        var update = CalendarPropertyUpdate.Parse(Remove(CalendarPropertyValue.Description));

        Assert.Null(update.Accepted[CalendarPropertyValue.Description]);
    }

    [Theory]
    [InlineData("displayname")]
    [InlineData("resourcetype")]
    public void RemovingAPropertyTheCalendarAlwaysCarries_IsStillRefused(string local)
    {
        // A calendar with no name is a blank row in every client, and resourcetype is protected
        // (RFC 4918 § 15.9). § 14.23 is about properties that are not there.
        Assert.Contains(DavXml.Dav + local, CalendarPropertyUpdate.Parse(Remove(DavXml.Dav + local)).Refused);
    }
```

`Dead(...)` fabrique un nom mort dans un espace de noms qui n'est ni `DAV:` ni CalDAV ni Apple —
`XNamespace.Get("http://example.com/ns/") + name` — et `Remove(...)` un `DAV:propertyupdate` à un
seul `DAV:remove`.

Et le bout en bout, dans `Controllers/CalDavProppatchTests.cs` :

```csharp
    [Fact]
    public async Task RemovingAPropertyThatWasNeverSet_Answers200InItsPropstat()
    {
        var response = await PropPatch(Calendar(), Remove(Dead("details")));

        Assert.Equal(207, response.StatusCode);
        Assert.Contains("200", StatusOf(response, Dead("details")));
    }
```

- [ ] **Step 2 : les faire échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~CalendarPropertyUpdateTests|FullyQualifiedName~CalDavProppatchTests"`
Expected : FAIL — les trois `remove` sortent en `Refused`.

- [ ] **Step 3 : trois sorts, pas deux**

Dans `Services/CalDav/CalendarPropertyUpdate.cs`, `Judge` :

```csharp
    /// <summary>The properties a calendar always carries, and the protected ones: a DAV:remove of
    /// either is the 403 of § 9.2.1 — a calendar with no name is a blank row in every client, and
    /// § 15 makes the protected ones unsettable. Everything else a remove names is not there, and
    /// § 14.23 says removing what is not there is not an error.</summary>
    private static readonly XName[] Undeletable =
    [
        CalendarPropertyValue.DisplayName, CalendarPropertyValue.Color, CalendarPropertyValue.Order,
        CalendarPropertyValue.TimeZone, CalendarPropertyValue.ResourceType,
        CalendarPropertyValue.ComponentSet,
        DavXml.Dav + "getetag", DavXml.Dav + "getcontentlength", DavXml.Dav + "getlastmodified",
        DavXml.Dav + "creationdate", DavXml.Dav + "lockdiscovery", DavXml.Dav + "supportedlock",
        DavXml.Dav + "sync-token", DavXml.Dav + "current-user-principal", DavXml.Dav + "owner",
        DavXml.Dav + "supported-report-set", DavXml.Dav + "current-user-privilege-set",
    ];

    private static (bool Stored, string? Value) Judge(XElement property, bool removing)
    {
        if (removing)
        {
            // Only the description is stored AND emptiable; the rest is either always carried, or
            // was never there — and § 14.23 answers 200 to the removal of what is not there.
            if (property.Name == CalendarPropertyValue.Description) return (true, null);
            return Undeletable.Contains(property.Name) ? (false, null) : (true, null);
        }
        …
```

**Attention à ce que `Accepted` porte alors** : une propriété morte y entre avec la valeur null,
donc l'appelant qui écrit en base doit continuer de n'écrire **que** les cinq inscriptibles. Vérifier
le consommateur d'`Accepted` (`CalDavController`, `PropPatchCalendarAsync`) : s'il itère sur
`Accepted` pour écrire, la propriété morte doit y être ignorée, pas écrite. Si cette séparation
n'est pas naturelle, ajouter à `CalendarPropertyUpdate` un troisième champ
`IReadOnlyList<XName> RemovedAndAbsent` plutôt que de faire porter à `Accepted` deux sens. **La
revue tranche sur la lisibilité du résultat, pas sur le nombre de lignes.**

- [ ] **Step 4 : le carnet, à côté**

Lire l'équivalent CardDAV (`Services/CardDav/`, le juge d'un `PROPPATCH` de carnet). S'il porte le
même refus, **le corriger de la même façon et avec son propre test** : le passage final le mesurera
et le § 0 du rapport est le repère. S'il n'a pas ce défaut, le dire dans le rapport de tâche.

- [ ] **Step 5 : les faire passer, puis toute la suite**

Run : `cd src && dotnet test` Expected : PASS.

- [ ] **Step 6 : commit**

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice
git commit -F - <<'EOF'
fix(dav): retirer une propriete absente n'est pas une erreur

RFC 4918 14.23. Les proprietes qu'un agenda porte toujours et les
protegees restent refusees.
EOF
```

---

### Task 11 : `calendar-timezone` hors d'`allprop`, et la zone que `floating.xml` trouve en arrivant

**Deux restes, sans rapport l'un avec l'autre, groupés parce que ni l'un ni l'autre ne mérite sa
propre revue.**

**(a) `calendar-timezone` en `allprop`.** RFC 4791 § 5.2.2 : « SHOULD NOT be returned by a PROPFIND
DAV:allprop request ». La raison est concrète : la propriété porte un `VTIMEZONE` entier, quelques
kilo-octets, et un `PROPFIND allprop` `Depth: 1` sur un home de dix agendas en verse dix. **Aucun
test de la campagne n'envoie d'`allprop`** : ce n'est pas un échec mesuré, c'est une lecture de
`CalDavProperties.CalendarTable`, consignée pour ne pas être perdue. Le mécanisme existe déjà —
`Excluding(...)`, employé sur `calendar-data` de la table `Event`.

**(b) La zone de `default` fuit d'`errors.xml` vers `floating.xml`.** `floating.xml` crée
`calendar-none` par un `MKCALENDAR` sans corps ; un agenda créé sans zone hérite de celle de
`default` (`CalendarStore` `:119`, choix de 5c). Or `errors.xml`, joué **avant** dans la liste, pose
`US/Eastern` sur `default` par un `PROPPATCH`. `calendar-none` naît donc en heure de New York, et le
test `free-busy` t1 — le seul qui suppose UTC — échoue. **La décision 9 n'avait prévu que la fuite
d'état du `<end>` de `ctag.xml` ; en voici une seconde, en sens inverse.**

Le correctif est du harnais et ne touche pas le produit : une **copie locale** de `floating.xml`
dont le `<start>` remet `default` en UTC avant tout le reste — même convention que les copies de
`reports.xml` et `delete.xml` (décision 10). **Ne pas réordonner `suites-caldav.txt`** : l'ordre est
celui du passage initial, et le passage final s'y compare.

**Files:**
- Modify: `src/snoopy.microservice/Services/CalDav/CalDavProperties.cs`
- Test: `…/Services/CalDav/CalDavPropertiesTests.cs`
- Create: `tools/caldavtester/suites/CalDAV/floating.xml`, `floating.xml.diff`
- Modify: `tools/caldavtester/suites/CalDAV/README.md`, `tools/caldavtester/suites-caldav.txt`

**Interfaces:**
- Consomme : `DavPropertyTables.Excluding(PropertySet, params XName[])`, `CalendarTable(int year)`.
- Produit : rien.

- [ ] **Step 1 : écrire le test qui échoue**

Dans `Services/CalDav/CalDavPropertiesTests.cs` :

```csharp
    [Fact]
    public void Allprop_PoursEveryCalendarProperty_ExceptTheTimeZone()
    {
        // RFC 4791 § 5.2.2, SHOULD NOT: the property carries a whole VTIMEZONE, and a Depth: 1
        // allprop on a home of ten calendars would pour ten of them. Named still answers it.
        var poured = Resolve(AllProp(), Calendar()).Found.Select(p => p.Name).ToList();

        Assert.DoesNotContain(CalendarPropertyValue.TimeZone, poured);
        Assert.Contains(CalendarPropertyValue.DisplayName, poured);
        Assert.Contains(CalendarPropertyValue.TimeZone,
            Resolve(Named(CalendarPropertyValue.TimeZone), Calendar()).Found.Select(p => p.Name));
    }
```

Employer les aides réelles du fichier pour bâtir une requête `allprop` et une requête nommée ; si
`CalDavPropertiesTests` n'en a pas, regarder comment `Excluding` est déjà testée sur
`calendar-data` de la table `Event` et **reprendre la même forme**.

- [ ] **Step 2 : le faire échouer**

Run : `cd src && dotnet test --filter "FullyQualifiedName~CalDavPropertiesTests"` Expected : FAIL —
`calendar-timezone` est versée.

- [ ] **Step 3 : l'exclure de l'allprop**

Dans `Services/CalDav/CalDavProperties.cs`, `CalendarTable` est **enveloppée** dans `Excluding` : le
`Set(...)` existant ne change pas d'une ligne, on ajoute l'appel autour et le nom exclu après.

```csharp
    // RFC 4791 § 5.2.2, SHOULD NOT: a named PROPFIND still answers calendar-timezone; allprop does
    // not pour it. Same mechanism, same reason as calendar-data on a member — it is large.
    private static PropertySet CalendarTable(int year) => Excluding(
        Set(/* les dix-huit entrees actuelles, mot pour mot, sans en toucher une */),
        DavXml.CalDav + "calendar-timezone");
```

- [ ] **Step 4 : la copie locale de `floating.xml`**

```bash
cd tools/caldavtester
cp .caldavtester/ccs-caldavtester/scripts/tests/CalDAV/floating.xml suites/CalDAV/floating.xml
```

Insérer **en tête du `<start>`**, avant le `MKCALENDAR` de `calendar-none`, une requête qui remet
`default` en UTC :

```xml
		<!-- Local copy, weesky: errors.xml, played earlier in suites-caldav.txt, PROPPATCHes
		     default's calendar-timezone to US/Eastern, and a calendar created with no zone
		     inherits default's. calendar-none would then be born in New York time, and
		     free-busy t1 - the one test that assumes UTC - fails on state another file left.
		     Nothing of the file's own tests changes. -->
		<request>
			<method>PROPPATCH</method>
			<ruri>$calendarpath1:/</ruri>
			<data>
				<content-type>text/xml; charset=utf-8</content-type>
				<filepath>suites/CalDAV/floating-default-utc.xml</filepath>
			</data>
		</request>
```

et écrire `tools/caldavtester/suites/CalDAV/floating-default-utc.xml`, un `DAV:propertyupdate` qui
pose `CALDAV:calendar-timezone` à un `VCALENDAR` portant le seul `VTIMEZONE` d'`UTC`.

**Le chemin `<filepath>` est relatif au répertoire de travail du tester** — vérifier comment les
copies existantes de `reports.xml` et `delete.xml` référencent leurs données, et faire pareil. Si le
chemin relatif ne se résout pas, poser le corps **en ligne** dans le `<data>` plutôt que d'inventer
une résolution de chemin.

Puis :
- `diff -u .caldavtester/ccs-caldavtester/scripts/tests/CalDAV/floating.xml suites/CalDAV/floating.xml > suites/CalDAV/floating.xml.diff` ;
- dans `suites-caldav.txt`, `CalDAV/floating.xml` devient `suites/CalDAV/floating.xml`, **à la même
  ligne** ;
- dans `suites/CalDAV/README.md`, une section « `floating.xml` » qui dit ce qui est **ajouté** (et
  non retiré, contrairement aux deux autres) et pourquoi, en reprenant le raisonnement ci-dessus.

Ajouter enfin, dans l'en-tête de `suites-caldav.txt`, sous la note « ORDRE », une ligne : la zone de
`default` fuit d'`errors.xml` vers ce qui suit, et `floating.xml` la remet ; réordonner la liste
demande de le revérifier.

- [ ] **Step 5 : les faire passer, puis toute la suite**

Run : `cd src && dotnet test` Expected : PASS. La partie harnais n'est **pas** testable hors ligne :
elle se vérifie au passage final.

- [ ] **Step 6 : commit**

```bash
cd src/snoopy.microservice && git checkout ApiDocumentation.xml
cd ../.. && git add -A src/snoopy.microservice tools/caldavtester
git commit -F - <<'EOF'
fix(caldav): calendar-timezone hors d'allprop, et floating.xml en UTC

RFC 4791 5.2.2, SHOULD NOT. Cote harnais, une copie locale remet la zone
de default avant que floating.xml cree son agenda.
EOF
```

---

## Après les onze tâches — l'exécution interactive

Rien ici ne s'automatise ; tout se consigne dans
[`calendar-5d-conformance.md`](../calendar-5d-conformance.md).

- [ ] **Le push, une seule fois, sur demande.** `git push origin caldav`, **isolé** — enchaîné à une
  autre commande, il est refusé par le classificateur du mode auto. Demander avant.

- [ ] **Le déploiement sur dev**, puis **quinze minutes pleines** avant de lancer l'outil : le
  passage `-Protocol Both` est exactement au plafond d'authentification (décision 2). Le compteur
  d'`AuthAttemptThrottle` est **en mémoire** — un redéploiement le vide, ce qui raccourcit l'attente
  d'autant, mais rien ne le garantit.

- [ ] **Le passage final** : `pwsh tools/caldavtester/run.ps1 -Protocol Both`. Consigner la ligne
  finale et le tableau par fichier dans le § 3 du rapport, **et dire à chaque comparaison lequel des
  deux repères CardDAV est commenté** — celui de 4d (`ok=107`) ou celui du § 0 (`ok=108`).

- [ ] **Ce que le rapport doit dire du passage final, et qu'aucun chiffre ne dira seul** :
  - les trois tests `resource-must-be-null` de `mkcalendar.xml` restent **rouges** par décision, et
    passent de « défaut » à « divergence nommée » (tâche 9) ;
  - les MUST `limit-recurrence-set` et `limit-freebusy-set` ne sont mesurés par **aucun** test joué :
    le test qui les envoie échoue avant, sur son `comp-filter VFREEBUSY`. **Ne pas le compter comme
    leur mesure** ;
  - `timezones.xml` t4 et t7 sont verts **faussement** (le vérificateur lit `props` là où le test
    passe `okprops`) : ne pas les compter comme une mesure non plus ;
  - la marque d'ordre d'octets (tâche 1) et, s'il y a lieu, le `remove` du carnet (tâche 10) doivent
    se voir **du côté CardDAV** : c'est la seule preuve que la correction partagée a servi les deux.

- [ ] **Étape 5 — les clients réels.** Thunderbird d'abord, scénarios 1 à 13 (8, 9 et 13 « non
  applicable »). Puis DAVx⁵ + Agenda Samsung, 1 à 13 (8 « non applicable »). **Les tâches 3 et 8
  sont celles qu'un client peut contredire** : la tâche 3 refuse désormais des corps que 5c
  acceptait, et la tâche 8 déplace un tout-journée d'au plus un jour. Si un client rougit là, c'est
  un faux refus ou une pose fautive, pas une nouvelle fonctionnalité à écrire. **Si cette vague
  touche `Services/Dav`, rejouer `-Protocol Both`.** Le scénario 12 régénère le secret : remettre
  `serverinfo.local.json` à jour avant tout rejeu de l'outil.

- [ ] **Étape 6 — Apple.** Lire le résultat d'`ical-client.xml` dans le passage final, et lancer
  `AppleDiscoveryReplayTests` sans son `Skip` (retiré par la tâche 7).

- [ ] **Étape 7 — la clôture.** `calendar-5c-residuals.md` ligne à ligne : le `param-filter` négatif
  se ferme (tâche 5), la présélection à plusieurs surcharges reste **ouverte** — aucun test ne
  l'a exercée. Écrire `calendar-5d-residuals.md`. **Régénérer le secret DAV du compte de test.**

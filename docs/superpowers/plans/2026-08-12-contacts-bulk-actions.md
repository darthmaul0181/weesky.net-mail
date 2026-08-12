# Actions groupées sur les contacts — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Donner à la liste de contacts la sélection multiple de la liste de messages, avec suppression en masse depuis la bande et mise en favori par glisser-déposer sur un scope.

**Architecture:** Deux routes backend prenant un tableau d'ids (un seul `SaveChanges`, id inconnu = no-op silencieux). Côté frontend, le squelette de la bande de sélection est extrait du module mail vers `src/components/`, `useSelection` devient générique sur la clé, et le module contacts reçoit une transcription de `dragMessages` plus les cases de ligne.

**Tech Stack:** ASP.NET Core 10 / EF Core (Pomelo) côté API ; React 18 + TypeScript + TanStack Query + Vitest + Testing Library côté frontend.

**Spec:** `docs/superpowers/specs/2026-08-12-contacts-bulk-actions-design.md`

## Global Constraints

- **Cap de 200 ids par lot.** Au-delà : 400. Même valeur que `PUT /Mail/Messages/Flags`.
- **Un id inconnu, ou appartenant à un autre utilisateur, est un no-op silencieux.** Jamais 404 sur un lot : un lot ne peut pas échouer à moitié.
- **Statuts des deux routes : 204 / 400 / 401.** Rien d'autre.
- **Un seul `SaveChanges` par appel.**
- **Les mutations frontend invalident `onSettled`**, jamais `onSuccess` — une écriture refusée doit laisser l'écran sur l'état du serveur.
- **Un token nomme un rôle, jamais une couleur.** Aucune couleur littérale ; réutiliser les tokens existants.
- **i18n :** toute clé ajoutée existe en `fr` **et** en `en` (`parity.test.ts`), avec espace insécable avant `: ? !`, guillemets `« »` et apostrophe `’` côté français. Toute clé atteignant `t()` doit être écrite en littéral dans le fichier qui l'utilise (`keys.test.ts` ne voit pas une clé passée en variable).
- **Pas de `@media (min-width: …)`**, et aucune troisième largeur : `responsive.test.ts` casse la build.
- **`dotnet test` sans `--no-build`** dès qu'un fichier de test est ajouté.
- **`src/snoopy.microservice/ApiDocumentation.xml` est régénéré par `dotnet test`** et versionné : le révertrer avant chaque commit backend s'il n'apporte que du bruit sans rapport.
- **Messages de commit :** deux lignes de description au maximum, et jamais un `@` en première ou dernière position.

---

## File Structure

**Backend**

| Fichier | Responsabilité |
|---|---|
| `Models/Contacts/BulkContactsRequest.cs` *(créer)* | `{ Ids }` — corps du DELETE en masse |
| `Models/Contacts/BulkFavoriteRequest.cs` *(créer)* | `{ Ids, IsFavorite }` — corps du PUT en masse |
| `Repositories/IContactStore.cs` *(modifier)* | deux méthodes de lot |
| `Repositories/ContactStore.cs` *(modifier)* | leur implémentation |
| `Controllers/ContactsController.cs` *(modifier)* | les deux routes |
| `snoopy.microservice.Tests/Repositories/ContactStoreBulkTests.cs` *(créer)* | tests du store |
| `snoopy.microservice.Tests/Controllers/ContactsControllerBulkTests.cs` *(créer)* | tests des routes |

**Frontend — socle partagé**

| Fichier | Responsabilité |
|---|---|
| `src/modules/mail/list/useSelection.ts` *(modifier)* | générique sur la clé |
| `src/components/SelectionBand.tsx` *(créer)* | case maîtresse + zone centrale + actions |
| `src/styles/selection.css` *(créer)* | les règles `.selection-*`, déplacées depuis `mail.css` |
| `src/main.tsx` *(modifier)* | import de la nouvelle feuille |
| `src/modules/mail/list/SelectionToolbar.tsx` *(modifier)* | consomme le squelette |

**Frontend — contacts**

| Fichier | Responsabilité |
|---|---|
| `src/modules/contacts/dragContacts.ts` *(créer)* | MIME, payload, `dragIds`, `canDropIntoScope` |
| `src/modules/contacts/ContactList.tsx` *(modifier)* | cases, bande, drag, confirmation |
| `src/modules/contacts/ContactScopes.tsx` *(modifier)* | cible de drop |
| `src/modules/contacts/queries.ts` *(modifier)* | deux mutations de lot |
| `src/api.js` *(modifier)* | deux appels |
| `src/index.css` *(modifier)* | case de tuile, gouttière, `is-dragging`, `.drop-ready` du scope |
| `src/locales/{fr,en}/contacts.json` *(modifier)* | libellés |
| `src/modules/mail/folders/FolderTree.tsx` + `src/styles/mail.css` *(modifier)* | `--drop-label` traduit |

---

## Task 1 : le store sait traiter un lot

**Files:**
- Modify: `src/snoopy.microservice/Repositories/IContactStore.cs`
- Modify: `src/snoopy.microservice/Repositories/ContactStore.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreBulkTests.cs` *(créer)*

**Interfaces:**
- Consumes: `ContactStore(PreferencesDbContext context)`, `FindAsync(userId, contactId, ct)`, `Result` de CSharpFunctionalExtensions.
- Produces:
  - `Task<int> DeleteManyAsync(Guid userId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)`
  - `Task<int> SetFavoriteManyAsync(Guid userId, IReadOnlyList<Guid> ids, bool isFavorite, CancellationToken cancellationToken)`
  - Les deux renvoient **le nombre de lignes réellement touchées** ; le contrôleur ne s'en sert pas mais les tests s'y appuient, et un futur rapport en aurait besoin.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ContactStoreBulkTests.cs`. Suivre le fixture des tests store existants (`TestDbContext`, une base InMemory par test).

```csharp
[Fact]
public async Task DeleteManyAsync_RemovesEveryContactAndItsAddresses()
{
    await using var context = TestDbContext.Create();
    var store = new ContactStore(context);
    var user = Guid.NewGuid();
    var first = (await store.CreateAsync(user, Write("Alice", "alice@x.example"), default)).Value;
    var second = (await store.CreateAsync(user, Write("Bob", "bob@x.example"), default)).Value;

    var removed = await store.DeleteManyAsync(user, [first, second], default);

    Assert.Equal(2, removed);
    Assert.Empty(await store.ListAsync(user, default));
    Assert.Empty(context.ContactEmails);
}

// Un lot ne peut pas échouer à moitié : l'id absent est ignoré, les autres partent.
[Fact]
public async Task DeleteManyAsync_IgnoresAnUnknownIdAndDeletesTheRest()
{
    await using var context = TestDbContext.Create();
    var store = new ContactStore(context);
    var user = Guid.NewGuid();
    var kept = (await store.CreateAsync(user, Write("Alice", "alice@x.example"), default)).Value;

    var removed = await store.DeleteManyAsync(user, [kept, Guid.NewGuid()], default);

    Assert.Equal(1, removed);
    Assert.Empty(await store.ListAsync(user, default));
}

// Le scope par utilisateur est la seule barrière : un id d'autrui ne résout rien.
[Fact]
public async Task DeleteManyAsync_LeavesAnotherUsersContactAlone()
{
    await using var context = TestDbContext.Create();
    var store = new ContactStore(context);
    var mine = Guid.NewGuid();
    var theirs = Guid.NewGuid();
    var foreign = (await store.CreateAsync(theirs, Write("Bob", "bob@x.example"), default)).Value;

    var removed = await store.DeleteManyAsync(mine, [foreign], default);

    Assert.Equal(0, removed);
    Assert.Single(await store.ListAsync(theirs, default));
}

[Fact]
public async Task SetFavoriteManyAsync_FlagsEveryContactItFinds()
{
    await using var context = TestDbContext.Create();
    var store = new ContactStore(context);
    var user = Guid.NewGuid();
    var first = (await store.CreateAsync(user, Write("Alice", "alice@x.example"), default)).Value;
    var second = (await store.CreateAsync(user, Write("Bob", "bob@x.example"), default)).Value;

    var touched = await store.SetFavoriteManyAsync(user, [first, second, Guid.NewGuid()], true, default);

    Assert.Equal(2, touched);
    Assert.All(await store.ListAsync(user, default), c => Assert.True(c.IsFavorite));
}

[Fact]
public async Task SetFavoriteManyAsync_ClearsTheFlagToo()
{
    await using var context = TestDbContext.Create();
    var store = new ContactStore(context);
    var user = Guid.NewGuid();
    var id = (await store.CreateAsync(user, Write("Alice", "alice@x.example", favorite: true), default)).Value;

    await store.SetFavoriteManyAsync(user, [id], false, default);

    Assert.False((await store.ListAsync(user, default)).Single().IsFavorite);
}

private static ContactWrite Write(string first, string address, bool favorite = false) =>
    new(first, string.Empty, string.Empty, favorite, [new ContactAddressWrite(address, string.Empty)]);
```

> Vérifier la forme exacte de `ContactWrite`/`ContactAddressWrite` dans `Models/Contacts/` avant d'écrire le helper : reprendre celui des tests store existants plutôt que d'en inventer un.

- [ ] **Step 2: Lancer les tests, vérifier qu'ils échouent**

Run: `cd src/snoopy.microservice && dotnet test --filter ContactStoreBulkTests`
Expected: échec de compilation — `DeleteManyAsync` n'existe pas.

- [ ] **Step 3: Déclarer les deux méthodes dans l'interface**

Dans `IContactStore.cs`, après `DeleteAsync` et `SetFavoriteAsync` :

```csharp
    /// <summary>
    /// Removes a batch and answers how many rows it actually held. An id this user does not own
    /// resolves to nothing and is skipped in silence: a batch may not half-fail, and telling an
    /// unknown id from a foreign one would say whether it exists.
    /// </summary>
    Task<int> DeleteManyAsync(Guid userId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Sets or clears the favourite flag over a batch, under the same silent-skip rule.</summary>
    Task<int> SetFavoriteManyAsync(
        Guid userId, IReadOnlyList<Guid> ids, bool isFavorite, CancellationToken cancellationToken);
```

- [ ] **Step 4: Implémenter dans `ContactStore`**

À placer après `SetFavoriteAsync`. Une seule requête pour trouver les lignes, un seul `SaveChanges` :

```csharp
    public async Task<int> DeleteManyAsync(
        Guid userId, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var rows = await context.Contacts
            .Where(c => c.UserId == userId && ids.Contains(c.Id))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return 0;

        // The FK cascades in MariaDB, but the InMemory provider the tests run on enforces none:
        // removing the children here is what makes the two behave alike — DeleteAsync's own reason.
        var found = rows.Select(r => r.Id).ToList();
        var addresses = await context.ContactEmails
            .Where(e => found.Contains(e.ContactId))
            .ToListAsync(cancellationToken);

        context.ContactEmails.RemoveRange(addresses);
        context.Contacts.RemoveRange(rows);
        await context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<int> SetFavoriteManyAsync(
        Guid userId, IReadOnlyList<Guid> ids, bool isFavorite, CancellationToken cancellationToken)
    {
        var rows = await context.Contacts
            .Where(c => c.UserId == userId && ids.Contains(c.Id))
            .ToListAsync(cancellationToken);
        if (rows.Count == 0) return 0;

        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.IsFavorite = isFavorite;
            row.UpdatedAt = now;
        }

        await context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }
```

- [ ] **Step 5: Lancer les tests, vérifier qu'ils passent**

Run: `cd src/snoopy.microservice && dotnet test --filter ContactStoreBulkTests`
Expected: 5 tests verts.

- [ ] **Step 6: Lancer toute la suite backend**

Run: `cd src/snoopy.microservice && dotnet test`
Expected: aucun échec. Si `ApiDocumentation.xml` a bougé sans rapport avec cette tâche, le révertrer.

- [ ] **Step 7: Commit**

```bash
git add src/snoopy.microservice/Repositories src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreBulkTests.cs
git commit -m "feat: le store des contacts traite un lot d'ids"
```

---

## Task 2 : les deux routes

**Files:**
- Create: `src/snoopy.microservice/Models/Contacts/BulkContactsRequest.cs`
- Create: `src/snoopy.microservice/Models/Contacts/BulkFavoriteRequest.cs`
- Modify: `src/snoopy.microservice/Controllers/ContactsController.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/ContactsControllerBulkTests.cs` *(créer)*

**Interfaces:**
- Consumes: `DeleteManyAsync` / `SetFavoriteManyAsync` (Task 1), `ApiBaseController.BadRequestEnveloppe`, `AuthenticatedUser.WebmailUid`, `ControllerTestHelpers.CreateAuthenticatedContext`.
- Produces: `DELETE /api/Contacts` et `PUT /api/Contacts/Favorite`, consommés par `api.js` en Task 7.

- [ ] **Step 1: Écrire les deux records**

`BulkContactsRequest.cs` — **propriétés, pas constructeur primaire** : MVC refuse de lier un record portant des métadonnées de validation sur une propriété générée par le constructeur (c'est la raison écrite dans `OpenDraftRequest`).

```csharp
namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>The batch a bulk contact write names. Empty and over-cap are both refused.</summary>
public sealed record BulkContactsRequest
{
    public IReadOnlyList<Guid> Ids { get; init; } = [];
}
```

`BulkFavoriteRequest.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>The batch, plus the flag it is being given.</summary>
public sealed record BulkFavoriteRequest
{
    public IReadOnlyList<Guid> Ids { get; init; } = [];

    public bool IsFavorite { get; init; }
}
```

- [ ] **Step 2: Écrire les tests qui échouent**

`ContactsControllerBulkTests.cs`, sur le patron des tests contrôleur existants (Moq + `CreateAuthenticatedContext`) :

```csharp
[Fact]
public async Task DeleteMany_AnswersNoContentAndPassesTheBatchThrough()
{
    var store = new Mock<IContactStore>();
    store.Setup(s => s.DeleteManyAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(2);
    var controller = Controller(store);
    var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };

    var result = await controller.DeleteMany(new BulkContactsRequest { Ids = ids }, default);

    Assert.IsType<NoContentResult>(result);
    store.Verify(s => s.DeleteManyAsync(It.IsAny<Guid>(), It.Is<IReadOnlyList<Guid>>(v => v.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
}

// Rien à supprimer n'est pas un succès muet : le client a envoyé une requête qui ne veut rien dire.
[Fact]
public async Task DeleteMany_RefusesAnEmptyBatch()
{
    var controller = Controller(new Mock<IContactStore>());

    var result = await controller.DeleteMany(new BulkContactsRequest { Ids = [] }, default);

    Assert.IsType<BadRequestObjectResult>(result);
}

[Fact]
public async Task DeleteMany_RefusesOverTheCap()
{
    var controller = Controller(new Mock<IContactStore>());
    var ids = Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToArray();

    var result = await controller.DeleteMany(new BulkContactsRequest { Ids = ids }, default);

    Assert.IsType<BadRequestObjectResult>(result);
}

// Le no-op silencieux est la règle du lot : zéro ligne touchée reste un 204.
[Fact]
public async Task DeleteMany_AnswersNoContentWhenNothingMatched()
{
    var store = new Mock<IContactStore>();
    store.Setup(s => s.DeleteManyAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(0);
    var controller = Controller(store);

    var result = await controller.DeleteMany(new BulkContactsRequest { Ids = [Guid.NewGuid()] }, default);

    Assert.IsType<NoContentResult>(result);
}

[Fact]
public async Task SetFavoriteMany_PassesTheFlagThrough()
{
    var store = new Mock<IContactStore>();
    store.Setup(s => s.SetFavoriteManyAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), true, It.IsAny<CancellationToken>()))
        .ReturnsAsync(1);
    var controller = Controller(store);

    var result = await controller.SetFavoriteMany(
        new BulkFavoriteRequest { Ids = [Guid.NewGuid()], IsFavorite = true }, default);

    Assert.IsType<NoContentResult>(result);
    store.Verify(s => s.SetFavoriteManyAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(), true, It.IsAny<CancellationToken>()), Times.Once);
}

[Fact]
public async Task SetFavoriteMany_RefusesAnEmptyBatch()
{
    var controller = Controller(new Mock<IContactStore>());

    var result = await controller.SetFavoriteMany(new BulkFavoriteRequest { Ids = [] }, default);

    Assert.IsType<BadRequestObjectResult>(result);
}
```

**Le fixture est celui de `ContactsControllerTests`, repris à l'identique** — un second dialecte de
construction du contrôleur dans le même dossier est ce qui finit par diverger :

```csharp
    private static readonly Guid Uid = Guid.NewGuid();
    private readonly Mock<IContactStore> _store = new();

    private ContactsController CreateController()
    {
        var controller = new ContactsController(_store.Object);
        controller.ControllerContext =
            ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", Uid);
        return controller;
    }
```

Les tests ci-dessus s'écrivent donc avec `CreateController()` et `_store`, et non avec un
`Controller(store)` local : remplacer `var controller = Controller(store);` par
`var controller = CreateController();` et régler les attentes sur `_store`.

- [ ] **Step 3: Lancer, vérifier l'échec**

Run: `cd src/snoopy.microservice && dotnet test --filter ContactsControllerBulkTests`
Expected: échec de compilation — `DeleteMany` n'existe pas.

- [ ] **Step 4: Écrire les deux actions**

Dans `ContactsController`, après `Delete(Guid id, …)`. La constante de cap est déclarée une fois en tête de classe :

```csharp
    /// <summary>The most ids one bulk call may name — the batch size PUT /Mail/Messages/Flags takes.</summary>
    private const int MaxBatch = 200;
```

```csharp
    /// <summary>
    /// Deletes a batch. An id this user does not own resolves to nothing and is skipped in silence:
    /// a batch may not half-fail, and a 404 on a foreign id would confirm that it exists.
    /// </summary>
    /// <param name="request">the ids to delete</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Deleted, whether or not every id matched</response>
    /// <response code="400">No id, or more than 200</response>
    /// <response code="401">Not authenticated</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> DeleteMany(
        BulkContactsRequest request, CancellationToken cancellationToken)
    {
        if (Refuse(request?.Ids) is { } refusal) return refusal;

        await store.DeleteManyAsync(AuthenticatedUser.WebmailUid, request!.Ids, cancellationToken);
        return NoContent();
    }

    /// <summary>Sets or clears the favourite flag over a batch, under the same silent-skip rule.</summary>
    /// <param name="request">the ids and the flag they are given</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Applied, whether or not every id matched</response>
    /// <response code="400">No id, or more than 200</response>
    /// <response code="401">Not authenticated</response>
    [HttpPut("Favorite")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> SetFavoriteMany(
        BulkFavoriteRequest request, CancellationToken cancellationToken)
    {
        if (Refuse(request?.Ids) is { } refusal) return refusal;

        await store.SetFavoriteManyAsync(
            AuthenticatedUser.WebmailUid, request!.Ids, request.IsFavorite, cancellationToken);
        return NoContent();
    }

    /// <summary>The one gate both bulk routes pass, so the two cannot drift on what they refuse.</summary>
    private ActionResult? Refuse(IReadOnlyList<Guid>? ids) => ids switch
    {
        null or { Count: 0 } => BadRequestEnveloppe("At least one contact is required"),
        { Count: > MaxBatch } => BadRequestEnveloppe($"No more than {MaxBatch} contacts at a time"),
        _ => null,
    };
```

> **Attention route :** `[HttpDelete]` sans gabarit et `[HttpDelete("{id:guid}")]` cohabitent sans ambiguïté — le second exige un segment. Vérifier au démarrage qu'aucune `AmbiguousMatchException` n'est levée.

- [ ] **Step 5: Lancer les tests ciblés**

Run: `cd src/snoopy.microservice && dotnet test --filter ContactsControllerBulkTests`
Expected: 6 tests verts.

- [ ] **Step 6: Lancer toute la suite backend**

Run: `cd src/snoopy.microservice && dotnet test`
Expected: aucun échec. Révertrer `ApiDocumentation.xml` s'il ne porte que du bruit — les deux nouveaux membres, eux, doivent y rester.

- [ ] **Step 7: Commit**

```bash
git add src/snoopy.microservice
git commit -m "feat: suppression et mise en favori de contacts par lot"
```

---

## Task 3 : `useSelection` devient générique

**Files:**
- Modify: `src/frontend/src/modules/mail/list/useSelection.ts`
- Test: `src/frontend/src/modules/mail/list/useSelection.test.ts`

**Interfaces:**
- Produces: `useSelection<T = number>(resetKey: string)` avec `selected: Set<T>`, `has(key: T)`, `toggle(key: T, index: number)`, `toggleRange(keys: T[], index: number)`, `selectAll(keys: T[])`, `clear()`. Consommé par `MessageList` (inchangé) et par `ContactList` en Task 6.

- [ ] **Step 1: Écrire le test qui échoue**

Ajouter au fichier de test existant :

```ts
// Les contacts sont des GUID : le hook ne peut plus être lié aux uids du mail.
it('holds string keys as readily as numeric ones', () => {
  const { result } = renderHook(() => useSelection<string>('all'))

  act(() => result.current.toggle('a4f1-…', 0))

  expect(result.current.has('a4f1-…')).toBe(true)
  expect(result.current.has('other')).toBe(false)
})

it('range-selects over string keys', () => {
  const keys = ['a', 'b', 'c', 'd']
  const { result } = renderHook(() => useSelection<string>('all'))

  act(() => result.current.toggle('a', 0))
  act(() => result.current.toggleRange(keys, 2))

  expect([...result.current.selected].sort()).toEqual(['a', 'b', 'c'])
})
```

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `cd src/frontend && npx vitest run src/modules/mail/list/useSelection.test.ts`
Expected: échec de typage/exécution — le hook ne prend pas de paramètre de type.

- [ ] **Step 3: Rendre le hook générique**

Une seule signature change ; le corps est inchangé à part les types.

```ts
/**
 * Checkbox selection over the loaded rows, keyed by whatever identifies one: the mail's numeric
 * uids, the contacts' GUIDs. `resetKey` clears it; the hook never stores the row list, so the
 * caller intersects `selected` with what is on screen — a departed row stops counting on its own.
 */
export function useSelection<T = number>(resetKey: string) {
  const [selected, setSelected] = useState<Set<T>>(() => new Set())
  const anchor = useRef<number | null>(null)
  // … corps inchangé, `uid: number` devenant `key: T` et `loadedUids: number[]` devenant `keys: T[]`
```

Renommer les paramètres (`uid` → `key`, `loadedUids` → `keys`) et ajuster le commentaire de `toggleRange`.

- [ ] **Step 4: Lancer les tests du hook et ceux du mail**

Run: `cd src/frontend && npx vitest run src/modules/mail/list && npm run typecheck`
Expected: tout vert, `MessageList` compris — le défaut `T = number` garde l'appelant existant intact.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/list/useSelection.ts src/frontend/src/modules/mail/list/useSelection.test.ts
git commit -m "refactor: useSelection generique sur la cle de ligne"
```

---

## Task 4 : extraire `SelectionBand`

**Files:**
- Create: `src/frontend/src/components/SelectionBand.tsx`
- Create: `src/frontend/src/styles/selection.css`
- Modify: `src/frontend/src/styles/mail.css` *(retirer le bloc `.selection-*`)*
- Modify: `src/frontend/src/main.tsx` *(importer la feuille)*
- Modify: `src/frontend/src/modules/mail/list/SelectionToolbar.tsx`
- Test: `src/frontend/src/modules/mail/list/SelectionToolbar.test.tsx` *(doit rester vert sans modification)*

**Interfaces:**
- Produces:

```ts
export interface SelectionBandProps {
  /** Cochée quand tout l'écran est sélectionné. */
  allSelected: boolean
  indeterminate: boolean
  onToggleAll: () => void
  selectionDisabled?: boolean
  selectAllLabel: string
  /** Combien de lignes sont cochées. Au-dessus de zéro, `count Label` remplace `center`. */
  count: number
  countLabel: string
  /** Ce que la bande porte AU REPOS : un titre, un champ de recherche, ce que l'appelant veut. */
  center: ReactNode
  /** Avant la case : le hamburger du tiroir, ou rien. */
  leading?: ReactNode
  /** Les actions, à droite. */
  children: ReactNode
}
```

- [ ] **Step 1: Écrire le test qui échoue**

Créer `src/frontend/src/components/SelectionBand.test.tsx` :

```tsx
// La règle que le squelette apporte, et la seule : le centre cède au décompte dès qu'une ligne est
// cochée. Écrite ici plutôt que dans chaque appelant, sinon les deux modules la réinventent.
it('shows the caller centre at rest and the count once rows are checked', () => {
  const { rerender } = render(
    <SelectionBand allSelected={false} indeterminate={false} onToggleAll={() => {}}
      selectAllLabel="Tout sélectionner" count={0} countLabel="0 sélectionné"
      center={<span>Boîte de réception</span>}>
      <button>Supprimer</button>
    </SelectionBand>)

  expect(screen.getByText('Boîte de réception')).toBeInTheDocument()

  rerender(
    <SelectionBand allSelected={false} indeterminate onToggleAll={() => {}}
      selectAllLabel="Tout sélectionner" count={3} countLabel="3 sélectionnés"
      center={<span>Boîte de réception</span>}>
      <button>Supprimer</button>
    </SelectionBand>)

  expect(screen.queryByText('Boîte de réception')).not.toBeInTheDocument()
  expect(screen.getByText('3 sélectionnés')).toBeInTheDocument()
})

// indeterminate est une propriété DOM et non un attribut : un JSX qui l'écrit ne la pose pas.
it('sets the master box indeterminate as a DOM property', () => {
  render(
    <SelectionBand allSelected={false} indeterminate onToggleAll={() => {}}
      selectAllLabel="Tout sélectionner" count={2} countLabel="2 sélectionnés" center={null}>
      <button>Supprimer</button>
    </SelectionBand>)

  expect((screen.getByLabelText('Tout sélectionner') as HTMLInputElement).indeterminate).toBe(true)
})
```

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `cd src/frontend && npx vitest run src/components/SelectionBand.test.tsx`
Expected: échec — le module n'existe pas.

- [ ] **Step 3: Écrire le composant**

```tsx
import { type ReactNode, useEffect, useRef } from 'react'

/**
 * The band both list columns wear. It owns the master checkbox, the rule that the centre gives way
 * to the count while a selection stands, and nothing else: the actions are the caller's, and so is
 * whatever sits in the centre at rest — the mail puts its folder name and starred filter there, the
 * contacts their search field and count.
 */
export default function SelectionBand({
  allSelected, indeterminate, onToggleAll, selectionDisabled, selectAllLabel,
  count, countLabel, center, leading, children,
}: SelectionBandProps) {
  const master = useRef<HTMLInputElement>(null)
  // A DOM property, not an attribute: React writes no such attribute, so it has to be set here.
  useEffect(() => { if (master.current) master.current.indeterminate = indeterminate }, [indeterminate])

  return (
    <div className={`selection-toolbar${count > 0 ? ' is-selecting' : ''}`}>
      {leading}
      {/* The finger-sized target on a phone is this label, not the box: a native checkbox paints
          its whole border box, so sizing it to 44px draws a slab twice its neighbours' weight. */}
      <label className="selection-master-hit">
        <input ref={master} type="checkbox" className="selection-master" aria-label={selectAllLabel}
          checked={allSelected} onChange={onToggleAll} disabled={selectionDisabled} />
      </label>
      <span className="selection-heading">
        {count > 0 ? <span className="selection-title">{countLabel}</span> : center}
      </span>
      <div className="selection-actions">{children}</div>
    </div>
  )
}
```

- [ ] **Step 4: Déplacer les styles**

Couper de `mail.css` le bloc allant de `.selection-toolbar` à `.selection-btn.is-danger:hover` inclus (y compris `.selection-star`, `.selection-master*`, `.selection-heading`, `.selection-title`, `.selection-actions`, `.selection-btn` et leurs variantes, ainsi que les copies présentes dans les blocs `@media`), et le coller tel quel dans `src/styles/selection.css`, en tête :

```css
/* The selection band, shared by the message list and the contacts list through
   components/SelectionBand.tsx. It lives here rather than in mail.css because a shared component
   whose stylesheet sits inside one module is how the other module silently changes shape. */
```

Puis dans `main.tsx`, importer la feuille **après `index.css` et avant `mail.css`**, à côté de `modal.css`.

> Ne rien renommer : les classes gardent leur nom, donc `responsive.test.ts` et les tests mail existants continuent de passer. Vérifier qu'aucune règle déplacée ne dépendait d'un ancêtre `.mail-list`.

- [ ] **Step 5: Faire consommer le squelette par `SelectionToolbar`**

Remplacer le JSX racine de `SelectionToolbar` par `<SelectionBand …>` en passant :
- `center` = le fragment `<span className="selection-title">{title}</span>` suivi du bouton étoile existant ;
- `countLabel` = `t('toolbar.selected', { count })` ;
- `children` = les cinq boutons d'action et le `DropdownMenu` inchangés.

Supprimer de `SelectionToolbar` le `useRef`/`useEffect` de l'indeterminate et le markup de la case : ils sont désormais dans le squelette.

- [ ] **Step 6: Lancer les tests du mail et le typecheck**

Run: `cd src/frontend && npx vitest run src/modules/mail && npm run typecheck && npm run lint`
Expected: tout vert **sans avoir touché aux tests** — c'est ce qui prouve que le refactor ne change aucun comportement.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/components/SelectionBand.tsx src/frontend/src/components/SelectionBand.test.tsx src/frontend/src/styles/selection.css src/frontend/src/styles/mail.css src/frontend/src/main.tsx src/frontend/src/modules/mail/list/SelectionToolbar.tsx
git commit -m "refactor: extraire la bande de selection partagee"
```

---

## Task 5 : `dragContacts`

**Files:**
- Create: `src/frontend/src/modules/contacts/dragContacts.ts`
- Test: `src/frontend/src/modules/contacts/dragContacts.test.ts`

**Interfaces:**
- Consumes: `ContactScope` (`'all' | 'favorites'`) depuis `ContactScopes.tsx`.
- Produces: `CONTACT_DRAG_MIME`, `ContactDragPayload { ids: string[] }`, `dragIds(selectedIds, id)`, `serializeContactDrag(payload)`, `parseContactDrag(raw)`, `canDropIntoScope(scope)`.

- [ ] **Step 1: Écrire les tests qui échouent**

```ts
describe('dragIds', () => {
  // La ligne saisie emporte la sélection quand elle en fait partie, elle seule sinon : glisser une
  // ligne non cochée ne doit jamais déranger une sélection faite pour autre chose.
  it('carries the whole selection when the grabbed row belongs to it', () => {
    expect(dragIds(['a', 'b'], 'a')).toEqual(['a', 'b'])
  })

  it('carries the grabbed row alone when it does not', () => {
    expect(dragIds(['a', 'b'], 'c')).toEqual(['c'])
  })
})

describe('parseContactDrag', () => {
  it('reads back what serialize wrote', () => {
    expect(parseContactDrag(serializeContactDrag({ ids: ['a'] }))).toEqual({ ids: ['a'] })
  })

  it.each([
    ['not json', 'oops'],
    ['no ids', JSON.stringify({})],
    ['an empty batch', JSON.stringify({ ids: [] })],
    ['a non-string id', JSON.stringify({ ids: [7] })],
  ])('answers null for %s', (_label, raw) => {
    expect(parseContactDrag(raw)).toBeNull()
  })
})

describe('canDropIntoScope', () => {
  // « Tous les contacts » est la vue complète, pas un groupe : rien à y ajouter.
  it('refuses the all scope and accepts favourites', () => {
    expect(canDropIntoScope('all')).toBe(false)
    expect(canDropIntoScope('favorites')).toBe(true)
  })
})
```

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `cd src/frontend && npx vitest run src/modules/contacts/dragContacts.test.ts`
Expected: échec — le module n'existe pas.

- [ ] **Step 3: Écrire le module**

```ts
import type { ContactScope } from './ContactScopes'

/**
 * A custom MIME so a scope can recognise our payload from its dragover types alone: the browser
 * withholds dataTransfer *values* until drop, but always exposes the list of types. Distinct from
 * the mail's, so dragging messages over the contacts column offers nothing.
 */
export const CONTACT_DRAG_MIME = 'application/x-weesky-contacts'

export interface ContactDragPayload { ids: string[] }

/**
 * The dragged tile carries the whole checked selection when it belongs to it, itself alone
 * otherwise — so dragging an unchecked tile never disturbs a selection made for something else.
 */
export function dragIds(selectedIds: string[], id: string): string[] {
  return selectedIds.includes(id) ? selectedIds : [id]
}

export function serializeContactDrag(payload: ContactDragPayload): string {
  return JSON.stringify(payload)
}

/** Null for anything that is not our shape: a foreign drag, a truncated string, no ids. */
export function parseContactDrag(raw: string): ContactDragPayload | null {
  try {
    const value = JSON.parse(raw)
    if (!Array.isArray(value?.ids) || value.ids.length === 0) return null
    if (!value.ids.every((id: unknown) => typeof id === 'string')) return null
    return { ids: value.ids }
  } catch {
    return null
  }
}

/**
 * A drop target is a scope a contact can belong to. `all` is the complete view rather than a
 * group, so nothing can be added to it — the same refusal `canDropInto` makes for the source
 * folder. Groups, when they land, are targets by construction.
 */
export function canDropIntoScope(scope: ContactScope): boolean {
  return scope !== 'all'
}
```

- [ ] **Step 4: Lancer, vérifier que ça passe**

Run: `cd src/frontend && npx vitest run src/modules/contacts/dragContacts.test.ts`
Expected: 8 tests verts.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/contacts/dragContacts.ts src/frontend/src/modules/contacts/dragContacts.test.ts
git commit -m "feat: charge utile du glisser-deposer de contacts"
```

---

## Task 6 : cases de sélection dans la liste

**Files:**
- Modify: `src/frontend/src/modules/contacts/ContactList.tsx`
- Modify: `src/frontend/src/index.css`
- Modify: `src/frontend/src/locales/fr/contacts.json`, `src/frontend/src/locales/en/contacts.json`
- Test: `src/frontend/src/modules/contacts/ContactList.test.tsx`

**Interfaces:**
- Consumes: `useSelection<string>` (Task 3), `SelectionBand` (Task 4).
- Produces: `ContactList` accepte `onDeleteMany(ids: string[]): void`, et expose la sélection courante à son parent via `onSelectionChange(ids: string[]): void` (dont Task 9 se sert pour le drag).

- [ ] **Step 1: Ajouter les clés i18n**

`fr/contacts.json` — sous `list` :

```json
"selectAll": "Tout sélectionner",
"selectOne": "Sélectionner {{name}}",
"selected_one": "{{count}} sélectionné",
"selected_other": "{{count}} sélectionnés",
"deleteSelected": "Supprimer la sélection",
"deleteSelectedConfirm_one": "Supprimer ce contact ? Cette action est définitive.",
"deleteSelectedConfirm_other": "Supprimer ces {{count}} contacts ? Cette action est définitive."
```

`en/contacts.json` — mêmes clés :

```json
"selectAll": "Select all",
"selectOne": "Select {{name}}",
"selected_one": "{{count}} selected",
"selected_other": "{{count}} selected",
"deleteSelected": "Delete selection",
"deleteSelectedConfirm_one": "Delete this contact? This cannot be undone.",
"deleteSelectedConfirm_other": "Delete these {{count}} contacts? This cannot be undone."
```

> Le français prend l'espace insécable avant `?` — le caractère U+00A0, pas un espace ordinaire, sinon `parity.test.ts` rougit.

- [ ] **Step 2: Écrire les tests qui échouent**

```tsx
it('checks a contact and counts it in the band', async () => {
  setup()

  await userEvent.click(screen.getByLabelText('Select Alice Martin'))

  expect(screen.getByText('1 selected')).toBeInTheDocument()
})

// La case maîtresse porte sur ce qui est à l'écran, donc sur les lignes filtrées.
it('selects every filtered row from the master box', async () => {
  setup()
  await userEvent.type(screen.getByRole('searchbox'), 'alice')
  await userEvent.click(screen.getByLabelText('Select all'))

  expect(screen.getByText('1 selected')).toBeInTheDocument()
})

// Choix assumé : resetKey inclut la requête, donc taper vide la sélection.
it('clears the selection when the query changes', async () => {
  setup()
  await userEvent.click(screen.getByLabelText('Select Alice Martin'))
  await userEvent.type(screen.getByRole('searchbox'), 'a')

  expect(screen.queryByText(/selected/)).not.toBeInTheDocument()
})

it('asks for confirmation before deleting a selection', async () => {
  const onDeleteMany = vi.fn()
  setup({ onDeleteMany })
  await userEvent.click(screen.getByLabelText('Select Alice Martin'))
  await userEvent.click(screen.getByLabelText('Delete selection'))

  expect(onDeleteMany).not.toHaveBeenCalled()
  await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
  expect(onDeleteMany).toHaveBeenCalledWith(['a'])
})

it('leaves the delete action disabled while nothing is checked', () => {
  setup()
  expect(screen.getByLabelText('Delete selection')).toBeDisabled()
})
```

- [ ] **Step 3: Lancer, vérifier l'échec**

Run: `cd src/frontend && npx vitest run src/modules/contacts/ContactList.test.tsx`
Expected: échec — aucune case, aucune bande.

- [ ] **Step 4: Câbler la sélection dans `ContactList`**

Remplacer `.contacts-list-heading` par la bande, en gardant le champ et le compteur **au centre** :

```tsx
const selection = useSelection<string>(`${scopeKey}::${query}`)
const shownIds = shown.map(contact => contact.id)
const selectedIds = shownIds.filter(id => selection.has(id))
const count = selectedIds.length

<SelectionBand
  allSelected={count > 0 && count === shown.length}
  indeterminate={count > 0 && count < shown.length}
  onToggleAll={() => (count === shown.length ? selection.clear() : selection.selectAll(shownIds))}
  selectAllLabel={t('list.selectAll')}
  count={count}
  countLabel={t('list.selected', { count })}
  leading={leading}
  center={<>
    <span className="contacts-search">…champ inchangé…</span>
    <span className="contacts-count" data-testid="contact-count">…inchangé…</span>
  </>}
>
  <button type="button" className="selection-btn is-danger" aria-label={t('list.deleteSelected')}
    title={t('list.deleteSelected')} disabled={count === 0} onClick={() => setConfirming(true)}>
    <TrashIcon size={20} />
  </button>
</SelectionBand>
```

`scopeKey` est une nouvelle prop `scope: string` fournie par `ContactsLayout` (le scope courant), pour que changer de scope vide aussi la sélection.

Ajouter la case dans chaque tuile, **premier enfant** :

```tsx
<input type="checkbox" className="contact-tile-check"
  aria-label={t('list.selectOne', { name })}
  checked={selection.has(contact.id)}
  onClick={event => event.stopPropagation()}
  onChange={() => selection.toggle(contact.id, index)} />
```

et `has-selection` sur le conteneur : `<div className={`contact-tiles${count > 0 ? ' has-selection' : ''}`}>`.

La confirmation réutilise `DeleteConfirmModal` avec `message={t('list.deleteSelectedConfirm', { count })}` et `onConfirm={() => { onDeleteMany(selectedIds); selection.clear(); setConfirming(false) }}`.

- [ ] **Step 5: Ajouter le CSS**

Dans `index.css`, à côté des règles `.contact-tile` :

```css
/* The gutter is reserved permanently, exactly as `.message-row:not(.is-line)` reserves 34px:
   revealing the box on hover must never shove the name sideways. */
.contact-tile { padding-left: 34px; }

/* `.message-row-check`, to the declaration: hidden and inert at rest, centred over the gutter so
   it stays centred whatever the tile's height, pinned once a selection stands. */
.contact-tile-check {
  position: absolute; left: 12px; top: 50%; transform: translateY(-50%);
  width: 16px; height: 16px; margin: 0;
  accent-color: var(--action-primary); cursor: pointer;
  opacity: 0; pointer-events: none;
}
.contact-tile:hover .contact-tile-check,
.contact-tile:focus-within .contact-tile-check,
.contact-tiles.has-selection .contact-tile-check,
.contact-tile-check:checked { opacity: 1; pointer-events: auto; }

/* Dimmed while it rides the cursor: the pill is the thing being moved, the tile is its origin. */
.contact-tile.is-dragging { opacity: 0.45; }
```

- [ ] **Step 6: Lancer les tests contacts, le typecheck et le lint**

Run: `cd src/frontend && npx vitest run src/modules/contacts && npm run typecheck && npm run lint`
Expected: vert. Le test d'anatomie existant doit être mis à jour : la case devient le premier enfant de la tuile, avant `.contact-tile-line`.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/modules/contacts src/frontend/src/index.css src/frontend/src/locales
git commit -m "feat: selection multiple dans la liste de contacts"
```

---

## Task 7 : les deux appels et leurs mutations

**Files:**
- Modify: `src/frontend/src/api.js`
- Modify: `src/frontend/src/modules/contacts/queries.ts`
- Test: `src/frontend/src/api.test.js`

**Interfaces:**
- Consumes: les routes de la Task 2.
- Produces: `api.deleteContacts(ids)`, `api.setContactsFavorite(ids, isFavorite)`, `useDeleteContacts()`, `useSetContactsFavorite()`.

- [ ] **Step 1: Écrire les tests qui échouent**

```js
describe('deleteContacts', () => {
  it('sends the batch in the body of a DELETE', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.deleteContacts(['a', 'b'])

    const [url, options] = globalThis.fetch.mock.calls[0]
    expect(url).toContain('/api/Contacts')
    expect(options.method).toBe('DELETE')
    expect(JSON.parse(options.body)).toEqual({ ids: ['a', 'b'] })
  })
})

describe('setContactsFavorite', () => {
  it('sends the batch and the flag', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.setContactsFavorite(['a'], true)

    const [url, options] = globalThis.fetch.mock.calls[0]
    expect(url).toContain('/api/Contacts/Favorite')
    expect(options.method).toBe('PUT')
    expect(JSON.parse(options.body)).toEqual({ ids: ['a'], isFavorite: true })
  })
})
```

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `cd src/frontend && npx vitest run src/api.test.js`
Expected: échec — `api.deleteContacts is not a function`.

- [ ] **Step 3: Ajouter les deux appels**

Dans `api.js`, sous `setContactFavorite` :

```js
  // Le lot passe par le corps, pas par l'URL : une liste d'ids en query string casse au-delà de
  // quelques dizaines et n'a pas de forme convenue.
  deleteContacts: (ids) =>
    request('DELETE', '/api/Contacts', { ids }),

  setContactsFavorite: (ids, isFavorite) =>
    request('PUT', '/api/Contacts/Favorite', { ids, isFavorite }),
```

> `request()` sérialise le corps sans regarder la méthode (`body: body ? … : undefined`) : un `DELETE` porteur d'un corps passe sans rien changer au helper. Vérifié.

- [ ] **Step 4: Ajouter les mutations**

Dans `contacts/queries.ts` :

```ts
export function useDeleteContacts() {
  return useContactMutation((ids: string[]) => api.deleteContacts(ids))
}

export function useSetContactsFavorite() {
  return useContactMutation(
    ({ ids, isFavorite }: { ids: string[]; isFavorite: boolean }) =>
      api.setContactsFavorite(ids, isFavorite))
}
```

`useContactMutation` invalide déjà `onSettled` : rien à ajouter.

- [ ] **Step 5: Lancer les tests et le typecheck**

Run: `cd src/frontend && npx vitest run src/api.test.js && npm run typecheck`
Expected: vert.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/api.js src/frontend/src/api.test.js src/frontend/src/modules/contacts/queries.ts
git commit -m "feat: appels et mutations de lot pour les contacts"
```

---

## Task 8 : brancher la suppression en masse

**Files:**
- Modify: `src/frontend/src/modules/contacts/ContactsLayout.tsx`
- Test: `src/frontend/src/modules/contacts/ContactsLayout.test.tsx`

**Interfaces:**
- Consumes: `useDeleteContacts` (Task 7), `ContactList.onDeleteMany` (Task 6).

- [ ] **Step 1: Écrire le test qui échoue**

```tsx
it('deletes the selection and toasts on refusal', async () => {
  mocks.deleteContacts.mockRejectedValueOnce(new Error('refused'))
  renderLayout()

  await userEvent.click(await screen.findByLabelText('Select Alice Martin'))
  await userEvent.click(screen.getByLabelText('Delete selection'))
  await userEvent.click(screen.getByRole('button', { name: 'Delete' }))

  expect(mocks.deleteContacts).toHaveBeenCalledWith(['a'])
  expect(await screen.findByText(/could not be deleted/i)).toBeInTheDocument()
})
```

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `cd src/frontend && npx vitest run src/modules/contacts/ContactsLayout.test.tsx`
Expected: échec.

- [ ] **Step 3: Câbler**

Dans `ContactsLayout`, à côté des mutations existantes :

```tsx
const deleteMany = useDeleteContacts()

function deleteSelection(ids: string[]) {
  deleteMany.mutate(ids, {
    onError: error => addToast(apiErrorMessage(error, t('layout.deleteManyFailed')), 'error'),
  })
}
```

et passer `onDeleteMany={deleteSelection}` plus `scope={scope}` à `ContactList`.

Ajouter la clé `layout.deleteManyFailed` aux deux catalogues :
- fr : `"deleteManyFailed": "Les contacts n’ont pas pu être supprimés"`
- en : `"deleteManyFailed": "The contacts could not be deleted"`

- [ ] **Step 4: Lancer les tests contacts**

Run: `cd src/frontend && npx vitest run src/modules/contacts && npm run typecheck && npm run lint`
Expected: vert.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/contacts src/frontend/src/locales
git commit -m "feat: supprimer une selection de contacts"
```

---

## Task 9 : le glisser-déposer sur un scope

**Files:**
- Modify: `src/frontend/src/modules/contacts/ContactList.tsx` *(départ du drag)*
- Modify: `src/frontend/src/modules/contacts/ContactScopes.tsx` *(cible)*
- Modify: `src/frontend/src/modules/contacts/ContactsLayout.tsx` *(le handler)*
- Modify: `src/frontend/src/index.css`
- Modify: `src/frontend/src/locales/{fr,en}/contacts.json`
- Test: `src/frontend/src/modules/contacts/ContactScopes.test.tsx`, `ContactList.test.tsx`

**Interfaces:**
- Consumes: `dragContacts` (Task 5), `useSetContactsFavorite` (Task 7).
- Produces: `ContactScopes` accepte `onDropContacts?: (scope: ContactScope, payload: ContactDragPayload) => void`.

- [ ] **Step 1: Écrire les tests qui échouent**

```tsx
// « Tous les contacts » n'est pas un groupe : il ne s'allume jamais et n'appelle rien.
it('never lights up the all scope', () => {
  const onDropContacts = vi.fn()
  render(<ContactScopes scope="all" total={2} favorites={0} onScope={() => {}} onDropContacts={onDropContacts} />)

  fireEvent.dragOver(screen.getByText('All contacts').closest('button')!, { dataTransfer: dt() })
  fireEvent.drop(screen.getByText('All contacts').closest('button')!, { dataTransfer: dt() })

  expect(onDropContacts).not.toHaveBeenCalled()
})

it('lights up favourites and hands the payload over on drop', () => {
  const onDropContacts = vi.fn()
  render(<ContactScopes scope="all" total={2} favorites={0} onScope={() => {}} onDropContacts={onDropContacts} />)
  const target = screen.getByText('Favourites').closest('button')!

  fireEvent.dragOver(target, { dataTransfer: dt() })
  expect(target).toHaveClass('drop-ready')

  fireEvent.drop(target, { dataTransfer: dt() })
  expect(onDropContacts).toHaveBeenCalledWith('favorites', { ids: ['a'] })
})

// dt() imite le dataTransfer : types visibles au survol, valeur lisible au drop seulement.
function dt() {
  return {
    types: [CONTACT_DRAG_MIME],
    getData: () => JSON.stringify({ ids: ['a'] }),
    dropEffect: '',
  }
}
```

Et côté liste :

```tsx
it('drags the whole selection when the grabbed tile belongs to it', () => {
  setup()
  fireEvent.click(screen.getByLabelText('Select Alice Martin'))
  const setData = vi.fn()
  fireEvent.dragStart(screen.getByTestId('contact-tile-a'), { dataTransfer: { setData, setDragImage: vi.fn() } })

  expect(JSON.parse(setData.mock.calls[0][1])).toEqual({ ids: ['a'] })
})
```

- [ ] **Step 2: Lancer, vérifier l'échec**

Run: `cd src/frontend && npx vitest run src/modules/contacts`
Expected: échec.

- [ ] **Step 3: Faire du scope une cible**

Dans `ContactScopes`, un état par bouton via un sous-composant `ScopeRow` :

```tsx
const [dropReady, setDropReady] = useState(false)
const droppable = Boolean(onDropContacts) && canDropIntoScope(scope)

function onDragOver(event: DragEvent<HTMLButtonElement>) {
  if (!droppable || !event.dataTransfer.types.includes(CONTACT_DRAG_MIME)) return
  event.preventDefault()  // The default is "no drop"; preventing it opens the scope up.
  event.dataTransfer.dropEffect = 'copy'
  setDropReady(true)
}

function onDrop(event: DragEvent<HTMLButtonElement>) {
  setDropReady(false)
  if (!droppable) return
  const payload = parseContactDrag(event.dataTransfer.getData(CONTACT_DRAG_MIME))
  if (payload) onDropContacts!(scope, payload)
}
```

La classe `drop-ready` s'ajoute au `className` du bouton, et `onDragLeave` la retire.

- [ ] **Step 4: Faire partir le drag depuis la tuile**

Dans `ContactList`, sur la tuile : `draggable`, `onDragStart` posant `CONTACT_DRAG_MIME` avec `serializeContactDrag({ ids: dragIds(selectedIds, contact.id) })`, `effectAllowed = 'copy'`, la pilule `.drag-pill` construite comme dans `MessageList` (élément hors écran le temps du snapshot), et `is-dragging` sur les tuiles emportées.

- [ ] **Step 5: Câbler le handler**

Dans `ContactsLayout` :

```tsx
const setManyFavorite = useSetContactsFavorite()

function dropOnScope(target: ContactScope, payload: ContactDragPayload) {
  // Le dépôt ajoute le favori, il ne le retire jamais : un geste qui ajouterait ou retirerait
  // selon l'état de chaque ligne donnerait un résultat différent par contact.
  if (target !== 'favorites') return
  setManyFavorite.mutate({ ids: payload.ids, isFavorite: true }, {
    onError: error => addToast(apiErrorMessage(error, t('layout.favouriteFailed')), 'error'),
  })
}
```

- [ ] **Step 6: Ajouter le CSS de la cible**

```css
/* `.folder-line.drop-ready .folder-row`, transcribed onto the scope row. Louder than `is-active`
   on purpose: the scope you are already in must read as excluded, not as target. */
.contact-scope.drop-ready {
  background: color-mix(in oklab, var(--badge-count-bg) 16%, transparent);
  color: var(--text);
  font-weight: 600;
  box-shadow: inset 0 0 0 2px var(--badge-count-bg);
}
.contact-scope.drop-ready::after {
  content: var(--drop-label, '');
  margin-left: auto; flex: none;
  font-size: 10.5px; font-weight: 700; letter-spacing: 0.04em;
  color: var(--badge-count-bg);
}
```

Le composant pose `style={{ '--drop-label': `"${t('scopes.dropHere')}"` }}` sur le bouton. Clés : fr `"dropHere": "Déposer ici"`, en `"dropHere": "Drop here"`.

- [ ] **Step 7: Corriger la même chaîne côté mail**

`mail.css` écrit `content: 'Drop here'` en dur : l'arbre des dossiers affiche cet anglais en français aujourd'hui. Remplacer par `content: var(--drop-label, '')` et poser la propriété depuis `FolderTree` avec `t('folders.dropHere')`, en ajoutant la clé aux deux catalogues `mail`.

- [ ] **Step 8: Lancer toute la suite frontend**

Run: `cd src/frontend && npx vitest run && npm run typecheck && npm run lint`
Expected: vert, y compris les tests `FolderTree` existants.

- [ ] **Step 9: Commit**

```bash
git add src/frontend/src
git commit -m "feat: mettre en favori une selection par glisser-deposer"
```

---

## Task 10 : probe, documentation, ménage

**Files:**
- Modify: `src/frontend/probes/mobile-layout.html`
- Modify: `src/frontend/website-design.md`
- Modify: `src/frontend/docs/architecture-contacts.md`
- Delete: `src/frontend/probes/contacts-bulk-mockup.html`

- [ ] **Step 1: Mettre le probe au markup réel**

Dans le cas `contacts-list`, ajouter la case en premier enfant de chaque tuile et l'ajouter au sélecteur `touch`. Un fixture qui n'est pas le markup du composant ne garde rien.

- [ ] **Step 2: Documenter dans `website-design.md`**

Sous la règle de la liste-colonne, ajouter que **la bande d'une liste sélectionnable est `SelectionBand`**, que sa zone centrale appartient à l'appelant au repos et cède au décompte pendant une sélection, et que la cible de glisser-déposer porte `drop-ready` avec son libellé passé par `--drop-label`.

- [ ] **Step 3: Documenter dans `architecture-contacts.md`**

Décrire la sélection (clé de reset = scope + requête, donc une frappe la vide), la suppression en masse et sa confirmation, le dépôt qui ajoute le favori sans jamais le retirer, et le refus du scope `all`.

- [ ] **Step 4: Supprimer la maquette**

```bash
git rm src/frontend/probes/contacts-bulk-mockup.html
```

Elle a servi à valider la conception ; la garder ferait un second markup contacts à maintenir.

- [ ] **Step 5: Vérification finale**

Run: `cd src/frontend && npx vitest run && npm run typecheck && npm run lint`
Run: `cd src/snoopy.microservice && dotnet test`
Expected: tout vert.

Puis, en navigateur — jsdom ne calcule aucune mise en page, donc rien de ce qui suit n'est couvert par un test : ouvrir `/contacts`, vérifier que les cases apparaissent au survol et restent affichées pendant une sélection, que la bande bascule et revient, qu'un glissé sur « Favoris » allume la cible et que « Tous les contacts » ne s'allume jamais, dans les deux thèmes.

- [ ] **Step 6: Commit**

```bash
git add -A src/frontend
git commit -m "docs: consigner la selection multiple des contacts"
```

---

## Self-review

**Couverture du spec** — les deux endpoints (T1–T2), le cap et le no-op silencieux (T1–T2), le squelette partagé et le déplacement des styles (T4), `useSelection` générique (T3), `dragContacts` (T5), les cases et la gouttière (T6), le champ de recherche conservé au centre (T6), la sélection vidée à la frappe (T6), la confirmation (T6, T8), le dépôt qui n'ajoute que (T9), le refus de `all` (T5, T9), `--drop-label` et la correction du mail (T9), le probe et la suppression de la maquette (T10). Aucune section du spec sans tâche.

**Cohérence des noms** — `DeleteManyAsync`/`SetFavoriteManyAsync` (T1) sont appelées sous ces noms en T2 ; `api.deleteContacts`/`api.setContactsFavorite` (T7) sont consommées sous ces noms en T8 et T9 ; `CONTACT_DRAG_MIME`, `dragIds`, `parseContactDrag`, `canDropIntoScope` (T5) reparaissent à l'identique en T9 ; `SelectionBand` (T4) est consommée en T6 avec les props qu'elle déclare.

**Les deux incertitudes de la première rédaction ont été levées sur pièce** : `request()` sérialise
le corps quelle que soit la méthode, donc le `DELETE` porteur d'un corps ne demande aucun ajustement
du helper ; et le fixture des tests contacts est `CreateController()` + `_store`, recopié dans la
Task 2 plutôt que réinventé.

**Ce que le plan ne couvre pas, et qui est structurel** : jsdom ne calcule aucune mise en page, donc
la gouttière, l'anneau de la cible et la pilule ne sont vérifiables par aucun test de ce dépôt. La
Task 10 les fait vérifier en navigateur, dans les deux thèmes. Toute affirmation « c'est aligné »
sans cette passe serait invérifiée.

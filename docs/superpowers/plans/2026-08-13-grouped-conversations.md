# Regroupement des conversations — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** Un réglage de compte `mail.groupConversations` (défaut off) qui regroupe la liste de messages en fils dépliables, par dossier, calculés côté serveur via IMAP `THREAD=REFERENCES`.

**Architecture :** Le backend gagne un paramètre `grouped` sur `GET /api/Mail/Messages` : quand la session annonce `SORT` + `THREAD=REFERENCES`, il répond `Threads`/`TotalThreads` en plus de la forme actuelle (une page = N fils, un seul FETCH pour tous les membres) ; sinon il répond à plat, sans erreur. Le frontend détecte le mode par la présence de `threads`, rend une ligne par fil (dernier message + badge + chevron, état agrégé), et transpose le stream « All » et le merge du poll par clé de fil (UID du membre le plus ancien).

**Tech stack :** ASP.NET Core (.NET 10) + MailKit côté backend, xUnit/Moq ; React + TanStack Query + Vitest côté frontend.

**Spec :** `docs/superpowers/specs/2026-08-13-grouped-conversations-design.md`

## Global Constraints

- Réponses d'API : `System.Text.Json` omet les champs `null` (`WhenWritingNull`) — côté client un champ absent est `undefined`, jamais `null` ; les nouveaux champs sont optionnels (`threads?`, `totalThreads?`).
- Le repli sans capability est **silencieux** : `grouped=true` sur un serveur sans `THREAD` répond la forme plate actuelle, jamais une erreur (pattern du repli `SORT`, règle 2 du CLAUDE.md backend).
- La recherche (`POST /api/Mail/Messages/Search`) et le filtre étoilé restent **à plat** — aucun changement sur ce chemin.
- Un fil de 1 message se rend **exactement** comme aujourd'hui (pas de badge, pas de chevron).
- Clé stable d'un fil = UID de son membre le plus ancien (le dernier de la liste newest-first).
- Jamais d'`invalidateQueries` sur la clé du stream — les règles de `useListRefresh` restent en l'état.
- i18n : toute chaîne UI passe par les catalogues en/fr ; en français apostrophe U+2019 et espace insécable U+00A0 devant `; : ? !` et dans les guillemets (tests de parité/typographie existants). L'outil Edit écrit une espace ordinaire là où il faut U+00A0 : écrire les chaînes françaises en une passe avec Write, ou corriger en PowerShell.
- `dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés), lancé depuis `src/`. Après un `dotnet test`, vérifier `git status` : si `ApiDocumentation.xml` a dérivé (~855 lignes sans rapport), le revert avant de committer.
- Messages de commit : concis (2 lignes max), jamais commencer/finir par `@`, terminés par `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`. Utiliser un heredoc `git commit -F -` dans l'outil Bash.
- Frontend : pas de commentaire quand le code suffit ; 3 lignes max quand il en faut un.

---

### Task 1 : Registre backend — `mail.groupConversations`

**Files:**
- Modify: `src/snoopy.microservice/Models/UserPreferences.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Models/UserPreferencesTests.cs` (le fichier existe déjà ; sinon le créer à côté d'`AppSettingsTests.cs`)

**Interfaces:**
- Produces: constante `UserPreferences.MailGroupConversations = "mail.groupConversations"`, déclarée dans `All` avec défaut `"false"` et valeurs `["true","false"]`. Consommée par le frontend (Task 5) via `GET /api/Preferences`, qui répond déjà toutes les clés du registre — aucun autre changement backend.

- [ ] **Step 1 : Écrire le test qui échoue**

Dans la classe de tests du registre (chercher `class UserPreferencesTests` ; si elle n'existe pas, la créer sur le modèle des tests de modèles existants) :

```csharp
[Fact]
public void GroupConversations_defaults_to_false_and_accepts_booleans_only()
{
    var effective = UserPreferences.Effective([]);

    Assert.Equal("false", effective["mail.groupConversations"]);
    Assert.True(UserPreferences.IsValid("mail.groupConversations", "true"));
    Assert.True(UserPreferences.IsValid("mail.groupConversations", "false"));
    Assert.False(UserPreferences.IsValid("mail.groupConversations", "yes"));
}
```

- [ ] **Step 2 : Vérifier l'échec**

Run : `cd src && dotnet test --filter "FullyQualifiedName~UserPreferences"`
Attendu : FAIL — la clé `mail.groupConversations` est absente d'`Effective`.

- [ ] **Step 3 : Implémenter**

Dans `UserPreferences.cs`, ajouter la constante après `MailShowFolderIcons` :

```csharp
public const string MailGroupConversations = "mail.groupConversations";
```

et la déclaration dans `All`, après l'entrée `MailShowFolderIcons` :

```csharp
// Off by default: the list has always been flat, so an account that never opens the
// setting sees exactly what it saw yesterday.
new(MailGroupConversations, "false", Booleans),
```

- [ ] **Step 4 : Vérifier le vert**

Run : `cd src && dotnet test --filter "FullyQualifiedName~UserPreferences"`
Attendu : PASS.

- [ ] **Step 5 : Commit**

```
feat: register the mail.groupConversations preference
```

---

### Task 2 : `MailThreading` — l'arithmétique pure des fils

**Files:**
- Create: `src/snoopy.microservice/Services/MailThreading.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailThreadingTests.cs`

**Interfaces:**
- Consumes: `MailKit.MessageThread` (arbre du `THREAD` — `UniqueId?` + `Children`), `MailKit.UniqueId`.
- Produces: `internal static IReadOnlyList<IReadOnlyList<UniqueId>> MailThreading.Arrange(IList<MessageThread> tree, IList<UniqueId> newestFirst)` — chaque fil = ses UIDs du plus récent au plus ancien ; les fils triés par leur membre le plus récent, plus récent d'abord. Consommé par Task 3. La pagination réutilise `MailPaging.PageOf` (existant).

- [ ] **Step 1 : Écrire les tests qui échouent**

`MailThreadingTests.cs` :

```csharp
using MailKit;
using weesky.Snoopy.Microservice.Services;

namespace snoopy.microservice.Tests.Services;

public class MailThreadingTests
{
    private static MessageThread Node(uint uid, params MessageThread[] children)
    {
        var node = new MessageThread(new UniqueId(uid));
        foreach (var child in children) node.Children.Add(child);
        return node;
    }

    private static UniqueId U(uint uid) => new(uid);

    [Fact]
    public void Orders_threads_by_their_newest_member()
    {
        // Sorted newest-first: 30, 20, 10. Thread A = {10, 30}, thread B = {20}.
        // A's newest member (30) outranks B's (20), so A comes first despite its old root.
        var tree = new List<MessageThread> { Node(10, Node(30)), Node(20) };
        var sorted = new List<UniqueId> { U(30), U(20), U(10) };

        var threads = MailThreading.Arrange(tree, sorted);

        Assert.Equal(2, threads.Count);
        Assert.Equal([U(30), U(10)], threads[0]);
        Assert.Equal([U(20)], threads[1]);
    }

    [Fact]
    public void Members_come_newest_first_whatever_the_tree_order()
    {
        var tree = new List<MessageThread> { Node(1, Node(3, Node(2))) };
        var sorted = new List<UniqueId> { U(3), U(2), U(1) };

        var threads = MailThreading.Arrange(tree, sorted);

        Assert.Equal([U(3), U(2), U(1)], Assert.Single(threads));
    }

    [Fact]
    public void A_phantom_root_contributes_no_uid()
    {
        // THREAD may answer a parent the mailbox no longer holds: UniqueId is null there.
        var phantom = new MessageThread(null);
        phantom.Children.Add(Node(5));
        var sorted = new List<UniqueId> { U(5) };

        var threads = MailThreading.Arrange([phantom], sorted);

        Assert.Equal([U(5)], Assert.Single(threads));
    }

    [Fact]
    public void A_uid_the_sort_does_not_know_is_dropped()
    {
        // THREAD and SORT are two commands; a message expunged between them is in one only.
        var tree = new List<MessageThread> { Node(7, Node(99)) };
        var sorted = new List<UniqueId> { U(7) };

        var threads = MailThreading.Arrange(tree, sorted);

        Assert.Equal([U(7)], Assert.Single(threads));
    }

    [Fact]
    public void A_thread_with_no_known_member_disappears()
    {
        var tree = new List<MessageThread> { Node(99), Node(4) };
        var sorted = new List<UniqueId> { U(4) };

        var threads = MailThreading.Arrange(tree, sorted);

        Assert.Equal([U(4)], Assert.Single(threads));
    }

    [Fact]
    public void Empty_inputs_answer_empty()
    {
        Assert.Empty(MailThreading.Arrange([], []));
    }
}
```

- [ ] **Step 2 : Vérifier l'échec**

Run : `cd src && dotnet test --filter "FullyQualifiedName~MailThreading"`
Attendu : échec de compilation — `MailThreading` n'existe pas.

- [ ] **Step 3 : Implémenter**

`src/snoopy.microservice/Services/MailThreading.cs` :

```csharp
using MailKit;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The pure arithmetic behind conversation grouping: flattens the THREAD tree into per-thread
/// UID sets and orders both members and threads off the SORT result. No IMAP call anywhere —
/// the MailPaging pattern, so every rule is unit-testable apart from a server.
/// </summary>
internal static class MailThreading
{
    /// <summary>
    /// Each thread's UIDs newest-first, threads ordered by their newest member (newest first).
    /// A uid the sort does not know is dropped — THREAD and SORT are two commands, and a
    /// message expunged between them must not surface a row the fetch cannot fill.
    /// </summary>
    internal static IReadOnlyList<IReadOnlyList<UniqueId>> Arrange(
        IList<MessageThread> tree, IList<UniqueId> newestFirst)
    {
        var rank = new Dictionary<UniqueId, int>(newestFirst.Count);
        for (var index = 0; index < newestFirst.Count; index++) rank[newestFirst[index]] = index;

        var threads = new List<List<UniqueId>>();
        foreach (var root in tree)
        {
            var members = new List<UniqueId>();
            Collect(root, members);

            var known = members.Where(rank.ContainsKey).OrderBy(uid => rank[uid]).ToList();
            if (known.Count > 0) threads.Add(known);
        }

        // A thread sits where its newest member sits.
        return threads.OrderBy(thread => rank[thread[0]]).ToList();
    }

    private static void Collect(MessageThread node, List<UniqueId> members)
    {
        if (node.UniqueId is { } uid) members.Add(uid);
        foreach (var child in node.Children) Collect(child, members);
    }
}
```

Note : si `MessageThread.Children` s'avère non mutable dans la version de MailKit du projet, utiliser son constructeur `MessageThread(UniqueId?)` puis la surcharge/propriété que l'IDE propose — adapter le helper `Node` des tests, jamais la signature d'`Arrange`.

- [ ] **Step 4 : Vérifier le vert**

Run : `cd src && dotnet test --filter "FullyQualifiedName~MailThreading"`
Attendu : PASS (6 tests).

- [ ] **Step 5 : Commit**

```
feat: add MailThreading, the pure thread ordering behind grouped lists
```

---

### Task 3 : Branche `grouped` du listing IMAP + modèles de réponse

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/MailThread.cs`
- Modify: `src/snoopy.microservice/Models/Mail/MailFolderPage.cs`
- Modify: `src/snoopy.microservice/Services/ImapMessageCommands.cs:312-363` (`ListMessagesAsync`)
- Modify: `src/snoopy.microservice/Services/ImapSession.cs:157-158`
- Modify: `src/snoopy.microservice/Repositories/IMailMessageRepository.cs:11`
- Modify: `src/snoopy.microservice/Repositories/MailMessageRepository.cs:15-22`

**Interfaces:**
- Consumes: `MailThreading.Arrange` (Task 2), `MailPaging.PageOf`, `SummaryItems`/`SummaryHeaders`/`ToSummary` existants.
- Produces: `ListAsync`/`ListMessagesAsync` prennent un `bool grouped` supplémentaire (avant le `CancellationToken`) ; `MailFolderPage.Threads : List<MailThread>?` et `MailFolderPage.TotalThreads : int?` (null → absents du JSON) ; `MailThread.Messages : List<MailMessageSummary>` newest-first. Consommés par Task 4 (contrôleur) et Task 6 (types frontend).

- [ ] **Step 1 : Créer les modèles**

`src/snoopy.microservice/Models/Mail/MailThread.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>One conversation of a grouped page: its messages, newest first.</summary>
public sealed class MailThread
{
    public List<MailMessageSummary> Messages { get; set; } = [];
}
```

Dans `MailFolderPage.cs`, ajouter après `Messages` :

```csharp
/// <summary>Grouped mode only: one entry per conversation, newest thread first. Null — and
/// therefore absent from the JSON — on a flat page, which is how the client tells the modes
/// apart.</summary>
public List<MailThread>? Threads { get; set; }

/// <summary>Grouped mode only: how many conversations the folder holds — what the pager
/// pages. Total keeps counting messages.</summary>
public int? TotalThreads { get; set; }
```

- [ ] **Step 2 : Faire traverser `grouped`**

Chaîne de signatures, du contrôleur vers l'IMAP (le contrôleur lui-même est Task 4) :

`IMailMessageRepository.cs` ligne 11 :

```csharp
Task<Result<MailFolderPage>> ListAsync(User user, MailAccountConnection connection, string folderPath, int page, int pageSize, bool grouped, CancellationToken cancellationToken);
```

`MailMessageRepository.cs` :

```csharp
public Task<Result<MailFolderPage>> ListAsync(
    User user, MailAccountConnection connection, string folderPath, int page, int pageSize,
    bool grouped, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(user);
    return sessions.WithSessionAsync(connection,
        session => session.ListMessagesAsync(folderPath, page, pageSize, grouped, cancellationToken), cancellationToken);
}
```

`ImapSession.cs` :

```csharp
public Task<Result<MailFolderPage>> ListMessagesAsync(string folderPath, int page, int pageSize, bool grouped, CancellationToken cancellationToken) =>
    _messages.ListMessagesAsync(folderPath, page, pageSize, grouped, cancellationToken);
```

- [ ] **Step 3 : La branche groupée d'`ImapMessageCommands.ListMessagesAsync`**

Nouvelle signature : `ListMessagesAsync(string folderPath, int page, int pageSize, bool grouped, CancellationToken cancellationToken)`. Insérer la branche **avant** le bloc `if (client.Capabilities.HasFlag(ImapCapabilities.Sort))` existant, qui reste le repli inchangé :

```csharp
// Grouped needs both capabilities: THREAD for the tree, SORT for the order threads and
// members take. Missing either, the page silently stays flat — the SORT fallback's pattern:
// a capability the server lacks degrades the shape, never the request.
if (grouped
    && client.Capabilities.HasFlag(ImapCapabilities.Sort)
    && client.Capabilities.HasFlag(ImapCapabilities.Thread)
    && client.ThreadingAlgorithms.Contains(ThreadingAlgorithm.References))
{
    var sorted = await folder.SortAsync(
        SearchQuery.All, [OrderBy.ReverseDate], cancellationToken);
    var tree = await folder.ThreadAsync(ThreadingAlgorithm.References, SearchQuery.All, cancellationToken);

    var threads = MailThreading.Arrange(tree, sorted);
    result.TotalThreads = threads.Count;
    result.Threads = [];

    var wantedThreads = MailPaging.PageOf(threads, page, pageSize);
    var uids = wantedThreads.SelectMany(thread => thread).ToList();
    if (uids.Count == 0) return Result.Success(result);

    // One FETCH for every member of the page's threads — expanding a row client-side costs
    // nothing, and a page of 50 threads stays one round trip like a flat page of 50 rows.
    var fetched = await folder.FetchAsync(uids, SummaryItems, SummaryHeaders, cancellationToken);
    var byUid = fetched.ToDictionary(item => item.UniqueId);

    foreach (var thread in wantedThreads)
    {
        var members = thread.Where(byUid.ContainsKey).Select(uid => ToSummary(byUid[uid])).ToList();
        if (members.Count > 0) result.Threads.Add(new MailThread { Messages = members });
    }

    return Result.Success(result);
}
```

Note : `folder.ThreadAsync` exige le dossier ouvert — il l'est déjà (ligne 316). Ne pas remplir `result.Messages` en mode groupé : la page groupée parle par `Threads`.

- [ ] **Step 4 : Compiler et lancer la suite backend entière**

Run : `cd src && dotnet build` puis `cd src && dotnet test`
Attendu : la compilation révèle chaque appelant de `ListAsync`/`ListMessagesAsync` restant (mocks des tests contrôleur inclus — leur passer `false` ou adapter les `It.IsAny<bool>()`) ; corriger jusqu'au vert complet. Vérifier `git status` — revert `ApiDocumentation.xml` s'il a dérivé.

- [ ] **Step 5 : Commit**

```
feat: grouped listing over IMAP THREAD, threads and totalThreads on the page
```

---

### Task 4 : Contrôleur — `grouped=true` sur `GET /api/Mail/Messages`

**Files:**
- Modify: `src/snoopy.microservice/Controllers/MailMessagesController.cs:55-75`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailMessagesControllerTests.cs` (fichier existant)

**Interfaces:**
- Consumes: `IMailMessageRepository.ListAsync(user, connection, folder, page, pageSize, grouped, ct)` (Task 3).
- Produces: `GET /api/Mail/Messages?folder=&page=&pageSize=&grouped=` — `grouped` optionnel, défaut `false`. Consommé par Task 6 (`api.js`).

- [ ] **Step 1 : Écrire les tests qui échouent**

Dans `MailMessagesControllerTests.cs`, sur le modèle des tests `GetMessages` existants (mock d'`IMailMessageRepository`, contexte authentifié via `ControllerTestHelpers.CreateAuthenticatedContext`) :

```csharp
[Fact]
public async Task GetMessages_passes_grouped_to_the_repository()
{
    // Arrange like the existing GetMessages tests, then:
    messages.Setup(m => m.ListAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(),
            "INBOX", 0, 50, true, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Success(new MailFolderPage()));

    var result = await controller.GetMessages("INBOX", 0, 50, grouped: true);

    messages.VerifyAll();
}

[Fact]
public async Task GetMessages_defaults_grouped_to_false()
{
    messages.Setup(m => m.ListAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(),
            "INBOX", 0, 50, false, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Success(new MailFolderPage()));

    var result = await controller.GetMessages("INBOX");

    messages.VerifyAll();
}
```

Adapter le squelette exact (noms des mocks, construction du contrôleur) à ce que font les tests `GetMessages` déjà présents dans le fichier — reprendre leur Arrange à l'identique.

- [ ] **Step 2 : Vérifier l'échec**

Run : `cd src && dotnet test --filter "FullyQualifiedName~MailMessagesController"`
Attendu : échec de compilation — `GetMessages` n'a pas de paramètre `grouped`.

- [ ] **Step 3 : Implémenter**

Dans `GetMessages`, ajouter le paramètre et le passer :

```csharp
public async Task<ActionResult<MailFolderPage>> GetMessages(
    [FromQuery] string folder,
    [FromQuery] int page = 0,
    [FromQuery] int pageSize = 50,
    [FromQuery] bool grouped = false,
    CancellationToken cancellationToken = default)
```

et ligne 70 : `messages.ListAsync(AuthenticatedUser, connection, folder, page, pageSize, grouped, cancellationToken)`. Compléter le doc-comment : `/// <param name="grouped">group the page into conversations (server THREAD permitting)</param>`.

- [ ] **Step 4 : Vérifier le vert, suite entière**

Run : `cd src && dotnet test`
Attendu : PASS partout — `MailRouteSurfaceTests` ne bouge pas (paramètre de query, même template). Revert `ApiDocumentation.xml` si dérivé.

- [ ] **Step 5 : Commit**

```
feat: grouped query parameter on GET /api/Mail/Messages
```

---

### Task 5 : Préférence frontend + toggle Settings

**Files:**
- Modify: `src/frontend/src/hooks/usePreferences.ts`
- Modify: `src/frontend/src/modules/settings/general/GeneralPage.tsx` (section Layout, après le `ToggleRow` show-preview, lignes 221-229)
- Modify: `src/frontend/src/locales/en/settings.json`, `src/frontend/src/locales/fr/settings.json`
- Test: `src/frontend/src/modules/settings/general/GeneralPage.test.tsx` (fichier existant)

**Interfaces:**
- Consumes: la clé registre `mail.groupConversations` (Task 1).
- Produces: `PREFERENCE_KEYS.groupConversations`, `groupConversationsOf(preferences: Preferences): boolean` (strictement `'true'`). Consommés par Tasks 7-9.

- [ ] **Step 1 : Écrire le test qui échoue**

Dans `GeneralPage.test.tsx`, sur le modèle du test du toggle show-preview existant (préférences primées dans le cache, `save` vérifié via le mock d'`api.setPreference`) :

```tsx
it('toggles conversation grouping', async () => {
  renderPage()  // the file's existing helper, preferences primed with defaults
  const toggle = await screen.findByLabelText('Group conversations')
  expect(toggle).not.toBeChecked()

  await userEvent.click(toggle)
  expect(api.setPreference).toHaveBeenCalledWith('mail.groupConversations', 'true')
})
```

Reprendre les noms exacts du helper de rendu et du mock déjà utilisés dans ce fichier.

- [ ] **Step 2 : Vérifier l'échec**

Run : `cd src/frontend && npx vitest run src/modules/settings/general/GeneralPage.test.tsx`
Attendu : FAIL — aucun contrôle « Group conversations ».

- [ ] **Step 3 : Implémenter**

`usePreferences.ts` — dans `PREFERENCE_KEYS`, après `showFolderIcons` :

```ts
groupConversations: 'mail.groupConversations',
```

et l'accesseur, après `showFolderIconsOf` :

```ts
/** Off unless explicitly on — the list has always been flat, so a backend that does not know
    the key yet must keep it that way. */
export function groupConversationsOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.groupConversations] === 'true'
}
```

`GeneralPage.tsx` — après le `ToggleRow` show-preview (importer `groupConversationsOf`) :

```tsx
<ToggleRow
  id="group-conversations"
  label={t('general.groupConversations.label')}
  hint={t('general.groupConversations.hint')}
  checked={groupConversationsOf(preferences)}
  disabled={setPreference.isPending}
  onChange={on => save(PREFERENCE_KEYS.groupConversations, String(on),
    t(on ? 'general.groupConversations.on' : 'general.groupConversations.off'))}
/>
```

`en/settings.json`, dans `general`, à côté de `preview` :

```json
"groupConversations": {
  "label": "Group conversations",
  "hint": "Show the messages of one conversation as a single expandable row.",
  "on": "Conversations will be grouped.",
  "off": "Messages will be listed individually."
}
```

`fr/settings.json` (écrire le bloc en une passe — apostrophes U+2019, aucune ponctuation à insécable ici) :

```json
"groupConversations": {
  "label": "Grouper les conversations",
  "hint": "Afficher les messages d’une même conversation sur une seule ligne dépliable.",
  "on": "Les conversations seront groupées.",
  "off": "Les messages seront listés individuellement."
}
```

- [ ] **Step 4 : Vérifier le vert + parité**

Run : `cd src/frontend && npx vitest run src/modules/settings/general/GeneralPage.test.tsx src/locales/parity.test.ts`
Attendu : PASS.

- [ ] **Step 5 : Commit**

```
feat: group-conversations setting on the General page
```

---

### Task 6 : Types, client d'API et helpers de fil purs

**Files:**
- Modify: `src/frontend/src/modules/mail/api/mailTypes.ts`
- Modify: `src/frontend/src/api.js:304-305` (`getMailMessages`)
- Create: `src/frontend/src/modules/mail/list/threading.ts`
- Test: `src/frontend/src/modules/mail/list/threading.test.ts`

**Interfaces:**
- Consumes: la forme de réponse de Task 3 (`threads?`, `totalThreads?`).
- Produces:
  - `MailThread { messages: MailMessageSummary[] }` ; `MailFolderPage` gagne `threads?: MailThread[]` et `totalThreads?: number` (optionnels : l'API omet les null).
  - `api.getMailMessages(folder, page, pageSize, options)` accepte `options.grouped` et ajoute `&grouped=true` à l'URL quand il est vrai.
  - `threading.ts` : `interface ThreadGroup { key: number; messages: MailMessageSummary[] }` ; `threadKeyOf(messages): number` (UID du dernier élément — le plus ancien) ; `groupsOf(page: MailFolderPage): ThreadGroup[]` (fils, ou singletons en mode plat) ; `dedupeThreads(pages: MailFolderPage[]): ThreadGroup[]` (snapshot : premier fil vu par clé gagne, un membre déjà vu ailleurs est retiré, un fil vidé disparaît) ; `memberUids(groups: ThreadGroup[]): number[]` (aplati, ordre d'affichage). Consommés par Tasks 7-9.

- [ ] **Step 1 : Écrire les tests qui échouent**

`threading.test.ts` :

```ts
import { describe, expect, it } from 'vitest'
import type { MailFolderPage, MailMessageSummary } from '../api/mailTypes'
import { dedupeThreads, groupsOf, memberUids, threadKeyOf } from './threading'

const msg = (uid: number): MailMessageSummary => ({
  uid, subject: `s${uid}`, fromName: '', fromAddress: 'a@b.c', to: [], date: '2026-08-13T10:00:00Z',
  seen: true, flagged: false, answered: false, hasAttachments: false, size: 1, preview: '', priority: 'normal',
})

const page = (over: Partial<MailFolderPage>): MailFolderPage => ({
  folderPath: 'INBOX', uidValidity: 1, total: 0, page: 0, pageSize: 100, messages: [], ...over,
})

describe('threadKeyOf', () => {
  it('is the oldest member — the last of the newest-first list', () => {
    expect(threadKeyOf([msg(30), msg(10)])).toBe(10)
  })
})

describe('groupsOf', () => {
  it('maps a grouped page one group per thread', () => {
    const p = page({ threads: [{ messages: [msg(30), msg(10)] }, { messages: [msg(20)] }] })
    expect(groupsOf(p)).toEqual([
      { key: 10, messages: [msg(30), msg(10)] },
      { key: 20, messages: [msg(20)] },
    ])
  })

  it('maps a flat page to singleton groups', () => {
    const p = page({ messages: [msg(3), msg(2)] })
    expect(groupsOf(p)).toEqual([
      { key: 3, messages: [msg(3)] },
      { key: 2, messages: [msg(2)] },
    ])
  })
})

describe('dedupeThreads', () => {
  it('keeps the first version of a thread seen twice across blocks', () => {
    const block0 = page({ threads: [{ messages: [msg(30), msg(10)] }] })
    const block1 = page({ threads: [{ messages: [msg(10)] }] })
    expect(dedupeThreads([block0, block1])).toEqual([{ key: 10, messages: [msg(30), msg(10)] }])
  })

  it('drops a member already shown under another thread, and an emptied thread whole', () => {
    const block0 = page({ threads: [{ messages: [msg(30), msg(10)] }] })
    const block1 = page({ threads: [{ messages: [msg(30)] }] })
    expect(dedupeThreads([block0, block1])).toEqual([{ key: 10, messages: [msg(30), msg(10)] }])
  })
})

describe('memberUids', () => {
  it('flattens in display order', () => {
    expect(memberUids([{ key: 10, messages: [msg(30), msg(10)] }, { key: 20, messages: [msg(20)] }]))
      .toEqual([30, 10, 20])
  })
})
```

- [ ] **Step 2 : Vérifier l'échec**

Run : `cd src/frontend && npx vitest run src/modules/mail/list/threading.test.ts`
Attendu : FAIL — module absent.

- [ ] **Step 3 : Implémenter**

`mailTypes.ts` — après `MailFolderPage.messages` :

```ts
/** Grouped mode only — absent on a flat page, which is how the client tells the modes apart. */
threads?: MailThread[]
/** Grouped mode only: what the pager pages. `total` keeps counting messages. */
totalThreads?: number
```

et au-dessus de `MailFolderPage` :

```ts
/** One conversation of a grouped page: its messages, newest first. */
export interface MailThread {
  messages: MailMessageSummary[]
}
```

`api.js` :

```js
getMailMessages: (folder, page, pageSize, options) =>
  request('GET', `/api/Mail/Messages?folder=${encodeURIComponent(folder)}&page=${page}&pageSize=${pageSize}`
    + (options?.grouped ? '&grouped=true' : ''), undefined, options),
```

`threading.ts` :

```ts
import type { MailFolderPage, MailMessageSummary, MailThread } from '../api/mailTypes'

/** One list row: a conversation, or a single message wrapped as one. */
export interface ThreadGroup {
  /** The oldest member's uid — the newest changes on every arrival, so it cannot be the key. */
  key: number
  /** Newest first, as the backend sends them. */
  messages: MailMessageSummary[]
}

export function threadKeyOf(messages: MailMessageSummary[]): number {
  return messages[messages.length - 1].uid
}

const toGroup = (thread: MailThread): ThreadGroup =>
  ({ key: threadKeyOf(thread.messages), messages: thread.messages })

/** A grouped page speaks through `threads`; a flat one is its messages, one group each. */
export function groupsOf(page: MailFolderPage): ThreadGroup[] {
  if (page.threads) return page.threads.filter(t => t.messages.length > 0).map(toGroup)
  return page.messages.map(message => ({ key: message.uid, messages: [message] }))
}

/**
 * Snapshot semantics, the dedupeByUid rules transposed: the first version of a thread wins,
 * a member already shown under an earlier thread is dropped, and a thread emptied by that
 * drop disappears — two rows for one message would otherwise survive an offset shift.
 */
export function dedupeThreads(pages: MailFolderPage[]): ThreadGroup[] {
  const seenThreads = new Set<number>()
  const seenUids = new Set<number>()
  const groups: ThreadGroup[] = []

  for (const page of pages) {
    for (const group of groupsOf(page)) {
      if (seenThreads.has(group.key)) continue
      seenThreads.add(group.key)
      const fresh = group.messages.filter(message => !seenUids.has(message.uid))
      fresh.forEach(message => seenUids.add(message.uid))
      if (fresh.length > 0) groups.push({ key: group.key, messages: fresh })
    }
  }

  return groups
}

export function memberUids(groups: ThreadGroup[]): number[] {
  return groups.flatMap(group => group.messages.map(message => message.uid))
}
```

- [ ] **Step 4 : Vérifier le vert**

Run : `cd src/frontend && npx vitest run src/modules/mail/list/threading.test.ts`
Attendu : PASS.

- [ ] **Step 5 : Commit**

```
feat: thread types, grouped api flag and pure thread grouping helpers
```

---

### Task 7 : Couche requêtes — `grouped` dans les clés, le stream et `useMessageList`

**Files:**
- Modify: `src/frontend/src/modules/mail/queries.ts` (`mailKeys.messages`/`messageStream`, `useMessages`, `useMessageStream`)
- Modify: `src/frontend/src/modules/mail/list/messageStream.ts` (`nextBlockIndex`)
- Modify: `src/frontend/src/modules/mail/list/useMessageList.ts`
- Test: `src/frontend/src/modules/mail/queries.test.tsx`, `src/frontend/src/modules/mail/list/messageStream.test.ts` (existants), `src/frontend/src/modules/mail/list/useMessageList.test.tsx` (existant ; sinon créer)

**Interfaces:**
- Consumes: `groupConversationsOf` (Task 5), `groupsOf`/`dedupeThreads`/`memberUids` (Task 6), `api.getMailMessages(..., { grouped })`.
- Produces:
  - `useMessages(folderPath, page, pageSize, enabled?, grouped?)` et `useMessageStream(folderPath, requestSize, enabled, grouped?)` — `grouped` par défaut `false`, ajouté **en fin** de clé (les index 1/3 lus par `placeholderData` et `useListRefresh` ne bougent pas) et passé à l'API.
  - `nextBlockIndex` juge le bloc sur `lastPage.threads?.length ?? lastPage.messages.length`.
  - `MessageListState` gagne `groups: ThreadGroup[]` ; `messages` devient la liste aplatie des membres (identique à aujourd'hui en mode plat) ; `paging.lastPage` se calcule sur `totalThreads` quand la page en porte un.
  - Consommés par Tasks 8-9.

- [ ] **Step 1 : Écrire les tests qui échouent**

Dans `messageStream.test.ts` :

```ts
it('judges a grouped block on its thread count', () => {
  const short = page({ threads: [{ messages: [msg(1)] }], messages: [] })
  expect(nextBlockIndex(short, 1, 2)).toBeUndefined()

  const full = page({
    threads: [{ messages: [msg(9), msg(1)] }, { messages: [msg(5)] }], messages: [],
  })
  expect(nextBlockIndex(full, 1, 2)).toBe(1)
})
```

(réutiliser les fabriques `page`/`msg` du fichier ; en créer sur le modèle de Task 6 si absentes).

Dans `queries.test.tsx`, sur le modèle des tests `useMessages` existants :

```tsx
it('passes grouped to the api and keys the query on it', async () => {
  vi.mocked(api.getMailMessages).mockResolvedValue({ ...page0, threads: [], totalThreads: 0 })
  renderHook(() => useMessages('INBOX', 0, 30, true, true), { wrapper })
  await waitFor(() => expect(api.getMailMessages).toHaveBeenCalledWith(
    'INBOX', 0, 30, expect.objectContaining({ grouped: true })))
})
```

Dans `useMessageList.test.tsx` (suivre le montage existant du fichier : `usePreferences` primé, `useMessages`/`useMessageStream` mockés) :

```tsx
it('exposes thread groups and pages on totalThreads when the page is grouped', () => {
  // Paged mode, preferences with mail.groupConversations = 'true' and pageSize '30';
  // useMessages mocked to answer { threads: [{messages:[msg(30), msg(10)]}], totalThreads: 61, total: 90, ... }
  const { result } = renderList()
  expect(result.current.groups).toEqual([{ key: 10, messages: [msg(30), msg(10)] }])
  expect(result.current.messages.map(m => m.uid)).toEqual([30, 10])
  expect(result.current.paging?.lastPage).toBe(2)  // ceil(61 / 30) - 1
})
```

- [ ] **Step 2 : Vérifier l'échec**

Run : `cd src/frontend && npx vitest run src/modules/mail/list/messageStream.test.ts src/modules/mail/queries.test.tsx src/modules/mail/list/useMessageList.test.tsx`
Attendu : FAIL (signatures et `groups` absents).

- [ ] **Step 3 : Implémenter**

`messageStream.ts` :

```ts
/** Stops on a short block rather than on `total`: the total moves when mail arrives. A grouped
    block is measured in threads — its unit of request. */
export function nextBlockIndex(
  lastPage: MailFolderPage, loadedBlocks: number, requestSize: number,
): number | undefined {
  const count = lastPage.threads ? lastPage.threads.length : lastPage.messages.length
  return count < requestSize ? undefined : loadedBlocks
}
```

`queries.ts` — clés (vérifier la forme exacte de `mailKeys` en tête de fichier et ajouter `grouped` en dernière position des deux entrées `messages` et `messageStream`) ; puis :

```ts
export function useMessages(
  folderPath: string | null, page: number, pageSize: number, enabled = true, grouped = false,
) {
  const accountId = useAccountId()

  return useQuery<MailFolderPage>({
    queryKey: mailKeys.messages(accountId, folderPath ?? '', page, pageSize, grouped),
    queryFn: ({ signal }) => api.getMailMessages(folderPath, page, pageSize, { signal, accountId, grouped }),
    ...
  })
}

export function useMessageStream(folderPath: string | null, requestSize: number, enabled: boolean, grouped = false) {
  const accountId = useAccountId()

  return useInfiniteQuery({
    queryKey: mailKeys.messageStream(accountId, folderPath ?? '', requestSize, grouped),
    queryFn: ({ pageParam, signal }) =>
      api.getMailMessages(folderPath, pageParam, requestSize,
        { signal, accountId, grouped }) as Promise<MailFolderPage>,
    ...
  })
}
```

(`...` = le corps existant inchangé — `placeholderData`, `getNextPageParam`, `enabled`, `refetchOnWindowFocus`.)

`useMessageList.ts` :

```ts
import { dedupeThreads, groupsOf, memberUids, type ThreadGroup } from './threading'
import { groupConversationsOf, isStreaming, requestSizeOf, usePreferences } from '../../../hooks/usePreferences'

export interface MessageListState {
  groups: ThreadGroup[]
  messages: MailMessageSummary[]
  ...  // le reste inchangé
}
```

Corps : `const grouped = preferences ? groupConversationsOf(preferences) : false`, passé aux deux hooks. En streaming : `const streamedGroups = useMemo(() => dedupeThreads(blocks), [blocks])` remplace le memo actuel (en mode plat `dedupeThreads` rend des singletons — équivalent à `dedupeByUid` ; supprimer l'import devenu inutile) ; `groups: streamedGroups`, `messages: memberUids` non — `messages` doit rester des résumés : ajouter dans `threading.ts` si besoin `flatMessages(groups) = groups.flatMap(g => g.messages)` et l'utiliser ici (`messages: useMemo(() => groups.flatMap(g => g.messages), [groups])`). En pagé : `const pageGroups = paged.data ? groupsOf(paged.data) : []`, `groups: pageGroups`, `messages: pageGroups.flatMap(g => g.messages)`, et :

```ts
const pagedUnit = paged.data?.totalThreads ?? total
paging: {
  page,
  lastPage: requestSize > 0 ? Math.max(0, Math.ceil(pagedUnit / requestSize) - 1) : 0,
  onSelect: setPage,
},
```

`WAITING` gagne `groups: []`.

- [ ] **Step 4 : Vérifier le vert, suite frontend entière**

Run : `cd src/frontend && npx vitest run`
Attendu : PASS — les tests existants de `useMessageList`/`queries` compilent avec les nouveaux membres.

- [ ] **Step 5 : Commit**

```
feat: grouped-aware query layer, stream blocks and useMessageList groups
```

---

### Task 8 : Poll — merge du bloc 0 par clé de fil

**Files:**
- Modify: `src/frontend/src/modules/mail/list/useListRefresh.ts`
- Test: `src/frontend/src/modules/mail/list/useListRefresh.test.tsx` (existant)

**Interfaces:**
- Consumes: `groupConversationsOf` (Task 5), `dedupeThreads` (Task 6), la clé de stream à 4 composants (Task 7).
- Produces: `refreshFirstBlock(client, accountId, folder, grouped)` — même mécanique, clé et merge adaptés au mode.

- [ ] **Step 1 : Écrire le test qui échoue**

Dans `useListRefresh.test.tsx`, sur le modèle du test de merge streaming existant (cache primé avec un `InfiniteData`, poll simulé par un changement de `useFolders`) — préférences avec `mail.groupConversations: 'true'` et `mail.pageSize: 'all'` :

```tsx
it('merges a grouped fresh block 0 by thread key, fresh first', async () => {
  // Cache primed under mailKeys.messageStream(account, 'INBOX', BLOCK_SIZE, true) with
  // pages: [{ threads: [{messages:[msg(30), msg(10)]}], ... }]
  // api.getMailMessages resolves { threads: [{messages:[msg(40), msg(30), msg(10)]}, {messages:[msg(20)]}], ... }
  // After the poll tick:
  const merged = client.getQueryData<InfiniteData<MailFolderPage>>(key)!
  expect(merged.pages[0].threads!.map(t => t.messages.map(m => m.uid)))
    .toEqual([[40, 30, 10], [20]])
})
```

- [ ] **Step 2 : Vérifier l'échec**

Run : `cd src/frontend && npx vitest run src/modules/mail/list/useListRefresh.test.tsx`
Attendu : FAIL — la clé du cache ne matche pas (pas de composant `grouped`) ou le merge écrase le fil.

- [ ] **Step 3 : Implémenter**

```ts
async function refreshFirstBlock(client: QueryClient, accountId: string, folder: string, grouped: boolean) {
  const key = mailKeys.messageStream(accountId, folder, BLOCK_SIZE, grouped)
  try {
    const fresh: MailFolderPage = await api.getMailMessages(folder, 0, BLOCK_SIZE, { accountId, grouped })
    client.setQueryData<InfiniteData<MailFolderPage>>(key, old =>
      old
        ? {
            ...old,
            pages: [
              grouped
                ? { ...fresh,
                    threads: dedupeThreads([fresh, old.pages[0]]).map(group => ({ messages: group.messages })) }
                : { ...fresh, messages: dedupeByUid([fresh, old.pages[0]]) },
              ...old.pages.slice(1),
            ],
          }
        : old)
  } catch {
    // A poll-driven refresh fails in silence; the next tick tries again.
  }
}
```

Dans l'effet : `const grouped = groupConversationsOf(preferences)` et `refreshFirstBlock(client, accountId, folderPath, grouped)`. Le chemin pagé (`invalidateQueries` sur le préfixe `messagesIn`) couvre déjà les deux modes — le préfixe attrape la clé quelle que soit sa queue.

- [ ] **Step 4 : Vérifier le vert**

Run : `cd src/frontend && npx vitest run src/modules/mail/list/useListRefresh.test.tsx`
Attendu : PASS, anciens tests inclus (le mode plat passe `grouped: false` et garde son merge).

- [ ] **Step 5 : Commit**

```
feat: grouped block-0 merge in the poll refresh
```

---

### Task 9 : La ligne de fil dans `MessageList`

**Files:**
- Modify: `src/frontend/src/modules/mail/list/MessageList.tsx`
- Modify: `src/frontend/src/styles/mail.css`
- Modify: `src/frontend/src/locales/en/mail.json`, `src/frontend/src/locales/fr/mail.json`
- Test: `src/frontend/src/modules/mail/list/MessageList.test.tsx` (existant)

**Interfaces:**
- Consumes: `MessageListState.groups` (Task 7), `memberUids` (Task 6), `useSelection` (inchangé), `useSetFlags`/`useMoveMessages`/`useDeleteMessages` (batch d'UIDs, inchangés).
- Produces: le rendu groupé — aucun nouveau composant exporté.

- [ ] **Step 1 : Écrire les tests qui échouent**

Dans `MessageList.test.tsx`, sur le modèle des tests de rendu existants (mock d'`useMessageList` — lui faire désormais répondre `groups` en plus de `messages`) :

```tsx
it('renders one row per thread with count badge and aggregate state', () => {
  // groups: [{ key: 10, messages: [read msg 30 flagged=false, unread msg 10 flagged=true] }]
  renderList()
  expect(screen.getByText('2')).toBeInTheDocument()               // the count badge
  expect(screen.getByLabelText('Expand conversation')).toBeInTheDocument()
  // aggregate: the row reads unread (dot) and starred even though the latest member is neither
})

it('renders a single-message thread exactly as a plain row', () => {
  // groups: [{ key: 20, messages: [msg(20)] }]
  renderList()
  expect(screen.queryByLabelText('Expand conversation')).not.toBeInTheDocument()
})

it('expands to member sub-rows; a sub-row opens its own message', async () => {
  renderList()
  await userEvent.click(screen.getByLabelText('Expand conversation'))
  // two member rows now visible; clicking the older one selects uid 10
  // (assert through the onSelect spy the file's harness already wires)
})

it('opens the latest member when the collapsed row is clicked', async () => {
  renderList()
  await userEvent.click(screen.getByRole('button', { name: /s30/ }))
  expect(onSelect).toHaveBeenCalledWith(30)
})

it('checks the whole thread from the collapsed checkbox and stars it whole', async () => {
  renderList()
  await userEvent.click(screen.getByLabelText(/Select conversation/))
  // toolbar count reads 2
  await userEvent.click(screen.getAllByLabelText('Star')[0])
  expect(api.setMailFlags).toHaveBeenCalledWith(expect.objectContaining({ uids: [30, 10] }))
})
```

Adapter libellés/spies aux conventions exactes du fichier (il mocke `useMessageList` et les mutations — reprendre son Arrange).

- [ ] **Step 2 : Vérifier l'échec**

Run : `cd src/frontend && npx vitest run src/modules/mail/list/MessageList.test.tsx`
Attendu : FAIL.

- [ ] **Step 3 : Implémenter le rendu**

Dans `MessageList.tsx` :

1. **Consommer les groupes.** `const { groups, messages, total, isLoading, isError, paging, streaming } = view` — le chemin recherche fabrique ses singletons : dans l'objet `view` de la branche `searching`, ajouter `groups: (searchQuery.data?.results ?? []).map(r => ({ key: r.uid, messages: [r] }))`.
2. **État de dépliage.** À côté de la sélection :

```tsx
const [expanded, setExpanded] = useState<Set<number>>(() => new Set())
useEffect(() => { setExpanded(new Set()) }, [resetKey])
const toggleExpanded = (key: number) => setExpanded(prev => {
  const next = new Set(prev)
  if (next.has(key)) next.delete(key); else next.add(key)
  return next
})
```

3. **Généraliser deux handlers au batch** (mêmes noms, paramètre élargi) : `toggle(uids: number[], flag, value)` appelle `setFlags.mutate({ folderPath, uids, flag, value })` (les appels mono-message passent `[message.uid]` et `!message.seen`/`!message.flagged`) ; `moveTo(target, uids: number[])` passe `uids` et appelle `onDeparted?.(uids[0], uids)`.
4. **Extraire le corps du `.map()` en `renderRow`** — fonction locale au-dessus de `rows()` :

```tsx
function renderRow(message: MailMessageSummary, rowIndex: number, thread?: {
  count: number; uids: number[]; expanded: boolean; onToggle: () => void
  anyUnread: boolean; anyFlagged: boolean; anyAttachments: boolean
}, member = false): ReactNode
```

Le corps est l'actuel contenu du `.map()` avec ces substitutions quand `thread` est présent : le dot non-lu et la classe `is-unread` lisent `thread.anyUnread` (sinon `!message.seen`) ; l'étoile lit `thread.anyFlagged` et clique `toggle(thread.uids, 'flagged', !thread.anyFlagged)` ; le trombone lit `thread.anyAttachments` ; la checkbox a l'aria-label `t('list.selectThread', { from })`, `checked` = tous les membres sélectionnés, et son onClick fait `thread.uids.forEach(...)` — concrètement ajouter au retour d'`useSelection` un `setMany(keys: T[], on: boolean)` (nouveau, dans `useSelection.ts` : `setSelected(prev => { const next = new Set(prev); keys.forEach(k => on ? next.add(k) : next.delete(k)); return next })`) et appeler `selection.setMany(thread.uids, !allMembersSelected)` ; les boutons du cluster passent `thread.uids` à `toggle`/`moveTo` (le delete en corbeille ouvre le confirm d'expunge sur le fil : stocker les uids à expurger — élargir l'état `expunging` en `{ label: string; uids: number[] }`, les appels mono-ligne passant `[message.uid]`) ; après la date, le badge et le chevron :

```tsx
{thread && (
  <span className="message-row-thread-count" title={t('list.threadCount', { count: thread.count })}>
    {thread.count}
  </span>
)}
{thread && (
  <button
    type="button"
    className={`row-btn thread-toggle${thread.expanded ? ' is-open' : ''}`}
    aria-expanded={thread.expanded}
    aria-label={t(thread.expanded ? 'list.collapseThread' : 'list.expandThread')}
    onClick={event => { event.stopPropagation(); thread.onToggle() }}
  >
    <ChevronIcon size={14} />
  </button>
)}
```

(utiliser l'icône chevron existante de `src/icons/` — chercher `Chevron` ; en créer une sur le modèle des voisines seulement si aucune ne convient). `member` ajoute la classe `is-thread-member`. Le badge et le chevron se placent dans les deux skins (`wide` : après `message-row-date` ; étroite : dans `message-row-top`).

5. **La boucle des groupes.** `rows()` itère `groups` ; `rowIndex` court sur les membres aplatis (l'ordre de `loadedUids`, désormais `memberUids(groups)` — remplacer la ligne `const loadedUids = messages.map(...)`, le résultat est identique) :

```tsx
let rowIndex = 0
{groups.map((group, groupIndex) => {
  const single = group.messages.length === 1
  const latest = group.messages[0]
  const startIndex = rowIndex
  rowIndex += group.messages.length
  const isOpen = expanded.has(group.key)
  const thread = single ? undefined : {
    count: group.messages.length,
    uids: group.messages.map(m => m.uid),
    expanded: isOpen,
    onToggle: () => toggleExpanded(group.key),
    anyUnread: group.messages.some(m => !m.seen),
    anyFlagged: group.messages.some(m => m.flagged),
    anyAttachments: group.messages.some(m => m.hasAttachments),
  }
  return (
    <li key={group.key}>
      {streaming && groupIndex === sentinelRow && <LoadMoreSentinel onReach={streaming.loadMore} />}
      {renderRow(latest, startIndex, thread)}
      {isOpen && !single && group.messages.map((m, i) => renderRow(m, startIndex + i, undefined, true))}
    </li>
  )
})}
```

`sentinelRow` passe sur les groupes : `sentinelIndexOf(groups.length)`. La ligne repliée ouvre `latest` (comportement d'`openRow` inchangé — c'est le message rendu). Les sous-lignes dépliées incluent le dernier message : la ligne parent représente le fil, les sous-lignes chaque message.
6. **`onRows`** : inchangé dans l'esprit — il publie `messages.map(m => m.uid)`, qui est déjà l'aplati.

7. **CSS** (`mail.css`, avec les tokens existants — aucune couleur littérale) :

```css
.message-row-thread-count {
  flex: none;
  min-width: 18px;
  padding: 0 5px;
  border-radius: 9px;
  background: var(--badge-count-bg, var(--action-primary));
  color: var(--badge-count-fg);
  font-size: 11px;
  line-height: 18px;
  text-align: center;
}

.thread-toggle svg { transition: transform 0.15s; }
.thread-toggle.is-open svg { transform: rotate(90deg); }

.message-row.is-thread-member { padding-left: 34px; }
.message-row.is-line.is-thread-member { padding-left: 46px; }
```

Vérifier les noms de tokens réellement présents dans les palettes (`--badge-count-fg` existe — cf. badge du reader) ; ne pas en inventer : si `--badge-count-bg` n'existe pas, réutiliser le couple que `.folder-badge`/le badge de compteur non-lu emploie déjà dans `mail.css`.
8. **i18n** (`mail.json`, bloc `list`) — en :

```json
"threadCount": "{{count}} messages in this conversation",
"threadCount_one": "{{count}} message in this conversation",
"expandThread": "Expand conversation",
"collapseThread": "Collapse conversation",
"selectThread": "Select conversation from {{from}}"
```

fr (apostrophes U+2019 ; pas de ponctuation à insécable) :

```json
"threadCount": "{{count}} messages dans cette conversation",
"threadCount_one": "{{count}} message dans cette conversation",
"expandThread": "Déplier la conversation",
"collapseThread": "Replier la conversation",
"selectThread": "Sélectionner la conversation de {{from}}"
```

- [ ] **Step 4 : Vérifier le vert, suite entière**

Run : `cd src/frontend && npx vitest run`
Attendu : PASS — y compris parité des catalogues et les anciens tests de `MessageList` (le mode plat rend des singletons, donc byte-identique).

- [ ] **Step 5 : Commit**

```
feat: expandable conversation rows in the message list
```

---

### Task 10 : Sonde géométrique + vérification finale

**Files:**
- Modify: `src/frontend/probes/mobile-layout.html`

**Interfaces:**
- Consumes: les classes de Task 9 (`.message-row-thread-count`, `.thread-toggle`, `.is-thread-member`).

- [ ] **Step 1 : Ajouter le cas de sonde**

Dans `probes/mobile-layout.html`, sur le modèle des cas de liste existants : un cas « thread row expanded-360 » restituant le markup réel d'une ligne de fil dépliée (ligne parent avec badge + chevron + deux sous-lignes `is-thread-member`) dans une colonne de 360px, mesurant `clipped` et `smallest` sur le chevron (cible tactile) et le badge. Reprendre le markup **exact** produit par Task 9 — un fixture approximatif ne garde rien (règle du fichier).

- [ ] **Step 2 : Mesurer**

Ouvrir la sonde via `npm run dev` dans un navigateur Blink en émulation 360×640 et 320×640 ; lire `clipped`/`smallest`. Le chevron est un `.row-btn` du cluster : en dessous de 640px il suit les règles tactiles existantes — si `smallest` < 44 au même titre que les boutons de cluster actuels (déférés connus), le noter tel quel dans le rapport de tâche, ne pas « corriger » unilatéralement.

- [ ] **Step 3 : Vérification finale des deux suites**

Run : `cd src && dotnet test` puis `cd src/frontend && npx vitest run && npm run lint && npm run typecheck && npm run build`
Attendu : tout vert (l'instabilité connue du chunk lazy — mémoire projet — reste l'exception admise). Revert `ApiDocumentation.xml` si dérivé.

- [ ] **Step 4 : Commit**

```
test: probe case for expanded conversation rows
```

---

## Self-review (fait à l'écriture)

- Couverture spec : réglage (T1, T5), branche THREAD + modèles + repli (T2-T3), contrôleur (T4), types/API/helpers purs (T6), pagination sur `totalThreads` + stream (T7), poll (T8), rendu/sélection/étoile/skins/i18n (T9), sonde + vérif (T10). Recherche à plat : aucun changement — contrainte globale.
- Types cohérents : `Arrange(IList<MessageThread>, IList<UniqueId>)` (T2) consommé tel quel en T3 ; `grouped` avant le `CancellationToken` sur toute la chaîne ; `ThreadGroup {key, messages}` identique T6→T9 ; clés de requête à queue `grouped` T7→T8.

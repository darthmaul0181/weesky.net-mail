# Contacts 4b — l'éditeur étendu : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Les rapports de sous-agents dans le scratchpad sont préfixés `4b-task-N-…`.

**Spec :** [`docs/superpowers/specs/2026-08-22-webmail-contacts-4b-editor-design.md`](../specs/2026-08-22-webmail-contacts-4b-editor-design.md) — toute décision citée ici (« décision N ») y renvoie. En cas de doute, la spec fait foi.

**Goal :** rendre l'éditeur capable d'écrire tout ce que la fiche sait lire, et fermer au passage les deux pertes de données qu'il provoque aujourd'hui à chaque enregistrement — les positions non rendues et le nom affiché jamais envoyé.

**Architecture :** un fil `pref` traversant le backend jusqu'à `Preference` (le seul changement serveur), puis trois vagues frontend sur `ContactEditView` — d'abord un brouillon fidèle qui rend les positions sans ajouter un seul champ visible, puis les deux familles répétables, puis les neuf scalaires derrière un menu « ajouter un champ ».

**Tech stack :** .NET 10, EF Core (InMemory pour les tests), xUnit 2.9.3 ; React 18 + TypeScript, TanStack Query, Vitest + Testing Library, i18next.

## Global constraints

- Backend : `cd src && dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés) ; `cd src && dotnet build -warnaserror` doit rester à zéro avertissement.
- Frontend : `cd src/frontend && npm test` ; `npx tsc --noEmit` doit rester propre.
- `ApiDocumentation.xml` : artefact versionné que `dotnet test` régénère avec des centaines de lignes sans rapport — le réverter avant chaque commit (`git checkout -- src/snoopy.microservice/ApiDocumentation.xml`).
- `Assert.IsType<T>` vérifie le type exact : `BadRequestObjectResult` pour `BadRequest(body)`, jamais `ObjectResult`.
- Style C# : file-scoped namespaces, un type par fichier, records pour les DTO, `sealed`, `internal` par défaut, cancellation tokens partout.
- Style TS : pas de `any`, les champs que l'API omet (`WhenWritingNull`) se déclarent `champ?: T`, jamais `T | null`.
- i18n : toute clé neuve existe dans `locales/en/contacts.json` **et** `locales/fr/contacts.json` ; l'UI du site est en anglais, la parité est vérifiée par la suite.
- Commits : concis, sujet + ligne vide + corps de 2 lignes max, jamais commencer/finir par `@`, terminer par `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- **N'écrire aucun outil d'écriture de fichier avec un here-string PowerShell dans l'outil Bash** : utiliser `git commit -F -` avec un heredoc.
- Le plafond de chaque famille vient de `ContactValidator` : `MaxAddressesPerContact = 50`, `MaxPhonesPerContact = 10`, `MaxPostalAddressesPerContact = 10`.

## Découpage

Quatre paquets, chacun livrant un incrément vérifiable seul :

| | Paquet | Vérifiable par |
|---|---|---|
| 1 | Le fil de `pref`, backend | la suite .NET ; aucun écran ne bouge |
| 2 | Le brouillon fidèle : positions et nom affiché rendus | la suite frontend ; l'écran est identique, les deux pertes sont fermées |
| 3 | Les deux familles répétables dans l'éditeur | la suite frontend + l'écran |
| 4 | Les neuf scalaires et « + Ajouter un champ » | la suite frontend + l'écran |

---

### Task 1 : le fil de `pref` jusqu'à `Preference`

**Files :**
- Modify : `src/snoopy.microservice/Models/Contacts/ContactLine.cs` (les trois payloads)
- Modify : `src/snoopy.microservice/Models/Contacts/ContactWrite.cs` (les trois records de ligne)
- Modify : `src/snoopy.microservice/Services/ContactValidator.cs`
- Modify : `src/snoopy.microservice/Services/Contacts/VCardComposer.cs` (`TextLine`, `PostalLine`, un helper neuf)
- Modify : `src/snoopy.microservice/Repositories/ContactStore.cs` (les constructions de `ContactWriteEmail`/`Phone`/`Address`)
- Modify : `src/snoopy.microservice/Services/Contacts/VCardImportMapper.cs` (idem)
- Modify : `src/snoopy.microservice/Controllers/ContactsController.cs` (idem, chemin CSV)
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/VCardComposerTests.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/ContactValidatorTests.cs`

**Interfaces (produit pour les tâches suivantes) :**

```csharp
// ContactLine.cs — les trois payloads gagnent le même champ
public int? Pref { get; set; }

// ContactWrite.cs
public sealed record ContactWriteEmail(int? Position, string Address, string Type, int? Pref = null);
public sealed record ContactWritePhone(int? Position, string Number, string Type, int? Pref = null);
public sealed record ContactWriteAddress(
    int? Position, string Type, string? PoBox, string? Extended, string? Street,
    string? Locality, string? Region, string? PostalCode, string? Country, int? Pref = null);
```

Le paramètre optionnel en dernière position garde tous les sites de construction existants compilables — c'est voulu, aucun autre producteur que l'éditeur ne nomme `pref`.

**Sémantique du champ, à respecter partout :**

| Valeur | Sens |
|---|---|
| `null` | l'écriture ne nomme pas la préférence, la carte garde la sienne |
| `1`–`100` | poser `PREF` à cette valeur |
| `101` | retirer `PREF` de la ligne |

C'est l'exacte réciproque de ce que `VCardProjector.Line` produit en lecture (décision 5 bis de 4a) : `PREF=` → la valeur, `TYPE=..,PREF` → 1, sinon 101.

- [ ] **Step 1 : Écrire les tests du composeur, rouges**

Dans `VCardComposerTests.cs`, à la suite des tests existants :

```csharp
[Fact] // pref posé par une écriture atteint la carte, et la projection le relit
public void Compose_PosesThePreferenceTheWriteNames()
{
    var card = Card("EMAIL;TYPE=INTERNET:a@b.c");
    var write = WriteWith(addresses: [new ContactWriteEmail(0, "a@b.c", "INTERNET", 1)]);

    var output = VCardComposer.Compose(card, Uid, write);

    Assert.Equal(1, VCardProjector.Project(output).Addresses.Single().Line.Pref);
}

[Fact] // 101 est l'effacement : la ligne cesse de revendiquer une place
public void Compose_ClearsThePreferenceOn101()
{
    var card = Card("EMAIL;TYPE=INTERNET,PREF:a@b.c");
    var write = WriteWith(addresses: [new ContactWriteEmail(0, "a@b.c", "INTERNET", 101)]);

    var output = VCardComposer.Compose(card, Uid, write);

    Assert.Equal(101, VCardProjector.Project(output).Addresses.Single().Line.Pref);
}

[Fact] // null laisse la carte tranquille — la règle de toutes les écritures qui ne nomment pas
public void Compose_LeavesThePreferenceAloneWhenTheWriteIsSilent()
{
    var card = Card("EMAIL;TYPE=INTERNET,PREF:a@b.c");
    var write = WriteWith(addresses: [new ContactWriteEmail(0, "a@b.c", "INTERNET")]);

    var output = VCardComposer.Compose(card, Uid, write);

    Assert.Equal(1, VCardProjector.Project(output).Addresses.Single().Line.Pref);
}

[Fact] // le jeton PREF dans le champ type reste ignoré : c'est Pref qui parle, pas TYPE
public void Compose_StillIgnoresAPrefTokenInTheTypeField()
{
    var card = Card("EMAIL;TYPE=INTERNET:a@b.c");
    var write = WriteWith(addresses: [new ContactWriteEmail(0, "a@b.c", "INTERNET,PREF")]);

    var output = VCardComposer.Compose(card, Uid, write);

    Assert.Equal(101, VCardProjector.Project(output).Addresses.Single().Line.Pref);
}

[Fact] // la même mécanique sur une adresse postale
public void Compose_PosesThePreferenceOnAPostalAddress()
{
    var card = Card("ADR;TYPE=HOME:;;Rue X 1;Namur;;5000;BE");
    var write = WriteWith(postalAddresses:
        [new ContactWriteAddress(0, "HOME", null, null, "Rue X 1", "Namur", null, "5000", "BE", 1)]);

    var output = VCardComposer.Compose(card, Uid, write);

    Assert.Equal(1, VCardProjector.Project(output).PostalAddresses.Single().Line.Pref);
}
```

Ces tests passent par le projecteur plutôt que par une sous-chaîne : la bibliothèque fusionne et réordonne les blocs de paramètres, donc une assertion octet à octet sur `TYPE=` est impossible par construction (résidus de 4a).

- [ ] **Step 2 : Les faire échouer**

`cd src && dotnet test snoopy.microservice/snoopy.microservice.Tests --filter "FullyQualifiedName~VCardComposerTests.Compose_Poses|FullyQualifiedName~VCardComposerTests.Compose_Clears|FullyQualifiedName~VCardComposerTests.Compose_Leaves|FullyQualifiedName~VCardComposerTests.Compose_Still"`

Attendu : erreur de compilation d'abord (`ContactWriteEmail` n'accepte pas 4 arguments). Ajouter **uniquement** les champs `Pref` aux records de `ContactWrite.cs`, relancer, et obtenir des échecs d'assertion — pas des erreurs.

- [ ] **Step 3 : Poser la préférence dans le composeur**

Dans `VCardComposer.cs`, un helper à côté d'`ApplyType` :

```csharp
// Le seul endroit où une écriture atteint PREF. ApplyType n'y touche pas — il retire le jeton du
// bloc TYPE — pour que la valeur ait une porte unique. 101 est l'effacement, la valeur que le
// projecteur donne à une ligne dont la carte ne dit rien (décision 5 bis de 4a).
private const int NoPreference = 100;

private static void ApplyPreference(ParameterSection parameters, int? pref)
{
    if (pref == null) return;
    parameters.Preference = pref.Value >= 101 ? NoPreference : Math.Clamp(pref.Value, 1, 100);
    if (pref.Value < 101) return;
    // La bibliothèque n'émet rien pour la valeur par défaut, mais un PREF venu du bloc de
    // paramètres bruts survivrait à l'écriture : il faut aussi le retirer de là.
    parameters.NonStandard = (parameters.NonStandard ?? [])
        .Where(p => !p.Key.Equals("PREF", StringComparison.OrdinalIgnoreCase))
        .ToList() is { Count: > 0 } kept ? kept : null;
}
```

Puis l'appeler :

```csharp
private static TextProperty TextLine(string value, string type, TextProperty? old, Family family, int? pref)
{
    var replaced = new TextProperty(value, old?.Group);
    if (old != null) replaced.Parameters.Assign(old.Parameters);
    ApplyType(replaced.Parameters, family, type);
    ApplyPreference(replaced.Parameters, pref);
    return replaced;
}
```

Les trois appelants de `TextLine` passent `l.Pref` (e-mails, téléphones) ; `PostalLine` termine par `ApplyPreference(replaced.Parameters, line.Pref)`.

- [ ] **Step 4 : Vérifier le vert, et l'hypothèse sur la bibliothèque**

`cd src && dotnet test snoopy.microservice/snoopy.microservice.Tests --filter "FullyQualifiedName~VCardComposerTests"`

Si `Compose_ClearsThePreferenceOn101` reste rouge, c'est que FolkerKinzel 8.2.0 émet quand même quelque chose pour `Preference = 100` : ajouter alors dans `ApplyType`, à la liste `kept`, le même filtre sur la clé `PREF` — et **le dire dans le commentaire du helper**, parce que c'est un comportement mesuré du paquet, pas une déduction.

- [ ] **Step 5 : Le validateur transporte le champ**

Dans `ContactValidator.Validate`, les trois projections de payload vers `ContactWrite*` passent `payload.Pref`. Une seule règle neuve, avec son test dans `ContactValidatorTests.cs` :

```csharp
[Fact] // hors de 1..101 la préférence n'a pas de sens : ni un PREF vCard, ni notre effacement
public void Validate_RefusesAPreferenceOutOfRange()
{
    var result = ContactValidator.Validate(new ContactRequest
    {
        FirstName = "Ana",
        Addresses = [new ContactEmailPayload { Address = "a@b.c", Pref = 0 }],
    });

    Assert.True(result.IsFailure);
}
```

Le message suit la forme des voisins : `$"A preference must be between 1 and 101"`.

- [ ] **Step 6 : Suite complète, build sans avertissement, commit**

```bash
cd src && dotnet build -warnaserror && dotnet test
cd .. && git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add -A && git commit -F - <<'MSG'
feat(contacts): une écriture peut enfin nommer la préférence d'une ligne

pref traverse les payloads jusqu'à Preference ; 101 efface, null laisse la
carte tranquille. Seul changement backend de 4b.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
MSG
```

---

### Task 2 : le brouillon fidèle — positions et nom affiché rendus

Aucun champ visible n'est ajouté. À la fin de cette tâche l'écran est **identique** et les deux pertes de données de la spec sont fermées. C'est ce qui la rend indépendamment livrable, et c'est aussi pourquoi elle vient avant les familles : tout le reste s'appuie sur ce brouillon.

**Files :**
- Modify : `src/frontend/src/modules/contacts/contactTypes.ts`
- Modify : `src/frontend/src/modules/contacts/ContactsLayout.tsx`
- Modify : `src/frontend/src/modules/contacts/ContactEditView.tsx`
- Modify : `src/frontend/src/modules/contacts/useCaptureContacts.ts`
- Modify : `src/frontend/src/locales/{en,fr}/contacts.json`
- Test : `src/frontend/src/modules/contacts/ContactEditView.test.tsx`
- Test : `src/frontend/src/modules/contacts/ContactsLayout.test.tsx`

**Interfaces (produit pour les tâches 3 et 4) :**

```ts
export interface ContactDraftEmail {
  /** The card rank this line replaces; null for a line the user just added. */
  position: number | null
  address: string
  type: string
  /** 1 on the primary, 101 to clear it, null to leave the card's own alone. */
  pref: number | null
}

export interface ContactDraftPhone {
  position: number | null
  number: string
  type: string
}

export interface ContactDraftPostal {
  position: number | null
  type: string
  poBox: string | null
  extended: string | null
  street: string | null
  locality: string | null
  region: string | null
  postalCode: string | null
  country: string | null
}

export interface ContactDraft {
  firstName: string | null
  lastName: string | null
  nickname: string | null
  displayName: string | null
  middleName: string | null
  namePrefix: string | null
  nameSuffix: string | null
  organization: string | null
  department: string | null
  jobTitle: string | null
  birthday: string | null
  website: string | null
  notes: string | null
  isFavorite: boolean
  addresses: ContactDraftEmail[]
  /** Omitted by every producer but the editor: absent means the card keeps its own. */
  phones?: ContactDraftPhone[]
  postalAddresses?: ContactDraftPostal[]
  source?: 'captured'
}
```

`ContactEditView` change de prop : `contact: ContactDetail | null` (null en création). La vue liste ne porte ni position, ni type, ni les neuf scalaires — elle ne peut plus amorcer le formulaire.

- [ ] **Step 1 : Écrire les tests, rouges**

Dans `ContactEditView.test.tsx`, remplacer les fixtures `Contact` par des `ContactDetail` — par exemple :

```tsx
const bruno: ContactDetail = {
  id: 'b', firstName: 'Bruno', lastName: 'Mertens', nickname: 'bru',
  displayName: 'Dr. Bruno Mertens', isFavorite: false, hasPhoto: false,
  addresses: [
    { position: 0, address: 'bruno@x.be', type: 'INTERNET', pref: 101, params: '', groupName: 'item1' },
    { position: 3, address: 'b.mertens@wk.be', type: 'WORK', pref: 1, params: '', groupName: '' },
  ],
  phones: [], postalAddresses: [],
}
```

La position `3` sur la seconde ligne n'est pas décorative : elle prouve que le brouillon transporte le rang de la carte et ne le recalcule pas depuis l'index du tableau.

Puis les trois tests neufs :

```tsx
it('renvoie la position de chaque ligne amorcée, et null pour une ligne neuve', async () => {
  const { onSave } = setup({ contact: bruno })
  await userEvent.click(screen.getByRole('button', { name: /add an address/i }))
  await userEvent.type(screen.getByLabelText(/address 3/i), 'troisieme@x.be')
  await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

  expect(onSave.mock.calls[0][0].addresses).toEqual([
    { position: 0, address: 'bruno@x.be', type: 'INTERNET', pref: 1 },
    { position: 3, address: 'b.mertens@wk.be', type: 'WORK', pref: 101 },
    { position: null, address: 'troisieme@x.be', type: '', pref: 101 },
  ])
})

it('renvoie le nom affiché que la carte porte, sans y toucher', async () => {
  const { onSave } = setup({ contact: bruno })
  await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

  expect(onSave.mock.calls[0][0].displayName).toBe('Dr. Bruno Mertens')
})

it('rendre principale pose pref et ne réordonne pas la liste', async () => {
  const { onSave } = setup({ contact: bruno })
  await userEvent.click(screen.getByRole('button', { name: /make this the primary/i }))
  await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

  const sent = onSave.mock.calls[0][0].addresses
  expect(sent.map((a: ContactDraftEmail) => a.address))
    .toEqual(['bruno@x.be', 'b.mertens@wk.be'])
  expect(sent.map((a: ContactDraftEmail) => a.pref)).toEqual([101, 1])
})
```

Le premier test dit aussi ce qu'est le `pref` par défaut : la première ligne de la liste est la principale, les autres portent `101`. Le troisième prouve que le bouton cesse de déplacer.

- [ ] **Step 2 : Les faire échouer**

`cd src/frontend && npm test -- ContactEditView`

Attendu : erreurs de type sur les fixtures, puis échecs d'assertion. Corriger les types avant de regarder les assertions.

- [ ] **Step 3 : Le brouillon et l'amorçage**

Dans `contactTypes.ts`, poser les quatre interfaces du bloc **Interfaces** ci-dessus.

Dans `ContactEditView.tsx`, l'état des adresses cesse d'être `string[]` :

```tsx
const [addresses, setAddresses] = useState<ContactDraftEmail[]>(
  contact && contact.addresses.length > 0
    ? contact.addresses.map(line => ({
        position: line.position, address: line.address, type: line.type, pref: null,
      }))
    : [{ position: null, address: '', type: '', pref: null }])
```

`makePrimary(index)` remplace `moveUp` : il ne déplace rien, il pose l'index choisi en tête de préférence.

```tsx
// La préférence est une propriété de la ligne, pas son rang : déplacer la ligne ne changerait
// plus rien depuis que le composeur la remet à sa position (décision 5).
function makePrimary(index: number) {
  setAddresses(previous => previous.map((line, i) => ({ ...line, pref: i === index ? 1 : 101 })))
}
```

À la soumission, les lignes vides tombent et la principale se résout — la première ligne survivante si l'utilisateur n'a rien choisi :

```tsx
const kept = addresses.filter(line => line.address.trim() !== '')
const chosen = kept.findIndex(line => line.pref === 1)
const primary = chosen >= 0 ? chosen : 0
onSave({
  …,
  displayName: blank(displayName),
  addresses: kept.map((line, i) => ({
    position: line.position,
    address: line.address.trim(),
    type: line.type,
    pref: i === primary ? 1 : 101,
  })),
})
```

`displayName` est un état amorcé de `contact?.displayName ?? ''` et **non affiché** en tâche 2 : il n'a pas encore de champ, il est simplement renvoyé tel quel. La tâche 4 lui donne le sien.

Les neuf autres scalaires suivent la même règle : amorcés depuis le détail, renvoyés inchangés, pas encore affichés. C'est ce qui rend l'écran identique tout en fermant les pertes.

- [ ] **Step 4 : La porte du détail**

Dans `ContactsLayout.tsx` :

```tsx
const { data: detail, isLoading: detailLoading } = useContact(routeId ?? null)
// Le formulaire s'amorce depuis son contact une seule fois, au montage : il attend donc le détail
// comme il attendait déjà le carnet, sans quoi les positions arriveraient après la seed.
const editorReady = (!routeId || (contacts != null && detail != null)) && !missing
```

et le rendu passe `contact={detail ?? null}`. La ligne `{!editorReady && isLoading && …}` devient `{!editorReady && (isLoading || detailLoading) && …}`.

Un test dans `ContactsLayout.test.tsx` :

```tsx
it("n'amorce pas l'éditeur avant que le détail soit là", async () => {
  // détail en vol : le carnet a répondu, l'éditeur ne doit pas encore être monté
  renderLayout({ route: '/contacts/b/edit', detail: 'pending' })

  expect(screen.queryByLabelText(/first name/i)).not.toBeInTheDocument()
  expect(screen.getByText(/loading contacts/i)).toBeInTheDocument()
})
```

- [ ] **Step 5 : La capture**

`useCaptureContacts.ts` envoie encore une chaîne nue. Une ligne :

```tsx
addresses: [{ position: null, address: candidate.address, type: '', pref: null }],
```

Elle continue de **ne pas** nommer `phones` ni `postalAddresses` : elle ne crée que des contacts neufs, et la règle « absent = la carte garde les siens » la protège si cela change.

- [ ] **Step 6 : Vert, types, commit**

```bash
cd src/frontend && npm test && npx tsc --noEmit
cd ../.. && git add -A && git commit -F - <<'MSG'
fix(contacts): l'éditeur rend les positions et le nom affiché

Sans elles chaque enregistrement reconstruisait les EMAIL, perdant groupes
et X-, et réécrivait le FN depuis les seuls prénom et nom.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
MSG
```

---

### Task 3 : les deux familles répétables

**Files :**
- Modify : `src/frontend/src/modules/contacts/ContactEditView.tsx`
- Create : `src/frontend/src/modules/contacts/contactLineTypes.ts`
- Modify : `src/frontend/src/locales/{en,fr}/contacts.json`
- Modify : `src/frontend/src/index.css`
- Test : `src/frontend/src/modules/contacts/contactLineTypes.test.ts`
- Test : `src/frontend/src/modules/contacts/ContactEditView.test.tsx`

**Interfaces (produit pour la tâche 4) :** aucune — la tâche 4 n'utilise que ce que la tâche 2 a posé.

Le module neuf isole la seule règle métier de cette tâche, pour qu'elle se teste sans monter un formulaire :

```ts
/** The type tokens the editor offers, and the labels they wear. The table is the CSV exporter's,
    which is where the mapping between a vCard type and a human word already lives. */
export const PHONE_TYPES = ['CELL', 'HOME,VOICE', 'WORK,VOICE', 'HOME,FAX', 'WORK,FAX', 'VOICE'] as const
export const POSTAL_TYPES = ['HOME', 'WORK'] as const

/** The options one row offers: the known list, plus the row's own token when the card carries one
    we do not list. A type we cannot name is still a type the card holds — offering only the closest
    label would rewrite it on a save that never meant to touch it. */
export function typeOptions(known: readonly string[], current: string): string[]
```

- [ ] **Step 1 : Écrire les tests du module, rouges**

`contactLineTypes.test.ts` :

```ts
import { describe, expect, it } from 'vitest'
import { PHONE_TYPES, typeOptions } from './contactLineTypes'

describe('typeOptions', () => {
  it('offre la liste connue telle quelle', () => {
    expect(typeOptions(PHONE_TYPES, 'CELL')).toEqual([...PHONE_TYPES])
  })

  it('ajoute le type de la carte quand la liste ne le contient pas', () => {
    expect(typeOptions(PHONE_TYPES, 'OTHER')).toEqual([...PHONE_TYPES, 'OTHER'])
  })

  it('ignore la casse et les espaces avant de conclure à un inconnu', () => {
    expect(typeOptions(PHONE_TYPES, 'cell')).toEqual([...PHONE_TYPES])
  })

  it('une ligne neuve sans type ne fabrique pas une option vide', () => {
    expect(typeOptions(PHONE_TYPES, '')).toEqual([...PHONE_TYPES])
  })
})
```

- [ ] **Step 2 : Les faire échouer, puis écrire le module**

`cd src/frontend && npm test -- contactLineTypes` → « Cannot find module ». Puis :

```ts
export function typeOptions(known: readonly string[], current: string): string[] {
  const token = current.trim()
  if (token === '' || known.some(k => k.toUpperCase() === token.toUpperCase())) return [...known]
  return [...known, token]
}
```

- [ ] **Step 3 : Écrire les tests du formulaire, rouges**

Sur la fixture `bruno` de la tâche 2, augmentée :

```tsx
const withLines: ContactDetail = {
  ...bruno,
  phones: [
    { position: 0, number: '+32 493 82 44 15', type: 'CELL', pref: 101, params: '', groupName: '' },
    { position: 1, number: '+32 493 82 44 15', type: 'OTHER', pref: 101, params: '', groupName: '' },
  ],
  postalAddresses: [{
    position: 0, type: 'HOME,POSTAL', pref: 101, params: '', groupName: '',
    poBox: null, extended: null, street: 'Rue du Village 138',
    locality: 'Flémalle', region: 'Belgique', postalCode: '4400', country: 'Belgique',
  }],
}
```

```tsx
it('un type absent de la liste survit à un enregistrement qui ne touche pas sa ligne', async () => {
  const { onSave } = setup({ contact: withLines })
  await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

  expect(onSave.mock.calls[0][0].phones).toEqual([
    { position: 0, number: '+32 493 82 44 15', type: 'CELL' },
    { position: 1, number: '+32 493 82 44 15', type: 'OTHER' },
  ])
})

it('vider une famille envoie une liste vide, pas une omission', async () => {
  const { onSave } = setup({ contact: withLines })
  const bin = screen.getAllByRole('button', { name: /remove phone/i })
  await userEvent.click(bin[1])
  await userEvent.click(screen.getAllByRole('button', { name: /remove phone/i })[0])
  await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

  expect(onSave.mock.calls[0][0].phones).toEqual([])
})

it("une adresse postale sans aucune composante n'est pas envoyée, type ou pas", async () => {
  const { onSave } = setup({ contact: bruno })
  await userEvent.click(screen.getByRole('button', { name: /add a postal address/i }))
  await userEvent.selectOptions(screen.getByLabelText(/postal address 1 type/i), 'WORK')
  await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

  expect(onSave.mock.calls[0][0].postalAddresses).toEqual([])
})

it('au plafond, le bouton d’ajout de la famille disparaît', async () => {
  const many = { ...bruno, phones: Array.from({ length: 10 }, (_, i) => (
    { position: i, number: `+3247000000${i}`, type: 'CELL', pref: 101, params: '', groupName: '' })) }
  setup({ contact: many })

  expect(screen.queryByRole('button', { name: /add a phone/i })).not.toBeInTheDocument()
})
```

Le troisième test est le piège de la décision 6 : `ContactValidator.IsMeaningful(ContactWriteAddress)` renvoie vrai dès que le type est non vide, donc sans ce filtre côté éditeur, ouvrir un bloc puis changer d'avis poserait une `ADR` vide dans la carte.

- [ ] **Step 4 : Les faire échouer, puis bâtir les deux familles**

`cd src/frontend && npm test -- ContactEditView`

Puis, dans `ContactEditView.tsx`, deux blocs sur le patron de celui des adresses — une `.field-h` portant une liste de lignes et un bouton d'ajout. Points à ne pas manquer :

- l'état est `ContactDraftPhone[]` / `ContactDraftPostal[]`, amorcé depuis le détail comme les adresses ;
- chaque `<select>` est peuplé par `typeOptions(PHONE_TYPES, line.type)` et porte un `<label>` visuellement caché — `Phone {{index}} type`, `Postal address {{index}} type` ;
- l'adresse postale rend ses composantes sur trois lignes : rue seule ; code postal + localité ; région + pays ;
- le bouton d'ajout n'est rendu que sous le plafond ;
- **les filtres de soumission** :

```tsx
const keptPhones = phones
  .filter(line => line.number.trim() !== '')
  .map(line => ({ position: line.position, number: line.number.trim(), type: line.type }))

// Une adresse dont les sept composantes sont vides ne dit rien, quel que soit son type : le
// validateur la trouverait pourtant significative et poserait une ADR vide dans la carte.
const POSTAL_PARTS = ['poBox', 'extended', 'street', 'locality', 'region', 'postalCode', 'country'] as const
const keptPostal = postalAddresses
  .filter(line => POSTAL_PARTS.some(part => (line[part] ?? '').trim() !== ''))
  .map(line => ({
    ...line,
    ...Object.fromEntries(POSTAL_PARTS.map(part => [part, blank(line[part] ?? '')])),
  }))
```

- [ ] **Step 5 : i18n et CSS**

Clés neuves, dans les deux bundles (anglais donné, français à écrire en regard) : `editor.addPhone`, `editor.removePhone`, `editor.phoneType`, `editor.phonePlaceholder`, `editor.addPostal`, `editor.removePostal`, `editor.postalType`, et un libellé par composante (`editor.postal.street`, `.locality`, `.region`, `.postalCode`, `.country`, `.poBox`, `.extended`). Les libellés des types vivent sous `editor.types.*`, clés = jetons en minuscules avec `,` remplacé par `_` (`cell`, `home_voice`, `work_fax`…) ; un jeton inconnu s'affiche brut.

CSS : réutiliser `.contact-address-row` pour les téléphones ; une classe `.contact-postal-row` pour les trois lignes de composantes, en `display: grid` avec `gap`, jamais en marges par élément.

- [ ] **Step 6 : Vert, types, commit**

```bash
cd src/frontend && npm test && npx tsc --noEmit
cd ../.. && git add -A && git commit -F - <<'MSG'
feat(contacts): l'éditeur écrit téléphones et adresses postales

Un type que la carte porte et que notre liste ignore reste intact ; une
adresse sans composante n'est jamais envoyée.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
MSG
```

---

### Task 4 : les neuf scalaires et « + Ajouter un champ »

**Files :**
- Modify : `src/frontend/src/modules/contacts/ContactEditView.tsx`
- Modify : `src/frontend/src/locales/{en,fr}/contacts.json`
- Modify : `src/frontend/src/index.css`
- Test : `src/frontend/src/modules/contacts/ContactEditView.test.tsx`

Les neuf : nom affiché, 2e prénom, préfixe, suffixe, société, service, fonction, site, notes. L'anniversaire n'en est pas — il est toujours visible (décision 1), et son contrôle est décidé par la décision 7.

- [ ] **Step 1 : Écrire les tests, rouges**

```tsx
it('affiche d’office un champ que la carte remplit, et ne le propose pas au menu', async () => {
  setup({ contact: { ...bruno, organization: 'Weesky' } })

  expect(screen.getByLabelText(/organisation/i)).toHaveValue('Weesky')
  await userEvent.click(screen.getByRole('button', { name: /add a field/i }))
  expect(screen.queryByRole('menuitem', { name: /organisation/i })).not.toBeInTheDocument()
})

it('un champ ajouté depuis le menu devient saisissable et part à l’enregistrement', async () => {
  const { onSave } = setup({ contact: bruno })
  await userEvent.click(screen.getByRole('button', { name: /add a field/i }))
  await userEvent.click(screen.getByRole('menuitem', { name: /job title/i }))
  await userEvent.type(screen.getByLabelText(/job title/i), 'Ingénieure')
  await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

  expect(onSave.mock.calls[0][0].jobTitle).toBe('Ingénieure')
})

it('un champ vidé reste affiché tant que le formulaire vit', async () => {
  setup({ contact: { ...bruno, organization: 'Weesky' } })
  await userEvent.clear(screen.getByLabelText(/organisation/i))

  expect(screen.getByLabelText(/organisation/i)).toBeInTheDocument()
})

it('l’anniversaire accepte une forme que nul calendrier n’exprime', async () => {
  const { onSave } = setup({ contact: bruno })
  await userEvent.type(screen.getByLabelText(/birthday/i), '--10-27')
  await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

  expect(onSave.mock.calls[0][0].birthday).toBe('--10-27')
})
```

Le troisième test épingle la décision 1 : retirer le champ sous les doigts de qui vient d'effacer une faute de frappe serait pire que la ligne vide.

- [ ] **Step 2 : Les faire échouer**

`cd src/frontend && npm test -- ContactEditView`

- [ ] **Step 3 : Le registre des champs facultatifs**

Un tableau local plutôt que neuf blocs copiés — c'est ce qui rend le menu et le rendu incapables de diverger :

```tsx
// The nine a card may carry and most contacts do not. A field the card fills is always rendered
// and never offered here: the menu hides emptiness, never content.
const OPTIONAL: { key: OptionalKey; label: string; long?: boolean }[] = [
  { key: 'displayName', label: 'editor.displayName' },
  { key: 'middleName', label: 'editor.middleName' },
  { key: 'namePrefix', label: 'editor.namePrefix' },
  { key: 'nameSuffix', label: 'editor.nameSuffix' },
  { key: 'organization', label: 'fields.organization' },
  { key: 'department', label: 'fields.department' },
  { key: 'jobTitle', label: 'fields.jobTitle' },
  { key: 'website', label: 'fields.website' },
  { key: 'notes', label: 'fields.notes', long: true },
]
```

L'état : un objet `scalars: Record<OptionalKey, string>` amorcé depuis le détail, et un `Set<OptionalKey>` des champs **révélés** — initialisé aux clés que le détail remplit, augmenté par le menu, jamais réduit. Le menu propose `OPTIONAL.filter(f => !revealed.has(f.key))` et disparaît quand il ne reste rien.

`notes` est un `<textarea>` ; les huit autres des `<input type="text">` avec le `maxLength` de leur colonne : 100 pour `displayName`/`middleName`, 50 pour préfixe et suffixe, 255 pour société/service/fonction, 512 pour le site, 16000 pour les notes.

- [ ] **Step 4 : L'anniversaire**

Un `<input type="text">`, jamais `type="date"` — la décision 7 exige quatre formes dont trois qu'un calendrier ne sait pas exprimer. Le placeholder porte l'exemple (`27/10/1979`), et la valeur part **telle que tapée** : la normalisation est le travail du composeur, qui ré-impose la forme textuelle de toute façon (décision 11 de 4a).

- [ ] **Step 5 : i18n, CSS, parité**

Clés neuves : `editor.addField`, `editor.displayName`, `editor.middleName`, `editor.namePrefix`, `editor.nameSuffix`, `editor.birthdayPlaceholder`. `fields.organization`, `fields.department`, `fields.jobTitle`, `fields.website`, `fields.notes` existent déjà et sont réutilisées — ne pas en créer de doubles sous `editor.`.

CSS : le menu réutilise `DropdownMenu` du dossier `components/`, comme la fiche le fait déjà pour ses actions ; pas de composant de menu neuf.

Vérifier la parité : `cd src/frontend && npm test -- i18n`.

- [ ] **Step 6 : Vert, types, commit**

```bash
cd src/frontend && npm test && npx tsc --noEmit
cd ../.. && git add -A && git commit -F - <<'MSG'
feat(contacts): les neuf scalaires, derrière un menu qui ne cache que du vide

Un champ que la carte remplit est toujours affiché ; l'anniversaire reste
un champ texte, seul capable des quatre formes qu'une vCard admet.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
MSG
```

---

## Vérification de fin de tranche

Après la tâche 4, avant toute revue de branche :

1. `cd src && dotnet build -warnaserror && dotnet test` — zéro avertissement, zéro échec.
2. `cd src/frontend && npm test && npx tsc --noEmit && npm run build`.
3. `git checkout -- src/snoopy.microservice/ApiDocumentation.xml` puis `git status` propre.
4. **Sur `snoopy-dev`, une fois déployé** : ouvrir la fiche `0bdef6f7-855d-4b58-bb7b-dcf215a04917` (Aurélie Etienne, importée d'un vrai `.vcf`), l'éditer sans rien changer, enregistrer, puis relire `GET /api/Contacts/{id}` — les deux téléphones, leurs types `CELL` et `OTHER`, l'adresse postale et son type `HOME,POSTAL` doivent être identiques au caractère près. C'est la seule vérification qui prouve la décision 4 sur des données que nous n'avons pas fabriquées.

## Self-review (fait à l'écriture du plan)

**Couverture de la spec** — décision 1 → tâches 3 et 4 ; décision 2 → tâche 2 step 4 ; décision 3 → tâche 2 ; décision 4 → tâche 3 ; décision 5 → tâche 1 ; décision 6 → tâches 2 (`[]` jamais omis) et 3 (filtres de soumission) ; décision 7 → tâche 4 step 4 ; décision 8 → tâche 3 step 3 ; décision 9 → rien à implémenter, c'est une propriété du composeur que la tâche 2 préserve en rendant les positions, et la vérification de fin de tranche l'éprouve sur données réelles.

**Placeholders** — aucun « TBD », aucun « similaire à la tâche N » : les fixtures sont réécrites à chaque endroit où elles servent.

**Cohérence des types** — `ContactDraftEmail`/`Phone`/`Postal` sont définis une fois en tâche 2 et utilisés tels quels en 3 et 4 ; `typeOptions(known, current)` est défini en tâche 3 et n'est appelé qu'y ; `Pref` est ajouté en dernière position optionnelle sur les trois records C#, ce qui laisse compiler les sites de construction que la tâche 1 ne touche pas.

**Un risque nommé** — la tâche 1 step 4 dépend d'un comportement de FolkerKinzel 8.2.0 (`Preference = 100` n'émet rien) qui n'a pas été mesuré à l'écriture du plan. Le step donne la branche de repli et exige que le résultat mesuré soit écrit en commentaire, jamais déduit.

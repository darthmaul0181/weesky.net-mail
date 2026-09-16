# Tranche 2b1 — Drapeaux (lu/non-lu, suivi) — design

Date : 2026-07-22. Maquettes validées : variante B2 (étoile haut-droite, cluster bas-droite)
et planche d'icônes 16 px, dans `.superpowers/brainstorm/13128-1784734739/content/`.

## 1. Découpage de la tranche 2b

La tranche 2b (« un webmail où l'on organise ») est trop large pour un seul spec. Découpage
validé, chaque sous-tranche avec son cycle spec → plan → implémentation :

| Sous-tranche | Contenu |
|---|---|
| **2b1** (ce document) | Drapeaux : lu/non-lu (`\Seen`), suivi (`\Flagged`) — la première écriture IMAP du projet |
| 2b2 | Actions de message : corbeille, archive, indésirable, déplacer/copier — kebab et cluster de ligne |
| 2b3 | Sélection multiple + barre d'actions groupées |
| 2b4 | Recherche IMAP |

## 2. Décisions validées

| Sujet | Décision |
|---|---|
| Marquage lu | **À l'ouverture** du message dans le volet de lecture (comportement Gmail). Pas de délai, pas de réglage |
| Qui marque lu | **Le client** : `GetMessage` reste pur (MailKit lit en `BODY.PEEK[]`), le frontend déclenche la mutation quand le détail arrive |
| Surfaces UI | Lignes de la liste (étoile + cluster au survol) **et** menu kebab du lecteur — ses deux premières entrées |
| Métaphore du suivi | **Étoile** « Star / Unstar » (convention Gmail), ambre quand posée |
| API | **Un endpoint batch** dès 2b1 — prêt pour la sélection multiple de 2b3 sans retouche |
| Caches | **Mutations optimistes** avec rollback ; jamais d'`invalidateQueries` sur le stream (règle établie : rejouer N blocs = N connexions IMAP) |
| Disposition ligne | **B2** : étoile fixe en haut à droite (après la date), cluster d'actions en bas à droite au survol — extensible pour archiver/supprimer en 2b2 |
| Icônes | Style Feather du site (stroke 2), **16 px** dans une zone cliquable 26×26. Enveloppe **ouverte** = Mark as read, **fermée** = Mark as unread — l'icône montre l'action à venir, comme le toggle soleil/lune |

## 3. Backend

### Endpoint

`PUT /api/Mail/Messages/Flags` — le chemin du dossier voyage dans le body, jamais en segment
de route (règle 3 : le séparateur peut être `/`).

```csharp
public sealed record SetMessageFlagsRequest(
    string FolderPath,
    IReadOnlyList<uint> Uids,   // 1 à 200 éléments — le plafond de pageSize
    MailFlag Flag,              // enum : Seen | Flagged, sérialisé en chaîne JSON
    bool Value);                // true = poser, false = retirer
```

- **Validation** → 400 : dossier vide, `Uids` vide ou > 200. Enum invalide → 400 par le
  binding JSON.
- **Réponses** : 204 succès, 401 `credentials_unavailable`, 502 refus IMAP — le patron exact
  de `SetFolderSubscription` (`FromResult` non générique, `successStatusCode: 204`).

### ImapSession

`SetFlagsAsync(folderPath, uids, flag, value, ct)` :

- Ouvre le dossier en **ReadWrite** — la première ouverture en écriture du projet ; les
  lectures existantes restent en ReadOnly.
- `AddFlagsAsync(uids, messageFlags, silent: true)` quand `value` est vrai,
  `RemoveFlagsAsync(...)` sinon. `silent: true` : les untagged FETCH de retour ne servent à
  rien ici.
- Mappe `MailFlag.Seen` → `MessageFlags.Seen`, `MailFlag.Flagged` → `MessageFlags.Flagged`.
- Un UID inexistant est un no-op IMAP silencieux — pas de 404 : le batch réussit ou échoue
  en bloc, jamais partiellement.

### Ce qui ne bouge pas

- **Aucune garde `uidValidity`** dans la requête : au pire un UID recyclé entre deux polls
  reçoit un drapeau à tort — le risque de tout client IMAP, recalé par le poll dans la
  minute. Une garde coûterait un STATUS par clic pour un cas d'école.
- `GetMessage` reste en peek. Aucun changement de modèle : `Seen`/`Flagged` sont déjà
  exposés sur `MailMessageSummary`.

## 4. Frontend — couche de données

- **`api.js`** : `setMessageFlags(folderPath, uids, flag, value)` → `PUT /api/Mail/Messages/Flags`.
- **`list/flagPatch.ts`** — pur, testable seul :
  - `patchSummaries(messages, uids, flag, value)` → les listes réécrites **et le delta de
    non-lus réellement produit** (re-marquer lu un message déjà lu compte zéro) ;
  - `patchFolderUnread(tree, folderPath, delta)` → l'arbre avec le compteur `unread` du
    dossier ajusté, borné à ≥ 0, les autres nœuds intacts.
- **`queries.ts` : `useSetFlags`** — mutation TanStack optimiste :
  - `onMutate` : snapshot puis `setQueryData` sur **trois** caches — les pages
    `mailKeys.messages` du dossier, les blocs `mailKeys.messageStream`, et
    `mailKeys.folders` pour le compteur non-lus quand le drapeau est `seen` ;
  - `onError` : restauration des snapshots + toast « Could not update the message » ;
  - pas d'invalidation en `onSettled` : le poll 60 s et `highestModSeq` sont le mécanisme
    de vérité existant.
- **`reader/useMarkSeenOnOpen(folderPath, uid, detailLoaded)`** :
  - se déclenche **une fois par ouverture** — armé au changement d'`uid`, tiré quand le
    détail arrive ;
  - ne tire que si le résumé en cache dit `seen: false`, **ou** si aucun résumé n'est en
    cache (lien profond) ;
  - le patch optimiste posant `seen: true`, il ne peut pas se re-déclencher ; « Mark as
    unread » ne re-marque pas lu tant que l'uid ne change pas ;
  - échec **silencieux** (pas de toast) : un marquage lu raté se corrige au poll suivant.

## 5. Frontend — UI

### Structure de ligne

La ligne entière est aujourd'hui un `<button>` ; un bouton imbriqué est du HTML invalide.
La ligne devient un `<div role="button" tabIndex={0}>` (Enter/Espace au clavier — le
comportement actuel est préservé, les tests s'adaptent). L'étoile et le cluster sont de
vrais `<button>` enfants avec `stopPropagation`.

### L'étoile

- Toujours visible, ferme la première ligne (skin étroite) ou la ligne unique (skin large),
  après la date.
- `StarIcon` (prop `filled`) : contour `--text-muted` au repos, **pleine ambre** posée — un
  token existant (`--badge-count-bg`), aucune couleur littérale.
- `aria-label` : `Star` / `Unstar`.

### Le cluster d'actions

- `.message-row-cluster` : visible au survol **et** au focus clavier (`:focus-within`).
- Skin étroite : absolu bas-droite, posé sur la ligne d'aperçu — ou celle du sujet quand
  l'aperçu est désactivé. Fond `--surface-sunken` pour rester lisible sur la ligne survolée.
- Skin large (une seule ligne, pas de « bas ») : le cluster **remplace la date** au survol,
  l'étoile reste fixe au bout.
- En 2b1 il porte un seul bouton : le toggle lu/non-lu — `MailOpenIcon` (« Mark as read »)
  sur un non-lu, `MailIcon` fermée (« Mark as unread ») sur un lu. 2b2 y ajoutera archive
  et corbeille.

### Le kebab du lecteur

- Nouveau composant générique **`components/DropdownMenu.tsx`** — le pattern d'`AvatarMenu` :
  ouvert au clic, fermé au clic extérieur (`mousedown`) et à Escape. Générique parce que
  2b2 y ajoutera ses entrées ; un second menu bespoke créerait deux dialectes.
- Deux entrées en 2b1, icône + libellé, calculées depuis le résumé en cache :
  `Mark as unread` / `Mark as read`, et `Star` / `Unstar`. Chaque entrée ferme le menu et
  déclenche la même mutation `useSetFlags` que la liste.
- Sans résumé en cache (lien profond), les libellés suivent l'état optimiste connu — par
  défaut `Mark as unread`, puisque l'ouverture vient de marquer lu.

### Icônes nouvelles

`StarIcon` (prop `filled`) — style Feather (viewBox 24, stroke 2, `currentColor`).
`MailOpenIcon` — dessinée dans le style exact de `MailIcon` (viewBox 20, stroke 1.6) :
les deux enveloppes se remplacent au même endroit selon l'état, un saut de graisse de
trait se verrait. Un fichier par icône ; rendu à 16 px dans les lignes. Le trombone
(13 px) n'est pas touché.

## 6. Tests

**Backend** (xUnit, patrons des suites existantes) :
- `MailControllerTests.SetMessageFlags` : 400 sans dossier, 400 sur `Uids` vide ou > 200,
  401 sans credentials, 204 sur succès, 502 sur refus IMAP.
- `ImapSessionTests.SetFlagsAsync` : mapping `Seen`/`Flagged` → `MessageFlags`,
  `AddFlagsAsync` si `value`, `RemoveFlagsAsync` sinon, `silent: true`, ouverture
  ReadWrite ; échec d'ouverture → `Result.Failure`.

**Frontend** (Vitest, fichiers à côté de ce qu'ils testent) :
- `flagPatch.test.ts` — les bons UIDs réécrits, delta non-lus juste (déjà-lu re-marqué
  lu = 0) ; arbre : bon dossier, borne ≥ 0, autres nœuds intacts.
- Mutation `useSetFlags` — l'optimiste patche pages, blocs et arbre ; l'erreur restaure
  les trois snapshots ; **jamais d'`invalidateQueries` sur le stream**.
- `MessageList.test.tsx` — ligne `role="button"` clavier-navigable ; l'étoile bascule sans
  ouvrir le message ; cluster atteignable au focus ; `aria-label` selon l'état.
- `useMarkSeenOnOpen.test.tsx` — tire une fois quand le détail arrive sur un non-lu ; pas
  sur un lu ; pas après « Mark as unread » tant que l'uid ne change pas ; tire sur lien
  profond sans résumé.
- `DropdownMenu.test.tsx` — clic ouvre, clic extérieur et Escape ferment, une entrée
  cliquée ferme et agit.
- `MessageReader.test.tsx` — le kebab ouvre le menu, libellés selon l'état du résumé.

## 7. Hors périmètre

- Corbeille, archive, indésirable, déplacer/copier — 2b2.
- Sélection multiple, actions groupées — 2b3.
- Recherche — 2b4.
- `\Answered` en écriture (répondre est en 2c ; le drapeau est déjà lu et affiché).
- Tout réglage utilisateur nouveau — le marquage lu à l'ouverture n'est pas configurable.

# Tranche 2b3 — Multi-sélection des messages (webmail) — design

Date : 2026-07-23. 2b1 (drapeaux) et 2b2 (actions : corbeille, archive, indésirable,
déplacer/copier, suppression définitive) sont livrées. Ce document couvre **2b3 en
entier** : la sélection multiple dans la liste et l'application des actions à un lot, plus
une action de dossier neuve — **vider un dossier**. Reste ensuite 2b4 (recherche). 2b3
débloque aussi le drag & drop mis en earmark (`2026-07-23-drag-drop-messages-earmark.md`),
qui glisse la sélection courante.

## 1. Place dans le découpage

La couche de données de 2b2 **batche déjà** : `useSetFlags`, `useMoveMessages`,
`useDeleteMessages` prennent tous `uids: number[]` (plafond backend de 200). 2b3 est donc
essentiellement une **couche de sélection dans l'UI** posée par-dessus, plus une **barre
d'actions groupées** — aucune nouvelle plomberie côté messages. La seule capacité backend
neuve est **vider un dossier** (§4), non bornée par la sélection ni par le plafond 200.

Deux concepts vivent désormais côte à côte et restent **indépendants** :

- le **message ouvert** dans le lecteur (`?uid=…` dans les search params, inchangé) ;
- la **sélection cochée** — l'ensemble des lignes marquées pour une action groupée, état
  local de la liste (jamais dans l'URL : un `Set` d'UID dans la query string serait illisible
  et cassant).

Ouvrir un message ne touche pas les coches ; cocher n'ouvre rien.

## 2. Modèle d'interaction — cases à cocher, style Gmail

| Geste | Effet |
|---|---|
| Survol d'une ligne | La case à cocher apparaît (à gauche de la ligne). |
| ≥1 ligne cochée | Les cases deviennent **persistantes** sur toutes les lignes. |
| Clic sur la case | Coche/décoche cette ligne (`stopPropagation` — n'ouvre pas le message). |
| Clic ailleurs sur la ligne | Ouvre le message (comportement 2b2 inchangé). |
| **Shift-clic** sur une case | Sélectionne la **plage** depuis la dernière case cliquée (l'ancre) jusqu'à celle-ci, dans l'ordre des lignes chargées. |

La case par ligne existe dans **les deux skins** (deux lignes en volet droit, ligne simple
en volet bas/plein). Elle se place **à gauche** de la ligne. La géométrie du cluster de
survol et de la nouvelle colonne de case doit être **mesurée au navigateur** (jsdom ne fait
pas de layout) — la discipline 2b1/2b2 : quatre contrôles au survol en skin large ne doivent
pas serrer le sujet.

Accessibilité : chaque case porte un `aria-label` (« Select message from {sender} »), la case
maître « Select all », les boutons d'action leur libellé. Le compteur « N selected » est lu.

## 3. Barre d'actions permanente (le bandeau de titre)

Le `<h2 class="message-list-heading">` actuel devient un composant **`SelectionToolbar`**,
bande `flex: none` toujours présente en tête de la liste.

```
┌──────────────────────────────────────────────┐
│ ☐  Inbox            🗄  ⚠  🗑  ↪      ⋮        │  rien coché → 🗄⚠🗑↪ grisés
│ ☑  3 selected       🗄  ⚠  🗑  ↪      ⋮        │  ≥1 coché → actifs
├──────────────────────────────────────────────┤
```

- **Case maître** à gauche : coche/décoche **les lignes chargées** (portée = page courante en
  mode paginé, blocs déjà streamés en mode « All »). État **indéterminé** quand la sélection
  est partielle. Tout coché puis re-cliqué = vider la sélection.
- **Titre du dossier** (`Inbox`, via `roleLabel` — le seul endroit nommant le rôle du dossier)
  → remplacé par « N selected » quand la sélection est active, et revient dès qu'on vide.
- **Actions directes** (grisées quand sélection vide, actives sinon) : **Archive** 🗄 ·
  **Report as junk** ⚠ · **Delete** 🗑 · **Move to…** ↪.
- **Kebab ⋮** : **Mark as read** · **Mark as unread** · **Copy to…** (dépendent de la
  sélection, grisés si vide) — séparateur — **Empty folder** (indépendant de la sélection, §4).

**Conscience des rôles** (mêmes règles que le cluster de ligne et le lecteur en 2b2) :
Archive désactivée dans le dossier Archive ; Report as junk désactivée dans Junk ; Delete
dans la corbeille = **purge définitive** derrière `DeleteConfirmModal`, ailleurs = déplacement
vers la corbeille sans confirm. Une action désactivée porte sa raison en infobulle, jamais
retirée. `rolePathsOf(folders)` est déjà mémoïsé dans `MessageList`.

## 4. Actions groupées — réutilisent la plomberie 2b2

Chaque action câble le `Set<uid>` (matérialisé en `number[]`) sur un hook **existant** :

| Action | Hook / chemin |
|---|---|
| Archive | `useMoveMessages` → rôle archive |
| Report as junk | `useMoveMessages` → rôle junk |
| Delete (hors corbeille) | `useMoveMessages` → rôle trash (pas de confirm) |
| Delete (dans corbeille) | `useDeleteMessages` + `DeleteConfirmModal` (libellé **pluriel**) |
| Move to… / Copy to… | `MoveMessagesModal` (existant ; libellés pluriels déjà prêts en 2b2) |
| Mark as read / unread | `useSetFlags(uids, 'seen', value)` |

**Avance du lecteur** : si le message ouvert (`selectedUid`) appartient au lot agi, la liste
appelle `onDeparted(selectedUid)` (helper 2b2) et le lecteur passe au suivant ; sinon il reste.
Un lot peut faire partir plusieurs lignes, mais une seule est « ouverte » — un seul
`onDeparted` suffit.

**Vidage de la sélection après action** : les UID agis quittent la sélection (ils quittent
aussi les caches, donc la liste). Une sélection résiduelle ne pointe jamais sur une ligne
disparue.

## 5. « Empty folder » — action de dossier (nouveau backend)

De nature différente : **ignore la sélection**, agit sur **tout** le dossier, dépasse le
plafond 200. Deux points d'entrée, **un seul chemin de code** (`useEmptyFolder`) : l'entrée
kebab (tous dossiers) et la bande contextuelle (§6, corbeille + indésirables).

### Sémantique

| Dossier (rôle) | « Empty folder » fait | Confirm |
|---|---|---|
| Trash | **purge définitive** de tout le contenu | **Oui** (`DeleteConfirmModal`) |
| Junk | **purge définitive** de tout le contenu | **Oui** (`DeleteConfirmModal`) |
| Dossier normal | **déplace tout vers la corbeille** | Non (la corbeille est le filet, doctrine 2b2) |

Désactivée quand le dossier est déjà vide (`total = 0`). Si aucun dossier ne porte le rôle
trash, le mode « déplacer vers la corbeille » est indisponible (comme Delete en 2b2) →
l'entrée est désactivée avec sa raison.

### Backend

Nouvel endpoint **`POST /api/Mail/Folders/Empty`**, body `EmptyFolderRequest { FolderPath,
TargetFolderPath? }` (chemins dans le corps, jamais en segment de route) :

- **`TargetFolderPath` absent → purge** : `AddFlags(1:*, \Deleted)` puis `Expunge()`. Non
  borné, et **sans besoin d'UIDPLUS** — on expunge tout le dossier, pas un sous-ensemble, donc
  un `EXPUNGE` simple suffit (contrairement au `DELETE Messages` de 2b2 qui refuse sans
  UIDPLUS parce qu'il vise une liste d'UID).
- **`TargetFolderPath` présent → déplacement de masse** : `UID MOVE 1:*` vers la cible.
  Validations comme MoveOrCopy : source non vide, cible non vide, **cible ≠ source**, cible
  sélectionnable (détectée par la session, `TargetNotSelectable` → 400).

Statuts : 204 succès ; 400 (dossier manquant / cible = source / cible non sélectionnable) ;
401 (auth / credentials) ; 502 (serveur injoignable). Méthode `EmptyAsync` sur
`ImapSession` (derrière l'interface de session), tests contrôleur (validations) + session.

### Cache frontend (`useEmptyFolder`)

Optimiste, sur le patron de `useMoveMessages` (cancel → snapshot → drop → patch → rollback) :

- **Purge** : `cancelListQueries(source)` puis `dropFolderCaches(source)` (retire messages +
  stream), et l'arbre passe le dossier à `total = 0 / unread = 0`.
- **Déplacement** : idem source, **plus** la corbeille gagne `+total / +unread` **lus depuis
  le nœud d'arbre du dossier source** (déjà connus via `GET /Folders`). Pas de nouveaux UID
  insérés côté cible (pas de position calculable, doctrine 2b2).

`useEmptyFolder` **porte la clé d'écriture partagée `mailKeys.writes`** : vider bouge `total`
et `unread` sur (jusqu'à) deux dossiers — exactement le cas qui a mordu en 2b1 — donc
`useListRefresh` doit se mettre en veille pendant l'opération, comme pour les trois mutations
de 2b2. Test de veille vérifié par cassage. Le poll réconcilie ensuite (pas de compensation).

## 6. Bande contextuelle « vider » (corbeille + indésirables)

Raccourci découvrable, épinglé, **hors de la zone scrollable** — visible sur toutes les pages
et à tout niveau de scroll (une ligne dans le flux disparaîtrait au défilement). Composant
**`EmptyFolderBanner`**, bande `flex: none` **entre `SelectionToolbar` et la zone scrollable**,
rendue par `MessageList` **quand `folderRole ∈ {trash, junk}` et `total > 0`**.

```
┌──────────────────────────────────────────────┐
│ ☐  Trash              🗄  ⚠  🗑  ↪      ⋮      │  ← SelectionToolbar
├──────────────────────────────────────────────┤
│ 🗑  Emptying the trash permanently deletes      │  ← EmptyFolderBanner
│     these messages.         Empty trash now →  │     (trash/junk, total>0)
├──────────────────────────────────────────────┤
│  Alice    Subject…                            │  ← les mails scrollent dessous
```

- **Texte** : phrase orientée action + lien, adaptée au rôle. Elle décrit **l'effet du geste**,
  **jamais la rétention serveur** (certains serveurs purgent la corbeille au bout de X
  jours/mois — on ne contrôle pas ce fait, donc on ne l'affirme pas) :
  - Trash : « Emptying the trash permanently deletes these messages. » + lien « Empty trash now ».
  - Junk : « Emptying the junk folder permanently deletes these messages. » + lien « Empty junk now ».
- Le lien déclenche exactement la même purge + `DeleteConfirmModal` que le kebab « Empty
  folder » (`useEmptyFolder`, mode purge).
- Le kebab « Empty folder » reste l'accès **uniforme** à tous les dossiers, y compris —
  redondant mais cohérent — corbeille et indésirables.
- Distinct de Delete par construction : lien texte contextualisé par une phrase, pas une
  icône poubelle de plus ; « empty » + la phrase disent « tout le dossier », là où 🗑 dit
  « la sélection ».

## 7. Cycle de vie & cas limites

- **Vidage automatique de la sélection** : au **changement de dossier** et au **changement de
  page** (mode paginé). En mode « All » (stream), la sélection **persiste** au déroulé /
  chargement de blocs (les lignes cochées restent à l'écran) ; une ligne qui part (action ou
  poll) **quitte** la sélection.
- **> 200 lignes cochées** (possible en « All » très déroulé) : les quatre actions directes +
  Move/Copy se **désactivent** avec l'infobulle « Select 200 or fewer » (le plafond backend).
  « Empty folder » n'est pas concernée (elle ignore la sélection).
- **Escape** vide la sélection quand la liste a le focus et qu'une sélection est active. Pas de
  conflit : l'Escape du lecteur n'est lié qu'en mode `none` (2b2), et la liste y est masquée.
- **Mode `none`** (pas de split) : barre et bande vivent dans la liste ; pendant la lecture la
  liste est masquée, donc les actions groupées ne sont accessibles qu'en vue liste. Acceptable.

## 8. Découpage fichiers / testabilité

**Frontend**
- `list/useSelection.ts` — hook **pur** : `selected: Set<number>`, `toggle(uid, index)`,
  `toggleRange(index)` (de l'ancre à `index` sur les lignes chargées), `selectAllLoaded(uids)`,
  `clear()`, ancre de plage. Réinitialisé sur `folderPath` et page. Testé isolément (toggle,
  plage, ancre après clic non adjacent, indéterminé, all/clear).
- `list/SelectionToolbar.tsx` — le bandeau (case maître + indéterminée, compteur/titre,
  quatre boutons role-aware, kebab). Composant piloté par props. Testé isolément (états
  désactivés, compteur, kebab, « Empty folder » actif sans sélection, > 200).
- `list/EmptyFolderBanner.tsx` — la bande épinglée trash/junk. Testée (rend seulement
  trash/junk + `total > 0`, copie par rôle, ouvre le confirm).
- `list/MessageList.tsx` — câble `useSelection`, ajoute la case par ligne (deux skins),
  branche les actions groupées ; `<h2>` → `<SelectionToolbar>`, insère `<EmptyFolderBanner>`.
- `queries.ts` — `useEmptyFolder` (+ patch cache, clé `mailKeys.writes`) ; `api.js` — méthode
  `emptyFolder(folderPath, targetFolderPath?)`. Les autres actions réutilisent les hooks 2b2.

**Backend**
- `MailController.EmptyFolder` (endpoint + validations) ; DTO `EmptyFolderRequest` ;
  `ImapSession.EmptyAsync` (purge vs move) ; tests contrôleur + session ;
  `ApiDocumentation.xml` régénéré.

## 9. Contraintes globales (rappel, valeurs exactes)

- **Tokens uniquement**, jamais de littéral couleur ; géométrie de ligne **mesurée au
  navigateur**, jamais supposée.
- **Jamais `invalidateQueries` sur la clé `messageStream`** ; `settle()` (de `src/test-utils`)
  avant chaque assertion de silence.
- Toute écriture porte **`mailKeys.writes`** pour mettre le poll en veille — y compris
  `useEmptyFolder`.
- Lot d'UID plafonné à **200** (actions groupées) ; « Empty folder » **contourne** ce plafond
  côté serveur (opère sur `1:*`).
- Backend : `dotnet test` (jamais `--no-build`) quand un fichier de test est ajouté ;
  `Assert.IsType<BadRequestObjectResult>` pour les 400 via `BadRequest(body)` ; chemins de
  dossier dans le corps, jamais en segment de route ; ordre de validation source → cible vide
  → cible = source → credentials → session.

## 10. Hors périmètre (YAGNI)

- « Tout sélectionner » au-delà des lignes chargées (« les N du dossier » façon Gmail) — aucune
  action groupée côté serveur par requête n'existe, le backend prend une liste d'UID.
- Star/Unstar en lot (l'étoile reste une action par ligne).
- Drag & drop de la sélection vers l'arbre (tranche suivante, earmark).
- Recherche (2b4). Vidage automatique programmé de la corbeille (rétention serveur).

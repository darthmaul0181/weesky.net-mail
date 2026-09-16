# Tranche 2b2 — Actions de message (corbeille, archive, indésirable, déplacer/copier) — design

Date : 2026-07-22. Maquette validée : la modale « Move to folder » filtrable, dans
`.superpowers/brainstorm/13128-1784734739/content/move-modal.html`.

## 1. Place dans le découpage

2b1 (drapeaux) est livrée. Ce document couvre **2b2 en entier** : les trois actions de rôle
(corbeille, archive, indésirable) **et** déplacer/copier vers un dossier quelconque —
découpage confirmé plutôt que de reporter déplacer/copier. Restent 2b3 (sélection multiple)
et 2b4 (recherche).

## 2. Décisions validées

| Sujet | Décision |
|---|---|
| Suppression | **Corbeille puis définitif** : ailleurs, déplacement vers la corbeille sans confirmation (réversible) ; dans la corbeille, `\Deleted` + `UID EXPUNGE` avec confirmation (`DeleteConfirmModal`) |
| Annulation | **Aucune.** La corbeille est le filet. Aucun toast de succès non plus : la ligne qui disparaît est le retour. Conséquence : les nouveaux UID ne remontent jamais |
| Volet de lecture | Après suppression/déplacement du message ouvert : **passer au suivant** (au précédent en bout de liste ; refermer si seul) |
| Cluster de ligne | **Trois boutons** au survol : lu/non-lu, archiver, corbeille — la maquette 2b1 validée. Indésirable et déplacer/copier restent au kebab |
| Zone d'actions du lecteur | **Delete visible en permanence**, entre le toggle couleurs et le kebab. Neutre au repos, `--danger` au survol. Retiré du kebab en conséquence |
| Kebab du lecteur | Deux groupes, un filet : `Mark as unread · Star` — `Archive · Move to… · Copy to… · Report as junk` |
| Sélecteur de dossier | **Modale filtrable** (recherche + liste indentée `sortFolders`/`flatten`/`indent`), servant Move et Copy avec titre et bouton adaptés |
| Copie dans le dossier courant | **Interdite**, comme le déplacement : la règle « cible ≠ source » vaut pour les deux verbes, sans branche selon le mode |
| Rôle non attribué | Action **présente et désactivée** avec infobulle vers Réglages → Dossiers — jamais absente, jamais en échec silencieux |
| Report as junk | **Un déplacement, rien d'autre** : aucun signalement au filtre, aucun apprentissage rspamd |

## 3. Backend

### Endpoints

Trois actions, chemins de dossiers dans le corps (règle 3 : le séparateur peut être `/`) :

- `POST /api/Mail/Messages/Move` — corps `{ folderPath, uids, targetFolderPath }`
- `POST /api/Mail/Messages/Copy` — même corps
- `DELETE /api/Mail/Messages` — corps `{ folderPath, uids }` : suppression **définitive**
  (`\Deleted` + `UID EXPUNGE`). Le « supprimer = corbeille » du quotidien n'est pas un
  endpoint : c'est un Move vers le dossier `trash`, décidé par le frontend qui connaît les
  rôles.

Move et Copy sont des jumelles de huit lignes déléguant à une méthode commune du repository
avec un booléen — deux routes plutôt qu'un `copy: true`, qui se lirait mal dans la doc d'API.

### Validation et statuts

- 400 : dossier source vide ; `uids` vide ou > 200 (le plafond de 2b1) ; cible vide
  (Move/Copy) ; **cible identique à la source** (comparaison de chaînes, dans le contrôleur).
- 400 aussi : **cible non sélectionnable** — mais détectée par la **session**, pas le
  contrôleur : vérifier dans le contrôleur exigerait de charger l'arbre et doublerait les
  connexions IMAP. `ImapSession` expose une constante partagée (`TargetNotSelectable`, le
  patron `MessageNotFound`) que le contrôleur mappe en 400.
- 401 `credentials_unavailable` ; 204 succès ; 502 refus IMAP.
- Delete sans UIDPLUS : **502 avec un message explicite** (voir ci-dessous).

### ImapSession

- `MoveAsync` / `CopyAsync(sourcePath, uids, targetPath, ct)` : source ouverte ReadWrite,
  cible résolue par `GetFolderAsync` ; refus `TargetNotSelectable` si `\NoSelect` ou
  inexistante. Puis `MoveToAsync` / `CopyToAsync` de MailKit, qui utilisent `MOVE` quand le
  serveur l'annonce et retombent seuls sur COPY + `\Deleted` + EXPUNGE — la bascule relève
  de l'agnosticisme (règle 1), rien à coder.
- `DeleteAsync(folderPath, uids, ct)` : **UID EXPUNGE exigé.** Un EXPUNGE nu purgerait tout
  message `\Deleted` du dossier, y compris ceux marqués par un autre client. Sans UIDPLUS
  (capacité lue après authentification, règle 2), refus explicite
  (`"The mail server cannot delete single messages (no UIDPLUS)"`) plutôt qu'une purge
  élargie. Dovecot annonce UIDPLUS ; c'est un serveur externe de 2d qui pourrait non.
- Un UID disparu entre-temps reste un no-op silencieux, comme en 2b1.

## 4. Frontend — couche de données

### Mutations

`useMoveMessages(onError?)` — args `{ folderPath, uids, targetFolderPath, copy }` — et
`useDeleteMessages(onError?)` — args `{ folderPath, uids }` — sur le patron optimiste de
`useSetFlags` : snapshot, patch, rollback en `onError`, toast d'erreur
`Could not move the message` / `Could not delete the message`.

### Ce qu'un déplacement patche — asymétrique par nature

- **Source** : les lignes quittent chaque page et chaque bloc en cache
  (`removeSummaries`) ; l'arbre perd `total − N` et `unread −` (les non-lus retirés,
  comptés une fois à travers les caches, la mécanique `unreadTally` de 2b1).
- **Cible** : rien d'insérable — pas de nouveaux UID (pas d'annulation), pas de position
  calculable dans une liste triée paginée. Compteurs de l'arbre patchés (`total + N`,
  `unread +`), et pages/blocs en cache du dossier cible **jetés** (`removeQueries`), pour
  qu'une prochaine visite recharge au lieu d'afficher une liste trouée. Jeter n'est pas
  invalider : rien n'est refetché tant que le dossier n'est pas affiché.
- **Copie** : la source n'est pas touchée ; cible idem.
- **Suppression définitive** : comme la source d'un déplacement, sans cible.
- Une page devient courte d'une ligne et **rien ne remonte la combler** : c'est le poll qui
  recale, le comportement des clients de référence (2a). Pas de compensation.

### Le garde du poll s'étend — la leçon du bug de 2b1

Un déplacement bouge `unread` **et** `total` sur **deux** dossiers — exactement ce que
`folderChanged` surveille. Sans extension du garde, chaque déplacement rejouerait la boucle
corrigée hier : patch optimiste → arbre modifié → refetch pendant le MOVE en vol → la ligne
réapparaît. La clé `mailKeys.flags` est renommée en clé d'écriture partagée
(`mailKeys.writes`) et portée par les **trois** mutations ; `useListRefresh` se met en
veille pour toutes. Le test de veille est vérifié par cassage, comme en 2b1.

### Passer au suivant

`MailLayout` possède la sélection (paramètres d'URL) ; c'est lui qui avance. Un helper pur
répond « qui suit l'UID X dans l'ordre affiché ? » en lisant les mêmes caches que
`findCachedSummary` ; `MailLayout` construit `onAdvance(uid)` : suivant, sinon précédent,
sinon fermeture (uid retiré de l'URL). Fourni à la liste et au lecteur — les deux surfaces
déclenchent des départs de message.

## 5. Frontend — UI

### Cluster de ligne (les deux skins)

Trois boutons au survol/focus : lu/non-lu (2b1), archiver (`ArchiveIcon`), corbeille
(`TrashIcon` 16). La réserve d'ellipse passe de 32 à **88 px** (3 × 26 + 2 × 2 + 6). En
skin large, vérifier **au navigateur** que quatre contrôles (cluster + étoile) ne serrent
pas la ligne — la hauteur de ligne récupérée hier ne doit pas régresser ; mêmes mesures.

### Zone d'actions du lecteur

`[toggle couleurs si présent] │ [Delete] [⋮]` — le filet garde sa condition actuelle (le
seul toggle). Delete : `--text-muted` au repos, `--danger` au survol — pas de rouge
permanent dans un en-tête regardé à chaque mail. `aria-label` `Delete`, ou
`Delete permanently` dans la corbeille.

### Kebab

Deux groupes séparés d'un filet : `Mark as unread · Star`, puis
`Archive · Move to… · Copy to… · Report as junk`. `DropdownMenu` gagne un **séparateur**
(pas de variante danger : Delete n'y est plus). Depuis la corbeille, `Archive` et
`Report as junk` restent actifs (en sortir est légitime) ; `Move to…` aussi ; seul le sens
de Delete change.

### Règles d'activation

- Rôle `archive`/`trash`/`junk` non résolu → bouton/entrée **désactivé** + infobulle
  « Assign the … folder in Settings → Folders ». Source des rôles : `useFolders` déjà en
  cache (le `specialUse` stampé).
- Dans le dossier d'un rôle, l'action de ce rôle est désactivée (archiver depuis Archive).
- Suppression **définitive** : `DeleteConfirmModal` (existant), libellé au pluriel prêt
  pour 2b3.

### La modale Move/Copy

`MoveMessagesModal` — la maquette validée :

- Recherche focus à l'ouverture ; filtre insensible à la casse **et aux accents**
  (`normalize('NFD')` + suppression des diacritiques) : « indesirable » trouve « Courrier
  indésirable ». Un seul résultat → Entrée valide.
- Liste `sortFolders`/`flatten`/`indent` ; un enfant dont le parent est filtré remonte à
  plat. Compteur « n of 14 folders ».
- Dossier courant (`current`) et conteneurs (`container`) **présents, désactivés,
  badgés** — retirés, la liste se lirait comme un bug. Badges de rôle (`junk`…) affichés.
- Deux modes : titre `Move to folder` / `Copy to folder`, bouton `Move` / `Copy`. Vide et
  aucun-résultat comme la maquette. Squelette modale du site (`AddEditUserModal` de
  référence, ✕ pour sortir, `htmlFor`/`id`).

### Icônes nouvelles

`ArchiveIcon` (Feather, viewBox 24, stroke 2), `FolderMoveIcon`/`CopyIcon` si le kebab en
veut (mêmes règles), `JunkIcon` (interdit/poubelle barrée — choisir dans le style Feather).
Un fichier par icône, `currentColor`, jamais de couleur littérale.

## 6. Tests

**Backend** — `MailControllerTests` : Move/Copy — 400 (source vide, cible vide, lot vide,
> 200, cible = source, `TargetNotSelectable`), 401, 204, 502, délégation vérifiée ;
Delete — mêmes bords, plus UIDPLUS absent → 502 au message explicite.
`MailMessageRepositoryTests` : délégation/dispose/échec pour les trois verbes.

**Frontend** —
- `flagPatch.ts` → **`listPatch.ts`** (un module « flag » qui supprime des lignes serait un
  mensonge) ; `removeSummaries(messages, uids)` → restantes + retirés + delta non-lus.
- `useMoveMessages` : lignes hors de la source ; pages cible **jetées, pas patchées** ;
  compteurs des deux dossiers ; rollback complet ; jamais d'invalidation du flux ; la copie
  laisse la source intacte.
- `useDeleteMessages` : idem sans cible.
- `useListRefresh` : **en veille pendant un déplacement** — le test anti-régression du bug
  de 2b1, vérifié par cassage.
- `nextUidOf` (helper d'avance) : suivant, précédent en bout, null si seul.
- `MoveMessagesModal` : filtre accents/casse (le test qui échoue sans `normalize`), source
  et conteneurs désactivés, Entrée sur résultat unique, les deux modes.
- `ReaderActions` : Delete présent dans les deux thèmes, libellé selon le dossier, filet
  conditionné au seul toggle couleurs.
- Rôle manquant : bouton présent et désactivé, jamais absent.
- `MessageList` : trois boutons au cluster, réserve 88 px, archiver/corbeille déclenchent
  leurs mutations sans ouvrir la ligne.

## 7. Hors périmètre

- Annulation des déplacements (écartée), toasts de succès.
- Vider la corbeille, purge automatique — plus tard.
- Signalement au filtre antispam / apprentissage rspamd.
- Sélection multiple (2b3) — mais lots et libellés pluriels prêts.
- Recherche (2b4). Drag-and-drop vers l'arbre des dossiers — un jour, pas ici.

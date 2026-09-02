# Plan — correctifs de la revue de cardav-step2 (PR #69)

Spec: docs/superpowers/specs/2026-08-31-webmail-contacts-4e-groups-design.md

## Global Constraints

- Code « state of the art » : un mineur visible à l'écran se corrige. Pas de commentaire quand le code parle ; 3 lignes max par commentaire ajouté. Pas de duplication.
- Chaque correctif est couvert par un test qui rougit avant / verdit après (TDD). Ne jamais figer une valeur dépendante de l'hôte (fin de ligne, tri d'OS).
- Backend : `cd src/snoopy.microservice && dotnet test` (jamais `--no-build` quand un fichier de test est ajouté). Avant de committer, révertrer `ApiDocumentation.xml` si `dotnet test` l'a régénéré (`git checkout -- "**/ApiDocumentation.xml"`).
- Frontend : `cd src/frontend && npm test && npm run typecheck && npm run lint`. L'UI reste en anglais ; les clés i18n vivent dans les fichiers `contacts.json` des locales en et fr (trouver le chemin réel avant d'ajouter).
- Le dépôt est en `autocrlf=true` : ne pas convertir de fins de ligne ; vérifier `git diff --stat` avant de committer qu'aucun fichier non visé n'apparaît modifié.
- Commits : message concis (sujet + corps de 2 lignes max, ligne vide entre les deux), jamais de caractère `@` en début/fin, conventionnel `fix(scope): …`. Terminer par les lignes :
  ```
  Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01PCtivr8FyYuYHxChFv1hVX
  ```
- Un commit par constat corrigé (ou un commit pour deux constats indissociables).

## Task 1 — Stores de groupes : re-clé d'UID, suppression d'un groupe membre, casse du préfixe, tri

Fichiers : `src/snoopy.microservice/Repositories/ContactGroupStore.cs`, `src/snoopy.microservice/Repositories/DavContactWriter.cs`, `src/snoopy.microservice/Repositories/ContactStore.cs` (là où vit `StripFromGroupsAsync`/`Forms`), tests dans `src/snoopy.microservice/snoopy.microservice.Tests/`.

### 1.1 — DavContactWriter.cs:309 — un PUT qui change l'UID sous le même href orpheline les groupes
Un PUT CardDAV accepté par `GateAsync` avec un UID différent sous le même href fait `row.Uid = identity` mais ne re-clé ni `contact_group_members.member_uid` ni les lignes `MEMBER:` des cartes de groupe qui portaient l'ancien UID. Résultat : le contact sort de tous ses groupes dans le webmail (jointure `ContactGroupStore.ListAsync` sur `c.Uid`), et la carte de groupe servie aux autres clients pointe vers un UID inexistant.
Correction attendue : quand l'UID change, réécrire les cartes de groupe qui portent l'ancien UID (sous ses deux formes `Forms(oldUid)`) avec le nouvel UID à la place, et mettre à jour `member_uid` — même mécanique et même comptabilité de rang/révision que `StripFromGroupsAsync` (une carte de groupe réécrite prend un rang et une révision, comme à la suppression). Factoriser avec `StripFromGroupsAsync` plutôt que dupliquer (un « re-clé ou retrait » paramétré). Test : PUT sous le même href avec un nouvel UID → le groupe liste toujours le contact, sa carte porte `MEMBER:urn:uuid:<nouveau>` et plus l'ancien, `member_uid` mis à jour.

### 1.2 — ContactGroupStore.cs:117 — DeleteAsync d'un groupe ne retire pas le groupe des groupes parents
`ContactGroupStore.DeleteAsync` supprime la ligne et ses membres mais n'appelle pas `StripFromGroupsAsync(Forms(row.Uid))`, contrairement à `ContactStore.DeleteAsync`, `DeleteManyAsync(includeGroups)` et `DavContactWriter.DeleteAsync`. Un groupe parent (importé ou PUT DAV, décision 9 : membres imbriqués stockés non résolus) garde un `MEMBER:` fantôme et une ligne `contact_group_members` orpheline (pas de FK, pas de cascade). Décision 7 : « une carte supprimée quitte tous les groupes qui la portent » doit tenir sur les trois portes.
Correction : appeler le même retrait, dans la même transaction. Test : groupe P avec `MEMBER:urn:uuid:<uid de C>` ; DELETE de C via `ContactGroupStore.DeleteAsync` → la carte de P ne porte plus la ligne, la ligne membre a disparu, P a pris un rang.

### 1.3 — ContactGroupStore.cs:40 — la jointure ne reconnaît le préfixe `urn:uuid:` qu'en minuscules
`c.Uid == m.MemberUid || c.Uid == UrnUuidPrefix + m.MemberUid` tourne sous la collation binaire de `contacts.uid` et ne reconnaît que le préfixe minuscule, alors que `StripUrnUuid`, `Forms()` et `RemoveGroupMember` acceptent le préfixe quelle que soit sa casse (décision 7 : « urn:uuid: quelle que soit sa casse »). Un contact stocké `UID:URN:UUID:abc` est « non membre » à la lecture mais « membre » à la suppression (le retrait réécrit la carte et dépense un rang).
Correction : rendre la lecture cohérente avec le retrait, sans casser la sensibilité à la casse de l'UID lui-même (seul le préfixe de 9 caractères est insensible). Une traduction SQL qui reste indexable est préférable ; une comparaison en mémoire côté C# après un pré-filtre est acceptable si EF ne traduit pas proprement — justifier dans le rapport. Test : contact `URN:UUID:abc`, membre `abc` → listé comme membre.

### 1.4 — ContactGroupStore.cs:23 — ListAsync sans tri
Aucun `OrderBy` et rien en aval ne trie (le frontend affiche dans l'ordre reçu, et `suggestionsFor` coupe à `GROUP_LIMIT` avant tout tri). Les groupes sortent en ordre de GUID.
Correction : trier par nom d'affichage, insensible à la casse et aux accents autant que la collation le permet, puis par `Id` pour un ordre stable ; documenter le tri dans le contrat de l'API (commentaire XML de l'action si elle en porte un). Test : trois groupes créés en ordre « Work, Clients, Family » → listés « Clients, Family, Work ».

## Task 2 — Validation du nom `?` et conversion des cartes de groupe à double dialecte

Fichiers : `src/snoopy.microservice/Services/ContactValidator.cs`, `src/snoopy.microservice/Services/CardDav/VCardVersionConverter.cs`, et leurs tests.

### 2.1 — ContactValidator.cs:70 — le nom `?` est accepté puis effacé par la projection
`ValidateGroupName("?")` accepte, `ComposeNewGroup` écrit `FN:?`, `VCardProjector.WithoutPlaceholder` traite `?` comme le placeholder du writer et stocke `display_name` NULL : POST/PUT répondent 200/204 avec le nom, puis GET liste un groupe sans nom.
Correction : refuser dans `ValidateGroupName` tout nom que la projection effacerait (réutiliser la même définition du placeholder que `WithoutPlaceholder`, pas une seconde constante), message clair (« A group needs a name »). Test : `?` refusé à la création et au renommage, `a?` accepté.

### 2.2 — VCardVersionConverter.cs:114 — une carte portant les deux dialectes est doublée
`TranslateGroupProperties` suppose une carte stockée dans un seul dialecte. Une carte portant KIND/MEMBER **et** X-ADDRESSBOOKSERVER-KIND/-MEMBER (des clients écrivent les deux ; `AddGroupMember` peut aussi en produire) donne en 3.0 chaque ligne X- deux fois (la bibliothèque recopie les NonStandard, puis la reconstruction les ajoute), et en 4.0 deux lignes KIND. Les doublons reviennent en `vcard_raw` au PUT suivant.
Correction : en 3.0, ne reconstruire que ce que la sortie ne porte pas déjà (dédoublonner sur nom + valeur normalisée) ; en 4.0, après renommage, retirer les doublons KIND/MEMBER exacts. Vérifier que `AddGroupMember` (ContactStore.cs ~497) n'écrit pas un second dialecte sur une carte qui en a déjà un ; si c'est le cas, corriger là aussi. Tests : carte mixte → une seule ligne par membre dans chaque version ; cartes mono-dialecte inchangées (tests existants verts).

## Task 3 — Composer : index de suggestion hors bornes, identité d'adresse des groupes

Fichiers : `src/frontend/src/modules/mail/compose/RecipientsField.tsx`, `src/frontend/src/modules/contacts/contactSearch.ts`, tests associés.

### 3.1 — RecipientsField.tsx:86 — `active` n'est jamais borné quand `suggestions` rétrécit
`setActive(-1)` ne tourne que dans `reset()`/`type()`. Si les suggestions rétrécissent sans frappe (refetch au focus après 5 min ; groupe renommé/supprimé), Entrée / `,` / `;` appellent `commitSuggestion(suggestions[active])` avec `undefined` → TypeError sur `.kind` après `preventDefault` : la touche ne fait rien, et `aria-activedescendant` nomme un id inexistant.
Correction : dériver l'index effectif borné à `suggestions.length - 1` (ou -1 quand la liste est vide) à la lecture, ou le re-borner par effet quand `suggestions` change ; `commitSuggestion` ne doit jamais recevoir `undefined`. Test : suggestions passent de 4 à 3 avec `active=3`, Entrée → engage la troisième (ou rien, sans exception) ; `aria-activedescendant` cohérent.

### 3.2 — contactSearch.ts:76 (et RecipientsField.tsx:88) — `fold()` sert d'identité d'adresse
L'expansion d'un groupe dédoublonne ses adresses sur `fold(address.trim())` (NFD, sans diacritiques, minuscules : le normaliseur de recherche), et l'exclusion des destinataires déjà présents fait de même, alors que l'identité d'adresse du code est `canonicalAddress()` (`src/frontend/src/lib/canonicalAddress.ts`, trim + minuscules). `josé@x.com` et `jose@x.com` (deux boîtes SMTPUTF8 distinctes, toutes deux acceptées par `isValidAddress`) fusionnent : un membre ne reçoit jamais le mail.
Correction : utiliser `canonicalAddress` comme identité aux deux endroits ; `fold` reste réservé à la correspondance de recherche. Tests : groupe avec `josé@x.com` et `jose@x.com` → deux adresses insérées ; groupe dont toutes les adresses sont déjà présentes (à la casse près) → `onEmptyGroup`.

## Task 4 — Liste des contacts : message vide sous un groupe, libellé du fantôme de drag

Fichiers : `src/frontend/src/modules/contacts/ContactList.tsx`, `src/frontend/src/modules/contacts/ContactsLayout.tsx` si le scope doit descendre, fichiers de locale `contacts.json` en/fr, tests associés.

### 4.1 — ContactList.tsx:152 — un groupe vide affiche « No contacts yet »
Sous un scope groupe, `contacts` est déjà filtré par appartenance : un groupe vide (tout groupe fraîchement créé) affiche `list.empty` = « No contacts yet » / « Aucun contact pour l'instant » alors que « All contacts 300 » est visible une colonne à gauche. Aucune clé scope-aware n'existe.
Correction : un message propre au scope groupe (en : « This group has no members yet » ; fr : « Ce groupe n'a pas encore de membre »), et vérifier aussi le scope Favoris s'il souffre du même libellé (en : « No favourites yet » / fr : « Aucun favori pour l'instant ») — corriger les deux si c'est le cas. Ajouter la ou les clés dans les deux locales. Test : scope `group:…` avec liste vide → le libellé de groupe, pas `list.empty`.

### 4.2 — ContactList.tsx:82 — le fantôme de drag dit toujours « Add to favourites ★ »
`buildDragPill(ids.length, t('favourites.add'), STAR_GLYPH)` est figé alors que les lignes de groupe sont désormais des cibles de dépôt (`canDropIntoScope` accepte `group:*`). `setDragImage` est capturé au départ du drag, le libellé ne peut pas suivre la cible.
Correction : un libellé neutre au départ du drag qui reste vrai pour toutes les cibles (en : « Drag to a list » — ou mieux, le nombre de contacts seul avec un glyphe neutre ; choisir et justifier dans le rapport), clé i18n dans les deux locales. Test : la pastille ne contient plus `favourites.add`.

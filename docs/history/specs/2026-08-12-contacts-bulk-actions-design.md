# Actions groupées sur les contacts — design

Donner à la liste de contacts la sélection multiple que la liste de messages possède déjà, et deux
actions sur cette sélection : **supprimer**, depuis une icône de la bande de titre, et **mettre en
favori** en glissant la sélection sur un scope de la colonne de gauche.

La contrainte qui gouverne tout le document : **les deux modules doivent parler la même langue.**
Ce n'est pas une préférence esthétique — un webmail où cocher des lignes se fait d'une manière dans
le courrier et d'une autre dans le carnet d'adresses est un produit que l'utilisateur doit
réapprendre à chaque colonne. Partout où la question « comment fait le mail ? » a une réponse, elle
est la réponse ici aussi, et les écarts sont nommés et justifiés.

## Ce que le code existant a tranché

Relevé dans `MessageList.tsx`, `SelectionToolbar.tsx`, `useSelection.ts`, `dragMessages.ts` et
`FolderTree.tsx` avant d'écrire quoi que ce soit :

| Question | Réponse du mail |
|---|---|
| La recherche coexiste-t-elle avec la sélection ? | Oui. La loupe est une **action de la barre**, jamais désactivée. |
| Quand la sélection est-elle refusée ? | Sur une recherche **tous dossiers** seulement : ces lignes ne portent pas de case (`selectionDisabled`). |
| Que devient la sélection quand la vue change ? | `resetKey` (dossier + page, ou page de recherche) la vide. |
| Comment une ligne non cochée se comporte-t-elle au drag ? | `dragUids` : elle part seule, sans perturber une sélection faite pour autre chose. |
| Que refuse une cible de drop ? | `canDropInto` : le dossier source, et tout ce qui n'est pas sélectionnable. |
| Comment la cible s'annonce-t-elle ? | `.drop-ready` : anneau et teinte accent, **plus fort** que l'état actif, pour que l'endroit où l'on est déjà se lise comme exclu. |

Le carnet n'a pas d'équivalent de la recherche tous dossiers — un seul carnet, une seule vue — donc
la ligne « quand la sélection est-elle refusée » n'a pas de transposition : **la sélection reste
disponible pendant une recherche**, et « tout sélectionner » porte sur les lignes filtrées.

## Les décisions

| Décision | Retenu | Pourquoi |
|---|---|---|
| Suppression en masse | Endpoint backend | 50 contacts = 50 requêtes autrement, et un échec au 30ᵉ laisse un état bâtard qu'il faudrait savoir raconter. Le mail prend un tableau partout. |
| Favori en masse | Endpoint backend | Symétrie avec la suppression : un appel, un `SaveChanges`. |
| Drop sur « Tous les contacts » | Refusé | Ce n'est pas un groupe mais la vue complète : rien à y ajouter. C'est `canDropInto` refusant le dossier source. |
| Sélection pendant la frappe | **Vidée** | Choix du propriétaire, contre la recommandation initiale. `resetKey` inclut la requête, exactement comme le mail y met sa page de recherche. **Conséquence assumée : taper une lettre vide la sélection en cours**, là où le mail ne la vide qu'à la validation d'une recherche. Le filtre contacts étant vif, le cas se produira souvent ; c'est le prix de la stricte identité de comportement. |
| Barre de sélection | Squelette partagé | Deux composants à garder en phase sont deux composants qui divergent. |
| Recherche contacts | Champ permanent conservé | La première maquette la transposait en loupe sans le dire : c'était faire passer de zéro à un clic le geste le plus courant du carnet. La bande gagne la sélection sans que la recherche perde sa place. |

## Backend

Deux routes sur `ContactsController`, dans la forme que les écritures du mail ont déjà :

```
DELETE /api/Contacts          { ids: string[] }                  → 204
PUT    /api/Contacts/Favorite { ids: string[], isFavorite: bool } → 204
```

**Un id inconnu est un no-op silencieux, jamais une erreur.** `ContactStore` est scopé par
utilisateur : un id appartenant à quelqu'un d'autre ne résout rien, exactement comme un id
inexistant, et les distinguer dirait qu'il existe. C'est déjà la règle de `GET /Contacts/{id}`
(404 et non 403) et celle de `PUT /Mail/Messages/Flags` (« a UID the folder no longer holds is a
silent no-op, so the batch never half-fails »). Un lot ne peut donc pas échouer à moitié.

**Cap de 200 ids**, le même que le lot de drapeaux du mail. Au-delà : 400. Le cap borne la requête
comme le plafond de 5000 contacts borne la table.

**Un seul `SaveChanges` par appel.** La validation (liste vide, cap dépassé) est faite par le
contrôleur avant que le store soit touché, comme `ContactValidator` l'est déjà pour `POST`/`PUT`.

Les deux routes suivent la liste de statuts de leurs voisines : 204 / 400 / 401.

## Frontend

### `src/components/SelectionBand.tsx` — le squelette partagé

Trois emplacements et une règle :

1. la **case maîtresse**, avec son état indéterminé — un champ DOM, jamais un attribut ;
2. la **zone centrale**, remplie par l'appelant **au repos** et remplacée par le décompte dès qu'une
   ligne est cochée ; c'est la règle, et elle est ce que le squelette apporte ;
3. la **zone d'actions**, passée en `children`.

Rien d'autre : ni archive, ni corbeille, ni filtre — ce sont les appelants qui les fournissent.

Le mail met le nom du dossier et son filtre étoilé dans la zone centrale, et y garde ses huit
actions. Les contacts y mettent **leur champ de recherche et leur compteur**, et deux actions :
supprimer et rechercher.

**La bande contacts ne perd pas son champ de recherche permanent.** C'était une régression que la
première maquette portait sans le dire : la recherche contacts est aujourd'hui à zéro clic, et la
transposer en loupe la mettait à un. Elle reste donc en place au repos, cède la bande au décompte
pendant une sélection, et revient quand la sélection se vide. C'est le seul point où les deux
modules ne se ressemblent qu'à moitié, et c'est délibéré : le mail cherche dans un dossier parmi
quinze et gagne à ne montrer sa recherche que sur demande, le carnet est une liste unique où
chercher est le geste courant.

**La loupe reste offerte pendant la sélection, et l'utiliser videra la sélection.** C'est la
conséquence directe de `resetKey` incluant la requête (décision ci-dessus) et c'est exactement ce
que fait le mail, dont la loupe est elle aussi disponible pendant une sélection. Le bouton ne doit
pas être masqué pour autant : le retirer ferait de la bande deux barres différentes selon l'état,
là où elle n'en est qu'une qui change de contenu.

**Les règles `.selection-*` quittent `mail.css` pour `src/styles/selection.css`.** Un composant
partagé dont la feuille de style vit dans un module est la prochaine dérive : le jour où une règle
mail est ajustée, elle bouge la barre des contacts sans que personne le voie. Le nouveau fichier
est importé au même endroit que `modal.css`, avec les autres feuilles partagées.

### `useSelection<T>`

Le hook est aujourd'hui typé `Set<number>`, les uids du mail ; les contacts sont des GUID. Il
devient générique sur la clé. Aucun changement de comportement, un seul appelant à toucher côté
mail. Le hook continue de ne pas stocker la liste des lignes : l'appelant intersecte avec ce qui est
à l'écran, donc une ligne disparue cesse de compter d'elle-même.

### `contacts/dragContacts.ts`

Transcription de `dragMessages.ts`, module à module :

- MIME propre `application/x-weesky-contacts`, pour que la cible reconnaisse la charge à ses seuls
  `types` — le navigateur retient les *valeurs* jusqu'au drop mais expose toujours la liste.
- `dragIds(selectedIds, id)` : la ligne traînée emporte la sélection quand elle en fait partie,
  elle seule sinon.
- `parseDrag` refusant toute forme étrangère.
- `canDropInto(scope)` : `false` pour `all`, `true` pour `favorites` et, demain, pour un groupe.

### `ContactList`

Case par tuile, gouttière de 34px **réservée en permanence** — révéler une case ne doit pas
décaler les noms — case masquée au repos et épinglée dès qu'une sélection existe
(`.contact-tiles.has-selection`), départ du drag portant la pilule `.drag-pill`, et
`.contact-tile.is-dragging` à 0,45 comme la ligne de message.

`resetKey = scope + requête`.

### `ContactScopes`

Reçoit `onDropContacts`, gère `dragOver`/`dragLeave`/`drop` et porte `.drop-ready`. Le scope `all`
n'est jamais une cible.

**Le drop ajoute le favori, il ne le retire jamais.** Déposer sur « Favoris » met le drapeau ; le
retirer reste l'affaire de l'étoile, sur la tuile ou la fiche. Un geste qui ajouterait ou
retirerait selon l'état de chaque contact donnerait un résultat différent par ligne pour un seul
mouvement de souris.

### Suppression

`DeleteConfirmModal`, déjà dans `src/components/`, avec le décompte dans son texte. **Le mail
supprime sans confirmation hors corbeille parce que la corbeille est l'annulation ; le carnet n'en
a pas**, donc la confirmation est la seule prise de recul. C'est le premier écart assumé entre les
deux modules, et il est justifié par une différence réelle du domaine, pas par le goût.

### Mutations

`useDeleteContacts` et `useSetContactsFavorite`, sur le `useContactMutation` existant : invalidation
**`onSettled`**, pour qu'une écriture refusée laisse l'écran sur l'état du serveur plutôt que sur un
mensonge optimiste. La sélection est vidée après un succès.

### i18n

Nouvelles clés dans `contacts.json` (fr et en) : titre du décompte, libellés et `aria-label` des
deux actions, texte de la confirmation avec pluriel, libellé de la cible de drop. `parity.test.ts`
impose l'égalité des deux catalogues, l'espace insécable devant `:` et `!` et l'apostrophe
typographique française.

## Un défaut existant que ce travail rend visible

`mail.css` écrit `content: 'Drop here'` **en dur** dans `.folder-line.drop-ready .folder-row::after`.
L'application affiche donc « Drop here » en français, aujourd'hui, dans l'arbre des dossiers.
Reprendre l'idiome tel quel pour les contacts propagerait la faute à un second module.

La chaîne passe donc par une propriété personnalisée (`--drop-label`) posée sur l'élément depuis le
composant, alimentée par `t()`. Le mail est corrigé au passage — c'est trois lignes et cela évite
d'ajouter un deuxième endroit à réparer plus tard.

## Hors périmètre

- **Les groupes.** Le drop est construit pour eux (`canDropInto` prend un scope, pas un booléen
  « est-ce Favoris »), mais aucun groupe n'est créé ici. Le jour où ils arrivent, ils s'ajoutent
  sous « Favoris » et héritent du comportement sans une ligne de plus.
- **Le retrait de favori par glisser-déposer**, pour la raison donnée plus haut.
- **Les autres actions de masse** (export d'une sélection, fusion de doublons) : rien ne les
  demande aujourd'hui.
- **Le tactile.** Le mail ouvre sa sélection par la case maîtresse et par l'appui long ; les
  contacts reprendront la case maîtresse, qui est dessinée à toutes les largeurs. L'appui long
  n'est pas repris dans cette tranche.

## Tests

**Backend** — contrôleur : 204 sur un lot valide, 400 sur liste vide et sur cap dépassé, no-op
silencieux sur un id inconnu et sur un id d'un autre utilisateur, et la preuve qu'un lot mixte
(ids valides + un inconnu) supprime bien les valides. Store : un seul `SaveChanges`.

**Frontend** — `useSelection` générique sur des clés string ; `dragContacts` (fonctions pures,
formes étrangères refusées) ; `ContactList` (case révélée, épinglée sous sélection, décompte, drag
portant la sélection ou la seule ligne) ; `ContactScopes` (`all` ne s'allume jamais, `favorites`
s'allume et appelle le handler) ; la barre (actions désactivées à zéro sélection) ; et la
confirmation avant suppression. `SelectionBand` est couvert par ses deux consommateurs plutôt que
par des tests de son propre rendu.

**Non couvert par les tests, et c'est structurel** : jsdom ne calcule aucune mise en page, donc ni
la gouttière, ni l'anneau de la cible, ni la pilule ne sont vérifiables en test. Ils se vérifient
dans `probes/mobile-layout.html`, dont le cas `contacts-list` doit suivre le nouveau markup — un
fixture qui n'est pas le markup du composant ne garde rien.

## Références

`probes/contacts-bulk-mockup.html` porte les trois états validés (repos, sélection, drop) avec les
vraies feuilles de style. **Il est une pièce de conception, pas un garde-fou : il doit être supprimé
à la fin de l'implémentation**, sans quoi il deviendra un second markup contacts à maintenir.

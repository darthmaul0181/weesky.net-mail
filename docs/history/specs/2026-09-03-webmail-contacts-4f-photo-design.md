# Contacts 4f — la photo s'écrit

Sixième tranche du projet CardDAV, à la suite de
[4e](2026-08-31-webmail-contacts-4e-groups-design.md). Elle ouvre la porte d'écriture que 4a a
fermée en connaissance de cause (décision 12 : « la photo n'a pas de porte d'écriture dans cette
tranche […] l'éditeur de 4b l'ajoutera ») et que 4b a reportée à son tour (« Pas de porte d'écriture
photo »). Tout le reste existe : la projection `contact_photos`, la route `GET /api/Contacts/{id}/Photo`
avec son ETag, l'avatar de la fiche et du bandeau de l'éditeur.

## Où en est le projet

| | Tranche | État |
|---|---|---|
| 4a | Modèle de données complet + moteur d'aller-retour vCard | livrée |
| 4b | Éditeur et fiche webmail étendus | livrée |
| 4c | Serveur CardDAV | livrée |
| 4d | Conformité clients | livrée |
| 4e | Groupes de contacts | livrée |
| **4f** | Photo de contact *(ce document)* | à faire |

## Ce que fait la tranche

Dans l'éditeur, l'avatar se clique : on choisit une image, elle s'affiche aussitôt, et un bouton la
retire. Rien ne part avant « Save », rien ne change sur « Cancel ». À la sauvegarde la photo voyage
dans le même `PUT` (ou `POST`) que les autres champs, la carte est réécrite avec une ligne `PHOTO`
dans son dialecte, la projection suit, une révision est archivée et le rang CardDAV avance : un
téléphone synchronisé voit la nouvelle photo au sync suivant.

## Décisions

### 1. La photo voyage avec le formulaire, pas par une route à part

Une route `PUT /Photo` appelée dès le choix du fichier aurait changé la fiche avant que
l'utilisateur confirme, produit deux révisions CardDAV pour une édition « photo + nom », et n'aurait
pas su servir un contact pas encore créé. Le champ dans `ContactRequest` donne une transaction, une
révision, la protection du `cardHash` étendue à la photo, et la création avec photo gratuitement.

### 2. Un champ `photo` à la convention des champs que l'éditeur ne possède pas

`ContactRequest` gagne `photo: string?`, lu par `Given` comme `Notes`, `Organization` ou `Website` :
**absent ou `null`** = la carte garde sa photo, **chaîne vide** = retirée, **chaîne base64** =
remplacée. C'est la convention que `ContactWrite` documente déjà pour tout ce que l'éditeur ne
possède pas, et que le draft de l'éditeur reproduit : elle répond aux deux besoins — le client ne
renvoie pas 200 Ko de base64 à chaque sauvegarde, et un client qui ignore le champ n'efface aucune
photo — sans convertisseur JSON ni détection de présence de clé.

Le validateur traduit la valeur en `PhotoPayload`, et c'est ce type, pas la chaîne, que
`ContactWrite` transporte : un record abstrait à **deux** cas, `Remove` et
`Replace(bytes, mediaType)`, porté par un paramètre nullable — `PhotoPayload? Photo = null`. Pas de
troisième cas `Keep` : une valeur par défaut d'argument optionnel doit être une constante de
compilation, et `PhotoPayload Photo = Keep` ne compile pas (CS1736) sur une hiérarchie de records.
`null` dit exactement la même chose dans la convention que `ContactWrite` documente déjà — la
requête ne nomme pas le champ, la carte garde le sien — plutôt qu'un cas de plus à porter. Une
chaîne non vide qui n'est pas du base64 valide est un 400.

### 3. Les octets décident, pas le client

Le serveur ne lit ni type MIME ni préfixe `data:` : la chaîne est du base64 nu, et
`VCardProjector.SniffRasterType` — déjà la règle de la projection — dit si c'est un JPEG, PNG, GIF
ou WebP. Autre chose est refusé en 400 (« The photo is not a JPEG, PNG, GIF or WebP image »). Un
SVG est du XML exécutable ; il n'entrera pas par cette porte. Le sniff est extrait du projecteur
en méthode `internal static` partagée, pas dupliqué. Le mot vCard que la ligne portera (`JPEG`,
`PNG`, `GIF`, `WEBP`) se lit du même endroit, en `VCardProjector.RasterTypeName(mediaType)` : le
sniff et son nom sont une seule table, écrite une fois.

### 4. 512 Ko bruts, et le plafond de la carte reste souverain

Le plafond de la photo est `ContactValidator.MaxPhotoBytes = 512 * 1024` octets décodés (400 :
« The photo exceeds 512 KB »). Une seule garde le décide, avant tout décodage :
`System.Buffers.Text.Base64.IsValid(chars, out decodedLength)` — validité et taille décodée
exacte, sans allocation, blancs tolérés comme `FromBase64String` les tolère. Invalide, c'est
« not valid base64 » ; valide et `decodedLength > MaxPhotoBytes`, c'est « exceeds ». Le décodage
ne vient qu'après.

Pas de garde grossière sur la longueur de la chaîne avant celle-là. Elle refuserait sur la
longueur ce que seule la taille décodée sait juger : une base64 valide pliée à 76 colonnes, que
`IsValid` accepte, dépasse `4 * ceil(MaxPhotoBytes / 3)` caractères bien avant de dépasser le
plafond, et répondrait « exceeds » à une image parfaitement admissible. `IsValid` est déjà un
balayage linéaire sans allocation sur une chaîne que `RequestSizeLimit` borne : la garde
n'achetait rien qu'une contradiction. Et la longueur ne peut de toute façon pas décider — 512 Ko
et 512 Ko + 1 s'encodent tous deux sur 699 052 caractères, le padding absorbant l'octet.

Pliée à 74 par `Fold`, la ligne fait ~727 Ko, sous le plafond `MaxCardBytes` d'1 Mo que
`PrepareCard` continue d'appliquer à la carte entière : une carte déjà lourde peut donc être
refusée par le second plafond (`CardTooLarge`, 400) même avec une photo admise, et c'est le
comportement attendu. Le corps de `POST`/`PUT /api/Contacts` n'a pas de `RequestSizeLimit` propre
aujourd'hui ; il en gagne un de `2 * ContactStore.MaxCardBytes`, la constante que
`CardDavController.PutBodyBytes` épelle déjà pour le `PUT` CardDAV — c'est elle qui borne le
travail de `IsValid`.

512 Ko ne borne que cette porte-ci. Une photo déposée par un téléphone en `PUT` CardDAV n'y passe
pas : elle n'a que `MaxCardBytes` au-dessus d'elle, donc jusqu'à ~700 Ko décodés, et le webmail la
sert telle quelle. L'écart est assumé — le plafond du webmail est ce que son réducteur sait
produire, pas une règle de la boîte.

### 5. La famille `PHOTO` entière est remplacée ou retirée

Écart assumé à la règle « première occurrence » de 4b. La projection prend la première `PHOTO`
matricielle (décision 12 de 4a) : retirer seulement la première laisserait la deuxième devenir
l'avatar, et l'utilisateur verrait son retrait échouer ; remplacer seulement la première laisserait
une ancienne photo dormante que le prochain retrait réveillerait. Quand `photo` est `Replace`, toutes
les lignes `PHOTO` sont retirées et une seule est posée avant `END:VCARD` ; quand il est `Remove`,
toutes sont retirées. Pas de règle de rang : l'ordre des propriétés n'a aucun sens vCard, et une
4.0 dont `card.Photos` a été vidé n'a de toute façon plus de « première » à remplacer. Quand il
est `null`, rien ne change par rapport à aujourd'hui : sur une carte 3.0, `PHOTO` est une famille
non modélisée épissée verbatim par `SpliceUnmodelledFamilies` ; sur une 4.0 ou une 2.1 promue,
c'est la bibliothèque qui la re-sérialise depuis `card.Photos`, comme elle le fait déjà pour tout
le reste de ces cartes.

### 6. La ligne écrite suit le dialecte de la carte

`SourceCard.Version` décide, comme pour tout le reste du composeur :

- 3.0 : `PHOTO;ENCODING=b;TYPE=JPEG:<base64>` — `TYPE` est le mot vCard (`JPEG`, `PNG`, `GIF`,
  `WEBP`) dérivé du type sniffé ;
- 4.0 : `PHOTO:data:image/jpeg;base64,<base64>`.

La ligne est construite à la main et pliée par `Fold`, sans passer par la bibliothèque : le
composeur ne modélise pas `PHOTO` (il continue de ne pas la lire), il la pose. `PlacePhoto` vit
dans `Emit`, sur les lignes logiques de la sortie, après le bloc de réparations propre au 3.0 et
avant `RestoreUid` : c'est le seul endroit qui voit la carte dans les deux dialectes, `Apply`
n'ayant pas la main sur les lignes et `SpliceUnmodelledFamilies` ne tournant qu'en 3.0. Quand le
payload est `Replace` ou `Remove`, `card.Photos` est vidé avant la sérialisation pour que la
bibliothèque n'écrive pas 700 Ko destinés à être retirés, et `SpliceUnmodelledFamilies` saute la
famille `PHOTO` pour la même raison : sans quoi elle réinjecterait l'ancienne ligne que
`PlacePhoto` retirerait aussitôt. `ComposeNew` la pose de la même façon
sur une carte neuve (3.0). Le sniff ayant eu lieu au validateur, le composeur reçoit
`(bytes, mediaType)` et ne re-sniffe pas.

Deux précisions que le voisinage rend nécessaires. La valeur ne passe **pas** par `EscapeText` :
le `data:image/jpeg;base64,…` de la 4.0 porte un `;` et une `,` qui ne s'échappent pas dans une
valeur URI, et l'échapper la corromprait — c'est la seule ligne écrite à la main du composeur qui
sorte crue, parce que l'alphabet base64 n'a rien à échapper. Et `Emit` gagne le payload en
paramètre : `Apply` le tient de `ContactWrite`, `Reconcile`, `MergeFill` et `ComposeNewGroup`
passent `null` — ces trois-là ne portent pas de photo et ne doivent toucher à aucune.

### 6 bis. `LogicalLines` cesse d'être quadratique

`VCardComposer.LogicalLines` recolle chaque ligne de continuation par `lines[^1] += …`, une
réallocation de la ligne logique entière à chaque pli. Une `PHOTO` de 700 Ko pliée à 75 fait
~9 500 plis : ~3 G de caractères copiés par appel, en autant d'allocations LOH. Une sauvegarde y
passe quatre fois (`SourceCard.Read`, `RawBirthday` et `RawUid` via `FirstRawLine`, puis `Emit`
sur la sortie — le scanner brut du projecteur, lui, est un `Split` sur span, linéaire), et chaque
REPORT CardDAV y passe pour chaque carte servie (`AddressDataFilter`, `AddressBookFilter`, plus
`VCardVersionConverter` dès que le client demande la 3.0 d'une carte stockée en 4.0 — le chemin
nominal de 4d). Le `PUT` CardDAV y passe aussi, par `DavContactWriter`. Le
problème préexiste pour les photos que les téléphones déposent, mais 4f fait de 512 Ko le cas
nominal ; `WithUid` a déjà été écrit pour ne pas reconstruire une `PHOTO` pliée, `LogicalLines`
reçoit le même traitement — un `StringBuilder` par ligne logique, ou des indices — sans changer sa
signature ni son résultat. Dans la foulée, `IsName` cesse de déplier le chunk entier pour en lire
le nom : deux copies de 700 Ko par test de nom, des dizaines par `Emit`, pour un nom qui tient
dans la première ligne physique. Elle ne déplie que si cette première ligne ne porte ni `;` ni `:`.

### 7. Le store ne change pas de forme

`ContactWrite` gagne `PhotoPayload? Photo = null` en dernier paramètre, après `CardHash` qui porte
déjà sa valeur par défaut. **Le validateur est le seul à le poser** : `WriteOf` et l'import, les
deux autres producteurs de `ContactWrite`, s'arrêtent à `Source` et n'ont pas une ligne à changer —
le défaut dit déjà « garde la sienne », qui est leur intention. `UpdateAsync` et `CreateAsync`
composent comme aujourd'hui ; `PrepareCard` / `ApplyCardAsync` re-projettent `contact_photos` par le
chemin existant (la projection relit la carte, elle trouve la nouvelle `PHOTO`). Aucune nouvelle
transaction, aucun nouvel appel à `NextSequenceAsync`. `Create` répond `hasPhoto` =
`Photo is Replace` au lieu de `false` par construction.

Ce que la tranche change sans le changer, c'est le volume : chaque écriture d'une fiche à photo
archive la carte précédente entière dans `contact_revisions`, soit ~1 Mo au lieu de quelques Ko,
gardés 30 jours par `ContactTombstoneSweeper`. Les colonnes encaissent — `vcard_raw` est
`MEDIUMTEXT` des deux côtés, `contact_photos.bytes` `MEDIUMBLOB` — et la rétention borne le total ;
c'est dit ici pour que personne ne découvre la croissance en production.

### 8. Le navigateur réduit avant d'envoyer

Une photo de téléphone pèse 3 à 6 Mo ; envoyée telle quelle elle serait refusée à chaque fois. Un
module pur `contactPhoto.ts` fait : `createImageBitmap(file, { imageOrientation: 'from-image' })`
→ recadrage carré centré → réduction à 1024 px de côté au plus, jamais agrandie → fond blanc
(`fillRect`) puis `drawImage` → `canvas.toBlob('image/jpeg', q)` avec `q` = 0,85, puis 0,7 et 0,55
tant que le résultat dépasse 512 Ko → base64. Si 0,55 dépasse encore — une image très bruitée —
le côté est ramené à 512 px et la même descente rejouée ; si elle échoue aussi, erreur inline
`editor.photoTooLarge`, jamais un envoi voué au 400. L'orientation est demandée explicitement : le JPEG
réencodé perd sa balise EXIF, et une photo de téléphone en portrait sortirait couchée pour
toujours si le navigateur ne l'appliquait pas. Le fond blanc est posé parce qu'un canvas naît noir
transparent et que le JPEG jette l'alpha : sans lui, un logo d'entreprise sur fond transparent
devient un carré noir. Tout devient JPEG : l'avatar est dessiné en rond avec `object-fit: cover`,
l'animation n'y est pas visible et la transparence devient du blanc. Un fichier que
`createImageBitmap` refuse — un HEIC d'iPhone sur Chrome, entre autres — produit une erreur inline
sous l'avatar (`editor.photoUnreadable`, qui nomme les formats acceptés), pas un banner : c'est le
champ qui a échoué, pas la sauvegarde.

Le dernier pas, blob → base64, se fait par `FileReader.readAsDataURL` et la coupe au premier
`','`, pas par `btoa(String.fromCharCode(...new Uint8Array(buffer)))` : l'étalement d'un demi-million
d'octets en arguments fait sauter la pile, et c'est la façon dont ce code se casse partout ailleurs.
`FileReader` est asynchrone et mockable sous jsdom, ce que la boucle par morceaux n'est pas mieux.
L'`input` porte `accept="image/jpeg,image/png,image/gif,image/webp"` — le même quatuor que le
serveur sniffe, pour que le sélecteur de fichiers dise la règle avant qu'on la découvre par une
erreur ; `accept` étant un filtre d'affichage et non une garantie, `editor.photoUnreadable` reste
le vrai refus.

### 9. L'éditeur reste sans réseau

`ContactEditView` ne fait toujours aucune requête. `ContactDraft` gagne `photo: string | null`
avec la convention des autres scalaires optionnels du draft : `null` = inchangé, `''` = retiré,
base64 = remplacé. Le champ est obligatoire comme les autres scalaires, donc `draftFor` dans
`useCaptureContacts.ts`, l'autre producteur de draft, pose `photo: null`.

Ce que l'éditeur tient n'est pas une valeur comparée à un départ, mais **ce que l'utilisateur a
fait** — un état à trois cas, `{ kind: 'kept' }` au montage, `{ kind: 'removed' }`, ou
`{ kind: 'chosen', base64, url }` où `url` est l'object URL du blob réduit, révoqué au démontage et
à chaque nouveau choix. Il se lit dans les deux sens sans ambiguïté : `kept` affiche la prop
`photo` telle que le layout la rend à cet instant et soumet `null` ; `chosen` affiche `url` et
soumet `base64` ; `removed` affiche les initiales et soumet `''`. « Remove » sur un `chosen` rend
`kept` — retour au départ, `null` soumis ; « Remove » sur un `kept` rend `removed`.

C'est là l'écart avec `seededScalars` du 4b, et il est obligatoire : les autres scalaires sont
seedés depuis `contact`, déjà chargé quand le formulaire se monte, tandis que la photo arrive de
`useContactPhotoUrl`, dont la prop vaut `null` au premier rendu et ne devient un object URL qu'une
fois le blob téléchargé. Un `useState(photo)` gelé au montage capturerait donc `null`, et « Remove »
sur une photo apparue depuis comparerait `''` à `''` : le retrait serait silencieusement remplacé
par un `null`, l'écran ayant l'air d'avoir obéi. Un état d'action ne se laisse pas prendre à ça —
et le draft ne porte jamais un `blob:`, l'aperçu vivant dans `url`, jamais dans la valeur soumise.

### 10. Le `cardHash` entre dans la clé du cache

La clé `photo` est aujourd'hui `[…, id, 'photo']`, sous `contactKeys.all`, donc l'invalidation
`onSettled` de chaque écriture l'atteint. Elle ne suffit à aucun des deux gestes de la tranche. Au
retrait, `useContactPhoto` passe `enabled: false` quand `hasPhoto` retombe, mais react-query
continue de servir le blob en cache à une requête désactivée et l'invalidation ne la refait pas :
`useContactPhotoUrl` recréerait l'object URL de la photo retirée, que la fiche et le bandeau
montreraient jusqu'au GC du cache. Au remplacement, l'invalidation refait bien la requête, mais
react-query sert la donnée périmée pendant qu'elle vole : l'ancienne photo réapparaît un instant
sous la nouvelle.

La clé devient donc `contactKeys.photo(accountId, id, cardHash)`. Les deux appelants ont le
`cardHash` sous la main — `ContactCard` et `ContactsLayout` tiennent déjà le `detail` d'où ils
lisent `hasPhoto`. Une carte qui a changé est une autre clé : aucune entrée en cache, donc rien de
périmé à servir, ni au retrait ni au remplacement, et sans que le hook ait à se méfier de son
propre cache. `enabled: hasPhoto && id != null` reste, pour ne pas aller chercher un 404.

Ça ne coûte pas un téléchargement de plus : l'ETag de `GET /Photo` étant le `cardHash`, la
revalidation répondait déjà 200 et non 304 après n'importe quelle édition de la fiche. Ça ne le
retire pas non plus — voir « ce que la tranche ne fait pas ».

## API

`POST /api/Contacts` et `PUT /api/Contacts/{id}` — `ContactRequest` :

```
photo?: string | null   // absent ou null : gardée ; "" : retirée ; base64 nu : remplacée
```

Nouveaux 400 : `The photo is not valid base64`, `The photo is not a JPEG, PNG, GIF or WebP image`,
`The photo exceeds 512 KB`. `CardTooLarge` existant reste possible.
`RequestSizeLimit(2 * ContactStore.MaxCardBytes)` sur les deux routes.

## Fichiers

**Backend** — `Models/Contacts/ContactRequest.cs` (champ `Photo`), `Models/Contacts/ContactWrite.cs`
(`PhotoPayload` + paramètre `Photo = null`), `Services/ContactValidator.cs` (base64, sniff,
plafond), `Services/Contacts/VCardProjector.cs` (sniff et nom de type rendus `internal static`),
`Services/Contacts/VCardComposer.cs` (`PlacePhoto` dans `Emit` et le payload en paramètre, le rendu
de ligne par version, `SpliceUnmodelledFamilies` sautant `PHOTO` hors du cas `null`, `LogicalLines`
linéaire, `IsName` sans dépliage), `Controllers/ContactsController.cs` (`RequestSizeLimit`, réponse
de `Create`). `Repositories/ContactStore.cs` n'est pas touché — c'est ce que dit la décision 7.

**Frontend** — `modules/contacts/contactPhoto.ts` (nouveau, pur), `ContactEditView.tsx` (avatar
cliquable, input fichier caché, bouton retirer, état d'action à trois cas, erreur inline),
`contactTypes.ts` (`ContactDraft.photo`), `useCaptureContacts.ts` (`photo: null`), `queries.ts`
(`contactKeys.photo` portant le `cardHash`, `useContactPhoto` le recevant),
`useContactPhotoUrl.ts` et ses deux appelants `ContactCard.tsx` et `ContactsLayout.tsx` (le
`cardHash` passé avec le `hasPhoto` qu'ils lisent déjà du même `detail`),
`locales/en|fr/contacts.json` (`editor.changePhoto`, `editor.removePhoto`,
`editor.photoUnreadable`, `editor.photoTooLarge`), `index.css` (état bouton de
l'avatar, bouton retirer), `docs/architecture-contacts.md`.

## Tests

**`VCardComposerTests`** — remplacement et retrait en 3.0 et en 4.0 ; deux `PHOTO` ramenées à une ;
carte sans `PHOTO` qui en gagne une avant `END:VCARD` ; un payload `null` laisse une
`PHOTO;X-ABCROP-RECTANGLE=…` d'Apple octet pour octet ; ligne pliée à 75 octets ; la ligne 4.0
sort avec son `data:image/jpeg;base64,` intact, `;` et `,` non échappés ; `ComposeNew`
avec photo ; `LogicalLines` sur une carte portant une `PHOTO` de 700 Ko pliée rend le même
résultat qu'avant et alloue au plus quelques fois la taille de l'entrée
(`GC.GetAllocatedBytesForCurrentThread` avant/après — une borne d'allocations est déterministe,
un budget de temps est un flake par construction et la suite en porte déjà un classé comme
exception). **`VCardCorpusTests`** — la carte réécrite se re-projette avec la nouvelle photo
(`SniffRasterType` sur le résultat). **`VCardVersionConverterTests`** — une 4.0 portant la `PHOTO`
que 4f vient d'écrire se convertit en 3.0 sans perdre ses octets : c'est le chemin par lequel un
téléphone la lira.

**`ContactsControllerTests`** — absent, `null`, `""` et valeur produisent `null`, `null`, `Remove`,
`Replace` ; base64 invalide, SVG en base64, 512 Ko + 1 → 400 avec le message (ce dernier a la
même longueur base64 que 512 Ko : c'est `decodedLength` qui le refuse) ; une base64 valide pliée à
76 colonnes d'une image sous le plafond est **acceptée** — le test qui empêche la garde de longueur
de revenir ; `Create` répond `hasPhoto: true`.

**`ContactStoreTests`** — `UpdateAsync` avec `Replace` remplace la ligne `contact_photos` et archive
une révision ; `Remove` la supprime et `hasPhoto` passe à `false` ; un payload `null` ne touche à
rien et un `UpdateAsync` sans autre changement ne prend pas de rang.

**Frontend** — `contactPhoto.test.ts` : recadrage centré (paysage et portrait), pas d'agrandissement
d'une 300 px, fond blanc peint avant l'image, `imageOrientation: 'from-image'` transmis,
dégradation 0,85 → 0,7 → 0,55 quand le blob dépasse 512 Ko, puis 512 px, puis refus
`editor.photoTooLarge` ; le rendu est bien la base64 nue, sans le préfixe `data:` que
`readAsDataURL` pose ; rejet propre d'un fichier illisible ;
`createImageBitmap`, `HTMLCanvasElement.getContext` et `toBlob` mockés, jsdom n'ayant aucun des
trois. `ContactEditView.test.tsx` : le choix affiche l'aperçu et soumet `photo: <base64>` ;
« Remove » sur une photo existante soumet `photo: ''` ; **et le fait aussi quand la prop `photo`
arrive après le montage**, le test qui tient la décision 9 — sans lui, un seed gelé passerait ;
« Remove » sur un choix local revient au départ et soumet `photo: null` ; un fichier illisible
affiche `editor.photoUnreadable` et soumet `photo: null`. `useContactPhotoUrl.test.tsx` : le blob
mis en cache sous un `cardHash` n'est pas servi sous le suivant.
L'orientation EXIF se vérifie à la main, sur Chrome, Firefox et Safari, avec une photo de
téléphone en portrait : jsdom ne décode aucune image.

## Ce que la tranche ne fait pas

- **Pas de recadrage manuel** : le carré est centré, sans poignées.
- **Pas de photo de groupe** : `ContactGroupsController` ignore le champ.
- **Pas de `PHOTO` par URL** : le webmail n'écrit que des octets embarqués, et n'en va chercher
  aucun (SSRF, décision 12 de 4a).
- **Pas de transparence ni d'animation conservée** : tout devient JPEG, la transparence sur fond
  blanc.
- **Pas de format d'origine conservé** : un PNG choisi est réencodé ; un PNG déjà sur la carte,
  posé par un autre client, y reste tant qu'il n'est pas remplacé.
- **Pas d'ETag propre à la photo** : celui de `GET /Photo` reste le `cardHash`, donc renommer un
  contact périme sa photo et la fait re-télécharger — 512 Ko pour un prénom corrigé. Le régler
  demande un hash des octets, donc une colonne dans `contact_photos` ou un SHA-256 par requête ;
  c'est une tranche de performance, pas celle-ci. La décision 10 la nomme pour qu'on sache où
  chercher quand elle se paiera.

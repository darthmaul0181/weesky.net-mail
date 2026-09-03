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

### 2. Un champ tri-état `photo`

`ContactRequest` gagne `photo` : **absent** = la photo actuelle est gardée, **`null`** = retirée,
**chaîne base64** = remplacée. Les scalaires de la requête confondent absent et `null` (les deux
vident), mais ce contrat-là forcerait le client à renvoyer 200 Ko de base64 à chaque sauvegarde, et
un client qui ignore le champ effacerait toutes les photos. La présence de la clé est distinguée
par un convertisseur `System.Text.Json` dédié qui écrit la valeur dans un `PhotoPayload`
(`Keep` / `Remove` / `Replace(bytes)`) — le même schéma que `ContactLineJsonConverter` pour les
adresses. Une chaîne qui n'est pas du base64 valide est un 400.

### 3. Les octets décident, pas le client

Le serveur ne lit ni type MIME ni préfixe `data:` : la chaîne est du base64 nu, et
`VCardProjector.SniffRasterType` — déjà la règle de la projection — dit si c'est un JPEG, PNG, GIF
ou WebP. Autre chose est refusé en 400 (« The photo is not a JPEG, PNG, GIF or WebP image »). Un
SVG est du XML exécutable ; il n'entrera pas par cette porte. Le sniff est extrait du projecteur
en méthode `internal static` partagée, pas dupliqué.

### 4. 512 Ko bruts, et le plafond de la carte reste souverain

Le plafond de la photo est `ContactValidator.MaxPhotoBytes = 512 * 1024` octets décodés (400 :
« The photo exceeds 512 KB »). En base64 cela fait ~700 Ko, sous le plafond `MaxCardBytes` d'1 Mo
que `PrepareCard` continue d'appliquer à la carte entière : une carte déjà lourde peut donc être
refusée par le second plafond (`CardTooLarge`, 400) même avec une photo admise, et c'est le
comportement attendu. Le corps de `POST`/`PUT /api/Contacts` n'a pas de `RequestSizeLimit` propre
aujourd'hui ; il en gagne un de 2 Mo, comme le `PUT` CardDAV a le sien.

### 5. La famille `PHOTO` entière est remplacée ou retirée

Écart assumé à la règle « première occurrence » de 4b. La projection prend la première `PHOTO`
matricielle (décision 12 de 4a) : retirer seulement la première laisserait la deuxième devenir
l'avatar, et l'utilisateur verrait son retrait échouer ; remplacer seulement la première laisserait
une ancienne photo dormante que le prochain retrait réveillerait. Quand `photo` est `Replace`, toutes
les lignes `PHOTO` sont retirées et une seule est posée à la place de la première (ou avant
`END:VCARD` s'il n'y en avait pas) ; quand il est `Remove`, toutes sont retirées. Quand il est
`Keep`, `PHOTO` reste une famille non modélisée épissée verbatim, comme aujourd'hui.

### 6. La ligne écrite suit le dialecte de la carte

`SourceCard.Version` décide, comme pour tout le reste du composeur :

- 3.0 : `PHOTO;ENCODING=b;TYPE=JPEG:<base64>` — `TYPE` est le mot vCard (`JPEG`, `PNG`, `GIF`,
  `WEBP`) dérivé du type sniffé ;
- 4.0 : `PHOTO:data:image/jpeg;base64,<base64>`.

La ligne est construite à la main et pliée par `Fold`, sans passer par la bibliothèque : le
composeur ne modélise pas `PHOTO` (il continue de ne pas la lire), il la pose. `ComposeNew` la pose
de la même façon sur une carte neuve (3.0). Le sniff ayant eu lieu au contrôleur, le composeur
reçoit `(bytes, mediaType)` et ne re-sniffe pas.

### 7. Le store ne change pas de forme

`ContactWrite` gagne `PhotoPayload Photo`. `UpdateAsync` et `CreateAsync` composent comme
aujourd'hui ; `PrepareCard` / `ApplyCardAsync` re-projettent `contact_photos` par le chemin existant
(la projection relit la carte, elle trouve la nouvelle `PHOTO`). Aucune nouvelle transaction, aucun
nouvel appel à `NextSequenceAsync`. `Create` répond `hasPhoto` = `Photo is Replace` au lieu de `false`
par construction. `WriteOf` (le store rejouant un contact sans carte) passe `Keep`.

### 8. Le navigateur réduit avant d'envoyer

Une photo de téléphone pèse 3 à 6 Mo ; envoyée telle quelle elle serait refusée à chaque fois. Un
module pur `contactPhoto.ts` fait : `createImageBitmap(file)` → recadrage carré centré → réduction
à 1024 px de côté au plus, jamais agrandie → `canvas.toBlob('image/jpeg', q)` avec `q` = 0,85, puis
0,7 et 0,55 tant que le résultat dépasse 512 Ko → base64. Tout devient JPEG : l'avatar est dessiné
en rond avec `object-fit: cover`, la transparence et l'animation n'y sont pas visibles. Un fichier
que `createImageBitmap` refuse produit une erreur inline sous l'avatar (`editor.photoUnreadable`),
pas un banner : c'est le champ qui a échoué, pas la sauvegarde.

### 9. L'éditeur reste sans réseau

`ContactEditView` ne fait toujours aucune requête. `ContactDraft` gagne `photo?: string | null`
avec la convention du champ serveur : absent = inchangé, `null` = retiré, base64 = remplacé.
L'aperçu est un object URL du blob produit, révoqué au démontage et à chaque remplacement. Le
`photo` d'entrée (l'object URL que le layout résout) reste la valeur de départ ; un choix local le
recouvre ; « Remove » sur la photo de départ la vide et pose `null` dans le draft ; « Remove » sur un
choix local revient à l'état de départ et n'émet pas la clé — c'est le `seededScalars` du 4b
appliqué à la photo.

### 10. Ce que le cache sait déjà faire

La clé `photo` est sous `contactKeys.all` : l'invalidation `onSettled` de chaque écriture refait la
requête. L'ETag de `GET /Photo` est le `cardHash`, qui change avec la carte : pas de 304 trompeur.
Rien à ajouter dans `queries.ts`.

## API

`POST /api/Contacts` et `PUT /api/Contacts/{id}` — `ContactRequest` :

```
photo?: string | null   // absent : gardée ; null : retirée ; base64 nu : remplacée
```

Nouveaux 400 : `The photo is not valid base64`, `The photo is not a JPEG, PNG, GIF or WebP image`,
`The photo exceeds 512 KB`. `CardTooLarge` existant reste possible. `RequestSizeLimit(2 Mo)` sur
les deux routes.

## Fichiers

**Backend** — `Models/Contacts/ContactRequest.cs` (champ + `PhotoPayload` + `PhotoJsonConverter`),
`Models/Contacts/ContactWrite.cs`, `Services/Contacts/ContactValidator.cs` (base64, sniff, plafond),
`Services/Contacts/VCardProjector.cs` (sniff rendu `internal static`),
`Services/Contacts/VCardComposer.cs` (`PlacePhoto` appelé par `Apply` après
`SpliceUnmodelledFamilies`, plus le rendu de ligne par version), `Repositories/ContactStore.cs`
(`WriteOf`, `hasPhoto` à la création), `Controllers/ContactsController.cs` (`RequestSizeLimit`,
réponse de `Create`).

**Frontend** — `modules/contacts/contactPhoto.ts` (nouveau, pur), `ContactEditView.tsx` (avatar
cliquable, input fichier caché, bouton retirer, erreur inline), `contactTypes.ts` (`ContactDraft.photo`),
`locales/en|fr/contacts.json` (`editor.changePhoto`, `editor.removePhoto`, `editor.photoUnreadable`),
`index.css` (état bouton de l'avatar, bouton retirer), `docs/architecture-contacts.md`.

## Tests

**`VCardComposerTests`** — remplacement et retrait en 3.0 et en 4.0 ; deux `PHOTO` ramenées à une,
posée à la place de la première ; carte sans `PHOTO` qui en gagne une avant `END:VCARD` ; `Keep`
laisse une `PHOTO;X-ABCROP-RECTANGLE=…` d'Apple octet pour octet ; ligne pliée à 75 octets ;
`ComposeNew` avec photo. **`VCardCorpusTests`** — la carte réécrite se re-projette avec la nouvelle
photo (`SniffRasterType` sur le résultat).

**`ContactsControllerTests`** — absent/`null`/valeur produisent `Keep`/`Remove`/`Replace` ; base64
invalide, SVG en base64, 512 Ko + 1 → 400 avec le message ; `Create` répond `hasPhoto: true`.

**`ContactStoreTests`** — `UpdateAsync` avec `Replace` remplace la ligne `contact_photos` et archive
une révision ; `Remove` la supprime et `hasPhoto` passe à `false` ; `Keep` ne touche à rien et un
`UpdateAsync` sans autre changement ne prend pas de rang.

**Frontend** — `contactPhoto.test.ts` : recadrage centré (paysage et portrait), pas d'agrandissement
d'une 300 px, dégradation 0,85 → 0,7 → 0,55 quand le blob dépasse 512 Ko, rejet propre d'un fichier
illisible ; `createImageBitmap` et `toBlob` mockés, jsdom n'ayant ni l'un ni l'autre.
`ContactEditView.test.tsx` : le choix affiche l'aperçu et soumet `photo: <base64>` ; « Remove » sur
une photo existante soumet `photo: null` ; « Remove » sur un choix local revient au départ et
n'émet pas la clé ; un fichier illisible affiche `editor.photoUnreadable` et n'émet pas la clé.

## Ce que la tranche ne fait pas

- **Pas de recadrage manuel** : le carré est centré, sans poignées.
- **Pas de photo de groupe** : `ContactGroupsController` ignore le champ.
- **Pas de `PHOTO` par URL** : le webmail n'écrit que des octets embarqués, et n'en va chercher
  aucun (SSRF, décision 12 de 4a).
- **Pas de transparence ni d'animation conservée** : tout devient JPEG.
- **Pas de format d'origine conservé** : un PNG choisi est réencodé ; un PNG déjà sur la carte,
  posé par un autre client, y reste tant qu'il n'est pas remplacé.

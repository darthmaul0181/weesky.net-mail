# Contacts 4a — ce que la tranche laisse derrière elle

Ce document est le tri de fin de tranche : ce qui a été délibérément différé, et ce que les revues ont
appris de FolkerKinzel.VCards 8.2.0 et qui n'est écrit nulle part ailleurs. Il existe pour que 4b et 4c
n'aient pas à redécouvrir à leurs frais ce qui a déjà coûté une sonde.

## Reporté de 4b vers 4c / backlog

Cette section s'appelait « À traiter en 4b ». La tranche 4b n'en a fermé aucun point, et la revue
de fin de tranche a refusé qu'ils disparaissent avec le titre : ils sont donc rebaptisés ici, avec
ce que 4b change à l'atteignabilité de chacun — c'est elle qui a fait de l'éditeur le premier
écrivain de ces familles.

| Point | Où | Ce que 4b y change |
|---|---|---|
| **Une édition qui ramène une famille effondrée à une seule occurrence perd ses paramètres `X-`** | `VCardComposer.SpliceFamily`, garde `model.Count < 2` | **Celui qui compte.** La spec le donnait comme *rendu courant par 4b*, 4b étant le premier éditeur à écrire ces familles, et la tranche ne l'a pas traité : il part en 4c inchangé, mais désormais atteignable en routine et non plus en théorie. |
| `URL;TYPE=PREF` est perdu sur un aller-retour 3.0 | `VCardComposer.RestoreDroppedParameters` ne restaure que les paramètres non standard | Préexistant, et **prouvé atteignable avant cette tranche** : sondé en revue de la tâche 2, `Compose` le perd que `Website` soit null ou inchangé, parce qu'`Emit` re-sérialise la carte entière et qu'`URL`, étant dans `OwnedNames`, n'est jamais recollée verbatim. Le correctif C1 réduit l'exposition — un site web intact part maintenant à `null` — sans fermer le point. |
| `Fold` compte des unités UTF-16 et peut couper une paire de substitution | `VCardComposer.Fold` | Intact, territoire 4c. L'éditeur écrit désormais du texte libre — notes, société — ce que le résidu annonçait comme le facteur d'aggravation. |
| Aucun test n'épingle la troncature des scalaires ni des composantes d'adresse, et l'aller-retour de troncature est vivant | tests du projecteur ; `VCardComposer.Apply` pour l'aller-retour | Le correctif C1 **ferme le cas courant** : un scalaire que l'utilisateur n'a pas touché part à `null`, donc une édition sans rapport ne réécrit plus dans la carte une `NOTE`, une `ORG` ou une `URL` que le projecteur avait raccourcie à la largeur de sa colonne. Restent l'utilisateur qui édite un champ déjà tronqué — inhérent aux largeurs de colonnes, rien côté client ne connaît l'original — et les tests d'assurance, toujours à écrire. |
| Une composante de `N` à plusieurs valeurs dont une seule est le remplissage laisse passer le `?` | `VCardProjector.NamePart` | Intact, et inchangé en atteignabilité : `N:Smith,?;John;;;` projette `Smith,?`, le filtre portant sur la composante entière et non sur chaque valeur. Aucun client connu n'émet cette forme — le remplissage est toujours seul — et le correctif tient en une ligne : filtrer les valeurs avant la jointure. |

## À traiter en 4c

| Point | Où | Pourquoi là |
|---|---|---|
| `UID:urn:uuid:X` ressort en `UID;VALUE=TEXT:urn:uuid:X` en 4.0 | `VCardComposer.Emit` | **La valeur de l'UID ne tourne pas** sur le chemin de production : la colonne vient de `VCardImportMapper.UidOf`, un balayage textuel qui garde le préfixe. Seul un libellé `VALUE=TEXT` s'ajoute sur une valeur en forme d'URI — non conforme cosmétiquement. Retirer le libellé, ou coller la ligne `UID` d'origine quand la valeur égale déjà la colonne. |
| Le test du corpus compose avec l'`Uid` du **projecteur** | `VCardCorpusTests.Corpus_SurvivesASingleFieldEdit` | Le projecteur retire le préfixe `urn:uuid:` ; la production ne l'utilise jamais comme source. Faire composer le test avec la forme `UidOf` pour qu'il modélise la production. |
| `VCardProjector.RawCard` ne s'arrête pas au premier `END:VCARD` | `VCardProjector` | Inatteignable tant que le découpeur garantit une carte par morceau. Le `PUT` de 4c devient un second producteur de `vcard_raw` : aligner à ce moment-là. |
| `If-None-Match: *`, les ETags faibles et les valeurs multiples ne sont pas honorés | `ContactsController.GetPhoto` | Comparaison exacte par valeur. Route d'avatar interne aujourd'hui ; 4c porte la vraie sémantique d'ETag. |
| Le repli du nom d'affichage à l'export ignore l'ordre `PREF` | `ContactCsvExporter.IsFirstAddress` | Compare à la première adresse projetée, ordonnée `(pref, position)`, alors que le composeur avait pris la première adresse de l'écriture. Diffèrent seulement si une adresse non première porte `PREF` — inatteignable depuis l'UI et depuis l'import CSV, atteignable par l'API. Tester toutes les adresses plutôt que la première. |

## Laissé tel quel, et pourquoi

- **La fusion d'import ne verse pas la photo de la carte entrante.** Elle arrive intacte à la création, où la carte est posée verbatim ; sur une fusion, le composeur n'a aucune porte d'écriture `PHOTO` (décision 12) et l'ouvrir ferait passer la fusion sous le plafond de 1 Mo de la carte, avec le cas « la carte grossit au-delà et l'écriture échoue » à traiter. Omission délibérée, tranchée le 22 août avec le reste de l'élargissement.
- **`VCardImportMapper` promeut l'adresse en pseudonyme** quand la carte ne porte ni prénom ni nom : c'est la carte elle-même qui l'affirme par son `FN`, contrairement à l'export où le nom serait fabriqué.
- **Le repli d'export est plus large que le défaut ne l'exige** : une fiche nommée dont le `FN` vaut légitimement sa propre adresse exporte un nom calculé. Aucune mutation de données ne s'ensuit — le mapper ne promeut la colonne que si prénom **et** nom manquent.
- **Une adresse de 256 à 320 caractères échappe au repli** : `display_name` est plafonné à 255, l'adresse à 320, donc l'égalité échoue. Conséquence bornée à une erreur d'import sur notre propre fichier, jamais une mutation.
- **`Truncate` peut couper une paire de substitution** aux onze endroits où il est appelé : le connecteur substitue U+FFFD, pas d'exception ni de débordement d'octets.
- **`PUT /api/AppSettings` portait déjà la politique Admin** avant cette tranche : sur `generic`, dont le fournisseur n'a pas de gestionnaire, le drapeau d'installation est donc inatteignable. Question produit préexistante, désormais documentée.
- **Une fiche dont la carte dépasse 1 Mo reste dans la file du rattrapage** : `docs/superpowers/contacts-4a-backfill.md` § 4 dit comment la localiser et pourquoi la laisser ne casse aucun écran.
- **Aucun bouton « se déconnecter partout » dans l'interface.** `DELETE /api/Login/All` est servi
  et révoque désormais aussi le secret de synchronisation (4c-i, décision 2), mais aucun écran ne
  l'appelle et `api.js` n'en porte pas d'entrée : l'avertissement que la décision réclame sur ce
  bouton n'a rien à annoter. À poser le jour où le bouton apparaît.

## Ce que les sondes ont appris de FolkerKinzel.VCards 8.2.0

Chacun de ces points a été mesuré contre le paquet installé, pas déduit. Ils expliquent pourquoi le
composeur porte une passe de réparation et un collage verbatim.

- **L'écrivain 3.0 n'émet que l'occurrence la plus préférée** de `URL`, `NOTE`, `TITLE`, `ORG` et `NICKNAME` — les autres sont détruites. Le collage verbatim des occurrences intactes existe pour ça.
- **L'écrivain 3.0 ignore `WriteNonStandardParameters`** sur `TEL`, `EMAIL`, `URL` et `BDAY` : les paramètres `X-` disparaissent sans la réparation.
- **`BDAY:--0315` devient `BDAY;VALUE=DATE:0004-03-15`** et `--03-15` est purement supprimé : la forme textuelle est réimposée après coup.
- **L'écrivain 3.0 réordonne les `TEL` par `Preference` croissante** : l'appariement de la réparation se fait par rang, avec une garde de comptage et un test-mouchard qui rougit si un futur paquet change cet ordre.
- **`Preference` vaut 100 par défaut** et le `TYPE` d'un e-mail n'est pas exposé sur le modèle : le projecteur dérive `type` et `pref` du texte brut, ce qui rend une désynchronisation scanner/bibliothèque plus grave qu'une colonne d'affichage.
- **`Parameters.MediaType` est normalisé depuis le `data:` URI** — mais en 3.0 `ENCODING=b`, ce que produisent iPhone et Outlook, seul le paramètre déclare le type. D'où le reniflage des octets.
- **`ContactID.Create("urn:uuid:X")` garde la chaîne** ; un `UID:urn:uuid:X` parsé rend `String` **et** `Uri` nuls et ne remplit que `Guid`.
- **`N` et `FN` sont obligatoires et la bibliothèque remplit un vide avec `?`** : `N:?;;;;` en 3.0 seulement, `FN:?` dans les deux versions, et l'écrivain **synthétise** `FN` depuis un `N` nommé quand `DisplayNames` manque. C'est ce remplissage qui a produit le bug de production du 20 août : six fiches sans nom affichaient « ? ». Le composeur le blanchit quand le vide est le nôtre, le projecteur refuse de l'admettre quelle que soit la carte.
- **`X-ABLabel` garde son groupe** à la réécriture, y compris avec le collage désactivé : la réserve que la spec portait sur ce point est levée.
- **`TYPE` est fusionné et réordonné** (`type=CELL;type=VOICE;type=pref` → `TYPE=VOICE,CELL,PREF`, `INTERNET` ajouté) : toute assertion d'égalité octet à octet sur un bloc de paramètres est impossible par construction.

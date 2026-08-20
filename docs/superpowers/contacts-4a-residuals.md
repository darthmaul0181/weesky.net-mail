# Contacts 4a — ce que la tranche laisse derrière elle

Ce document est le tri de fin de tranche : ce qui a été délibérément différé, et ce que les revues ont
appris de FolkerKinzel.VCards 8.2.0 et qui n'est écrit nulle part ailleurs. Il existe pour que 4b et 4c
n'aient pas à redécouvrir à leurs frais ce qui a déjà coûté une sonde.

## À traiter en 4b

| Point | Où | Pourquoi maintenant |
|---|---|---|
| `URL;TYPE=PREF` est perdu sur un aller-retour 3.0 | `VCardComposer.RestoreDroppedParameters` ne restaure que les paramètres non standard | C'est la dernière destruction de paramètre connue. Étroite (première `URL`, 3.0 seulement), mais c'est la classe que 4a existe pour fermer. Étendre la réparation ou coller la ligne intacte. |
| `Fold` compte des unités UTF-16 et peut couper une paire de substitution | `VCardComposer.Fold` | Atteignable par une ligne réparée de plus de 75 caractères contenant un caractère hors BMP. Rare aujourd'hui, plus probable dès que l'éditeur écrit du texte libre. |
| Une édition qui ramène une famille effondrée à une seule occurrence perd ses paramètres `X-` | `VCardComposer.SpliceFamily`, garde `model.Count < 2` | Forme d'éditeur : c'est 4b qui la rendra courante. |
| Aucun test n'épingle la troncature des scalaires ni des composantes d'adresse | tests du projecteur | Assurance à bas coût avant que l'éditeur n'écrive ces colonnes. |

## À traiter en 4c

| Point | Où | Pourquoi là |
|---|---|---|
| `UID:urn:uuid:X` ressort en `UID;VALUE=TEXT:urn:uuid:X` en 4.0 | `VCardComposer.Emit` | **La valeur de l'UID ne tourne pas** sur le chemin de production : la colonne vient de `VCardImportMapper.UidOf`, un balayage textuel qui garde le préfixe. Seul un libellé `VALUE=TEXT` s'ajoute sur une valeur en forme d'URI — non conforme cosmétiquement. Retirer le libellé, ou coller la ligne `UID` d'origine quand la valeur égale déjà la colonne. |
| Le test du corpus compose avec l'`Uid` du **projecteur** | `VCardCorpusTests.Corpus_SurvivesASingleFieldEdit` | Le projecteur retire le préfixe `urn:uuid:` ; la production ne l'utilise jamais comme source. Faire composer le test avec la forme `UidOf` pour qu'il modélise la production. |
| `VCardProjector.RawCard` ne s'arrête pas au premier `END:VCARD` | `VCardProjector` | Inatteignable tant que le découpeur garantit une carte par morceau. Le `PUT` de 4c devient un second producteur de `vcard_raw` : aligner à ce moment-là. |
| `If-None-Match: *`, les ETags faibles et les valeurs multiples ne sont pas honorés | `ContactsController.GetPhoto` | Comparaison exacte par valeur. Route d'avatar interne aujourd'hui ; 4c porte la vraie sémantique d'ETag. |
| Le repli du nom d'affichage à l'export ignore l'ordre `PREF` | `ContactCsvExporter.IsFirstAddress` | Compare à la première adresse projetée, ordonnée `(pref, position)`, alors que le composeur avait pris la première adresse de l'écriture. Diffèrent seulement si une adresse non première porte `PREF` — inatteignable depuis l'UI et depuis l'import CSV, atteignable par l'API. Tester toutes les adresses plutôt que la première. |

## Laissé tel quel, et pourquoi

- **`VCardImportMapper` promeut l'adresse en pseudonyme** quand la carte ne porte ni prénom ni nom : c'est la carte elle-même qui l'affirme par son `FN`, contrairement à l'export où le nom serait fabriqué.
- **Le repli d'export est plus large que le défaut ne l'exige** : une fiche nommée dont le `FN` vaut légitimement sa propre adresse exporte un nom calculé. Aucune mutation de données ne s'ensuit — le mapper ne promeut la colonne que si prénom **et** nom manquent.
- **Une adresse de 256 à 320 caractères échappe au repli** : `display_name` est plafonné à 255, l'adresse à 320, donc l'égalité échoue. Conséquence bornée à une erreur d'import sur notre propre fichier, jamais une mutation.
- **`Truncate` peut couper une paire de substitution** aux onze endroits où il est appelé : le connecteur substitue U+FFFD, pas d'exception ni de débordement d'octets.
- **`PUT /api/AppSettings` portait déjà la politique Admin** avant cette tranche : sur `generic`, dont le fournisseur n'a pas de gestionnaire, le drapeau d'installation est donc inatteignable. Question produit préexistante, désormais documentée.
- **Une fiche dont la carte dépasse 1 Mo reste dans la file du rattrapage** : `docs/superpowers/contacts-4a-backfill.md` § 4 dit comment la localiser et pourquoi la laisser ne casse aucun écran.

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
- **`X-ABLabel` garde son groupe** à la réécriture, y compris avec le collage désactivé : la réserve que la spec portait sur ce point est levée.
- **`TYPE` est fusionné et réordonné** (`type=CELL;type=VOICE;type=pref` → `TYPE=VOICE,CELL,PREF`, `INTERNET` ajouté) : toute assertion d'égalité octet à octet sur un bloc de paramètres est impossible par construction.

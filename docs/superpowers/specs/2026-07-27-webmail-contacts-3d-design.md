# Webmail — module Contacts (tranche 3d) : import et export CSV

**Date :** 2026-07-27
**Statut :** conception validée, prête pour le plan d'implémentation

## Objet

La tranche 3a avait renvoyé l'import à une tranche 3d et déclaré l'export hors périmètre. Cette spec
ouvre 3d et l'élargit à l'export, **en CSV seul** : le vCard, des deux côtés, est une tranche
ultérieure.

Le besoin est celui de tout carnet d'adresses : arriver d'ailleurs sans retaper, et repartir ailleurs
sans être retenu. Le fichier de référence est l'export de Snappymail/Rainloop, la webmail que cette
plateforme héberge déjà.

## Décisions

| Sujet | Décision |
|---|---|
| Format d'entrée | **en-têtes reconnus**, table d'intitulés couvrant Rainloop/Outlook, Google et Thunderbird. Pas d'écran de mappage manuel |
| Doublons à l'import | **fusion sur l'adresse, ou à défaut sur le nom exact** ; rien n'est jamais écrasé ; adresse ou nom ambigu → ligne sautée |
| Où le travail s'exécute | **entièrement au backend** : une route d'import, une route d'export |
| Déroulé | **direct puis rapport** ; pas d'aperçu, pas de second aller-retour |
| Colonnes hors modèle | **traduites en vCard 3.0 et rangées dans `vcard_raw`** |
| Bibliothèque CSV | aucune ; lecteur et écrivain internes |
| Emplacement dans l'UI | **pied de la colonne des portées**, sous les scopes |

### L'en-tête de référence

Snappymail exporte le jeu de colonnes d'**Outlook**, pas le sien : son interface n'édite que nom,
prénom, adresses, téléphone, pseudo et URL, mais le fichier porte trente-quatre colonnes dont la
plupart sortent vides.

```
Title,First Name,Middle Name,Last Name,Nick Name,Display Name,Company,Department,Job Title,
Office Location,E-mail Address,Notes,Web Page,Birthday,Other Email,Other Phone,Other Mobile,
Mobile Phone,Home Email,Home Phone,Home Fax,Home Street,Home City,Home State,Home Postal Code,
Home Country,Business Email,Business Phone,Business Fax,Business Street,Business City,
Business State,Business Postal Code,Business Country
```

Deux conséquences heureuses : couvrir Rainloop couvre Outlook par la même occasion, et le fichier
porte **quatre** colonnes d'adresse, ce qui tombe juste avec nos adresses multiples.

## Le format

### Reconnaissance des en-têtes

Chaque intitulé est normalisé — minuscules, espaces, tirets, tirets bas et points retirés — puis
cherché dans une table. Ce qui n'y figure pas n'est pas une erreur : c'est une colonne dont on ne
fait rien de structuré (voir « Ce qu'on ne modélise pas »).

| Champ | Intitulés reconnus |
|---|---|
| Prénom | `First Name`, `Given Name`, `Prénom` |
| Nom | `Last Name`, `Family Name`, `Surname`, `Nom` |
| Pseudo | `Nick Name`, `Nickname` |
| Nom affiché | `Display Name`, `Name`, `Full Name` |
| Adresses | `E-mail Address`, `Other Email`, `Home Email`, `Business Email` (Rainloop/Outlook) ; `E-mail 1 - Value` … `E-mail 4 - Value` (Google) ; `Primary Email`, `Secondary Email` (Thunderbird) ; `Email`, `Email Address` ; `E-mail 2 Address` … `E-mail N Address` (les nôtres, voir « Écriture du fichier ») |

Nos propres colonnes d'adresse secondaires sont reconnues **par motif** (`email` + un nombre +
`address`, après normalisation) et non énumérées : leur nombre dépend du carnet exporté, donc une
liste finie plafonnerait la relecture de nos propres fichiers.

**Le nom affiché est un repli, pas un champ.** Il n'alimente rien quand la ligne porte déjà un prénom
ou un nom ; sinon il part dans le pseudo, qui est exactement le champ que `displayNameOf` consulte
ensuite. Le découper en prénom et nom sur un espace serait deviner, et se tromper sur tout nom
composé.

**L'ordre des adresses est l'ordre des colonnes du fichier**, sans cas particulier. `E-mail Address`
précède `Other Email` chez Rainloop, `E-mail 1 - Value` précède `E-mail 2 - Value` chez Google : la
position 0 tombe juste des deux côtés sans qu'on ait à désigner une colonne « principale », ce qui
aurait demandé une seconde table à tenir en accord avec la première.

### Lecture du fichier

- **BOM retiré** s'il y en a un.
- **Séparateur reniflé sur la ligne d'en-tête** parmi `,`, `;` et la tabulation, en comptant les
  occurrences hors guillemets : Excel francophone écrit `;`, et un fichier lu au mauvais séparateur
  ne produit pas une erreur mais une seule colonne, c'est-à-dire un import silencieusement vide.
- **Encodage** : UTF-8 si le fichier décode strictement, Latin-1 sinon. Outlook exporte encore en
  Windows-1252 ; `Encoding.Latin1` est natif .NET et ne diffère de 1252 que sur `0x80`–`0x9F`
  (guillemets typographiques, €), jamais sur les lettres accentuées d'un nom. `System.Text.Encoding.CodePages`
  aurait été une dépendance pour cet écart-là seul.
- **Grammaire RFC 4180** : champ entre guillemets pouvant contenir le séparateur, un retour à la
  ligne, ou un guillemet doublé.

### Écriture du fichier

```
First Name,Last Name,Nick Name,Display Name,E-mail Address,E-mail 2 Address,…,Favorite
```

- **UTF-8 avec BOM.** Sans lui, Excel lit un fichier UTF-8 en 1252 et rend « Dupré » en « DuprĂ© ».
  Notre lecteur retire le BOM, donc l'aller-retour ne le voit jamais.
- **La première adresse va dans `E-mail Address`**, la colonne que Rainloop et Outlook comprennent ;
  les suivantes dans `E-mail 2 Address`, `E-mail 3 Address`… que nous seuls relisons. Le carnet
  ressort donc complet chez nous et utilisable ailleurs, plutôt que tronqué des deux côtés.
- **Le nombre de colonnes d'adresse suit le contact le plus fourni du carnet**, pas un maximum fixe :
  une colonne vide sur tout le fichier est du bruit, et un plafond arbitraire perdrait des adresses.
- **`Favorite` est une colonne à nous.** C'est elle qui fait qu'un export réimporté ne perd pas les
  étoiles ; les autres clients l'ignorent.
- **Un champ est mis entre guillemets** s'il contient le séparateur, un guillemet, un CR ou un LF, et
  seulement alors — un fichier intégralement guillemeté est lisible mais illisible à l'œil.
- **Un champ de nom qui commence par `=`, `+`, `-`, `@`, une tabulation, un retour chariot ou une
  apostrophe sort précédé d'une apostrophe.** Le guillemetage CSV n'y change rien : Excel et
  LibreOffice jugent la formule au premier caractère du champ, donc un nom fabriqué — arrivé par
  l'import d'un CSV tiers et resté dormant dans le carnet — devient une formule chez celui qui
  exporte et ouvre le fichier. La neutralisation vit dans l'exportateur et non dans `CsvWriter` :
  celui-ci est une primitive CSV générale, ceci est une politique de contacts. L'import retire
  exactement une apostrophe de tête, donc la fidélité est entière ; **l'apostrophe est elle-même
  déclencheuse**, sans quoi un nom qui commence vraiment par une apostrophe sortirait nu et
  reviendrait amputé d'un caractère. Les colonnes d'adresse ne sont pas concernées : une adresse
  valable ne peut pas épeler une formule dangereuse — il y faut une espace, une parenthèse ou un
  guillemet qu'elle ne porte pas — et retirer une apostrophe de tête à une adresse venue d'ailleurs
  réécrirait l'adresse de quelqu'un.
- Nom du fichier : `contacts-AAAA-MM-JJ.csv`, via `Content-Disposition: attachment`.

### Conséquence assumée : l'aller-retour CSV n'est pas complet

Un fichier importé avec des téléphones, réexporté en CSV, **ressort sans eux**. Ils sont en base,
dans `vcard_raw` ; les relire demanderait un lecteur vCard, c'est-à-dire la tranche suivante. Rien
n'est perdu, seulement invisible jusque-là. C'est le prix de ne pas écrire un demi-lecteur vCard
maintenant pour le réécrire proprement ensuite.

## Ce qu'on ne modélise pas

Les colonnes hors modèle — téléphones, société, fonction, adresses postales, notes, anniversaire,
page web — sont traduites en **vCard 3.0** et rangées dans `vcard_raw`, la colonne posée en 3a
précisément pour ça. C'est l'argument même de cette spec-là : *une propriété jamais stockée ne se
retrouve nulle part*.

| Colonnes | Propriété |
|---|---|
| `Mobile Phone`, `Home Phone`, `Business Phone`, `Home Fax`, `Business Fax`, `Other Phone`, `Other Mobile` | `TEL` avec le `TYPE` correspondant |
| `Company`, `Department` | `ORG` |
| `Job Title`, `Title` | `TITLE` |
| `Home Street/City/State/Postal Code/Country`, idem `Business` | `ADR;TYPE=home` / `ADR;TYPE=work` |
| `Notes` | `NOTE` |
| `Birthday` | `BDAY` |
| `Web Page` | `URL` |

**Une colonne inconnue de cette table est ignorée.** Inventer un `X-` par intitulé exotique
polluerait toute synchronisation CardDAV future pour une donnée que rien ne relira jamais.

**Le vCard n'est écrit que si la ligne portait au moins une de ces colonnes remplie.** Une fiche
purement nom + adresses laisse `vcard_raw` à `NULL` plutôt qu'une copie redondante de ses propres
colonnes — un `MEDIUMTEXT` par contact n'est pas gratuit sur un carnet de plusieurs milliers.

**Note pour la tranche vCard.** `UpdateAsync` laisse déjà `vcard_raw` intact par décision de 3a, donc
son `FN` et ses `EMAIL` vieillissent dès la première édition du contact. L'export vCard devra
**superposer les champs de la table par-dessus le brut**, jamais faire confiance à ceux du brut.

## Backend

`ContactsController`, `[Authorize]`, toujours sans cookie de credentials : c'est de la donnée
webmail, aucune session IMAP.

| Route | Verbe | Corps | Réponses |
|---|---|---|---|
| `/api/Contacts/Import` | POST | multipart, champ `file` | 200 le rapport, 400, 413 |
| `/api/Contacts/Export` | GET | — | 200 `text/csv` en pièce jointe |

### Le rapport

```
{ created, merged, skipped, failed, errors: [ { line, reason } ] }
```

`errors` est **plafonnée à 50 entrées**, `failed` portant le total : un fichier entièrement mauvais ne
doit pas répondre dix mille messages à un écran qui en montrera dix. Les entrées identiques sont
dédoublonnées avant d'être comptées — le même `n/a` dans deux colonnes e-mail est un problème, pas
deux — et les phrases ne sont interpolées qu'**après** le tri et le plafond : un fichier de 5 Mo fait
quelque 870 000 lignes, et les formater toutes pour en répondre cinquante coûte des dizaines de Mo.

`line` est le numéro de ligne **dans le fichier**, en-tête comprise et donc à partir de 2, parce que
c'est ce que l'utilisateur lit dans son tableur — pas l'index de la ligne de données.

### Fusion

`ContactStore.ImportAsync` charge le carnet et ses adresses **une fois**, et construit un index
adresse canonique → contacts. Pour chaque ligne, adresses canonicalisées par
`IdentityResolver.Canonical` comme partout ailleurs, puis :

- **une adresse connue d'un seul contact** → fusion dans celui-là : adresses manquantes ajoutées en
  queue, prénom / nom / pseudo remplis **seulement s'ils étaient vides**, `vcard_raw` posé seulement
  s'il était nul, `IsFavorite` monté et jamais descendu, `updated_at` bougé. Rien n'est écrasé, donc
  rejouer le même fichier ne change rien la seconde fois ;
- **une adresse connue de plusieurs contacts** → ligne sautée, motif « adresse ambiguë ». C'est la
  question ouverte que 3a avait laissée à cette tranche ; sauter est la seule réponse qui ne choisit
  pas au hasard entre deux fiches, et une adresse partagée est un cas voulu, pas un accident ;
- **plusieurs adresses de la ligne désignant des contacts différents** → fusion dans **aucun**, ligne
  sautée avec le même motif : deux fiches qu'une ligne prétend réunir sont une décision de
  déduplication que l'utilisateur n'a pas demandée ;
- **sinon** → création, `source = "imported"` — la valeur est déjà dans `ContactValidator.KnownSources`.

Un contact créé par l'import est aussi un contact que la ligne suivante peut retrouver : l'index est
tenu à jour au fil des lignes, sinon un fichier portant deux fois la même adresse créerait deux
fiches.

#### À défaut d'adresse, le nom exact

Un contact sans aucune adresse est un cas de plein droit ici — le validateur l'accepte, la tuile le
dessine, l'export lui écrit une ligne. Il est en revanche invisible à un index bâti sur les adresses,
donc réimporter notre propre export lui fabriquait un doublon, **et un de plus à chaque rejeu**. Cela
contredisait la promesse même sur laquelle repose l'absence d'aperçu.

Une ligne **qui ne porte aucune adresse valable** est donc cherchée dans un second index, nom →
contacts, qui ne contient **que les contacts sans aucune adresse**. Un contact qui en a n'y est
jamais atteignable, et c'est exactement juste : l'export écrit toujours les adresses d'un contact qui
en a, donc une ligne réduite à un nom ne peut décrire qu'un contact qui n'en a pas. Une ligne qui
porte une adresse ne retombe jamais sur le nom : l'adresse est le signal fort et elle a déjà tranché.

Le nom est normalisé **pour lui-même** — les trois parties coupées de leurs espaces, mises en
minuscules invariantes, jointes sur un caractère qu'aucun nom ne porte — et non par
`IdentityResolver.Canonical`, qui parle d'adresses et dont l'emprunt brouillerait ce que chacune des
deux veut dire. « Exactement le même nom » est ce qu'un utilisateur entend par là, et une comparaison
ordinale ferait de `Bruno` et `bruno` deux personnes.

Une seule correspondance → fusion dans celle-là, qui ne remplit en général rien et ne bouge donc pas
`updated_at`. **Plusieurs → ligne sautée**, avec un motif à elle : c'est le choix de l'adresse
ambiguë, mais la raison est un nom, et un rapport qui dirait « adresse » devant une ligne qui n'en
porte aucune serait illisible. Cet index-là est tenu à jour comme l'autre : un contact créé sans
adresse y entre, sinon un fichier nommant deux fois la même personne sans adresse laisserait deux
fiches.

**Un seul `SaveChangesAsync`, en fin de course.** L'import est tout ou rien : un échec à la
huit-centième ligne ne laisse pas un carnet à moitié importé qu'aucun écran ne sait décrire.

### Ce qui refuse, et à quel grain

**Une adresse illisible ne tue pas sa ligne** — elle est retirée et signalée, et la ligne passe si
elle garde un nom ou une adresse. Un `n/a` dans la quatrième colonne e-mail d'un export Outlook ne
doit pas faire perdre le contact. Le prédicat reste celui de `ContactValidator` (MimeKit sous
`RecipientAddressParser.Options`), exposé pour être appelé plutôt que réécrit — une seconde
définition de « adresse valable » finirait par diverger de celle du composeur.

**Une ligne sans nom ni adresse valable** est comptée en `failed` avec son numéro.

**Un fichier dont aucune colonne n'est reconnue est refusé en bloc**, 400 « aucune colonne
reconnue », plutôt qu'importé à vide. C'est ce qui attrape le fichier au mauvais séparateur, celui
sans ligne d'en-tête, et le fichier qui n'est pas un CSV du tout.

**Le plafond `ContactStore.MaxPerUser` est compté sur les créations seules** ; une fois atteint, les
lignes restantes sont rapportées en `skipped` avec son message interpolé, jamais par une exception.
Une fusion ne consomme pas de quota, puisqu'elle ne crée rien.

**Le corps est plafonné à 5 Mo par un filtre de ressource**, avant le binding de modèle, sur le modèle
d'`AttachmentSizeLimitFilter` : `IFormFile` bufferise le corps entier (sur disque au-delà de 64 Ko)
avant que l'action ne s'exécute, donc un contrôle dans l'action arriverait après la dépense.
`AttachmentSizeLimitFilter` est un filtre parce que **son** plafond vient de la configuration et
qu'un argument d'attribut doit être une constante ; ici le plafond *est* une constante, donc
`[RequestSizeLimit(5 * 1024 * 1024)]` sur l'action, et pas un second filtre.

### Pas de dépendance CSV

Un `CsvReader` et un `CsvWriter` internes, de l'ordre de cent vingt lignes, plutôt que CsvHelper. La
surface dont on a besoin est close — découper des champs, respecter les guillemets, en réécrire — et
ce qu'une bibliothèque ne règle pas de toute façon (reniflage du séparateur, encodage, BOM) reste à
notre charge dans les deux cas. Une dépendance de plus se suit et se met à jour pour toujours.

### Découpage

| Fichier | Rôle |
|---|---|
| `Services/Csv/CsvReader.cs` | grammaire RFC 4180, séparateur, BOM, encodage |
| `Services/Csv/CsvWriter.cs` | écriture et guillemetage |
| `Services/Contacts/ContactCsvMapper.cs` | table d'intitulés → lignes structurées + colonnes résiduelles |
| `Services/Contacts/ContactVCardWriter.cs` | colonnes résiduelles → vCard 3.0 |
| `Services/Contacts/ContactCsvExporter.cs` | carnet → texte CSV |
| `Repositories/ContactStore.ImportAsync` | index, fusion, plafond, une seule transaction |
| `Models/Contacts/ContactImportReport.cs` | le rapport |

Chacun est pur sauf le dernier : le lecteur ne connaît pas les contacts, le mappeur ne connaît pas la
base, et le store ne connaît pas le CSV. C'est ce qui les rend testables séparément, et c'est aussi
ce qui permettra au lecteur vCard de la tranche suivante de se brancher sur la même fusion.

## Frontend

Deux boutons en **pied de la colonne des portées**, `Import…` et `Export`, dans un
`contacts/ContactsTransfer.tsx` qui porte aussi l'`<input type="file">` caché et la modale de
rapport — `ContactsLayout` ne grossit pas d'une ligne de plus que le rendu du composant. La bande
avait été gardée pour ça en 3a ; un pied de colonne est déjà la grammaire du module Mail avec son
bloc identité.

- `Import…` ouvre le sélecteur (`accept=".csv,text/csv"`), et le fichier choisi part aussitôt : il n'y
  a pas d'étape intermédiaire à confirmer. La valeur de l'input est remise à zéro après coup, sinon
  rechoisir le même fichier ne déclenche aucun événement.
- La mutation invalide `['contacts', accountId]` **`onSettled`, pas `onSuccess`**, comme ses quatre
  sœurs : un import refusé doit laisser l'écran sur l'état du serveur.
- `ImportReportModal.tsx` : les quatre compteurs, la liste des lignes refusées avec leur numéro et
  leur motif, la ✕ comme seule sortie — la règle de dialogue du site.
- `Export` appelle `requestBlob`, qui lit déjà `Content-Disposition`, et déclenche le téléchargement.
  Il est **désactivé sur carnet vide**, avec l'infobulle qui le dit : un fichier à zéro ligne se lit
  comme une panne.

## Gestion d'erreur

- **Fichier vide, aucune colonne reconnue, pas de fichier** — 400, message porté dans la modale.
- **Fichier trop gros** — 413 du framework, ou 400 selon le chemin ; le client traite les deux comme
  « fichier trop volumineux ».
- **Plafond atteint** — pas une erreur : les lignes concernées sont dans `skipped` avec le message de
  `ContactStore.CapReached`.
- **Export en échec** — toast, la page ne bouge pas.
- **401** — chemin existant : `api.js` efface la session, le rendu suivant redirige vers `/login`.
- **Succès et échec parlent tous les deux.** L'import passe par sa modale, l'export par un toast.

## Tests

**Backend.** Le lecteur : guillemets, séparateur et saut de ligne à l'intérieur d'un champ, guillemet
doublé, `;`, tabulation, BOM, Latin-1, ligne plus courte que l'en-tête. Le mappeur : les trois
en-têtes réels (Rainloop/Outlook, Google, Thunderbird), casse et espaces, ordre des adresses tiré de
l'ordre des colonnes, nom affiché employé seulement en repli, colonne inconnue orientée vers le
vCard. L'écrivain vCard : chaque famille de propriétés, et le `NULL` quand il n'y avait rien à
conserver. L'import : création, fusion sans écrasement, favori monté jamais descendu, adresse
ambiguë sautée, adresse illisible retirée sans perdre la ligne, deux lignes de même adresse dans un
même fichier, plafond, isolation entre deux `user_id`, `source = "imported"`, et le repli sur le nom
— fusion dans un contact sans adresse, création quand le contact de ce nom en a, nom porté par deux
contacts sans adresse sauté, même nom deux fois dans un fichier. L'export : ordre des colonnes,
nombre de colonnes d'adresse tiré du carnet, échappement, BOM, neutralisation d'un nom en `=` et d'un
nom en `'` qui reviennent tous deux identiques, et **l'aller-retour export → import qui ne crée rien
et ne change rien** — sur un carnet portant un contact sans adresse et un contact réduit à un pseudo,
faute de quoi le trou que ce repli comble ne serait pas couvert. Le contrôleur : le rapport, 400 sans fichier,
400 sur en-tête non reconnu, en-têtes de réponse de l'export.

**Frontend.** Le pied de bande rend les deux boutons ; le fichier choisi déclenche la mutation ; la
modale rend les compteurs et la liste des erreurs ; l'export est désactivé sur carnet vide ; le cache
est invalidé après un import réussi **et** après un import refusé.

## Hors périmètre

- **vCard**, à l'import comme à l'export — tranche suivante.
- **Mappage manuel des colonnes** — la table d'intitulés couvre les exports réels ; un écran de
  mappage n'apparaîtra que si un fichier réel lui résiste.
- **Aperçu avant écriture** — la fusion n'écrase rien et rejouer un fichier ne crée rien, donc il n'y
  a pas de dégât à prévisualiser.
- **Groupes et listes de diffusion** — toujours pas de table, toujours pas d'UI.
- **Photo de contact** — un CSV n'en porte pas.

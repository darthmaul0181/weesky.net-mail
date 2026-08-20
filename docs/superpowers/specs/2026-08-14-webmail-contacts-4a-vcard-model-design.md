# Contacts 4a — modèle complet et moteur vCard

Première tranche du projet CardDAV, à la suite de
[3a/3b](2026-07-27-webmail-contacts-3a3b-design.md), [3c](2026-07-27-webmail-contacts-3c-design.md)
et [3d](2026-07-27-webmail-contacts-3d-design.md). Backend seul : aucun écran ne change.

## Le projet dont c'est la première pièce

Le module Contacts modélise aujourd'hui un nom, un pseudo, un drapeau favori et des adresses
e-mail. Tout le reste — téléphones, adresses postales, société, anniversaire, notes, photo — vit
dans `vcard_raw`, que rien ne lit. Ouvrir le carnet à CardDAV signifie donc d'abord le compléter,
faute de quoi une fiche venue d'un téléphone s'afficherait amputée.

Le projet se décompose en quatre tranches, aux dépendances strictes :

| | Tranche | Dépend de |
|---|---|---|
| **4a** | Modèle de données complet + moteur d'aller-retour vCard *(ce document)* | — |
| 4b | Éditeur et fiche webmail étendus | 4a |
| 4c | Serveur CardDAV (découverte, collection, rapports, verbes, ETags, pierres tombales) | 4a |
| 4d | Conformité clients (`ccs-caldavtester`, Thunderbird, DAVx⁵, iPhone emprunté) | 4c |

L'ordre retenu est **4a → 4b → 4c → 4d** : le carnet devient complet et utile avant qu'aucun
protocole ne s'appuie dessus, ce qui met le moteur vCard à l'épreuve d'une édition réelle avant
qu'un client externe n'en dépende.

## Ce que fait la tranche

Les tables du module accueillent les propriétés vCard que le carnet ignorait. `vcard_raw` cesse
d'être un dépôt inerte et devient **la donnée** : les colonnes en sont une projection recalculée à
chaque écriture. Un découpeur, un lecteur et un écrivain de vCard apparaissent, l'import accepte le
`.vcf` en plus du CSV, et un rattrapage réconcilie les fiches que 3d a laissées avec une carte
périmée.

Aucun écran ne bouge. Les nouveaux champs sont stockés, projetés, exportés — pas encore affichés.

## Décisions

**1. La carte est souveraine ; les colonnes sont une projection.** `vcard_raw` porte la vérité ;
les colonnes et les tables filles qui décrivent **le contact** n'existent que pour l'affichage, la
recherche, le tri, l'autocomplétion et l'export. Quatre colonnes échappent à la règle et ne sont
projetées de rien : `id`, `user_id`, `source` et `is_favorite` — l'étoile n'a pas de propriété
vCard, et le dire ici évite qu'un `PUT` CardDAV l'éteigne en 4c. `uid` est le cas mixte : il est lu
de la carte à la création puis figé, jamais reprojeté. Trois portes d'entrée, une seule sortie :

```
   édition webmail ──────▶ carte stockée + champs modifiés ──▶ nouvelle carte ──┐
   import qui fusionne ──▶ carte existante + champs fusionnés ─▶ nouvelle carte ├──▶ vcard_raw ──parse──▶ projection
   carte importée / PUT CardDAV (4c) ──── carte reçue, verbatim ────────────────┘
```

L'import est une porte comme les autres : une fusion qui remplit des colonnes recompose la carte
par le composeur, sans quoi les colonnes divergent de la carte au premier import fusionnant. Et
quand une carte `.vcf` fusionne dans une fiche qui a déjà la sienne, les propriétés non modélisées
de la carte **entrante** sont perdues — celles de la carte existante survivent, c'est elle qui
fait foi. Quand la fiche visée n'en a **aucune**, en revanche, la carte entrante est posée
verbatim : c'est la troisième porte, et la seule qui préserve ses `X-`. Verbatim à une ligne
près : une carte qui ne déclare aucun `UID` s'en voit insérer un, égal à la colonne, juste après
`VERSION` — l'invariant vaut pour toute carte stockée, et le synthétiser au moment de servir ferait
diverger les octets servis des octets hachés. Celle qui en déclare un n'est jamais touchée :
remplacer son `UID` ferait tourner l'identité sur laquelle un client se synchronise.

L'inverse — colonnes souveraines, carte régénérée à la lecture — a été écarté sur une conséquence
qui ne se paie qu'en 4c : une carte reconstruite n'est jamais octet-pour-octet identique à celle
qui est arrivée (repliement des lignes, ordre des propriétés, casse des paramètres), donc son ETag
change sans qu'aucune donnée n'ait changé, et un client re-télécharge le carnet entier à chaque
passe. RFC 6352 (§6.3.2.3) le dit deux fois, et la seconde est la plus contraignante : d'une part
un serveur ne rend un ETag **fort** en réponse à un `PUT` que si ce qu'il a stocké est équivalent
octet à octet à ce qui lui a été soumis ; d'autre part `DAV:getetag` **MUST** être un ETag fort sur
*toute* ressource d'adresse — il n'y a donc pas d'échappatoire par l'ETag faible. Seule la carte
stockée verbatim y donne droit. Le socle posé en 3a/3b pointait déjà dans cette direction : `vcard_raw` a été créé pour « ne
pas détruire ce qu'on ne modélise pas » et `uid` pour être « l'identité sur laquelle un client
CardDAV se synchronise ».

**2. On ne re-sérialise jamais une carte qu'on n'a pas modifiée.** C'est la règle d'or de la
tranche, et elle est ce qui rend l'ETag stable en 4c. Elle a une raison technique précise, donnée
en décision 3.

**3. La projection est totale et destructrice, jamais incrémentale.** À chaque écriture les lignes
filles sont effacées et réécrites depuis la carte. Une projection qui « met à jour ce qui a changé »
diverge silencieusement de la carte, et rien ne peut détecter la divergence.

**4. Le composeur ne réécrit jamais une propriété entière : il en remplace la valeur en place.**
C'est le corollaire dangereux de la décision 1, et c'est lui qui commande la forme du schéma. Une
propriété vCard porte bien plus que sa valeur — un groupe, un bloc de paramètres, et depuis
RFC 9554 des composantes que nous ne modélisons pas — et **tout ce qu'une réécriture ne reconstruit
pas, elle détruit**. Plutôt que de tout modéliser pour pouvoir tout reconstruire, le composeur
retrouve la propriété existante et n'en touche que ce qui a changé :

- **Chaque ligne fille est appariée à sa propriété par `position`**, qui est le **rang de la
  propriété de ce nom dans la carte** — la première `TEL` est en position 0, la deuxième en 1. La
  fiche transporte cette position, le `PUT` de 4b la rend, et le composeur va chercher la Nième
  `TEL` pour n'en remplacer que le numéro. Groupe, `PREF`, `PID`, `ALTID`, `LABEL`, `X-` :
  jamais relus, jamais réécrits, donc jamais perdus. Une ligne que l'éditeur **ajoute** arrive sans
  position et naît en fin de carte, avec pour seul paramètre le `TYPE` que le champ `type` porte ;
  sur une ligne existante, un `type` qui change remplace le seul paramètre `TYPE` — en préservant
  le jeton `PREF` qu'un 3.0 y loge (`TYPE=WORK,PREF`) — et ne touche à rien d'autre du bloc ;
  une ligne **retirée** voit sa propriété supprimée, et la re-projection renumérote tout le monde.
- **`ADR` et `N` sont remplacées composante par composante.** Les sept composantes de RFC 6350 pour
  `ADR`, les cinq pour `N`, et **rien au-delà** : RFC 9554 a porté `ADR` à dix-huit composantes
  (étage, numéro de rue, quartier, point de repère…) et `N` à sept (second nom de famille,
  génération), la bibliothèque les lit et les réécrit par défaut, et une réécriture en bloc les
  effacerait toutes. Elles restent sur la propriété, intouchées. Corollaire de lecture imposé par
  RFC 9554 : *si* une `ADR` porte l'une des nouvelles composantes, la composante « rue » est un
  doublon de leur valeur combinée et la projection l'ignore, sans quoi la fiche affiche la rue deux
  fois. La boîte postale reste modélisée bien que RFC 6350 la déclare dépréciée (« plagued with many
  interoperability issues ») : les cartes 3.0 des téléphones s'en servent, et l'écrivain actuel écrit
  déjà `null` en composante 0. Une composante multi-valuée (des valeurs séparées par des virgules,
  licites en 4.0, dans `N` comme dans `ADR`) est stockée jointe telle quelle, jamais réduite à sa
  première valeur.
- **`params` et `group_name` sont stockés pour l'affichage seul, et n'entrent jamais.** La colonne
  `params` porte le bloc complet tel qu'il figure sur la carte (`TYPE=WORK,VOICE;PREF=1`) et
  `group_name` le groupe (`item1`), pour que 4b puisse dire « professionnel, préféré » ou rendre le
  libellé Apple d'un `item1.X-ABLabel`. Le `PUT` ne les accepte pas : le composeur ne s'en sert
  jamais, donc les rendre acceptables n'ouvrirait qu'un chemin par lequel un `\r\n` dans un
  paramètre injecterait une propriété — voire un `END:VCARD` — dans la carte que 4c enverra à tous
  les clients de l'utilisateur. C'est le remplacement en place qui rend cette porte inutile, et
  c'est sa principale vertu. `type` et `pref` restent des colonnes à part, extraites de `params` à
  la projection : la première pour l'affichage, la seconde pour l'ordre (décision 5 bis).
  `contact_emails`, qui n'a aucune de ces colonnes aujourd'hui, les reçoit toutes.

**La position est une poignée, donc une prise concurrente.** Deux onglets qui éditent la même fiche
peuvent rendre des positions calculées sur deux états de la carte. La fenêtre est celle d'un
`GET`/`PUT` sur un carnet personnel, et le dégât est une valeur posée sur la mauvaise ligne, pas
une carte détruite ; 4b la refermera avec `card_hash` en `If-Match` s'il le juge utile.

**5. Une propriété répétable modélisée comme valeur unique n'est remplacée qu'en première
occurrence.** `URL` peut figurer plusieurs fois ; la colonne `website` n'en porte qu'une. Le
composeur remplace la première et laisse les suivantes en place, plutôt que de les écraser toutes.
La règle vaut pour toute propriété dans ce cas ; `EMAIL`, `TEL` et `ADR`, projetées intégralement,
sont appariées une à une par `position` (décision 4). Vider le champ obéit à la même règle : la première occurrence est
supprimée, les suivantes restent. Conséquence visible, à documenter pour 4b : la re-projection
promeut alors l'ancienne seconde occurrence, et le champ qu'on vient de vider se re-remplit —
cohérent avec la doctrine, mais surprenant si personne ne l'a écrit.

**5 bis. L'ordre de stockage suit la carte, l'ordre d'affichage suit `PREF`.** La décision 4 a fait
de `position` le rang dans la carte ; ce n'est pas l'ordre dans lequel l'utilisateur veut voir ses
adresses. Apple et Google ne réordonnent pas leurs propriétés pour désigner la principale, ils la
marquent — `PREF=1` en 4.0, `TYPE=PREF` en 3.0. Une colonne `pref` par ligne fille porte cette
valeur normalisée (1 à 100, **101 quand la carte n'en dit rien**, et `TYPE=PREF` vaut 1), extraite
de `params` à la projection comme `type` l'est. La liste, la fiche, l'autocomplétion et l'export
trient sur `(pref, position)` : un tri stable, écrit en SQL, sans reparser quoi que ce soit. Sans
cette colonne, une fiche venue d'un téléphone dont la seconde `EMAIL` est la préférée afficherait
l'autre partout — y compris dans le champ « À » d'un message.

**6. `FolkerKinzel.VCards` plutôt qu'un parseur maison.** Elle lit et écrit 2.1, 3.0 et 4.0,
implémente RFC 6868, RFC 9554 et RFC 9555, et préserve les propriétés non standard, ce qui n'est pas
accessoire : une carte iPhone est truffée de `X-AB*` groupées, et un moteur qui les perd détruit la
moitié de son information à la première édition. Un parseur maison a été envisagé et écarté —
dépliage des lignes, échappement RFC 6868, quoted-printable en 2.1, propriétés groupées, photos
base64 : le volume d'une bibliothèque, et chacun de ces cas rate en silence. La bibliothèque est sous
licence MIT, cible `netstandard2.0` et `net8.0`, et est activement maintenue (8.2.0 en juillet 2026) :
rien n'empêche la dépendance.

**Les options par défaut sont insuffisantes, et pas d'un seul cran.** `VcfOpts.Default` vaut
`WriteGroups | WriteRfc6474Extensions | WriteRfc6715Extensions | WriteImppExtension |
WriteXExtensions | AllowMultipleAdrAndLabelInVCard21 | UpdateTimeStamp | WriteRfc2739Extensions |
WriteRfc8605Extensions | WriteRfc9554Extensions | WriteRfc9555Extensions`. Quatre conséquences :

- **`WriteNonStandardProperties` et `WriteNonStandardParameters` sont tous deux absents**, et il faut
  les deux. Le premier seul sauve les propriétés `X-` et laisse disparaître les **paramètres** `X-`
  que la décision 4 existe pour préserver. C'est cette perte-là que le test de survie doit voir
  rougir, et elle passe inaperçue si l'on ne teste que les propriétés.
- **`SetPropertyIDs` doit rester dehors.** Il réécrit les `PID` et le `CLIENTPIDMAP` à chaque
  sérialisation, c'est-à-dire exactement l'identité fine que la décision 4 préserve. Il est hors des
  options par défaut aujourd'hui ; un test l'épingle, plutôt que d'en dépendre par chance.
- **`UpdateTimeStamp` y est déjà** : le rafraîchissement du `REV` promis par § Le moteur est gratuit,
  aucun code à écrire.
- **`WriteGroups` y est déjà** aussi, donc les groupes survivent sans réglage — mais voir la réserve
  ci-dessous sur `X-ABLabel`.

**Le bon exemple n'est pas `X-ABLabel`.** La v8 le **modélise** (au même rang que `JSPROP`,
`JSCOMPS` et `JSPTR`, via RFC 9555) : c'est le seul `X-AB*` que la bibliothèque comprend, et il peut
donc être **déplacé** hors de son groupe d'origine à la réécriture. L'argument porte sur ceux qu'elle
ne modélise pas — `X-ABADR`, `X-ABRELATEDNAMES`, `X-ABDATE`, `X-ABShowAs`, `X-ABUID` — et le sort
réel de `X-ABLabel` est à **constater sur le corpus** (§ Les tests), pas à déduire.

**La réserve, et elle fonde la règle d'or.** La bibliothèque convertit toute carte lue en 4.0 en
interne et la reconvertit à l'écriture ; un aller-retour n'est donc jamais octet-pour-octet. Sous
la décision 1 ce n'est pas un problème, puisqu'on ne sérialise que lorsqu'une modification a eu
lieu et que l'ETag doit alors changer de toute façon. C'est aussi pourquoi l'aller-retour
octet-pour-octet **n'est pas testé : il n'est pas promis**.

**7. Le composeur émet dans la version de la carte qu'il modifie ; une carte neuve naît en 3.0.**
Une carte 4.0 éditée reste 4.0, une 3.0 reste 3.0 : émettre toujours du 3.0 ferait perdre à une
carte 4.0 ses propriétés propres à la première édition webmail — la perte silencieuse que ce
document combat partout ailleurs. Seul le 2.1 est promu 3.0 : le format est obsolète et le
resérialiser le dégrade de toute façon. Le 3.0 des cartes neuves est la version qu'Apple et Google
produisent et attendent — et surtout celle que RFC 6352 impose par défaut à un serveur CardDAV :
sans `supported-address-data` annonçant mieux, un client est en droit de n'envoyer et n'attendre
que du 3.0, donc la valeur par défaut est celle du protocole de 4c, pas seulement celle du marché.
Et une carte stockée qu'on ne modifie pas n'est jamais convertie — on ne la touche pas
(décision 2).

**8. Le lecteur est tolérant ; il ne refuse jamais une carte.** Une propriété illisible est ignorée,
jamais fatale. Une carte refusée en projection est une carte qu'un client re-poussera indéfiniment,
et le refus se manifesterait en 4c par une boucle de synchronisation qu'aucun journal client ne
saurait expliquer. La tolérance descend jusqu'aux colonnes : une valeur qui déborde la sienne — un
`TYPE` interminable, un numéro hors gabarit — est tronquée à la projection au lieu de faire échouer
l'`INSERT`, que MariaDB en mode strict transformerait sinon en refus de carte. `ContactValidator`
ne garde que la porte éditeur ; la carte, elle, conserve la valeur entière — jusqu'à la première
édition seulement : le composeur ré-émet alors la valeur tronquée que la fiche lui rend, et la
carte perd à son tour l'excédent. Perte assumée ; l'alternative serait de refuser la carte,
exactement ce que cette décision interdit. `params`, devenu une colonne d'affichage que rien ne
ré-émet (décision 4), se tronque sans conséquence pour la carte : au pire 4b affiche un bloc de
paramètres coupé.

**Une exception nommée : l'adresse e-mail.** Une `EMAIL` qui déborde `VARCHAR(320)`, ou que
`ContactValidator.IsValidAddress` refuse, **n'est pas projetée du tout** — la ligne est abandonnée,
la carte la garde entière, la fiche a une adresse de moins. Tronquer une adresse ne fabrique pas une
valeur dégradée mais un destinataire faux, qui part ensuite dans la tuile, dans l'autocomplétion,
dans l'export CSV et un jour dans un vrai envoi. Le prédicat est celui de `ContactValidator`, déjà
exposé en 3d pour être appelé plutôt que réécrit.

**9. `card_hash` remplace `updated_at` comme base de l'ETag.** Le document de schéma de 3a/3b
désignait `updated_at`, faute de mieux, avant que la carte ne soit souveraine. Un SHA-256 de la
carte est exact — deux écritures dans la même seconde ne collisionnent pas, et une écriture qui ne
change rien ne change pas l'ETag. La colonne est posée ici parce qu'elle est gratuite maintenant et
qu'une migration de plus en 4c ne l'est pas.

**10. `display_name` est stocké.** `FN` est obligatoire en vCard et le frontend le devine
aujourd'hui (prénom + nom, sinon pseudo, sinon première adresse). Une carte portant
`FN:Dr. John Smith Jr.` s'afficherait « John Smith ». La colonne le capture ; `displayNameOf` la
préférera en 4b, en gardant la chaîne de repli actuelle pour les fiches qui n'en portent pas.

**11. `birthday` est du texte, pas une `DATE`.** vCard admet les dates partielles — `--0315`, un
anniversaire sans année — et du texte libre en 4.0. Une colonne `DATE` refuserait des cartes
parfaitement valides. La forme vCard est stockée telle quelle ; l'interprétation est un problème
d'affichage, donc de 4b. À l'écriture, le composeur émet la valeur telle quelle **quelle que soit
la version de la carte**, 3.0 compris : `BDAY:--0315` y est non conforme à la lettre de RFC 2426
mais toléré par les clients réels, et toute alternative — l'omettre, ou la convention propriétaire
`X-APPLE-OMIT-YEAR` — perd ou déforme la donnée. Un test épingle ce que la bibliothèque produit
ici, et la force si elle prétend mieux savoir. **Deux formes circulent** et la colonne les porte
toutes les deux : `--03-15` (3.0, ISO 8601 étendu) et `--0315` (4.0, forme de base). Le lecteur de
4b doit accepter les deux, plus l'année seule, plus le texte libre.

**12. La photo est une table de projection, et ne descend jamais dans la liste.** `PHOTO` est du
base64 en ligne, couramment 50 à 300 Ko. Or `GET /api/Contacts` rend le carnet entier en une
réponse — c'est le choix documenté, la recherche et le tri étant côté client. Deux mille contacts
avec photo dans une seule réponse est un chiffre qu'on ne veut pas écrire. La photo sort par une
route dédiée ; la liste ne porte qu'un booléen. La duplication avec `vcard_raw` est voulue et
cohérente avec la décision 1 : une projection est dérivée par définition, et sans elle servir un
avatar signifie charger la carte entière. La projection prend la **première occurrence** de
`PHOTO` (décision 5), et seulement si son type est une image matricielle — JPEG, PNG, GIF, WebP :
un SVG est du XML exécutable, pas un avatar, et il reste dans la carte sans être servi. Le
`TYPE=JPEG` du 3.0 est traduit en `image/jpeg` à la projection, la route servant un type MIME et
non un mot vCard. **Le critère n'est pas `VALUE=URI`** : en 4.0 *toute* `PHOTO` est un URI (RFC 6350
§6.2.4), photo embarquée comprise, qui y prend la forme d'un `data:` URI — s'y fier ne projetterait
aucune photo 4.0. Ce qui décide est le schéma : un `data:` est projeté, un `http(s):` ou tout autre
schéma ne l'est pas — le serveur ne va rien chercher, un fetch commandé par une carte est une SSRF —,
`hasPhoto` reste faux et la propriété survit dans la carte. Enfin la photo n'a pas de porte
d'écriture dans cette tranche : le composeur ne traite pas `PHOTO` et `POST`/`PUT` ne l'accueillent
pas, seule une carte importée en pose une ; l'éditeur de 4b l'ajoutera.

**13. La liste reste maigre ; la fiche complète devient une route.** `GET /api/Contacts` ne gagne ni
téléphones ni adresses postales : la liste n'affiche que le nom et l'adresse, et le carnet entier y
descend d'un coup. Elle gagne en revanche `display_name` et `hasPhoto`, dont les tuiles ont
besoin — sans le premier, `displayNameOf` n'aurait rien à préférer en 4b, la liste étant son seul
matériau (décision 10). `GET /api/Contacts/{id}` apparaît pour la fiche. C'est le changement de forme le
plus visible pour 4b, où la carte de droite passe d'un rendu depuis le cache à un appel dédié.

**14. L'import `.vcf` entre dans le périmètre.** Le lecteur existe désormais ; l'import vCard est
quasi gratuit, et c'est le format que les gens exportent réellement de leur téléphone. Il emprunte
la route et les règles de fusion de l'import CSV de 3d — jamais d'écrasement — avec une clé de
plus, placée devant : le `UID` de la carte, avant l'adresse, avant le nom. Un UID déjà connu de
l'utilisateur désigne le même contact et fusionne ; c'est la sémantique CardDAV — le UID est
l'identité sur laquelle un client se synchronise —, elle rend le réimport idempotent, et sans elle
une carte réimportée après un changement d'adresse violerait `uq_contacts_user_uid` : un 500 pour
le fichier entier, l'import étant un seul `SaveChanges`. Une carte créée adopte le UID qu'elle
porte ; sans UID, l'id généré sert, comme aujourd'hui. Une carte au-delà du plafond de 1 Mo est
une ligne en erreur, comme les skips du CSV ; un UID qui déborde la colonne (`VARCHAR(255)`)
aussi — le tronquer fabriquerait une identité de synchronisation que la carte ne porte pas.

**L'index des UID est tenu à jour au fil du fichier**, exactement comme l'index des adresses et
l'index des noms de 3d, et pour la même raison écrite là-bas : deux cartes **neuves** portant le même
UID dans un seul fichier créeraient deux lignes de même `uid`, donc la violation de
`uq_contacts_user_uid` et le 500 sur tout le fichier que cette décision existe pour supprimer. La
seconde carte fusionne dans la première.

**Le fichier est découpé avant d'être parsé.** `Vcf.Parse` rend une liste de cartes et **aucun accès
au texte source de chacune** : parser le fichier en bloc ne laisserait pour `vcard_raw` qu'une
re-sérialisation, c'est-à-dire précisément ce que la décision 1 interdit et un ETag faux dès la
première synchro de 4c. Un découpage textuel préalable sur les frontières `BEGIN:VCARD` /
`END:VCARD` rend chaque carte verbatim ; c'est aussi le seul endroit où le plafond de 1 Mo **par
carte** est mesurable, et c'est lui qui donne à chaque carte le numéro de ligne que le rapport
d'erreur cite — celui de son `BEGIN:VCARD` dans le fichier, pour que l'utilisateur puisse aller y
regarder comme il le fait pour une ligne de CSV.

**15. Le rattrapage est un endpoint admin idempotent, et il travaille par lots.** Les fiches saisies
à la main portent `vcard_raw = NULL` ; sous la décision 1 toute fiche doit avoir une carte. La
génération passe par le composeur, donc c'est du code et non du SQL, et l'idempotence permet de le
rejouer après un correctif du moteur. **Un appel traite un lot de fiches et répond
`{ traitées, restantes }`** ; l'appelant relance tant qu'il reste quelque chose. C'est un balayage
sur tous les utilisateurs, jusqu'à 5000 fiches chacun : une passe unique tiendrait mal dans une
requête HTTP, et une tâche de fond demanderait un état de progression à stocker et un cas
« relancé pendant qu'il tourne » à traiter, pour un geste qu'on exécute une fois dans la vie du
déploiement. L'endpoint vit dans le core, sous la policy `Admin` — insatisfiable sur la plateforme
generic, sans conséquence : seul le déploiement weesky porte des fiches antérieures à cette tranche,
un déploiement generic né après n'a rien à rattraper. L'affirmation qu'aucune route core ne porte
la policy est amendée aux **deux** endroits où elle est écrite : le CLAUDE.md du microservice, et le
commentaire de `Configuration/SecurityConfiguration.cs` qui la justifie (« since no route it serves
carries it »).

## Schéma

À rejouer sur `snoopy_webmail` **et** `snoopy_webmail_dev`. Création manuelle : ce projet n'utilise
pas les migrations EF. Le document de prérequis
[`webmail-contacts-tables.md`](../webmail-contacts-tables.md) reçoit ces blocs comme il a reçu ceux
de 3c. Son paragraphe « Pourquoi `updated_at` est géré par le schéma », qui désignait la colonne
comme base du futur ETag, est amendé du même geste : la décision 9 la détrône.

La collation suit la règle déjà posée pour ces tables : `utf8mb4_bin` par défaut, et
`utf8mb4_unicode_ci` sur les seules colonnes de texte humain, pour qu'un `LIKE` serveur y reste
utilisable si une recherche apparaît un jour.

```sql
ALTER TABLE `contacts`
  ADD COLUMN `display_name` VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL
    COMMENT 'La propriété FN de la carte ; devinée côté client jusqu''ici' AFTER `nickname`,
  ADD COLUMN `middle_name`  VARCHAR(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `display_name`,
  ADD COLUMN `name_prefix`  VARCHAR(50)  COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `middle_name`,
  ADD COLUMN `name_suffix`  VARCHAR(50)  COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `name_prefix`,
  ADD COLUMN `organization` VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `name_suffix`,
  ADD COLUMN `department`   VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL
    COMMENT 'Composantes 2..n de ORG, jointes par ; comme sur la carte' AFTER `organization`,
  ADD COLUMN `job_title`    VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `department`,
  ADD COLUMN `birthday`     VARCHAR(64)  DEFAULT NULL
    COMMENT 'Forme vCard telle quelle : une date partielle (--0315) ou du texte libre est valide' AFTER `job_title`,
  ADD COLUMN `website`      VARCHAR(512) DEFAULT NULL
    COMMENT 'Première occurrence de URL ; les suivantes restent dans la carte' AFTER `birthday`,
  ADD COLUMN `notes`        TEXT COLLATE utf8mb4_unicode_ci DEFAULT NULL AFTER `website`,
  ADD COLUMN `card_hash`    CHAR(64) NOT NULL DEFAULT ''
    COMMENT 'SHA-256 hex de vcard_raw ; base de l''ETag CardDAV' AFTER `vcard_raw`;

-- À vérifier avant de jouer le bloc suivant : il doit répondre zéro ligne, sinon le DROP
-- PRIMARY KEY laisse une table qu'ADD PRIMARY KEY refusera.
SELECT `contact_id`, `position`, COUNT(*) FROM `contact_emails`
  GROUP BY `contact_id`, `position` HAVING COUNT(*) > 1;

ALTER TABLE `contact_emails`
  DROP PRIMARY KEY,
  ADD PRIMARY KEY (`contact_id`, `position`),
  ADD COLUMN `type`       VARCHAR(64) NOT NULL DEFAULT ''
    COMMENT 'TYPE extrait de params, pour l''affichage ; vide = sans type',
  ADD COLUMN `pref`       SMALLINT UNSIGNED NOT NULL DEFAULT 101
    COMMENT 'PREF normalisée (1..100) ; 101 = la carte n''en dit rien. Tri : (pref, position)',
  ADD COLUMN `params`     VARCHAR(255) NOT NULL DEFAULT ''
    COMMENT 'Bloc de paramètres verbatim (TYPE=WORK;PREF=1) ; affichage seul, jamais ré-émis',
  ADD COLUMN `group_name` VARCHAR(64) NOT NULL DEFAULT ''
    COMMENT 'Groupe de la propriété (item1.EMAIL) ; ce qui rattache un X-ABLabel Apple';

CREATE TABLE `contact_phones` (
  `contact_id` CHAR(36)          NOT NULL,
  `position`   SMALLINT UNSIGNED NOT NULL COMMENT 'Rang de la TEL dans la carte ; la poignée du composeur',
  `number`     VARCHAR(64)       NOT NULL COMMENT 'Tel que porté par la carte ; aucune canonicalisation',
  `type`       VARCHAR(64)       NOT NULL DEFAULT '',
  `pref`       SMALLINT UNSIGNED NOT NULL DEFAULT 101,
  `params`     VARCHAR(255)      NOT NULL DEFAULT '',
  `group_name` VARCHAR(64)       NOT NULL DEFAULT '',
  PRIMARY KEY (`contact_id`, `position`),
  CONSTRAINT `fk_contact_phones_contact`
    FOREIGN KEY (`contact_id`) REFERENCES `contacts`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `contact_addresses` (
  `contact_id`  CHAR(36)          NOT NULL,
  `position`    SMALLINT UNSIGNED NOT NULL COMMENT 'Rang de l''ADR dans la carte ; la poignée du composeur',
  `type`        VARCHAR(64)       NOT NULL DEFAULT '',
  `pref`        SMALLINT UNSIGNED NOT NULL DEFAULT 101,
  `params`      VARCHAR(512)      NOT NULL DEFAULT ''
    COMMENT 'Verbatim, LABEL compris — l''adresse formatée de 4.0 peut être longue',
  `group_name`  VARCHAR(64)       NOT NULL DEFAULT '',
  `po_box`      VARCHAR(64)  COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `extended`    VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `street`      VARCHAR(255) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `locality`    VARCHAR(128) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `region`      VARCHAR(128) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `postal_code` VARCHAR(32)  DEFAULT NULL,
  `country`     VARCHAR(128) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  PRIMARY KEY (`contact_id`, `position`),
  CONSTRAINT `fk_contact_addresses_contact`
    FOREIGN KEY (`contact_id`) REFERENCES `contacts`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `contact_photos` (
  `contact_id` CHAR(36)    NOT NULL,
  `media_type` VARCHAR(64) NOT NULL,
  `bytes`      MEDIUMBLOB  NOT NULL,
  PRIMARY KEY (`contact_id`),
  CONSTRAINT `fk_contact_photos_contact`
    FOREIGN KEY (`contact_id`) REFERENCES `contacts`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

**`position` est le rang de la propriété dans la carte, pas le rang d'affichage.** C'est la poignée
du composeur (décision 4) ; l'ordre que l'utilisateur voit sort de `ORDER BY pref, position`
(décision 5 bis). Le commentaire « 0 = adresse principale » que `contact_emails.position` porte
depuis 3a est faux à partir de cette tranche et est réécrit.

**Toutes les tables filles sont clés sur `(contact_id, position)` et non sur leur valeur** —
`contact_emails` comprise, dont la clé portait jusqu'ici l'adresse. Un même numéro peut
légitimement figurer deux fois sous deux types, deux adresses postales partager toutes leurs
composantes sauf le type, et une même adresse e-mail porter `TYPE=HOME` et `TYPE=WORK` sur deux
propriétés distinctes : sous l'ancienne clé, projeter cette carte-là violait la clé primaire, et
dédupliquer aurait fait de l'édition en bloc la destructrice de la seconde propriété — l'exception
exacte que la décision 4 interdit. La déduplication canonique reste le fait de l'éditeur et de la
fusion d'import ; la projection écrit ce que la carte porte, l'adresse canonicalisée en minuscules
comme le contrat de la colonne l'exige.

**`card_hash` est `NOT NULL DEFAULT ''`** plutôt que nullable : la chaîne vide dit « pas encore
calculé », ce qui est exactement l'état des lignes existantes avant le rattrapage, et évite un
troisième état à raisonner.

**Les relations EF doivent être déclarées** dans `PreferencesDbContext`. Sans arête déclarée, EF
ordonne les `INSERT` par nom de table et les tests InMemory, qui n'appliquent aucune clé étrangère,
ne peuvent pas attraper l'inversion.

**Et la clé EF de `ContactEmail` change aussi.** `PreferencesDbContext` déclare aujourd'hui
`HasKey(e => new { e.ContactId, e.Address })` ; elle devient `{ e.ContactId, e.Position }`. Sans ce
geste, le modèle EF et le schéma SQL divergent en silence — et les tests InMemory, qui n'ont pas de
clé étrangère à opposer, ne peuvent pas davantage attraper celle-là.

## Le moteur

Quatre composants purs, dans `Services/Contacts/`, testables sans base — le découpage que 3d avait
posé pour le CSV, prolongé :

| Fichier | Rôle |
|---|---|
| `VCardSplitter.cs` | fichier `.vcf` → cartes **verbatim**, sur les frontières `BEGIN`/`END:VCARD` (décision 14) |
| `VCardProjector.cs` | carte → `ContactProjection` |
| `VCardComposer.cs` | (carte existante \| rien) + `ContactWrite` → nouvelle carte |
| `VCardImportMapper.cs` | carte → `ContactImportRow`, pour brancher le `.vcf` sur la fusion de 3d |

**`VCardSplitter`** ne parse rien : il découpe. C'est ce qui rend chaque carte stockable verbatim,
mesurable contre le plafond de 1 Mo, et citable par le numéro de ligne de son `BEGIN:VCARD`.

**`VCardProjector`** — carte → `ContactProjection`, un record portant les noms, les adresses e-mail,
les téléphones, les adresses postales, les scalaires et la photo. Chaque ligne fille porte son rang
dans la carte, son `type`, sa `pref` normalisée, son bloc `params` et son groupe. Ne touche à rien :
c'est le store qui écrit.

**`VCardImportMapper`** — carte → `ContactImportRow`, en réutilisant le projecteur plutôt qu'en
relisant la carte : le `.vcf` entre alors dans la fusion de 3d telle quelle, avec le `UID` comme clé
placée devant l'adresse et le nom.

**`VCardComposer`** — `(carte existante | rien) + ContactWrite` → nouvelle carte. Il **remplace des
valeurs, il ne réécrit pas des propriétés** (décision 4) : `EMAIL`, `TEL` et `ADR` sont appariées par
`position` au rang correspondant dans la carte et n'y voient changer que leur valeur — pour `ADR`,
seulement ses sept premières composantes ; `N` de même, sur ses cinq premières. Groupe, paramètres,
composantes RFC 9554 restent sur la propriété, jamais relus. Une ligne sans position est ajoutée en
fin de carte avec le seul `TYPE` que la fiche porte ; une position absente de la fiche voit sa
propriété supprimée. Les scalaires (`FN`, `NICKNAME`, `ORG`, `TITLE`, `NOTE`, `BDAY`, la première
`URL`) sont remplacés en place selon la même règle. Une carte née ici — création manuelle, ligne CSV,
rattrapage — porte `VERSION:3.0` et `UID`, la valeur de la colonne `uid` : une carte sans UID n'a
pas d'identité de synchronisation, et une carte dont le UID diverge de la colonne serait dupliquée
par le premier client DAV venu. Sur une carte existante, la version de sortie est celle de la
carte (décision 7). Deux invariants valent pour **toute** carte qui sort du composeur, née ici ou
non : elle porte un `UID` égal à la colonne — les cartes de `ContactVCardWriter` antérieures à
cette tranche n'en ont pas, et RFC 6352 exige un UID par ressource, donc le composeur l'ajoute
s'il manque — et son `REV` est rafraîchi, ce qui est exact par construction puisqu'une
sérialisation n'a lieu que sur modification (décision 2).

`ContactVCardWriter`, qui fabrique aujourd'hui une carte depuis une ligne CSV, **est absorbé par
`VCardComposer`**. Deux écrivains de vCard dans le même projet, c'est le doublon qui diverge. Sa
règle propre disparaît avec lui : il rend `null` quand la ligne CSV ne portait aucune colonne hors
modèle, pour ne pas coucher un `MEDIUMTEXT` redondant par contact — l'économie de 3d, que la
décision 1 ne permet plus. Toute fiche a une carte, y compris celle qui n'est qu'un nom, sinon
l'invariant est rompu par le premier import CSV du lendemain du rattrapage.

Le calcul de `card_hash` vit dans le store, à l'endroit unique où `vcard_raw` est écrit — un hash
calculé par les appelants est un hash qu'un appelant oubliera. C'est là aussi qu'une carte sans
`UID` reçoit celui de sa colonne, avant le plafond et avant le hash (décision 1) : le composeur ne
couvre que ce qu'il produit, le point d'écriture couvre la voie verbatim avec.

**`uid` et `source` restent intouchés par l'édition**, comme aujourd'hui : le premier est l'identité
sur laquelle un client se synchronise et le réécrire dupliquerait la fiche à sa prochaine passe ; le
second enregistre une origine que modifier une fiche ne change pas. La règle est déjà écrite dans
`ContactStore.UpdateAsync` ; elle survit à cette tranche telle quelle, `vcard_raw` mis à part, qui
cesse précisément d'être intouchable.

## Le contrat d'API

| Route | Changement |
|---|---|
| `GET /api/Contacts` | Inchangée en substance ; gagne `display_name` et `hasPhoto`. Ni téléphones ni postales. |
| `GET /api/Contacts/{id}` | **Nouvelle.** La fiche complète. Chaque ligne fille porte sa `position` — la poignée que le `PUT` doit rendre — plus `type`, `pref`, `params` et `group_name` pour l'affichage. 200 / 404. |
| `GET /api/Contacts/{id}/Photo` | **Nouvelle.** Binaire, `nosniff`, disposition attachement, `ETag` = `card_hash` et 304 sur `If-None-Match`. 200 / 304 / 404. |
| `POST /api/Contacts` | Le corps accueille les nouveaux champs ; la réponse reste la fiche validée. |
| `PUT /api/Contacts/{id}` | Idem. Remplace la fiche entière, nouveaux champs compris, `position` par ligne. |
| `POST /api/Contacts/Import` | Accepte le `.vcf` en plus du CSV, distingués sur le type MIME puis sur le contenu ; plafond porté à 20 Mo (§ Limites). |
| `GET /api/Contacts/Export` | **Le CSV gagne les colonnes hors modèle** — voir ci-dessous, ce n'est pas un simple remplissage. |

**`params`, `group_name` et `pref` sortent, ils n'entrent pas.** Le `PUT` les ignore s'ils sont
présents et ne les documente pas : le composeur ne s'en sert jamais (décision 4), et accepter
`params` ouvrirait le chemin d'injection décrit là-bas — quant à désigner l'adresse préférée,
c'est une question d'éditeur, donc de 4b. Ce que le corps rend par ligne : la valeur, `position`
et `type`.

**La route photo porte un `ETag`.** 4b dessinera une avalanche d'avatars ; `card_hash` est déjà là,
et le 304 ne coûte rien à écrire maintenant contre une route qu'on ne rouvrira pas.

**L'export CSV gagne des colonnes, il n'en remplit pas d'existantes.** `ContactCsvExporter` n'écrit
aujourd'hui que `First Name, Last Name, Nick Name, Display Name, E-mail Address[, E-mail N Address],
Favorite` : il n'a jamais écrit de colonne téléphone, société ou adresse postale, donc il n'y a rien
qui « sorte à vide ». Il faut **ajouter** l'en-tête de référence Outlook que 3d documente déjà comme
format d'entrée — le fichier redevient alors symétrique, ce qu'il n'a jamais été. Deux conséquences :
le test d'aller-retour export → import de 3d s'étend à ces colonnes, et la règle « le nombre de
colonnes d'adresse suit le contact le plus fourni » s'applique désormais aussi aux téléphones.

**Un contact d'autrui répond 404 et jamais 403**, sur les trois routes nouvelles comme sur les
existantes : le store est scopé par utilisateur, un id étranger ne résout rien, et 403 confirmerait
son existence.

**La liste dédoublonne les adresses à l'assemblage.** La clé `(contact_id, position)` autorise
désormais la même adresse sous deux `TYPE` ; la projection garde les deux lignes, mais la tuile et
l'autocomplétion des destinataires la montreraient deux fois — la réponse de la liste n'en rend
qu'une. « Aucun écran ne bouge » exigeait cette phrase.

## Le rattrapage

**Il ne projette pas d'abord : il réconcilie.** C'est le point où la tranche peut détruire des
données, et 3d l'avait annoncé mot pour mot :

> `UpdateAsync` laisse déjà `vcard_raw` intact par décision de 3a, donc son `FN` et ses `EMAIL`
> vieillissent dès la première édition du contact. L'export vCard devra **superposer les champs de
> la table par-dessus le brut**, jamais faire confiance à ceux du brut.

Toute fiche importée puis éditée porte donc une carte périmée. Projeter cette carte-là réécrirait
les colonnes avec le vieux nom et la vieille adresse — l'édition de l'utilisateur, effacée par la
migration même qui prétend le servir. La bascule vers la souveraineté de la carte n'est légitime
qu'**après** la superposition, jamais avant.

Chaque fiche passe donc par trois gestes, dans cet ordre :

1. **Composer — mais la superposition est bornée aux colonnes qui existaient avant 4a.** Carte
   absente → une carte neuve depuis les colonnes. Carte présente → le composeur repose `N`, `FN`,
   `NICKNAME` et les `EMAIL` depuis les colonnes actuelles, **et rien d'autre** : seuls ces
   champs-là ont pu dériver depuis 3d, et les colonnes neuves — téléphones, postales, `ORG`,
   `BDAY`, `NOTE` — sont vides par construction, la projection n'ayant jamais tourné. Les passer
   au composeur ordinaire, dont la règle « position absente = propriété supprimée » est faite pour
   l'éditeur, effacerait de la carte chaque `TEL`, `ADR`, `ORG`, `BDAY` et `NOTE` qu'elle seule
   porte — la perte exacte que cette section existe pour empêcher. Deux simplifications sont
   sûres parce que toute carte pré-4a sort de `ContactVCardWriter`, une forme connue : le `FN` est
   recalculé par la chaîne de repli (c'était déjà son calcul), et les `EMAIL` — de simples lignes
   `TYPE=INTERNET`, sans rien à préserver — sont remplacées en bloc.
2. **Compléter le `UID`** s'il manque — les cartes de `ContactVCardWriter` n'en ont pas, et RFC 6352
   en exige un par ressource. C'est une modification, leur hash change, et rien ne s'y synchronise
   encore.
3. **Projeter**, une fois la carte devenue vraie.

Un appel traite un lot et répond `{ traitées, restantes }` (décision 15) ; l'appelant relance
jusqu'à zéro. Il est documenté dans `docs/superpowers/` comme les autres prérequis de déploiement,
et journalise son avancement — c'est un balayage sur l'ensemble des utilisateurs, et une opération
silencieuse dont personne ne sait si elle a fini est une opération qu'on rejoue dans le doute.

Ordre imposé : les tables et colonnes d'abord, le déploiement du backend ensuite, le rattrapage en
dernier. Un backend qui projette avant que les tables n'existent tombe à la première écriture.

## Limites et validation

`ContactValidator` s'étend : `MaxPhonesPerContact` et `MaxPostalAddressesPerContact` à 10 chacun, et
les longueurs des nouvelles colonnes miroitées comme le sont déjà celles des noms — non bornée ici,
une valeur trop longue atteint une MariaDB en mode strict et revient en 500. Ces plafonds ne
bornent que la porte éditeur, jamais la projection, intégrale par définition (décision 4) : une
carte à quinze téléphones est projetée entière, et la conséquence — une telle fiche ne se sauvera
en 4b qu'en redescendant sous le plafond — est assumée.

**Une `position` que la carte n'a pas est une ligne neuve, jamais une erreur.** C'est une poignée,
pas une clé : hors gabarit, négative ou au-delà du nombre de propriétés, elle ne désigne rien, et le
composeur ajoute la ligne en fin de carte comme il le ferait d'une ligne sans position. Refuser
tiendrait le `PUT` de 4b en échec sur une fiche que l'utilisateur a le droit de sauver, pour une
divergence dont il n'est pas l'auteur.

**Le champ `type` est validé au gabarit d'un jeton.** C'est, depuis que `params` n'entre plus, le
seul fragment de paramètre qui traverse encore vers la carte : lettres ASCII, chiffres, tiret et
virgule, 64 caractères au plus, refusé sinon par `ContactValidator`. Un `type` qui porterait un
`;`, un `:` ou un retour à la ligne est le même vecteur d'injection que `params` fermé a déjà
clos — le fermer ici achève le tour.

**Nouveau plafond : 1 Mo par carte, mesuré à chaque écriture de `vcard_raw`** — donc dans le store,
là où le hash est calculé, et pas seulement à l'import. Une `NOTE` longue posée par 4b sur une carte
déjà lourde le franchirait sans que rien ne le voie, et 4c annoncerait alors un `max-resource-size`
que son propre store viole. C'est aussi ce `max-resource-size` que 4c devra annoncer aux clients ;
l'écrire ici évite qu'il soit choisi deux fois. Le plafond de 5000 contacts par utilisateur tient
tel quel. **La route d'import monte de 5 à 20 Mo** (`RequestSizeLimit`) : des cartes à photo de
quelques centaines de kilo-octets remplissent 5 Mo en quelques dizaines de contacts, et l'export
d'un téléphone est précisément le fichier que cette route doit accepter. Le commentaire de
`ContactsController.Import` qui dimensionne son `MemoryStream` sur `file.Length` a été écrit pour
5 Mo ; à 20 Mo c'est une allocation LOH par requête, et le geste mérite d'être relu plutôt que
recopié.

## Les tests

Le vrai test est un **corpus de cartes réelles** : un export iPhone avec ses groupes `item1.`, un
export Google, un Thunderbird, un DAVx⁵. Ces fichiers valent plus que n'importe quelle fixture
écrite à la main, et c'est le seul endroit de la tranche où le comportement réel des clients est
observable avant 4d. **Anonymisés avant d'être versionnés** : ce sont des exports de vraies
personnes, et le dépôt est pour toujours. Noms, adresses et photos remplacés, structure intacte —
c'est la structure qu'on teste.

Trois familles d'assertions :

- **Projection** — telle carte produit telles colonnes et telles lignes filles, dans tel ordre, avec
  telle `pref`. Une carte dont la seconde `EMAIL` porte `PREF=1` la rend en tête ; une `EMAIL`
  invalide ou trop longue ne produit pas de ligne (décision 8) ; une `PHOTO` `http(s)` ne produit
  pas de photo (décision 12).
- **Survie** — parse, on modifie un seul champ, on sérialise, on re-parse : toute propriété non
  modélisée est encore là, tout **paramètre** (`PREF`, `PID`, `LABEL`, `X-`…) est encore sur la
  sienne, tout libellé groupé désigne encore sa propriété, et les composantes RFC 9554 d'une `ADR`
  et d'un `N` sont intactes. C'est l'assertion qui protège les `X-AB*` d'Apple, et c'est celle qui
  doit rougir en premier si le moteur régresse. Elle doit couvrir les paramètres autant que les
  propriétés : c'est le drapeau que la décision 6 a failli oublier.
- **Réglages de la bibliothèque** — un test épingle les options de sérialisation : les deux
  `WriteNonStandard*` présents, `SetPropertyIDs` absent. Ce sont des réglages qui se perdent dans
  un refactor et dont la perte ne casse rien d'autre qu'une carte d'utilisateur.

Deux comportements sont à **constater** plutôt qu'à supposer, et le corpus est le seul endroit où
c'est possible : ce que la bibliothèque fait de `X-ABLabel`, qu'elle modélise (décision 6), et la
forme exacte qu'elle donne à un `BDAY` partiel en 3.0 (décision 11).

L'aller-retour octet-pour-octet n'est pas testé, parce qu'il n'est pas promis — décision 6.

## Hors périmètre

Les groupes de contacts (`KIND:group`, `CATEGORIES`), les carnets multiples, une corbeille des
contacts, l'affichage des nouveaux champs (4b) et tout DAV (4c). Chacun mérite sa tranche.

**Non modélisé n'est pas perdu.** `IMPP`, `ANNIVERSARY`, `RELATED`, `TZ`, `GEO`, `LANG`, et les
propriétés de RFC 9554 (`PRONOUNS`, `SOCIALPROFILE`, `GRAMGENDER`…) n'ont ni colonne ni écran : ils
survivent dans la carte, traversent une édition sans dommage, et repartent entiers par CardDAV en
4c. Ce qu'ils n'auront pas, c'est un champ dans l'éditeur de 4b — la question s'y reposera, et la
réponse d'ici là est qu'ils ne se perdent pas.

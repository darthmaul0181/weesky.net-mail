# Contacts 4c — le serveur CardDAV

Troisième tranche du projet CardDAV, à la suite de
[4a](2026-08-14-webmail-contacts-4a-vcard-model-design.md) et
[4b](2026-08-22-webmail-contacts-4b-editor-design.md).

## Où en est le projet

| | Tranche | État |
|---|---|---|
| 4a | Modèle de données complet + moteur d'aller-retour vCard | livrée |
| 4b | Éditeur et fiche webmail étendus | livrée |
| **4c** | **Serveur CardDAV (découverte, collection, rapports, verbes, ETags, pierres tombales, historique)** | *ce document* |
| 4d | Conformité clients (`ccs-caldavtester`, Thunderbird, DAVx⁵, iPhone emprunté) | à venir |

4a a fait de `vcard_raw` la donnée souveraine et des colonnes une projection recalculée à chaque
écriture ; il a posé `contacts.card_hash`, le SHA-256 de la carte, en annonçant qu'il serait la base
de l'ETag. 4b a donné à l'utilisateur de quoi remplir ces champs. 4c ouvre le carnet au protocole.

## Ce que fait la tranche

Le carnet devient accessible à un client CardDAV : découverte du principal et du carnet, listing,
lecture, écriture, suppression, et synchronisation incrémentale. Comme aucun client tiers ne peut
traverser l'écran de login du webmail, la tranche livre d'abord de quoi s'authentifier : un onglet
« Sync » dans les paramètres, avec un interrupteur, l'adresse du serveur et l'identifiant à copier,
et un secret engendré une fois et régénérable (décision 19).

La tranche touche aussi le webmail hors de `/dav`, et sur deux points qu'on ne devine pas depuis le
titre. **Toute suppression de fiche, d'où qu'elle vienne, doit désormais laisser une trace.** Une
fiche effacée depuis la liste ou la barre d'actions groupées sans pierre tombale reste sur le
téléphone de son propriétaire, indéfiniment et sans erreur (décisions 6 et 8). Et **toute écriture
qui en remplace une autre doit désormais archiver ce qu'elle efface** : à partir de cette tranche il
existe deux écrivains sur la même fiche, dont l'un peut avoir été hors réseau pendant deux jours.
Le protocole sait détecter ce conflit ; il ne sait pas le réparer, et c'est un historique de
contenus qui s'en charge — comme chez Radicale, où le crochet `git commit` joue ce rôle
(décision 17). Le webmail y gagne au passage le contrôle de concurrence qu'il n'avait pas.

Ce que 4c **ne** fait **pas** : prouver que Thunderbird, DAVx⁵ ou iOS sont contents. Ici on écrit
au RFC ; 4d écrit aux clients.

## Découpage

Un seul spec, deux plans d'implémentation, dans cet ordre strict :

- **4c-i — l'identifiant de synchronisation et son écran.** Table, engendrement, hachage, schéma
  d'authentification, API, et l'onglet « Sync » des paramètres — interrupteur, valeurs à copier,
  régénération (décisions 1, 2 et 19). Livrable et testable seul.
- **4c-ii — le serveur DAV.** Découverte, `PROPFIND`, les trois `REPORT`, les verbes, les ETags,
  la séquence, les pierres tombales et l'historique des cartes remplacées.

Couper là n'est pas cosmétique : sans 4c-i, 4c-ii n'a aucune authentification, et un plan couvrant
les deux ferait trente tâches dont la moitié attend l'autre.

## Décisions

### 1. Un secret par utilisateur, engendré par nous, donc haché vite

Une table `dav_credentials` porte **une ligne par utilisateur** : un secret de 20 caractères base32
— environ 100 bits — engendré par `RandomNumberGenerator`, affiché une seule fois, jamais restitué.
Il est haché en **SHA-256 salé, et non par un KDF à itérations**.

**Un seul secret, et non une liste étiquetée** — c'est la décision de forme dont tout le reste de ce
paragraphe découle. Un trousseau à la Google, un secret par appareil révocable séparément, achète
une chose : perdre un téléphone n'oblige pas à reconfigurer les autres. Il coûte un balayage de
sels sur le chemin le plus chaud du service, un plafond à défendre, un `409` à traduire, et un écran
qui est une liste là où l'utilisateur cherche trois valeurs à recopier. Pour un carnet personnel
synchronisé sur deux ou trois appareils, l'échange ne vaut pas : régénérer et reconfigurer trois
clients est le geste rare, tandis que lire l'écran est le geste courant. **La conséquence est que
`user_id` est la clé primaire de la table** : on retrouve la ligne par une lecture indexée, on
compare un condensat et un seul, et il n'y a rien à plafonner.

C'est l'inverse de la règle habituelle, et la raison doit être écrite ici pour que personne ne
« corrige » le hachage plus tard : un KDF lent existe pour rendre coûteuse l'attaque par
dictionnaire d'un secret que l'humain a choisi et qui porte donc une vingtaine de bits d'entropie.
Ici l'entropie vient de nous. Une recherche exhaustive sur 100 bits reste hors de portée quelle que
soit la vitesse du hachage, tandis qu'un client DAV se ré-authentifie à **chaque requête** — un
PBKDF2 à 100 000 itérations y serait un déni de service que nous nous infligeons nous-mêmes, et
qu'un attaquant déclencherait à volonté avec des requêtes non authentifiées.

Le sel reste par ligne : il empêche qu'une même chaîne engendrée deux fois se reconnaisse dans la
table, et il ne coûte plus rien du tout — la ligne se retrouve par sa clé, jamais par l'empreinte,
donc rien n'avait besoin que le condensat soit recherchable.

La comparaison du condensat se fait en temps constant (`CryptographicOperations.FixedTimeEquals`).
Le secret présenté est débarrassé de ses blancs de bord avant hachage : le copier-coller — mobile
surtout — en ajoute, l'alphabet base32 n'en contient aucun, et le symptôme sans ce `Trim` serait un
mot de passe refusé quoique juste, indiscernable d'une faute de frappe.

`last_used_at` est mis à jour au plus une fois par heure : à chaque requête ce serait une écriture
par `PROPFIND`, pour une colonne que l'écran rend en « il y a deux heures ».
**L'amortissement vit en mémoire, par instance**, et il est assumé tel quel : partagé, il coûterait
la lecture qu'il devait éviter ; perdu au redéploiement, il ne coûte qu'une écriture de plus par
utilisateur et par démarrage. La colonne n'est pas décorative : c'est elle qui permet à l'écran de
répondre à la seule question que l'utilisateur se pose devant un bouton « Regenerate » — est-ce que
quelque chose s'en sert encore ?

**Le cache mémoire de 60 secondes reste, mais pour la rafale et non pour le balayage.** Il est calqué
sur `SessionGuard.CacheWindow` : un client DAV envoie ses identifiants sur **chaque** requête, et une
synchronisation réelle enchaîne un `PROPFIND`, un `REPORT` et autant de `GET` en quelques secondes —
rien ne justifie d'y relire la table dix fois. Hors rafale il ne sert à rien et c'est voulu : DAVx⁵
et iOS interrogent l'état du carnet toutes les quinze minutes, où le coût est d'une lecture indexée
et d'un SHA-256, ce qui n'a pas besoin d'être optimisé. La clé du cache est le couple (identifiant,
SHA-256 du secret présenté) et la valeur l'identité résolue — jamais le secret en clair, qui ne
survit pas à la requête. Le cache est vidé à la régénération comme `SessionGuard.Forget` le fait à
la rotation, et sa fenêtre borne, sur les autres instances, la survie d'un secret remplacé : c'est
le compromis déjà retenu pour les sessions, et il vaut ici pour la même raison.

**Un échec d'authentification coûte un délai aléatoire avant le `401`.** La recherche exhaustive est
vaine, mais chaque tentative non authentifiée reste une lecture de table, et le temps de réponse nu
révélerait l'existence du compte. Un
délai aléatoire de quelques centaines de millisecondes après l'échec — le modèle de Radicale,
explicitement contre les oracles de temps — efface les deux signaux pour un coût nul ; son absence
sur le Basic DAV a valu un avis de sécurité à Nextcloud (GHSA-mr7q-xf62-fw54). Le délai ne retient
aucune ressource : `await Task.Delay`, jamais `Thread.Sleep` — un délai bloquant ferait du
ralentisseur un épuisement de pool, c'est-à-dire l'attaque qu'il devait rendre inutile. Pas de
verrou, pas de connexion base ouverte pendant l'attente.

**Mais un délai n'est pas une limitation de débit**, et il faut l'écrire pour que la décision ne se
lise pas comme une protection complète : il efface l'oracle de temps, il ne borne rien pour qui
ouvre mille connexions en parallèle. La recherche exhaustive sur 100 bits reste vaine, ce qui ne
l'est pas est le coût que l'attaquant nous impose. Un compteur d'échecs glissant — par adresse IP
**et** par identifiant, les deux, puisque l'un vise un compte depuis partout et l'autre tous les
comptes depuis une machine — refuse au-delà d'un seuil sans rien lire en base. Nextcloud n'a pas
ajouté qu'un délai après l'avis cité ; il a ajouté ce compteur, et pour cette raison exactement.
Le compteur vit en mémoire, par instance : le seuil effectif se multiplie par le nombre
d'instances, le même échange que l'amortissement et le cache ci-dessus, assumé pour la même raison.

### 2. La portée d'un secret est `/dav`, il s'éteint sans se perdre

Le secret n'ouvre pas l'API du webmail. Il est porté par un schéma d'authentification distinct —
`CardDav`, Basic sur TLS — que seules les routes `/dav` acceptent. Le JWT reste le schéma par défaut
de `/api` et vaut **aussi** sur `/dav`, ce qui rend la surface testable depuis une session webmail
ordinaire, sans engendrer de secret.

Aucune colonne de portée dans la table : il n'y a qu'une portée, et une colonne qui ne prend qu'une
valeur ment sur son extensibilité.

**En revanche il y a une colonne `enabled`, et elle n'est pas la même chose qu'un secret absent.**
C'est l'interrupteur de la décision 19, et il vaut d'être distingué de la régénération parce que les
deux gestes que l'utilisateur peut vouloir ne se ressemblent pas. « Je coupe la synchronisation »
est réversible et ne doit rien détruire : `enabled = 0`, le secret survit, et rallumer fait repartir
les appareils sans qu'aucun soit reconfiguré. « Je crois mon secret dans d'autres mains » détruit :
on régénère, et tout est à ressaisir. Faire porter les deux au même bouton — éteindre en révoquant —
ferait payer le prix du second à qui voulait le premier, et l'utilisateur qui met en pause une
semaine n'a aucune raison de reconfigurer trois clients au retour.

**La distinction se paie au bord par un ordre, pas par un code de plus.** Un compte dont la
synchronisation est éteinte répond `403` — authentifié, mais ce service-là est fermé —, ce qu'un
`401` ne dirait pas : un `401` enverrait l'utilisateur vérifier un mot de passe qui est correct,
alors que le `403` l'envoie regarder l'interrupteur. Mais le `403` n'est rendu **qu'après** une
comparaison réussie du condensat. L'ordre inverse — voir `enabled = 0` et répondre tout de suite —
serait plus rapide et ferait de la réponse un oracle : `403` sur un compte qui existe et dont le DAV
dort, `401` sur tout le reste, c'est-à-dire l'énumération de comptes que la décision 4 chasse
ailleurs. Le condensat est donc comparé d'abord, et le `403` n'est visible que de qui détient déjà
le secret. La lecture est la même dans les deux cas : `enabled` et `secret_hash` sont sur la ligne
qu'on charge de toute façon.

Le schéma répond `401` avec `WWW-Authenticate: Basic realm="weesky CardDAV"` — sans cet en-tête, un
client n'a aucune raison de renvoyer des identifiants et boucle sur l'échec.

**« Basic sur TLS » est une vérification, pas une intention.** Basic transporte le secret en clair
dans l'en-tête : hors TLS, un `PROPFIND` suffit à le donner à qui écoute, et un secret qui ouvre
tout le carnet ne se rejoue pas une fois — il se rejoue jusqu'à révocation. Le schéma refuse donc
une requête dont le protocole d'origine n'est pas `https`, avec un `403` et sans jamais lire la
table : rien n'est comparé à un secret déjà compromis par son transport. Le service étant derrière
un proxy inverse, l'origine se lit dans `X-Forwarded-Proto` — donc via `ForwardedHeaders`, jamais
sur `Request.IsHttps` que Kestrel voit toujours à `false`. Le contrôle est levé sur
l'environnement de développement, et à ce seul endroit.

**L'identifiant Basic est l'adresse e-mail complète**, celle de la connexion au webmail : c'est la
clé naturelle de `users`, celle que `SessionGuard` interroge déjà, et la seule que l'utilisateur
connaisse sans aller la chercher. Le GUID du principal n'y a pas sa place — il apparaît dans l'URL,
que le client déduit de la découverte, jamais dans ce que l'humain saisit. L'écran de la décision 19
affiche donc trois choses à copier : l'adresse du serveur, l'identifiant, et le secret montré une
fois.

**Le défi doit être Basic, et Basic seul, sur `/dav`.** `AuthorizationExtension` pose JwtBearer en
`DefaultChallengeScheme` ; un `[Authorize(AuthenticationSchemes = "Bearer,CardDav")]` ferait émettre
`WWW-Authenticate: Bearer` **avant** `Basic`, et plusieurs clients DAV ne lisent que le premier
défi — le symptôme serait un carnet qui refuse un mot de passe pourtant valide. Les routes `/dav`
authentifient donc sur les deux schémas mais **défient** sur `CardDav` seul, ce qu'une politique
d'autorisation nommée fixe une fois (`AuthenticationSchemes` pour la lecture, `CardDav` comme schéma
de défi), et le test d'authentification épingle qu'un `401` de `/dav` ne porte qu'un `Basic`.

**Un secret ne dispense d'aucun des contrôles que le JWT subit.** Le chemin JWT passe par
`ISessionGuard` : compte utilisable côté serveur de messagerie (`IAccountInfoProvider.IsUsableAsync`)
**et** `security_stamp` courant. Le schéma `CardDav` réutilise le premier tel quel : un compte
supprimé ou désactivé ne synchronise plus, et l'oublier ferait du carnet la dernière porte ouverte
d'un compte fermé.

Le `security_stamp`, lui, ne s'applique pas : le secret n'est pas une session et n'en porte pas
l'empreinte. La question qu'il pose se tranche donc explicitement, dans un sens : **une rotation du
`security_stamp` révoque le secret de l'utilisateur**, par suppression de sa ligne dans la même
transaction que la rotation, dans
`WebmailUserStore.RotateSecurityStampAsync`. Ses appelants sont aujourd'hui au nombre de trois, et
tous trois sont des gestes de reprise de contrôle : `LoginController.LogoutEverywhere` — « se
déconnecter partout », précisément le geste de qui croit ses accès dans d'autres mains —,
`AccountManagementController.ChangePassword` — l'utilisateur qui change son mot de passe — et
`AdminRepository.RevokeSessionsAsync` — la réinitialisation par un administrateur. La révocation est
voulue dans les trois cas : laisser survivre à l'un de ces gestes un secret qui rend tout le carnet
lisible et modifiable le viderait de son sens, et l'utilisateur qui le fait ne devine pas qu'un
second trousseau existe. Suppression de la ligne, et non `enabled = 0` : la distinction du début de
cette décision se lit exactement à l'envers ici. Éteindre est un geste de confort, dont l'auteur
sait ce qu'il fait ; une rotation de `security_stamp` est un geste de défiance, et ce qu'il faut
détruire est le secret lui-même. Le coût est connu et acceptable : les clients DAV sont à
reconfigurer, l'écran de la décision 19 le dit avant d'engendrer le premier secret, et le libellé du
bouton de déconnexion globale comme l'écran de changement de mot de passe le rappellent.

### 3. Un seul carnet, nommé `default`

Le modèle n'a pas de table de carnets et rien ne la réclame. L'ajouter par anticipation coûterait
une jointure sur chaque requête et une colonne sur `contacts`, pour une fonction que personne n'a
demandée. Le jour où plusieurs carnets seront voulus, l'URL les accueille déjà : c'est le segment
`default` qui deviendra variable.

### 4. Le principal est un GUID, et le chemin est vérifié

`/dav/principals/{userId}/` porte l'identifiant de `users`, pas l'adresse e-mail : une adresse dans
une URL doit être échappée, se retrouve dans les journaux du proxy, et n'apporte rien qu'un client
regarde.

Toute route `/dav` dont le segment `{userId}` diffère de l'utilisateur authentifié répond `404`, pas
`403` : un `403` confirmerait l'existence du principal visé.

### 5. Le nom de ressource est stocké, jamais dérivé

Un client choisit lui-même l'URL de son `PUT`, et rien dans le RFC ne l'oblige à la faire coïncider
avec l'`UID` de la carte. Une colonne `contacts.dav_name`, unique par utilisateur, porte donc ce que
le client a choisi ; une fiche née dans le webmail reçoit `{id}.vcf`.

Dériver le nom de l'`UID` casserait sur les UID contenant `/` ou `:` — que l'import accepte déjà,
puisque 4a garde l'UID de la carte source verbatim, préfixe `urn:uuid:` compris.

`dav_name` est validé à l'écriture : au plus 255 caractères, non vide, aucun `/`, aucun `\`, aucun
caractère de contrôle (`U+0000`–`U+001F`, `U+007F`), ni espace de tête ni espace de fin, et le nom
entier ne vaut ni `.` ni `..`.

**La route porte `{nom}`, pas `{nom}.vcf`.** Le suffixe est une convention de client, pas une règle
du protocole : le RFC laisse le client choisir son URL, et la décision qui précède en fait toute sa
substance. Un motif de route qui exige `.vcf` la contredirait en silence — un `PUT` sur
`…/default/carte` ne serait pas refusé par une réponse pensée mais par un `404` de routage, c'est-à-dire
par le seul code qu'un client lit comme « cette collection ne contient pas ça » plutôt que
comme « ce nom ne me convient pas ». Le nom est donc capté entier et passé à la validation
ci-dessus, qui est le seul juge. Les fiches nées dans le webmail gardent `{id}.vcf` : c'est ce que
les clients affichent dans leurs journaux, et il n'y a aucune raison de les dérouter.

**Le nom est un segment de chemin, donc il traverse un encodage à l'aller et un décodage au retour,
et les deux doivent être écrits ensemble.** Le chemin de la requête est décodé une fois, par
`Uri.UnescapeDataString` sur le segment brut, **avant** validation : c'est le décodage qui fait de
`%2F` un `/`, et valider avant lui laisserait passer une traversée que le stockage refuse ensuite.
« Une fois » se mesure depuis le chemin **encodé** de la requête, jamais depuis une valeur de
route : ASP.NET Core décode déjà les siennes, et les repasser dans `Uri.UnescapeDataString` ferait
de `%252F` un `/` — la traversée revenue par un double décodage.
Réciproquement tout `href` écrit dans une réponse est ré-encodé segment par segment
(`Uri.EscapeDataString`), sans quoi un nom portant un espace, un `#` ou un `?` — qu'un client a le
droit de choisir — produirait un `href` que ce même client ne saurait pas relire. `DavPaths` porte
les deux sens et rien d'autre ne construit ni ne lit ces chemins.

La colonne collationne en `utf8mb4_bin`, comme ses sœurs : deux noms qui ne diffèrent que par la
casse sont deux URL différentes pour tout client HTTP, et une collation insensible en ferait un
conflit d'unicité là où le protocole voit deux ressources.

**`utf8mb4_bin` règle la casse mais pas l'espace, et c'est pour cela que la validation refuse les
espaces de bord.** La collation est *PAD SPACE* sous MariaDB : `carte.vcf` et `carte.vcf ` y sont
égaux pour l'index unique, alors qu'ils sont deux URL distinctes pour tout client HTTP — exactement
le défaut que le choix de `_bin` chassait sur la casse, revenu par l'autre bout. Une comparaison
d'unicité qui fusionne deux ressources est pire qu'une qui les sépare : le second `PUT` échouerait
sur un doublon que le client ne peut ni comprendre ni corriger. Refuser les espaces de bord ferme le
cas dans la validation plutôt que dans la collation, et laisse `VARCHAR` là où il est lisible ; un
espace **intérieur** reste légitime et reste porté, c'est l'encodage du segment qui s'en occupe.

Un nom libre étant unique par utilisateur, un `PUT` qui vise un nom déjà pris par une **autre**
fiche est une écriture sur cette autre fiche, pas une création — c'est la sémantique du RFC, l'URL
identifie la ressource. C'est l'`UID` qui arbitre l'identité de la carte (décision 10), pas le nom.

### 6. La séquence avance exactement quand `card_hash` change, ou qu'une fiche disparaît

C'est l'invariant qui tient toute la synchronisation, et il tient en une phrase parce qu'il répond
seul au cas piégeux : basculer l'étoile ne modifie pas la carte, donc ne réveille aucun client.
`is_favorite` n'est projeté de rien (décision 1 de 4a) — il ne doit pas non plus être visible du
protocole. Même chose pour `source` et pour un `last_used_at` de secret.

Réciproquement, toute écriture qui change la carte avance la séquence, quelle qu'en soit la porte :
édition webmail, import, fusion, rattrapage, `PUT` DAV.

**Et toute suppression, quelle qu'en soit la porte, pose une pierre tombale et avance la séquence.**
Le point mérite sa phrase parce que la porte DAV est la moins fréquentée des trois : les
suppressions viennent d'abord de `ContactStore.DeleteAsync` et de `DeleteManyAsync`, c'est-à-dire de
la fiche et de la barre d'actions groupées de la tranche 2026-08-12. Une suppression qui ne pose pas
de tombe est invisible du protocole : le client ne voit ni modification ni disparition, garde la
fiche pour toujours, et la restitue à l'utilisateur qui vient de l'effacer. C'est le mode de
défaillance silencieux de la décision 8, atteint par une autre route — et par la route la plus
empruntée. `DeleteManyAsync` pose une tombe **par** fiche réellement supprimée.

Une fiche que le rattrapage n'a pas encore atteinte n'a pas de `dav_name` : sa suppression ne pose
**pas** de tombe — il n'y a pas de nom à enterrer, et la fiche n'a jamais été visible du protocole
(`sync_sequence = 0`). Le chemin de suppression doit le tolérer, pas s'y casser : la clé de
`contact_tombstones` refuse le `NULL`. Réciproquement, toute écriture qui avance la séquence d'une
fiche sans nom lui pose `dav_name = '{id}.vcf'` dans la même transaction : sans cela, une édition
webmail pendant la fenêtre de rattrapage créerait une ligne à rang > 0 et sans nom, qu'un rapport
ne saurait pas servir.

**Une fiche sans `dav_name` est invisible de tout le protocole, et pas seulement du rapport de
synchronisation.** L'invariant tombe tout seul dans `sync-collection`, où le filtre `sync_sequence >
n` avec `n ≥ 0` écarte déjà les fiches à rang `0` ; il ne tombe nulle part ailleurs. `PROPFIND
Depth: 1`, `addressbook-query` et `addressbook-multiget` n'ont aucune borne de séquence : sur une
fiche non rattrapée, ils construiraient un `href` à partir d'un nom qui n'existe pas. Les quatre
chemins écartent donc ces fiches, à la même clause, et c'est écrit ici plutôt que quatre
fois : la fenêtre de rattrapage n'est pas la seule à produire ces lignes — une fiche qu'un
rattrapage aurait manquée le reste indéfiniment, et un carnet qui sert un `href` mort est un carnet
qu'un client marque en erreur à chaque cycle.

**La clause porte trois conditions, et non une.** `dav_name IS NOT NULL` est la plus visible, mais elle
laisse passer deux voisines nées d'un autre rattrapage, celui de 4a : `contacts.vcard_raw` est
`DEFAULT NULL` et `contacts.card_hash` est `NOT NULL DEFAULT ''`. Une fiche que ce rattrapage-là aurait
manquée sortirait donc avec un corps vide et un `ETag: ""` — syntaxiquement valide, sémantiquement
faux, et rangé par le client comme n'importe quelle autre valeur, pour toujours. La clause est
`dav_name IS NOT NULL AND vcard_raw IS NOT NULL AND card_hash <> ''`, et le test de l'invariant assure
les trois : un ETag vide est précisément le genre de valeur qu'aucune assertion ne regarde, parce
qu'elle a l'air d'une valeur.

**Une transaction, un rang.** Un lot écrit dans une transaction unique prend un seul numéro : la ligne
d'état est verrouillée une fois et l'incrémenter davantage ne distinguerait rien, puisque tout devient
visible au même `COMMIT`. Un rang porte donc de une à plusieurs centaines de lignes, ce qui est sans
conséquence pour un client — il reçoit le lot entier ou rien — mais fixe une contrainte que la
décision 7 doit respecter au moment de tronquer une réponse.

**Le lot, lui, est découpé à cent fiches par transaction**, et l'import de cinq cents comme la
suppression groupée de cinq mille prennent donc plusieurs rangs. Ce n'est pas une optimisation : depuis
la décision 17, chacune de ces écritures **archive** ce qu'elle remplace ou efface, et une suppression
de carnet entier écrirait jusqu'à cinq gigaoctets de `MEDIUMTEXT` dans une seule transaction — un
journal de reprise qui déborde, et le verrou de la ligne d'état tenu assez longtemps pour que tous les
téléphones repartent en `503`. « Une transaction, un rang » reste vrai ; c'est « un import, un rang »
qui ne l'est pas, et rien ne le réclamait : plusieurs rangs pour un même import sont exactement ce que
la coupe au rang près de la décision 7 sait servir, et un client qui synchronise pendant un import
reçoit le début plutôt que d'attendre la fin.

La seule suppression qui n'en pose pas — ni n'archive quoi que ce soit — est la disparition de
l'utilisateur lui-même : le `ON DELETE CASCADE` emporte les fiches, les tombes, les révisions et la
ligne d'état ensemble, et il n'y a plus de carnet à synchroniser. Archiver là serait garder la
donnée de quelqu'un qui a demandé son effacement.

Le compteur vit dans `contact_sync_state`, une ligne par utilisateur, incrémenté sous le verrou de
sa propre ligne (`UPDATE … SET seq = seq + 1` puis relecture dans la même transaction).
Un `MAX(sync_sequence) + 1` court après deux écritures simultanées et rendrait deux fiches à la
même séquence — un client qui synchronise entre les deux en perdrait une définitivement.

**Le verrou doit être tenu jusqu'au `COMMIT`, et l'incrément doit être dans la même transaction que
l'écriture qu'il numérote.** C'est ce qui rend l'ordre des séquences égal à l'ordre des validations :
InnoDB garde le verrou exclusif de la ligne d'état jusqu'à la fin de la transaction, donc une
seconde écriture ne peut pas obtenir son rang avant que la première ne soit visible. Séparer les
deux — prendre un numéro, puis écrire dans une autre transaction — rouvrirait le trou par l'autre
bout : la séquence 11 validée avant la 10, un client qui synchronise entre les deux repart avec le
jeton 11 et ne verra jamais la 10. Ce n'est pas une optimisation à discuter : c'est l'unique raison
pour laquelle le jeton est sûr.

**La transaction explicite est de la mécanique neuve, et c'est le point le plus facile à
sous-estimer de la tranche.** Aucun dépôt du projet n'ouvre aujourd'hui de transaction : ni
`ContactStore`, ni `WebmailUserStore` n'appellent `BeginTransactionAsync` ni `TransactionScope`, et
`SaveChangesAsync` n'enveloppe qu'un seul appel. Or toute cette décision — et la révocation du
secret « dans la même transaction que la rotation » de la décision 2 — repose sur des transactions
qui couvrent **plusieurs** instructions. Trois choses en découlent, à écrire ici pour n'être pas
improvisées :

- La transaction s'ouvre par `Database.BeginTransactionAsync`, et à travers l'`IExecutionStrategy`
  du contexte. Aucune stratégie de réessai n'est configurée aujourd'hui — `EnableRetryOnFailure`
  n'apparaît nulle part dans le dépôt —, donc la traverser est présentement un geste vide ; il est
  fait quand même, parce qu'EF refuse une transaction manuelle le jour où la stratégie apparaît et
  que la contourner au lieu de la traverser rendrait alors le réessai silencieusement faux. Le motif
  coûte une ligne et ferme une régression future ; il ne décrit pas une contrainte présente, et une
  revue qui le vérifierait dans le dépôt conclurait autrement.
- **L'ordre de prise de verrou est toujours le même : la ligne d'état d'abord, les fiches ensuite.**
  Deux chemins qui verrouillent en ordre inverse s'interbloquent, et les deux existent déjà : un
  import de cinq cents fiches et un `PUT` DAV concurrent. Un ordre unique n'est pas une précaution,
  c'est la seule raison pour laquelle l'interblocage ne peut pas se produire.
- Un import tenant le verrou d'état jusqu'à son `COMMIT`, une écriture concurrente **attend** —
  jusqu'à `innodb_lock_wait_timeout`, cinquante secondes par défaut. Le dépassement, comme un
  interblocage arbitré par InnoDB, se traduit au bord DAV en **`503` avec `Retry-After`**, jamais en
  `500` : c'est la seule réponse qu'un client comprend comme « reviens plus tard », là où un `500`
  le fait boucler sur la même carte à chaque cycle.

**La ligne d'état est créée à la demande.** Le rattrapage en pose une par utilisateur existant, mais
un compte créé après le déploiement n'en aurait aucune, et une première écriture sans ligne à
verrouiller n'a pas de séquence à prendre. Toute lecture-écriture du compteur passe donc par un
`INSERT … ON DUPLICATE KEY UPDATE` qui crée la ligne à `seq = 0`, avec son `epoch` tiré au passage,
si elle manque, dans la même
transaction. Une lecture pure — `getctag` sur un carnet vide — répond `0` sans rien créer ; un
`sync-collection` sur ce même carnet vide, lui, a besoin d'une epoch pour former son jeton, et la
crée donc.

### 7. Le jeton et le ctag sortent du même compteur

`getctag` vaut la séquence courante. `sync-token` vaut
`http://weesky.net/ns/sync/{epoch}/{séquence}`.

**Le jeton porte une epoch, et le ctag non.** `contact_sync_state.epoch` est un GUID tiré à la
création de la ligne ; un jeton dont l'epoch n'est pas celle du carnet est refusé exactement comme un
jeton périmé. Il n'a qu'un usage, et il est décisif : c'est ce qui fait d'une restauration de
sauvegarde un `UPDATE` atomique — une nouvelle epoch, tous les jetons du monde deviennent étrangers au
carnet — plutôt qu'un raisonnement sur des bornes qu'un opérateur doit tenir juste au milieu d'un
incident (§ « Prérequis d'infrastructure », note 3). Le ctag, lui, reste la séquence nue : il est
opaque, jamais comparé qu'à lui-même d'un appel au suivant, et un client qui le voit changer
resynchronise sans avoir rien à comprendre.

`urn:snoopy:` a été écarté : `snoopy` n'est pas un NID enregistré, et un jeton est une URI. Une URI
`http://` sous un domaine que nous possédons est ce que fait sabre (`http://sabredav.org/ns/sync/…`) ;
elle n'est jamais déréférencée, seulement comparée octet à octet.

Un `sync-collection` portant le jeton *n* rend :

- toute fiche de `sync_sequence > n`, en réponse `200` avec `href` et **les propriétés que la
  requête demande** dans son `DAV:prop`, servies depuis le jeu de la décision 13, `propstat` à
  `404` pour les autres (RFC 6578 § 3.2) — jamais un `getetag` codé en dur : DAVx⁵ demande
  `getetag` **et** `resourcetype`, et se sert du second pour écarter les sous-collections ;
- toute pierre tombale de `sync_sequence > n`, en réponse `404` ;
- le nouveau jeton, égal à la séquence courante.

Jeton absent : synchro initiale — tout le carnet, aucune tombe. **Un `<DAV:sync-token/>` vide vaut
un jeton absent** (RFC 6578 § 3.2, qui met les deux formes sur le même pied) : c'est ce que
plusieurs clients envoient au premier appairage, et le laisser tomber dans « jeton syntaxiquement
autre » ci-dessous rendrait `403` là où le RFC veut une synchronisation complète — un carnet qui
refuse de s'appairer, sur la première requête que le client émet.

Un `sync-collection` ne se lit **pas** sur son `Depth` : RFC 6578 § 3 le remplace par
`DAV:sync-level`, et un en-tête `Depth` présent est ignoré plutôt que refusé — sauf pour servir de
repli quand `sync-level` manque, ci-dessous. Le point vaut d'être écrit parce que la
décision 14 refuse un `PROPFIND` sans `Depth` : les deux règles se contrediraient si l'on
appliquait la seconde ici — voir la mise en garde de cette décision-là.

**« Ignoré » est une divergence assumée, et il faut l'écrire pour qu'elle ne passe pas pour une
lecture.** Le même § 3 dit littéralement que le rapport n'est défini que pour `Depth: 0` et que toute
autre valeur donne un `400`. Nous ne le rendons pas, et sabre non plus : refuser un `Depth: 1` qu'un
client a posé par habitude n'apporte rien qu'un carnet qui ne s'appaire pas, sur un en-tête dont ce
rapport n'a plus l'usage. `ccs-caldavtester` peut relever le point en 4d ; ce sera une divergence
nommée, pas une découverte.

`CARDDAV:address-data` demandé dans le `DAV:prop` d'un `sync-collection` ressort en `propstat`
`404`, comme toute propriété hors du jeu de la décision 13. C'est un choix, pas un oubli : RFC 6352
§ 10.4 ne définit `address-data` que dans `addressbook-query` et `addressbook-multiget`, et le
servir ici ferait porter au rapport de synchronisation le poids que la décision 15 lui épargne — un
lot de cinq cents fiches à 1 Mo. Le coût est un aller-retour de plus pour les clients qui le
tentent (iOS le fait) ; ils enchaînent alors le `multiget` qu'ils savent faire. Si 4d montre que
l'un d'eux abandonne au lieu d'enchaîner, la décision se rouvrira avec un client nommé.

**La séquence courante est lue avant les lignes, et c'est un ordre, pas une préférence.** Le rapport
lit d'abord `seq`, puis les fiches et les tombes de `sync_sequence > n` **et `≤ seq`**, et rend `seq`
comme nouveau jeton. Lire les lignes d'abord et le compteur ensuite laisserait une écriture validée
entre les deux être couverte par le jeton rendu sans figurer dans la réponse : le client la croirait
vue, ne la redemanderait jamais, et la fiche manquerait définitivement — sans erreur, sans trace.
Dans l'ordre retenu, la même écriture concurrente est simplement rendue au tour suivant ; au pire un
client reçoit deux fois une fiche qu'il a déjà, ce qu'un ETag inchangé lui fait ignorer. La borne
haute `≤ seq` est ce qui rend l'affirmation vraie même si la lecture des lignes n'est pas dans la
même transaction que celle du compteur.

**La même règle vaut pour `PROPFIND Depth: 1` sur le carnet, et c'est le chemin qu'on oublie.** Le
raisonnement ci-dessus semble propre au rapport de synchronisation ; il ne l'est pas. DAVx⁵ demande
`getctag` **et** les membres dans un seul `PROPFIND`, et se sert du ctag rendu comme jeton d'état
jusqu'à l'interrogation suivante — c'est même son chemin principal quand `sync-collection` échoue.
Lire les membres puis le compteur y produit exactement la perte décrite plus haut : une écriture
validée entre les deux est couverte par le ctag rendu sans figurer dans la liste, le client la croit
vue et ne la redemande jamais. `PROPFIND Depth: 1` lit donc `seq` **d'abord**, borne ses membres à
`sync_sequence ≤ seq`, et rend ce même `seq` en `getctag`. La borne ne coûte rien — toute fiche
satisfait déjà `sync_sequence ≤ seq` par construction — et elle est ce qui rend les deux moitiés de
la réponse cohérentes entre elles.

**Un jeton supérieur à la séquence courante est refusé**, avec la même réponse `403
valid-sync-token` qu'un jeton périmé. Il ne vient pas d'ici : restauration de la base sur une
sauvegarde plus ancienne, carnet recréé, client qui a servi contre un autre serveur. L'accepter
ferait rendre une réponse vide, que le client lirait comme « rien n'a changé » sur un carnet qui a
tout changé.

**Un jeton syntaxiquement autre** — mauvais préfixe, epoch qui n'est pas celle du carnet, partie
numérique non entière, débordement de `long` — est refusé de la même façon. Il n'y a rien à comprendre
dans un jeton qu'on n'a pas émis.

**`DAV:sync-level` est obligatoire dans la requête** (RFC 6578 § 6.1), mais son absence a un repli
que le RFC écrit lui-même : l'annexe A prévoit qu'un serveur prenne alors la valeur de l'en-tête
`Depth`, parce que les clients d'avant le RFC, Apple en tête, n'envoyaient que lui. Le repli coûte
trois lignes et ferme une décision que 4d aurait dû rouvrir. La collection n'ayant pas de
sous-collection, `1` et `infinite` y sont de toute façon indiscernables.

**Le repli vaut pour tout `Depth`, `0` compris, et ce n'est pas une lecture large de l'annexe A —
c'est la seule qui ne se contredise pas.** Le § 3 exige `Depth: 0` sur ce rapport ; l'annexe A propose
de convertir un `Depth` en `sync-level`, où `0` ne vaut rien. Prise à la lettre, la paire refuse d'un
`400` le client qui a posé l'en-tête **conforme** et oublié le seul élément que le RFC ait introduit
pour le remplacer — c'est-à-dire qu'elle punit le plus proche de la norme, sur la première requête
qu'il émet. Un `sync-level` absent vaut donc `1` dès qu'un en-tête `Depth` est présent, quelle que soit
sa valeur.

Le `400` reste pour ce qui n'a aucune lecture : `sync-level` porteur d'une valeur autre que `1` ou
`infinite` — là, accepter reviendrait à deviner ce que le client a voulu dire, sur le rapport où l'on
peut le moins se le permettre — et `sync-level` absent sans aucun en-tête `Depth`, où il ne reste rien
à convertir.

**`DAV:limit`/`DAV:nresults` est honoré, et la troncature se coupe sur une frontière de séquence.**
Dans l'espace `DAV:`, celui de RFC 6578 § 3.6 : `addressbook-query` porte une borne de même nom local
dans un **autre** espace de noms, et la décision 11 s'en explique.
Le carnet monte à 5000 fiches et un client qui borne sa réponse le fait parce qu'il ne sait pas en
digérer plus. Les changements étant ordonnés par `sync_sequence`, une réponse tronquée est
reprenable : elle porte une `DAV:response` de statut `507` avec
`DAV:number-of-matches-within-limits`, et **le jeton du dernier changement rendu**, sur quoi le
client rejoue immédiatement et poursuit là où il s'est arrêté (RFC 6578 § 3.6).

La coupe ne peut pas tomber au milieu d'un rang. Un lot — une suppression groupée, un import — porte
plusieurs lignes à la même séquence ; rendre le jeton *n* après n'avoir servi qu'une partie du rang
*n* abandonnerait le reste pour toujours. La borne est donc respectée au rang près : on sert les
rangs entiers tant que le compte reste sous la borne, et on rend le jeton du dernier rang **complet**
servi. Un rang unique plus gros que la borne est servi entier — dépasser la borne demandée est un
désagrément, en perdre la moitié est une perte de données.

Le compteur étant par utilisateur, deux utilisateurs portent le même jeton pour des carnets
différents. C'est sans conséquence et volontaire : un jeton n'est jamais évalué que contre la
collection de l'utilisateur authentifié, et y mêler l'identifiant du principal ferait fuir celui-ci
dans les journaux de tous les clients sans rien rendre plus sûr.

### 8. Les pierres tombales, et le filigrane qui les rend sûres

`contact_tombstones` retient `(user_id, dav_name, sync_sequence, deleted_at)`. Un balayage les
élague à 180 jours — un troisième `PeriodicSweeper`, la mécanique existe.

La clé primaire étant `(user_id, dav_name)`, un nom qui est supprimé, recréé puis supprimé à nouveau
retomberait sur une ligne existante : la pose d'une tombe est donc un `INSERT … ON DUPLICATE KEY
UPDATE sync_sequence = VALUES(sync_sequence), deleted_at = VALUES(deleted_at)`. La tombe la plus
récente est la seule qui compte, et un `INSERT` nu ferait échouer la deuxième suppression du même
nom sur une violation de clé — en production, sur une donnée que l'utilisateur croit effacée.

**Le filigrane, et sa comparaison, s'écrivent ensemble.** L'élagage pose
`contact_sync_state.pruned_below = P` où `P` est la plus haute séquence à élaguer (jamais à la
baisse : `GREATEST`), **puis** supprime les tombes de `sync_sequence ≤ P`, **et les deux dans une
seule transaction**. Un client au jeton *n*
réclame les tombes de séquence `> n` ; celles qui manquent désormais sont celles de `(n, P]`, donc
**un jeton `n < P` est irrécupérable et répond `403 valid-sync-token`**, tandis que `n ≥ P` reste
servi exactement. La spec retient le refus dès `n ≤ P` : un rang de plus resynchronisé de zéro ne
coûte rien, et une comparaison conservatrice reste juste si un jour l'élagage change de borne.
**L'ordre et la transaction ne sont ni l'un ni l'autre une commodité.** Écrites l'une après l'autre
hors transaction — le `DELETE` d'abord, le filigrane ensuite, comme la première rédaction de cette
décision le disait — les deux instructions laissent une fenêtre où un arrêt du processus est
définitif : les tombes ont disparu, `pruned_below` est resté en arrière, et **les jetons périmés
sont donc acceptés**. La réponse omet alors la suppression sans que rien ne le signale, et le client
garde la fiche pour toujours : le mode de défaillance silencieux que cette colonne existe pour
fermer, réintroduit par le mot « puis ». Une transaction unique le ferme.

L'ordre à l'intérieur de la transaction est le second garde-fou, celui qui survit à ce que la
transaction ne couvre pas — un `P` calculé sur un instantané, une reprise partielle, un élagage
futur qui changerait de borne. Le filigrane d'abord, parce que les deux erreurs ne se valent pas :
un filigrane trop haut refuse un jeton qui aurait pu être servi et coûte une resynchronisation
complète — un désagrément mesurable, et rien de plus ; un filigrane trop bas accepte un jeton dont
la tombe n'existe plus et perd la suppression pour de bon. La règle de la décision 7 est la même,
appliquée à l'autre bout : quand deux écritures ne peuvent pas être simultanées, on ordonne du côté
où l'erreur se rattrape.

Le filigrane et les tombes se **lisent** dans la même transaction — le même instantané InnoDB : un
élagage qui s'intercalerait entre la lecture de `pruned_below` et celle des tombes ferait manquer
celles de `(n, P']` sous un filigrane déjà périmé, et rouvrirait par une course la faille que la
colonne ferme.

Le balayeur peut tourner sur plusieurs instances à la fois. C'est sans conséquence : le `GREATEST`
rend l'écriture du filigrane commutative, et un `DELETE` qui ne trouve plus ses lignes est un
`DELETE` à zéro ligne.

Sans ce filigrane, un jeton périmé serait accepté et la réponse omettrait une suppression **sans que
rien ne le signale** : le client garderait la fiche pour toujours. C'est le seul mode de défaillance
silencieux qui survive à ce document, et c'est pour lui que la colonne existe.

Un `403 valid-sync-token` fait repartir le client d'une synchro complète, où le carnet fait autorité
sur ce qu'il détient : les fiches absentes de la réponse initiale sont celles qu'il doit oublier.

**Un rang par fiche plus une tombe, et non un journal de changements : la divergence avec sabre est
délibérée.** sabre garde une ligne par changement dans `addressbookchanges` — `(uri, synctoken,
operation)`, où `operation` distingue l'ajout, la modification et la suppression — et son élagage
est celui de ce journal. Nous écrasons cette histoire : `contacts.sync_sequence` ne retient que le
dernier rang d'une fiche, et la tombe ne retient que sa disparition. Le résultat servi est
identique, parce qu'un client de `sync-collection` ne demande jamais le chemin parcouru, seulement
l'état d'arrivée — et il coûte une colonne plutôt qu'une table à faire grandir, à indexer et à
élaguer. C'est écrit ici pour qu'aucune revue ne relise ce choix comme un oubli. Ce que le journal
de sabre ne rend pas non plus, ce sont les **contenus** ; ceux-là ont leur table à eux, et pour une
autre raison (décision 17).

### 9. L'ETag est le `card_hash`, et un `PUT` qui transforme n'en renvoie pas

L'ETag vaut `"{card_hash}"`, fort, et il est honnête : les octets servis sont exactement
`vcard_raw`, dont `card_hash` est le SHA-256.

Un point se trompe facilement. 4a insère un `UID` dans une carte qui n'en déclare pas — l'invariant
vaut pour toute carte stockée. Quand cela se produit sur un `PUT`, ce qui est stocké diffère de ce
qui a été envoyé, et le RFC exige alors de **ne pas** renvoyer d'ETag dans la réponse, pour que le
client relise. Renvoyer l'ETag des octets stockés serait pire que de n'en renvoyer aucun : le client
croirait détenir la carte qu'il a envoyée, et ne la relirait jamais.

**Les octets sont stockés tels qu'ils arrivent, fins de ligne comprises.** Le RFC 6350 veut du CRLF ;
un client qui envoie du LF seul produit une carte non conforme, et la normaliser serait une
transformation — donc une réponse sans ETag, une relecture, et une carte qui ne coïncide jamais avec
celle du client. On ne normalise pas : le rôle du serveur est de rendre à tout autre client
exactement ce qu'il a reçu, et c'est aussi ce qui rend `card_hash` égal au SHA-256 des octets servis.
La règle est la même que celle du corollaire de 4a — on ne touche à la carte que lorsqu'on doit y
écrire. Corollaire de test : aucune assertion ne fige une fin de ligne observée, elle se spécifie.

**Un corps qui n'est pas de l'UTF-8 strict est refusé, parce que le stockage est du texte.**
« Les octets tels qu'ils arrivent » est une promesse que le modèle ne peut pas tenir sans cette
clause : `contacts.vcard_raw` est un `MEDIUMTEXT` en `utf8mb4` et `ContactStore` manipule une
`string` — la décision 9 de 4a a bâti l'ETag sur cette chaîne. Un corps en `ISO-8859-1`, que de
vieux exports produisent encore en 3.0 sous un paramètre `CHARSET`, se décoderait donc en `U+FFFD`,
et **l'ETag mentirait** : ce qui est stocké ne serait plus ce qui a été envoyé, `card_hash` ne serait
plus le SHA-256 des octets reçus, et le client croirait détenir sa carte. Le corps est décodé sous
`DecoderExceptionFallback` ; l'échec répond `403 valid-address-data`. C'est la condition juste — la
carte est en cause, pas la requête — et c'est le refus qui rend vraie la phrase du paragraphe
précédent. Le RFC 6350 impose l'UTF-8, et une carte 3.0 mal encodée se convertit chez le client, pas
chez nous : convertir serait transformer, ce que la même décision interdit.

`If-Match` en désaccord répond `412`. `If-None-Match: *` sur une ressource existante répond `412`
également. `If-Match` sur une ressource absente répond `412` et non `404` : la condition est fausse,
et c'est la réponse que le client sait interpréter comme « relis avant de réécrire ».

**La sémantique complète d'`If-Match` est celle du RFC 7232 § 3.1, et il faut l'écrire aussi bien
que celle de `If-None-Match`** — sans quoi seule la lecture serait servie correctement. `If-Match`
accepte une **liste** de valeurs et réussit si l'une d'elles correspond ; il accepte `*`, qui
réussit sur toute ressource existante ; et il compare en **comparaison forte**, celle qui refuse un
ETag faible. Nos ETags étant tous forts (décision 9), la comparaison forte ne rejette en pratique
qu'un `W/` qu'un client n'aurait pas dû nous renvoyer — mais un client qui envoie deux ETags est
courant, et le refuser à tort effacerait sa modification sur un `412` qu'il ne mérite pas. La
lecture reste à l'inverse : `If-None-Match` d'un `GET` compare **faiblement**, liste et `*`
compris, comme le résidu 4a le réclame pour `GetPhoto`.

**`If-Match` vaut aussi sur `DELETE`**, où les clients l'envoient précisément pour ne pas effacer une
fiche modifiée entre-temps ailleurs. Une suppression conditionnelle en désaccord répond `412` et ne
pose aucune tombe.

Les codes de succès sont écrits ici pour n'être pas choisis deux fois : `PUT` répond `201` quand il
crée la ressource et `204` quand il la remplace, `DELETE` répond `204`, `GET` répond `200` avec
`ETag` et `Content-Type: text/vcard; charset=utf-8`, et tout rapport comme tout `PROPFIND` répond
`207`. Un `GET` conditionnel dont l'`If-None-Match` couvre l'ETag courant répond `304` — c'est la
sémantique complète que le résidu 4a réclame, celle que `ContactsController.GetPhoto` adopte du même
geste : liste de valeurs, `*`, et ETags faibles comparés faiblement sur une lecture.

### 10. Le `PUT` est la troisième porte, pas une quatrième

Le diagramme de 4a nomme déjà « carte importée / PUT CardDAV (4c) » comme la porte qui pose la carte
verbatim. 4c s'y branche : carte reçue → `VCardProjector` → `ReplaceProjectionAsync`. Aucun nouveau
chemin d'écriture, aucune règle métier dupliquée.

Ce qui survit à une mise à jour par `PUT` : `id`, `user_id`, `is_favorite`, `source`. Ce qui est
recalculé : tout le reste, puisque tout le reste est une projection.

Un `UID` déjà porté par une **autre** ressource du carnet répond `403 no-uid-conflict` : l'index
unique `(user_id, uid)` posé par 4a est exactement ce garde-fou, il suffit de traduire sa violation
plutôt que de la laisser remonter en 500. Le corps `DAV:error` porte alors le `DAV:href` de la
ressource en conflit, comme le RFC 6352 § 6.3.2 l'exige : sans lui le client sait qu'il a échoué mais
pas ce qu'il doit relire, et la seule issue qui lui reste est de réessayer à l'identique.

**Un `PUT` sur une ressource existante dont la carte porte un autre `UID` est refusé de la même
façon** — `403 no-uid-conflict`, avec le `href` de la ressource elle-même. Le RFC 6352 § 6.3.2.1
couvre explicitement ce cas (« MUST NOT … overwrite an existing address object resource with one
that has a different UID property value ») : l'UID arbitre l'identité de la carte (décision 5), et
un UID qui change sous le même nom est une autre carte. Radicale refuse ; sabre/dav accepte, et ce
laxisme est un bug ouvert chez ses propres mainteneurs (sabre-io/dav#993) — pas un précédent.

Un `PUT` sur un nom que porte une pierre tombale la lève : la tombe est supprimée dans la même
transaction que la création, sous la même avance de séquence. Il ne reste donc jamais une tombe et
une fiche vivantes sur le même nom — un `sync-collection` les rendrait toutes deux, et l'ordre dans
lequel le client les applique déciderait s'il garde la fiche ou l'efface.

**Deux `PUT` créateurs simultanés sur le même nom sont une course que l'index tranche, et elle se
traduit.** Le second passe la pré-vérification d'existence, puis meurt sur l'index unique
`(user_id, dav_name)` — laissé tel quel, c'est un `500`, exactement ce que le § « Les erreurs »
promet de ne jamais rendre. La violation d'unicité à la création est donc rattrapée : rejouée comme
un remplacement de la ressource que l'autre écriture vient de créer — c'est ce que le même `PUT`
arrivé une seconde plus tard aurait été —, ou `412` si la requête portait `If-None-Match: *`,
puisque sa condition est désormais fausse.

**Le `Content-Type` de la requête n'est pas un juge, le corps l'est.** Les clients envoient
`text/vcard`, `text/x-vcard`, `text/directory`, parfois rien du tout, et les trois désignent la même
chose. Le RFC 6352 § 6.3.2 rattache bien le refus d'un média non servi à `supported-address-data`,
mais l'appliquer à l'en-tête refuserait de vieux clients parfaitement corrects pour un mot. L'en-tête
est donc ignoré ; ce qui décide est ce que le corps contient, et les refus sont ceux qui suivent.

**Un `PUT` sans `If-Match` sur une ressource existante est un écrasement aveugle.** Le RFC
l'autorise, et le refuser casserait iOS, qui en émet ; mais c'est par cette porte que la
modification d'une autre fenêtre disparaît sans laisser de trace. On ne l'interdit pas : on
l'archive, comme tout ce qui remplace une carte (décision 17).

**Le corps doit porter une carte, et une seule.** Un corps qui ne parse pas répond `403` avec
`CARDDAV:valid-address-data` — et non `400` : c'est la condition nommée que le client lit pour savoir
que sa carte est en cause et non sa requête. Un corps portant **plusieurs** cartes est refusé de la
même façon : une ressource d'adresse est une carte (RFC 6352 § 5.1). C'est le point que le résidu 4a
annonçait pour cette tranche — `VCardProjector.RawCard` ne s'arrête pas au premier `END:VCARD`, ce
qui était inatteignable tant que le découpeur garantissait une carte par morceau ; le `PUT` supprime
cette garantie, et le refus explicite doit précéder la projection, pas la suivre.

**Une version que `supported-address-data` n'annonce pas est refusée par sa condition à elle.** Un
`VERSION:2.1` — les vieux exports Android en produisent encore — répond `403
supported-address-data`, et non `valid-address-data` : c'est la précondition que le RFC 6352
§ 6.2.2 exige pour un type ou une version hors de la propriété annoncée, et la carte peut être
parfaitement lisible tout en étant refusable. Ni sabre/dav (`415` nu) ni Radicale (stockage
silencieux) ne rendent la condition nommée ; le RFC, si.

**Les plafonds du store deviennent des conditions du protocole.** `ContactStore` refuse au-delà de
1 Mo par carte et de 5000 fiches par utilisateur, et ses refus sont des `Result.Failure` porteurs
d'un message destiné à l'UI. Traduits : le dépassement de taille répond `403 max-resource-size` — la
valeur étant par ailleurs annoncée sur la collection (décision 13) —, le dépassement du nombre de
fiches répond `507 Insufficient Storage`. Laisser remonter l'un ou l'autre en `500` ferait boucler
le client indéfiniment sur la même carte, sans que rien ne lui dise que le carnet est plein.
Le mégaoctet est un point de guet pour 4d : une photo iOS pleine résolution, une fois en base64,
peut le dépasser — la carte ne monterait alors jamais du téléphone, et c'est le journal de la
décision 18, `max-resource-size` nommé, qui le dira.

### 11. `addressbook-query` : le filtre est évalué, ou refusé — jamais ignoré

Un filtre que le serveur ne sait pas évaluer répond `403 supported-filter`.

C'est la décision qui compte dans ce rapport : répondre « tout le carnet » à un filtre incompris a
l'apparence du succès et donne au client un jeu de résultats faux, qu'il inscrira dans son cache.
Un refus explicite le fait basculer sur un listing complet, qu'il sait faire.

**Mais le refus doit rester rare, et c'est la carte qui l'évalue — pas les colonnes.** Restreindre
l'évaluation aux colonnes projetées ferait répondre `403 supported-filter` à des filtres
parfaitement ordinaires : un `prop-filter` sur `NICKNAME`, sur `TITLE`, sur `CATEGORIES`, un
`param-filter` sur `TYPE`. Or le carnet détient `vcard_raw` et 4a en fournit l'analyseur : le
serveur peut évaluer **toutes** les propriétés d'une carte, pas seulement celles qu'il projette.
C'est ce que fait sabre, et c'est ce qui sépare ici un serveur utilisable d'un serveur qui refuse la
moitié des requêtes qu'on lui adresse. Les colonnes projetées gardent leur rôle, mais comme
**pré-filtre indexé** — un `prop-filter` sur `FN`, `EMAIL` ou `TEL` se réduit à une clause SQL qui
restreint le jeu à parser — jamais comme frontière de ce qu'on sait comprendre. Un carnet de 5000
fiches parsé en entier est un dernier recours acceptable ; un `403` sur `TITLE` ne l'est pas.

Ce que le rapport évalue est donc énuméré, et le reste est refusé (RFC 6352 § 10.5) :

| Élément | Traitement |
|---|---|
| `CARDDAV:filter/@test` | `anyof` (défaut) et `allof` |
| `CARDDAV:prop-filter/@name` | toute propriété de la carte, insensible à la casse comme le veut vCard |
| `CARDDAV:prop-filter/@test` | `anyof` (défaut) et `allof` |
| `CARDDAV:is-not-defined` | évalué, dans `prop-filter` comme dans `param-filter` |
| `CARDDAV:param-filter/@name` | évalué sur les paramètres de la propriété retenue |
| `CARDDAV:text-match/@match-type` | `contains` (défaut), `equals`, `starts-with`, `ends-with` |
| `CARDDAV:text-match/@negate-condition` | `yes` et `no` (défaut) |
| `CARDDAV:text-match/@collation` | les deux annoncées, ci-dessous ; toute autre → `403 supported-collation` |
| tout autre élément dans `filter` | `403 supported-filter` |

Une propriété absente de la carte fait échouer son `prop-filter` sans erreur : c'est un filtre qui
ne retient rien, pas un filtre qu'on ne comprend pas. La distinction est ce qui fait que `403
supported-filter` reste un signal, et non le code de retour ordinaire du rapport.

**Un `CARDDAV:filter` vide rend tout le carnet, et c'est un cas particulier — pas une conséquence de
`anyof`.** Le point est piégeux parce que la règle générale donne ici la mauvaise réponse : `anyof` sur
zéro test est faux, donc un `<filter/>` sans `prop-filter` ne retiendrait rien, et le client recevrait
un carnet vide là où il demandait tout. C'est pourtant la forme qu'envoient plusieurs clients pour
« donne-moi ce que tu as », et sabre la traite comme telle dès la première ligne de son évaluateur
(`if (!$filters) { return true; }`). La règle est donc écrite à part, et testée à part : filtre sans
enfant, tout le carnet.

**En revanche l'élément `filter` lui-même est obligatoire.** La définition du RFC 6352 § 10.3 est
`((allprop | propname | prop)?, filter, limit?)`, sans point d'interrogation sur `filter`. Un corps
d'`addressbook-query` qui n'en porte pas répond `400` : ce n'est pas un filtre qu'on ne sait pas
évaluer, c'est une requête incomplète, et `403 supported-filter` mentirait sur ce qui manque. Les deux
règles se ressemblent et disent le contraire l'une de l'autre — `filter` présent mais vide vaut tout,
`filter` absent vaut `400` —, ce qui est exactement pourquoi elles sont voisines ici.

La collation suit la même règle, avec sa condition à elle. `text-match` porte un attribut
`collation` ; le carnet annonce celles qu'il sait via `CARDDAV:supported-collation-set` —
`i;ascii-casemap` et `i;unicode-casemap`, les deux que le RFC 6352 § 8.3 rend obligatoires, toutes
deux servies par la comparaison insensible à la casse des colonnes projetées — et une collation
inconnue répond `403 supported-collation`, pas `supported-filter` : le client doit savoir si c'est
son filtre ou sa collation qui est en cause. sabre répond un `400` sans condition et Radicale
ignore l'attribut ; le MUST du RFC dit autre chose.

La même règle s'applique à ce que le rapport rend, et non plus seulement à ce qu'il filtre.
`CARDDAV:address-data` peut demander un **sous-ensemble** de propriétés (`<CARDDAV:prop
name="EMAIL"/>`) ; le RFC veut alors que la réponse ne porte que celles-là. Rendre la carte entière
serait la version silencieuse du même défaut, avec une conséquence de plus : le client inscrirait
une carte complète dans un cache qu'il croit partiel, et la réécrirait telle quelle. 4c **honore** la
demande partielle, en filtrant les lignes de la carte servie sur les noms demandés — `BEGIN`, `END`,
`VERSION` et `UID` étant toujours conservés, sans quoi ce qui sort n'est pas une carte.

**Et la réponse porte quand même son `getetag`.** La première rédaction de cette décision le
supprimait, par analogie avec le `PUT` transformé de la décision 9 ; l'analogie est fausse, et elle
coûtait cher. `DAV:getetag` est une **propriété de la ressource**, pas l'empreinte du corps qu'un
`propstat` transporte : c'est la valeur que le client range pour savoir, au tour suivant, s'il doit
relire — et un `GET` sur cette ressource lui rendra bien les octets dont `card_hash` est l'empreinte.
La supprimer laisserait le client sans repère sur une carte qu'il vient de recevoir : il la
redemanderait à chaque cycle, ou la tiendrait pour changée indéfiniment. Le cas du `PUT` est l'inverse
exact, et il reste tel quel : là, c'est l'en-tête `ETag` de la réponse — l'empreinte de ce que le
serveur vient de stocker — qui mentirait sur ce que le client a envoyé.

**Un `address-data` qui demande une version est lu, et il ne déclenche aucune conversion.** L'attribut
`version` porte `3.0` ou `4.0`, les deux que `supported-address-data` annonce (décision 13) ; une
valeur hors de ces deux-là — ou un `content-type` qui n'est pas `text/vcard` — répond `403
supported-address-data`, la précondition que le RFC 6352 § 8.6 nomme pour ce cas. Une version annoncée
mais différente de celle de la carte stockée est en revanche **servie telle quelle** : convertir serait
réécrire, ce que 4a interdit hors modification, et refuser rendrait illisible la moitié d'un carnet
mixte chez un client qui a simplement nommé une préférence. C'est une divergence avec sabre, qui
convertit ; elle est nommée ici et journalisée par la décision 18, de sorte que 4d saura si un client
la demande vraiment. L'issue de secours est celle que la décision 13 décrit déjà, et elle ne coûte
aucun invariant : convertir **à la lecture** ne touche pas ce qui est stocké.

**La borne de ce rapport est `CARDDAV:limit`/`CARDDAV:nresults`, et non `DAV:limit`.** Les deux
existent, portent le même nom local et ne sont pas la même chose : RFC 6352 § 10.6 définit la sienne
dans `urn:ietf:params:xml:ns:carddav`, RFC 6578 § 3.6 définit celle de `sync-collection` dans `DAV:`.
Un lecteur qui n'écouterait que `DAV:` ignorerait donc en silence la borne posée par un client
d'`addressbook-query`, et lui servirait les cinq mille fiches qu'il venait de dire ne pas savoir
digérer. La décision 12 pose qu'un élément se reconnaît à son espace de noms et à son nom local ;
c'est ici que la règle se paie. Au-delà de la borne, `507` et
`DAV:number-of-matches-within-limits` — cette postcondition-là est bien dans `DAV:`. Contrairement à
`sync-collection`, ce rapport n'a pas d'ordre de reprise : la troncature y est une réponse partielle
assumée, que le client sait compléter par un listing.

### 12. XML écrit et lu à la main, sans DTD

`XmlWriter` pour les réponses, `XDocument` pour les requêtes, lecteur configuré
`DtdProcessing = Prohibit` et `XmlResolver = null`. Un corps de `REPORT` est une entrée non fiable,
et l'expansion d'entités y est la faille classique — un fichier local lu et renvoyé dans une réponse
`multistatus`.

**La profondeur d'imbrication est bornée elle aussi, à cinquante niveaux.** `DtdProcessing` ferme
l'expansion d'entités, pas la pile : le mégaoctet que la décision 15 autorise laisse la place à
beaucoup de balises imbriquées, la construction de l'arbre y descend, et un débordement de pile en .NET
ne se rattrape pas — il emporte le processus qui sert tous les utilisateurs. Le lecteur compte donc ses
niveaux et refuse par un `400` au-delà. Aucune requête légitime de ce protocole n'en dépasse la
dizaine.

**Un élément se reconnaît à son espace de noms et à son nom local, jamais à son préfixe.** Les
clients écrivent `D:`, `d:`, `a:` ou rien, et lient `DAV:` comme ils veulent ; un lecteur qui compare
`"D:prop"` fonctionne contre l'exemple du RFC et échoue contre le premier client réel. `XDocument`
rend l'`XName` juste par construction — encore faut-il ne jamais redescendre au texte du nom.

Les réponses sortent en `Content-Type: application/xml; charset=utf-8`, avec la déclaration XML.
Le point est écrit ici parce que celui du `GET` l'est (décision 9) et que rien ne le poserait par
défaut sur un corps qu'on écrit soi-même dans `Response.Body`.

Aucune bibliothèque WebDAV .NET libre n'est maintenue ; la seule sérieuse est commerciale. Le volume
à écrire reste modeste parce que la surface est fixe : cinq documents de réponse, trois de requête.

### 13. Le jeu de propriétés est énuméré ici, `access-control` compris

Un client ne demande pas les propriétés qu'un serveur trouve intéressantes ; il demande celles dont
son écran a besoin, et traite l'absence comme un carnet cassé. La liste est donc close et écrite,
plutôt que découverte tranche après tranche par des rapports de bogue :

| Ressource | Propriétés servies |
|---|---|
| `/dav/` | `current-user-principal`, `principal-URL`, `resourcetype` (vide) |
| principal | `resourcetype` (`DAV:principal`, RFC 3744 § 4), `current-user-principal`, `principal-URL`, `displayname` (l'adresse), `addressbook-home-set`, `principal-collection-set`, `supported-report-set` (vide), `alternate-URI-set` (vide), `group-membership` (vide) |
| home | `resourcetype` (collection), `displayname`, `current-user-principal` |
| carnet | `resourcetype` (`collection` + `CARDDAV:addressbook`), `displayname`, `getctag`, `sync-token`, `supported-report-set`, `supported-address-data`, `supported-collation-set`, `max-resource-size`, `current-user-principal`, `current-user-privilege-set`, `owner` |
| carte | `getetag`, `getcontenttype`, `getcontentlength`, `getlastmodified`, `resourcetype` (vide), `current-user-privilege-set` |

Sept méritent leur ligne.

**`supported-report-set` est énuméré, et il est servi sur le principal aussi.** Sur le carnet il porte
les trois rapports de la tranche — `CARDDAV:addressbook-query`, `CARDDAV:addressbook-multiget`,
`DAV:sync-collection` — et rien d'autre : `DAV:expand-property` n'est pas servi, et l'annoncer ferait
tenter un client qui recevrait un `403 supported-report`. Sur le principal il est **vide**, et il y est
quand même parce qu'iOS le demande là — en compagnie de `DAV:resource-id`, de
`{calendarserver}email-address-set` et de `CARDDAV:directory-gateway`, que nous ne portons pas et qui
ressortent en `propstat 404` selon la décision 14. Un jeu vide dit « aucun rapport sur cette
ressource » ; une absence laisse un client conclure qu'il n'a pas su lire la réponse.

**`getlastmodified` et `getcontentlength` ont une source et une unité, et les deux se trompent
facilement.** La date vient de `contacts.updated_at`, que le schéma tient déjà à jour
(`ON UPDATE CURRENT_TIMESTAMP`), et s'écrit en HTTP-date GMT — jamais en ISO, que rien ne lit ici. La
longueur est un nombre d'**octets** UTF-8 (`Encoding.UTF8.GetByteCount`) et non de caractères : c'est
déjà l'unité de `ContactStore.MaxCardBytes` et celle que `max-resource-size` impose. Une carte
accentuée annoncerait sinon une longueur inférieure à son corps, et un client qui coupe à la longueur
annoncée recevrait une carte tronquée — donc invalide, donc rejetée, sans que rien n'indique pourquoi.

**`getctag` est une extension, pas un RFC.** Son espace de noms est celui de CalendarServer
(`http://calendarserver.org/ns/`), et il faut l'écrire ici parce qu'aucun RFC de la tranche ne le
définit. Il reste servi parce que les clients s'en servent encore : DAVx⁵ le demande à chaque
interrogation d'état et s'y replie quand `sync-collection` manque.

**`supported-address-data` annonce 3.0 et 4.0.** La décision 7 de 4a le prévoyait mot pour mot :
sans cette propriété, un client est en droit de ne rien attendre ni n'envoyer d'autre que du 3.0. Or
le carnet stocke verbatim les deux versions et sert ce qu'il détient — annoncer le seul 3.0 ferait
mentir la moitié des réponses. Les deux versions annoncées, un client averti sait qu'une carte 4.0
peut lui parvenir. Aucune conversion à la volée n'est offerte : nous ne réécrivons pas une carte
qu'on ne modifie pas, et `supported-address-data-conversion` reste hors de la tranche.

L'annonce a un effet de bord à nommer, parce qu'il ne se voit que sur une flotte mixte : DAVx⁵ lit
`supported-address-data` et, si le 4.0 y figure, peut téléverser ses cartes en 4.0 — qu'un iPhone
partageant le même carnet lit mal ou pas du tout. Annoncer n'est pas seulement décrire ce qu'on
détient, c'est inviter à en écrire. La décision tient — annoncer le seul 3.0 ferait mentir les
cartes 4.0 importées —, mais c'est un point de guet nommé pour 4d, avec l'issue de secours déjà
connue si un foyer Android + iPhone se présente : honorer l'attribut `version` d'`address-data`, la
conversion à la lecture que sabre sait faire.

**`max-resource-size` vaut le plafond de `ContactStore.MaxCardBytes`**, la même constante et non un
littéral recopié. 4a l'exigeait déjà de cette tranche ; une valeur annoncée que le store violerait,
ou l'inverse, se paierait en cartes refusées sans que le client comprenne pourquoi.

**`current-user-privilege-set` existe parce que le RFC 6352 § 3 impose le contrôle d'accès**
(RFC 3744) à tout serveur CardDAV, et parce que DAVx⁵ et iOS le lisent pour décider si le carnet est
en lecture seule — sans réponse, certains basculent en lecture seule par prudence et les
modifications du téléphone ne remontent jamais. Le carnet n'a qu'un propriétaire et pas de partage :
la propriété rend donc un jeu constant — `read`, `write`, `write-content`, `write-properties`,
`bind`, `unbind`, `read-current-user-privilege-set` — et l'en-tête `DAV:` annonce `access-control`.
Ce n'est pas un modèle d'ACL : c'est la déclaration honnête qu'un utilisateur peut tout faire sur son
propre carnet, et rien sur celui d'un autre, ce que la décision 4 fait déjà répondre `404`. Aucun
`ACL` ni `acl-principal-prop-set` n'est servi ; ils répondent `405`.

**Annoncer `access-control` engage les propriétés de principal du RFC 3744, et deux d'entre elles
sont vides.** Le § 4 rend `DAV:alternate-URI-set`, `DAV:principal-URL` et `DAV:group-membership`
obligatoires sur tout principal. La deuxième était déjà servie ; les deux autres sont des éléments
vides — aucune identité de rechange, aucune appartenance de groupe — et les écrire coûte deux lignes
là où les omettre laisse un client conclure que le principal n'en est pas un. En revanche
`DAV:supported-privilege-set` et `DAV:acl` ne sont **pas** servis, et c'est dit ici plutôt que
constaté en 4d : ils décrivent une politique négociable, le carnet n'en a pas, et les rendre
reviendrait à publier un arbre de privilèges figé que rien ne peut modifier. Un client qui les
demande reçoit le `404` de `propstat` de la décision 14 — la réponse qui dit « je ne porte pas cette
propriété », et non celle qui laisse attendre.

### 14. `allprop`, `propname`, un corps vide, et la propriété qui manque

Un `PROPFIND` **sans corps** vaut `allprop` (RFC 4918 § 9.1), et plusieurs clients en envoient un à
la découverte. Un corps `allprop` rend le jeu de la décision 13 pour la ressource visée, hors
`sync-token` et `current-user-privilege-set` — deux propriétés que le RFC autorise à ne pas verser
dans `allprop` parce qu'elles coûtent, et qu'un client qui les veut nomme explicitement. `propname`
rend les noms du même jeu, sans valeurs.

Une propriété demandée que la ressource ne porte pas **n'est pas omise** : elle ressort dans un
second `propstat` à `404 Not Found`, comme le RFC l'exige. L'omission pure est ce qui fait qu'un
client attend indéfiniment une valeur qu'il croit en route.

**Un `PROPFIND` sans en-tête `Depth` vaut `Depth: infinity`** (RFC 4918 § 10.2), donc répond `403
propfind-finite-depth` comme lui. sabre y devine `1`, Radicale `0` — deux réponses différentes au
même silence, ce qui est exactement pourquoi on ne devine pas. Et ici il n'y a rien à deviner : le
RFC pose lui-même la valeur du silence, et cette valeur est celle que la collection refuse. C'est ce
qui distingue le cas du `sync-level` absent de la décision 7, où le repli existe parce que le RFC
l'écrit et parce qu'un autre en-tête porte la réponse. Les clients réels envoient toujours l'en-tête ;
si 4d en trouve un qui l'omet, la décision se rouvrira avec un client nommé.

Le refus vaut mieux qu'une devinette pour une raison qui n'est pas symétrique : deviner `0` rendrait
un `multistatus` **valide** ne portant que la collection, qu'un client demandant `1` lit comme un
carnet vide — et un carnet vide, il l'applique en effaçant ses copies locales. Une erreur ne se
confond avec rien ; une réponse correcte au mauvais `Depth`, si.

**Cette règle est celle du `PROPFIND`, et d'aucun autre verbe.** Le `REPORT` a sa propre sémantique
de profondeur, et l'y étendre casserait les trois rapports : `addressbook-query` s'applique en
`Depth: 1` sur la collection (RFC 6352 § 8.6), `addressbook-multiget` en `Depth: 0`, ses cibles
venant du corps (§ 8.7), et `sync-collection` n'en porte pas du tout, `DAV:sync-level` l'ayant
remplacé (RFC 6578 § 3). Un `Depth` absent sur un `REPORT` prend donc la valeur que ce rapport-là
implique, et un `Depth` présent mais inattendu est ignoré plutôt que refusé — il n'y a rien à
deviner là où le rapport dit déjà à quoi il s'applique.

**« Ignoré » a un prix sur `addressbook-query`, et il est payé sciemment.** Le RFC 6352 § 8.6 dit que
la portée de ce rapport est celle de son en-tête `Depth` : un `Depth: 0` ne devrait donc évaluer que la
collection, c'est-à-dire ne rendre aucune carte. Nous rendons le résultat du filtre quelle que soit la
valeur. Aucun client connu n'envoie `Depth: 0` sur une requête dont il attend des cartes, et rendre
zéro carte à qui en demande est précisément le mode de défaillance que toute cette spec chasse.
`ccs-caldavtester` peut relever le point en 4d, comme le `Depth` de `sync-collection` (décision 7) ; ce
sera là aussi une divergence nommée.

### 15. Les réponses sortent au fil de l'eau, les requêtes sont bornées

Un carnet plein fait 5000 fiches, et une carte peut peser 1 Mo : une réponse `addressbook-query`
portant `address-data` sur tout le carnet se compte en gigaoctets. Les documents `multistatus` sont
donc écrits directement dans `Response.Body` par un `XmlWriter`, une `response` à la fois, la carte
étant lue par lots depuis la base — jamais un document construit en mémoire puis sérialisé, ce qui
mettrait le carnet entier dans le tas d'un processus qui sert tous les utilisateurs.

Symétriquement les corps de requête sont bornés avant d'être lus : **1 Mo pour un corps de rapport**
(`RequestSizeLimit`, dont le refus est le `413` standard d'ASP.NET) et **5000 `DAV:href` au plus**
dans un `addressbook-multiget`. Un multiget est une liste que le client compose ; rien n'en borne
la longueur côté protocole, et une requête de quelques kilo-octets ne doit pas pouvoir demander
cinquante mille lectures. Le RFC 6352 ne prévoit aucune précondition pour ce refus ; le dépassement
répond donc par le motif que les clients savent déjà lire, celui de la troncature (§ 8.6.2) : un
`207` dont la `DAV:response` de la Request-URI porte `507` et
`DAV:number-of-matches-within-limits` — pas un `403` sec, qui n'est adossé à aucun texte.

Dans un `multiget`, un `href` inconnu ressort en `404` **à l'intérieur** du `multistatus`, jamais en
erreur globale : le rapport est une lecture par lot, et un nom périmé dans la liste d'un client est
un cas courant, pas une faute. Un `href` qui ne désigne pas une ressource de cette collection est
traité de même — `404`, sans jamais aller lire ailleurs.

### 16. Ce qu'on ne sert pas répond `405`, pas `404` ni `500` — sauf `PROPPATCH`

`MKCOL`, `MKCALENDAR`, `COPY`, `MOVE`, `ACL`, `LOCK`, `UNLOCK`, et — sur une
collection — `GET`, `PUT` et `DELETE` : la réponse est `405 Method Not Allowed` avec un en-tête
`Allow` conforme à celui d'`OPTIONS`. `LOCK` et `UNLOCK` sont dans la liste bien que l'en-tête
`DAV: 1, 3` annonce déjà l'absence de verrous : l'annonce dit ce qu'on sait faire, le `405` dit ce
qu'on répond quand un client ne l'a pas lue, et sans lui le routage rendrait un `404` — donc « cette
carte n'existe pas » sur une carte qui existe. Thunderbird tente un `GET` de collection ; un `500`
dessus fait abandonner le carnet entier, là où un `405` est une réponse que tout client sait ranger.

**`PROPPATCH` est la seule exception, et elle répond `207`.** Le ranger avec les autres serait le geste
naturel — rien n'est mutable ici, le nom du carnet est fixe — et ce serait faux à deux titres. D'abord
l'en-tête `DAV: 1` engage : RFC 4918 § 18.1 fait de la classe 1 la satisfaction de **tous** les MUST du
document, `PROPPATCH` compris, et annoncer `1` en rendant `405` est une contradiction qu'un test de
conformité relève au premier passage. Ensuite les clients d'Apple ne s'en servent pas pour ce qu'on
croit : Contacts.app `PROPPATCH` la propriété `{http://calendarserver.org/ns/}me-card` sur le carnet
pour y désigner la fiche de son propriétaire, et sabre documente que l'absence de prise en charge peut
le faire **planter** — pas abandonner le carnet, planter. La réponse est donc celle que le RFC 4918
§ 9.2.1 prévoit pour une propriété qu'on ne laisse pas écrire : un `207` dont chaque `propstat` porte
`403 Forbidden` pour la propriété demandée. Le client apprend que rien n'a été écrit, ce qui est vrai,
par un chemin qu'il sait lire. Le coût est celui du `405` ; la différence est qu'il est conforme.

Rien n'est stocké au passage, et ce n'est pas un oubli : accepter `me-card` demanderait une propriété
morte de plus en base, pour un usage qu'aucun écran du produit ne rend. Si 4d montre qu'un client
d'Apple exige de relire ce qu'il vient d'écrire, ce sera une colonne — pas un modèle de propriétés
mortes ouvert par anticipation.

`PUT` et `DELETE` de collection méritent leur phrase parce que les routes ne les lient que sur
`{nom}`, un segment que l'URL du carnet — terminée par une barre — ne présente pas : le routage y
rendrait donc un `404` accidentel. Le RFC 4918
§ 9.7.2 interdit le `PUT` de collection, et un `DELETE` de collection effacerait le carnet entier —
un geste que le produit ne propose nulle part et qu'aucune route ne doit offrir par accident ; les
serveurs de référence le servent, mais leur carnet n'est pas lié au compte comme le nôtre. La même
logique vaut dans le corps plutôt que le verbe : un `REPORT` dont le corps nomme un rapport inconnu
répond `403 supported-report` (RFC 3253), jamais `400` ni `500`.

**`Allow` est énuméré ici, parce qu'un en-tête « conforme à `OPTIONS` » ne se vérifie pas.** Deux
valeurs, une par forme de ressource :

```
collection   OPTIONS, PROPFIND, PROPPATCH, REPORT
carte        OPTIONS, HEAD, GET, PUT, DELETE, PROPFIND
```

`PROPPATCH` figure dans l'`Allow` de la collection parce qu'il y est **servi** — d'un `207` qui refuse
chaque propriété, mais servi (ci-dessus). L'omettre ferait dire à l'en-tête le contraire de ce que la
méthode répond.

`HEAD` y figure parce que HTTP l'exige dès que `GET` existe. ASP.NET Core le sert d'office sur une
route `GET` — mêmes en-têtes, `ETag` compris, corps vide —, ce qui suffit ; ce qui ne se fait pas
tout seul est de le **nommer** dans `Allow` et dans `OPTIONS`, et un client qui ne l'y voit pas ne
l'essaie pas.

**Une URL de collection sans barre oblique finale est redirigée, pas refusée.**
`…/addressbooks/{userId}/default` — sans la barre — désigne la même collection pour un humain et
rien du tout pour le routage, qui rendrait un `404`. Le service répond `301` vers la forme avec
barre, comme le font sabre et Radicale. C'est le pendant de la règle des `href` du § « La surface
HTTP » : nous écrivons toujours la barre, donc nous n'en dépendons pas — mais l'adresse saisie à la
main dans un client, elle, ne passe pas par nos `href`.

### 17. Le conflit se détecte par ETag et se rattrape par un historique

Tout ce qui précède fait du serveur un bon détecteur de conflits. Il n'en fait pas un serveur d'où
la donnée ne sort pas. C'est la différence que cette décision comble, et elle vaut d'être posée
comme une question concrète : un téléphone modifie une fiche hors réseau, le webmail modifie la même
fiche pendant ce temps, le réseau revient. Quatre croisements, et ce que les décisions 9 et 10 en
font — la colonne de droite décrit l'état **avant** la présente décision, qui la corrige ligne par
ligne plus bas :

| Hors ligne, le téléphone… | Entre-temps, le webmail… | Ce que le serveur répond | Sort de la donnée |
|---|---|---|---|
| modifie | modifie | `PUT If-Match` en désaccord → `412` | DAVx⁵ abandonne sa version et reprend celle du serveur — l'édition du téléphone disparaît |
| modifie | supprime | `PUT If-Match` sur ressource absente → `412` | l'édition du téléphone disparaît |
| supprime | modifie | `DELETE If-Match` en désaccord → `412`, aucune tombe | correct : le client relit, rien n'est perdu |
| modifie | modifie, mais le client n'envoie pas d'`If-Match` | `PUT` accepté | l'édition du webmail est écrasée sans trace |

La détection est juste et conforme dans les quatre cas. **La récupération n'existe nulle part**, et
trois lignes sur quatre effacent le travail de quelqu'un. Un `412` n'est pas une perte du point de
vue du protocole — le serveur a refusé, le client sait — mais il l'est du point de vue de
l'utilisateur, qui a saisi une adresse dans un train et ne la retrouvera pas. Le protocole ne peut
pas trancher pour lui : deux versions d'une carte ne fusionnent pas, et un serveur qui choisirait à
la place de l'humain se tromperait la moitié du temps.

**Les deux serveurs de référence répondent différemment, et c'est instructif.** sabre/dav — donc
Baïkal — ne fait rien de plus : `If-Match` en désaccord, `412`, la version perdante disparaît ; sa
table `addressbookchanges` est un journal d'**opérations** (`uri`, `synctoken`, `operation`), pas de
contenus, et ne peut donc rien restituer. Radicale ne fait rien non plus dans le protocole — mais
son crochet de stockage documenté est un `git commit` après chaque écriture, et le dépôt git *est*
la récupération : toute version écrasée ou effacée reste dans l'historique. Autrement dit, celui des
deux qui traite le sujet le traite **hors du protocole, par un historique de contenus**. C'est la
bonne réponse, et elle se transpose sans git.

**Toute écriture qui remplace une carte, et toute suppression, archivent d'abord ce qu'elles
effacent**, dans `contact_revisions`, dans la même transaction que l'écriture — donc sous le même
rang, et donc jamais sans elle. La table retient les octets, pas un diff : `vcard_raw` est déjà la
donnée souveraine, et une révision qui aurait besoin d'être rejouée pour être lue ne serait pas une
sauvegarde. Chaque ligne porte la cause (`put`, `webmail`, `import`, `delete`, `rejected`), sans quoi
on ne sait pas si l'on regarde un écrasement à rattraper ou une modification voulue.

L'élagage est celui des tombes, à 30 jours plutôt que 180 : une révision sert à réparer un accident
qu'on remarque dans la semaine, pas à archiver. Le volume est borné par les plafonds existants et
reste, en pratique, de l'ordre de quelques mégaoctets par utilisateur actif.

**La fenêtre de réparation est donc de trente jours, y compris pour une suppression** — dont la tombe,
elle, vit six mois. L'asymétrie est voulue et se lit dans ce sens : la tombe est ce que le
**protocole** doit encore savoir dire à un client parti longtemps, la révision est ce qu'un **humain**
peut encore vouloir récupérer. Passé trente jours, une fiche effacée reste correctement effacée
partout ; elle n'est simplement plus restituable.

Ce que cela achète, ligne par ligne du tableau : l'écrasement aveugle d'iOS et la suppression
regrettée deviennent récupérables.

**Et les deux lignes à `412` le deviennent aussi, parce que la phrase qui les excusait était fausse.**
La première rédaction disait que la version refusée « n'a jamais atteint le carnet, elle ne vit que sur
l'appareil, et aucun serveur ne peut archiver des octets qu'il n'a pas reçus ». Le carnet ne les a pas,
c'est vrai ; le serveur, lui, **les a** — ils sont dans le corps de la requête, déjà lu, déjà borné à
1 Mo par la décision 15, déjà décodé et validé par la décision 9. Les jeter est une décision, pas une
fatalité. Or c'est exactement le cas qu'on redoute : une adresse saisie dans un train, un webmail qui a
bougé entre-temps, un `412`, et DAVx⁵ qui applique « le serveur gagne » sans consulter personne — son
manuel le dit en ces termes, et précise qu'il n'implique jamais l'utilisateur dans la résolution d'un
conflit. Le refus est juste ; l'effacement qui le suit ne l'est pas.

**Un `PUT` refusé pour cause d'`If-Match` archive donc son corps**, sous la cause `rejected`, avant que
le `412` ne parte. C'est une écriture sur un chemin déjà transactionnel, et c'est le seul endroit de
cette tranche où l'on fait strictement mieux que les deux serveurs de référence : le crochet git de
Radicale ne voit que les écritures **acceptées**, et sabre ne voit rien du tout. Deux garde-fous, parce
qu'un client en désaccord ne l'est pas une fois mais à chaque cycle : rien n'est archivé si le triplet
`(user_id, card_hash, cause)` a déjà été écrit dans les vingt-quatre heures — un téléphone qui rejoue
la même carte tous les quarts d'heure écrit une révision, pas quatre-vingt-seize — et un `DELETE`
refusé n'archive rien, puisqu'il n'apporte aucun octet : il laisse la ligne de journal de la
décision 18, et c'est tout.

Reste ce qu'aucun serveur ne peut faire : la carte que le téléphone n'a **pas encore** envoyée, faute
de réseau. Celle-là ne vit que sur l'appareil, et sa survie appartient au client. La distinction vaut
d'être tenue nette, sans quoi une revue croira l'historique capable d'un miracle qu'il ne fait pas.

**La tranche ne livre pas d'écran de
restauration** — c'est un geste rare, qui se fait par requête, et lui donner une interface avant
d'avoir vu un seul cas réel serait construire à l'aveugle. Elle livre la donnée sans laquelle cet
écran ne pourrait jamais exister, et c'est le seul moment où elle peut le faire : une révision qu'on
n'a pas écrite ne se retrouve pas après coup.

**La pierre tombale, elle, ne porte pas la carte** — et c'est ce qui justifie que les révisions
enregistrent aussi les suppressions plutôt que la tombe. La tentation est là : une tombe sait déjà
quel nom a disparu, lui ajouter les octets tiendrait en une colonne. Mais la carte serait alors
écrite à deux endroits selon la porte empruntée, avec deux chemins d'élagage, deux durées et deux
occasions de n'en réparer qu'un. La tombe reste ce qu'elle est — l'état minimal que le protocole
lit, une par nom, écrasée à chaque nouvelle suppression du même nom — et `contact_revisions` reste
le seul endroit où l'on va chercher un contenu, quelle qu'en soit la cause. Une fiche supprimée
depuis le téléphone alors que le webmail venait de l'enrichir s'y retrouve sous
`(user_id, dav_name)`, avec la cause `delete`.

**Et le webmail cesse d'écrire sans regarder.** `PUT /api/contacts/{id}` écrase aujourd'hui sans
aucune version : le côté DAV gagne des ETags dans cette tranche, le côté webmail garderait le
dernier-arrivé-gagne, et un onglet ouvert depuis dix minutes réécrirait en silence la fiche que le
téléphone vient de modifier. Ce serait le trou de la décision 6 — une porte qui ne respecte pas
l'invariant des autres — sur la porte la plus fréquentée. L'éditeur renvoie donc le `card_hash` qu'il
a lu, `UpdateAsync` refuse par un `409` s'il a bougé, et l'écran propose de recharger. C'est le même
contrôle qu'`If-Match`, exprimé dans la langue de l'API ; il est **dans cette tranche** parce que
c'est elle qui crée le second écrivain.

### 18. Ce qu'on journalise, parce qu'un carnet vide ne dit rien

Le prérequis du proxy inverse le formule déjà pour son propre cas : le symptôme d'à peu près toutes
les pannes de ce protocole est « le carnet est vide côté client », et c'est celui qui coûte le plus
cher à diagnostiquer parce qu'il ne distingue rien. Un en-tête `Authorization` avalé, un `PROPFIND`
refusé par le pare-feu, un rattrapage incomplet, un jeton refusé en boucle, un filtre `403` que le
client ne sait pas rattraper : cinq causes, un seul symptôme, et aucune trace côté serveur pour les
séparer.

Chaque requête `/dav` laisse donc une ligne structurée : verbe, ressource, profondeur ou rapport,
jeton reçu et jeton rendu, nombre de `DAV:response` écrites, code et — quand il y en a une — la
condition de précondition nommée. Cela ne coûte rien à écrire et transforme la conformité de 4d en
lecture de journal plutôt qu'en capture réseau. Les identifiants n'y figurent jamais, ni le secret,
ni le contenu d'une carte : l'utilisateur y est le GUID du principal, celui qui est déjà dans l'URL.

### 19. L'écran : un onglet « Sync », un interrupteur, trois valeurs à copier

C'est le seul écran de la tranche, et il porte tout 4c-i. Un onglet de plus dans les paramètres,
`/settings/sync`, inséré dans la liste de `SettingsLayout.tsx` comme ses voisins :

```
┌──────────────────────────────────────────────────────┐
│ Sync                                                 │
├──────────────────────────────────────────────────────┤
│                                                      │
│  Contacts (CardDAV)                    [ ●━] Enabled │
│  Sync your address book with your phone or           │
│  Thunderbird. Turning this off stops every device;   │
│  your password is kept.                              │
│                                                      │
│  ── Connection ─────────────────────────────────────  │
│                                                      │
│  Server URL   https://api.mail.weesky.net       [⧉]  │
│  Username     alice@weesky.be                   [⧉]  │
│  Password     ••••••••••••••••••    [ Regenerate ]   │
│  Last used    2 hours ago                            │
│                                                      │
└──────────────────────────────────────────────────────┘
```

**L'onglet est nommé pour ce qu'il fait, pas pour le protocole qu'il parle.** « CardDAV » est un mot
que l'utilisateur rencontre dans son client, pas dans sa tête ; il cherche à synchroniser ses
contacts. Et l'onglet accueillera CalDAV : le nommer d'après le premier protocole arrivé obligerait
à le renommer au second, sur une route que des marque-pages auront gardée.

**L'interrupteur est par protocole, le secret est partagé.** La colonne s'appelle donc
`carddav_enabled` et non `enabled` : le jour où le calendrier arrive, c'est une seconde ligne dans
le même panneau et une seconde colonne, pas une migration. À l'inverse le secret ne se dédouble pas
— il authentifie une personne, pas un protocole, et deux secrets à recopier là où un suffit seraient
deux fois plus de configuration pour la même sécurité. C'est le seul endroit où cette spec anticipe
une tranche à venir, et elle le fait parce que le coût de l'anticipation est un mot dans un nom de
colonne, tandis que le coût de l'omission est un renommage sous des clients déjà configurés.

**Allumer engendre, et c'est un seul geste.** Basculer l'interrupteur pour la première fois crée la
ligne, engendre le secret et le rend dans la **réponse de cette même requête** ; l'écran l'affiche
aussitôt. Demander à l'utilisateur d'activer puis de cliquer « créer un mot de passe » lui ferait
faire deux fois ce qu'il croyait faire une, et l'écran vide entre les deux n'aurait rien à dire.
Éteindre pose `carddav_enabled = 0` sans rien détruire (décision 2) et le rallumage ne réaffiche
aucun secret : il n'y a rien de nouveau à montrer.

**Le secret s'affiche à la génération, et à ce moment seulement.** Il n'existe en clair que dans
cette réponse-là ; l'écran le présente en clair, en chasse fixe, avec un bouton de copie, et un
avertissement disant qu'il ne sera plus montré. Il disparaît au premier changement de page — et il
n'y a **jamais** de bouton « révéler », parce qu'il n'y a rien à révéler : la table n'en porte que le
condensat (décision 1). Le point mérite d'être écrit dans la spec plutôt que découvert en revue :
un écran qui promettrait de réafficher le secret imposerait de le stocker en clair ou déchiffrable,
et ferait de cette table un trousseau à voler. Les deux autres valeurs, elles, sont affichées en
permanence — ce sont celles que l'utilisateur revient chercher pour configurer un deuxième appareil.

**`Regenerate` demande confirmation, et la confirmation nomme la conséquence** — « every device will
stop syncing until you enter the new password » — parce que c'est un geste dont l'effet se produit
ailleurs que sur l'écran où on le déclenche. La même phrase, sous une autre forme, est déjà due au
bouton de déconnexion globale et à l'écran de changement de mot de passe (décision 2) : ces deux-là
détruisent le secret sans que l'écran de synchronisation soit ouvert.

`Last used` répond à la seule question qu'on se pose devant ce bouton : est-ce que quelque chose
s'en sert encore ? La valeur vient de `last_used_at`, amortie à l'heure (décision 1), et se rend en
relatif. Jamais utilisé se dit, et ne se laisse pas deviner d'une case vide : c'est le symptôme le
plus courant d'une configuration client qui n'a jamais abouti.

**L'adresse du serveur vient du serveur.** Elle est rendue par l'API depuis sa configuration, jamais
composée côté navigateur : le front connaît l'URL qu'il appelle, qui n'est pas nécessairement celle
que le proxy publie, et une adresse fausse sur cet écran est une configuration client qui échoue
sans que rien n'indique où. C'est l'hôte nu, sans chemin — le client le complète par
`/.well-known/carddav` (§ « La surface HTTP »), et lui donner un chemin ferait échouer ceux qui
concatènent.

**Et sans port.** Certaines versions du client CardDAV d'iOS ignorent un port non standard et tentent
443 puis 80 quoi qu'on leur ait donné ; une adresse qui en porte un est donc une configuration qui
échoue sur un appareil et réussit sur l'autre, pour une raison invisible des deux côtés. Le service
étant publié en 443 derrière son proxy, la configuration qui alimente cet écran ne doit jamais en
composer un — et si un déploiement l'exigeait un jour, ce serait au proxy de le résoudre, pas à
l'écran de l'afficher.

L'onglet est gaté sur `isPrimary`, comme Account et Aliases : le secret authentifie l'utilisateur
weesky, et un compte externe connecté n'a ni carnet ni principal. Il l'est aussi sur une capacité —
`capabilities.dav !== false`, la lecture que `SettingsLayout` fait déjà pour Aliases et Admin —
pour qu'un déploiement sans les routes `/dav` n'affiche pas un onglet mort.

## La surface HTTP

```
*        /.well-known/carddav                    301 → /dav/ · anonyme · toute méthode
PROPFIND /                                       current-user-principal (l'hôte nu saisi tel quel)
OPTIONS  /dav/…                                  DAV: 1, 3, access-control, addressbook · Allow
PROPFIND /dav/                                   current-user-principal
PROPFIND /dav/principals/{userId}/               addressbook-home-set, principal-URL
PROPFIND /dav/addressbooks/{userId}/             depth 0 et 1 → la collection « default »
PROPFIND /dav/addressbooks/{userId}/default/     depth 0 → les propriétés de collection (décision 13)
                                                 depth 1 → la collection, puis une ressource par fiche
PROPPATCH …/default/                             207, chaque propriété refusée en 403 (décision 16)
REPORT   …/default/                              addressbook-multiget · addressbook-query · sync-collection
GET/HEAD …/default/{nom}                         200, la carte verbatim, ETag, text/vcard; charset=utf-8
PUT      …/default/{nom}                         201 / 204 · If-Match / If-None-Match
DELETE   …/default/{nom}                         204 · If-Match · pose une pierre tombale
*        …/default  (sans barre finale)          301 → …/default/
autres verbes                                    405 + Allow (décision 16)
```

`{nom}` est capté entier, suffixe compris : la route n'exige pas `.vcf`, c'est la validation de la
décision 5 qui juge. Les fiches nées dans le webmail portent `{id}.vcf` par convention, pas par
contrainte.

L'utilisateur saisit `https://api.mail.weesky.net` dans son client ; le service sert lui-même
`/.well-known/carddav`, sans rien à configurer sur le serveur web. L'adresse n'étant pas devinable,
l'onglet « Sync » l'affiche, prête à copier (décision 19).

**Le well-known répond à toute méthode, et sans authentification.** Un `[HttpGet]` ne suffit pas :
DAVx⁵ et Thunderbird y envoient un `PROPFIND`, pas un `GET`, et une redirection réservée au `GET`
leur rend un `405` au premier geste de la découverte. La route accepte donc tous les verbes et rend
`301` (RFC 6764 § 6) ; elle est anonyme, parce qu'un `401` sur une redirection publique est un
obstacle gratuit avant même que le client sache où s'authentifier.

**`PROPFIND /` sert `current-user-principal`.** Le client à qui l'on donne l'hôte nu essaie la racine
autant que le well-known ; deux lignes de plus lui évitent d'échouer sur un chemin que nous
n'utilisons pas nous-mêmes. La route vit hors de `/dav` mais porte la **même politique
d'autorisation** que lui — les deux schémas en lecture, le défi `Basic` seul : un client qui
commence par la racine avec le secret de synchronisation recevrait sinon un défi `Bearer`, et
c'est le symptôme que la décision 2 chasse.

`OPTIONS` répond sur toute URL `/dav`, authentifié ou non : un client demande les capacités avant
d'avoir des identifiants. L'en-tête `DAV:` porte `access-control` (décision 13) en plus de `1, 3,
addressbook`.

Les `href` des réponses sont des chemins absolus (`/dav/addressbooks/…`), jamais des URL complètes :
le service est derrière un proxy inverse, et une URL absolue reconstruite depuis l'hôte vu par
Kestrel n'est pas celle que le client a demandée. La collection porte toujours sa barre oblique
finale, une carte n'en porte jamais — un client compare des `href` littéralement.

ASP.NET Core route les méthodes non standard par `[AcceptVerbs("PROPFIND")]` ; Kestrel les accepte
sans configuration.

## Le schéma

Cinq changements, en SQL manuel — le projet n'a pas de migrations EF (`PreferencesDbContext`) — et
consignés dans `docs/superpowers/webmail-carddav-tables.md`, sur le modèle de
`webmail-contacts-tables.md`.

```
dav_credentials     user_id         CHAR(36)      NOT NULL  PK
                    carddav_enabled TINYINT(1)    NOT NULL DEFAULT 1
                    secret_hash     CHAR(64)      NOT NULL        SHA-256 hex de (sel ‖ secret)
                    salt            VARBINARY(16) NOT NULL
                    created_at      TIMESTAMP     NOT NULL
                    last_used_at    TIMESTAMP     NULL
                    FK user_id → users(id) ON DELETE CASCADE
contact_sync_state  user_id      CHAR(36) NOT NULL  PK
                    epoch        CHAR(36) NOT NULL            GUID ; change à la restauration
                    seq          BIGINT UNSIGNED NOT NULL DEFAULT 0
                    pruned_below BIGINT UNSIGNED NOT NULL DEFAULT 0
                    FK user_id → users(id) ON DELETE CASCADE
contact_tombstones  user_id       CHAR(36)     NOT NULL
                    dav_name      VARCHAR(255) NOT NULL            utf8mb4_bin
                    sync_sequence BIGINT UNSIGNED NOT NULL
                    deleted_at    TIMESTAMP    NOT NULL
                    PK (user_id, dav_name) · INDEX (user_id, sync_sequence)
                    FK user_id → users(id) ON DELETE CASCADE
contact_revisions   id            BIGINT UNSIGNED NOT NULL AUTO_INCREMENT  PK
                    user_id       CHAR(36)     NOT NULL
                    contact_id    CHAR(36)     NULL                la fiche, quand elle en a une
                    uid           VARCHAR(255) NOT NULL            l'UID de la carte archivée
                    dav_name      VARCHAR(255) NULL   utf8mb4_bin
                    card_hash     CHAR(64)     NOT NULL
                    vcard_raw     MEDIUMTEXT   NOT NULL            les octets remplacés ou refusés
                    cause         ENUM('put','webmail','import','delete','rejected') NOT NULL
                    replaced_at   TIMESTAMP    NOT NULL
                    INDEX (user_id, replaced_at) · INDEX (user_id, uid) · INDEX (user_id, dav_name)
                    FK user_id → users(id) ON DELETE CASCADE
contacts          + dav_name      VARCHAR(255) NULL  utf8mb4_bin  UNIQUE (user_id, dav_name)
                  + sync_sequence BIGINT UNSIGNED NOT NULL DEFAULT 0  INDEX (user_id, sync_sequence)
```

**`seq` et non `sequence`** : `SEQUENCE` est un mot-clé MariaDB depuis 10.3, et une colonne qui
n'existe qu'entre back-quotes est une erreur de production en attente, dans un projet où le SQL se
passe à la main.

**`dav_name` est nullable, `sync_sequence` ne l'est pas.** L'unicité MySQL ignore les `NULL`, donc la
colonne peut rester vide sur les fiches que le rattrapage n'a pas encore atteintes, sans que le
premier `PUT` d'un client bute sur un doublon de vide. `sync_sequence` part de `0`, la valeur qu'un
jeton de synchro ne réclame jamais (`> n` avec `n ≥ 0`) : une fiche non rattrapée est donc invisible
du protocole plutôt que servie sous un nom absent. **Les deux colonnes sont un prérequis dur, pas une
amélioration** : tant que le rattrapage n'est pas passé, un carnet DAV ouvert est un carnet
incomplet, et c'est pourquoi l'ordre du § « Prérequis d'infrastructure » n'est pas négociable.

**`contact_revisions` porte une clé technique, à l'inverse de ses voisines.** Les tombes sont un
état — une par nom, la plus récente écrase la précédente — tandis que les révisions sont un journal :
plusieurs lignes coexistent pour un même `dav_name`, et rien ne les distingue qu'un ordre. Un
`AUTO_INCREMENT` le donne ; y mettre `(user_id, dav_name, replaced_at)` ferait de deux écritures
dans la même seconde une collision, sur la table dont le rôle est précisément de ne rien perdre.
`dav_name` y est nullable pour la même raison qu'ailleurs : une fiche jamais rattrapée n'en a pas,
et sa carte mérite d'être archivée quand même.

**Mais alors `dav_name` ne peut pas être la seule poignée, et c'est pour cela que `uid` et
`contact_id` sont là.** Une table dont le rôle est de ne rien perdre serait mal conçue si la ligne
qu'elle vient d'écrire ne se retrouvait par aucune clé : `dav_name` étant nullable, une révision de
fiche non rattrapée ne s'indexerait que sur `(user_id, replaced_at)`, c'est-à-dire qu'on la chercherait
à l'heure. `uid` est `NOT NULL` parce que la décision 5 en fait l'arbitre de l'identité d'une carte —
le nom n'est qu'une URL, et 4a garantit qu'aucune carte stockée n'est sans `UID`. `contact_id` reste
nullable : il désigne la fiche quand elle existe encore, et une révision de cause `delete` survit à la
sienne.

**`contact_revisions.vcard_raw` est un `MEDIUMTEXT`, comme celui de `contacts`, et ce n'est pas
neutre** — c'est ce type qui impose le refus des corps non-UTF-8 de la décision 9. Les deux colonnes
sont identiques pour que la donnée ne traverse aucune conversion entre elles : une carte lue dans
une révision doit pouvoir être renvoyée par un `PUT` telle quelle, sinon l'historique ne restitue
pas ce qu'il a archivé.

**`contact_sync_state.epoch` est un GUID, et il ne bouge jamais dans l'exploitation normale.** Il est
tiré à la création de la ligne, entre dans le jeton (décision 7), et n'a qu'un seul autre moment de
vie : la restauration d'une sauvegarde, où un `UPDATE` le remplace et rend d'un coup étrangers tous les
jetons émis par la base d'avant. Il ne remplace pas `pruned_below`, qui répond à une autre question —
« ces tombes-là existent-elles encore ? » — et les deux se lisent dans la même transaction.

**`salt` en `VARBINARY(16)`** plutôt qu'en texte : 16 octets tirés au sort n'ont pas d'encodage, et
les stocker en base64 ferait dépendre le condensat d'un détail de sérialisation. `secret_hash` est le
SHA-256 hexadécimal de la concaténation `sel ‖ secret UTF-8`.

**`dav_credentials` a `user_id` pour clé primaire, et aucune colonne d'identité propre.** C'est la
forme qui dit qu'il y a un secret par utilisateur et pas deux (décision 1) : une clé technique et un
index sur `user_id` laisseraient la table accepter une deuxième ligne que rien dans le code ne
créerait — jusqu'au jour où une reprise l'y mettrait. L'absence de ligne signifie « jamais activé »,
et `carddav_enabled = 0` « éteint mais configuré » : deux états distincts, que la décision 2
distingue au bord.

**`carddav_enabled` par défaut à `1`.** La ligne n'existe que si l'utilisateur a allumé
l'interrupteur — le défaut décrit donc l'état dans lequel elle naît, et non une politique appliquée
à qui n'a rien demandé. Un compte sans ligne ne synchronise pas.

Les quatre nouvelles entités déclarent leur arête vers `WebmailUser` dans `OnModelCreating`, sans
propriété de navigation, comme les cinq tables existantes : sans arête déclarée, EF ordonne les
`INSERT` par nom de table et casse la clé étrangère.

Un rattrapage remplit `dav_name` (`CONCAT(id, '.vcf')`) et `sync_sequence` (`1` pour toutes les
fiches d'un même utilisateur : elles arrivent ensemble dans la première synchro, aucun client
n'existe encore pour distinguer leurs rangs) sur les fiches existantes, et crée une ligne
`contact_sync_state` par utilisateur avec `seq = 1` et `epoch = UUID()`. Il est livré en SQL à passer à la main, comme
`contacts-display-name-backfill.sql`, et il est **idempotent** : chaque instruction ne touche que les
lignes encore à `NULL` ou à `0`, de sorte qu'un opérateur qui le rejoue ne réattribue rien et ne
remet aucun compteur en arrière.

## Les erreurs

| Situation | Réponse |
|---|---|
| Pas d'identifiants, secret inconnu ou remplacé, synchronisation jamais activée, compte inutilisable | `401` + `WWW-Authenticate: Basic realm="weesky CardDAV"` (et rien d'autre) |
| Secret **valide**, mais `carddav_enabled = 0` | `403` — jamais avant la comparaison du condensat |
| Requête `/dav` dont l'origine n'est pas `https` (hors développement) | `403`, sans lecture de la table |
| Seuil d'échecs d'authentification atteint (par IP ou par identifiant) | `401`, sans lecture de la table |
| `{userId}` n'est pas l'utilisateur authentifié | `404` |
| `Depth: infinity` — ou absent — sur `PROPFIND` | `403 propfind-finite-depth` |
| `UID` déjà porté par une autre ressource, ou changé par le `PUT` d'une ressource existante | `403 no-uid-conflict` + le `DAV:href` du conflit |
| Carte au-delà de 1 Mo | `403 max-resource-size` |
| Carnet plein (5000 fiches) sur un `PUT` créateur | `507` |
| Corps vCard illisible, portant plus d'une carte, ou qui n'est pas de l'UTF-8 strict | `403 valid-address-data` |
| Version vCard hors de `supported-address-data` (`2.1`) sur un `PUT`, ou `version`/`content-type` non annoncé demandé dans un `address-data` | `403 supported-address-data` |
| Filtre `addressbook-query` non évaluable | `403 supported-filter` |
| `addressbook-query` sans élément `filter` (RFC 6352 § 10.3 le rend obligatoire) | `400` |
| Collation inconnue dans un `text-match` | `403 supported-collation` |
| `REPORT` dont le corps nomme un rapport inconnu | `403 supported-report` |
| Jeton de synchronisation périmé, inconnu, mal formé, d'une autre epoch, ou postérieur à la séquence courante | `403 valid-sync-token` |
| `DAV:sync-level` de valeur inconnue, ou absent **et** sans aucun en-tête `Depth` (annexe A) | `400` |
| Borne atteinte dans un rapport — `DAV:limit` pour `sync-collection`, `CARDDAV:limit` pour `addressbook-query` | `507 number-of-matches-within-limits` (jeton du dernier rang complet pour `sync-collection`) |
| Plus de 5000 `DAV:href` dans un `multiget` | `207` portant `507 number-of-matches-within-limits` sur la Request-URI |
| Corps de rapport au-delà de 1 Mo | `413` (`RequestSizeLimit`) |
| `href` inconnu ou hors collection dans un `multiget` | `404` **dans** le `multistatus` |
| Propriété demandée que la ressource ne porte pas | `404` dans un second `propstat` |
| `If-Match` en désaccord ou sur ressource absente, `If-None-Match: *` sur ressource existante | `412` — et le corps d'un `PUT` ainsi refusé est archivé en `rejected` (décision 17) |
| Violation d'unicité de `dav_name` entre deux `PUT` créateurs simultanés | rejoué en remplacement ; `412` si la requête portait `If-None-Match: *` |
| Entité externe ou DTD dans un corps XML, ou imbrication au-delà de cinquante niveaux | `400` |
| `PROPPATCH` sur le carnet | `207`, chaque propriété demandée en `403 Forbidden` |
| Méthode non servie (`MKCOL`, `MKCALENDAR`, `COPY`, `MOVE`, `ACL`, `LOCK`, `UNLOCK`, `GET`/`PUT`/`DELETE` de collection) | `405` + `Allow` |
| URL de collection sans barre oblique finale | `301` vers la forme avec barre |
| Attente de verrou dépassée, ou interblocage arbitré par InnoDB | `503` + `Retry-After` |
| Ressource inconnue | `404` |

Chaque `403` porte le corps `DAV:error` nommant sa condition — c'est ce que le client lit pour
choisir son repli, un `403` nu ne lui laissant que l'abandon.

Aucune de ces réponses n'est un `500`. Le point vaut d'être écrit : les refus du store
(`CapReached`, `CardTooLarge`, violation de l'index `(user_id, uid)`) sont des `Result.Failure` ou
des exceptions de base rédigées pour l'UI du webmail ; laissées telles quelles elles remontent en
`500`, et un `500` est ce qu'un client DAV retente indéfiniment, sur la même carte, à chaque cycle
de synchronisation. Toute erreur attendue est traduite au bord. Le `503` de l'attente de verrou est
le seul cas où retenter est **la** bonne conduite, et c'est justement pour cela qu'il porte un code
qui le dit et un `Retry-After` qui le date.

Une seule réponse de ce document ne vit pas sur `/dav` : `PUT /api/contacts/{id}` répond **`409`**
quand le `card_hash` que l'éditeur a lu n'est plus celui de la fiche (décision 17). Elle est ici
parce qu'elle protège le même invariant, depuis l'autre porte.

## Ce que 4c ferme des résidus de 4a

`docs/superpowers/contacts-4a-residuals.md` § « À traiter en 4c » énumère cinq points. Quatre entrent
dans cette tranche :

- **`UID:urn:uuid:X` ressort en `UID;VALUE=TEXT:…` en 4.0** — cosmétique jusqu'ici, servi à de vrais
  clients désormais.
- **`VCardProjector.RawCard` ne s'arrête pas au premier `END:VCARD`** — le résidu annonçait
  exactement ce moment : le `PUT` devient un second producteur de `vcard_raw`, et l'entrée cesse
  d'être garantie par le découpeur.
- **`If-None-Match: *`, ETags faibles et valeurs multiples non honorés** sur `GetPhoto` — 4c porte la
  vraie sémantique d'ETag, la route d'avatar s'aligne dessus.
- **Le test du corpus compose avec l'`Uid` du projecteur** plutôt que celui de la production.

Le cinquième — le repli du nom d'affichage à l'export ignore l'ordre `PREF` — reste au backlog : il
concerne l'export CSV, que le protocole ne traverse pas.

Le même document porte un **second** tableau, « Reporté de 4b vers 4c / backlog », que la première
rédaction de cette spec avait laissé de côté. Deux de ses cinq points sont des pertes de données que
le protocole rend routinières, et ils entrent donc ici :

- **`VCardComposer.SpliceFamily` perd les paramètres `X-` quand une famille retombe à une seule
  occurrence** (garde `model.Count < 2`). Les résidus le désignent comme « celui qui compte » :
  4b en a fait un cas atteignable depuis l'éditeur, 4c le rend atteignable depuis n'importe quel
  téléphone, et ce qui disparaît — un `X-ABLabel` Apple, par exemple — ne se retrouve nulle part.
- **`VCardComposer.Fold` compte des unités UTF-16 et peut couper une paire de substitution.** Jusqu'à
  cette tranche, la carte pliée n'allait qu'en base ; désormais elle est servie à des clients tiers,
  et une carte coupée au milieu d'un caractère est une carte invalide livrée par le protocole. Un
  émoji dans une `NOTE` suffit — et l'éditeur de 4b écrit du texte libre.

Les trois autres restent au backlog, et pour des raisons qui tiennent : `URL;TYPE=PREF` perdu sur un
aller-retour 3.0 est une propriété d'affichage préexistante, la troncature des scalaires est bornée
par les largeurs de colonnes, et le `?` d'une composante `N` à plusieurs valeurs n'est émis par aucun
client connu. Ils sont nommés ici pour qu'aucune revue de 4c n'ait à les redécouvrir.

## Fichiers

**4c-i, backend**

- `Data/Preferences/DavCredential.cs`, et son arête dans `PreferencesDbContext`
- `Repositories/IDavCredentialStore.cs`, `DavCredentialStore.cs` — lecture par `user_id`, création,
  régénération, bascule de `carddav_enabled`
- `Services/DavSecret.cs` — engendrement base32, sel, condensat, comparaison en temps constant
- `Authentication/CardDav/CardDavAuthenticationHandler.cs`, ses options et sa constante de schéma —
  identifiant = adresse, contrôle de `X-Forwarded-Proto`, condensat comparé **avant** la lecture de
  `carddav_enabled`, `IAccountInfoProvider.IsUsableAsync`, cache de 60 s, défi Basic seul, délai
  aléatoire asynchrone après échec
- `Authentication/CardDav/AuthAttemptThrottle.cs` — compteur d'échecs glissant, par IP et par
  identifiant, en mémoire
- `Controllers/DavCredentialsController.cs` — l'état de l'écran (adresse, identifiant,
  `carddav_enabled`, `last_used_at`), l'activation qui engendre et rend le secret une fois, la
  bascule, la régénération
- `Configuration` — l'adresse publique du serveur DAV, rendue au front, jamais composée par lui
- `Authentication/Extensions/AuthorizationExtension.cs` — `capabilities.dav`
- `Repositories/WebmailUserStore.RotateSecurityStampAsync` — supprime la ligne de l'utilisateur
  dans la même transaction que la rotation

**4c-i, frontend**

- `modules/settings/sync/` — l'onglet de la décision 19 : l'interrupteur, les trois valeurs à
  copier, le secret montré une fois, `Regenerate` et sa confirmation, `Last used`
- `SettingsLayout.tsx` — l'entrée de nav, gatée `isPrimary` et `capabilities.dav !== false`, et sa
  route
- l'avertissement sur l'écran de changement de mot de passe et le bouton de déconnexion globale :
  ces gestes détruisent le secret de synchronisation (décision 2)
- les libellés (l'UI reste en anglais)

**4c-ii**

- `Data/Preferences/ContactTombstone.cs`, `ContactSyncState.cs`, `ContactRevision.cs`, et leurs
  arêtes
- `Repositories/IContactSyncStore.cs` et son implémentation — avance de séquence sous verrou, ligne
  d'état créée à la demande avec son `epoch`, pose et levée de tombe, élagage et `pruned_below` dans
  une seule transaction, archivage d'une révision (y compris la cause `rejected`, avec sa fenêtre de
  dédoublonnage de vingt-quatre heures)
- `Repositories/ContactStore.cs` — transactions explicites via l'`IExecutionStrategy`, verrou de la
  ligne d'état pris en premier, lots bornés à cent fiches par transaction ; avance la séquence sur
  toute écriture de carte et pose `dav_name`
  s'il manque, **archive** ce qu'elle remplace ou efface, **pose une tombe** dans `DeleteAsync` et
  `DeleteManyAsync` (aucune si `dav_name` est `NULL`) ; `UpdateAsync` refuse sur `card_hash`
  périmé ; traduit ses plafonds et ses attentes de verrou pour le bord DAV
- `Services/CardDav/SyncStateConsistencyCheck.cs` — le contrôle de démarrage de la note 3 des
  prérequis : `MAX(contacts.sync_sequence) > contact_sync_state.seq` journalisé en erreur
- `Controllers/ContactsController.Update` — le `409` de la décision 17, et le `card_hash` rendu par
  `Get` pour que l'éditeur puisse le renvoyer
- `modules/contacts/` — l'éditeur renvoie le `card_hash` lu et propose de recharger sur `409`
- `Services/CardDav/DavPaths.cs` — construction et analyse des chemins, encodage et décodage des
  segments, validation de `dav_name`
- `Services/CardDav/DavXml.cs` — noms d'éléments et espaces de noms
- `Services/CardDav/MultiStatusWriter.cs` — écriture directe dans `Response.Body`
- `Services/CardDav/DavProperties.cs` — le jeu de la décision 13, `allprop`, `propname`, et le
  `propstat` à `404` d'une propriété absente
- `Services/CardDav/PropfindRequest.cs`, `ReportRequest.cs`, `ProppatchRequest.cs` — analyse, sans
  DTD, profondeur bornée, corps vide compris
- `Services/CardDav/AddressBookFilter.cs` — pré-filtre sur les colonnes indexées, évaluation exacte
  sur la carte parsée, ou refus
- `Services/CardDav/AddressDataFilter.cs` — restriction de la carte servie aux propriétés demandées
- `Services/CardDav/DavRequestLog.cs` — la ligne structurée de la décision 18
- `Controllers/CardDavController.cs`, `WellKnownController.cs`
- `Services/ContactTombstoneSweeper.cs` — tombes à 180 jours, révisions à 30

## Tests

- **Le secret** : engendrement distinct à chaque appel, condensat non réversible, comparaison en
  temps constant, `last_used_at` amorti ; une régénération remplace la ligne sans en créer une
  seconde, et vide le cache ; un secret présenté avec un blanc de bord est accepté.
- **L'écran** : allumer engendre et rend le secret **dans la même réponse** ; le rallumer n'en rend
  aucun ; l'état lu ne porte jamais de secret, sous aucune forme — c'est l'assertion qui garde
  fermée la porte qu'un « révéler » ouvrirait ; éteindre conserve `secret_hash` ; une rotation de
  `security_stamp` supprime la ligne, donc l'écran repasse à éteint ; l'adresse rendue est celle de
  la configuration, pas celle de l'hôte appelé ; l'onglet est absent sur un compte non primaire et
  sur `capabilities.dav === false`.
- **Le schéma d'authentification** : `401` avec l'en-tête de défi **et sans défi `Bearer`** ; secret
  remplacé par une régénération refusé ; secret d'un autre utilisateur refusé ; compte devenu
  inutilisable refusé ; rotation du `security_stamp` révoquant le secret ; **`carddav_enabled = 0`
  répondant `403` sur un secret valide et `401` sur un secret faux** — la paire, parce que c'est
  elle qui atteste l'ordre de la décision 2 et referme l'oracle d'énumération ; JWT accepté ; secret
  refusé sur `/api` ;
  un échec retardé d'un délai aléatoire avant le `401` ; une requête dont `X-Forwarded-Proto` n'est
  pas `https` refusée **sans lecture de la table** ; le seuil d'échecs refusant sans lecture non
  plus — les deux s'assertent sur le dépôt, pas sur le code de retour, qui est le même.
- **Les documents XML** : assertions sur les corps de réponse, adossées aux exemples **littéraux**
  des RFC 6352 et 6578 plutôt qu'à des corps inventés — un corps inventé prouve que le code fait ce
  que le code fait.
- **L'invariant de séquence** : une édition qui change la carte l'avance ; un basculement d'étoile
  ne l'avance pas.
- **Les suppressions** : une suppression depuis le webmail pose une tombe et avance la séquence ;
  une suppression en lot en pose une par fiche sous un seul rang ; le `sync-collection` suivant les
  rend toutes. C'est le test qui garde le trou fermé, et il vaut par la porte webmail autant que par
  la porte DAV. Une fiche sans `dav_name` se supprime sans tombe et sans erreur ; une écriture sur
  une fiche sans nom le pose.
- **La concurrence** : deux écritures simultanées ne rendent jamais deux fiches au même rang, et une
  synchro qui s'intercale entre les deux ne perd ni l'une ni l'autre — la seconde est rendue au tour
  suivant. Un import et un `PUT` DAV lancés ensemble aboutissent tous les deux, sans interblocage —
  c'est le test de l'ordre de verrou, et le seul qui l'atteste. Un import de cinq cents fiches prend
  **plusieurs** rangs et non un seul, et un `sync-collection` qui s'intercale en rend une partie sans
  jamais couper un rang en deux. Deux `PUT` créateurs simultanés sur
  le même nom aboutissent l'un en création, l'autre en remplacement — ou en `412` sous
  `If-None-Match: *` — jamais en `500`. Une attente de verrou dépassée sort
  en `503` avec `Retry-After`, jamais en `500`. À écrire contre une vraie base, pas contre le
  fournisseur InMemory, qui ne modélise ni verrou ni transaction.
- **L'invisibilité d'une fiche incomplète** : les quatre chemins — `PROPFIND Depth: 1`,
  `addressbook-query`, `addressbook-multiget`, `sync-collection` — écartent une fiche sans
  `dav_name`, **et** une fiche à `vcard_raw` nul, **et** une fiche à `card_hash` vide. Un seul test
  par chemin, sur une fixture portant les trois, parce que c'est un invariant et non quatre règles —
  mais la fixture porte les trois, sans quoi le test ne couvre que la condition qu'on avait en tête.
- **La synchro** : initiale sans jeton ; incrémentale rendant créations, modifications et tombes ;
  jeton périmé, mal formé ou postérieur à la séquence répondant `403 valid-sync-token` ;
  `sync-level` de valeur inconnue répondant `400` ; `sync-level` absent mais `Depth: 1` présent
  servi comme un `sync-level` de `1` (annexe A), et absent sans `Depth` répondant `400` — le
  triplet, parce que c'est lui qui atteste le repli ; réponse tronquée par `DAV:limit` rendant `507` et un jeton
  qui, rejoué, rend exactement le reste — un lot partagé par plusieurs fiches n'étant jamais coupé
  en deux ; les propriétés demandées dans le `DAV:prop` — `resourcetype` compris — rendues, pas un
  `getetag` figé ; un `<DAV:sync-token/>` **vide** traité comme un jeton absent, pas comme un jeton
  malformé ; un jeton dont l'**epoch** n'est pas celle du carnet refusé `403 valid-sync-token`, et un
  `UPDATE … SET epoch = UUID()` rendant d'un coup invalide un jeton qui l'instant d'avant était servi
  — c'est le test de la parade de restauration, et le seul qui atteste qu'elle marche ;
  `Depth: 0` **sans** `sync-level` servi comme un `sync-level` de `1`, parce que c'est le client
  conforme sur l'en-tête et oublieux de l'élément, et qu'il ne doit pas être celui qu'on refuse.
- **Le ctag et sa liste** : un `PROPFIND Depth: 1` demandant `getctag` **et** les membres ne rend
  jamais un ctag couvrant une fiche que sa propre liste omet. Le test écrit une fiche entre les deux
  lectures et vérifie que le ctag rendu est celui d'avant — c'est l'assertion qui atteste l'ordre,
  et sans elle l'ordre se perd au premier remaniement.
- **L'élagage** : le balayage remonte `pruned_below`, un jeton antérieur est refusé, un jeton
  postérieur reste servi exactement ; à la fin du balayage, `pruned_below` est **toujours** ≥ la plus
  haute séquence dont la tombe a disparu — l'invariant que l'ordre et la transaction protègent, et
  celui qu'il faut asserter puisqu'on ne peut pas tuer le processus au milieu.
- **Le `PUT`** : carte posée verbatim, `is_favorite` et `source` préservés, conflit d'`UID` portant
  le `href` du conflit, `If-Match`, `If-None-Match: *`, absence d'ETag quand la carte a été
  transformée, tombe levée, corps à plusieurs cartes refusé, carnet plein répondant `507`, fins de
  ligne rendues telles qu'elles ont été reçues, `UID` changé sur une ressource existante refusé,
  `VERSION:2.1` refusé avec `supported-address-data`, un corps non-UTF-8 refusé avec
  `valid-address-data`, un `Content-Type: text/x-vcard` — et un `Content-Type` absent — acceptés,
  un `If-Match` à deux valeurs dont l'une correspond accepté, un `If-Match: W/"…"` refusé.
- **L'historique** : un `PUT` qui remplace une carte archive les octets remplacés ; un écrasement sans
  `If-Match` archive lui aussi ; une suppression pose sa tombe
  **et** archive la carte, la tombe restant nue ; la suppression d'un utilisateur n'archive rien.
  Et par la porte webmail : un `PUT /api/contacts/{id}` portant un `card_hash` périmé répond `409`
  et ne touche à rien.
- **Les corps refusés** : un `PUT` en `412` archive **le corps reçu** sous la cause `rejected` et ne
  touche pas à la fiche — c'est le test de la ligne du tableau de la décision 17 qui perdait la saisie
  du train, et il faut l'asserter sur le contenu archivé, pas sur le nombre de lignes ; le même `PUT`
  rejoué dans l'heure n'en archive pas une seconde ; rejoué après vingt-quatre heures, si ; un
  `DELETE` en `412` n'archive rien et ne pose aucune tombe ; une révision `rejected` porte l'`uid` de
  la carte refusée, y compris quand la fiche visée n'a pas de `dav_name`.
- **Les chemins** : `dav_name` validé (255, vide, `/`, `\`, `.`, `..`, caractères de contrôle,
  espaces de bord), un `%2F` refusé après décodage et non avant, un nom à espace ou à `#` produisant
  un `href` que la même analyse relit, un nom **sans** suffixe `.vcf` servi comme les autres.
- **Le filtre** : chaque ligne du tableau de la décision 11 évaluée — `allof`/`anyof` aux deux
  niveaux, `is-not-defined`, `param-filter`, les quatre `match-type`, `negate-condition` — dont au
  moins une propriété **hors** des colonnes projetées (`TITLE`), qui atteste que l'évaluation porte
  sur la carte ; le reste refusé, jamais silencieusement ignoré ; collation inconnue répondant `403
  supported-collation` ; `address-data` partiel rendant les seules propriétés demandées **et son
  `getetag`** — l'assertion sur la présence de l'ETag, parce que c'est celle qui garde fermée la
  régression que la première rédaction avait écrite ; un `<filter/>` **vide** rendant tout le carnet
  et un corps **sans** `filter` répondant `400` — la paire, parce que la ressemblance des deux cas est
  tout le piège ; une borne `CARDDAV:limit` honorée là où un `DAV:limit` de même nom local est
  ignoré ; un `address-data` demandant `version="2.1"` refusé en `403 supported-address-data`, et un
  `version="4.0"` sur une carte 3.0 servi tel quel.
- **La découverte** : `PROPFIND` sur `/.well-known/carddav` redirigé comme un `GET` et sans
  authentification ; `PROPFIND /` défiant `Basic` seul, comme `/dav` ; `PROPFIND` sans `Depth`
  répondant `403 propfind-finite-depth` **et un `REPORT` sans `Depth` répondant normalement** — la
  paire, sans quoi la première règle déborde sur la seconde ; en-tête `DAV:` portant
  `access-control` ; les propriétés de principal du RFC 3744 servies, `alternate-URI-set` et
  `group-membership` vides compris ; `supported-report-set` servi sur le carnet avec ses trois
  rapports **et** sur le principal, vide ; une URL de collection sans barre finale redirigée en
  `301` ;
  `HEAD` servi ; `Allow` littéral, sur une carte comme sur la collection, et cohérent avec les
  `405` — donc portant `PROPPATCH` sur la collection.
- **`PROPPATCH`** : répond `207` et non `405`, chaque propriété demandée ressortant en `403
  Forbidden`, `{calendarserver}me-card` compris ; rien n'est écrit en base. C'est le test qui garde le
  `DAV: 1` honnête.
- **XXE et profondeur** : un corps de `REPORT` déclarant une entité externe est refusé ; un corps
  imbriqué au-delà de la borne est refusé par un `400`, et non par une exception qui traverse.

## Prérequis d'infrastructure

Trois notes, sur le modèle de `reverse-proxy-prerequisite.md` :

1. **Le DDL**, à passer à la main sur dev puis prod, rattrapage compris, et **dans cet ordre** :
   les tables et colonnes d'abord, le backend ensuite, le rattrapage immédiatement après. L'ordre
   n'est pas une commodité d'exploitation : entre le déploiement et le rattrapage, les fiches
   existantes n'ont ni `dav_name` ni rang, donc un client qui se connecterait dans cette fenêtre
   verrait un carnet vide et **effacerait ses propres copies** en les croyant supprimées côté
   serveur. 4c-ii ne s'ouvre donc aux clients qu'une fois le rattrapage confirmé à zéro ligne
   restante — c'est le contrôle à écrire dans la note, à côté de la requête qui le vérifie.
2. **Le proxy inverse** : vérifier qu'il laisse passer `PROPFIND`, `PROPPATCH`, `REPORT`, `OPTIONS`,
   `HEAD`, `PUT` et `DELETE`, qu'il ne
   retire ni `Depth`, ni `If-Match`, ni `If-None-Match`, ni `Authorization` — certaines
   configurations avalent l'en-tête `Authorization` sur les routes qu'elles croient publiques — et
   qu'il n'impose pas de plafond de corps inférieur au nôtre. Un `limit_except` ou un pare-feu
   applicatif les refuse silencieusement, et le symptôme côté client est un carnet vide, sans erreur
   — c'est-à-dire le symptôme qui coûte le plus cher à diagnostiquer.

   **Et vérifier qu'il ne répond pas lui-même sur `/.well-known/`.** C'est le mode de panne le plus
   courant des CDN et des pare-feux applicatifs devant un serveur DAV : le chemin est intercepté au
   bord — pour un certificat, pour une redirection maison —, le `301` de la décision de découverte
   n'atteint jamais le client, et l'appairage échoue sur un `404` avant la première requête
   authentifiée. Le contrôle tient en un `curl -X PROPFIND` depuis l'extérieur.
3. **La restauration d'une sauvegarde** rembobine la séquence, et le refus du jeton postérieur à la
   séquence courante (décision 7) n'attrape que les clients les plus en avance : un jeton resté
   sous la séquence restaurée passe, et couvre des rangs dont le contenu a changé — divergence
   silencieuse et permanente. Le remède tient en une ligne : `UPDATE contact_sync_state SET
   epoch = UUID()` — tous les jetons émis par la base d'avant deviennent étrangers au carnet, tous
   les clients repartent d'une synchro complète. **Elle est livrée comme un fichier `.sql`
   versionné**, à côté
   du DDL et du rattrapage, et non comme une phrase dans un paragraphe : une consigne qu'il faut
   retrouver dans un document de conception au moment d'une restauration est une consigne qui ne
   sera pas jouée.

   **L'epoch plutôt que `pruned_below = seq`, et la différence n'est pas cosmétique.** Les deux
   invalident les jetons ; le second le fait en déplaçant un filigrane dont le sens est « ces tombes-là
   n'existent plus », c'est-à-dire en mentant sur autre chose pour obtenir l'effet voulu, et il faut
   l'avoir compris pour le jouer juste. L'epoch ne dit qu'une chose et la dit entièrement : ce carnet
   n'est plus celui qui a émis vos jetons. C'est un `UPDATE` d'une colonne, sans borne à calculer, et
   qui reste juste s'il est joué deux fois.

   **Et un contrôle qui ne demande à personne de se souvenir.** Une restauration n'annonce pas qu'elle
   a eu lieu ; la parade ci-dessus suppose qu'un humain sache la jouer au bon moment. Le démarrage du
   service compare donc, par utilisateur, `MAX(contacts.sync_sequence)` à `contact_sync_state.seq` : le
   premier ne peut pas dépasser le second sans qu'une base ait été rembobinée sous le service. La
   divergence est journalisée en erreur, nommément, avec la ligne `.sql` à jouer. C'est le seul endroit
   de cette tranche où un incident n'a aucun symptôme côté client — les téléphones continuent de
   synchroniser, sur un carnet qui a changé sous eux — et c'est pour cela qu'il faut le détecter au
   démarrage plutôt que l'attendre en support.

   Le compromis mérite d'être nommé, parce qu'il est le seul endroit où ce serveur demande un geste
   humain là où Radicale n'en demande aucun. Le ctag de Radicale est dérivé du contenu de la
   collection : une restauration le change toute seule, et les clients resynchronisent sans que
   personne n'intervienne — auto-réparant, au prix d'un recalcul à chaque interrogation d'état. Le
   nôtre est un compteur, donc `O(1)` sur le chemin qu'un téléphone emprunte toutes les quinze
   minutes, mais rembobinable. C'est le bon échange pour 5000 fiches ; il ne l'est qu'à la condition
   que la ligne ci-dessus existe et soit jouée.

## Ce que la tranche ne fait pas

- **Aucune conformité client prouvée.** C'est 4d, et l'ordre est délibéré : un défaut trouvé par
  `ccs-caldavtester` sur un serveur qui suit le RFC est un défaut du serveur ; trouvé sur un serveur
  écrit contre un client, il est indiscernable d'une divergence de ce client.
- **Pas de CalDAV.** Le calendrier n'existe pas dans le produit. L'onglet « Sync » et la table
  `dav_credentials` sont nommés et découpés pour l'accueillir sans être refaits (décision 19) : le
  jour venu, c'est un second interrupteur, une seconde colonne et un second `home-set` sous le même
  principal — jamais un second secret à recopier.
- **Pas de gestion par appareil.** Un secret par utilisateur, régénérable ; perdre un téléphone
  oblige à reconfigurer les autres. C'est l'échange assumé de la décision 1, et il se rouvrira le
  jour où quelqu'un comptera ses appareils sur ses deux mains.
- **Pas de plusieurs carnets, pas de partage, pas de `MKCOL`.** Le carnet est créé avec
  l'utilisateur ; `MKCOL` répond `405`.
- **Pas de traitement des groupes de contacts.** iOS les écrit comme des cartes à part
  (`X-ADDRESSBOOKSERVER-KIND:group`, membres par `X-ADDRESSBOOKSERVER-MEMBER`) ; DAVx⁵ connaît ce
  mode-là et celui des `CATEGORIES`, au choix du compte. Le stockage verbatim les traverse sans
  perte — rien à faire côté protocole —, mais le webmail affichera ces cartes de groupe comme des
  fiches quasi vides portant le nom du groupe. C'est écrit ici pour que 4d ne prenne pas ces
  fiches-là pour un bug ; ce qu'on en fait à l'écran — les filtrer, les rendre, les ignorer — se
  décidera devant un client réel.
- **Pas de propriété mutable.** Rien de tel ne se présente : le nom du carnet est fixe. `PROPPATCH`
  est néanmoins **servi**, d'un `207` refusant chaque propriété en `403` — parce que l'en-tête
  `DAV: 1` l'engage et parce que les clients d'Apple y écrivent leur `me-card` (décision 16). Servi
  ne veut pas dire stocké : rien n'entre en base.
- **Pas de modèle d'ACL.** `current-user-privilege-set` rend un jeu constant et `access-control` est
  annoncé parce que le RFC 6352 l'exige et que les clients le lisent (décision 13) ; la méthode `ACL`
  et `acl-principal-prop-set` répondent `405`. Un carnet à un seul propriétaire n'a pas de politique
  à exprimer.
- **Pas de conversion de version vCard.** `supported-address-data` annonce 3.0 et 4.0 parce que le
  carnet contient les deux ; un client qui demande une version qu'une carte n'a pas reçoit la carte
  telle qu'elle est stockée. Convertir, ce serait réécrire — ce que 4a interdit hors modification.
- **Pas d'écran de restauration d'une révision.** La donnée est écrite (décision 17) parce qu'elle
  ne se retrouve pas après coup ; l'interface, elle, s'ajoute quand on le veut. Un premier cas réel
  dira si le geste est « rendre cette version » ou « comparer les deux », et construire avant de le
  savoir donnerait un écran qu'il faudrait refaire. En attendant, la reprise se fait par requête.
- **Pas de fusion automatique de deux versions d'une carte.** Deux cartes divergentes ne fusionnent
  pas sans un humain, et un serveur qui choisirait à sa place se tromperait la moitié du temps. Il
  détecte, il archive, il rend la main.
- **Pas de découverte par SRV DNS ni depuis `mail.weesky.net`.** L'adresse à saisir est celle de
  l'API ; les deux autres chemins demandent de la configuration hors dépôt et pourront s'ajouter en
  4d si un client les réclame.

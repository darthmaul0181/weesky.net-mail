# Ce que la tranche 4c-ii-c a décidé, et pourquoi

Les commits de cette tranche ont été écrasés en un seul avant d'être poussés. Ce fichier garde ce
qui vivait dans leurs messages : les décisions qui ont changé la conception, les défauts trouvés en
chemin, et ce qui reste ouvert. Il n'est pas un journal — il ne retient que ce qu'un lecteur devra
savoir pour ne pas défaire un choix sans en connaître le prix.

Chaque entrée dit **ce qui a été décidé**, **pourquoi**, et **ce qu'il en coûte si la décision est
renversée**.

## Les décisions qui ont changé la conception

### Le filigrane refuse `n < pruned_below`, jamais `n <= `

Le plan écrivait `<=`, en le justifiant ainsi : « un rang de plus resynchronisé de zéro ne coûte
rien, et une comparaison conservatrice reste juste si l'élagage change un jour de borne. »

C'est faux, et le coût n'est pas celui qu'annonçait ce commentaire. `ContactSyncStore.PruneAsync`
pose `PrunedBelow = Math.Max(PrunedBelow, plus grand rang supprimé)` : `P` est donc le rang d'une
tombe **disparue**, et toutes celles au-dessus survivent. Un client au jeton `P` dit « je suis à
jour jusqu'à `P` » et ne demande que les rangs **strictement supérieurs**, tous conservés —
l'accepter n'omet rien. C'est `n = P - 1` qui a besoin de la tombe ayant défini `P`, et qui doit
rester refusé.

Avec `<=`, un carnet dont le dernier événement est une suppression élaguée (`Seq == PrunedBelow`)
voit le serveur **refuser un jeton qu'il émet lui-même** : chaque synchronisation redevient
complète, jusqu'à la première écriture.

**Si l'on revient à `<=`** : resynchronisation complète perpétuelle sur tout carnet resté silencieux
plus de 180 jours. Pour que `<=` redevienne juste, il faudrait que `PruneAsync` fasse de
`pruned_below` un « tout ce qui est strictement en dessous a disparu » plutôt que « ce rang-là a été
élagué ». Le `COMMENT` de la colonne, dans `webmail-carddav-tables.md`, portait la même erreur et a
été corrigé.

Corollaire : la sentinelle `PrunedBelow == 0` devient inutile — sur `ulong`, `n < 0` est impossible.

### Tout jeton que ce serveur émet, il l'accepte en retour

Posé comme **invariant** plutôt que comme correctif ponctuel, après qu'une synchronisation initiale
tronquée sur un carnet élagué eut rendu un jeton refusé au tour suivant — le RFC 6578 § 3.2 renvoyant
alors le client vers une synchronisation initiale tronquée au même rang, indéfiniment.

Il y a **quatre émetteurs** : le ctag, le `sync-token` d'un `PROPFIND`, le rapport entier et le
rapport tronqué. Une propriété de test repasse chaque jeton émis à `DavSyncToken.Read` contre le même
état. Deux échappées sont voulues et documentées — un jeton relu contre un état qui a *changé*
(élagage, rotation d'epoch), et le carnet vierge sans ligne d'état : ce sont des cycles convergents,
pas des boucles.

**Si l'invariant saute** : la boucle revient, sous un déguisement que le test précédent ne voyait pas.
C'est déjà arrivé deux fois.

### La borne d'une lecture exige le snapshot qui a lu le compteur

Vrai pour `sync-collection` **et** pour `PROPFIND Depth: 1`, et pour deux raisons différentes.

Sur le rapport, la course est celle de l'élagage : `ReadStateAsync` le documente, et un élagage tombé
entre la lecture du compteur et celle des tombes servirait des suppressions sous un filigrane déjà
périmé.

Sur le `PROPFIND`, la raison est autre et moins visible : **une carte simplement modifiée prend un
rang neuf**. Une édition depuis le webmail entre la lecture de l'état et la requête de flux la fait
passer au-dessus de la borne — elle disparaît de la liste des membres pendant que le ctag couvre
encore son ancien rang, et **DAVx⁵ comme Thunderbird lisent cette absence comme une suppression et
retirent leur copie**, rétablie seulement au sondage suivant.

La prémisse du plan — « toute fiche satisfait déjà `sync_sequence <= seq` par construction » — n'est
vraie qu'**à l'intérieur d'un snapshot**. Dehors, la borne n'est pas gratuite.

**Donc : borne + snapshot, ou pas de borne du tout. Jamais la borne seule.**

**Non éprouvé :** l'InMemory ignore les transactions par configuration, donc la suite prouve que le
snapshot ne casse rien, pas qu'il isole. Le test constate son **ouverture**, pas son effet.

### La borne d'`addressbook-query` coupe après l'évaluation exacte

Compter avant de filtrer fait émettre les cartes correspondantes **plus un faux `507`** : un client
informé à tort que son résultat complet est tronqué requête indéfiniment.

### Le pré-filtre SQL est retiré ; `addressbook-query` lit tout le carnet

La légitimité entière d'un pré-filtre est qu'il **sur-sélectionne**. Trois cas de sous-sélection ont
été trouvés sur `display_name`, dont un seulement en relecture :

1. la colonne est **NULL** quand le FN égale le repli que le projecteur reconstruirait ;
2. elle est **tronquée à 255** et ne stocke qu'un préfixe ;
3. elle ne garde que le **premier** FN — et une carte 4.0 peut en porter plusieurs, légalement
   (cardinalité `1*`), stockée verbatim par l'import comme par `PUT`.

Les deux premiers se ferment par des bras `IS NULL` et `CHAR_LENGTH = 255`. Le troisième ne se ferme
pas : aucune colonne ne dit « cette carte a un second FN ».

Une formulation de repli a été cherchée avant de renoncer, et sa réfutation vaut d'être gardée :
`LOWER(vcard_raw) LIKE` **paraît** sûr et ne l'est pas — les lignes vCard sont **pliées** vers 75
octets, donc une aiguille peut être coupée par un `CRLF`+espace dans la colonne alors que la valeur
dépliée la contient ; et les valeurs sont **échappées**, si bien que `FN:Ada\, Jr` ne correspond
jamais textuellement à une recherche de `Ada, Jr`.

**Conséquence assumée :** chaque `addressbook-query` parse tout le carnet, borné par le seul plafond
de 5000 cartes. C'est le chemin le plus coûteux de `/dav`, et c'est celui qu'emprunte la recherche
d'adresse de DAVx⁵. `AddressBookFilter` et ses deux collations restent ; c'est le second livrable de
sa tâche qui est parti.

**Si quelqu'un réintroduit un pré-filtre** : il doit ne narrower que sur une colonne qui capture
**chaque instance et la valeur entière**. Aucune colonne projetée ne le fait aujourd'hui.

### Les préconditions se décident sous le verrou, pas avant

Deux pertes de mise à jour ont été fermées, la seconde trouvée seulement à la relecture finale.

`If-None-Match: *` : le rejeu de la porte **archivait le gagnant, le remplaçait par les octets du
perdant et prenait un rang**, et le contrôleur ne répondait `412` qu'ensuite. Le perdant s'entendait
dire « je n'ai rien fait » alors que sa carte était vivante ; le gagnant détenait un `201` pour des
octets qui n'étaient plus servis. Fermé par une intention **`createOnly`** portée jusqu'au verrou.

`If-Match` : même forme, pour le remplacement conditionnel **et** pour `DELETE`. Le contrôleur
évaluait contre une lecture préalable ; un `PUT` concurrent commis entre-temps faisait passer un
`If-Match` périmé, le perdant recevait `204` et écrasait la version du gagnant.

**L'en-tête descend BRUT jusqu'à la porte, pas un tag unique reconstitué** : la décision sous verrou
est un compare-and-swap sur le hash **observé**. Autrement, un `If-Match` listant aussi le tag du
gagnant archiverait la copie périmée et perdrait les octets du gagnant des révisions. Le filet
pré-verrou est conservé — il répond à la plupart des cas sans toucher le store — mais la décision qui
compte est celle prise sous le verrou.

### Une faute non transitoire dans l'archivage continue de traverser

Un `1205`/`1213` sur l'insertion de révision est attrapé et laisse le `412` debout. Une faute
**réelle** — une table de révisions absente, par exemple — traverse. Déguiser un store cassé en
« non archivé » l'enterrerait.

## Deux défauts de production trouvés en chemin

### Le `catch` sur les codes MySQL ne pouvait jamais se déclencher

`catch (MySqlException e) when (e.Number is 1205 or 1213)` : **EF enveloppe l'exception du
fournisseur dans `DbUpdateException`**, donc ce filtre ne voyait rien. Sur `PUT`, une attente de
verrou tombait dans le bras « violation d'index » et **était rejouée**, prenant un second rang ; sur
`DELETE`, elle n'était attrapée par rien et s'échappait en `500` — ce qu'un client DAV retente
indéfiniment, sur la même carte, à chaque cycle.

`IsTransient` parcourt désormais la chaîne interne, et le `catch` filtré précède les bras
`DbUpdate*`.

### `tokenIn` et `tokenOut` étaient morts

La ligne de journal les imprimait, sa documentation promettait de séparer « un jeton refusé en
boucle » des quatre autres causes d'un carnet vide, et l'unique appelant passait `null, null`.
Câblés depuis : `tokenIn` est extrait **avant** l'appel pour que le chemin du refus le porte aussi,
et `tokenOut` est la chaîne réellement écrite — la coupe sur une troncature, le compteur sinon.

`DavSyncToken.ForLog` retire notre préfixe, sans quoi la borne de 64 caractères tronquerait le rang,
c'est-à-dire le discriminant ; et il **blanchit les caractères de contrôle**, ce champ étant le seul
de la ligne à échoir un document client — un `\n` brut y serait une ligne de journal forgée.

## Une décision d'une tranche antérieure, corrigée

**Le ruling AM de la tranche 4c-ii-b reposait sur une prémisse fausse.** Il affirmait qu'ASP.NET
décode les valeurs de route, donc qu'un `%2F` arrive en `/` littéral. Mesuré : un catch-all
`{*davName}` le garde **encodé**, `..%2F..%2Fetc` arrive littéralement et est un nom valide, et un
`..` nu est replié par `System.Uri` **avant** le routage. Aucun `/` n'atteint `DavName` par
pourcent-encodage.

Le garde-fou reste — il attrape ce qui arrive vraiment : un segment littéral `a/b`, une traversée par
antislash, des caractères de contrôle, des espaces en bordure sous PAD-SPACE, la longueur. Mais la
raison écrite était fausse, et **la même phrase vivait dans trois commentaires** (`PUT`, `GET`,
`PROPFIND`), tous corrigés.

## Les deux coutures d'authentification

**L'asymétrie des clés est la conception, pas un détail.** `ForgetIdentifier` efface la clé
d'identifiant — légitime, l'appelant vient de prouver son identité par un JWT, facteur que le
limiteur ne protège pas — et **laisse la clé d'adresse**, sans quoi qui partage le /64 de la victime
se déverrouillerait en faisant régénérer un tiers.

**La couture n'aide pas dans le cas le plus courant, et il faut le savoir.** Derrière un NAT, la clé
d'adresse franchit aussi le seuil et survit : régénérer sans éteindre la synchro donne `429` jusqu'à
quinze minutes. Même sur des adresses distinctes, un appareil accumulant seul dix échecs bloque sa
propre clé d'adresse. **L'avertissement de l'onglet « Sync » — éteindre d'abord — reste le vrai
correctif et ne doit pas être retiré au motif que cette couture existe.**

**L'ordre des deux lignes de `Forget` porte, et aucun test ne peut le voir** : la génération bouge
**d'abord**, le retrait suit. Un `Store` en course lit alors la nouvelle génération et se retire, ou
écrit après le retrait et se retire par sa propre revérification. L'ordre inverse republierait un
secret révoqué pour la minute du cache. Un mutant qui permute les deux lignes survit à toute la
suite.

## Ce qui reste ouvert

- **La transaction n'est exercée nulle part** : l'InMemory l'ignore par configuration. Le vert prouve
  qu'elle ne casse rien, pas qu'elle isole.
- **Le compteur de carnet plein est lu hors transaction** : deux `PUT` créateurs simultanés au
  plafond le dépassent de un, sans index pour l'arrêter. Le passer sous le verrou coûterait un
  `COUNT` par `PUT`, verrou tenu.
- **Le second `FN` d'une carte 4.0 reste invisible** de l'interface et de la recherche. Il n'est pas
  pire que cela : le lecteur sert `VCardRaw` verbatim et l'ETag est le SHA-256 de ces octets exacts,
  donc un `GET` rend toujours ce qui fut stocké.
- **`LiftTombstoneAsync` n'est épinglé que par une vérification unitaire** : aucun test de bout en
  bout ne prouve qu'un `DELETE` → recréation → synchronisation ne sert pas la tombe *et* la carte.
- Les branches `405` de collection sont **mortes** (l'attrape-tout sans verbe répond avant) et
  gardées : les retirer exigerait un `davName!`, donc échanger du code mort mais sûr contre une
  `NullReferenceException` latente. Elles portent la mention `UNREACHABLE` et la raison.

## Deux pièges d'outillage, payés cher

**`dotnet test` en français écrit une espace insécable avant `échec :`.** Un prédicat de mutation qui
la cherche avec une espace ordinaire ne correspond jamais et rend **ROUGE sans condition**. Un agent
y a perdu vingt-sept verdicts : ses « mutations tuées » étaient la sortie d'un détecteur mort, et
deux d'entre elles contredisaient un constat de relecture correct. Il avait vérifié que chaque
mutation s'appliquait et se révertait ; jamais que son détecteur de vert savait reconnaître le vert.

**Donc : juger sur le code de sortie, et faire tourner une mutation témoin qui doit SURVIVRE avant
de croire une batterie.** Un témoin qui rend ROUGE prouve que le détecteur est cassé.

**C# 14 compile un argument optionnel omis dans un expression tree.** Ce qui était `CS0854` devient
un matcher Moq sur `null` constant — vert à la compilation, faux à l'exécution. Ajouter un paramètre
optionnel à une interface mockée ne casse plus la build : il faut relire tous les `Setup` à la main.

## Ce que la revue de branche a corrigé, après coup

Sept constats, relevés en relisant `master..cardav` d'un bloc plutôt que tranche par tranche. Aucun
n'était une régression : ce sont des choses qu'une revue par paquet ne pouvait pas voir, parce
qu'elles ne sont visibles qu'en comparant deux fichiers qu'aucune tâche ne touchait ensemble.

**Un rapport n'est servi que là où une propriété l'annonce, et l'inverse aussi.** L'invariant existait
dans un sens seul. `addressbook-query` et `sync-collection` étaient gardés par leur forme ; `multiget`
ne l'était pas, et répondait sur le principal et sur le home, où `supported-report-set` ne le nomme
pas. Symétriquement, la racine de service et le home servaient `expand-property` sans porter la
propriété du tout : leur `Allow` nommait REPORT et la réponse rendait un propstat 404 — le même
désaccord en-tête/réponse qui avait fait boucler DAVx⁵. Les deux sens sont refermés : les cinq
formes annoncent exactement ce qu'elles servent, `expand-property` compris, qui n'est plus servi sur
une carte puisqu'aucune propriété d'une carte ne porte d'href.

**Le snapshot de `sync-collection` s'arrête aux tombes.** Il couvrait aussi la lecture des cartes,
donc l'écriture de la réponse, donc le rythme auquel le client vide la socket : un lecteur lent
tenait une vue de lecture InnoDB ouverte aussi longtemps qu'il lui plaisait. Ce que le snapshot
protège est le couple compteur/tombes — un élagage entre les deux efface des suppressions que la
réponse doit encore, sous un filigrane qu'elle a lu plus bas. Les cartes n'en ont pas besoin :
l'élagage ne touche jamais `contacts`. Les lectures sont donc faites et la transaction fermée avant
que le premier octet ne soit composé.

**Le PROPFIND `Depth: 1`, lui, ne peut pas.** Son snapshot protège le compteur contre la requête des
membres elle-même — la lecture qui ruisselle. Il garde donc sa transaction, et c'est pourquoi sa
boucle est la seule à ne **pas** appeler `FlushIfDueAsync` : y pousser rendrait le client maître de
la durée du snapshot. La sortie propre serait une projection de membre portant un nombre d'octets au
lieu de `vcard_raw` — les propriétés servies là veulent la longueur, jamais la carte — et c'est une
modification de `DavCard`, de `IDavContactReader` et des tables de propriétés, pas de la méthode.
**C'est le seul des sept constats laissé ouvert, et il est laissé ouvert exprès.**

**`MultiStatusWriter.FlushAsync` n'avait aucun appelant.** Le type dit exister pour ne pas tenir un
carnet entier en mémoire, et rien ne poussait avant la disposition finale. `FlushIfDueAsync` porte
désormais la cadence (une poussée tous les 64 `response`), et les deux rapports lourds — `query` et
`multiget`, les seuls à porter `address-data` — l'appellent.

**`PruneAsync` matérialisait des révisions entières pour les supprimer.** Une révision porte la carte
qu'elle archive, jusqu'à 1 Mio, et le balayage couvre tout le déploiement : trente jours de cartes
écrasées de tous les utilisateurs sur le tas, pour un DELETE qui n'a jamais eu besoin que de la clé.
Les clés seules, donc, et les deux passes sont bornées à 50 000 lignes — non pas une politique de
conservation, la passe suivante prend le reste, mais un plafond qui rend l'empreinte d'une passe
indépendante de la taille du retard accumulé. Un `Capped` le dit, et le balayeur l'avertit à voix
haute : lu sur les seuls compteurs, un balayage borné ressemble trait pour trait à un balayage
complet.

**Le contrôle de cohérence ne retient plus le démarrage.** `IHostedService.StartAsync` est attendu
avant que l'hôte n'écoute, et ce diagnostic fait un `GROUP BY` sur toute la table `contacts`.
Personne n'attend sa réponse : il écrit une ligne de journal qu'un opérateur lit après coup.

**Le découpage sur `@` de l'authentification est gardé.** Les claims Upn/Dns sont recomposées en
`{Upn}@{Dns}` par `GetUser`, qui rend `null` si une moitié est vide — le `throw` derrière
`AuthenticatedUser`, donc un **500 sur le seul chemin dont toute la conception est de répondre 401**.
Les deux autres découpages sur `@` du dépôt vérifiaient déjà leur indice.

**L'href de `no-uid-conflict` nommait la ressource écrite.** § 6.2.2 veut celle qui **détient déjà**
l'UID en cause. Envoyée l'URI de sa propre requête, un client relit la carte qu'il vient d'essayer de
remplacer et n'apprend rien. Le vrai détenteur est cherché ; quand il n'y en a pas, le refus ne porte
plus d'href du tout plutôt qu'un href inventé.

Les trois tests qui ont rougi au premier correctif — le jeu clos des propriétés, deux fois, et l'href
du conflit d'UID — sont la preuve que ces comportements étaient épinglés et non accidentels. Neuf
tests ont été ajoutés, et chacun a été vérifié par mutation : reposer la garde retirée les fait
rougir.

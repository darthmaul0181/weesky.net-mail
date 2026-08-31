# Vérification de 4c-ii-b sur l'environnement de développement

À jouer une fois, après le déploiement de la branche `cardav` portant la tranche 4c-ii-b sur
`snoopy_webmail_dev`. **C'est ce déploiement qui ouvre `/dav` pour la première fois** : jusqu'ici la
tranche 4c-ii-a écrivait dans la base sans qu'aucune route ne lise, et 4c-i posait l'authentification
sans surface à protéger.

Ce fichier existe pour la même raison que `carddav-4c-ii-a-dev-verification.md` : une procédure qu'il
faut reconstituer au moment de s'en servir est une procédure qu'on ne joue pas.

La suite de tests couvre 2710 cas et ne peut pas faire une seule des choses écrites ici. Elle parle à
un serveur en mémoire ; ce qui manque est tout ce qui se trouve **entre** un vrai client et lui : un
proxy inverse qui n'achemine pas `PROPFIND`, un en-tête `Authorization` avalé en route, un
rattrapage inachevé, et un client qui a ses propres idées. Les quatre se présentent au même endroit,
avec la même phrase — **« le carnet est vide »** — et c'est précisément pour les séparer que la
tâche 11 a posé une ligne de journal.

## L'ordre de déploiement, qui est lui-même un contrôle

1. **Le contrôle du rattrapage, joué AVANT le déploiement.** Section suivante. Il ne se rattrape pas.
2. **Déployer le backend.** Aucun DDL neuf : 4c-ii-b n'ajoute aucune colonne ni aucune table. Le
   schéma de 4c-ii-a suffit, et il est joué sur `snoopy_webmail_dev` depuis le 27 août 2026.
3. **La fumée sans client**, en six requêtes. Cinq minutes, et elle sépare un défaut de proxy d'un
   défaut de code — ce qu'un appairage raté ne sépare pas.
4. **L'appairage d'un vrai client.**
5. **La lecture du journal de la session d'appairage.**

Vérifier avant tout que `Dav:PublicUrl` est renseignée : sans elle l'onglet « Sync » ne montre aucune
adresse et personne ne peut s'appairer. En développement elle vaut `https://api.mail.weesky.net`.

## Le contrôle qui ne se rattrape pas

Les deux requêtes de fin de `assets/contacts-dav-backfill.sql`, jouées **avant** d'ouvrir la moindre
route à un client :

```sql
SELECT COUNT(*) AS `restantes`
FROM `contacts`
WHERE `dav_name` IS NULL OR `sync_sequence` = 0;

SELECT COUNT(*) AS `sans_carte`
FROM `contacts`
WHERE `vcard_raw` IS NULL OR `card_hash` = '';
```

**Les deux doivent rendre 0.** Un chiffre non nul sur la première signifie que le rattrapage DAV n'a
pas été joué, ou l'a été partiellement ; sur la seconde, que le rattrapage de cartes de la tranche 4a
n'est pas terminé.

Et ce n'est pas un avertissement, c'est la seule fenêtre où l'on peut encore agir. **Un client qui
s'appaire sur un carnet incomplet ne voit pas un carnet incomplet : il voit des fiches supprimées, et
il efface ses propres copies pour se conformer au serveur.** Ce geste-là ne se défait pas depuis le
serveur. La clause de visibilité à trois conditions protège la lecture — une fiche sans carte ni
condensat n'est jamais servie — mais elle ne peut rien contre une fiche que le client possédait déjà
et qui a cessé d'apparaître.

Si `sans_carte` n'est pas nul : `POST /api/Contacts/Backfill?batchSize=1000`, route d'administrateur,
idempotente, rejouée jusqu'à `remaining = 0`, **puis** rejouer `assets/contacts-dav-backfill.sql`.
L'ordre est imposé et son inversion est le défaut que le plan a fermé en dernier.

## La fumée sans client

Six requêtes, dans cet ordre. En PowerShell, écrire **`curl.exe`** et non `curl` — ce dernier est un
alias d'`Invoke-WebRequest`, qui ne sait pas envoyer un `PROPFIND` — et garder les corps XML entre
apostrophes simples, littérales en PowerShell.

Poser d'abord :

```powershell
$base = "https://api.mail.weesky.net"
$cred = "adresse@domaine:le-secret-de-l-onglet-sync"
```

**1. Le well-known, en `PROPFIND` et pas seulement en `GET`.** C'est le premier geste de DAVx⁵ et de
Thunderbird, et une redirection réservée au `GET` leur rend un `405` avant même qu'ils sachent où
s'authentifier.

```powershell
curl.exe -i -s -X PROPFIND "$base/.well-known/carddav"
```

Attendu : `301`, `Location: /dav/`, un `Cache-Control` portant un `max-age`. **Sans authentification :
un `401` ici est un défaut.** Et si le `Cache-Control` manque, la redirection se met en cache
indéfiniment et changer un jour le chemin `/dav` devient impossible sur les appareils déjà appairés.

**2. `OPTIONS`, sans identifiants.** Un client demande les capacités avant d'avoir de quoi
s'authentifier.

```powershell
curl.exe -i -s -X OPTIONS "$base/dav/"
```

Attendu : `200`, `DAV: 1, 3, access-control, addressbook`, un en-tête `Allow`. **L'en-tête `DAV:` est
ce qu'Apple lit sur la première réponse venue plutôt que par un `OPTIONS` dédié** ; son absence est
la panne qui ne dit pas son nom.

**3. Le principal.** C'est ici que le client apprend son propre identifiant.

```powershell
curl.exe -i -s -u $cred -X PROPFIND -H "Depth: 0" `
  --data '<D:propfind xmlns:D="DAV:"><D:prop><D:current-user-principal/></D:prop></D:propfind>' `
  "$base/dav/"
```

Attendu : `207`, et un `<D:href>/dav/principals/{guid}/</D:href>`. Noter ce GUID : c'est
`$uid` pour la suite. Un `401` ici avec un `WWW-Authenticate: Basic realm="weesky CardDAV"` signifie
que le secret est faux ou que la synchronisation n'est pas activée pour ce compte ; un `401` **sans**
cet en-tête signifie que quelque chose entre le client et Kestrel a mangé l'`Authorization`.

**4. Le carnet, avec ses membres.** La requête qui compte.

```powershell
curl.exe -i -s -u $cred -X PROPFIND -H "Depth: 1" `
  --data '<D:propfind xmlns:D="DAV:"><D:prop><D:getetag/></D:prop></D:propfind>' `
  "$base/dav/addressbooks/$uid/default/"
```

Attendu : `207`, une première `response` dont le `href` est la collection **avec sa barre finale**,
puis **une `response` par fiche visible**, chacune sans barre finale et portant un `getetag` entre
guillemets. Compter : ce nombre doit être celui de `restantes = 0` plus haut, autrement dit le nombre
de fiches du compte.

**Une seule `response` alors que le compte a des fiches est le symptôme central de ce plan**, et il a
trois causes distinctes que cette requête sépare déjà : le rattrapage inachevé (mais le contrôle
précédent l'aurait dit), un `Depth` que le proxy a retiré — la réponse serait alors un `403` portant
`propfind-finite-depth`, jamais un `207` incomplet, c'est délibéré —, ou un compte dont les fiches
appartiennent à un autre `user_id`.

**5. Une carte, verbatim.**

```powershell
curl.exe -i -s -u $cred "$base/dav/addressbooks/$uid/default/<un-nom-de-l-etape-4>"
```

Attendu : `200`, `Content-Type: text/vcard; charset=utf-8`, un `ETag` entre guillemets identique à
celui annoncé à l'étape 4, un `Last-Modified` en date HTTP GMT, et un corps qui commence par
`BEGIN:VCARD`. **Les octets servis sont ceux qui sont stockés, fins de ligne comprises** : si le
corps diffère de ce que la base contient, l'ETag ne décrit plus ce qui sort et le client relira
indéfiniment.

**6. Le conditionnel.** En reprenant l'ETag de l'étape 5 :

```powershell
curl.exe -i -s -u $cred -H 'If-None-Match: "le-etag"' `
  "$base/dav/addressbooks/$uid/default/<le-meme-nom>"
```

Attendu : `304`, avec son `ETag` et **sans corps**. C'est ce qui fait qu'un client ne retélécharge pas
tout le carnet à chaque cycle.

## L'appairage d'un vrai client

Prendre l'adresse de l'onglet « Sync » et son secret. **DAVx⁵** ou **Thunderbird** : les deux
envoient un `PROPFIND` au well-known, ce que la fumée a déjà vérifié.

Ce qu'il faut regarder, dans l'ordre où ça casse :

- **Le carnet apparaît et se remplit.** C'est la vérification. Le nombre de contacts doit être celui
  de l'étape 4.
- **Une fiche accentuée s'affiche entière.** `getcontentlength` compte des octets UTF-8 ; un client
  qui couperait à la longueur annoncée rendrait une carte tronquée, donc invalide, sans rien dire.
- **Une fiche dont le nom porte un espace, un `#` ou un `?` s'ouvre.** C'est l'aller-retour
  d'échappement du href, corrigé deux fois pendant ce plan avant de tenir.
- **Thunderbird propose l'édition plutôt que la lecture seule.** S'il est en lecture seule, le jeu de
  privilèges est parti incomplet — présent et incomplet est pire qu'absent, chez lui.
- **Contacts.app d'Apple, si vous en avez un sous la main, ne plante pas.** Il écrit son `me-card` sur
  le home d'adresses par un `PROPPATCH`, et sabre documente qu'un refus mal formé peut le faire
  planter — pas abandonner le carnet, planter. Nous répondons `207` avec un `403` par propriété, et
  rien n'est stocké.

**Aucune écriture n'est attendue de fonctionner.** `PUT` et `DELETE` appartiennent au plan c ; un
client qui tente d'ajouter un contact recevra un `405`, et c'est le comportement voulu de cette
tranche.

## La ligne de journal, et les cinq causes d'un carnet vide

Chaque requête `/dav` laisse une ligne, toujours au même modèle, filtrable sur son préfixe `dav ` :

```
dav {Method} {Resource} depth={Depth} report={Report} tokenIn={TokenIn} tokenOut={TokenOut} responses={Responses} status={Status} condition={Condition}
```

`responses` est le nombre d'éléments `response` que le `multistatus` a réellement portés — **le
chiffre qu'aucun journal d'accès HTTP ne peut donner**, et c'est lui qui distingue un `207` plein
d'un `207` vide. `Resource` est le chemin, jamais la requête, qui pourrait porter un jeton. Rien de
cette ligne n'est un secret ni une carte : l'utilisateur y est le GUID du principal, celui que l'URL
porte déjà.

Ce qu'on lit selon le symptôme :

| Ce qu'on voit | Ce que dit la ligne |
|---|---|
| Carnet vide côté client | `responses=1` sur un `PROPFIND` du carnet : le rattrapage, ou le mauvais `user_id` |
| Carnet vide côté client | `status=403 condition=propfind-finite-depth` : le proxy a retiré l'en-tête `Depth` |
| Carnet vide côté client | **aucune ligne du tout** : l'authentification a refusé avant toute action — voir ci-dessous |
| Le client boucle sur un rapport | `report=addressbook-query status=403 condition=supported-report` : normal jusqu'au plan c |
| Corps refusé | `status=413` : le client envoie plus d'un mégaoctet |

**La réserve connue, et il faut la connaître :** un `401` ne laisse **aucune** ligne. L'autorisation
est refusée par l'intergiciel avant qu'aucune action ne s'exécute, donc la première des cinq causes —
un en-tête `Authorization` avalé — se diagnostique par l'**absence** de ligne, pas par son contenu.
C'est un point reporté explicitement au plan c. En attendant : si le client dit « carnet vide » et que
le journal ne montre rien du tout, c'est l'authentification ou l'acheminement, jamais le carnet.

## Ce que dev ne dira pas

- **La conformité au protocole.** C'est la tranche 4d et `ccs-caldavtester`, et l'ordre est délibéré :
  un défaut trouvé par un outil de conformité sur un serveur qui suit le RFC est un défaut du serveur ;
  trouvé sur un serveur écrit contre un client, il est indiscernable d'une divergence de ce client.
- **Le comportement sous charge.** Un carnet de développement tient dans une page ; la lecture en flux
  du carnet et la borne de 5000 `href` du multiget n'y sont pas éprouvées.
- **Ce qu'un proxy de production fait des verbes rares.** Dev et production n'ont pas nécessairement la
  même chaîne devant Kestrel, et `PROPFIND`, `REPORT` et `PROPPATCH` sont exactement les verbes qu'un
  intermédiaire mal configuré refuse.
- **Les écritures.** Rien de cette tranche n'écrit, donc rien de cette tranche n'éprouve les tombes,
  les révisions ni la séquence sous concurrence — c'est le plan c, et le contrôle à deux sessions de
  `webmail-carddav-tables.md` reste la seule chose qui les ait observées.

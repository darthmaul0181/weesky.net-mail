# Vérification de 4c-ii-c sur l'environnement de développement

À jouer une fois, après le déploiement de la tranche 4c-ii-c sur `snoopy_webmail_dev`. **C'est le
déploiement qui rend le carnet bidirectionnel** : jusqu'ici un client pouvait lire, désormais il
peut créer, modifier et supprimer.

Le document de 4c-ii-b (`carddav-4c-ii-b-dev-verification.md`) reste valable pour la lecture et n'est
pas répété ici. Celui-ci ne couvre que ce que la tranche c ajoute — et il commence par un contrôle
dont la fenêtre s'est refermée.

## Le contrôle qui ne se rattrape plus, et pourquoi il a changé de nature

Les deux requêtes de `assets/contacts-dav-backfill.sql` :

```sql
SELECT COUNT(*) AS `restantes`
FROM `contacts` WHERE `dav_name` IS NULL OR `sync_sequence` = 0;

SELECT COUNT(*) AS `sans_carte`
FROM `contacts` WHERE `vcard_raw` IS NULL OR `card_hash` = '';
```

**Les deux doivent rendre 0, et cette fois avant que le premier client n'ÉCRIVE — pas avant qu'il ne
lise.**

En lecture seule, un rattrapage incomplet se corrigeait en le finissant. Maintenant, une fiche sans
`dav_name` est invisible du protocole, donc le téléphone ne la voit pas ; il tient sa propre copie
pour une création que le serveur n'a pas, et il la **téléverse**. Le serveur crée une ligne neuve. Le
rattrapage tourne ensuite, rend visible la ligne d'origine — et vous avez **deux lignes par
personne**, que rien ne pourra plus rapprocher : le rattrapage attribue des noms et des rangs, il ne
sait pas fusionner.

L'index d'unicité ne les arrête pas : il porte sur `(user_id, uid)`, et l'UID de la copie du
téléphone n'est pas celui de la ligne du serveur. Le refus `no-uid-conflict` de cette tranche ne
protège que le cas inverse — deux ressources se disputant un même UID.

Sur `snoopy_webmail_dev`, le rattrapage a été joué en août ; ces deux `SELECT` sont donc une
vérification de dix secondes, pas une tâche. En production, c'est l'ordre entier de
`carddav-4c-ii-b-dev-verification.md` qui reprend, avec cette contrainte resserrée.

Aucun DDL neuf : la tranche c n'ajoute ni colonne ni table.

## La fumée d'écriture, sans client

En PowerShell, `curl.exe` et non `curl`. Reprendre `$base`, `$cred` et `$uid` du document de la
tranche b.

**1. Créer une carte.** Le `Content-Type` n'est pas un juge — le corps l'est — mais l'envoyer est ce
que fait un vrai client.

```powershell
$card = "BEGIN:VCARD`r`nVERSION:3.0`r`nUID:probe-1`r`nFN:Probe One`r`nEND:VCARD`r`n"
curl.exe -i -s -u $cred -X PUT -H "Content-Type: text/vcard; charset=utf-8" `
  --data-binary $card "$base/dav/addressbooks/$uid/default/probe-1.vcf"
```

Attendu : **`201`** et un `ETag` entre guillemets. Vérifiez ensuite dans le webmail que le contact
apparaît — c'est la moitié qui prouve que l'écriture passe par la porte de 4a et non par un chemin
parallèle.

**2. Relire ce qu'on vient d'écrire, octet pour octet.**

```powershell
curl.exe -s -u $cred "$base/dav/addressbooks/$uid/default/probe-1.vcf"
```

Le corps doit être **exactement** ce que vous avez envoyé, fins de ligne comprises, et l'`ETag` doit
être celui du `PUT`. Si les deux diffèrent, l'ETag a cessé de décrire ce qui sort et le client
relira indéfiniment — c'est l'invariant central de la tranche.

**3. Le remplacement conditionnel.** Avec l'ETag de l'étape 1 :

```powershell
curl.exe -i -s -u $cred -X PUT -H 'If-Match: "le-etag"' `
  --data-binary $card2 "$base/dav/addressbooks/$uid/default/probe-1.vcf"
```

Attendu : **`204`** et un `ETag` neuf. Rejouez la même commande avec l'ETag désormais périmé :
attendu **`412`**, et le contact **inchangé** dans le webmail.

**4. La création exclusive.**

```powershell
curl.exe -i -s -u $cred -X PUT -H "If-None-Match: *" `
  --data-binary $card "$base/dav/addressbooks/$uid/default/probe-1.vcf"
```

Attendu : **`412`** — la ressource existe. Sur un nom neuf, la même commande doit rendre `201`.

**5. Une carte refusée.** Un corps sans `VERSION` :

```powershell
curl.exe -i -s -u $cred -X PUT --data-binary "BEGIN:VCARD`r`nFN:X`r`nEND:VCARD`r`n" `
  "$base/dav/addressbooks/$uid/default/probe-2.vcf"
```

Attendu : **`403`** portant `valid-address-data` dans le corps XML — **pas** `supported-address-data`,
qui répond à une version hors de `3.0`/`4.0`. Les deux conditions ne sont pas interchangeables : le
client lit l'une comme « cette carte est illisible » et l'autre comme « ré-exporte dans une autre
version ». **Et aucune des deux ne doit être un `500`.**

**6. La synchronisation incrémentale, le cœur de la tranche.**

```powershell
curl.exe -s -u $cred -X REPORT -H "Depth: 0" `
  --data '<D:sync-collection xmlns:D="DAV:"><D:sync-token/><D:prop><D:getetag/></D:prop></D:sync-collection>' `
  "$base/dav/addressbooks/$uid/default/"
```

Attendu : `207`, une `response` par carte visible, et un `<D:sync-token>` en fin de document. Notez
ce jeton, puis **modifiez un contact depuis le webmail** et rejouez la même requête en remplaçant
`<D:sync-token/>` par `<D:sync-token>le-jeton-noté</D:sync-token>`.

Attendu : **une seule `response`**, celle du contact modifié, et un jeton neuf. C'est la propriété
que tout le paquet 1 existe pour tenir.

**7. La suppression et sa tombe.**

```powershell
curl.exe -i -s -u $cred -X DELETE "$base/dav/addressbooks/$uid/default/probe-1.vcf"
```

Attendu : **`204`**. Rejouez le `sync-collection` avec le jeton de l'étape 6 : la carte supprimée doit
revenir en **`404` comme enfant direct de sa `response`**, jamais logée dans un `propstat`.

**8. Le jeton qu'on n'a pas émis.** Reprenez le jeton de l'étape 6 en changeant un chiffre :

```powershell
curl.exe -i -s -u $cred -X REPORT `
  --data '<D:sync-collection xmlns:D="DAV:"><D:sync-token>http://weesky.net/ns/sync/00000000-0000-0000-0000-000000000000/1</D:sync-token><D:prop><D:getetag/></D:prop></D:sync-collection>' `
  "$base/dav/addressbooks/$uid/default/"
```

Attendu : **`403`** portant `valid-sync-token`. Un client conforme repart alors sur une
synchronisation initiale — ce qui doit fonctionner, et se voit à l'étape 6.

## L'appairage d'un vrai client, en écriture

Les gestes, dans l'ordre où ils cassent :

- **Créer un contact depuis le téléphone.** Il doit apparaître dans le webmail, avec son nom, ses
  adresses et ses numéros — c'est la projection qui travaille.
- **Le modifier depuis le webmail, puis synchroniser.** Le téléphone doit recevoir la modification
  *seule*, pas tout le carnet.
- **Le modifier depuis le téléphone, puis rouvrir le webmail.** Même chose dans l'autre sens.
- **Le supprimer depuis le téléphone.** Il doit disparaître du webmail, et **aucun autre contact ne
  doit disparaître** — une tombe fantôme se propage à tous les appareils.
- **Modifier des deux côtés entre deux synchronisations.** Un des deux perd, et c'est attendu ; ce
  qui ne l'est pas, c'est qu'aucun des deux ne survive, ou qu'un doublon apparaisse.
- **Une carte accentuée créée depuis le téléphone** doit s'afficher entière des deux côtés.
- **Un contact portant deux numéros identiques** doit garder les deux : ne pas les dédoublonner à
  l'affichage est un choix de ce projet, pas un oubli.

## La ligne de journal, désormais complète

Le modèle n'a pas changé, mais **deux champs qui étaient vides le sont enfin** :

```
dav {Method} {Resource} depth={Depth} report={Report} tokenIn={TokenIn} tokenOut={TokenOut} responses={Responses} status={Status} condition={Condition}
```

| Ce qu'on voit | Ce que dit la ligne |
|---|---|
| Le téléphone reboucle sur la synchro | `tokenIn` renseigné + `status=403 condition=valid-sync-token` : le jeton présenté n'est pas des nôtres — restauration, rotation d'epoch, ou client venu d'un autre serveur |
| Le téléphone retélécharge tout à chaque fois | `tokenIn=null` à chaque cycle : le client ne renvoie pas le jeton qu'on lui a donné |
| Une écriture refusée sans que le client s'en explique | `status=403` + la `condition` : `valid-address-data` (carte illisible), `supported-address-data` (version), `no-uid-conflict` (UID déjà pris), `max-resource-size` |
| Le client réessaie sans fin | `status=503` est **normal** et daté par un `Retry-After` ; `status=500` ne doit jamais apparaître — s'il apparaît, c'est un défaut, pas une charge |
| Un contact disparaît partout | chercher un `DELETE` avec `status=204` que personne n'a demandé |

**La réserve n'a pas changé :** un `401` ne laisse **aucune** ligne, l'autorisation étant refusée
avant qu'aucune action ne s'exécute. Si le client dit « carnet vide » et que le journal ne montre
rien du tout, c'est l'authentification ou l'acheminement — jamais le carnet.

## Ce que dev ne dira pas

- **L'isolation transactionnelle.** Les tests tournent sur un fournisseur en mémoire qui ignore les
  transactions ; le snapshot qui protège la synchronisation d'un élagage concurrent, et le verrou qui
  décide les préconditions, ne sont éprouvés nulle part. Un carnet de développement à un seul
  utilisateur ne les éprouve pas non plus.
- **Les courses.** Deux appareils écrivant la même carte dans la même milliseconde, un élagage tombant
  entre deux lectures : ce sont des fenêtres de quelques millisecondes qu'un usage normal ne
  rencontre pas.
- **La charge.** Chaque `addressbook-query` parse tout le carnet — borné à 5000 cartes, mais c'est le
  chemin le plus coûteux de `/dav`, et c'est celui de la recherche d'adresse de DAVx⁵. Un carnet de
  développement de vingt fiches n'en dira rien.
- **La conformité au protocole**, qui est la tranche 4d et son outil dédié.
- **Ce qu'un proxy de production fait de `PUT`, `DELETE` et `REPORT`** : dev et production n'ont pas
  nécessairement la même chaîne devant Kestrel.

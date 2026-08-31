# Contacts 4d — la conformité clients

Quatrième et dernière tranche du projet CardDAV, à la suite de
[4a](2026-08-14-webmail-contacts-4a-vcard-model-design.md),
[4b](2026-08-22-webmail-contacts-4b-editor-design.md) et
[4c](2026-08-23-webmail-contacts-4c-carddav-design.md).

## Où en est le projet

| | Tranche | État |
|---|---|---|
| 4a | Modèle de données complet + moteur d'aller-retour vCard | livrée |
| 4b | Éditeur et fiche webmail étendus | livrée |
| 4c | Serveur CardDAV (découverte, collection, rapports, verbes, ETags, pierres tombales, historique) | livrée, vérifiée sur dev à la main |
| **4d** | **Conformité : `ccs-caldavtester`, Thunderbird, DAVx⁵** | *ce document* |

4c a écrit le serveur au RFC, et deux revues l'ont confronté au texte et à sabre/dav et Radicale.
Ce qu'aucune revue ne remplace : un outil tiers qui envoie ce que le serveur n'attend pas, et des
clients réels qui ne lisent ni les RFC ni nos specs.

## Ce que fait la tranche

Trois choses, dans cet ordre.

**Une mesure.** `ccs-caldavtester`, la suite de conformité de CalendarServer, tourne contre dev
avec ses suites CardDAV, et son premier résultat est consigné brut, avant tout correctif. C'est le
chiffre de départ ; tout ce qui suit se lit par rapport à lui.

**Un triage.** Chaque échec reçoit un verdict écrit, avec la ligne du RFC qui le justifie : défaut
du serveur, divergence nommée, ou défaut de l'outil. Les défauts du serveur se corrigent dans la
tranche, en une vague, et un test unitaire fige chacun. Les divergences nommées se listent — 4c en
a déjà annoncé cinq, et cette tranche vérifie que la liste est complète.

**Deux clients.** Thunderbird et DAVx⁵ sont appairés contre dev par l'adresse de l'onglet Sync, et
jouent des scénarios fixés d'avance : création, modification, suppression dans chaque sens, photo,
conflit, régénération du secret. Ce qu'ils font de travers reçoit le même triage.

L'ordre est celui que 4c avait fixé, et pour la raison qu'elle donnait : un défaut trouvé par
l'outil sur un serveur qui suit le RFC est un défaut du serveur ; trouvé sur un serveur écrit
contre un client, il serait indiscernable d'une divergence de ce client. L'outil passe donc avant
les clients, et une décision de 4c ne se rouvre que devant un client réel, jamais devant l'outil
seul.

## Décisions

### 1. L'outil est `ccs-caldavtester`, tel quel, épinglé, en Python 2.7

`apple/ccs-caldavtester` est archivé depuis le 24 février 2024, et c'est du Python 2 sans
ambiguïté (`print x`, `except X, e`, `os.path.walk`) ; sa dépendance `ccs-pycalendar` aussi. Le fork
CalConnect n'y change rien. Trois voies : l'exécuter tel quel sous Python 2.7.18, le porter en
Python 3, ou l'isoler dans WSL.

**On l'exécute tel quel, sous le Python 2.7.18 de python.org installé sur le poste.** Un portage
serait un projet à part entière sur deux bases de code, avant le premier résultat, pour un outil
qui ne bougera plus ; et un outil modifié n'est plus un tiers. WSL n'est pas installé et
n'apporterait qu'une couche.

L'outil n'entre pas dans le dépôt. Il est cloné dans un dossier ignoré, **au commit de l'archivage** :
`ccs-caldavtester` à `bed21e5924275552c1561febc8203a9f194cf737`, `ccs-pycalendar` à
`a12dd4e1ce8822b022d4abf2cfe6cc93902ff03f` (tous deux du 2024-02-24). Ce sont les derniers commits
de deux dépôts archivés ; les épingler documente ce qu'on a lancé, rien de plus.

Ce qui entre dans le dépôt, c'est **le harnais** : la description du serveur, la liste des suites,
le script qui lance et épure. Voir « Le harnais ».

### 2. La cible est dev, avec un compte dédié

L'outil tourne contre `https://api-dev.mail.weesky.net`, pas contre un Kestrel local. Deux raisons :
c'est le serveur que les clients réels verront ensuite, et c'est la seule façon de répondre à la
question que 4c laissait ouverte — ce qu'une chaîne TLS/proxy fait de `PROPFIND`, `REPORT`,
`PROPPATCH` et `DELETE`.

Le prix : chaque correctif passe par un push et un déploiement. Il est accepté ; la boucle courte
reste celle des tests unitaires, qui figent chaque correctif avant qu'il ne parte.

**Le compte est créé pour l'outil et ne sert à rien d'autre.** Les blocs `<start>` de ses suites
vident le carnet (`DELETEALL` sur le home, `DELETE` du carnet) avant de le remplir ; un compte
personnel n'y survivrait pas. L'utilisateur d'administration que l'outil demande (`$useradmin:`)
est ce même compte : un carnet à un seul propriétaire n'a pas d'administrateur distinct.

### 3. `DELETE` sur le carnet le vide, et répond `204`

Le carnet est unique et créé avec l'utilisateur ; `MKCOL` répond `405` (4c, décision 3). Le
`DELETE` de la collection répondait `405` par la même logique — et c'est ce qui fait sauter
`put.xml` et `sync-report.xml` dès leur bloc `<start>` : l'outil compte la requête sans `<verify>`
comme réussie sur `2xx` seulement, et un `<start>` en échec **saute le fichier entier**. Ce sont les
deux suites les plus utiles.

**Un `DELETE` sur `/dav/addressbooks/{guid}/default/` vide le carnet et répond `204`.** Chaque carte
visible est tombée par `ContactStore.DeleteManyAsync` — par lots de cent, chacune archivée
(décision 17 de 4c), la séquence avançant à chaque lot, l'epoch conservée. Le carnet est ensuite
vide, jamais absent : le `PROPFIND` suivant le trouve, et le `sync-collection` suivant rend
autant de tombes que de cartes. `If-Match` est ignoré, la collection n'ayant pas d'ETag.
`CollectionAllow` gagne `DELETE` ; `OPTIONS` et le `405` du home le suivent. Home, principal et
racine restent à `405`.

Ce n'est pas un aménagement pour l'outil, c'est une décision produit que l'outil a forcée à
prendre : « supprimer » le seul carnet d'un compte ne peut vouloir dire que « le vider ». C'est
exactement le geste « Delete collection » de DAVx⁵ — confirmé par l'utilisateur, avec l'avertissement
que le serveur perdra les données —, et c'est ce que le RFC 4918 § 9.6 décrit, à la nuance près
que la collection réapparaît aussitôt, ce que le RFC n'interdit pas. Les révisions restent
récupérables par requête ; la tranche 4c n'a pas d'écran pour ça et 4d n'en ajoute pas.

Ce que ça ne fait pas : un `DELETE` sur le carnet ne touche ni l'epoch ni le jeton d'un autre
appareil autrement qu'en lui donnant des tombes à lire. Un téléphone qui revient après reçoit une
liste de suppressions, pas un `403 valid-sync-token`.

### 4. Trois verdicts, et la ligne du RFC pour chacun

Chaque test en échec — de l'outil comme d'un client — reçoit exactement un verdict, écrit dans le
rapport avec la référence qui le fonde :

- **Défaut serveur** : un MUST ou MUST NOT des RFC 6352, 4918, 6578, 3744 ou 5051 est violé. Il se
  corrige dans la tranche ; un test unitaire fige le comportement, vérifié par mutation (le test
  rougit quand la garde saute).
- **Divergence nommée** : l'attente est propre à CalendarServer, ou porte sur un SHOULD que 4c a
  tranché autrement. Elle entre dans la table des divergences avec la décision qui la couvre. Une
  décision de 4c ne se rouvre pas devant l'outil seul.
- **Défaut de l'outil** : l'attente contredit le RFC. Consigné, rien ne bouge.

Un correctif qui contredit une décision de 4c n'est pas un correctif, c'est une décision : elle
s'écrit dans le rapport comme telle, avec ce qui l'a forcée (le client, le test), avant le code.

Les divergences que 4c a **déjà nommées**, et que l'outil relèvera sans que ce soit une nouvelle :

| Divergence | Décision 4c | Où l'outil la verra |
|---|---|---|
| `Depth` ignoré sur `sync-collection` et `addressbook-query` | 7, 14 | `sync-report.xml`, `reports.xml` |
| `PROPPATCH` refuse chaque propriété en `403` | 16 | `proppatch.xml` attend des succès |
| Propriétés et rapports du RFC 3744 non servis, `access-control` retiré de `DAV:` | 13, revue 2 (P1) | `aclreports.xml`, `propfind.xml` |
| `address-data` demandé dans un `sync-collection` ressort en `propstat 404` | 14 | `sync-report.xml` |
| Plafond d'un mébioctet par carte | 15 | `limits.xml` (`max-resource-size`) |
| Pas de `MKCOL`, pas de `COPY`/`MOVE` | 3, 16 | `mkcol.xml`, `copymove.xml` |

Si l'outil en relève une qui n'est pas dans cette table et qui n'est pas un défaut serveur, la
table s'allonge — c'est le second livrable de la mesure.

### 5. Le rapport garde la mesure brute

Le rapport `docs/superpowers/carddav-4d-conformance.md` s'écrit en trois temps, et le premier ne
s'efface jamais : **le passage initial**, avant tout correctif, suite par suite (tests passés,
échoués, ignorés, fichiers sautés) ; **le triage**, un verdict par échec ; **le passage final**,
après la vague de correctifs, mêmes colonnes. Puis les clients, scénario par scénario, avec ce qui
a été observé et son verdict.

Un rapport qui ne montrerait que le passage final dirait « conforme » sans dire à quoi on a
renoncé. Le passage initial est la mesure ; le final est la preuve que le triage a été joué.

### 6. Le secret ne traverse jamais le dépôt, ni le rapport

`--print-details-onfail` imprime la requête entière sur chaque échec, en-tête `Authorization`
compris — le secret DAV en Base64. Deux barrières :

- Le guid, l'e-mail et le secret du compte de test viennent d'un `serverinfo.local.json` **ignoré
  par git**, dont le harnais engendre `serverinfo.xml` (ignoré aussi) à chaque lancement.
- Le script **épure toute ligne `Authorization:`** de la sortie avant qu'elle ne touche le disque
  du dossier `results/`, lui-même ignoré. Ce qui entre dans le rapport versionné est recopié à la
  main depuis cette sortie déjà épurée.

Le secret de ce compte est à régénérer dans l'onglet Sync quand la tranche se ferme.

### 7. Les clients réels : Thunderbird et DAVx⁵, scénarios fixés d'avance

L'iPhone emprunté de la liste initiale n'est pas disponible ; il sort de la tranche (voir « Ce que
la tranche ne fait pas »). Restent Thunderbird sur ce poste et DAVx⁵ sur un Android.

Les scénarios sont les mêmes pour les deux et s'écrivent **avant** de brancher le premier client,
pour que ce qu'on observe soit comparé à une attente et non raconté après coup :

1. Appairage par l'adresse complète de l'onglet Sync (`https://api-dev.mail.weesky.net/dav/`) —
   le principal et le carnet doivent être trouvés sans rien saisir d'autre.
2. Création côté client → visible dans le webmail avec ses champs à leur place.
3. Création côté webmail → visible côté client à la synchronisation suivante.
4. Modification dans chaque sens, y compris une photo.
5. Suppression dans chaque sens.
6. Carte 4.0 écrite par Thunderbird, relue par DAVx⁵ ; et l'inverse.
7. Conflit : la même carte modifiée des deux côtés avant synchronisation — `412`, et qui gagne
   (4c, décision 17 : le serveur, l'autre version archivée).
8. Régénération du secret dans l'onglet Sync — ce que fait le client, et le ré-appairage.
9. `DELETE` du carnet (décision 3) joué depuis DAVx⁵ ; Thunderbird n'a pas le geste.

L'utilisateur manipule les clients ; le diagnostic se fait sur trois journaux : celui de DAVx⁵
(journal de débogage exportable), la console d'erreurs de Thunderbird, et le journal des requêtes
de dev (4c, décision 18). Un client qui abandonne, boucle ou perd une donnée est un échec ; il
reçoit un verdict de la décision 4.

## Le harnais

Tout tient dans `tools/caldavtester/` :

| Fichier | Rôle |
|---|---|
| `run.ps1` | Vérifie `py -2.7` ; clone les deux dépôts aux commits de la décision 1 dans `.caldavtester/` (ignoré) s'ils manquent ; installe `pycalendar` dans un virtualenv local ; engendre `serverinfo.xml` depuis le gabarit et `serverinfo.local.json` ; lance `testcaldav.py --ssl --print-details-onfail -s serverinfo.xml` sur la liste de `suites.txt` ; écrit dans `results/<horodatage>.txt` la sortie **épurée** (décision 6). |
| `serverinfo.template.xml` | La description du serveur, avec `{guid}`, `{email}`, `{secret}` à substituer. |
| `serverinfo.local.example.json` | Le gabarit des trois valeurs ; le vrai `serverinfo.local.json` est ignoré. |
| `suites.txt` | Les fichiers de `scripts/tests/CardDAV/` à lancer, un par ligne, avec en commentaire pourquoi les autres sont exclus. |
| `README.md` | Installation de Python 2.7.18, création du compte dev, lancement, lecture de la sortie. |

**La description du serveur.** Les substitutions de l'outil sont du texte libre ; on y met nos
chemins tels que 4c les a fixés :

| Substitution | Valeur |
|---|---|
| hôte, port, `authtype` | `api-dev.mail.weesky.net`, `443`, `basic` |
| `$root:` | `/dav/` |
| `$principalcollection:` | `/dav/principals/` |
| `$principal1:` | `/dav/principals/{guid}/` |
| `$userid1:`, `$pswd1:` | `{email}`, `{secret}` |
| `$addressbookhome1:` | `/dav/addressbooks/{guid}/` |
| `$addressbook:` | `default` |
| `$addressbookpath1:` | `/dav/addressbooks/{guid}/default` |
| `$useradmin:`, `$pswdadmin:` | les mêmes que l'utilisateur 1 (décision 2) |

Le second utilisateur (`$userid2:`) n'est pas défini : aucune suite retenue ne l'exerce ; celles
qui le font (partage, ACL entre principaux) sont exclues ou attendues en échec nommé.

**Les fonctions annoncées** (`<features>`) : `carddav`, `sync-report`, `current-user-principal`,
`well-known`, `limits`. Tout le reste reste éteint — `default-addressbook`, `shared-addressbooks`,
`directory-gateway`, `ACL Method`, `COPY Method`, `MOVE Method`, `Extended MKCOL`, `ctag`, `brief`,
`prefer`, `quota`, `add-member`, `bulk-post` — parce que le serveur ne les a pas ou ne les annonce
pas. Un test conditionné à une fonction éteinte est compté « ignoré », pas « échoué » ; c'est la
distinction que le rapport doit préserver.

**Les suites lancées** (`suites.txt`) : `propfind`, `proppatch`, `put`, `get`, `reports`,
`sync-report`, `errors`, `errorcondition`, `limits`, `nonascii`, `well-known`,
`current-user-principal`, `mkcol`, `copymove`, `aclreports`, `ab-client`. Exclues, parce qu'elles
testent CalendarServer et non CardDAV : `sharing-*`, `directory.xml`, `directory-gateway.xml`,
`bulk.xml`, `add-member.xml`, `default-addressbook.xml`.

`mkcol`, `copymove` et `aclreports` sont lancées **en sachant** qu'elles échoueront : leur échec est
la mesure d'une divergence nommée, et un fichier qu'on ne lance pas ne mesure rien.

## Fichiers

Côté serveur, pour la décision 3 :

- `Services/CardDav/DavHeaders.cs` — `CollectionAllow` gagne `DELETE`.
- `Controllers/CardDavController.cs` — `CollectionAsync` dispatche `DELETE` vers le writer ;
  `HomeAsync` et la racine gardent le `405` ; le `405` du home porte son `Allow` inchangé.
- `Repositories/IDavContactWriter.cs`, `DavContactWriter.cs` — `DeleteAllAsync(Guid userId, ct)` :
  lit les identifiants des cartes visibles (la clause de visibilité de `DeleteAsync`), les passe à
  `ContactStore.DeleteManyAsync`, rend le nombre tombé. Pas de transaction propre : le store en
  ouvre une par lot, et c'est voulu (le commentaire de `DeleteManyAsync` dit pourquoi).
- `OptionsCollection` et `MethodNotAllowedOnCollection` ne bougent pas : ils lisent `CollectionAllow`.

Et pour tout correctif issu du triage : le fichier que le verdict désigne, avec son test.

Côté harnais : les cinq fichiers de `tools/caldavtester/`, et `.gitignore` pour
`tools/caldavtester/.caldavtester/`, `tools/caldavtester/results/`,
`tools/caldavtester/serverinfo.xml`, `tools/caldavtester/serverinfo.local.json`.

Côté documentation : le rapport (décision 5) ; et la spec 4c, dont la section « Ce que la tranche
ne fait pas » dit encore qu'`access-control` est annoncé — corrigée pour renvoyer à la seconde
revue, qui l'a retiré.

## Tests

**La décision 3, figée par mutation.**

- Contrôleur : `DELETE` sur le carnet répond `204` sans corps et `DAV: 1, 3, addressbook` ; sur le
  home, `405` avec `Allow` sans `DELETE` ; `OPTIONS` sur le carnet annonce `DELETE`.
- Writer : après `DeleteAllAsync`, plus aucune carte visible, autant de révisions `Delete` que de
  cartes, la séquence a avancé, et le `sync-collection` suivant depuis l'ancien jeton rend chaque
  nom en `404`. Cent cinquante cartes, pour traverser au moins deux lots. Une carte invisible (non
  rétro-portée par 4a) n'est ni tombée ni archivée.
- Sans surprise (`CardDavNoFiveHundredTests`) : `DELETE` sur un carnet vide répond `204`.

**Chaque correctif du triage** : un test qui rougit quand la garde saute, dans la classe qui couvre
déjà la forme concernée. Le rapport cite le test à côté du verdict.

**Le harnais lui-même** n'a pas de test : c'est un script de lancement. Sa preuve est le passage
initial consigné.

## Ordre d'exécution

1. Harnais, `.gitignore`, README ; l'utilisateur installe Python 2.7.18, crée le compte dev et
   remplit `serverinfo.local.json`. **Premier passage**, consigné brut — y compris les deux suites
   qui sautent au `<start>` : c'est la mesure de départ.
2. Décision 3 (`DELETE` du carnet), tests, push, déploiement. Second passage : les deux suites
   tournent.
3. Triage de tout ce qui reste ; **une seule vague** de correctifs serveur, chacun avec son test ;
   push, déploiement, passage final consigné.
4. Thunderbird, scénarios 1 à 8. Puis DAVx⁵, 1 à 9. Triage, et une vague de correctifs si un
   client en impose — c'est le seul endroit où une décision de 4c peut se rouvrir.
5. Spec 4c corrigée, secret du compte de test régénéré, rapport clos.

## Ce que la tranche ne fait pas

- **Pas d'iPhone, pas de Contacts.app.** L'appareil n'est pas disponible. Les points que 4c
  réservait à Apple restent des points de guet, nommés dans le rapport : le mébioctet face aux
  photos iOS, le `me-card` en `PROPPATCH` sur le home, la lecture du 4.0. Le jour où un appareil
  se présente, la liste de la décision 7 se rejoue telle quelle.
- **Pas de CalDAV.** Les suites CalDAV de l'outil ne sont pas lancées ; `caldav` reste éteint dans
  `<features>`.
- **Pas de charge.** L'outil ne mesure pas le carnet de 5000 fiches que 4c-ii-c nommait ; c'est
  un autre outil et une autre tranche.
- **Pas de portage de l'outil.** Ce qui ne s'exécute pas sous Python 2.7.18 tel quel est un défaut
  de l'outil (décision 4), consigné, et la suite concernée est comptée sautée.
- **Pas de découverte SRV ni `.well-known` sur `mail.weesky.net`.** L'adresse à saisir reste celle
  de l'API ; si Thunderbird ou DAVx⁵ ne s'appairent pas par elle, c'est un défaut serveur, pas une
  raison d'ajouter de la configuration DNS.
- **Pas d'écran de restauration.** Un carnet vidé par la décision 3 se récupère par requête, comme
  toute révision de 4c.

## Risques

- **TLS.** Le Python 2.7.18 de python.org embarque OpenSSL 1.0.2 : TLS 1.2, pas 1.3. Si le proxy de
  dev n'offre que 1.3, l'outil ne parle pas, et on le saura à la première requête. La réponse
  serait alors WSL (voie écartée par la décision 1, à rouvrir), pas un serveur en clair.
- **Vérification du certificat.** `httplib` vérifie depuis 2.7.9 ; un certificat Let's Encrypt
  avec la chaîne complète passe. Un échec ici est un défaut de la chaîne servie, à corriger côté
  proxy, pas à contourner dans l'outil.
- **L'outil plante sur ce qu'il ne sait pas lire.** Une réponse XML qu'il n'attend pas peut
  faire sauter un fichier entier avec une trace Python plutôt qu'un échec de test. Ça se lit
  comme « fichier sauté », se consigne comme tel, et se rejoue avec `--always-print-response`
  pour voir la réponse en cause.
- **Le compte de test n'est pas un compte personnel.** Répété ici parce que le bloc `<start>` de
  `put.xml` commence par vider le home.

# CardDAV 4d — rapport de conformité

Rapport de la tranche [4d](specs/2026-08-31-webmail-contacts-4d-conformance-design.md). Les
chiffres viennent de `tools/caldavtester/results/` (sortie épurée) ; rien ici ne se régénère,
chaque passage est recopié une fois et daté.

## 1. Passage initial — avant tout correctif

Date : 2026-08-31 · commit serveur déployé : `6743b06b` (origin/cardav, avant la décision 3) ·
fichier : `results/20260831-183718.txt` · 148 tests en 67 s · toutes les lignes `Authorization` épurées (67/67)

| Suite | Tests | OK | Échecs | Ignorés | Fichier sauté |
|---|---|---|---|---|---|
| propfind.xml | 16 | 13 | 3 | 0 | non |
| proppatch.xml | 7 | 0 | 7 | 0 | non |
| put.xml | — | — | — | — | **oui** — start : `DELETEALL` du home → `DELETE default/` répond `405` |
| get.xml | 3 | 1 | 2 | 2 suites (directory gateway) | non |
| reports.xml | 39 | 33 | 6 | 2 suites (directory gateway) | non |
| sync-report.xml | — | — | — | — | **oui** — start : `DELETE` de la collection répond `405` |
| errors.xml | 10 | 4 | 6 | 0 | non |
| errorcondition.xml | 12 | 4 | 8 | 1 suite | non |
| limits.xml | — | — | — | fichier entier | non (ignoré : require-feature non satisfaite) |
| nonascii.xml | 7 | 3 | 4 | 0 | non |
| well-known.xml | 10 | 0 | 10 | 0 | non |
| current-user-principal.xml | 3 | 0 | 3 | 1 suite | non |
| mkcol.xml | 4 | 2 | 1 | 1 test | non |
| copymove.xml | 3 | 0 | 3 | 1 suite | non |
| aclreports.xml | 21 | 1 | 18 | 2 tests | non |
| ab-client.xml | 0 | 0 | 0 | 0 | non (aucun test retenu) |

Attendu avant mesure : `put.xml` et `sync-report.xml` sautent au `<start>` (`DELETE` de la
collection encore en 405) ; `mkcol.xml`, `copymove.xml` et `aclreports.xml` échouent (divergences
nommées).

Constaté : **exactement les deux sauts prédits**, pour la raison prédite (le `405` du `DELETE` de
collection, décision 3 non déployée). Total de l'outil : `ok=64, failed=71, ignored=11, errors=2`.
Les échecs de `mkcol`/`copymove`/`aclreports` (22) sont les divergences nommées ; `proppatch` 0/7
est la divergence PROPPATCH-403 ; restent à trier notamment `well-known` 0/10 et
`current-user-principal` 0/3 (section 3).

## 2. Second passage — après le `DELETE` du carnet (décision 3)

Date : 2026-08-31 · commit : `e4c45012` · fichier : `results/20260831-195140.txt` ·
201 tests en 88 s · `ok=89, failed=90, ignored=22, errors=0` · épuration vérifiée (0 ligne en clair)

Seules les lignes qui ont bougé — la décision 3 a fait tourner les deux fichiers sautés,
tout le reste est au chiffre près identique au passage initial :

| Suite | Tests | OK | Échecs | Ignorés | Fichier sauté |
|---|---|---|---|---|---|
| put.xml | 18 | 14 | 4 | 0 | non — le `<start>` (DELETEALL) passe |
| sync-report.xml | 34 | 11 | 15 | 8 tests + 3 suites | non — le `<start>` (`DELETE` de la collection) passe |
| ab-client.xml | 3 | 3 | 0 | 0 | non (3 tests exécutés cette fois) |

## 3. Triage

Un verdict par échec (décision 4) : **défaut serveur** (corrigé, test cité), **divergence nommée**
(décision 4c citée), **défaut de l'outil** (RFC cité).

Triage du passage `20260831-195140` — les 90 échecs se réduisent à 29 lignes, DS = défaut
serveur, DN = divergence nommée, DO = défaut de l'outil, H = harnais :

| Suite / test | Constat | Verdict | Référence | Suite donnée |
|---|---|---|---|---|
| propfind / prop errors 1-2 | corps `propfind` invalide accepté (`allprop`+`propname` ; enfant inconnu) → 207 | **DS** | RFC 4918 § 14.20 (DTD) | vague : `400` |
| propfind / regular 4 | `getcontentlength` attendu à `0` sur la collection, servi en 404 | DO | RFC 4918 § 15.4 (due seulement si GET sert Content-Length) | — |
| proppatch (7) | propriétés mortes refusées `403` | DN | 4c décision 16 | — |
| put / PUT groups 1-4 | verbatim (ETag présent, pas de réécriture), type groupe/personne non contrôlé, + cascade `no-uid-conflict` | DN | 4a verbatim ; groupes hors produit (4c « ne fait pas ») | — |
| get / 2-3 | listing HTML d'une collection, chemin `$principaluri1:` | DO | fonctions CalendarServer, tests non gardés par feature | — |
| reports / basic query 19, 20, 21, 23, 26 | sous `nresults`, notre sous-ensemble ≠ celui de CalendarServer | DO | RFC 6352 § 8.6 : la troncature est libre | — |
| reports / basic query 28 | filtre sur propriété à préfixe de groupe (`item1.`) refusé `supported-filter` | DN | 4c décision 11 (évalué ou refusé, jamais ignoré) | — |
| sync-report / support-report-set 1 | `PROPFIND /dav/addressbooks/` → `404` | à trancher | — | question S4 |
| sync-report / support-report-set 3 | jeton attendu préfixé `data:,` | DO | RFC 6578 § 6.1 : jeton opaque | — |
| sync-report / suites « no props » | réponses `<D:response>` sans `<D:status>` quand aucune propriété n'est demandée | **DS** | RFC 4918 § 14.24 (`href+status` ou `propstat`) | vague : `status 200` |
| sync-report / « …/default » attendu partout | `$calendar_sync_extra_items:` resté à `[-]` (= inclure la collection, un CalendarServer-isme) | H | src/utils.py du tester | vague : `[]` dans le gabarit |
| sync-report / diff 5 et props 5 | un PUT à octets identiques n'avance pas la séquence, le delta est vide | DN | 4c décision 6 (avance ssi `card_hash` change) | — |
| errors / 2 et 5 | UID changé sous le même nom accepté (2), et 5 est sa cascade | DN | revue 2, P4 (sabre fait pareil ; DAVx⁵ boucle sinon) | — |
| errors / 4 ; errorcondition / PUT 2 | cartes « invalides » de l'outil parfaitement lisibles, acceptées | DO | aucun MUST identifiable dans les fixtures | — |
| errors / 8 ; (même carte ailleurs) | **caractère de contrôle (BEL) accepté dans NOTE** | **DS** | RFC 2426 ABNF (CTL exclus des valeurs) | vague : `403 valid-address-data` |
| errors / 25-26 | `PUT` sur home/collection → notre `405`, attendu `403` | DN | 4c décision 16 (`Allow` honnête) | — |
| errorcondition / PUT 1 | `Content-Type: text/xml` sur un PUT ignoré, le corps seul juge | DN | 4c (doc du writer : le corps est le seul juge) | — |
| errorcondition / PUT 3 | **ligne sans deux-points (carte structurellement cassée) acceptée** | **DS** | ABNF `contentline` (RFC 2426/6350) | vague : `403 valid-address-data` |
| errorcondition / PUT 5 | **corps portant deux VCARD pris pour un seul**, refusé `no-uid-conflict` au lieu d'invalide | **DS** | RFC 6352 § 5.1 (une ressource = un vCard) | vague : `403 valid-address-data` |
| errorcondition / PUT 4 et 7 | propriété inconnue (`METHOD`) et `PHOTO;BASE64` 2.1 tolérées ; UID changé = P4 | DO/DN | extensibilité RFC 2426 § 3 ; P4 | — |
| errorcondition / REPORT 2 et 4 | `address-data version="4.0"` servie (l'outil attend un refus) | DO | nous servons 4.0 (4c décision 11) | — |
| nonascii / non-utf-8 1-3 | la fixture du dépôt archivé contient déjà U+FFFD : le corps envoyé EST de l'UTF-8 valide | DO | octets `EF BF BD` dans `vnonascii/2.vcf` | — (notre décodeur strict est testé chez nous) |
| nonascii / high-ascii 3 | deux cartes en trop sur une requête haute-ascii | DN | collation NFC+lower (revue 2, DavCollation) | — |
| well-known (10) | `Location: /dav/` relatif (licite) vs égalité stricte avec `https://hôte/` ; `200` exigé sur `/.well-known/` nu | DO | RFC 7231 § 7.1.2 ; RFC 8615 | — |
| current-user-principal (3) | `PROPFIND /dav/principals/` → `404` — URL que `principal-collection-set` publie | à trancher | RFC 3744 § 5.8 | question S4 |
| mkcol / MKCOL with body | `404` rendu, `403` attendu | DN | 4c décision 3 (pas de MKCOL) | — |
| copymove (3), aclreports (18), mkcol (1) | verbes et rapports RFC 3744 non servis | DN | 4c décisions 13 et 16 | — |
| limits (fichier) | ignoré : `require-feature` exige `caldav` — c'est un fichier de quotas CalDAV | DO | en-tête de `limits.xml` | note dans suites.txt |

**Vague de correctifs retenue** (un test mutation-vérifié chacun) : S1 corps `propfind` invalide
→ `400` ; S2 `<D:status>HTTP/1.1 200 OK</D:status>` dans les réponses sans propriété du
`sync-collection` ; S3 le PUT refuse en `valid-address-data` les caractères de contrôle (hors
CR/LF/HTAB), un corps à plusieurs VCARD, et une ligne non pliée sans deux-points ; H1
`$calendar_sync_extra_items:` → `[]` + note `limits.xml`. S4 (servir `PROPFIND` sur
`/dav/principals/` et `/dav/addressbooks/`) : décision utilisateur, voir section.

## 4. Passage final — après la vague de correctifs

Date : 2026-08-31 · commit : `56814369` (S1..S4 + H1 déployés) · fichier :
`results/20260831-204451.txt` · 201 tests en 92 s · `ok=107, failed=72, ignored=22, errors=0` ·
épuration vérifiée (0 ligne en clair)

| Suite | Tests | OK | Échecs | Ignorés | Note |
|---|---|---|---|---|---|
| propfind.xml | 16 | 15 | 1 | 0 | reste `getcontentlength=0` (DO) |
| proppatch.xml | 7 | 0 | 7 | 0 | DN décision 16 |
| put.xml | 18 | 14 | 4 | 0 | DN groupes |
| get.xml | 3 | 1 | 2 | 2 suites | DO |
| reports.xml | 39 | 33 | 6 | 2 suites | DO troncature + DN `supported-filter` |
| sync-report.xml | 26 | 23 | 3 | 8 tests + 3 suites | S2+H1 ont fermé 12 échecs ; restent `data:,` (DO) et le PUT à octets identiques (DN décision 6) ×2 |
| errors.xml | 10 | 5 | 5 | 0 | S3 a fermé le BEL ; restent P4 (+cascade), « non-vcf » lisible (DO), 405 vs 403 (DN) |
| errorcondition.xml | 12 | 6 | 6 | 1 suite | S3 a fermé la ligne cassée et le double UID ; restent Content-Type (DN), cartes lisibles + 4.0 servie (DO) |
| limits.xml | — | — | — | fichier | l'outil l'ignore lui-même (`require-feature: caldav`) |
| nonascii.xml | 7 | 3 | 4 | 0 | fixture corrompue amont (DO) + collation (DN) |
| well-known.xml | 10 | 0 | 10 | 0 | DO (Location relatif licite, `200` exigé hors RFC) |
| current-user-principal.xml | 3 | 1 | 2 | 1 suite | S4 a ouvert l'URL ; restent deux artefacts : `$principaluri1:` resté à la forme `__uids__` de CalendarServer (harnais), et l'attente d'un principal non authentifié — notre `401` est la politique 4c |
| mkcol.xml | 4 | 2 | 1 | 1 test | DN (404 vs 403) |
| copymove.xml | 3 | 0 | 3 | 1 suite | DN décision 16 |
| aclreports.xml | 21 | 1 | 18 | 2 tests | DN décision 13 |
| ab-client.xml | 3 | 3 | 0 | 0 | — |

**Bilan.** De 64/71 (initial) à 107/72 sur 53 tests exécutés de plus : les quatre défauts serveur du
triage sont fermés et vérifiés en conditions réelles ; **chaque échec restant est rattaché à une
ligne DO ou DN de la section 3** — aucun défaut serveur connu ne subsiste sur cette surface. Le
volet outil de la tranche est clos ; la suite est la section 5 (clients réels).

## 5. Clients réels

### Thunderbird (Windows, version —)

| # | Scénario (spec, décision 7) | Observé | Verdict |
|---|---|---|---|
| 1 | Appairage par l'adresse de l'onglet Sync | Principal et carnet trouvés depuis `https://api-dev.mail.weesky.net/dav/`, rien d'autre à saisir | conforme |
| 2 | Création côté client → webmail | Fiche créée dans Thunderbird visible dans le webmail, champs à leur place | conforme |
| 3 | Création côté webmail → client | Fiche créée au webmail reçue par Thunderbird à la synchronisation suivante | conforme |
| 4 | Modification dans chaque sens, photo comprise | Modifications traversent dans les deux sens ; la photo posée côté client traverse. L'éditeur du webmail n'offre pas la modification de photo — manque produit hors 4d, à faire plus tard | conforme (protocole) |
| 5 | Suppression dans chaque sens | Suppression Thunderbird → absente du webmail ; suppression webmail → la tombe est lue et la fiche disparaît de Thunderbird | conforme |
| 6 | Carte 4.0 croisée avec DAVx⁵ | Carte 4.0 écrite par Thunderbird relue intacte par DAVx⁵ (conversion 3.0 à la lecture, décision 11) ; carte 3.0 de DAVx⁵ relue intacte par Thunderbird | conforme |
| 7 | Conflit : 412, le serveur gagne, l'autre version archivée | Thunderbird pousse chaque édition immédiatement — fenêtre de conflit nulle en usage normal. Joué avec DAVx⁵ : édition téléphone non synchronisée + édition webmail → `412`, DAVx⁵ reprend la version serveur, l'édition du téléphone archivée | conforme |
| 8 | Régénération du secret | Thunderbird et DAVx⁵ tombent tous deux en échec d'authentification à l'instant de la régénération — un secret par utilisateur (décision 1 de 4c) ; ré-appairage avec le nouveau secret repris sans reste | conforme |

### DAVx⁵ (Android, version —)

Scénarios joués sur DAVx⁵ pendant la même campagne : appairage (1), croisement 4.0/3.0 (6),
conflit — « le serveur gagne », l'édition du téléphone archivée (7), régénération du secret (8),
plus le sien propre :

| # | Scénario (spec, décision 7) | Observé | Verdict |
|---|---|---|---|
| 9 | « Delete collection » : le carnet se vide et réapparaît vide | `DELETE` de DAVx⁵ → `204`, toutes les cartes tombées et archivées (décision 3) ; le carnet réapparaît vide au rafraîchissement, récupérable par les révisions | conforme |

**Clôture (2026-08-31).** Les deux clients synchronisent dans les deux sens sans perte ni boucle ;
le seul manque relevé est produit (pas d'édition de photo au webmail), hors 4d. Le secret du compte
de test a été régénéré — les identifiants ayant servi à la campagne sont morts. La tranche 4d est
close : outil passé (section 4), clients passés (ci-dessus), divergences rapprochées (section 6).

### Groupes de contacts (tranche 4e, campagne à mener)

Spec [4e](specs/2026-08-31-webmail-contacts-4e-groups-design.md), § Tests, fin : aucun groupe
n'existe en base au 2026-08-31, donc le scénario ne peut pas être une observation — il faut
**créer** un groupe de chaque côté. Le tableau nomme son client par ligne : Thunderbird ne mappe pas
`KIND:group` / `X-ADDRESSBOOKSERVER-KIND:group` sur une liste de diffusion — la carte en sort en
fiche, « créé côté client » n'y est pas jouable, et ce n'est pas un défaut à relever. L'app Contacts
d'iOS crée des « listes » depuis iOS 16, qui se synchronisent en cartes de groupe : « créé sur le
téléphone » s'y joue comme sur DAVx⁵. Rien d'Apple n'a encore été observé sur ce serveur (section
7) : la ligne Apple reste **ouverte**, jamais cochée, tant qu'aucun appareil ne s'est présenté.

| # | Scénario | Client | Observé | Verdict |
|---|---|---|---|---|
| 1 | Groupe créé au webmail → retrouvé sur le téléphone | DAVx⁵ | | à jouer |
| 2 | Groupe créé sur le téléphone → retrouvé au webmail | DAVx⁵ | | à jouer |
| 3 | Membre ajouté au webmail → retrouvé sur le téléphone | DAVx⁵ | | à jouer |
| 4 | Membre ajouté sur le téléphone → retrouvé au webmail | DAVx⁵ | | à jouer |
| 5 | Groupe supprimé au webmail → absent du téléphone | DAVx⁵ | | à jouer |
| 6 | Groupe supprimé sur le téléphone → absent du webmail | DAVx⁵ | | à jouer |
| 7 | Groupe créé au webmail → retrouvé sur le téléphone | Apple (iPhone, Contacts iOS 16+, « liste ») | | **OUVERTE** |
| 8 | Groupe créé sur le téléphone → retrouvé au webmail | Apple (iPhone, Contacts iOS 16+, « liste ») | | **OUVERTE** |
| 9 | Membre ajouté au webmail → retrouvé sur le téléphone | Apple (iPhone, Contacts iOS 16+, « liste ») | | **OUVERTE** |
| 10 | Membre ajouté sur le téléphone → retrouvé au webmail | Apple (iPhone, Contacts iOS 16+, « liste ») | | **OUVERTE** |
| 11 | Groupe supprimé au webmail → absent du téléphone | Apple (iPhone, Contacts iOS 16+, « liste ») | | **OUVERTE** |
| 12 | Groupe supprimé sur le téléphone → absent du webmail | Apple (iPhone, Contacts iOS 16+, « liste ») | | **OUVERTE** |
| 13 | Les six scénarios ci-dessus | Thunderbird | — | non jouable — limite du client, pas un défaut |

> **Mesure de la décision 8 — réglage *Contact group method* de DAVx⁵.**
> À relever par l'utilisateur sur l'appareil de la campagne, **à la création du compte** (avant tout
> autre réglage) : le mode que DAVx⁵ propose par défaut, *separate vCards* ou *categories*
> (spec 4e, décision 8).
>
> - Relevé : _(à compléter à la campagne)_
> - Si *separate vCards* : la tranche marche d'origine sur Android ; le repli ne concerne que qui a
>   changé le réglage lui-même.
> - Si *categories* : chaque utilisateur Android doit toucher ce réglage pour voir la fonction — la
>   note de version doit dire de le passer sur *separate vCards*.

## 6. Divergences nommées

Reprises de la spec 4d (décision 4), confirmées ou allongées par les passages :

| Divergence | Décision 4c | Constatée par |
|---|---|---|
| `Depth` ignoré sur `sync-collection` et `addressbook-query` | 7, 14 | |
| `PROPPATCH` refuse chaque propriété en `403` | 16 | |
| RFC 3744 non servi, `access-control` retiré | 13, revue 2 (P1) | |
| `address-data` en `propstat 404` dans `sync-collection` | 14 | |
| Plafond d'un mébioctet | 15 | |
| Pas de `MKCOL`, `COPY`, `MOVE` | 3, 16 | |
| `DavPaths.Parse` ne reconnaît pas `/dav/principals/` ni `/dav/addressbooks/` : un `expand-property` sur `principal-collection-set` rend un `404` imbriqué pour une URL que le PROPFIND sert (S4) | adjudication de la revue de vague | interne, parquée — aucun client réel n'étend cette propriété, le repli PROPFIND répond |

## 7. Points de guet Apple (hors tranche)

Le mébioctet face aux photos iOS, le `me-card` en `PROPPATCH`, la lecture du 4.0 : à rejouer avec
la liste de la décision 7 le jour où un appareil Apple se présente.
